using System;
using System.Windows;
using MouseDock.Models;

namespace MouseDock.Services;

public static class FloatingIconGeometry
{
    private static bool Character(string style) => style is "character" or "portrait" or "photo";
    public static double Scale(AppSettings settings, string style)
    {
        double scale = Character(style) ? settings.CharacterIconScale ?? settings.IconScale : settings.IconScale;
        return double.IsFinite(scale) ? Math.Clamp(scale, .5, 3) : 1;
    }
    public static void StoreScale(AppSettings settings, string style, double scale)
    {
        settings.CharacterIconScale ??= double.IsFinite(settings.IconScale) ? Math.Clamp(settings.IconScale, .5, 3) : 1;
        scale = double.IsFinite(scale) ? Math.Clamp(scale, .5, 3) : 1;
        if (Character(style)) settings.CharacterIconScale = scale;
        else settings.IconScale = scale;
    }

    public static int HitTest(Point point, Rect bounds, double grip)
    {
        var expanded = bounds;
        expanded.Inflate(grip, grip);
        if (!expanded.Contains(point)) return 0;
        int edge = 0;
        if (Math.Abs(point.X - bounds.Left) <= grip) edge |= 1;
        else if (Math.Abs(point.X - bounds.Right) <= grip) edge |= 2;
        if (Math.Abs(point.Y - bounds.Top) <= grip) edge |= 4;
        else if (Math.Abs(point.Y - bounds.Bottom) <= grip) edge |= 8;
        return edge;
    }

    public static Rect Resize(Rect start, Vector delta, int edges, double minimum, double maximum, Rect workArea)
    {
        if (start.Width <= 0 || start.Height <= 0 || !double.IsFinite(delta.X + delta.Y)) return start;
        double dw = (edges & 1) != 0 ? -delta.X : (edges & 2) != 0 ? delta.X : 0;
        double dh = (edges & 4) != 0 ? -delta.Y : (edges & 8) != 0 ? delta.Y : 0;
        bool horizontal = (edges & 3) != 0, vertical = (edges & 12) != 0;
        double factor = horizontal && vertical
            ? 1 + (dw * start.Width + dh * start.Height) / (start.Width * start.Width + start.Height * start.Height)
            : horizontal ? 1 + dw / start.Width : 1 + dh / start.Height;
        double horizontalRoom = (edges & 1) != 0 ? start.Right - workArea.Left :
            (edges & 2) != 0 ? workArea.Right - start.Left :
            2 * Math.Min(start.Left + start.Width / 2 - workArea.Left, workArea.Right - start.Left - start.Width / 2);
        double verticalRoom = (edges & 4) != 0 ? start.Bottom - workArea.Top :
            (edges & 8) != 0 ? workArea.Bottom - start.Top :
            2 * Math.Min(start.Top + start.Height / 2 - workArea.Top, workArea.Bottom - start.Top - start.Height / 2);
        double available = Math.Max(.01, Math.Min(horizontalRoom / start.Width, verticalRoom / start.Height));
        factor = Math.Clamp(factor, Math.Min(minimum, available), Math.Min(maximum, available));
        double width = start.Width * factor, height = start.Height * factor;
        double x = (edges & 1) != 0 ? start.Right - width : (edges & 2) != 0 ? start.Left : start.Left + (start.Width - width) / 2;
        double y = (edges & 4) != 0 ? start.Bottom - height : (edges & 8) != 0 ? start.Top : start.Top + (start.Height - height) / 2;
        return new Rect(Math.Clamp(x, workArea.Left, workArea.Right - width),
            Math.Clamp(y, workArea.Top, workArea.Bottom - height), width, height);
    }
}
