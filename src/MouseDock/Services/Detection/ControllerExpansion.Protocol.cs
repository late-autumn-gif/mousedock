// Protocol adaptation from MouseDock 1.0.3 and Halo Battery (MIT, 2026 contributors).
// The complete reference permission notices are embedded through THIRD-PARTY-NOTICES.md.
using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static class ControllerExpansionProtocol
{
    internal static ReferenceBatteryProtocol.Reading Decode(string family, byte[] raw,
        bool bluetooth = false, bool adapter = false)
    {
        try
        {
            Require(raw != null, "Missing controller report");
            return family switch
            {
                "SonyDs4" => ControllerExpansionSony.Decode(raw, false, bluetooth, adapter),
                "SonyDs5" => ControllerExpansionSony.Decode(raw, true, bluetooth, false),
                "EightBitDo" => ControllerExpansionEightBitDo.Decode(raw, bluetooth),
                "Nintendo" => ControllerExpansionNintendo.Decode(raw, bluetooth),
                "Vader5" => ControllerExpansionFlydigi.Decode(raw, false),
                "Apex5" => ControllerExpansionFlydigi.Decode(raw, true),
                "Leadjoy" => ControllerExpansionLeadjoy.Decode(raw),
                _ => throw new InvalidDataException("Unknown controller protocol")
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        { return ReferenceBatteryProtocol.Reading.Failed(ex.Message); }
    }

    internal static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidDataException(reason); }

    internal static ReferenceBatteryProtocol.Reading Percentage(int level, bool charging,
        string diagnostic, bool full = false) =>
        new(level, charging, true, full ? ReferenceBatteryProtocol.State.Full :
            charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online, diagnostic);

    internal static ReferenceBatteryProtocol.Reading Band(int band, int maximum, bool charging, string source) =>
        new(-1, charging, true, charging ? ReferenceBatteryProtocol.State.Charging :
            ReferenceBatteryProtocol.State.Online, $"{source}-band={band}/{maximum}");

    internal static bool BluetoothPath(string path) =>
        (path ?? "").Contains("vid&", StringComparison.OrdinalIgnoreCase) ||
        (path ?? "").Contains("{00001124-0000-1000-8000-00805f9b34fb}", StringComparison.OrdinalIgnoreCase);
}
