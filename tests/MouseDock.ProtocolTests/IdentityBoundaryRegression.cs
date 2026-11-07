using MouseDock.Models;
using MouseDock.Services.Detection;
internal static class IdentityBoundaryRegression
{
    internal static int Run()
    {
        int n = 0;
        void Check(bool value, string reason) { n++; if (!value) throw new Exception(reason); }
        InstanceScope.BeginScan();
        var a = new[] { (@"USB\VID_089D&PID_062F\FIRST", "usbccgp") };
        var b = new[] { (@"USB\VID_089D&PID_062F\SECOND", "usbccgp") };
        string first = InstanceScope.Register("first-input", a);
        Check(first == InstanceScope.Register("first-vendor", a), "Collections of one USB parent must merge");
        string second = InstanceScope.Register("second-vendor", b);
        Check(first != second, "Identical receiver IDs must not merge distinct USB instances");
        using (InstanceScope.Enter(first))
        {
            Check(InstanceScope.Allows("first-vendor"), "Own vendor channel admitted");
            Check(!InstanceScope.Allows("second-vendor"), "Other receiver excluded");
            Check(!InstanceScope.Allows("unregistered"), "Unregistered channel excluded");
            using (InstanceScope.Enter(second)) Check(!InstanceScope.Allows("first-vendor"), "Nested isolation");
            Check(InstanceScope.Allows("first-vendor"), "Previous scope restored");
        }
        Check(InstanceScope.Current == null, "Scope released");
        // Descriptor facts from the supplied 1.3.9 report; no raw paths or user settings.
        var headset = new HidDiscovery.Bucket { Vid=0x4C4A, Pid=0x4155, Product="USB Composite Device", Kind=DeviceKind.Headset, InstanceId=first }.ToDevice();
        Check(headset.RawName == "USB Composite Device" && !headset.Name.Contains("竹林鸟"), "Shared audio ID must not assign author's headset brand");
        Check(headset.ConfirmedModel == "" && !headset.PeripheralConfirmed && !headset.BatteryRead, "Discovery alone confirms neither model nor live battery");
        var mouse = new HidDiscovery.Bucket { Vid=0x3837, Pid=0x1018, Product="MCHOSE K5 Ultra", Kind=DeviceKind.Mouse };
        mouse.Counts[DeviceKind.Mouse]=1; mouse.Counts[DeviceKind.Keyboard]=3;
        Check(mouse.ToDevice().Kind == DeviceKind.Mouse, "Reported K5 mouse must not match keyboard K-number heuristic");
        var source = new AutoDetectSource();
        var devices = source.UpdateFromDetection(new() {
            new() { Id=first, PhysicalKey="aula-sc580", Name="SC580" },
            new() { Id=second, PhysicalKey="aula-sc580", Name="SC580" } });
        Check(devices.Count == 2, "Model aliases must not merge two physical devices");
        Check(!DeviceRoutes.IsKnown(0x4C4A,0x4155,"USB Composite Device"), "Unknown headset has no active protocol route");
        Check(!DeviceRoutes.IsKnown(0x3837,0x1018,"MCHOSE K5 Ultra MAX"), "Unlisted K5 variants do not receive guessed queries");
        Check(DeviceRoutes.IsKnown(0x089D,0x062F,"Wireless-Receiver"), "Hardware verified SC580 receiver route retained");
        foreach (var profile in new (ushort Vid, ushort Pid, ushort Page, ushort Usage)[] {
            (0x3554,0xF517,0xFF02,2), (0x395A,6,0xFF00,2), (0x373B,0x11D8,0xFF02,2),
            (0x373B,0x1278,0xFF02,2), (0x24AE,0x1453,0xFF00,2), (0x1915,0xAE8A,0xFFA0,1) })
        {
            Check(ReferenceProfiles.Match(profile.Vid,profile.Pid,profile.Page,profile.Usage) != null, "Verified reference descriptor admitted");
            Check(ReferenceProfiles.Match(profile.Vid,profile.Pid,1,6) == null, "Auxiliary keyboard must not establish reference model");
            Check(ReferenceProfiles.Match(profile.Vid,(ushort)(profile.Pid+1),profile.Page,profile.Usage) == null, "Neighbor PID must not establish reference model");
        }
        return n;
    }
}

