using System.Text.Json;
using System.Windows.Media;
using MouseDock.Models;
using MouseDock.Services;

internal static class AppearanceColorChecks
{
    internal static void IndependentColors()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {"ThemeId":"ocean","ThemeColors":{"accent":"#DD3377","text":"#FFEEDD"},
             "IconColors":{"accent":"#33AAEE"},"TrayColors":{"accent":"#CCAA22"}}
            """)!;
        var theme = Palette.ResolveTheme(settings, out _);
        Require(theme.Accent == ThemePreset.C("#DD3377") && theme.Text == ThemePreset.C("#FFEEDD"),
            "Ordinary theme ignores its custom colors.");
        Require(ThemePreset.ById("ocean").Accent == ThemePreset.C("#7BC9D3"),
            "Custom colors changed a shared theme preset.");
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.AppearanceColors")!;
        var icon = (ThemePreset)type.GetMethod("IconPalette")!.Invoke(null, [settings, theme, true])!;
        var tray = (ThemePreset)type.GetMethod("TrayPalette")!.Invoke(null, [settings, theme])!;
        Require(icon.Accent == ThemePreset.C("#33AAEE") && tray.Accent == ThemePreset.C("#CCAA22"),
            "Floating and tray colors are not independent.");
        var options = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var clone = settings.Clone();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(clone, options));
        Require(json.RootElement.GetProperty("IconColors").GetProperty("accent").GetString() == "#33AAEE",
            "Cloning lost the floating palette.");
        var dict = (Dictionary<string, string>)typeof(AppSettings).GetProperty("ThemeColors")!.GetValue(clone)!;
        dict.Clear();
        Require(Palette.ResolveTheme(clone, out _).Accent == ThemePreset.C("#7BC9D3") &&
            Palette.ResolveTheme(settings, out _).Accent == ThemePreset.C("#DD3377"),
            "Resetting a draft modified the original settings or failed to restore the theme.");
    }

    internal static void Contrast()
    {
        double Ratio(Color a, Color b)
        {
            double L(Color c)
            {
                double C(byte v) { double x = v / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
                return .2126 * C(c.R) + .7152 * C(c.G) + .0722 * C(c.B);
            }
            return (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
        }
        foreach (string hex in new[] { "#000000", "#FFFFFF", "#808080" })
        {
            var settings = new AppSettings { ThemeColors = new()
                { ["background"] = hex, ["surface"] = hex, ["text"] = hex, ["muted"] = hex, ["accent"] = hex } };
            var theme = Palette.ResolveTheme(settings, out _);
            Require(Ratio(theme.Text, theme.Surface) >= 4.5 && Ratio(theme.TextDim, theme.Surface) >= 3 &&
                Ratio(theme.Accent, theme.Surface) >= 3, "Custom colors remain unreadable against their surface.");
            var saved = JsonSerializer.Serialize(settings.ThemeColors);
            Require(saved.Contains(hex), "Contrast correction overwrote the user's input.");
        }
        var readable = JsonSerializer.Deserialize<AppSettings>("""{"ThemeColors":{"background":"#000000","surface":"#000000","text":"#FFFFFF"}}""")!;
        Require(Palette.ResolveTheme(readable, out _).Text == Colors.White, "Already readable text changed.");
        var mixed = new AppSettings { ThemeColors = new()
            { ["background"] = "#000000", ["surface"] = "#FFFFFF", ["text"] = "#000000" } };
        var mixedTheme = Palette.ResolveTheme(mixed, out _);
        Require(Ratio(mixedTheme.Text, mixedTheme.BgTop) >= 4.5 && Ratio(mixedTheme.Text, mixedTheme.Surface) >= 4.5,
            "Opposite light and dark surfaces incorrectly force white or black text.");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
