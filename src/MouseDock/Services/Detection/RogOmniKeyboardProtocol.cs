using System;

namespace MouseDock.Services.Detection;

// Protocol facts: G-Helper PeripheralsProvider / Keyboard/Models/StrixScopeII /
// Keyboard/AsusKeyboard. Pair list: 01 A0 00, RX2 PID 1B7A; keyboard: 02 12 01,
// percentage [6], charging [9]. No lighting, pairing or configuration commands.
// Reference revision: seerge/g-helper 8dc2b614f9261fbc8806dfa557cbda2ba7d06d99.
internal static class RogOmniKeyboardProtocol
{
    internal const string Model = "ROG Strix Scope II 96 RX";
    internal enum Channel { Pairing, Keyboard }

    internal static bool IsCandidate(ushort vid, ushort pid, string product) =>
        vid == 0x0B05 && pid == 0x1ACE &&
        string.Equals(product?.Trim(), "ROG OMNI RECEIVER", StringComparison.OrdinalIgnoreCase);

    internal static Channel? Match(ushort vid, ushort pid, string product, string path,
        ushort page, ushort usage, int input, int output, int feature)
    {
        if (!IsCandidate(vid, pid, product) || usage != 1 || input != 64 || output != 64 || feature != 0)
            return null;
        var parts = (path ?? "").Split('#');
        if (parts.Length < 3) return null;
        string collection = parts[1];
        if (page == 0xFF02 && collection.Equals("vid_0b05&pid_1ace&mi_02&col01", StringComparison.OrdinalIgnoreCase))
            return Channel.Pairing;
        if (page == 0xFF00 && collection.Equals("vid_0b05&pid_1ace&mi_02&col02", StringComparison.OrdinalIgnoreCase))
            return Channel.Keyboard;
        return null;
    }

    private static byte[] Request(Channel channel)
    {
        var b = new byte[64];
        b[0] = channel == Channel.Pairing ? (byte)1 : (byte)2;
        b[1] = channel == Channel.Pairing ? (byte)0xA0 : (byte)0x12;
        b[2] = channel == Channel.Pairing ? (byte)0 : (byte)1;
        return b;
    }

    internal static bool HasTarget(ReadOnlySpan<byte> b)
    {
        if (b.Length != 64 || b[0] != 1 || b[1] != 0xA0 || b[2] != 0) return false;
        int matches = 0;
        for (int at = 5; at + 3 < b.Length; at += 4)
        {
            int pid = b[at] | b[at + 1] << 8;
            if (pid == 0) break;
            if (pid == 0x1B7A) matches++;
        }
        return matches == 1;
    }

    internal static bool TryBattery(ReadOnlySpan<byte> b, out ReferenceBatteryProtocol.Reading reading)
    {
        reading = ReferenceBatteryProtocol.Reading.Failed("键盘电量应答不匹配");
        if (b.Length != 64 || b[0] != 2 || b[1] != 0x12 || b[2] != 1 ||
            b[6] > 100 || b[9] > 1 || b.SequenceEqual(Request(Channel.Keyboard)))
            return false;
        bool charging = b[9] == 1;
        reading = new(b[6], charging, true,
            charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
            "OMNI 键盘 02/12/01 电量应答", Model, "1B7A");
        return true;
    }

    internal static ReferenceBatteryProtocol.Reading Read(
        ReferenceBatteryProtocol.ILink pairing, ReferenceBatteryProtocol.ILink keyboard)
    {
        var before = Exchange(pairing, Channel.Pairing, out string pairTrace);
        if (before == null || !HasTarget(before))
            return ReferenceBatteryProtocol.Reading.Failed("OMNI 配对列表未确认游侠 RX2（1B7A），未查询键盘电量；" + pairTrace);
        var battery = Exchange(keyboard, Channel.Keyboard, out string batteryTrace);
        if (battery == null || !TryBattery(battery, out var reading))
            return new(-1, false, false, ReferenceBatteryProtocol.State.Failed,
                "已确认游侠 RX2 配对，键盘未返回有效电量（可能离线、休眠或查询超时）；" + batteryTrace, Model, "1B7A");
        var after = Exchange(pairing, Channel.Pairing, out string afterTrace);
        if (after == null || !HasTarget(after) || !SameModels(before, after))
            return ReferenceBatteryProtocol.Reading.Failed("OMNI 配对列表变化或复核失败，未采用本次电量；" + afterTrace);
        return reading with { Diagnostic = reading.Diagnostic + "；" + batteryTrace };
    }

    private static bool SameModels(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        for (int at = 5; at + 3 < before.Length; at += 4)
        {
            int first = before[at] | before[at + 1] << 8;
            int second = after[at] | after[at + 1] << 8;
            if (first != second) return false;
            if (first == 0) return true;
        }
        return true;
    }

    private static byte[] Exchange(ReferenceBatteryProtocol.ILink link, Channel channel, out string diagnostic)
    {
        diagnostic = "旧输入队列未清空";
        bool drained = false;
        for (int i = 0; i < 32; i++)
            if (link.Read(1) is not { Length: > 0 }) { drained = true; break; }
        if (!drained) return null;
        var request = Request(channel);
        diagnostic = "查询未完整发送";
        if (!link.Write(request, 300)) return null;
        diagnostic = "等待应答超时";
        long deadline = link.Now + 700;
        while (link.Now < deadline)
        {
            var b = link.Read((int)Math.Max(1, Math.Min(100, deadline - link.Now)));
            if (b is { Length: > 0 })
                diagnostic = $"TX={Convert.ToHexString(request.AsSpan(0, 3))} 最近RX={Convert.ToHexString(b)}";
            if (b is not { Length: 64 } || b[0] != request[0]) continue;
            if (b[1] == 0xFF && b[2] == 0xAA) return null;
            if (b[1] != request[1] || b[2] != request[2] || b.AsSpan().SequenceEqual(request)) continue;
            if (channel == Channel.Pairing || TryBattery(b, out _)) return b;
        }
        return null;
    }
}
