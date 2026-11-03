using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MouseDock.Services
{
    internal static class TaskbarAnchor
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            public uint Size;
            public IntPtr Window;
            public uint Callback, Edge;
            public NativeRect Rect;
            public IntPtr Parameter;
        }

        [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);

        private static Rect Convert(NativeRect r) =>
            new Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));

        public static void Configure(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            // A tool window that never takes keyboard focus from the active application.
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x00000080);
        }

        public static bool TryGet(out Rect bounds, out uint edge, out double dpi, out bool suppressed)
        {
            bounds = Rect.Empty;
            edge = 3;
            dpi = 1;
            suppressed = true;
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return false;
            var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = taskbar };
            if (SHAppBarMessage(5, ref data) == UIntPtr.Zero) return false; // ABM_GETTASKBARPOS
            bounds = Convert(data.Rect);
            edge = data.Edge;
            dpi = Math.Max(96, GetDpiForWindow(taskbar)) / 96.0;
            var screen = System.Windows.Forms.Screen.FromHandle(taskbar).Bounds;
            var monitor = new Rect(screen.X, screen.Y, screen.Width, screen.Height);
            suppressed = !IsWindowVisible(taskbar);
            if (GetWindowRect(taskbar, out var actual))
            {
                var visible = Rect.Intersect(Convert(actual), monitor);
                suppressed |= visible.IsEmpty || visible.Width < 8 || visible.Height < 8;
            }
            var foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero && foreground != taskbar && GetWindowRect(foreground, out var foregroundRect))
            {
                var name = new System.Text.StringBuilder(128);
                GetClassName(foreground, name, name.Capacity);
                var className = name.ToString();
                // The desktop also covers its monitor and is not a full-screen application.
                if (TaskbarPlacement.IsFullscreen(Convert(foregroundRect), monitor, IsZoomed(foreground), className))
                    suppressed = true;
            }
            return bounds.Width > 0 && bounds.Height > 0;
        }

        public static void Move(Window window, Rect bounds)
        {
            var handle = new WindowInteropHelper(window).Handle;
            bool sameBounds = GetWindowRect(handle, out var current) &&
                current.Left == (int)Math.Round(bounds.X) && current.Top == (int)Math.Round(bounds.Y) &&
                current.Right - current.Left == (int)Math.Round(bounds.Width) &&
                current.Bottom - current.Top == (int)Math.Round(bounds.Height);
            if (!sameBounds)
                SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y),
                    Math.Max(1, (int)Math.Round(bounds.Width)), Math.Max(1, (int)Math.Round(bounds.Height)),
                    0x0004 | 0x0010 | 0x0200); // SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER

            // Explorer can raise the taskbar without changing its bounds. Restore only the order relative
            // to the taskbar, leaving menus and other windows already above it in front of the strip.
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return;
            for (var above = GetWindow(handle, 3); above != IntPtr.Zero; above = GetWindow(above, 3))
            {
                if (above != taskbar) continue;
                var predecessor = GetWindow(taskbar, 3); // GW_HWNDPREV
                SetWindowPos(handle, predecessor == IntPtr.Zero ? new IntPtr(-1) : predecessor,
                    0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
                break;
            }
        }
    }
}
