using MouseDock.Models;
using MouseDock.Services.Detection;

internal static class ReceiverClassificationRegression
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }

        DetectedDevice Classify(string product, int mouseCount, int keyboardCount,
            ushort vid = 0x1234, ushort pid = 0x5678, int controllerCount = 0)
        {
            // Synthetic composite descriptors, not a capture of the reported device.
            var bucket = new HidDiscovery.Bucket { Vid = vid, Pid = pid, Product = product };
            foreach (ushort usage in Enumerable.Repeat((ushort)2, mouseCount)
                .Concat(Enumerable.Repeat((ushort)6, keyboardCount))
                .Concat(Enumerable.Repeat((ushort)5, controllerCount)))
            {
                if (!HidDiscovery.TryDeviceKind(vid, pid, 1, usage, out var kind))
                    throw new Exception("Fixture must contain standard input collections.");
                bucket.Counts[kind] = bucket.Counts.GetValueOrDefault(kind) + 1;
                if (UsageMap.Priority(kind) > UsageMap.Priority(bucket.Kind)) bucket.Kind = kind;
            }
            return bucket.ToDevice();
        }

        foreach (string product in new[] { "8K Receiver", "Wireless Receiver", "Gaming Mouse 8K Receiver" })
        {
            var receiver = Classify(product, 1, 3);
            Check(receiver.Kind == (product=="8K Receiver"?DeviceKind.Other:DeviceKind.Mouse),
                $"{product}: auxiliary keyboard collections must not outvote the mouse collection");
            Check(receiver.Transport == "2.4G" && receiver.Level == -1 && !receiver.Charging,
                $"{product}: classification must preserve receiver transport and unknown battery");
        }
        Check(Classify("8K Receiver", 1, 1).Kind == DeviceKind.Other,
            "A generic mixed 8K receiver keeps unresolved peripheral type");
        Check(Classify("8K Receiver", 1, 0).Kind == DeviceKind.Mouse,
            "Mouse-only receivers remain mice");
        Check(Classify("8K Receiver", 0, 2).Kind == DeviceKind.Keyboard,
            "8K in the name alone must not turn a keyboard into a mouse");
        foreach (string product in new[] { "8K Keyboard Receiver", "Keychron Receiver", "F87 Receiver", "87 Pro Receiver" })
            Check(Classify(product, 1, 3).Kind == DeviceKind.Keyboard,
                $"{product}: explicit keyboard identity keeps precedence");
        Check(Classify("Wireless Receiver", 1, 3, 0x391D, 0x2A05).Kind == DeviceKind.Keyboard,
            "Verified Neon75 keyboard identity overrides generic receiver priority");
        Check(Classify("Wireless Receiver", 1, 3, 0x046D, 0xC52B).Kind == DeviceKind.Keyboard,
            "Known shared Logitech receiver policy is preserved");
        Check(Classify("Gamepad Receiver", 0, 1, controllerCount: 2).Kind == DeviceKind.Controller,
            "Composite controller with auxiliary keyboard keeps existing classification");
        Check(Classify("Gamepad Receiver", 1, 1, controllerCount: 3).Kind == DeviceKind.Controller,
            "Controller collections must not be classified by the mouse-keyboard receiver rule");
        return assertions;
    }
}
