using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace MouseDock.Services.Detection;
internal static class InstanceScope
{
    [ThreadStatic] internal static string Current;
    [ThreadStatic] private static Dictionary<string,string> paths;
    [ThreadStatic] private static Dictionary<string, (ushort Vid, ushort Pid)> usbPaths;
    internal static void BeginScan()
    {
        paths = new(StringComparer.OrdinalIgnoreCase);
        usbPaths = new(StringComparer.OrdinalIgnoreCase);
        Current = null;
    }
    internal static string Register(string path, IEnumerable<(string InstanceId, string Service)> ancestors)
    {
        // A USB device parent identifies an instance; interface IDs and model names do not.
        string parent = ancestors.Select(x => x.InstanceId).FirstOrDefault(x =>
            x?.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) == true &&
            !x.Contains("&MI_", StringComparison.OrdinalIgnoreCase));
        string key = "instance-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((parent ?? path).ToUpperInvariant())))[..24];
        (paths ??= new(StringComparer.OrdinalIgnoreCase))[path] = key;
        if (parent != null && ancestors.TakeWhile(x => x.InstanceId != parent)
                .All(x => !IsBluetoothNode(x.InstanceId)))
        {
            HidInventory.ParsePath(parent, out ushort vid, out ushort pid, out _);
            (usbPaths ??= new(StringComparer.OrdinalIgnoreCase))[path] = (vid, pid);
        }
        else usbPaths?.Remove(path);
        return key;
    }
    internal static string ForPath(string path, string fallback) => paths != null && paths.TryGetValue(path ?? "", out var key) ? key : fallback;
    internal static bool MatchesUsbInstance(string instance, string path, ushort vid, ushort pid) =>
        vid != 0 && pid != 0 && path != null && usbPaths != null && usbPaths.TryGetValue(path, out var device) &&
        device == (vid, pid) && ForPath(path, null) == instance;

    private static bool IsBluetoothNode(string id) =>
        id?.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase) == true ||
        id?.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase) == true ||
        id?.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase) == true ||
        id?.StartsWith("BTH\\", StringComparison.OrdinalIgnoreCase) == true;
    internal static IReadOnlyDictionary<string, string> SnapshotPaths() =>
        paths == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) :
            new Dictionary<string, string>(paths, StringComparer.OrdinalIgnoreCase);
    internal static bool Allows(string path) => Current == null || ForPath(path, "") == Current;
    internal static IDisposable Enter(string key) { var previous = Current; Current = key; return new Restore(previous); }
    private sealed class Restore(string previous) : IDisposable { public void Dispose() => Current = previous; }
}
