using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Controls;

internal sealed class RingVolumeInput
{
    private readonly FrameworkElement _owner;
    private readonly DispatcherTimer _poll;
    private readonly List<(Rect Bounds, DeviceInfo Device)> _tracks = new();
    private IReadOnlyList<AudioEndpointInfo> _endpoints = Array.Empty<AudioEndpointInfo>();
    private static readonly IHeadsetAudioBackend Backend = new WindowsHeadsetAudioBackend();
    private static Task<IReadOnlyList<AudioEndpointInfo>> _snapshot;
    private static long _snapshotAt;
    private (Rect Bounds, DeviceInfo Device, string Endpoint)? _drag;
    private int _preview;
    private bool _busy;

    internal RingVolumeInput(FrameworkElement owner)
    {
        _owner = owner;
        _poll = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background,
            async (_, _) => await Refresh(), owner.Dispatcher);
        _poll.Stop();
        owner.IsVisibleChanged += (_, _) => { if (!owner.IsVisible) _poll.Stop(); else owner.InvalidateVisual(); };
        owner.Unloaded += (_, _) => _poll.Stop();
        owner.PreviewMouseLeftButtonDown += Begin;
        owner.PreviewMouseMove += Move;
        owner.PreviewMouseLeftButtonUp += End;
        owner.LostMouseCapture += (_, _) => { _drag = null; owner.InvalidateVisual(); };
    }

    internal void Clear() => _tracks.Clear();

    internal int? Track(Rect bounds, DeviceInfo device)
    {
        _tracks.Add((bounds, device));
        if (_owner.IsVisible && !_poll.IsEnabled)
        {
            _poll.Start();
            _owner.Dispatcher.BeginInvoke(new Action(async () => await Refresh()), DispatcherPriority.Background);
        }
        return _drag is { } drag && ReferenceEquals(drag.Device, device) ? _preview : Endpoint(device, _endpoints)?.Volume;
    }

    internal static AudioEndpointInfo Endpoint(DeviceInfo device, IReadOnlyList<AudioEndpointInfo> endpoints)
    {
        if (device is not { Kind: DeviceKind.Headset, Connected: true }) return null;
        var names = new[] { device.Name, device.RawName, device.ConfirmedModel }
            .Where(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length >= 5 &&
                !new[] { "Headset", "Headphones", "Earbuds", "Wireless Headset", "USB Audio", "USB Audio Device" }
                    .Contains(n.Trim(), StringComparer.OrdinalIgnoreCase))
            .Select(n => n.Trim()).ToArray();
        var matches = endpoints.Where(endpoint => endpoint.IsHeadset && endpoint.Volume.HasValue &&
            names.Any(name => string.Equals(endpoint.Name, name, StringComparison.OrdinalIgnoreCase) ||
                endpoint.Name.EndsWith("(" + name + ")", StringComparison.OrdinalIgnoreCase))).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private async Task Refresh()
    {
        if (!_owner.IsVisible || _tracks.Count == 0) { _poll.Stop(); return; }
        if (_drag.HasValue || _busy) return;
        long now = Environment.TickCount64;
        if (_snapshot == null || (_snapshot.IsCompleted && now - _snapshotAt >= 1500))
        {
            _snapshotAt = now;
            _snapshot = Task.Run(Snapshot);
        }
        _endpoints = await _snapshot;
        _owner.InvalidateVisual();
    }

    private static IReadOnlyList<AudioEndpointInfo> Snapshot()
    {
        try { return Backend.Enumerate(); }
        catch { return Array.Empty<AudioEndpointInfo>(); }
    }

    private void Begin(object sender, MouseButtonEventArgs e)
    {
        if (_busy || e.ClickCount != 1 || Keyboard.Modifiers != ModifierKeys.None) return;
        Point point = e.GetPosition(_owner);
        foreach (var track in _tracks)
        {
            if (!track.Bounds.Contains(point) || Endpoint(track.Device, _endpoints) is not { } endpoint) continue;
            if (!_owner.CaptureMouse()) return;
            _drag = (track.Bounds, track.Device, endpoint.Id);
            Preview(track.Bounds, point.X);
            e.Handled = true;
            return;
        }
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (_drag is not { } drag) return;
        Preview(drag.Bounds, e.GetPosition(_owner).X);
        e.Handled = true;
    }

    private void Preview(Rect bounds, double x)
    {
        _preview = (int)Math.Round(100 * Math.Clamp((x - bounds.Left) / bounds.Width, 0, 1));
        _owner.InvalidateVisual();
    }

    private async void End(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not { } drag) return;
        int value = _preview;
        e.Handled = true;
        _drag = null;
        _owner.ReleaseMouseCapture();
        if (!_tracks.Any(track => ReferenceEquals(track.Device, drag.Device)) || !drag.Device.Connected) return;
        _busy = true;
        try
        {
            _endpoints = await Task.Run(() =>
            {
                var current = Snapshot();
                if (Endpoint(drag.Device, current)?.Id == drag.Endpoint)
                    Backend.SetVolume(drag.Endpoint, value);
                return Snapshot();
            });
            _snapshot = null;
        }
        finally
        {
            _busy = false;
            _owner.InvalidateVisual();
        }
    }
}
