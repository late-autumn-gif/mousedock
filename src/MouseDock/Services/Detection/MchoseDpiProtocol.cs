using System;
using System.Text;

namespace MouseDock.Services.Detection;

/// <summary>
/// 迈从（MCHOSE）鼠标 DPI 读取。
///
/// 依据：官方网页驱动 <c>main-DCRwBIvm.js</c> 的 <c>readMap</c>：
/// <code>"0x12 0x67": {order: mouseAllReadOrder, parser: mouseAllReadParser}</code>
/// <code>mouseAllReadOrder = "12 67 " + 63 个 00</code>，64 字节 Feature 请求；
/// 回包按小端解析：
/// <code>
/// profileIndex uint8
/// gDpiIndex bit4, gRateIndex bit4
/// usbDpiIndex bit4, usbRateIndex bit4
/// reserved uint8
/// dpi0..dpi5 uint16 × 6
/// dpiSum uint8
/// sensor uint8
/// keyDebounce uint8
/// sleep uint8
/// </code>
///
/// 只做读取，不发写入命令：写入走官方配置表，命令字节未取到，不猜。
/// </summary>
internal static class MchoseDpiProtocol
{
    internal const int FrameLength = 64;

    internal static byte[] Request()
    {
        var frame = new byte[FrameLength];
        frame[0] = 0x12;
        frame[1] = 0x67;
        return frame;
    }

    internal static bool IsRequest(ReadOnlySpan<byte> frame) =>
        frame.Length == FrameLength && frame[0] == 0x12 && frame[1] == 0x67;

    /// <summary>
    /// 解析 allInfo。返回 false 表示这不是 allInfo 回包（形状不对或档位数非法）。
    /// <paramref name="index"/> 是当前生效的档位，<paramref name="dpi"/> 是对应 DPI。
    /// </summary>
    internal static bool TryParse(byte[] frame, bool wired, out int dpi, out int index, out int slots,
        out string diagnostic)
    {
        dpi = -1;
        index = -1;
        slots = 0;
        diagnostic = "MCHOSE allInfo 未采用";
        if (frame == null || frame.Length < FrameLength) return false;
        if (frame[0] != 0x12 || frame[1] != 0x67) return false;

        int profile = frame[2];
        int gIndex = frame[3] & 0x0F;
        int gRate = (frame[3] >> 4) & 0x0F;
        int usbIndex = frame[4] & 0x0F;
        int usbRate = (frame[4] >> 4) & 0x0F;
        int sum = frame[18];
        int sensor = frame[19];

        if (sum is < 1 or > 6) return false;
        slots = sum;

        var values = new int[6];
        for (int i = 0; i < 6; i++)
            values[i] = frame[6 + i * 2] | (frame[7 + i * 2] << 8);

        int active = wired ? usbIndex : gIndex;
        if (active < 0 || active >= sum) return false;
        int current = values[active];
        // 官方常见范围 50..32000；超出说明解析位置不对，宁可不显示。
        if (current is < 50 or > 32000) return false;

        dpi = current;
        index = active;
        var text = new StringBuilder("MCHOSE allInfo 0x12 0x67");
        text.Append(" 档位=").Append(active + 1).Append('/').Append(sum);
        text.Append(" DPI=").Append(current);
        text.Append(" 轮询=").Append((wired ? usbRate : gRate) + 1);
        text.Append(" profile=").Append(profile);
        text.Append(" sensor=").Append(sensor);
        diagnostic = text.ToString();
        return true;
    }

    /// <summary>把各档位一起给界面用，顺序固定 6 个，未使用的位置填 -1。</summary>
    internal static int[] ReadSlots(byte[] frame)
    {
        var values = new int[6];
        for (int i = 0; i < 6; i++) values[i] = -1;
        if (frame == null || frame.Length < FrameLength) return values;
        int sum = frame[18];
        if (sum is < 1 or > 6) return values;
        for (int i = 0; i < sum; i++) values[i] = frame[6 + i * 2] | (frame[7 + i * 2] << 8);
        return values;
    }
}
