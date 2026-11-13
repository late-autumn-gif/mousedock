using System;
using System.Collections.Generic;

namespace MouseDock.Services.Detection;

internal static class HidIoLease
{
    private static readonly object gate = new();
    private static readonly HashSet<string> owners = new(StringComparer.OrdinalIgnoreCase);

    internal static IDisposable TryAcquire(string instance)
    {
        if (string.IsNullOrWhiteSpace(instance)) return null;
        lock (gate)
            return owners.Add(instance) ? new Lease(instance) : null;
    }

    private sealed class Lease(string instance) : IDisposable
    {
        private bool released;
        public void Dispose()
        {
            lock (gate)
            {
                if (released) return;
                released = true;
                owners.Remove(instance);
            }
        }
    }
}
