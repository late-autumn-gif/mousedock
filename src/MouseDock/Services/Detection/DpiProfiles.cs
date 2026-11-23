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

    // DB227 cMouse 1.3.13. The live CID/MID selects the sensor codec before writing.
    private static readonly HashSet<ushort> pulsarPids =
    [
        0x3414, 0x3415, 0x3501, 0x3502, 0x3503, 0x3504, 0x3505, 0x3506, 0x3507,
        0x3508, 0x3509, 0x3510, 0x3511, 0x3512, 0x3513, 0x3514, 0x3515, 0x3516,
        0x3517, 0x3518, 0x3519, 0x3520, 0x3521, 0x3522, 0x3523, 0x3524, 0x3525,
        0x3526, 0x3527, 0x3528, 0x3529, 0x3601, 0x3603, 0x3604, 0x3605, 0x3606,
        0x3607, 0x3608, 0x3609, 0x3610, 0x3611, 0x5406, 0x5407, 0x5501, 0x5502,
        0x5503, 0x5504, 0x5601, 0x5603, 0x7501, 0x7502, 0x7503, 0x7504, 0x7505,
        0x7506, 0x7507, 0x7508, 0x7509, 0x7510, 0x7601, 0x7602, 0x7603, 0x7604,
        0x7605, 0x9502, 0x9503, 0x9601, 0xF004, 0xF005
    ];
    private static readonly HashSet<int> pulsar3950 =
    [
        68, 69, 91, 92, 93, 94, 99, 100, 127,
        128, 129
    ];
    private static readonly HashSet<int> pulsar3955 =
    [
        101, 107, 108, 110, 111, 112, 113, 114, 115,
        120, 121, 122, 123, 124, 125, 126, 133, 134,
        135, 136, 137, 138, 139, 140, 141, 142, 143,
        144, 145, 146, 147, 148, 149
    ];
    private static readonly HashSet<int> pulsarX1 =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9,
        10, 11, 12, 13, 14, 15, 16, 17, 18,
        19, 20, 21, 22, 23, 24, 25, 26, 27,
        28, 29, 30, 31, 32, 33, 34, 35, 36,
        37, 38, 39, 40, 41, 42, 43, 44, 45,
        46, 47, 48, 49, 50, 51, 52, 53, 54,
        55, 56, 57, 58, 59, 60, 61, 62, 63,
        64, 65, 66, 67, 70, 71, 72, 73, 74,
        75, 76, 77, 78, 79, 80, 81, 82, 83,
        84, 85, 86, 87, 88, 89, 90, 95, 96,
        97, 98, 102, 103, 104, 105, 106, 109, 116,
        117, 118, 119, 130, 131, 132
    ];

    internal static DpiProfile Find(ushort vid, ushort pid) =>
        All.FirstOrDefault(p => p.Vid == vid && p.Pid == pid) ??
        (vid == 0x3710 && pulsarPids.Contains(pid) ? new(vid, pid, 0xFF02, 2, "Pulsar cMouse", DpiFamily.Pulsar) : null);

    // Sora's per-ID feature layout is not proven by Windows value caps.
    internal static DpiProfile Native(ushort vid,ushort pid) =>
        Find(vid,pid) is {Family:not DpiFamily.Sora} profile?profile:null;

    internal static string PulsarSensor(int mid) => pulsar3950.Contains(mid) ? "3950" :
        pulsar3955.Contains(mid) ? "3955" : pulsarX1.Contains(mid) ? "x1" : null;
}
