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
    internal static void BeginScan() { paths = new(StringComparer.OrdinalIgnoreCase); Current = null; }
    internal static string Register(string path, IEnumerable<(string InstanceId, string Service)> ancestors)
    {
        string key = KeyFor(path, ancestors);
        (paths ??= new(StringComparer.OrdinalIgnoreCase))[path] = key;
        return key;
    }
    internal static string KeyFor(string path, IEnumerable<(string InstanceId, string Service)> ancestors)
    {
        // A USB device parent identifies an instance; interface IDs and model names do not.
        string parent = ancestors.Select(x => x.InstanceId).FirstOrDefault(x =>
            x.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) &&
            !x.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) ?? path;
        return "instance-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parent.ToUpperInvariant())))[..24];
    }
    internal static string ForPath(string path, string fallback) => paths != null && paths.TryGetValue(path ?? "", out var key) ? key : fallback;
    internal static bool Allows(string path) => Current == null || ForPath(path, "") == Current;
    internal static IDisposable Enter(string key) { var previous = Current; Current = key; return new Restore(previous); }
    private sealed class Restore(string previous) : IDisposable { public void Dispose() => Current = previous; }
}
