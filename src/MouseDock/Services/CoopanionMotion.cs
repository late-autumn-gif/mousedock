using System;
using System.IO;
using System.Text.Json;
using Jint;
using Jint.Native;

namespace MouseDock.Services;

internal sealed class CoopanionMotion
{
    internal sealed record Sample(double X, double Y, double Width, double Height, double Angle,
        int Pose, string Mode, string Gesture, bool Carried, bool NativeReaction, string Face,
        double ModeTime, double Time, Particle[] Dust, FaceSprite[] Sprites, RigFrame Rig);
    internal sealed record Particle(double X, double Y, double Radius, double Opacity);
    internal sealed record FaceSprite(string Name, double Angle, double Scale);
    internal sealed record RigFrame(double Neck, double NeckY, double ArmNear, double ArmFar, double Tail, double Waist,
        double LegBack, double LegFront, double HairX, double SkirtX = 0, double SkirtY = 0);

    private readonly Engine _engine = new();
    private readonly JsValue _bridge;

    internal CoopanionMotion(Random random)
    {
        _engine.SetValue("motionRandom", new Func<double>(random.NextDouble));
        _engine.Execute("Math.random = motionRandom;");
        foreach (string file in new[] { "rig.js", "body.js", "figure.js", "whale-figure.js", "whale-host.js", "bridge.js" })
            _engine.Modules.Add("./" + file, ReadAsset(file));
        _bridge = _engine.Modules.Import("./bridge.js");
        Call("initialize", ReadAsset("whale-model.json")).UnwrapIfPromise();
    }

    private static string ReadAsset(string file)
    {
        using var stream = typeof(CoopanionMotion).Assembly.GetManifestResourceStream($"MouseDock.Coopanion.{file}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal void Reset(double width, double height, double floor, double scale, double start, bool rest) =>
        Call("reset", width, height, floor, scale, start, rest);

    internal Sample Advance(double seconds, bool automatic, bool blink, double frequency, bool canTravel, double speed = 1) =>
        JsonSerializer.Deserialize<Sample>(
            Call("advance", seconds, automatic, blink, frequency, canTravel, speed).AsString());

    internal bool Pointer(string kind, double x, double y, long time) =>
        Call("pointer", kind, x, y, time).AsBoolean();

    internal void Act(string word) => Call("act", word);

    private JsValue Call(string name, params object[] arguments) => _engine.Invoke(_bridge.Get(name), arguments);
}
