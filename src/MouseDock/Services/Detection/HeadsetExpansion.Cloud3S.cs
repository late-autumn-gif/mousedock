using System;
using System.Collections.Generic;
using System.IO;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionCloud3S
{
    internal static byte[] Request(byte command)
    {
        Require(command is 6 or 0x48, "Unsupported Cloud III S query");
        return Packet(64, 12, 2, 3, 1, 0, command);
    }
    internal static int Decode(byte[] b, byte command)
    {
        var packet = Request(command);
        Require(b.Length is >= 7 and <= 64 && b.AsSpan(0, 6).SequenceEqual(packet.AsSpan(0, 6)) &&
            !b.AsSpan().SequenceEqual(packet.AsSpan(0, b.Length)), "Cloud III S frame/echo mismatch");
        Require(command == 6 ? b[6] <= 100 : b[6] <= 2, "Cloud III S power value unknown");
        return b[6];
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire writer, IReadOnlyList<ExpansionWire> readers)
    {
        int Get(byte command)
        {
            foreach (var reader in readers) reader.Drain();
            writer.Send(Request(command));
            long until = writer.Link.Now + 1200;
            for (int poll = 0; poll < 128 && writer.Link.Now < until; poll++)
                foreach (var reader in readers)
                {
                    reader.CheckTime();
                    var b = reader.Link.Read(20);
                    if (b?.Length < 7 || b == null || !b.AsSpan(0, 6).SequenceEqual(Request(command).AsSpan(0, 6))) continue;
                    reader.Trace?.Add("expansion-input", true, raw: b, keep: true);
                    return Decode(b, command);
                }
            throw new IOException("Cloud III S response timeout");
        }
        Get(6); int level = Get(6);
        try { return Value(level, Get(0x48) is 1 or 2); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return Value(level); }
    }
}
