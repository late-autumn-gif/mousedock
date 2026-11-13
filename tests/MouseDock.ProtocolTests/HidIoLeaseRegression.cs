using MouseDock.Services.Detection;

internal static class HidIoLeaseRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new Exception("HID lease: " + message);
        }
        string instance = "instance-lease-" + Guid.NewGuid().ToString("N");
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        using var ended = new ManualResetEventSlim();
        using (InstanceScope.Enter(instance))
        {
            var reading = ReferenceQueryGate.Run("lease-path", () =>
            {
                entered.Set();
                finish.Wait();
                ended.Set();
                return ReferenceBatteryProtocol.Reading.Failed("fixture");
            }, 30);
            Check(entered.Wait(1000), "battery worker entered");
            try
            {
                bool called = false;
                try { HidDiscovery.RunDpiOperation(instance, () => called = true); }
                catch (System.IO.IOException) { }
                Check(!called, "timed-out battery worker retains exclusive ownership");
                using var other = HidIoLease.TryAcquire(instance + "-other");
                Check(other != null, "independent physical device remains available");
            }
            finally { finish.Set(); }
        }
        Check(ended.Wait(2000), "native worker eventually completes");
        IDisposable? available = null;
        Check(SpinWait.SpinUntil(() => (available = HidIoLease.TryAcquire(instance)) != null, 2000),
            "lease released after actual worker completion");
        available?.Dispose();
        using (HidIoLease.TryAcquire(instance))
        using (InstanceScope.Enter(instance))
        {
            bool called = false;
            ReferenceQueryGate.Run("lease-path-reverse", () =>
            {
                called = true;
                return ReferenceBatteryProtocol.Reading.Failed("fixture");
            }, 2000);
            Check(!called, "battery worker cannot enter an active DPI transaction");
        }
        return checks;
    }
}
