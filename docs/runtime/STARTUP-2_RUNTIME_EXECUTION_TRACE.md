# STARTUP-2 — Runtime Execution Trace & Live Host Forensics

Phase: Live runtime forensics (post STARTUP-1 static audit)
Date of capture: 2026-07-24
Investigator: Hermes (automated live forensics session)
Mode: OBSERVATION ONLY — no source, config, or infrastructure was modified during this phase.

---

## 1. Executive Summary

`Host.StartAsync()` never completes because the **process main thread is trapped in an
unbounded, mutually recursive dependency-injection resolution loop** between two singleton
registrations:

- `IConnectionMultiplexer` factory lambda —
  `PointOfSale.Infrastructure.Redis.RedisServiceCollectionExtensions.AddRedisCache`,
  `PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs:64-68`
- `RedisConnectionFactory` — whose **second (3-parameter) constructor** takes
  `IConnectionMultiplexer? connectionOverride`,
  `PointOfSale.Infrastructure/Redis/Services/RedisConnectionFactory.cs:35-43`

Microsoft.Extensions.DependencyInjection selects the constructor with the largest set of
resolvable parameters. Because `IConnectionMultiplexer` IS registered, the DI container
selects the 3-parameter constructor for `RedisConnectionFactory`. Resolving
`RedisConnectionFactory` therefore requires resolving `IConnectionMultiplexer`, whose
factory lambda calls `sp.GetRequiredService<RedisConnectionFactory>()` — a **singleton
dependency cycle**. Because one side of the cycle is a runtime factory lambda (not a
statically analyzable constructor callsite), the container's build-time circular-dependency
detection never fires. The resolution recurses forever.

The recursion does not crash with `StackOverflowException` because MEDI's
`ServiceLookup.StackGuard` detects low stack and re-dispatches each deep segment via
`RunOnEmptyStack`, whose caller blocks on `System.Threading.WaitHandle.WaitOne()`
(kernel wait, infinite). The observable result is a silently frozen startup thread:
no exception, no log, no Kestrel bind, forever.

**Redis the server is NOT involved.** `RedisConnectionFactory.CreateConnection()` is never
reached; the hang occurs during object graph construction, before any Redis
enable/disable check or connect attempt executes. `Redis:Enabled` has no effect on this
hang.

Certification: **A. Runtime root cause conclusively identified.**

---

## 2. Environment Verification

| Item | Value | Evidence |
|---|---|---|
| Repository | `C:\Users\fadya\source\repos\PointOfSale` | session cwd |
| Branch | `recovery/3-greenfield-baseline` | `git branch --show-current` |
| Commit | `8317aef1fb14b80147ecc4c2a91783706053f74e` ("docs(recovery): add TASK-01A stabilization execution report") | `git rev-parse HEAD` |
| Working tree | DIRTY — both cycle-participant files carry uncommitted modifications (`git status --porcelain` shows ` M PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs` and ` M PointOfSale.Infrastructure/Redis/Services/RedisConnectionFactory.cs`) | `git status` |
| Build configuration | Debug, net9.0 (`bin\Debug\net9.0\PointOfSale.API.exe`) | process command line |
| SDK | 9.0.308 | build output |
| Launch command (parent) | `dotnet run --project PointOfSale.API/PointOfSale.API.csproj --no-build -c Debug --urls http://localhost:5010` (PID 15076, started 2026-07-24 18:19:29) | `Get-CimInstance Win32_Process` |
| App process | `PointOfSale.API.exe --urls http://localhost:5010`, **PID 17212**, started 2026-07-24 **18:19:30** | `Get-CimInstance Win32_Process` |
| ASPNETCORE_ENVIRONMENT | `Development` (dotnet run applies `Properties/launchSettings.json`; both profiles set `ASPNETCORE_ENVIRONMENT=Development` at launchSettings.json:10,19; no overriding env var observed on the parent) | launchSettings.json |
| Uptime at first observation | 55m 42s with zero listening sockets | `(Get-Date) - (Get-Process -Id 17212).StartTime`; `netstat -ano` |

Binary freshness: `PointOfSale.API\bin\Debug\net9.0\PointOfSale.Infrastructure.dll`
timestamp 2026-07-24 18:19 — same minute the process launched; source files last modified
17:33. The running binary contains the current (modified) working-tree source, including
both cycle participants. A rebuild attempted during this session failed with MSB3021
(file locked by PID 17212), further proving the hung process holds the current build.

## 3. Effective Configuration

Configuration layering for `Development` (standard WebApplicationBuilder order:
`appsettings.json` → `appsettings.Development.json` → `appsettings.Development.local.json`
if added by the app → env vars):

| Setting | appsettings.json | appsettings.Development.local.json | Effective note |
|---|---|---|---|
| Redis:Enabled | `true` | `false` | **Irrelevant to the hang** — never read; resolution deadlocks before `CreateConnection()` executes (proven by call stack: zero frames in `RedisConnectionFactory.CreateConnection` / `ConnectWithTimeout`, and zero threads named `RedisConnectTimeoutGuard`/`RedisConnectTimeoutWatchdog` in the dump). |
| Redis:ConnectionString | `""` | `192.168.1.5:6379` | see above |
| ConnectionStrings:DefaultConnection | — | `Host=192.168.1.5;Port=5432;Database=PosEcommerceDb_Baseline;Username=postgres` | consistent with observed live PG sockets to 192.168.1.5:5432 |
| MongoDB | — | `mongodb://192.168.109.128:27017` | consistent with observed live Mongo sockets to 192.168.109.128:27017 |
| RabbitMQ:Host | — | `192.168.1.5` | no RabbitMQ (5672) socket observed for PID 17212 — MassTransit bus start never reached |
| Elasticsearch:Url | — | `http://192.168.1.5:9200` (Enabled=true) | no 9200 socket observed for PID 17212 |

The live socket table (Section 10) matches `appsettings.Development.local.json` values
(PG at 192.168.1.5:5432, Mongo at 192.168.109.128:27017), confirming that file is loaded
by the running process.

## 4. Host Lifetime Timeline

| Event | Timestamp | Evidence |
|---|---|---|
| Process start | 2026-07-24 18:19:30 | Win32_Process CreationDate |
| PostgreSQL connections established (2) | during startup window | netstat: `192.168.1.70:52687/52688 -> 192.168.1.5:5432 ESTABLISHED` (PID 17212) |
| MongoDB connections established (2) | during startup window | netstat: `192.168.109.1:51830/51831 -> 192.168.109.128:27017 ESTABLISHED` (PID 17212) |
| Redis connections established (2) | during startup window | netstat: `-> 192.168.1.5:6379 ESTABLISHED` (PID 17212) — attributable to `AddStackExchangeRedisCache`/other Redis client paths, NOT to `RedisConnectionFactory` (whose connect code has zero frames in the dump) |
| Kestrel bind (`Now listening`) | **NEVER** | `netstat` shows NO LISTEN entry for port 5010 owned by 17212; `curl http://localhost:5010/health` → HTTP 000 after 8s |
| ApplicationStarted | **NEVER** | no listener; main thread frozen (Section 6) |
| Dump captured | 2026-07-24 19:15 (uptime ≈ 56 min) | `dotnet-dump collect -p 17212 -o C:\Users\fadya\tmp\startup2\app.dmp` → "Complete", 502,836,054 bytes |

Forward progress stops on the main thread inside DI singleton resolution and never
resumes for the remaining ~56 minutes of observation. Duration of the block at capture
time: ≥ 55 minutes (process start 18:19:30 → dump 19:15), unbounded.

## 5. Runtime Thread Inventory

Source: `dotnet-dump analyze app.dmp -c threads -c clrthreads`
(artifacts: `C:\Users\fadya\tmp\startup2\threads.txt`, `stacks.txt`, `stacks_full.txt`).

- Native (OS) threads: 45
- Managed threads (`clrthreads`): ThreadCount 37, Background 26, Dead 9, Unstarted 1, Pending 0
- Threads with managed frames on their stacks (`clrstack -all`): **2**
  - DBG 3 / OSID `0x3d9c` — Finalizer thread, idle (`DebuggerU2MCatchHandlerFrame` only)
  - DBG 0 / OSID `0x364` — **main/startup thread, the only working thread — BLOCKED** (Section 6)
- All other live managed threads (thread-pool workers, MTA threads) show no managed
  frames: parked/idle. The thread pool is NOT exhausted; this is not a pool-starvation
  hang.
- No threads named `RedisConnectTimeoutGuard` or `RedisConnectTimeoutWatchdog` exist —
  direct proof `RedisConnectionFactory.ConnectWithTimeout` (RedisConnectionFactory.cs:183-250)
  was never invoked.

## 6. Managed Thread Stack Analysis — the blocked thread

Thread DBG 0, OSID **0x364** (main thread). Top of stack (verbatim from `clrstack -all`):

```
System.Threading.WaitHandle.WaitOneCore(IntPtr, Int32, Boolean)              [HelperMethodFrame]
System.Threading.WaitHandle.WaitOneNoCheck(...)                              WaitHandle.cs @ 183
System.Threading.WaitHandle.WaitOne()                                        WaitHandle.cs @ 503   <- INFINITE kernel wait
Microsoft.Extensions.DependencyInjection.ServiceLookup.StackGuard.RunOnEmptyStackCore(...)  StackGuard.cs @ 74
Microsoft.Extensions.DependencyInjection.ServiceLookup.StackGuard.RunOnEmptyStack(...)      StackGuard.cs @ 53
```

Below that, the stack is a **strictly repeating 12-frame cycle** repeated for the entire
remaining depth of the stack (thousands of frames; the recursion marker frame
`PointOfSale.Infrastructure.Redis.RedisServiceCollectionExtensions+<>c.<AddRedisCache>b__0_1(System.IServiceProvider)`
appears **813 times** in the captured `clrstack -all` output alone — `grep -c` on
stacks.txt):

```
CallSiteRuntimeResolver.VisitRootCache          (resolve singleton IConnectionMultiplexer)
CallSiteVisitor`2.VisitCallSite
CallSiteRuntimeResolver.VisitConstructor        (construct RedisConnectionFactory — 3-param ctor)
CallSiteRuntimeResolver.VisitRootCache          (resolve singleton RedisConnectionFactory)
CallSiteVisitor`2.VisitCallSite
CallSiteRuntimeResolver.Resolve
ServiceProvider.CreateServiceAccessor
ServiceProvider.GetService
ServiceProviderEngineScope.GetService
ServiceProviderServiceExtensions.GetRequiredService
ServiceProviderServiceExtensions.GetRequiredService<T>
PointOfSale.Infrastructure.Redis.RedisServiceCollectionExtensions+<>c.<AddRedisCache>b__0_1   <- the IConnectionMultiplexer factory lambda
```

Blocked-thread report (as required):

| Field | Value |
|---|---|
| Thread | DBG 0 / OSID 0x364 (main thread) |
| State | `202a020` (MTA), waiting |
| Current frame | `System.Threading.WaitHandle.WaitOneCore` |
| Wait reason | infinite `WaitHandle.WaitOne()` issued by MEDI `StackGuard.RunOnEmptyStackCore` (StackGuard.cs:74), which awaits a continuation that itself re-enters the same recursion |
| Owner / blocking object | self-inflicted: the DI resolution it is waiting on can never complete (cycle) |
| Duration | ≥ 55 minutes at dump time; unbounded |

`StackGuard.RunOnEmptyStack` exists precisely to prevent `StackOverflowException` on deep
callsite chains; here it converts what would have been a crash-with-stack-trace into a
silent infinite hang.

## 7. Task State Analysis

- The blocking wait is NOT task-based: it is a raw kernel `WaitHandle.WaitOne()` inside
  `StackGuard.RunOnEmptyStackCore` (frame evidence in Section 6). No `Task.Wait`,
  `SemaphoreSlim`, `ManualResetEventSlim`, `Lazy<T>.Value`, or async state-machine frames
  appear anywhere on the blocked thread.
- No other managed thread has task continuation frames — there are no faulted, no
  long-running, and no network-await tasks holding up startup.
- Consequence: `Lazy<IConnectionMultiplexer?>` in `RedisConnectionFactory` (line 32) is
  never materialized — the hang precedes any use of the factory instance.

## 8. HostedService Live Timeline

| HostedService stage | Live state | Evidence |
|---|---|---|
| Any `IHostedService.StartAsync` | **NEVER ENTERED** | The single active thread (0x364) is inside singleton DI resolution; no `HostedService`, `BackgroundService`, `GenericWebHostService`, or MassTransit frames exist anywhere in `clrstack -all` output (grep for `StartAsync|ExecuteAsync|GenericWebHostService|MassTransit` over stacks.txt returns only DI/WaitOne frames). |
| GenericWebHostService / Kestrel | **NEVER ENTERED** | no LISTEN socket on 5010; no Kestrel frames in dump |
| MassTransit bus | **NEVER STARTED** | no AMQP (5672) socket for PID 17212 |

Whether the fatal resolution was triggered during `builder.Build()`
(ValidateOnStart / eager singleton materialization) or during the first
`IHostedService` activation inside `Host.StartAsync()` is not distinguishable from the
captured frames (the stack bottom is beyond the clrstack capture window — the recursion
consumed the entire visible stack). What IS proven: no hosted service ever ran, and the
block sits in singleton graph construction on the startup thread.

## 9. Dependency Injection Resolution Trace (complete activation cycle)

```
resolve IConnectionMultiplexer  (singleton, factory)
  -> factory lambda: RedisServiceCollectionExtensions.cs:64-68
       sp.GetRequiredService<RedisConnectionFactory>()          (line 66)
     -> resolve RedisConnectionFactory  (singleton, constructor)
          MEDI constructor selection: picks the LARGEST constructor whose parameters are
          all resolvable. RedisConnectionFactory has TWO public constructors:
            (IOptions<RedisSettings>, ILogger<RedisConnectionFactory>)                       RedisConnectionFactory.cs:24
            (IOptions<RedisSettings>, ILogger<RedisConnectionFactory>, IConnectionMultiplexer?)  RedisConnectionFactory.cs:35   <- SELECTED
          Because IConnectionMultiplexer IS registered (line 64), the 3-parameter
          constructor wins.
        -> constructor parameter: IConnectionMultiplexer
          -> resolve IConnectionMultiplexer  (singleton, factory)   <<< CYCLE — back to top
```

Proof of selection at runtime: the repeating stack segment shows
`VisitConstructor` (constructing `RedisConnectionFactory`) immediately dispatching back
into `VisitRootCache` for a dependency that re-invokes `<AddRedisCache>b__0_1` — i.e. the
constructor being built demands `IConnectionMultiplexer`. Only the 3-parameter
constructor has that dependency. 813 recorded iterations of this exact cycle on one
thread is the runtime signature.

Why the container did not throw `CircularDependencyException`: MEDI's cycle detection
operates on the static callsite graph. The `IConnectionMultiplexer` edge into
`RedisConnectionFactory` is hidden inside an opaque `Func<IServiceProvider, object>`
(the lambda), and the return edge is a fresh top-level `GetRequiredService` call rather
than a child callsite — so each loop iteration looks like a brand-new root resolution.
Each `VisitRootCache` re-enters because the singleton under construction is not yet
published to the root cache, and the per-callsite lock is reentrant on the owning thread.

Provenance of the cycle: the 3-parameter constructor and the lambda's null-returning
rewrite are both **uncommitted working-tree modifications** (git diff vs HEAD `8317aef1`:
HEAD's `RedisConnectionFactory.cs` has a single constructor; the working tree added the
`connectionOverride` overload — added for testability, cf.
`PointOfSale.Tests/Infrastructure/ProductInventorySqlAuthorityFallbackTests.cs:444`).
The hang is therefore introduced by the current uncommitted stabilization changes, not
by baseline commit code.

## 10. Network Activity Timeline

`netstat -ano` filtered to PID 17212 during the hang:

| Remote endpoint | Service | State | Count |
|---|---|---|---|
| 192.168.1.5:6379 | Redis | ESTABLISHED | 2 |
| 192.168.1.5:5432 | PostgreSQL | ESTABLISHED | 2 |
| 192.168.109.128:27017 | MongoDB | ESTABLISHED | 2 |
| (none) | RabbitMQ 5672 | — | 0 |
| (none) | Elasticsearch 9200 | — | 0 |
| local :5010 | Kestrel | **NO LISTEN** | 0 |

All established connections are healthy (no SYN_SENT / FIN_WAIT / repeated retries
observed) — no network dependency is timing out or blocking. The absent RabbitMQ and
Elasticsearch connections corroborate that startup froze before bus/ES initialization.

## 11. Blocking Wait Chain

```
Host startup (main thread 0x364)
  -> singleton DI resolution (trigger: first resolution touching the Redis singleton graph)
    -> IConnectionMultiplexer factory lambda            RedisServiceCollectionExtensions.cs:64
      -> GetRequiredService<RedisConnectionFactory>     RedisServiceCollectionExtensions.cs:66
        -> ctor(…, IConnectionMultiplexer?)             RedisConnectionFactory.cs:35
          -> resolve IConnectionMultiplexer  ──────────┐
    ┌──────────────────────────────────────────────────┘
    └-> (repeat ×∞; 813 iterations captured)
          -> StackGuard.RunOnEmptyStack (stack nearly exhausted)
            -> WaitHandle.WaitOne()  [INFINITE]         StackGuard.cs:74 / WaitHandle.cs:503
```

## 12. Host.StartAsync Waiting Chain

`Host.StartAsync()` is not waiting on any HostedService, Kestrel, MassTransit, Redis,
Mongo, RabbitMQ, options validation, or ApplicationLifetime component. The startup
thread never got past **ServiceProvider singleton materialization** for the Redis
component graph. The single blocking component class is: **Dependency Injection /
ServiceProvider (CallSiteRuntimeResolver)** executing an unresolvable circular
registration.

## 13. First Component That Never Returned

`PointOfSale.Infrastructure.Redis.RedisServiceCollectionExtensions+<>c.<AddRedisCache>b__0_1(IServiceProvider)`
— the `IConnectionMultiplexer` singleton factory lambda
(`RedisServiceCollectionExtensions.cs:64-68`). It is the first application-owned frame
entered on the blocked resolution and it never returns, because its own body re-triggers
its own resolution transitively via `RedisConnectionFactory`'s 3-parameter constructor
(`RedisConnectionFactory.cs:35-43`).

## 14. Verified Root Cause

**Singleton dependency cycle: `IConnectionMultiplexer` (factory lambda) ⇄
`RedisConnectionFactory` (3-parameter constructor with `IConnectionMultiplexer?`
parameter).** MEDI selects the 3-parameter constructor because `IConnectionMultiplexer`
is registered; the runtime-lambda edge defeats static cycle detection; resolution recurses
without bound; MEDI's `StackGuard` suppresses the would-be `StackOverflowException` and
parks the startup thread in an infinite kernel `WaitHandle.WaitOne()`. Startup freezes
silently before any hosted service, Kestrel bind, or Redis connect attempt.

Root-cause criteria:
1. Observed directly at runtime — clrstack of live process PID 17212 (dump 19:15, uptime 56m). ✔
2. Reproducible — deterministic consequence of the registration shape; process has been in this state since 18:19:30 and any launch of this build re-executes the identical resolution path. ✔
3. Call stack proves ownership — 813 captured iterations naming `<AddRedisCache>b__0_1` and the `RedisConnectionFactory` constructor callsite. ✔
4. Blocking chain identified — Section 11, terminal frame `WaitHandle.WaitOne()` at StackGuard.cs:74. ✔
5. No competing explanation fits — Section 15. ✔

## 15. Alternative Hypotheses Eliminated

| Hypothesis | Verdict | Disproving evidence |
|---|---|---|
| Redis server unresponsive / sync `ConnectionMultiplexer.Connect` deadlock (historic root cause) | ELIMINATED | Zero frames in `CreateConnection`/`ConnectWithTimeout`; zero `RedisConnectTimeoutGuard`/`Watchdog` threads; hang precedes any Redis:Enabled evaluation. |
| Blocking `IHostedService.StartAsync` | ELIMINATED | No hosted-service frames on any thread; hosted services never entered (Section 8). |
| MassTransit / RabbitMQ bus start | ELIMINATED | No AMQP socket, no MassTransit frames. |
| Thread-pool starvation | ELIMINATED | 20+ idle pool threads with empty managed stacks; only 1 blocked thread. |
| PostgreSQL / MongoDB / Elasticsearch network wait | ELIMINATED | PG and Mongo sockets ESTABLISHED and idle; no ES socket needed yet; no network-IO frames on the blocked thread. |
| Kestrel port conflict | ELIMINATED | No process listening on 5010; Kestrel never reached bind. |
| Options `ValidateOnStart` validation hang | ELIMINATED as an independent cause | The blocked frames are pure CallSiteRuntimeResolver recursion into the Redis graph, not validation logic (validation could at most be the *trigger* of the resolution, not the blocker). |

## 16. Evidence Matrix

| # | Claim | Evidence artifact | Location |
|---|---|---|---|
| E1 | Process hung, no listener, 55+ min | netstat, curl 000, Get-Process StartTime | session transcript |
| E2 | Full memory dump of hung process | `app.dmp` (502 MB), PID 17212 | `C:\Users\fadya\tmp\startup2\app.dmp` |
| E3 | Thread inventory | `threads` + `clrthreads` output | `C:\Users\fadya\tmp\startup2\threads.txt` |
| E4 | Blocked-thread stack, 813× recursion of factory lambda | `clrstack -all` output | `C:\Users\fadya\tmp\startup2\stacks.txt` (3.4 MB), `stacks_full.txt` |
| E5 | Cycle participant: factory lambda calling GetRequiredService<RedisConnectionFactory> | source | `PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs:64-68` |
| E6 | Cycle participant: 3-param constructor taking IConnectionMultiplexer? | source | `PointOfSale.Infrastructure/Redis/Services/RedisConnectionFactory.cs:35-43` |
| E7 | Both files are uncommitted working-tree modifications vs HEAD 8317aef1 | `git status` / `git diff` | session transcript |
| E8 | Terminal wait frame | `WaitHandle.WaitOne` ← `StackGuard.RunOnEmptyStackCore` (StackGuard.cs:74) | stacks.txt lines 7-10 |
| E9 | Running binary = current source | DLL timestamp 18:19 = process launch minute; MSB3021 lock by PID 17212 | session transcript |

## 17. Recommended Remediation Strategy (NOT implementation)

Break the singleton cycle — any ONE of the following is sufficient; none were applied in
this phase:

1. Remove the DI ambiguity: mark the 3-parameter `RedisConnectionFactory` constructor
   with `[ActivatorUtilitiesConstructor]` on the *2-parameter* one — or better, make the
   test-only override constructor `internal`/non-DI-visible (it exists solely for
   `ProductInventorySqlAuthorityFallbackTests.cs:444`).
2. Or invert the factory: register `IConnectionMultiplexer` via a lambda that constructs
   the multiplexer directly from `RedisSettings` (no `RedisConnectionFactory` resolution),
   keeping `RedisConnectionFactory` consumers unchanged.
3. Defensive hardening (secondary): any DI factory lambda that calls back into
   `IServiceProvider` for a type whose constructors may reference the service being
   registered should be treated as a review-blocking pattern in this codebase.

Also recommended: terminate the currently hung PID 17212 (it holds file locks that break
rebuilds) once no further dump analysis is needed.

## 18. Certification

**A. Runtime root cause conclusively identified.**

The blocking component is the Microsoft.Extensions.DependencyInjection singleton
resolution of the pair {`IConnectionMultiplexer` factory lambda,
`RedisServiceCollectionExtensions.cs:64` ⇄ `RedisConnectionFactory` 3-parameter
constructor, `RedisConnectionFactory.cs:35`}, observed live on thread OSID 0x364 of
PID 17212 as an unbounded 12-frame resolution cycle (813 captured iterations) terminating
in an infinite `WaitHandle.WaitOne()` inside MEDI `StackGuard` — with the process frozen
pre-Kestrel for the entire 56-minute observation window.
