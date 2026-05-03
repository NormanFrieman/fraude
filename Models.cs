using System.Text.Json.Serialization;
namespace Fraude;

public record Transaction(float amount, int installments, DateTime requested_at);

public record Customer(float avg_amout, int tx_count_24h, string[] know_merchants);

public record Merchant(string id, int mcc, float avg_amount);

public record Terminal(bool is_online, bool card_present, float km_from_home);

public record LastTransaction(DateTime timestamp, float km_from_current);

public record FraudScore(string id, Transaction transaction, Customer customer, Merchant merchant, Terminal terminal, LastTransaction? last_transaction);

public record FraudScoreResponse(bool approved, float fraud_score);

public record VectorBase(float[] vector, string label);

[JsonSerializable(typeof(Transaction))]
[JsonSerializable(typeof(Customer))]
[JsonSerializable(typeof(Merchant))]
[JsonSerializable(typeof(Terminal))]
[JsonSerializable(typeof(LastTransaction))]
[JsonSerializable(typeof(FraudScore))]
[JsonSerializable(typeof(VectorBase[]))]
[JsonSerializable(typeof(FraudScoreResponse))]
internal partial class SharedJsonContext : JsonSerializerContext { }