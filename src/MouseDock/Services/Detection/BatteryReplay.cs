using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MouseDock.Services.Detection;

internal static class BatteryReplay
{
    internal const string EntryName = "battery-replay.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [ThreadStatic] internal static Scan Current;

    internal sealed record Collection(string Receiver, int Interface, ushort Vid, ushort Pid, string Product,
        ushort Page, ushort Usage, int Input, int Output, int Feature, bool Excluded, bool Mi02, int OmniReport = 0, string InterfaceLabel = null, ushort? UsbRevision = null,
        int? OpenError = null, string Exclusion = null, string Status = null,
        string ReportIds = null, string Route = null, string QueryPolicy = null);
    internal sealed record Sample(string Stage, bool Ok, byte Sequence = 0, int Length = 0, string Hex = null, DateTimeOffset? At = null, string Result = null, int? NativeError = null);
    internal sealed record Query(string Receiver, int? Interface, string Protocol, string Status, string Model,
        int? ObservedPercent, Sample[] Samples);
    internal sealed record Observation(string Receiver, string Model, string Transport, int? Percent, bool Stale, bool Connected = true, bool? Charging = null, bool? Full = null,
        string Reader = null, bool ReceiverPresent = false, bool PeripheralConfirmed = false, string Product = null,
        string Name = null, string Source = null);
    internal sealed record Capture(int Schema, string AppVersion, Guid BuildId, string WindowsVersion, string ScanError,
        DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
        Collection[] Collections, Query[] Queries, Observation[] Observations);
    internal sealed record Result(string Receiver, int? Interface, string Protocol, string Status, string Model,
        int? Percent, bool? Charging, string ValueKind);

    internal sealed class Trace
    {
        private readonly List<Sample> samples = new();

        internal void Add(string stage, bool ok, byte sequence = 0, byte[] raw = null, bool keep = false, string result = null, int? nativeError = null)
        {
            lock (samples)
            {
                if (samples.Count >= 64) return;
                samples.Add(new(stage, ok, sequence, raw?.Length ?? 0, keep && raw != null ? Convert.ToHexString(raw) : null, DateTimeOffset.UtcNow, result, nativeError));
            }
        }

        internal Sample[] Snapshot() { lock (samples) return samples.ToArray(); }
    }

    internal sealed class Scan
    {
        private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
        private readonly Dictionary<string, string> receivers;
        internal bool? SafeMode { get; }

        internal Scan(Dictionary<string, string> aliases = null, bool? safeMode = null)
        {
            receivers = aliases ?? new(StringComparer.OrdinalIgnoreCase);
            SafeMode = safeMode;
        }
        private readonly Dictionary<string, int> interfaces = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Collection> collections = new();
        private readonly List<Query> queries = new();
        internal Trace Active;
        internal string ScanError;
        internal List<DetectedDevice> Detected = new();

        private string Receiver(string instance) => Alias(receivers, instance);

        internal void AddCollection(string instance, string path, ushort vid, ushort pid, string product,
            ushort page, ushort usage, int input, int output, int feature, bool excluded, ushort? usbRevision = null, string exclusion = null,
            int? openError = null, string status = "capabilities-read", string reportIds = null, string route = null, string queryPolicy = null)
        {
            HidInventory.ParsePath(path, out _, out _, out string interfaceLabel);
            int number = collections.Count + 1;
            interfaces[path] = number;
            collections.Add(new(Receiver(instance), number, vid, pid, SafeLabel(product), page, usage, input, output, feature, excluded, path.Contains("&mi_02", StringComparison.OrdinalIgnoreCase),
                VendorProtocol.IsRogOmni(vid, pid)
                    ? VendorProtocol.IsRogOmniChannel(path, product, page, usage, input, output, feature, 1) ? 1 :
                        VendorProtocol.IsRogOmniChannel(path, product, page, usage, input, output, feature, 2) ? 2 : 0
                    : 0, interfaceLabel, usbRevision, openError, exclusion, status, reportIds, route, queryPolicy));
        }

        internal void AddQuery(string instance, string path, string protocol, string status, string model,
            int level = -1, Trace trace = null) =>
            queries.Add(new(Receiver(instance), path != null && interfaces.TryGetValue(path, out int number) ? number : null,
                protocol, status, SafeLabel(model), level >= 0 ? level : null, trace?.Snapshot() ?? Array.Empty<Sample>()));

        internal Capture Finish(IEnumerable<string> selectedIds)
        {
            var selected = selectedIds?.Where(receivers.ContainsKey).Select(id => receivers[id]).ToHashSet();
            return new(1, typeof(BatteryReplay).Assembly.GetName().Version.ToString(),
                typeof(BatteryReplay).Module.ModuleVersionId, Environment.OSVersion.Version.ToString(), ScanError,
                started, DateTimeOffset.UtcNow,
                collections.Where(c => selected == null || selected.Contains(c.Receiver)).ToArray(),
                queries.Where(q => selected == null || selected.Contains(q.Receiver)).ToArray(),
                Detected.Where(device => receivers.TryGetValue(device.Id, out string label) &&
                        (selected == null || selected.Contains(label)))
                    .Select(device => new Observation(receivers[device.Id], SafeLabel(device.ConfirmedModel), device.Transport,
                        device.Level is >= 0 and <= 100 ? device.Level : null, device.IsStale,
                        device.Connected, device.IsStale || (!device.Charging &&
                            !queries.Any(q => q.Receiver == receivers[device.Id] &&
                                (q.Protocol is "Sc580Receiver" or "Neon75Feature") && q.ObservedPercent.HasValue))
                            ? null : device.Charging, device.ChargeFull && !device.IsStale ? true : null,
                        device.HandledBy.ToString(), device.ReceiverDiscovered, device.PeripheralConfirmed, SafeLabel(device.RawName),
                        SafeLabel(device.Name), device.Level >= 0 ? (device.IsStale ? "cached" : "reader") : "unknown")).ToArray());
        }
    }

    internal static string Alias(Dictionary<string, string> aliases, string instance)
    {
        if (!aliases.TryGetValue(instance, out string label))
            aliases[instance] = label = "receiver-" + (aliases.Count + 1);
        return label;
    }

    internal static string SafeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        string label = Regex.Replace(value, @"(?i)(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}|\b[0-9a-f]{12,}\b", "[ID]");
        return label.Contains('\\') || label.Contains('/') ? "[名称已隐藏]" : label;
    }

    internal static string Serialize(Capture capture) => JsonSerializer.Serialize(capture, Json);

    internal static string ReplayFile(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            using var file = File.OpenRead(path);
            return ReplayStream(file, file.Length);
        }

        using var zip = ZipFile.OpenRead(path);
        var first = zip.GetEntry(EntryName) ?? throw new InvalidDataException("报告缺少 " + EntryName);
        var additional = new[] { zip.GetEntry("samples/02.json"), zip.GetEntry("samples/03.json") }
            .Where(entry => entry != null).ToArray();
        string ReplayEntry(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            return ReplayStream(stream, entry.Length);
        }
        if (additional.Length == 0) return ReplayEntry(first);
        var rounds = new List<JsonElement>();
        foreach (var entry in new[] { first }.Concat(additional))
        {
            using var json = JsonDocument.Parse(ReplayEntry(entry));
            rounds.Add(json.RootElement.Clone());
        }
        return JsonSerializer.Serialize(new { Rounds = rounds }, Json);
    }

    private static string ReplayStream(Stream stream, long length)
    {
        if (length > 4 * 1024 * 1024) throw new InvalidDataException("重放数据超出大小限制");
        var capture = JsonSerializer.Deserialize<Capture>(stream);
        if (capture?.Schema != 1 || capture.Queries == null || capture.Collections == null)
            throw new InvalidDataException("不支持的重放格式");
        return JsonSerializer.Serialize(new
        {
            capture.AppVersion, capture.BuildId, capture.WindowsVersion, capture.ScanError, capture.StartedAt, capture.FinishedAt,
            Note = "离线重放只验证采集时的应答。无有效样本时保持未知。",
            capture.Collections, capture.Observations,
            Results = capture.Queries.Select(query => Replay(query, capture.Collections)).ToArray()
        }, Json);
    }

    private static bool MatchesChannel(string protocol, Collection channel, Collection[] collections)
    {
        if (protocol == "Neon75Feature")
            return VendorProtocol.IsNeon75StatusChannel(channel.Vid, channel.Pid, channel.Page, channel.Usage, channel.Feature);
        if (protocol == "RogScopeRxOmni")
            return VendorProtocol.IsRogOmni(channel.Vid, channel.Pid) && channel.OmniReport == 2 &&
                VendorProtocol.IsRogOmniReport(channel.Page, channel.Usage, channel.Input, channel.Output, 2) &&
                collections.Count(c => c.Receiver == channel.Receiver && !c.Excluded && c.OmniReport == 1 &&
                    VendorProtocol.IsRogOmni(c.Vid, c.Pid) &&
                    VendorProtocol.IsRogOmniReport(c.Page, c.Usage, c.Input, c.Output, 1)) == 1;

        if (protocol is "Sc580Receiver" or "Sc630Pro")
        {
            string instanceProduct = collections.FirstOrDefault(c => c.Receiver == channel.Receiver &&
                !c.Excluded && c.Vid == channel.Vid && c.Pid == channel.Pid && VendorProtocol.IsAulaSc630(c.Product))?.Product;
            var route = AulaBatteryProtocol.Match(channel.Vid, channel.Pid,
                AulaBatteryProtocol.CollectionProduct(channel.Product, instanceProduct),
                channel.Page, channel.Usage, channel.Input, channel.Output, channel.Feature);
            return route?.ToString() == protocol;
        }
        return protocol is "InphicIn9" or "InphicIn9Pro" &&
            VendorProtocol.IsInphicSplitReceiver(channel.Vid, channel.Pid) == (protocol == "InphicIn9Pro") &&
            VendorProtocol.IsScopedInphicChannel(channel.Vid, channel.Pid, channel.Product,
                channel.Mi02 ? "&mi_02" : "", channel.Page, channel.Usage, channel.Input, channel.Output);
    }

    private static Result ReplayRogScope(Query query)
    {
        byte[] paired = null;
        int? percent = null;
        bool? charging = null;
        bool afterSeen = false;
        bool unchanged = false;
        foreach (var sample in query.Samples ?? Array.Empty<Sample>())
        {
            if (sample.Stage == "omni-paired-after") afterSeen = true;
            if (!sample.Ok || sample.Hex?.Length != 128 || sample.Length != 64) continue;
            byte[] raw;
            try { raw = Convert.FromHexString(sample.Hex); }
            catch (FormatException) { continue; }
            if (sample.Stage == "omni-paired" && VendorProtocol.RogOmniHasScopeRx(raw)) paired = raw;
            if (paired != null && sample.Stage == "omni-battery" &&
                VendorProtocol.TryParseRogScopeBattery(raw, out int level, out bool charge))
            {
                percent = level;
                charging = charge;
            }
            if (percent != null && sample.Stage == "omni-paired-after")
                unchanged = VendorProtocol.RogOmniPairingUnchanged(paired, raw);
        }

        // Reports from older builds lacked the second pairing response.
        bool verified = percent != null && (query.Status is "Online" or "Charging") && (!afterSeen || unchanged);
        return new(query.Receiver, query.Interface, query.Protocol, query.Status,
            paired != null && (!afterSeen || unchanged) ? VendorProtocol.RogScopeRxWireless : null,
            verified ? percent : null, verified ? charging : null,
            verified ? (unchanged ? "captured" : "captured-without-pairing-recheck") : "unavailable");
    }

    internal static Result Replay(Query query, Collection[] collections)
    {
        var channel = collections.FirstOrDefault(c => c.Interface == query.Interface &&
            c.Receiver == query.Receiver && !c.Excluded);
        if (channel == null || !MatchesChannel(query.Protocol, channel, collections))
            return new(query.Receiver, query.Interface, query.Protocol,
                query.Interface == null ? query.Status : "collection-mismatch", query.Model, null, null, "unavailable");
        if (query.Protocol == "RogScopeRxOmni") return ReplayRogScope(query);
        if (query.Protocol == "Neon75Feature")
        {
            bool pending = false, charging = false;
            int level = -1, valid = 0;
            foreach (var sample in query.Samples ?? Array.Empty<Sample>())
            {
                if (sample.Stage == "set-feature")
                {
                    pending = sample.Ok && sample.Length == 65 &&
                        sample.Hex == Convert.ToHexString(VendorProtocol.BuildNeon75StatusRequest());
                    continue;
                }
                if (sample.Stage != "get-feature" || !pending) continue;
                pending = false;
                if (!sample.Ok || sample.Length != 65 || sample.Hex?.Length != 130) continue;
                try
                {
                    if (VendorProtocol.TryParseNeon75Status(Convert.FromHexString(sample.Hex), out int next, out bool charge))
                    { valid++; level = next; charging = charge; }
                }
                catch (FormatException) { }
            }
            bool neonVerified = valid >= 2 && (query.Status is "Online" or "Charging");
            return new(query.Receiver, query.Interface, query.Protocol, query.Status, query.Model,
                neonVerified ? level : null, neonVerified ? charging : null, neonVerified ? "captured" : "unavailable");
        }

        ReferenceBatteryProtocol.Reading last = null;
        int count = 0;
        bool mismatch = false;
        bool requestPending = query.Protocol == "InphicIn9Pro";
        byte sequence = 0;
        foreach (var sample in query.Samples ?? Array.Empty<Sample>())
        {
            if (sample.Stage is "write-identify" or "write-identity" or "set-feature")
            {
                requestPending = sample.Ok && (query.Protocol != "Sc580Receiver" ||
                    sample.Hex == Convert.ToHexString(AulaBatteryProtocol.IdentifyRequest()));
                sequence = sample.Sequence;
                continue;
            }
            if (!requestPending || !sample.Ok || sample.Hex == null || sample.Hex.Length > 82 || (sample.Hex.Length & 1) != 0)
                continue;
            byte[] bytes;
            try { bytes = Convert.FromHexString(sample.Hex); }
            catch (FormatException) { continue; }
            if (bytes.Length != sample.Length) continue;
            ReferenceBatteryProtocol.Reading reading;
            if (query.Protocol == "Sc580Receiver" && sample.Stage == "input" &&
                AulaBatteryProtocol.TryDecodeIdentify(bytes, out reading))
            {
                if (reading.Status == ReferenceBatteryProtocol.State.Sleep)
                { count = 0; last = null; continue; }
            }
            else if (query.Protocol == "Sc630Pro" && sample.Stage == "get-feature" && sequence != 0 && sample.Sequence == sequence &&
                VendorProtocol.TryParseAulaSc630Battery(bytes, sample.Sequence, out int level, out bool charging))
                reading = new(level, charging, true, charging ? ReferenceBatteryProtocol.State.Charging :
                    ReferenceBatteryProtocol.State.Online, "", "AULA SC630 Pro");
            else if (query.Protocol is "InphicIn9" or "InphicIn9Pro" && sample.Stage == "input" &&
                VendorProtocol.TryScopedInphicFrame(bytes, query.Protocol == "InphicIn9Pro", out reading)) { }
            else continue;

            if (last != null && last.Identity != reading.Identity) { mismatch = true; break; }
            last = reading;
            count++;
            requestPending = query.Protocol == "InphicIn9Pro";
        }
        bool completed = query.Status is "Online" or "Charging";
        bool verified = completed && !mismatch && count >= 2;
        string model = !mismatch && (completed || (count >= 1 && query.Protocol is "InphicIn9" or "InphicIn9Pro"))
            ? last?.Model ?? query.Model : query.Model;
        return new(query.Receiver, query.Interface, query.Protocol, query.Status, model,
            verified ? last.Level : null, verified && last.ChargingKnown ? last.Charging : null,
            verified ? "captured" : "unavailable");
    }
}
