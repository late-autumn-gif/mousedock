using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using MouseDock.Models;

namespace MouseDock.Services
{
    public static class SettingsStore
    {
        public static string Dir => Path.Combine(
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
                        return s;
                    }
                }
            }
            catch { /* 配置损坏时回落到默认值 */ }
            return new AppSettings();
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
    }
}
