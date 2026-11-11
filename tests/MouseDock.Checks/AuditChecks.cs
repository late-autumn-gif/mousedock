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

    internal static void DesktopPet()
    {
        foreach (string character in new[] { "shin-chan", "xin-yue-hu", "rikka" })
            VerifyDesktopPet(character);
    }

    private static void VerifyDesktopPet(string character)
    {
        using var battery = EmptyService(out var settings);
        settings.DesktopPetCharacter = character;
        Require(settings.Clone().DesktopPetCharacter == character, "The selected character must survive settings cloning.");
        var source = new SharedSource { Reading = 62 };
        source.Device.Name = "狼蛛 SC580";
        source.ExtraDevices = new[]
        {
            new DeviceInfo { Id = "pet-device-1", Name = "VGN Neon75 Air", Kind = DeviceKind.Keyboard, Level = 88 },
            new DeviceInfo { Id = "pet-device-2", Name = "Air Plus Pro3", Kind = DeviceKind.Headset, Level = 80 }
        };
        if (character == "shin-chan")
            source.ExtraDevices = source.ExtraDevices.Concat(Enumerable.Range(3, 5).Select(i =>
                new DeviceInfo { Id = $"pet-device-{i}", Name = $"测试设备 {i}", Level = 40 + i })).ToArray();
        SetSource(battery, source);
        Wait(battery.PollAsync());
        var pet = new DesktopPetWindow(battery, settings);
        void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        var image = (Image)typeof(DesktopPetWindow).GetField("_character", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
        DeviceInfo Followed() => (DeviceInfo)typeof(DesktopPetWindow).GetField("_device", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
        long ActionUntil() => (long)typeof(DesktopPetWindow).GetField("_actionUntil", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
        try
        {
            pet.Show();
            Flush();
            Require(image.Source is System.Windows.Media.Imaging.BitmapSource { PixelWidth: 512, PixelHeight: 512 }, "The embedded atlas must load in the real window.");
            if (character == "shin-chan")
            {
                var list = (StackPanel)typeof(DesktopPetWindow).GetField("_deviceList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
                string[] Rows() => list.Children.OfType<Button>().Select(item => ((DeviceInfo)((Grid)item.Content).DataContext).Id).ToArray();
                Require(Rows().Length == 6 && Rows().SequenceEqual(battery.VisibleDevices.Select(d => d.Id)),
                    "The desktop pet must respect the same six-device cap and order as other surfaces.");
                typeof(DesktopPetWindow).GetMethod("BuildMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(pet, null);
                var displayMenu = pet.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "显示设备"));
                var last = (MenuItem)displayMenu.Items[5];
                last.IsChecked = false;
                last.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Flush();
                Require(!battery.Devices[5].Selected && Rows().Length == 6 && Rows().Contains(battery.Devices[6].Id), "The next selected device must replace a deselected row.");
                battery.SetSelected(battery.Devices[5], true);
                Flush();
                Require(Rows().Length == 6 && Rows().Contains(battery.Devices[5].Id), "Settings-side selection must restore the pet row.");
            }
            var frames = (ImageSource[])typeof(DesktopPetWindow).GetField("_frames", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pet)!;
            if (character != "shin-chan")
            {
                source.Reading = 15;
                Wait(battery.PollAsync());
                Flush();
                Require(ReferenceEquals(image.Source, frames[1]), "The selected character must show its low battery art.");
            }
            source.Device.Charging = true;
            Wait(battery.PollAsync());
            Flush();
            Require(Followed().IsChargingActive, "A confirmed charging transition must reach the followed device.");
            if (character != "shin-chan")
                Require(ReferenceEquals(image.Source, frames[1]), "Confirmed charging must select that character's charging art.");
            long firstAction = ActionUntil();
            source.Reading = 63;
            Wait(battery.PollAsync());
            Flush();
            Require(ActionUntil() == firstAction, "A percentage change must not replay the plug pose.");
            source.Device.Charging = false;
            source.Device.ChargeFull = true;
            source.Reading = 100;
            Wait(battery.PollAsync());
            Flush();
            long completion = ActionUntil();
            Require(Followed().IsFullActive, "Explicit Full must reach the pet.");
            if (Environment.GetEnvironmentVariable("MOUSEDOCK_PET_PREVIEW") is { Length: > 0 } preview)
            {
                Wait(Task.Delay(360));
                var surface = (FrameworkElement)pet.Content;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight),
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                string output = Path.Combine(Path.GetDirectoryName(preview) ?? "", $"{Path.GetFileNameWithoutExtension(preview)}-{character}.png");
                using var file = File.Create(output);
                png.Save(file);
            }
            Wait(battery.PollAsync());
            Flush();
            Require(ActionUntil() == completion, "Repeated full reports must not restart the celebration.");
            source.Device.ChargeFull = false;
            source.Reading = -1;
            Wait(battery.PollAsync());
            Flush();
            Require(Followed() is { IsStale: true, Level: 100 }, "The pet must retain the historical level as stale.");
        }
        finally { pet.Close(); }
        Require(!pet.IsVisible, "Closing the desktop pet must release the window.");
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
        Require(!merged.Charging && merged.Level == 80, "A channel without a reading must not override the active channel.");
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
        using (var service = EmptyService(out _))
        {
            var source = new SharedSource { Reading = 98, ReuseInstance = true };
            SetSource(service, source);
            int notices = 0;
            service.ChargeCompleted += _ => notices++;
            source.Device.Charging = true;
            Wait(service.PollAsync());
            source.Reading = 100;
            Wait(service.PollAsync());
            Require(notices == 0, "100% while charging must not complete the session.");
            source.Device.Charging = false;
            source.Device.ChargeFull = true;
            Wait(service.PollAsync());
            Wait(service.PollAsync());
            Require(notices == 1 && service.Devices.Single().IsFullActive, "An explicit Full must notify only once.");
            source.Device.ChargeFull = false;
            source.Device.Charging = true;
            source.Reading = 98;
            Wait(service.PollAsync());
            source.Device.Connected = false;
            Wait(service.PollAsync());
            source.Device.Connected = true;
            source.Device.Charging = false;
            source.Reading = -1;
            Wait(service.PollAsync());
            Require(notices == 1 && !service.Devices.Single().IsFullActive, "A reconnect with a cached level cannot notify.");
        }
        foreach (bool reuse in new[] { true, false })
        {
        using var battery = EmptyService(out _);
        var source = new SharedSource { Reading = 70, ReuseInstance = reuse };
        SetSource(battery, source);
        Wait(battery.PollAsync());
        int first = battery.Devices.Single().Level;
        source.Reading = -1;
        source.Device.Name = "狼蛛 2.4G 接收器（型号未确认）";
        Wait(battery.PollAsync());
        Console.WriteLine($"AUDIT cached reading: first={first}%, next read failed, displayed={battery.Devices.Single().Level}, stale={battery.Devices.Single().IsStale}.");
        Require(first == 70 && battery.Devices.Single().Level == 70, "A failed read erased the last valid level.");
        Require(battery.Devices.Single() is { Name: "Audit device", ValueOpacity: 0.45 },
            "A sleeping receiver must preserve the last model name and dim the cached value.");
        Require(source.Device.Level == -1, "The cached display value must not be written back to the source.");
        Wait(battery.PollAsync());
        Wait(battery.PollAsync());
        Require(battery.Devices.Single().Level == 70 && battery.Devices.Single().IsStale,
            "Repeated failures must retain the level and mark it stale.");
        source.Reading = 64;
        source.Device.Name = "Awake mouse";
        Wait(battery.PollAsync());
        Require(battery.Devices.Single() is { Name: "Awake mouse", ValueOpacity: 1 },
            "Fresh detection must replace the retained name and restore brightness.");
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
            battery.VisibleDevices.Add(new DeviceInfo { Name = $"Device {i + 1}", Level = 50 });
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
        public DeviceInfo[] ExtraDevices { get; set; } = Array.Empty<DeviceInfo>();
        public bool ReuseInstance { get; set; } = true;
        public IEnumerable<DeviceInfo> Read()
        {
            Device.Level = Reading;
            return new[] { ReuseInstance ? Device : new DeviceInfo
            {
                Id = Device.Id, Name = Device.Name, Level = Reading, Selected = Device.Selected
            } }.Concat(ExtraDevices);
        }
    }

    internal static void MiniLiveReading()
    {
        using var battery = EmptyService(out var settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        var device = new DeviceInfo { Name = "Audit mouse", Level = 75 };
        battery.VisibleDevices.Add(device);
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
        string image = FindRepoAsset("src/MouseDock/Assets/app.png");
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
        return (BatteryService)Activator.CreateInstance(typeof(BatteryService),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object?[] { settings, null, false }, null)!;
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

    /// <summary>Tests can run from the repo root or from bin; walk up until the asset is found.</summary>
    private static string FindRepoAsset(string relative)
    {
        string direct = Path.GetFullPath(relative);
        if (File.Exists(direct)) return direct;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        return direct;
    }
}
