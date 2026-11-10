using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Models;
using MouseDock.Services;

internal static class ReferenceThemeChecks
{
    internal static void Themes()
    {
        var expected = new[] {
            ("reference-mint", "#141719", "#a6f5bf", "#f6f7f7", "#53b89e"),
            ("pine", "#111916", "#accbb3", "#edf1ec", "#52785d"),
            ("ocean", "#101c20", "#7bc9d3", "#e9f4f5", "#247783"),
            ("rose", "#21191d", "#e4afc8", "#f8eef2", "#a14f76"),
            ("plum", "#1b1720", "#cdb0dd", "#f2edf5", "#86519f"),
            ("amber", "#201912", "#f1b77e", "#f7efe6", "#a15e24")
        };
        foreach (var (id, darkBg, darkAccent, lightBg, lightAccent) in expected)
        {
            Require(ThemePreset.Presets.Any(p => p.Id == id), $"Reference theme {id} is missing.");
            foreach (bool light in new[] { false, true })
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(
                    $$"""{"ThemeId":"{{id}}","ThemeMode":"{{(light ? "light" : "dark")}}"}""")!;
                var theme = Palette.ResolveTheme(settings, out _);
                Require(theme.IsLight == light && theme.BgTop == ThemePreset.C(light ? lightBg : darkBg) &&
                    theme.Accent == ThemePreset.C(light ? lightAccent : darkAccent), $"{id} lost its reference palette.");
                Require(theme.Text != theme.BgTop && theme.TextDim != theme.Surface, "Theme text became invisible.");
            }
        }
        Require(ThemePreset.ById("mint").Accent == ThemePreset.C("#7FE7C4"), "Legacy mint changed.");
        Require(ReferenceThemes.Resolve("amber", "light").TextDim == ThemePreset.C("#503F30"),
            "Amber secondary text differs from the original light palette.");
        Require(ThemePreset.ById("cloud").IsLight && ThemePreset.ById("missing").Id == "dusk", "Legacy theme fallback changed.");
    }

    internal static void Preferences()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """{"ThemeMode":"light","BackgroundZoom":2.25,"BackgroundPositionX":0.2,"BackgroundPositionY":0.8,"BackgroundDepth":0.65,"SelectedDeviceIds":["r98"],"DockBackground":"own.png"}""")!;
        var options = new JsonSerializerOptions
        { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        using var clone = JsonDocument.Parse(JsonSerializer.Serialize(settings.Clone(), options));
        foreach (var pair in new[] { ("BackgroundZoom", 2.25), ("BackgroundPositionX", .2), ("BackgroundPositionY", .8), ("BackgroundDepth", .65) })
            Require(clone.RootElement.TryGetProperty(pair.Item1, out var value) && value.GetDouble() == pair.Item2,
                $"Clone or serialization lost {pair.Item1}.");
        Require(clone.RootElement.GetProperty("ThemeMode").GetString() == "light" &&
            settings.Clone().SelectedDeviceIds.SequenceEqual(new[] { "r98" }) && settings.Clone().DockBackground == "own.png",
            "Appearance cloning altered existing preferences.");
    }

    internal static void Crop()
    {
        var crop = ProductionType("MouseDock.Services.PictureBackground").GetMethod("Crop")!;
        Rect Calculate(Size source, Size target, double zoom, double x, double y) =>
            (Rect)crop.Invoke(null, [source, target, zoom, x, y])!;
        var left = Calculate(new Size(400, 200), new Size(100, 100), 1, 0, .5);
        var right = Calculate(new Size(400, 200), new Size(100, 100), 1, 1, .5);
        Require(left == new Rect(0, 0, .5, 1) && right == new Rect(.5, 0, .5, 1),
            "Landscape cropping stretched or ignored its position.");
        var portrait = Calculate(new Size(200, 400), new Size(100, 100), 2, .5, 1);
        Require(portrait == new Rect(.25, .75, .5, .25), "Portrait zoom/crop is incorrect.");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -100d, 100d })
        {
            var bounds = Calculate(new Size(400, 200), new Size(320, 700), invalid, invalid, invalid);
            Require(!bounds.IsEmpty && new Rect(0, 0, 1, 1).Contains(bounds), "Invalid persisted crop escaped the image.");
        }
    }

    internal static void Background()
    {
        var type = ProductionType("MouseDock.Services.PictureBackground");
        var image = BitmapSource.Create(4, 2, 96, 96, PixelFormats.Bgr32, null,
            Enumerable.Repeat(new byte[] { 0, 0, 255, 0 }, 8).SelectMany(p => p).ToArray(), 16);
        var theme = ThemePreset.ById("dusk");
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"BackgroundDepth":0}""")!;
        Brush Create(AppSettings s) => (Brush)type.GetMethod("Create")!.Invoke(null, [image, new Size(40, 40), s, theme])!;
        var plain = Pixel(Create(settings));
        typeof(AppSettings).GetProperty("BackgroundDepth")!.SetValue(settings, .8);
        var tinted = Pixel(Create(settings));
        Require(plain.R > 250 && plain.G < 3 && tinted.R < plain.R - 100, "Background depth was not applied.");
        Require(plain.A == 255 && tinted.A == 255, "Picture background left transparent gaps.");

        string directory = Path.GetFullPath("artifacts/reference-theme-checks");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "input.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(source)) encoder.Save(file);
        var imported = (string)type.GetMethod("Import")!.Invoke(null, [source, directory])!;
        Require(File.Exists(imported) && imported != source, "Picture import did not retain an independent local copy.");
        File.Delete(source);
        Require(Palette.Load(imported) != null, "Imported picture still depends on its original path.");
        var editorType = ProductionType("MouseDock.Views.BackgroundEditorWindow");
        var original = new AppSettings { CustomBackground = imported };
        var editor = (Window)Activator.CreateInstance(editorType, [original])!;
        var draft = (AppSettings)editorType.GetProperty("Draft")!.GetValue(editor)!;
        typeof(AppSettings).GetProperty("BackgroundZoom")!.SetValue(draft, 3d);
        editor.Close();
        Require((double)typeof(AppSettings).GetProperty("BackgroundZoom")!.GetValue(original)! == 1,
            "Closing the editor changed saved settings.");
    }

    internal static void Surfaces()
    {
        string[] names = ["Settings", "Theme", "Battery", "Dock", "Mini", "Icon", "Taskbar", "Edge", "SettingsView"];
        var properties = names.Select(n => typeof(MouseDock.App).GetProperty(n)!).ToArray();
        var previous = properties.Select(p => p.GetValue(null)).ToArray();
        var directory = typeof(SettingsStore).GetProperty("TestDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldDirectory = directory.GetValue(null);
        var settings = new AppSettings { Provider = "json", JsonPath = "missing-theme.json", ThemeId = "ocean" };
        using var battery = (BatteryService)Activator.CreateInstance(typeof(BatteryService), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [settings, null, false], null)!;
        MouseDock.Views.SettingsWindow? window = null;
        MouseDock.Views.MiniWindow? mini = null;
        try
        {
            foreach (var property in properties) property.SetValue(null, null);
            properties[0].SetValue(null, settings);
            properties[1].SetValue(null, Palette.ResolveTheme(settings, out _));
            properties[2].SetValue(null, battery);
            directory.SetValue(null, Path.GetFullPath("artifacts/reference-theme-checks/settings"));
            window = new MouseDock.Views.SettingsWindow();
            window.Reload();
            Require(window.FindName("EditBackgroundButton") is Button, "Settings have no picture editor action.");
            Require(window.FindName("ThemeLight") is RadioButton, "Settings have no light/dark selection.");
            ((RadioButton)window.FindName("ThemeLight")).IsChecked = true;
            Require(settings.ThemeMode == "light" && MouseDock.App.Theme.Accent == ThemePreset.C("#247783"),
                "Changing mode did not apply the reference light palette.");
            settings.CustomBackground = Directory.GetFiles(Path.GetFullPath("artifacts/reference-theme-checks"), "*.png").First();
            window.Reload();
            Require(((Border)window.FindName("Root")).Background is DrawingBrush, "Global picture did not reach settings.");
            mini = new MouseDock.Views.MiniWindow();
            mini.ApplyBackground("");
            var root = (Border)mini.FindName("Root");
            Require(root.Background is DrawingBrush, "Global picture did not reach the mini view.");
            mini.ApplyBackground(settings.CustomBackground);
            root.Measure(new Size(200, 200));
            root.Arrange(new Rect(0, 0, 200, 200));
            root.UpdateLayout();
            Require(root.Background is ImageBrush, "Resizing overwrote the mini view's own picture.");
            settings.CustomBackground = "missing-image.png";
            mini.ApplyBackground("");
            Require(root.Background is LinearGradientBrush, "Missing global picture did not restore the theme.");
        }
        finally
        {
            window?.Close();
            mini?.Close();
            for (int i = 0; i < properties.Length; i++) properties[i].SetValue(null, previous[i]);
            directory.SetValue(null, oldDirectory);
        }
    }

    private static Color Pixel(Brush brush)
    {
        var element = new Border { Width = 40, Height = 40, Background = brush };
        element.Measure(new Size(40, 40));
        element.Arrange(new Rect(0, 0, 40, 40));
        var bitmap = new RenderTargetBitmap(40, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(20, 20, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static Type ProductionType(string name)
    {
        var type = typeof(AppSettings).Assembly.GetType(name);
        Require(type != null, $"Missing {name}.");
        return type!;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
