using System;
using System.Collections.Generic;
using System.Linq;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionAulaHub
{
    private sealed record Board(ushort Receiver, string Product, string Model, ushort[] Ids);
    private static readonly Board[] boards =
    [
        new(0xFEFC,"AULA F87ProV2C Dongle","AULA F87 Pro V2 C",[0x2712,0x2738,0x2747]),
        new(0xFEFE,"AULA F87ProV2D Dongle","AULA F87 Pro V2 D",[0x2711,0x2737,0x2746]),
        new(0xFEFE,"AULA F87ProV2Z Dongle","AULA F87 Pro V2 Z",[0x2710,0x2736,0x2748]),
        new(0xFEFE,"AULA F108Pro V2 Dongle","AULA F108 Pro V2",[0x271A,0x2739]),
        new(0xFEFC,"AULA F108ProV2C Dongle","AULA F108 Pro V2 C",[0x273A,0x2749]),
        new(0xFEFC,"AULA NOVA75 Pro","AULA NOVA75 Pro",[0x275C]),
        new(0xFEFC,"F75 V2 Dongle","Epomaker AULA F75 V2",[0x273F]),
        new(0xFEFC,"AULA F75 V2 Dongle","AULA F75 V2",[0x2768]),
        new(0xFEFC,"AULA NOVA98 Dongle","AULA NOVA98",[0x273B,0x275D]),
        new(0xFEF9,"F75 Master Dongle","AULA F75 Master",[0x2758])
    ];
    internal static bool HasProfile(ushort pid, string product) => boards.Any(b => b.Receiver == pid &&
        KeyboardExpansionProfiles.Name(b.Product) == KeyboardExpansionProfiles.Name(product));
    internal static byte[] Request(int size, int address, int total)
    {
        Require(size is 32 or 64 && total is 48 or 56 && address >= 0 && address < total,
            "Invalid device-info range");
        int count = Math.Min(size - 8,total - address);
        return Packet(size + 1,0,0xAA,0x10,(byte)count,(byte)address,(byte)(address >> 8),0,
            (byte)(address + count == total ? 1 : 0));
    }
    internal static byte[] Block(byte[] b, int size, int address, int count)
    {
        Require(b.Length == size && b[0] == 0x55 && b[1] == 0x10 &&
            b[2] == count && U16(b,3) == address && count > 0 && count <= size - 8,
            "Invalid device-info response block");
        return b.AsSpan(8,count).ToArray();
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire,
        ushort pid, string product, bool weikav)
    {
        var board = boards.SingleOrDefault(b => b.Receiver == pid &&
            KeyboardExpansionProfiles.Name(b.Product) == KeyboardExpansionProfiles.Name(product));
        Require(weikav || board != null, "Missing mechanical keyboard board profile");
        if (!weikav) wire.CheckInput = b =>
        {
            Require(!(b.Length >= 3 && b[0] == 0x55 && b[1] == 0xFC && b[2] is 4 or 5 or 6),
                "AULA keyboard disconnected, reset or entered sleep");
        };
        byte[] previous = null;
        int level = -1;
        ushort boardPid = 0;
        int total = weikav ? 48 : 56;
        for (int round = 0; round < 2; round++)
        {
            var data = new List<byte>(total);
            for (int address = 0; address < total; address += wire.Input - 8)
            {
                int at = address, count = Math.Min(wire.Input - 8,total - address);
                var b = wire.Exchange(Request(wire.Input,address,total),
                    b => b.Length >= 5 && b[0] == 0x55 && b[1] == 0x10 && U16(b,3) == at,500);
                data.AddRange(Block(b,wire.Input,address,count));
            }
            var info = data.ToArray();
            Require(info.Length == total && info[17] <= 100, "Invalid device-info percentage");
            level = info[17];
            if (weikav) continue;
            boardPid = (ushort)U16(info,6);
            Require(U16(info,4) == 0x38A6 && board.Ids.Contains(boardPid) && info[18] <= 2,
                "Unexpected mechanical keyboard board or power status");
            var identity = info.AsSpan(4,6).ToArray().Concat(info.AsSpan(12,4).ToArray()).ToArray();
            Require(previous == null || identity.AsSpan().SequenceEqual(previous),
                "Mechanical keyboard identity changed");
            previous = identity;
        }
        if (!weikav) wire.Drain();
        // Hardware charging semantics remain unverified for these profiles.
        return Value(level, model: weikav ? null : board.Model,
            identity: weikav ? null : "38A6:" + boardPid.ToString("X4"));
    }
}
