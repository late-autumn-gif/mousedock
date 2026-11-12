using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
using MouseDock.Views;

internal static class UiRegressionChecks
{
    internal static void DockDpi()
    {
        using var battery = Fixtures(start: false);
        var mouse = battery.Devices.Single(d => d.Id == "mouse");
        mouse.DpiSettings = new MouseDpiState(0, 1, 2, false, [800, 1600, 0, 0, 0, 0]);
        mouse.Dpi = 1600;
        battery.SaveSelection();
        var window = new DockWindow { Left = -2000, ShowActivated = false };
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            var list = (ItemsControl)window.FindName("DeviceList");
            var buttons = Descendants<Button>(list).Where(b => b.IsVisible).ToArray();
            Require(buttons.Length == 1 && Equals(buttons[0].Content, "1600 DPI"),
                "Only the mouse with verified DPI settings should show the DPI selector.");
            mouse.DpiSettings = new MouseDpiState(0, 0, 1, false, [1600], MouseDpiProtocol.Hidpp, "Logitech G603", Writable: false);
            window.UpdateLayout();
            Require(!buttons[0].IsVisible && Descendants<TextBlock>(list).Count(t => t.IsVisible && t.Text == "1600 DPI") == 1,
                "Read-only Logitech DPI must stay visible without a selector.");
            mouse.Dpi = -1;
            mouse.DpiSettings = null;
            window.UpdateLayout();
            Require(!buttons[0].IsVisible && Descendants<TextBlock>(list).Count(t => t.IsVisible && t.Text == "DPI —") == 1,
                "An unreadable mouse DPI should show a single unknown label and hide the selector.");
        }
        finally { window.Close(); }
    }

    internal static void SelectedDevicesOnly(string surface, bool empty)
    {
        using var battery = Fixtures();
        Window window = surface switch
        {
            "dock" => new DockWindow(),
            "mini" => new MiniWindow(),
            _ => new IconWindow()
        };
        try
        {
            if (empty)
                foreach (var device in battery.Devices) device.Selected = false;
            if (window is MiniWindow mini) mini.Rebuild();
            if (window is IconWindow icon) icon.UpdateValue();
            // Dock also needs to refresh when the selection changes.
            if (window is DockWindow dock)
                typeof(DockWindow).GetMethod("RefreshDevices")?.Invoke(dock, null);
            var list = (ItemsControl)window.FindName(surface == "mini" ? "CellList" : "DeviceList");
            var ids = list.Items.Cast<DeviceInfo>().Select(d => d.Id).ToArray();
            Require(ids.SequenceEqual(empty ? [] : new[] { "mouse", "keyboard" }),
                $"{surface} displayed [{string.Join(", ", ids)}] instead of the selection.");
        }
        finally { window.Close(); }
    }

    internal static void ThemeForeground(string surface, bool edge = false)
    {
        using var battery = Fixtures();
        if (edge) MouseDock.App.Settings.SurfaceMode = "edge";
        Window window = surface switch
        {
            "dock" => new DockWindow(),
            "mini" => new MiniWindow(),
            "settings" => new SettingsWindow(),
            _ => new IconWindow()
        };
        try
        {
            if (window is SettingsWindow settings)
            {
                settings.Reload();
                // These checks render controls without persisting test selections to the user's settings.
                typeof(SettingsWindow).GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(settings, true);
                settings.Loaded += (_, _) => typeof(SettingsWindow)
                    .GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(settings, true);
            }
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Left = -2000;
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            window.BeginAnimation(UIElement.OpacityProperty, null);
            window.Opacity = 1;
            if (window.FindName("Root") is UIElement animatedRoot)
            {
                animatedRoot.BeginAnimation(UIElement.OpacityProperty, null);
                animatedRoot.Opacity = 1;
            }
            if (window is IconWindow previewIcon) previewIcon.ApplyStyle("tile", false);
            foreach (var theme in new[] { ThemePreset.Presets[1], ThemePreset.Presets[0], ThemePreset.Presets[1] })
            {
                window.GetType().GetMethod("ApplyTheme")!.Invoke(window, [theme, false]);
                window.UpdateLayout();
                if (surface == "settings")
                {
                    var themes = (ListBox)window.FindName("ThemeList");
                    var checks = (ItemsControl)window.FindName("DeviceChecks");
                    Require(ColorOf(themes.Foreground) == theme.Text, "Theme names use the wrong foreground.");
                    var boxes = Descendants<CheckBox>(checks).ToArray();
                    Require(boxes.Length >= 2, "Device checkboxes were not realized.");
                    Require(boxes.All(box => ColorOf(box.Foreground) == theme.Text),
                        "Device labels do not follow the theme.");
                    var firstSurface = (Button)window.FindName("SurfaceDock");
                    var modes = (Panel)firstSurface.Parent;
                    var modeButtons = modes.Children.OfType<Button>().ToArray();
                    Point Offset(Button button) => button.TranslatePoint(new Point(), modes);
                    Require(modeButtons.Length == 6 && modeButtons.All(button => button.ActualHeight >= 36),
                        "Display mode buttons need six visible, comfortably sized targets.");
                    Require(Offset(modeButtons[1]).X - Offset(firstSurface).X - firstSurface.ActualWidth >= 7 &&
                        Offset(modeButtons[3]).Y - Offset(firstSurface).Y - firstSurface.ActualHeight >= 7,
                        "Display mode buttons need clear horizontal and vertical gaps.");
                    if (edge)
                    {
                        var options = (FrameworkElement)window.FindName("EdgeOptionsPanel");
                        Require(options.IsVisible && options.ActualHeight > 0, "Edge settings are hidden.");
                        foreach (var label in Descendants<TextBlock>(options))
                            Require(label.ActualWidth + label.Margin.Left + label.Margin.Right >= label.DesiredSize.Width,
                                $"Edge settings text clipped: {label.Text}, actual={label.ActualWidth}, desired={label.DesiredSize.Width}.");
                    }
                }
                else if (surface == "icon")
                {
                    foreach (string name in new[] { "MiniText", "RingText", "PillText", "BatteryText", "PlainText" })
                        Require(ColorOf(((TextBlock)window.FindName(name)).Foreground) == theme.Text,
                            $"{name} does not follow the theme.");
                    ((IconWindow)window).UpdateValue();
                    var multi = (StackPanel)window.FindName("MultiPanel");
                    Require(multi.Children.OfType<TextBlock>().All(t => ColorOf(t.Foreground) == theme.Text),
                        "Polling restored the old text color.");
                    Require(((GradientStop)window.FindName("PillTop")).Color == theme.BgTop,
                        "Pill background must follow its text theme.");
                }
                else
                {
                    var list = (ItemsControl)window.FindName(surface == "mini" ? "CellList" : "DeviceList");
                    Require(ColorOf(list.Foreground) == theme.Text, $"{surface} list resets inherited text color.");
                    Require(Descendants<TextBlock>(list).Where(t => t.Text == "Mouse fixture")
                        .All(t => ColorOf(t.Foreground) == theme.Text), "Device name has the wrong color.");
                }
                Capture((FrameworkElement)window.Content, $"{surface}{(edge ? "-edge" : "")}-{theme.Id}");
                if (surface == "settings")
                {
                    ((FrameworkElement)((Button)window.FindName("SurfaceDock")).Parent).BringIntoView();
                    window.UpdateLayout();
                    Capture((FrameworkElement)window.Content, $"settings-display-modes-{theme.Id}");
                    ((ScrollViewer)window.FindName("RootScroll")).ScrollToTop();
                    window.UpdateLayout();
                }
            }
        }
        finally { window.Close(); }
    }

    internal static void ExclusiveSurfaces()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.SurfaceVisibility");
        Require(type != null, "Missing shared single-surface visibility policy.");
        var windows = Enumerable.Range(0, 6).Select(_ => new Window
        {
            Width = 10, Height = 10, ShowInTaskbar = false, ShowActivated = false, Left = -1000, Top = -1000
        }).ToArray();
        try
        {
            var names = new[] { "dock", "mini", "icon", "taskbar", "edge", "pet" };
            var controller = Activator.CreateInstance(type!, windows.Cast<object>().ToArray())!;
            var apply = type!.GetMethod("Apply")!;
            for (int cycle = 0; cycle < 3; cycle++)
                for (int i = 0; i < names.Length; i++)
                {
                    foreach (var window in windows) window.Show();
                    apply.Invoke(controller, [names[i], true]);
                    Require(windows.Count(w => w.IsVisible) == 1 && windows[i].IsVisible,
                        "Showing a surface must hide all others, including already-visible old windows.");
                    apply.Invoke(controller, [names[i], false]);
                    Require(windows.All(w => !w.IsVisible), "Toggling off must leave no surface visible.");
                    apply.Invoke(controller, [names[i], true]);
                    apply.Invoke(controller, [names[i], true]);
                    Require(windows.Count(w => w.IsVisible) == 1 && windows[i].IsVisible,
                        "Repeated wake events must keep the chosen surface.");
                }
        }
        finally { foreach (var window in windows) window.Close(); }
    }

    internal static void AppWakeUsesSavedMode()
    {
        using var battery = Fixtures();
        var windows = new Window[] { new DockWindow(), new MiniWindow(), new IconWindow(), new TaskbarWindow() };
        var names = new[] { "Dock", "Mini", "Icon", "Taskbar" };
        try
        {
            for (int i = 0; i < names.Length; i++)
            {
                typeof(MouseDock.App).GetProperty(names[i])!.SetValue(null, windows[i]);
                windows[i].ShowActivated = false;
                windows[i].Left = -2000;
            }
            foreach (string mode in new[] { "dock", "mini", "icon", "taskbar" })
            {
                MouseDock.App.Settings.SurfaceMode = mode;
                foreach (var window in windows) window.Show();
                for (int click = 0; click < 3; click++) MouseDock.App.ShowCurrentSurface();
                Require(MouseDock.App.Settings.SurfaceMode == mode, "Waking the app changed the chosen mode.");
                Require(windows.Count(w => w.IsVisible) == 1, "The app wake entry point displayed multiple surfaces.");
                Require(windows[Array.IndexOf(names, mode switch { "dock" => "Dock", "mini" => "Mini", "icon" => "Icon", _ => "Taskbar" })].IsVisible,
                    "The visible surface does not match the saved mode.");
            }
        }
        finally
        {
            for (int i = 0; i < names.Length; i++)
            {
                windows[i].Close();
                typeof(MouseDock.App).GetProperty(names[i])!.SetValue(null, null);
            }
        }
    }

    internal static void PinnedTrayClick()
    {
        using var settingsGuard = File.Exists(SettingsStore.FilePath)
            ? File.Open(SettingsStore.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
        using var battery = Fixtures();
        var window = new TaskbarWindow();
        typeof(MouseDock.App).GetProperty("Taskbar")!.SetValue(null, window);
        MouseDock.App.Settings.SurfaceMode = "taskbar";
        try
        {
            window.Show();
            for (int i = 0; i < 3; i++)
            {
                MouseDock.App.ToggleCurrentSurface();
                Require(window.IsVisible, "A tray click hid the pinned battery display.");
            }
        }
        finally
        {
            window.Close();
            typeof(MouseDock.App).GetProperty("Taskbar")!.SetValue(null, null);
        }
    }

    private static void Capture(FrameworkElement root, string name)
    {
        if (root.ActualWidth <= 0 || root.ActualHeight <= 0) return;
        var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 1.5),
            (int)Math.Ceiling(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        image.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory("artifacts");
        using var file = File.Create(Path.Combine("artifacts", $"ui-regression-{name}.png"));
        encoder.Save(file);
    }

    private static BatteryService Fixtures(bool start = true)
    {
        var settings = new AppSettings { Provider = "json", JsonPath = "nonexistent-ui-fixture.json" };
        var battery = new BatteryService(settings, null, start);
        typeof(MouseDock.App).GetProperty("Settings")!.SetValue(null, settings);
        typeof(MouseDock.App).GetProperty("Battery")!.SetValue(null, battery);
        battery.Devices.Add(new DeviceInfo { Id = "hidden", Name = "Not selected", Selected = false });
        battery.Devices.Add(new DeviceInfo { Id = "mouse", Name = "Mouse fixture", Kind = DeviceKind.Mouse, Level = 75 });
        battery.Devices.Add(new DeviceInfo { Id = "keyboard", Name = "Keyboard fixture", Kind = DeviceKind.Keyboard, Level = 60 });
        battery.Devices.Add(new DeviceInfo { Id = "offline", Name = "Offline", Connected = false });
        return battery;
    }

    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
