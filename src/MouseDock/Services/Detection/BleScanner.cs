using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// BLE 广播扫描：只被动收广播包，不配对、不连接。
    /// 用来把厂商自定义数据原样打出来，给 AirPods / TWS 这类私有广播做逆向取样。
    /// </summary>
    public static class BleScanner
    {
        public sealed class Hit
        {
            public ulong Address;
            public string LocalName = "";
            public int Rssi;
            public List<(ushort CompanyId, byte[] Data)> Manufacturer = new();
            public List<byte[]> Sections = new();
            public DateTime FirstSeen = DateTime.Now;
            public int Count;
        }

        private static byte[] ToBytes(IBuffer buffer)
        {
            if (buffer == null) return Array.Empty<byte>();
            var bytes = new byte[buffer.Length];
            using var reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(bytes);
            return bytes;
        }

        /// <summary>扫描若干秒，按地址聚合重复广播。</summary>
        public static List<Hit> Scan(int seconds, IEnumerable<ushort> onlyCompanies = null)
        {
            var map = new Dictionary<ulong, Hit>();
            var filter = onlyCompanies == null ? null : new HashSet<ushort>(onlyCompanies);

            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            watcher.Received += (_, e) =>
            {
                try
                {
                    if (!map.TryGetValue(e.BluetoothAddress, out var hit))
                    {
                        hit = new Hit { Address = e.BluetoothAddress, FirstSeen = DateTime.Now };
                        map[e.BluetoothAddress] = hit;
                    }

                    hit.Count++;
                    hit.Rssi = e.RawSignalStrengthInDBm;
                    if (!string.IsNullOrEmpty(e.Advertisement.LocalName))
                        hit.LocalName = e.Advertisement.LocalName;

                    foreach (var md in e.Advertisement.ManufacturerData)
                    {
                        if (filter != null && !filter.Contains(md.CompanyId)) continue;
                        var bytes = ToBytes(md.Data);
                        if (!hit.Manufacturer.Any(m => m.CompanyId == md.CompanyId))
                            hit.Manufacturer.Add((md.CompanyId, bytes));
                    }

                    foreach (var section in e.Advertisement.DataSections)
                    {
                        var bytes = ToBytes(section.Data);
                        if (!hit.Sections.Any(s => s.Length == bytes.Length && s.SequenceEqual(bytes)))
                            hit.Sections.Add(bytes);
                    }
                }
                catch { }
            };

            watcher.Start();
            Thread.Sleep(Math.Max(1, seconds) * 1000);
            try { watcher.Stop(); } catch { }

            return map.Values
                .OrderByDescending(h => h.Count)
                .ThenBy(h => h.Address)
                .ToList();
        }

        public static string Describe(List<Hit> hits)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"BLE 广播样本：{hits.Count} 台设备（{DateTime.Now:yyyy-MM-dd HH:mm:ss}）");
            sb.AppendLine();

            foreach (var h in hits)
            {
                sb.AppendLine($"── {h.Address:X12}  rssi={h.Rssi}dBm  包数={h.Count}  名称=\"{h.LocalName}\"");

                foreach (var (companyId, data) in h.Manufacturer)
                {
                    string tag = companyId switch
                    {
                        0x004C => "Apple",
                        0x0006 => "Microsoft",
                        0x0075 => "Samsung",
                        0x0087 => "Garmin",
                        0x00E0 => "Google",
                        0x0157 => "Anker/Soundcore",
                        0x027D => "Huawei",
                        0x02E5 => "Edifier",
                        0x038F => "Xiaomi",
                        _ => "?"
                    };
                    sb.AppendLine($"   厂商 0x{companyId:X4} ({tag})  长度={data.Length}  {BitConverter.ToString(data)}");
                    sb.AppendLine($"     ASCII: {Printable(data)}");

                    if (companyId == 0x004C)
                        sb.AppendLine($"     Apple 首字节 0x{(data.Length > 0 ? data[0] : 0):X2} (0x19=邻近配对)");
                }

                foreach (var s in h.Sections)
                    sb.AppendLine($"   数据段 长度={s.Length}  {BitConverter.ToString(s)}");

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string Printable(byte[] data)
        {
            var sb = new StringBuilder(data.Length);
            foreach (var b in data)
                sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            return sb.ToString();
        }
    }
}
