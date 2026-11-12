using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static class RogOmniMouseProtocol
{
    internal const string Model = "ROG 月刃2 ACE";
    internal const string Protocol = "RogKerisIIOmni";

    // Gear Link PID 6936 (P714): report 3, 12/00/index 2 presence, 12/07/index 0 power.
    // WebHID excludes the report byte: power[4]/[9] become HID bytes [5]/[10].
    internal static bool IsPaired(ReadOnlySpan<byte> reply) =>
        VendorProtocol.TryReadRogOmniPairing(reply, out var pids) && pids.Count == 1 && pids[0] == 0x1B18;

    internal static bool TryParsePresence(ReadOnlySpan<byte> reply, out bool present)
    {
        present = false;
        if (reply.Length != 64 || !reply[..5].SequenceEqual(new byte[] { 3, 0x12, 0, 2, 0 }) ||
            reply[5] > 1) return false;
        present = reply[5] == 1;
        return true;
    }

    internal static bool TryParseBattery(ReadOnlySpan<byte> reply, out int percent, out bool charging)
    {
        percent = -1;
        charging = false;
        // Only official power fields can distinguish genuine zero from a query echo.
        if (reply.Length != 64 || !reply[..5].SequenceEqual(new byte[] { 3, 0x12, 7, 0, 0 }) ||
            reply[5] > 100 || reply[10] > 1 || reply[5..13].IndexOfAnyExcept((byte)0) < 0) return false;
        percent = reply[5];
        charging = reply[10] == 1;
        return true;
    }

    internal static ReferenceBatteryProtocol.Reading Read(byte[] paired,
        Func<byte, byte, ushort, byte[]> query, BatteryReplay.Trace trace = null)
    {
        if (!IsPaired(paired)) return ReferenceBatteryProtocol.Reading.Failed("没有唯一的已确认月刃2 ACE 配对型号");
        ReferenceBatteryProtocol.Reading Unknown(string reason) =>
            new(-1, false, false, ReferenceBatteryProtocol.State.Sleep, reason, Model);
        try
        {
            var presence = query(3, 0, 2);
            bool validPresence = TryParsePresence(presence, out bool present);
            trace?.Add("omni-mouse-present", validPresence, raw: Evidence(presence, validPresence, 6), keep: true);
            if (!validPresence || !present) return Unknown("Omni 已配对月刃2 ACE，鼠标未确认在线");

            var battery = query(3, 7, 0);
            bool decoded = TryParseBattery(battery, out int percent, out bool charging);
            trace?.Add("omni-mouse-battery", decoded, raw: Evidence(battery, decoded, 13), keep: true);
            if (!decoded) return Unknown("Omni 月刃2 ACE 未取得有效在线电量");

            var after = query(1, 0, 0);
            bool unchanged = IsPaired(after) && VendorProtocol.RogOmniPairingUnchanged(paired, after);
            byte[] pairingEvidence = null;
            if (VendorProtocol.TryReadRogOmniPairing(after, out var pids))
            {
                pairingEvidence = new byte[64];
                after.AsSpan(0, 3).CopyTo(pairingEvidence);
                for (int i = 0; i < pids.Count; i++)
                {
                    pairingEvidence[5 + i * 4] = (byte)pids[i];
                    pairingEvidence[6 + i * 4] = (byte)(pids[i] >> 8);
                }
            }
            trace?.Add("omni-paired-after", unchanged, raw: pairingEvidence, keep: true);
            if (!unchanged) return ReferenceBatteryProtocol.Reading.Failed("Omni 配对列表变化或复核失败，未采用鼠标电量");
            return new(percent, charging, true, charging ? ReferenceBatteryProtocol.State.Charging :
                ReferenceBatteryProtocol.State.Online, "Omni 月刃2 ACE 电量 03/12/07 配对 PID=0x1B18", Model);
        }
        catch (IOException ex) { return Unknown("Omni 鼠标查询失败：" + ex.Message); }
    }

    private static byte[] Evidence(byte[] response, bool valid, int length)
    {
        if (!valid) return null;
        var evidence = new byte[64];
        response.AsSpan(0, length).CopyTo(evidence);
        return evidence;
    }
}
