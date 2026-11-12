using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

internal static class CharacterIdentityChecks
{
    private static DeviceInfo[] Pair() => new[] {
        new DeviceInfo { Id = "mouse-a", Name = "MCHOSE A7 V2 Ultra+", Level = 90 },
        new DeviceInfo { Id = "keyboard-b", Name = "RK R98Pro V2", Kind = DeviceKind.Keyboard, Level = 90 }
    };
    private static TaskbarBatteryStrip Strip(DeviceInfo[] devices, AppSettings settings, bool vertical = false,
        bool showNumber = true) => new(devices, "character", showNumber, vertical, Brushes.WhiteSmoke, 20,
            "#FFCC00", "", null, settings, ThemePreset.ById("dusk"));
    private static Dictionary<string, string> Artwork(DependencyObject root) => Children<CharacterBatteryIcon>(root)
        .ToDictionary(art => ((DeviceInfo)art.DataContext).Id, art => art.Character, StringComparer.OrdinalIgnoreCase);

    internal static void Assignments()
    {
        var settings = new AppSettings();
        var devices = Pair();
        CharacterAssignments.Ensure(settings, devices);
        var initial = Artwork(Strip(devices, settings));
        Require(initial.Values.Distinct().Count() == 2, "The two devices still use identical characters.");
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings.Clone(), options), options)!;
        devices[0].Name = "Renamed mouse";
        var reordered = Artwork(Strip(devices.Reverse().ToArray(), restored));
        Require(initial.All(pair => reordered[pair.Key] == pair.Value),
            "Reordering or renaming devices swaps their saved characters.");
        devices[1].Id = "KEYBOARD-B";
        Require(Artwork(Strip(new[] { devices[1] }, restored))[devices[1].Id] == initial["keyboard-b"],
            "A reconnect with different ID casing loses the saved character.");
        Require(Artwork(Strip(new[] { devices[0] }, restored))[devices[0].Id] == initial[devices[0].Id],
            "A temporarily absent device causes the remaining character to change.");
    }

    internal static void PreviewAndFull()
    {
        var settings = new AppSettings();
        var devices = Pair();
        Strip(devices, settings);
        Require(settings.DeviceCharacters.Count == 0,
            "Rendering a preview adds fictional device assignments to saved settings.");
        devices[0].ChargeFull = true;
        devices[1].Charging = true;
        foreach (bool vertical in new[] { false, true })
        {
            var strip = Strip(devices, settings, vertical, false);
            Arrange(strip);
            var full = Children<TextBlock>(strip).Where(t => t.Name == "FullMark").ToArray();
            Require(full[0].Visibility == Visibility.Visible && full[0].Text.Length > 0,
                "Hiding battery numbers also hides the full-charge mark.");
            Require(Children<TextBlock>(strip).Single(t => t.Name == "ChargingMark" &&
                ReferenceEquals(t.DataContext, devices[1])).Visibility == Visibility.Visible,
                "Hiding battery numbers also hides the charging mark.");
            devices[0].IsStale = true;
            Arrange(strip);
            Require(full[0].Visibility == Visibility.Collapsed,
                "Stale readings retain a full-charge mark.");
            devices[0].IsStale = false;
        }
    }

    internal static void Captions()
    {
        var gallery = new StackPanel { Background = Brushes.DimGray };
        foreach (bool vertical in new[] { false, true })
        {
            var devices = Pair();
            var strip = Strip(devices, new AppSettings(), vertical);
            Arrange(strip);
            var arts = Children<CharacterBatteryIcon>(strip).ToArray();
            var names = Children<TextBlock>(strip).Where(t => t.Name == "CharacterDeviceName").ToArray();
            var numbers = Children<TextBlock>(strip).Where(t => t.Name == "BatteryNumber").ToArray();
            Require(names.Select(t => t.Text).SequenceEqual(devices.Select(d => d.Name)),
                "Each character needs a visible name bound to its own device.");
            Require(numbers.Select(t => t.Text).SequenceEqual(new[] { "90", "90" }), "Character readings were lost.");
            for (int i = 0; i < 2; i++)
            {
                var image = Bounds(arts[i], strip);
                var name = Bounds(names[i], strip);
                var number = Bounds(numbers[i], strip);
                Require(image.Bottom <= name.Top + .1 && name.Bottom <= number.Top + .1,
                    "A name or battery number overlaps its character.");
                Require(new Rect(strip.RenderSize).Contains(number) && new Rect(strip.RenderSize).Contains(name),
                    $"A compact character caption extends beyond its strip: vertical={vertical}, strip={strip.RenderSize}, name={name}, number={number}.");
                Require(!arts[i].ShowNumber, "Battery numbers still repeat inside the character image.");
            }
            devices[0].Level = -1;
            devices[1].Level = 0;
            devices[1].Name = "Renamed keyboard";
            devices[0].Charging = true;
            devices[0].IsStale = true;
            Arrange(strip);
            Require(numbers[0].Text == "\u2014" && numbers[1].Text == "0" && names[1].Text == devices[1].Name,
                "Character name or battery updates affect the wrong device.");
            Require(Children<TextBlock>(strip).Where(t => t.Name == "ChargingMark").All(t => t.Visibility == Visibility.Collapsed),
                "Stale character data retains a live charging mark.");
            foreach (double dpi in new[] { 1d, 1.5, 2, 3 })
                Save(strip, $"compact-{vertical}-{dpi}", dpi);
            var hidden = Strip(devices, new AppSettings(), vertical, false);
            Arrange(hidden);
            Require(Children<TextBlock>(hidden).Where(t => t.Name == "BatteryNumber").All(t => t.Visibility == Visibility.Collapsed),
                "Character captions ignore the number visibility setting.");
            gallery.Children.Add(strip);
        }
        Save(gallery, "compact-gallery", 3);
    }

    internal static void Floating()
    {
        using var fixture = new AppearanceFixture();
        fixture.Battery.VisibleDevices.Clear();
        var devices = Pair();
        foreach (var device in devices) fixture.Battery.VisibleDevices.Add(device);
        var icon = new IconWindow();
        fixture.Set("Icon", icon);
        icon.ApplyStyle("character", false);
        var root = (FrameworkElement)icon.FindName("RootGrid");
        Arrange(root);
        var initial = Artwork(root);
        Require(initial.Values.Distinct().Count() == 2, "Floating characters still have identical artwork.");
        var names = Children<TextBlock>(root).Where(t => t.Name == "CharacterDeviceName").ToArray();
        var numbers = Children<TextBlock>(root).Where(t => t.Name == "BatteryNumber").ToArray();
        Require(names.Select(t => t.Text).SequenceEqual(devices.Select(d => d.Name)) &&
            numbers.Select(t => t.Text).SequenceEqual(new[] { "90%", "90%" }),
            "Floating characters do not show their own names and percentages underneath.");
        foreach (var art in Children<CharacterBatteryIcon>(root))
        {
            var number = numbers.Single(n => ReferenceEquals(n.DataContext, art.DataContext));
            Require(Bounds(art, root).Bottom <= Bounds(number, root).Top,
                "A floating battery number overlaps its character.");
        }
        Save(root, "floating-pair", 2);
        var compact = Artwork(Strip(devices.Reverse().ToArray(), fixture.Settings));
        Require(initial.All(pair => compact[pair.Key] == pair.Value), "Surface selection changes a device's character.");
        fixture.Battery.VisibleDevices.Move(0, 1);
        icon.UpdateValue();
        Arrange(root);
        Require(initial.All(pair => Artwork(root)[pair.Key] == pair.Value), "Floating refresh swaps characters by list index.");
        devices[0].Level = 42;
        Arrange(root);
        Require(Children<TextBlock>(root).Single(t => t.Name == "BatteryNumber" &&
            ReferenceEquals(t.DataContext, devices[0])).Text == "42%", "Floating battery no longer updates live.");
    }

    internal static void Editor()
    {
        using var fixture = new AppearanceFixture();
        fixture.Battery.VisibleDevices.Clear();
        var devices = Pair();
        foreach (var device in devices) fixture.Battery.VisibleDevices.Add(device);
        CharacterAssignments.Ensure(fixture.Settings, devices);
        var initial = Artwork(Strip(devices, fixture.Settings));
        var editor = new CharacterEditorWindow(fixture.Settings);
        try
        {
            Arrange((FrameworkElement)editor.Content);
            var selectors = Children<ComboBox>(editor.Content as DependencyObject ?? editor).ToArray();
            var deviceChoice = selectors.SingleOrDefault(c => c.Name == "CharacterDeviceChoices");
            var artworkChoice = selectors.SingleOrDefault(c => c.Name == "CharacterArtworkChoices");
            Require(deviceChoice != null && artworkChoice != null, "Character editing cannot target a particular device.");
            deviceChoice!.SelectedIndex = 1;
            artworkChoice!.SelectedItem = artworkChoice.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag == "peek");
            Require(Artwork(Strip(devices, fixture.Settings)).All(pair => initial[pair.Key] == pair.Value),
                "Changing a draft leaks into saved device assignments.");
            editor.ApplyTo(fixture.Settings);
            var applied = Artwork(Strip(devices, fixture.Settings));
            Require(applied[devices[0].Id] == initial[devices[0].Id] && applied[devices[1].Id] == "peek",
                "Changing one device's character changes another device.");
            string key = TaskbarBatteryStrip.AppearanceKey(fixture.Settings, ThemePreset.ById("dusk"));
            deviceChoice.SelectedIndex = 0;
            artworkChoice.SelectedItem = artworkChoice.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag == "asuka");
            Require(TaskbarBatteryStrip.AppearanceKey(fixture.Settings, ThemePreset.ById("dusk")) == key,
                "Applying a draft shares its mutable character map with saved settings.");
        }
        finally { editor.Close(); }
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
    }
    private static void Save(FrameworkElement element, string name, double dpi)
    {
        Arrange(element);
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi),
            (int)Math.Ceiling(element.ActualHeight * dpi), dpi * 96, dpi * 96, PixelFormats.Pbgra32);
        image.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory("artifacts/character-device-identity/ui");
        using var stream = File.Create($"artifacts/character-device-identity/ui/{name}.png");
        encoder.Save(stream);
    }
    private static void Require(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); }
}
