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
        internal bool IsBatteryEstimated { get; init; }
        internal MouseDpiState Dpi { get; init; }
        /// <summary>与电量同一次会话顺带读到的硬件 DPI；-1 表示这次没读或设备不提供。</summary>
        internal int DpiValue { get; init; } = -1;
        internal bool MouseTypeConfirmed { get; init; }
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
                return CompxMouseBattery.CreateQuery();
            case ReferenceProfiles.Protocol.ScyroxV12:
                return ScyroxV12Dpi.RequestProfiles();
            case ReferenceProfiles.Protocol.SoraV2:
                return SoraV2BatteryProtocol.CreateQuery();
            case ReferenceProfiles.Protocol.Rapoo:
                return null; // Passive. Never replace this with a different Rapoo query.
            default:
                throw new ArgumentOutOfRangeException(nameof(protocol));
        }
    }

    internal static byte[] CompxCommand(byte command)
    {
        if (command is not (4 or CompxMouseBattery.ReadIdentity)) throw new ArgumentOutOfRangeException(nameof(command));
        return CompxMouseBattery.CreateCommand(command);
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
                if (!CompxMouseBattery.TryRead(b, out level, out charging)) return false;
                break;
            case ReferenceProfiles.Protocol.ScyroxV12:
                if (!ScyroxV12Dpi.TryReadBattery(b, out level)) return false;
                reading = new(level, false, false, State.Online,
                    "Scyrox 当前配置电量；协议未提供已确认的充电字段");
                return true;
            case ReferenceProfiles.Protocol.SoraV2:
                return SoraV2BatteryProtocol.TryDecode(b, out reading);
            case ReferenceProfiles.Protocol.Rapoo:
                return RapooVt9sBattery.TryDecode(b, out reading);
            default:
                return false;
        }
        if (level > 100) return false;
        reading = new(level, charging, true, charging ? State.Charging : State.Online,
            "本次事务取得有效电量应答");
        return true;
    }

    internal static bool TryIdentity(ReadOnlySpan<byte> report, out byte cid, out byte mid) =>
        CompxMouseBattery.TryReadIdentity(report, out cid, out mid);

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
            if (profile.Battery == ReferenceProfiles.Protocol.SoraV2)
                return SoraV2BatteryProtocol.Read(link);

            byte[] request = Request(profile.Battery);
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
            if (!Drain(link) || !link.Write(CompxCommand(CompxMouseBattery.ReadIdentity), 300))
                return battery with { Diagnostic = battery.Diagnostic + "；型号身份查询未完成" };
            long deadline = link.Now + 1500;
            while (link.Now < deadline)
            {
                byte[] reply = link.Read((int)Math.Min(100, deadline - link.Now));
                if (reply != null && TryIdentity(reply, out byte cid, out byte mid))
                    return battery with { Model = AtkMouseIdentityCatalog.Find(cid, mid), Identity = $"{cid},{mid}",
                        Diagnostic = battery.Diagnostic + $"；本次身份 CID={cid}/MID={mid}" };
            }
        }
        catch { /* A failed optional identity transaction must not erase valid battery evidence. */ }
        return battery with { Diagnostic = battery.Diagnostic + "；型号身份未取得，名称仅来自精确描述符配置" };
    }
}
