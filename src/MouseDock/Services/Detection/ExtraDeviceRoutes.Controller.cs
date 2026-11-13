using System.Collections.Generic;
using System;
using System.Linq;
using MouseDock.Models;

namespace MouseDock.Services.Detection;

internal static partial class ExtraDeviceRoutes
{
    static partial void FindController(ushort vid, ushort pid, string product, ref Route route)
    {
        if (route != null || !VendorProtocol.ExtraControllerCandidate(vid, pid, product)) return;
        route = new(DeviceKind.Controller, VendorProtocol.ExtraControllerFamily(vid, pid, product))
        { Query = VendorProtocol.ReadExtraController };
    }

    static partial void AppendSystemControllers(List<DetectedDevice> devices, DevicePreferences preferences, bool safeMode) =>
        MergeSystemControllers(devices, VendorProtocol.ReadSystemControllers(preferences, safeMode, devices));

    internal static void MergeSystemControllers(List<DetectedDevice> devices, IEnumerable<DetectedDevice> supplemental)
    {
        foreach (var item in supplemental)
        {
            var existing = devices.FirstOrDefault(d => string.Equals(d.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            if (existing == null) { devices.Add(item); continue; }
            if (existing.Kind != DeviceKind.Controller || !existing.Connected || existing.Level >= 0 || item.Level < 0) continue;
            existing.Level = item.Level;
            existing.Charging = item.Charging;
            existing.ChargeFull = item.ChargeFull;
            existing.IsStale = false;
            existing.Note = item.Note;
        }
    }
}
