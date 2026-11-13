using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using MouseDock.Services.Detection;

internal static class HeadsetExpansionSerialRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string name)
        { if (!ok) throw new InvalidOperationException("Serial headset: " + name); checks++; }
        var type = typeof(ReferenceBatteryProtocol).Assembly.GetType("MouseDock.Services.Detection.HeadsetExpansionSerial");
        Check(type != null, "same-container native serial reader exists");
        var select = type!.GetMethod("SelectPort", BindingFlags.NonPublic | BindingFlags.Static)!;
        var decode = type.GetMethod("DecodeAtk", BindingFlags.NonPublic | BindingFlags.Static)!;
        Guid owner = new("a8bf257e-7d48-422e-9cc0-150e43871811"), other = Guid.NewGuid();
        const string anchor = @"HID\VID_373B&PID_11CB&MI_04&COL01\7&123&0&0000";
        const string node = @"USB\VID_373B&PID_11CB&MI_06\7&123&0&0006";
        (string, string, Guid) Select(string a, Guid o, ushort v, ushort p,
            List<(string, string, Guid)> ports) =>
            ((string, string, Guid))select.Invoke(null, [a, o, v, p, ports])!;
        bool Reject(Action operation)
        {
            try { operation(); return false; }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { return true; }
        }
        Check(Select(anchor, owner, 0x373B, 0x11CB, [("COM7", node, owner)]).Item1 == "COM7",
            "exact hardware and same nonzero container");
        Check(Reject(() => Select(anchor, owner, 0x373B, 0x11CB, [("COM7", node, other)])),
            "wrong physical receiver never supplies COM");
        Check(Reject(() => Select(anchor, owner, 0x373B, 0x11CB,
            [("COM7", node, owner), ("COM8", node + "X", owner)])), "two matching COM ports remain ambiguous");
        Check(Reject(() => Select(anchor, Guid.Empty, 0x373B, 0x11CB, [("COM7", node, Guid.Empty)])),
            "zero container is not identity evidence");
        Check(Reject(() => Select(anchor, owner, 0x373B, 0x1204, [("COM7", node, owner)])),
            "another model cannot match anchor");
        Check(Reject(() => Select(anchor, owner, 0x373B, 0x11CB, [("../COM7", node, owner)])),
            "COM name cannot inject a path");
        const string sony = @"HID\VID_054C&PID_0E53&MI_03&COL01\7&123&0&0000";
        Check(Reject(() => Select(sony, owner, 0x054C, 0x0E53,
            [("COM7", @"USB\VID_054C&PID_0E53&MI_05\7&123&0&0005", owner)])),
            "Sony pins the USB serial MI06");
        byte[] b = new byte[64]; b[0] = 4; b[2] = 3; b[4] = 128 | 61;
        var battery = (ReferenceBatteryProtocol.Reading)decode.Invoke(null, [b, (byte)3])!;
        Check(battery is { Level: 61, ChargingKnown: false }, "ATK serial fresh percent, charging unverified");
        b[4] = 229;
        Check(Reject(() => decode.Invoke(null, [b, (byte)3])), "ATK serial percentage range");
        b[2] = 4; b[4] = 0;
        Check(Reject(() => decode.Invoke(null, [b, (byte)4])), "ATK serial offline status");
        bool RejectTransaction(Action operation)
        {
            try { operation(); return false; }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { return true; }
        }
        int statusReads = 0, batteryReads = 0;
        SerialInput Atk(bool disconnect) => new(packet =>
        {
            Check(packet.Length == 64 && packet[0] == 2 && packet[1] == 0 && packet[2] is 3 or 4,
                "ATK native serial only uses documented GETs");
            var reply = new byte[64]; reply[0] = 4; reply[2] = packet[2];
            reply[4] = packet[2] == 4 ? (byte)(disconnect && ++statusReads == 3 ? 0 : 1) :
                (byte)(60 + ++batteryReads);
            return reply;
        });
        var atk = Atk(false);
        Check(HeadsetExpansionSerial.ReadAtk(atk, null).Level == 62 &&
            atk.Outputs.Select(p => p[2]).SequenceEqual(new byte[] {4,3,4,3,4}),
            "fragmented ATK reads reassemble and finish with online check");
        statusReads = 0; batteryReads = 0;
        Check(RejectTransaction(() => HeadsetExpansionSerial.ReadAtk(Atk(true), null)),
            "ATK serial final disconnect invalidates earlier percent");
        int modelReads = 0, sonyBattery = 0;
        SerialInput Sony(bool replace) => new(packet =>
        {
            Check(packet.Length == 12 && packet[0] == 1 && packet[2] == 0xFC && packet[7] is 1 or 2 or 4,
                "Sony native serial only uses link/model/battery GETs");
            byte command = packet[7];
            byte[] payload = command switch
            {
                1 => [1],
                2 => [0, 2, 3, (byte)(replace && ++modelReads == 2 ? 9 : 4), 5, 6],
                _ => [0, (byte)(70 + ++sonyBattery)]
            };
            var reply = new byte[12 + payload.Length];
            reply[0] = 4; reply[1] = 255; reply[2] = (byte)(reply.Length - 3);
            reply[4] = 0x96; reply[5] = 0xC3; reply[6] = command == 1 ? (byte)0x12 : (byte)0x14;
            reply[7] = command; reply[8] = 16; reply[9] = packet[9]; reply[10] = packet[10];
            payload.CopyTo(reply,11);
            reply[^1] = unchecked((byte)reply.Skip(3).Take(reply.Length - 4).Sum(v => (int)v));
            return new byte[] {0xCC,0xDD}.Concat(reply).ToArray();
        });
        var sonyInput = Sony(false);
        Check(HeadsetExpansionSerial.ReadSony(sonyInput, null) is
            { Level:-1,Model:"Sony INZONE H9",Identity:"0002030405",ChargingKnown:false },
            "fragmented Sony HCI checks identity and preserves approximate scale as unknown percent");
        modelReads = 0; sonyBattery = 0;
        Check(RejectTransaction(() => HeadsetExpansionSerial.ReadSony(Sony(true), null)),
            "Sony serial model change rejects battery");
        return checks;
    }

    private sealed class SerialInput(Func<byte[],byte[]> respond) : IHeadsetExpansionSerialLink
    {
        private readonly Queue<byte> queue = new();
        internal readonly List<byte[]> Outputs = new();
        public long Now { get; private set; }
        public void Purge() { queue.Clear(); Now++; }
        public bool Write(byte[] packet)
        {
            Outputs.Add((byte[])packet.Clone());
            foreach (byte value in respond(packet)) queue.Enqueue(value);
            Now++; return true;
        }
        public byte[] Read(int maximum)
        {
            Now += queue.Count == 0 ? 50 : 1;
            int size = Math.Min(maximum, Math.Min(7, queue.Count));
            return Enumerable.Range(0,size).Select(_ => queue.Dequeue()).ToArray();
        }
    }
}
