using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Views;
using System.Reflection;
using System.IO;

internal static class CharacterEditorChecks
{
    internal static void Draft()
    {
        using var fixture = new AppearanceFixture();
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Views.CharacterEditorWindow");
        Require(type != null, "Unified character editor is missing.");
        var settings = new AppSettings { IconStyle = "ring", ReferenceCharacter = "rei", PhotoZoom = 1.4, TaskbarOffset = 47 };
        var editor = (Window)Activator.CreateInstance(type!, [settings])!;
        var draft = (AppSettings)type!.GetProperty("Draft")!.GetValue(editor)!;
        draft.ReferenceCharacter = "asuka";
        draft.PhotoZoom = 2.2;
        editor.Close();
        Require(settings.ReferenceCharacter == "rei" && settings.PhotoZoom == 1.4, "Canceling character editing changes saved settings.");
        type.GetMethod("ApplyTo")!.Invoke(editor, [settings]);
        Require(settings.ReferenceCharacter == "asuka" && settings.PhotoZoom == 2.2 && settings.TaskbarOffset == 47,
            "Applying character editing loses unrelated preferences or ignores the draft.");
    }

    internal static void PhotoDetails()
    {
        var device = new DeviceInfo { Id = "key", Name = "Keyboard", Kind = DeviceKind.Keyboard, Level = 76 };
        var icon = new CharacterBatteryIcon { Character = "photo", Width = 240, Height = 260, Devices = new[] { device } };
        var original = Snapshot(icon);
        device.Name = "Renamed R98";
        var renamed = Snapshot(icon);
        Require(!original.SequenceEqual(renamed), "Photo overlay does not display the device name.");
        device.Charging = true;
        Require(!renamed.SequenceEqual(Snapshot(icon)), "Photo panel does not display live device status.");

        byte[] PhotoWith(int count)
        {
            var size = CharacterBatteryIcon.PhotoSize(count);
            var portrait = new CharacterBatteryIcon { Character = "photo", Width = size.Width, Height = size.Height,
                Devices = Enumerable.Repeat(device, count).ToArray() };
            return Snapshot(portrait);
        }
        const int avatarEnd = 156 * 160 * 4;
        Require(PhotoWith(1).AsSpan(0, avatarEnd).SequenceEqual(PhotoWith(6).AsSpan(0, avatarEnd)),
            "Adding device rows must leave the entire avatar region unchanged.");
    }

    internal static void Application()
    {
        using var fixture = new AppearanceFixture();
        fixture.Settings.SurfaceMode = "icon";
        var icon = new IconWindow();
        var settings = new SettingsWindow();
        fixture.Set("Icon", icon); fixture.Set("SettingsView", settings);
        icon.ApplyStyle("ring", false);
        var editor = new CharacterEditorWindow(fixture.Settings, "portrait");
        try
        {
            var apply = typeof(SettingsWindow).GetMethod("ApplyCharacterEdit", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(apply != null, "The editor has no integrated completion action.");
            apply!.Invoke(settings, [editor]);
            Require((string)typeof(IconWindow).GetField("_activeStyle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(icon)! == "portrait" &&
                fixture.Settings.SurfaceMode == "icon", "Completing the editor leaves the visible floating style unchanged.");
        }
        finally { editor.Close(); }
    }

    internal static void CompactSurface()
    {
        foreach (string surface in new[] { "taskbar", "edge" })
        {
            using var fixture = new AppearanceFixture();
            var first = fixture.Battery.VisibleDevices[0];
            var second = new DeviceInfo { Id = "keyboard-editor", Name = "Keyboard", Level = 28 };
            fixture.Battery.VisibleDevices.Add(second);
            fixture.Settings.SurfaceMode = surface;
            fixture.Settings.IconStyle = "portrait";
            fixture.Settings.TaskbarStyle = fixture.Settings.EdgeStyle = "character";
            fixture.Settings.DeviceCharacters[first.Id] = "asuka";
            fixture.Settings.DeviceCharacters[second.Id] = "rei";
            var editor = new CharacterEditorWindow(fixture.Settings);
            try
            {
                var root = (FrameworkElement)editor.Content;
                root.Measure(new Size(364, 680)); root.Arrange(new Rect(0, 0, 364, 680)); root.UpdateLayout();
                var preview = Children<CharacterBatteryIcon>((DependencyObject)editor.Content).Single();
                Require(preview.Character == "asuka", $"{surface} character editing opens the unrelated floating photo style.");
                var devices = Children<ComboBox>((DependencyObject)editor.Content).Single(c => c.Name == "CharacterDeviceChoices");
                var characters = Children<ComboBox>((DependencyObject)editor.Content).Single(c => c.Name == "CharacterArtworkChoices");
                devices.SelectedIndex = 1;
                characters.SelectedItem = characters.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == "peek");
                Require(fixture.Settings.DeviceCharacters[second.Id] == "rei", "Compact character drafts leak before completion.");
                editor.ApplyTo(fixture.Settings);
                Require(fixture.Settings.IconStyle == "portrait", $"{surface} character editing overwrites the floating photo style.");
                var strip = new TaskbarBatteryStrip(new[] { first, second }, "character", true, surface == "edge",
                    Brushes.White, 20, "", "", null, fixture.Settings, ThemePreset.ById("dusk"));
                var artwork = Children<CharacterBatteryIcon>(strip).ToDictionary(i => ((DeviceInfo)i.DataContext).Id, i => i.Character);
                Require(artwork[first.Id] == "asuka" && artwork[second.Id] == "peek",
                    $"{surface} cannot independently apply the selected device's character.");
                Save(root, surface + "-character-editor");
            }
            finally { editor.Close(); }
        }
    }

    internal static void CompactCompletion()
    {
        foreach (string surface in new[] { "taskbar", "edge" })
        foreach (bool show in new[] { false, true })
        {
            using var fixture = new AppearanceFixture();
            fixture.Settings.SurfaceMode = surface;
            fixture.Settings.IconStyle = "ring";
            fixture.Settings.TaskbarStyle = fixture.Settings.EdgeStyle = "character";
            var settings = new SettingsWindow();
            fixture.Set("SettingsView", settings);
            var editor = new CharacterEditorWindow(fixture.Settings, "portrait");
            try
            {
                if (show)
                {
                    editor.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                        Children<Button>((DependencyObject)editor.Content).Single(b => b.Content is string text && text == "使用并显示")
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
                    Require(editor.ShowDialog() == true, "The editor's apply-and-show action did not complete.");
                }
                typeof(SettingsWindow).GetMethod("ApplyCharacterEdit", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(settings, [editor]);
                Require(fixture.Settings.SurfaceMode == surface, $"Completing {surface} editing switches to a different surface.");
                Require(fixture.Settings.IconStyle == "ring", $"Completing {surface} editing changes the floating style.");
                Require(fixture.Settings.TaskbarStyle == (surface == "taskbar" ? "portrait" : "character") &&
                    fixture.Settings.EdgeStyle == (surface == "edge" ? "portrait" : "character"),
                    $"Completing {surface} editing changes the wrong compact style.");
            }
            finally { editor.Close(); }
        }
    }

    internal static void PreviewAndBounds()
    {
        using var fixture = new AppearanceFixture();
        for (int i = 1; i < 6; i++)
            fixture.Battery.VisibleDevices.Add(new DeviceInfo { Id = "preview-" + i, Name = "Device " + i, Level = 80 });
        var editor = new CharacterEditorWindow(fixture.Settings, "portrait");
        try
        {
            var preview = (CharacterBatteryIcon)typeof(CharacterEditorWindow).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            Require(preview.DataContext == fixture.Battery.VisibleDevices[0] && !preview.ShowNumber &&
                preview.Width == 112 && preview.Height == 112,
                "Photo editing does not preview the selected device's individual photo.");
            var devices = (System.Windows.Controls.ComboBox)typeof(CharacterEditorWindow)
                .GetField("_deviceChoices", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            Require(devices.Items.Count == 6 && devices.Visibility == Visibility.Visible,
                "Photo editing cannot select every displayed device.");
            var root = (FrameworkElement)editor.Content;
            root.Measure(new Size(364, 680)); root.Arrange(new Rect(0, 0, 364, 680)); root.UpdateLayout();
            Save(root, "character-editor");
        }
        finally { editor.Close(); }
        var character = new CharacterBatteryIcon { Width = 240, Height = 240, TransparentBackground = false };
        Snapshot(character);
        Require(character.VisibleArtworkBounds == new Rect(0, 0, 240, 240),
            "An opaque character plate still uses its smaller transparent artwork bounds.");
    }

    internal static void PhotoContrast()
    {
        Directory.CreateDirectory("artifacts/reference-interaction/images");
        string path = Path.GetFullPath("artifacts/reference-interaction/images/black-photo.png");
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var output = File.Create(path)) encoder.Save(output);
        var icon = new CharacterBatteryIcon { Character = "photo", PhotoPath = path, Width = 240, Height = 260,
            Ink = Brushes.Black, Devices = new[] { new DeviceInfo { Name = "R98Pro V2", Level = 76 } } };
        var pixels = Snapshot(icon);
        int bright = 0;
        for (int y = 206; y < 250; y++)
        for (int x = 0; x < 240; x++)
        {
            int at = (y * 240 + x) * 4;
            if (pixels[at] > 120 && pixels[at + 1] > 120 && pixels[at + 2] > 120) bright++;
        }
        Require(bright > 50, "Dark photo text remains unreadable against its dark backing.");
        Save(icon, "photo-light-theme");
    }

    private static void Save(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts/reference-interaction/ui");
        using var output = File.Create("artifacts/reference-interaction/ui/" + name + ".png");
        encoder.Save(output);
    }

    private static byte[] Snapshot(FrameworkElement element)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.Width, (int)element.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
