using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MouseDock.Services;

namespace MouseDock.Views
{
    /// <summary>
    /// 首次启动弹出的「关于」通告。
    ///
    /// 只在第一次运行时出现，之后靠 AppSettings.HasShownAbout 记住。
    /// 设置页里可以随时再打开。
    /// </summary>
    internal static class AboutWindow
    {
        /// <summary>
        /// 显示关于窗口。isFirstRun 决定标题和按钮文案。
        /// </summary>
        public static void Show(Window owner, bool isFirstRun,
            Func<string> exportReport, Action<string> openFeedback)
        {
            var win = new Window
            {
                Title = "关于 MouseDock",
                Width = isFirstRun ? 460 : 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Owner = owner,
                ShowInTaskbar = false,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x22))
            };

            var root = new StackPanel { Margin = new Thickness(28, 24, 28, 20) };

            // 标题
            root.Children.Add(new TextBlock
            {
                Text = "MouseDock",
                FontSize = 26,
                FontWeight = FontWeights.Light,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE9, 0xF0))
            });

            root.Children.Add(new TextBlock
            {
                Text = "v" + UpdateService.CurrentVersionText,
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8C, 0x9A))
            });

            // 一句话
            root.Children.Add(new TextBlock
            {
                Text = "Windows 桌面外设电量面板。插上就认，读不到就显示「—」，绝不编数字。",
                FontSize = 12.5,
                Margin = new Thickness(0, 16, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xBA, 0xC6))
            });

            root.Children.Add(Divider());

            // 反馈区
            root.Children.Add(SectionLabel("反馈与支持"));

            root.Children.Add(new TextBlock
            {
                Text = "遇到不适配的设备、有功能建议，发邮件给我。\n"
                     + "邮件里附上错误报告最有用 —— 里面有本机全部 HID 设备的型号和 VID/PID。",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0xA0, 0xAE))
            });

            root.Children.Add(new TextBlock
            {
                Text = FeedbackService.ContactEmail,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xB4, 0xFF)),
                Cursor = System.Windows.Input.Cursors.Hand
            });

            var mail = (TextBlock)root.Children[root.Children.Count - 1];
            mail.MouseLeftButtonUp += (_, __) =>
            {
                try { Process.Start(new ProcessStartInfo("mailto:" + FeedbackService.ContactEmail) { UseShellExecute = true }); }
                catch { }
            };

            // QQ 群
            root.Children.Add(new TextBlock
            {
                Text = "QQ 群 148894057 —— 也可以加群直接说",
                FontSize = 11.5,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x7C, 0x8A))
            });

            root.Children.Add(Divider());

            // 按钮区
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var btnReport = MakeButton("导出错误报告");
            btnReport.Click += (_, __) =>
            {
                try
                {
                    string path = exportReport?.Invoke();
                    if (string.IsNullOrEmpty(path))
                    {
                        MessageBox.Show(win, "导出失败，请检查磁盘权限。", "MouseDock", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    var r = MessageBox.Show(win,
                        "报告已生成：\n" + path + "\n\n现在打开它所在的文件夹吗？",
                        "导出成功", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (r == MessageBoxResult.Yes) FeedbackService.OpenReportFolder(path);
                }
                catch { }
            };
            buttons.Children.Add(btnReport);

            var btnFeedback = MakeButton("写反馈邮件", true);
            btnFeedback.Margin = new Thickness(8, 0, 0, 0);
            btnFeedback.Click += (_, __) =>
            {
                try
                {
                    string path = exportReport?.Invoke();
                    if (!string.IsNullOrEmpty(path)) FeedbackService.OpenReportFolder(path);
                    openFeedback?.Invoke(path);
                }
                catch { }
            };
            buttons.Children.Add(btnFeedback);

            var btnClose = MakeButton(isFirstRun ? "开始使用" : "关闭");
            btnClose.Margin = new Thickness(8, 0, 0, 0);
            btnClose.Click += (_, __) => win.Close();
            buttons.Children.Add(btnClose);

            root.Children.Add(buttons);

            win.Content = root;
            win.ShowDialog();
        }

        private static TextBlock SectionLabel(string text) => new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD2, 0xDC))
        };

        private static Border Divider() => new Border
        {
            Height = 1,
            Margin = new Thickness(0, 16, 0, 16),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x36))
        };

        private static Button MakeButton(string text, bool primary = false)
        {
            var b = new Button
            {
                Content = text,
                Padding = new Thickness(14, 7, 14, 7),
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = new SolidColorBrush(primary ? Color.FromRgb(0x10, 0x12, 0x18) : Color.FromRgb(0xD0, 0xD2, 0xDC)),
                Background = new SolidColorBrush(primary ? Color.FromRgb(0x9C, 0xB4, 0xFF) : Color.FromRgb(0x2A, 0x2C, 0x36))
            };
            return b;
        }
    }
}
