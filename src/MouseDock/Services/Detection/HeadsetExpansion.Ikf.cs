using System;
using System.Linq;
using System.Security.Cryptography;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionIkf
{
    internal static ushort Crc(ReadOnlySpan<byte> b)
    {
        ushort crc = 0;
        foreach (byte value in b)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++) crc = (ushort)((crc >> 1) ^ ((crc & 1) != 0 ? 0xA001 : 0));
        }
        return crc;
    }
    internal static int? Status(byte[] raw)
    {
        if (raw?.Length != 64 || raw[0] != 3) return null;
        var b = raw[1..];
        byte[] payload;
        if (b[0] == 0xD6 && b[1] == 0xBE)
        {
            if (U16(b, 3) != 1 || b[5] != 10) return null;
            payload = [b[8]];
        }
        else
        {
            if (b[1] != 0xD6 || b[2] != 0xBE || U16(b, 4) != 2 || b[0] != 9 || b[8] != 10) return null;
            payload = [b[8], b[9]];
        }
        return Crc(payload) == U16(b, 6) ? payload[^1] : null;
    }
    internal static byte[] AppReply(byte[] raw, byte sequence, byte command)
    {
        if (raw?.Length != 64 || raw[0] != 3 || raw[1] != 0x55 || raw[2] != 128 || raw[3] != 0) return null;
        int size = U16(raw, 4);
        if (size is < 5 or > 58 || raw.AsSpan(6 + size).ContainsAnyExcept((byte)0)) return null;
        var app = raw.AsSpan(6, size);
        if (app[0] != 255 || app[1] != sequence || app[^1] != 0xAA || app[2] != app.Length - 4) return null;
        var payload = app[3..^1];
        ReadOnlySpan<byte> body;
        if (command == 12 && payload.Length >= 2 && payload[0] == 0xFA && payload[1] == 12) body = payload[2..];
        else if (payload.Length > 0 && payload[0] == command) body = payload[1..];
        else return null;
        return command == 0xEF && body.Length == 4 || command == 12 && body.Length is 3 or 4 ? body.ToArray() : null;
    }
    internal static byte[] StatusRequest(byte report)
    {
        var b = Packet(64, report, 7, 0x5A, 0xAA, 0xA5);
        ushort crc = Crc(new byte[] { 1, 10 });
        b[5] = (byte)crc; b[6] = (byte)(crc >> 8); b[7] = 1; b[8] = 10; return b;
    }
    internal static byte[] AppRequest(byte report, byte sequence, params byte[] command)
    {
        var b = Packet(64, report, 0x55, 128, 0, (byte)(command.Length + 4), 0, 255, sequence, (byte)command.Length);
        command.CopyTo(b, 9); b[9 + command.Length] = 0xAA; return b;
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire, ushort pid)
    {
        bool featureOnly = pid == 0x3412;
        byte report = featureOnly ? (byte)3 : (byte)2;
        ushort mid = pid == 0x3412 ? (ushort)0x3411 : pid == 0x3411 ? (ushort)0x3412 : pid;
        byte[] expected = [0x1F, 0x38, (byte)mid, (byte)(mid >> 8)];
        byte sequence = (byte)RandomNumberGenerator.GetInt32(256);
        byte[] Get(byte[] packet, Func<byte[], byte[]> parse)
        {
            wire.Drain(); wire.CheckTime();
            if (featureOnly)
            {
                Require(wire.Link.SetFeature(packet), "iKF Feature query failed");
                wire.Trace?.Add("expansion-set-feature", true, raw: packet, keep: true);
            }
            else wire.Send(packet);
            long until = wire.Link.Now + 1500;
            for (int i = 0; i < 64 && wire.Link.Now < until; i++)
            {
                var raw = wire.Link.Read(50); wire.CheckTime();
                var value = parse(raw);
                wire.Trace?.Add("expansion-input", value != null, raw: raw, keep: true);
                if (value != null) return value;
                if (wire.Feature != 64) continue;
                raw = wire.Link.GetFeature(3, 64); wire.CheckTime(); value = parse(raw);
                wire.Trace?.Add("expansion-get-feature", value != null, raw: raw, keep: true);
                if (value != null) return value;
            }
            throw new System.IO.IOException("No matching iKF response");
        }
        void Online()
        {
            var data = Get(StatusRequest(report), b => Status(b) is int value ? new byte[] { (byte)value } : null);
            Require(data[0] == 1, "iKF is disconnected or in wired mode");
        }
        byte[] Identity()
        {
            byte seq = sequence++;
            var data = Get(AppRequest(report, seq, 0xEF, 1), b => AppReply(b, seq, 0xEF));
            Require(data.AsSpan().SequenceEqual(expected), "iKF runtime headset identity mismatch");
            return data;
        }
        int level = -1;
        Online(); Identity();
        for (int i = 0; i < 2; i++)
        {
            byte seq = sequence++;
            var data = Get(AppRequest(report, seq, 0xFA, 12), b => AppReply(b, seq, 12));
            Require(data[0] <= 100, "Invalid iKF battery percentage"); level = data[0];
        }
        Identity(); Online();
        string model = mid switch { 0x3411 => "iKF King G", 0x3412 => "iKF V11 Pro", 0x3418 => "iKF Yingci Pro",
            0x3419 => "iKF V11 Pro+", _ => null };
        return Value(level) with { Model = model, Identity = $"381F:{mid:X4}" };
    }
}
