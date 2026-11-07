using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

internal static class EdgeDisplayChecks
{
    private static Type TypeOf(string name) => typeof(AppSettings).Assembly.GetType("MouseDock." + name)
        ?? throw new Exception("Missing " + name);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    /// <summary>直接改 Devices 的夹具要让 BatteryService 重新算一次可见列表。</summary>
    private static void RefreshVisible(BatteryService battery) =>
        typeof(BatteryService).GetMethod("RefreshVisible", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(battery, null);
    private static void Set(object obj, string property, object value)
    {
        var prop = obj.GetType().GetProperty(property);
        Require(prop != null, "Missing " + property);
        prop!.SetValue(obj, value);
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }

    public static void Badge()
    {
        var device = new DeviceInfo { Kind = DeviceKind.Keyboard, Name = "R98Pro V2", Level = 85 };
        var badge = (FrameworkElement)Activator.CreateInstance(TypeOf("Controls.DeviceBatteryBadge"),
            device, Brushes.White, true, false, 20, false)!;
        badge.Measure(new Size(300, 300));
        badge.Arrange(new Rect(badge.DesiredSize));
        badge.UpdateLayout();
        var number = Children<TextBlock>(badge).Single(t => t.Name == "BatteryNumber");
        var ring = Children<DeviceBatteryRing>(badge).Single();
        Require(number.Text == "85", "Number must omit percent sign.");
        foreach (int level in new[] { 0, 100, -1, 42 })
        {
            device.Level = level;
            badge.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Require(number.Text == (level < 0 ? "\u2014" : level.ToString()), "Number stopped updating.");
            Require(ring.Level == level, "Ring stopped updating.");
        }
        device.Charging = true;
        badge.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        Require(ring.Charging, "Charging state stopped updating.");
        device.IsStale = true;
        badge.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        Require(badge.ToolTip?.ToString()?.Contains("上次读数") == true &&
            System.Windows.Automation.AutomationProperties.GetName(badge).Contains("上次读数"),
            "Cached readings need a live stale description.");
        device.IsStale = false;
        badge.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        Require(badge.ToolTip?.ToString()?.Contains("上次读数") == false,
            "Recovered readings must clear the stale description.");
        Require(number.TransformToAncestor(badge).Transform(new Point()).X >= 40,
            "Taskbar number overlaps the ring.");
    }

    public static void Charging()
    {
        var ring = new DeviceBatteryRing { Level = -1, Charging = true };
        ring.Measure(new Size(40, 40));
        ring.Arrange(new Rect(0, 0, 40, 40));
        ring.UpdateLayout();
        var bolt = Children<TextBlock>(ring).FirstOrDefault(t => t.Name == "ChargingMark");
        Require(bolt != null && bolt.Visibility == Visibility.Visible,
            "Charging needs a visible bolt even when level is unknown.");
        ring.Charging = false;
        Require(bolt!.Visibility == Visibility.Collapsed, "Bolt must disappear when not charging.");
    }

    public static void SettingsRoundTrip()
    {
        var settings = new AppSettings();
        Set(settings, "ShowBatteryNumber", false);
        Set(settings, "EdgeGlyphColor", "#39A9F0");
        Set(settings, "EdgeVerticalPosition", 0.8);
        Set(settings, "EdgeInset", 24d);
        settings.SurfaceMode = "edge";
        var options = new JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings.Clone(), options), options)!;
        Require((bool)restored.GetType().GetProperty("ShowBatteryNumber")!.GetValue(restored)! == false,
            "Number preference lost during clone/save.");
        Require((string)restored.GetType().GetProperty("EdgeGlyphColor")!.GetValue(restored)! == "#39A9F0",
            "Color preference lost during clone/save.");
        Require((double)restored.GetType().GetProperty("EdgeVerticalPosition")!.GetValue(restored)! == 0.8 &&
            (double)restored.GetType().GetProperty("EdgeInset")!.GetValue(restored)! == 24,
            "Placement preferences lost during clone/save.");
    }

    public static void Placement()
    {
        Rect Place(Rect work, Size content, double position, double inset, double dpi) =>
            (Rect)TypeOf("Services.ScreenEdgePlacement").GetMethod("Calculate")!
                .Invoke(null, [work, content, position, inset, dpi])!;
        var result = Place(new Rect(-1920, 0, 1920, 1040), new Size(48, 128), 0.5, 12, 1.5);
        Require(result == new Rect(-90, 424, 72, 192), "DPI or negative-monitor placement is wrong.");
        var work = new Rect(0, 0, 320, 200);
        Require(work.Contains(Place(work, new Size(48, 320), 99, 999, 2)),
            "Five devices must fit even on a short screen.");
        Require(work.Contains(Place(work, new Size(48, 128), double.NaN, double.NaN, 1)),
            "Invalid saved settings must not move the window off screen.");
    }

    public static void Exclusive()
    {
        var windows = Enumerable.Range(0, 5).Select(_ => new Window
        {
            Width = 10, Height = 10, ShowActivated = false, ShowInTaskbar = false, Left = -3000
        }).ToArray();
        try
        {
            var constructor = typeof(SurfaceVisibility).GetConstructor(
                [typeof(Window), typeof(Window), typeof(Window), typeof(Window), typeof(Window)]);
            Require(constructor != null, "Edge mode is missing from exclusive visibility.");
            var controller = (SurfaceVisibility)constructor!.Invoke(windows.Cast<object>().ToArray());
            foreach (var mode in new[] { "edge", "taskbar", "edge", "mini", "edge" })
            {
                controller.Apply(mode, true);
                Require(windows.Count(w => w.IsVisible) == 1 && controller.GetWindow(mode)!.IsVisible,
                    "Switching edge mode leaves another surface visible.");
            }
        }
        finally { foreach (var window in windows) window.Close(); }
    }

    public static void Visuals()
    {
        var badgeType = TypeOf("Controls.DeviceBatteryBadge");
        foreach (bool vertical in new[] { false, true })
        foreach (bool show in new[] { false, true })
        foreach (double dpi in new[] { 96d, 144d, 192d })
        {
            var panel = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal };
            foreach (int level in new[] { 100, 85, 0, -1, 9 })
            {
                var device = new DeviceInfo { Kind = level == 100 ? DeviceKind.Keyboard : DeviceKind.Mouse,
                    Level = level, Charging = level == 85 || level == -1 };
                panel.Children.Add((UIElement)Activator.CreateInstance(badgeType,
                    device, Brushes.DeepSkyBlue, show, vertical, 20, false)!);
            }
            panel.Measure(new Size(1000, 1000));
            panel.Arrange(new Rect(panel.DesiredSize));
            panel.UpdateLayout();
            foreach (var number in Children<TextBlock>(panel).Where(t => t.Name == "BatteryNumber"))
            {
                Require(number.Visibility == (show ? Visibility.Visible : Visibility.Collapsed), "Number switch ignored.");
                if (show) Require(number.ActualWidth >= number.DesiredSize.Width, "Number text clipped.");
                Require(((SolidColorBrush)number.Foreground).Color == Colors.DeepSkyBlue, "Color not applied to number.");
            }
            var image = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth * dpi / 96),
                (int)Math.Ceiling(panel.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            image.Render(panel);
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
            image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            Require(pixels.Any(b => b != 0) && pixels[3] == 0, "Bad rendering or opaque background.");
            System.IO.Directory.CreateDirectory("artifacts/edge-display");
            using var file = System.IO.File.Create($"artifacts/edge-display/{vertical}-{show}-{dpi}.png");
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(image));
            png.Save(file);
        }
    }

    public static void WindowIntegration()
    {
        var settings = new AppSettings { Provider = "json", JsonPath = "missing-edge-fixture.json",
            SurfaceMode = "edge", EdgeGlyphColor = "#39A9F0", AlwaysOnTop = true };
        using var battery = new BatteryService(settings);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        battery.Devices.Add(new DeviceInfo { Kind = DeviceKind.Keyboard, Name = "R98Pro V2", Level = 100, Charging = true });
        battery.Devices.Add(new DeviceInfo { Name = "Mouse", Level = 18 });
        battery.Devices.Add(new DeviceInfo { Name = "Hidden", Selected = false });
        RefreshVisible(battery);
        var edge = new ScreenEdgeWindow();
        var taskbar = new TaskbarWindow();
        typeof(MouseDock.App).GetProperty("Edge")!.SetValue(null, edge);
        typeof(MouseDock.App).GetProperty("Taskbar")!.SetValue(null, taskbar);
        try
        {
            foreach (string mode in new[] { "edge", "taskbar", "edge" })
            {
                settings.SurfaceMode = mode;
                MouseDock.App.ShowCurrentSurface();
                Require(edge.IsVisible == (mode == "edge") && taskbar.IsVisible == (mode == "taskbar"),
                    "App wake does not honor edge mode.");
            }
            edge.UpdateLayout();
            var badges = Children<DeviceBatteryBadge>(edge).ToArray();
            Require(badges.Length == 2, "Edge ignores selected devices.");
            Require(badges.All(b => ((SolidColorBrush)b.Ring.GlyphBrush).Color == Color.FromRgb(57, 169, 240)),
                "Custom color was not applied.");
            Require(edge.ActualWidth > 0 && edge.ActualHeight > 0, "Edge native window has no size.");
            settings.ShowBatteryNumber = false;
            edge.UpdateDevices();
            Require(Children<TextBlock>(edge).Where(t => t.Name == "BatteryNumber").All(t => t.Visibility == Visibility.Collapsed),
                "Live number setting ignored.");
            settings.EdgeGlyphColor = "invalid saved color";
            edge.UpdateDevices();
            Require(Children<DeviceBatteryRing>(edge).All(r => ((SolidColorBrush)r.GlyphBrush).Color == Colors.WhiteSmoke),
                "Invalid color must fall back without a crash.");
            foreach (var device in battery.Devices) device.Selected = false;
            RefreshVisible(battery);
            edge.UpdateDevices();
            Require(edge.Opacity == 0 && !edge.IsHitTestVisible, "Empty edge must be hidden and not intercept clicks.");
        }
        finally
        {
            edge.Close();
            taskbar.Close();
            typeof(MouseDock.App).GetProperty("Edge")!.SetValue(null, null);
            typeof(MouseDock.App).GetProperty("Taskbar")!.SetValue(null, null);
        }
    }
}
