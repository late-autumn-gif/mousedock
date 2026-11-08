using System;

namespace MouseDock.Services.Detection;

/// <summary>
/// RealTek 系游戏外设的只读命令集。
///
/// 依据：迈从官方网页驱动 <c>main-DCRwBIvm.js</c> 的 <c>readMap</c>：
/// <code>
/// mouseDeviceReadOrder = "11 06 " + 19 个 00        （21 字节）
/// mouseDeviceReadParser = uint16 vid, uint16 pid, uint32 fwVersion,
///                         bit3 connectMode, bit1 connectStatus, bit4 inReserved,
///                         uint8 batteryLevel, uint8 chargeStatus
/// mouseAllReadOrder    = "12 67 " + 63 个 00        （65 字节）
/// </code>
/// 该驱动的 <c>readCommand</c> 会把 Feature 回包前两字节当作命令头剥掉，
/// 再对剩余字节逐字节 <c>^0xFF</c> 解码后交给 parser。
///
/// 这里只做帧与解析，不发 I/O。
/// </summary>
internal static class RealTekHidProtocol
{
    internal const int BatteryRequestLength = 21;
    internal const int AllInfoRequestLength = 65;

    internal static byte[] BatteryRequest()
    {
        var frame = new byte[BatteryRequestLength];
        frame[0] = 0x11;
        frame[1] = 0x06;
        return frame;
    }

    internal static bool IsBatteryRequest(ReadOnlySpan<byte> frame) =>
        frame.Length >= 2 && frame[0] == 0x11 && frame[1] == 0x06;

    internal static byte[] AllInfoRequest()
    {
        var frame = new byte[AllInfoRequestLength];
        frame[0] = 0x12;
        frame[1] = 0x67;
        return frame;
    }

    /// <summary>
    /// 解码官方 <c>mouseDeviceRead</c> 回包。
    /// <paramref name="response"/> 是 HidD_GetFeature 拿到的原始缓冲区；
    /// 前两个字节是命令头，之后的字节取反后才是字段。
    /// </summary>
    internal static bool TryParseBattery(byte[] response, ushort expectVid, out int level, out bool charging,
        out string diagnostic)
    {
        level = -1;
        charging = false;
        diagnostic = "RealTek 电量回包形状不符";
        if (response == null || response.Length < 13) return false;

        var body = new byte[response.Length - 2];
        for (int i = 0; i < body.Length; i++) body[i] = (byte)(response[i + 2] ^ 0xFF);

        int vid = body[0] | (body[1] << 8);
        int pid = body[2] | (body[3] << 8);
        int status = body[8];
        int battery = body[9];
        int charge = body[10];

        if (vid == 0 || pid == 0) return false;
        if (battery is < 0 or > 100)
        {
            diagnostic = $"RealTek 电量 {battery} 超出 0..100（VID={vid:X4} PID={pid:X4}）";
            return false;
        }
        if (expectVid != 0 && vid != expectVid)
        {
            diagnostic = $"RealTek 回包 VID={vid:X4} 与设备 {expectVid:X4} 不符";
            return false;
        }

        level = battery;
        charging = charge == 1;
        diagnostic = $"RealTek 0x11 0x06 电量={battery}% 充电={(charging ? "是" : "否")} " +
                     $"VID={vid:X4} PID={pid:X4} 状态=0b{Convert.ToString(status, 2).PadLeft(8, '0')}";
        return true;
    }
}
