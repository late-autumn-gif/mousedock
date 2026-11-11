using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using MouseDock;
using MouseDock.Models;
using MouseDock.Services;

internal static class StartupChecks
{
    private sealed class FixtureApp : App
    {
        protected override void OnStartup(StartupEventArgs e) { }
    }

    private sealed class StartupSource : IBatterySource
    {
        public string Key => "auto";
        public string Label => "Startup fixture";
        public bool Discovered;
        public IEnumerable<DeviceInfo> Read() => Discovered
            ? new[] { new DeviceInfo { Id = "startup-mouse", Name = "Startup mouse", Level = 86 } }
            : Array.Empty<DeviceInfo>();
    }

    internal static int Run(string mode)
    {
        var app = new FixtureApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        typeof(SettingsStore).GetProperty("TestDirectory", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, System.IO.Path.GetFullPath(System.IO.Path.Combine("artifacts", "startup-settings", mode)));
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var settings = new AppSettings
        {
            SurfaceMode = mode, AutoUpdate = false, HasShownAbout = true,
            SelectedDeviceIds = new() { "startup-mouse" }, DeviceSelectionInitialized = true
        };
        var source = new StartupSource();
        using var battery = (BatteryService)Activator.CreateInstance(typeof(BatteryService),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { settings, source, false }, null)!;
        typeof(App).GetProperty(nameof(App.Settings))!.SetValue(null, settings);
        typeof(App).GetProperty(nameof(App.Battery))!.SetValue(null, battery);
        typeof(App).GetProperty(nameof(App.QuietStartup))!.SetValue(null, true);
        typeof(App).GetMethod("BuildWindows", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
        var surfaces = new Dictionary<string, Window>
        {
            ["dock"] = App.Dock, ["mini"] = App.Mini, ["icon"] = App.Icon,
            ["taskbar"] = App.Taskbar, ["edge"] = App.Edge
        };
        if (mode == "pet")
            surfaces["pet"] = (Window)typeof(App).GetField("_desktopPet", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        try
        {
            if (!surfaces[mode].IsVisible || surfaces.Count(pair => pair.Value.IsVisible) != 1)
                throw new InvalidOperationException($"Startup must show only the saved {mode} surface before device discovery.");
            if (App.SettingsView.IsVisible)
                throw new InvalidOperationException("Startup must not open settings.");
            if (surfaces[mode].ShowActivated)
                throw new InvalidOperationException("Startup must not steal keyboard focus.");
            source.Discovered = true;
            var poll = battery.PollAsync();
            var frame = new DispatcherFrame();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) =>
            {
                if (poll.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
            if (!poll.IsCompleted) throw new InvalidOperationException("Battery discovery timed out.");
            poll.GetAwaiter().GetResult();
            if (!surfaces[mode].IsVisible || battery.VisibleDevices.Count != 1)
                throw new InvalidOperationException($"Late discovery: visible={surfaces[mode].IsVisible}, devices={battery.Devices.Count}, displayed={battery.VisibleDevices.Count}.");
            if (mode == "pet")
            {
                App.SetSurface("mini");
                if (surfaces["pet"].IsVisible || !App.Mini.IsVisible)
                    throw new InvalidOperationException("Switching to mini must hide the desktop pet.");
                App.SetSurface("pet");
                if (!surfaces["pet"].IsVisible || surfaces.Count(pair => pair.Value.IsVisible) != 1)
                    throw new InvalidOperationException("Switching back to pet must hide every other display mode.");
            }
            var pinnedSurface = mode == "taskbar" && App.Taskbar.UsesScreenEdge ? App.Edge : surfaces[mode];
            if (mode == "taskbar" && App.Taskbar.UsesScreenEdge && !App.Edge.IsVisible)
                throw new InvalidOperationException("Automatic taskbar overflow did not show the right-edge surface.");
            bool usesEdge = mode == "edge" || (mode == "taskbar" && App.Taskbar.UsesScreenEdge);
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (usesEdge && screen != null && ScreenEdgeAnchor.IsForegroundFullscreen(screen.Bounds))
            {
                if (pinnedSurface.Opacity != 0 || pinnedSurface.IsHitTestVisible)
                    throw new InvalidOperationException("Fullscreen suppression must keep the restored edge surface transparent and non-interactive.");
                Console.WriteLine($"PASS startup restores {mode} and respects fullscreen suppression.");
                return 0;
            }
            if (mode is "taskbar" or "edge" && pinnedSurface.Opacity <= 0)
            {
                object[] anchorArgs = { Rect.Empty, (uint)0, 0.0 };
                var anchor = typeof(App).Assembly.GetType("MouseDock.Services.TaskbarAnchor")!
                    .GetMethod("TryGet")!.Invoke(null, anchorArgs);
                if (anchor is false) {
                    Console.WriteLine("SKIP shell opacity assertion: Explorer taskbar unavailable; saved surface and late discovery checks passed.");
                    return 0;
                }
                throw new InvalidOperationException($"Pinned surface stayed transparent: mode={mode}, taskbar={anchor}, bounds={anchorArgs[0]}.");
            }
            Console.WriteLine($"PASS startup restores {mode} before and after device discovery without opening settings.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL " + ex.Message);
            return 1;
        }
        finally { app.Shutdown(); }
    }
}
