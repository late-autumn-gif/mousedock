using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MouseDock.Models;
using Windows.Gaming.Input;

namespace MouseDock.Services.Detection;

internal static class ControllerExpansionSystem
{
    internal sealed record Sample(string Id, string Name, ushort Vid, ushort Pid, bool Wireless,
        string Status, int? Remaining, int? Full, int? Design, int? ChargeRate);
    internal sealed record Bindings(IReadOnlyDictionary<string, string> Paths, IReadOnlySet<(ushort Vid, ushort Pid)> Native,
        bool AllowIndependent = false)
    {
        internal string Resolve(string identity, ushort vid, ushort pid)
        {
            if (string.IsNullOrWhiteSpace(identity) || identity.Length > 2048) return null;
            if (Paths.TryGetValue(identity, out string instance)) return instance;
            return Native.Contains((vid, pid)) && !AllowIndependent ? null : StableId(identity);
        }
    }
    private static readonly object sync = new();
    private sealed record Recorded(Sample Sample, string Outcome, int? Error);
    private sealed record Batch(IReadOnlyList<DetectedDevice> Devices, IReadOnlyList<Recorded> Records);
    private static Task<Batch> pending;

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
        var bindings = new Bindings(paths, native, AllowIndependent:true);
        var scope = BatteryReplay.Current?.SelectedIds;
        Task<Batch> worker;
        lock (sync)
        {
            if (pending is { IsCompleted: false }) { RecordScan("Busy"); return Array.Empty<DetectedDevice>(); }
            worker = Task.Run(() => Capture(preferences, safeMode, bindings, scope)); pending = worker;
        }
        try
        {
            if (!worker.Wait(2500)) { RecordScan("Timeout"); return Array.Empty<DetectedDevice>(); }
            var batch = worker.Result;
            foreach (var record in batch.Records) Record(record.Sample,record.Outcome,record.Error,bindings);
            if (batch.Records.Count == 0) RecordScan("NoMatchingSystemController");
            return batch.Devices;
        }
        catch (Exception ex) { RecordScan(ex.GetType().Name); return Array.Empty<DetectedDevice>(); }
    }

    internal static bool CanQuery(string identity, ushort vid, ushort pid, Bindings bindings,
        DevicePreferences preferences, bool safeMode)
    {
        if (safeMode || bindings.Resolve(identity,vid,pid) is not { } id ||
            !(preferences?.CanRead(id,StableId(identity),$"hid-{vid:X4}-{pid:X4}") ?? true)) return false;
        // An independent WGI object cannot bypass exclusions on a same-model unresolved HID device.
        if (bindings.AllowIndependent && !bindings.Paths.ContainsKey(identity) && bindings.Native.Contains((vid,pid)))
        {
            bool found = false;
            foreach (var path in bindings.Paths)
            {
                HidInventory.ParsePath(path.Key,out ushort v,out ushort p,out _);
                if (v == vid && p == pid)
                {
                    found = true;
                    if (!(preferences?.CanRead(path.Value) ?? true)) return false;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    internal static bool InScope(string identity,ushort vid,ushort pid,Bindings bindings,IReadOnlySet<string> scope) =>
        bindings.Resolve(identity,vid,pid) is { } id && (scope == null || scope.Contains(id));

    private static Batch Capture(DevicePreferences preferences, bool safeMode, Bindings bindings, IReadOnlySet<string> scope)
    {
        var samples = new List<Sample>();
        var records = new List<Recorded>();
        foreach (var c in RawGameController.RawGameControllers.Take(16).ToArray())
        {
            if (Gamepad.FromGameController(c) == null || string.IsNullOrEmpty(c.NonRoamableId)) continue;
            if (VendorProtocol.ExtraControllerFamily(c.HardwareVendorId, c.HardwareProductId, null) != null) continue;
            if (bindings.Resolve(c.NonRoamableId, c.HardwareVendorId, c.HardwareProductId) == null) continue;
            if (!InScope(c.NonRoamableId,c.HardwareVendorId,c.HardwareProductId,bindings,scope)) continue;
            var status = "Unknown";
            int? remaining = null, full = null, design = null, rate = null;
            string outcome = safeMode ? "SafeMode" : "RecognitionDisabled"; int? error = null;
            try
            {
                var b = CanQuery(c.NonRoamableId, c.HardwareVendorId, c.HardwareProductId,
                    bindings, preferences, safeMode) ? c.TryGetBatteryReport() : null;
                if (b != null)
                {
                    outcome = "Reported";
                    status = b.Status.ToString(); remaining = b.RemainingCapacityInMilliwattHours;
                    full = b.FullChargeCapacityInMilliwattHours; design = b.DesignCapacityInMilliwattHours;
                    rate = b.ChargeRateInMilliwatts;
                }
                else if (CanQuery(c.NonRoamableId,c.HardwareVendorId,c.HardwareProductId,bindings,preferences,safeMode)) outcome = "NoBatteryReport";
            }
            catch (Exception ex) { outcome = ex.GetType().Name; error = ex.HResult; }
            var sample = new Sample(c.NonRoamableId,c.DisplayName,c.HardwareVendorId,c.HardwareProductId,
                c.IsWireless,status,remaining,full,design,rate);
            samples.Add(sample); records.Add(new(sample,outcome,error));
        }
        var current = RawGameController.RawGameControllers.Select(c => c.NonRoamableId)
            .ToHashSet(StringComparer.Ordinal);
        return new(Normalize(samples.Where(s => current.Contains(s.Id)),bindings)
            .Where(d => preferences?.CanRead(d.Id) ?? true).ToArray(), records.Where(r => current.Contains(r.Sample.Id)).ToArray());
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
            int level = -1;
            bool coarse = false;
            bool known = s.Status is "Charging" or "Discharging" or "Idle" &&
                s.Full is > 0 && s.Remaining is >= 0 && s.Remaining <= s.Full;
            if (known)
            {
                if (s.Design is not > 0 && !s.ChargeRate.HasValue)
                {
                    foreach (int percent in new[] { 0,10,40,70,100 })
                        if (s.Remaining.Value * 100L == s.Full.Value * (long)percent) coarse = true;
                }
                if (!coarse && s.Full > 10)
                    level = (int)Math.Round(s.Remaining.Value * 100.0 / s.Full.Value);
            }
            string id = bindings == null ? StableId(s.Id) : bindings.Resolve(s.Id, s.Vid, s.Pid);
            if (id == null) continue;
            string bandReading = known && (coarse || s.Full <= 10) ? $"{s.Remaining}/{s.Full}" : "";
            string note = bandReading.Length > 0 ? $"系统电量相对读数 {bandReading}，不是精确百分比" :
                known && s.Full <= 10 ? "系统仅提供粗略电量档位" : level < 0 ? "系统未提供有效电量" : "";
            result.Add(new DetectedDevice
            {
                Id = id, PhysicalKey = id, Name = string.IsNullOrWhiteSpace(s.Name) ? "游戏手柄" : s.Name,
                RawName = s.Name ?? "", Kind = DeviceKind.Controller, InterfaceDiscovered = true,
                PeripheralConfirmed = true, Connected = true, Transport = s.Wireless ? "无线" : "USB",
                Level = level, BatteryBand = bandReading, Charging = known && s.Status == "Charging",
                HandledBy = HandledBy.SystemController,
                ChargeFull = known && level >= 0 && s.Status == "Idle" && s.Remaining == s.Full,
                Note = note,
                Detail = $"WGI; VID={s.Vid:X4};PID={s.Pid:X4};status={s.Status};" +
                    $"remaining={s.Remaining};full={s.Full};design={s.Design};rate={s.ChargeRate};" +
                    $"reporting={(bandReading.Length > 0 ? "relative-reading" : level >= 0 ? "percentage" : "unknown")}"
            });
            if (bindings is { AllowIndependent:true } && !bindings.Paths.ContainsKey(s.Id) && bindings.Native.Contains((s.Vid,s.Pid)))
            {
                result[^1].Name += " · 系统";
                result[^1].Note += "；独立系统身份，尚未与 HID 接口关联";
            }
        }
        return result;
    }
    internal static string StableId(string identity) =>
        "system-controller-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24].ToLowerInvariant();

    internal sealed record Evidence(ushort Vid,ushort Pid,bool Wireless,string Status,int? Remaining,int? Full,int? Design,int? ChargeRate,string Association);
    private static void Record(Sample sample,string outcome,int? error,Bindings bindings)
    {
        if (BatteryReplay.Current is not { } scan) return;
        string id = bindings.Resolve(sample.Id,sample.Vid,sample.Pid);
        if (id == null) return;
        var evidence = new Evidence(sample.Vid,sample.Pid,sample.Wireless,sample.Status,sample.Remaining,sample.Full,sample.Design,sample.ChargeRate,
            bindings.Paths.ContainsKey(sample.Id) ? "exact-path" : "independent-system-id");
        var trace = new BatteryReplay.Trace();
        trace.Add("system-battery-report",outcome == "Reported",result:JsonSerializer.Serialize(evidence),nativeError:error);
        scan.AddQuery(id,null,"WindowsGameController",outcome,null,trace:trace);
    }
    private static void RecordScan(string status) => BatteryReplay.Current?.AddQuery("system-controller-scan",null,"WindowsGameControllerScan",status,null);
}

internal static partial class VendorProtocol
{
    internal static IReadOnlyList<DetectedDevice> ReadSystemControllers(DevicePreferences preferences, bool safeMode,
        IReadOnlyList<DetectedDevice> devices) => ControllerExpansionSystem.Read(preferences, safeMode, devices);
}
