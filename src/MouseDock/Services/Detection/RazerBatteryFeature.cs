using System;

namespace MouseDock.Services.Detection;

// Windows prefixes the standard Razer control payload with HID report ID 0.
internal static class RazerBatteryFeature
{
    internal const int ReportLength = 91;
    internal const byte BatteryLevel = 0x80;
    internal const byte ChargingStatus = 0x84;
    private const byte BatteryClass = 7;
    private const int ChecksumIndex = 89;

    internal static byte[] Request(byte transaction, byte command)
    {
        var report = new byte[ReportLength];
        report[2] = transaction;
        report[6] = 2;
        report[7] = BatteryClass;
        report[8] = command;
        report[ChecksumIndex] = CalculateChecksum(report);
        return report;
    }

    internal static bool TryRead(ReadOnlySpan<byte> report, byte transaction, byte command, out byte value)
    {
        value = 0;
        if (report.Length != ReportLength || report[0] != 0 || report[1] != 2 ||
            report[2] != transaction || report.Slice(3, 3).ContainsAnyExcept((byte)0) ||
            report[6] != 2 || report[7] != BatteryClass || report[8] != command ||
            report[ChecksumIndex] != CalculateChecksum(report))
            return false;

        value = report[10];
        return true;
    }

    private static byte CalculateChecksum(ReadOnlySpan<byte> report)
    {
        byte result = 0;
        foreach (byte field in report[3..ChecksumIndex])
            result ^= field;
        return result;
    }
}
