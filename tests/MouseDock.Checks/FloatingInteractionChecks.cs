using System.Windows;
using MouseDock.Models;

internal static class FloatingInteractionChecks
{
    internal static void Geometry()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.FloatingIconGeometry");
        Require(type != null, "Floating edge resizing is missing.");
        var hit = type!.GetMethod("HitTest")!;
        var points = new[] { new Point(1, 1), new Point(50, 1), new Point(99, 1), new Point(1, 50),
            new Point(99, 50), new Point(1, 99), new Point(50, 99), new Point(99, 99) };
        int[] expected = [5, 4, 6, 1, 2, 9, 8, 10];
        for (int i = 0; i < points.Length; i++)
            Require((int)hit.Invoke(null, [points[i], new Rect(0, 0, 100, 100), 7d])! == expected[i],
                "One of the eight resize handles is unreachable.");
        var resize = type.GetMethod("Resize")!;
        var start = new Rect(100, 100, 100, 100);
        var resized = (Rect)resize.Invoke(null, [start, new Vector(-40, -40), 5, .5, 3d, new Rect(0, 0, 1000, 800)])!;
        Require(resized.BottomRight == new Point(200, 200) && resized.Size == new Size(140, 140), "Resize does not preserve the opposite corner.");
        var edgeStart = new Rect(900, 100, 100, 100);
        var edgeResize = (Rect)resize.Invoke(null, [edgeStart, new Vector(100, 0), 2, .5, 3d, new Rect(0, 0, 1000, 800)])!;
        Require(edgeResize == edgeStart, "Growing at the screen edge moves the opposite anchor instead of stopping.");
        var centerStart = new Rect(100, 0, 100, 100);
        var centerResize = (Rect)resize.Invoke(null, [centerStart, new Vector(100, 0), 2, .5, 3d, new Rect(0, 0, 1000, 800)])!;
        Require(centerResize == centerStart, "Centered orthogonal growth jumps away from the screen edge.");
        var settings = new AppSettings { IconScale = 1.25 };
        var store = type.GetMethod("StoreScale")!;
        store.Invoke(null, [settings, "character", 2d]);
        store.Invoke(null, [settings, "ring", .75]);
        var scale = type.GetMethod("Scale")!;
        Require((double)scale.Invoke(null, [settings, "character"])! == 2 &&
            (double)scale.Invoke(null, [settings, "ring"])! == .75, "Ordinary and character sizes are still shared.");
        var clone = settings.Clone();
        Require((double)scale.Invoke(null, [clone, "character"])! == 2, "Cloning loses the character size.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
