using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Models;

namespace MouseDock.Services;

internal static class TrayDeviceSelection
{
    internal static bool NeedsAnimation(IEnumerable<DeviceInfo> devices, int threshold) =>
        devices?.Any(device => device.IsChargingActive ||
            device.Level >= 0 && device.Level <= threshold && !device.IsFullActive) == true;

    internal static DeviceInfo[] Resolve(AppSettings settings, IEnumerable<DeviceInfo> devices)
    {
        if (settings?.MultiTray != true || devices == null) return Array.Empty<DeviceInfo>();
        var available = devices.Where(d => d != null && d.Connected && !d.Hidden &&
                d.RecognitionEnabled && !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
        var targets = settings.TrayDeviceIds ??
            available.Values.Where(d => d.Selected).Select(d => d.Id).ToList();
        return targets.Where(id => id != null && available.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(id => available[id]).ToArray();
    }
}
