namespace MouseDock.Services.Detection;

internal enum MouseDpiProtocol { Mhub, Hidpp, AtkZero, ScyroxV12, MchoseA7Ultra, AulaSc580, RogKerisIIOmni, AulaSc630, VgnF1Moba }

internal sealed record MouseDpiState(byte Profile, byte Index, byte Count, bool SeparateAxes, int[] Values,
    MouseDpiProtocol Protocol = MouseDpiProtocol.Mhub, string Identity = null, bool Writable = true,
    int[] HardwareStages = null)
{
    internal int StageNumber(int index) => HardwareStages?[index] ?? index + 1;
    internal string OptionLabel(int index) => Values[index] <= 0 ? $"档位 {StageNumber(index)}" :
        Protocol == MouseDpiProtocol.Hidpp ? $"{Values[index]} DPI" : $"档位 {StageNumber(index)} · {Values[index]} DPI";
}

internal sealed record MouseDpiChange(MouseDpiState Expected, int Index);
