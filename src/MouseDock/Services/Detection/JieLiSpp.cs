using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 杰理（JieLi）方案的私有 SPP 通道。
    ///
    /// 这台 Air Plus Pro3 暴露了 BTHENUM\{FE010000-1234-5678-ABCD-00805F9B34FB}，节点名 "JL_SPP"，
    /// 另外还有一个标准 SPP 串口 COM4（00001101）。这两条都不受 Windows 音频栈占用，
    /// 和 HFP 那条被独占到连不上完全不是一回事。
    /// </summary>
    internal static class JieLiSpp
    {
        /// <summary>杰理私有 SPP 服务 UUID（从设备节点名反推，实测存在于 BTHENUM 枚举）。</summary>
        public static readonly Guid JlSppUuid = new Guid("FE010000-1234-5678-ABCD-00805F9B34FB");

        private static readonly Guid StandardSpp = new Guid("00001101-0000-1000-8000-00805F9B34FB");

        public sealed class Result
        {
            public bool Connected;
            public string Target = "";
            public List<string> Trace = new();
            public List<string> Replies = new();
            public string Error = "";
        }

        public static Result Probe(string nameHint, IEnumerable<byte[]> commands, int perCmdMs = 800, int totalMs = 20000)
        {
            try { return ProbeAsync(nameHint, commands, perCmdMs, totalMs).GetAwaiter().GetResult(); }
            catch (Exception ex) { return new Result { Error = ex.GetType().Name + ": " + ex.Message }; }
        }

        private static async Task<Result> ProbeAsync(string nameHint, IEnumerable<byte[]> commands, int perCmdMs, int totalMs)
        {
            var result = new Result();

            var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector()).AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(2000));

            var target = infos.FirstOrDefault(d => !string.IsNullOrEmpty(d.Name) &&
                                                   d.Name.Contains(nameHint, StringComparison.OrdinalIgnoreCase));
            if (target == null) { result.Error = "找不到设备 " + nameHint; return result; }
            result.Target = target.Name;

            var device = await BluetoothDevice.FromIdAsync(target.Id).AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(2000));
            if (device == null) { result.Error = "FromIdAsync 返回 null"; return result; }
            if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
            {
                result.Error = "设备未连接: " + device.ConnectionStatus;
                return result;
            }

            var services = await device.GetRfcommServicesAsync().AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(2500));

            result.Trace.Add($"RFCOMM 服务数: {services.Services.Count}");
            foreach (var s in services.Services)
                result.Trace.Add($"   {s.ServiceId.Uuid}  {s.ConnectionServiceName}");

            // 先找杰理私有 SPP，找不到再退回标准 SPP
            var service = services.Services.FirstOrDefault(s => s.ServiceId.Uuid == JlSppUuid)
                       ?? services.Services.FirstOrDefault(s => s.ServiceId.Uuid == StandardSpp);

            if (service == null)
            {
                result.Error = "没有 JL_SPP / 标准 SPP 服务";
                return result;
            }

            result.Trace.Add($"选中服务: {service.ServiceId.Uuid}");

            using var socket = new StreamSocket();
            try
            {
                await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName,
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask()
                    .WaitAsync(TimeSpan.FromMilliseconds(5000));
            }
            catch (Exception ex)
            {
                result.Error = "连接失败: " + ex.Message;
                return result;
            }

            result.Connected = true;
            result.Trace.Add("已连接");

            var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            var writer = new DataWriter(socket.OutputStream);
            var deadline = DateTime.UtcNow.AddMilliseconds(totalMs);

            async Task Drain(int ms, string label)
            {
                var stop = DateTime.UtcNow.AddMilliseconds(ms);
                while (DateTime.UtcNow < stop && DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var load = reader.LoadAsync(256).AsTask();
                        if (await Task.WhenAny(load, Task.Delay(120)) != load) continue;
                        uint n = await load;
                        if (n == 0) continue;
                        var buf = new byte[n];
                        reader.ReadBytes(buf);
                        string text = Encoding.ASCII.GetString(buf);
                        string hex = BitConverter.ToString(buf);
                        result.Replies.Add($"{label}  hex={hex}  ascii={Printable(text)}");
                    }
                    catch (Exception ex)
                    {
                        result.Replies.Add($"{label} 读取异常: {ex.GetType().Name}");
                        break;
                    }
                }
            }

            // 连上先被动听一会儿，设备可能主动上报
            await Drain(1200, "[主动]");

            foreach (var cmd in commands)
            {
                if (DateTime.UtcNow >= deadline) break;

                try
                {
                    writer.WriteBytes(cmd);
                    await writer.StoreAsync().AsTask();
                    await writer.FlushAsync().AsTask();
                }
                catch (Exception ex)
                {
                    result.Replies.Add($"写 {BitConverter.ToString(cmd)} 失败: {ex.Message}");
                    break;
                }

                await Drain(perCmdMs, $"[发 {BitConverter.ToString(cmd)}]");
            }

            try { socket.Dispose(); } catch { }
            return result;
        }

        private static string Printable(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) sb.Append(c >= 0x20 && c < 0x7F ? c : '.');
            return sb.ToString();
        }
    }
}
