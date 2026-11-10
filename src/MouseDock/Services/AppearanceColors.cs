using System;
using System.Collections.Generic;
using System.Windows.Media;
using MouseDock.Models;

namespace MouseDock.Services;

public static class AppearanceColors
{
    public static bool TryColor(string value, out Color color)
    {
        color = default;
        value = value?.Trim();
        if (value == null || value.Length != 7 || value[0] != '#') return false;
        for (int i = 1; i < value.Length; i++)
            if (!Uri.IsHexDigit(value[i])) return false;
        return Palette.TryParseColor(value, out color);
    }

    public static ThemePreset Apply(ThemePreset source, Dictionary<string, string> values)
    {
        var result = source.Clone();
        result.BgTop = Read(values, "background", result.BgTop);
        if (Has(values, "background")) result.BgBottom = result.BgTop;
        result.Surface = Read(values, "surface", result.Surface);
        result.Text = Read(values, "text", result.Text);
        result.TextDim = Read(values, "muted", result.TextDim);
        result.Accent = Read(values, "accent", result.Accent);
        if (Has(values, "accent")) result.Accent2 = result.Accent;
        result.Low = Read(values, "low", result.Low.A == 0 ? ThemePreset.C("#FF687D") : result.Low);
        return values?.Count > 0 ? ColorContrast.Readable(result) : result;
    }

    public static ThemePreset IconPalette(AppSettings settings, ThemePreset theme, bool follow)
    {
        var source = follow ? theme : ReferenceThemes.Resolve("reference-mint", theme.IsLight ? "light" : "dark");
        return Apply(source, settings.IconColors);
    }

    public static ThemePreset TrayPalette(AppSettings settings, ThemePreset theme) =>
        Apply(ReferenceThemes.Resolve("reference-mint", theme.IsLight ? "light" : "dark"), settings.TrayColors);

    private static bool Has(Dictionary<string, string> values, string key) =>
        values != null && values.TryGetValue(key, out var value) && TryColor(value, out _);

    private static Color Read(Dictionary<string, string> values, string key, Color fallback) =>
        values != null && values.TryGetValue(key, out var value) && TryColor(value, out var color) ? color : fallback;
}
