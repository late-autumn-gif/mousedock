using System.Reflection;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class BluetoothStageChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static Task<List<DetectedDevice>> Read(string stage, Func<Task<List<DetectedDevice>>> work,
        DevicePreferences? preferences = null, bool includeHeadsets = false)
    {
        var method = typeof(BluetoothDiscovery).GetMethod("SafeAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        Require(method.GetParameters().Length == 5, "Bluetooth stages have no bounded result-aware reader.");
        return Task.Run(() => (Task<List<DetectedDevice>>)method.Invoke(null,
            [work, stage, 25, preferences, includeHeadsets])!);
    }

    public static void Timeout()
    {
        var release = new TaskCompletionSource<List<DetectedDevice>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int started = 0;
        try
        {
            Task<List<DetectedDevice>> Work() { Interlocked.Increment(ref started); return release.Task; }
            var first = Read("bounded-stage", Work);
            Require(first.Wait(500) && first.Result.Count == 0, "A stalled Bluetooth stage blocked the polling cycle.");
            var second = Read("bounded-stage", Work);
            Require(second.Wait(500) && second.Result.Count == 0 && started == 1,
                "Polling waited again or started another native operation while one was pending.");
        }
        finally { release.TrySetResult([]); }

        int recovered = 0;
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (recovered == 0 && DateTime.UtcNow < deadline)
            Read("bounded-stage", () => { recovered++; return Task.FromResult(new List<DetectedDevice>()); })
                .GetAwaiter().GetResult();
        Require(recovered == 1, "Bluetooth scanning did not recover after the pending operation completed.");
    }

    public static void LateResults()
    {
        var release = new TaskCompletionSource<List<DetectedDevice>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var first = Read("late-stage", () => release.Task);
            Require(first.Wait(500) && first.Result.Count == 0, "A pending stage exposed an unfinished result.");
            release.SetResult([
                new() { Id = "late-ble", Level = 72, Charging = true },
                new() { Id = "old-offline", Connected = false, Level = 99 }
            ]);
            var deadline = DateTime.UtcNow.AddSeconds(2);
            List<DetectedDevice> late = [];
            while (late.Count == 0 && DateTime.UtcNow < deadline)
                late = Read("late-stage", () => throw new Exception("A completed late batch was discarded."))
                    .GetAwaiter().GetResult();
            Require(late is [{ Id: "late-ble", Level: 72, IsStale: true }],
                "Only connected late results may return, marked as cached.");
            Require(first.Result.Count == 0, "Late discovery changed an already returned snapshot.");
            var fresh = Read("late-stage", () => Task.FromResult(new List<DetectedDevice>
                { new() { Id = "late-ble", Level = 68 } })).GetAwaiter().GetResult();
            Require(fresh is [{ Level: 68, IsStale: false }], "A successful scan did not replace the late cached reading.");
        }
        finally { release.TrySetResult([]); }
    }

    public static void PreferenceChanges()
    {
        foreach (bool changePreferences in new[] { true, false })
        {
            var settings = new AppSettings { SelectedDeviceIds = ["saved-selection"] };
            var preferences = new DevicePreferences(settings);
            var stages = new[] { "changed-ble", "changed-classic" };
            var releases = stages.Select(_ => new TaskCompletionSource<List<DetectedDevice>>(
                TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            bool includeHeadsets = false;
            var recovered = new HashSet<string>();
            try
            {
                for (int i = 0; i < stages.Length; i++)
                {
                    int index = i;
                    Require(Invoke(stages[i], () => releases[index].Task).GetAwaiter().GetResult().Count == 0,
                        "The held stage did not time out.");
                }
                if (changePreferences) preferences.Disable(new DeviceInfo { Id = "old-reading" });
                else includeHeadsets = true;
                foreach (var stage in stages)
                    Require(Invoke(stage, Fresh).GetAwaiter().GetResult().Count == 0,
                        "A mode change started another worker before the previous one completed.");
                foreach (var release in releases)
                    release.SetResult([new() { Id = "old-reading", Level = 72 }]);
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (recovered.Count != 2 && DateTime.UtcNow < deadline)
                    foreach (var stage in stages.Where(stage => !recovered.Contains(stage)).ToArray())
                    {
                        var batch = Invoke(stage, Fresh).GetAwaiter().GetResult();
                        Require(batch.All(device => device.Level != 72),
                            "An old result crossed a preference generation or headset mode change.");
                        if (batch.Any(device => device.Level == 68)) recovered.Add(stage);
                    }
                Require(recovered.Count == 2, "Completed old stages kept both worker slots occupied.");
                Require(settings.SelectedDeviceIds.SequenceEqual(["saved-selection"]),
                    "Bluetooth recovery changed the saved display selection.");
            }
            finally { foreach (var release in releases) release.TrySetResult([]); }

            Task<List<DetectedDevice>> Fresh() => Task.FromResult(new List<DetectedDevice> { new() { Id = "fresh", Level = 68 } });
            Task<List<DetectedDevice>> Invoke(string stage, Func<Task<List<DetectedDevice>>> work) =>
                Read(stage, work, preferences, includeHeadsets);
        }
    }
}
