using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MouseDock.Services;
using MouseDock.Views;
using NAudio.CoreAudioApi;
using Forms = System.Windows.Forms;

namespace MouseDock;

public partial class App
{
    internal static ContextMenu CreateThemedMenu(FrameworkElement target, bool themeBackground = false)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };
        menu.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/MouseDock;component/Views/DesktopPetMenu.xaml", UriKind.Relative)
        });
        ApplyCompactMenuTheme(menu, themeBackground);
        return menu;
    }

    internal static MenuItem MenuAction(string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action();
        return item;
    }

    private static readonly (string Mode, string Label)[] AppearanceModes =
    [
        ("light", "浅色（日间）"), ("dark", "深色（夜间）"), ("theme", "跟随主题")
    ];

    internal static void SetAppearanceMode(string mode)
    {
        mode = Palette.NormalizeThemeMode(mode);
        if (Settings.AppearanceMode == mode) return;
        Settings.AppearanceMode = mode;
        RefreshTheme();
        SettingsView?.SyncThemeOptions();
        SaveSettings();
    }

    internal static MenuItem CreateAppearanceMenu()
    {
        var menu = new MenuItem { Header = "外观模式" };
        var selected = Palette.NormalizeThemeMode(Settings.AppearanceMode);
        foreach (var (mode, label) in AppearanceModes)
        {
            var item = MenuAction(label, () => SetAppearanceMode(mode));
            item.IsChecked = selected == mode;
            menu.Items.Add(item);
        }
        return menu;
    }

    private static Forms.ToolStripMenuItem CreateTrayAppearanceMenu()
    {
        var menu = new Forms.ToolStripMenuItem("外观模式");
        foreach (var (mode, label) in AppearanceModes)
        {
            var item = new Forms.ToolStripMenuItem(label) { Tag = mode };
            item.Click += (_, _) => SetAppearanceMode(mode);
            menu.DropDownItems.Add(item);
        }
        menu.DropDownOpening += (_, _) =>
        {
            var selected = Palette.NormalizeThemeMode(Settings.AppearanceMode);
            foreach (Forms.ToolStripMenuItem item in menu.DropDownItems)
                item.Checked = Equals(item.Tag, selected);
        };
        return menu;
    }

    internal static void RescanDevices()
    {
        // AutoDetectSource enumerates current HID, system and Bluetooth endpoints on
        // every poll. Use that pipeline (including the in-flight scan queue) without
        // touching selection, cached readings or the hotplug debounce.
        Battery?.Poll();
    }

    internal static void AddDeviceMenuActions(ItemCollection items, Window owner, bool includeRescan = true)
    {
        if (includeRescan)
        {
            var scan = MenuAction("刷新设备", RescanDevices);
            scan.ToolTip = "重新扫描当前连接的设备，保留显示与识别设置";
            items.Add(scan);
        }
        items.Add(CreateAudioSubmenu(DataFlow.Capture, owner));
        items.Add(CreateAudioSubmenu(DataFlow.Render, owner));
    }

    private static string AudioMenuTitle(DataFlow flow) => flow == DataFlow.Capture ? "切换输入设备" : "切换输出设备";

    internal static MenuItem CreateAudioSubmenu(DataFlow flow, Window owner,
        Func<DataFlow, WindowsAudioRouting.Snapshot> list = null, Func<DataFlow, string, AudioActionResult> apply = null)
    {
        list ??= WindowsAudioRouting.List;
        apply ??= WindowsAudioRouting.Switch;
        var menu = new MenuItem { Header = AudioMenuTitle(flow), ToolTip = "切换 Windows 默认设备" };
        static MenuItem Status(string text) => new() { Header = text, IsEnabled = false };
        menu.Items.Add(Status("正在读取 Windows 音频设备…"));
        int request = 0;
        menu.SubmenuOpened += async (_, e) =>
        {
            if (!ReferenceEquals(e.Source, menu)) return;
            int current = ++request;
            menu.Items.Clear();
            menu.Items.Add(Status("正在读取 Windows 音频设备…"));
            var snapshot = await Task.Run(() => list(flow));
            if (!menu.IsSubmenuOpen || current != request) return;

            menu.Items.Clear();
            if (snapshot.Devices.Length == 0)
                menu.Items.Add(Status(snapshot.Error ?? "没有可用设备"));
            else if (snapshot.DefaultId == null) menu.Items.Add(Status("当前无默认设备"));
            foreach (var device in snapshot.Devices)
            {
                bool selected = WindowsAudioRouting.IsSame(device.Id, snapshot.DefaultId);
                string title = device.Name + (selected ? " · 当前默认" : "");
                var option = MenuAction(title, async () => await SwitchAudio(flow, device.Id, owner, apply));
                option.IsChecked = selected;
                menu.Items.Add(option);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuAction("打开 Windows 声音设置…", OpenSoundSettings));
        };
        return menu;
    }

    private static Forms.ToolStripMenuItem CreateTrayAudioSubmenu(DataFlow flow)
    {
        var menu = new Forms.ToolStripMenuItem(AudioMenuTitle(flow));
        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("正在读取 Windows 音频设备…") { Enabled = false });
        int request = 0;
        menu.DropDownOpened += async (_, _) =>
        {
            int current = ++request;
            ClearItems();
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem("正在读取 Windows 音频设备…") { Enabled = false });
            var snapshot = await Task.Run(() => WindowsAudioRouting.List(flow));
            if (menu.IsDisposed || !menu.DropDown.Visible || current != request) return;
            ClearItems();
            if (snapshot.Devices.Length == 0)
                menu.DropDownItems.Add(new Forms.ToolStripMenuItem(snapshot.Error ?? "没有可用设备") { Enabled = false });
            else if (snapshot.DefaultId == null)
                menu.DropDownItems.Add(new Forms.ToolStripMenuItem("当前无默认设备") { Enabled = false });
            foreach (var device in snapshot.Devices)
            {
                bool selected = WindowsAudioRouting.IsSame(device.Id, snapshot.DefaultId);
                var option = new Forms.ToolStripMenuItem(device.Name + (selected ? " · 当前默认" : "")) { Checked = selected };
                option.Click += async (_, _) => await SwitchAudio(flow, device.Id, null, WindowsAudioRouting.Switch);
                menu.DropDownItems.Add(option);
            }
            menu.DropDownItems.Add(new Forms.ToolStripSeparator());
            menu.DropDownItems.Add("打开 Windows 声音设置…", null, (_, _) => OpenSoundSettings());
        };
        return menu;

        void ClearItems()
        {
            while (menu.DropDownItems.Count > 0) menu.DropDownItems[0].Dispose();
        }
    }

    private static async Task SwitchAudio(DataFlow flow, string id, Window owner, Func<DataFlow, string, AudioActionResult> apply)
    {
        var result = await Task.Run(() => apply(flow, id));
        if (result.Success) return;
        if (owner?.IsVisible == true)
            MessageBox.Show(owner, result.Message, AudioMenuTitle(flow), MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(result.Message, AudioMenuTitle(flow), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void OpenSoundSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch { MessageBox.Show("请在 Windows 设置中打开「系统 → 声音」。", "声音设置"); }
    }

    internal static void ApplyCharacterEdit(CharacterEditorWindow editor)
    {
        editor.ApplyTo(Settings);
        if (editor.SurfaceMode == "icon") Icon?.ApplyStyle(Settings.IconStyle, false);
        if (editor.ShowAfterApply) SetSurface(editor.SurfaceMode);
        RefreshDeviceViews();
        SaveSettings();
        SettingsView?.Reload();
    }

    internal static void SetBatteryRingStyle(bool continuous)
    {
        if (Settings.ContinuousBatteryRing == continuous) return;
        Settings.ContinuousBatteryRing = continuous;
        Icon?.UpdateValue();
        Taskbar?.UpdateDevices();
        Edge?.UpdateDevices();
        SaveSettings();
    }
}
