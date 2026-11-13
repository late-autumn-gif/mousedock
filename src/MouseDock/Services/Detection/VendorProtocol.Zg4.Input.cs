using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal sealed class Zg4InputLink : ReferenceBatteryProtocol.ILink, IDisposable
    {
        private sealed class Input
        {
            internal readonly ReferenceBatteryProtocol.ILink Link;
            internal readonly byte Id;
            private IntPtr handle, signal;

            internal Input(ReferenceBatteryProtocol.ILink link, byte id,
                IntPtr handle = default, IntPtr signal = default)
            {
                Link = link;
                Id = id;
                this.handle = handle;
                this.signal = signal;
            }

            internal void Close()
            {
                if (signal != IntPtr.Zero) { CloseHandle(signal); signal = IntPtr.Zero; }
                if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
            }
        }

        private readonly ReferenceBatteryProtocol.ILink primary;
        private readonly List<Input> inputs = new();
        private readonly HashSet<byte> inputIds = new();
        private int nextInput;
        private bool disposed;

        internal IReadOnlySet<byte> InputIds => inputIds;

        internal Zg4InputLink(ReferenceBatteryProtocol.ILink primary, byte primaryInputId,
            HidDiscovery.VendorCollectionInfo primaryChannel,
            IReadOnlyList<HidDiscovery.VendorCollectionInfo> channels, string instance,
            IReadOnlyDictionary<string, string> pathsByInstance, BatteryReplay.Trace trace)
        {
            this.primary = primary ?? throw new ArgumentNullException(nameof(primary));
            bool MatchesInstance(string path) => pathsByInstance != null &&
                pathsByInstance.TryGetValue(path ?? "", out string owner) &&
                string.Equals(owner, instance, StringComparison.OrdinalIgnoreCase);
            if (primaryChannel == null || string.IsNullOrWhiteSpace(instance) ||
                !IsZg4Channel(primaryChannel.Vid, primaryChannel.Pid, primaryChannel.Product,
                    primaryChannel.UsagePage, primaryChannel.Input, primaryChannel.Output) ||
                !MatchesInstance(primaryChannel.Path))
                throw new IOException("ZG4 input collection or physical instance does not match");

            inputs.Add(new(primary, primaryInputId));
            inputIds.Add(primaryInputId);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primaryChannel.Path };
            try
            {
                if (channels == null) return;
                foreach (var channel in channels)
                {
                    if (channel == null || string.IsNullOrWhiteSpace(channel.Path) || !paths.Add(channel.Path) ||
                        channel.Vid != primaryChannel.Vid || channel.Pid != primaryChannel.Pid ||
                        !string.Equals(channel.Product?.Trim(), primaryChannel.Product?.Trim(),
                            StringComparison.OrdinalIgnoreCase) ||
                        !IsZg4Channel(channel.Vid, channel.Pid, channel.Product, channel.UsagePage,
                            channel.Input, channel.Output) ||
                        !MatchesInstance(channel.Path))
                        continue;
                    OpenInput(channel, trace);
                }
            }
            catch { Dispose(); throw; }
        }

        private void OpenInput(HidDiscovery.VendorCollectionInfo channel, BatteryReplay.Trace trace)
        {
            IntPtr handle = IntPtr.Zero, signal = IntPtr.Zero;
            try
            {
                handle = CreateFile(channel.Path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                {
                    handle = IntPtr.Zero;
                    return;
                }

                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(handle, ref attributes) ||
                    attributes.VendorID != channel.Vid || attributes.ProductID != channel.Pid)
                    return;
                var caps = Caps(handle);
                string product = HidDiscovery.ReadHidProduct(handle, channel.Path);
                if (!string.Equals(product?.Trim(), channel.Product?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    !IsZg4Channel(attributes.VendorID, attributes.ProductID, product, caps.UsagePage,
                        caps.InputReportByteLength, caps.OutputReportByteLength) ||
                    caps.UsagePage != channel.UsagePage || caps.Usage != channel.Usage ||
                    caps.InputReportByteLength != channel.Input || caps.OutputReportByteLength != channel.Output ||
                    caps.FeatureReportByteLength != channel.Feature)
                    return;
                if (!HidD_GetPreparsedData(handle, out IntPtr prep)) return;
                byte inputId;
                try
                {
                    if (!TryGetReportId(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps,
                            out inputId) || inputIds.Contains(inputId))
                        return;
                }
                finally { HidD_FreePreparsedData(prep); }

                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                if (signal == IntPtr.Zero || !HidD_FlushQueue(handle)) return;
                var input = new Input(new Zg4HidLink(handle, signal, caps.InputReportByteLength), inputId,
                    handle, signal);
                inputs.Add(input);
                handle = signal = IntPtr.Zero;
                inputIds.Add(inputId);
                HidInventory.ParsePath(channel.Path, out _, out _, out string label);
                trace?.Add("zg4-input-open", true, inputId, result: label);
            }
            catch
            {
                // An unavailable auxiliary collection does not prevent other inputs from being used.
            }
            finally
            {
                if (signal != IntPtr.Zero) CloseHandle(signal);
                if (handle != IntPtr.Zero) CloseHandle(handle);
            }
        }

        public long Now => primary.Now;

        public byte[] Read(int timeoutMs)
        {
            ThrowIfDisposed();
            long deadline = Environment.TickCount64 + Math.Max(0, timeoutMs);
            while (Environment.TickCount64 < deadline)
            {
                int timeout = (int)Math.Min(20, deadline - Environment.TickCount64);
                if (timeout <= 0) break;
                var input = inputs[nextInput];
                nextInput = (nextInput + 1) % inputs.Count;
                byte[] report = input.Link.Read(timeout);
                if (report is { Length: > 0 } && report[0] == input.Id) return report;
            }
            return null;
        }

        public bool Write(byte[] request, int timeoutMs)
        {
            ThrowIfDisposed();
            return primary.Write(request, timeoutMs);
        }

        public bool SetFeature(byte[] request)
        {
            ThrowIfDisposed();
            return primary.SetFeature(request);
        }

        public byte[] GetFeature(byte reportId, int length)
        {
            ThrowIfDisposed();
            return primary.GetFeature(reportId, length);
        }

        public void Delay(int milliseconds)
        {
            ThrowIfDisposed();
            primary.Delay(milliseconds);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(Zg4InputLink));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var input in inputs) input.Close();
        }
    }
}
