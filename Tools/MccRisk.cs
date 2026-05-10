using System.Runtime.CompilerServices;

namespace Fraude.Tools;

public static class MccRisk
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetValue(string mcc) => mcc switch
    {
        "5411" => 0.15f,
        "5812" => 0.30f,
        "5912" => 0.20f,
        "5944" => 0.45f,
        "7801" => 0.80f,
        "7802" => 0.75f,
        "7995" => 0.85f,
        "4511" => 0.35f,
        "5311" => 0.25f,
        "5999" => 0.50f,
        _ => 0.5f
    };
}
