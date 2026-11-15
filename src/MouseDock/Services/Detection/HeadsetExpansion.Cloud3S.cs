using System;
using System.Collections.Generic;
using System.IO;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionCloud3S
{
    private const byte Battery = 0x06;
    private const byte Charging = 0x48;
    private const int ValueOffset = 6;
    private static ReadOnlySpan<byte> ReadHeader => [0x0c, 0x02, 0x03, 0x01, 0x00];

    internal static byte[] Request(byte command)
    {
        Require(command is Battery or Charging, "Unsupported Cloud III S query");
        var report = new byte[64];
        ReadHeader.CopyTo(report);
        report[ValueOffset - 1] = command;
        return report;
    }

    private static bool Matches(byte[] report, byte command) =>
        report is { Length: >= ValueOffset + 1 } &&
        report.AsSpan(0, ReadHeader.Length).SequenceEqual(ReadHeader) && report[ValueOffset - 1] == command;

    internal static int Decode(byte[] report, byte command)
    {
        byte[] request = Request(command);
        Require(Matches(report, command) && report.Length <= request.Length &&
            !report.AsSpan().SequenceEqual(request.AsSpan(0, report.Length)), "Cloud III S frame/echo mismatch");
        int value = report[ValueOffset];
        Require(value <= (command == Battery ? 100 : 2), "Cloud III S power value unknown");
        return value;
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
                    if (!Matches(b, command)) continue;
                    reader.Trace?.Add("expansion-input", true, raw: b, keep: true);
                    return Decode(b, command);
                }
            throw new IOException("Cloud III S response timeout");
        }
        Get(Battery); int level = Get(Battery);
        try { return Value(level, Get(Charging) is 1 or 2); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return Value(level); }
    }
}
