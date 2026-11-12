namespace MouseDock.Services.Detection;

internal enum MouseDpiProtocol { Mhub, Hidpp, AtkZero, ScyroxV12, MchoseA7Ultra, AulaSc580, RogKerisIIOmni }

internal sealed record MouseDpiState(byte Profile, byte Index, byte Count, bool SeparateAxes, int[] Values,
    MouseDpiProtocol Protocol = MouseDpiProtocol.Mhub, string Identity = null, bool Writable = true);

internal sealed record MouseDpiChange(MouseDpiState Expected, int Index);
