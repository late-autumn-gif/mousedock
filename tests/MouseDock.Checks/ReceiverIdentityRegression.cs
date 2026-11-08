using System;
using MouseDock.Services.Detection;

/// <summary>
/// 248A:5B2F 身份诊断的回归。
///
/// 来源：合作者 PR #8 的 ReceiverIdentityRegression，按当前 ReceiverIdentityProtocol 重写。
/// 旧测试跑完整 Capture 流程（需要真机句柄），这里改为锁协议常量与解析规则——
/// 这两部分错了，真机上也一定读不到身份串。
/// </summary>
internal static class ReceiverIdentityRegression
{
    internal static void Run()
    {
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        // 只认这一对 VID:PID。
        Check(ReceiverIdentityProtocol.IsCandidate(0x248A, 0x5B2F),
            "248A:5B2F 必须被识别为身份诊断目标。");
        Check(!ReceiverIdentityProtocol.IsCandidate(0x248A, 0x5B30),
            "相邻 PID 不能误入身份诊断。");
        Check(!ReceiverIdentityProtocol.IsCandidate(0x093A, 0x5B2F),
            "相同 PID 配别的 VID 不能误入。");

        // 通道必须是 FF00/0001、输入输出都为 0、Feature 33。
        Check(ReceiverIdentityProtocol.MatchesChannel(0x248A, 0x5B2F, 0xFF00, 1, 0, 0, 33),
            "FF00/0001 Feature33 才是身份通道。");
        Check(!ReceiverIdentityProtocol.MatchesChannel(0x248A, 0x5B2F, 0xFF00, 1, 0, 0, 32),
            "Feature 长度不是 33 时不能走身份通道。");
        Check(!ReceiverIdentityProtocol.MatchesChannel(0x248A, 0x5B2F, 0xFF02, 1, 0, 0, 33),
            "FF02 是电量通道，不是身份通道。");
        Check(!ReceiverIdentityProtocol.MatchesChannel(0x248A, 0x5B2F, 0xFF00, 1, 33, 0, 33),
            "带输入报表的集合不是身份通道。");
        Check(!ReceiverIdentityProtocol.MatchesChannel(0x248A, 0x5B2F, 0xFF00, 2, 0, 0, 33),
            "usage 非 1 时不能走身份通道。");

        // 请求帧与 M137 的 GetInfo 同源：12 01 20 00 seq，其余全零，总长 33。
        var frame = ReceiverIdentityProtocol.Request(0x07);
        Check(frame.Length == 33, "身份请求帧必须是 33 字节。");
        Check(frame[0] == 0x0C && frame[1] == 0x01 && frame[2] == 0x20 && frame[3] == 0x00,
            "帧头必须是 0C 01 20 00（PR 原文写十进制 12，即 0x0C）。");
        Check(frame[4] == 0x07, "第五字节是调用方给的序号。");
        for (int i = 5; i < frame.Length; i++)
            Check(frame[i] == 0, $"第 {i} 字节必须为零。");

        // 序号变化只影响第五字节。
        var other = ReceiverIdentityProtocol.Request(0x08);
        Check(other[4] == 0x08 && other[0] == frame[0],
            "序号之外的字节不能随序号变化。");

        // 身份串解析：从偏移 5 起取可打印 ASCII，遇到 0 停。
        var ascii = new byte[33];
        ascii[5] = (byte)'A'; ascii[6] = (byte)'J'; ascii[7] = (byte)'A';
        ascii[8] = (byte)'Z'; ascii[9] = (byte)'Z';
        Check(ReceiverIdentityProtocol.DecodeIdentity(ascii) == "AJAZZ",
            "连续的 ASCII 片段应当解析为身份串。");

        var zeroed = new byte[33];
        Check(ReceiverIdentityProtocol.DecodeIdentity(zeroed) == null,
            "全零缓冲区不能产出身份串。");

        var shortText = new byte[33];
        shortText[5] = (byte)'A'; shortText[6] = (byte)'B';
        Check(ReceiverIdentityProtocol.DecodeIdentity(shortText) == null,
            "短于三个字符的片段不算身份串。");

        var padded = new byte[33];
        padded[5] = (byte)'X'; padded[6] = (byte)'Y'; padded[7] = (byte)'Z'; padded[8] = 0;
        Check(ReceiverIdentityProtocol.DecodeIdentity(padded) == "XYZ",
            "遇到零字节必须停止，后面的残留字节不能混进来。");

        Check(ReceiverIdentityProtocol.DecodeIdentity(null) == null,
            "空缓冲区必须安全返回 null。");
        Check(ReceiverIdentityProtocol.DecodeIdentity(new byte[3]) == null,
            "过短的缓冲区必须安全返回 null。");
    }
}

