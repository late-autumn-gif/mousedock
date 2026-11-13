using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;
using Forms = System.Windows.Forms;

namespace MouseDock;

public partial class App
{
    private static DpiService _dpiService;
    private static readonly Dictionary<string, DpiWindow> _dpiWindows = new(StringComparer.OrdinalIgnoreCase);
    private static DpiService Dpi => _dpiService ??= new DpiService(
        () => Settings.SafeMode, () => Battery?.Preferences);

    public static bool SupportsDpi(DeviceInfo device) => device != null && Dpi.Supports(device);

    public static void ShowDpi(DeviceInfo device)
    {
        if (!SupportsDpi(device) || Battery?.Devices.Contains(device) != true) return;
        if (!_dpiWindows.TryGetValue(device.Id, out var window))
        {
            string id = device.Id;
            window = new DpiWindow(Dpi, device, Theme);
            _dpiWindows[id] = window;
            window.Closed += (_, _) => _dpiWindows.Remove(id);
            ApplyIcon(window);
        }
        window.Show();
        if (window.WindowState == System.Windows.WindowState.Minimized)
            window.WindowState = System.Windows.WindowState.Normal;
        window.Activate();
    }

    private static void WatchDpiDevices()
    {
        Battery.Devices.CollectionChanged += (_, _) =>
        {
            foreach (var pair in _dpiWindows.ToArray())
            {
                if (Battery.Devices.Any(device => device.Id == pair.Key)) continue;
                Dpi.Invalidate(pair.Key);
                pair.Value.Close();
            }
        };
    }

    private static void AddDpiMenu(Forms.ContextMenuStrip menu, DeviceInfo device)
    {
        var dpi = new Forms.ToolStripMenuItem("DPI 调节");
        menu.Items.Add(dpi);
        menu.Opening += (_, _) =>
        {
            foreach (var item in dpi.DropDownItems.Cast<Forms.ToolStripItem>().ToArray()) item.Dispose();
            dpi.DropDownItems.Clear();
            var targetDevice = device ?? menu.Tag as DeviceInfo;
            var devices = targetDevice != null ? new[] { targetDevice } :
                Battery?.Devices.Where(SupportsDpi).GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() == 1).Select(group => group.Single()).ToArray()
                ?? Array.Empty<DeviceInfo>();
            foreach (var target in devices.Where(SupportsDpi))
            {
                var choice = new Forms.ToolStripMenuItem(target.Name)
                {
                    Enabled = !Settings.SafeMode && target.Connected && target.RecognitionEnabled
                };
                choice.Click += (_, _) => ShowDpi(target);
                dpi.DropDownItems.Add(choice);
            }
            dpi.Visible = dpi.HasDropDownItems;
        };
    }
}
