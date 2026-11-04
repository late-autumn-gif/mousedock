#nullable disable
using MouseDock.Services.Detection;

internal static class BluetoothRegression
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new Exception("FAIL: " + name);
        }

        foreach (var address in new[] { "849D4B3669C7", "849d4b3669c7", "84:9d:4b:36:69:c7", "84-9D-4B-36-69-C7" })
            Check(BtSystemBattery.TryParseAddress(address, out var parsed) && parsed == 0x849D4B3669C7UL, "report address " + address);
        foreach (var address in new[] { null, "", "ROSE Ceramics U", "000000000000", "84:9D-4B:36:69:C7", "849D4B3669C7AA", "Bluetooth#84:9D:4B:36:69:C7", "84:9D:4B:36:69:GG" })
            Check(!BtSystemBattery.TryParseAddress(address, out _), "reject invalid address " + address);

        BtSystemBattery.Entry Apply(object level, object charging = null)
        {
            var entry = new BtSystemBattery.Entry();
            BtSystemBattery.ApplyProperties(entry, new Dictionary<string, object>
            {
                ["System.Devices.BatteryLife"] = level,
                ["System.Devices.ChargingState"] = charging
            }, "test");
            return entry;
        }
        foreach (var value in new object[] { (byte)0, (byte)100, (short)63, (ushort)63, 63, 63U, 63L, 63UL })
            Check(Apply(value).Level == Convert.ToInt32(value), "typed percentage " + value.GetType().Name + value);
        foreach (var value in new object[] { (byte)101, (byte)255, -1, 101, uint.MaxValue, long.MaxValue, ulong.MaxValue, "80", 80.0, null })
            Check(Apply(value).Level == -1, "reject unknown/invalid percentage " + value);
        Check(Apply((byte)0, (byte)1) is { Level: 0, Charging: true, ChargingKnown: true }, "zero battery and charging");
        Check(Apply((byte)100, (byte)0) is { Level: 100, Charging: false, ChargingKnown: true }, "full battery not charging");
        Check(!Apply(63, (byte)2).ChargingKnown, "unknown charging state remains unknown");

        var packed = new BtSystemBattery.Entry();
        BtSystemBattery.ApplyProperties(packed, new Dictionary<string, object>
        {
            ["System.Devices.BatteryPlusCharging"] = (byte)165,
            ["System.Devices.BatteryPlusChargingText"] = "65% (charging)"
        }, "test");
        Check(packed.Level == -1 && !packed.ChargingKnown && packed.Raw == "165", "packed/text values are diagnostic only");

        var fallback = Apply((byte)101, (byte)2);
        BtSystemBattery.ApplyProperties(fallback, new Dictionary<string, object>
        {
            ["System.Devices.BatteryLife"] = (byte)63,
            ["System.Devices.ChargingState"] = (byte)1
        }, "container");
        Check(fallback is { Level: 63, Charging: true, ChargingKnown: true }, "container fills missing endpoint properties");
        BtSystemBattery.ApplyProperties(fallback, new Dictionary<string, object> { ["System.Devices.BatteryLife"] = (byte)80 }, "other");
        Check(fallback.Level == 63, "valid endpoint value is not replaced");
        return checks;
    }
}
