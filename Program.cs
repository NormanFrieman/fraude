using System.Text.Json;
using System.Text.Json.Serialization;
using Fraude;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = SharedJsonContext.Default;
});

var jsonContent = File.ReadAllText("References/references.json");
var vectorBases = JsonSerializer.Deserialize<VectorBase[]>(jsonContent, SharedJsonContext.Default.VectorBaseArray);
if (vectorBases == null || vectorBases.Length == 0)
    throw new Exception();

var fraudeApi = builder.Build();

fraudeApi.MapPost("/fraud-score", IResult (FraudScore score) =>
{
    var transaction = score.transaction;
    var customer = score.customer;
    var lastTransaction = score.last_transaction;
    var terminal = score.terminal;
    var merchant = score.merchant;
    
    var amount = Math.Clamp((transaction.amount / Normalization.MaxAmount), 0, 1);
    var installments = Math.Clamp((transaction.installments / Normalization.MaxInstallments), 0, 1);
    var amountVsAvg = Math.Clamp(((transaction.amount / customer.avg_amout) / Normalization.AmountVsAvgRatio), 0, 1);
    var hourOfDay = Math.Clamp((transaction.requested_at.Hour / 23), 0, 23);
    var dayOfWeek = Math.Clamp((transaction.requested_at.Day / 6), 0, 6);
    var minutesSinceLastTx = lastTransaction is not null
        ? Math.Clamp(((DateTime.UtcNow - lastTransaction.timestamp).Minutes / Normalization.MaxMinutes), 0, 1)
        : -1;
    var kmFromLastTx = lastTransaction is not null
        ? Math.Clamp((lastTransaction.km_from_current / Normalization.MaxKm), 0, 1)
        : -1;
    var kmFromHome = Math.Clamp((terminal.km_from_home / Normalization.MaxKm), 0, 1);
    var txCount24H = Math.Clamp((customer.tx_count_24h / Normalization.MaxTxCount24H), 0, 1);
    var isOnline = terminal.is_online ? 1f : 0f;
    var cardPresent = terminal.card_present ? 1f : 0f;
    var unknownMerchant = !customer.know_merchants.Contains(merchant.id) ? 1f : 0f;
    var mccRisk = MccRisk.GetValue(merchant.mcc);
    var merchantAvgAmount = Math.Clamp((merchant.avg_amount / Normalization.MaxMerchantAvgAmount), 0, 1);

    float[] vector =
    [
        amount, installments, amountVsAvg, hourOfDay, dayOfWeek, minutesSinceLastTx, kmFromLastTx,
        kmFromHome, txCount24H, isOnline, cardPresent, unknownMerchant, mccRisk, merchantAvgAmount
    ];
    
    FraudManager.CalcTopRegisters(vectorBases, vector);
    var (decision, fraudScore) = FraudManager.Detect();
    var response = new FraudScoreResponse(decision, fraudScore);
    return Results.Ok(response);
});

fraudeApi.MapGet("/ready", IResult () => Results.Ok());
fraudeApi.Run();