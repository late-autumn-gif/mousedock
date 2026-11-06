namespace MouseDock.Services.Detection;
/// <summary>Protocol facts from 1.0.2 entries explicitly marked verified=true.
/// Descriptor recognition does not certify this application's battery implementation.</summary>
internal static class ReferenceProfiles
{
    internal static string Match(ushort vid, ushort pid, ushort page, ushort usage) => (vid, pid, page, usage) switch
    {
        (0x3554, 0xF517, 0xFF02, 2) => "EWEADN Z7",
        (0x395A, 0x0006, 0xFF00, 2) => "Scyrox V12M",
        (0x373B, 0x11D8, 0xFF02, 2) => "ATK Zero",
        (0x373B, 0x1278, 0xFF02, 2) => "ATK F1 V3 Ultimate+",
        (0x24AE, 0x1453, 0xFF00, 2) => "Rapoo VT9S V2",
        (0x1915, 0xAE8A, 0xFFA0, 1) => "Ninjutso Sora V2",
        _ => null
    };
}
