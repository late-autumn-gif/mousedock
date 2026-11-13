using System.Collections.Generic;
using MouseDock.Models;

namespace MouseDock.Services.Detection;

internal static partial class ExtraDeviceRoutes
{
    internal sealed record Route(DeviceKind Kind, string Family, string Transport = null, bool SharedReceiver = false)
    {
        internal string Reader => "Extra" + Kind + ":" + Family;
        internal System.Func<ushort, ushort, string, IReadOnlyList<HidDiscovery.VendorCollectionInfo>,
            BatteryReplay.Trace, ReferenceBatteryProtocol.Reading> Query { get; init; }
    }

    internal static Route Find(ushort vid, ushort pid, string product)
    {
        // Existing validated instance readers keep ownership of their devices.
        if (VendorProtocol.ReportedReceiverRoute(vid, pid, product) != null ||
            VgnF1Protocol.IsCandidate(vid, pid, product) || F75Protocol.IsCandidate(vid, pid, product) ||
            M9ProProtocol.IsCandidate(vid, pid, product) || RazerBlackSharkProtocol.IsCandidate(vid, pid) ||
            VendorProtocol.IsRogOmni(vid, pid) ||
            M137FeatureProtocol.IsCandidate(vid, pid, product) ||
            MchoseKeyboardProtocol.IsSupportedDevice(vid, pid, product) ||
            ReferenceProfiles.Find(vid, pid) != null ||
            ZeroRainyProtocol.Candidate(vid, pid, product) ||
            VendorProtocol.IsScopedModernMouse(vid, pid, product) ||
            VendorProtocol.IsScopedG7(vid, pid, product) ||
            VendorProtocol.IsScopedInphic(vid, pid, product) ||
            VendorProtocol.IsReportedX23Se(vid, pid, product) ||
            OriginalGpwProtocol.IsCandidate(vid, pid) ||
            VendorProtocol.IsMchoseA7V2(vid, pid) ||
            AulaBatteryProtocol.IsCandidate(vid, pid, product) ||
            RkKeyboardProtocol.IsCandidate(vid, pid) ||
            VendorProtocol.IsAulaF99(vid, pid, product) ||
            VendorProtocol.IsNeon75Air(vid, pid) ||
            (vid != 0 && pid != 0 && VendorProtocol.IsAulaSc630(product)))
            return null;
        Route route = null;
        FindMouse(vid, pid, product, ref route);
        FindKeyboard(vid, pid, product, ref route);
        FindController(vid, pid, product, ref route);
        FindHeadset(vid, pid, product, ref route);
        return route;
    }

    internal static Route Primary(ushort vid, ushort pid, string product) =>
        Find(vid, pid, product) is { SharedReceiver: false } route ? route : null;

    internal static ReferenceBatteryProtocol.Reading Read(Route route, ushort vid, ushort pid, string product,
        IReadOnlyList<HidDiscovery.VendorCollectionInfo> channels, BatteryReplay.Trace trace) =>
        route?.Query?.Invoke(vid, pid, product, channels, trace) ??
        ReferenceBatteryProtocol.Reading.Failed("没有已注册的扩展电量读取器");

    internal static void AddSystemDevices(List<DetectedDevice> devices, DevicePreferences preferences, bool safeMode) =>
        AppendSystemControllers(devices, preferences, safeMode);

    static partial void FindMouse(ushort vid, ushort pid, string product, ref Route route);
    static partial void FindKeyboard(ushort vid, ushort pid, string product, ref Route route);
    static partial void FindController(ushort vid, ushort pid, string product, ref Route route);
    static partial void FindHeadset(ushort vid, ushort pid, string product, ref Route route);
    static partial void AppendSystemControllers(List<DetectedDevice> devices, DevicePreferences preferences, bool safeMode);
}
