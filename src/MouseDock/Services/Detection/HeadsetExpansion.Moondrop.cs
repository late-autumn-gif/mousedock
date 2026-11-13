using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionMoondrop
{
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire)
    {
        byte[] Get(byte command) => wire.Exchange(Packet(wire.Output, 0x4B, 0x80, command, 0),
            b => b.Length >= 5 && b[0] == 0x4B && b[2] == command, 1500);
        void Online()
        {
            Require(Get(0x31)[4] != 0, "MOONDROP headset offline");
            wire.Link.Delay(50);
        }
        ReferenceBatteryProtocol.Reading result = null;
        Online();
        for (int i = 0; i < 2; i++)
        {
            var b = Get(0x27);
            Require(b.Length >= 10 && b[8] != 0 && b[9] <= 100, "MOONDROP battery unavailable");
            result = Value(b[9]); wire.Link.Delay(50); Online();
        }
        return result;
    }
}
