using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

// One bounded, persistent notification session per physical receiver. Never returns
// an already consumed sample as a fresh poll. Native handles belong to their worker.
internal static class RapooAhubSessions
{
    private static readonly object gate = new();
    private static readonly Dictionary<string, Session> sessions = new(StringComparer.OrdinalIgnoreCase);
    internal static void Reconcile(IEnumerable<HidDiscovery.VendorCollectionInfo> channels, bool safeMode)
    {
        var paths = channels.Select(c => c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (gate) foreach (var session in sessions.Values)
            if (safeMode || session.Paths.Any(p => !paths.Contains(p))) session.Stop = true;
    }
    internal static void Stop(string instance)
    { lock (gate) if (sessions.TryGetValue(instance, out var session)) session.Stop = true; }

    internal static ReferenceBatteryProtocol.Reading Read(string instance,
        HidDiscovery.VendorCollectionInfo[] channels, BatteryReplay.Trace trace)
    {
        var selected = new[] { channels.Where(RapooVt3MaxProtocol.Matches).ToArray(),
            channels.Where(RapooAhubProtocol.Control).ToArray(), channels.Where(RapooAhubProtocol.Response).ToArray() };
        if (selected.Any(c => c.Length != 1))
            return ReferenceBatteryProtocol.Reading.Failed("AHUB missing/ambiguous report07, report06 or Feature08 channel");
        var ordered = selected.Select(c => c[0]).ToArray();
        Session session;
        lock (gate)
        {
            foreach (var key in sessions.Where(p => p.Value.Worker.IsCompleted && Environment.TickCount64 >= p.Value.RetryAfter)
                .Select(p => p.Key).ToArray()) sessions.Remove(key);
            if (!sessions.TryGetValue(instance, out session))
            {
                if (sessions.Count >= 4) return ReferenceBatteryProtocol.Reading.Failed("AHUB session limit reached");
                session = new(instance, ordered); sessions[instance] = session;
            }
            if (!session.Paths.SequenceEqual(ordered.Select(c => c.Path), StringComparer.OrdinalIgnoreCase)) session.Stop = true;
        }
        return session.Poll(trace);
    }

    private sealed class Session
    {
        internal readonly string[] Paths;
        internal readonly Task Worker;
        internal volatile bool Stop;
        internal long RetryAfter;
        private long touched = Environment.TickCount64;
        private readonly object sync = new();
        private readonly BatteryReplay.Trace setup = new();
        private readonly RapooAhubInbox inbox = new();
        private string failure;
        private bool ready;
        internal Session(string instance, HidDiscovery.VendorCollectionInfo[] channels)
        {
            Paths = channels.Select(c => c.Path).ToArray();
            Worker = Task.Factory.StartNew(() => Run(instance, channels), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        private bool Alive => !Stop && Environment.TickCount64 - Interlocked.Read(ref touched) < 180000 &&
            MouseDock.App.Settings?.SafeMode != true;
        private void Run(string instance, HidDiscovery.VendorCollectionInfo[] c)
        {
            try
            {
                using var lease = HidIoLease.TryAcquire(instance);
                Require(lease != null, "AHUB receiver communication in use");
                using var input = new VendorProtocol.ExpansionHidLink(new("RapooVt3Max", RapooVt3MaxProtocol.Matches,
                    RapooVt3MaxProtocol.Validate, Passive: true), c[0], null);
                input.Wire().Drain();
                using var output = new VendorProtocol.ExpansionHidLink(new("RapooAhubControl", RapooAhubProtocol.Control,
                    RapooAhubProtocol.ValidateControl, OutputAllowed: RapooAhubProtocol.Allowed), c[1], null);
                using var feature = new VendorProtocol.ExpansionHidLink(new("RapooAhubResponse", RapooAhubProtocol.Response,
                    RapooAhubProtocol.ValidateResponse, OutputAllowed: _ => false), c[2], null);
                long deadline = Environment.TickCount64 + 12000;
                setup.Add("ahub-session-start", true);
                RapooAhubProtocol.Initialize(request =>
                {
                    Require(Alive && Environment.TickCount64 < deadline, "AHUB initialization expired/stopped");
                    bool sent = output.Write(request, 300);
                    setup.Add("ahub-read-output", sent, raw: request, keep: true);
                    Require(sent, "AHUB read request failed");
                    for (int retry = 0; retry < 5; retry++)
                    {
                        Thread.Sleep(retry == 0 ? 10 : 100);
                        Require(Alive && Environment.TickCount64 < deadline, "AHUB initialization expired/stopped");
                        var response = feature.GetFeature(8, 33);
                        int error = response == null ? Marshal.GetLastWin32Error() : 0;
                        Require(Alive && Environment.TickCount64 < deadline, "AHUB late native response discarded");
                        if (response is { Length: 33 } && response[0] == 8 && response[1] == 2) continue;
                        try { RapooAhubProtocol.ValidateReply(request, response); }
                        catch
                        {
                            setup.Add("ahub-read-rejected", false, raw: response == null ? null :
                                PassiveBatteryNotifications.Mask(response, 0, 1, 2, 3, 4), keep: true, nativeError: error);
                            throw;
                        }
                        var mask = request.AsSpan().SequenceEqual(RapooAhubProtocol.ReadMemory(268, 1)) ||
                            request.AsSpan().SequenceEqual(RapooAhubProtocol.ReadMemory(2200, 1))
                            ? PassiveBatteryNotifications.Mask(response, 0, 1, 2, 3, 4, 5)
                            : PassiveBatteryNotifications.Mask(response, 0, 1, 2, 3, 4);
                        setup.Add("ahub-read-ack", true, raw: mask, keep: true);
                        return response;
                    }
                    throw new IOException("AHUB busy response retry limit");
                });
                setup.Add("ahub-initialized", true);
                lock (sync) { ready = true; Monitor.PulseAll(sync); }
                while (Alive)
                {
                    var raw = input.Read(100);
                    if (raw is not { Length: > 0 }) continue;
                    var at = DateTimeOffset.UtcNow;
                    try
                    {
                        RapooVt3MaxProtocol.Decode(raw);
                        lock (sync)
                        {
                            inbox.Push(raw, at); failure = null;
                            Monitor.PulseAll(sync);
                        }
                    }
                    catch (InvalidDataException)
                    { lock (sync) { inbox.Clear(); failure = "AHUB invalid/offline notification"; Monitor.PulseAll(sync); } }
                }
            }
            catch (Exception ex)
            {
                setup.Add("ahub-session-failure", false, result: ex.Message);
                lock (sync) { failure = ex.Message; Monitor.PulseAll(sync); }
            }
            finally { lock (sync) { RetryAfter = Environment.TickCount64 + 30000; Stop = true; Monitor.PulseAll(sync); } }
        }
        internal ReferenceBatteryProtocol.Reading Poll(BatteryReplay.Trace trace)
        {
            Interlocked.Exchange(ref touched, Environment.TickCount64);
            long until = Environment.TickCount64 + 4500;
            lock (sync)
            {
                while (!Stop && !inbox.HasValue && Environment.TickCount64 < until)
                    Monitor.Wait(sync, (int)Math.Max(1, Math.Min(100, until - Environment.TickCount64)));
                trace?.Append(setup.Snapshot());
                var sample = inbox.Take(DateTimeOffset.UtcNow);
                if (!Stop && ready && sample != null)
                {
                    trace?.Append(new[] { sample });
                    trace?.Add("ahub-battery-complete", true);
                    return RapooVt3MaxProtocol.Decode(Convert.FromHexString(sample.Hex)) with
                    { Diagnostic = $"AHUB initialized session; new report07 notification; age={(DateTimeOffset.UtcNow - sample.At.Value).TotalSeconds:F1}s" };
                }
                return ReferenceBatteryProtocol.Reading.Failed(failure ?? (ready
                    ? "AHUB initialized; waiting for new report07 notification" : "AHUB initialization pending"));
            }
        }
    }
}

// Only unconsumed events are fresh. Caller serializes access. Latest event wins,
// including invalid/offline events; an old good value cannot survive a bad event.
internal sealed class RapooAhubInbox
{
    private BatteryReplay.Sample pending;
    internal bool HasValue => pending != null;
    internal void Clear() => pending = null;
    internal void Push(byte[] raw, DateTimeOffset at)
    {
        Clear();
        RapooVt3MaxProtocol.Decode(raw);
        var masked = PassiveBatteryNotifications.Mask(raw, 0, 1, 7, 8, 18);
        pending = new("ahub-battery-input", true, Length: masked.Length, Hex: Convert.ToHexString(masked), At: at);
    }
    internal BatteryReplay.Sample Take(DateTimeOffset now)
    {
        var value = pending; Clear();
        return value != null && value.At <= now && now - value.At <= TimeSpan.FromSeconds(30) ? value : null;
    }
}
