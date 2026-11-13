using MouseDock.Models;

namespace MouseDock.Services.Detection;

internal static partial class ExtraDeviceRoutes
{
    static partial void FindKeyboard(ushort vid, ushort pid, string product, ref Route route)
    {
        if (route != null || !VendorProtocol.ExtraKeyboardCandidate(vid, pid, product)) return;
        string family = VendorProtocol.ExtraKeyboardFamily(vid, pid, product);
        route = new(DeviceKind.Keyboard, family, SharedReceiver: family == "Logi")
        { Query = VendorProtocol.ReadExtraKeyboard };
    }
}
