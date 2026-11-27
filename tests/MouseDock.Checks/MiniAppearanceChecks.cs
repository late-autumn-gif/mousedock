using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;
using App = MouseDock.App;

internal static class MiniAppearanceChecks
{
    internal static void Run()
    {
        using var fixture = new AppearanceFixture();
        var settings = fixture.Settings;
        settings.SurfaceMode = "mini";
        var mini = new MiniWindow { ShowActivated = false };
        fixture.Set("Mini", mini);
        mini.Show();
        var view = new SettingsWindow { ShowActivated = false };
        fixture.Set("SettingsView", view);
        view.Reload();
        view.SelectSection("appearance");
        view.Show();

        var root = (Border)mini.FindName("Root");
        var follow = (CheckBox)view.FindName("MiniFollowThemeBackgroundBox");
        Require(follow.IsChecked == true, "New profiles should follow the theme.");
        Require(new AppSettings { MiniBackground = "existing.png" }.FollowsMiniThemeBackground == false,
            "Legacy profiles should keep their independent picture.");

        var theme = ThemePreset.ById("cloud");
        fixture.Set("Theme", theme);
        view.ApplyTheme(theme, false);
        mini.RefreshBackground();
        Require(Top(root) == theme.BgTop, "Mini background did not update with the theme.");
        var menu = App.CreateQuickMenu(mini);
        var items = menu.Items.OfType<MenuItem>().ToArray();
        var menuFollow = items.Single(item => Equals(item.Header, "背景色跟随主题"));
        Require(menuFollow.IsChecked, "Mini menu state disagrees with Settings.");
        if (!SystemParameters.HighContrast)
            Require(((SolidColorBrush)menu.Resources["PetMenuSurface"]).Color.R == theme.BgTop.R,
                "Mini quick menu does not use the theme background.");

        menu.IsOpen = true;
        try
        {
            TaskbarEmbeddingChecks.Pump(100);
            Capture(menu, "mini-quick-menu");
        }
        finally { menu.IsOpen = false; }

        var menuEdit = items.Single(item => Equals(item.Header, "迷你窗配色…"));
        Edit(mini, () => menuEdit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), (editor, panel) =>
        {
            Capture((FrameworkElement)editor.Content, "mini-color-palette");
            var input = Descendants<TextBox>(panel).Single();
            var choices = Descendants<RadioButton>(panel).ToArray();
            input.Text = "#202B29";
            choices[1].IsChecked = true;
            var white = PickPreset(panel, "#FFFFFF");
            Require(input.Text == "#FFFFFF" && editor.Draft["text"] == "#FFFFFF" &&
                editor.Draft["background"] == "#202B29", "A preset did not update only the active field.");
            Require(System.Windows.Automation.AutomationProperties.GetItemStatus(white) == "当前颜色",
                "The selected preset did not expose its current state.");
            choices[2].IsChecked = true;
            var wheel = Descendants<Canvas>(panel).Single();
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(wheel), 0, Key.Up)
                { RoutedEvent = Keyboard.KeyDownEvent };
            wheel.RaiseEvent(key);
            Require(key.Handled && editor.Draft.ContainsKey("accent"), "Keyboard did not adjust the color wheel.");
            var brightness = Descendants<Slider>(panel).Single();
            brightness.Value = .25;
            Require(input.Text == editor.Draft["accent"], "Brightness and HEX are out of sync.");
            input.Text = "#A6F5BF";
            Press(panel, "完成");
        });
        Require(follow.IsChecked == false && Top(root) == ThemePreset.C("#202B29"), "Custom background was not applied.");
        Require(((SolidColorBrush)mini.Foreground).Color == Colors.White &&
            ((SolidColorBrush)mini.BatteryBrush).Color == ThemePreset.C("#A6F5BF"), "Custom foreground colors were not applied.");
        var saved = SettingsStore.Load();
        Require(!saved.FollowsMiniThemeBackground && saved.Clone().MiniColors["accent"] == "#A6F5BF",
            "Mini appearance did not survive the settings round trip.");

        var imagePath = Path.GetFullPath("artifacts/mini-theme-image.png");
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 255, 255, 255 }, 4);
        Save(bitmap, imagePath);
        settings.CustomBackground = imagePath;
        follow.IsChecked = true;
        follow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        view.ApplyTheme(theme, false);
        Require(Top(root) == theme.BgTop && SettingsStore.Load().FollowsMiniThemeBackground,
            "Settings picture changed the mini window while following the theme.");
        Require(((Border)view.FindName("Root")).Background is DrawingBrush, "Settings did not display its own picture.");
        Require(((SolidColorBrush)mini.BatteryBrush).Color == ThemePreset.C("#A6F5BF"),
            "Background follow should preserve battery colors.");

        var settingsEdit = LogicalTreeHelper.GetChildren(view.FindName("PageAppearance") as DependencyObject)
            .OfType<DependencyObject>().SelectMany(Buttons).Single(button => Equals(button.Tag, "mini"));
        follow.BringIntoView();
        TaskbarEmbeddingChecks.Pump(100);
        Capture((FrameworkElement)view.Content, "mini-settings");
        Edit(view, () => settingsEdit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), (_, panel) =>
        {
            Press(panel, "恢复默认");
            var cancel = Buttons(panel).Single(button => Equals(button.Content, "取消"));
            ((IInvokeProvider)new ButtonAutomationPeer(cancel).GetPattern(PatternInterface.Invoke)).Invoke();
        });
        Require(settings.MiniColors.Count == 3, "Cancel changed saved colors.");
        Edit(view, () => settingsEdit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), (_, panel) =>
        {
            Press(panel, "恢复默认");
            Press(panel, "完成");
        });
        Require(SettingsStore.Load().MiniColors.Count == 0 && follow.IsChecked == true, "Reset did not restore the default palette.");
        view.SelectSection("battery");
        var legacyLow = new AppSettings { ThemeColors = new() { ["low"] = "#EA6715" } };
        Require(AppearanceColors.LowBatteryOverride(legacyLow) == "#EA6715", "The old theme low-battery color was lost.");
        var low = (TextBlock)view.FindName("LowBatteryColorLabel");
        var lowButton = Descendants<Button>((DependencyObject)view.FindName("PageBattery"))
            .Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "编辑低电量颜色");
        Edit(view, () => lowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), (editor, panel) =>
        {
            Capture((FrameworkElement)editor.Content, "low-battery-palette");
            Descendants<TextBox>(panel).Single().Text = "#C97816";
            Press(panel, "完成");
        });
        Require(low.Text == "#C97816" && SettingsStore.Load().LowBatteryColor == "#C97816",
            "The low-battery color did not persist in its own setting.");
        Require(((SolidColorBrush)AppearanceColors.BatteryState(Brushes.Black, 10, 20, false)).Color == ThemePreset.C("#C97816") &&
            AppearanceColors.BatteryState(Brushes.Black, -1, 20, false) == Brushes.Black,
            "Low-battery color leaked to unknown battery or was not applied.");
        Edit(view, () => lowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), (_, panel) =>
        {
            Press(panel, "恢复默认");
            Press(panel, "完成");
        });
        Require(low.Text == "默认颜色" && SettingsStore.Load().LowBatteryColor == "", "The low-battery color did not reset.");
    }

    internal static void ChargingColor()
    {
        using var fixture = new AppearanceFixture();
        var device = fixture.Battery.VisibleDevices.Single();
        device.Connected = true;
        device.Level = 16;
        device.Charging = true;
        var mini = new MiniWindow { ShowActivated = false };
        fixture.Set("Mini", mini);
        mini.Show();
        var view = new SettingsWindow { ShowActivated = false };
        fixture.Set("SettingsView", view);
        view.Reload();
        view.SelectSection("appearance");
        view.Show();

        var button = (Button)view.FindName("EditChargingColorButton");
        var label = (TextBlock)view.FindName("ChargingColorLabel");
        var swatch = (System.Windows.Shapes.Ellipse)view.FindName("ChargingColorSwatch");
        var normal = Brushes.White;
        var defaultCharging = AppearanceColors.BatteryState(normal, 16, 20, true);
        var defaultAlternative = AppearanceColors.BatteryState(defaultCharging, 16, 20, true);
        var originalKey = MouseDock.Controls.TaskbarBatteryStrip.AppearanceKey(fixture.Settings, App.Theme);
        Require(label.Text == "默认颜色" && AppearanceColors.DefaultChargingColor == ((SolidColorBrush)defaultCharging).Color &&
            defaultCharging != defaultAlternative, "Existing profiles lost the default charging colors.");

        void Open() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        const string hex = "#F97316";
        Edit(view, Open, (_, panel) =>
        {
            var input = Descendants<TextBox>(panel).Single();
            input.Text = "#xyz";
            Press(panel, "完成");
            PickPreset(panel, hex);
            var brightness = Descendants<Slider>(panel).Single();
            Require(input.Text == hex && Math.Abs(brightness.Value - 249 / 255d) < .001,
                "Preset did not recover invalid HEX and update brightness.");
            Press(panel, "完成");
        });
        var color = ThemePreset.C(hex);
        Require(label.Text == hex && ((SolidColorBrush)swatch.Fill).Color == color &&
            SettingsStore.Load().Clone().ChargingColor == hex, "The charging color did not display and persist.");
        Require(((SolidColorBrush)AppearanceColors.BatteryState(defaultCharging, 16, 20, true)).Color == color &&
            AppearanceColors.BatteryState(normal, 16, 20, false) != swatch.Fill &&
            AppearanceColors.BatteryState(normal, -1, 20, true) == normal &&
            AppearanceColors.BatteryState(normal, 100, 20, true, full: true) == normal,
            "Charging color did not respect full, unknown, and low-battery states.");
        Require(MouseDock.Controls.TaskbarBatteryStrip.AppearanceKey(fixture.Settings, App.Theme) != originalKey,
            "Taskbar and screen-edge visuals did not invalidate on a charging color change.");
        TaskbarEmbeddingChecks.Pump(100);
        Require(VisualText(mini, device.MiniText).Any(text => text.Foreground is SolidColorBrush ink && ink.Color == color),
            "The already-visible mini window did not update its charging number.");
        button.BringIntoView();
        TaskbarEmbeddingChecks.Pump(100);
        Capture((FrameworkElement)view.Content, "charging-color-settings");
        Edit(view, Open, (editor, panel) =>
        {
            Capture((FrameworkElement)editor.Content, "charging-color-editor");
            PickPreset(panel, "#38BDF8");
            Press(panel, "恢复默认");
            var cancel = Buttons(panel).Single(item => Equals(item.Content, "取消"));
            ((IInvokeProvider)new ButtonAutomationPeer(cancel).GetPattern(PatternInterface.Invoke)).Invoke();
        });
        Require(label.Text == hex && SettingsStore.Load().ChargingColor == hex, "Cancel changed the charging color.");
        Edit(view, Open, (_, panel) =>
        {
            Press(panel, "恢复默认");
            Press(panel, "完成");
        });
        Require(label.Text == "默认颜色" && SettingsStore.Load().ChargingColor == null &&
            AppearanceColors.BatteryState(normal, 16, 20, true) == defaultCharging, "Reset did not restore the default charging color.");
    }

    private static IEnumerable<TextBlock> VisualText(DependencyObject parent, string value)
    {
        if (parent is TextBlock text && text.Text == value) yield return text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in VisualText(VisualTreeHelper.GetChild(parent, i), value)) yield return child;
    }

    private static Color Top(Border root) => ((LinearGradientBrush)root.Background).GradientStops[0].Color;

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject =>
        node is T item ? [item] : LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>().SelectMany(Descendants<T>);

    private static IEnumerable<Button> Buttons(DependencyObject node) => Descendants<Button>(node);

    private static Button PickPreset(StackPanel panel, string hex)
    {
        var swatch = Buttons(panel).Single(button => Equals(button.Tag, hex));
        Require(swatch.Focusable && swatch.IsTabStop, "The preset is missing from keyboard navigation.");
        ((IInvokeProvider)new ButtonAutomationPeer(swatch).GetPattern(PatternInterface.Invoke)).Invoke();
        TaskbarEmbeddingChecks.Pump(50);
        return swatch;
    }

    private static void Edit(Window owner, Action open, Action<ColorEditorWindow, StackPanel> interact)
    {
        Exception? error = null;
        bool opened = false;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var editor = owner.OwnedWindows.OfType<ColorEditorWindow>().SingleOrDefault();
            if (editor == null) return;
            opened = true;
            try { interact(editor, (StackPanel)((ScrollViewer)editor.Content).Content); }
            catch (Exception ex) { error = ex; editor.Close(); }
        }));
        open();
        if (error != null) throw error;
        Require(opened, "The palette editor did not open.");
    }

    private static void Press(StackPanel panel, string choice) =>
        Buttons(panel).Single(button => Equals(button.Content, choice)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Capture(FrameworkElement element, string name)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);
        Save(bitmap, Path.Combine("artifacts", name + ".png"));
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
