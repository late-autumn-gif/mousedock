using static MouseDock.Services.Detection.ControllerExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class ControllerExpansionEightBitDo
{
    private const int BatteryOffset = 14;
    private const byte ChargeFlag = 0x80;

    internal static ReferenceBatteryProtocol.Reading Decode(byte[] report, bool bluetooth)
    {
        int statusReport = bluetooth ? 0x01 : 0x04;
        Require(report.Length > BatteryOffset && report.Length <= 64 &&
            report[0] == statusReport, "Invalid 8BitDo input framing");

        byte battery = report[BatteryOffset];
        int percentage = battery & (ChargeFlag - 1);
        Require(percentage is >= 1 and <= 100, "8BitDo percentage unavailable");
        bool activelyCharging = percentage < 100 && battery >= ChargeFlag;
        return Percentage(percentage, activelyCharging, "Fresh 8BitDo battery input");
    }
}
