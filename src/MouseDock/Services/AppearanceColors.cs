using System;
using System.Collections.Generic;
using System.Windows.Media;
using MouseDock.Models;

namespace MouseDock.Services;

public static class AppearanceColors
{
    internal static readonly SolidColorBrush LowBatteryFill = CreateLowBatteryFill();

    private static readonly Brush ChargingFill = Solid(Color.FromRgb(53, 168, 231));
    private static readonly Brush AlternateChargingFill = Solid(Color.FromRgb(160, 107, 229));
    private static readonly Brush LowBatteryText = Solid(Color.FromRgb(243, 99, 113));

    internal static Brush BatteryState(Brush normal, int level, int threshold, bool charging, bool full = false, bool text = false)
    {
        if (full) return normal;
        if (charging && level >= 0)
        {
            var color = ((SolidColorBrush)ChargingFill).Color;
            return normal is SolidColorBrush solid &&
                Math.Abs(color.R - solid.Color.R) + Math.Abs(color.G - solid.Color.G) + Math.Abs(color.B - solid.Color.B) < 95
                ? AlternateChargingFill : ChargingFill;
        }
        return IsLowBattery(level, threshold) ? (text ? LowBatteryText : LowBatteryFill) : normal;
    }

    private static Brush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    internal static bool IsLowBattery(int level, int threshold) => level >= 0 && level <= threshold;

    private static SolidColorBrush CreateLowBatteryFill()
    {
        var brush = new SolidColorBrush(Color.FromRgb(180, 35, 50));
        brush.Freeze();
        return brush;
    }

    public static T Battery<T>(int level, T normal, T low, T unknown) =>
        level < 0 ? unknown : level <= 20 ? low : normal;

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
