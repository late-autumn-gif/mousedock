using System;
using System.IO;
using System.Security.Cryptography;

namespace MouseDock.Services.Detection;

/// <summary>
/// VGN 蜻蜓 F1 MOBA（3554:F503，产品串 "VGN Mouse 2.4G Receiver"）。
///
/// 协议来源：用户提供的 1.1.1+bac923f 构建里 <c>VgnF1Protocol</c> 的反编译结果，
/// 该构建已在真机上跑通这款鼠标。厂商没有公开文档，这是唯一的实现依据。
///
/// 帧：17 字节，<c>[0]=0x08</c>、<c>[1]=命令</c>，<c>[16] = 0x55 - sum([0..15])</c>。
/// 命令 1 = 身份（[5]=8，[6..9] 随机，应答 [10],[11] 是 CID/MID），
/// 命令 3 = 在线状态、命令 4 = 电量；应答 <c>[6]</c> 分别给状态与百分比，<c>[7]</c> 是充电位。
/// </summary>
internal static class VgnF1Protocol
{
    internal const string Model = "VGN F1 MOBA";
    internal const string Product = "VGN Mouse 2.4G Receiver";

    internal static bool IsCandidate(ushort vid, ushort pid, string product) =>
        vid == 0x3554 && pid == 0xF503 &&
        string.Equals(product?.Trim(), Product, StringComparison.OrdinalIgnoreCase);

    internal static bool MatchesChannel(ushort vid, ushort pid, string product, ushort page, ushort usage,
        int input, int output, int feature) =>
        IsCandidate(vid, pid, product) && MatchesSignature(page, usage, input, output, feature);

    /// <summary>只校验集合签名，不校验产品串（读取阶段拿不到产品名）。</summary>
    internal static bool MatchesSignature(ushort page, ushort usage, int input, int output, int feature) =>
        page == 0xFF02 && usage == 2 && input == 17 && output == 17 && feature == 0;

    /// <summary>身份应答里 [10],[11] 拼出的 CID/MID；这三个值才确认是 MOBA。</summary>
    internal static bool IsMobaIdentity(string identity) => identity is "2,5" or "2,6" or "2,33";

    private static byte[] Command(byte command)
    {
        if (command is not (3 or 4)) throw new ArgumentOutOfRangeException(nameof(command));
        var frame = new byte[17];
        frame[0] = 8;
        frame[1] = command;
        SetChecksum(frame);
        return frame;
    }

    private static byte[] IdentityCommand()
    {
        var frame = new byte[17];
        frame[0] = 8;
        frame[1] = 1;
        frame[5] = 8;
        RandomNumberGenerator.Fill(frame.AsSpan(6, 4));
        SetChecksum(frame);
        return frame;
    }

    private static void SetChecksum(byte[] frame)
    {
        int sum = 0;
        for (int i = 0; i < 16; i++) sum += frame[i];
        frame[16] = (byte)(85 - sum);
    }

    internal static bool ValidReply(ReadOnlySpan<byte> reply, byte[] request)
    {
        if (reply.Length != 17 || reply[0] != 8 || reply[1] != request[1] ||
            reply[2] != 0 || reply[3] != 0 || reply[4] != 0 || reply.SequenceEqual(request)) return false;
        int sum = 0;
        for (int i = 0; i < reply.Length; i++) sum += reply[i];
        return (sum & 0xFF) == 85;
    }

    private static byte[] Exchange(ReferenceBatteryProtocol.ILink link, byte[] request, long deadline, int timeoutMs)
    {
        long until = Math.Min(deadline, link.Now + timeoutMs);
        bool drained = false;
        for (int i = 0; i < 32; i++)
        {
            if (link.Now >= until) break;
            byte[] stale = link.Read(1);
            if (stale == null || stale.Length <= 0)
            {
                drained = true;
                break;
            }
        }
        if (!drained) throw new IOException("旧输入队列未清空");
        if (link.Now >= until || !link.Write(request, (int)Math.Min(300L, until - link.Now)))
            throw new IOException("只读查询未完整发送");
        for (int i = 0; i < 128; i++)
        {
            if (link.Now >= until) break;
            byte[] reply = link.Read((int)Math.Max(1L, Math.Min(100L, until - link.Now)));
            if (reply != null && ValidReply(reply, request)) return reply;
        }
        return null;
    }

    internal static ReferenceBatteryProtocol.Reading Read(ReferenceBatteryProtocol.ILink link)
    {
        try
        {
            long deadline = link.Now + 3500;
            string identity = null;
            string model = null;
            byte[] hello = Exchange(link, IdentityCommand(), deadline, 650);
            if (hello != null && hello[5] == 8 && hello[12] <= 5)
            {
                identity = hello[10] + "," + hello[11];
                if (IsMobaIdentity(identity)) model = Model;
            }
            byte[] online = Exchange(link, Command(3), deadline, 800);
            if (online == null || online[6] != 1)
                return ReferenceBatteryProtocol.Reading.Failed("VGN F1 未取得本轮在线响应，未读取电量");
            byte[] battery = Exchange(link, Command(4), deadline, 800);
            if (battery == null || battery[6] > 100)
                return ReferenceBatteryProtocol.Reading.Failed("VGN F1 电量响应无效或超时");
            byte[] confirm = Exchange(link, Command(3), deadline, 800);
            if (confirm == null || confirm[6] != 1 || !online.AsSpan(7, 3).SequenceEqual(confirm.AsSpan(7, 3)))
                return ReferenceBatteryProtocol.Reading.Failed("VGN F1 电量查询前后在线状态或配对地址不一致");
            bool charging = battery[7] == 1;
            string diagnostic = "VGN F1 本轮在线/电量/在线响应，配对地址一致；CID/MID=" + (identity ?? "未取得") +
                "；RX=" + Convert.ToHexString(online) + "/" + Convert.ToHexString(battery) + "/" + Convert.ToHexString(confirm);
            return new ReferenceBatteryProtocol.Reading(battery[6], charging, true,
                charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
                diagnostic, model, identity);
        }
        catch (Exception ex)
        {
            return ReferenceBatteryProtocol.Reading.Failed("VGN F1 查询失败：" + ex.Message);
        }
    }
}

