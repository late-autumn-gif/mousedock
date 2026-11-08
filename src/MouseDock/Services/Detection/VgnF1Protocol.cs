using System;
using System.IO;
using System.Security.Cryptography;

namespace MouseDock.Services.Detection;

internal static class VgnF1Protocol
{
    internal const string Model = "VGN F1 MOBA";
    internal const string Product = "VGN Mouse 2.4G Receiver";

    internal static bool IsCandidate(ushort vid, ushort pid, string product) =>
        vid == 0x3554 && pid == 0xF503 &&
        string.Equals(product?.Trim(), Product, StringComparison.OrdinalIgnoreCase);

    internal static bool MatchesChannel(ushort vid, ushort pid, string product,
        ushort page, ushort usage, int input, int output, int feature) =>
        IsCandidate(vid, pid, product) && page == 0xFF02 && usage == 2 &&
        input == 17 && output == 17 && feature == 0;

    internal static bool IsMobaIdentity(string identity) => identity is "2,5" or "2,6" or "2,33";

    internal static byte[] Command(byte command)
    {
        if (command is not (3 or 4)) throw new ArgumentOutOfRangeException(nameof(command));
        var bytes = new byte[17];
        bytes[0] = 8; bytes[1] = command;
        SetChecksum(bytes);
        return bytes;
    }

    private static byte[] IdentityCommand()
    {
        var bytes = new byte[17];
        bytes[0] = 8; bytes[1] = 1; bytes[5] = 8;
        RandomNumberGenerator.Fill(bytes.AsSpan(6, 4));
        SetChecksum(bytes);
        return bytes;
    }

    private static void SetChecksum(byte[] bytes)
    {
        int sum = 0;
        for (int i = 0; i < 16; i++) sum += bytes[i];
        bytes[16] = unchecked((byte)(0x55 - sum));
    }

    private static bool ValidReply(ReadOnlySpan<byte> bytes, byte[] request)
    {
        if (bytes.Length != 17 || bytes[0] != 8 || bytes[1] != request[1] ||
            bytes[2] != 0 || bytes[3] != 0 || bytes[4] != 0 ||
            bytes.SequenceEqual(request)) return false;
        int sum = 0;
        foreach (byte value in bytes) sum += value;
        return (sum & 255) == 0x55;
    }

    private static byte[] Exchange(ReferenceBatteryProtocol.ILink link, byte[] request,
        long deadline, int timeoutMs)
    {
        long until = Math.Min(deadline, link.Now + timeoutMs);
        bool drained = false;
        for (int i = 0; i < 32 && link.Now < until; i++)
            if (link.Read(1) is not { Length: > 0 }) { drained = true; break; }
        if (!drained) throw new IOException("旧输入队列未清空");
        if (link.Now >= until || !link.Write(request, (int)Math.Min(300, until - link.Now)))
            throw new IOException("只读查询未完整发送");
        for (int i = 0; i < 128 && link.Now < until; i++)
        {
            var bytes = link.Read((int)Math.Max(1, Math.Min(100, until - link.Now)));
            if (bytes != null && ValidReply(bytes, request)) return bytes;
        }
        return null;
    }

    internal static ReferenceBatteryProtocol.Reading Read(ReferenceBatteryProtocol.ILink link)
    {
        try
        {
            long deadline = link.Now + 3500;
            string identity = null, model = null;
            var identified = Exchange(link, IdentityCommand(), deadline, 650);
            if (identified != null && identified[5] == 8 && identified[12] <= 5)
            {
                identity = $"{identified[10]},{identified[11]}";
                if (IsMobaIdentity(identity)) model = Model;
            }
            var before = Exchange(link, Command(3), deadline, 800);
            if (before == null || before[6] != 1)
                return ReferenceBatteryProtocol.Reading.Failed("VGN F1 未取得本轮在线响应，未读取电量");
            var battery = Exchange(link, Command(4), deadline, 800);
            if (battery == null || battery[6] > 100)
                return ReferenceBatteryProtocol.Reading.Failed("VGN F1 电量响应无效或超时");
            var after = Exchange(link, Command(3), deadline, 800);
            // The receiver can remain present while its paired mouse disconnects or changes.
            if (after == null || after[6] != 1 || !before.AsSpan(7, 3).SequenceEqual(after.AsSpan(7, 3)))
                return ReferenceBatteryProtocol.Reading.Failed("VGN F1 电量查询前后在线状态或配对地址不一致");
            bool charging = battery[7] == 1;
            string trace = $"VGN F1 本轮在线/电量/在线响应，配对地址一致；CID/MID={identity ?? "未取得"}" +
                $"；RX={Convert.ToHexString(before)}/{Convert.ToHexString(battery)}/{Convert.ToHexString(after)}";
            return new(battery[6], charging, true,
                charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
                trace, model, identity);
        }
        catch (Exception ex)
        {
            return ReferenceBatteryProtocol.Reading.Failed("VGN F1 查询失败：" + ex.Message);
        }
    }
}
