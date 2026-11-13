using System;
using System.Collections.Generic;
using System.Linq;

namespace MouseDock.Services.Detection;

public enum DpiFamily { Atk, Z7, Scyrox, Rapoo, Sora, Catalog, Pulsar, MchoseReadOnly }

public readonly record struct DpiStage(int X, int Y)
{
    public string Label => X == Y ? $"{X} DPI" : $"X {X} / Y {Y} DPI";
}

/// <summary>A validated device snapshot; editing creates a new value and never changes this snapshot.</summary>
public sealed class DpiSnapshot
{
    internal DpiProfile Profile { get; }
    internal string Context { get; }
    internal string ConnectionKey { get; }
    public string InstanceId { get; }
    public DateTimeOffset CapturedAt { get; }
    public DpiFamily Family => Profile.Family;
    public string Model => Profile.Model;
    public int Active { get; }
    public IReadOnlyList<DpiStage> Stages { get; }
    public DpiStage Current => Stages[Active];
    public bool Writable { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    public int Step { get; }
    public string Note { get; }

    internal DpiSnapshot(DpiProfile profile, int active, IEnumerable<DpiStage> stages, string context,
        int minimum, int maximum, int step, bool writable = true, string note = "",
        string instanceId = "", string connectionKey = "", DateTimeOffset? capturedAt = null)
    {
        var rows = stages.ToArray();
        if (rows.Length is < 1 or > 8 || active < 0 || active >= rows.Length)
            throw new InvalidOperationException("DPI 档位数据无效");
        Profile = profile; Active = active; Stages = Array.AsReadOnly(rows); Context = context;
        Minimum = minimum; Maximum = maximum; Step = step; Writable = writable && profile.WriteApproved;
        Note = note; InstanceId = instanceId; ConnectionKey = connectionKey;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
    }

    internal DpiSnapshot Bind(string instanceId, string connectionKey) =>
        new(Profile, Active, Stages, Context, Minimum, Maximum, Step, Writable, Note, instanceId, connectionKey);

    internal DpiSnapshot Target(int stage, int? value)
    {
        if (!Writable) throw new InvalidOperationException(Note.Length > 0 ? Note : "此连接方式只支持 DPI 读取");
        if (stage < 0 || stage >= Stages.Count) throw new ArgumentOutOfRangeException(nameof(stage), "请选择有效档位");
        var rows = Stages.ToArray();
        if (value.HasValue)
        {
            if (value.Value < Minimum || value.Value > Maximum || value.Value % Step != 0)
                throw new ArgumentException($"DPI 应为 {Minimum}–{Maximum} 内的 {Step} 的倍数");
            DpiProtocol.ValidateValue(this, value.Value);
            rows[stage] = new(value.Value, value.Value);
        }
        return new(Profile, value.HasValue ? Active : stage, rows, Context,
            Minimum, Maximum, Step, Writable, Note, InstanceId, ConnectionKey, CapturedAt);
    }

    internal bool SameSettings(DpiSnapshot other) => other != null &&
        Profile == other.Profile && Active == other.Active && Context == other.Context &&
        Writable == other.Writable && Stages.SequenceEqual(other.Stages);
}

internal interface IDpiLink
{
    long Now { get; }
    byte[] Read(int timeoutMs);
    bool Write(byte[] packet, int timeoutMs);
    bool SetFeature(byte[] packet);
    byte[] GetFeature(byte reportId, int length);
    void Delay(int milliseconds);
}
