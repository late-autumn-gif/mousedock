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
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);
    internal static int RunDragFixture()
    {
        using var fixture = new AppearanceFixture();
        fixture.Battery.VisibleDevices[0].Note = "RX=synthetic-private-diagnostic";
        var window = new DockWindow { Title = "MouseDock drag verification", Left = 240, Top = 160, ShowInTaskbar = true };
        fixture.Set("Dock", window);
        window.ApplyTheme(MouseDock.App.Theme, false);
        window.Show();
        string result = Path.Combine(Environment.GetEnvironmentVariable("MOUSEDOCK_DATA_DIR")!, "native-drag.txt");
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
        var density = (ComboBox)window.FindName("DockDensityChoice");
        density.SelectedIndex = 1;
        Require(fixture.Settings.DockStyle == "compact", "Density selection did not reach the real settings.");
        window.SelectSection("devices");
        Require(window.FindName("DeviceChecks") != null && window.FindName("TrayHoverCheck") != null,
            "Existing device or independent tray controls were lost.");
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
