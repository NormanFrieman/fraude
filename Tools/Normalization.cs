namespace Fraude.Tools;

public static class Normalization
{
    public static float MaxAmount { get; } = 10000;
    public static float MaxInstallments { get; } = 12;
    public static float AmountVsAvgRatio { get; } = 10;
    public static float MaxMinutes { get; } = 1440;
    public static float MaxKm { get; } = 1000;
    public static float MaxTxCount24H { get; } = 20;
    public static float MaxMerchantAvgAmount { get; } = 10000;
}