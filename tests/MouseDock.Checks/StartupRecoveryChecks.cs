using System.Reflection;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class StartupRecoveryChecks
{
    internal static void BluetoothStageTimeout()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int started = 0;
        Task Work()
        {
            Interlocked.Increment(ref started);
            return release.Task;
        }

        Task? first = null;
        try
        {
            first = RunStage(Work, "startup-timeout-fixture");
            Require(first.Wait(500), "A stalled Bluetooth startup stage blocked the completed HID scan.");
            first.GetAwaiter().GetResult();
            var second = RunStage(Work, "startup-timeout-fixture");
            Require(second.Wait(500), "A repeated poll waited on the stalled Bluetooth stage again.");
            second.GetAwaiter().GetResult();
            Require(started == 1, "Polling started another native operation while the first was still pending.");
        }
        finally
        {
            release.TrySetResult();
            first?.Wait(1000);
        }

        int recovered = 0;
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (recovered == 0 && DateTime.UtcNow < deadline)
        {
            var next = RunStage(() => { recovered++; return Task.CompletedTask; }, "startup-timeout-fixture");
            Require(next.Wait(500), "A completed startup operation prevented the next Bluetooth scan.");
            next.GetAwaiter().GetResult();
        }
        Require(recovered == 1, "Bluetooth scanning did not recover after the pending operation completed.");
    }

    internal static void BluetoothLateResults()
    {
        var method = typeof(BluetoothDiscovery).GetMethod("SafeAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var release = new TaskCompletionSource<List<DetectedDevice>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Invoke(() => release.Task);
        try
        {
            Require(first.Wait(500), "A delayed Bluetooth stage did not release the HID polling cycle.");
            Require(first.Result.Count == 0, "An unfinished Bluetooth stage exposed a partial result.");
            release.SetResult([
                new DetectedDevice { Id = "late-ble", Level = 72, Charging = true },
                new DetectedDevice { Id = "old-offline", Connected = false }
            ]);
            var deadline = DateTime.UtcNow.AddSeconds(2);
            List<DetectedDevice> late = [];
            while (late.Count == 0 && DateTime.UtcNow < deadline)
                late = Invoke(() => throw new InvalidOperationException("The completed late batch was discarded.")).GetAwaiter().GetResult();
            Require(late.Count == 1 && late[0].Level == 72 && late[0].IsStale,
                "A completed late batch must remain available as a stale reading.");
            Require(first.Result.Count == 0, "Late discovery mutated the already published empty snapshot.");
            var fresh = Invoke(() => Task.FromResult(new List<DetectedDevice> { new() { Id = "late-ble", Level = 68 } }))
                .GetAwaiter().GetResult();
            Require(fresh.Count == 1 && fresh[0].Level == 68 && !fresh[0].IsStale,
                "A later successful scan did not replace the stale batch with current readings.");
        }
        finally { release.TrySetResult([]); }

        Task<List<DetectedDevice>> Invoke(Func<Task<List<DetectedDevice>>> work) =>
            Task.Run(() => (Task<List<DetectedDevice>>)method.Invoke(null, [work, "startup-late-fixture", 25, null, false])!);
    }

    internal static void BluetoothChangedPreferences()
    {
        foreach (bool changePreferences in new[] { true, false })
        {
            var settings = new AppSettings { SelectedDeviceIds = ["saved-selection"] };
            var preferences = new DevicePreferences(settings);
            var method = typeof(BluetoothDiscovery).GetMethod("SafeAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            var stages = new[] { "startup-mode-ble", "startup-mode-classic" };
            var releases = stages.Select(_ => new TaskCompletionSource<List<DetectedDevice>>(
                TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            bool includeHeadsets = false;
            var recovered = new HashSet<string>();
            try
            {
                for (int i = 0; i < stages.Length; i++)
                    Require(Invoke(stages[i], () => releases[i].Task).GetAwaiter().GetResult().Count == 0,
                        "The held stage did not time out.");
                if (changePreferences) preferences.Disable(new DeviceInfo { Id = "old-reading" });
                else includeHeadsets = true;
                foreach (var stage in stages)
                    Require(Invoke(stage, Fresh).GetAwaiter().GetResult().Count == 0,
                        "A changed mode started a second native worker while the old one was pending.");
                foreach (var release in releases)
                    release.SetResult([new DetectedDevice { Id = "old-reading", Level = 72 }]);
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (recovered.Count != 2 && DateTime.UtcNow < deadline)
                    foreach (var stage in stages.Where(stage => !recovered.Contains(stage)).ToArray())
                    {
                        var batch = Invoke(stage, Fresh).GetAwaiter().GetResult();
                        Require(batch.All(device => device.Level != 72),
                            "A late batch crossed a changed preference generation or headset mode.");
                        if (batch.Any(device => device.Level == 68)) recovered.Add(stage);
                    }
                Require(recovered.Count == 2, "Completed old stages kept the two worker slots occupied after the mode changed.");
                Require(settings.SelectedDeviceIds.SequenceEqual(["saved-selection"]),
                    "Startup recovery changed the saved display selection.");
            }
            finally { foreach (var release in releases) release.TrySetResult([]); }

            Task<List<DetectedDevice>> Fresh() => Task.FromResult(new List<DetectedDevice> { new() { Id = "fresh", Level = 68 } });
            Task<List<DetectedDevice>> Invoke(string stage, Func<Task<List<DetectedDevice>>> work) =>
                Task.Run(() => (Task<List<DetectedDevice>>)method.Invoke(null, [work, stage, 25, preferences, includeHeadsets])!);
        }
    }

    private static Task RunStage(Func<Task> work, string stage)
    {
        var method = typeof(BluetoothDiscovery).GetMethod("SafeAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        Func<Task<List<DetectedDevice>>> batch = async () => { await work(); return []; };
        object?[] args = method.GetParameters().Length == 2 ? [work, stage] : [batch, stage, 25, null, false];
        return Task.Run(() => (Task)method.Invoke(null, args)!);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
