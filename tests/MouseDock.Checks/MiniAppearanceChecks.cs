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
            input.Text = "#FFFFFF";
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

    private static Color Top(Border root) => ((LinearGradientBrush)root.Background).GradientStops[0].Color;

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject =>
        node is T item ? [item] : LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>().SelectMany(Descendants<T>);

    private static IEnumerable<Button> Buttons(DependencyObject node) => Descendants<Button>(node);

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
