using System;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal const string MchoseG3AName = "MCHOSE G3 A";
    internal const string RapooVt7Model = "Rapoo VT7";

    internal static bool IsMchoseG3A(ushort vid, ushort pid, string product) =>
        vid == 0xA8A5 && pid == 0x2255 && MouseExpansionName(product, MchoseG3AName);

    internal static byte[] MchoseG3ABatteryRequest() =>
        MouseExpansionPacket(65, 0, 0x55, 0x30, 0xA5, 0x0B, 0x2E, 1, 1, 1);

    internal static bool TryMchoseG3ABattery(ReadOnlySpan<byte> payload, out int level, out bool charging)
    {
        level = -1;
        charging = false;
        if (payload.Length != 64 || payload[0] != 0xAA || payload[1] != 0x30 ||
            payload[8] > 100 || payload[9] > 1) return false;
        level = payload[8];
        charging = payload[9] == 1;
        return true;
    }

    private static ReferenceBatteryProtocol.Reading ReadMchoseG3ABattery(
        ReferenceBatteryProtocol.ILink link, BatteryReplay.Trace trace)
    {
        trace?.Add("g3a-open", true);
        int level = -1;
        bool charging = false;
        for (int round = 0; round < 2; round++)
        {
            var request = MchoseG3ABatteryRequest();
            byte[] reply;
            try
            {
                reply = MouseExpansionExchange(link, request,
                    frame => TryMchoseG3ABattery(frame, out _, out _), budget: 1200);
            }
            catch (MouseExpansionTimeout)
            {
                trace?.Add("g3a-request", true, raw: request, keep: true);
                trace?.Add("g3a-response", false);
                return ReferenceBatteryProtocol.Reading.Failed("G3 A: missing fresh battery reply");
            }
            trace?.Add("g3a-request", true, raw: request, keep: true);
            trace?.Add("g3a-response", true, raw: reply, keep: true);
            TryMchoseG3ABattery(reply, out level, out charging);
        }
        return MouseExpansionValue(level, "G3 A: two fresh battery replies", charging, MchoseG3AName);
    }

    internal static ReferenceBatteryProtocol.Reading ReadRapooVt7Status(
        ReferenceBatteryProtocol.ILink link, BatteryReplay.Trace trace = null)
    {
        trace?.Add("vt7-open", true);
        long deadline = link.Now + 3500;
        for (int reads = 0; reads < 64; reads++)
        {
            long remaining = deadline - link.Now;
            if (remaining <= 0) break;
            var frame = link.Read((int)Math.Min(100, remaining));
            if (frame?.Length != 19 || frame[0] != 7 || (frame[1] & 0x0F) is not (0 or 2))
                continue;
            if (frame[7] == 0)
            {
                trace?.Add("vt7-offline", true);
                return MouseExpansionSleep("VT7: peripheral offline; stored percentage ignored");
            }
            if (!TryParseRapooVt7Battery(frame, out int level, out bool charging)) continue;
            trace?.Add("vt7-status", true, raw: frame, keep: true);
            return MouseExpansionValue(level, "VT7: current passive Report 07 status", charging, RapooVt7Model);
        }
        trace?.Add("vt7-status", false);
        return ReferenceBatteryProtocol.Reading.Failed("VT7: no current Report 07 battery notification");
    }
}
