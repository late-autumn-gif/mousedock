using System.Reflection;
using MouseDock.Services.Detection;

internal static class KeyboardExpansionRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Keyboard expansion: " + name);
            count++;
        }
        var assembly = typeof(ReferenceBatteryProtocol).Assembly;
        var decoder = assembly.GetType("MouseDock.Services.Detection.KeyboardExpansionProtocol")?
            .GetMethod("DecodeForReplay", BindingFlags.Static | BindingFlags.NonPublic);
        ReferenceBatteryProtocol.Reading Read(string family, byte[] bytes) =>
            decoder?.Invoke(null, [family, bytes]) as ReferenceBatteryProtocol.Reading ??
            ReferenceBatteryProtocol.Reading.Failed("missing keyboard expansion implementation");
        byte[] Frame(int size, params (int At, byte Value)[] fields)
        {
            var b = new byte[size];
            foreach (var (at, value) in fields) b[at] = value;
            return b;
        }
        var fixtures = new (string Family, byte[] Frame, int Level, bool? Charge)[]
        {
            ("AtkHfd", Frame(32, (0,2),(1,0x96),(2,0x24),(3,61)), 61, null),
            ("Cherry", Frame(16,(0,4),(3,0x1A),(4,3),(7,0x55),(8,37)),37,null),
            ("Nova", Frame(32,(0,0x20),(1,1),(3,49)),49,null),
            ("RogDirect", Frame(64,(0,0x12),(1,1),(5,71),(8,1)),71,true),
            ("Irok", Frame(65,(2,63)),63,null),
            ("Neon", Frame(65,(2,62),(10,1)),62,true),
            ("RoyuanLegacy", Frame(65,(2,44),(4,1)),44,null),
            ("MchoseBy", Frame(20,(0,19),(1,74),(2,1),(5,82)),82,null),
            ("VgnV4", Frame(64,(0,1),(1,0x87),(4,1),(6,2),(7,66)),66,null),
            ("Exx", Frame(64,(0,1),(1,0x87),(4,1),(6,2),(7,55),(8,0x10)),55,true),
            ("Langtu", Frame(64,(0,0xAA),(1,0x55),(2,0xCC),(3,0x33),(4,0x0E),(5,57),(6,1)),57,null),
            ("RapooEvent", Frame(9,(0,0xBD),(1,1),(7,2),(8,31)),31,true)
        };
        foreach (var (family, frame, level, charge) in fixtures)
        {
            var result = Read(family, frame);
            Check(result.Level == level && result.Status is ReferenceBatteryProtocol.State.Online or
                ReferenceBatteryProtocol.State.Charging, family + " valid fresh report");
            Check(charge.HasValue ? result.ChargingKnown && result.Charging == charge.Value :
                !result.ChargingKnown, family + " charging evidence");
            Check(Read(family, family == "Cherry" ? frame[..8] : frame[..^1]).Level < 0,
                family + " rejects a truncated report");
            Check(Read(family, Array.Empty<byte>()).Level < 0, family + " rejects an empty report");
        }
        Check(Read("AtkHfd", Frame(32,(0,2),(1,0x96),(2,0x24),(3,101))).Level < 0,
            "ATK rejects percentage outside range");
        Check(Read("AtkHfd", Frame(32,(0,2),(1,0x96),(2,0x24),(3,61),(4,1))).Level < 0,
            "ATK does not publish frozen percentage while charging");
        Check(Read("Nova", Frame(32,(0,0x20),(1,1))).Level < 0,
            "NOVA zero remains uninitialized");
        Check(Read("Neon", Frame(65,(2,62),(4,1))).Level < 0,
            "Neon offline status does not publish cached percentage");
        Check(Read("RoyuanLegacy", Frame(65,(2,44),(3,1),(4,1))).Level < 0,
            "legacy ROYUAN offline status");
        Check(Read("MchoseBy", Frame(20,(0,19),(1,74),(2,1),(5,82),(6,2))).Level < 0,
            "MCHOSE rejects an unknown flag nibble");
        Check(Read("Exx", Frame(64,(0,1),(1,0x87),(4,1),(6,2),(7,55),(8,0x21))).Level < 0,
            "EXX rejects undocumented charge state");
        Check(Read("Langtu", Frame(64,(0,0xAA),(1,0x55),(2,0xCC),(3,0x33),(4,0x0E),(5,57),(6,2))).Level < 0,
            "Langtu link state must be online");
        var candidate = typeof(VendorProtocol).GetMethod("ExtraKeyboardCandidate",
            BindingFlags.Static | BindingFlags.NonPublic);
        bool Candidate(ushort v, ushort p, string n) =>
            candidate?.Invoke(null,[v,p,n]) is true;
        Check(Candidate(0x373B,0x111F,"ATK A87 Pro"),"ATK HFD identity is admitted");
        Check(!Candidate(0x373B,0x1232,"ATK A87 Ultra"),"unspecified A87 Ultra wireless PID is rejected");
        Check(Candidate(0x0C45,0xFEFC,"AULA F87ProV2C Dongle"),"AULA exact mechanical identity");
        Check(!Candidate(0x0C45,0xFEFC,"8K Receiver"),"AULA shared receiver cannot authorize mechanical queries");
        Check(Candidate(0x3837,0x1015,"MCHOSE G98 V3 2.4G"),"G98 QHW extension");
        Check(!Candidate(0x3837,0x1015,"MCHOSE K87 V3 2.4G"),"existing K87 route retains ownership");
        Check(!Candidate(0x3151,0x5038,"MonsGeek 2.4G Wireless Keyboard"),
            "existing MonsGeek route retains ownership");
        Check(!Candidate(0x3837,0x1015,"MCHO\u017FE G98 V3 2.4G"),
            "USB full-name gate does not case-fold non-ASCII characters");
        return count + KeyboardExpansionTransactions.Run();
    }
}
