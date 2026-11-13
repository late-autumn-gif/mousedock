using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal interface IHeadsetExpansionSerialLink
{
    long Now { get; }
    void Purge();
    bool Write(byte[] packet);
    byte[] Read(int maximum);
}

internal static class HeadsetExpansionSerial
{
    internal static (string Port, string Instance, Guid Owner) SelectPort(string anchor, Guid owner,
        ushort vid, ushort pid, IReadOnlyList<(string Port, string Instance, Guid Owner)> ports)
    {
        bool sony = vid == 0x054C && pid == 0x0E53;
        Require(sony || vid == 0x373B && pid is 0x11CB or 0x1204, "No verified serial receiver profile");
        string hardware = $"VID_{vid:X4}&PID_{pid:X4}";
        string anchorPattern = @"^HID\\" + hardware + (sony ? @"&MI_03(?:&|\\)" : @"&MI_04(?:&|\\)");
        Require(owner != Guid.Empty && anchor != null && Regex.IsMatch(anchor, anchorPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Invalid serial HID anchor/container");
        string portPattern = @"^USB\\" + hardware + (sony ? @"&MI_06\\" : @"(?:&|\\)");
        var choices = ports.Where(p => p.Owner == owner &&
            Regex.IsMatch(p.Port ?? "", @"^COM[1-9][0-9]{0,4}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            Regex.IsMatch(p.Instance ?? "", portPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        Require(choices.Length == 1, "No unique present COM port in this receiver container");
        return choices[0];
    }

    internal static string AnchorInstance(string path)
    {
        Require(path != null && path.StartsWith(@"\\?\", StringComparison.Ordinal), "Invalid HID anchor path");
        var parts = path[4..].Split('#');
        Require(parts.Length == 4 && string.Equals(parts[0], "hid", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(parts[1]) && !string.IsNullOrEmpty(parts[2]), "HID instance path unavailable");
        return string.Join("\\", parts.Take(3));
    }
    internal static byte[] AtkRequest(byte command)
    {
        Require(command is 3 or 4, "Only ATK serial status/battery commands are allowed");
        return Packet(64, 2, 0, command);
    }
    internal static ReferenceBatteryProtocol.Reading DecodeAtk(byte[] b, byte command)
    {
        Require(command is 3 or 4 && b?.Length == 64, "Incomplete ATK serial frame");
        if (b.AsSpan().SequenceEqual(AtkRequest(command)) || b[1] != 0 || b[2] != command) return null;
        Require(b[0] is >= 4 and <= 63, "Invalid ATK serial payload size");
        if (command == 4)
        {
            Require(b[4] == 1, "ATK serial headset offline or status unknown");
            return Value(-1, diagnostic: "ATK serial headset online");
        }
        Require((b[4] & 127) <= 100, "ATK serial percentage out of range");
        return Value(b[4] & 127, diagnostic: "Fresh ATK serial battery; charging unverified");
    }
    internal static ReferenceBatteryProtocol.Reading ReadAtk(IHeadsetExpansionSerialLink port, BatteryReplay.Trace trace)
    {
        long deadline = port.Now + 9000;
        ReferenceBatteryProtocol.Reading Get(byte command)
        {
            long until = Math.Min(deadline, port.Now + 1500);
            port.Purge(); Require(port.Now < until, "ATK serial query expired");
            var packet = AtkRequest(command);
            bool sent = port.Write(packet); trace?.Add("headset-serial-output", sent, raw: packet, keep: true);
            Require(sent, "ATK serial query incomplete");
            var buffer = new List<byte>(64);
            for (int chunk = 0, frames = 0; chunk < 128 && frames < 16 && port.Now < until; chunk++)
            {
                var raw = port.Read(64 - buffer.Count);
                Require(raw != null && raw.Length <= 64 - buffer.Count && port.Now < until, "ATK serial read expired/oversized");
                buffer.AddRange(raw);
                if (buffer.Count != 64) continue;
                frames++; var frame = buffer.ToArray(); buffer.Clear();
                var result = DecodeAtk(frame, command);
                trace?.Add("headset-serial-input", result != null, raw: frame, keep: true);
                if (result != null) return result;
            }
            throw new IOException("No complete fresh ATK serial response");
        }
        ReferenceBatteryProtocol.Reading battery = null;
        for (int round = 0; round < 2; round++) { Get(4); battery = Get(3); }
        Get(4); return battery;
    }
    internal static ReferenceBatteryProtocol.Reading ReadSony(IHeadsetExpansionSerialLink port, BatteryReplay.Trace trace)
    {
        long deadline = port.Now + 9000;
        ushort transaction = (ushort)RandomNumberGenerator.GetInt32(2, 65528);
        byte[] Get(byte command)
        {
            transaction = (ushort)(transaction % 65534 + 2);
            long until = Math.Min(deadline, port.Now + 1500);
            port.Purge(); Require(port.Now < until, "Sony serial query expired");
            var packet = HeadsetExpansionSony.Request(command, transaction);
            bool sent = port.Write(packet); trace?.Add("headset-serial-output", sent, raw: packet, keep: true);
            Require(sent, "Sony serial GET incomplete");
            var buffer = new List<byte>();
            for (int reads = 0; reads < 40 && port.Now < until; reads++)
            {
                var chunk = port.Read(128);
                Require(chunk != null && chunk.Length <= 128 && port.Now < until, "Sony serial read expired/oversized");
                buffer.AddRange(chunk); Require(buffer.Count <= 512, "Sony serial buffer overflow");
                while (buffer.Count >= 3)
                {
                    if (buffer[0] != 4 || buffer[1] != 255 || buffer[2] is < 9 or > 59)
                    { buffer.RemoveAt(0); continue; }
                    int size = buffer[2] + 3;
                    if (buffer.Count < size) break;
                    var frame = buffer.Take(size).ToArray(); buffer.RemoveRange(0, size);
                    var value = HeadsetExpansionSony.Payload(frame, command, transaction);
                    trace?.Add("headset-serial-input", value != null, raw: frame, keep: true);
                    if (value != null) return value;
                }
            }
            throw new IOException("No complete matching Sony serial response");
        }
        byte[] Model()
        {
            var data = Get(2);
            Require(data.Length == 6 && data[0] == 0, "Sony receiver has not identified an H9");
            return data[..5];
        }
        HeadsetExpansionSony.Online(Get(1)); var before = Model();
        HeadsetExpansionSony.Battery(Get(4)); var battery = HeadsetExpansionSony.Battery(Get(4));
        Require(Model().AsSpan().SequenceEqual(before), "Sony H9 pairing identity changed");
        HeadsetExpansionSony.Online(Get(1));
        return battery with { Model = "Sony INZONE H9", Identity = Convert.ToHexString(before) };
    }
}
