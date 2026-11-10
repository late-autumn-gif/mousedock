using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using MouseDock.Models;

namespace MouseDock.Services;

public static class ColorContrast
{
    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            double channel = value / 255d;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    public static double Ratio(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }

    public static Color Legible(Color color, IEnumerable<Color> backgrounds, double minimum = 4.5)
    {
        var surfaces = backgrounds.ToArray();
        if (surfaces.Length == 0) return color;
        double Score(Color candidate) => surfaces.Min(surface => Ratio(candidate, surface));
        if (Score(color) >= minimum) return color;
        var target = Score(Colors.White) >= Score(Colors.Black) ? Colors.White : Colors.Black;
        if (Score(target) < minimum) return target;
        double lo = 0, hi = 1;
        for (int i = 0; i < 18; i++)
        {
            double middle = (lo + hi) / 2;
            if (Score(Palette.Mix(color, target, middle)) >= minimum) hi = middle;
            else lo = middle;
        }
        return Palette.Mix(color, target, hi);
    }

    public static ThemePreset Readable(ThemePreset source)
    {
        var palette = source.Clone();
        var backgrounds = new[] { palette.BgTop, palette.BgBottom, palette.Surface };
        palette.Text = Legible(palette.Text, backgrounds);
        palette.TextDim = Legible(palette.TextDim, backgrounds, 3);
        palette.Accent = Legible(palette.Accent, backgrounds, 3);
        palette.Accent2 = Legible(palette.Accent2, backgrounds, 3);
        palette.Low = Legible(palette.Low, backgrounds, 3);
        return palette;
    }
}
