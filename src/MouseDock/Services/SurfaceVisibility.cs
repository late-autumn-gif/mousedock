using System.Windows;

namespace MouseDock.Services
{
    public sealed class SurfaceVisibility
    {
        private readonly (string Mode, Window Window)[] _windows;

        public SurfaceVisibility(Window dock, Window mini, Window icon, Window taskbar)
            : this(dock, mini, icon, taskbar, null) { }

        public SurfaceVisibility(Window dock, Window mini, Window icon, Window taskbar, Window edge)
            : this(dock, mini, icon, taskbar, edge, null) { }

        public SurfaceVisibility(Window dock, Window mini, Window icon, Window taskbar, Window edge, Window pet)
        {
            _windows = new[] { ("dock", dock), ("mini", mini), ("icon", icon), ("taskbar", taskbar), ("edge", edge), ("pet", pet) };
        }

        public Window GetWindow(string mode)
        {
            foreach (var entry in _windows)
                if (entry.Mode == mode) return entry.Window;
            return null;
        }

        public void Apply(string mode, bool visible)
        {
            var active = GetWindow(mode);
            foreach (var entry in _windows)
                if (entry.Window != null && (!visible || entry.Window != active))
                    entry.Window.Hide();
            if (visible) active?.Show();
        }
    }
}
