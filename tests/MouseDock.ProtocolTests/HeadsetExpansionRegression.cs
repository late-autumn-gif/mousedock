using System;
using System.Reflection;
using MouseDock.Services.Detection;

internal static class HeadsetExpansionRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Headset expansion: " + name);
            checks++;
        }
        var type = typeof(ReferenceBatteryProtocol).Assembly.GetType("MouseDock.Services.Detection.HeadsetExpansionProtocol");
        Check(type != null, "native headset decoder is present");
        var decode = type!.GetMethod("Decode", BindingFlags.NonPublic | BindingFlags.Static);
        Check(decode != null, "headset decoder contract");
        ReferenceBatteryProtocol.Reading Read(string family, byte[] b) =>
            (ReferenceBatteryProtocol.Reading)decode!.Invoke(null, [family, b])!;
        byte[] Frame(int size, params (int At, byte Value)[] fields)
        {
            var b = new byte[size];
            foreach (var (at, value) in fields) b[at] = value;
            return b;
        }
        Check(Read("Bolutek", Frame(64, (0, 0x55), (1, 0x65), (2, 73), (3, 3))) is
            { Level: 73, ChargingKnown: false }, "Bolutek exact percent without unverified charging");
        Check(Read("V9Pro", Frame(64, (0, 0x55), (1, 0x65), (2, 97), (3, 3))) is
            { Level: -1, ChargingKnown: false }, "V9 Pro external power never returns frozen percentage");
        Check(Read("V9Pro", Frame(64, (0, 0x55), (1, 0x65), (2, 77), (3, 2))).Level == 77,
            "V9 Pro on-battery read");
        Check(Read("Bolutek", Frame(64, (0, 0x55), (1, 0x65), (2, 70), (3, 0x1A))).Status ==
            ReferenceBatteryProtocol.State.Failed, "Bolutek sleep rejected");
        Check(Read("Mhub", Frame(64, (0, 0x55), (1, 0xAA), (2, 1), (4, 3),
            (6, 1), (7, 11), (8, 1), (9, 84))).Level == 84, "MHUB headset response framing");
        Check(Read("Cloud3", Frame(64, (0, 0x66), (1, 0x89), (2, 1), (4, 66))).Level == 66,
            "Cloud III confirmed response fields");
        Check(Read("Cloud3", Frame(64, (0, 0x66), (1, 0x89), (4, 66))).Level < 0,
            "Cloud III rejects empty connection payload");
        Check(Read("Cloud2", Frame(20, (0, 6), (1, 255), (2, 187), (3, 2), (7, 62))).Level == 62,
            "Cloud II exact twenty-byte response");
        Check(Read("Alpha2", Frame(64, (0, 0x51), (1, 2), (2, 56), (6, 128))) is
            { Level: 56, Charging: true }, "Cloud Alpha2 power flags");
        Check(Read("Rog", Frame(64, (0, 0xCC), (1, 0x12), (2, 7), (6, 48))).Level == 48,
            "ROG report CC battery");
        Check(Read("Rog", Frame(64, (0, 0xCC), (1, 0x12), (2, 7), (5, 255), (6, 170))).Level < 0,
            "ROG negative acknowledgement");
        Check(Read("Hecate", Frame(16, (0, 0xD0), (1, 0x35), (2, 0xBB), (3, 4), (5, 1), (6, 59), (7, 2))) is
            { Level: 59, ChargingKnown: false }, "HECATE charging enum kept unknown");
        Check(Read("HecateTws", Frame(16, (0, 0xD0), (1, 0x35), (2, 0xBB), (3, 7),
            (5, 3), (6, 70), (7, 49), (8, 4))).Level == 49, "TWS returns minimum online earbud only");
        Check(Read("Siren", Frame(64, (0, 2), (1, 11), (2, 1), (4, 4))) is
            { Level: -1, Diagnostic: "device-band=5/10" }, "Siren ten steps are not exact percentage");
        Check(Read("Jbl", Frame(3, (0, 8), (1, 67))).Level == 67, "JBL passive report");
        foreach (string family in new[] { "Bolutek", "V9Pro", "Mhub", "Cloud3", "Cloud2", "Alpha2", "Rog", "Hecate",
            "HecateTws", "Siren", "Jbl" })
        {
            Check(Read(family, []).Status == ReferenceBatteryProtocol.State.Failed, family + " empty report");
            Check(Read(family, [0]).Status == ReferenceBatteryProtocol.State.Failed, family + " short report");
        }
        Check(HeadsetExpansionProfiles.Find(0x291D,0x385D,"MCHO\u017FE V9 PRO") == null,
            "V9 Pro full name only folds ASCII");
        var headsetChannel = new HidDiscovery.VendorCollectionInfo
        { Path = @"\\?\hid#vid_03f0&pid_06be&mi_03&col01#x",Vid=0x03F0,Pid=0x06BE,
            UsagePage=0x1C0,Usage=1,Input=64,Output=64 };
        Check(HeadsetExpansionProfiles.Matches("Cloud3S",headsetChannel), "Cloud III S finite uncommon usage pair");
        headsetChannel.Path = @"\\?\hid#vid_03f0&pid_06be&mi_02&col01#x";
        Check(!HeadsetExpansionProfiles.Matches("Cloud3S",headsetChannel), "Cloud III S interface bound");
        return checks;
    }
}
