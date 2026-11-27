using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;
using NAudio.CoreAudioApi;

internal static class DeviceQuickActionsChecks
{
    internal static void Discovery()
    {
        using var fixture = new AppearanceFixture();
        var source = new ScanSource();
        typeof(BatteryService).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fixture.Battery, source);
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            bool refreshed = false;
            fixture.Battery.Refreshed += (_, _) => refreshed = true;
            fixture.Settings.HiddenDeviceIds.Add("hidden-fixture");
            var dock = new DockWindow();
            fixture.Set("Dock", dock);
            ((Button)dock.FindName("RefreshDevicesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => refreshed);
            Require(source.Scans == 1 && fixture.Battery.VisibleDevices.Any(d => d.Id == "usb-fixture" && d.Level < 0),
                "The card control did not discover a new device with an unknown battery.");
            Require(fixture.Battery.Devices.Any(d => d.Id == "hidden-fixture") &&
                fixture.Battery.VisibleDevices.All(d => d.Id != "hidden-fixture"), "Refresh overrode a hidden device.");
            source.Bluetooth = true;
            refreshed = false;
            var icon = new IconWindow { ShowActivated = false };
            fixture.Set("Icon", icon);
            icon.Show();
            var menu = icon.CreateIconMenu();
            Find(menu.Items, "刷新设备").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            PumpUntil(() => refreshed);
            Require(source.Scans == 2 && fixture.Battery.VisibleDevices.Any(d => d.Id == "ble-fixture"),
                "The floating menu did not discover the second connected device.");
            Require(Find(menu.Items, "悬浮图标样式").Items.Count == IconWindow.StyleChoices.Length &&
                Find(menu.Items, "切换显示方式").Items.Count == 6, "The floating menu lost a group of choices.");
            menu.IsOpen = true;
            try { TaskbarEmbeddingChecks.Pump(100); SaveVisual(menu, "floating-device-menu"); }
            finally { menu.IsOpen = false; }
        }
        finally { SynchronizationContext.SetSynchronizationContext(oldContext); }
    }

    internal static void Rings()
    {
        using var fixture = new AppearanceFixture();
        var window = new SettingsWindow();
        fixture.Set("SettingsView", window);
        window.Reload();
        window.SelectSection("display");
        var whole = (RadioButton)window.FindName("RingContinuous");
        var split = (RadioButton)window.FindName("RingSegmented");
        Require(split.IsChecked == true, "Existing profiles should retain three arcs.");
        string oldKey = TaskbarBatteryStrip.AppearanceKey(fixture.Settings, MouseDock.App.Theme);
        whole.IsChecked = true;
        Require(SettingsStore.Load().Clone().ContinuousBatteryRing &&
            oldKey != TaskbarBatteryStrip.AppearanceKey(fixture.Settings, MouseDock.App.Theme) &&
            ScreenEdgeWindow.CreateRingBadge(fixture.Battery.VisibleDevices.First(), fixture.Settings, MouseDock.App.Theme).Ring.ContinuousRing,
            "The continuous setting did not persist or reach the taskbar and right edge.");
        split.IsChecked = true;
        Require(!SettingsStore.Load().ContinuousBatteryRing, "The segmented option did not persist.");

        var segmented = Ring(100, false);
        var empty = Ring(0, true);
        var unknown = Ring(-1, true);
        var full = Ring(100, true);
        var partial = Ring(75, true);
        Require(Pixel(segmented, 32, 12).A == 0 && Pixel(empty, 32, 12).B >= 200,
            "The continuous track still has a gap or the segmented track closed up.");
        Require(Pixel(unknown, 32, 12).A < Pixel(empty, 32, 12).A && Pixel(full, 32, 12).R >= 200,
            "Unknown, zero and full ring states lost their visual distinction.");
        Require(Pixel(partial, 32, 52).R >= 200 && Pixel(partial, 18, 18).B >= 200,
            "A progress arc greater than 180 degrees rendered the wrong sweep.");

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.White };
        panel.Children.Add(new Image { Source = full, Width = 64, Height = 64 });
        panel.Children.Add(new Image { Source = segmented, Width = 64, Height = 64 });
        panel.Measure(new Size(128, 64));
        panel.Arrange(new Rect(0, 0, 128, 64));
        SaveVisual(panel, "battery-ring-variants");
    }

    internal static void Audio()
    {
        var roles = new Dictionary<Role, string> { [Role.Console] = "old-default", [Role.Multimedia] = "old-media",
            [Role.Communications] = "calls" };
        var failed = WindowsAudioRouting.ApplyDefaults("new", role => roles[role], (role, id) =>
        {
            if (role == Role.Multimedia) throw new InvalidOperationException("fixture");
            roles[role] = id;
        });
        Require(!failed.Success && roles[Role.Console] == "old-default" && roles[Role.Multimedia] == "old-media",
            "A partial default switch did not restore the previous default.");
        var applied = WindowsAudioRouting.ApplyDefaults("new", role => roles[role], (role, id) => roles[role] = id);
        Require(applied.Success && roles[Role.Console] == "new" && roles[Role.Multimedia] == "new" &&
            roles[Role.Communications] == "calls", "Switch did not verify both ordinary defaults or changed calls.");
        var endpoints = WindowsAudioRouting.MenuOrder(
            [new("wire", "Headset"), new("usb", "Headset"), new("bt", "Bluetooth"), new("USB", "Duplicate endpoint")], "usb");
        Require(endpoints.Length == 3 && endpoints[0].Id == "usb" && endpoints.Any(e => e.Name == "Headset（2）"),
            "Default ordering or disambiguation was lost.");

        using var fixture = new AppearanceFixture();
        var owner = new Window { Width = 200, Height = 100, ShowActivated = false, ShowInTaskbar = false };
        owner.Show();
        var menu = MouseDock.App.CreateThemedMenu(owner);
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        (DataFlow flow, string id) chosen = default;
        try
        {
            foreach (var direction in new[] { DataFlow.Capture, DataFlow.Render })
            {
                var audio = MouseDock.App.CreateAudioSubmenu(direction, owner,
                    flow => flow == direction ? new(endpoints, "usb") : new([], null),
                    (flow, id) => { chosen = (flow, id); return new(true, "ok"); });
                menu.Items.Add(audio);
                menu.IsOpen = true;
                audio.IsSubmenuOpen = true;
                PumpUntil(() => audio.Items.OfType<MenuItem>().Any(i => i.IsChecked));
                Require(Find(audio.Items, "Headset · 当前默认").IsChecked &&
                    Find(audio.Items, "Bluetooth").IsEnabled, "The audio list did not show its current default and available routes.");
                Find(audio.Items, "Bluetooth").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                PumpUntil(() => chosen == (direction, "bt"));
                menu.IsOpen = false;
                menu.Items.Clear();
            }
        }
        finally
        {
            menu.IsOpen = false;
            owner.Close();
            SynchronizationContext.SetSynchronizationContext(oldContext);
        }

        bool audioAvailable = false;
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            var snapshot = WindowsAudioRouting.List(flow);
            // Audio service can be unavailable on a headless machine. The menu must
            // still receive a bounded, user-visible result without performing writes.
            var active = snapshot.Devices ?? throw new InvalidOperationException("Windows did not return an endpoint array.");
            Require(snapshot.Error == null || active.Length == 0, "Windows enumeration returned an invalid snapshot.");
            audioAvailable |= active.Length > 0;
            Console.WriteLine($"Windows audio {flow}: {active.Length} active, default present={snapshot.DefaultId != null}.");
        }
        if (audioAvailable)
        {
            var clientType = typeof(WindowsAudioRouting).GetNestedType("PolicyConfigClient", BindingFlags.NonPublic)!;
            var policyType = typeof(WindowsAudioRouting).GetNestedType("IPolicyConfig", BindingFlags.NonPublic)!;
            var client = Activator.CreateInstance(clientType)!;
            try { Require(policyType.IsInstanceOfType(client), "Windows does not expose the expected audio policy interface."); }
            finally { Marshal.ReleaseComObject(client); }
        }
    }

    private static MenuItem Find(ItemCollection items, string label) =>
        items.OfType<MenuItem>().Single(item => Equals(item.Header, label));

    private static RenderTargetBitmap Ring(int level, bool continuous)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            BatteryRingDrawing.Draw(dc, new Point(32, 32), 20, level, Brushes.Blue, Brushes.Red, continuous);
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static Color Pixel(BitmapSource source, int x, int y)
    {
        var bytes = new byte[4];
        source.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0);
        return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    private static void SaveVisual(FrameworkElement element, string name)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);
        Directory.CreateDirectory("artifacts");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine("artifacts", name + ".png"));
        encoder.Save(file);
    }

    private static void PumpUntil(Func<bool> done)
    {
        for (int i = 0; i < 50 && !done(); i++) TaskbarEmbeddingChecks.Pump(40);
        Require(done(), "Timed out waiting for the menu action.");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ScanSource : IBatterySource
    {
        public string Key => "discovery";
        public string Label => "discovery";
        internal int Scans;
        internal bool Bluetooth;
        public IEnumerable<DeviceInfo> Read()
        {
            Scans++;
            yield return new() { Id = "usb-fixture", Name = "USB fixture", Kind = DeviceKind.Mouse, Connected = true, Level = -1 };
            yield return new() { Id = "hidden-fixture", Name = "Hidden fixture", Kind = DeviceKind.Mouse, Connected = true, Level = -1 };
            if (Bluetooth)
                yield return new() { Id = "ble-fixture", Name = "Bluetooth fixture", Kind = DeviceKind.Headset, Connected = true, Level = -1 };
        }
    }
}
