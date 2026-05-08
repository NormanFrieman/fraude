using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace Fraude.Tools;

public static class MccRisk
{
    private static readonly FrozenDictionary<string, float> Risks = new Dictionary<string, float>
    {
        ["5411"] = 0.15f, ["5812"] = 0.30f, ["5912"] = 0.20f,
        ["5944"] = 0.45f, ["7801"] = 0.80f, ["7802"] = 0.75f,
        ["7995"] = 0.85f, ["4511"] = 0.35f, ["5311"] = 0.25f,
        ["5999"] = 0.50f
    }.ToFrozenDictionary();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetValue(string mcc) =>
        Risks.TryGetValue(mcc, out var v) ? v : 0.5f;
}
