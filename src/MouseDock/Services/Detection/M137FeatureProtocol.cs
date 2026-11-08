using System;
using System.Text;

namespace MouseDock.Services.Detection;

/// <summary>
/// 248A:5B2F 接收器的官方 GetInfo 身份查询。
///
/// 依据：PR #8（GitHub late-autumn-gif/mousedock）提供的 AJAZZ Driver (X) 1.0.5.3
/// 反汇编，GetInfo 在 0x44B250：33 字节 Feature 请求，report id 12，
/// 帧头 <c>12 01 20 00 seq</c>，回包 <c>[8..11]</c> 是四字节身份串，
/// 电量在 <c>[18]</c>。同一号段被多个品牌共用，身份串决定型号：
/// M137=狼蛛 SC580、M139=黑爵 AJ139 Pro、M620=黑爵 AJ159、M184=黑爵 AJ179。
///
/// 这里只做帧与解析，不发 I/O。
/// </summary>
internal static class M137FeatureProtocol
{
    internal const byte ReportId = 12;
    internal const int FrameLength = 33;

    /// <summary>
    /// 只认 248A:5B2F 且产品串为 Wireless-Receiver 的实例。
    /// 产品串是别人时可能是同号段的其它接收器，不抢。
    /// </summary>
    internal static bool IsCandidate(ushort vid, ushort pid, string product) =>
        vid == 0x248A && pid == 0x5B2F &&
        string.Equals(product?.Trim(), "Wireless-Receiver", StringComparison.OrdinalIgnoreCase);

    /// <summary>官方集合：FF00/0001，无输入无输出，Feature 报告 33 字节。</summary>
    internal static bool MatchesChannel(ushort vid, ushort pid, ushort page, ushort usage,
        int input, int output, int feature) =>
        vid == 0x248A && pid == 0x5B2F && page == 0xFF00 && usage == 1 &&
        input == 0 && output == 0 && feature == FrameLength;

    internal static byte[] Request(byte sequence)
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = 1;
        frame[2] = 32;
        frame[4] = sequence;
        return frame;
    }

    /// <summary>只允许官方 GetInfo 请求形状，防止误发配置命令。</summary>
    internal static bool IsRequest(ReadOnlySpan<byte> frame, byte sequence)
    {
        if (frame.Length != FrameLength || frame[0] != ReportId || frame[1] != 1 ||
            frame[2] != 32 || frame[3] != 0 || frame[4] != sequence) return false;
        for (int i = 5; i < frame.Length; i++) if (frame[i] != 0) return false;
        return true;
    }

    internal static bool TryIdentity(byte[] frame, out string identity)
    {
        identity = null;
        if (frame == null || frame.Length != FrameLength) return false;
        if (frame[0] != ReportId || frame[1] != 1 || frame[2] != 32 || frame[3] != 0) return false;
        if (frame[8] != (byte)'M') return false;
        for (int i = 9; i < 12; i++) if (frame[i] < '0' || frame[i] > '9') return false;
        identity = Encoding.ASCII.GetString(frame, 8, 4);
        return true;
    }

    /// <summary>电量偏移 18，0..100；身份不符时整体拒绝。</summary>
    internal static bool TryPercent(byte[] frame, out int level, out string identity)
    {
        level = -1;
        if (!TryIdentity(frame, out identity)) return false;
        if (frame[18] > 100) return false;
        level = frame[18];
        return true;
    }

    internal static string ModelForIdentity(string identity) => identity switch
    {
        "M137" => "狼蛛 SC580",
        "M139" => "黑爵 AJ139 Pro",
        "M620" => "黑爵 AJ159",
        "M184" => "黑爵 AJ179",
        _ => null
    };

    internal static string DisplayForIdentity(string identity)
    {
        string model = ModelForIdentity(identity);
        return model ?? (string.IsNullOrEmpty(identity) ? "248A:5B2F 接收器" : "接收器（身份 " + identity + "，型号未收录）");
    }
}
