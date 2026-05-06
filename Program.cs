using System.Runtime.InteropServices;
using Fraude.Models;
using Fraude.Tools;
using SharedJsonContext = Fraude.Models.SharedJsonContext;


#region Read reference base

const string path = "References/refs_native.bin";
const int weightsLength = 14;
const int recordSize = (weightsLength * sizeof(float)) + 1;

var rawBytes = File.ReadAllBytes(path);
var fileSize = rawBytes.Length - sizeof(int);
var count = fileSize / recordSize;
ReadOnlySpan<byte> body = rawBytes.AsSpan(4);

var expectedBodySize = count * recordSize;
if (body.Length != expectedBodySize)
    throw new Exception();

var allVectors = new float[count * weightsLength];
var allFrauds = new bool[count];

for (var i = 0; i < count; i++)
{
    var recordOffset = i * recordSize;

    var src = MemoryMarshal.Cast<byte, float>(body.Slice(recordOffset, weightsLength * sizeof(float)));
    src.CopyTo(allVectors.AsSpan(i * weightsLength));
    allFrauds[i] = body[recordOffset + (weightsLength * sizeof(float))] == 1;
}

#endregion

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = SharedJsonContext.Default;
});

var fraudeApi = builder.Build();

fraudeApi.MapPost("/fraud-score", (
    FraudScore score) =>
{
    #region Normalize values and store in vector

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
        ? Math.Clamp((float)(DateTime.UtcNow - lastTransaction.timestamp).TotalMinutes / Normalization.MaxMinutes, 0, 1)
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

    Span<float> vector = stackalloc float[14];
    vector[0] = amount;
    vector[1] = installments;
    vector[2] = amountVsAvg;
    vector[3] = hourOfDay;
    vector[4] = dayOfWeek;
    vector[5] = minutesSinceLastTx;
    vector[6] = kmFromLastTx;
    vector[7] = kmFromHome;
    vector[8] = txCount24H;
    vector[9] = isOnline;
    vector[10] = cardPresent;
    vector[11] = unknownMerchant;
    vector[12] = mccRisk;
    vector[13] = merchantAvgAmount;

    #endregion

    #region Calc euclidian distance

    Span<(float, bool)> topScore = stackalloc (float, bool)[5];
    var length = 0;
    
    for (var i = 0; i < count; i++)
    {
        ReadOnlySpan<float> refVector = allVectors.AsSpan(i * weightsLength, weightsLength);
        var isFraud = allFrauds[i];
        
        var dist = FraudManager.Euclidean(refVector, vector);
        FraudManager.Add(dist, isFraud, topScore, ref length);
    }

    #endregion
    
    var (decision, fraudScore) = FraudManager.Detect(topScore);
    var response = new FraudScoreResponse(decision, fraudScore);
    
    return response;
});

fraudeApi.MapGet("/ready", IResult () => Results.Ok());
fraudeApi.Run();