using System;

namespace MouseDock.Services.Detection;

internal static partial class M137FeatureProtocol
{
    internal const string Reader = "M137Feature";

    internal static ReferenceBatteryProtocol.Reading Read(Func<byte[], bool> setFeature,
        Func<byte[], bool> getFeature, Action<int> delay, Func<int> lastError, BatteryReplay.Trace trace = null)
    {
        byte sequence = 0;
        int level = -1;
        string identity = null, hex = "";
        int transactionCount = 2;
        for (int transaction = 0; transaction < transactionCount; transaction++)
        {
            byte setSequence = sequence++;
            var set = Request(setSequence);
            bool sent = setFeature(set);
            int? setError = sent ? null : lastError();
            trace?.Add("set-feature", sent, setSequence, Request(setSequence), keep: true, nativeError: setError);
            if (!sent)
                return ReferenceBatteryProtocol.Reading.Failed("M137 SET_FEATURE 失败 Win32=" + setError);
            int accepted = 0;
            for (int attempt = 0; attempt < 2 && accepted == 0; attempt++)
            {
                delay(16);
                byte getSequence = sequence++;
                var get = Request(getSequence);
                if (!getFeature(get))
                {
                    int error = lastError();
                    trace?.Add("get-feature", false, getSequence, nativeError: error);
                    return ReferenceBatteryProtocol.Reading.Failed("M137 GET_FEATURE 失败 Win32=" + error);
                }
                hex = Convert.ToHexString(get);
                bool valid = TryPercent(get, out int current, out string currentIdentity);
                bool known = valid && ModelForIdentity(currentIdentity) != null;
                bool same = known && (identity == null || identity == currentIdentity);
                if (trace != null)
                {
                    byte[] evidence = null;
                    if (currentIdentity != null)
                    {
                        // Retain only the validated header, model identifier and battery byte.
                        evidence = new byte[FrameLength];
                        get.AsSpan(0, 4).CopyTo(evidence);
                        get.AsSpan(8, 4).CopyTo(evidence.AsSpan(8));
                        evidence[18] = get[18];
                    }
                    trace.Add("get-feature", true, getSequence, evidence, keep: true,
                        result: !valid ? "invalid-frame" : !known ? "unknown-model" : !same ? "model-mismatch" : "accepted");
                }
                if (!valid) continue;
                if (!known)
                    return ReferenceBatteryProtocol.Reading.Failed(
                        "M137 身份 " + currentIdentity + " 未收录型号，未采用读数；最近RX=" + hex);
                if (!same)
                    return ReferenceBatteryProtocol.Reading.Failed(
                        "M137 事务间型号不一致，未采用；最近RX=" + hex);
                if (transaction == 1 && currentIdentity == "M137" && level > 0 && current == 0)
                    transactionCount = 3;
                level = current;
                identity = currentIdentity;
                accepted = 1;
            }
            if (accepted == 0)
                return ReferenceBatteryProtocol.Reading.Failed("M137 事务未取得有效身份帧；最近RX=" + hex);
        }
        string model = ModelForIdentity(identity) ?? DisplayForIdentity(identity);
        string source = transactionCount == 3
            ? "M137 GetInfo SC580 跳零复核，采用第三次同型号电量"
            : "M137 GetInfo 两次有效同型号事务，采用第二次电量";
        return new(level, false, false, ReferenceBatteryProtocol.State.Online,
            source + "；充电状态未确认；最近RX=" + hex,
            model, identity);
    }
}
