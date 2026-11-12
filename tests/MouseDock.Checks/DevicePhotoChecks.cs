using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Views;

internal static class DevicePhotoChecks
{
    private static readonly JsonSerializerOptions JsonOptions = new()
        { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static DeviceInfo[] Pair() => new[] {
        new DeviceInfo { Id = "mouse-a", Name = "Mouse", Level = 90 },
        new DeviceInfo { Id = "keyboard-b", Name = "Keyboard", Kind = DeviceKind.Keyboard, Level = 28 }
    };
    private static AppSettings Settings(string first, string second) => JsonSerializer.Deserialize<AppSettings>(
        JsonSerializer.Serialize(new
        {
            IconStyle = "portrait", PhotoAsset = "legacy-photo.png", PhotoZoom = 1.2,
            DevicePhotos = new Dictionary<string, object>
            {
                ["mouse-a"] = new { Path = first, Zoom = 1.5, OffsetX = .15, OffsetY = -.2 },
                ["keyboard-b"] = new { Path = second, Zoom = 2.0, OffsetX = -.1, OffsetY = .25 }
            }
        }), JsonOptions)!;
    private static TaskbarBatteryStrip Strip(DeviceInfo[] devices, AppSettings settings, bool vertical = false) =>
        new(devices, "portrait", true, vertical, Brushes.WhiteSmoke, 20, "", "", null,
            settings, ThemePreset.ById("dusk"));

    internal static void Surfaces()
    {
        var first = Photo("mouse", Colors.IndianRed);
        var second = Photo("keyboard", Colors.DodgerBlue);
        var settings = Settings(first, second);
        settings = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings.Clone(), JsonOptions), JsonOptions)!;
        var devices = Pair();
        var gallery = new StackPanel { Background = Brushes.DimGray };
        foreach (bool vertical in new[] { false, true })
        {
            var strip = Strip(devices, settings, vertical);
            CheckPhotos(strip, first, second);
            CheckCaptions(strip, devices, "90", "28");
            gallery.Children.Add(strip);
        }
        devices[0].Id = "MOUSE-A";
        devices[0].Name = "Renamed mouse";
        var reordered = Children<CharacterBatteryIcon>(Strip(devices.Reverse().ToArray(), settings)).ToArray();
        Require(reordered[0].PhotoPath == second && reordered[1].PhotoPath == first,
            "Reordering or reconnecting devices swaps their photos.");
        Require(Children<CharacterBatteryIcon>(Strip(new[] { devices[0] }, settings)).Single().PhotoPath == first,
            "Temporarily removing another device loses the saved photo.");

        using var fixture = new AppearanceFixture();
        fixture.Set("Settings", settings);
        fixture.Battery.VisibleDevices.Clear();
        foreach (var device in devices) fixture.Battery.VisibleDevices.Add(device);
        var icon = new IconWindow();
        fixture.Set("Icon", icon);
        icon.ApplyStyle("portrait", false);
        var root = (FrameworkElement)icon.FindName("RootGrid");
        CheckPhotos(root, first, second);
        CheckCaptions(root, devices, "90%", "28%");
        Save(root, "floating-photos", 2);
        devices[0].Level = -1;
        devices[1].Level = 0;
        Arrange(root);
        CheckCaptions(root, devices, "\u2014", "0%");
        foreach (double dpi in new[] { 1d, 1.5, 2, 3 }) Save(gallery, $"photo-surfaces-{dpi}", dpi);
    }

    internal static void Editor()
    {
        using var fixture = new AppearanceFixture();
        var devices = Pair();
        fixture.Battery.VisibleDevices.Clear();
        foreach (var device in devices) fixture.Battery.VisibleDevices.Add(device);
        var editor = new CharacterEditorWindow(fixture.Settings, "portrait");
        try
        {
            Arrange((FrameworkElement)editor.Content);
            var select = Children<ComboBox>(editor.Content as DependencyObject ?? editor)
                .Single(c => c.Name == "CharacterDeviceChoices");
            Require(select.Visibility == Visibility.Visible, "Photo mode hides the device selector.");
            var usePhoto = typeof(CharacterEditorWindow).GetMethod("UsePhoto", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(usePhoto != null, "Selecting a photo has no per-device editing path.");
            var zoom = Children<Slider>((DependencyObject)editor.Content).Single();
            string first = Photo("editor-mouse", Colors.IndianRed), second = Photo("editor-keyboard", Colors.DodgerBlue);
            usePhoto!.Invoke(editor, [first]);
            zoom.Value = 2.4;
            select.SelectedIndex = 1;
            usePhoto.Invoke(editor, [second]);
            zoom.Value = 1.6;
            select.SelectedIndex = 0;
            var preview = Children<CharacterBatteryIcon>((DependencyObject)editor.Content).Single();
            Require(preview.PhotoPath == first && preview.PhotoZoom == 2.4 && zoom.Value == 2.4,
                "Returning to a device loses its independently edited photo or crop.");
            Require(fixture.Settings.PhotoAsset == "" &&
                Children<CharacterBatteryIcon>(Strip(devices, fixture.Settings)).All(i => i.PhotoPath == ""),
                "Cancelling a photo draft would change saved photos.");
            editor.ApplyTo(fixture.Settings);
            var saved = Children<CharacterBatteryIcon>(Strip(devices, fixture.Settings)).ToArray();
            Require(saved[0].PhotoPath == first && saved[0].PhotoZoom == 2.4 &&
                saved[1].PhotoPath == second && saved[1].PhotoZoom == 1.6,
                "Applying per-device photos loses a file or shares one crop across devices.");
            string before = TaskbarBatteryStrip.AppearanceKey(fixture.Settings, ThemePreset.ById("dusk"));
            zoom.Value = 3;
            Require(TaskbarBatteryStrip.AppearanceKey(fixture.Settings, ThemePreset.ById("dusk")) == before,
                "The applied photo map still shares mutable draft state.");
            editor.ApplyTo(fixture.Settings);
            Require(TaskbarBatteryStrip.AppearanceKey(fixture.Settings, ThemePreset.ById("dusk")) != before,
                "Applying a new per-device crop does not invalidate compact surface caches.");
            var reset = Children<Button>((DependencyObject)editor.Content).Single(b => (string?)b.ToolTip == "恢复构图");
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.ApplyTo(fixture.Settings);
            saved = Children<CharacterBatteryIcon>(Strip(devices, fixture.Settings)).ToArray();
            Require(saved[0].PhotoZoom == 1 && saved[0].PhotoOffsetX == 0 && saved[0].PhotoOffsetY == 0 &&
                saved[1].PhotoPath == second && saved[1].PhotoZoom == 1.6,
                "Resetting one device's crop changes another device's photo.");
            Save((FrameworkElement)editor.Content, "photo-device-editor", 1.5);
        }
        finally { editor.Close(); }
    }

    internal static void LegacyAndMissing()
    {
        var devices = Pair();
        var settings = new AppSettings { PhotoAsset = Photo("legacy", Colors.MediumSeaGreen),
            PhotoZoom = 1.7, PhotoOffsetX = .2, PhotoOffsetY = -.3 };
        var before = JsonSerializer.Serialize(settings, JsonOptions);
        var strip = Strip(devices, settings);
        Require(Children<CharacterBatteryIcon>(strip).All(i => i.PhotoPath == settings.PhotoAsset &&
            i.PhotoZoom == 1.7 && i.PhotoOffsetX == .2 && i.PhotoOffsetY == -.3),
            "Devices without an individual photo lose the legacy photo or crop.");
        Require(JsonSerializer.Serialize(settings, JsonOptions) == before,
            "Photo previews mutate real device settings.");
        var missing = Strip(devices, Settings("missing-device-photo.png", settings.PhotoAsset), true);
        Arrange(missing);
        Require(Children<CharacterBatteryIcon>(missing).Count() == 2, "A missing image removes a device badge.");
        devices[0].Charging = true;
        devices[0].IsStale = true;
        Arrange(missing);
        Require(Children<TextBlock>(missing).Where(t => t.Name == "ChargingMark")
            .All(t => t.Visibility == Visibility.Collapsed), "Stale photos show live charging.");
        var invalid = JsonSerializer.Deserialize<AppSettings>("""
            {"DevicePhotos":{"mouse-a":{"Path":"","Zoom":"NaN","OffsetX":"Infinity","OffsetY":"-Infinity"}}}
            """, JsonOptions)!;
        TaskbarBatteryStrip.AppearanceKey(invalid, ThemePreset.ById("dusk"));
        var safe = Children<CharacterBatteryIcon>(Strip(new[] { devices[0] }, invalid)).Single();
        Require(safe.PhotoZoom == 1 && safe.PhotoOffsetX == 0 && safe.PhotoOffsetY == 0,
            "Invalid saved photo framing does not recover to a safe crop.");
    }

    internal static void FloatingWheel()
    {
        using var fixture = new AppearanceFixture();
        var settings = Settings(Photo("wheel-mouse", Colors.IndianRed), Photo("wheel-keyboard", Colors.DodgerBlue));
        fixture.Set("Settings", settings);
        fixture.Battery.VisibleDevices.Clear();
        foreach (var device in Pair()) fixture.Battery.VisibleDevices.Add(device);
        var icon = new IconWindow();
        fixture.Set("Icon", icon);
        icon.ApplyStyle("portrait", false);
        var root = (FrameworkElement)icon.FindName("RootGrid");
        Arrange(root);
        var badge = Children<CharacterDeviceBadge>(root).First();
        Invoke(icon, "OnWheel", badge, new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            { RoutedEvent = UIElement.MouseWheelEvent });
        var photos = Children<CharacterBatteryIcon>(root).ToArray();
        Require(Math.Abs(photos[0].PhotoZoom - 1.62) < .0001 &&
            photos[1].PhotoZoom == 2 && settings.PhotoZoom == 1.2,
            "Floating photo wheel changes shared framing instead of the selected device.");
        Require(photos[0].PhotoOffsetX == .15 && photos[0].PhotoOffsetY == -.2,
            "Zooming a floating photo resets its crop.");

        settings.DevicePhotos.Remove("mouse-a");
        icon.UpdateValue();
        badge = Children<CharacterDeviceBadge>(root).First();
        Invoke(icon, "OnWheel", badge, new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            { RoutedEvent = UIElement.MouseWheelEvent });
        Require(Math.Abs(settings.DevicePhotos["mouse-a"].Zoom - 1.296) < .0001 &&
            settings.PhotoZoom == 1.2 && settings.DevicePhotos["keyboard-b"].Zoom == 2,
            "Zooming a fallback photo modifies the shared fallback or another device.");
    }

    internal static void FloatingDrag()
    {
        using var fixture = new AppearanceFixture();
        var settings = Settings(Photo("drag-mouse", Colors.IndianRed), Photo("drag-keyboard", Colors.DodgerBlue));
        fixture.Set("Settings", settings);
        fixture.Battery.VisibleDevices.Clear();
        foreach (var device in Pair()) fixture.Battery.VisibleDevices.Add(device);
        var icon = new IconWindow { Left = 100, Top = 100 };
        fixture.Set("Icon", icon);
        icon.ApplyStyle("portrait", false);
        var root = (FrameworkElement)icon.FindName("RootGrid");
        Arrange(root);
        var badge = Children<CharacterDeviceBadge>(root).First();
        Invoke(icon, "OnDragWindow", badge,
            new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        Require((double)Field("_dragOriginX").GetValue(icon)! == .15 &&
            (double)Field("_dragOriginY").GetValue(icon)! == -.2,
            "Floating photo drag starts from shared framing instead of the selected crop.");
        Field("_photoCropDrag").SetValue(icon, true);
        var move = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseMoveEvent };
        Field("_iconDragStart").SetValue(icon, move.GetPosition(icon) - new Vector(12, 10));
        Invoke(icon, "OnDragMove", badge, move);
        var photo = settings.DevicePhotos["mouse-a"];
        var artwork = Children<CharacterBatteryIcon>(badge).Single();
        Require(photo.OffsetX > .15 && photo.OffsetY > -.2 &&
            artwork.PhotoOffsetX == photo.OffsetX && artwork.PhotoOffsetY == photo.OffsetY &&
            icon.Left == 100 && icon.Top == 100,
            "Cropping a floating photo moves the window or fails to update the selected artwork.");
        Require(settings.DevicePhotos["keyboard-b"].OffsetX == -.1 &&
            settings.DevicePhotos["keyboard-b"].OffsetY == .25 &&
            settings.PhotoOffsetX == 0 && settings.PhotoOffsetY == 0,
            "Cropping one floating photo changes another device or shared fallback.");
        Invoke(icon, "OnDragMove", new Grid(), move);
        Require(icon.Left == 100 && icon.Top == 100, "An invalid photo crop target moves the window.");
        Invoke(icon, "OnDragEnd", badge,
            new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonUpEvent });

        static FieldInfo Field(string name) =>
            typeof(IconWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    private static void Invoke(IconWindow icon, string name, object sender, RoutedEventArgs args) =>
        typeof(IconWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(icon, [sender, args]);

    private static void CheckPhotos(FrameworkElement root, string first, string second)
    {
        Arrange(root);
        var photos = Children<CharacterBatteryIcon>(root).ToArray();
        Require(photos.Length == 2 && photos[0].PhotoPath == first && photos[1].PhotoPath == second,
            "Devices still share one photo instead of their independently saved images.");
        Require(photos[0].PhotoZoom == 1.5 && photos[0].PhotoOffsetX == .15 && photos[0].PhotoOffsetY == -.2 &&
            photos[1].PhotoZoom == 2 && photos[1].PhotoOffsetX == -.1 && photos[1].PhotoOffsetY == .25,
            "The rendered devices share a crop instead of using their own saved framing.");
        Require(!Pixels(photos[0]).SequenceEqual(Pixels(photos[1])), "Different device photos render identical artwork.");
    }

    private static void CheckCaptions(FrameworkElement root, DeviceInfo[] devices, params string[] values)
    {
        Arrange(root);
        var photos = Children<CharacterBatteryIcon>(root).ToArray();
        var names = Children<TextBlock>(root).Where(t => t.Name == "CharacterDeviceName").ToArray();
        var numbers = Children<TextBlock>(root).Where(t => t.Name == "BatteryNumber").ToArray();
        Require(names.Select(t => t.Text).SequenceEqual(devices.Select(d => d.Name)) &&
            numbers.Select(t => t.Text).SequenceEqual(values), "Photos do not retain their own names and battery values.");
        for (int i = 0; i < photos.Length; i++)
        {
            var image = Bounds(photos[i], root);
            var name = Bounds(names[i], root);
            var number = Bounds(numbers[i], root);
            Require(image.Bottom <= name.Top + .1 && name.Bottom <= number.Top + .1 &&
                new Rect(root.RenderSize).Contains(number), "A photo caption overlaps its image or extends outside the surface.");
        }
    }

    private static string Photo(string name, Color color)
    {
        string directory = Path.GetFullPath("artifacts/character-device-identity/images");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".png");
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { color.B, color.G, color.R, 255 }, 4);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);
        return path;
    }
    private static Rect Bounds(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Arrange(FrameworkElement root)
    {
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
    }
    private static RenderTargetBitmap Snapshot(FrameworkElement root, double dpi = 1)
    {
        Arrange(root);
        var image = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(root.ActualWidth * dpi)),
            Math.Max(1, (int)Math.Ceiling(root.ActualHeight * dpi)), dpi * 96, dpi * 96, PixelFormats.Pbgra32);
        image.Render(root);
        return image;
    }
    private static byte[] Pixels(FrameworkElement root)
    {
        var image = Snapshot(root);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }
    private static void Save(FrameworkElement root, string name, double dpi)
    {
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(Snapshot(root, dpi)));
        Directory.CreateDirectory("artifacts/character-device-identity/ui");
        using var file = File.Create($"artifacts/character-device-identity/ui/{name}.png");
        png.Save(file);
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
