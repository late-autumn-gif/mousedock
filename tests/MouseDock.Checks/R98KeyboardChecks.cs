using System.Reflection;
using MouseDock.Models;

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
