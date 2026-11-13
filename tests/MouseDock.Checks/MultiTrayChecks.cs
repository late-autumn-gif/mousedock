using System.Collections;
using System.Reflection;
using System.Text.Json;
using MouseDock.Models;
using Forms = System.Windows.Forms;

internal static class MultiTrayChecks
{
    internal static void Selection()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TrayDeviceSelection");
        Require(type != null, "Independent per-device tray selection is missing.");
        var resolve = type!.GetMethod("Resolve", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        DeviceInfo[] Read(AppSettings settings, DeviceInfo[] input) =>
            ((IEnumerable)resolve.Invoke(null, new object[] { settings, input })!).Cast<DeviceInfo>().ToArray();
        var a = new DeviceInfo { Id = "mouse-a", Name = "Mouse", Level = 62 };
        var b = new DeviceInfo { Id = "keyboard-b", Name = "Keyboard", Kind = DeviceKind.Keyboard, Level = 31 };
        var c = new DeviceInfo { Id = "headset-c", Name = "Headset", Kind = DeviceKind.Headset, Selected = false, Level = 90 };
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"MultiTray":true}""")!;
        Require(Read(settings, [a, b, c]).Select(d => d.Id).SequenceEqual(["mouse-a", "keyboard-b"]),
            "Automatic tray targets must follow selected devices.");
        settings = JsonSerializer.Deserialize<AppSettings>("""{"MultiTray":true,"TrayDeviceIds":["headset-c","mouse-a"]}""")!;
        Require(Read(settings, [b, a, c]).Select(d => d.Id).SequenceEqual(["headset-c", "mouse-a"]),
            "Independent tray selection lost its order or depends on main-panel selection.");
        a.Connected = false;
        Require(Read(settings, [a, b, c]).Select(d => d.Id).SequenceEqual(["headset-c"]),
            "Disconnected targets left a stale tray icon.");
        a.Connected = true;
        Require(Read(settings, [b, c, a]).Select(d => d.Id).SequenceEqual(["headset-c", "mouse-a"]),
            "Reconnected target did not regain its stable tray slot.");
        c.RecognitionEnabled = false;
        Require(Read(settings, [a, b, c]).Single() == a, "Disabled recognition leaked into tray selection.");
        a.Hidden = true;
        Require(Read(settings, [a, b, c]).Length == 0, "Globally hidden devices remained in the tray.");
        settings = JsonSerializer.Deserialize<AppSettings>("""{"MultiTray":true,"TrayDeviceIds":[]}""")!;
        Require(Read(settings, [b]).Length == 0, "Explicit empty tray selection was replaced with defaults.");
        settings = JsonSerializer.Deserialize<AppSettings>("""{"MultiTray":true,"TrayDeviceIds":["keyboard-b"]}""")!;
        Require(Read(settings, [b, new DeviceInfo { Id = "keyboard-b" }]).Length == 0,
            "Ambiguous stable identities must not select an arbitrary physical device.");
        Require(Read(new AppSettings(), [b]).Length == 0, "Legacy single-tray mode started per-device icons.");
        var animate = type.GetMethod("NeedsAnimation", BindingFlags.Static | BindingFlags.NonPublic)!;
        c.RecognitionEnabled = true; c.Hidden = false; c.Level = 8;
        settings.MultiTray = true; settings.TrayDeviceIds = new() { "headset-c" };
        Require((bool)animate.Invoke(null, [Read(settings, [b, c]), 20])!,
            "A low independent tray target cannot start animation outside the main panel.");
        c.Level = 80; c.Charging = true;
        Require((bool)animate.Invoke(null, [Read(settings, [b, c]), 20])!,
            "A charging independent tray target cannot start animation outside the main panel.");
    }

    internal static void Preferences()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """{"MultiTray":true,"TrayDeviceIds":["a","b"],"SelectedDeviceIds":["c"]}""")!;
        var copy = settings.Clone();
        var options = new JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(copy, options));
        Require(json.RootElement.TryGetProperty("MultiTray", out var enabled) && enabled.GetBoolean(),
            "Cloning lost multi-tray mode.");
        Require(json.RootElement.GetProperty("TrayDeviceIds").EnumerateArray().Select(x => x.GetString())
            .SequenceEqual(["a", "b"]), "Cloning lost independent tray targets.");
        var targets = (List<string>)typeof(AppSettings).GetProperty("TrayDeviceIds")!.GetValue(copy)!;
        targets.Clear();
        using var original = JsonDocument.Parse(JsonSerializer.Serialize(settings, options));
        Require(original.RootElement.GetProperty("TrayDeviceIds").GetArrayLength() == 2 &&
            settings.SelectedDeviceIds.SequenceEqual(["c"]), "Editing tray draft mutated saved targets or main selection.");
        Require(typeof(AppSettings).GetProperty("TrayDeviceIds")!.GetValue(new AppSettings().Clone()) == null,
            "Legacy automatic selection was changed to an explicit empty list.");
    }

    internal static void Lifecycle()
    {
        var type = typeof(AppSettings).Assembly.GetType("MouseDock.Services.TrayIconGroup")!;
        int draws = 0, menus = 0;
        Func<DeviceInfo, System.Drawing.Icon> render = _ =>
        {
            draws++;
            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        };
        Func<DeviceInfo, Forms.ContextMenuStrip> menu = device =>
        {
            menus++;
            var result = new Forms.ContextMenuStrip();
            result.Items.Add(device.Name);
            return result;
        };
        Action<DeviceInfo, bool> click = (_, _) => { };
        using var group = (IDisposable)Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [render, menu, click], null)!;
        var sync = type.GetMethod("Sync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Forms.NotifyIcon[] Icons() => (Forms.NotifyIcon[])type.GetProperty("Icons",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(group)!;
        void Sync(params DeviceInfo[] devices) => sync.Invoke(group, [devices, "theme"]);
        var first = new DeviceInfo { Id = "a", Name = "First", Level = 67 };
        var second = new DeviceInfo { Id = "b", Name = "Second", Level = 33 };
        Sync(first, second);
        var original = Icons();
        Require(original.Length == 2 && original.All(icon => icon.Visible), "Device icons were not created.");
        Sync(second, first);
        Require(draws == 2 && menus == 2 && Icons().Contains(original[0]), "An unchanged refresh recreated resources.");
        first.Name = "Renamed";
        Sync(first, second);
        Require(original[0].ContextMenuStrip!.Items[0].Text == "Renamed",
            "Renaming the existing device left its tray menu stale.");
        var removedMenu = original[1].ContextMenuStrip!;
        Sync(first);
        Require(Icons().Length == 1 && !original[1].Visible && removedMenu.IsDisposed,
            "Disconnect did not release the device icon and menu.");
        Sync(first, second);
        Require(Icons().Length == 2 && !ReferenceEquals(Icons()[1], original[1]), "Reconnect reused a disposed icon.");
        var remaining = Icons();
        group.Dispose();
        Require(Icons().Length == 0 && remaining.All(icon => !icon.Visible), "Closing left ghost device icons.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
