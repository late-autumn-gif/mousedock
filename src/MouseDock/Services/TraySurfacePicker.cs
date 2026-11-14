using System;
using System.Collections.Generic;
using System.Drawing;
using MouseDock.Models;
using Forms = System.Windows.Forms;

namespace MouseDock.Services;

/// <summary>Native tray quick choices: one shared mode, keyboard-focusable buttons.</summary>
internal sealed class TraySurfacePicker : Forms.ToolStripControlHost
{
    private readonly Dictionary<string, Forms.Button> buttons = new();
    internal static readonly (string Mode, string Label)[] Choices =
    [ ("dock", "卡片窗"), ("mini", "迷你窗"), ("icon", "悬浮图标"),
      ("taskbar", "任务栏"), ("edge", "屏幕右侧"), ("pet", "桌宠") ];

    internal TraySurfacePicker(Action<string> select) : base(new Forms.TableLayoutPanel
    {
        ColumnCount = 3, RowCount = 2, Size = new Size(270, 76),
        Margin = Forms.Padding.Empty, Padding = new Forms.Padding(3), TabStop = false,
        AccessibleName = "显示方式"
    })
    {
        AutoSize = false;
        Size = new Size(270, 76);
        Margin = new Forms.Padding(3, 4, 3, 4);
        var panel = (Forms.TableLayoutPanel)Control;
        for (int i = 0; i < 3; i++) panel.ColumnStyles.Add(new(Forms.SizeType.Percent, 100f / 3));
        for (int i = 0; i < 2; i++) panel.RowStyles.Add(new(Forms.SizeType.Percent, 50));
        foreach (var (mode, label) in Choices)
        {
            string target = mode;
            var button = new Forms.Button
            {
                Text = label, Tag = mode, Dock = Forms.DockStyle.Fill,
                FlatStyle = Forms.FlatStyle.Flat, Margin = new Forms.Padding(2),
                AccessibleName = label, TabStop = true, UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) =>
            {
                (Owner as Forms.ToolStripDropDown)?.Close(Forms.ToolStripDropDownCloseReason.ItemClicked);
                select(target);
            };
            panel.Controls.Add(button);
            buttons.Add(mode, button);
        }
    }

    internal void Refresh(ThemePreset theme, string mode)
    {
        static Color C(System.Windows.Media.Color c) => Color.FromArgb(c.R, c.G, c.B);
        Control.BackColor = C(theme.Surface);
        foreach (var (key, button) in buttons)
        {
            bool selected = key == mode;
            button.Text = (selected ? "✓ " : "") + Array.Find(Choices, c => c.Mode == key).Label;
            button.BackColor = C(selected ? Palette.Mix(theme.Surface, theme.Accent, .09) : theme.Surface);
            button.ForeColor = C(theme.Text);
            button.FlatAppearance.MouseOverBackColor = C(Palette.Mix(theme.Surface, theme.Text, .07));
            button.FlatAppearance.MouseDownBackColor = C(Palette.Mix(theme.Surface, theme.Text, .11));
        }
    }
}
