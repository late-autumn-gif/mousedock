using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;
using App = MouseDock.App;

internal static class SharedDisplayStyleChecks
{
    private static readonly string[] Styles = ["ring", "tile", "pill", "battery", "plain", "multi"];

    internal static void Preferences()
    {
        var edgeStyle = typeof(AppSettings).GetProperty("EdgeStyle");
        Require(edgeStyle != null, "Right-edge styles need an independently saved selection.");
        var settings = new AppSettings { IconStyle = "tile", TaskbarStyle = "battery" };
        edgeStyle!.SetValue(settings, "pill");
        var options = new JsonSerializerOptions
        { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings.Clone(), options), options)!;
        Require(restored.IconStyle == "tile" && restored.TaskbarStyle == "battery" &&
            (string?)edgeStyle.GetValue(restored) == "pill", "Cloning or restarting coupled the three style selections.");
        Require(TaskbarBatteryStrip.NormalizeStyle("card") == "card",
            "The upstream mini-card preference must remain supported.");
        Require(JsonSerializer.Deserialize<AppSettings>("{}")!.EdgeStyle == "ring",
            "An existing configuration must retain the original right-edge ring.");
    }

    internal static void Rendering()
    {
        var gallery = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(38, 41, 45)), Margin = new Thickness(12) };
        foreach (string style in Styles)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
            row.Children.Add(new TextBlock { Text = style, Width = 75, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            foreach (bool vertical in new[] { false, true })
            {
                var devices = new[] {
                    new DeviceInfo { Name = "Keyboard", Kind = DeviceKind.Keyboard, Level = 100 },
                    new DeviceInfo { Name = "Mouse", Kind = DeviceKind.Mouse, Level = 0, Charging = true },
                    new DeviceInfo { Name = "Unknown", Level = -1 },
                    new DeviceInfo { Name = "Cached", Level = 28, IsStale = true }
                };
                var strip = new TaskbarBatteryStrip(devices, style, true, vertical, Brushes.WhiteSmoke,
                    20, "#FFCC00", "#00AAFF");
                Require(strip.StyleName == style, $"{style} still falls back to the ring style.");
                Arrange(strip);
                Require((vertical ? strip.ActualWidth : strip.ActualHeight) <= 40, "A style became too wide for the edge or too tall for the taskbar.");
                var numbers = Children<TextBlock>(strip).Where(t => t.Name == "BatteryNumber").ToArray();
                Require(numbers.Select(t => t.Text).SequenceEqual(new[] { "100", "0", "\u2014", "28" }),
                    $"{style} lost zero, unknown, or device ordering.");
                foreach (var number in numbers)
                {
                    Require(new Rect(strip.RenderSize).Contains(number.TransformToAncestor(strip).TransformBounds(new Rect(number.RenderSize))),
                        $"{style} clips its upright battery number.");
                    Require(((SolidColorBrush)number.Foreground).Color == Color.FromRgb(255, 204, 0), "Style ignored the text color.");
                }
                devices[0].Level = 73;
                devices[1].IsStale = true;
                Arrange(strip);
                Require(numbers[0].Text == "73", "Battery values stopped updating in the selected style.");
                Require(Children<TextBlock>(strip).Where(t => t.Name == "ChargingMark").All(t => t.Visibility == Visibility.Collapsed),
                    "A stale device retained a live charging mark.");
                foreach (double dpi in new[] { 1d, 1.5, 2, 3 })
                {
                    var bounds = vertical ? new Rect(0, 0, 32 * dpi, 600 * dpi) : new Rect(0, 0, 600 * dpi, 32 * dpi);
                    var content = vertical ? new Size(strip.Height, strip.Width) : new Size(strip.Width, strip.Height);
                    var placed = TaskbarPlacement.Calculate(bounds, vertical ? 2u : 3u, content, 50, dpi);
                    var tolerance = bounds;
                    tolerance.Inflate(.001, .001);
                    Require(tolerance.Contains(placed), $"{style} escaped a narrow taskbar at {dpi} DPI.");
                    var image = Snapshot(strip, dpi);
                    var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
                    image.CopyPixels(pixels, image.PixelWidth * 4, 0);
                    Require(pixels.Where((_, i) => i % 4 == 3).Count(a => a > 0) > 100, $"{style} rendered blank.");
                }
                row.Children.Add(new Border { Child = strip, Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center });
                var hidden = new TaskbarBatteryStrip(devices, style, false, vertical, Brushes.White, 20);
                Arrange(hidden);
                Require(Children<TextBlock>(hidden).Where(t => t.Name == "BatteryNumber").All(t => t.Visibility == Visibility.Collapsed),
                    $"{style} ignored the number visibility preference.");
            }
            gallery.Children.Add(row);
        }
        Arrange(gallery);
        Save(gallery, "artifacts/shared-display-styles/gallery.png");
    }

    internal static void Settings()
    {
        using var fixture = new Fixture();
        var window = new SettingsWindow();
        try
        {
            window.Reload();
            var taskbarChoices = (Panel)window.FindName("TaskbarStyleChoices");
            var edgeChoices = window.FindName("EdgeStyleChoices") as Panel;
            Require(edgeChoices != null, "Right-edge settings have no style selector.");
            foreach (string style in Styles.Concat(new[] { "panel", "card" }))
            {
                var taskbar = taskbarChoices.Children.OfType<RadioButton>().SingleOrDefault(b => (string)b.Tag == style);
                var edge = edgeChoices!.Children.OfType<RadioButton>().SingleOrDefault(b => (string)b.Tag == style);
                Require(taskbar != null && edge != null, $"Settings omit {style} from a display surface.");
                fixture.Settings.SurfaceMode = "taskbar";
                taskbar!.IsChecked = true;
                Require(fixture.Settings.TaskbarStyle == style && fixture.Settings.SurfaceMode == "taskbar" &&
                    fixture.Settings.IconStyle == "tile", "Taskbar style selection switched or changed the floating surface.");
                // Reload replaces choice controls, as in the running settings window.
                fixture.Settings.SurfaceMode = "edge";
                edge = ((Panel)window.FindName("EdgeStyleChoices")).Children.OfType<RadioButton>().Single(b => (string)b.Tag == style);
                edge.IsChecked = true;
                Require((string?)typeof(AppSettings).GetProperty("EdgeStyle")!.GetValue(fixture.Settings) == style &&
                    fixture.Settings.SurfaceMode == "edge" && fixture.Settings.IconStyle == "tile",
                    "Edge style selection changed another surface.");
                taskbarChoices = (Panel)window.FindName("TaskbarStyleChoices");
                edgeChoices = (Panel)window.FindName("EdgeStyleChoices");
            }
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Left = -3000;
            window.Show();
            foreach (string mode in new[] { "taskbar", "edge" })
            foreach (var theme in new[] { ThemePreset.Presets[0], ThemePreset.Presets[1] })
            {
                fixture.Settings.SurfaceMode = mode;
                window.Reload();
                window.ApplyTheme(theme, false);
                var root = (FrameworkElement)window.Content;
                root.BeginAnimation(UIElement.OpacityProperty, null);
                root.Opacity = 1;
                var choices = (Panel)window.FindName(mode == "edge" ? "EdgeStyleChoices" : "TaskbarStyleChoices");
                choices.BringIntoView();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                foreach (var label in Children<TextBlock>(choices).Where(t => t.Visibility == Visibility.Visible))
                    Require(label.ActualWidth + label.Margin.Left + label.Margin.Right >= label.DesiredSize.Width,
                        $"{mode} clips its style label or battery preview: {label.Text}.");
                Save(root, $"artifacts/shared-display-styles/settings-{mode}-{theme.Id}.png");
            }
        }
        finally { window.Close(); }
    }

    internal static void Edge()
    {
        using var fixture = new Fixture();
        var property = typeof(AppSettings).GetProperty("EdgeStyle");
        Require(property != null, "Edge window cannot select a saved style.");
        var edge = new ScreenEdgeWindow();
        try
        {
            foreach (string style in Styles.Where(s => s != "ring").Concat(new[] { "panel", "card" }))
            {
                property!.SetValue(fixture.Settings, style);
                edge.UpdateDevices();
                var content = (FrameworkElement)edge.Content;
                Arrange(content);
                var strip = Children<TaskbarBatteryStrip>(content).SingleOrDefault();
                Require(strip?.StyleName == style, $"The edge window ignored {style}.");
                Require(strip!.Height > strip.Width, "Multiple edge devices must form a vertical column.");
                var number = Children<TextBlock>(strip).First(t => t.Name == "BatteryNumber");
                fixture.Battery.VisibleDevices[0].Level = 41;
                Arrange(strip);
                Require(number.Text == "41", "Right-edge readings require a window rebuild.");
                var original = strip;
                edge.UpdateDevices();
                Require(ReferenceEquals(original, Children<TaskbarBatteryStrip>(content).Single()),
                    "An unchanged edge scan recreated the selected style.");
                fixture.Settings.ShowBatteryNumber = false;
                edge.UpdateDevices();
                Arrange(content);
                Require(Children<TextBlock>(content).Where(t => t.Name == "BatteryNumber").All(t => t.Visibility == Visibility.Collapsed),
                    "The edge style ignored the number toggle.");
                fixture.Settings.ShowBatteryNumber = true;
            }
        }
        finally { edge.Close(); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string[] names = ["Settings", "Battery", "Taskbar", "Edge", "SettingsView", "Icon"];
        private readonly object?[] previous;
        private readonly PropertyInfo directory = typeof(SettingsStore).GetProperty("TestDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? oldDirectory;
        internal readonly AppSettings Settings = new() { Provider = "json", JsonPath = "missing-shared-style.json", IconStyle = "tile" };
        internal readonly BatteryService Battery;
        internal Fixture()
        {
            previous = names.Select(name => typeof(App).GetProperty(name)!.GetValue(null)).ToArray();
            oldDirectory = directory.GetValue(null);
            directory.SetValue(null, Path.GetFullPath("artifacts/shared-display-styles/settings"));
            Battery = (BatteryService)Activator.CreateInstance(typeof(BatteryService), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [Settings, null, false], null)!;
            for (int i = 0; i < names.Length; i++) typeof(App).GetProperty(names[i])!.SetValue(null, null);
            typeof(App).GetProperty("Settings")!.SetValue(null, Settings);
            typeof(App).GetProperty("Battery")!.SetValue(null, Battery);
            Battery.VisibleDevices.Add(new DeviceInfo { Id = "style-keyboard", Name = "Keyboard", Kind = DeviceKind.Keyboard, Level = 100 });
            Battery.VisibleDevices.Add(new DeviceInfo { Id = "style-mouse", Name = "Mouse", Kind = DeviceKind.Mouse, Level = 28 });
        }
        public void Dispose()
        {
            Battery.Dispose();
            for (int i = 0; i < names.Length; i++) typeof(App).GetProperty(names[i])!.SetValue(null, previous[i]);
            directory.SetValue(null, oldDirectory);
        }
    }

    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
    }
    private static RenderTargetBitmap Snapshot(FrameworkElement element, double dpi)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi), (int)Math.Ceiling(element.ActualHeight * dpi),
            dpi * 96, dpi * 96, PixelFormats.Pbgra32);
        image.Render(element);
        return image;
    }
    private static void Save(FrameworkElement element, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Snapshot(element, 2)));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
