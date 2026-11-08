using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;

internal static class TaskbarStyleChecks
{
    private static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    internal static void Placement()
    {
        foreach (double dpi in new[] { 1d, 1.25, 1.5, 2, 3 })
        foreach (uint edge in new uint[] { 0, 1, 2, 3 })
        foreach (int count in new[] { 1, 2, 4, 6 })
        foreach (double offset in new[] { 0d, 50, 100, double.NaN })
        {
            bool vertical = edge is 0 or 2;
            var bar = vertical ? new Rect(-1920, -400, 32 * dpi, 500 * dpi)
                : new Rect(-1920, -400, 500 * dpi, 32 * dpi);
            var result = TaskbarPlacement.CalculateBadges(bar, edge, count, true, offset, dpi);
            Require(bar.Contains(result), "Taskbar digits must stay inside a 32-DIP taskbar at every edge and DPI.");
            Require(result.Width > 0 && result.Height > 0, "Taskbar content must remain visible.");
            foreach (string style in new[] { "ring", "panel" })
            {
                var strip = Strip(Enumerable.Range(0, count).Select(_ => new DeviceInfo { Level = 100 }).ToArray(),
                    style, true, vertical, Brushes.White);
                var content = vertical ? new Size(strip.Height, strip.Width) : new Size(strip.Width, strip.Height);
                var panelBounds = TaskbarPlacement.Calculate(bar, edge, content, offset, dpi);
                var tolerance = bar;
                tolerance.Inflate(.001, .001);
                Require(tolerance.Contains(panelBounds),
                    $"The complete panel including border and dividers must fit: {style}, {dpi}, {edge}, {count}, {offset}, {panelBounds}.");
            }
        }
    }

    internal static void Preferences()
    {
        var property = typeof(AppSettings).GetProperty("TaskbarStyle");
        Require(property != null, "Taskbar style must be a persistent user preference.");
        var options = new JsonSerializerOptions
        { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var legacy = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Require((string?)property!.GetValue(legacy) == "ring", "Existing users must default to the ring layout.");
        foreach (string style in new[] { "ring", "panel" })
        {
            property.SetValue(legacy, style);
            var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(legacy.Clone(), options), options)!;
            Require((string?)property.GetValue(restored) == style, "Style choice must survive clone and restart.");
        }
    }

    internal static void CustomColors()
    {
        var textProperty = typeof(AppSettings).GetProperty("TaskbarTextColor");
        var batteryProperty = typeof(AppSettings).GetProperty("TaskbarBatteryColor");
        Require(textProperty != null && batteryProperty != null, "Taskbar text and battery colors must be independent saved preferences.");
        var settings = new AppSettings();
        textProperty!.SetValue(settings, "#FFCC00");
        batteryProperty!.SetValue(settings, "#00AAFF");
        var options = new JsonSerializerOptions
        { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings.Clone(), options), options)!;
        Require((string?)textProperty.GetValue(restored) == "#FFCC00" &&
            (string?)batteryProperty.GetValue(restored) == "#00AAFF", "Color choices must survive cloning and restart.");
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Controls.TaskbarBatteryStrip")!;
        foreach (string style in new[] { "ring", "panel" })
        {
            var devices = new[] { new DeviceInfo { Level = 80, Kind = DeviceKind.Keyboard } };
            var strip = (FrameworkElement)Activator.CreateInstance(type, devices, style, true, false,
                Brushes.WhiteSmoke, 20, "#FFCC00", "#00AAFF")!;
            Arrange(strip);
            var number = Children<TextBlock>(strip).Single(t => t.Name == "BatteryNumber");
            Require(((SolidColorBrush)number.Foreground).Color == Color.FromRgb(255, 204, 0), "Taskbar text must use its own chosen color.");
            var glyph = Children<MouseDock.Controls.DeviceGlyph>(strip).Single(g => g.Visibility == Visibility.Visible);
            Require(((SolidColorBrush)glyph.GlyphBrush).Color == Color.FromRgb(0, 170, 255), "Battery glyph must use its independent color.");
            var bitmap = new RenderTargetBitmap((int)strip.Width * 3, (int)strip.Height * 3, 288, 288, PixelFormats.Pbgra32);
            bitmap.Render(strip);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            Require(Enumerable.Range(0, pixels.Length / 4).Count(i =>
                pixels[i * 4] > 200 && pixels[i * 4 + 1] > 100 && pixels[i * 4 + 2] < 20) > (style == "ring" ? 250 : 20),
                "The chosen battery color must be visible in the rendered ring or icon.");
            devices[0].Level = 29; Arrange(strip);
            Require(number.Text == "29", "Custom colors must retain live battery updates.");
            var fallback = (FrameworkElement)Activator.CreateInstance(type, devices, style, true, false,
                Brushes.WhiteSmoke, 20, "invalid", "")!;
            Arrange(fallback);
            Require(((SolidColorBrush)Children<TextBlock>(fallback).Single(t => t.Name == "BatteryNumber").Foreground).Color == Colors.WhiteSmoke,
                "Invalid or reset color settings must fall back without breaking the panel.");
        }
    }

    private static FrameworkElement Strip(DeviceInfo[] devices, string style, bool numbers, bool vertical, Brush ink)
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Controls.TaskbarBatteryStrip");
        Require(type != null, "Both taskbar styles need the shared production renderer.");
        return (FrameworkElement)Activator.CreateInstance(type!, devices, style, numbers, vertical, ink, 20)!;
    }

    internal static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }

    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
    }

    internal static void Rendering()
    {
        string directory = Path.GetFullPath("artifacts/taskbar-styles");
        Directory.CreateDirectory(directory);
        foreach (string style in new[] { "ring", "panel", "invalid" })
        foreach (bool vertical in new[] { false, true })
        foreach (bool numbers in new[] { false, true })
        foreach (bool light in new[] { false, true })
        {
            var devices = new[] {
                new DeviceInfo { Name = "Keyboard", Kind = DeviceKind.Keyboard, Level = 100 },
                new DeviceInfo { Name = "Mouse", Kind = DeviceKind.Mouse, Level = 0, Charging = true },
                new DeviceInfo { Name = "Unknown", Level = -1 },
                new DeviceInfo { Name = "Cached", Level = 28, IsStale = true }
            };
            var strip = Strip(devices, style, numbers, vertical, light ? Brushes.Black : Brushes.WhiteSmoke);
            Arrange(strip);
            Require((vertical ? strip.ActualWidth : strip.ActualHeight) <= 40, "Neither style may require a second row outside the taskbar.");
            var values = Children<TextBlock>(strip).Where(t => t.Name == "BatteryNumber").ToArray();
            Require(values.Length == 4, "One battery value must stay associated with each device.");
            Require(values.Select(t => t.Text).SequenceEqual(new[] { "100", "0", "\u2014", "28" }), "Unknown and zero must remain distinct.");
            foreach (var number in values)
            {
                Require(number.Visibility == (numbers ? Visibility.Visible : Visibility.Collapsed), "Number visibility setting ignored.");
                if (!numbers) continue;
                var bounds = number.TransformToAncestor(strip).TransformBounds(new Rect(number.RenderSize));
                Require(new Rect(strip.RenderSize).Contains(bounds), "Digits extend beyond the taskbar strip.");
                var text = new FormattedText(number.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(number.FontFamily, number.FontStyle, number.FontWeight, number.FontStretch),
                    number.FontSize, number.Foreground, 1);
                Require(text.WidthIncludingTrailingWhitespace <= number.ActualWidth + .1, "Three-digit battery text is clipped.");
            }
            devices[0].Level = 73;
            devices[0].IsStale = true;
            devices[1].IsStale = true;
            Arrange(strip);
            Require(values[0].Text == "73", "Live battery changes require no renderer rebuild.");
            var cells = Children<Grid>(strip).Where(g => g.Name == "TaskbarDevice").ToArray();
            Require(cells[0].Opacity < 1 && AutomationProperties.GetName(cells[0]).Contains("上次读数"),
                "Stale readings must stay dimmed and accessible.");
            Require(Children<TextBlock>(strip).Where(t => t.Name == "ChargingMark").All(t => t.Visibility == Visibility.Collapsed),
                "Cached charging must not keep a live charging mark.");
            devices[0].IsStale = false;
            devices[1].IsStale = false;
            Arrange(strip);
            foreach (double dpi in new[] { 1d, 1.25, 1.5, 2, 3 })
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(strip.ActualWidth * dpi),
                    (int)Math.Ceiling(strip.ActualHeight * dpi), dpi * 96, dpi * 96, PixelFormats.Pbgra32);
                bitmap.Render(strip);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                Require(pixels.Where((_, i) => i % 4 == 3).Count(a => a > 0) > 100, "Taskbar preview must contain visible pixels.");
                if (style == "invalid") continue;
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(directory, $"{style}-{vertical}-{numbers}-{light}-{dpi}.png"));
                encoder.Save(output);
            }
        }
    }
}
