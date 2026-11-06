namespace MouseDock.Services.Detection;
/// <summary>Finite protocol profiles from the six 1.0.2 entries marked verified=true.
/// That is upstream hardware evidence, not a claim of local hardware verification.</summary>
internal static class ReferenceProfiles
{
    internal enum Protocol { Compx, ScyroxV12, SoraV2, Rapoo }
    internal sealed record Profile(ushort Vid, ushort Pid, ushort Page, ushort Usage,
        string Model, Protocol Battery, bool ReadIdentity = false);

    internal static readonly System.Collections.Generic.IReadOnlyList<Profile> All =
        System.Array.AsReadOnly(new[]
        {
            new Profile(0x3554, 0xF517, 0xFF02, 2, "EWEADN Z7", Protocol.Compx),
            new Profile(0x395A, 0x0006, 0xFF00, 2, "Scyrox V12M", Protocol.ScyroxV12),
            new Profile(0x373B, 0x11D8, 0xFF02, 2, "ATK Zero", Protocol.Compx),
            new Profile(0x373B, 0x1278, 0xFF02, 2, "ATK F1 V3 Ultimate+", Protocol.Compx, true),
            new Profile(0x24AE, 0x1453, 0xFF00, 2, "Rapoo VT9S V2", Protocol.Rapoo),
            new Profile(0x1915, 0xAE8A, 0xFFA0, 1, "Ninjutso Sora V2", Protocol.SoraV2)
        });

    internal static Profile Find(ushort vid, ushort pid)
    {
        foreach (var profile in All)
            if (profile.Vid == vid && profile.Pid == pid) return profile;
        return null;
    }

    internal static string Match(ushort vid, ushort pid, ushort page, ushort usage)
    {
        var profile = Find(vid, pid);
        return profile != null && profile.Page == page && profile.Usage == usage ? profile.Model : null;
    }

    internal static bool AcceptsChannel(Profile profile, ushort page, ushort usage,
        int input, int output, int feature)
    {
        if (profile == null || profile.Page != page || profile.Usage != usage) return false;
        return profile.Battery switch
        {
            Protocol.Compx => input == 17 && output == 17,
            Protocol.ScyroxV12 => input == 64 && output == 64,
            Protocol.SoraV2 => feature == 32,
            // The verified VT9S V2 route is passive, not the different VT2/VT7 short-report query.
            Protocol.Rapoo => input is 7 or 19 or 20,
            _ => false
        };
    }
}
