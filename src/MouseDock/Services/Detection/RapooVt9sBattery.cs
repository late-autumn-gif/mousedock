using System;

namespace MouseDock.Services.Detection;

internal static class RapooVt9sBattery
{
    internal static bool TryReadReport7(ReadOnlySpan<byte> report, out int percent, out byte state)
    {
        percent = -1;
        state = 0;
        if (report.Length is not (19 or 20) || report[0] != 7)
            return false;

        // A HUB's VT_nrf54L parser takes the transport from the low nibble.
        // Report 07 payload bytes 6 and 7 are battery state and percentage.
        ReadOnlySpan<byte> payload = report[1..];
        if ((payload[0] & 0x0f) > 2 || payload[6] > 2)
            return false;

        state = payload[6];
        percent = payload[7];
        return true;
    }
}
