using MouseDock.Services.Detection;

/// <summary>雷蛇 class 0x04 的离线回归：报文结构、档位表解析、改档值与切档。</summary>
internal static class RazerDpiRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string why)
        {
            checks++;
            if (!value) throw new Exception("Razer DPI: " + why);
        }
        var profile = DpiProfiles.Find(0x1532, 0x007B)!;
        Check(profile != null && profile.Family == DpiFamily.Razer && profile.Page == 1 && profile.Usage == 2,
            "Viper Ultimate 接收器收录为雷蛇系统鼠标集合");
        Check(DpiProfiles.Find(0x1532, 0x007A)?.Family == DpiFamily.Razer, "同款 USB 直连同样走雷蛇 DPI");
        Check(DpiProfiles.Native(0x1532, 0x0C00) == null, "未收录的雷蛇型号不继承 DPI 权限");

        var link = new Fake();
        var state = DpiProtocol.Read(profile, link);
        Check(state.Active == 2 && state.Stages.Count == 5 &&
              state.Stages.Select(row => row.X).SequenceEqual(new[] { 400, 800, 1200, 2400, 3200 }),
            "档位表按编号顺序还原并使用中档位以索引表示");
        Check(state.Minimum == 100 && state.Maximum == 20000 && state.Writable, "写入范围与可写标记");
        var edited = state.Target(4, 1600);
        Check(state.Stages[4].X == 3200 && edited.Stages[4].X == 1600 && edited.Active == state.Active,
            "改档值不修改原快照且不切换档位");
        Check(state.Target(1, null).Active == 1 && state.Target(1, null).Stages.SequenceEqual(state.Stages),
            "切档只改当前档位编号");

        var applied = DpiProtocol.Apply(profile, link, state, 2, 1600, () => { });
        Check(link.Writes == 1 && applied.Active == 2 && applied.Stages[2].X == 1600 && applied.Current.X == 1600,
            "改使用中档位后当前 DPI 跟随且只写一次档位表");
        Check(link.Query.Length == 91 && link.Query[2] == 0xFF && link.Query[7] == 4 && link.Query[8] == 0x06 &&
              link.Query[9] == 1 && link.Query[10] == 3 && link.Query[11] == 5 &&
              link.Query[89] == Fake.Checksum(link.Query),
            "写档位表使用 class 0x04 / 命令 0x06 / VARSTORE 与事务号 0xFF");

        link = new Fake();
        state = DpiProtocol.Read(profile, link);
        applied = DpiProtocol.Apply(profile, link, state, 1, null, () => { });
        Check(applied.Active == 1 && applied.Current.X == 800 && link.Live == 800,
            "切档写回后实时 DPI 跟随所选档位");

        link = new Fake();
        state = DpiProtocol.Read(profile, link);
        applied = DpiProtocol.Apply(profile, link, state, 0, 500, () => { });
        Check(applied.Active == 2 && applied.Stages[0].X == 500 && applied.Current.X == 1200,
            "改非使用中档位不改变当前 DPI");

        link = new Fake { IgnoreWrites = true };
        state = DpiProtocol.Read(profile, link);
        Check(Throws(() => DpiProtocol.Apply(profile, link, state, 0, 500, () => { })) && link.Writes == 0,
            "设备拒绝写入时不谎报成功");

        link = new Fake { Corrupt = true };
        Check(Throws(() => DpiProtocol.Read(profile, link)) && link.Writes == 0, "校验和错误的回包被拒绝");

        link = new Fake { Live = 1600 };
        state = DpiProtocol.Read(profile, link);
        Check(state.Current.X == 1600 && state.Note.Contains("不一致"),
            "实时 DPI 与档位表不一致时按实时值显示并提示");
        return checks;
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or
            System.IO.IOException or System.IO.InvalidDataException or TimeoutException)
        { return true; }
    }

    private sealed class Fake : IDpiLink
    {
        internal byte[] Query = Array.Empty<byte>();
        internal int Writes;
        internal int Live = 1200;
        internal bool IgnoreWrites, Corrupt;
        internal byte Active = 3;
        private readonly byte[] ids = { 1, 2, 3, 4, 5 };
        private readonly int[] values = { 400, 800, 1200, 2400, 3200 };
        private byte[]? pending;

        public long Now => 0;
        public byte[] Read(int timeoutMs) => null!;
        public bool Write(byte[] packet, int timeoutMs) => false;
        public void Delay(int milliseconds) { }

        public bool SetFeature(byte[] packet)
        {
            if (packet is not { Length: 91 } || packet[7] != 4 || packet[89] != Checksum(packet)) return false;
            if (IgnoreWrites && packet[8] is 0x05 or 0x06) return false;
            if (packet[8] is 0x05 or 0x06) { Writes++; Query = (byte[])packet.Clone(); }
            pending = (byte[])packet.Clone();
            return true;
        }

        public byte[] GetFeature(byte reportId, int length)
        {
            if (pending == null || reportId != 0 || length != 91) return null!;
            var query = pending; pending = null;
            if (Corrupt) { var bad = new byte[91]; bad[1] = 2; bad[8] = query[8]; bad[89] = 0x7F; return bad; }
            var reply = query.ToArray();
            reply[1] = 2;
            switch (query[8])
            {
                case 0x85:
                    reply[9] = query[9];
                    Byte(reply, 10, Live); Byte(reply, 12, Live);
                    break;
                case 0x05:
                    Live = Read2(query, 10);
                    break;
                case 0x86:
                    reply[9] = 0; reply[10] = Active; reply[11] = (byte)values.Length;
                    for (int i = 0; i < values.Length; i++)
                    {
                        int at = 12 + i * 7; reply[at] = ids[i];
                        Byte(reply, at + 1, values[i]); Byte(reply, at + 3, values[i]);
                    }
                    break;
                case 0x06:
                    Active = query[10];
                    for (int i = 0; i < query[11] && i < 5; i++)
                    {
                        int at = 12 + i * 7; values[i] = Read2(query, at + 1);
                    }
                    Live = values[Array.IndexOf(ids, Active)];
                    break;
            }
            reply[89] = Checksum(reply);
            return reply;
        }

        internal static byte Checksum(byte[] report)
        {
            byte value = 0;
            for (int i = 3; i < 89; i++) value ^= report[i];
            return value;
        }
        private static int Read2(byte[] b, int at) => b[at] << 8 | b[at + 1];
        private static void Byte(byte[] b, int at, int value) { b[at] = (byte)(value >> 8); b[at + 1] = (byte)value; }
    }
}
