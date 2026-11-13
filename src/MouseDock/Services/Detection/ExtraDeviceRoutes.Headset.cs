using MouseDock.Models;

namespace MouseDock.Services.Detection;

internal static partial class ExtraDeviceRoutes
{
    static partial void FindHeadset(ushort vid, ushort pid, string product, ref Route route)
    {
        if (route != null || !VendorProtocol.ExtraHeadsetCandidate(vid, pid, product)) return;
        route = new(DeviceKind.Headset, VendorProtocol.ExtraHeadsetFamily(vid, pid, product))
        { Query = VendorProtocol.ReadExtraHeadset };
    }
}
