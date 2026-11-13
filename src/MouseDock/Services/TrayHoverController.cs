using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace MouseDock.Services;

internal sealed class TrayHoverController : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
        int x, int y, int width, int height, uint flags);
    private readonly HashSet<Forms.NotifyIcon> icons = new();
    private readonly Func<bool> enabled, busy;
    private readonly Func<Window> target;
    private readonly TrayHoverState state = new();
    private readonly DispatcherTimer timer;
    private Forms.NotifyIcon hoveredIcon;
    private System.Drawing.Rectangle anchor;
    private Window preview;
    private NativeRect savedPosition;
    private bool savedTopmost, savedShowActivated, suppressed, disposed;

    internal TrayHoverController(Func<bool> enabled, Func<Window> target, Func<bool> busy)
    {
        this.enabled = enabled; this.target = target; this.busy = busy;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += Tick;
    }

    internal void Attach(Forms.NotifyIcon icon)
    {
        if (disposed || icon == null || !icons.Add(icon)) return;
        icon.MouseMove += Move;
    }
    internal void Detach(Forms.NotifyIcon icon)
    {
        if (icon == null || !icons.Remove(icon)) return;
        icon.MouseMove -= Move;
        if (hoveredIcon == icon)
        {
            hoveredIcon = null;
            if (state.Owned) ClosePreview();
        }
    }
    private void Move(object sender, Forms.MouseEventArgs args)
    {
        if (!enabled() || disposed) return;
        hoveredIcon = (Forms.NotifyIcon)sender;
        var point = Forms.Cursor.Position;
        // NotifyIcon exposes pointer motion but no public shell identifier or leave event.
        anchor = new System.Drawing.Rectangle(point.X - 12, point.Y - 12, 24, 24);
        if (!timer.IsEnabled) timer.Start();
    }
    internal bool Owns(Window window) => state.Owned && ReferenceEquals(preview, window);

    internal bool Pin()
    {
        bool owned = state.Pin();
        if (owned) ReleasePreview();
        suppressed = true;
        return owned;
    }

    private void Tick(object sender, EventArgs args)
    {
        var point = Forms.Cursor.Position;
        bool overTray = hoveredIcon?.Visible == true && anchor.Contains(point);
        if (!overTray) suppressed = false;
        var window = preview ?? target();
        bool overWindow = false;
        if (preview?.IsVisible == true &&
            GetWindowRect(new WindowInteropHelper(preview).Handle, out var bounds))
            overWindow = point.X >= bounds.Left && point.X < bounds.Right &&
                point.Y >= bounds.Top && point.Y < bounds.Bottom;
        bool blocked = busy() || icons.Any(i => i.ContextMenuStrip?.Visible == true) ||
            Mouse.Captured != null || Mouse.LeftButton == MouseButtonState.Pressed ||
            Mouse.RightButton == MouseButtonState.Pressed;
        switch (state.Tick(Environment.TickCount64, enabled(), overTray && !suppressed, overWindow,
            blocked || window == null, window?.IsVisible == true))
        {
            case TrayHoverState.Change.Open: OpenPreview(window); break;
            case TrayHoverState.Change.Close: ClosePreview(); break;
        }
        if (preview != null && !state.Owned) ReleasePreview();
        if (!overTray && preview == null) timer.Stop();
    }
    private void OpenPreview(Window window)
    {
        preview = window;
        try
        {
            savedTopmost = window.Topmost;
            savedShowActivated = window.ShowActivated;
            new WindowInteropHelper(window).EnsureHandle();
            GetWindowRect(new WindowInteropHelper(window).Handle, out savedPosition);
            window.ShowActivated = false;
            window.PreviewMouseDown += ManualInput;
            window.PreviewKeyDown += ManualInput;
            window.Show();
            window.UpdateLayout();
            var handle = new WindowInteropHelper(window).Handle;
            GetWindowRect(handle, out var rect);
            var area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            int x = Math.Clamp(anchor.Left + anchor.Width / 2 - width / 2, area.Left,
                Math.Max(area.Left, area.Right - width));
            int y = anchor.Top - height - 8;
            if (y < area.Top) y = anchor.Bottom + 8;
            y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height));
            window.Topmost = true;
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
        }
        catch { ClosePreview(); }
    }
    private void ManualInput(object sender, InputEventArgs args) => Pin();
    private void ReleasePreview()
    {
        if (preview == null) return;
        preview.PreviewMouseDown -= ManualInput;
        preview.PreviewKeyDown -= ManualInput;
        preview.Topmost = savedTopmost;
        preview.ShowActivated = savedShowActivated;
        preview = null;
    }
    private void ClosePreview()
    {
        state.Pin();
        if (preview == null) return;
        var window = preview;
        window.Hide();
        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, savedPosition.Left,
            savedPosition.Top, 0, 0, 0x0015);
        ReleasePreview();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        timer.Tick -= Tick;
        ClosePreview();
        foreach (var icon in icons.ToArray()) Detach(icon);
    }
}
