using Fraude.Models;
using Fraude.Tools;
using SharedJsonContext = Fraude.Models.SharedJsonContext;

var ivfSearch = IvfSearchEngine.Load("References/refs_ivf_int16.bin");

var builder = WebApplication.CreateSlimBuilder(args);
var unixSocketPath = Environment.GetEnvironmentVariable("FRAUDE_UNIX_SOCKET");
if (!string.IsNullOrEmpty(unixSocketPath))
{
    if (File.Exists(unixSocketPath)) File.Delete(unixSocketPath);
    builder.WebHost.ConfigureKestrel(k => k.ListenUnixSocket(unixSocketPath));
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = SharedJsonContext.Default;
});

var app = builder.Build();
if (!string.IsNullOrWhiteSpace(unixSocketPath) && OperatingSystem.IsLinux())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        SetUnixSocketPermissions(unixSocketPath);
    });
}

app.MapPost("/fraud-score", (FraudScore score) =>
{
    var transaction = score.transaction;
    var customer = score.customer;
    var lastTransaction = score.last_transaction;
    var terminal = score.terminal;
    var merchant = score.merchant;

    var amount = Math.Clamp(transaction.amount / Normalization.MaxAmount, 0, 1);
    var installments = Math.Clamp(transaction.installments / Normalization.MaxInstallments, 0, 1);
    var amountVsAvg = customer.avg_amount > 0
    ? Math.Clamp(transaction.amount / customer.avg_amount / Normalization.AmountVsAvgRatio, 0, 1)
    : 1f;
    var hourOfDay = Math.Clamp(transaction.requested_at.Hour / 23f, 0, 1);
    var dayOfWeekReq = transaction.requested_at.DayOfWeek == DayOfWeek.Sunday
        ? 6
        : (int)transaction.requested_at.DayOfWeek - 1;
    var dayOfWeek = Math.Clamp(dayOfWeekReq / 6f, 0, 1);
    var minutesSinceLastTx = lastTransaction is not null
        ? Math.Clamp((float)(transaction.requested_at - lastTransaction.timestamp).TotalMinutes / Normalization.MaxMinutes, 0, 1)
        : -1;
    var kmFromLastTx = lastTransaction is not null
        ? Math.Clamp(lastTransaction.km_from_current / Normalization.MaxKm, 0, 1)
        : -1;
    var kmFromHome = Math.Clamp(terminal.km_from_home / Normalization.MaxKm, 0, 1);
    var txCount24H = Math.Clamp(customer.tx_count_24h / Normalization.MaxTxCount24H, 0, 1);
    var isOnline = terminal.is_online ? 1f : 0f;
    var cardPresent = terminal.card_present ? 1f : 0f;
    var unknownMerchant = Array.IndexOf(customer.known_merchants, merchant.id) < 0 ? 1f : 0f;
    var mccRisk = MccRisk.GetValue(merchant.mcc);
    var merchantAvgAmount = Math.Clamp(merchant.avg_amount / Normalization.MaxMerchantAvgAmount, 0, 1);

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

    var (approved, fraudScore) = ivfSearch.Search(vector);
    return new FraudScoreResponse(approved, fraudScore);
});

app.MapGet("/ready", IResult () => Results.Ok());
app.Run();

static void SetUnixSocketPermissions(string unixSocketPath)
{
#pragma warning disable CA1416
    File.SetUnixFileMode(
        unixSocketPath,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
#pragma warning restore CA1416
}
