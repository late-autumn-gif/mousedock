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

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
