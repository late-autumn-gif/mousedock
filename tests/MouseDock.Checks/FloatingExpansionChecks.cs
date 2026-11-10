using System.Windows;
using MouseDock.Services;
using MouseDock.Views;

internal static class FloatingExpansionChecks
{
    internal static void Expansion()
    {
        using var fixture = new AppearanceFixture();
        var method = typeof(MouseDock.App).GetMethod("ExpandDock");
        Require(method != null, "Floating surfaces have no full-panel expansion action.");
        var dock = new DockWindow { ShowActivated = false };
        var mini = new MiniWindow { ShowActivated = false };
        var icon = new IconWindow { ShowActivated = false };
        fixture.Set("Dock", dock); fixture.Set("Mini", mini); fixture.Set("Icon", icon);
        foreach (string mode in new[] { "mini", "icon" })
        {
            MouseDock.App.SetSurface(mode);
            method!.Invoke(null, null);
            Require(fixture.Settings.SurfaceMode == "dock" && dock.IsVisible && !mini.IsVisible && !icon.IsVisible,
                "Expanding a floating surface does not exclusively reveal the main panel.");
            Require(SettingsStore.Load().SurfaceMode == "dock", "Expanded mode was not persisted.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
