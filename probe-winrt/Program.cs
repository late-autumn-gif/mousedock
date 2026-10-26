using System;

internal static class Probe
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "vendor";

        switch (mode)
        {
            case "rawinput":
                int secs = args.Length > 1 && int.TryParse(args[1], out int s0) ? s0 : 12;
                Console.WriteLine("=== Raw Input：找出你此刻真正在用的鼠标 ===");
                RawInputProbe.Run(secs);
                break;

            case "hfp":
                string hint = args.Length > 1 ? args[1] : "Air Plus";
                HfpBatteryProbe.RunAsync(hint).GetAwaiter().GetResult();
                break;

            case "hfpag":
                int agSecs = args.Length > 2 ? (int.TryParse(args[2], out int agTmp) ? agTmp : 15) : 15;
                HfpAgProbe.RunAsync(args.Length > 1 ? args[1] : "Air Plus", agSecs).GetAwaiter().GetResult();
                break;

            case "rfcomm":
                RfcommProbe.RunAsync(args.Length > 1 ? args[1] : "Air Plus").GetAwaiter().GetResult();
                break;

            case "protocol":
                Console.WriteLine("=== 复刻官方驱动握手 ===");
                ProtocolProbe.Run(0x089D, 0x062F);
                break;

            case "noop":
                ushort v5 = args.Length > 1 ? Convert.ToUInt16(args[1], 16) : (ushort)0x089D;
                ushort p5 = args.Length > 2 ? Convert.ToUInt16(args[2], 16) : (ushort)0x062F;
                Console.WriteLine("=== 发送全 0 空帧 ===");
                SweepProbe.Noop(v5, p5);
                break;

            case "listen":
                ushort v4 = args.Length > 1 ? Convert.ToUInt16(args[1], 16) : (ushort)0x089D;
                ushort p4 = args.Length > 2 ? Convert.ToUInt16(args[2], 16) : (ushort)0x062F;
                int sec4 = args.Length > 3 && int.TryParse(args[3], out int s4) ? s4 : 20;
                SweepProbe.Listen(v4, p4, sec4);
                break;

            case "sweep":
                ushort v3 = args.Length > 1 ? Convert.ToUInt16(args[1], 16) : (ushort)0x089D;
                ushort p3 = args.Length > 2 ? Convert.ToUInt16(args[2], 16) : (ushort)0x062F;
                Console.WriteLine($"=== 扫描 + 被动监听 {v3:X4}:{p3:X4} ===");
                SweepProbe.Run(v3, p3);
                break;

            case "probe24g2":
                ushort v2 = args.Length > 1 ? Convert.ToUInt16(args[1], 16) : (ushort)0x089D;
                ushort p2 = args.Length > 2 ? Convert.ToUInt16(args[2], 16) : (ushort)0x062F;
                Console.WriteLine($"=== 主动查询探针 v2（按报告描述符） {v2:X4}:{p2:X4} ===");
                ActiveProbe2.Run(v2, p2);
                break;

            case "probe24g":
                ushort vid = args.Length > 1 ? Convert.ToUInt16(args[1], 16) : (ushort)0x089D;
                ushort pid = args.Length > 2 ? Convert.ToUInt16(args[2], 16) : (ushort)0x062F;
                Console.WriteLine($"=== 主动查询探针 {vid:X4}:{pid:X4} ===");
                ActiveProbe.Run(vid, pid);
                break;

            case "capture":
                int seconds = args.Length > 1 && int.TryParse(args[1], out int s) ? s : 6;
                int known = args.Length > 2 && int.TryParse(args[2], out int k) ? k : 43;
                Console.WriteLine($"=== 2.4G 接收器报文抓取（真值 {known}%，{seconds}s）===");
                Capture.Run(known, seconds);
                break;

            default:
                int known2 = args.Length > 1 && int.TryParse(args[1], out int k2) ? k2 : 43;
                Console.WriteLine($"=== 厂商集合扫描（真值 {known2}%）===");
                VendorProbe.Run(known2);
                break;
        }

        Console.WriteLine("\n完成。");
    }
}
