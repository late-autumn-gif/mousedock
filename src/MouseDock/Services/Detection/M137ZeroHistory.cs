using System;
using System.Collections.Generic;
using System.Linq;

namespace MouseDock.Services.Detection;

internal sealed class M137ZeroHistory
{
    internal static M137ZeroHistory Recent { get; } = new();
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(15);
    private const int Capacity = 16;

    private readonly object gate = new();
    private readonly List<(string Instance, Entry Event)> events = new();
    private readonly Dictionary<string, (int Level, DateTimeOffset At)> previous = new(StringComparer.OrdinalIgnoreCase);

    internal sealed record Entry(string Receiver, string Interface, DateTimeOffset At, int PreviousPercent,
        bool ExtraRecheck, int? FinalPercent, BatteryReplay.Sample[] Samples);

    internal void Observe(string instance, string path, ReferenceBatteryProtocol.Reading reading,
        BatteryReplay.Sample[] samples, DateTimeOffset at)
    {
        bool rechecked = samples.Any(sample => sample.Result == "zero-recheck");
        bool confirmed = reading is { Identity: "M137", Level: >= 0, Status: ReferenceBatteryProtocol.State.Online };
        var first = samples.FirstOrDefault(sample => sample is { Stage: "get-feature", Result: "accepted", Hex: not null });
        int firstPercent = -1;
        if (first != null && M137FeatureProtocol.TryPercent(Convert.FromHexString(first.Hex), out int level, out string model) &&
            model == "M137")
            firstPercent = level;

        lock (gate)
        {
            int? before = null;
            if (rechecked && firstPercent > 0)
            {
                before = firstPercent;
            }
            else if (firstPercent == 0 && previous.TryGetValue(instance, out var last) &&
                last.Level > 0 && at - last.At < Retention)
            {
                before = last.Level;
            }

            if (before.HasValue)
            {
                HidInventory.ParsePath(path, out _, out _, out string label);
                events.RemoveAll(item => at - item.Event.At >= Retention);
                if (events.Count == Capacity) events.RemoveAt(0);
                events.Add((instance, new(null, label, at, before.Value, rechecked, confirmed ? reading.Level : null, samples)));
            }

            if (confirmed)
            {
                if (previous.Count == Capacity && !previous.ContainsKey(instance))
                    previous.Remove(previous.MinBy(item => item.Value.At).Key);
                previous[instance] = (reading.Level, at);
            }
            else if (reading?.Identity != null)
            {
                previous.Remove(instance);
            }
        }
    }

    internal Entry[] Snapshot(IEnumerable<string> scope, Dictionary<string, string> aliases, DateTimeOffset at)
    {
        var selected = scope?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (gate)
        {
            events.RemoveAll(item => at - item.Event.At >= Retention);
            return events.Where(item => item.Event.At <= at && (selected == null || selected.Contains(item.Instance)))
                .Select(item => item.Event with { Receiver = BatteryReplay.Alias(aliases, item.Instance) }).ToArray();
        }
    }
}
