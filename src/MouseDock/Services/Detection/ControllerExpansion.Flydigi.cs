using static MouseDock.Services.Detection.ControllerExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class ControllerExpansionFlydigi
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] raw, bool apex)
    {
        Require(apex ? raw.Length == 32 && raw[0] == 4 : raw.Length is >= 31 and <= 64,
            "Invalid Flydigi input size");
        int at = apex ? 1 : 0;
        Require(raw[at] == 0x5A && raw[at + 1] == 0xA5 && raw[at + 2] == 1,
            "Flydigi GET_INFO header mismatch");
        Require(apex ? raw[at + 5] is 128 or 129 or 133 or 134 : raw[at + 5] == 130,
            "Flydigi runtime device identity mismatch");
        int level = raw[at + 11] & 15, state = raw[at + 11] >> 4;
        Require(state is 0 or 1 or 2 && (apex ? level <= 5 : state == 2 || level <= 6),
            "Invalid Flydigi battery band");
        return Band(state == 2 ? 5 : System.Math.Min(level + 1, 5), 5, state == 1, "device");
    }

    internal static byte[] Request(int size, bool apex)
    {
        Require(apex ? size == 32 : size is >= 32 and <= 65, "Invalid Flydigi output size");
        var b = new byte[size];
        new byte[] { apex ? (byte)3 : (byte)0, 0x5A, 0xA5, 1, 2, 0 }.CopyTo(b, 0);
        return b;
    }
}
