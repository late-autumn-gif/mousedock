using System.Reflection;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class R98KeyboardChecks
{
    private static Type Protocol => typeof(AppSettings).Assembly.GetType(
        "MouseDock.Services.Detection.RkKeyboardProtocol")
        ?? throw new Exception("RK R98 keyboard recognition is missing.");

    private static object Call(string name, params object[] args) =>
        Protocol.GetMethod(name)!.Invoke(null, args)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void DiscoveryReachable()
    {
        Require(DeviceRoutes.IsKnown(0x258A, 0x01BB, "2.4G Wireless Receiver"),
            "Normal discovery blocks the verified R98 receiver before its identity query.");
        Require(DeviceRoutes.IsKnown(0x258A, 0x0233, "2.4G Wireless Receiver"),
            "The second verified R98 receiver cannot reach the identity reader.");
        Require(!DeviceRoutes.IsKnown(0x258A, 0x9999, "2.4G Wireless Receiver"),
            "Unverified shared receivers must not reach the R98 reader.");
    }

    private static HidDiscovery.Bucket Receiver() => new()
    {
        InstanceId = "instance-r98",
        Vid = 0x258A,
        Pid = 0x01BB,
        Product = "2.4G Wireless Receiver"
    };

    private static DetectedDevice? Discover(HidDiscovery.Bucket bucket, int count, bool safe,
        DevicePreferences? preferences, Func<ushort, ushort, bool, DetectedDevice> query)
    {
        var read = typeof(HidDiscovery).GetMethod("ReadRkReceiver", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("Normal discovery has no preference-aware R98 reader.");
        return (DetectedDevice?)read.Invoke(null, [bucket, count, safe, preferences, query]);
    }

    public static void DiscoveryIdentity()
    {
        using var previous = InstanceScope.Enter("outer-instance");
        var result = Discover(Receiver(), 1, false, null, (vid, pid, safe) =>
        {
            Require(InstanceScope.Current == "instance-r98" && vid == 0x258A && pid == 0x01BB && !safe,
                "R98 queries must be scoped to the discovered physical receiver.");
            return RkKeyboardProtocol.ReadReceiverAsync(vid, pid, request => Task.FromResult(
                Convert.FromHexString(request[1] switch
                {
                    7 => "130701000101000000000000000000000000001D",
                    5 => "13050100060A000000004600000000000000006F",
                    0x4A => "134A0100026401000000000000000000000000C5",
                    _ => throw new Exception("Unexpected receiver query.")
                }))).GetAwaiter().GetResult();
        });
        Require(result is { Id: "instance-r98", PhysicalKey: "instance-r98",
            RawName: "2.4G Wireless Receiver", ConfirmedModel: "RK R98Pro V2",
            InterfaceDiscovered: true, ReceiverDiscovered: true, PeripheralConfirmed: true,
            Kind: DeviceKind.Keyboard, Connected: true, Level: 100, Charging: false },
            "Normal discovery must retain the verified R98 identity, battery and physical instance.");
        Require(InstanceScope.Current == "outer-instance", "R98 reading leaked the instance scope.");
    }

    public static void DiscoveryGuards()
    {
        int queries = 0;
        DetectedDevice Query(ushort vid, ushort pid, bool safe)
        {
            queries++;
            throw new Exception("Blocked R98 discovery must never open hardware.");
        }
        Require(Discover(Receiver(), 1, true, null, Query) == null, "Safe mode queried R98.");
        foreach (string disabled in new[] { "instance-r98", "hid-258a-01bb" })
        {
            var preferences = new DevicePreferences(new AppSettings
                { DisabledRecognitionDeviceIds = [disabled] });
            Require(Discover(Receiver(), 1, false, preferences, Query) == null,
                "A disabled receiver queried R98.");
        }
        Require(Discover(Receiver(), 2, false, null, Query) == null,
            "Ambiguous shared receivers must not supply another instance's battery.");
        var missingInstance = Receiver();
        missingInstance.InstanceId = "";
        Require(Discover(missingInstance, 1, false, null, Query) == null,
            "R98 discovery without a physical instance must not issue unscoped queries.");
        var unrelated = Receiver();
        unrelated.Pid = 0x9999;
        Require(Discover(unrelated, 1, false, null, Query) == null,
            "Unknown receivers must not receive R98 queries.");
        Require(queries == 0, "A blocked R98 query reached hardware.");
    }

    internal static int RunLive()
    {
        var result = RkKeyboardProtocol.TryRead(0x258A, 0x01BB, false);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        return result is { Connected: true, Level: >= 0 } ? 0 : 1;
    }

    public static void Queries()
    {
        Require(((byte[])Call("CreateIdentityQuery")).SequenceEqual(
            Convert.FromHexString("1305010000000000000000000000000000000019")),
            "Identity query must match the official read-only command.");
        Require(((byte[])Call("CreateConnectionQuery")).SequenceEqual(
            Convert.FromHexString("130701000000000000000000000000000000001B")),
            "Connection query must match the official read-only command.");
        Require(((byte[])Call("CreateBatteryQuery")).SequenceEqual(
            Convert.FromHexString("134A01000000000000000000000000000000005E")),
            "Battery query must match the official GetPower command.");
    }

    public static void Identity()
    {
        Require((bool)Call("IsSupportedInterface", (ushort)0xFF02, (ushort)2, (ushort)20, (ushort)20),
            "Verified receiver interface must be selected.");
        Require(!(bool)Call("IsSupportedInterface", (ushort)1, (ushort)6, (ushort)20, (ushort)20),
            "Standard keyboard reports must not receive vendor queries.");
        Require(!(bool)Call("IsSupportedInterface", (ushort)0xFF02, (ushort)1, (ushort)20, (ushort)20),
            "A different vendor collection must not receive queries.");
        Require(!(bool)Call("IsSupportedInterface", (ushort)0xFF02, (ushort)2, (ushort)64, (ushort)64),
            "Unverified report lengths must not receive queries.");
        byte[] actual = Convert.FromHexString("13050100060A000000004600000000000000006F");
        Require((bool)Call("IsR98Identity", actual), "Captured R98Pro V2 identity was rejected.");
        Require(!(bool)Call("IsR98Identity", Convert.FromHexString("13050100060A0000000020000000000000000049")),
            "The shared R98Pro 3 receiver identity must not become V2.");
        Require(!(bool)Call("IsR98Identity", actual.Take(19).ToArray()), "Truncated identity was accepted.");
        for (int i = 0; i < actual.Length; i++)
        {
            byte[] corrupt = actual.ToArray();
            corrupt[i] ^= 0x80;
            Require(!(bool)Call("IsR98Identity", corrupt), $"Corrupt identity byte {i} was accepted.");
        }
    }

    public static void Connection()
    {
        Require((bool?)Call("ParseConnection", Convert.FromHexString("130701000101000000000000000000000000001D")) == true,
            "Captured online state was lost.");
        Require((bool?)Call("ParseConnection", Convert.FromHexString("130701000100000000000000000000000000001C")) == false,
            "A disconnected keyboard must not appear connected because its receiver is present.");
        Require(Call("ParseConnection", Array.Empty<byte>()) == null, "Missing reply must stay unknown.");
    }

    public static void SafeMode()
    {
        Require(Call("TryRead", (ushort)0x258A, (ushort)0x01BB, true) == null,
            "Safe mode must not query hardware.");
        Require(Call("TryRead", (ushort)0x258A, (ushort)0x9999, false) == null,
            "Unverified receivers must not receive queries.");
    }

    public static void Battery()
    {
        byte[] actual = Convert.FromHexString("134A0100026401000000000000000000000000C5");
        Require((int)Call("ParseBattery", actual) == 100, "Captured full battery was not decoded.");
        Require((int)Call("ParseBattery", Convert.FromHexString("134A010002000100000000000000000000000061")) == 0,
            "Measured zero must not become unknown.");
        Require((int)Call("ParseBattery", Convert.FromHexString("134A0100026501000000000000000000000000C6")) == -1,
            "Out-of-range battery must remain unknown.");
        Require((int)Call("ParseBattery", actual.Take(19).ToArray()) == -1,
            "Truncated battery must remain unknown.");
        for (int i = 0; i < actual.Length; i++)
        {
            byte[] corrupt = actual.ToArray();
            corrupt[i] ^= 0x80;
            Require((int)Call("ParseBattery", corrupt) == -1, $"Corrupt battery byte {i} was accepted.");
        }
    }
}
