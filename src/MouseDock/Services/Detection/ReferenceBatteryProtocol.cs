using System;

namespace MouseDock.Services.Detection;

/// <summary>Battery-only C# adaptation of the 1.0.2 verified profiles.
/// See THIRD-PARTY-NOTICES.md and docs/REFERENCE-1.0.2-PORT.md.</summary>
internal static class ReferenceBatteryProtocol
{
    internal enum State { Invalid, Failed, Online, Charging, Sleep, Full, Cached }
    internal sealed record Reading(int Level, bool Charging, bool ChargingKnown, State Status,
        string Diagnostic, string Model = null, string Identity = null)
    {
        internal ModernMouseDpi.State Dpi { get; init; }
        internal static Reading Failed(string reason) => new(-1, false, false, State.Failed, reason);
    }

    internal interface ILink
    {
        long Now { get; }
        byte[] Read(int timeoutMs);
        bool Write(byte[] request, int timeoutMs);
        bool SetFeature(byte[] request);
        byte[] GetFeature(byte reportId, int length);
        void Delay(int milliseconds);
    }

    internal static byte[] Request(ReferenceProfiles.Protocol protocol)
    {
        switch (protocol)
        {
            case ReferenceProfiles.Protocol.Compx:
                return CompxCommand(4);
            case ReferenceProfiles.Protocol.ScyroxV12:
                var scyrox = new byte[64];
                scyrox[0] = 8; scyrox[1] = 1; scyrox[63] = 9;
                return scyrox;
            case ReferenceProfiles.Protocol.SoraV2:
                var sora = new byte[32];
                sora[0] = 5; sora[1] = 0x15; sora[4] = 1;
                return sora;
            case ReferenceProfiles.Protocol.Rapoo:
                return null; // Passive. Never replace this with a different Rapoo query.
            default:
                throw new ArgumentOutOfRangeException(nameof(protocol));
        }
    }

    internal static byte[] CompxCommand(byte command)
    {
        if (command is not (4 or 0x10)) throw new ArgumentOutOfRangeException(nameof(command));
        var request = new byte[17];
        request[0] = 8; request[1] = command;
        request[16] = unchecked((byte)(0x55 - 8 - command));
        return request;
    }

    private static bool CompxChecksum(ReadOnlySpan<byte> report)
    {
        int sum = 0;
        foreach (byte value in report) sum += value;
        return (sum & 255) == 0x55;
    }

    internal static bool TryDecode(ReferenceProfiles.Protocol protocol, ReadOnlySpan<byte> b,
        out Reading reading)
    {
        reading = new(-1, false, false, State.Invalid, "应答不符合所选协议");
        int level;
        bool charging;
        switch (protocol)
        {
            case ReferenceProfiles.Protocol.Compx:
                if (b.Length != 17 || b[0] != 8 || b[1] != 4 || b[2] == 255 ||
                    !CompxChecksum(b) || b.SequenceEqual(Request(protocol))) return false;
                level = b[6]; charging = (b[7] & 1) != 0;
                break;
            case ReferenceProfiles.Protocol.ScyroxV12:
                if (b.Length != 64 || b[0] != 8 || b[1] != 1 || b[3] == 0 ||
                    b[4] != b[5] || b[4] > 3 || b[7] is < 1 or > 5 || b[62] == 1) return false;
                level = b[20]; charging = false;
                if (level > 100) return false;
                reading = new(level, false, false, State.Online,
                    "Scyrox 当前配置电量；协议未提供已确认的充电字段");
                return true;
            case ReferenceProfiles.Protocol.SoraV2:
                if (b.Length != 32 || b[0] != 5 || b[1] != 0x15) return false;
                if (b[12] == 0)
                {
                    reading = new(-1, false, false, State.Sleep, "Sora 外设休眠或离线，未采用电量字节");
                    return true;
                }
                level = b[9]; charging = b[10] != 0;
                break;
            case ReferenceProfiles.Protocol.Rapoo:
                if (b.Length is 19 or 20 && b[0] == 7 && (b[1] & 15) <= 2 && b[7] <= 2)
                {
                    if (b[7] == 0)
                    {
                        reading = new(-1, false, false, State.Sleep, "雷柏状态为休眠，未采用电量字节");
                        return true;
                    }
                    level = b[8]; charging = b[7] == 2;
                }
                else if (b.Length == 7 && b[0] == 0xBB && b[1] == 0xB0 && b[5] is 1 or 2)
                {
                    level = b[6]; charging = b[5] == 2;
                }
                else return false;
                break;
            default:
                return false;
        }
        if (level > 100) return false;
        reading = new(level, charging, true, charging ? State.Charging : State.Online,
            "本次事务取得有效电量应答");
        return true;
    }

    internal static bool TryIdentity(ReadOnlySpan<byte> b, out byte cid, out byte mid)
    {
        cid = mid = 0;
        if (b.Length != 17 || b[0] != 8 || b[1] != 0x10 || b[2] != 0 ||
            b[5] is < 2 or > 10 || !CompxChecksum(b)) return false;
        cid = b[6]; mid = b[7];
        return true;
    }

    private static bool Drain(ILink link)
    {
        for (int i = 0; i < 32; i++)
            if (link.Read(1) is not { Length: > 0 }) return true;
        return false;
    }

    internal static Reading Read(ReferenceProfiles.Profile profile, ILink link)
    {
        if (profile == null) return Reading.Failed("未匹配有限设备配置，未发送命令");
        try
        {
            byte[] request = Request(profile.Battery);
            if (profile.Battery == ReferenceProfiles.Protocol.SoraV2)
            {
                if (!link.SetFeature(request)) return Reading.Failed("Sora Feature 查询发送失败");
                link.Delay(100);
                byte[] reply = link.GetFeature(5, 32);
                return reply != null && TryDecode(profile.Battery, reply, out var result)
                    ? result : Reading.Failed("Sora Feature 应答缺失或无效");
            }
            if (request != null)
            {
                if (!Drain(link)) return Reading.Failed("旧输入队列未清空，取消本次查询");
                if (!link.Write(request, 300)) return Reading.Failed("电量查询未完整发送");
            }
            int budget = profile.Battery == ReferenceProfiles.Protocol.Compx ? 1200 : 4000;
            long deadline = link.Now + budget;
            int rejected = 0;
            while (link.Now < deadline)
            {
                byte[] reply = link.Read((int)Math.Min(100, deadline - link.Now));
                if (reply is not { Length: > 0 }) continue;
                if (!TryDecode(profile.Battery, reply, out var result)) { rejected++; continue; }
                if (result.Level >= 0 && profile.ReadIdentity)
                    result = AddIdentity(link, result);
                return result;
            }
            return Reading.Failed($"等待 {profile.Battery} 电量超时；拒绝无效应答={rejected}");
        }
        catch (Exception ex) { return Reading.Failed("电量事务失败：" + ex.GetType().Name); }
    }

    private static Reading AddIdentity(ILink link, Reading battery)
    {
        // This is optional enrichment on 373B:1278, not a requirement for a valid battery.
        try
        {
            if (!Drain(link) || !link.Write(CompxCommand(0x10), 300))
                return battery with { Diagnostic = battery.Diagnostic + "；型号身份查询未完成" };
            long deadline = link.Now + 1500;
            while (link.Now < deadline)
            {
                byte[] reply = link.Read((int)Math.Min(100, deadline - link.Now));
                if (reply != null && TryIdentity(reply, out byte cid, out byte mid))
                    return battery with { Model = ReferenceIdentityNames.Find(cid, mid), Identity = $"{cid},{mid}",
                        Diagnostic = battery.Diagnostic + $"；本次身份 CID={cid}/MID={mid}" };
            }
        }
        catch { /* A failed optional identity transaction must not erase valid battery evidence. */ }
        return battery with { Diagnostic = battery.Diagnostic + "；型号身份未取得，名称仅来自精确描述符配置" };
    }
}
