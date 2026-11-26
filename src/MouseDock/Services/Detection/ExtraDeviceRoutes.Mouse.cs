using MouseDock.Models;

namespace MouseDock.Services.Detection;

internal static partial class ExtraDeviceRoutes
{
    internal static Route Rog8kMouse(ushort vid, ushort pid) => VendorProtocol.IsRog8kReceiver(vid, pid)
        ? new(DeviceKind.Mouse, "rog8k", "2.4G", SharedReceiver: true) { Query = VendorProtocol.ReadExtraMouse }
        : null;

    static partial void FindMouse(ushort vid, ushort pid, string product, ref Route route)
    {
        if (route == null && ScyroxLegacyProtocol.IsCandidate(vid,pid,product))
        {
            route = new(DeviceKind.Mouse,"ScyroxLegacy","2.4G") { Query = VendorProtocol.ReadScyroxLegacy };
            return;
        }
        if (route == null && RapooVt3MaxProtocol.IsCandidate(vid, pid))
        {
            route = new(DeviceKind.Mouse, "RapooVt3Max", "2.4G") { Query = VendorProtocol.ReadRapooVt3Max };
            return;
        }
        if (route != null || VendorProtocol.IsRog8kReceiver(vid, pid) || !VendorProtocol.ExtraMouseCandidate(vid, pid, product)) return;
        route = new(DeviceKind.Mouse, VendorProtocol.ExtraMouseFamily(vid, pid, product),
            VendorProtocol.ExtraMouseTransport(vid, pid, product)) { Query = VendorProtocol.ReadExtraMouse };
    }
}
