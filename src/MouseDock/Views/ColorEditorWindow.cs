using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Views;

public sealed class ColorEditorWindow : Window
{
    public Dictionary<string, string> Draft { get; }
    private readonly List<(string Key, TextBox Input)> _inputs = new();

    public ColorEditorWindow(string title, Dictionary<string, string> values, ThemePreset palette, bool ordinary)
    {
        Draft = new(values ?? new());
        Title = title;
        Width = 350;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 48);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(palette.BgTop);
        Foreground = new SolidColorBrush(palette.Text);
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var panel = new StackPanel { Margin = new Thickness(16) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (ordinary) Add("background", "背景", palette.BgTop);
        if (ordinary || title.Contains("悬浮")) Add("surface", "底色", palette.Surface);
        Add("text", "文字", palette.Text);
        Add("accent", "正常 / 强调", palette.Accent);
        Add("low", "低电量", palette.Low);
        Add("muted", ordinary ? "次级文字 / 未知" : "未知电量", palette.TextDim);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(actions);
        var reset = Action("恢复默认");
        reset.Click += (_, __) => { Draft.Clear(); foreach (var entry in _inputs) entry.Input.Text = ""; };
        actions.Children.Add(reset);
        var cancel = Action("取消"); cancel.IsCancel = true; actions.Children.Add(cancel);
        var done = Action("完成"); done.IsDefault = true;
        done.Click += (_, __) =>
        {
            foreach (var entry in _inputs)
                if (!string.IsNullOrWhiteSpace(entry.Input.Text) && !AppearanceColors.TryColor(entry.Input.Text, out var ignoredColor))
                { entry.Input.Focus(); return; }
            DialogResult = true;
        };
        actions.Children.Add(done);

        void Add(string key, string label, Color fallback)
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            panel.Children.Add(row);
            row.Children.Add(new TextBlock { Text = label, Width = 117, VerticalAlignment = VerticalAlignment.Center });
            var input = new TextBox { Width = 88, MaxLength = 7, VerticalContentAlignment = VerticalAlignment.Center,
                Text = Draft.TryGetValue(key, out var value) ? value : "", ToolTip = "#RRGGBB" };
            _inputs.Add((key, input));
            var swatch = new Button { Width = 30, Height = 26, Margin = new Thickness(0, 0, 9, 0),
                Background = new SolidColorBrush(fallback), ToolTip = label };
            swatch.Click += (_, __) =>
            {
                var initial = AppearanceColors.TryColor(input.Text, out var selected) ? selected : fallback;
                using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true,
                    Color = System.Drawing.Color.FromArgb(initial.R, initial.G, initial.B) };
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    input.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            };
            input.TextChanged += (_, __) =>
            {
                if (string.IsNullOrWhiteSpace(input.Text))
                { Draft.Remove(key); input.ClearValue(Border.BorderBrushProperty); swatch.Background = new SolidColorBrush(fallback); }
                else if (AppearanceColors.TryColor(input.Text, out var color))
                { Draft[key] = Palette.ToHex(color); input.ClearValue(Border.BorderBrushProperty); swatch.Background = new SolidColorBrush(color); }
                else input.BorderBrush = Brushes.IndianRed;
            };
            if (AppearanceColors.TryColor(input.Text, out var current)) swatch.Background = new SolidColorBrush(current);
            row.Children.Add(swatch); row.Children.Add(input);
            var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(117, 0, 0, 5) };
            foreach (string hex in new[] { "#FFFFFF", "#202B29", "#A6F5BF", "#7BC9D3", "#E4AFC8", "#FF687D" })
            {
                var preset = new Button { Width = 20, Height = 18, Margin = new Thickness(0, 0, 4, 0),
                    Background = new SolidColorBrush(ThemePreset.C(hex)), ToolTip = hex };
                preset.Click += (_, __) => input.Text = hex;
                presets.Children.Add(preset);
            }
            panel.Children.Add(presets);
        }
    }

    private static Button Action(string title) => new() { Content = title, Padding = new Thickness(9, 5, 9, 5),
        Margin = new Thickness(4, 0, 0, 0) };
}
