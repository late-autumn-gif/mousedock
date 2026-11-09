using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

internal static class VisualAuditChecks
{
    internal static void BackgroundRecovery(string surface)
    {
        using var fixture = new Fixture();
        string valid = FindAsset("src/MouseDock/Assets/app.png");
        string corrupt = Path.GetTempFileName();
        File.WriteAllText(corrupt, "not an image");
        Window? window = null;
        try
        {
            fixture.Settings.DockBackground = corrupt;
            fixture.Settings.MiniBackground = corrupt;
            window = surface == "dock" ? new DockWindow() : new MiniWindow();
            var apply = window.GetType().GetMethod("ApplyBackground")!;
            var root = (Border)window.FindName("Root");
            bool HasImage() => root.Background is ImageBrush { ImageSource: BitmapSource };
            apply.Invoke(window, [corrupt]);
            Require(root.Background is LinearGradientBrush, "A corrupt saved background must fall back to the theme.");
            apply.Invoke(window, [valid]);
            Require(HasImage(), "A valid image must still load.");
            apply.Invoke(window, [corrupt]);
            Require(root.Background is LinearGradientBrush, "A corrupt replacement must clear the old image.");
            window.GetType().GetMethod("ApplyTheme")!.Invoke(window, [ThemePreset.Presets[1], false]);
            Require(((LinearGradientBrush)root.Background).GradientStops[0].Color == ThemePreset.Presets[1].BgTop,
                "The fallback background must still follow theme changes.");
            apply.Invoke(window, [valid]);
            Require(HasImage(), "The window must recover when a valid background is chosen.");
            apply.Invoke(window, [""]);
            Require(root.Background is LinearGradientBrush, "Clearing a recovered image must restore the theme.");
        }
        finally
        {
            window?.Close();
            File.Delete(corrupt);
        }
    }

    internal static void PhotoRecovery()
    {
        using var fixture = new Fixture();
        string valid = FindAsset("src/MouseDock/Assets/app.png");
        string corrupt = Path.GetTempFileName();
        File.WriteAllText(corrupt, "not an image");
        SetApp("DefaultPhotoAsset", null);
        var icon = new IconWindow();
        try
        {
            var brush = (ImageBrush)icon.FindName("PhotoBrush");
            fixture.Settings.IconBackground = corrupt;
            icon.LoadPhoto();
            Require(brush.ImageSource == null, "A corrupt saved photo must leave an empty photo surface.");
            fixture.Settings.IconBackground = valid;
            icon.LoadPhoto();
            Require(brush.ImageSource is BitmapSource, "A valid photo must load after a corrupt saved photo.");
            fixture.Settings.IconBackground = corrupt;
            icon.LoadPhoto();
            Require(brush.ImageSource == null, "A corrupt replacement must not retain the previous photo.");
            fixture.Settings.IconBackground = valid;
            icon.LoadPhoto();
            Require(brush.ImageSource is BitmapSource, "Photo loading must recover after a failed replacement.");
            fixture.Settings.IconBackground = "";
            icon.LoadPhoto();
            Require(brush.ImageSource == null, "Clearing the photo must remove the image.");
        }
        finally
        {
            icon.Close();
            File.Delete(corrupt);
        }
    }

    internal static void IconOpacity()
    {
        using var fixture = new Fixture();
        fixture.Settings.IconMode = "tile";
        fixture.Settings.Opacity = 0.93;
        var icon = new IconWindow { Left = -2000, Top = -2000, ShowActivated = false };
        SetApp("Icon", icon);
        try
        {
            MouseDock.App.ApplyOpacity();
            icon.Show();
            Pump(TimeSpan.FromMilliseconds(350));
            Require(Math.Abs(icon.Opacity - 0.93) < 0.001,
                $"The loaded icon ignored the configured opacity: {icon.Opacity}.");
            fixture.Settings.Opacity = 0.9;
            MouseDock.App.ApplyOpacity();
            Pump(TimeSpan.FromMilliseconds(50));
            Require(Math.Abs(icon.Opacity - 0.9) < 0.001, "A live opacity change was masked by the entrance animation.");
            fixture.Settings.Opacity = 0.4;
            MouseDock.App.ApplyOpacity();
            Require(Math.Abs(icon.Opacity - 0.85) < 0.001, "The icon's existing minimum opacity must be preserved.");
        }
        finally { icon.Close(); }
    }

    internal static void DockLiveWarning()
    {
        using var fixture = new Fixture();
        var device = new DeviceInfo { Name = "Visual audit", Level = 50 };
        fixture.Battery.Devices.Add(device);
        RefreshVisible(fixture.Battery);
        var dock = OpenDock();
        try
        {
            dock.ApplyTheme(ThemePreset.Presets[1], false);
            Require(PercentColors(dock).Single() == ThemePreset.Presets[1].Text, "Normal values must use the selected theme.");
            device.Level = 20;
            dock.UpdateLayout();
            Require(PercentColors(dock).Single() == Color.FromRgb(255, 138, 138),
                "The percentage must turn red immediately when a live reading reaches the warning threshold.");
            device.Level = 21;
            dock.UpdateLayout();
            Require(PercentColors(dock).Single() == ThemePreset.Presets[1].Text, "A recovered reading must restore the theme text color.");
            device.Level = -1;
            dock.UpdateLayout();
            Require(PercentColors(dock).Single() == ThemePreset.Presets[1].Text, "Unknown readings must not appear as low battery.");
            fixture.Battery.Devices.Add(new DeviceInfo { Name = "New device", Level = 10 });
            RefreshVisible(fixture.Battery);
            dock.RefreshDevices();
            dock.UpdateLayout();
            Require(PercentColors(dock).Last() == Color.FromRgb(255, 138, 138), "Newly discovered low battery rows must receive warning color.");
        }
        finally { dock.Close(); }
    }

    internal static void DockWarningThreshold()
    {
        using var fixture = new Fixture();
        fixture.Settings.LowBatteryWarn = 30;
        fixture.Battery.Devices.Add(new DeviceInfo { Name = "Threshold audit", Level = 25 });
        RefreshVisible(fixture.Battery);
        var dock = OpenDock();
        try
        {
            dock.ApplyTheme(ThemePreset.Presets[0], false);
            Require(PercentColors(dock).Single() == Color.FromRgb(255, 138, 138), "The dock ignored the configured 30% threshold.");
            fixture.Settings.LowBatteryWarn = 10;
            dock.ApplyTheme(ThemePreset.Presets[0], false);
            Require(PercentColors(dock).Single() == ThemePreset.Presets[0].Text, "Changing the threshold must restore normal text color.");
            fixture.Settings.LowBatteryWarn = 25;
            dock.ApplyTheme(ThemePreset.Presets[1], false);
            Require(PercentColors(dock).Single() == Color.FromRgb(255, 138, 138), "The warning boundary must be inclusive and survive theme changes.");
        }
        finally { dock.Close(); }
    }

    private static DockWindow OpenDock()
    {
        var dock = new DockWindow { Left = -2000, Top = -2000, ShowActivated = false };
        dock.Show();
        dock.UpdateLayout();
        return dock;
    }

    private static IEnumerable<Color> PercentColors(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock { Name: "Pct", Foreground: SolidColorBrush brush }) yield return brush.Color;
            foreach (var color in PercentColors(child)) yield return color;
        }
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void SetApp(string property, object? value) =>
        typeof(MouseDock.App).GetProperty(property)!.SetValue(null, value);


    private static void RefreshVisible(BatteryService battery) =>
        typeof(BatteryService).GetMethod("RefreshVisible", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(battery, null);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Dictionary<string, object?> _original = new();
        internal AppSettings Settings { get; } = new() { Provider = "json", JsonPath = "missing-visual-audit.json" };
        internal BatteryService Battery { get; }

        internal Fixture()
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            foreach (string property in new[] { "Settings", "Battery", "Dock", "Mini", "Icon", "DefaultPhotoAsset" })
                _original[property] = typeof(MouseDock.App).GetProperty(property)!.GetValue(null);
            Battery = new BatteryService(Settings);
            var frame = new DispatcherFrame();
            EventHandler refreshed = (_, _) => frame.Continue = false;
            Battery.Refreshed += refreshed;
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timeout.Tick += (_, _) => frame.Continue = false;
            timeout.Start();
            Dispatcher.PushFrame(frame);
            timeout.Stop();
            Battery.Refreshed -= refreshed;
            SetApp("Battery", Battery);
            SetApp("Settings", Settings);
            SetApp("Dock", null);
            SetApp("Mini", null);
            SetApp("Icon", null);
        }

        public void Dispose()
        {
            Battery.Dispose();
            foreach (var (property, value) in _original) SetApp(property, value);
        }
    }

    /// <summary>Tests can run from the repo root or from bin; walk up until the asset is found.</summary>
    private static string FindAsset(string relative)
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

