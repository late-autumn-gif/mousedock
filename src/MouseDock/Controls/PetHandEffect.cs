using System;
using System.Windows;
using System.Windows.Media.Effects;

namespace MouseDock.Controls;

internal sealed class PetHandEffect : ShaderEffect
{
    private static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(PetHandEffect), 0, SamplingMode.Bilinear);
    private static readonly DependencyProperty MouseOffsetProperty = Register("MouseOffset", new Point(), 0);
    private static readonly DependencyProperty KeyStrokeProperty = Register("KeyStroke", 0d, 1);
    private static readonly DependencyProperty AnchorsProperty = Register("Anchors", new Point(.25, .70), 2);
    private static readonly PixelShader Shader = LoadShader();

    internal PetHandEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(MouseOffsetProperty);
        UpdateShaderValue(KeyStrokeProperty);
        UpdateShaderValue(AnchorsProperty);
    }

    internal Point MouseOffset { get => (Point)GetValue(MouseOffsetProperty); set => SetValue(MouseOffsetProperty, value); }
    internal double KeyStroke { get => (double)GetValue(KeyStrokeProperty); set => SetValue(KeyStrokeProperty, value); }
    internal Point Anchors { set => SetValue(AnchorsProperty, value); }

    private static DependencyProperty Register<T>(string name, T initial, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(PetHandEffect),
            new UIPropertyMetadata(initial, PixelShaderConstantCallback(register)));

    private static PixelShader LoadShader()
    {
        var shader = new PixelShader { UriSource = new Uri(
            "pack://application:,,,/MouseDock;component/Controls/PetHandEffect.ps", UriKind.Absolute) };
        shader.Freeze();
        return shader;
    }
}
