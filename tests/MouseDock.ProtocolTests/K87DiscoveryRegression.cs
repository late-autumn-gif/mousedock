using MouseDock.Models;
using MouseDock.Services.Detection;

internal static class K87DiscoveryRegression
{
    internal static int Run()
    {
        int checks = 0;
        var failures = new List<string>();
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) failures.Add(message);
        }

        const string product = "MCHOSE K87 V3 2.4G";
        // PR #7 的原始断言：K87 必须能从常规发现流程读到电量，极性是"可达"。
        Check(DeviceRoutes.IsKnown(0x3837, 0x1015, product),
            "The verified K87 battery reader must be reachable from normal discovery.");
        Check(!DeviceRoutes.IsKnown(0x3837, 0x1015, "MCHOSE G98 V3 2.4G"),
            "A shared PID must not admit a different keyboard.");
        Check(DeviceRoutes.IsKnown(0x3837, 0x1018, "MCHOSE K5 Ultra"),
            "K5 Ultra now has its separate exact MHUB profile; K87 does not grant it.");

        foreach (int level in new[] { -1, 0, 85 })
        {
            var bucket = new HidDiscovery.Bucket
            {
                Vid = 0x3837, Pid = 0x1015, Product = product,
                Kind = DeviceKind.Mouse, Battery = level,
                InstanceId = "instance-k87"
            };
            bucket.Counts[DeviceKind.Mouse] = 1;
            bucket.Counts[DeviceKind.Keyboard] = 2;
            var device = bucket.ToDevice();
            Check(device.Kind == DeviceKind.Keyboard,
                $"K87 must retain its keyboard glyph even with a mouse collection and battery={level}.");
            Check(device.Level == level, "Classification must not invent or change a battery reading.");
            Check(device.Id == "instance-k87", "Classification must retain the physical instance.");
        }

        var mouse = new HidDiscovery.Bucket
        {
            Vid = 0x3837, Pid = 0x1018, Product = "MCHOSE K5 Ultra", Kind = DeviceKind.Mouse
        };
        mouse.Counts[DeviceKind.Mouse] = 1;
        mouse.Counts[DeviceKind.Keyboard] = 3;
        Check(mouse.ToDevice().Kind == DeviceKind.Mouse, "A K-number mouse must remain a mouse.");
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
        return checks;
    }
}

