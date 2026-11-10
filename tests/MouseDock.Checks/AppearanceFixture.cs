using System.IO;
using System.Reflection;
using System.Windows;
using MouseDock.Models;
using MouseDock.Services;

internal sealed class AppearanceFixture : IDisposable
{
    private readonly PropertyInfo[] _properties;
    private readonly object?[] _previous;
    private readonly PropertyInfo _directory;
    private readonly object? _oldDirectory;
    internal AppSettings Settings { get; } = new() { Provider = "json", JsonPath = "missing-appearance-fixture.json", IconStyle = "ring" };
    internal BatteryService Battery { get; }
    internal AppearanceFixture()
    {
        string[] names = ["Settings", "Theme", "Battery", "Dock", "Mini", "Icon", "Taskbar", "Edge", "SettingsView"];
        _properties = names.Select(n => typeof(MouseDock.App).GetProperty(n)!).ToArray();
        _previous = _properties.Select(p => p.GetValue(null)).ToArray();
        _directory = typeof(SettingsStore).GetProperty("TestDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        _oldDirectory = _directory.GetValue(null);
        _directory.SetValue(null, Path.GetFullPath("artifacts/reference-interaction/settings/" + Guid.NewGuid().ToString("N")));
        foreach (var property in _properties) property.SetValue(null, null);
        Set("Settings", Settings);
        Set("Theme", ThemePreset.ById("dusk"));
        Battery = (BatteryService)Activator.CreateInstance(typeof(BatteryService), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [Settings, null, false], null)!;
        Set("Battery", Battery);
        Battery.VisibleDevices.Add(new DeviceInfo { Id = "mouse-fixture", Name = "Mouse fixture", Level = 76 });
    }
    internal void Set(string name, object value) => _properties.Single(p => p.Name == name).SetValue(null, value);
    public void Dispose()
    {
        foreach (var property in _properties)
            if (property.GetValue(null) is Window window) window.Close();
        Battery.Dispose();
        for (int i = 0; i < _properties.Length; i++) _properties[i].SetValue(null, _previous[i]);
        _directory.SetValue(null, _oldDirectory);
    }
}
