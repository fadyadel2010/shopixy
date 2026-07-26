# STARTUP-4 — Pre-Kestrel Startup Stall Forensics

Phase: STARTUP-4 (read-only forensic investigation; ZERO source/config modifications)
Date: 2026-07-24
Repo state: branch `recovery/3-greenfield-baseline`, working tree includes uncommitted STARTUP-1..3 changes
Process examined: `PointOfSale.API.exe` PID 2184 (started 2026-07-24 20:24:49 local / 17:24:49 UTC)
Primary artifacts:
- Live run log: `/tmp/startup4.log` (fresh run, `ASPNETCORE_ENVIRONMENT=Development`, `dotnet run --no-build`)
- Full-memory dump captured AT the stall: `C:\Users\fadya\AppData\Local\Temp\s4.dmp` (dotnet-dump collect, PID 2184)
- `clrstack -all` extract: `/tmp/s4_stacks.txt`; per-thread: `/tmp/s4_main.txt`, `/tmp/s4_full.txt`
- Minimal isolated repro (temp project, outside repo, deleted after): MEDI ctor-selection test

---

## 1. Executive Summary

**Kestrel is never reached because the STARTUP-2 DI singleton cycle is STILL ALIVE at runtime.
The STARTUP-3 remediation (`[ActivatorUtilitiesConstructor]` on the 2-parameter
`RedisConnectionFactory` constructor) does NOT work for services activated by the
Microsoft.Extensions.DependencyInjection ServiceProvider — the attribute is honored only by
`ActivatorUtilities.CreateInstance`, not by the container's `CallSiteFactory` constructor
selection. This was proven by a live isolated repro (Section 11).**

At the stall point, the host's startup thread (dump thread index 0, OS thread `0x4b94`) is
frozen forever inside DI resolution of `IConnectionMultiplexer`:

```
System.Threading.WaitHandle.WaitOne()                                   <- never returns
Microsoft.Extensions.DependencyInjection.ServiceLookup.StackGuard.RunOnEmptyStackCore  (StackGuard.cs @ 74)
... then HUNDREDS of repeated 12-frame cycles of:
    CallSiteRuntimeResolver.VisitConstructor           (RedisConnectionFactory 3-param ctor)
    -> VisitCallSiteMain -> VisitRootCache -> VisitCallSite
    -> RedisServiceCollectionExtensions+<>c.<AddRedisCache>b__0_1(IServiceProvider)
       [PointOfSale.Infrastructure\Redis\RedisServiceCollectionExtensions.cs @ 66]
    -> GetRequiredService -> ServiceProvider.GetService -> ConcurrentDictionary.GetOrAdd
    -> CallSiteRuntimeResolver.Resolve -> (repeat)
```

`b__0_1` (the `IConnectionMultiplexer` factory lambda at
`RedisServiceCollectionExtensions.cs:66`) appears **372 times** in the truncated `clrstack -all`
capture and **548 times** in the deeper single-thread capture (`/tmp/s4_main.txt`), matching the
813-frame signature of STARTUP-2 exactly. `RunOnEmptyStackCore -> WaitHandle.WaitOne()` converts
the recursion into a silent infinite kernel wait: no exception, no StackOverflow, no log line.

Why the process LOOKS healthier than in STARTUP-2: the cycle no longer fires during
`builder.Build()` (root-singleton eager construction path changed by the uncommitted STARTUP-1/2/3
edits' recompilation and the lazy `Lazy<IConnectionMultiplexer?>` shape), so `Build()` completes and
`Host.StartAsync()` begins starting hosted services. `BackgroundService.ExecuteAsync` bodies that do
NOT touch `IConnectionMultiplexer` run happily on pool threads (that is all the "workers are alive"
evidence). But the sequential hosted-service `StartAsync` chain executes on the startup thread, and
the FIRST resolution on that thread whose object graph includes `IConnectionMultiplexer` re-enters
the cycle and freezes the startup thread permanently. `GenericWebHostService` — always the LAST
hosted service started by WebApplication hosts — is therefore never reached. No Kestrel, no bind,
no `ApplicationStarted`.

**Certification: A — Pre-Kestrel blocker conclusively identified.**
Blocker: unresolved `IConnectionMultiplexer` ⇄ `RedisConnectionFactory`(3-param ctor) singleton
cycle, still selected by MEDI because `[ActivatorUtilitiesConstructor]` is ignored by the
ServiceProvider's constructor selection.

---

## 2. Runtime Timeline (fresh run, UTC, from /tmp/startup4.log)

| Time (UTC) | Event | Evidence |
|---|---|---|
| 17:24:49 | Process start (PID 2184) | `Get-Process` StartTime 20:24:49 local |
| 17:24:50.784 | First log line: MassTransit endpoint configuration (during Build/StartAsync boundary) | log line 2 |
| 17:24:51.661 | `MongoDbContext` initialized (`ecommerce_read_db`) | log |
| 17:24:53.6–53.8 | EF Core model validation warnings (ApplicationDbContext first materialization) | log |
| 17:24:54.487 | `PaymentCancellationOutboxWorker` "Worker starting" | log |
| 17:24:54.545 | `ProviderDeliveryWorker` starting | log |
| 17:24:54.641 | `CanonicalIdentityReconciliationHostedService` delaying 30s | log |
| 17:24:54.642–54.873 | CommerceHub/Storefront reconciliation workers, RedirectUrlRecovery, ShipmentAuthorityRecovery all "started" | log |
| 17:24:59.489–59.503 | `ChartOfAccountsStartupSeeder` runs to completion (0 tenants) — proves PostgreSQL reachable | log |
| 17:25:24.640 | CanonicalIdentity reconciliation resumes after its 30s delay — proves pool threads alive | log |
| 17:25:24.718 | `MarketplaceReadModelIndexInitializer` logs Mongo `TimeoutException` (external Mongo 192.168.131.128:27017 unreachable) — CAUGHT, non-fatal, fire-and-forget thread | log + MarketplaceReadModelIndexInitializer.cs:21–39 |
| 17:25:26.183 | `CommerceHubDailyFinancialAggregateRefreshService` refreshes 365 rows — PostgreSQL healthy | log |
| 17:25:26 → dump time (~17:31) and beyond | **NO further startup progression. Zero occurrences of `Now listening` / `Application started`.** `grep -cE "Now listening|Application started"` = **0**. Ports 5010/7263: nothing LISTENING (netstat). | log + netstat |

The log alone proves the "background workers tick but Application started never logs" signature:
`IHost.StartAsync()` never completed.

---

## 3. Host.StartAsync Timeline

.NET hosts start `IHostedService`s **sequentially, in registration order, awaiting each
`StartAsync`**, with `GenericWebHostService` (which owns Kestrel) registered LAST by
`WebApplicationBuilder`. Therefore:

- Every `BackgroundService` whose "started" line appears in the log had its `StartAsync`
  return (BackgroundService.StartAsync only kicks off `ExecuteAsync` and returns when it yields).
- The chain progressed at least through the services listed in Section 2.
- The chain **halted before reaching `GenericWebHostService`**: no
  `Microsoft.Hosting.Lifetime` messages of any kind appear in the entire log (grep = 0).
- Transition that never occurs: **`<blocked hosted service>.StartAsync` → next hosted service →
  … → GenericWebHostService.StartAsync`**. Everything downstream of that (Server.StartAsync,
  KestrelServer, AddressBinder, SocketTransport, Bind, Listen, ApplicationStarted) is
  transitively unreached — see Section 6.

The dump identifies exactly where the awaited chain died: thread 0 blocked in DI resolution
(Section 8), not in any service's business logic.

---

## 4. Hosted Service Timeline / Forensics

39 hosted-service registrations were enumerated from source (Program.cs + extension methods).
Classification by base type (source-verified, file:line in Section 12 table):

- **28 × `BackgroundService`** — cannot block host startup by design; their `ExecuteAsync`
  bodies run concurrently. Log confirms many started and kept ticking.
- **11 × plain `IHostedService`** — each was read line-by-line:
  - `PaymobSensitiveArtifactBackfillHostedService`, `FawrySensitiveArtifactBackfillHostedService`,
    `CommerceHubPlatformStartupValidator`, `MarketplaceCmsPageSeeder`, `SuperAdminRootSeeder`,
    `MongoDbIndexInitializer`, `CommerceHubMongoDbIndexInitializer`,
    `MarketplaceReadModelIndexInitializer`, `ElasticsearchIndexInitializer`,
    `CommerceHubElasticsearchIndexInitializer` — ALL use the fire-and-forget pattern
    `_ = Task.Run(...); return Task.CompletedTask;` (verified at each file's StartAsync).
    None can block the startup thread.
  - `HangfireInitializationHostedService` (Program.cs:2851) — its `StartAsync` is synchronous
    but bounded: a 5-second-timeout Npgsql probe (`CanConnectToPostgres`,
    HangfireInitializationHostedService.cs:150–160, `Timeout = 5`), then either immediate
    registration or a background retry task; every path `return Task.CompletedTask`. The log
    shows Hangfire SQL objects installing successfully, so Postgres was up and this returned.

**Conclusion: no hosted service blocks via its own awaited work.** What blocks is the DI
*resolution/construction* of one of these services (or a dependency inside the chain) on the
startup thread — the dump shows the startup thread stuck inside
`ServiceProvider.GetService`→`CallSiteRuntimeResolver`, i.e. constructing an object graph, not
executing `StartAsync` body code.

---

## 5. GenericWebHostService Analysis

- `GenericWebHostService` never appears in any captured stack and never logs.
- It is registered last in the hosted-service order (WebApplication default). It cannot start
  because a predecessor's DI activation froze the startup thread.
- Verdict: GenericWebHostService is NOT the blocker; it is a downstream victim. Proof: the
  frozen thread's stack contains only MEDI resolution frames plus the Redis factory lambda —
  zero `Microsoft.AspNetCore.Hosting` frames (grep for `Hosting|GenericWebHost|Kestrel` over all
  stack captures = 0 matches).

---

## 6. Kestrel Startup Analysis

- `KestrelServer`, `AddressBinder`, `SocketTransportFactory`, `Socket.Bind`, `Listen`:
  **never reached.** Zero Kestrel frames in the dump; zero `Now listening` log lines;
  `netstat -ano` shows nothing LISTENING on 5010 or 7263 while the process is alive.
- Port conflict is excluded: nothing else holds 5010/7263 (netstat empty), and a conflict would
  produce an `IOException: address already in use` — the log has no exception from Kestrel at all.

---

## 7. Thread Analysis

`threads` in the dump lists 50 managed threads. Key facts:

- **Thread index 0 (OS `0x4b94`)** — the startup thread. Stack: ~8,200 captured lines,
  top = `WaitHandle.WaitOneCore` (kernel wait, infinite) under
  `StackGuard.RunOnEmptyStackCore` (StackGuard.cs @ 74); below it, an unbroken repeating
  12-frame DI cycle containing `<AddRedisCache>b__0_1` (RedisServiceCollectionExtensions.cs @ 66)
  and `CallSiteRuntimeResolver.VisitConstructor`. Occurrences of the lambda frame: 372 (clrstack
  -all capture) / 548 (single-thread capture, still truncated by reader timeout at 8,222 lines).
  This thread is permanently blocked; it owns host startup.
- **Thread `0x4a5c`** — only `DebuggerU2MCatchHandlerFrame` (the dump-collection helper thread).
- **Remaining 48 threads** — thread-pool / timer / MassTransit / Npgsql / Mongo heartbeat
  workers; these carry the still-ticking `ExecuteAsync` loops observed in the log (e.g. the
  30-second CanonicalIdentity delay completing at 17:25:24). None of them hold a lock the
  startup thread needs — the startup thread is not waiting on a lock at all, it is waiting on
  the `ManualResetEvent` inside `StackGuard.RunOnEmptyStack`'s helper, which will never be set
  because the recursion it delegates re-enters itself.
- ThreadPool: not starved (workers keep logging on schedule — 30s timers fire on time).
- GC: no GC-suspension frames present; process memory stable (~420 MB).
- SynchronizationContext: none (console host) — irrelevant to this wait.

---

## 8. Wait Chain Analysis — the first runtime frame that never completes

```
FROZEN (never returns):
  System.Threading.WaitHandle.WaitOne()                       [WaitHandle.cs @ 503]
  ^ called by
  MEDI ServiceLookup.StackGuard.RunOnEmptyStackCore<T>(...)   [StackGuard.cs @ 74]
  ^ invoked when the DI resolution recursion exceeded the stack guard depth
  ^ recursion loop (repeats 100s of times):
      CallSiteRuntimeResolver.VisitConstructor                 <- RedisConnectionFactory ctor
      CallSiteVisitor`2.VisitCallSiteMain
      CallSiteRuntimeResolver.VisitRootCache
      CallSiteVisitor`2.VisitCallSite
      PointOfSale.Infrastructure.Redis.RedisServiceCollectionExtensions+<>c.<AddRedisCache>b__0_1
          [RedisServiceCollectionExtensions.cs @ 66]           <- IConnectionMultiplexer factory
      ServiceProviderServiceExtensions.GetRequiredService<RedisConnectionFactory>
      ServiceProvider.GetService -> ConcurrentDictionary.GetOrAdd -> CreateServiceAccessor
      CallSiteRuntimeResolver.Resolve  -> (re-enters VisitConstructor)
```

Wait type: kernel `WaitOne()` with no timeout on the event that RunOnEmptyStack's
helper-thread completion would signal — but the helper simply continues the same infinite
recursion, so the event is never set. This is MEDI's documented behavior of converting
runaway resolution recursion into a silent hang instead of `StackOverflowException`.

No network wait, no semaphore, no Task wait is involved in the blocker.

---

## 9. Program.cs Execution Timeline

- `PointOfSale.API/Program.cs` (3,535 lines): lines 1–3265 = registration; **line 3266
  `var app = builder.Build();` COMPLETED** (proof: hosted services executed — impossible
  otherwise). Lines 3266–3530 = pure middleware/pipeline registration (verified: zero
  `.Wait()/.Result/GetAwaiter().GetResult()/Thread.Sleep/Migrate` in the file — grep returned
  only lines 3266 and 3531).
- **Line 3531 `app.Run();` WAS reached** and is where the process sits: `Run()` →
  `HostingAbstractionsHostExtensions.Run` → `IHost.StartAsync()` → sequential hosted-service
  start → frozen inside DI activation of a service graph that pulls `IConnectionMultiplexer`.
- Last executed statement: `app.Run()` (in progress, never returns).
- First statement never reached: none in Program.cs — the file completed; the unreached code is
  framework-internal (`GenericWebHostService.StartAsync`).

Which registration re-enters the cycle during StartAsync: any singleton-or-scoped graph
containing `IConnectionMultiplexer` resolved on the startup thread. 30+ types depend on it
(e.g. `AnalyticsAlertStore` — Analytics/Alerts/AnalyticsAlertStore.cs:33–43,
`StorefrontJwtBlacklist` — Services/Storefront/StorefrontJwtBlacklist.cs:59–61,
`DistributedLockService`, MassTransit consumers, etc.). The dump shows the resolution
originated from a constructor call site chain on thread 0 — the exact top-level service is
below the 8,222-line capture window, but its identity is immaterial: every path terminates in
the same `b__0_1` ⇄ 3-param-ctor cycle.

---

## 10. First Runtime Operation That Never Returns

`Microsoft.Extensions.DependencyInjection.ServiceLookup.StackGuard.RunOnEmptyStackCore` →
`System.Threading.WaitHandle.WaitOne()` on the host startup thread (OS `0x4b94`), entered from
the `IConnectionMultiplexer` singleton factory lambda at
`PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs:66`.

---

## 11. Verified Root Cause

**`[ActivatorUtilitiesConstructor]` does not influence MEDI ServiceProvider constructor
selection.** STARTUP-3's static verification used `ActivatorUtilities.CreateInstance`, which
DOES honor the attribute — that test passed while the container-level cycle remained.

Isolated repro executed during this investigation (temp console app, .NET 9,
Microsoft.Extensions.DependencyInjection 9.0.x — the same package family the API uses),
modelling the exact registration shape (bare singleton with two public ctors, attribute on the
2-param ctor, `IMux` registered):

```
ServiceProvider selected:      3-param (test-only)     <- container IGNORES the attribute
ActivatorUtilities selected:   2-param (attributed)    <- ActivatorUtilities honors it
```

(Reference: MEDI's `CallSiteFactory.CreateConstructorCallSite` selects the constructor with the
most resolvable parameters and has never consulted `ActivatorUtilitiesConstructorAttribute`;
dotnet/runtime #42339.)

Therefore at runtime the container still binds `RedisConnectionFactory` to the 3-parameter
constructor (`RedisConnectionFactory.cs:41`, the `IConnectionMultiplexer? connectionOverride`
overload), recreating the cycle:

```
IConnectionMultiplexer (factory, RedisServiceCollectionExtensions.cs:64-68)
  -> GetRequiredService<RedisConnectionFactory>()           (line 66)
    -> container picks 3-param ctor (attribute ignored)
      -> needs IConnectionMultiplexer -> factory -> ... infinite
```

Dump signature matches STARTUP-2's byte-for-byte (same lambda, same VisitConstructor loop, same
StackGuard→WaitOne terminal). The blocker was never actually removed; it merely moved from
`builder.Build()` time to the first `IConnectionMultiplexer` resolution inside
`Host.StartAsync()`, which sits before `GenericWebHostService` in the start order.

---

## 12. Eliminated Hypotheses (evidence per item)

| Hypothesis | Verdict | Evidence |
|---|---|---|
| HostedService deadlock (own StartAsync awaiting work) | ELIMINATED | All 11 plain IHostedServices verified fire-and-forget or bounded (Section 4); frozen stack contains zero user StartAsync frames |
| GenericWebHostService internal wait | ELIMINATED | Never constructed/started; zero Hosting frames in dump |
| MassTransit (RabbitMQ bus start) | ELIMINATED | MassTransit endpoints configured 17:24:50 (log); MassTransit's bus hosted service never got a chance to block — frozen stack has zero MassTransit frames; no `MassTransitHostOptions.WaitUntilStarted` in source (grep = 0) |
| Hangfire | ELIMINATED | SQL objects installed successfully (log); probe timeout = 5s (HangfireInitializationHostedService.cs:150–160); all paths return CompletedTask |
| Mongo initialization | ELIMINATED as blocker | Failure at 17:25:24.718 was thrown INSIDE a fire-and-forget `Task.Run` thread (MarketplaceReadModelIndexInitializer.cs:21–24), caught and logged (line 36–39); startup thread has zero Mongo frames |
| Kestrel itself | ELIMINATED | Never reached (Section 6) |
| ApplicationLifetime | ELIMINATED | Notifications fire only after all hosted services start; upstream blocker explains absence |
| HealthChecks | ELIMINATED | `CommerceHubPlatformStartupValidator.StartAsync` is fire-and-forget (Health/CommerceHubPlatformStartupValidator.cs:24–28) |
| Middleware region (Program.cs 3266–3530) | ELIMINATED | Pure registration; no blocking primitives (grep verified); `app.Run()` reached |
| Socket bind failure / port conflict | ELIMINATED | No bind attempted; ports free; no exception |
| Configuration/env mis-resolution | ELIMINATED as blocker | Development config loaded (Mongo IP/DB names unique to local config appear in log); Postgres/Hangfire working |
| DI singleton cycle (IConnectionMultiplexer ⇄ RedisConnectionFactory) | **CONFIRMED — root cause** | Dump: 372–548 repeats of `b__0_1`@RedisServiceCollectionExtensions.cs:66 + VisitConstructor, terminal `StackGuard.RunOnEmptyStackCore`→`WaitHandle.WaitOne` on startup thread 0x4b94; repro proves attribute ignored by container |

## Host lifetime events

None fired. `ApplicationStarted`, `HostStarted`: blocked behind the frozen `IHost.StartAsync()`.
`ApplicationStopping/Stopped`, `HostStopping/Stopped`: not applicable (process never asked to
stop; when killed externally, forced termination bypasses graceful lifetime). The single
component preventing all of them: the DI cycle on the startup thread.

## Network investigation

Startup is NOT waiting on any network dependency: PostgreSQL healthy (Hangfire + COA seeder +
aggregates all succeeded), Mongo unreachable but handled on a background thread, Redis never
contacted (the freeze occurs during DI resolution BEFORE `CreateConnection()` — no
StackExchange.Redis frames anywhere in the dump), RabbitMQ endpoints configured without error,
no DNS/TLS/pipe waits on the frozen thread.

---

## 13. Recommended Remediation Strategy (no implementation — out of scope)

The fix must remove the 3-parameter constructor from the CONTAINER's view (the attribute is
insufficient by design). Options, smallest first:

1. Register the factory with an explicit factory delegate so the container never performs
   constructor selection:
   `services.AddSingleton(sp => new RedisConnectionFactory(sp.GetRequiredService<IOptions<RedisSettings>>(), sp.GetRequiredService<ILogger<RedisConnectionFactory>>()));`
   (two-line change in RedisServiceCollectionExtensions.cs; zero impact on the 8 test files
   that `new` the 3-param overload directly).
2. Or make the 3-param test-only constructor `internal` + `InternalsVisibleTo` the test
   assembly (container only considers public ctors).
3. Either way, re-verify with a fresh run + fresh dump: expect `Now listening on:` within
   seconds and zero `b__0_1` frames.
4. Additionally, add a regression guard (architecture test) asserting that no DI factory lambda
   resolves a type exposing a public ctor that takes the service being registered.

---

## 14. Certification

**A — Pre-Kestrel blocker conclusively identified.**

The component preventing `GenericWebHostService` from reaching Kestrel bind is the still-live
singleton resolution cycle `IConnectionMultiplexer` (factory,
`RedisServiceCollectionExtensions.cs:64-68`) ⇄ `RedisConnectionFactory` 3-parameter constructor
(`RedisConnectionFactory.cs:41`), selected by MEDI's `CallSiteFactory` because
`[ActivatorUtilitiesConstructor]` is ignored by ServiceProvider activation (proven by isolated
runtime repro). The host startup thread (OS `0x4b94`) is permanently parked in
`StackGuard.RunOnEmptyStackCore → WaitHandle.WaitOne()` with 372–548 captured repetitions of the
cycle frames, blocking the sequential hosted-service start chain before `GenericWebHostService`.
No source, configuration, or registration was modified during this investigation.
