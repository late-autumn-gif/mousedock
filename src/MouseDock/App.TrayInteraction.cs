using System.Linq;
using System.Windows;
using MouseDock.Services;

namespace MouseDock;

public partial class App
{
    private static TrayHoverController _trayHover;

    private static void InitializeTrayInteraction()
    {
        _trayHover?.Dispose();
        Window Target() => Settings.SurfaceMode is "dock" or "mini" or "icon"
            ? Surfaces.GetWindow(Settings.SurfaceMode) : null;
        _trayHover = new TrayHoverController(() => Settings.TrayHover, Target,
            () => Current?.Windows.Cast<Window>().Any(window => window.IsVisible && window != Target()) == true);
        _trayHover.Attach(_tray);
        _deviceTrays.IconAdded += _trayHover.Attach;
        _deviceTrays.IconRemoved += _trayHover.Detach;
        foreach (var icon in _deviceTrays.Icons) _trayHover.Attach(icon);
    }
}
