using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionDailing
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b)
    {
        Require(b.Length == 64 && b[0] == 0x55 && b[1] == 0x65 && b[2] <= 100 &&
            b[3] is 2 or 3 or 4, "Invalid Dailing/legacy iKF power state");
        return Value(b[2], b[3] == 3);
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire)
    {
        bool unnumbered = wire.Metadata?.UnnumberedInput == true;
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
        {
            var packet = Packet(wire.Output, unnumbered ? (byte)0 : (byte)0x55);
            int at = unnumbered ? 1 : 0;
            packet[at] = 0x55; packet[at + 1] = 0x65; packet[at + 2] = 1;
            if (unnumbered)
            {
                packet[0] = 0; packet[1] = 0x55; packet[2] = 0x65; packet[3] = 1;
            }
            var b = wire.Exchange(packet, b => b.Length >= 4 && b[0] == 0x55 && b[1] == 0x65, 2000);
            if (b.Length == 65)
            { Require(b[64] == 0, "Dailing report padding invalid"); b = b[..64]; }
            result = Decode(b);
        }
        return result;
    }
}
