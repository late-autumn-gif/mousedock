using System.Reflection;
using System.Windows;
using System.Windows.Media;
using MouseDock.Models;

internal static class IconCompositionChecks
{
    internal static void Layout()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.IconComposition");
        Require(type != null, "Fixed multi-device composition is missing.");
        var measure = type!.GetMethod("Measure")!;
        foreach (string style in new[] { "ring", "tile", "battery", "plain" })
        foreach (int count in new[] { 2, 3, 4 })
        {
            var size = (Size)measure.Invoke(null, [style, count])!;
            Require(size.Width == size.Height, $"{style} with {count} devices does not stay square.");
        }
        var cells = (Rect[])type.GetMethod("CompactCells")!.Invoke(null, [4, new Size(144, 144)])!;
        Require(cells.Length == 4 && cells[0].Top == cells[1].Top && cells[2].Top == cells[3].Top &&
            cells[2].Top > cells[0].Bottom && cells[1].Left > cells[0].Right, "Four compact devices do not form two separate rows and columns.");
        var identity = type.GetMethod("IdentityColor")!;
        var first = (Color)identity.Invoke(null, ["rk-r98-instance"])!;
        var second = (Color)identity.Invoke(null, ["g603-instance"])!;
        Require(first != second && first == (Color)identity.Invoke(null, ["RK-R98-INSTANCE"])!,
            "Device colors depend on ordering or casing rather than stable identity.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
