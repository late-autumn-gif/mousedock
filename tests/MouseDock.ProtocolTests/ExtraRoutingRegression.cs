using MouseDock.Services.Detection;

internal static class ExtraRoutingRegression
{
    internal static int Run()
    {
        var existing = new (ushort Vid, ushort Pid, string Name)[]
        {
            (0x373B, 0x1155, "ATK ZERO"),
            (0x373B, 0x11D8, "ATK Zero"),
            (0x395A, 0x0006, "Scyrox V12M"),
            (0x3837, 0x1018, "MCHOSE K5 Ultra"),
            (0x3837, 0x1018, "MCHOSE G7"),
            (0x3837, 0x100B, "A7 V2 Ultra+"),
            (0x046D, 0xC539, "USB Receiver"),
            (0x046D, 0xC547, "USB Receiver"),
            (0x3554, 0xFA09, "AULA F99"),
            (0x3554, 0xFA09, "AULA F99 Wired"),
            (0x089D, 0x062F, "Wireless-Receiver"),
            (0xA8A5, 0x2255, "EWEADN X23SE"),
            (0x3554, 0xF503, VgnF1Protocol.Product),
            (0x3554, 0xFA09, "2.4G Wireless Receiver"),
            (0x3837, 0x1015, "MCHOSE K87 V3 2.4G")
        };
        foreach (var (vid, pid, name) in existing)
            if (ExtraDeviceRoutes.Find(vid, pid, name) != null)
                throw new Exception($"Extra route displaced an existing reader: {vid:X4}:{pid:X4} {name}");
        return existing.Length;
    }
}
