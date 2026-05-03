namespace Fraude;

public static class Normalization
{
    public static int MaxAmount { get; } = 10000;
    public static int MaxInstallments { get; } = 12;
    public static int AmountVsAvgRatio { get; } = 10;
    public static int MaxMinutes { get; } = 1440;
    public static int MaxKm { get; } = 1000;
    public static int MaxTxCount24H { get; } = 20;
    public static int MaxMerchantAvgAmount { get; } = 10000;
}