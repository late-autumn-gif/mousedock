using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MouseDock.Models;

internal static class TaskbarRegressionChecks
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    internal static void Activation()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) throw new InvalidOperationException("This check requires the Windows taskbar.");
        var window = new Window { Width = 40, Height = 40, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Left = -2000 };
        var popup = new Window { Width = 20, Height = 20, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Left = -2000 };
        try
        {
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            var move = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TaskbarAnchor")!.GetMethod("Move")!;
            GetWindowRect(handle, out var rect);
            var bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            var foreground = GetForegroundWindow();
            for (int i = 0; i < 3; i++)
            {
                // Reproduce Explorer raising the taskbar above an already-positioned strip.
                SetWindowPos(taskbar, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
                Require(Above(taskbar, handle), "The taskbar cover scenario was not reproduced.");
                popup.Show();
                var popupHandle = new WindowInteropHelper(popup).Handle;
                SetWindowPos(popupHandle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
                move.Invoke(null, [window, bounds]);
                Require(Above(handle, taskbar), "The stationary strip stayed underneath the activated taskbar.");
                Require(Above(popupHandle, handle), "Restoring the strip covered a popup above the taskbar.");
                Require(GetForegroundWindow() == foreground, "Restoring taskbar order stole keyboard focus.");
            }
        }
        finally { popup.Close(); window.Close(); }
    }

    private static bool Above(IntPtr first, IntPtr second)
    {
        for (var previous = GetWindow(second, 3); previous != IntPtr.Zero; previous = GetWindow(previous, 3))
            if (previous == first) return true;
        return false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
