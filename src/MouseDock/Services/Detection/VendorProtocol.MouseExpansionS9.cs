using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private sealed record S9Model(ulong Uuid, string Name);
    private sealed record S9Profile(ushort Pid, string Product, bool Wired, S9Model[] Models);

    private static readonly S9Profile[] S9Profiles =
    [
        new(0x1029, "EWEADN S9 Pro", true, [new(0x09000000000EUL, "前行者 S9 Pro")]),
        new(0x1026, "EWEADN S9", false, [
            new(0x09000000002AUL, "前行者 S9 Max"),
            new(0x09000000000EUL, "前行者 S9 Pro"),
            new(0x090000000010UL, "前行者 S9 Ultra"),
            new(0x09000000004AUL, "前行者 S9 Ultra+")
        ]),
        new(0x103D, "Gaming 2.4G", false, [
            new(0x09000000002AUL, "前行者 S9 Max"),
            new(0x09000000000EUL, "前行者 S9 Pro"),
            new(0x090000000010UL, "前行者 S9 Ultra"),
            new(0x09000000004AUL, "前行者 S9 Ultra+")
        ]),
        new(0x1026, "EWEADN S7Max", false, [new(0x09000000003EUL, "前行者 S7 Max")]),
        new(0x1026, "EWEADN S7Pro", false, [new(0x09000000003DUL, "前行者 S7 Pro")]),
        new(0x1026, "EWEADN S7Ultra", false, [new(0x090000000048UL, "前行者 S7 Ultra")]),
        new(0x1027, "EWEADN S9 Ultra", true, [new(0x090000000010UL, "前行者 S9 Ultra")]),
        new(0x103D, "EWEADN S7Max", false, [new(0x09000000003EUL, "前行者 S7 Max")]),
        new(0x103D, "EWEADN S7Pro", false, [new(0x09000000003DUL, "前行者 S7 Pro")]),
        new(0x103D, "EWEADN S7Ultra", false, [new(0x090000000048UL, "前行者 S7 Ultra")]),
        new(0x105E, "EWEADN S9 Max", true, [new(0x09000000002AUL, "前行者 S9 Max")]),
        new(0x106E, "EWEADN S7Max", false, [new(0x09000000003EUL, "前行者 S7 Max")]),
        new(0x106E, "EWEADN S7Ultra", false, [new(0x090000000048UL, "前行者 S7 Ultra")]),
        new(0x107B, "EWEADN S7Pro", true, [new(0x09000000003DUL, "前行者 S7 Pro")]),
        new(0x107C, "EWEADN S7Max", true, [new(0x09000000003EUL, "前行者 S7 Max")]),
        new(0x108A, "EWEADN S7Ultra", true, [new(0x090000000048UL, "前行者 S7 Ultra")]),
        new(0x108D, "EWEADN S9Ultra+", true, [new(0x09000000004AUL, "前行者 S9 Ultra+")]),
    ];

    private static S9Profile FindS9Profile(ushort vid, ushort pid, string product) =>
        vid != 0x372E ? null :
        Array.Find(S9Profiles, profile => profile.Pid == pid && MouseExpansionName(product, profile.Product));

    private static ReferenceBatteryProtocol.Reading MouseExpansionS9(string family, ushort vid, ushort pid,
        string product, ReferenceBatteryProtocol.ILink link)
    {
        bool vk = family == "vk-m2";
        S9Profile profile = vk ? null : FindS9Profile(vid, pid, product);
        if (!vk && profile == null) throw new IOException("Unknown S7/S9 transport");

        bool wired = vk ? pid is 0xA304 or 0xA307 : profile.Wired;
        S9Model paired = null;
        ulong expected = !vk ? 0UL : pid switch
        {
            0xA304 or 0xA305 => 0x09000000000DUL,
            0xA307 or 0xA308 => 0x090000000003UL,
            0xA313 => 0x09000000001EUL,
            0xA316 => 0x090000000022UL,
            _ => throw new IOException("Unknown Valkyrie transport")
        };
        byte[] Query(byte command,byte offset=0)
        {
            var request=MouseExpansionPacket(64,3,0,80,0,(byte)(vk?(command==0x47?1:2):0),command,offset);
            request[1]=(byte)MouseExpansionSum(request.AsSpan(1));
            return MouseExpansionFeature(link,request,b=>b.Length==64&&
                MouseExpansionPrefix(b,3,80,0)&&b[4]==request[4]&&b[5]==offset&&b[3] is >=1 and <=10,4,100);
        }
        void Online()
        {
            var b=Query(0x47);
            if(b[6]!=1)throw new IOException("mouse offline or invalid link state");
        }
        void Identity()
        {
            var b=Query(0x4F,0x80);
            if(b[3]<8)throw new IOException("truncated model UUID");
            ulong uuid=0;
            for(int n=8;n<14;n++)uuid=(uuid<<8)|b[n];
            if (!vk && paired == null)
            {
                paired = Array.Find(profile.Models, model => model.Uuid == uuid);
                if (paired != null) expected = paired.Uuid;
            }
            if (expected == 0 || uuid != expected) throw new IOException("paired model UUID mismatch");
        }
        int level=-1;
        for(int n=0;n<2;n++)
        {
            if(!wired)Online();
            Identity();
            var b=Query(0x4F,0x81);
            if(b[3]<7||(b[6]&127)>100)throw new IOException("invalid battery payload");
            level=b[6]&127;
        }
        Identity();
        if(!wired)Online();
        return MouseExpansionValue(level,"Feature3 reported percent; paired UUID confirmed",
            model: paired?.Name, identity: expected.ToString("X12"));
    }
}
