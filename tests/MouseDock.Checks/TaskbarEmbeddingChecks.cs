using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

internal static class TaskbarEmbeddingChecks
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    internal static void AttachedToTaskbar()
    {
        using var fixture = new Fixture();
        fixture.Window.Show();
        fixture.Window.UpdateAnchor();
        Pump(100);
        IntPtr handle = new WindowInteropHelper(fixture.Window).Handle;
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        Require(taskbar != IntPtr.Zero, "A Windows taskbar is required.");
        Require(GetParent(handle) == taskbar, "The battery strip is still a separate top-level window.");
        Require((GetWindowLong(handle, -16) & 0x40000000) != 0, "The strip must use WS_CHILD.");
        Require((GetWindowLong(handle, -16) & unchecked((int)0x80000000)) == 0, "A child must not retain WS_POPUP.");
        Require((GetWindowLong(handle, -20) & 8) == 0, "The embedded strip must not compete for topmost order.");
        GetWindowRect(handle, out var stripRect);
        GetWindowRect(taskbar, out var barRect);
        Require(stripRect.Left >= barRect.Left && stripRect.Right <= barRect.Right &&
            stripRect.Top >= barRect.Top && stripRect.Bottom <= barRect.Bottom,
            "Parent-relative coordinates put the strip outside the taskbar.");
        fixture.Window.Hide();
        Require(!IsWindowVisible(handle), "Hiding the selected surface left its native child visible.");
        fixture.Window.Show();
        fixture.Window.UpdateAnchor();
        Require(GetParent(handle) == taskbar && IsWindowVisible(handle), "The embedded strip did not return after Show.");
    }

    internal static void FollowsParentWithoutRefresh()
    {
        var parent = new Window { Width = 500, Height = 100, Left = -1800, Top = 100,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false };
        var strip = new Window { Width = 80, Height = 40, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            parent.Show();
            strip.Show();
            var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TaskbarAnchor")!;
            var attach = type.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static);
            Require(attach != null, "Missing native taskbar child attachment.");
            IntPtr parentHandle = new WindowInteropHelper(parent).Handle;
            IntPtr stripHandle = new WindowInteropHelper(strip).Handle;
            Require((bool)attach!.Invoke(null, [strip, parentHandle])!, "Native child attachment failed.");
            GetWindowRect(parentHandle, out var parentRect);
            type.GetMethod("Move")!.Invoke(null, [strip, new Rect(parentRect.Left + 25, parentRect.Top + 10, 80, 40)]);
            GetWindowRect(stripHandle, out var before);
            Require(before.Left == parentRect.Left + 25 && before.Top == parentRect.Top + 10,
                "Screen placement was not translated to the parent's client coordinates.");
            SetWindowPos(parentHandle, IntPtr.Zero, parentRect.Left + 130, parentRect.Top + 30, 0, 0, 0x0015);
            GetWindowRect(stripHandle, out var after);
            Require(after.Left - before.Left == 130 && after.Top - before.Top == 30,
                "The strip requires a timer or event repair to follow its parent.");
            parent.Hide();
            Require(!IsWindowVisible(stripHandle), "The child did not follow its hidden parent.");
            parent.Show();
            Require(IsWindowVisible(stripHandle), "The child did not follow its shown parent.");
        }
        finally { strip.Close(); parent.Close(); }
    }

    internal static void TransparentComposition()
    {
        // 分层子窗口只有嵌进真实的 Shell 任务栏才会被合成；用一个 WPF 顶层窗口当宿主演不到像素。
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        Require(taskbar != IntPtr.Zero, "A Windows taskbar is required.");
        GetWindowRect(taskbar, out var bar);
        var target = new Rect(bar.Left + 320, bar.Top + 4, 120, 36);
        using var before = Capture(target);
        var strip = new Window { Width = 80, Height = 40, WindowStyle = WindowStyle.None,
            AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, Content = new DeviceBatteryRing { Width = 40, Height = 40,
                Kind = DeviceKind.Keyboard, Level = 66 } };
        try
        {
            strip.Show();
            var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TaskbarAnchor")!;
            Require((bool)type.GetMethod("Attach")!.Invoke(null, [strip, taskbar])!, "Attachment failed.");
            type.GetMethod("Move")!.Invoke(null, [strip, target]);
            Pump(800);
            using var after = Capture(target);
            System.IO.Directory.CreateDirectory("artifacts");
            after.Save("artifacts/taskbar-child-composition.png", System.Drawing.Imaging.ImageFormat.Png);
            int ink = 0;
            for (int y = 0; y < after.Height; y++)
                for (int x = 0; x < after.Width; x++)
                {
                    var a = after.GetPixel(x, y);
                    var b = before.GetPixel(x, y);
                    if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 60) ink++;
                }
            Require(ink > 30, "The embedded layered window is blank: the ring and glyph never reached the compositor.");
        }
        finally { strip.Close(); }
    }

    private static System.Drawing.Bitmap Capture(Rect rect)
    {
        var bitmap = new System.Drawing.Bitmap(Math.Max(1, (int)rect.Width), Math.Max(1, (int)rect.Height));
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen((int)rect.X, (int)rect.Y, 0, 0, bitmap.Size);
        return bitmap;
    }

    internal static void HostLossRecovery()
    {
        using var fixture = new Fixture();
        var savedTaskbar = MouseDock.App.Taskbar;
        var create = typeof(MouseDock.App).GetMethod("CreateTaskbarWindow", BindingFlags.NonPublic | BindingFlags.Static);
        Require(create != null, "There is no taskbar host recovery factory.");
        var parent = new Window { Width = 300, Height = 70, Left = -1800, Top = 100,
            ShowInTaskbar = false, ShowActivated = false };
        TaskbarWindow? initial = null;
        try
        {
            parent.Show();
            initial = (TaskbarWindow)create!.Invoke(null, null)!;
            typeof(MouseDock.App).GetProperty("Taskbar")!.SetValue(null, initial);
            initial.Show();
            var timer = (DispatcherTimer)typeof(TaskbarWindow).GetField("_anchorTimer",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(initial)!;
            timer.Stop();
            var anchor = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TaskbarAnchor")!;
            Require((bool)anchor.GetMethod("Attach")!.Invoke(null,
                [initial, new WindowInteropHelper(parent).Handle])!, "Could not attach the fixture host.");
            parent.Close();
            Pump(300);
            Require(MouseDock.App.Taskbar != initial, "Losing the native parent permanently closed the battery surface.");
            var replacement = MouseDock.App.Taskbar;
            Require(replacement != null && replacement.IsVisible, "The replacement surface was not shown.");
            Require(GetParent(new WindowInteropHelper(replacement!).Handle) == FindWindow("Shell_TrayWnd", null),
                "The replacement did not attach to the available taskbar.");
            replacement!.Close();
            Pump(100);
            Require(MouseDock.App.Taskbar == replacement, "An ordinary close incorrectly recreated the surface.");
        }
        finally
        {
            initial?.Close();
            MouseDock.App.Taskbar?.Close();
            parent.Close();
            typeof(MouseDock.App).GetProperty("Taskbar")!.SetValue(null, savedTaskbar);
        }
    }

    internal static int RunHostFixture()
    {
        var app = new Application();
        var window = new Window { Width = 300, Height = 80, Left = -1800, Top = 80,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        window.Show();
        Console.WriteLine(new WindowInteropHelper(window).Handle.ToInt64());
        Console.Out.Flush();
        return app.Run();
    }

    internal static void CrossProcessHostLoss()
    {
        using var fixture = new Fixture();
        var savedTaskbar = MouseDock.App.Taskbar;
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };
        if (System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--taskbar-host-fixture");
        using var host = System.Diagnostics.Process.Start(start)!;
        TaskbarWindow? initial = null;
        try
        {
            var ready = host.StandardOutput.ReadLineAsync();
            Require(ready.Wait(10000) && long.TryParse(ready.Result, out _), "The isolated native host did not start.");
            var parent = new IntPtr(long.Parse(ready.Result!));
            initial = (TaskbarWindow)typeof(MouseDock.App).GetMethod("CreateTaskbarWindow",
                BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
            typeof(MouseDock.App).GetProperty("Taskbar")!.SetValue(null, initial);
            initial.Show();
            var anchor = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TaskbarAnchor")!;
            Require((bool)anchor.GetMethod("Attach")!.Invoke(null, [initial, parent])!, "Cross-process attachment failed.");
            host.Kill();
            Require(host.WaitForExit(5000), "The isolated host did not exit.");
            Pump(1200);
            var replacement = MouseDock.App.Taskbar;
            Require(replacement != null && replacement.IsVisible, "The taskbar surface was lost after host termination.");
            Require(GetParent(new WindowInteropHelper(replacement!).Handle) == FindWindow("Shell_TrayWnd", null),
                "The surviving/recreated surface did not reconnect to the taskbar.");
            var refreshed = (Delegate?)typeof(BatteryService).GetField("Refreshed",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(MouseDock.App.Battery);
            Require(refreshed == null || refreshed.GetInvocationList().All(handler => handler.Target != initial),
                "The lost child is still subscribed to battery refreshes.");
        }
        finally
        {
            if (!host.HasExited) { host.Kill(); host.WaitForExit(5000); }
            initial?.Close();
            MouseDock.App.Taskbar?.Close();
            typeof(MouseDock.App).GetProperty("Taskbar")!.SetValue(null, savedTaskbar);
        }
    }

    internal static void ShellFullscreen()
    {
        var monitor = new Rect(0, 0, 2560, 1440);
        bool Evaluate(string windowClass, string process) =>
            TaskbarPlacement.IsFullscreen(monitor, monitor, false, windowClass, process);
        Require(!Evaluate("Windows.UI.Core.CoreWindow", "StartMenuExperienceHost"), "Start was treated as a game.");
        Require(!Evaluate("Windows.UI.Core.CoreWindow", "SearchHost"), "Search was treated as a game.");
        Require(!Evaluate("XamlExplorerHostIslandWindow", "explorer"), "A shell overlay was treated as a game.");
        Require(Evaluate("Windows.UI.Core.CoreWindow", "FullscreenGame"), "A fullscreen UWP app was excluded.");
        Require(Evaluate("CabinetWClass", "explorer"), "A fullscreen file browser is not a shell overlay.");
    }

    internal static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly AppSettings _settings = MouseDock.App.Settings;
        private readonly BatteryService? _battery = MouseDock.App.Battery;
        private readonly BatteryService _testBattery;
        internal TaskbarWindow Window { get; }
        internal Fixture()
        {
            var settings = new AppSettings { Provider = "json", JsonPath = "missing-embedded-fixture.json" };
            _testBattery = new BatteryService(settings);
            typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
            typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, _testBattery);
            _testBattery.Devices.Add(new DeviceInfo { Id = "embedded", Name = "Embedded mouse", Level = 66 });
            Window = new TaskbarWindow();
        }
        public void Dispose()
        {
            Window.Close();
            _testBattery.Dispose();
            typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, _settings);
            typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, _battery);
        }
    }
}

