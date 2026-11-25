using System;
using System.Windows;
using System.Linq;
using MouseDock.Models;
using System.Windows.Controls;
using MouseDock.Services;

namespace MouseDock.Views;

internal static class AdaptationPrompt
{
    internal static FeedbackService.PhaseNote Show(Window owner, int index, DeviceInfo initial, string model, out DeviceInfo target)
    {
        string[] stages = { "正常使用", "接入充电线后", "再次拔线后" };
        var panel = new StackPanel { Margin = new Thickness(20) };
        var dialog = new Window
        {
            Title = $"适配新设备 · {index + 1}/3 · {stages[index]}", Width = 470,
            SizeToContent = SizeToContent.Height, Content = panel, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        if (owner?.IsVisible == true)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        void Label(string text) => panel.Children.Add(new TextBlock
        { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 3) });
        ComboBox Choice(string[] choices)
        {
            var box = new ComboBox { ItemsSource = choices, SelectedIndex = 0 };
            panel.Children.Add(box);
            return box;
        }

        target = initial;
        ComboBox endpoint = null;
        if (initial != null)
        {
            Label("本阶段目标设备（插线后若切换接口，请选择新出现的设备）");
            endpoint = new ComboBox { DisplayMemberPath = "Name" };
            endpoint.Items.Add(initial);
            if (App.Battery != null)
                foreach (var device in App.Battery.Devices.Where(d => d.Id != initial.Id && d.Connected))
                    endpoint.Items.Add(device);
            endpoint.SelectedIndex = 0;
            panel.Children.Add(endpoint);
        }
        else Label("范围：全部设备。接收器与填写型号的对应关系仍需核对。");
        Label(index switch
        {
            0 => "请唤醒设备。可充电设备请先拔线；仅支持有线连接的设备请正常连接。",
            1 => "请接入充电线，等待驱动或指示灯稳定。设备不支持充电时可直接保存已采样本。",
            _ => "请再次拔掉充电线，等待设备恢复连接。"
        });
        Label("铭牌完整型号（含后缀）");
        var name = new TextBox { Text = model ?? "", MaxLength = 80, IsReadOnly = index > 0 };
        panel.Children.Add(name);
        Label("当前连接方式");
        var transport = Choice(new[] { "未知", "2.4G", "蓝牙", "USB 有线" });
        Label("官方驱动显示的电量（0–100，未知留空）");
        var percent = new TextBox { MaxLength = 3 };
        panel.Children.Add(percent);
        Label("驱动或指示灯明确显示的状态");
        var state = Choice(new[] { "未知", "未充电", "充电中", "已充满", "不支持充电" });
        var buttons = new WrapPanel { Margin = new Thickness(0, 15, 0, 0) };
        panel.Children.Add(buttons);
        var record = new Button { Content = "采集此状态", IsDefault = true, Padding = new Thickness(10, 4, 10, 4) };
        buttons.Children.Add(record);
        FeedbackService.PhaseNote note = null;
        record.Click += (_, _) =>
        {
            int? level = null;
            if (percent.Text.Trim().Length > 0)
            {
                if (!int.TryParse(percent.Text, out int value) || value < 0 || value > 100)
                { MessageBox.Show(dialog, "请输入 0 到 100 的整数，或留空。"); return; }
                level = value;
            }
            if (string.IsNullOrWhiteSpace(name.Text))
            { MessageBox.Show(dialog, "请填写设备铭牌上的完整型号。"); return; }
            note = new(stages[index], name.Text.Trim(), (string)transport.SelectedItem, level,
                (string)state.SelectedItem, DateTimeOffset.UtcNow);
            dialog.DialogResult = true;
        };
        if (index > 0)
        {
            var finish = new Button { Content = "保存已有样本", Padding = record.Padding };
            finish.Click += (_, _) => dialog.DialogResult = true;
            buttons.Children.Add(finish);
        }
        buttons.Children.Add(new Button { Content = "取消导出", IsCancel = true, Padding = record.Padding });
        if (dialog.ShowDialog() != true) throw new OperationCanceledException();
        target = endpoint?.SelectedItem as DeviceInfo;
        return note;
    }
}
