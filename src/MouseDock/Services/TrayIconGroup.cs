using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MouseDock.Models;
using Forms = System.Windows.Forms;

namespace MouseDock.Services;

internal sealed class TrayIconGroup : IDisposable
{
    private sealed class Entry : IDisposable
    {
        internal DeviceInfo Device;
        internal Forms.NotifyIcon Icon;
        internal Forms.Timer ClickTimer;
        internal string Signature;
        internal string MenuName;

        public void Dispose()
        {
            ClickTimer?.Stop();
            ClickTimer?.Dispose();
            if (Icon == null) return;
            Icon.Visible = false;
            var image = Icon.Icon;
            var menu = Icon.ContextMenuStrip;
            Icon.Dispose();
            image?.Dispose();
            menu?.Dispose();
            Icon = null;
        }
    }

    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DeviceInfo, Icon> render;
    private readonly Func<DeviceInfo, Forms.ContextMenuStrip> createMenu;
    private readonly Action<DeviceInfo, bool> invoke;
    private bool disposed;

    internal event Action<Forms.NotifyIcon> IconAdded;
    internal event Action<Forms.NotifyIcon> IconRemoved;
    internal int Count => entries.Count;
    internal string[] DeviceIds => entries.Keys.ToArray();
    internal Forms.NotifyIcon[] Icons => entries.Values.Select(e => e.Icon).ToArray();

    internal TrayIconGroup(Func<DeviceInfo, Icon> render,
        Func<DeviceInfo, Forms.ContextMenuStrip> createMenu, Action<DeviceInfo, bool> invoke)
    {
        this.render = render ?? throw new ArgumentNullException(nameof(render));
        this.createMenu = createMenu ?? throw new ArgumentNullException(nameof(createMenu));
        this.invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
    }

    internal void Sync(IEnumerable<DeviceInfo> targets, string appearanceKey)
    {
        if (disposed) return;
        var wanted = targets.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var id in entries.Keys.Where(id => !wanted.ContainsKey(id)).ToArray())
        {
            var old = entries[id];
            entries.Remove(id);
            IconRemoved?.Invoke(old.Icon);
            old.Dispose();
        }
        foreach (var device in wanted.Values)
        {
            if (!entries.TryGetValue(device.Id, out var entry))
            {
                entry = new Entry { Device = device, MenuName = device.Name };
                try
                {
                    entry.Icon = new Forms.NotifyIcon { ContextMenuStrip = createMenu(device) };
                    entry.ClickTimer = new Forms.Timer { Interval = Forms.SystemInformation.DoubleClickTime };
                    var captured = entry;
                    entry.ClickTimer.Tick += (_, _) =>
                    {
                        captured.ClickTimer.Stop();
                        invoke(captured.Device, false);
                    };
                    entry.Icon.MouseClick += (_, args) =>
                    {
                        captured.ClickTimer.Stop();
                        if (args.Button != Forms.MouseButtons.Left) return;
                        captured.ClickTimer.Start();
                    };
                    entry.Icon.MouseDoubleClick += (_, args) =>
                    {
                        if (args.Button != Forms.MouseButtons.Left) return;
                        captured.ClickTimer.Stop();
                        invoke(captured.Device, true);
                    };
                    entries.Add(device.Id, entry);
                    IconAdded?.Invoke(entry.Icon);
                }
                catch
                {
                    entries.Remove(device.Id);
                    entry.Dispose();
                    continue;
                }
            }
            if (!ReferenceEquals(entry.Device, device) || entry.MenuName != device.Name)
            {
                var oldMenu = entry.Icon.ContextMenuStrip;
                entry.Icon.ContextMenuStrip = createMenu(device);
                entry.MenuName = device.Name;
                oldMenu?.Dispose();
            }
            entry.Device = device;
            string signature = $"{device.Name}\n{device.Level}/{device.Connected}/{device.IsStale}/" +
                $"{device.IsChargingActive}/{device.IsFullActive}/{appearanceKey}";
            if (entry.Signature == signature && entry.Icon.Visible) continue;
            Icon replacement = null;
            try
            {
                replacement = render(device);
                var previous = entry.Icon.Icon;
                entry.Icon.Icon = replacement;
                replacement = null;
                previous?.Dispose();
                string text = $"MouseDock · {device.Name} · {device.LevelText} · {device.StatusText}";
                entry.Icon.Text = text.Length <= 63 ? text : text[..60] + "...";
                entry.Icon.Visible = true;
                entry.Signature = signature;
            }
            catch { replacement?.Dispose(); }
        }
    }

    internal void Restore()
    {
        foreach (var entry in entries.Values)
        {
            try
            {
                entry.Icon.Visible = false;
                entry.Icon.Visible = true;
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var entry in entries.Values)
        {
            IconRemoved?.Invoke(entry.Icon);
            entry.Dispose();
        }
        entries.Clear();
    }
}
