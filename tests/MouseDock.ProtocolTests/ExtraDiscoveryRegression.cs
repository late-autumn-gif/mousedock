using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class ExtraDiscoveryRegression
{
    internal static int Run()
    {
        int checks = 0, calls = 0;
        void Check(bool ok, string name) { checks++; if (!ok) throw new Exception("Extra discovery: " + name); }
        InstanceScope.BeginScan();
        string first = InstanceScope.Register("one", new[] { ("USB\\VID_373B&PID_111F\\ONE", "") });
        string second = InstanceScope.Register("two", new[] { ("USB\\VID_373B&PID_111F\\TWO", "") });
        HidDiscovery.Bucket Bucket(string id) => new()
        { InstanceId = id, Vid = 0x373B, Pid = 0x111F, Product = "ATK A87 Pro", Kind = DeviceKind.Keyboard };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [first] = Bucket(first), [second] = Bucket(second) };
        var channels = new List<HidDiscovery.VendorCollectionInfo>
        {
            new() { Path = "one", Vid = 0x373B, Pid = 0x111F, Product = "ATK A87 Pro",
                UsagePage = 0xFF60, Usage = 0x61, Input = 33, Output = 33 },
            new() { Path = "two", Vid = 0x373B, Pid = 0x111F, Product = "ATK A87 Pro",
                UsagePage = 0xFF60, Usage = 0x61, Input = 33, Output = 33 }
        };
        ReferenceBatteryProtocol.Reading Query(ExtraDeviceRoutes.Route route, ushort vid, ushort pid,
            string name, IReadOnlyList<HidDiscovery.VendorCollectionInfo> selected, BatteryReplay.Trace trace)
        {
            calls++;
            Check(selected.Count == 1 && InstanceScope.Allows(selected[0].Path),
                "query received an unrelated physical instance");
            return new(53, true, false, ReferenceBatteryProtocol.State.Online, "fresh", "ATK A87 Pro");
        }
        HidDiscovery.QueryExtraDevices(buckets, channels, true, new DevicePreferences(new()), Query);
        Check(calls == 0 && buckets[first].Battery < 0, "safe mode started a query");
        var settings = new AppSettings { DisabledRecognitionDeviceIds = new() { first } };
        HidDiscovery.QueryExtraDevices(buckets, channels, false, new DevicePreferences(settings), Query);
        Check(calls == 1 && buckets[first].Battery < 0 && buckets[second].Battery == 53,
            "disabled instance borrowed a neighbor's result");
        Check(!buckets[second].Charging, "unknown charging was promoted to charging");
        var output = buckets[second].ToDevice();
        Check(output.Kind == DeviceKind.Keyboard && output.ConfirmedModel == "ATK A87 Pro",
            "the new reader did not reach the published device");
        HidDiscovery.QueryExtraDevices(buckets, channels, false, new DevicePreferences(new()),
            (_, _, _, _, _, _) => ReferenceBatteryProtocol.Reading.Failed("offline"));
        Check(buckets[second].Battery < 0 && !buckets[second].Charging, "failed read retained a fresh percentage");
        Check(ExtraDeviceRoutes.Primary(0x3837, 0x1015, "MCHOSE K87 V3 2.4G") == null,
            "existing K87 route was displaced");
        Check(ExtraDeviceRoutes.Primary(0x3554, 0xFA09, "2.4G Wireless Receiver") == null,
            "existing F75 route was displaced");
        Check(ExtraDeviceRoutes.Primary(0x046D, 0xC548, "USB Receiver") == null,
            "shared Bolt receiver was classified as a keyboard before runtime identity");
        InstanceScope.BeginScan();
        string logi = InstanceScope.Register("logi", new[] { ("USB\\VID_046D&PID_C548\\ONE", "") });
        var parent = new HidDiscovery.Bucket
        {
            InstanceId = logi, Vid = 0x046D, Pid = 0xC548, Product = "USB Receiver",
            Kind = DeviceKind.Mouse, Battery = 72, Path = "logi"
        };
        buckets = new() { [logi] = parent };
        channels = new() { new() { Path = "logi", Vid = 0x046D, Pid = 0xC548 } };
        var preferences = new DevicePreferences(new());
        HidDiscovery.QueryExtraDevices(buckets, channels, false, preferences,
            (_, _, _, _, _, _) => new(65, false, true, ReferenceBatteryProtocol.State.Online,
                "identity-confirmed", "Logitech keyboard", "1:confirmed"));
        var childBucket = buckets.Values.Single(b => b != parent);
        Check(parent.Kind == DeviceKind.Mouse && parent.Battery == 72 && childBucket.Battery == 65,
            "keyboard identity replaced the shared receiver's mouse");
        preferences.Disable(new DeviceInfo { Id = childBucket.InstanceId });
        buckets = new() { [logi] = parent };
        int blockedCalls = 0;
        HidDiscovery.QueryExtraDevices(buckets, channels, false, preferences, (_, _, _, _, _, _) =>
        {
            blockedCalls++;
            return ReferenceBatteryProtocol.Reading.Failed("should not read");
        });
        Check(blockedCalls == 0 && buckets.ContainsKey(childBucket.InstanceId) &&
            buckets[childBucket.InstanceId].Battery < 0, "disabled child still queried the shared receiver");
        preferences.Enable(new DeviceInfo { Id = childBucket.InstanceId });
        buckets = new() { [logi] = parent };
        HidDiscovery.QueryExtraDevices(buckets, channels, false, preferences,
            (_, _, _, _, _, _) => new(-1, true, true, ReferenceBatteryProtocol.State.Charging,
                "device-band=2/5", "Logitech keyboard", "1:confirmed"));
        Check(buckets.Values.Single(b => b != parent).ToDevice() is { Level: -1, Charging: true },
            "coarse battery state was converted into a precise percentage or lost charging");
        var typeReply = new byte[20]; typeReply[0] = 0x11; typeReply[4] = 0;
        Check(!LogitechHidpp.IsMouseType(typeReply), "keyboard type was accepted as a mouse");
        typeReply[4] = 3;
        Check(LogitechHidpp.IsMouseType(typeReply), "confirmed HID++ mouse type was rejected");
        Check(!LogitechHidpp.IsMouseType(typeReply.AsSpan(0, 5)), "truncated type was accepted");
        return checks;
    }
}
