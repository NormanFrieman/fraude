using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Fraude.Models;
using Fraude.Tools;
using SharedJsonContext = Fraude.Models.SharedJsonContext;

const string path = "References/references.bin";
await using var fs = File.OpenRead(path);
var vectorBases = await JsonSerializer.DeserializeAsync<ImmutableArray<VectorBase>>(
    fs,
    SharedJsonContext.Default.ImmutableArrayVectorBase);
if (vectorBases == null || vectorBases.Length == 0)
    throw new Exception();

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = SharedJsonContext.Default;
});

var fraudeApi = builder.Build();

fraudeApi.MapPost("/fraud-score", IResult (
    FraudScore score) =>
{
    var transaction = score.transaction;
    var customer = score.customer;
    var lastTransaction = score.last_transaction;
    var terminal = score.terminal;
    var merchant = score.merchant;
    
    var amount = Math.Clamp((transaction.amount / Normalization.MaxAmount), 0, 1);
    var installments = Math.Clamp((transaction.installments / Normalization.MaxInstallments), 0, 1);
    var amountVsAvg = Math.Clamp((transaction.amount / customer.avg_amount) / Normalization.AmountVsAvgRatio, 0, 1);
    var hourOfDay = Math.Clamp((transaction.requested_at.Hour / 23f), 0, 23);
    var dayOfWeekReq = transaction.requested_at.DayOfWeek == DayOfWeek.Sunday
        ? 6
        : (int)transaction.requested_at.DayOfWeek - 1;
    var dayOfWeek = Math.Clamp(dayOfWeekReq / 6f, 0, 6);
    var minutesSinceLastTx = lastTransaction is not null
        ? Math.Clamp(((DateTime.UtcNow - lastTransaction.timestamp).Minutes / Normalization.MaxMinutes), 0, 1)
        : -1;
    var kmFromLastTx = lastTransaction is not null
        ? Math.Clamp((lastTransaction.km_from_current / Normalization.MaxKm), 0, 1)
        : -1;
    var kmFromHome = Math.Clamp((terminal.km_from_home / Normalization.MaxKm), 0, 1);
    var txCount24H = Math.Clamp((customer.tx_count_24h / Normalization.MaxTxCount24H), 0, 1);
    var isOnline = terminal.is_online
        ? 1f
        : 0f;
    var cardPresent = terminal.card_present
        ? 1f
        : 0f;
    var unknownMerchant = customer.known_merchants.Any(x => string.Equals(x, merchant.id))
        ? 0f
        : 1f;
    var mccRisk = MccRisk.GetValue(merchant.mcc);
    var merchantAvgAmount = Math.Clamp((merchant.avg_amount / Normalization.MaxMerchantAvgAmount), 0, 1);

    float[] vector =
    [
        amount, installments, amountVsAvg, hourOfDay, dayOfWeek, minutesSinceLastTx, kmFromLastTx,
        kmFromHome, txCount24H, isOnline, cardPresent, unknownMerchant, mccRisk, merchantAvgAmount
    ];

    // Console.WriteLine("Start");
    // var sw = Stopwatch.StartNew();
    var topScores = new TopScores();
    FraudManager.CalcTopRegisters(vectorBases, vector, topScores);
    // sw.Stop();
    // Console.WriteLine($"{sw.ElapsedMilliseconds} ms");
    // Console.WriteLine("Finished");
    
    var (decision, fraudScore) = FraudManager.Detect(topScores);
    var response = new FraudScoreResponse(decision, fraudScore);
    
    return Results.Ok(response);
});

fraudeApi.MapGet("/ready", IResult () => Results.Ok());
fraudeApi.Run();