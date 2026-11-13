using System;
using System.Collections.Generic;
using System.IO;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionXiberia
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b, byte reportId)
    {
        Require(b.Length == 64 && reportId is 0x55 or 0x41 && b[0] == reportId &&
            b[1] == 0x10 && b[2] == 1 && b[4] is 1 or 2 && (b[7] & 127) <= 100,
            "Xiberia headset offline or invalid status");
        return Value(b[7] & 127);
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire writer,
        IReadOnlyList<(byte Report, ExpansionWire Wire)> readers, bool session)
    {
        void Drain() { foreach (var (_, wire) in readers) wire.Drain(); }
        if (session) { Drain(); writer.Send(Packet(64, 0x55, 0x57)); }
        ReferenceBatteryProtocol.Reading result = null;
        for (int round = 0; round < 2; round++)
        {
            Drain(); writer.Send(Packet(64, 0x55, 0x10, 1));
            long until = writer.Link.Now + 2000;
            result = null;
            for (int i = 0; i < 128 && writer.Link.Now < until && result == null; i++)
                foreach (var (report, wire) in readers)
                {
                    var b = wire.Link.Read(50);
                    if (b?.Length != 64 || b[0] != report || b[1] != 0x10 || b[2] != 1) continue;
                    result = Decode(b, report); wire.Trace?.Add("expansion-input", true, raw: b, keep: true); break;
                }
            if (result == null) throw new IOException("No fresh Xiberia status response");
        }
        return result;
    }
}
