using System.Drawing;
using MouseDock.Models;
using Forms = System.Windows.Forms;

namespace MouseDock.Services;

internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    private sealed class ThemeTable(ThemePreset theme) : Forms.ProfessionalColorTable
    {
        internal static Color C(System.Windows.Media.Color color) => Color.FromArgb(255, color.R, color.G, color.B);
        public override Color ToolStripDropDownBackground => C(theme.Surface);
        public override Color ImageMarginGradientBegin => C(theme.Surface);
        public override Color ImageMarginGradientMiddle => C(theme.Surface);
        public override Color ImageMarginGradientEnd => C(theme.Surface);
        public override Color MenuItemSelected => C(theme.Hover.A == 0 ? Palette.Mix(theme.Surface, theme.Text, 0.12) : theme.Hover);
        public override Color MenuItemBorder => MenuItemSelected;
        public override Color MenuBorder => C(theme.Border.A == 0 ? Palette.Mix(theme.Surface, theme.Text, 0.25) : theme.Border);
        public override Color SeparatorDark => MenuBorder;
        public override Color SeparatorLight => C(theme.Surface);
        public override Color CheckBackground => MenuItemSelected;
        public override Color CheckSelectedBackground => MenuItemSelected;
    }
    private readonly ThemePreset theme;
    private TrayMenuRenderer(ThemePreset theme) : base(new ThemeTable(theme))
    {
        this.theme = theme;
        RoundedEdges = false;
    }
    internal static void Apply(Forms.ContextMenuStrip menu, ThemePreset theme)
    {
        menu.Renderer = new TrayMenuRenderer(theme);
        menu.BackColor = ThemeTable.C(theme.Surface);
        menu.ForeColor = ThemeTable.C(theme.Text);
        menu.ShowImageMargin = false;
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.ForeColor = ThemeTable.C(item.Enabled ? theme.Text : theme.TextDim);
            if (item is Forms.ToolStripMenuItem parent && parent.HasDropDownItems)
                parent.DropDown.Renderer = new TrayMenuRenderer(theme);
        }
    }
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = ThemeTable.C(e.Item.Enabled ? theme.Text : theme.TextDim);
        base.OnRenderItemText(e);
    }
    protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = ThemeTable.C(e.Item.Enabled ? theme.Text : theme.TextDim);
        base.OnRenderArrow(e);
    }
}
