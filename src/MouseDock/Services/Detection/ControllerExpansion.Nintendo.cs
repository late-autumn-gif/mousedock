using System;
using static MouseDock.Services.Detection.ControllerExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class ControllerExpansionNintendo
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b, bool bluetooth)
    {
        Require(bluetooth && b.Length == 49 && b[0] is 0x21 or 0x30,
            "Invalid Nintendo Bluetooth input");
        Require(b[0] != 0x21 || b[13] == 0x82 && b[14] == 2, "Nintendo device-info ACK mismatch");
        int band = b[2] >> 5 & 7;
        Require(band <= 4, "Invalid Nintendo battery band");
        return Band(band, 4, (b[2] & 16) != 0, "device");
    }

    internal static byte[] Request()
    {
        var b = new byte[49];
        new byte[] { 1, 0, 0, 1, 64, 64, 0, 1, 64, 64, 2 }.CopyTo(b, 0);
        return b;
    }
}
