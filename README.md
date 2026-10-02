# PaymentDemo

A small .NET 10 solution that demonstrates how a backend service takes payments through an external payment provider **safely**, i.e. without double-charging a customer when a client retries a request.

The central idea is **idempotency**: every "create payment" call carries an `Idempotency-Key` header. If the same request is sent twice (network glitch, client timeout, user double-clicks "Pay"), the API recognises the key and returns the original result instead of charging again.

---

## Contents

- [Solution overview](#solution-overview)
- [How a payment is created](#how-a-payment-is-created)
- [Idempotency rules](#idempotency-rules)
- [API reference](#api-reference)
- [Data model](#data-model)
- [Project structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Setup](#setup)
- [Running the application](#running-the-application)
- [Trying it out](#trying-it-out)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)

---

## Solution overview

The solution (`PaymentDemo.sln`) contains two ASP.NET Core Web API projects and a SQL Server database:

```
 ┌──────────┐  POST /api/payments   ┌──────────────┐  POST /api/charges  ┌──────────────────────┐
 │  Client  │ ────────────────────► │  Payment.Api │ ──────────────────► │ PaymentProvider.Mock │
 │ (Postman)│  Idempotency-Key: …   │  :7085/:5190 │                     │      :7111/:5048     │
 └──────────┘                       └──────┬───────┘                     └──────────────────────┘
                                           │ EF Core
                                           ▼
                                  ┌──────────────────┐
                                  │ SQL Server       │
                                  │ LocalDB          │
                                  │ DB: PaymentDemo  │
                                  └──────────────────┘
```

| Component | Role |
|---|---|
| **Payment.Api** | The service you own. Accepts payment requests, enforces idempotency, stores payments in SQL Server, and calls the provider to actually charge the money. |
| **PaymentProvider.Mock** | Stands in for a real payment gateway (Stripe, Adyen, …). Approves any charge of **10,000 or less** and declines anything above that. Returns a provider reference such as `prov_4376ef8d…`. |
| **SQL Server LocalDB** (`PaymentDemo` database) | Holds two tables: `Payments` and `IdempotencyRequests`. Schema is managed by EF Core migrations. |
| **Postman collection** (`PaymentDemo/`) | Ready-made requests for both APIs, stored in Postman's local-files format. |

---

## How a payment is created

`POST /api/payments` is handled by `PaymentsController.Create` → `PaymentService.CreateAsync`:

1. **Validate input.** The request body is validated by data annotations (`OrderId` required, `Amount > 0`, `Currency` is 3 letters). The `Idempotency-Key` header must be present and at most 100 characters. Failures return **400**.
2. **Hash the request.** `RequestHasher` builds a canonical string `OrderId|Amount|CURRENCY` and computes a SHA-256 hash. This lets the API detect a key being reused with a *different* body.
3. **Look up the idempotency key** in `IdempotencyRequests`.
   - **Not found** → insert a new row with status `Processing`. A unique index on `IdempotencyKey` guarantees that if two identical requests race, only one insert wins; the loser catches the `DbUpdateException` and re-reads the winner's row.
   - **Found** → apply the [idempotency rules](#idempotency-rules) below and possibly return early.
4. **Create the payment** row with status `Pending` *before* calling the provider, so there is always a record of the attempt.
5. **Charge via the provider** (`PaymentProviderClient` → `POST {PaymentProvider:BaseUrl}/api/charges`).
   - `Approved` → payment status `Succeeded`, `ProviderReference` saved.
   - `Declined` → payment status `Declined`.
   - Network error, timeout, non-2xx, or empty response → payment status `Failed` (the error is logged).
6. **Complete the idempotency record**: status `Completed`, link to the `PaymentId`, store response code `201` and the serialized payment.
7. **Return 201 Created** with the payment and a `Location` header pointing to `GET /api/payments/{id}`.

> Note: a declined or failed charge still returns **201**. The HTTP status says "a payment record was created"; the business outcome is in the payment's `status` field.

---

## Idempotency rules

| Situation | Response |
|---|---|
| New key | Payment is processed → **201 Created** |
| Same key, same body, original request **completed** | Original payment returned, provider **not** called again → **200 OK** |
| Same key, same body, original request **still processing** | **409 Conflict** (client should retry later) |
| Same key, **different** body | **409 Conflict** (key misuse) |
| Missing or empty `Idempotency-Key` | **400 Bad Request** |
| Key longer than 100 characters | **400 Bad Request** |

---

## API reference

### Payment.Api

Base URLs: `https://localhost:7085` (https profile) or `http://localhost:5190`. HTTP requests are redirected to HTTPS by `UseHttpsRedirection`.

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/payments` | Create a payment. Requires `Idempotency-Key` header. |
| `GET` | `/api/payments/{id}` | Get a payment by its GUID. **404** if not found. |
| `GET` | `/api/payments/by-order/{orderId}` | List all payments for an order. |
| `GET` | `/openapi/v1.json` | OpenAPI document (Development environment only). |

Example request:

```http
POST https://localhost:7085/api/payments
Content-Type: application/json
Idempotency-Key: 3f2c9a7e-1b4d-4c55-9e0a-7d1f2b6c8a90

{
  "orderId": "ORD-1001",
  "amount": 49.99,
  "currency": "USD"
}
```

Example response (`201 Created`):

```json
{
  "id": "d011a757-bdcd-4c06-85c4-7b76318f71e8",
  "orderId": "ORD-1001",
  "amount": 49.99,
  "currency": "USD",
  "status": 1,
  "providerReference": "prov_0e5cd9f1143c4b28b576878162fa2b27",
  "createdAt": "2026-10-02T13:18:10.8881994+00:00"
}
```

`status` is serialized as a number:

| Value | Name | Meaning |
|---|---|---|
| 0 | `Pending` | Created, provider not yet answered |
| 1 | `Succeeded` | Provider approved the charge |
| 2 | `Declined` | Provider declined the charge |
| 3 | `Failed` | Provider could not be reached or returned an error |
| 4 | `Unknown` | Provider returned an unrecognised status |

### PaymentProvider.Mock

Base URLs: `https://localhost:7111` or `http://localhost:5048`.

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/charges` | Body `{ orderId, amount, currency }`. Returns `{ providerReference, status, amount, currency, processedAt }` where `status` is `"Approved"` (amount ≤ 10,000) or `"Declined"` (amount > 10,000). Invalid input returns **400**. |

The mock is stateless: it does not store anything and will happily charge the same order twice. Preventing duplicates is Payment.Api's job.

---

## Data model

Created by the EF Core migrations in `Payment.Api/Migrations`:

**`Payments`**

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | Primary key |
| `OrderId` | `nvarchar(max)` | |
| `Amount` | `decimal(18,2)` | |
| `Currency` | `nvarchar(max)` | Stored upper-case |
| `Status` | `int` | `PaymentStatus` enum |
| `ProviderReference` | `nvarchar(max)` | Null until the provider responds |
| `CreatedAt` | `datetimeoffset` | |

**`IdempotencyRequests`**

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | Primary key |
| `IdempotencyKey` | `nvarchar(100)` | **Unique index** — the core of the race-condition protection |
| `RequestHash` | `varchar(64)` | SHA-256 hex of the canonical request |
| `PaymentId` | `uniqueidentifier` | FK → `Payments.Id` (restrict delete), null while processing |
| `Status` | `int` | `Processing` (0), `Completed` (1), `Failed` (2) |
| `ResponseCode` | `int` | e.g. 201 |
| `ResponseBody` | `nvarchar(max)` | Serialized payment JSON |
| `CreatedAt` / `UpdatedAt` | `datetimeoffset` | |

> The `.sql` files in the `Database/` folder are early hand-written drafts and **do not match** the EF model (e.g. they define `Status` as `NVARCHAR`). Use the EF migrations to create the schema.

---

## Project structure

```
PaymentDemo/
├── PaymentDemo.sln
├── Payment.Api/                     # The payment service
│   ├── Controllers/PaymentsController.cs
│   ├── Services/
│   │   ├── PaymentService.cs        # Orchestration + idempotency logic
│   │   ├── PaymentProviderClient.cs # Typed HttpClient for the provider
│   │   └── RequestHasher.cs         # SHA-256 request fingerprint
│   ├── Data/
│   │   ├── PaymentDbContext.cs      # EF Core model configuration
│   │   ├── EfPaymentStore.cs        # IPaymentStore implementation
│   │   ├── EfIdempotencyStore.cs    # IIdempotencyStore implementation
│   │   ├── PaymentRecord.cs
│   │   └── IdempotencyRequest.cs
│   ├── Models/                      # Request DTOs (incoming + provider)
│   ├── Migrations/                  # EF Core migrations
│   ├── appsettings.json             # Connection string + provider URL
│   └── Payment.Api.http             # Quick requests for Rider/VS
├── PaymentProvider.Mock/            # Fake payment gateway
│   ├── Controllers/ChargesController.cs
│   └── Models/
├── Database/                        # Draft SQL scripts (see note above)
├── PaymentDemo/                     # Postman collection (local files)
└── .postman/resources.yaml          # Postman workspace link
```

---

## Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | 10.0 or later | `dotnet --list-sdks` |
| SQL Server LocalDB | any recent (ships with Visual Studio / SQL Server Express) | `sqllocaldb info` |
| EF Core CLI | 10.x | `dotnet tool list -g` |
| Postman (optional) | v11+ desktop app | |
| IDE (optional) | JetBrains Rider or Visual Studio 2022+ | |

Install the EF Core CLI if you don't have it:

```powershell
dotnet tool install --global dotnet-ef
```

---

## Setup

All commands are run from the solution folder `C:\Projects\PaymentDemo`.

### 1. Restore and build

```powershell
dotnet restore
dotnet build
```

### 2. Trust the HTTPS development certificate

Both APIs use HTTPS and the Payment API calls the provider over HTTPS, so the dev certificate must be trusted:

```powershell
dotnet dev-certs https --trust
```

### 3. Start LocalDB

```powershell
sqllocaldb start MSSQLLocalDB
```

### 4. Create the database

The connection string in `Payment.Api/appsettings.json` is:

```
Server=(localdb)\MSSQLLocalDB;Database=PaymentDemo;Trusted_Connection=True;TrustServerCertificate=True
```

Apply the migrations to create the `PaymentDemo` database and both tables:

```powershell
dotnet ef database update --project Payment.Api
```

### 5. (Recommended) Turn off AUTO_CLOSE

LocalDB databases default to `AUTO_CLOSE ON`, which makes the first request after an idle period take 20–30+ seconds and can make clients time out:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "ALTER DATABASE PaymentDemo SET AUTO_CLOSE OFF"
```

### 6. Check configuration

`Payment.Api/appsettings.json` must point at the provider:

```json
"PaymentProvider": {
  "BaseUrl": "https://localhost:7111"
}
```

If you run the provider with the `http` profile instead, change this to `http://localhost:5048`.

---

## Running the application

**Both projects must be running at the same time.** Start the provider first.

### Option A — command line (two terminals)

```powershell
# Terminal 1 — payment provider mock
dotnet run --project PaymentProvider.Mock --launch-profile https
```

```powershell
# Terminal 2 — payment API
dotnet run --project Payment.Api --launch-profile https
```

You should see:

| Service | URLs |
|---|---|
| PaymentProvider.Mock | `https://localhost:7111`, `http://localhost:5048` |
| Payment.Api | `https://localhost:7085`, `http://localhost:5190` |

### Option B — Rider

1. Open `PaymentDemo.sln`.
2. Create a **Compound** run configuration (Run → Edit Configurations → `+` → Compound) containing `PaymentProvider.Mock: https` and `Payment.Api: https`.
3. Run or Debug the compound configuration.

### Option C — Visual Studio

1. Open `PaymentDemo.sln`.
2. Right-click the solution → **Configure Startup Projects** → **Multiple startup projects**.
3. Set both `PaymentProvider.Mock` and `Payment.Api` to **Start**, with the `https` profile.
4. Press F5.

---

## Trying it out

### With curl

```bash
# Create a payment (approved)
curl -k -X POST https://localhost:7085/api/payments \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-key-1" \
  -d '{"orderId":"ORD-1001","amount":49.99,"currency":"USD"}'

# Send the exact same request again -> 200 OK, same payment id, no second charge
curl -k -X POST https://localhost:7085/api/payments \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-key-1" \
  -d '{"orderId":"ORD-1001","amount":49.99,"currency":"USD"}'

# Same key, different amount -> 409 Conflict
curl -k -X POST https://localhost:7085/api/payments \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-key-1" \
  -d '{"orderId":"ORD-1001","amount":99.99,"currency":"USD"}'

# Amount above 10,000 -> 201 with status 2 (Declined)
curl -k -X POST https://localhost:7085/api/payments \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-key-2" \
  -d '{"orderId":"ORD-1002","amount":15000,"currency":"USD"}'

# Look up payments for an order
curl -k https://localhost:7085/api/payments/by-order/ORD-1001
```

### With Postman

1. In the Postman desktop app, open the local collection in the `PaymentDemo/` folder (linked via `.postman/resources.yaml`).
2. The collection variables default to the **http** profile URLs. Either keep them or change them to the https URLs:

   | Variable | http profile | https profile |
   |---|---|---|
   | `paymentApiBaseUrl` | `http://localhost:5190` | `https://localhost:7085` |
   | `providerBaseUrl` | `http://localhost:5048` | `https://localhost:7111` |

   If you use https and haven't trusted the dev certificate, turn off *Settings → General → SSL certificate verification*.
3. Requests included:
   - **Payment.Api – Payments**: Create Payment, Create Payment – Declined (amount > 10,000), Create Payment – Invalid (400), Get Payment By Id, Get Payments By Order Id.
   - **PaymentProvider.Mock – Charges**: Charge – Approved, Charge – Declined, Charge – Invalid (400).
4. Create requests send `Idempotency-Key: {{$guid}}`, so each send is a new payment. To see replay behaviour, replace `{{$guid}}` with a fixed value and send twice.
5. "Create Payment" saves the returned `id` into the `paymentId` collection variable, so "Get Payment By Id" works immediately afterwards.

### With the `.http` file

`Payment.Api/Payment.Api.http` can be run from Rider or Visual Studio. Add an `Idempotency-Key` header to the POST before using it.

---

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `POST /api/payments` returns **400** "Idempotency-Key header is required" | Add the `Idempotency-Key` header. |
| Payment created with status **3 (Failed)** | Payment.Api could not reach the provider. Make sure PaymentProvider.Mock is running and `PaymentProvider:BaseUrl` matches its URL. Check the Payment.Api console for "Payment provider charge failed". |
| First request takes 20–30 s or the client times out | LocalDB was idle and the database was auto-closed / the instance stopped. Run setup step 5 and send a warm-up request. |
| Postman: *"Could not send request — Unhandled Promise Rejection … Operation timed out"* and no breakpoint is hit | Postman's internal agent failed to start; the request never left Postman. Fully quit Postman (all `Postman.exe` processes) and restart it, or update Postman. Verify the API with curl. |
| SSL / certificate errors | Run `dotnet dev-certs https --trust`, or use the http URLs. |
| `Cannot open database "PaymentDemo"` | Run `dotnet ef database update --project Payment.Api`. |
| "Address already in use" on start | Another instance is already running; stop it or change ports in `Properties/launchSettings.json`. |

---

## Known limitations

This is a demo, and a few production concerns are deliberately or not-yet handled:

- **Stuck `Processing` records.** If the process crashes (or an unhandled exception occurs) between creating the idempotency row and completing it, that key stays `Processing` forever and every retry gets 409. The `IdempotencyStatus.Failed` value exists but is not used yet. A real system would expire or recover stale `Processing` rows.
- **No transaction** spans the payment insert, the provider call, and the idempotency update.
- **Provider calls are not idempotent.** The mock is not sent the idempotency key; a real provider integration should forward one so the provider can de-duplicate too.
- **Replays always return 200** with the payment, even though the original response was 201. `ResponseCode`/`ResponseBody` are stored but not used for the replay.
- **No authentication/authorization**, rate limiting, or retry/circuit-breaker policy on the provider `HttpClient`.
- **No automated tests.**
