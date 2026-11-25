using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionNova
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b, int size)
    {
        Require(b.Length == size && size is >= 32 and <= 64 && b[0] == 0x20 &&
            b[1] == 1 && b[2] == 0 && b[3] is >= 1 and <= 100, "Invalid NOVA/VK battery");
        return Value(b[3]);
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire, bool checkSum = false)
    {
        var packet = Packet(wire.Output, 0, 0x20, 1);
        packet[32] = 0x21;
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
        {
            var response = wire.Exchange(packet,
                b => b.Length == wire.Input && b[0] == 0x20 && b[1] == 1, 500);
            if (checkSum)
            {
                int sum = 0;
                for (int index = 0; index < response.Length - 1; index++) sum += response[index];
                Require((sum & 255) == response[^1], "Invalid AK820 MAX reply checksum");
            }
            result = Decode(response, wire.Input);
        }
        return result;
    }
}
