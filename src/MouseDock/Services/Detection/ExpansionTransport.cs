using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MouseDock.Services.Detection;

internal sealed class ExpansionHidMetadata
{
    internal ushort Revision;
    internal bool UnnumberedInput;
    internal HashSet<byte> InputIds, OutputIds, FeatureIds;
    internal Dictionary<byte, int> InputSizes, OutputSizes, FeatureSizes;
    internal bool HasInput(byte id, int size) => InputSizes.GetValueOrDefault(id) == size;
    internal bool HasOutput(byte id, int size) => OutputSizes.GetValueOrDefault(id) == size;
    internal bool HasFeature(byte id, int size) => FeatureSizes.GetValueOrDefault(id) == size;
}

internal sealed record ExpansionHidRules(string Family,
    Func<HidDiscovery.VendorCollectionInfo, bool> Matches,
    Action<HidDiscovery.VendorCollectionInfo, ExpansionHidMetadata> Validate,
    bool Passive = false, bool UnnumberedInput = false);

internal interface IExpansionOutputReport
{
    bool SetOutputReport(byte[] packet);
}

internal static class ExpansionFacts
{
    internal static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidDataException(reason); }
    internal static byte[] Packet(int size, params byte[] prefix)
    {
        Require(size >= prefix.Length, "Report capacity too small");
        var b = new byte[size]; prefix.CopyTo(b, 0); return b;
    }
    internal static string Name(string product)
    {
        string value = (product ?? "").Trim(' ', '\t', '\r', '\n');
        return string.Create(value.Length, value, static (target, source) =>
        {
            for (int i = 0; i < source.Length; i++)
                target[i] = source[i] is >= 'a' and <= 'z' ? (char)(source[i] - 32) : source[i];
        });
    }
    internal static bool Mi(HidDiscovery.VendorCollectionInfo c, int number) =>
        (c.Path ?? "").Contains($"&mi_{number:X2}", StringComparison.OrdinalIgnoreCase);
    internal static bool Col(HidDiscovery.VendorCollectionInfo c, int number) =>
        (c.Path ?? "").Contains($"&col{number:X2}#", StringComparison.OrdinalIgnoreCase) ||
        (c.Path ?? "").Contains($"&col{number:X2}&", StringComparison.OrdinalIgnoreCase);
    internal static ReferenceBatteryProtocol.Reading Value(int level, bool? charging = null,
        string diagnostic = "Fresh headset battery response") =>
        new(level, charging == true, charging.HasValue,
            charging == true ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online, diagnostic);
    internal static int U16(byte[] b, int at) => b[at] | b[at + 1] << 8;
}

internal sealed class ExpansionWire
{
    internal readonly ReferenceBatteryProtocol.ILink Link;
    internal readonly int Input, Output, Feature;
    internal readonly ushort Revision;
    internal readonly ExpansionHidMetadata Metadata;
    internal readonly BatteryReplay.Trace Trace;
    internal Action<byte[]> CheckInput;
    private readonly long deadline;
    internal ExpansionWire(ReferenceBatteryProtocol.ILink link, int input, int output, int feature = 0,
        ushort revision = 0, ExpansionHidMetadata metadata = null, BatteryReplay.Trace trace = null, int budget = 10000)
    {
        Link = link; Input = input; Output = output; Feature = feature; Revision = revision;
        Metadata = metadata; Trace = trace; deadline = link.Now + budget;
    }
    internal void CheckTime() => ExpansionFacts.Require(Link.Now < deadline, "Device transaction expired");
    internal void Drain()
    {
        for (int i = 0; i < 32; i++)
        {
            CheckTime();
            var b = Link.Read(1);
            if (b is not { Length: > 0 }) return;
            CheckInput?.Invoke(b); Trace?.Add("expansion-drain", true, raw: b);
        }
        throw new InvalidDataException("Old input queue did not drain");
    }
    internal byte[] Wait(Func<byte[], bool> accept, int budget = 1000, int reports = 128)
    {
        long until = Math.Min(deadline, Link.Now + budget);
        for (int i = 0; i < reports && Link.Now < until; i++)
        {
            var b = Link.Read((int)Math.Max(1, Math.Min(50, until - Link.Now)));
            CheckTime();
            if (b is not { Length: > 0 }) continue;
            CheckInput?.Invoke(b);
            bool ok = accept(b); Trace?.Add("expansion-input", ok, raw: b, keep: true);
            if (ok) return b;
        }
        throw new IOException("Fresh matching device response not received");
    }
    internal void Send(byte[] packet, bool setOutput = false, Action beforeWrite = null)
    {
        CheckTime();
        if (packet?.Length == Output) beforeWrite?.Invoke();
        bool sent = packet?.Length == Output && (setOutput
            ? Link is IExpansionOutputReport output && output.SetOutputReport(packet)
            : Link.Write(packet, 300));
        Trace?.Add("expansion-output", sent, raw: packet, keep: true);
        ExpansionFacts.Require(sent, "Device output was not completely sent"); CheckTime();
    }
    internal void CleanupSend(byte[] packet)
    {
        bool sent = packet?.Length == Output && Link.Write(packet, 300);
        Trace?.Add("expansion-cleanup-output", sent, raw: packet, keep: true);
        ExpansionFacts.Require(sent, "Device session cleanup output incomplete");
    }
    internal byte[] Exchange(byte[] packet, Func<byte[], bool> accept, int budget = 1000,
        bool drain = true, bool setOutput = false)
    {
        if (drain) Drain();
        Send(packet, setOutput);
        return Wait(b => !b.AsSpan().SequenceEqual(packet) && accept(b), budget);
    }
    internal byte[] FeatureRead(byte[] packet, byte id, int delay = 20)
    {
        CheckTime();
        bool sent = packet?.Length == Feature && packet[0] == id && Link.SetFeature(packet);
        Trace?.Add("expansion-set-feature", sent, raw: packet, keep: true);
        ExpansionFacts.Require(sent, "Device Feature query failed");
        Link.Delay(delay); CheckTime();
        var b = Link.GetFeature(id, Feature); CheckTime();
        bool valid = b?.Length == Feature && b[0] == id && !b.AsSpan().SequenceEqual(packet);
        Trace?.Add("expansion-get-feature", valid, raw: b, keep: true);
        ExpansionFacts.Require(valid, "Invalid device Feature framing/echo");
        return b;
    }
}
