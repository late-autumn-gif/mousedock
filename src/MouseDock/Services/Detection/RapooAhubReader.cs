using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

// Uses the existing discovery scope, query gate and per-refresh handle lifecycle.
internal static class RapooAhubReader
{
    internal static ReferenceBatteryProtocol.Reading Read(string instance,
        HidDiscovery.VendorCollectionInfo[] channels, BatteryReplay.Trace trace)
    {
        var selected = new[] { channels.Where(RapooVt3MaxProtocol.Matches).ToArray(),
            channels.Where(RapooAhubProtocol.Control).ToArray(), channels.Where(RapooAhubProtocol.Response).ToArray() };
        if (selected.Any(c => c.Length != 1))
            return ReferenceBatteryProtocol.Reading.Failed("AHUB missing/ambiguous report07, report06 or Feature08 channel");
        return ReferenceQueryGate.Run(instance + ":rapoo-ahub", () =>
        {
            using var input = new VendorProtocol.ExpansionHidLink(new("RapooVt3Max", RapooVt3MaxProtocol.Matches,
                RapooVt3MaxProtocol.Validate, Passive: true), selected[0][0], null);
            using var output = new VendorProtocol.ExpansionHidLink(new("RapooAhubControl", RapooAhubProtocol.Control,
                RapooAhubProtocol.ValidateControl, OutputAllowed: RapooAhubProtocol.Allowed), selected[1][0], null);
            using var feature = new VendorProtocol.ExpansionHidLink(new("RapooAhubResponse", RapooAhubProtocol.Response,
                RapooAhubProtocol.ValidateResponse, OutputAllowed: _ => false), selected[2][0], null);
            var wire = input.Wire(12000);
            return Execute(wire, request =>
            {
                wire.CheckTime();
                bool sent = output.Write(request, 300);
                trace?.Add("ahub-read-output", sent, raw: request, keep: true);
                Require(sent, "AHUB read request failed");
                for (int retry = 0; retry < 5; retry++)
                {
                    input.Delay(retry == 0 ? 10 : 100); wire.CheckTime();
                    var response = feature.GetFeature(8, 33);
                    int error = response == null ? Marshal.GetLastWin32Error() : 0;
                    wire.CheckTime();
                    if (RapooAhubProtocol.Busy(response)) continue;
                    try { RapooAhubProtocol.ValidateReply(request, response); }
                    catch
                    {
                        trace?.Add("ahub-read-rejected", false, raw: response == null ? null :
                            RapooAhubProtocol.MaskReply(response, false), keep: true, nativeError: error);
                        throw;
                    }
                    trace?.Add("ahub-read-ack", true, raw: RapooAhubProtocol.MaskReply(response,
                        request.AsSpan().SequenceEqual(RapooAhubProtocol.ReadMemory(268, 1)) ||
                        request.AsSpan().SequenceEqual(RapooAhubProtocol.ReadMemory(2200, 1))), keep: true);
                    return response;
                }
                throw new IOException("AHUB busy response retry limit");
            }, trace);
        }, 12500);
    }
    internal static ReferenceBatteryProtocol.Reading Execute(ExpansionWire input,
        Func<byte[], byte[]> exchange, BatteryReplay.Trace trace)
    {
        try
        {
            input.Drain();
            trace?.Add("ahub-session-start", true);
            RapooAhubProtocol.Initialize(request =>
            { input.CheckTime(); var response = exchange(request); input.CheckTime(); return response; });
            trace?.Add("ahub-initialized", true);
            var raw = input.Wait(b => b.Length == 19 && b[0] == 7, 1500);
            var reading = RapooVt3MaxProtocol.Decode(raw);
            trace?.Add("ahub-battery-input", true, raw: PassiveBatteryNotifications.Mask(raw, 0, 1, 7, 8, 18), keep: true);
            trace?.Add("ahub-battery-complete", true);
            return reading with { Diagnostic = "AHUB read initialization and fresh report07 in current discovery transaction" };
        }
        catch (Exception ex)
        { trace?.Add("ahub-session-failure", false, result: ex.Message); throw; }
    }
}
