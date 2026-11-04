using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

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

    internal static void ShellFullscreen()
    {
        var method = typeof(TaskbarPlacement).GetMethod("IsFullscreen")!;
        var monitor = new Rect(0, 0, 2560, 1440);
        bool Evaluate(string windowClass, string process)
        {
            object[] arguments = method.GetParameters().Length == 4
                ? [monitor, monitor, false, windowClass]
                : [monitor, monitor, false, windowClass, process];
            return (bool)method.Invoke(null, arguments)!;
        }
        Require(!Evaluate("Windows.UI.Core.CoreWindow", "StartMenuExperienceHost"),
            "Opening Start is incorrectly treated as a fullscreen game.");
        Require(!Evaluate("Windows.UI.Core.CoreWindow", "SearchHost"),
            "Opening Windows Search must not hide the battery strip.");
        Require(!Evaluate("XamlExplorerHostIslandWindow", "explorer"),
            "Explorer's shell overlay must not hide the battery strip.");
        Require(Evaluate("Windows.UI.Core.CoreWindow", "FullscreenGame"),
            "A fullscreen UWP application still needs to suppress the strip.");
        Require(Evaluate("CabinetWClass", "explorer"),
            "A fullscreen file browser is not a shell overlay.");
    }

    internal static void ImmediateOrderRecovery()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        Require(taskbar != IntPtr.Zero, "This check requires the Windows taskbar.");
        var oldSettings = MouseDock.App.Settings;
        var oldBattery = MouseDock.App.Battery;
        var settings = new AppSettings { Provider = "json", JsonPath = "missing-taskbar-fixture.json" };
        using var battery = new BatteryService(settings);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        battery.Devices.Add(new DeviceInfo { Id = "event-fixture", Name = "Fixture", Level = 55 });
        var window = new TaskbarWindow();
        var popup = new Window { Width = 20, Height = 20, Left = -2000,
            ShowInTaskbar = false, ShowActivated = false, Topmost = true };
        int activations = 0;
        window.Activated += (_, _) => activations++;
        popup.Activated += (_, _) => activations++;
        try
        {
            window.Show();
            popup.Show();
            var handle = new WindowInteropHelper(window).Handle;
            var popupHandle = new WindowInteropHelper(popup).Handle;
            // Disable the fallback poll: only real native events may restore the strip.
            var timer = (DispatcherTimer)typeof(TaskbarWindow)
                .GetField("_anchorTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            timer.Stop();
            PumpUntil(() => false, 100);
            for (int i = 0; i < 3; i++)
            {
                SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x0013);
                Require(SetWindowPos(handle, taskbar, 0, 0, 0, 0, 0x0013), "Could not reproduce taskbar occlusion.");
                SetWindowPos(popupHandle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
                Require(PumpUntil(() => Above(handle, taskbar), 200),
                    "The strip still needs the 500 ms timer to recover from taskbar activation.");
                Require(Above(popupHandle, handle), "The immediate repair covered a popup.");
                Require(activations == 0 && GetForegroundWindow() != handle && GetForegroundWindow() != popupHandle,
                    "The immediate repair activated a battery or popup window.");
            }
            window.Hide();
            window.Show();
            timer.Stop();
            SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x0013);
            SetWindowPos(handle, taskbar, 0, 0, 0, 0, 0x0013);
            Require(PumpUntil(() => Above(handle, taskbar), 200),
                "Native updates did not resume after hiding and reopening the strip.");
        }
        finally
        {
            popup.Close();
            window.Close();
            typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, oldSettings);
            typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, oldBattery);
        }
    }

    private static bool PumpUntil(Func<bool> condition, int timeoutMs)
    {
        var frame = new DispatcherFrame();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) => { if (condition() || elapsed.ElapsedMilliseconds >= timeoutMs) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return condition();
    }

    internal static void Activation()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) throw new InvalidOperationException("This check requires the Windows taskbar.");
        var window = new Window { Width = 40, Height = 40, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Left = -2000 };
        var popup = new Window { Width = 20, Height = 20, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Left = -2000 };
        int activations = 0;
        window.Activated += (_, _) => activations++;
        popup.Activated += (_, _) => activations++;
        try
        {
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            var move = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TaskbarAnchor")!.GetMethod("Move")!;
            GetWindowRect(handle, out var rect);
            var bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            for (int i = 0; i < 3; i++)
            {
                // Place the fixture below Explorer without relying on Explorer accepting a raise.
                SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x0013);
                Require(SetWindowPos(handle, taskbar, 0, 0, 0, 0, 0x0013),
                    "Could not place the strip underneath the taskbar.");
                Require(Above(taskbar, handle), "The taskbar cover scenario was not reproduced.");
                popup.Show();
                var popupHandle = new WindowInteropHelper(popup).Handle;
                SetWindowPos(popupHandle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
                move.Invoke(null, [window, bounds]);
                Require(Above(handle, taskbar), "The stationary strip stayed underneath the activated taskbar.");
                Require(Above(popupHandle, handle), "Restoring the strip covered a popup above the taskbar.");
                Require(activations == 0 && GetForegroundWindow() != handle && GetForegroundWindow() != popupHandle,
                    "Restoring taskbar order activated a battery or popup window.");
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
