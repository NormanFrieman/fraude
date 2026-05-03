using System.Text.Json.Serialization;
using Fraude;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});


var app = builder.Build();
var fraudeApi = app.MapGroup("/todos");
fraudeApi.MapGet("/", () => "hello");

fraudeApi.MapPost("/fraud-score", IResult (FraudScore fraudScore) =>
{
    var transaction = fraudScore.transaction;
    var customer = fraudScore.customer;
    var lastTransaction = fraudScore.last_transaction;
    var terminal = fraudScore.terminal;
    var merchant = fraudScore.merchant;
    
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
    var isOnline = terminal.is_online;
    var cardPresent = terminal.card_present;
    var unknownMerchant = !customer.know_merchants.Contains(merchant.id);
    var mccRisk = MccRisk.GetValue(merchant.mcc);
    var merchantAvgAmount = Math.Clamp((merchant.avg_amount / Normalization.MaxMerchantAvgAmount), 0, 1);

    object[] vector =
    [
        amount, installments, amountVsAvg, hourOfDay, dayOfWeek, minutesSinceLastTx, kmFromLastTx,
        kmFromHome, txCount24H, isOnline, cardPresent, unknownMerchant, mccRisk, merchantAvgAmount
    ];
    
    var test = new FraudScoreResponse(true, 1.0f);
    return Results.Ok(test);
});

fraudeApi.MapGet("/ready", IResult () => Results.Ok());

app.Run();

[JsonSerializable(typeof(Transaction))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}