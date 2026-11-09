using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using MouseDock.Models;

namespace MouseDock.Services
{
    public static class SettingsStore
    {
        internal static string TestDirectory { get; set; }
        public static string Dir => TestDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseDock");

        public static string FilePath => Path.Combine(Dir, "settings.json");

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            // 窗口位置默认是 double.NaN，Strict 模式会直接抛异常，必须放宽
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json, Options);
                    if (s != null)
                    {
                        s.SelectedDeviceIds ??= new System.Collections.Generic.List<string>();
                        s.HiddenDeviceIds ??= new System.Collections.Generic.List<string>();
                        s.DisabledRecognitionDeviceIds ??= new System.Collections.Generic.List<string>();
                        return s;
                    }
                }
            }
            catch { /* 配置损坏时回落到默认值 */ }
            return new AppSettings();
        }

        internal static bool RestoreStartupUpdates(AppSettings settings)
        {
            if (settings.Version >= 7) return false;
            settings.AutoUpdate = true;
            settings.Version = 7;
            return true;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
            }
            catch { /* 磁盘不可写时忽略，不影响运行 */ }
        }
    }

    /// <summary>开机自启（HKCU Run）。</summary>
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MouseDock";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (key == null) return;
                if (enabled)
                    key.SetValue(ValueName, "\"" + Environment.ProcessPath + "\" --tray");
                else
                    key.DeleteValue(ValueName, false);
            }
            catch { /* 无权限时静默失败 */ }
        }

        /// <summary>
        /// 每次启动时把 Run 项对齐到「当前 exe + 设置里的开关」。
        ///
        /// 绿色版解压到哪就是哪，用户可能整个文件夹搬走、改名、或者上次更新把路径换了，
        /// 这时旧值会指向一个不存在的文件，开机自然什么都不会发生。这里每次启动都校正一次，
        /// 让入口永远指向正在运行的这个 exe。开关关掉时则确保值被删干净。
        /// </summary>
        public static void Sync(bool enabled)
        {
            try
            {
                string current = Environment.ProcessPath;
                if (string.IsNullOrEmpty(current) || !File.Exists(current)) return;

                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (key == null) return;

                if (!enabled)
                {
                    if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                    return;
                }

                string want = "\"" + current + "\" --tray";
                string existing = key.GetValue(ValueName) as string;
                if (string.Equals(existing, want, StringComparison.OrdinalIgnoreCase)) return;

                // 值不存在、路径变了、或者旧值指向的文件已经不在，都重写。
                key.SetValue(ValueName, want);
            }
            catch { /* 无权限时静默失败，不影响主程序 */ }
        }
    }
}
