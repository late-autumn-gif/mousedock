namespace MouseDock.Services.Detection;
/// <summary>Finite, reviewed routes. Offline parser tests alone do not admit hardware.</summary>
internal static class RecognitionPolicy
{
    internal static bool CanQuery(ushort vid, ushort pid, string product) =>
        // Only IDs enter the candidate set here. Exact collection/capability validation
        // and the dedicated protocol route are mandatory before any reference I/O.
        ReferenceProfiles.Find(vid, pid) != null ||
        // Explicitly requested adaptation, bounded by this report's exact product/interface.
        // Protocol-supported; real battery responses are still pending hardware confirmation.
        VendorProtocol.IsReportedX23Se(vid, pid, product) ||
        // Repository documents local hardware verification for these receivers.
        (vid == 0x089D && pid == 0x062F) ||
        (vid == 0x391D && pid == 0x2A05) ||
        (vid == 0x3837 && pid == 0x100B && VendorProtocol.IsMchoseA7V2(vid, pid)) ||
        (vid == 0x5253 && pid == 0x1021);
}
