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
        Require(Allowed(request) && b is { Length: 33 } && b[0] == 8 && b[1] == 1, "AHUB invalid Feature08 acknowledgement");
        if (request[2] == 164)
            Require(b[2] == 164 && b[3] == request[3] && b[4] == 0, "AHUB memory response mismatch");
        return b;
    }
    internal static void Initialize(Func<byte[], byte[]> exchange)
    {
        foreach (var request in Names) ValidateReply(request, exchange(request));
        var current = ReadMemory(268, 1);
        int slot = ValidateReply(current, exchange(current))[5];
        Require(slot < 8, "AHUB configuration index out of range");
        var name = Names[slot]; ValidateReply(name, exchange(name));
        var dpi = ReadMemory(2200, 1);
        Require(ValidateReply(dpi, exchange(dpi))[5] < 7, "AHUB DPI gear out of range");
        foreach (var request in Firmware) ValidateReply(request, exchange(request));
    }
}
