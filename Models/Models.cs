using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Fraude.Models;

public record Transaction(float amount, int installments, DateTime requested_at);

public record Customer(float avg_amount, int tx_count_24h, string[] known_merchants);

public record Merchant(string id, string mcc, float avg_amount);

public record Terminal(bool is_online, bool card_present, float km_from_home);

public record LastTransaction(DateTime timestamp, float km_from_current);

public record FraudScore(string id, Transaction transaction, Customer customer, Merchant merchant, Terminal terminal, LastTransaction? last_transaction);

public record FraudScoreResponse(bool approved, float fraud_score);

// public record struct VectorBase(float[] vector, bool isFraud);

[JsonSerializable(typeof(Transaction))]
[JsonSerializable(typeof(Customer))]
[JsonSerializable(typeof(Merchant))]
[JsonSerializable(typeof(Terminal))]
[JsonSerializable(typeof(LastTransaction))]
[JsonSerializable(typeof(FraudScore))]
// [JsonSerializable(typeof(ImmutableArray<VectorBase>))]
[JsonSerializable(typeof(FraudScoreResponse))]
internal partial class SharedJsonContext : JsonSerializerContext { }