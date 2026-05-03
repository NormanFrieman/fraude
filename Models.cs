namespace Fraude;

public record Transaction(float amount, int installments, DateTime requested_at);

public abstract record Customer(float avg_amout, int tx_count_24h, string[] know_merchants);

public abstract record Merchant(string id, int mcc, float avg_amount);

public abstract record Terminal(bool is_online, bool card_present, float km_from_home);

public abstract record LastTransaction(DateTime timestamp, float km_from_current);

public record FraudScore(string id, Transaction transaction, Customer customer, Merchant merchant, Terminal terminal, LastTransaction? last_transaction);

public record FraudScoreResponse(bool approved, float fraud_score);