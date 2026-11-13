using static MouseDock.Services.Detection.ControllerExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class ControllerExpansionEightBitDo
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b, bool bluetooth)
    {
        Require(b.Length is >= 15 and <= 64 && b[0] == (bluetooth ? 1 : 4),
            "Invalid 8BitDo input framing");
        int level = b[14] & 127;
        Require(level is >= 1 and <= 100, "8BitDo percentage unavailable");
        return Percentage(level, (b[14] & 128) != 0 && level < 100, "Fresh 8BitDo battery input");
    }
}
