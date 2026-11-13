using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MouseDock.Models;
using Windows.Gaming.Input;

namespace MouseDock.Services.Detection;

internal static class ControllerExpansionSystem
{
    internal sealed record Sample(string Id, string Name, ushort Vid, ushort Pid, bool Wireless,
        string Status, int? Remaining, int? Full, int? Design, int? ChargeRate);
    internal sealed record Bindings(IReadOnlyDictionary<string, string> Paths, IReadOnlySet<(ushort Vid, ushort Pid)> Native)
    {
        internal string Resolve(string identity, ushort vid, ushort pid)
        {
            if (Paths.TryGetValue(identity, out string instance)) return instance;
            return Native.Contains((vid, pid)) ? null : StableId(identity);
        }
    }
    private static readonly object sync = new();
    private static Task<IReadOnlyList<DetectedDevice>> pending;

    internal static IReadOnlyList<DetectedDevice> Read(DevicePreferences preferences, bool safeMode,
        IReadOnlyList<DetectedDevice> devices)
    {
        // InstanceScope is thread-local; capture its exact path mapping before entering the worker.
        var paths = InstanceScope.SnapshotPaths();
        var ids = devices.Where(d => d.Kind == DeviceKind.Controller).Select(d => d.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var native = new HashSet<(ushort Vid, ushort Pid)>();
        foreach (var path in paths.Where(p => ids.Contains(p.Value)).Select(p => p.Key))
        {
            HidInventory.ParsePath(path, out ushort vid, out ushort pid, out _);
            native.Add((vid, pid));
        }
        var bindings = new Bindings(paths, native);
        Task<IReadOnlyList<DetectedDevice>> worker;
        lock (sync)
        {
            if (pending is { IsCompleted: false }) return Array.Empty<DetectedDevice>();
            worker = Task.Run(() => Capture(preferences, safeMode, bindings)); pending = worker;
        }
        try { return worker.Wait(2500) ? worker.Result : Array.Empty<DetectedDevice>(); }
        catch { return Array.Empty<DetectedDevice>(); }
    }

    internal static bool CanQuery(string identity, ushort vid, ushort pid, Bindings bindings,
        DevicePreferences preferences, bool safeMode) =>
        !safeMode && bindings.Resolve(identity, vid, pid) is { } id &&
        (preferences?.CanRead(id, StableId(identity), $"hid-{vid:X4}-{pid:X4}") ?? true);

    private static IReadOnlyList<DetectedDevice> Capture(DevicePreferences preferences, bool safeMode, Bindings bindings)
    {
        var samples = new List<Sample>();
        foreach (var c in RawGameController.RawGameControllers.ToArray())
        {
            if (Gamepad.FromGameController(c) == null || string.IsNullOrEmpty(c.NonRoamableId)) continue;
            if (VendorProtocol.ExtraControllerFamily(c.HardwareVendorId, c.HardwareProductId, null) != null) continue;
            if (bindings.Resolve(c.NonRoamableId, c.HardwareVendorId, c.HardwareProductId) == null) continue;
            var status = "Unknown";
            int? remaining = null, full = null, design = null, rate = null;
            try
            {
                var b = CanQuery(c.NonRoamableId, c.HardwareVendorId, c.HardwareProductId,
                    bindings, preferences, safeMode) ? c.TryGetBatteryReport() : null;
                if (b != null)
                {
                    status = b.Status.ToString(); remaining = b.RemainingCapacityInMilliwattHours;
                    full = b.FullChargeCapacityInMilliwattHours; design = b.DesignCapacityInMilliwattHours;
                    rate = b.ChargeRateInMilliwatts;
                }
            }
            catch { }
            samples.Add(new(c.NonRoamableId, c.DisplayName, c.HardwareVendorId, c.HardwareProductId,
                c.IsWireless, status, remaining, full, design, rate));
        }
        var current = RawGameController.RawGameControllers.Select(c => c.NonRoamableId)
            .ToHashSet(StringComparer.Ordinal);
        return Normalize(samples.Where(s => current.Contains(s.Id)), bindings)
            .Where(d => preferences?.CanRead(d.Id) ?? true).ToArray();
    }

    internal static IReadOnlyList<DetectedDevice> Normalize(IEnumerable<Sample> samples, Bindings bindings = null)
    {
        var result = new List<DetectedDevice>();
        foreach (var group in samples.Where(s => s != null && s.Id is { Length: >= 1 and <= 2048 })
            .GroupBy(s => s.Id, StringComparer.Ordinal))
        {
            if (group.Count() != 1) continue;
            var s = group.Single();
            string family = VendorProtocol.ExtraControllerFamily(s.Vid, s.Pid, null);
            if (family != null) continue;
            int level = -1, band = 0;
            bool known = s.Status is "Charging" or "Discharging" or "Idle" &&
                s.Full is > 0 && s.Remaining is >= 0 && s.Remaining <= s.Full;
            if (known)
            {
                if (s.Design is not > 0 && !s.ChargeRate.HasValue)
                {
                    foreach (var (percent, bucket) in new[] { (10, 1), (40, 2), (70, 3), (100, 5) })
                        if (s.Remaining.Value * 100L == s.Full.Value * (long)percent) band = bucket;
                }
                if (band == 0 && s.Full > 10)
                    level = (int)Math.Round(s.Remaining.Value * 100.0 / s.Full.Value);
            }
            string id = bindings == null ? StableId(s.Id) : bindings.Resolve(s.Id, s.Vid, s.Pid);
            if (id == null) continue;
            string note = band > 0 ? $"系统电量档位 {band}/5" :
                known && s.Full <= 10 ? "系统仅提供粗略电量档位" : level < 0 ? "系统未提供有效电量" : "";
            result.Add(new DetectedDevice
            {
                Id = id, PhysicalKey = id, Name = string.IsNullOrWhiteSpace(s.Name) ? "游戏手柄" : s.Name,
                RawName = s.Name ?? "", Kind = DeviceKind.Controller, InterfaceDiscovered = true,
                PeripheralConfirmed = true, Connected = true, Transport = s.Wireless ? "无线" : "USB",
                Level = level, Charging = known && s.Status == "Charging",
                ChargeFull = known && s.Status == "Idle" && s.Remaining == s.Full,
                Note = note,
                Detail = $"WGI; VID={s.Vid:X4};PID={s.Pid:X4};status={s.Status};" +
                    $"remaining={s.Remaining};full={s.Full};design={s.Design};rate={s.ChargeRate};" +
                    $"reporting={(band > 0 ? "system-band=" + band + "/5" : level >= 0 ? "percentage" : "unknown")}"
            });
        }
        return result;
    }
    internal static string StableId(string identity) =>
        "system-controller-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24].ToLowerInvariant();
}

internal static partial class VendorProtocol
{
    internal static IReadOnlyList<DetectedDevice> ReadSystemControllers(DevicePreferences preferences, bool safeMode,
        IReadOnlyList<DetectedDevice> devices) => ControllerExpansionSystem.Read(preferences, safeMode, devices);
}
