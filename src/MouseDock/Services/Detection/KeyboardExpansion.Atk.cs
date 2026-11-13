using System;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionAtk
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b)
    {
        Require(b.Length == 32 && b[0] == 2 && b[1] == 0x96 && b[2] == 0x24 &&
            b[3] <= 100 && b[4] <= 1 && b.AsSpan(3).IndexOfAnyExcept((byte)0) >= 0,
            "Invalid ATK HFD battery response");
        // The official SDK deliberately suppresses the frozen percentage under external power.
        return b[4] == 1 ? new(-1, false, false, ReferenceBatteryProtocol.State.Online,
            "ATK reports external power; numeric percentage is unavailable") : Value(b[3]);
    }

    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire, bool wired)
    {
        void Online()
        {
            var status = wire.Exchange(Packet(33, 0, 0xAA, 0xFD),
                b => b.Length == 32 && b[0] == 0x55 && b[1] == 0xFD, 800);
            Require(status[2] == 1, "ATK keyboard is offline or connection state is unknown");
        }
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
        {
            if (!wired) Online();
            result = Decode(wire.Exchange(Packet(33, 0, 2, 0x96, 0x24),
                b => b.Length == 32 && b[0] == 2 && b[1] == 0x96 && b[2] == 0x24, 800));
        }
        if (!wired) Online();
        return result;
    }
}
