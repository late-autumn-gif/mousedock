using System.Collections;
using System.Reflection;
using MouseDock.Models;

internal static class HidAuditChecks
{
    private static readonly Type Discovery = typeof(AppSettings).Assembly.GetType(
        "MouseDock.Services.Detection.HidDiscovery")!;
    private static readonly Type Bucket = Discovery.GetNestedType("Bucket", BindingFlags.NonPublic)!;
    private static readonly List<(string Path, ushort Vid, ushort Pid, ushort UsagePage, string Product)> Logi =
        [("usb#vid_046d&pid_c53f&mi_01", 0x046D, 0xC53F, 0xFF00, "USB Receiver")];

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static object MakeBucket(ushort vid, ushort pid, string name = "Wireless Keyboard", int level = -1)
    {
        var bucket = Activator.CreateInstance(Bucket)!;
        foreach (var (field, value) in new (string, object)[]
        {
            ("Vid", vid), ("Pid", pid), ("Product", name), ("Kind", DeviceKind.Keyboard),
            ("Path", $"usb#vid_{vid:x4}&pid_{pid:x4}&mi_00"), ("Battery", level)
        }) Bucket.GetField(field)!.SetValue(bucket, value);
        return bucket;
    }

    private static object Device(object bucket) => Bucket.GetMethod("ToDevice")!.Invoke(bucket, null)!;
    private static T Value<T>(object device, string name) =>
        (T)device.GetType().GetProperty(name)!.GetValue(device)!;

    private static void Query(object[] buckets,
        List<(string Path, ushort Vid, ushort Pid, ushort UsagePage, string Product)> collections,
        Func<string, (int Battery, string Name, bool Charging)> probe,
        Func<string, ushort, ushort, int> read, bool safeMode = false)
    {
        var method = Discovery.GetMethod("QueryVendorCollections", BindingFlags.Static | BindingFlags.NonPublic);
        Require(method != null, "HID vendor collections have no per-scan query stage.");
        var map = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), Bucket))!;
        foreach (var bucket in buckets)
        {
            var vid = (ushort)Bucket.GetField("Vid")!.GetValue(bucket)!;
            var pid = (ushort)Bucket.GetField("Pid")!.GetValue(bucket)!;
            map.Add($"{vid:X4}:{pid:X4}", bucket);
        }
        method!.Invoke(null, [map, collections, safeMode, probe, read]);
    }

    private static void WithCleanHints(Action action)
    {
        var hints = (IDictionary)Discovery.GetField("_logiHints", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var saved = new List<DictionaryEntry>();
        foreach (DictionaryEntry entry in hints) saved.Add(entry);
        hints.Clear();
        try { action(); }
        finally
        {
            hints.Clear();
            foreach (var entry in saved) hints.Add(entry.Key, entry.Value);
        }
    }

    public static void LogitechRefresh() => WithCleanHints(() =>
    {
        var first = MakeBucket(0x046D, 0xC53F);
        Query([first], Logi, _ => (75, "G304", true), (_, _, _) => -1);
        Require(Value<int>(Device(first), "Level") == 75 && Value<bool>(Device(first), "Charging"),
            "First HID++ sample was not published.");
        var second = MakeBucket(0x046D, 0xC53F);
        Query([second], Logi, _ => (12, "G304", false), (_, _, _) => -1);
        var current = Device(second);
        Require(Value<int>(current, "Level") == 12, "A successful first probe froze later battery samples.");
        Require(!Value<bool>(current, "Charging"), "Charging must clear when the current sample says false.");
    });

    public static void LogitechFailure() => WithCleanHints(() =>
    {
        Query([MakeBucket(0x046D, 0xC53F)], Logi, _ => (75, "G304", true), (_, _, _) => -1);
        var failed = MakeBucket(0x046D, 0xC53F);
        Query([failed], Logi, _ => (-1, null!, false), (_, _, _) => -1);
        var current = Device(failed);
        Require(Value<string>(current, "Name") == "G304", "A transient read failure lost learned identity.");
        Require(Value<int>(current, "Level") == -1, "Old battery was mislabeled as a fresh sample.");
        Require(!Value<bool>(current, "Charging"), "Old charging state survived a failed current read.");
        var missing = MakeBucket(0x046D, 0xC53F);
        Query([missing], [], _ => throw new Exception("No interface should be queried."), (_, _, _) => -1);
        Require(Value<int>(Device(missing), "Level") == -1, "A missing vendor interface revived stale battery.");
        var invalid = MakeBucket(0x046D, 0xC53F);
        Query([invalid], Logi, _ => (101, null!, true), (_, _, _) => -1);
        Require(Value<int>(Device(invalid), "Level") == -1 && !Value<bool>(Device(invalid), "Charging"),
            "An invalid reading published battery or charging.");
    });

    public static void LogitechDedupAndPreference() => WithCleanHints(() =>
    {
        int probes = 0;
        var bucket = MakeBucket(0x046D, 0xC53F, level: 41);
        Query([bucket], [Logi[0], Logi[0]], _ =>
        {
            probes++;
            return (12, "G304", false);
        }, (_, _, _) => -1);
        Require(probes == 1, "The same HID++ interface was probed repeatedly in one scan.");
        Require(Value<int>(Device(bucket), "Level") == 41, "HID++ overrode an existing generic reading.");
        var fresh = MakeBucket(0x046D, 0xC53F);
        Query([fresh], Logi, _ => { probes++; return (0, "G304", false); }, (_, _, _) => -1);
        Require(probes == 2 && Value<int>(Device(fresh), "Level") == 0,
            "Each scan must refresh and preserve a measured zero.");
        var fallback = MakeBucket(0x046D, 0xC53F);
        Query([fallback], [Logi[0],
                ("usb#vid_046d&pid_c53f&mi_02", 0x046D, 0xC53F, 0xFF00, "USB Receiver")],
            path => path.EndsWith("mi_01") ? (-1, null!, false) : (28, "G304", false), (_, _, _) => -1);
        Require(Value<int>(Device(fallback), "Level") == 28,
            "An unusable first interface blocked another usable HID++ interface.");
    });

    public static void VendorRoutes()
    {
        foreach (var (vid, pid, name, route) in new (ushort, ushort, string, string)[]
        {
            (0x391D, 0x0001, "VGN Keyboard", "vgn"),
            (0x1A2C, 0x6281, "AJAZZ Keyboard", "ajazz")
        })
        {
            var bucket = MakeBucket(vid, pid, name);
            var item = ($"usb#vid_{vid:x4}&pid_{pid:x4}&mi_01", vid, pid, (ushort)0xFF00, name);
            int reads = 0;
            Query([bucket], [item, item], _ => throw new Exception("Wrong protocol."),
                (selected, actualVid, actualPid) =>
                {
                    Require(selected == route && actualVid == vid && actualPid == pid, "Wrong vendor query routed.");
                    reads++;
                    return 63;
                });
            Require(reads == 1 && Value<int>(Device(bucket), "Level") == 63,
                "Recognized standard HID device did not receive its vendor collection battery.");
        }
    }

    public static void VendorGuards()
    {
        foreach (var (vid, pid, product, path, safe, hasBucket, level) in
            new (ushort, ushort, string, string, bool, bool, int)[]
        {
            (0x391D, 1, "VGN", "usb#receiver", true, true, -1),
            (0x391D, 1, "VGN", "usb#receiver", false, false, -1),
            (0x391D, 1, "Virtual Keyboard", "usb#receiver", false, true, -1),
            (0x391D, 1, "VGN", "hidi2c#receiver", false, true, -1),
            (0x9999, 1, "Unknown", "usb#receiver", false, true, -1),
            (0x1A2C, 1, "Unknown", "usb#receiver", false, true, -1),
            (0x0C45, 1, "Unknown", "usb#receiver", false, true, -1),
            (0x0C45, 0x8006, "Unknown", "usb#receiver", false, true, -1),
            (0x0C45, 0x8006, "X87", "usb#receiver", false, true, -1),
            (0x3554, 1, "Eweadn Mouse", "usb#receiver", false, true, -1),
            (0x391D, 1, "VGN", "usb#receiver", false, true, 35)
        })
        {
            var bucket = MakeBucket(vid, pid, product, level);
            int calls = 0;
            Query(hasBucket ? [bucket] : [], [(path, vid, pid, 0xFF00, product)],
                _ => { calls++; return (92, "Unexpected", false); },
                (_, _, _) => { calls++; return 92; }, safe);
            Require(calls == 0, "Unsafe or redundant vendor query reached the hardware boundary.");
            Require(Value<int>(Device(bucket), "Level") == level, "Rejected query altered the battery.");
        }
        var wired = MakeBucket(0x9999, 1, "USB Keyboard");
        ((HashSet<string>)Bucket.GetField("Interfaces")!.GetValue(wired)!).Add("mi_00");
        Query([wired], [("usb#vid_9999&pid_0001&mi_01", 0x9999, 1, 0xFF00, "USB Keyboard")],
            _ => (-1, null!, false), (_, _, _) => -1);
        Require(Value<string>(Device(wired), "Transport") == "\u6709\u7ebf",
            "Vendor query metadata changed a wired device into a wireless receiver.");
    }

    private static int Parse(string name, params object[] args)
    {
        var protocol = typeof(AppSettings).Assembly.GetType("MouseDock.Services.Detection.VendorProtocol")!;
        var method = protocol.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Require(method != null, $"Missing strict {name} response parser.");
        return (int)method!.Invoke(null, args)!;
    }

    public static void VgnResponses()
    {
        // Format fixtures from the documented SDK layout, not captured hardware responses.
        var frame = new byte[64];
        frame[0] = 0x0D;
        frame[1] = 0x01;
        frame[2] = 1;
        frame[4] = 37;
        Require(Parse("ParseVgnBattery", frame) == 37, "VGN basic-info battery was not decoded.");
        frame[4] = 0;
        Require(Parse("ParseVgnBattery", frame) == 0, "VGN measured zero was discarded.");
        frame[4] = 100;
        Require(Parse("ParseVgnBattery", frame) == 100, "VGN measured full battery was discarded.");
        frame[4] = 101;
        Require(Parse("ParseVgnBattery", frame) == -1, "VGN out-of-range battery was accepted.");
        frame[4] = 37;
        foreach (byte command in new byte[] { 0, 2, 3, 0xFF })
        {
            frame[1] = command;
            Require(Parse("ParseVgnBattery", frame) == -1, "Unrelated VGN subcommand supplied a battery.");
        }
        frame[1] = 1;
        frame[0] = 0x0E;
        Require(Parse("ParseVgnBattery", frame) == -1, "Unrelated VGN module supplied a battery.");
        frame[0] = 0x0D;
        Require(Parse("ParseVgnBattery", frame.Take(5).ToArray()) == -1,
            "Truncated VGN reply supplied a battery.");
        Require(Parse("ParseVgnBattery", frame.Concat(new byte[] { 0, 0 }).ToArray()) == -1,
            "Unexpected VGN frame length was accepted.");
        var prefixed = new byte[65];
        frame.CopyTo(prefixed, 1);
        Require(Parse("ParseVgnBattery", prefixed) == 37, "Zero report ID was not handled.");
        prefixed[0] = 0x7F;
        Require(Parse("ParseVgnBattery", prefixed) == -1, "Unknown VGN report prefix was stripped.");
        Require(Parse("ParseVgnBattery", new object[] { null! }) == -1, "Missing VGN reply was accepted.");
    }

    public static void AjazzResponses()
    {
        // Format fixtures from documented fields; identity is big-endian VID/PID.
        byte[] frame = [0xAA, 0, 0, 0, 14, 95, 1, 0, 0x1A, 0x2C, 0x62, 0x81, 1, 0];
        int Read(byte[] bytes, ushort vid = 0x1A2C, ushort pid = 0x6281) =>
            Parse("ParseAjazzBattery", bytes, vid, pid);
        Require(Read(frame) == 95, "AJAZZ rounded a reported percentage to full battery.");
        frame[5] = 0;
        Require(Read(frame) == 0, "AJAZZ measured zero was discarded.");
        frame[5] = 100;
        Require(Read(frame) == 100, "AJAZZ measured full battery was discarded.");
        frame[5] = 101;
        Require(Read(frame) == -1, "AJAZZ out-of-range battery was clamped to a fabricated percentage.");
        frame[5] = 255;
        Require(Read(frame) == -1, "AJAZZ invalid byte was clamped to full battery.");
        frame[5] = 95;
        Require(Read(frame, pid: 0x6381) == -1, "Another AJAZZ device's reply supplied a battery.");
        Require(Read(frame, vid: 0x9999) == -1, "AJAZZ reply with another vendor identity was accepted.");
        frame[0] = 0xAB;
        Require(Read(frame) == -1, "Wrong AJAZZ command was accepted.");
        frame[0] = 0xAA;
        frame[4] = 13;
        Require(Read(frame) == -1, "Wrong AJAZZ status tag was accepted.");
        frame[4] = 14;
        Require(Read(frame.Take(13).ToArray()) == -1, "Truncated AJAZZ response was accepted.");
        var prefixed = new byte[15];
        frame.CopyTo(prefixed, 1);
        Require(Read(prefixed) == 95, "AJAZZ zero report ID was not handled.");
        prefixed[0] = 0x7F;
        Require(Read(prefixed) == -1, "Unknown AJAZZ report prefix was stripped.");
        Require(Read(null!) == -1, "Missing AJAZZ response was accepted.");
    }

    public static void VendorQueryInterfaces()
    {
        var protocol = typeof(AppSettings).Assembly.GetType("MouseDock.Services.Detection.VendorProtocol")!;
        bool Allows(string name, ushort usagePage, ushort outputLength)
        {
            var method = protocol.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            Require(method != null, $"Missing {name} query interface guard.");
            return (bool)method!.Invoke(null, [usagePage, outputLength])!;
        }
        Require(Allows("IsVgnQueryInterface", 0xFF00, 64), "Known VGN output format was rejected.");
        Require(!Allows("IsVgnQueryInterface", 1, 64), "VGN query reached a standard input collection.");
        foreach (ushort length in new ushort[] { 0, 7, 63, 65 })
            Require(!Allows("IsVgnQueryInterface", 0xFF00, length), "Unverified VGN query length was accepted.");
        Require(Allows("IsAjazzQueryInterface", 0xFF00, 14), "AJAZZ minimum status output was rejected.");
        Require(Allows("IsAjazzQueryInterface", 0xFF00, 64), "AJAZZ padded output was rejected.");
        Require(!Allows("IsAjazzQueryInterface", 1, 64), "AJAZZ query reached a standard input collection.");
        foreach (ushort length in new ushort[] { 0, 7, 13 })
            Require(!Allows("IsAjazzQueryInterface", 0xFF00, length), "AJAZZ output is too short for status query.");
    }
}
