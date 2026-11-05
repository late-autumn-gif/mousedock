using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MouseDock.Models;

namespace MouseDock.Views
{
    /// <summary>
    /// 导出错误报告前的设备选择框。
    /// 只抓一台设备时，报告里只放这一台的接口、身份、只读查询和原始应答；
    /// 选「全部设备」则保留旧版全量报告。
    /// </summary>
    internal static class ReportDevicePicker
    {
        internal sealed class Target
        {
            public DeviceInfo Device;      // null = 全部设备
            public string Title;
            public string Subtitle;
            public string Detail;
        }

        internal static DeviceInfo Pick(Window owner, IReadOnlyList<DeviceInfo> devices, out bool canceled)
        {
            canceled = true;

            var window = new Window
            {
                Title = "选择要抓取的设备",
                Width = 640,
                Height = 500,
                MinWidth = 520,
                MinHeight = 420,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x23)),
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.CanResize
            };
            if (owner != null && owner.IsVisible) window.Owner = owner;

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "只抓这一台",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var desc = new TextBlock
            {
                Text = "选中哪台，报告里就只放哪台。会抓：产品名、VID:PID、全部 HID 接口、只读查询帧与原始应答、电量判定。" +
                       "\n不会发配置写入，也不会碰没选中的设备。",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xAE, 0xBC)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(desc, 1);
            root.Children.Add(desc);

            var list = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x24, 0x27, 0x2E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x3A, 0x44)),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(list, 2);
            root.Children.Add(list);

            var all = new Target
            {
                Device = null,
                Title = "全部设备（旧版报告）",
                Subtitle = "不发送查询帧，只列本机全部 HID 集合",
                Detail = "兼容旧报告，不针对单台设备"
            };
            list.Items.Add(MakeItem(all, false));

            foreach (var device in devices ?? Array.Empty<DeviceInfo>())
            {
                var target = new Target
                {
                    Device = device,
                    Title = device.Name,
                    Subtitle = $"{device.KindLabel} · {device.Transport} · {(device.Level < 0 ? "电量未知" : device.Level + "%")}",
                    Detail = $"{device.Detail} · {device.Id}"
                };
                list.Items.Add(MakeItem(target, true));
            }

            // 默认选中第一台真实设备；没有设备时退回「全部」。
            list.SelectedIndex = devices != null && devices.Count > 0 ? 1 : 0;

            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(bottom, 3);

            var hint = new TextBlock
            {
                Text = devices != null && devices.Count > 0
                    ? "双击设备也可以直接开始。"
                    : "当前没有已识别设备，只能导出全量报告。",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0x9C)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };
            Grid.SetColumn(hint, 0);
            bottom.Children.Add(hint);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(buttons, 1);

            var cancel = new Button
            {
                Content = "取消",
                Width = 88,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };
            cancel.Click += (_, __) => window.Close();

            var ok = new Button
            {
                Content = "开始抓取",
                Width = 104,
                Height = 30,
                IsDefault = true
            };
            ok.Click += (_, __) => Accept(window, list);

            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            bottom.Children.Add(buttons);
            root.Children.Add(bottom);

            list.MouseDoubleClick += (_, __) => Accept(window, list);

            window.Content = root;
            bool? result = window.ShowDialog();
            if (result != true) return null;

            canceled = false;
            return (window.Tag as Target)?.Device;
        }

        private static ListBoxItem MakeItem(Target target, bool isDevice)
        {
            var panel = new StackPanel { Margin = new Thickness(2) };
            panel.Children.Add(new TextBlock
            {
                Text = target.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            panel.Children.Add(new TextBlock
            {
                Text = target.Subtitle,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xAE, 0xBC)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = target.Detail,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x72, 0x78, 0x86)),
                Margin = new Thickness(0, 1, 0, 0)
            });

            return new ListBoxItem
            {
                Tag = target,
                Content = panel,
                Padding = new Thickness(10, 8, 10, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Foreground = Brushes.White
            };
        }

        private static void Accept(Window window, ListBox list)
        {
            if (!(list.SelectedItem is ListBoxItem item) || !(item.Tag is Target target)) return;
            window.Tag = target;
            window.DialogResult = true;
        }
    }
}
