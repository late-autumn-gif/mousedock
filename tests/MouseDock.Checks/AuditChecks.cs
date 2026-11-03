using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

// Regression checks promoted from the original diagnostic probes.
internal static class AuditChecks
{
    internal static int Run()
    {
        ChargingMerge();
        PollInterval();
        CachedReading();
        FifthMiniCell();
        MiniLiveReading();
        ClosedSettings();
        ClearBackground();
        return 0;
    }

    internal static void ChargingMerge()
    {
        var assembly = typeof(AppSettings).Assembly;
        var detectedType = assembly.GetType("MouseDock.Services.Detection.DetectedDevice")!;
        dynamic detected = Activator.CreateInstance(detectedType)!;
        detected.Id = "audit";
        detected.Name = "Rechargeable mouse";
        detected.Level = 80;
        detected.Connected = true;
        detected.Charging = true;
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(detectedType))!;
        list.Add(detected);
        dynamic merged = assembly.GetType("MouseDock.Services.Detection.AutoDetectSource")!
            .GetMethod("MergeGroup", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [list])!;
        Console.WriteLine($"AUDIT charging merge: input Charging=True, output Charging={merged.Charging}.");
        Require(merged.Charging, "Merging the detection results discarded the charging flag.");
        detected.Charging = false;
        merged = assembly.GetType("MouseDock.Services.Detection.AutoDetectSource")!
            .GetMethod("MergeGroup", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [list])!;
        Require(!merged.Charging, "A non-charging device must remain non-charging.");
        dynamic second = Activator.CreateInstance(detectedType)!;
        second.Id = "audit";
        second.Connected = true;
        second.Level = -1;
        second.Charging = true;
        list.Add(second);
        merged = assembly.GetType("MouseDock.Services.Detection.AutoDetectSource")!
            .GetMethod("MergeGroup", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [list])!;
        Require(merged.Charging && merged.Level == 80, "Charging and the valid level must survive channel merging.");
    }

    internal static void PollInterval()
    {
        using var battery = EmptyService(out _, 120);
        var source = new SharedSource();
        source.Device.Selected = false;
        SetSource(battery, source);
        for (int i = 0; i < 3; i++) Wait(battery.PollAsync());
        var timer = (DispatcherTimer)typeof(BatteryService).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(battery)!;
        Console.WriteLine($"AUDIT refresh interval: configured=120s, unknown unselected device, actual={timer.Interval.TotalSeconds}s after 3 polls.");
        Require(timer.Interval == TimeSpan.FromSeconds(120), "An unreadable device overrode the selected poll interval.");
        source.Device.Selected = true;
        battery.Devices.Single().Selected = true;
        Require(battery.Devices.Single().Selected, "The selected-device scenario was not configured.");
        for (int i = 0; i < 6; i++) Wait(battery.PollAsync());
        Require(timer.Interval == TimeSpan.FromSeconds(120), "Selecting an unreadable device caused endless fast polling.");
    }

    internal static void CachedReading()
    {
        foreach (bool reuse in new[] { true, false })
        {
        using var battery = EmptyService(out _);
        var source = new SharedSource { Reading = 70, ReuseInstance = reuse };
        SetSource(battery, source);
        Wait(battery.PollAsync());
        int first = battery.Devices.Single().Level;
        source.Reading = -1;
        Wait(battery.PollAsync());
        Console.WriteLine($"AUDIT cached reading: first={first}%, next read failed, displayed={battery.Devices.Single().Level}, stale={battery.Devices.Single().IsStale}.");
        Require(first == 70 && battery.Devices.Single().Level == 70, "A failed read erased the last valid level.");
        Require(source.Device.Level == -1, "The cached display value must not be written back to the source.");
        Wait(battery.PollAsync());
        Wait(battery.PollAsync());
        Require(battery.Devices.Single().Level == 70 && battery.Devices.Single().IsStale,
            "Repeated failures must retain the level and mark it stale.");
        source.Reading = 64;
        Wait(battery.PollAsync());
        Require(battery.Devices.Single().Level == 64 && !battery.Devices.Single().IsStale,
            "A successful reading must replace the cached level and clear stale state.");
        }
    }

    internal static void FifthMiniCell()
    {
        using var battery = EmptyService(out var settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        for (int i = 0; i < 5; i++)
            battery.Devices.Add(new DeviceInfo { Name = $"Device {i + 1}", Level = 50 });
        var mini = new MiniWindow { Left = -2000, ShowActivated = false };
        try
        {
            mini.Show();
            mini.UpdateLayout();
            var list = (ItemsControl)mini.FindName("CellList");
            var fifth = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(4);
            var bounds = fifth.TransformToAncestor(list).TransformBounds(new Rect(fifth.RenderSize));
            Console.WriteLine($"AUDIT mini fifth cell: item count={list.Items.Count}, list height={list.ActualHeight}, fifth bottom={bounds.Bottom}, overflow={bounds.Bottom > list.ActualHeight}.");
            Require(list.Items.Count == 5 && bounds.Bottom <= list.ActualHeight + 1,
                "The fifth selected device extends outside the mini layout.");
        }
        finally { mini.Close(); }
    }

    private sealed class SharedSource : IBatterySource
    {
        public string Key => "audit";
        public string Label => "audit";
        public DeviceInfo Device { get; } = new DeviceInfo { Id = "shared", Name = "Audit device" };
        public int Reading { get; set; } = -1;
        public bool ReuseInstance { get; set; } = true;
        public IEnumerable<DeviceInfo> Read()
        {
            Device.Level = Reading;
            return new[] { ReuseInstance ? Device : new DeviceInfo
            {
                Id = Device.Id, Name = Device.Name, Level = Reading, Selected = Device.Selected
            } };
        }
    }

    internal static void MiniLiveReading()
    {
        using var battery = EmptyService(out var settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        var device = new DeviceInfo { Name = "Audit mouse", Level = 75 };
        battery.Devices.Add(device);
        var mini = new MiniWindow { Left = -2000, ShowActivated = false };
        try
        {
            mini.Show();
            mini.UpdateLayout();
            string before = string.Join("|", Texts(mini));
            device.Level = 74;
            mini.Rebuild();
            mini.UpdateLayout();
            Console.WriteLine($"AUDIT mini live reading: before={before}, model={device.MiniText}, rendered={string.Join("|", Texts(mini))}.");
            Require(Texts(mini).Contains("74%") && !Texts(mini).Contains("75%"),
                "The mini window still renders the old percentage.");
            device.Level = -1;
            mini.UpdateLayout();
            Require(Texts(mini).Contains(device.MiniText) && !Texts(mini).Contains("74%"),
                "The mini text did not update when the reading became unknown.");
            device.Connected = false;
            mini.UpdateLayout();
            Require(Texts(mini).Contains(device.MiniText), "The mini text did not update on disconnect.");
        }
        finally { mini.Close(); }
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text) yield return text.Text;
            foreach (var value in Texts(child)) yield return value;
        }
    }

    internal static void ClosedSettings()
    {
        using var battery = EmptyService(out var settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        var window = new SettingsWindow { Left = -2000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.Close();
            Require(!window.IsVisible, "Closing settings must hide it.");
            window.Show();
            Require(window.IsVisible, "Settings did not reopen.");
        }
        finally { window.Hide(); }
    }

    internal static void ClearBackground()
    {
        using var battery = EmptyService(out var settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        string image = Path.GetFullPath("src/MouseDock/Assets/app.png");
        foreach (Window window in new Window[] { new DockWindow(), new MiniWindow() })
        {
            try
            {
                var apply = window.GetType().GetMethod("ApplyBackground")!;
                var root = (Border)window.FindName("Root");
                apply.Invoke(window, [image]);
                Require(root.Background is ImageBrush, "The fixture did not install an image background.");
                apply.Invoke(window, [""]);
                Require(root.Background is LinearGradientBrush,
                    $"{window.GetType().Name} retained its image after clearing the path.");
                window.GetType().GetMethod("ApplyTheme")!.Invoke(window, [ThemePreset.Presets[1], false]);
                var gradient = (LinearGradientBrush)root.Background;
                Require(gradient.GradientStops[0].Color == ThemePreset.Presets[1].BgTop,
                    "The restored background no longer follows theme changes.");
            }
            finally { window.Close(); }
        }
    }

    private static BatteryService EmptyService(out AppSettings settings, int refreshSeconds = 30)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        settings = new AppSettings { Provider = "json", JsonPath = "nonexistent-audit-fixture.json", RefreshSeconds = refreshSeconds };
        var battery = new BatteryService(settings);
        var frame = new DispatcherFrame();
        EventHandler handler = (_, _) => frame.Continue = false;
        battery.Refreshed += handler;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => frame.Continue = false;
        timeout.Start();
        Dispatcher.PushFrame(frame);
        timeout.Stop();
        battery.Refreshed -= handler;
        return battery;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void SetSource(BatteryService battery, IBatterySource source) =>
        typeof(BatteryService).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(battery, source);

    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false),
                TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
}
