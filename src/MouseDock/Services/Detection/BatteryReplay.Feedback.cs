using System;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class BatteryReplay
{
    internal sealed record ProtocolProfile(string Model, string Reader, string Field, string Source);
    internal static readonly ProtocolProfile[] Profiles =
    [
        new("SC580", "M137", "Feature12 [8..11]=model ID, [18]=percent", "AJAZZ Driver X 1.0.5.3 GetInfo"),
        new("SC580", "Sc580Receiver", "Identify payload [4..7]=model ID, [12]=charging, [13]=percent, [14]=online", "SC580 capture / docs/AULA-SC580-SC630PRO.md"),
        new("SC630Pro", "Sc630Pro", "Feature6 [9]=online, [10]&127=percent, bit7=charging", "AULA 1.0.5 / docs/AULA-SC580-SC630PRO.md"),
        new("G403", "HidppPairedMouse", "1000/1004 byte[4]=percent; 1001 [4..5]=big-endian mV, [6]=flags", "Logitech/cpg-docs HID++ 2.0 (0x1000); G403 voltage curve from user-tested archive"),
        new("Neon75Air", "Neon75Feature", "Feature [2]=percent", "VendorProtocol.TryParseNeon75Status")
    ];

    internal static object DescribeCapture(Capture capture)
    {
        var receivers = capture.Collections.Select(c => c.Receiver).Concat(capture.Queries.Select(q => q.Receiver))
            .Concat(capture.Observations.Select(o => o.Receiver)).Distinct();
        return receivers.Select(receiver => new
        {
            Receiver = receiver,
            Interfaces = capture.Collections.Where(c => c.Receiver == receiver).Select(c => new
            {
                c.Interface, ReceiverName = c.Product, VidPid = $"{c.Vid:X4}:{c.Pid:X4}", c.Page, c.Usage,
                c.Input, c.Output, c.Feature, c.ReportIds, c.Route, c.QueryPolicy, c.Exclusion, c.OpenError
            }),
            QueryRecorded = capture.Queries.Any(q => q.Receiver == receiver),
            Queries = capture.Queries.Where(q => q.Receiver == receiver).Select(q =>
            {
                var replay = Replay(q, capture.Collections);
                return new { q.Interface, q.Protocol, q.Status, q.Model, q.ObservedPercent, q.FailureStage,
                    Field = Profiles.FirstOrDefault(p => p.Reader == q.Protocol &&
                        (q.Protocol != "HidppPairedMouse" || q.Model == LogitechG403Battery.Model))?.Field,
                    Response = q.Samples.Any(s => s.Hex != null && (s.Stage is "input" or "get-feature" ||
                        s.Stage.StartsWith("battery-", StringComparison.Ordinal))) ? "bytes-recorded" :
                        q.Samples.Any(s => s.Stage == "read-deadline") ? "deadline-without-recorded-input" : "undetermined",
                    Offline = replay.Percent.HasValue && replay.ValueKind is ("captured" or "system-report-captured") ?
                        replay.Percent == q.ObservedPercent ? "matched" : "reading-mismatch" : "insufficient-evidence",
                    Replay = replay };
            })
        });
    }
}
