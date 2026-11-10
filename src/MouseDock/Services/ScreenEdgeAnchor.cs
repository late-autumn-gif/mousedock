using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Screen = System.Windows.Forms.Screen;

namespace MouseDock.Services
{
    internal static class ScreenEdgeAnchor
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, uint type, out uint x, out uint y);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after,
            int x, int y, int width, int height, uint flags);

        internal static bool IsForegroundFullscreen(System.Drawing.Rectangle bounds)
        {
            bool suppressed = false;
            var foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero && GetWindowRect(foreground, out var rect))
            {
                var name = new StringBuilder(128);
                GetClassName(foreground, name, name.Capacity);
                suppressed = TaskbarPlacement.IsFullscreen(
                    new Rect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top)),
                    new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height), IsZoomed(foreground), name.ToString());
            }
            return suppressed;
        }

        public static void Update(Window window, Size content)
        {
            var screen = Screen.PrimaryScreen;
            if (screen == null) return;
            var area = screen.WorkingArea;
            var work = new Rect(area.X, area.Y, area.Width, area.Height);
            var bounds = screen.Bounds;
            double dpi = 1;
            var monitor = MonitorFromPoint(new NativePoint { X = bounds.Left + bounds.Width / 2,
                Y = bounds.Top + bounds.Height / 2 }, 1);
            if (GetDpiForMonitor(monitor, 0, out uint dx, out _) == 0) dpi = Math.Max(96, dx) / 96d;
            bool suppressed = IsForegroundFullscreen(bounds);
            double scale = ScreenEdgePlacement.NormalizeSizePercent(App.Settings.EdgeSizePercent) / 100;
            var placement = ScreenEdgePlacement.Calculate(work, new Size(content.Width * scale, content.Height * scale),
                App.Settings.EdgeVerticalPosition, App.Settings.EdgeInset, dpi);
            var handle = new WindowInteropHelper(window).Handle;
            SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(placement.X), (int)Math.Round(placement.Y),
                Math.Max(1, (int)Math.Round(placement.Width)), Math.Max(1, (int)Math.Round(placement.Height)),
                0x0004 | 0x0010 | 0x0200);
            window.Opacity = suppressed ? 0 : Math.Clamp(App.Settings.Opacity, 0.4, 1);
            window.IsHitTestVisible = !suppressed && !App.Settings.ClickThrough;
        }
    }
}
