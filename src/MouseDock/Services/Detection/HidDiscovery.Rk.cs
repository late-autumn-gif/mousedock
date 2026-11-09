using System;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    internal static DetectedDevice ReadRkReceiver(Bucket bucket, int matchingInstances,
        bool safeMode, DevicePreferences preferences, Func<ushort, ushort, bool, DetectedDevice> query)
    {
        if (!RkKeyboardProtocol.IsCandidate(bucket.Vid, bucket.Pid) ||
            !DeviceRoutes.IsKnown(bucket.Vid, bucket.Pid, bucket.Product) ||
            safeMode || matchingInstances != 1 || string.IsNullOrWhiteSpace(bucket.InstanceId) ||
            !(preferences?.CanRead(bucket.InstanceId, $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}",
                VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true))
            return null;

        using var scope = InstanceScope.Enter(bucket.InstanceId);
        var result = query(bucket.Vid, bucket.Pid, safeMode);
        if (result == null) return null;
        result.Id = bucket.InstanceId;
        result.PhysicalKey = bucket.InstanceId;
        result.RawName = bucket.Product;
        result.InterfaceDiscovered = true;
        result.ReceiverDiscovered = true;
        result.PeripheralConfirmed = result.Connected;
        result.ConfirmedModel = result.Connected ? result.Name : "";
        return result;
    }
}
