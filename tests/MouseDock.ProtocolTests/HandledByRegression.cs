using MouseDock.Models;
using MouseDock.Services.Detection;

// HandledBy 收敛后的路由状态断言。全部是合成 Bucket，不枚举、不碰硬件。
internal static class HandledByRegression
{
    public static int Run()
    {
        int count = 0;
        void Check(bool ok, string why)
        {
            count++;
            if (!ok) throw new Exception("HandledBy: " + why);
        }

        // 公版 33 字节接收器：没读到身份帧时品牌兜底，类型是鼠标
        var inphic = new HidDiscovery.Bucket
        {
            Vid = 0x248A, Pid = 0x5C2F, Product = "Wireless-Receiver",
            Kind = DeviceKind.Keyboard, HandledBy = HandledBy.Receiver33
        }.ToDevice();
        Check(inphic != null && inphic.Name == "英菲克 2.4G 接收器（型号未确认）" &&
              inphic.Kind == DeviceKind.Mouse && inphic.Transport == "2.4G",
            "Receiver33 without identity keeps brand, mouse kind and 2.4G");

        var inphicNamed = new HidDiscovery.Bucket
        {
            Vid = 0x248A, Pid = 0x5C2F, Product = "Wireless-Receiver",
            Kind = DeviceKind.Keyboard, HandledBy = HandledBy.Receiver33,
            AulaModel = "英菲克 J315", AulaOnline = true
        }.ToDevice();
        Check(inphicNamed != null && inphicNamed.Name == "英菲克 J315" &&
              inphicNamed.ConfirmedModel == "英菲克 J315",
            "Receiver33 confirmed identity stays the displayed model");

        var aula = new HidDiscovery.Bucket
        {
            Vid = 0x089D, Pid = 0x062F, Product = "Wireless-Receiver",
            Kind = DeviceKind.Keyboard, HandledBy = HandledBy.Receiver33
        }.ToDevice();
        Check(aula != null && aula.Name == "狼蛛 2.4G 接收器（型号未确认）" &&
              aula.Kind == DeviceKind.Mouse,
            "AULA brand fallback follows the VID, not the shared product string");

        // 英菲克老协议：Inphic 专属路由，通道按 2.4G
        var inphicLegacy = new HidDiscovery.Bucket
        {
            Vid = 0x248A, Pid = 0x5C2F, Product = "IN9",
            Kind = DeviceKind.Mouse, HandledBy = HandledBy.Inphic
        }.ToDevice();
        Check(inphicLegacy != null && inphicLegacy.Kind == DeviceKind.Mouse &&
              inphicLegacy.Transport == "2.4G",
            "Inphic route keeps mouse kind and 2.4G transport");

        // ROG Omni：配对成功走键盘，型号用解析出的名字
        var rog = new HidDiscovery.Bucket
        {
            Vid = 0x0B05, Pid = 0x1ACE, Product = "ROG OMNI RECEIVER",
            Kind = DeviceKind.Keyboard, HandledBy = HandledBy.RogScope,
            RogScopeModel = "ROG 游侠2 RX 无线版"
        }.ToDevice();
        Check(rog != null && rog.Kind == DeviceKind.Keyboard &&
              rog.Transport == "2.4G" && rog.Name == "ROG 游侠2 RX 无线版",
            "RogScope paired keeps keyboard, 2.4G and the resolved model");

        // ROG 有线型号：没有配对，但 TryCollect 记了型号
        var rogWired = new HidDiscovery.Bucket
        {
            Vid = 0x0B05, Pid = 0x1AB5, Product = "ROG STRIX SCOPE II RX",
            Kind = DeviceKind.Keyboard, RogScopeModel = "ROG 游侠2 RX"
        }.ToDevice();
        Check(rogWired != null && rogWired.Kind == DeviceKind.Keyboard &&
              rogWired.Transport == "有线" && rogWired.Name == "ROG 游侠2 RX",
            "RogScope wired model without pairing stays wired");

        // 迈从 Modern / G7：模型为 null 时回退产品串
        var modern = new HidDiscovery.Bucket
        {
            Vid = 0x3837, Pid = 0x1018, Product = "MCHOSE A7 V3",
            Kind = DeviceKind.Mouse, HandledBy = HandledBy.MchoseModern
        }.ToDevice();
        Check(modern != null && modern.Kind == DeviceKind.Mouse && modern.Name == "MCHOSE A7 V3",
            "Modern route falls back to the product name when the model is unknown");

        var g7 = new HidDiscovery.Bucket
        {
            Vid = 0x3837, Pid = 0x1014, Product = "MCHOSE G7",
            Kind = DeviceKind.Mouse, HandledBy = HandledBy.MchoseG7
        }.ToDevice();
        Check(g7 != null && g7.Kind == DeviceKind.Mouse && g7.Name == "MCHOSE G7",
            "G7 route falls back to the product name when the model is unknown");

        // ZeroRainy：373B:1155 共享接收器，鼠标型
        var zero = new HidDiscovery.Bucket
        {
            Vid = 0x373B, Pid = 0x1155, Product = "8K Wireless Receiver",
            Kind = DeviceKind.Mouse, HandledBy = HandledBy.ZeroRainy
        }.ToDevice();
        Check(zero != null && zero.Kind == DeviceKind.Mouse,
            "ZeroRainy route keeps the mouse kind on a shared receiver PID");

        // GPW 一代：读到型号但外设未确认时，PeripheralConfirmed 必须为 false
        var gpw = new HidDiscovery.Bucket
        {
            Vid = 0x046D, Pid = 0xC539, Product = "USB Receiver",
            Kind = DeviceKind.Mouse, HandledBy = HandledBy.OriginalGpw,
            GpwModel = "Logitech G Pro Wireless", GpwLive = false, Battery = 93
        }.ToDevice();
        Check(gpw != null && gpw.Kind == DeviceKind.Mouse && !gpw.PeripheralConfirmed,
            "GPW route without a live sample is not peripheral-confirmed");

        // AULA 专属：读到身份才算外设确认
        var sc580 = new HidDiscovery.Bucket
        {
            Vid = 0x089D, Pid = 0x062F, Product = "Wireless-Receiver",
            Kind = DeviceKind.Mouse, HandledBy = HandledBy.Aula,
            AulaModel = "狼蛛 SC580", AulaOnline = true, Battery = 82
        }.ToDevice();
        Check(sc580 != null && sc580.Kind == DeviceKind.Mouse &&
              sc580.PeripheralConfirmed && sc580.Name == "狼蛛 SC580",
            "AULA route with a live identity is peripheral-confirmed");

        // 没有任何读取器接管：类型按集合计数兜底
        var unknown = new HidDiscovery.Bucket
        {
            Vid = 0x1234, Pid = 0x5678, Product = "USB Receiver",
            Kind = DeviceKind.Keyboard, HandledBy = HandledBy.None
        };
        unknown.Counts[DeviceKind.Keyboard] = 1;
        Check(unknown.ToDevice() is { Kind: DeviceKind.Keyboard },
            "None keeps the collection-count fallback");

        return count;
    }
}
