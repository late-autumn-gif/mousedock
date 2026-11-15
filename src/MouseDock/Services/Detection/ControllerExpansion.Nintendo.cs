using static MouseDock.Services.Detection.ControllerExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class ControllerExpansionNintendo
{
    private const int PacketSize = 49;
    private const byte InputWithCommandReply = 0x21;
    private const byte StandardInput = 0x30;
    private const byte DeviceInfo = 0x02;
    private const byte DeviceInfoAck = 0x82;

    internal static ReferenceBatteryProtocol.Reading Decode(byte[] report, bool bluetooth)
    {
        Require(bluetooth && report.Length == PacketSize, "Invalid Nintendo Bluetooth input");
        bool validReport = report[0] == StandardInput ||
            (report[0] == InputWithCommandReply && report[13] == DeviceInfoAck && report[14] == DeviceInfo);
        Require(validReport, report[0] == InputWithCommandReply
            ? "Nintendo device-info ACK mismatch" : "Invalid Nintendo Bluetooth input");

        byte power = report[2];
        int band = power >> 5;
        Require(band <= 4, "Invalid Nintendo battery band");
        return Band(band, 4, (power & 0x10) != 0, "device");
    }

    internal static byte[] Request()
    {
        var report = new byte[PacketSize];
        report[0] = 0x01;
        // The subcommand report carries neutral rumble for both motors.
        report[3] = report[7] = 0x01;
        report[4] = report[5] = report[8] = report[9] = 0x40;
        report[10] = DeviceInfo;
        return report;
    }
}
