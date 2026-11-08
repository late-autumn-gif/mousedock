using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;

namespace MouseDock.Services
{
    internal static class TaskbarAnchor
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
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
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);

        private sealed class FailedAttachment
        {
            public IntPtr Window, Parent, WindowDpi, ParentDpi;
            public uint ParentProcessId;
        }
        private static readonly ConditionalWeakTable<Window, FailedAttachment> FailedAttachments = new();

        private static Rect Convert(NativeRect r) =>
            new Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));

        public static void Configure(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            // A tool window that never takes keyboard focus from the active application.
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x00000080);
        }

        public static bool Detach(Window window)
        {
            var handle=new WindowInteropHelper(window).Handle;
            if(handle==IntPtr.Zero)return false;
            if(GetParent(handle)==IntPtr.Zero)return true;
            Marshal.SetLastPInvokeError(0);SetParent(handle,IntPtr.Zero);
            if(Marshal.GetLastPInvokeError()!=0)return false;
            int style=GetWindowLong(handle,-16);
            SetWindowLong(handle,-16,(style & ~0x40000000) | unchecked((int)0x80000000));
            SetWindowPos(handle,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0010|0x0020);
            return GetParent(handle)==IntPtr.Zero;
        }

        public static bool AttachToTaskbar(Window window) => Attach(window, FindWindow("Shell_TrayWnd", null));

        public static bool Attach(Window window, IntPtr parent)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || parent == IntPtr.Zero || !IsWindow(parent)) return false;
            if (GetParent(handle) == parent) return true;
            var windowDpi = GetWindowDpiAwarenessContext(handle);
            var parentDpi = GetWindowDpiAwarenessContext(parent);
            GetWindowThreadProcessId(parent, out uint parentProcessId);
            // A denied native host is not retried every timer tick: demoting and restoring
            // the floating fallback would otherwise visibly flash even while nothing changed.
            if (FailedAttachments.TryGetValue(window, out var failed) &&
                failed.Window == handle && failed.Parent == parent && failed.ParentProcessId == parentProcessId &&
                failed.WindowDpi == windowDpi && failed.ParentDpi == parentDpi)
                return false;
            FailedAttachments.Remove(window);
            // Do not let cross-process parenting reset either process's DPI awareness.
            if (!AreDpiAwarenessContextsEqual(windowDpi, parentDpi)) return false;
            int style = GetWindowLong(handle, -16);
            SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            SetWindowLong(handle, -16, (style & ~unchecked((int)0x80000000)) | 0x40000000);
            Marshal.SetLastPInvokeError(0);
            IntPtr previous = SetParent(handle, parent);
            if (previous == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            {
                SetWindowLong(handle, -16, style);
                FailedAttachments.Add(window, new FailedAttachment
                { Window = handle, Parent = parent, WindowDpi = windowDpi, ParentDpi = parentDpi,
                    ParentProcessId = parentProcessId });
                return false;
            }
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0020);
            return GetParent(handle) == parent;
        }

        public static bool TryGet(out Rect bounds, out uint edge, out double dpi)
        {
            bounds = Rect.Empty;
            edge = 3;
            dpi = 1;
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return false;
            var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = taskbar };
            if (!GetWindowRect(taskbar, out var actual)) return false;
            // Use the live parent origin, including its auto-hide animation position.
            bounds = Convert(actual);
            if (SHAppBarMessage(5, ref data) != UIntPtr.Zero) // ABM_GETTASKBARPOS
                edge = data.Edge;
            else
            {
                // Explorer may expose its window before the appbar service is ready.
                var screen = System.Windows.Forms.Screen.FromHandle(taskbar).Bounds;
                edge = bounds.Width >= bounds.Height
                    ? (Math.Abs(bounds.Top - screen.Top) < Math.Abs(bounds.Bottom - screen.Bottom) ? 1u : 3u)
                    : (Math.Abs(bounds.Left - screen.Left) < Math.Abs(bounds.Right - screen.Right) ? 0u : 2u);
            }
            dpi = Math.Max(96, GetDpiForWindow(taskbar)) / 96.0;
            return bounds.Width > 0 && bounds.Height > 0;
        }

        public static void Move(Window window, Rect bounds)
        {
            var handle = new WindowInteropHelper(window).Handle;
            var parent = GetParent(handle);
            var position = new NativePoint { X = (int)Math.Round(bounds.X), Y = (int)Math.Round(bounds.Y) };
            if (parent != IntPtr.Zero && !ScreenToClient(parent, ref position)) return;
            bool sameBounds = GetWindowRect(handle, out var current) &&
                current.Left == (int)Math.Round(bounds.X) && current.Top == (int)Math.Round(bounds.Y) &&
                current.Right - current.Left == (int)Math.Round(bounds.Width) &&
                current.Bottom - current.Top == (int)Math.Round(bounds.Height);
            if (!sameBounds)
                SetWindowPos(handle, IntPtr.Zero, position.X, position.Y,
                    Math.Max(1, (int)Math.Round(bounds.Width)), Math.Max(1, (int)Math.Round(bounds.Height)),
                    0x0004 | 0x0010 | 0x0200); // SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER
        }

        public static void EnsureOrder(Window window, bool attached)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            if (attached)
            {
                // Moving above a sibling preserves the parent and the existing composition surface.
                if (GetWindow(handle, 3) != IntPtr.Zero)
                    SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                return;
            }
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero || !IsWindowVisible(taskbar)) return;
            // Repair only when the taskbar is above us, leaving its flyouts above both windows.
            for (var above = GetWindow(handle, 3); above != IntPtr.Zero; above = GetWindow(above, 3))
            {
                if (above != taskbar) continue;
                var previous = GetWindow(taskbar, 3);
                while (previous != IntPtr.Zero && !IsWindowVisible(previous))
                    previous = GetWindow(previous, 3);
                // Inserting a non-topmost window after a topmost sibling does not
                // reliably promote it. Enter the band once, then retain flyout order.
                if ((GetWindowLong(handle, -20) & 8) == 0)
                    SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
                SetWindowPos(handle, previous == IntPtr.Zero ? new IntPtr(-1) : previous,
                    0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
                break;
            }
        }
    }
}
