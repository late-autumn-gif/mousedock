using System;

namespace MouseDock.Services.Detection;

// ATK HUB's COMPX codec uses a 16-byte payload after the HID report ID.
internal static class CompxMouseBattery
{
    private const byte ReportId = 8;
    private const byte ReadBattery = 4;
    internal const byte ReadIdentity = 0x10;
    private const int ReportLength = 17;
    private const int PayloadOffset = 1 + 5;
    private const int ChecksumTarget = 0x55;

    internal static byte[] CreateQuery() => CreateCommand(ReadBattery);

    internal static byte[] CreateCommand(byte command)
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = command;
        report[^1] = unchecked((byte)(ChecksumTarget - ReportId - command));
        return report;
    }

    internal static bool HasChecksum(ReadOnlySpan<byte> report)
    {
        int sum = 0;
        foreach (byte value in report) sum += value;
        return (sum & 0xff) == ChecksumTarget;
    }

    internal static bool TryReadIdentity(ReadOnlySpan<byte> report, out byte cid, out byte mid)
    {
        cid = mid = 0;
        if (report.Length != ReportLength || report[0] != ReportId || report[1] != ReadIdentity ||
            report[2] != 0 || report[PayloadOffset - 1] is < 2 or > 10 || !HasChecksum(report))
            return false;

        cid = report[PayloadOffset];
        mid = report[PayloadOffset + 1];
        return true;
    }

    internal static bool TryRead(ReadOnlySpan<byte> report, out int percentage, out bool charging)
    {
        percentage = -1;
        charging = false;
        if (report.Length != ReportLength || report[0] != ReportId || report[1] != ReadBattery ||
            report[2] == 0xff || !HasChecksum(report) || report.SequenceEqual(CreateQuery()))
            return false;

        percentage = report[PayloadOffset];
        charging = (report[PayloadOffset + 1] & 1) != 0;
        return true;
    }
}
