using System.Reflection;
using MouseDock.Models;

internal static class MchoseKeyboardChecks
{
    private static Type Protocol => typeof(AppSettings).Assembly.GetType(
        "MouseDock.Services.Detection.MchoseKeyboardProtocol")
        ?? throw new Exception("K87 battery protocol is missing.");

    private static object Call(string name, params object[] args) =>
        Protocol.GetMethod(name)!.Invoke(null, args)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Query()
    {
        var bytes = (byte[])Call("CreateBatteryQuery", (byte)0x73);
        Require(bytes.Length == 64, "Windows HID output must use the descriptor length.");
        Require(bytes.Take(9).SequenceEqual(new byte[] { 0x4D, 1, 3, 0, 4, 9, 0, 0x73, 0x7D }),
            "The read-only command or its checksum is wrong.");
        Require(bytes.Skip(9).All(b => b == 0), "Unused report bytes must be zero.");
    }

    private static (int Level, bool Charging) Parse(byte[] frame, byte sequence = 0x73) =>
        ((int, bool))Call("ParseBatteryResponse", frame, sequence);

    public static void Responses()
    {
        // Actual K87 V3 reply, with Windows report ID; remaining HID padding is omitted.
        byte[] actual = [0x4D, 1, 3, 2, 4, 9, 0, 0x73, 0, 0x55, 0x2A];
        Require(Parse(actual) == (85, false), "Captured 85% reading was not decoded.");
        Require(Parse([0x4D, 1, 3, 2, 4, 9, 0, 0x73, 1, 0x55, 0x2B]) == (85, true),
            "Charging flag was lost.");
        Require(Parse([0x4D, 1, 3, 2, 4, 9, 0, 0x73, 2, 100, 0x19]) == (100, false),
            "Fully charged must not mean actively charging.");
        Require(Parse([0x4D, 1, 3, 2, 4, 9, 0, 0x73, 0, 0, 0x7F]) == (0, false),
            "A valid zero must remain a real reading.");
        Require(Parse(actual, 0x74).Level == -1, "Old replies must be ignored.");
        for (int i = 0; i < actual.Length; i++)
        {
            var damaged = actual.ToArray();
            damaged[i] ^= 0x80;
            Require(Parse(damaged).Level == -1, $"Corrupt byte {i} was accepted.");
        }
        Require(Parse(actual.Take(10).ToArray()).Level == -1, "Truncated reply was accepted.");
        Require(Parse([0x4D, 1, 3, 2, 4, 9, 0, 0x73, 0, 101, 0x1A]).Level == -1,
            "Out-of-range battery must remain unknown.");
    }

    public static void Identity()
    {
        bool Device(ushort vid, ushort pid, string name) =>
            (bool)Call("IsSupportedDevice", vid, pid, name);
        Require(Device(0x3837, 0x1015, "MCHOSE K87 V3 2.4G"), "K87 not recognized.");
        Require(!Device(0x3837, 0x1015, "MCHOSE G98 V3 2.4G"), "Shared PID is insufficient.");
        Require(!Device(0x3837, 0x100B, "MCHOSE K87 V3 2.4G"), "Wrong PID accepted.");
        Require(!Device(0x5253, 0x1015, "MCHOSE K87 V3 2.4G"), "Wrong VID accepted.");
        Require((bool)Call("IsSupportedInterface", (ushort)0xFF80, (ushort)1, (ushort)64, (ushort)64),
            "Battery interface not recognized.");
        Require(!(bool)Call("IsSupportedInterface", (ushort)0xFF81, (ushort)1, (ushort)64, (ushort)64),
            "Wrong vendor interface accepted.");
        Require(!(bool)Call("IsSupportedInterface", (ushort)0xFF80, (ushort)1, (ushort)32, (ushort)32),
            "Unverified descriptor accepted.");
    }

    public static void SafeMode()
    {
        Require(((int, bool))Call("TryRead", (ushort)0x3837, (ushort)0x1015,
            "MCHOSE K87 V3 2.4G", true) == (-1, false), "Safe mode must skip the query.");
    }

    public static void Merge()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.Detection.HidDiscovery+Bucket")!;
        var bucket = Activator.CreateInstance(type)!;
        foreach (var (key, value) in new (string, object)[]
        {
            ("Vid", (ushort)0x3837), ("Pid", (ushort)0x1015), ("Product", "MCHOSE K87 V3 2.4G"),
            ("Kind", DeviceKind.Keyboard), ("Battery", 85)
        }) type.GetField(key)!.SetValue(bucket, value);
        var charging = type.GetField("Charging");
        Require(charging != null, "HID discovery cannot preserve keyboard charging yet.");
        charging!.SetValue(bucket, true);
        var device = type.GetMethod("ToDevice")!.Invoke(bucket, null)!;
        Require((int)device.GetType().GetProperty("Level")!.GetValue(device)! == 85,
            "Keyboard battery did not reach detection.");
        Require((bool)device.GetType().GetProperty("Charging")!.GetValue(device)!,
            "Keyboard charging did not reach detection.");
    }
}
