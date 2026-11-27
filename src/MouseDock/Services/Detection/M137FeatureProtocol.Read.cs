using System;

namespace MouseDock.Services.Detection;

internal static partial class M137FeatureProtocol
{
    internal static ReferenceBatteryProtocol.Reading Read(Func<byte[], bool> setFeature,
        Func<byte[], bool> getFeature, Action<int> delay, Func<int> lastError, BatteryReplay.Trace trace = null)
    {
        byte sequence = 0;
        int level = -1, firstLevel = -1;
        string identity = null, hex = "";
        for (int transaction = 0; transaction < 2; transaction++)
        {
            var set = Request(sequence++);
            if (!setFeature(set))
            {
                int error = lastError();
                trace?.Add("set-feature", false, set[4], nativeError: error);
                return ReferenceBatteryProtocol.Reading.Failed("M137 SET_FEATURE 失败 Win32=" + error);
            }
            trace?.Add("set-feature", true, set[4]);
            int accepted = 0;
            for (int attempt = 0; attempt < 2 && accepted == 0; attempt++)
            {
                delay(16);
                var get = Request(sequence++);
                byte requestSequence = get[4];
                if (!getFeature(get))
                {
                    int error = lastError();
                    trace?.Add("get-feature", false, requestSequence, nativeError: error);
                    return ReferenceBatteryProtocol.Reading.Failed("M137 GET_FEATURE 失败 Win32=" + error);
                }
                hex = Convert.ToHexString(get);
                if (!TryPercent(get, out int current, out string currentIdentity))
                {
                    trace?.Add("get-feature", false, requestSequence, result: "invalid-frame");
                    continue;
                }
                trace?.Add("get-feature", true, requestSequence, result: $"{currentIdentity}: {current}%");
                if (currentIdentity != "M137" && ModelForIdentity(currentIdentity) == null)
                    return ReferenceBatteryProtocol.Reading.Failed(
                        "M137 身份 " + currentIdentity + " 未收录型号，未采用读数；最近RX=" + hex);
                if (level >= 0 && identity != currentIdentity)
                    return ReferenceBatteryProtocol.Reading.Failed(
                        "M137 两次事务型号不一致，未采用；最近RX=" + hex);
                level = current;
                if (transaction == 0) firstLevel = current;
                identity = currentIdentity;
                accepted = 1;
            }
            if (accepted == 0)
                return ReferenceBatteryProtocol.Reading.Failed("M137 事务未取得有效身份帧；最近RX=" + hex);
        }
        string model = ModelForIdentity(identity) ?? DisplayForIdentity(identity);
        if (identity == "M137" && firstLevel > 0 && level == 0)
        {
            trace?.Add("zero-confirmation", false, result: "inconsistent-percent");
            return new(-1, false, false, ReferenceBatteryProtocol.State.Invalid,
                $"SC580 两次事务电量不一致（{firstLevel}% → 0%），本轮电量未知", model, identity);
        }
        return new(level, false, false, ReferenceBatteryProtocol.State.Online,
            "M137 GetInfo 两次有效同型号事务，采用第二次电量；充电状态未确认；最近RX=" + hex,
            model, identity);
    }
}
