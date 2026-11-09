using System;
using MouseDock.Models;

namespace MouseDock.Services.Detection;

/// <summary>
/// 这台设备最终由哪个读取器接管。替代 Bucket 上散落的布尔标志
/// （AulaHandled / InphicHandled / ZeroRainyHandled / ModernHandled / G7Handled /
/// GpwHandled / RogScopePaired）：只有一条处理链，不再出现"英菲克借狼蛛字段"。
/// </summary>
internal enum HandledBy
{
    None,
    Receiver33,
    M137,
    RealTek,
    Aula,
    Inphic,
    RogScope,
    MchoseModern,
    MchoseG7,
    ZeroRainy,
    OriginalGpw,
    /// <summary>VGN 蜻蜓 F1 MOBA（3554:F503），见 VgnF1Protocol。</summary>
    VgnF1,
    /// <summary>狼蛛 AULA F75（3554:FA09），见 F75Protocol。</summary>
    AulaF75,
    /// <summary>凌豹 M9 Pro（093A:522C，产品串 LB-M9PRO），见 M9ProProtocol。</summary>
    M9Pro
}

/// <summary>
/// 设备识别路由的唯一来源。
///
/// 以前有两套并行条件：RecognitionPolicy.CanQuery 决定"要不要查"，
/// 各读取器的 IsCandidate 决定"认不认"。248A/249A 的 33 字节旁路就是因为
/// CanQuery 漏写号段，代码写好了却从没执行。这里把两件事收到一处：
/// 每个读取器仍然自己回答"认不认"，这里只做聚合，不复制任何条件。
///
/// 注意：IsKnown 保持与旧 CanQuery 完全相同的放行集合，只用于"是否允许发查询"；
/// Find 是更宽的设备路由表，供分类与通道显示使用，两者语义不同，不要合并。
/// </summary>
internal static class DeviceRoutes
{
    internal sealed class Route
    {
        public string Reader;
        public DeviceKind Kind;
        public string Transport;   // null = 保持原推断
    }

    /// <summary>允许对这台设备发厂商查询。等同于旧 RecognitionPolicy.CanQuery。</summary>
    internal static bool IsKnown(ushort vid, ushort pid, string product) =>
        VgnF1Protocol.IsCandidate(vid, pid, product) ||
        F75Protocol.IsCandidate(vid, pid, product) ||
        M9ProProtocol.IsCandidate(vid, pid, product) ||
        RkKeyboardProtocol.IsCandidate(vid, pid) ||
        VendorProtocol.IsRogOmni(vid, pid) ||
        VendorProtocol.IsScopedModernMouse(vid, pid, product) ||
        ReferenceProfiles.Find(vid, pid) != null ||
        ZeroRainyProtocol.Candidate(vid, pid, product) ||
        VendorProtocol.IsScopedG7(vid, pid, product) ||
        VendorProtocol.IsScopedInphic(vid, pid, product) ||
        VendorProtocol.IsReportedX23Se(vid, pid, product) ||
        OriginalGpwProtocol.IsCandidate(vid, pid) ||
        // 248A:5B2F 的官方 GetInfo 是独立路由，公版 33 字节旁路已让出这个号段。
        M137FeatureProtocol.IsCandidate(vid, pid, product) ||
        // 凌豹 M9 Pro：RealTek 系 FF00/0001 Feature 64，官方 0x11 0x06 只读电量。
        VendorProtocol.IsRealTekShape(vid, pid) ||
        // Logitech HID++ (non-GPW receivers such as 046D:C53F) needs the probe lane too;
        // the GPW-specific model keeps its own dedicated route above.
        (vid == 0x046D && !OriginalGpwProtocol.IsCandidate(vid, pid)) ||
        (vid != 0 && pid != 0 && VendorProtocol.IsAulaSc630(product)) ||
        VendorProtocol.IsGenericReceiver33(vid, pid, product) ||
        (vid == 0x089D && pid == 0x062F) ||
        (vid == 0x391D && pid == 0x2A05) ||
        // Generic vendor-collection routes: the vendor reader still validates the reply frame.
        (VendorProtocol.IsVgnVid(vid) && pid != 0) ||
        (VendorProtocol.IsAjazzVid(vid) && VendorProtocol.IsAjazzPid(pid)) ||
        (vid == 0x3837 && VendorProtocol.IsMchoseA7V2(vid, pid)) ||
        // K87 V3 是键盘。PR #7 的 K87DiscoveryRegression 明确要求它可从常规发现流程可达；
        // 这里补回放行，实际读数仍优先走 HidDiscovery.K87 的按实例读取器。
        MchoseKeyboardProtocol.IsSupportedDevice(vid, pid, product) ||
        (vid == 0x5253 && pid == 0x1021);

    /// <summary>
    /// 设备路由表：VID:PID 命中已知读取器时直接给出类型与通道。
    /// 分类不再依赖"能看见哪些集合"，厂商驱动独占鼠标集合也不会把鼠标判成键盘。
    /// 未命中返回 null，由 RefineKind 的集合计数兜底。
    /// </summary>
    internal static Route Find(ushort vid, ushort pid, string product)
    {
        // 3554:F503 / 3554:FA09 有精确产品串与精确集合要求，必须排在通用 33 字节旁路之前，
        // 否则会被公版接收器协议抢先认领。
        if (VgnF1Protocol.IsCandidate(vid, pid, product))
            return new Route { Reader = "VgnF1", Kind = DeviceKind.Mouse, Transport = "2.4G" };
        if (F75Protocol.IsCandidate(vid, pid, product))
            return new Route { Reader = "AulaF75", Kind = DeviceKind.Keyboard, Transport = "2.4G" };
        // 凌豹 M9 Pro 走被动 Input9，优先于 RealTek 的 0x11 0x06 主动查询。
        if (M9ProProtocol.IsCandidate(vid, pid, product))
            return new Route { Reader = "M9Pro", Kind = DeviceKind.Mouse, Transport = "2.4G" };
        if (VendorProtocol.IsRogOmni(vid, pid))
            return new Route { Reader = "RogScope", Kind = DeviceKind.Keyboard, Transport = "2.4G" };
        if (VendorProtocol.IsMchoseA7V2(vid, pid))
            return new Route { Reader = "MchoseA7V2", Kind = DeviceKind.Mouse, Transport = "2.4G" };
        if (VendorProtocol.IsMchoseModern(vid, pid))
            return new Route { Reader = "MchoseModern", Kind = DeviceKind.Mouse, Transport = "2.4G" };
        if (M137FeatureProtocol.IsCandidate(vid, pid, product))
            return new Route { Reader = "M137", Kind = DeviceKind.Mouse, Transport = "2.4G" };
        if (VendorProtocol.IsRealTekShape(vid, pid))
            return new Route { Reader = "RealTek", Kind = DeviceKind.Mouse, Transport = "2.4G" };
        if (VendorProtocol.IsGenericReceiver33(vid, pid, product))
            return new Route { Reader = "Receiver33", Kind = DeviceKind.Mouse, Transport = "2.4G" };
        if (VendorProtocol.IsScopedInphic(vid, pid, product))
            return new Route { Reader = "Inphic", Kind = DeviceKind.Mouse, Transport = null };
        if (VendorProtocol.IsScopedModernMouse(vid, pid, product))
            return new Route { Reader = "MchoseModernScoped", Kind = DeviceKind.Mouse, Transport = "2.4G" };
        if (VendorProtocol.IsScopedG7(vid, pid, product))
            return new Route { Reader = "MchoseG7", Kind = DeviceKind.Mouse, Transport = null };
        // 373B:1154/1155/124F 是共享接收器号段：只有产品名明确 ATK ZERO 才归鼠标，
        // 8K 接收器上的 "Wireless Gaming Keyboard" 不能被这条路由抢走。
        if (VendorProtocol.IsAtkZeroBitMouse(vid, pid) &&
            (string.Equals(product, "ATK ZERO", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(product, "ATK ZERO 2.4G Receiver", StringComparison.OrdinalIgnoreCase)))
            return new Route { Reader = "AtkZero", Kind = DeviceKind.Mouse, Transport = null };
        if (VendorProtocol.IsRainy75Candidate(vid, pid) && VendorProtocol.IsRainy75Name(product))
            return new Route { Reader = "Rainy75", Kind = DeviceKind.Keyboard, Transport = null };
        if (VendorProtocol.IsReportedX23Se(vid, pid, product))
            return new Route { Reader = "X23Se", Kind = DeviceKind.Mouse, Transport = "2.4G" };
        if (OriginalGpwProtocol.IsCandidate(vid, pid))
            return new Route { Reader = "OriginalGpw", Kind = DeviceKind.Mouse, Transport = null };
        if (ReferenceProfiles.Find(vid, pid) != null)
            return new Route { Reader = "Reference", Kind = DeviceKind.Mouse, Transport = "2.4G" };
        if (vid != 0 && pid != 0 && VendorProtocol.IsAulaSc630(product))
            return new Route { Reader = "AulaSc630", Kind = DeviceKind.Mouse, Transport = "2.4G" };
        if (VendorProtocol.IsMchoseV9Pro(vid, pid, product))
            return new Route { Reader = "MchoseV9Pro", Kind = DeviceKind.Headset, Transport = null };
        if (VendorProtocol.IsRapooVt7(vid, pid))
            return new Route { Reader = "RapooVt7", Kind = DeviceKind.Mouse, Transport = null };
        if (VendorProtocol.IsAtkV75X(vid, pid, product))
            return new Route { Reader = "AtkV75X", Kind = DeviceKind.Keyboard, Transport = null };
        if (VendorProtocol.IsAulaF99(vid, pid, product))
            return new Route { Reader = "AulaF99", Kind = DeviceKind.Keyboard, Transport = null };
        if (MchoseKeyboardProtocol.IsSupportedDevice(vid, pid, product))
            return new Route { Reader = "MchoseKeyboard", Kind = DeviceKind.Keyboard, Transport = null };        return null;
    }
}
