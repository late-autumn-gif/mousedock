using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;
using Forms = System.Windows.Forms;

internal static class LightUiChecks
{
    internal static void AppearanceLayout()
    {
        using var fixture = new AppearanceFixture();
        var window = new SettingsWindow { Left = -2000, ShowActivated = false };
        fixture.Set("SettingsView", window);
        window.Show(); window.SelectSection("appearance");
        var root = (FrameworkElement)window.Content;
        root.BeginAnimation(UIElement.OpacityProperty, null); root.Opacity = 1;
        var list = (ListBox)window.FindName("ThemeList");
        foreach (double width in new[] { 680d, 760d })
        {
            window.Width = width; window.UpdateLayout();
            Require(Descendants<System.Windows.Controls.Primitives.UniformGrid>(list).Any(grid => grid.Columns == 2), "Theme choices must use a two-column grid.");
            Require(list.Items.Count == ThemePreset.Presets.Length, "Appearance layout lost existing theme choices.");
            var label = (RadioButton)window.FindName("ThemeDark");
            Require(label.Foreground is System.Windows.Media.SolidColorBrush brush && brush.Color == ((System.Windows.Media.SolidColorBrush)window.Resources["TextBrush"]).Color, "Mode labels lost readable theme foreground.");
        }
        ((RadioButton)window.FindName("ThemeDark")).IsChecked = true;
        Require(fixture.Settings.ThemeMode == "dark", "Dark mode no longer reaches real preferences.");
        ((RadioButton)window.FindName("ThemeLight")).IsChecked = true;
        Require(fixture.Settings.ThemeMode == "light", "Light mode no longer reaches real preferences.");
        list.SelectedItem = list.Items.Cast<SettingsWindow.ThemeRow>().Single(row => row.Id == "rose");
        Require(fixture.Settings.ThemeId == "rose" && ((TextBlock)window.FindName("CurrentThemeText")).Text.Contains("雾樱粉"), "Theme selection and its saved caption disagree.");
        Require(File.Exists(SettingsStore.FilePath) && window.FindName("EditBackgroundButton") is Button, "Appearance persistence or picture editing action was removed.");
        foreach (string mode in new[] { "light", "dark" })
        {
            ((RadioButton)window.FindName(mode == "light" ? "ThemeLight" : "ThemeDark")).IsChecked = true;
            window.Width = 760; window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            Directory.CreateDirectory("artifacts/appearance-layout");
            using var file = File.Create($"artifacts/appearance-layout/{mode}.png"); encoder.Save(file);
        }
    }
    internal static void FloatingContrast()
    {
        Directory.CreateDirectory("artifacts/light-controls");
        var light = ReferenceThemes.Resolve("rose", "light");
        foreach (bool hide in new[] { false, true })
        foreach (var background in new[] { System.Windows.Media.Colors.Black, System.Windows.Media.Colors.White })
        {
            var mouse = new DeviceInfo { Kind = DeviceKind.Mouse, Level = 99 };
            var keyboard = new DeviceInfo { Kind = DeviceKind.Keyboard, Level = 54, IsStale = true };
            var icon = new MouseDock.Controls.ReferenceBatteryIcon { StyleName = "plain", Width = 160, Height = 130, HideBackground = hide, DeviceModels = new[] { mouse, keyboard } };
            icon.ApplyPalette(light, new AppSettings { IconFollowTheme = true });
            var canvas = new Grid { Width = 160, Height = 130, Background = new System.Windows.Media.SolidColorBrush(background) };
            canvas.Children.Add(icon);
            canvas.Measure(new Size(160, 130)); canvas.Arrange(new Rect(0, 0, 160, 130)); canvas.UpdateLayout();
            System.Windows.Media.Imaging.RenderTargetBitmap Snapshot()
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(160, 130, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(canvas); return bitmap;
            }
            var pixels = new byte[160 * 130 * 4]; Snapshot().CopyPixels(pixels, 160 * 4, 0);
            int Visible(int y0, bool bright)
            {
                int count = 0;
                for (int y = y0; y < y0 + 37; y++) for (int x = 60; x < 142; x++)
                {
                    int i = (y * 160 + x) * 4;
                    if (bright ? pixels[i] > 180 && pixels[i+1] > 180 && pixels[i+2] > 180 : pixels[i] < 80 && pixels[i+1] < 80 && pixels[i+2] < 80) count++;
                }
                return count;
            }
            Require(Visible(16, background == System.Windows.Media.Colors.Black) > 40,
                "Live floating digits vanish on a light/dark desktop.");
            long Difference(byte[] image)
            {
                long total = 0;
                for (int y = 76; y < 113; y++) for (int x = 60; x < 142; x++)
                {
                    int offset = (y * 160 + x) * 4;
                    total += Math.Abs(image[offset] - background.B) + Math.Abs(image[offset+1] - background.G) + Math.Abs(image[offset+2] - background.R);
                }
                return total;
            }
            keyboard.IsStale = false; icon.InvalidateVisual(); canvas.UpdateLayout();
            var live = new byte[pixels.Length]; Snapshot().CopyPixels(live, 160 * 4, 0);
            double faded = Difference(pixels) / (double)Math.Max(1, Difference(live));
            Require(faded > .40 && faded < .50, "Sleeping readings must retain their original 45% fade rather than a new label or boosted opacity.");
            keyboard.IsStale = true; icon.InvalidateVisual(); canvas.UpdateLayout();
            Require(mouse.Level == 99 && keyboard.Level == 54 && keyboard.IsStale && !keyboard.IsChargingActive, "Readability changed battery evidence.");
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(Snapshot()));
            using (var file = File.Create($"artifacts/light-controls/floating-{background.R}-{hide}.png")) encoder.Save(file);
            mouse.Level = -1; icon.InvalidateVisual(); canvas.UpdateLayout();
            var unknown = new byte[pixels.Length]; Snapshot().CopyPixels(unknown, 160 * 4, 0);
            Require(!pixels.SequenceEqual(unknown) && mouse.LevelText == "—", "Unknown reading was replaced with a made-up percentage.");
        }
    }
    internal static void SettingsContrast()
    {
        using var fixture = new AppearanceFixture();
        var window = new SettingsWindow { Left = -2000, ShowActivated = false };
        fixture.Set("SettingsView", window);
        window.Show(); window.SelectSection("behavior");
        var visibleRoot = (UIElement)window.Content;
        visibleRoot.BeginAnimation(UIElement.OpacityProperty, null); visibleRoot.Opacity = 1;
        var custom = ReferenceThemes.Resolve("resident", "light").Clone();
        custom.Accent = System.Windows.Media.Colors.White;
        var themes = new[] { ReferenceThemes.Resolve("rose", "light"), ReferenceThemes.Resolve("resident", "light"), ThemePreset.ById("cloud"), ReferenceThemes.Resolve("resident", "dark"), custom };
        static double Ratio(System.Windows.Media.Color a, System.Windows.Media.Color b)
        {
            static double L(System.Windows.Media.Color c)
            {
                static double S(byte b) { double v = b / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
                return .2126 * S(c.R) + .7152 * S(c.G) + .0722 * S(c.B);
            }
            return (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
        }
        foreach (var theme in themes)
        {
            fixture.Set("Theme", theme);
            window.ApplyTheme(theme, false); window.UpdateLayout();
            var checkbox = (CheckBox)window.FindName("TopmostBox"); checkbox.ApplyTemplate();
            var box = (Border)checkbox.Template.FindName("Box", checkbox);
            var slider = (Slider)window.FindName("OpacitySlider"); slider.ApplyTemplate();
            var rail = (Border)slider.Template.FindName("SliderRail", slider);
            var outline = ((System.Windows.Media.SolidColorBrush)box.BorderBrush).Color;
            var track = ((System.Windows.Media.SolidColorBrush)rail.Background).Color;
            var card = ((System.Windows.Media.SolidColorBrush)window.Resources["CardBrush"]).Color;
            var painted = Palette.Mix(theme.BgTop, System.Windows.Media.Color.FromRgb(card.R, card.G, card.B), card.A / 255d);
            Require(outline.A == 255 && Ratio(outline, painted) >= 3.19 && Ratio(track, painted) >= 3.19,
                $"{theme.Id}: checkbox boundary or slider rail disappears into its actual card background.");
            checkbox.IsChecked = true; window.UpdateLayout();
            var fill = ((System.Windows.Media.SolidColorBrush)box.Background).Color;
            var tick = (System.Windows.Shapes.Path)checkbox.Template.FindName("Tick", checkbox);
            Require(tick.Opacity == 1 && Ratio(fill, painted) >= 3.19 && Ratio(fill, ((System.Windows.Media.SolidColorBrush)tick.Stroke).Color) >= 4.5,
                "Checked state or check mark is not readable.");
            checkbox.IsChecked = false;
            Require(tick.Opacity == 0 && Equals(box.BorderBrush, window.Resources["ControlOutlineBrush"]), "Unchecking did not restore the theme boundary.");
            slider.Value = .8;
            Require(Math.Abs(fixture.Settings.Opacity - .8) < .001, "The visible slider no longer applies its value.");
            if (theme.Id == "rose")
            {
                checkbox.IsChecked = true;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                var root = (FrameworkElement)window.Content;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                Directory.CreateDirectory("artifacts/light-controls");
                using var output = File.Create("artifacts/light-controls/rose-behavior.png"); encoder.Save(output);
            }
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);
    internal static int RunDragFixture(bool settings = false)
    {
        using var fixture = new AppearanceFixture();
        fixture.Battery.VisibleDevices[0].Note = "RX=synthetic-private-diagnostic";
        Window window = settings ? new SettingsWindow() : new DockWindow();
        window.Title = settings ? "MouseDock settings drag verification" : "MouseDock drag verification";
        window.Left = 240; window.Top = 160; window.ShowInTaskbar = true;
        fixture.Set(settings ? "SettingsView" : "Dock", window);
        if (window is DockWindow dock) dock.ApplyTheme(MouseDock.App.Theme, false);
        else ((SettingsWindow)window).ApplyTheme(MouseDock.App.Theme, false);
        window.Show();
        string result = Path.Combine(Environment.GetEnvironmentVariable("MOUSEDOCK_DATA_DIR")!, settings ? "native-settings-drag.txt" : "native-drag.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(result)!);
        File.WriteAllText(result, $"start {window.Left},{window.Top}\n");
        window.LocationChanged += (_, _) => File.AppendAllText(result, $"move {window.Left},{window.Top}\n");
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        SetWindowLong(handle, -20, (GetWindowLong(handle, -20) & ~0x80) | 0x40000);
        window.Closed += (_, _) => System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
        System.Windows.Threading.Dispatcher.Run();
        return 0;
    }
    internal static void CardGestures()
    {
        using var fixture = new AppearanceFixture();
        var device = fixture.Battery.VisibleDevices[0];
        device.Note = "MHUB4D/0900 connectStatus RX=4D0101100009000373826400";
        var window = new DockWindow { Left = -2000, ShowActivated = false };
        fixture.Set("Dock", window);
        window.Show(); window.UpdateLayout();
        var row = Descendants<Border>((DependencyObject)window.Content).Single(b => b.Name == "Row");
        var detail = (Border)window.FindName("DetailPanel");
        var pin = (Button)window.FindName("PinButton");
        Require(pin.FocusVisualStyle == null && pin.Content is Viewbox, "Card actions must use vector icons and subtle custom focus feedback.");
        foreach (string style in new[] { "standard", "compact", "bare" })
        {
            window.ApplyDensity(style); window.UpdateLayout();
            Require(Equals(row.ToolTip, device.Name), "A card tooltip exposed raw diagnostics.");
            Require(window.BeginCardGesture(row, new Point(10, 10)), "Device rows must be draggable.");
            Require(!window.TryStartCardDrag(new Point(11, 10)), "A small click movement started a drag.");
            window.CompleteCardGesture();
            Require(detail.Visibility == Visibility.Visible && ReferenceEquals(detail.DataContext, device), "Click no longer opens device details.");
            Require(!Descendants<TextBlock>(detail).Any(t => t.Text.Contains("RX=") || t.Text.Contains("connectStatus")), "Device details exposed raw protocol bytes.");
            detail.Visibility = Visibility.Collapsed;
            window.BeginCardGesture(row, new Point(10, 10));
            Require(window.TryStartCardDrag(new Point(40, 10)), "Crossing the drag threshold was ignored.");
            window.CompleteCardGesture();
            Require(detail.Visibility == Visibility.Collapsed, "Dragging accidentally opened device details.");
            Require(!window.BeginCardGesture((Button)window.FindName("PinButton"), new Point(10, 10)), "A toolbar button was taken over by dragging.");
            Require(!window.TryStartCardDrag(new Point(40, 40)), "Canceled button gestures retained an old drag.");
        }
        Require(device.Note.Contains("RX="), "UI cleanup deleted diagnostic evidence.");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    internal static void Navigation()
    {
        using var fixture = new AppearanceFixture();
        var window = new SettingsWindow { ShowActivated = false, Left = -2000 };
        fixture.Set("SettingsView", window);
        window.Show();
        var panels = new[] { "Display", "Devices", "Appearance", "Behavior", "Battery", "Help" };
        foreach (string panel in panels)
        {
            window.SelectSection(panel.ToLowerInvariant());
            window.UpdateLayout();
            Require(panels.Count(name => ((FrameworkElement)window.FindName("Page" + name)).Visibility == Visibility.Visible) == 1,
                "Settings navigation must show exactly one category.");
            Require(((FrameworkElement)window.FindName("Page" + panel)).IsVisible, "Selected settings page is inaccessible.");
        }
        window.SelectSection("display");
        Require(window.FindName("DockDensityChoice") == null, "Card density must no longer appear in settings.");
        window.SelectSection("devices");
        Require(window.FindName("DeviceChecks") != null && window.FindName("TrayHoverCheck") != null,
            "Existing device or independent tray controls were lost.");
    }

    internal static void TrayDeviceEditing()
    {
        using var fixture = new AppearanceFixture();
        fixture.Battery.Devices.Add(new DeviceInfo { Id = "mouse", Name = "Mouse", Selected = true });
        fixture.Battery.Devices.Add(new DeviceInfo { Id = "keyboard", Name = "Keyboard", Selected = true });
        var window = new SettingsWindow { ShowActivated = false, Left = -2000 };
        fixture.Set("SettingsView", window);
        window.Show();
        window.SelectSection("devices");
        var rows = (StackPanel)window.FindName("TrayDeviceRows");
        var row = (CheckBox)rows.Children[0];
        var follow = (CheckBox)window.FindName("TrayFollowMainCheck");
        var multi = (CheckBox)window.FindName("MultiTrayCheck");
        var click = typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Require(row.IsEnabled && row.IsChecked == true && follow.IsChecked == true && multi.IsChecked == false,
            "A device must be directly editable while following the main panel.");
        click.Invoke(row, null);
        var saved = SettingsStore.Load();
        Require(row.IsChecked == false && follow.IsChecked == false && multi.IsChecked == true &&
            saved.MultiTray && saved.TrayDeviceIds!.SequenceEqual(new[] { "keyboard" }),
            "Direct editing must switch to explicit tray selection and preserve the other selected devices.");
        click.Invoke(follow, null);
        Require(SettingsStore.Load().TrayDeviceIds == null && ((CheckBox)rows.Children[0]).IsChecked == true,
            "Resuming follow must restore the main panel preview.");
        File.Delete(SettingsStore.FilePath);
        Directory.Delete(SettingsStore.Dir);
    }

    internal static void SaveFailure()
    {
        using var fixture = new AppearanceFixture();
        var window = new SettingsWindow { ShowActivated = false, Left = -2000 };
        fixture.Set("SettingsView", window);
        window.Show();
        MouseDock.App.SaveSettings();
        var label = (TextBlock)window.FindName("SaveStateText");
        Require(label.Text.StartsWith("已保存") && File.Exists(SettingsStore.FilePath), "Save success was not tied to a real file.");
        var directory = typeof(SettingsStore).GetProperty("TestDirectory", BindingFlags.NonPublic | BindingFlags.Static)!;
        string original = (string)directory.GetValue(null)!;
        string blocked = Path.Combine(original, "file-as-directory");
        File.WriteAllText(blocked, "fixture");
        try
        {
            directory.SetValue(null, blocked);
            MouseDock.App.SaveSettings();
            Require(label.Text.StartsWith("保存失败"), "Failed persistence must not claim settings were saved.");
        }
        finally { directory.SetValue(null, original); }
    }

    internal static void IsolatedAutoStart()
    {
        const string variable = "MOUSEDOCK_DATA_DIR";
        string? previous = Environment.GetEnvironmentVariable(variable);
        using var fixture = new AppearanceFixture();
        string directory = SettingsStore.Dir;
        fixture.Settings.AutoStart = false;
        Environment.SetEnvironmentVariable(variable, directory);
        try
        {
            var window = new SettingsWindow { ShowActivated = false, Left = -2000 };
            fixture.Set("SettingsView", window);
            window.Show();
            window.SelectSection("behavior");
            var check = (CheckBox)window.FindName("AutoStartCheck");
            var click = typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Require(check.IsVisible && check.IsEnabled && check.IsChecked == false, "The isolated startup control must start visible, enabled and unchecked.");
            click.Invoke(check, null);
            Require(check.IsChecked == true && SettingsStore.Load().AutoStart, "Checking startup must persist the isolated preference.");
            window.Reload();
            Require(check.IsChecked == true, "Reload must display the isolated startup preference.");
            click.Invoke(check, null);
            Require(check.IsChecked == false && !SettingsStore.Load().AutoStart, "Unchecking startup must clear the isolated preference.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
            if (Directory.Exists(directory))
            {
                File.Delete(Path.Combine(directory, "settings.json"));
                Directory.Delete(directory);
            }
        }
    }

    internal static void TrayChoices()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TraySurfacePicker")!;
        string selected = "";
        using var picker = (Forms.ToolStripControlHost)Activator.CreateInstance(type,
            BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { new Action<string>(value => selected = value) }, null)!;
        using var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(picker);
        var buttons = picker.Control.Controls.OfType<Forms.Button>().ToArray();
        Require(buttons.Length == 6 && buttons.All(b => b.TabStop), "Six native quick choices must support keyboard focus.");
        foreach (var button in buttons)
        {
            string mode = (string)button.Tag!;
            type.GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(picker, [ThemePreset.ById("resident"), mode]);
            Require(buttons.Count(b => b.Text.StartsWith("✓")) == 1 && button.Text.StartsWith("✓"), "Current display mode is not unique.");
            button.PerformClick();
            Require(selected == mode, "A quick choice did not dispatch its exact display mode.");
        }
    }

    internal static void BatteryStates()
    {
        using var fixture = new AppearanceFixture();
        var device = fixture.Battery.VisibleDevices[0];
        var window = new DockWindow { ShowActivated = false, Left = -2000 };
        fixture.Set("Dock", window);
        window.Show();
        double standard = ((FrameworkElement)window.Content).ActualWidth;
        window.ApplyDensity("compact"); window.UpdateLayout();
        Require(((FrameworkElement)window.Content).ActualWidth < standard, "Compact mode did not reduce the resident footprint.");
        device.IsStale = true;
        Require(device.StatusText.Contains("已过期") && !device.IsChargingActive, "Stale state must be explicit and stop charging effects.");
        device.Level = -1;
        Require(device.LevelText == "—", "Unknown battery is being rendered as a measured value.");
        window.ApplyDensity("bare"); window.UpdateLayout();
        Require(window.Foreground != null && ((FrameworkElement)window.Content).ActualWidth <= 320, "Bare mode lost readability or compact sizing.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
