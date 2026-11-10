using System.IO;
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

internal static class ReferenceStyleChecks
{
    internal static void Choices()
    {
        Require(TaskbarBatteryStrip.NormalizeStyle("digits") == "plain" &&
            TaskbarBatteryStrip.NormalizeStyle("compact") == "multi", "Reference style aliases are missing.");
        foreach (string style in new[] { "character", "portrait" })
            Require(TaskbarBatteryStrip.Styles(false).Any(s => s.Id == style) &&
                TaskbarBatteryStrip.Styles(true).Any(s => s.Id == style), $"Taskbar or edge omits {style}.");
    }

    internal static void PhotoDraft()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Views.PortraitEditorWindow");
        Require(type != null, "Photo crop editor is missing.");
        var settings = new AppSettings { PhotoZoom = 1.5, PhotoOffsetX = .1, PhotoOffsetY = -.2 };
        var editor = (Window)Activator.CreateInstance(type!, [settings])!;
        var draft = (AppSettings)type!.GetProperty("Draft")!.GetValue(editor)!;
        draft.PhotoZoom = 3;
        draft.PhotoOffsetX = -.5;
        editor.Close();
        Require(settings.PhotoZoom == 1.5 && settings.PhotoOffsetX == .1 && settings.PhotoOffsetY == -.2,
            "Cancelling photo editing changed the saved crop.");
        Require(settings.Clone().ReferenceCharacter == settings.ReferenceCharacter, "Cloning lost the selected character.");
    }

    internal static void DefaultContrast()
    {
        foreach (var theme in new[] { ThemePreset.ById("dusk"), ThemePreset.ById("cloud") })
        foreach (string style in new[] { "tile", "pill", "battery", "multi" })
        {
            var foreground = theme.IsLight ? Brushes.White : Brushes.Black;
            var strip = new TaskbarBatteryStrip(new[] { new DeviceInfo { Level = 76 } }, style, true, false,
                foreground, 20, "", "", null, new AppSettings(), theme);
            Arrange(strip);
            var number = Children<TextBlock>(strip).Single(t => t.Name == "BatteryNumber");
            Require(((SolidColorBrush)number.Foreground).Color == theme.Text,
                $"{style} uses shell text on a differently colored theme surface.");
        }
    }

    internal static void Portraits()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Controls.CharacterBatteryIcon");
        Require(type != null, "Reference character renderer is missing.");
        var load = type!.GetMethod("LoadArtwork", BindingFlags.Public | BindingFlags.Static)!;
        foreach (string kind in new[] { "rei", "asuka", "peek" })
        {
            var image = (BitmapSource)load.Invoke(null, [kind])!;
            Require(image.PixelWidth > 500 && image.PixelHeight > 500, $"Embedded {kind} artwork is missing.");
            var element = (FrameworkElement)Activator.CreateInstance(type)!;
            type.GetProperty("Character")!.SetValue(element, kind);
            type.GetProperty("Level")!.SetValue(element, 100);
            element.Width = element.Height = 144;
            Arrange(element);
            var pixels = Pixels(Snapshot(element));
            Require(pixels.Where((_, i) => i % 4 == 3).Count(p => p > 30) > 2000, $"{kind} is blank.");
            Save(element, $"artifacts/reference-styles/{kind}.png");
        }
        Require(load.Invoke(null, ["missing-character"]) is BitmapSource, "Unknown character has no safe fallback.");
    }

    internal static void PaletteAndGrouping()
    {
        var single = new ReferenceBatteryIcon { StyleName = "tile", Width = 144, Height = 144,
            Devices = new() { (DeviceKind.Mouse, 76, false, false) } };
        Arrange(single);
        var pixel = new byte[4];
        Snapshot(single).CopyPixels(new Int32Rect(30, 30, 1, 1), pixel, 4, 0);
        Require(pixel[2] == 27 && pixel[1] == 31 && pixel[0] == 33, "Reference tile lost its dark surface color.");
        var grouped = new ReferenceBatteryIcon { StyleName = "ring", Width = 144, Height = 180,
            Devices = new() { (DeviceKind.Mouse, 76, false, false), (DeviceKind.Keyboard, 28, true, false) } };
        Arrange(grouped);
        var pixels = Pixels(Snapshot(grouped));
        Require(pixels.Where((_, i) => i % 4 == 3).Count(a => a > 0) > 144 * 180 * .7,
            "Multiple readings still render as one standalone ring instead of a shared panel.");
    }

    internal static void Rendering()
    {
        var gallery = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(34, 38, 41)), Margin = new Thickness(12) };
        foreach (string style in new[] { "ring", "tile", "pill", "battery", "plain", "multi", "character", "portrait" })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
            row.Children.Add(new TextBlock { Text = style, Width = 70, Foreground = Brushes.White });
            if (style is not ("character" or "portrait"))
            {
                var icon = new ReferenceBatteryIcon
                {
                    StyleName = style, Width = style == "multi" ? 240 : 144, Height = style == "multi" ? 88 : 224,
                    Devices = new() { (DeviceKind.Keyboard, 100, false, false), (DeviceKind.Mouse, 0, false, true), (DeviceKind.Headset, -1, true, false) }
                };
                Arrange(icon);
                var pixels = Pixels(Snapshot(icon));
                Require(pixels.Where((_, i) => i % 4 == 3).Count(p => p > 0) > 100, $"{style} floating rendering is blank.");
                row.Children.Add(icon);
            }
            foreach (bool vertical in new[] { false, true })
            {
                var devices = new[] {
                    new DeviceInfo { Kind = DeviceKind.Keyboard, Name = "Keyboard", Level = 100 },
                    new DeviceInfo { Kind = DeviceKind.Mouse, Name = "Mouse", Level = 0, Charging = true },
                    new DeviceInfo { Kind = DeviceKind.Headset, Name = "Headset", Level = -1 }
                };
                var strip = new TaskbarBatteryStrip(devices, style, true, vertical, Brushes.WhiteSmoke, 20, "#FFCC00", "#00AAFF");
                Require(strip.StyleName == style, $"{style} still falls back.");
                Arrange(strip);
                var numbers = Children<TextBlock>(strip).Where(n => n.Name == "BatteryNumber").ToArray();
                Require(numbers.Select(n => n.Text).SequenceEqual(new[] { "100", "0", "\u2014" }), $"{style} lost values.");
                foreach (var number in numbers)
                {
                    Require(((SolidColorBrush)number.Foreground).Color == ThemePreset.C("#FFCC00"), $"{style} lost manual color.");
                    Require(new Rect(strip.RenderSize).Contains(number.TransformToAncestor(strip).TransformBounds(new Rect(number.RenderSize))),
                        $"{style} clips numbers in {(vertical ? "vertical" : "horizontal")} orientation.");
                }
                devices[0].Level = 73;
                devices[1].IsStale = true;
                Arrange(strip);
                Require(numbers[0].Text == "73", $"{style} stopped updating live.");
                Require(Children<TextBlock>(strip).Where(n => n.Name == "ChargingMark").All(n => n.Visibility == Visibility.Collapsed),
                    $"{style} shows charging for stale data.");
                Require((vertical ? strip.Width : strip.Height) <= 40, $"{style} exceeded the taskbar/edge thickness.");
                foreach (double dpi in new[] { 1d, 1.5, 2, 3 })
                    Require(Pixels(Snapshot(strip, dpi)).Where((_, i) => i % 4 == 3).Count(p => p > 0) > 100, $"{style} blank at {dpi} DPI.");
                row.Children.Add(new Border { Child = strip, Margin = new Thickness(12) });
            }
            gallery.Children.Add(row);
        }
        Arrange(gallery);
        Save(gallery, "artifacts/reference-styles/gallery.png");
    }

    internal static void Floating()
    {
        string[] names = ["Settings", "Battery", "Dock", "Mini", "Icon", "Taskbar", "Edge", "SettingsView", "Theme"];
        var properties = names.Select(n => typeof(MouseDock.App).GetProperty(n)!).ToArray();
        var previous = properties.Select(p => p.GetValue(null)).ToArray();
        var directory = typeof(SettingsStore).GetProperty("TestDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldDirectory = directory.GetValue(null);
        var settings = new AppSettings { Provider = "json", JsonPath = "missing-style.json", IconStyle = "tile" };
        using var battery = (BatteryService)Activator.CreateInstance(typeof(BatteryService), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [settings, null, false], null)!;
        IconWindow? icon = null;
        SettingsWindow? window = null;
        TaskbarWindow? taskbar = null;
        ScreenEdgeWindow? edge = null;
        try
        {
            foreach (var property in properties) property.SetValue(null, null);
            properties[0].SetValue(null, settings);
            properties[1].SetValue(null, battery);
            properties[^1].SetValue(null, ThemePreset.ById("dusk"));
            directory.SetValue(null, Path.GetFullPath("artifacts/reference-styles/settings"));
            battery.VisibleDevices.Add(new DeviceInfo { Id = "key", Kind = DeviceKind.Keyboard, Name = "Keyboard", Level = 100 });
            battery.VisibleDevices.Add(new DeviceInfo { Id = "mouse", Kind = DeviceKind.Mouse, Name = "Mouse", Level = 76 });
            icon = new IconWindow();
            foreach (string style in new[] { "ring", "tile", "pill", "battery", "plain", "multi", "character", "portrait" })
            {
                icon.ApplyStyle(style, false);
                var root = (FrameworkElement)icon.FindName("RootGrid");
                Arrange(root);
                Require(root.DesiredSize.Width > 50 && root.DesiredSize.Height > 50, $"{style} has no floating content.");
                Save(root, $"artifacts/reference-styles/floating-{style}.png");
            }
            window = new SettingsWindow();
            window.Reload();
            foreach (string style in new[] { "character", "portrait" })
                Require(((Panel)window.FindName("IconStyleChoices")).Children.OfType<Button>().Any(b => (string)b.Tag == style),
                    $"Floating settings omit {style}.");
            Require(window.FindName("ReferenceCharacterChoices") is ComboBox, "Character selection is inaccessible.");
            settings.TaskbarStyle = settings.EdgeStyle = "character";
            settings.IconFollowTheme = true;
            taskbar = new TaskbarWindow();
            edge = new ScreenEdgeWindow();
            properties[5].SetValue(null, taskbar);
            properties[6].SetValue(null, edge);
            properties[^1].SetValue(null, ThemePreset.ById("cloud"));
            MouseDock.App.ApplyTheme(false);
            foreach (var surface in new FrameworkElement[] { (FrameworkElement)taskbar.Content, (FrameworkElement)edge.Content })
            {
                Arrange(surface);
                Require(Children<TextBlock>(surface).Where(t => t.Name == "BatteryNumber")
                    .All(t => ((SolidColorBrush)t.Foreground).Color == ThemePreset.C("#1E2230")),
                    "Theme changes left a taskbar or edge appearance using the previous palette.");
            }
            foreach (var theme in new[] { ThemePreset.ById("dusk"), ThemePreset.ById("cloud") })
            {
                properties[^1].SetValue(null, theme);
                foreach (var (style, _) in TaskbarBatteryStrip.Styles(true))
                {
                    settings.EdgeStyle = style;
                    edge.UpdateDevices();
                    var surface = (FrameworkElement)edge.Content;
                    Arrange(surface);
                    Require(Children<TextBlock>(surface).Where(t => t.Name == "BatteryNumber")
                        .All(t => ((SolidColorBrush)t.Foreground).Color == theme.Text),
                        $"{style} edge text ignores theme following.");
                    CheckEdgeInk(surface, theme.Accent, style);
                    settings.EdgeGlyphColor = "#CC44AA";
                    edge.UpdateDevices();
                    Arrange(surface);
                    CheckEdgeInk(surface, ThemePreset.C("#CC44AA"), style);
                    settings.EdgeGlyphColor = "#F5F5F5";
                }
            }
            settings.EdgeStyle = "ring";
            typeof(SettingsWindow).GetMethod("ApplyEdgeColor", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, ["#F5F5F5"]);
            edge.UpdateDevices();
            CheckEdgeInk((FrameworkElement)edge.Content, ThemePreset.C("#F5F5F5"), "manual white");
            var jsonOptions = new JsonSerializerOptions
                { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
            Require(settings.Clone().EdgeGlyphColorCustomized &&
                JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, jsonOptions), jsonOptions)!
                    .EdgeGlyphColorCustomized, "Explicit white color was lost across clone or restart.");
            typeof(SettingsWindow).GetMethod("OnResetEdgeColor", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [window, new RoutedEventArgs()]);
            edge.UpdateDevices();
            CheckEdgeInk((FrameworkElement)edge.Content, ThemePreset.ById("cloud").Accent, "reset");
        }
        finally
        {
            icon?.Close();
            window?.Close();
            taskbar?.Close();
            edge?.Close();
            for (int i = 0; i < properties.Length; i++) properties[i].SetValue(null, previous[i]);
            directory.SetValue(null, oldDirectory);
        }
    }

    private static void CheckEdgeInk(FrameworkElement surface, Color expected, string style)
    {
        var brushes = Children<DeviceBatteryRing>(surface).Select(r => r.BatteryBrush)
            .Concat(Children<DeviceGlyph>(surface).Select(g => g.GlyphBrush))
            .Concat(Children<ReferenceBatteryIcon>(surface).Select(i => i.Accent))
            .Concat(Children<CharacterBatteryIcon>(surface).Select(i => i.Accent)).ToArray();
        Require(brushes.Length > 0 && brushes.All(b => b is SolidColorBrush solid && solid.Color == expected),
            $"{style} edge glyph or progress ignores its effective color.");
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
    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
    }
    private static RenderTargetBitmap Snapshot(FrameworkElement element, double dpi = 1)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi)),
            Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi)), dpi * 96, dpi * 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }
    private static byte[] Pixels(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }
    private static void Save(FrameworkElement element, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Snapshot(element, 2)));
        using var output = File.Create(path);
        encoder.Save(output);
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
