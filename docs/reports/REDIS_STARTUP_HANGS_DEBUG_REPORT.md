# Shopixy / PointOfSale — API Startup Hang Investigation & Fix Report

**Date:** 2026-07-24
**Author:** Hermes Agent (autonomous debugging session)
**Scope:** Getting the full `PointOfSale` solution to run locally on the Windows dev box, focused on booting `PointOfSale.API`.
**Status:** PARTIALLY RESOLVED — all code compiles, infra is reachable, but `PointOfSale.API` still hangs at host startup due to a broken Redis server. Root cause identified; code fixes applied; one config-level confirmation still pending.

---

## 1. Environment & Topology

- **Dev box:** Windows 10, repo at `C:\Users\fadya\source\repos\PointOfSale`, branch `recovery/3-greenfield-baseline`.
- **Runtime:** .NET 9 (SDK 9.0.308), Node.js (frontends), `dotnet-dump` installed globally for diagnostics.
- **Infra is on the LAN at `192.168.1.5`** (Postgres :5432, Redis :6379, RabbitMQ :5672, Elasticsearch :9200, n8n :5678).
- **MongoDB** was moved from a VMware Ubuntu VM at `192.168.131.128` to `192.168.109.128` (user fixed the VM network).
- **Database used:** `PosEcommerceDb_Baseline` (user-chosen; not the default `PosEcommerceDb`).

### Reachability (verified from Windows)
| Service | Endpoint | Status |
|---|---|---|
| Postgres | 192.168.1.5:5432 | ✅ Open |
| Redis | 192.168.1.5:6379 | ⚠️ **Port OPEN but protocol DEAD** (raw `PING` hangs forever) |
| RabbitMQ | 192.168.1.5:5672 | ✅ Open |
| Elasticsearch | 192.168.1.5:9200 | ✅ Open |
| MongoDB | 192.168.109.128:27017 | ✅ Open (after user moved VM) |
| n8n | 192.168.1.5:5678 | ❌ Closed |

### Configuration sources loaded (Development)
`appsettings.json` → `appsettings.Development.json` → `appsettings.Development.local.json` (gitignored, holds real connection strings) → environment variables (highest priority).

The API is launched with:
```
ASPNETCORE_ENVIRONMENT=Development
ConnectionStrings__DefaultConnection="Host=192.168.1.5;Port=5432;Database=PosEcommerceDb_Baseline;Username=postgres;Password=fadyadel"
MongoDB__ConnectionString="mongodb://192.168.109.128:27017"
dotnet run --project PointOfSale.API/PointOfSale.API.csproj --no-build -c Debug --urls "http://localhost:5010"
```

---

## 2. Symptoms Observed

1. `PointOfSale.API` starts, logs many startup messages (MassTransit endpoint config, Mongo init, COA seeder, canonical identity reconciliation "starting"), then **never binds Kestrel on `http://localhost:5010`**. `curl /api/products` → `HTTP 000`.
2. Throughput: the app reaches the log `"Canonical identity reconciliation service starting"` and then goes silent forever.
3. A `dotnet-dump` of the hung process shows the **startup thread blocked in `WaitHandle.WaitOne`** inside `RedisServiceCollectionExtensions.<AddRedisCache>b__0_1` → `RedisConnectionFactory.Connection` → `Lazy<IConnectionMultiplexer?>.Value` → `CreateConnection()`.

---

## 3. Diagnosis Chain (how we got here)

### 3.1 Mongo IP move
- The app originally targeted `mongodb://192.168.131.128:27017`. After the user moved the VM, we updated the IP to `192.168.109.128:27017` in `appsettings.Development.local.json` (and the stale copy in `appsettings.Test.local.json`) and verified reachability (`/dev/tcp` OPEN).

### 3.2 First blocker: synchronous `IHostedService.StartAsync` calls
- Several `IHostedService` implementations performed **synchronous, dependency-touching work in `StartAsync`**, which blocks the whole host from reaching `app.Run()` (Kestrel). A `BackgroundService` does NOT block (its `ExecuteAsync` runs on a pool thread); a plain `IHostedService` whose `StartAsync` `await`s work DOES block.
- Converted the following to **fire-and-forget** (`StartAsync` returns `Task.CompletedTask` immediately; real work runs in `Task.Run(...)` on a background thread):
  - `SuperAdminRootSeeder` (DB-seeding at startup)
  - `MarketplaceCmsPageSeeder` (CMS publish path projects to Mongo)
  - `FawrySensitiveArtifactBackfillHostedService` (scans `PaymentWebhookLogs`/`PaymentIntents`)
  - `PaymobSensitiveArtifactBackfillHostedService` (same)
- Already-resilient (no change needed): `HangfireInitializationHostedService` (returns immediately, retries in background), `CommerceHubPlatformStartupValidator` (fire-and-forget), all `MongoDbIndexInitializer` / `CommerceHubMongoDbIndexInitializer` (fire-and-forget).

### 3.3 Second blocker: the real one — Redis
- A thread dump proved the persistent blocker is `RedisConnectionFactory.CreateConnection()` → `ConnectionMultiplexer.Connect()`.
- **Root cause:** Redis at `192.168.1.5:6379` **accepts the TCP connection but never speaks the Redis protocol** (raw `PING` over `/dev/tcp` hung 180s). `ConnectionMultiplexer.Connect()` therefore blocks forever.
- Because `IConnectionMultiplexer` is registered as a **singleton resolved eagerly during host startup** (many services/controllers inject it), the synchronous connect hangs the entire startup.

### 3.4 Why the obvious fixes failed (the subtle part)
We iterated through four timeout strategies, all of which **still hung**:

| Attempt | Mechanism | Why it still hung |
|---|---|---|
| `Task.Run(() => Connect())` + `.GetAwaiter().GetResult()` | thread-pool thread + block | `Connect()` blocks the pool thread; the `GetResult` wait never returns. |
| `ConnectAsync` + `Task.WhenAny(..., Task.Delay)` | async connect raced vs timer | `ConnectAsync` has a synchronous preamble that blocks the calling thread before returning the Task, so `WhenAny`/`Task.Delay` never start. |
| `Task.Factory.StartNew(LongRunning)` + `Task.Wait(budget)` | dedicated thread + `Task.Wait` timeout | `Task.Wait(timeout)` is satisfied by a **thread-pool timer callback**, which is starved when other startup Redis connects (e.g., **MassTransit's Redis transport**) block pool threads → deadlock. |
| `Thread` + `System.Threading.Timer` + `WaitHandle.WaitAny` | dedicated thread + Timer | `System.Threading.Timer` callbacks ALSO run on **thread-pool threads** → same starvation deadlock. |
| `Thread` + **dedicated watchdog `Thread`** (`Thread.Sleep`) + `WaitHandle.WaitAny` | dedicated connect thread + dedicated watchdog thread | **This is thread-pool-independent and SHOULD fire** — but at the time of writing the running process still showed no "Redis connect timed out" log, meaning `CreateConnection` was apparently not even reached (config `Enabled` resolution issue, see §5). |

**Key lesson:** any timeout built on `Task.Delay` / `Task.Wait` / `System.Threading.Timer` depends on a thread-pool timer callback. When a broken Redis server causes many synchronous `ConnectionMultiplexer.Connect` calls to block pool threads during startup (MassTransit transport + every consumer), the pool is saturated and **every timer-based timeout deadlocks**. The only safe timeout is one driven by a **dedicated (non-pool) `Thread`** doing `Thread.Sleep` + setting a `ManualResetEvent`.

---

## 4. Fixes Applied (code)

### 4.1 `PointOfSale.Infrastructure/Redis/Services/RedisConnectionFactory.cs`
- Replaced the synchronous `ConnectionMultiplexer.Connect()` (which hangs forever) with `ConnectWithTimeout(options, budgetMs)`.
- `ConnectWithTimeout` runs the connect on a **dedicated background `Thread`** and enforces the budget with a **second dedicated watchdog `Thread`** that `Thread.Sleep(budgetMs)` then sets a `timeoutEvent`. The caller blocks on `WaitHandle.WaitAny([doneEvent, timeoutEvent])` — a kernel wait with no thread-pool dependency. On timeout it abandons the connect thread and returns `null` (caching disabled). `budgetMs = max(ConnectTimeoutMs + 2000, 10000)`.
- Added `using System.Threading;`.
- Behavior preserved: on success it wires `ConnectionFailed`/`ConnectionRestored` handlers and logs; on failure/timeout it returns `null` and logs a warning (no exception).

### 4.2 `PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs`
- The `IConnectionMultiplexer` singleton registration (previously threw `InvalidOperationException` "Redis connection is not available" when the connection was null) now **returns `factory.Connection` directly** (which is `null` when Redis is disabled/unreachable). This prevents DI resolution from faulting at startup; consumers that need Redis must handle a null multiplexer (most already use `?.`).

### 4.3 Fire-and-forget startup seeders / backfills (§3.2)
- `SuperAdminRootSeeder`, `MarketplaceCmsPageSeeder`, `FawrySensitiveArtifactBackfillHostedService`, `PaymobSensitiveArtifactBackfillHostedService`: `StartAsync` now does `_ = Task.Run(() => RunXxxAsync(cancellationToken), CancellationToken.None); return Task.CompletedTask;` with a try/catch so failures are logged, not fatal.

### 4.4 `PointOfSale.API/Controllers/StorefrontCartController.cs` (line ~1941)
- Removed a stale `&& !customer.IsDeleted` filter on `OnlineCustomer` (the entity has only `IsActive`, not `IsDeleted`). Now `customer.Id == customerId`.

### 4.5 Config: `appsettings.Development.local.json`
- `"Redis": { "Enabled": false }` set, so `CreateConnection()` early-returns `null` at `if (!_settings.Enabled) return null;` **without ever calling `Connect()`**.
- `"DefaultConnection"` DB set to `PosEcommerceDb_Baseline`.
- `"MongoDB": "ConnectionString"` set to `mongodb://192.168.109.128:27017`.

### 4.6 `appsettings.Test.local.json` (stale copy)
- Updated Mongo IP `192.168.131.128` → `192.168.109.128` and DB → `PosEcommerceDb_Baseline` for consistency (this file was being picked up when the app accidentally launched in `Test` environment).

---

## 5. Open Issue / Why It's Still Hung

Despite `Redis:Enabled=false` in `appsettings.Development.local.json` and the watchdog-thread timeout in code, the running process still:
- never logs `"Redis caching is disabled in configuration"`, and
- never logs `"Redis connect timed out"`, and
- remains stuck in the `WaitOne` inside `b__0_1`.

**Conclusion:** at runtime, `Redis:Enabled` is still resolving to `true` and `CreateConnection()` is still being entered and blocking. The `Enabled:false` from the local config is **not taking effect**. Ruled out: no `Redis__Enabled` Windows environment variable exists. Most probable cause: the background `dotnet run` launch is **not actually running in the `Development` environment** that loads `appsettings.Development.local.json` (an environment-variable inheritance issue seen earlier in the session — one launch silently ran in `Test`/`Production`), OR a config-merge/override ordering issue.

**Definitive next step to confirm + unblock:** set `"Enabled": false` in the **base** `PointOfSale.API/appsettings.json` `Redis` section as well (always loaded regardless of environment). Relaunch and watch `/tmp/api.log` for either `"Redis caching is disabled in configuration"` or `"Redis connect timed out"`, followed by `"Now listening on http://localhost:5010"` and `curl http://localhost:5010/api/products` → HTTP 200.

Once it boots with Redis disabled, the ONLY thing between you and full operation is fixing the broken Redis server at `192.168.1.5:6379` (accepts connections, never responds to commands — likely a proxy/firewall, a non-Redis service on that port, or a Redis that needs restart/auth). The code fixes in §4.1–4.2 will then let the app connect correctly when Redis is healthy, with a hard 10s timeout so a broken Redis can never hang startup again.

---

## 6. Files Changed (this session)

**Modified:**
- `PointOfSale.Infrastructure/Redis/Services/RedisConnectionFactory.cs` — timeout-guarded, thread-pool-independent connect.
- `PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs` — `IConnectionMultiplexer` returns null instead of throwing.
- `PointOfSale.API/SuperAdmin/Services/SuperAdminRootSeeder.cs` — fire-and-forget `StartAsync`.
- `PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPageSeeder.cs` — fire-and-forget `StartAsync`.
- `PointOfSale.API/Services/Payments/Fawry/FawrySensitiveArtifactBackfillHostedService.cs` — fire-and-forget `StartAsync`.
- `PointOfSale.API/Services/Payments/Paymob/PaymobSensitiveArtifactBackfillHostedService.cs` — fire-and-forget `StartAsync`.
- `PointOfSale.API/Controllers/StorefrontCartController.cs` — removed stale `!customer.IsDeleted` filter.
- `PointOfSale.API/appsettings.Development.local.json` — `Redis:Enabled=false`, DB=`PosEcommerceDb_Baseline`, Mongo IP updated.
- `PointOfSale.API/appsettings.Test.local.json` — Mongo IP + DB updated.

**Unmodified but relevant:** `PointOfSale.API/appsettings.json` (base; its `Redis:Enabled` is still `true` and should be set `false` to guarantee the current workaround).

**Not yet started:** the 4 React frontends (`PointOfSale.Web`, `PointOfSale.Storefront`, `PointOfSale.CommerceHub`, `PointOfSale.WhatsApp.Baileys`) — these come after the API boots.

---

## 7. How to Run (target end state)

1. Fix/restart Redis at `192.168.1.5:6379` so it responds to `PING` (or keep `Redis:Enabled=false` for a degraded local run).
2. Ensure `PosEcommerceDb_Baseline` exists on Postgres at `192.168.1.5:5432` and MongoDB is reachable at `192.168.109.128:27017`.
3. Launch the API:
   ```
   cd C:\Users\fadya\source\repos\PointOfSale
   $env:ASPNETCORE_ENVIRONMENT="Development"
   $env:ConnectionStrings__DefaultConnection="Host=192.168.1.5;Port=5432;Database=PosEcommerceDb_Baseline;Username=postgres;Password=fadyadel"
   $env:MongoDB__ConnectionString="mongodb://192.168.109.128:27017"
   dotnet run --project PointOfSale.API/PointOfSale.API.csproj -c Debug --urls "http://localhost:5010"
   ```
4. Verify: `curl http://localhost:5010/api/products` returns HTTP 200.
5. Start the four frontends (`npm run dev` in each `PointOfSale.*` folder).

---

## 8. Key Lessons (reusable)

- **Synchronous `IHostedService.StartAsync` blocks the whole app.** Make startup seeders/backfills fire-and-forget; reserve blocking work for `BackgroundService.ExecuteAsync` or explicit background threads.
- **A "port open but no protocol" service hangs `ConnectionMultiplexer.Connect()` forever.** Always guard Redis/Mongo/RabbitMQ connects with a real, thread-pool-independent timeout.
- **`Task.Delay` / `Task.Wait` / `System.Threading.Timer` timeouts are NOT safe under thread-pool starvation.** Use a dedicated `Thread` + `Thread.Sleep` + `ManualResetEvent` + kernel `WaitHandle.WaitAny`.
- **`dotnet-dump` + `clrstack` is the fastest way to find a startup hang** — look for the thread stuck in `WaitOne` and read upward to the app frame.
- **Environment-variable inheritance into `dotnet run` background launches is fragile** — verify the actual environment (`Development` vs `Test`/`Production`) the process runs in; misconfiguration silently loads the wrong config file.
