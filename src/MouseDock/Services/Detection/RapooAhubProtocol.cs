using System;
using System.Collections.Generic;
using System.Linq;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

// AHUB Kk.onConnected, qx, Gx, Vk, S6 and SC. Only their read operations.
internal static class RapooAhubProtocol
{
    internal static byte[] ReadMemory(int address, int length) => Packet(33, 6, 165, 164,
        (byte)length, (byte)address, (byte)(address >> 8), 0, 0);
    internal static byte[][] Names => Enumerable.Range(0, 8).Select(i => ReadMemory(1536 + 1024 * i + 1004, 16)).ToArray();
    internal static byte[][] Firmware => new[] { Packet(33, 6, 165, 163, 0), Packet(33, 6, 165, 163, 1),
        Packet(33, 6, 176, 0), Packet(33, 6, 176, 1) };
    internal static bool Allowed(byte[] b)
    {
        if (b is not { Length: 33 }) return false;
        return Names.Concat(Firmware).Concat(new[] { ReadMemory(268, 1), ReadMemory(2200, 1) })
            .Any(p => p.AsSpan().SequenceEqual(b));
    }
    internal static bool Control(HidDiscovery.VendorCollectionInfo c) => Base(c) && c.Usage == 14 &&
        c.Input == 33 && c.Output == 33 && c.Feature == 0;
    internal static bool Response(HidDiscovery.VendorCollectionInfo c) => Base(c) && c.Usage == 15 &&
        c.Input == 0 && c.Output == 0 && c.Feature == 33;
    private static bool Base(HidDiscovery.VendorCollectionInfo c) => c != null &&
        RapooVt3MaxProtocol.IsCandidate(c.Vid, c.Pid) && Mi(c, 1) && c.UsagePage == 0xFF00;
    internal static void ValidateControl(HidDiscovery.VendorCollectionInfo c, ExpansionHidMetadata m) =>
        Require(Control(c) && m.Revision == 0x0105 && m.HasInput(6, 33) && m.HasOutput(6, 33), "AHUB report06 descriptor mismatch");
    internal static void ValidateResponse(HidDiscovery.VendorCollectionInfo c, ExpansionHidMetadata m) =>
        Require(Response(c) && m.Revision == 0x0105 && m.HasFeature(8, 33), "AHUB Feature08 descriptor mismatch");

    internal static byte[] ValidateReply(byte[] request, byte[] b)
    {
        Require(Allowed(request), "AHUB non-whitelisted request");
        var payload = Payload(b);
        // Official Nx checks payload[0] ACK; at()/qx/Gx read data at payload[4].
        // Real receiver returns zero reserved bytes, not an echoed command/length.
        Require(payload[0] == 1 && payload.Length >= (request[2] == 164 ? 4 + request[3] : 9),
            "AHUB invalid Feature08 acknowledgement/payload length");
        return payload;
    }
    internal static byte[] Payload(byte[] raw)
    {
        Require(raw is { Length: 33 }, "AHUB invalid native Feature08 length");
        // Exact, separately validated Feature08 collection only. The user report
        // proves ACK-first native data; preserve report-ID-prefixed compatibility.
        Require(raw[0] is 8 or 1 or 2, "AHUB unexpected Feature08 framing");
        return raw[0] == 8 ? raw.AsSpan(1).ToArray() : raw;
    }
    internal static bool Busy(byte[] raw) => raw is { Length: 33 } &&
        (raw[0] == 2 || raw[0] == 8 && raw[1] == 2);
    internal static byte[] MaskReply(byte[] raw, bool keepValue)
    {
        int offset = raw is { Length: > 0 } && raw[0] == 8 ? 1 : 0;
        return keepValue ? PassiveBatteryNotifications.Mask(raw, 0, offset, offset + 1, offset + 2, offset + 3, offset + 4)
            : PassiveBatteryNotifications.Mask(raw, 0, offset, offset + 1, offset + 2, offset + 3);
    }
    internal static void Initialize(Func<byte[], byte[]> exchange)
    {
        foreach (var request in Names) ValidateReply(request, exchange(request));
        var current = ReadMemory(268, 1);
        int slot = ValidateReply(current, exchange(current))[4];
        Require(slot < 8, "AHUB configuration index out of range");
        var name = Names[slot]; ValidateReply(name, exchange(name));
        var dpi = ReadMemory(2200, 1);
        Require(ValidateReply(dpi, exchange(dpi))[4] < 7, "AHUB DPI gear out of range");
        foreach (var request in Firmware) ValidateReply(request, exchange(request));
    }
}
