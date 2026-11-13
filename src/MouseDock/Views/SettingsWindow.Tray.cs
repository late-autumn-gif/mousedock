using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MouseDock.Views;

public partial class SettingsWindow
{
    private void ReloadTraySettings()
    {
        bool previous = _loading;
        _loading = true;
        try
        {
            MultiTrayCheck.IsChecked = App.Settings.MultiTray;
            TrayFollowMainCheck.IsChecked = App.Settings.TrayDeviceIds == null;
            TrayFollowMainCheck.IsEnabled = App.Settings.MultiTray;
            TrayDeviceRows.Children.Clear();
            var selected = new HashSet<string>(App.Settings.TrayDeviceIds ??
                App.Battery.Devices.Where(d => d.Selected).Select(d => d.Id).ToList(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var device in App.Battery.Devices)
            {
                string id = device.Id;
                var row = new CheckBox
                {
                    Content = new TextBlock { Text = device.Name, TextWrapping = TextWrapping.Wrap, MaxWidth = 270 },
                    IsChecked = selected.Contains(id),
                    IsEnabled = device.RecognitionEnabled && !device.Hidden,
                    ToolTip = device.StatusText
                };
                row.Checked += (_, _) => SetTrayTarget(id, true);
                row.Unchecked += (_, _) => SetTrayTarget(id, false);
                TrayDeviceRows.Children.Add(row);
            }
            TrayDeviceRows.IsEnabled = App.Settings.MultiTray && App.Settings.TrayDeviceIds != null;
        }
        finally { _loading = previous; }
    }

    private void OnMultiTrayChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.MultiTray = MultiTrayCheck.IsChecked == true;
        SaveTraySelection();
    }

    private void OnTrayFollowMainChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.TrayDeviceIds = TrayFollowMainCheck.IsChecked == true ? null :
            App.Battery.Devices.Where(d => d.Selected).Select(d => d.Id).ToList();
        SaveTraySelection();
    }

    private void SetTrayTarget(string id, bool selected)
    {
        if (_loading || App.Settings.TrayDeviceIds == null) return;
        App.Settings.TrayDeviceIds.RemoveAll(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase));
        if (selected) App.Settings.TrayDeviceIds.Add(id);
        SaveTraySelection();
    }

    private void SaveTraySelection()
    {
        App.RefreshTray();
        App.SaveSettings();
        ReloadTraySettings();
    }
}
