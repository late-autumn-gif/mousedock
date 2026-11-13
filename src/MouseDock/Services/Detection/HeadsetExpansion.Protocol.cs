// Protocol adaptation from MouseDock 1.0.3 and Halo Battery (MIT, 2026 contributors).
// The complete reference permission notices are embedded through THIRD-PARTY-NOTICES.md.
using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionProtocol
{
    internal static ReferenceBatteryProtocol.Reading Decode(string family, byte[] raw)
    {
        if (raw == null) return ReferenceBatteryProtocol.Reading.Failed("Missing headset report");
        try
        {
            return family switch
            {
                "Bolutek" => HeadsetExpansionBolutek.Decode(raw),
                "V9Pro" => HeadsetExpansionBolutek.Decode(raw, true),
                "Mhub" => HeadsetExpansionBolutek.DecodeMhub(raw),
                "Cloud3" => HeadsetExpansionHyperX.DecodeCloud3(raw),
                "Cloud2" => HeadsetExpansionHyperX.DecodeCloud2(raw),
                "Alpha2" => HeadsetExpansionHyperX.DecodeAlpha2(raw),
                "Rog" => HeadsetExpansionRog.Decode(raw),
                "Hecate" => HeadsetExpansionHecate.Decode(raw, false),
                "HecateTws" => HeadsetExpansionHecate.Decode(raw, true),
                "Siren" => HeadsetExpansionPassive.DecodeSiren(raw),
                "Jbl" => HeadsetExpansionPassive.DecodeJbl(raw),
                "AulaG9" => HeadsetExpansionPassive.DecodeAula(raw),
                _ => ReferenceBatteryProtocol.Reading.Failed("Unsupported headset standalone decoder")
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        { return ReferenceBatteryProtocol.Reading.Failed(ex.Message); }
    }
}
