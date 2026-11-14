using MouseDock.Models;

namespace MouseDock.Services.Detection;

internal static partial class ExtraDeviceRoutes
{
    static partial void FindMouse(ushort vid, ushort pid, string product, ref Route route)
    {
        if (route == null && RapooVt3MaxProtocol.IsCandidate(vid, pid))
        {
            route = new(DeviceKind.Mouse, "RapooVt3Max", "2.4G") { Query = VendorProtocol.ReadRapooVt3Max };
            return;
        }
        if (route != null || !VendorProtocol.ExtraMouseCandidate(vid, pid, product)) return;
        route = new(DeviceKind.Mouse, VendorProtocol.ExtraMouseFamily(vid, pid, product),
            VendorProtocol.ExtraMouseTransport(vid, pid, product)) { Query = VendorProtocol.ReadExtraMouse };
    }
}
