# Fraude

[![Rinha de Backend 2026](https://img.shields.io/badge/Rinha%20de%20Backend-2026-blue)](https://github.com/zanfranceschi/rinha-de-backend-2026)
[![C#](https://img.shields.io/badge/lang-C%23-239120)](https://dotnet.microsoft.com/)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)

A **Fraude** is a C# implementation of the [Rinha de Backend 2026](https://github.com/zanfranceschi/rinha-de-backend-2026) challenge — **Fraud Detection using Vector Search**.

The server receives a transaction with customer, merchant, and terminal data, normalizes it into a 14-dimensional feature vector, and classifies it as fraudulent or legitimate via a K-Nearest Neighbors (K=5) algorithm using Euclidean distance against a pre-computed reference dataset.

---

## Architecture

```
┌─────────┐       HTTP       ┌──────────┐
│  nginx  │ ──────────────→  │  api1    │  (0.4 CPU · 160 MB)
│ (LB)    │                  ├──────────┤
│ 0.2 CPU │ ──────────────→  │  api2    │  (0.4 CPU · 160 MB)
│ 30 MB   │                  └──────────┘
└─────────┘
```

- **nginx** acts as a load balancer, distributing requests between two API instances via HTTP.
- Both API instances share the same reference dataset (`References/refs_native.bin`), loaded at startup.
- Resource limits follow the competition's strict constraints.

---

## Technologies & Key Decisions

| Decision | Rationale |
|---|---|
| **.NET 10 Native AOT** | Configures Native AOT (`<PublishAot>true</PublishAot>`) for minimal memory footprint and fast startup — essential under the 160 MB API limit. |
| **KNN with K=5** | Simple, interpretable, and effective for fraud detection with a labeled reference dataset. Fraud score = proportion of fraudulent neighbors among the 5 closest matches. |
| **SIMD Euclidean Distance** | `System.Numerics.Vector<T>` processes 8+ floats per instruction on modern CPUs (AVX2/AVX-512), making the nearest-neighbor search the critical but optimised path. |
| **HTTP Internal Communication** | Each API instance listens on port 8080; nginx load balances requests across both instances. |
| **Binary Reference File** | `refs_native.bin` stores 100,000 float vectors (14 dimensions each) + fraud labels in a compact, memory-mappable format — no parsing overhead at load time. |
| **`stackalloc` + `Span<T>`** | Zero heap allocations on the hot inference path. The feature vector and the top-5 scores buffer live on the stack. |

---

## Feature Vector (14 Dimensions)

Each transaction is transformed into a normalized vector for distance comparison:

| Index | Feature | Range | Description |
|---|---|---|---|
| 0 | Amount | [0, 1] | Transaction amount / 10000 |
| 1 | Installments | [0, 1] | Installments / 12 |
| 2 | Amount vs Avg | [0, 1] | (amount / customer avg) / 10 |
| 3 | Hour of Day | [0, 1] | Hour / 23 |
| 4 | Day of Week | [0, 1] | Day index / 6 |
| 5 | Minutes Since Last Tx | [0, 1] or -1 | Clamped to 1440 min |
| 6 | Km From Last Tx | [0, 1] or -1 | Clamped to 1000 km |
| 7 | Km From Home | [0, 1] | Terminal distance / 1000 |
| 8 | Tx Count 24H | [0, 1] | Count / 20 |
| 9 | Is Online | {0, 1} | Terminal type |
| 10 | Card Present | {0, 1} | Terminal presence |
| 11 | Unknown Merchant | {0, 1} | 1 if merchant not in known list |
| 12 | MCC Risk | [0, 1] | Pre-defined risk per MCC code |
| 13 | Merchant Avg Amount | [0, 1] | Merchant avg / 10000 |

Features without a `last_transaction` reference receive `-1` (out of range), naturally increasing distance from labeled frauds that do have recent activity.

---

## Endpoints

### `POST /fraud-score`

**Request:**
```json
{
  "id": "tx-123",
  "transaction": {
    "amount": 250.00,
    "installments": 1,
    "requested_at": "2026-05-07T14:30:00Z"
  },
  "customer": {
    "avg_amount": 120.00,
    "tx_count_24h": 3,
    "known_merchants": ["merchant-a", "merchant-b"]
  },
  "merchant": {
    "id": "merchant-x",
    "mcc": 5812,
    "avg_amount": 95.00
  },
  "terminal": {
    "is_online": true,
    "card_present": false,
    "km_from_home": 15.0
  },
  "last_transaction": {
    "timestamp": "2026-05-07T13:00:00Z",
    "km_from_current": 5.0
  }
}
```

**Response:**
```json
{
  "approved": true,
  "fraud_score": 0.4
}
```

- `approved`: `true` if fraud score < 0.6 (i.e., fewer than 3 of 5 nearest neighbors are fraudulent).
- `fraud_score`: proportion of fraudulent neighbors among the 5 closest matches.

### `GET /ready`

Returns `200 OK` when the server is ready to accept requests (used by the load balancer health check).

---

## Project Structure

```
Fraude/
├── Program.cs                 # Application entry point, endpoint definitions
├── Fraude.csproj              # .NET 10 project (Native AOT enabled)
├── Fraude.sln
├── Dockerfile                 # Multi-stage build (SDK → publish → runtime)
├── docker-compose.yml         # nginx + 2 API instances with resource limits
├── nginx.conf                 # nginx load balancer configuration
├── appsettings.json
├── appsettings.Development.json
├── .dockerignore
├── .gitignore
├── Models/
│   └── Models.cs              # Request/response records and JSON serialization
├── Tools/
│   ├── FraudManager.cs        # KNN logic, SIMD Euclidean distance, top-5 heap
│   ├── MccRisk.cs             # Merchant category code risk lookup
│   └── Normalization.cs       # Feature scaling constants
└── References/
    └── refs_native.bin        # Pre-computed reference vectors + fraud labels
```

---

## How to Run

**Prerequisites:** Docker with Compose support.

```bash
docker compose up --build
```

The service will listen on port `9999` (via nginx).

To test:
```bash
curl -X POST http://localhost:9999/fraud-score \
  -H "Content-Type: application/json" \
  -d '{"id":"test","transaction":{"amount":100,"installments":1,"requested_at":"2026-05-07T12:00:00Z"},"customer":{"avg_amount":80,"tx_count_24h":2,"known_merchants":["m1"]},"merchant":{"id":"m1","mcc":5411,"avg_amount":75},"terminal":{"is_online":false,"card_present":true,"km_from_home":2},"last_transaction":{"timestamp":"2026-05-07T10:00:00Z","km_from_current":1}}'
```

---

## References

- [Rinha de Backend 2026 — Challenge Repository](https://github.com/zanfranceschi/rinha-de-backend-2026)
- [Challenge Docs (EN)](https://github.com/zanfranceschi/rinha-de-backend-2026/blob/main/docs/en/README.md)
- [Official Website](https://rinhadebackend.com.br/)
- [Rinha de Backend 2025 — Payment Processor](https://github.com/zanfranceschi/rinha-de-backend-2025)
