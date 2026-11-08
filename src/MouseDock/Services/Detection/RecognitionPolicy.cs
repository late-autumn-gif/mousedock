namespace MouseDock.Services.Detection;
/// <summary>Finite, reviewed routes. Offline parser tests alone do not admit hardware.</summary>
internal static class RecognitionPolicy
{
    internal static bool CanQuery(ushort vid, ushort pid, string product) =>
        MchoseKeyboardProtocol.IsSupportedDevice(vid, pid, product) ||
        VgnF1Protocol.IsCandidate(vid, pid, product) ||
        M9ProProtocol.IsCandidate(vid, pid, product) ||
        F75Protocol.IsCandidate(vid, pid, product) ||
        M137FeatureProtocol.IsCandidate(vid, pid, product) ||
        RogOmniKeyboardProtocol.IsCandidate(vid, pid, product) ||
        // Only IDs enter the candidate set here. Exact collection/capability validation
        // and the dedicated protocol route are mandatory before any reference I/O.
        VendorProtocol.IsScopedModernMouse(vid,pid,product) ||
        ReferenceProfiles.Find(vid, pid) != null ||
        ZeroRainyProtocol.Candidate(vid,pid,product) ||
        VendorProtocol.IsScopedG7(vid,pid,product) ||
        VendorProtocol.IsScopedInphic(vid,pid,product) ||
        // Explicitly requested adaptation, bounded by this report's exact product/interface.
        // Protocol-supported; real battery responses are still pending hardware confirmation.
        VendorProtocol.IsReportedX23Se(vid, pid, product) ||
        OriginalGpwProtocol.IsCandidate(vid, pid) ||
        // Requested SC630 adaptation: exact official product plus the dedicated
        // Feature6/41 channel is required by the reader; physical validation pending.
        (vid != 0 && pid != 0 && VendorProtocol.IsAulaSc630(product)) ||
        // Repository documents local hardware verification for these receivers.
        (vid == 0x089D && pid == 0x062F) ||
        (vid == 0x391D && pid == 0x2A05) ||
        (vid == 0x3837 && pid == 0x100B && VendorProtocol.IsMchoseA7V2(vid, pid)) ||
        (vid == 0x5253 && pid == 0x1021);
}
