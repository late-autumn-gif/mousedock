using System;
using System.Collections.Generic;
using System.Linq;

namespace MouseDock.Services.Detection;

internal sealed record DpiProfile(ushort Vid, ushort Pid, ushort Page, ushort Usage,
    string Model, DpiFamily Family, bool Wired = false, bool WriteApproved = true,
    int Cid = -1, int Mid = -1, string Codec = "");

/// <summary>Finite settings approvals from the MIT reference. Battery support never adds settings permission.</summary>
internal static class DpiProfiles
{
    internal static IReadOnlyList<DpiProfile> All { get; } = Array.AsReadOnly(new[]
    {
        new DpiProfile(0x3554, 0xF517, 0xFF02, 2, "EWEADN Z7", DpiFamily.Z7),
        new DpiProfile(0x395A, 0x0006, 0xFF00, 2, "Scyrox V12M", DpiFamily.Scyrox),
        new DpiProfile(0x373B, 0x11D8, 0xFF02, 2, "ATK Zero", DpiFamily.Atk),
        new DpiProfile(0x373B, 0x1278, 0xFF02, 2, "ATK F1 V3 Ultimate+", DpiFamily.Atk),
        new DpiProfile(0x24AE, 0x1453, 0xFF00, 2, "Rapoo VT9S V2", DpiFamily.Rapoo),
        new DpiProfile(0x1915, 0xAE8A, 0xFFA0, 1, "Ninjutso Sora V2", DpiFamily.Sora),
        new DpiProfile(0x373B, 0x1031, 0xFF02, 2, "ATK F1 Ultimate", DpiFamily.Catalog, Cid: 2, Mid: 42, Codec: "ultra"),
        new DpiProfile(0x373B, 0x102E, 0xFF02, 2, "ATK F1 Ultimate", DpiFamily.Catalog, true, Cid: 2, Mid: 42, Codec: "ultra"),
        new DpiProfile(0x373B, 0x11D9, 0xFF02, 2, "ATK A9 Ultimate", DpiFamily.Catalog, Cid: 1, Mid: 3, Codec: "ultra"),
        new DpiProfile(0x373B, 0x11B6, 0xFF02, 2, "ATK A9 Ultimate", DpiFamily.Catalog, true, Cid: 1, Mid: 3, Codec: "ultra"),
        new DpiProfile(0x3554, 0xF58A, 0xFF02, 2, "VXE R1 Pro Max", DpiFamily.Catalog, Cid: 2, Mid: 27, Codec: "step50"),
        new DpiProfile(0x3554, 0xF58C, 0xFF02, 2, "VXE R1 Pro Max", DpiFamily.Catalog, true, Cid: 2, Mid: 27, Codec: "step50"),
        new DpiProfile(0x3554, 0xF58E, 0xFF02, 2, "VXE R1 SE+", DpiFamily.Catalog, WriteApproved: false, Cid: 2, Mid: 32, Codec: "se"),
        new DpiProfile(0x3554, 0xF58F, 0xFF02, 2, "VXE R1 SE+", DpiFamily.Catalog, true, Cid: 2, Mid: 32, Codec: "se")
    });

    private static readonly HashSet<ushort> pulsarPids = new(new ushort[]
    {
        21510,21762,21761,21764,21511,22017,22019,13332,13333,29953,29954,29955,29956,29957,29958,
        13574,13575,13576,13577,13584,13586,13587,13588,13589,13590,13591,13592,13593,
        13604,13605,13606,13607,13608,13609,13825,13827,13828,13830,13831,13832,13833,13840,13841,
        38146,38147,38401,29960,29961,29968,29959,30209,30210,30211,30212,30213
    });
    private static readonly HashSet<int> pulsar3950 = new(new[] { 68,69,91,92,93,94,99,100,127,128,129,130,131,132 });
    private static readonly HashSet<int> pulsar3955 = new(new[]
    {
        101,107,108,110,111,112,113,114,115,120,121,122,123,124,125,126,
        133,134,135,136,137,138,139,140,141,142,143,144,145,146,147,148,149
    });
    private static readonly HashSet<int> pulsarX1 = new(Enumerable.Range(1, 67)
        .Concat(Enumerable.Range(70, 21)).Concat(new[] {95,96,97,98,102,103,104,105,106,109,116,117,118,119}));

    internal static DpiProfile Find(ushort vid, ushort pid) =>
        All.FirstOrDefault(p => p.Vid == vid && p.Pid == pid) ??
        (vid == 0x3710 && pulsarPids.Contains(pid) ? new(vid, pid, 0xFF02, 2, "Pulsar cMouse", DpiFamily.Pulsar) : null);

    // Sora's per-ID feature layout is not proven by Windows value caps.
    internal static DpiProfile Native(ushort vid,ushort pid) =>
        Find(vid,pid) is {Family:not DpiFamily.Sora} profile?profile:null;

    internal static string PulsarSensor(int mid) => pulsar3950.Contains(mid) ? "3950" :
        pulsar3955.Contains(mid) ? "3955" : pulsarX1.Contains(mid) ? "x1" : null;
}
