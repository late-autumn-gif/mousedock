using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace MouseDock.Services;

public static class IconComposition
{
    public static Size Measure(string style, int count)
    {
        count = Math.Clamp(count, 1, 6);
        if (style == "multi") return new Size((int)Math.Ceiling(count / 3d) * 84 + 16, Math.Min(count, 3) * 70 + 12);
        if (count == 1) return new Size(112, 112);
        return count <= 4 ? new Size(144, 144) : new Size(176, 176);
    }

    public static Rect[] CompactCells(int count, Size size)
    {
        count = Math.Clamp(count, 1, 6);
        int columns = (int)Math.Ceiling(count / 3d);
        int rows = Math.Min(3, count);
        double width = Math.Max(1, (size.Width - 16) / columns);
        double height = Math.Max(1, (size.Height - 12) / rows);
        var cells = new Rect[count];
        for (int i = 0; i < count; i++)
            cells[i] = new Rect(8 + (i / rows) * width + 2, 6 + (i % rows) * height + 2,
                Math.Max(1, width - 4), Math.Max(1, height - 4));
        return cells;
    }

    public static Color IdentityColor(string identity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes((identity ?? "").Trim().ToUpperInvariant()));
        return Palette.FromHsl((hash[0] * 256 + hash[1]) / 65536d, .62, .68);
    }
}
