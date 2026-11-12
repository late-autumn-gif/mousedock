using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

internal static class DisplaySizeChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    private static PropertyInfo SizePreference => typeof(AppSettings).GetProperty("TaskbarSizePercent")
        ?? throw new InvalidOperationException("Taskbar size cannot be saved independently.");
    private static double TaskbarSize(AppSettings settings) => (double)SizePreference.GetValue(settings)!;
    private static void TaskbarSize(AppSettings settings, double value) => SizePreference.SetValue(settings, value);
    private static Size Scale(Size content, double percent)
    {
        var scale = typeof(TaskbarPlacement).GetMethod("ScaleContent");
        Require(scale != null, "Taskbar placement has no user size input.");
        return (Size)scale!.Invoke(null, [content, percent])!;
    }

    internal static void Preferences()
    {
        using var fixture = new AppearanceFixture();
        TaskbarSize(fixture.Settings, 75);
        fixture.Settings.EdgeSizePercent = 135;
        fixture.Settings.IconScale = 1.7;
        fixture.Settings.CharacterIconScale = 2.3;
        SettingsStore.Save(fixture.Settings.Clone());
        var restored = SettingsStore.Load();
        Require(TaskbarSize(restored) == 75 && restored.EdgeSizePercent == 135 &&
            restored.IconScale == 1.7 && restored.CharacterIconScale == 2.3,
            "Saving compact sizes changes another surface or loses the selected percentage.");
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"EdgeSizePercent\":125}")!;
        Require(TaskbarSize(legacy) == 100 && legacy.EdgeSizePercent == 125,
            "Legacy settings must retain their edge size and a 100% taskbar default.");
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        TaskbarSize(restored, double.NaN);
        restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(restored, options), options)!;
        Require(Scale(new Size(80, 40), TaskbarSize(restored)) == new Size(80, 40),
            "An invalid saved size produces invalid placement dimensions.");
    }

    internal static void Placement()
    {
        var content = new Size(80, 40);
        foreach (var (percent, expected) in new[] { (50d, new Size(40, 20)), (75d, new Size(60, 30)),
            (150d, new Size(120, 60)), (-10d, new Size(40, 20)), (900d, new Size(120, 60)),
            (double.NaN, content), (double.PositiveInfinity, content) })
            Require(Scale(content, percent) == expected, "Taskbar size does not normalize consistently.");
        var bar = new Rect(0, 1000, 1920, 48);
        Require(TaskbarPlacement.Calculate(bar, 3, Scale(content, 50), 25, 1) == new Rect(470, 1014, 40, 20),
            "Manual placement ignores the smaller footprint.");
        Require(TaskbarPlacement.Calculate(bar, 3, Scale(content, 150), 25, 1) == new Rect(456, 1000, 96, 48),
            "Large taskbar icons must fit the taskbar height.");
        var icons = new Rect(400, 1000, 500, 48);
        var tray = new Rect(980, 1000, 940, 48);
        Require(TaskbarPlacement.TryFollow(bar, 3, Scale(content, 50), 1, icons, [icons, tray],
            out var placed, iconOffset: 24) && placed == new Rect(924, 1014, 40, 20),
            "Following lost the signed offset or smaller footprint.");
        Require(!TaskbarPlacement.TryFollow(bar, 3, Scale(content, 100), 1, icons, [icons, tray],
            out _, iconOffset: 24), "Growing the strip overlaps the tray instead of requesting overflow.");
        Require(TaskbarPlacement.TryFollow(new Rect(-72, 0, 72, 1600), 0, Scale(content, 50), 1.5,
            new Rect(-72, 200, 72, 500), [], out placed, iconOffset: -24) &&
            placed == new Rect(-51, 104, 30, 60), "Vertical high-DPI placement changes the saved left offset.");
    }

    internal static void Settings()
    {
        using var fixture = new AppearanceFixture();
        fixture.Settings.SurfaceMode = "taskbar";
        TaskbarSize(fixture.Settings, 75);
        fixture.Settings.EdgeSizePercent = 135;
        fixture.Settings.IconScale = 1.7;
        var window = new SettingsWindow();
        fixture.Set("SettingsView", window);
        window.Reload();
        var taskbar = window.FindName("TaskbarSizeSlider") as Slider;
        Require(taskbar != null, "Taskbar settings have no size control.");
        var edge = (Slider)window.FindName("EdgeSizeSlider");
        Require(taskbar!.Value == 75 && edge.Value == 135, "Reload couples the compact size sliders.");
        taskbar.Value = 110;
        Require(TaskbarSize(fixture.Settings) == 110 && fixture.Settings.EdgeSizePercent == 135 &&
            ((TextBlock)window.FindName("TaskbarSizeValue")).Text == "110%",
            "The taskbar slider does not save or display its independent size.");
        var resetTaskbar = window.FindName("TaskbarSizeReset") as Button;
        var resetEdge = window.FindName("EdgeSizeReset") as Button;
        Require(resetTaskbar != null && resetEdge != null, "Compact surfaces need separate 100% reset actions.");
        resetTaskbar!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(TaskbarSize(fixture.Settings) == 100 && fixture.Settings.EdgeSizePercent == 135 && taskbar.Value == 100,
            "Resetting taskbar size modifies the edge size.");
        TaskbarSize(fixture.Settings, 85);
        window.Reload();
        edge.Value = 120;
        resetEdge!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(fixture.Settings.EdgeSizePercent == 100 && TaskbarSize(fixture.Settings) == 85 &&
            fixture.Settings.IconScale == 1.7 && edge.Value == 100,
            "Resetting edge size modifies taskbar or floating size.");
        fixture.Settings.SurfaceMode = "edge";
        window.Reload();
        Require(((FrameworkElement)window.FindName("TaskbarStylePanel")).Visibility == Visibility.Collapsed &&
            ((FrameworkElement)window.FindName("EdgeOptionsPanel")).Visibility == Visibility.Visible,
            "Size controls appear under the wrong display mode.");
    }

    internal static void Wheel()
    {
        using var fixture = new AppearanceFixture();
        TaskbarSize(fixture.Settings, 100);
        fixture.Settings.EdgeSizePercent = 125;
        var settings = new SettingsWindow();
        fixture.Set("SettingsView", settings);
        var taskbar = new TaskbarWindow();
        var edge = new ScreenEdgeWindow();
        fixture.Set("Taskbar", taskbar);
        fixture.Set("Edge", edge);
        foreach (var (before, delta, after) in new[] { (100d, 120, 105d), (50d, -120, 50d),
            (150d, 120, 150d), (100d, -240, 90d), (100d, 0, 100d), (double.NaN, 120, 105d) })
        {
            TaskbarSize(fixture.Settings, before);
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.MouseWheelEvent };
            taskbar.RaiseEvent(wheel);
            Require(TaskbarSize(fixture.Settings) == after && fixture.Settings.EdgeSizePercent == 125,
                "Taskbar wheel resizing changes another surface or fails to clamp.");
            Require(wheel.Handled == (delta != 0), "Taskbar resizing does not consume the wheel event.");
            if (delta != 0 && after != before)
                Require(((Slider)settings.FindName("TaskbarSizeSlider")).Value == after,
                    "Taskbar wheel resizing leaves the settings slider stale.");
        }
        edge.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
        Require(fixture.Settings.EdgeSizePercent == 120 && TaskbarSize(fixture.Settings) == 105 &&
            ((Slider)settings.FindName("EdgeSizeSlider")).Value == 120,
            "Edge wheel resizing changes taskbar size or leaves its slider stale.");
    }

    internal static void Windows()
    {
        using var fixture = new AppearanceFixture();
        TaskbarSize(fixture.Settings, 100);
        fixture.Settings.TaskbarFollowIcons = false;
        var taskbar = new TaskbarWindow();
        var edge = new ScreenEdgeWindow();
        fixture.Set("Taskbar", taskbar);
        fixture.Set("Edge", edge);
        taskbar.Show();
        edge.Show();
        foreach (var (style, _) in TaskbarBatteryStrip.Styles(false))
        {
            fixture.Settings.TaskbarStyle = fixture.Settings.EdgeStyle = style;
            taskbar.UpdateDevices();
            edge.UpdateDevices();
            var sizes = new List<(Rect Taskbar, Rect Edge)>();
            foreach (double percent in new[] { 50d, 100, 150 })
            {
                TaskbarSize(fixture.Settings, percent);
                fixture.Settings.EdgeSizePercent = percent;
                taskbar.UpdateAnchor();
                edge.UpdateAnchor();
                Pump(taskbar);
                Pump(edge);
                var actual = Bounds(taskbar);
                Require(TaskbarAnchor.TryGet(out var bar, out uint position, out double dpi),
                    "The native taskbar is unavailable.");
                var strip = (FrameworkElement)((Viewbox)taskbar.Content).Child;
                var content = position is 0 or 2 ? new Size(strip.Height, strip.Width) : new Size(strip.Width, strip.Height);
                var expected = TaskbarPlacement.Calculate(bar, position, Scale(content, percent),
                    fixture.Settings.TaskbarOffset, dpi);
                Require(Math.Abs(actual.Width - Math.Round(expected.Width)) <= 1 &&
                    Math.Abs(actual.Height - Math.Round(expected.Height)) <= 1,
                    $"{style} taskbar native bounds do not match the resized footprint.");
                sizes.Add((actual, Bounds(edge)));
            }
            Require(sizes[0].Taskbar.Width <= sizes[1].Taskbar.Width &&
                sizes[0].Taskbar.Height <= sizes[1].Taskbar.Height &&
                sizes[1].Taskbar.Width <= sizes[2].Taskbar.Width,
                $"{style} taskbar window ignores size or shrinks when enlarged.");
            Require(sizes[0].Edge.Width < sizes[1].Edge.Width && sizes[1].Edge.Width < sizes[2].Edge.Width &&
                sizes[0].Edge.Height < sizes[1].Edge.Height && sizes[1].Edge.Height < sizes[2].Edge.Height,
                $"{style} right-edge window ignores size.");
        }
    }

    internal static void Rendering()
    {
        var gallery = new StackPanel { Background = Brushes.DimGray };
        var devices = new[] { new DeviceInfo { Id = "size-mouse", Name = "Mouse", Level = 100 },
            new DeviceInfo { Id = "size-keyboard", Name = "Keyboard", Level = 28 } };
        var settings = new AppSettings { PhotoAsset = Path.GetFullPath("src/MouseDock/Assets/app.png") };
        foreach (var (style, _) in TaskbarBatteryStrip.Styles(false))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            foreach (bool vertical in new[] { false, true })
            foreach (double percent in new[] { 50d, 150 })
            {
                var strip = new TaskbarBatteryStrip(devices, style, true, vertical, Brushes.White, 20,
                    "", "", null, settings, ThemePreset.ById("dusk"), screenEdge: vertical);
                var view = new Viewbox { Child = strip, Width = strip.Width * percent / 100,
                    Height = strip.Height * percent / 100, Stretch = Stretch.Uniform };
                Arrange(view);
                var pixels = Snapshot(view);
                Require(pixels.Any(b => b != 0), $"{style} at {percent}% renders blank.");
                foreach (var label in Children<TextBlock>(view).Where(t => t.Visibility == Visibility.Visible &&
                    t.Name is "BatteryNumber" or "CharacterDeviceName"))
                {
                    var bounds = label.TransformToAncestor(view).TransformBounds(new Rect(label.RenderSize));
                    var tolerance = new Rect(view.RenderSize);
                    tolerance.Inflate(.1, .1);
                    Require(tolerance.Contains(bounds), $"{style} at {percent}% clips a battery number or device name.");
                }
                row.Children.Add(new Border { Child = view, Margin = new Thickness(6) });
            }
            gallery.Children.Add(row);
        }
        Arrange(gallery);
        Directory.CreateDirectory("artifacts/taskbar-edge-icon-size");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Image(gallery)));
        using var output = File.Create("artifacts/taskbar-edge-icon-size/style-sizes.png");
        encoder.Save(output);
    }

    private static Rect Bounds(Window window)
    {
        Require(GetWindowRect(new WindowInteropHelper(window).Handle, out var rect), "Missing native window bounds.");
        return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    private static void Pump(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }
    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        Pump(element);
    }
    private static RenderTargetBitmap Image(FrameworkElement element)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        return image;
    }
    private static byte[] Snapshot(FrameworkElement element)
    {
        var image = Image(element);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
