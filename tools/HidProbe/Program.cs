using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HidProbe
{
    /// <summary>
    /// 把厂商驱动发给接收器的查询帧、接收器的应答、以及设备主动上报全部录下来。
    ///
    /// 能录到什么：
    ///   IN      —— 设备/接收器发上来的输入报告（驱动查询的应答也走这条，会被同一份广播复制过来）
    ///   SEND    —— 本工具主动下发的查询帧（帧内容来自各家官方驱动的实测样本）
    ///   FEATURE —— HidD_GetFeature 读到的 feature 报告
    ///   FAIL    —— 打开失败、写入失败，带 Win32 错误码
    ///
    /// 抓不到的：驱动自己写下去的帧内容。Windows 用户态没有 API 能旁听别的进程的 HID 写。
    /// 要拿到那个，用 --usbpcap 参数（需已装 USBPcap），或者看本工具在同样设备上
    /// 发同一帧、收到什么应答——两边的应答形状一致就说明帧抄对了。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            var options = Options.Parse(args);
            if (options.Help) { Options.PrintUsage(); return 0; }

            Console.WriteLine("MouseDock HID 电量抓取器 1.0");
            Console.WriteLine("本机: " + Environment.MachineName + "  " + Environment.OSVersion.VersionString);
            Console.WriteLine();

            string outDir = Options.ResolveOutputDirectory(options.OutputDirectory);
            Directory.CreateDirectory(outDir);
            Console.WriteLine("输出目录: " + outDir);
            Console.WriteLine();

            var collections = Enumerate();
            Console.WriteLine("枚举到 " + collections.Count + " 个 HID 集合。");
            WriteDevices(Path.Combine(outDir, "devices.txt"), collections);
            Console.WriteLine("  devices.txt 已写出");
            Console.WriteLine();

            var recorder = new Recorder();
            using var cancel = new CancellationTokenSource();
            var listeners = StartListeners(collections, recorder, cancel.Token);

            if (!options.ReadOnly)
            {
                Console.WriteLine("------------------------------------------------------------");
                Console.WriteLine("现在是「外部触发窗口」，请去官方驱动/网页驱动里点一下电量、");
                Console.WriteLine("或把鼠标/键盘切一次连接模式。这期间设备发出的报告会原样录下来。");
                Console.WriteLine("窗口 " + options.WindowSeconds + " 秒，按回车可提前结束。");
                Console.WriteLine("------------------------------------------------------------");
                Countdown(options.WindowSeconds, () => !Console.IsInputRedirected && Console.KeyAvailable);
            }

            if (!options.ReadOnly && options.Probe)
            {
                Console.WriteLine();
                Console.WriteLine("开始主动查询（只发各家官方的只读电量/设备信息命令）。");
                ProbeAll(collections, recorder, options.HoldMs);
            }

            cancel.Cancel();
            foreach (var t in listeners) { try { t.Join(1500); } catch { } }

            var events = recorder.Snapshot();
            WriteTimeline(Path.Combine(outDir, "timeline.txt"), events, collections);
            WriteJsonLines(Path.Combine(outDir, "events.jsonl"), events, collections);
            WritePairs(Path.Combine(outDir, "pairs.txt"), events, collections);
            WriteSummary(Path.Combine(outDir, "summary.txt"), events, collections, outDir);

            Console.WriteLine();
            Console.WriteLine("完成。共记录 " + events.Count + " 条事件。");
            Console.WriteLine("把整个目录打包发回来就行：");
            Console.WriteLine("  " + outDir);
            Console.WriteLine();
            Console.WriteLine("先看 summary.txt 和 pairs.txt；timeline.txt 是全量时间线。");

            if (!options.NoPause)
            {
                Console.WriteLine();
                Console.WriteLine("按回车关闭。");
                try { Console.ReadLine(); } catch { }
            }
            return 0;
        }

        // ───────────────────────── 外部触发窗口 ─────────────────────────

        private static void Countdown(int seconds, Func<bool> enterPressed)
        {
            for (int i = seconds; i > 0; i--)
            {
                Console.Write("\r剩余 " + i + " 秒   ");
                for (int slice = 0; slice < 5; slice++)
                {
                    Thread.Sleep(200);
                    try { if (Console.IsInputRedirected || enterPressed()) { Console.WriteLine(); return; } }
                    catch { }
                }
            }
            Console.WriteLine();
        }

        // ───────────────────────── 枚举 ─────────────────────────

        private static List<HidCollection> Enumerate()
        {
            var list = new List<HidCollection>();
            Native.HidD_GetHidGuid(out Guid guid);
            IntPtr set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;

            try
            {
                int index = 0;
                for (uint i = 0; ; i++)
                {
                    var did = new Native.SP_DEVICE_INTERFACE_DATA
                    {
                        Size = Marshal.SizeOf(typeof(Native.SP_DEVICE_INTERFACE_DATA))
                    };
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did)) break;

                    Native.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out int needed, IntPtr.Zero);
                    if (needed <= 0) continue;

                    IntPtr buffer = Marshal.AllocHGlobal(needed);
                    string path = null;
                    try
                    {
                        Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                        if (Native.SetupDiGetDeviceInterfaceDetail(set, ref did, buffer, needed, out _, IntPtr.Zero))
                            path = Marshal.PtrToStringUni(buffer + 4);
                    }
                    finally { Marshal.FreeHGlobal(buffer); }

                    if (string.IsNullOrEmpty(path)) continue;

                    IntPtr h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                        IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                    try
                    {
                        var attributes = new Native.HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES)) };
                        if (!Native.HidD_GetAttributes(h, ref attributes)) continue;

                        var caps = Native.Caps(h);
                        index++;
                        list.Add(new HidCollection
                        {
                            Index = index,
                            Path = path,
                            Vid = attributes.VendorID,
                            Pid = attributes.ProductID,
                            Version = attributes.VersionNumber,
                            UsagePage = caps.UsagePage,
                            Usage = caps.Usage,
                            InputLength = caps.InputReportByteLength,
                            OutputLength = caps.OutputReportByteLength,
                            FeatureLength = caps.FeatureReportByteLength,
                            Product = Native.ProductString(h)
                        });
                    }
                    finally { Native.CloseHandle(h); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }

            return list;
        }

        // ───────────────────────── 监听 ─────────────────────────

        private static List<Thread> StartListeners(List<HidCollection> collections, Recorder recorder, CancellationToken token)
        {
            var threads = new List<Thread>();
            foreach (var c in collections)
            {
                if (c.InputLength <= 0 || c.InputLength > 4096) continue;
                // 标准键盘/鼠标集合被系统独占，GENERIC_READ 一律 err=5。
                // 电量只可能出现在厂商私有集合上，那些才是要听的对象。
                if (!IsWorthListening(c)) continue;

                var capture = c;
                var thread = new Thread(() => ListenLoop(capture, recorder, token))
                {
                    IsBackground = true,
                    Name = "listen-" + capture.Index.ToString(CultureInfo.InvariantCulture)
                };
                thread.Start();
                threads.Add(thread);
            }
            return threads;
        }

        /// <summary>厂商集合，或者名字里带 receiver/dongle 的接收器集合。</summary>
        private static bool IsWorthListening(HidCollection c)
        {
            if (c.Path.EndsWith("\\kbd", StringComparison.OrdinalIgnoreCase)) return false;
            if (c.Path.EndsWith("\\mouse", StringComparison.OrdinalIgnoreCase)) return false;
            if (c.UsagePage >= 0xFF00) return true;
            string product = c.Product ?? "";
            return product.IndexOf("receiver", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   product.IndexOf("dongle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ListenLoop(HidCollection c, Recorder recorder, CancellationToken token)
        {
            IntPtr h = Native.CreateFile(c.Path, Native.GENERIC_READ,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
            {
                if (c.UsagePage >= 0xFF00)
                    recorder.Add("FAIL", c, null, "监听打开失败 err=" + Marshal.GetLastWin32Error() +
                                                 "（官方驱动可能独占了这个集合，先关掉驱动再跑）");
                return;
            }

            IntPtr ev = Native.CreateEvent(IntPtr.Zero, false, false, IntPtr.Zero);
            try
            {
                int quiet = 0;
                while (!token.IsCancellationRequested)
                {
                    if (!Native.OverlappedRead(h, ev, c.InputLength, 400, out byte[] data))
                    {
                        quiet++;
                        if (quiet > 2 && token.IsCancellationRequested) break;
                        continue;
                    }
                    quiet = 0;
                    if (data != null && data.Length > 0) recorder.Add("IN", c, data, "");
                }
            }
            catch (Exception ex)
            {
                recorder.Add("FAIL", c, null, "监听异常 " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (ev != IntPtr.Zero) Native.CloseHandle(ev);
                Native.CloseHandle(h);
            }
        }

        // ───────────────────────── 主动查询 ─────────────────────────

        private static void ProbeAll(List<HidCollection> collections, Recorder recorder, int holdMs)
        {
            foreach (var c in collections)
            {
                var commands = CandidateFrames.Build(c);
                if (commands.Count == 0) continue;

                IntPtr h = Native.CreateFile(c.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                    IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                {
                    recorder.Add("FAIL", c, null, "查询打开失败 err=" + Marshal.GetLastWin32Error());
                    continue;
                }

                IntPtr ev = Native.CreateEvent(IntPtr.Zero, false, false, IntPtr.Zero);
                try
                {
                    foreach (var command in commands)
                    {
                        if (command.FeatureRead)
                        {
                            int length = c.FeatureLength;
                            if (length <= 0 || length > 4096) continue;
                            var buffer = new byte[length];
                            bool ok = Native.HidD_GetFeature(h, buffer, length);
                            string note = command.Name;
                            if (!ok)
                            {
                                int err = Marshal.GetLastWin32Error();
                                note += err == 0 ? "（设备拒绝，未设置错误码）" : " err=" + err;
                            }
                            recorder.Add(ok ? "FEATURE" : "FAIL", c, ok ? buffer : null, note);
                            Thread.Sleep(60);
                            continue;
                        }

                        recorder.Add("SEND", c, command.Frame, command.Name);
                        bool written;
                        if (command.Feature)
                            written = Native.HidD_SetFeature(h, command.Frame, command.Frame.Length);
                        else
                            written = Native.OverlappedWrite(h, ev, command.Frame, 400);

                        if (!written)
                            recorder.Add("FAIL", c, null, command.Name + " 写入失败 err=" + Marshal.GetLastWin32Error());

                        Thread.Sleep(holdMs);
                    }
                }
                catch (Exception ex)
                {
                    recorder.Add("FAIL", c, null, "查询异常 " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    if (ev != IntPtr.Zero) Native.CloseHandle(ev);
                    Native.CloseHandle(h);
                }
            }
        }

        // ───────────────────────── 输出 ─────────────────────────

        private static void WriteDevices(string path, List<HidCollection> collections)
        {
            var sb = new StringBuilder();
            sb.AppendLine("HidProbe 设备清单");
            sb.AppendLine("生成: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine("格式: 序号  VID:PID  用法页/用法  输入/输出/feature 字节  产品名");
            sb.AppendLine("厂商私有用法页是 0xFF00 以上，电量协议基本都在那些集合里。");
            sb.AppendLine();

            foreach (var c in collections)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:D3}. {1:X4}:{2:X4}  {3:X4}/{4:X4}  in={5} out={6} feat={7}  {8}",
                    c.Index, c.Vid, c.Pid, c.UsagePage, c.Usage, c.InputLength, c.OutputLength, c.FeatureLength,
                    string.IsNullOrWhiteSpace(c.Product) ? "(无产品名)" : c.Product));
                sb.AppendLine("     路径: " + c.Path);
                if (c.UsagePage >= 0xFF00) sb.AppendLine("     ↑ 厂商私有接口");
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static void WriteTimeline(string path, List<Ev> events, List<HidCollection> collections)
        {
            var map = collections.ToDictionary(c => c.Index);
            var sb = new StringBuilder();
            sb.AppendLine("HidProbe 时间线（按发生顺序）");
            sb.AppendLine("生成: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine("IN      = 设备/接收器发上来的输入报告");
            sb.AppendLine("SEND    = 本工具下发的查询帧");
            sb.AppendLine("FEATURE = HidD_GetFeature 读到的内容");
            sb.AppendLine("FAIL    = 打开/写入失败，带 Win32 错误码");
            sb.AppendLine();

            foreach (var e in events)
            {
                map.TryGetValue(e.Collection, out var c);
                string where = c == null ? "col?" : string.Format(CultureInfo.InvariantCulture,
                    "#{0:D3} {1:X4}:{2:X4} up={3:X4}", c.Index, c.Vid, c.Pid, c.UsagePage);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "+{0,7}ms  {1,-7} {2}  {3,3}B  {4}",
                    e.Ms, e.Kind, where, e.Length, string.IsNullOrEmpty(e.Hex) ? e.Note : e.Hex));
                if (!string.IsNullOrEmpty(e.Hex) && !string.IsNullOrEmpty(e.Note))
                    sb.AppendLine("                                  说明: " + e.Note);
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static void WriteJsonLines(string path, List<Ev> events, List<HidCollection> collections)
        {
            var map = collections.ToDictionary(c => c.Index);
            var sb = new StringBuilder();
            foreach (var e in events)
            {
                map.TryGetValue(e.Collection, out var c);
                sb.Append("{\"t\":").Append(e.Ms)
                  .Append(",\"kind\":\"").Append(e.Kind)
                  .Append("\",\"col\":").Append(e.Collection)
                  .Append(",\"vid\":").Append(c?.Vid ?? 0)
                  .Append(",\"pid\":").Append(c?.Pid ?? 0)
                  .Append(",\"up\":").Append(c?.UsagePage ?? 0)
                  .Append(",\"usage\":").Append(c?.Usage ?? 0)
                  .Append(",\"len\":").Append(e.Length)
                  .Append(",\"hex\":\"").Append(e.Hex ?? "")
                  .Append("\",\"note\":\"").Append(Escape(e.Note))
                  .AppendLine("\"}");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        /// <summary>
        /// 把每条 SEND 和它之后的 IN 对起来。这一段是收件人最该看的：
        /// 发下去什么、设备回了什么、隔了多少毫秒。
        /// </summary>
        private static void WritePairs(string path, List<Ev> events, List<HidCollection> collections)
        {
            var map = collections.ToDictionary(c => c.Index);
            var sb = new StringBuilder();
            sb.AppendLine("HidProbe 请求/应答配对");
            sb.AppendLine();
            sb.AppendLine("每条 SEND 后面跟的 IN 就是这段时间里设备发上来的报告。");
            sb.AppendLine("间隔在几十毫秒内、长度一致的，基本就是这条命令的应答。");
            sb.AppendLine();

            string current = null;
            int currentCol = -1;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.Kind != "SEND") continue;

                map.TryGetValue(e.Collection, out var c);
                current = e.Note;
                currentCol = e.Collection;
                sb.AppendLine("--------------------------------------------------------------");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "[命令] {0}   → {1:X4}:{2:X4} #{3:D3} up={4:X4} usage={5:X4}",
                    e.Note, c?.Vid ?? 0, c?.Pid ?? 0, e.Collection, c?.UsagePage ?? 0, c?.Usage ?? 0));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  SEND +{0,7}ms  {1}", e.Ms, e.Hex));

                int shown = 0;
                for (int j = i + 1; j < events.Count; j++)
                {
                    var next = events[j];
                    if (next.Kind == "SEND") break;
                    if (next.Kind != "IN") continue;
                    if (next.Collection != currentCol) continue;
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  RECV +{0,7}ms  ({1}ms 后)  {2,3}B  {3}", next.Ms, next.Ms - e.Ms, next.Length, next.Hex));
                    if (++shown >= 6) { sb.AppendLine("  …（更多见 timeline.txt）"); break; }
                }

                if (shown == 0)
                    sb.AppendLine("  RECV （这段窗口里没有输入报告——设备没应答，或者应答走了别的集合）");
            }

            if (current == null) sb.AppendLine("这次没有发出任何查询帧。");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// 摘要：哪些集合看着像电量通道、应答里哪些字节像百分比。
        /// 只是启发式，最后怎么解还得看 hex。
        /// </summary>
        private static void WriteSummary(string path, List<Ev> events, List<HidCollection> collections, string outDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine("HidProbe 摘要");
            sb.AppendLine("==================");
            sb.AppendLine();
            sb.AppendLine("机器: " + Environment.MachineName);
            sb.AppendLine("系统: " + Environment.OSVersion.VersionString);
            sb.AppendLine("输出: " + outDir);
            sb.AppendLine("集合: " + collections.Count + " 个，事件 " + events.Count + " 条");
            sb.AppendLine();

            sb.AppendLine("厂商私有集合:");
            foreach (var c in collections.Where(x => x.UsagePage >= 0xFF00))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  #{0:D3} {1:X4}:{2:X4} up={3:X4} usage={4:X4} in={5} out={6} feat={7}  {8}",
                    c.Index, c.Vid, c.Pid, c.UsagePage, c.Usage, c.InputLength, c.OutputLength, c.FeatureLength,
                    c.Product));
            sb.AppendLine();

            sb.AppendLine("收到过输入报告的集合:");
            foreach (var group in events.Where(e => e.Kind == "IN").GroupBy(e => e.Collection).OrderBy(g => g.Key))
            {
                var c = collections.FirstOrDefault(x => x.Index == group.Key);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  #{0:D3} {1:X4}:{2:X4} up={3:X4}  {4} 条，最长 {5}B",
                    group.Key, c?.Vid ?? 0, c?.Pid ?? 0, c?.UsagePage ?? 0,
                    group.Count(), group.Max(x => x.Length)));
            }
            sb.AppendLine();

            sb.AppendLine("应答里看起来像电量百分比的字节（1..100，启发式，别当结论）:");
            bool any = false;
            foreach (var e in events.Where(e => e.Kind == "IN" || e.Kind == "FEATURE").Take(4000))
            {
                if (e.Data == null || e.Data.Length < 5) continue;
                var hits = new List<string>();
                for (int i = 0; i < e.Data.Length && i < 64; i++)
                {
                    int v = e.Data[i];
                    if (v >= 1 && v <= 100)
                        hits.Add("[" + i + "]=" + v);
                }
                if (hits.Count == 0) continue;
                any = true;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  #{0:D3} +{1}ms {2,-7} {3}…  →  {4}",
                    e.Collection, e.Ms, e.Kind,
                    e.Hex.Substring(0, Math.Min(24, e.Hex.Length)),
                    string.Join(" ", hits.Take(10))));
            }
            if (!any) sb.AppendLine("  （没有）");
            sb.AppendLine();

            int failures = events.Count(e => e.Kind == "FAIL");
            sb.AppendLine("失败事件: " + failures + " 条" + (failures > 0 ? "（看 timeline.txt 的 err= 码）" : ""));
            sb.AppendLine();
            sb.AppendLine("下一步: 把 pairs.txt / timeline.txt 发回来，按应答的实际布局改适配器。");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }

    // ───────────────────────── 数据 ─────────────────────────

    internal sealed class HidCollection
    {
        public int Index;
        public string Path;
        public ushort Vid;
        public ushort Pid;
        public ushort Version;
        public ushort UsagePage;
        public ushort Usage;
        public int InputLength;
        public int OutputLength;
        public int FeatureLength;
        public string Product;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "#{0:D3} {1:X4}:{2:X4} up={3:X4}", Index, Vid, Pid, UsagePage);
    }

    internal sealed class Ev
    {
        public long Ms;
        public string Kind;
        public int Collection;
        public int Length;
        public string Hex;
        public string Note;
        public byte[] Data;
    }

    internal sealed class Recorder
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private readonly List<Ev> _events = new List<Ev>();
        private readonly object _gate = new object();

        public void Add(string kind, HidCollection c, byte[] data, string note)
        {
            var e = new Ev
            {
                Ms = Clock.ElapsedMilliseconds,
                Kind = kind,
                Collection = c?.Index ?? -1,
                Length = data?.Length ?? 0,
                Hex = data == null ? "" : Convert.ToHexString(data),
                Note = note ?? "",
                Data = data
            };
            lock (_gate)
            {
                if (_events.Count < 80000) _events.Add(e);
            }
        }

        public List<Ev> Snapshot()
        {
            lock (_gate) return new List<Ev>(_events);
        }
    }

    internal sealed class ProbeCommand
    {
        public string Name;
        public byte[] Frame;
        public bool Feature;
        public bool FeatureRead;
    }

    // ───────────────────────── 候选帧 ─────────────────────────

    internal static class CandidateFrames
    {
        public static List<ProbeCommand> Build(HidCollection c)
        {
            var list = new List<ProbeCommand>();
            bool matched = false;
            if (c.UsagePage >= 0xFF00)
            {
                switch (c.UsagePage)
                {
                    case 0xFF05:                       // ATK ZERO 的 BITMOUSE 控制集合（真机实测存在）
                        AddOutput(list, c, "ATK ZERO 身份 89 06", 0x08, 0x09, 0x72, 0x08, 0x00, 0x00, 0x89, 0x06);
                        AddOutput(list, c, "ATK ZERO 电量 07（接收器）", 0x08, 0x7D, 0x72, 0x02, 0x00, 0x01, 0x07, 0x01);
                        AddOutput(list, c, "ATK ZERO 电量 07（有线）", 0x08, 0x7E, 0x72, 0x02, 0x00, 0x00, 0x07, 0x01);
                        AddOutput(list, c, "ATK ZERO 充电 0F", 0x08, 0x7F, 0x72, 0x02, 0x00, 0x01, 0x0F, 0x01);
                        matched = true;
                        break;

                    case 0xFF1C:                       // Rainy 75 / VGN / ATK V75X 共用的状态集合
                        if (c.Usage == 0x0092)
                        {
                            AddOutput(list, c, "Rainy75/VGN 状态 04 20 00 1A 06", 0x04, 0x20, 0x00, 0x1A, 0x06);
                            AddOutput(list, c, "Rainy75/VGN 状态 00 20 00 1A 06", 0x00, 0x20, 0x00, 0x1A, 0x06);
                            AddOutput(list, c, "Rainy75/VGN 状态 04 20 00 1A 06 01", 0x04, 0x20, 0x00, 0x1A, 0x06, 0x01);
                            AddOutput(list, c, "VIA GET_DEVICE_INFO AA 10", 0x00, 0xAA, 0x10, 0x30, 0x00, 0x00, 0x00, 0x01);
                            matched = true;
                        }
                        break;

                    case 0xFF60:                       // 维咖 DriveAll / 狼蛛 / 前行者 的 33 或 65 字节集合
                        if (c.Usage == 0x0061)
                        {
                            AddOutput(list, c, "DriveAll 设备信息 AA 10 24", 0x00, 0xAA, 0x10, 0x18, 0x00, 0x00, 0x00, 0x00);
                            AddOutput(list, c, "DriveAll 设备信息 AA 10 48", 0x00, 0xAA, 0x10, 0x30, 0x00, 0x00, 0x00, 0x01);
                            AddOutput(list, c, "Rainy/VGN 状态 on FF60", 0x04, 0x20, 0x00, 0x1A, 0x06);
                            AddOutput(list, c, "Rainy/VGN 状态 on FF60 (report 0)", 0x00, 0x20, 0x00, 0x1A, 0x06);
                            matched = true;
                        }
                        break;

                    case 0xFFEF:                       // VGN 旧队列
                        AddOutput(list, c, "VGN 旧协议 04 20 00 1A 06", 0x04, 0x20, 0x00, 0x1A, 0x06);
                        AddOutput(list, c, "VGN 旧协议 00 20 00 1A 06", 0x00, 0x20, 0x00, 0x1A, 0x06);
                        matched = true;
                        break;

                    case 0xFFC0:                       // M HUB（迈从 / 部分 VGN）
                        AddOutput(list, c, "M HUB 11 F9 FF*19", BuildMHub());
                        matched = true;
                        break;

                    case 0xFF00:
                    case 0xFF01:
                        if (c.FeatureLength >= 64)
                        {
                            AddFeature(list, c, "ATK ZERO 电量 via feature", 0x08, 0x7D, 0x72, 0x02, 0x00, 0x01, 0x07, 0x01);
                            AddFeature(list, c, "ATK ZERO 身份 via feature", 0x08, 0x09, 0x72, 0x08, 0x00, 0x00, 0x89, 0x06);
                            matched = true;
                        }
                        break;
                }

                // 没命中任何已知布局的厂商集合，把两家最常见的只读状态查询各发一遍。
                // 设备不应答就是空跑，这类命令不改任何设置。
                if (!matched && !IsVirtualOrBuiltIn(c.Vid) && c.OutputLength is 33 or 64 or 65)
                {
                    AddOutput(list, c, "通用状态查询 04 20 00 1A 06", 0x04, 0x20, 0x00, 0x1A, 0x06);
                    AddOutput(list, c, "通用 VIA 设备信息 AA 10", 0x00, 0xAA, 0x10, 0x30, 0x00, 0x00, 0x00, 0x01);
                }
            }

            // 任何声明了 feature 报告的集合都读一次，不写不改。
            if (c.FeatureLength > 0 && c.FeatureLength <= 4096)
                list.Add(new ProbeCommand { Name = "GET_FEATURE 只读", FeatureRead = true });

            return list;
        }

        /// <summary>虚拟设备、触摸板、Intel 蓝牙 HID 不发帧，免得在系统组件上试协议。</summary>
        private static bool IsVirtualOrBuiltIn(ushort vid) => vid is 0xFFAA or 0x06CB or 0x8087 or 0x0000;

        private static void AddOutput(List<ProbeCommand> list, HidCollection c, string name, params byte[] payload)
        {
            int length = c.OutputLength;
            if (length <= 0 || length > 4096) return;
            list.Add(new ProbeCommand { Name = name, Frame = Fit(payload, length) });
        }

        private static void AddFeature(List<ProbeCommand> list, HidCollection c, string name, params byte[] payload)
        {
            int length = c.FeatureLength;
            if (length <= 0 || length > 4096) return;
            list.Add(new ProbeCommand { Name = name, Frame = Fit(payload, length), Feature = true });
        }

        private static byte[] Fit(byte[] payload, int length)
        {
            var frame = new byte[length];
            Array.Copy(payload, frame, Math.Min(payload.Length, length));
            return frame;
        }

        private static byte[] BuildMHub()
        {
            var frame = new byte[257];
            frame[0] = 0x11;
            frame[1] = 0xF9;
            for (int i = 2; i < 21; i++) frame[i] = 0xFF;
            return frame;
        }
    }

    // ───────────────────────── 命令行 ─────────────────────────

    internal sealed class Options
    {
        public int WindowSeconds = 15;
        public int HoldMs = 900;
        public bool Probe = true;
        public bool ReadOnly;
        public bool NoPause;
        public bool Help;
        public string OutputDirectory;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--seconds":
                    case "-s":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int s) && s > 0) o.WindowSeconds = s;
                        break;
                    case "--hold":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int h) && h > 0) o.HoldMs = h;
                        break;
                    case "--out":
                    case "-o":
                        if (i + 1 < args.Length) o.OutputDirectory = args[++i];
                        break;
                    case "--readonly":
                        o.ReadOnly = true;
                        o.Probe = false;
                        break;
                    case "--no-probe":
                        o.Probe = false;
                        break;
                    case "--no-pause":
                        o.NoPause = true;
                        break;
                    case "--help":
                    case "-h":
                        o.Help = true;
                        break;
                }
            }
            return o;
        }

        public static string ResolveOutputDirectory(string requested)
        {
            if (!string.IsNullOrWhiteSpace(requested)) return Path.GetFullPath(requested);

            string baseDir = AppContext.BaseDirectory;
            try
            {
                string probe = Path.Combine(baseDir, ".w" + Guid.NewGuid().ToString("N").Substring(0, 6));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            }
            catch
            {
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HidProbe");
            }

            return Path.Combine(baseDir, "HidProbe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        }

        public static void PrintUsage()
        {
            Console.WriteLine("HidProbe —— MouseDock 的 HID 电量抓取器");
            Console.WriteLine();
            Console.WriteLine("  HidProbe.exe                完整流程：枚举 → 监听窗口 → 主动查询");
            Console.WriteLine("  HidProbe.exe --seconds 30   监听窗口 30 秒");
            Console.WriteLine("  HidProbe.exe --readonly     只枚举 + 读 feature，不发任何帧");
            Console.WriteLine("  HidProbe.exe --out D:\\x     指定输出目录");
            Console.WriteLine("  HidProbe.exe --hold 1500    每条查询后等 1.5 秒");
            Console.WriteLine("  HidProbe.exe --no-pause     结束不等回车（批处理用）");
            Console.WriteLine();
            Console.WriteLine("输出: devices.txt / timeline.txt / events.jsonl / pairs.txt / summary.txt");
        }
    }

    // ───────────────────────── Win32 ─────────────────────────

    internal static class Native
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const int ERROR_IO_PENDING = 997;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int Size;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr h, IntPtr buffer, int length, IntPtr read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr h, IntPtr buffer, int length, IntPtr written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, IntPtr name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(IntPtr h, IntPtr overlapped, out uint transferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(IntPtr h, IntPtr overlapped);

        [DllImport("kernel32.dll")]
        public static extern bool ResetEvent(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out Guid guid);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int needed, IntPtr info);

        [DllImport("setupapi.dll")]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        public static extern bool HidD_GetProductString(IntPtr h, byte[] buffer, int length);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetFeature(IntPtr h, byte[] buffer, int length);

        [DllImport("hid.dll")]
        public static extern bool HidD_SetFeature(IntPtr h, byte[] buffer, int length);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr preparsed);

        [DllImport("hid.dll")]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsed);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsed, ref HIDP_CAPS caps);

        public static HIDP_CAPS Caps(IntPtr handle)
        {
            var caps = new HIDP_CAPS();
            if (HidD_GetPreparsedData(handle, out IntPtr preparsed))
            {
                HidP_GetCaps(preparsed, ref caps);
                HidD_FreePreparsedData(preparsed);
            }
            return caps;
        }

        public static string ProductString(IntPtr handle)
        {
            var buffer = new byte[256];
            if (!HidD_GetProductString(handle, buffer, buffer.Length)) return "";
            string text = Encoding.Unicode.GetString(buffer);
            int end = text.IndexOf('\0');
            return (end >= 0 ? text.Substring(0, end) : text).Trim();
        }

        public static IntPtr AllocOverlapped(IntPtr eventHandle)
        {
            IntPtr block = Marshal.AllocHGlobal(Marshal.SizeOf<System.Threading.NativeOverlapped>());
            Marshal.StructureToPtr(new System.Threading.NativeOverlapped { EventHandle = eventHandle }, block, false);
            return block;
        }

        /// <summary>读一条输入报告。超时返回 false，调用方继续下一轮。</summary>
        public static bool OverlappedRead(IntPtr handle, IntPtr eventHandle, int length, int timeoutMs, out byte[] data)
        {
            data = null;
            IntPtr buffer = Marshal.AllocHGlobal(length);
            IntPtr overlapped = AllocOverlapped(eventHandle);
            bool pending = false;
            try
            {
                ResetEvent(eventHandle);
                bool started = ReadFile(handle, buffer, length, IntPtr.Zero, overlapped);
                if (!started && Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return false;
                pending = true;

                if (WaitForSingleObject(eventHandle, (uint)timeoutMs) != 0) return false;
                if (!GetOverlappedResult(handle, overlapped, out uint transferred, false)) return false;
                pending = false;
                if (transferred == 0 || transferred > length) return false;

                data = new byte[transferred];
                Marshal.Copy(buffer, data, 0, (int)transferred);
                return true;
            }
            finally
            {
                if (pending)
                {
                    CancelIoEx(handle, overlapped);
                    GetOverlappedResult(handle, overlapped, out _, true);
                }
                Marshal.FreeHGlobal(overlapped);
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static bool OverlappedWrite(IntPtr handle, IntPtr eventHandle, byte[] frame, int timeoutMs)
        {
            IntPtr buffer = Marshal.AllocHGlobal(frame.Length);
            IntPtr overlapped = AllocOverlapped(eventHandle);
            bool pending = false;
            try
            {
                Marshal.Copy(frame, 0, buffer, frame.Length);
                ResetEvent(eventHandle);
                bool started = WriteFile(handle, buffer, frame.Length, IntPtr.Zero, overlapped);
                if (!started && Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return false;
                pending = true;

                if (WaitForSingleObject(eventHandle, (uint)timeoutMs) != 0) return false;
                if (!GetOverlappedResult(handle, overlapped, out uint transferred, false)) return false;
                pending = false;
                return transferred == frame.Length;
            }
            finally
            {
                if (pending)
                {
                    CancelIoEx(handle, overlapped);
                    GetOverlappedResult(handle, overlapped, out _, true);
                }
                Marshal.FreeHGlobal(overlapped);
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
