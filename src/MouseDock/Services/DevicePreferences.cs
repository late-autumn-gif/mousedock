using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using MouseDock.Models;

namespace MouseDock.Services
{
    /// <summary>Persistent device choices; readers only see a locked snapshot, never UI collections.</summary>
    public sealed class DevicePreferences
    {
        private readonly AppSettings _settings;
        private readonly object _gate = new object();
        private HashSet<string> _disabled;
        private int _generation;
        public int Generation { get { lock (_gate) return _generation; } }
        internal bool IsFor(AppSettings settings) => ReferenceEquals(_settings, settings);

        public DevicePreferences(AppSettings settings)
        {
            _settings = settings;
            settings.SelectedDeviceIds ??= new List<string>();
            settings.HiddenDeviceIds ??= new List<string>();
            settings.DisabledRecognitionDeviceIds ??= new List<string>();
            if (settings.HasDeviceSelection || settings.SelectedDeviceIds.Count > 0) settings.DeviceSelectionInitialized = true;
            _disabled = Set(settings.DisabledRecognitionDeviceIds);
        }

        private static HashSet<string> Set(IEnumerable<string> values) => new HashSet<string>(
            values.Where(v => !string.IsNullOrWhiteSpace(v)), StringComparer.OrdinalIgnoreCase);
        public static string[] Keys(DeviceInfo device) => new[] { device.Id }
            .Concat(device.DetectionIds ?? Array.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public bool CanRead(params string[] keys)
        {
            lock (_gate) return !keys.Any(k => k != null && _disabled.Contains(k));
        }

        public void Apply(DeviceInfo device)
        {
            string[] keys = Keys(device);
            device.RecognitionEnabled = CanRead(keys);
            device.Hidden = _settings.HiddenDeviceIds.Any(id => keys.Contains(id, StringComparer.OrdinalIgnoreCase));
            device.Selected = device.RecognitionEnabled && !device.Hidden &&
                (!_settings.DeviceSelectionInitialized || _settings.SelectedDeviceIds.Any(id => keys.Contains(id, StringComparer.OrdinalIgnoreCase)));
        }

        internal bool MigrateLegacyIds(IEnumerable<DeviceInfo> devices)
        {
            bool changed = false;
            lock (_gate)
            {
                foreach (var group in devices.Where(d => d.Connected)
                    .GroupBy(LegacyId, StringComparer.OrdinalIgnoreCase).Where(g => g.Key != null))
                {
                    var ids = group.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    foreach (var choices in new[] { _settings.SelectedDeviceIds, _settings.HiddenDeviceIds, _settings.DisabledRecognitionDeviceIds })
                    {
                        // Never guess a display selection; exclusions must stay effective and individually reversible.
                        if (ids.Length != 1 && ReferenceEquals(choices, _settings.SelectedDeviceIds)) continue;
                        if (choices.RemoveAll(id => string.Equals(id, group.Key, StringComparison.OrdinalIgnoreCase)) == 0) continue;
                        foreach (string id in ids)
                            if (!choices.Contains(id, StringComparer.OrdinalIgnoreCase)) choices.Add(id);
                        changed = true;
                    }
                }
                if (changed)
                {
                    _disabled = Set(_settings.DisabledRecognitionDeviceIds);
                    _generation++;
                }
            }
            return changed;
        }

        private static string LegacyId(DeviceInfo device)
        {
            if (!device.Id.StartsWith("instance-", StringComparison.OrdinalIgnoreCase)) return null;
            var parts = (device.Detail ?? "").Split(':');
            if (parts.Length != 2 || parts[0].Length != 4 || parts[1].Length != 4 ||
                !ushort.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort vid) ||
                !ushort.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort pid))
                return null;
            return $"hid-{vid:X4}-{pid:X4}";
        }

        public void CaptureSelection(IEnumerable<DeviceInfo> devices)
        {
            var selected = Set(_settings.SelectedDeviceIds);
            foreach (var device in devices)
            {
                string[] keys = Keys(device);
                selected.ExceptWith(keys);
                string legacy = LegacyId(device);
                if (legacy != null) selected.Remove(legacy);
                if (device.Selected) selected.UnionWith(keys);
            }
            _settings.SelectedDeviceIds = selected.ToList();
            _settings.DeviceSelectionInitialized = true; // An empty explicit selection stays empty.
        }

        public void Hide(DeviceInfo device)
        {
            var hidden = Set(_settings.HiddenDeviceIds);
            hidden.UnionWith(Keys(device));
            _settings.HiddenDeviceIds = hidden.ToList();
        }

        public void Disable(DeviceInfo device)
        {
            lock (_gate)
            {
                _disabled.UnionWith(Keys(device));
                _settings.DisabledRecognitionDeviceIds = _disabled.ToList();
                _generation++;
            }
        }

        public bool Enable(DeviceInfo device, bool forceRefresh = false)
        {
            string[] keys = Keys(device);
            _settings.HiddenDeviceIds.RemoveAll(k => keys.Contains(k, StringComparer.OrdinalIgnoreCase));
            lock (_gate)
            {
                bool changed = _disabled.RemoveWhere(k => keys.Contains(k, StringComparer.OrdinalIgnoreCase)) > 0;
                _settings.DisabledRecognitionDeviceIds = _disabled.ToList();
                if (changed || forceRefresh) _generation++;
                return changed || forceRefresh;
            }
        }
    }
}
