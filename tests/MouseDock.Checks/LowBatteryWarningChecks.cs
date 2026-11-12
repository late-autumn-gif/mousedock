using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

internal static class LowBatteryWarningChecks
{
    internal static void DisabledOption()
    {
        using var fixture = new AppearanceFixture();
        var window = new SettingsWindow();
        fixture.Set("SettingsView", window);
        var disabled = window.FindName("Warn0") as Button;
        Require(disabled != null, "Low battery reminders have no off option.");
        var enabled = (Button)window.FindName("Warn20");
        disabled!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(fixture.Settings.LowBatteryWarn == 0 &&
            SettingsStore.Load().LowBatteryWarn == 0 && fixture.Settings.Clone().LowBatteryWarn == 0,
            "The off option does not persist a disabled reminder threshold.");
        window.Reload();
        Require(disabled.FontWeight == FontWeights.SemiBold && enabled.FontWeight == FontWeights.Normal,
            "Reload does not retain the selected off option.");

        var warned = (HashSet<string>)typeof(MouseDock.App)
            .GetField("_lowWarned", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var previous = warned.ToArray();
        var check = typeof(MouseDock.App).GetMethod("CheckLowBattery", BindingFlags.NonPublic | BindingFlags.Static)!;
        fixture.Battery.Devices.Add(new DeviceInfo { Id = "empty-battery", Name = "Empty battery",
            Connected = true, Level = 0 });
        try
        {
            warned.Clear();
            check.Invoke(null, null);
            Require(warned.Count == 0, "Disabled reminders still trigger for an empty battery.");
            enabled.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check.Invoke(null, null);
            Require(fixture.Settings.LowBatteryWarn == 20 && warned.Contains("empty-battery") &&
                enabled.FontWeight == FontWeights.SemiBold && disabled.FontWeight == FontWeights.Normal,
                "Selecting a normal threshold does not restore reminders and selection.");
        }
        finally
        {
            warned.Clear();
            foreach (var id in previous) warned.Add(id);
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
