using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

internal static class TaskbarFollowChecks
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string id);

    internal static int RunButtonFixture(string id)
    {
        SetCurrentProcessExplicitAppUserModelID("MouseDock.FollowFixture." + id);
        var app = new Application();
        var window = new Window { Title = "MouseDock taskbar layout fixture " + id,
            Width = 200, Height = 80, Left = -20000, Top = -20000, ShowActivated = false };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
        timer.Start();
        app.Run(window);
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static Rect Follow(Rect bar, uint edge, Size content, double dpi, Rect icons,
        Rect[] occupied, double extra = 0)
    {
        var method = typeof(TaskbarPlacement).GetMethod("TryFollow");
        Require(method != null, "Taskbar placement cannot follow the application icon group.");
        object[] args = [bar, edge, content, dpi, icons, occupied, Rect.Empty, extra];
        bool fits = (bool)method!.Invoke(null, args)!;
        return fits ? (Rect)args[6] : Rect.Empty;
    }

    internal static void Motion()
    {
        var bar = new Rect(0, 1040, 1920, 40);
        var tray = new Rect(1700, 1040, 220, 40);
        foreach (var (icons, expected) in new[] {
            (new Rect(500, 1040, 500, 40), new Rect(1008, 1040, 80, 40)),
            (new Rect(400, 1040, 700, 40), new Rect(1108, 1040, 80, 40)),
            (new Rect(550, 1040, 400, 40), new Rect(958, 1040, 80, 40))
        })
            Require(Follow(bar, 3, new Size(80, 40), 1, icons, [icons, tray]) == expected,
                "The strip failed to follow opening or closing applications.");
        var leftAligned = new Rect(0, 1040, 400, 40);
        Require(Follow(bar, 3, new Size(80, 40), 1, leftAligned, [leftAligned, tray]) ==
            new Rect(408, 1040, 80, 40), "Left-aligned taskbars must follow the final application.");
    }

    internal static void Obstacles()
    {
        var bar = new Rect(0, 1040, 1920, 40);
        var icons = new Rect(500, 1040, 1000, 40);
        var tray = new Rect(1540, 1040, 380, 40);
        Require(Follow(bar, 3, new Size(80, 40), 1, icons, [icons, tray]) ==
            new Rect(412, 1040, 80, 40), "A growing tray must move the strip to the other side.");
        var widget = new Rect(400, 1040, 80, 40);
        Require(Follow(bar, 3, new Size(80, 40), 1, icons, [icons, tray, widget]) ==
            new Rect(312, 1040, 80, 40), "Widgets must remain clickable and unobstructed.");
        Require(Follow(bar, 3, new Size(80, 40), 1, icons,
            [icons, tray, new Rect(0, 1040, 500, 40)]).IsEmpty,
            "A full taskbar must request the right-edge surface instead of overlapping.");
        Require(Follow(bar, 3, new Size(80, 40), 1, Rect.Empty, [tray]).IsEmpty,
            "Unknown application geometry cannot justify an arbitrary taskbar position.");
    }

    internal static void Scaling()
    {
        var icons = new Rect(-1400, 1368, 600, 72);
        Require(Follow(new Rect(-2560, 1368, 2560, 72), 3, new Size(80, 40), 1.5,
            icons, [icons, new Rect(-300, 1368, 300, 72)]) == new Rect(-788, 1374, 120, 60),
            "Following must preserve physical-pixel DPI and negative monitor coordinates.");
        var verticalIcons = new Rect(-48, 100, 48, 600);
        Require(Follow(new Rect(-48, 0, 48, 1080), 0, new Size(80, 40), 1,
            verticalIcons, [verticalIcons, new Rect(-48, 950, 48, 130)]) ==
            new Rect(-44, 708, 40, 80), "Vertical taskbars must follow along their long axis.");
        Require(Follow(new Rect(0, 0, 320, 24), 1, new Size(80, 40), 1,
            new Rect(0, 0, 100, 24), [new Rect(0, 0, 100, 24)]) ==
            new Rect(108, 0, 48, 24), "Thin taskbars must scale only to their thickness.");
        Require(Follow(new Rect(0, 0, 320, 40), 3, new Size(1000, 40), 1,
            new Rect(0, 0, 100, 40), [new Rect(0, 0, 100, 40)]).IsEmpty,
            "An oversized strip must overflow instead of becoming unreadably small.");
    }

    internal static void RestorationReserve()
    {
        var bar = new Rect(0, 0, 400, 40);
        var icons = new Rect(0, 0, 300, 40);
        var tray = new Rect(396, 0, 4, 40);
        Require(!Follow(bar, 3, new Size(80, 40), 1, icons, [icons, tray]).IsEmpty,
            "An exact safe gap must fit while already on the taskbar.");
        Require(Follow(bar, 3, new Size(80, 40), 1, icons, [icons, tray], 8).IsEmpty,
            "Restoration needs a reserve to avoid bouncing at the space boundary.");
        icons.Width = 280;
        Require(!Follow(bar, 3, new Size(80, 40), 1, icons, [icons, tray], 8).IsEmpty,
            "The strip must return when sufficient space is restored.");
    }

    internal static void Preferences()
    {
        var property = typeof(AppSettings).GetProperty("TaskbarFollowIcons");
        Require(property != null, "Automatic following needs a saved opt-out.");
        var settings = new AppSettings { TaskbarOffset = 17 };
        Require((bool)property!.GetValue(settings)!, "Existing settings should enable automatic following.");
        property.SetValue(settings, false);
        var options = new JsonSerializerOptions {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings.Clone(), options), options)!;
        Require(!(bool)property.GetValue(restored)! && restored.TaskbarOffset == 17,
            "Manual mode and its original offset must survive save/restart.");
        Require((bool)property.GetValue(JsonSerializer.Deserialize<AppSettings>("{}")!)!,
            "An old configuration must receive automatic following without a destructive migration.");
    }

    internal static void LayoutFiltering()
    {
        var assembly = typeof(TaskbarPlacement).Assembly;
        var reader = assembly.GetType("MouseDock.Services.TaskbarLayoutReader");
        var element = assembly.GetType("MouseDock.Services.TaskbarElement");
        Require(reader != null && element != null, "No taskbar layout reader exists.");
        object Item(Rect bounds, string cls, string id, bool button = true, bool hidden = false, int pid = 42) =>
            Activator.CreateInstance(element!, bounds, cls, id, button, hidden, pid)!;
        object[] items = [
            Item(new Rect(400, 0, 200, 40), "Taskbar.TaskListButtonAutomationPeer", "Appid: example"),
            Item(new Rect(350, 0, 50, 40), "ToggleButton", "StartButton"),
            Item(new Rect(900, 0, 100, 40), "TrayNotifyWnd", "", false),
            Item(new Rect(0, 0, 100, 40), "ToggleButton", "WidgetsButton"),
            Item(new Rect(0, 0, 1000, 40), "Taskbar.TaskbarFrameAutomationPeer", "TaskbarFrame", false),
            Item(new Rect(600, 0, 290, 40), "Button", "Appid: own-window", pid: Environment.ProcessId),
            Item(new Rect(800, 0, 100, 40), "Taskbar.TaskListButtonAutomationPeer", "Appid: hidden", hidden: true)
        ];
        var elements = Array.CreateInstance(element!, items.Length);
        for (int i = 0; i < items.Length; i++) elements.SetValue(items[i], i);
        var snapshot = reader!.GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [new Rect(0, 0, 1000, 40), 3u, elements]);
        Require(snapshot != null, "A visible app group with a tray should produce usable geometry.");
        var type = snapshot!.GetType();
        Require((Rect)type.GetProperty("Icons")!.GetValue(snapshot)! == new Rect(350, 0, 250, 40),
            "Hidden buttons, widgets or our own window distorted the icon-group bounds.");
        Require(((Rect[])type.GetProperty("Occupied")!.GetValue(snapshot)!).Length == 4,
            "Enclosing containers cannot occupy all free taskbar space.");
        var matches = type.GetMethod("Matches")!;
        var captured = (DateTime)type.GetProperty("CapturedUtc")!.GetValue(snapshot)!;
        Require((bool)matches.Invoke(snapshot, [new Rect(0, 0, 1000, 40), 3u, captured.AddSeconds(1)])!,
            "Fresh geometry was discarded.");
        Require(!(bool)matches.Invoke(snapshot, [new Rect(0, 0, 1000, 40), 3u, captured.AddSeconds(4)])!,
            "Stale geometry could overlap new icons.");
        Require(!(bool)matches.Invoke(snapshot, [new Rect(0, 10, 1000, 40), 3u, captured])!,
            "A moved taskbar cannot reuse old screen coordinates.");
    }

    internal static void RefreshContinuity()
    {
        var type = typeof(TaskbarPlacement).Assembly.GetType("MouseDock.Services.TaskbarLayoutReader")!;
        var readingType = type.GetNestedType("Reading", BindingFlags.NonPublic)!;
        var snapshotType = typeof(TaskbarPlacement).Assembly.GetType("MouseDock.Services.TaskbarLayoutSnapshot")!;
        var publish = type.GetMethod("Publish", BindingFlags.NonPublic | BindingFlags.Instance);
        Require(publish != null, "Failed refreshes must preserve a still-valid taskbar layout.");
        using var reader = (IDisposable)Activator.CreateInstance(type, Dispatcher.CurrentDispatcher, new Action(() => { }))!;
        var field = type.GetField("_reading", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object Reading(DateTime captured) => Activator.CreateInstance(readingType,
            new IntPtr(1), 42u, Activator.CreateInstance(snapshotType, new Rect(0, 0, 1000, 40), 3u,
                new Rect(300, 0, 400, 40), new[] { new Rect(300, 0, 400, 40) }, captured)!)!;
        var first = Reading(DateTime.UtcNow.AddSeconds(-1));
        publish!.Invoke(reader, [first]);
        publish.Invoke(reader, [null]);
        Require(ReferenceEquals(field.GetValue(reader), first),
            "A transient refresh failure discarded the previous layout and would toggle the visible surface.");
        var recovered = Reading(DateTime.UtcNow);
        publish.Invoke(reader, [recovered]);
        Require(ReferenceEquals(field.GetValue(reader), recovered),
            "A successful refresh did not replace the retained layout.");
        reader.Dispose();
        publish.Invoke(reader, [first]);
        Require(ReferenceEquals(field.GetValue(reader), recovered),
            "A late refresh modified a disposed reader.");
    }

    internal static void OverflowRouting()
    {
        using var fixture = new Fixture();
        var create = typeof(MouseDock.App).GetMethod("CreateTaskbarWindow", BindingFlags.NonPublic | BindingFlags.Static)!;
        var window = (TaskbarWindow)create.Invoke(null, null)!;
        var edge = new ScreenEdgeWindow();
        typeof(MouseDock.App).GetProperty("Taskbar")!.SetValue(null, window);
        typeof(MouseDock.App).GetProperty("Edge")!.SetValue(null, edge);
        try
        {
            var setOverflow = typeof(TaskbarWindow).GetMethod("SetOverflow", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(setOverflow != null, "The taskbar cannot request temporary right-edge overflow.");
            fixture.Settings.TaskbarFollowIcons = false;
            MouseDock.App.ShowCurrentSurface();
            IntPtr handle = new WindowInteropHelper(window).Handle;
            setOverflow!.Invoke(window, [true]);
            Require(edge.IsVisible && window.IsVisible && fixture.Settings.SurfaceMode == "taskbar",
                "Overflow must keep the taskbar controller alive without changing the saved surface.");
            Require(fixture.Settings.EdgeStyle == "card", "Overflow changed the user's right-edge style.");
            for (int i = 0; i < 20; i++) setOverflow.Invoke(window, [true]);
            Require(new WindowInteropHelper(window).Handle == handle,
                "Repeated layout updates recreated the taskbar window.");
            window.UpdateAnchor();
            Require(!edge.IsVisible && window.Opacity > 0,
                "Returning to manual placement left the overflow surface visible.");
            setOverflow.Invoke(window, [true]);
            MouseDock.App.SetSurface("edge");
            Require(edge.IsVisible && !window.IsVisible, "Explicit edge mode was hidden by taskbar cleanup.");
            MouseDock.App.SetSurface("taskbar");
            Require(!edge.IsVisible && window.IsVisible, "Manual taskbar mode left duplicate battery surfaces.");
        }
        finally { window.Close(); edge.Close(); }
    }

    internal static void SettingsControl()
    {
        using var fixture = new Fixture();
        var window = new SettingsWindow();
        try
        {
            window.Reload();
            var check = window.FindName("TaskbarFollowIconsBox") as CheckBox;
            var row = (Grid)window.FindName("TaskbarPositionRow");
            Require(check != null, "The automatic-follow setting is missing.");
            Require(check!.IsChecked == true && !row.IsEnabled,
                "Automatic placement must disable the irrelevant percentage slider.");
            check.IsChecked = false;
            Require(!fixture.Settings.TaskbarFollowIcons && row.IsEnabled && fixture.Settings.TaskbarOffset == 17,
                "Turning off automatic placement must restore the saved manual offset.");
            check.IsChecked = true;
            Require(fixture.Settings.TaskbarFollowIcons && !row.IsEnabled,
                "Automatic following cannot be re-enabled.");
        }
        finally { window.Hide(); }
    }

    internal static void LiveLayout()
    {
        var type = typeof(TaskbarPlacement).Assembly.GetType("MouseDock.Services.TaskbarLayoutReader")!;
        int notifications = 0;
        using var reader = (IDisposable)Activator.CreateInstance(type, Dispatcher.CurrentDispatcher,
            new Action(() => notifications++))!;
        type.GetMethod("Request")!.Invoke(reader, null);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (notifications == 0 && DateTime.UtcNow < deadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(20);
        }
        Require(notifications > 0, "The background accessibility read did not complete.");
        var reading = type.GetField("_reading", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader);
        Require(reading != null, "Explorer did not expose a usable taskbar layout.");
        var snapshot = reading!.GetType().GetProperty("Layout")!.GetValue(reading)!;
        var layoutType = snapshot.GetType();
        var bar = (Rect)layoutType.GetProperty("Taskbar")!.GetValue(snapshot)!;
        var icons = (Rect)layoutType.GetProperty("Icons")!.GetValue(snapshot)!;
        var occupied = (Rect[])layoutType.GetProperty("Occupied")!.GetValue(snapshot)!;
        var edge = (uint)layoutType.GetProperty("Edge")!.GetValue(snapshot)!;
        var placed = Follow(bar, edge, new Size(80, 40), 1, icons, occupied);
        Require(placed.IsEmpty || occupied.All(rect => !rect.IntersectsWith(placed)),
            "The real Explorer layout selected a position over an existing button.");
        Directory.CreateDirectory("artifacts/taskbar-follow");
        File.WriteAllText("artifacts/taskbar-follow/live-layout.json",
            JsonSerializer.Serialize(new { Bar = bar.ToString(), Icons = icons.ToString(),
                Occupied = occupied.Select(r => r.ToString()), Placement = placed.ToString() }));
    }

    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(IntPtr token, int kind, IntPtr buffer, int length, out int returned);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    internal static bool IsLowIntegrity()
    {
        if (!OpenProcessToken(new IntPtr(-1), 8, out var token)) return false;
        var buffer = Marshal.AllocHGlobal(2048);
        try
        {
            if (!GetTokenInformation(token, 25, buffer, 2048, out _)) return false;
            var sid = Marshal.ReadIntPtr(buffer);
            byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return count > 0 && Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1))) < 8192;
        }
        finally { Marshal.FreeHGlobal(buffer); CloseHandle(token); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string[] names = ["Settings", "Battery", "Taskbar", "Edge", "SettingsView", "Icon"];
        private readonly object?[] previous;
        private readonly PropertyInfo directory = typeof(SettingsStore).GetProperty("TestDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? oldDirectory;
        internal readonly AppSettings Settings = new() { Provider = "json", JsonPath = "missing-follow.json",
            TaskbarOffset = 17, SurfaceMode = "taskbar", EdgeStyle = "card" };
        private readonly BatteryService battery;
        internal Fixture()
        {
            previous = names.Select(name => typeof(MouseDock.App).GetProperty(name)!.GetValue(null)).ToArray();
            oldDirectory = directory.GetValue(null);
            directory.SetValue(null, Path.GetFullPath("artifacts/taskbar-follow/settings"));
            battery = (BatteryService)Activator.CreateInstance(typeof(BatteryService), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [Settings, null, false], null)!;
            foreach (var name in names) typeof(MouseDock.App).GetProperty(name)!.SetValue(null, null);
            typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, Settings);
            typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
            battery.VisibleDevices.Add(new DeviceInfo { Name = "Keyboard", Kind = DeviceKind.Keyboard, Level = 73 });
            battery.VisibleDevices.Add(new DeviceInfo { Name = "Mouse", Level = 90 });
        }
        public void Dispose()
        {
            battery.Dispose();
            for (int i = 0; i < names.Length; i++) typeof(MouseDock.App).GetProperty(names[i])!.SetValue(null, previous[i]);
            directory.SetValue(null, oldDirectory);
        }
    }
}
