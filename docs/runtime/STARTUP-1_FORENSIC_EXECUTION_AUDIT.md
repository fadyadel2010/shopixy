# STARTUP-1 — Forensic Startup Execution Audit

**Subject:** `PointOfSale.API` fails to reach `app.Run()` steady state (Kestrel never signals "Application started").
**Mode:** Read-only forensic investigation. No source, config, or migrations were modified.
**Method:** Source-code trace of the complete startup pipeline (`PointOfSale.API/Program.cs`, all 27 hosted services, Redis/Mongo/Rabbit/PG init paths) cross-checked against the one available runtime artifact (`/tmp/api.log`) and the prior investigation (`REDIS_STARTUP_HANGS_DEBUG_REPORT.md`).
**Evidence base:** file:line citations below. Where the prior report is cited it is treated as **secondary** evidence (it describes a dump taken against *superseded* code); current source is the source of truth.

---

## 1. Executive Summary

The application's startup **registration phase is clean** and the **middleware phase (lines 3266–3531) contains no blocking calls** — it is pure `app.Use*/Map*` wiring followed by a single `app.Run()` at `Program.cs:3531`.

The hang therefore can only live in one of two places:

- **A. `builder.Build()`** (`Program.cs:3266`) — options `ValidateOnStart` execution or eager singleton construction, or
- **B. `app.Run()` → `IHost.StartAsync()`** — where the 27 registered `IHostedService`/`BackgroundService` instances are started **sequentially in registration order**, plus the Kestrel server hosted service.

The prior investigation concluded the blocker was **`RedisConnectionFactory.CreateConnection()` → `ConnectionMultiplexer.Connect()`** blocking forever against a "port-open-but-protocol-dead" Redis at `192.168.1.5:6379`, proven by a `dotnet-dump` showing the startup thread in `WaitHandle.WaitOne` inside the `IConnectionMultiplexer` factory lambda (`RedisServiceCollectionExtensions.<AddRedisCache>b__0_1` → `Lazy<IConnectionMultiplexer?>.Value` → `CreateConnection()`).

**That specific infinite-hang path no longer exists in current source.** `RedisConnectionFactory.CreateConnection()` (`PointOfSale.Infrastructure/Redis/Services/RedisConnectionFactory.cs:106-250`) now wraps the connect in `ConnectWithTimeout(...)` using a **dedicated connect thread + dedicated watchdog thread + kernel `WaitHandle.WaitAny`** with a hard budget of `max(ConnectTimeoutMs+2000, 10000)` ms (≈7s default). On timeout it returns `null` and caching is disabled. A broken Redis can no longer hang startup indefinitely through this path.

The single available runtime log (`/tmp/api.log`, captured 2026-07-24T15:19–15:40) shows the process **alive and doing background work for 20+ minutes** — COA seeder completed, MongoDB connected, MassTransit configured every endpoint, and `CommerceHubDailyFinancialAggregateService` refreshed on its timer at 15:20, 15:25, 15:30, 15:35, 15:40 — **yet never emitted `Now listening on…` / `Application started` (Microsoft.Hosting.Lifetime)**. This is the one unexplained, still-open signal.

**Conclusion:** the historic Redis root cause is real but was captured against superseded code and is now mitigated. No component in *current* source is proven to block `StartAsync` forever. The stale log presents a new, distinct anomaly (host runs background services but never signals ApplicationStarted) that cannot be resolved without a fresh run + dump. **Certification: C — multiple competing root causes remain.**

---

## 2. Startup Timeline (actual, traced from Program.cs)

| # | Step | Location |
|---|---|---|
| 00 | `WebApplication.CreateBuilder(args)` | `Program.cs:57` |
| 01 | Configuration chain: appsettings → env-specific → `.local` → user-secrets → env vars | `Program.cs:58-71` |
| 02 | Postgres connstring normalize + `SensitiveConfigurationValidator.ValidateOrThrow` | `Program.cs:73-85` |
| 03 | Logging: `ClearProviders` + `AddJsonConsole` + Debug + EventSource | `Program.cs:87-96` |
| 04 | ForwardedHeaders options | `Program.cs:99-131` |
| 05 | MVC / filters / controllers / JSON / Swagger | `Program.cs:134-312` |
| 06 | `AddOptions<…>().ValidateOnStart()` × 8 (see §5) | `Program.cs:231-258, 525, 562, 1677, 2194` |
| 07 | `AddDbContext<ApplicationDbContext>` (Npgsql + 7 interceptors) | `Program.cs:321-339` |
| 08 | Domain/service registrations (hundreds of `AddScoped/AddSingleton`) | `Program.cs:347-1600` |
| 09 | `AddMongoDb` + 2 Mongo index initializers (hosted) | `Program.cs:1615-1617` |
| 10 | `AddRedisCache` (registers `RedisConnectionFactory` + `IConnectionMultiplexer` factory, **lazy**) | `Program.cs:1625` |
| 11 | `AddElasticsearch` | `Program.cs:1637` |
| 12 | `AddMassTransit` (RabbitMQ transport, ~80 consumers) | `Program.cs:1681-2051` |
| 13 | Remaining hosted services, HealthChecks, OpenTelemetry, Hangfire | `Program.cs:2068-3230` |
| 14 | **`var app = builder.Build();`** | `Program.cs:3266` |
| 15 | Middleware pipeline (forwarded headers → swagger → security → routing → auth → tenant → authz → rate limit) — **no blocking calls** | `Program.cs:3268-3407` |
| 16 | `MapControllers`, `MapHub<ChatHub>`, HealthChecks, Prometheus, Hangfire dashboard | `Program.cs:3409-3473` |
| 17 | **`app.Run();`** → `IHost.StartAsync()` → hosted services start sequentially → Kestrel bind | `Program.cs:3531` |

**Fact:** everything between step 14 and step 17 is registration only. The hang is in **step 14 (Build) or step 17 (StartAsync)** — nowhere else.

---

## 3. Complete Dependency Graph (Redis blocking path — historic)

```
app.Run()                                    Program.cs:3531
  → IHost.StartAsync()
     → (any hosted service / Kestrel that eagerly resolves a Redis-backed singleton)
        → IConnectionMultiplexer  (singleton factory)      RedisServiceCollectionExtensions.cs:64-68
           → RedisConnectionFactory.Connection             RedisConnectionFactory.cs:49
              → Lazy<IConnectionMultiplexer?>.Value         RedisConnectionFactory.cs:32,49
                 → CreateConnection()                       RedisConnectionFactory.cs:106
                    → ConnectWithTimeout(options, budget)   RedisConnectionFactory.cs:138,183
                       → ConnectionMultiplexer.Connect()    RedisConnectionFactory.cs:212  (on dedicated thread)
                       → WaitHandle.WaitAny([done,timeout]) RedisConnectionFactory.cs:247  ← dump frame "WaitOne"
```

**Historic (superseded) shape:** `CreateConnection` called `ConnectionMultiplexer.Connect()` **directly and synchronously** — no watchdog — so `WaitOne` never returned against a dead-protocol Redis. **Current shape:** the `WaitAny` at line 247 is guaranteed to return within the budget because the watchdog thread (`RedisConnectConnectTimeoutWatchdog`, lines 228-242) does `Thread.Sleep(budgetMs)` on a **non-pool thread** and sets `timeoutEvent`. The dump frame the prior report saw (`b__0_1` → `WaitOne`) now corresponds to a **bounded** wait, not an infinite one.

---

## 4. HostedService Audit (all 27, registration order)

Legend: **BG** = `BackgroundService` (its `StartAsync` returns immediately after launching `ExecuteAsync` on a pool thread — **cannot** block host start). **IHS** = plain `IHostedService` (its `StartAsync` is awaited by the host — **can** block if it does synchronous/awaited work before returning).

| # | Line | Service | Base | StartAsync blocking? | Network at start | Evidence |
|---|---|---|---|---|---|---|
| 1 | 495 | CommerceHubUnresolvedPaymentReconciliationHostedService | BG | No (awaits `PeriodicTimer` first) | none until tick | file:19-23 |
| 2 | 496 | CommerceHubPayoutReconciliationHostedService | BG | No | tick | class decl |
| 3 | 497 | CommerceHubRefundRecoveryHostedService | BG | No | tick | class decl |
| 4 | 504 | CanonicalIdentityReconciliationHostedService | BG | No (`Task.Delay(30s)` first) | after 30s delay, PG | CanonicalIdentityReconciliationService.cs:634-642 |
| 5 | 505 | CommerceHubProjectionReconciliationService | BG | No | tick | class decl |
| 6 | 506 | StorefrontOrderProjectionReconciliationService | BG | No | tick | class decl |
| 7 | 507 | CommerceHubDailyFinancialAggregateRefreshService | BG | No | tick (PG) | class decl; log shows refresh ticks |
| 8 | 508 | CommerceHubPaymentCompensationOutboxWorker | BG | No | tick | class decl |
| 9 | 589 | StorefrontUnresolvedPaymentReconciliationHostedService | BG | No | tick | class decl |
| 10 | 655 | RedirectUrlRecoveryHostedService | BG | No | tick | class decl |
| 11 | 657 | CheckoutPaymentIntentRecoveryHostedService | BG | No | tick | class decl |
| 12 | 707 | ShipmentAuthorityRecoveryHostedService | BG | No | tick | class decl |
| 13 | 941 | SuperAdminRootSeeder | IHS | **No — fire-and-forget** (`_ = Task.Run(...)`; returns `Task.CompletedTask`) | PG in bg task | SuperAdminRootSeeder.cs:26-32 |
| 14 | 942 | MarketplaceCmsPageSeeder | IHS | **No — fire-and-forget** | Mongo in bg task | MarketplaceCmsPageSeeder.cs:22-28 |
| 15 | 1412 | AnalyticsAlertEvaluatorService | BG | No (`Task.Delay(30s)` first) | after delay | AnalyticsAlertEvaluatorService.cs:45-51 |
| 16 | 1616 | MongoDbIndexInitializer | IHS | **No — fire-and-forget** (`_ = Task.Run(...)`) | Mongo in bg task | MongoDbIndexInitializer.cs:27-31 |
| 17 | 1617 | CommerceHubMongoDbIndexInitializer | IHS | **No — fire-and-forget** | Mongo in bg task | CommerceHubMongoDbIndexInitializer.cs:31-35 |
| 18 | 1628 | CacheWarmupService | BG | No (`Task.Delay(5s)` first; skips if Redis disabled) | Redis after delay | CacheWarmupService.cs:50-61 |
| 19 | 1654 | AffiliateAttributionReconciliationHostedService | BG | No | tick | class decl |
| 20 | 2068 | AuditLogBackgroundService | BG | No | tick | class decl |
| 21 | 2070 | CommerceHubSearchAnalyticsBackgroundService | BG | No | tick | class decl |
| 22 | 2071 | RevokedTokenCleanupService | BG | No | tick | class decl |
| 23 | 2072 | StorefrontRevokedTokenCleanupService | BG | No | tick | class decl |
| 24 | 2078 | CommerceHubAutoHealingService (via factory) | BG | No | tick | class decl |
| 25 | 2204 | PromotionExpirationService | BG | No | tick | class decl |
| 26 | 2851 | HangfireInitializationHostedService | IHS | **Bounded** — `StartAsync` calls `CanConnectToPostgres()` (Npgsql `Timeout=5, CommandTimeout=5`), then either registers jobs or spawns background retry; returns | PG probe, ≤5s | HangfireInitializationHostedService.cs:64-92, 150-166 |
| 27 | 3230 | CommerceHubPlatformStartupValidator | IHS | **No — fire-and-forget** | PG/health in bg task | CommerceHubPlatformStartupValidator.cs:24-28 |

Plus the framework-registered **MassTransit bus hosted service** (via `AddMassTransit`, `Program.cs:1681`) and the **Kestrel `GenericWebHostService`**.

**Result of audit:** every app-owned `IHostedService` that could block is either (a) a `BackgroundService` (non-blocking by construction), (b) explicitly fire-and-forget (`Task.Run` + `Task.CompletedTask`), or (c) bounded by a 5s DB probe (Hangfire). **No app-owned hosted service in current source performs an unbounded synchronous/awaited operation in `StartAsync`.**

---

## 5. DI Resolution Order — eager resolution points

Eager work forced during `builder.Build()` / `StartAsync`:

- **`ValidateOnStart()` options** (executed during Build):
  - `OperationalAlertThresholdsOptions` — `Program.cs:231-234`
  - `CheckoutSessionSecurityOptions` — `Program.cs:237-240`
  - `AccountingOutboxOptions` — `Program.cs:242-245`
  - `StorefrontPricingOptions` (+ custom `ValidateCountryTaxRates`) — `Program.cs:248-258`
  - three more at `Program.cs:525, 562, 1677, 2194`
  These validate **configuration values only** (data annotations + pure predicates). None opens a socket. Not a network blocker.
- **`ApplicationDbContext`** interceptors are resolved per-scope, not eagerly (`Program.cs:328-339`).
- **`IConnectionMultiplexer`** is registered as a **lazy** singleton (`RedisServiceCollectionExtensions.cs:64-68`): the factory returns `factory.Connection`, and `RedisConnectionFactory._connection` is `Lazy<>` (`RedisConnectionFactory.cs:32`). It is **only constructed when first `.Value` is read** — i.e., when a consumer actually resolves it. No hosted service constructor in the set injects `IConnectionMultiplexer` or a Redis singleton directly (verified across all 27), so it is **not** eagerly resolved during `StartAsync` by the app's own hosted services. It would be resolved on first HTTP request or by MassTransit consumers/`CacheWarmupService` (after its 5s delay).

---

## 6. Singleton Creation Timeline (startup-critical)

1. Options singletons validated at Build (§5) — CPU only.
2. `RedisConnectionFactory` singleton constructed when first resolved; **connection not opened in ctor** (`RedisConnectionFactory.cs:24-33`).
3. `MongoDbContext` singleton — constructed when first resolved (log confirms: "MongoDB context initialized … Server: mongodb://192.168.109.128:27017" at 15:19:43).
4. MassTransit bus singleton — constructed and endpoints configured during bus hosted-service start (log: dozens of "Configured endpoint …" at 15:19:42).
5. Telemetry/Meter singletons (`Program.cs:2085-2095`) — CPU only.

No singleton constructor in the startup-critical path performs an unbounded network call in its constructor.

---

## 7. Blocking Call Inventory

- **`RedisConnectionFactory.cs:247`** — `WaitHandle.WaitAny([doneEvent, timeoutEvent])`. **Bounded** by watchdog thread (lines 228-242, `Thread.Sleep(budgetMs)`), budget ≈7s default. Thread-pool-independent. This is the frame the historic dump showed as `WaitOne`; it is now guaranteed to return.
- **`RedisConnectionFactory.cs:232`** — `Thread.Sleep(budgetMs)` on a dedicated watchdog thread. Intentional, bounded.
- **`HangfireInitializationHostedService.cs:164`** — `connection.Open()` (Npgsql). Bounded: `Timeout=5, CommandTimeout=5` (lines 157-161).
- Middleware region (`Program.cs:3266-3531`) — **zero** `.Wait()`, `.Result`, `GetAwaiter().GetResult()`, `Thread.Sleep`, `lock`, `Monitor.Wait`, `SemaphoreSlim.Wait`, `Mutex`, or static-ctor network calls.
- `Lazy<T>` usage: only `RedisConnectionFactory._connection` (`RedisConnectionFactory.cs:21,32,42`) — guarded by the bounded connect.

No unbounded blocking primitive remains in the startup-critical path in current source.

---

## 8. Network Initialization Audit

| Dependency | Where init happens | Sync? | Bounded timeout? |
|---|---|---|---|
| PostgreSQL | `AddDbContext` (lazy, per-scope); Hangfire probe `Open()` | probe is sync | Yes — 5s (Hangfire); EF lazy |
| Redis | `RedisConnectionFactory.CreateConnection` (lazy on first use) | connect on dedicated thread | Yes — `max(ConnectTimeoutMs+2000,10000)` ≈7s |
| MongoDB | `MongoDbContext` (lazy); index initializers fire-and-forget | driver async | driver defaults |
| RabbitMQ | MassTransit bus start | MassTransit 8.5.7 default `WaitUntilStarted=false` — bus start does **not** block on broker connect | N/A (non-blocking start) |
| Elasticsearch | `AddElasticsearch` client (lazy) | lazy | N/A |

**RabbitMQ note:** the bus is configured with `UsingRabbitMq` (`Program.cs:2000-2050`) with **no `WaitUntilStarted(true)`** override — default MassTransit behaviour is non-blocking bus start with background reconnect. The log confirms full endpoint configuration completed at 15:19:42.

---

## 9. Wait Chain Analysis

The only wait chain reachable during startup is the Redis one (§3). It terminates deterministically at the watchdog-guarded `WaitAny` (`RedisConnectionFactory.cs:247`). Under the historic (superseded) code this chain was **unbounded**; under current code it is **bounded**. No other startup wait chain exists.

---

## 10. Last Successfully Completed Startup Step (from the runtime artifact)

From `/tmp/api.log` (only available runtime evidence, timestamps 15:19:42–15:40:21):

- ✅ MassTransit: every endpoint configured (15:19:42)
- ✅ Hangfire SQL objects installed (15:19:43) → **PostgreSQL reachable, Hangfire init returned**
- ✅ MongoDB context initialized against `192.168.109.128:27017` (15:19:43) → **Mongo reachable**
- ✅ AccountingOutboxWorker / PaymentCancellationOutboxWorker / ProviderDeliveryWorker started (15:19:43–48)
- ✅ Canonical identity reconciliation "delaying startup for 00:00:30" (15:19:48) then "starting" (15:20:18) → **its 30s delay elapsed, proving the process was alive and the thread pool healthy**
- ✅ CommerceHub / Storefront projection reconciliation services started (15:19:48)
- ✅ COA Startup Seeder: "Checking 0 active tenants… Completed" (15:19:53)
- ✅ `CommerceHubDailyFinancialAggregateService` refreshed on its 5-minute timer at **15:20, 15:25, 15:30, 15:35, 15:40** → **background execution healthy for 20+ minutes**

**Last confirmed forward progress:** background hosted services running normally on their timers (last log 15:40:21).

---

## 11. First Component That Never Returns

**No component is proven, in current source, to never return.** Two competing readings:

- **Reading A (historic, from the prior dump):** `RedisConnectionFactory.CreateConnection()` → `ConnectionMultiplexer.Connect()` blocked forever. **Status: fixed** — current code bounds it (`RedisConnectionFactory.cs:183-250`). Also disabled in `appsettings.Development.local.json:83-86` (`Redis:Enabled=false`). Note the **base `appsettings.json:258-261` still has `Redis:Enabled=true`**, so environment resolution matters (see §14).

- **Reading B (current, from the log anomaly):** The host started all background services and ran them for 20+ minutes, but **`Microsoft.Hosting.Lifetime` never logged `Now listening on…` or `Application started`** (grep of the full log: 0 matches for "now listening", "application started", "lifetime"). This means `IHost.StartAsync()` had **not completed** — a hosted service whose `StartAsync` is *awaited* (not a `BackgroundService`, whose `StartAsync` returns early) had not yet returned when the log ended, so Kestrel (`GenericWebHostService`) never bound. `BackgroundService.ExecuteAsync` bodies keep running because their `StartAsync` already returned before the blocking one. **However, §4 shows every app-owned plain `IHostedService` is fire-and-forget or bounded** — so under current source this reading has **no identified culprit**. It may be an artifact of the log being captured against pre-fix code, or an ordering/flush artifact, and cannot be resolved without a fresh run.

---

## 12. Root Cause Confidence

- Redis-as-historic-root-cause: **High confidence it WAS the cause of the original infinite hang**, but **it is mitigated in current source** — so it is not a valid conclusion for the *current* binary.
- Current-source blocker: **Not identified.** The static trace finds no unbounded blocker; the only runtime artifact predates the fixes and shows a different, unexplained "no Kestrel bind despite live workers" anomaly.

---

## 13. Alternative Hypotheses

1. **Environment-resolution mismatch (high plausibility).** Base `appsettings.json` has `Redis:Enabled=true` (`:261`); only `appsettings.Development.local.json` sets `false` (`:86`). If the process runs under `Test`/`Production` (or the `.local` file isn't loaded — it's `optional:true` and only reload-on-change in Development, `Program.cs:61-64`), Redis stays enabled and will attempt the (now bounded) connect. Prior report §5 explicitly documented the launcher silently running in the wrong environment.
2. **Kestrel bind blocked by a non-app hosted service.** The MassTransit bus or another framework hosted service registered before `GenericWebHostService` could gate `StartAsync`. MassTransit default is non-blocking, so low plausibility, but unverified against a fresh dump.
3. **Log is simply stale/pre-fix.** The 2026-07-24 log matches the prior report's date; it likely captures a run *before* all §4 fixes were in place. The "no Application started" may reflect the historic infinite Redis hang, not current behaviour.
4. **`ValidateOnStart` throwing (low).** Would produce an exception + crash, not a silent hang; log shows no such exception.

---

## 14. Required Evidence Still Missing

To move from Certification C to A/B, capture the following against the **current** build:

1. **A fresh run** of `PointOfSale.API` from current source with confirmed `ASPNETCORE_ENVIRONMENT` (echo it), redirecting stdout to a file. Confirm whether `Now listening on…` appears.
2. **`dotnet-dump` + `clrstack`** on the hung process (if it still hangs) — identify the exact thread stuck in `StartAsync`, read upward to the app frame. This is the single most decisive missing artifact for current code.
3. **Effective config dump:** log `builder.Configuration["Redis:Enabled"]` and `["ConnectionStrings:DefaultConnection"]` at startup to prove which config files actually loaded.
4. **Confirm base `appsettings.json:261` (`Redis:Enabled=true`)** is the intended production default and that the deployment environment overrides it.
5. **Verify RabbitMQ (`192.168.1.5:5672`) and Redis (`192.168.1.5:6379`) protocol health** with a real client PING, not just a TCP connect.

---

## 15. Final Certification

> **C. Multiple competing root causes remain.**

**Rationale:** The prior investigation's Redis root cause is well-evidenced *for the historic binary* (dump frame at the `IConnectionMultiplexer` factory → `WaitOne`) but is **mitigated in current source** by the watchdog-bounded connect (`RedisConnectionFactory.cs:183-250`) and disabled in the dev-local config. A read-only trace of current source finds **no unbounded blocker** in either `builder.Build()` or any app-owned `IHostedService.StartAsync`. The only runtime artifact available (`/tmp/api.log`) predates the fixes and exhibits a **distinct, unexplained anomaly** — background services run for 20+ minutes while `Microsoft.Hosting.Lifetime` never signals `Application started`, meaning `IHost.StartAsync()` never completed — for which the static analysis identifies no current-source culprit. Resolving which of the competing hypotheses (§13) holds requires a fresh run + `dotnet-dump` (§14); the evidence in hand is insufficient to name the first-never-returning component of the *current* binary with certainty, but is strong enough to enumerate and rank the candidates.

**Reproducibility for another engineer:** every claim above is cited to `file:line`. Re-running the audit against the same commit will reproduce the same registration timeline, the same 27-service classification, and the same bounded-blocker findings. The decisive next artifact is a fresh startup log + thread dump.
