using System;
using System.IO;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class BatteryReplay
{
    private static Result ReplayScyroxLegacy(Query q,Collection[] collections)
    {
        Result Missing(string status = null) => new(q.Receiver,q.Interface,q.Protocol,status ?? q.Status,null,null,null,"unavailable");
        var channels = collections.Where(c => !c.Excluded && c.Receiver == q.Receiver &&
            (q.Interface == null || c.Interface == q.Interface) && ScyroxLegacyProtocol.IsCandidate(c.Vid,c.Pid,c.Product) &&
            c.InterfaceLabel == "MI_01/COL_05" && c.Page == 0xFF02 && c.Usage == 2 && c.Input == 17 && c.Output == 17 &&
            c.Feature == 0 && c.UsbRevision == 0x0216 && c.ReportIds == "IN[08] OUT[08] FEATURE[]").ToArray();
        if (channels.Length != 1) return Missing("collection-mismatch");
        if (q.Status is not ("Online" or "Charging")) return Missing();
        var samples = q.Samples ?? Array.Empty<Sample>();
        if (samples.Any(s => s == null || s.Stage == null)) return Missing("invalid-captured-frame");
        if (!samples.Any(s => s.Stage == "scyrox-redacted" && s.Ok)) return Missing();
        var frames = samples.Where(s => s.Stage == "expansion-output" || s.Stage == "expansion-input" && s.Ok).ToArray();
        if (frames.Length != 10) return Missing();
        try
        {
            ReferenceBatteryProtocol.Reading value = null;
            for (int i=0;i<5;i++)
            {
                byte cmd = i is 1 or 3 ? (byte)4 : (byte)3;
                var sent = frames[i*2]; var received = frames[i*2+1];
                byte[] Bytes(Sample s)
                {
                    if (!s.Ok || s.Length != 17 || s.Hex?.Length != 34) throw new InvalidDataException();
                    return Convert.FromHexString(s.Hex);
                }
                if (sent.Stage != "expansion-output" || received.Stage != "expansion-input" ||
                    !Bytes(sent).AsSpan().SequenceEqual(ScyroxLegacyProtocol.Request(cmd))) return Missing();
                var raw = Bytes(received);
                if (!ScyroxLegacyProtocol.Valid(raw,cmd) || cmd == 3 && raw[6] != 1) return Missing();
                if (cmd == 4) value = ScyroxLegacyProtocol.Decode(raw);
            }
            return new(q.Receiver,channels[0].Interface,q.Protocol,q.Status,null,value.Level,value.Charging,"captured-redacted");
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException) { return Missing("invalid-captured-frame"); }
    }
}
