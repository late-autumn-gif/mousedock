using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;
using App = MouseDock.App;

internal static class TaskbarThemeChecks
{
    internal static void RendererColors()
    {
        var constructor = typeof(TaskbarBatteryStrip).GetConstructor(
            [typeof(IEnumerable<DeviceInfo>), typeof(string), typeof(bool), typeof(bool), typeof(Brush),
                typeof(int), typeof(string), typeof(string), typeof(Brush)]);
        Require(constructor != null, "The taskbar renderer must accept the active theme accent.");
        foreach (string style in new[] { "ring", "panel" })
        foreach (bool followTheme in new[] { true, false })
        foreach (string custom in new[] { "", "#00AAFF", "invalid" })
        {
            var strip = (TaskbarBatteryStrip)constructor!.Invoke(
                [new[] { new DeviceInfo { Kind = DeviceKind.Keyboard, Level = 12 } }, style, true, false,
                    Brushes.WhiteSmoke, 20, "#FFCC00", custom, followTheme ? Brushes.Magenta : null]);
            Arrange(strip);
            var glyph = Children<DeviceGlyph>(strip).Single(g => g.Visibility == Visibility.Visible);
            var expected = custom == "#00AAFF" ? Color.FromRgb(0, 170, 255)
                : followTheme ? Colors.Magenta : style == "panel" ? Colors.White : Colors.WhiteSmoke;
            Require(ColorOf(glyph.GlyphBrush) == expected, $"{style} glyph lost theme or custom color precedence.");
            Require(ColorOf(Children<TextBlock>(strip).Single(t => t.Name == "BatteryNumber").Foreground)
                == Color.FromRgb(255, 204, 0), "Theme following must preserve the independent text override.");
            if (style != "ring") continue;
            var ring = Children<DeviceBatteryRing>(strip).Single();
            if (followTheme || custom == "#00AAFF")
                Require(ColorOf(ring.BatteryBrush) == expected, "The ring and device glyph must share the selected battery color.");
            else
                Require(ring.BatteryBrush == null, "Disabling theme following must restore live battery status colors.");
        }
    }

    internal static void LiveTheme()
    {
        var names = new[] { "Settings", "Battery", "Theme", "Taskbar", "SettingsView", "Dock", "Mini", "Icon", "HeadsetView" };
        var properties = names.Select(name => typeof(App).GetProperty(name)!).ToArray();
        var originals = properties.Select(property => property.GetValue(null)).ToArray();
        var directoryProperty = typeof(SettingsStore).GetProperty("TestDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        var originalDirectory = directoryProperty.GetValue(null);
        directoryProperty.SetValue(null, Path.GetFullPath(Path.Combine("artifacts", "taskbar-theme-settings")));
        var settings = new AppSettings { Provider = "json", JsonPath = "missing-taskbar-theme.json",
            IconFollowTheme = true, TaskbarTextColor = "#FFCC00" };
        using var battery = (BatteryService)Activator.CreateInstance(typeof(BatteryService),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [settings, null, false], null)!;
        battery.VisibleDevices.Add(new DeviceInfo { Id = "theme-keyboard", Kind = DeviceKind.Keyboard, Level = 80 });
        TaskbarWindow? taskbar = null;
        SettingsWindow? window = null;
        try
        {
            foreach (var property in properties.Where(property => property.Name is "Dock" or "Mini" or "Icon" or "HeadsetView"))
                property.SetValue(null, null);
            SetApp("Settings", settings);
            SetApp("Battery", battery);
            taskbar = new TaskbarWindow();
            window = new SettingsWindow();
            SetApp("Taskbar", taskbar);
            SetApp("SettingsView", window);
            window.Reload();
            foreach (string style in new[] { "ring", "panel" })
            {
                settings.TaskbarStyle = style;
                taskbar.UpdateDevices();
                foreach (var theme in new[] { ThemePreset.Presets[0], ThemePreset.Presets[1] })
                {
                    SetApp("Theme", theme);
                    App.ApplyTheme(false);
                    AssertTheme(taskbar, window, theme.Accent);
                }
            }
            var taskbarToggle = window.FindName("TaskbarFollowThemeBox") as CheckBox;
            Require(taskbarToggle != null, "Taskbar settings must expose theme following.");
            var iconToggle = (CheckBox)window.FindName("IconFollowThemeBox");
            taskbarToggle!.IsChecked = false;
            Require(!settings.IconFollowTheme && iconToggle.IsChecked == false,
                "The taskbar theme toggle must update the saved preference and floating icon control.");
            var strip = (TaskbarBatteryStrip)((Viewbox)taskbar.Content).Child;
            Arrange(strip);
            Require(ColorOf(Children<DeviceGlyph>(strip).Single(g => g.Visibility == Visibility.Visible).GlyphBrush)
                == Colors.White, "Turning theme following off must refresh the current panel immediately.");
            iconToggle.IsChecked = true;
            Require(settings.IconFollowTheme && taskbarToggle.IsChecked == true,
                "The floating icon theme toggle must synchronize the taskbar control.");
            AssertTheme(taskbar, window, App.Theme.Accent);

            settings.TaskbarBatteryColor = "#00AAFF";
            SetApp("Theme", ThemePreset.Presets[0]);
            App.ApplyTheme(false);
            AssertTheme(taskbar, window, Color.FromRgb(0, 170, 255));
            Require(settings.TaskbarTextColor == "#FFCC00" && settings.TaskbarBatteryColor == "#00AAFF",
                "Theme changes must not overwrite saved manual colors.");
        }
        finally
        {
            taskbar?.Close();
            window?.Close();
            for (int i = 0; i < properties.Length; i++) properties[i].SetValue(null, originals[i]);
            directoryProperty.SetValue(null, originalDirectory);
        }
    }

    private static void AssertTheme(TaskbarWindow taskbar, SettingsWindow window, Color expected)
    {
        var strip = (TaskbarBatteryStrip)((Viewbox)taskbar.Content).Child;
        var choices = (Panel)window.FindName("TaskbarStyleChoices");
        var previews = choices.Children.OfType<RadioButton>().Select(choice =>
            (TaskbarBatteryStrip)((Viewbox)((StackPanel)choice.Content).Children[0]).Child);
        foreach (var rendered in previews.Prepend(strip))
        {
            Arrange(rendered);
            Require(rendered.DeviceCount > 0, "The theme fixture must retain a visible device.");
            Require(Children<DeviceGlyph>(rendered).Where(g => g.Visibility == Visibility.Visible)
                .All(g => ColorOf(g.GlyphBrush) == expected), "Theme changes must refresh both taskbar icons and settings previews.");
            Require(Children<TextBlock>(rendered).Where(t => t.Name == "BatteryNumber")
                .All(t => ColorOf(t.Foreground) == Color.FromRgb(255, 204, 0)), "Live theme refresh must retain custom text color.");
        }
        Require(ColorOf(((Border)window.FindName("TaskbarBatteryColorSwatch")).Background) == expected,
            "The color picker swatch must show the effective taskbar battery color.");
    }

    private static void SetApp(string name, object value) => typeof(App).GetProperty(name)!.SetValue(null, value);
    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
    }

    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
}
