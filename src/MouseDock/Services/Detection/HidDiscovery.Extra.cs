using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    internal static void QueryExtraDevices(Dictionary<string, Bucket> buckets, List<VendorCollectionInfo> collections,
        bool safeMode, DevicePreferences preferences,
        Func<ExtraDeviceRoutes.Route, ushort, ushort, string, IReadOnlyList<VendorCollectionInfo>,
            BatteryReplay.Trace, ReferenceBatteryProtocol.Reading> query)
    {
        foreach (var bucket in buckets.Values.ToArray())
        {
            var route = ExtraDeviceRoutes.Find(bucket.Vid, bucket.Pid, bucket.Product);
            if (route == null) continue;
            if (!route.SharedReceiver)
            {
                bucket.ExtraRoute = route;
                bucket.Kind = route.Kind;
                bucket.HandledBy = route.Kind switch
                {
                    DeviceKind.Mouse => HandledBy.ExtraMouse,
                    DeviceKind.Controller => HandledBy.ExtraController,
                    DeviceKind.Headset => HandledBy.ExtraHeadset,
                    _ => HandledBy.ExtraKeyboard
                };
                bucket.Battery = -1; bucket.Charging = bucket.ChargeFull = bucket.ReadStale = false;
                bucket.BatterySource = ""; bucket.ExtraModel = null;
            }
            var trace = new BatteryReplay.Trace();
            string instance = bucket.InstanceId;
            string legacy = $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}";
            Bucket Child(string child) => new()
            {
                InstanceId = child, Vid = bucket.Vid, Pid = bucket.Pid, Path = bucket.Path,
                Product = bucket.Product, Kind = route.Kind, ExtraRoute = route,
                HandledBy = HandledBy.ExtraKeyboard
            };
            void Record(string status, string model = null, int level = -1) =>
                BatteryReplay.Current?.AddQuery(instance, null, route.Reader, status, model, level, trace);
            var disabledChildren = route.SharedReceiver
                ? preferences?.DisabledDescendants(instance + "-keyboard-") : null;
            if (disabledChildren?.Length > 0)
            {
                foreach (string child in disabledChildren) buckets[child] = Child(child);
                Record("RecognitionDisabled");
                continue;
            }
            if (string.IsNullOrEmpty(instance) || safeMode ||
                !(preferences?.CanRead(instance, legacy, VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true))
            {
                bucket.BatteryFailures.Add(safeMode ? "安全模式已跳过扩展协议" : "此实例识别已停用");
                Record(safeMode ? "SafeMode" : "RecognitionDisabled");
                continue;
            }
            var selected = collections.Where(c => c.Vid == bucket.Vid && c.Pid == bucket.Pid &&
                InstanceScope.ForPath(c.Path, "") == instance)
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
            if (selected.Length == 0)
            {
                bucket.BatteryFailures.Add(route.Reader + " · 未找到本实例的可用集合");
                Record("MissingChannel");
                continue;
            }
            ReferenceBatteryProtocol.Reading reading;
            using (InstanceScope.Enter(instance))
            {
                try { reading = query(route, bucket.Vid, bucket.Pid, bucket.Product, selected, trace); }
                catch (Exception ex) { reading = ReferenceBatteryProtocol.Reading.Failed(ex.GetType().Name); }
            }
            reading ??= ReferenceBatteryProtocol.Reading.Failed("读取器未返回结果");
            bool active = reading.Status is ReferenceBatteryProtocol.State.Online or
                ReferenceBatteryProtocol.State.Charging or ReferenceBatteryProtocol.State.Full;
            bool valid = reading.Level is >= 0 and <= 100 && active;
            Record(reading.Status.ToString(), reading.Model, valid ? reading.Level : -1);
            if (active && (valid || reading.ChargingKnown))
            {
                var destination = bucket;
                if (route.SharedReceiver)
                {
                    if (string.IsNullOrWhiteSpace(reading.Identity)) continue;
                    string child = instance + "-keyboard-" + Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(reading.Identity)))[..12];
                    destination = Child(child);
                    buckets[child] = destination;
                }
                destination.Battery = valid ? reading.Level : -1;
                destination.Charging = reading.ChargingKnown && reading.Charging;
                destination.ChargeFull = reading.Status == ReferenceBatteryProtocol.State.Full;
                destination.BatterySource = route.Reader + " · " + reading.Diagnostic;
                destination.ExtraModel = reading.Model;
            }
            else bucket.BatteryFailures.Add(route.Reader + " · " + reading.Diagnostic);
            LastDiagnostics.Add($"{instance} {route.Reader}: {reading.Status}, {reading.Level}, {reading.Diagnostic}");
        }
    }
}
