// Protocol adaptation from MouseDock 1.0.3 and Halo Battery (MIT, 2026 contributors).
// The complete reference permission notices are embedded through THIRD-PARTY-NOTICES.md.
using System;
using System.IO;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionProtocol
{
    internal static ReferenceBatteryProtocol.Reading Value(int level, bool? charging = null,
        string model = null, string identity = null) =>
        new(level, charging == true, charging.HasValue,
            charging == true ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
            "Fresh keyboard battery response", model, identity);

    internal static ReferenceBatteryProtocol.Reading DecodeForReplay(string family, byte[] raw)
    {
        if (raw == null) return ReferenceBatteryProtocol.Reading.Failed("Missing report");
        try
        {
            return family switch
            {
                "AtkHfd" => KeyboardExpansionAtk.Decode(raw),
                "Cherry" => KeyboardExpansionCherry.Decode(raw, raw.Length),
                "Nova" => KeyboardExpansionNova.Decode(raw, 32),
                "RogDirect" => KeyboardExpansionRog.DecodeDirect(raw),
                "Irok" => KeyboardExpansionRoyuan.Decode(raw, false),
                "Neon" => KeyboardExpansionRoyuan.Decode(raw, true),
                "RoyuanLegacy" => KeyboardExpansionRoyuan.DecodeLegacy(raw),
                "MchoseBy" => KeyboardExpansionMchoseBy.Decode(raw),
                "VgnV4" => KeyboardExpansionVgn.DecodeV4(raw, 1, false),
                "Exx" => KeyboardExpansionVgn.DecodeV4(raw, 1, true),
                "Langtu" => KeyboardExpansionLangtu.Decode(raw),
                "RapooEvent" => KeyboardExpansionNotifications.DecodeRapoo(raw),
                _ => ReferenceBatteryProtocol.Reading.Failed("Unsupported standalone decoder")
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or IndexOutOfRangeException)
        { return ReferenceBatteryProtocol.Reading.Failed(ex.Message); }
    }

    internal static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException(reason);
    }

    internal static int U16(byte[] data, int offset) => data[offset] | data[offset + 1] << 8;
    internal static uint U32(byte[] data, int offset) =>
        (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);
    internal static byte[] Packet(int length, params byte[] prefix)
    {
        Require(length >= prefix.Length, "Report capacity too small");
        var result = new byte[length];
        prefix.CopyTo(result, 0);
        return result;
    }
}

internal sealed class KeyboardExpansionWire
{
    internal readonly ReferenceBatteryProtocol.ILink Link;
    internal readonly int Input, Output, Feature;
    internal readonly byte InputId, OutputId, FeatureId;
    internal readonly ushort Revision;
    internal readonly BatteryReplay.Trace Trace;
    private readonly long deadline;
    internal Action<byte[]> CheckInput;

    internal KeyboardExpansionWire(ReferenceBatteryProtocol.ILink link, int input, int output,
        int feature, byte inputId, byte outputId, byte featureId, ushort revision = 0,
        BatteryReplay.Trace trace = null, int budget = 12000)
    {
        Link = link; Input = input; Output = output; Feature = feature;
        InputId = inputId; OutputId = outputId; FeatureId = featureId;
        Revision = revision; Trace = trace; deadline = link.Now + budget;
    }

    internal void CheckTime() => KeyboardExpansionProtocol.Require(Link.Now < deadline, "Keyboard transaction expired");
    internal void Drain()
    {
        for (int i = 0; i < 32; i++)
        {
            CheckTime();
            var b = Link.Read(1);
            if (b is not { Length: > 0 }) return;
            CheckInput?.Invoke(b);
            Trace?.Add("keyboard-drain", true, raw: b);
        }
        throw new InvalidDataException("Old input queue did not drain");
    }

    internal byte[] Wait(Func<byte[], bool> accept, int budget = 1000)
    {
        long until = Math.Min(deadline, Link.Now + budget);
        for (int i = 0; i < 128 && Link.Now < until; i++)
        {
            var b = Link.Read((int)Math.Max(1, Math.Min(100, until - Link.Now)));
            CheckTime();
            if (b is not { Length: > 0 }) continue;
            CheckInput?.Invoke(b);
            bool ok = accept(b);
            Trace?.Add("keyboard-input", ok, raw: b, keep: true);
            if (ok) return b;
        }
        throw new IOException("No fresh matching keyboard response");
    }

    internal byte[] Exchange(byte[] packet, Func<byte[], bool> accept, int budget = 1000,
        bool drain = true, bool allowEcho = false, Action beforeWrite = null)
    {
        if (drain) Drain();
        CheckTime();
        if (packet?.Length == Output) beforeWrite?.Invoke();
        bool sent = packet?.Length == Output && Link.Write(packet, 250);
        Trace?.Add("keyboard-output", sent, raw: packet, keep: true);
        KeyboardExpansionProtocol.Require(sent, "Incomplete keyboard output");
        return Wait(b => (allowEcho || !b.AsSpan().SequenceEqual(packet)) && accept(b), budget);
    }

    internal byte[] CleanupExchange(byte[] packet, Func<byte[], bool> accept)
    {
        long until = Link.Now + 1000;
        for (int i = 0; i < 32; i++)
            if (Link.Read(1) is not { Length: > 0 }) break;
        RequireCleanup(packet?.Length == Output && Link.Write(packet,250));
        Trace?.Add("keyboard-cleanup-output",true,raw:packet,keep:true);
        for (int i = 0; i < 32 && Link.Now < until; i++)
        {
            var b = Link.Read(50);
            if (b is not { Length: > 0 }) continue;
            bool ok = !b.AsSpan().SequenceEqual(packet) && accept(b);
            Trace?.Add("keyboard-cleanup-input",ok,raw:b,keep:true);
            if (ok) return b;
        }
        throw new IOException("Keyboard cleanup acknowledgement missing");
    }
    private static void RequireCleanup(bool ok) =>
        KeyboardExpansionProtocol.Require(ok,"Keyboard cleanup output incomplete");

    internal byte[] FeatureRead(byte[] packet, int delay = 20)
    {
        CheckTime();
        bool sent = packet?.Length == Feature && packet[0] == FeatureId && Link.SetFeature(packet);
        Trace?.Add("keyboard-set-feature", sent, raw: packet, keep: true);
        KeyboardExpansionProtocol.Require(sent, "Keyboard Feature query failed");
        Link.Delay(delay); CheckTime();
        var b = Link.GetFeature(FeatureId, Feature);
        CheckTime();
        bool valid = b?.Length == Feature && b[0] == FeatureId && !b.AsSpan().SequenceEqual(packet);
        Trace?.Add("keyboard-get-feature", valid, raw: b, keep: true);
        KeyboardExpansionProtocol.Require(valid, "Invalid keyboard Feature framing or echo");
        return b;
    }

    internal void FeatureSend(params byte[] body)
    {
        CheckTime();
        var packet = KeyboardExpansionProtocol.Packet(Feature, FeatureId);
        KeyboardExpansionProtocol.Require(body.Length < packet.Length, "Feature body too long");
        body.CopyTo(packet, 1);
        Link.Delay(10); CheckTime();
        KeyboardExpansionProtocol.Require(Link.SetFeature(packet), "Keyboard Feature command failed");
        Trace?.Add("keyboard-set-feature", true, raw: packet, keep: true);
        CheckTime();
    }

    internal byte[] FeaturePacket(params byte[] body)
    {
        var packet = KeyboardExpansionProtocol.Packet(Feature, FeatureId);
        KeyboardExpansionProtocol.Require(body.Length < packet.Length, "Feature body too long");
        body.CopyTo(packet, 1);
        return packet;
    }
}
