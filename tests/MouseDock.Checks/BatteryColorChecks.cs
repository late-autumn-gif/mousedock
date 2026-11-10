using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Controls;
using MouseDock.Models;

internal static class BatteryColorChecks
{
    internal static void LiveStatus()
    {
        var device = new DeviceInfo { Id = "mouse", Level = 21 };
        var icon = new ReferenceBatteryIcon { Width = 112, Height = 112, StyleName = "plain", DeviceModels = new[] { device } };
        var normal = Snapshot(icon);
        device.Level = 20;
        Require(Count(Snapshot(icon), ThemePreset.C("#FF687D")) > 10, "Ordinary icon does not show the low status color at 20%.");
        device.Level = 0;
        Require(Count(Snapshot(icon), ThemePreset.C("#FF687D")) > 10, "Zero battery lost its low status color.");
        device.Level = -1;
        Require(Count(Snapshot(icon), ThemePreset.C("#BCC6C1")) > 5, "Unknown battery does not show muted color.");
        device.Level = 40;
        Require(Count(Snapshot(icon), ThemePreset.C("#FF687D")) == 0, "Recovered normal battery retains the low color.");
        var character = new CharacterBatteryIcon { Width = 240, Height = 240, Level = 20 };
        Require(Count(Snapshot(character), ThemePreset.C("#FF687D")) > 5, "Character battery plate ignores low status color.");
        character.Level = -1;
        Require(Count(Snapshot(character), ThemePreset.C("#BCC6C1")) > 1, "Character unknown reading is not muted.");
        var theme = ThemePreset.ById("ocean").Clone();
        string initialKey = TaskbarBatteryStrip.AppearanceKey(new AppSettings(), theme);
        theme.Low = Colors.Red;
        Require(TaskbarBatteryStrip.AppearanceKey(new AppSettings(), theme) != initialKey,
            "Changing only the low color leaves taskbar and edge character plates cached.");
        string lowKey = TaskbarBatteryStrip.AppearanceKey(new AppSettings(), theme);
        theme.TextDim = Colors.Gray;
        Require(TaskbarBatteryStrip.AppearanceKey(new AppSettings(), theme) != lowKey,
            "Changing only the unknown color leaves compact character plates cached.");
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
    private static int Count(byte[] pixels, Color color)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 240 && Math.Abs(pixels[i] - color.B) < 4 &&
                Math.Abs(pixels[i + 1] - color.G) < 4 && Math.Abs(pixels[i + 2] - color.R) < 4) count++;
        return count;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
