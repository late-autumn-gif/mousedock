using System;
using System.Collections.Generic;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionPmo
{
    private static readonly Dictionary<ushort,string> products = new()
    {
        [0x3004]="PMO Aurora 80",[0x3008]="PMO WAVE 75",[0x3009]="PMO WAVE 75 RGB",
        [0x3012]="PMO80",[0x302B]="PMO P75"
    };
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        int bodySize = wire.Input - (wire.InputId == 0 ? 0 : 1);
        Require(bodySize is 32 or 64,"Invalid PMO report body");
        ushort expected = 0;
        byte[] Body(byte[] raw)
        {
            if (wire.InputId != 0)
            {
                Require(raw.Length == bodySize + 1 && raw[0] == wire.InputId,"Invalid PMO numbered response");
                return raw[1..];
            }
            Require(raw.Length == bodySize,"Invalid PMO unnumbered response");
            return raw;
        }
        (ushort Vid,ushort Pid) Identity(byte[] b,byte command)
        {
            Require(b[0] == 0xAA && b[1] == command && (command != 0xD0 || b[13] == 4),
                "PMO keyboard offline");
            return ((ushort)U16(b,9),(ushort)U16(b,11));
        }
        byte[] Get(byte command,bool cleanup = false,Action beforeWrite = null)
        {
            var packet = Packet(wire.Output,wire.OutputId,0xAA,command,0,0,24);
            bool Accept(byte[] b)
            {
                var data = Body(b);
                if (data[0] == 0xAA && data[1] == 0xD0 && command != 0xD0)
                {
                    var id = Identity(data,0xD0);
                    Require(expected == 0 || id == (0x36B0,expected),"PMO keyboard changed");
                    return false;
                }
                return data[0] == 0xAA && data[1] == command;
            }
            var raw = cleanup ? wire.CleanupExchange(packet,Accept) :
                wire.Exchange(packet,Accept,1500,beforeWrite:beforeWrite);
            var body = Body(raw);
            Require(!body.AsSpan().SequenceEqual(packet.AsSpan(1)),"PMO query echo");
            Require(command == 0xD0 || body[2] == 0 && body[3] == 0 && body[4] == 24 && body[7] == 0x55,
                "Invalid PMO response header or status");
            return body;
        }
        T Session<T>(Func<byte[],T> work)
        {
            Exception original = null;
            bool startAttempted = false;
            try { return work(Get(0x10,beforeWrite:() => startAttempted = true)); }
            catch (Exception ex) { original = ex; throw; }
            finally
            {
                try { if (startAttempted) Get(0x11,true); }
                catch when (original != null) { }
            }
        }
        ushort Identify()
        {
            var id = Identity(Get(0xD0),0xD0);
            if (id.Pid == 0) id = Session(start => Identity(start,0x10));
            Require(id.Vid == 0x36B0 && products.ContainsKey(id.Pid),"Unsupported PMO downstream keyboard");
            return id.Pid;
        }
        expected = Identify();
        int level = Session(_ =>
        {
            int value = -1;
            for (int i = 0; i < 2; i++)
            {
                var b = Get(0x30);
                Require(b[8] <= 100 && b[9] <= 1,"Invalid PMO power fields");
                value = b[8];
            }
            return value;
        });
        Require(Identify() == expected,"PMO identity changed after read");
        return Value(level,model:products[expected],identity:"36B0:" + expected.ToString("X4"));
    }
}
