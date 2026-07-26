# STARTUP-3 — Surgical DI Cycle Remediation

Phase: STARTUP-3
Scope: Remove ONLY the verified singleton dependency cycle. No redesign, no cleanup, no unrelated edits.
Date: 2026-07-24
Author: Runtime Recovery (agent-executed, evidence-backed)

---

## 1. Executive Summary

STARTUP-1 and STARTUP-2 conclusively proved that application startup froze inside
the DI container while resolving `IConnectionMultiplexer`, because
`RedisConnectionFactory` exposes two public constructors and
Microsoft.Extensions.DependencyInjection (MEDI) greedily selected the 3-parameter
test-only constructor. That constructor takes an `IConnectionMultiplexer`, which is
registered via a factory lambda that itself resolves `RedisConnectionFactory` —
a self-referential singleton cycle. STARTUP-2's forensic dump captured 813 repeated
frames naming `<AddRedisCache>b__0_1` and the `RedisConnectionFactory` constructor
callsite; `CreateConnection()` was never reached, proving the Redis server was NOT
involved.

The surgical fix (OPTION A) applies a single attribute — `[ActivatorUtilitiesConstructor]`
— to the 2-parameter constructor, forcing MEDI to always select it. This breaks the
cycle at its exact root with a two-hunk diff and zero behavioral change.

Result: the DI cycle is ELIMINATED. Startup now traverses the ENTIRE DI graph and
executes hosted services (MassTransit endpoint config, Hangfire storage, outbox
workers, Mongo initializers, Chart-of-Accounts seeder, financial aggregate refresh,
identity reconciliation) — all of which were previously unreachable because the host
never got past DI resolution. `builder.Build()` now succeeds and `Host.StartAsync()`
progresses into background-service execution.

However, Kestrel does NOT yet bind (`Now listening` / `Application started` never
emitted; ports 5010/7263 closed). This is a SEPARATE, downstream blocker unrelated to
the Redis DI cycle that was the sole target of STARTUP-3. See sections 10 and 12.

Certification: **B — Root cause removed; runtime validation of full listen-state pending
(new, unrelated downstream blocker surfaced).**

---

## 2. Verified Runtime Root Cause

Confirmed by direct read of the two files (STEP 1 — not assumed):

- `PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs`
  - Line 57: `services.AddSingleton<RedisConnectionFactory>();` — bare singleton, MEDI must pick a constructor.
  - Lines 64–68: `services.AddSingleton<IConnectionMultiplexer>(sp => { var factory = sp.GetRequiredService<RedisConnectionFactory>(); return factory.Connection; });`

- `PointOfSale.Infrastructure/Redis/Services/RedisConnectionFactory.cs`
  - Lines 24–33: 2-parameter constructor `(IOptions<RedisSettings>, ILogger<...>)` — the real runtime constructor.
  - Lines 35–43: 3-parameter constructor `(IOptions<RedisSettings>, ILogger<...>, IConnectionMultiplexer? connectionOverride)` — test-only override.

The cycle (verified, matches STARTUP-2 dump exactly):

```
resolve IConnectionMultiplexer (singleton)
  -> factory lambda <AddRedisCache>b__0_1
    -> sp.GetRequiredService<RedisConnectionFactory>()
      -> MEDI picks the 3-param ctor (most satisfiable params; IConnectionMultiplexer is registered)
        -> needs IConnectionMultiplexer
          -> factory lambda <AddRedisCache>b__0_1   [RECURSION -> 813 frames -> hang]
```

`CreateConnection()` never runs → the Redis server is not involved. Diagnosis FINAL and
independently reconfirmed. The runtime report was NOT contradicted.

---

## 3. Files Modified

1. `PointOfSale.Infrastructure/Redis/Services/RedisConnectionFactory.cs`
   - Added `using Microsoft.Extensions.DependencyInjection;`
   - Added `[ActivatorUtilitiesConstructor]` on the 2-parameter constructor + explanatory comment.

No other files were modified for STARTUP-3.

(Note: the same file also contains a pre-existing, uncommitted `ConnectWithTimeout`
hardening block from STARTUP-1/2. That is NOT part of STARTUP-3 and was left untouched.)

---

## 4. Exact Git Diff (STARTUP-3 change only)

```diff
@@ -1,7 +1,9 @@
+using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Logging;
 using Microsoft.Extensions.Options;
 using PointOfSale.Infrastructure.Redis.Configuration;
 using StackExchange.Redis;
 using System.Threading;

@@ -20,6 +22,11 @@ public class RedisConnectionFactory : IDisposable
     private readonly Lazy<IConnectionMultiplexer?> _connection;
     private bool _disposed;

+    // STARTUP-3: This attribute forces Microsoft.Extensions.DependencyInjection to always select
+    // this 2-parameter constructor. Without it, MEDI greedily selects the 3-parameter test-only
+    // constructor below (because IConnectionMultiplexer is registered via a factory that resolves
+    // RedisConnectionFactory), producing a singleton resolution cycle that hangs Host.StartAsync().
+    [ActivatorUtilitiesConstructor]
     public RedisConnectionFactory(
         IOptions<RedisSettings> settings,
         ILogger<RedisConnectionFactory> logger)
     {
```

---

## 5. Why This Fix Works

`[ActivatorUtilitiesConstructor]` is the MEDI-native, first-class mechanism for telling
the container exactly which constructor to use. When present, MEDI stops applying its
greedy "most satisfiable parameters" heuristic and unconditionally binds the annotated
constructor. Because the 2-parameter constructor depends only on `IOptions<RedisSettings>`
and `ILogger<>` — neither of which touches `IConnectionMultiplexer` — the resolution of
`RedisConnectionFactory` no longer requires `IConnectionMultiplexer`. The recursive edge
in the singleton graph is severed at its exact origin.

Runtime behavior, API surface, and public contracts are fully preserved:
- The 2-param constructor was already the intended runtime constructor.
- `IConnectionMultiplexer` is still registered and still resolves lazily via
  `RedisConnectionFactory.Connection` (null when disabled/unreachable) — no consumer contract changes.
- The 3-param test-only constructor remains public and unchanged; tests keep instantiating it directly via `new`.

---

## 6. Why Alternative Fixes Were Rejected

- Changing constructor visibility (make 3-param `internal`/non-public): would work, but
  larger and riskier — 8 test files call `new RedisConnectionFactory(...)` with the 3-arg
  overload; narrowing visibility could break test compilation depending on assembly
  boundaries / InternalsVisibleTo. The attribute achieves the same result with zero risk to tests.
- Removing the 3-param constructor: breaks 8 test files. Rejected — violates "preserve tests".
- OPTION B (adjust factory registration, e.g. register `IConnectionMultiplexer` without
  resolving the factory): larger blast radius, changes the registration contract, and does
  not address the root ambiguity. Rejected in favor of the minimal, root-cause fix.
- Redesigning Redis/DI/HostedServices: explicitly forbidden by STARTUP-3 scope.

STARTUP-2's own report (`docs/runtime/STARTUP-2_RUNTIME_EXECUTION_TRACE.md:333`) already
recommended `[ActivatorUtilitiesConstructor]` on the 2-parameter constructor — this fix
is the pre-endorsed minimal remediation.

---

## 7. DI Graph BEFORE

```
IConnectionMultiplexer (singleton, factory)
    │  factory: sp.GetRequiredService<RedisConnectionFactory>().Connection
    ▼
RedisConnectionFactory (singleton)
    │  MEDI selects 3-param ctor (greedy) -> requires IConnectionMultiplexer
    ▼
IConnectionMultiplexer  ──►  (back to top)  ==> CYCLE (813 frames, hang)
```

---

## 8. DI Graph AFTER

```
IConnectionMultiplexer (singleton, factory)
    │  factory: sp.GetRequiredService<RedisConnectionFactory>().Connection
    ▼
RedisConnectionFactory (singleton)
    │  [ActivatorUtilitiesConstructor] -> 2-param ctor
    ▼
IOptions<RedisSettings> + ILogger<RedisConnectionFactory>   ==> ACYCLIC, resolves cleanly
```

---

## 9. Build Results

- `dotnet build PointOfSale.Infrastructure` → **0 Errors**, 85 warnings (all pre-existing, unrelated).
- `dotnet build PointOfSale.API` → **0 Errors**, 0 warnings (after clearing a stale locked
  API process; the initial MSB3021/MSB3027 file-lock errors were an orphaned running instance,
  not compile errors).
- The `[ActivatorUtilitiesConstructor]` attribute resolved cleanly (Microsoft.Extensions.DependencyInjection.Abstractions already referenced transitively).

---

## 10. Runtime Results

Command: `ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build` (Redis `Enabled:false` in
`appsettings.Development.local.json` — irrelevant to the cycle, which occurred during DI
resolution before `CreateConnection()`).

Observed (reproduced across two clean runs):
- ✅ `builder.Build()` completes (no 813-frame recursion; the cycle is gone).
- ✅ `Host.StartAsync()` progresses into hosted-service execution — previously IMPOSSIBLE.
- ✅ Hosted services run: MassTransit endpoints configured, Hangfire PostgreSQL storage,
  MongoDbContext, outbox/compensation workers, ChartOfAccounts seeder, CommerceHub daily
  financial aggregate refresh (365 rows), Canonical identity reconciliation, etc.
- ❌ `Now listening on: ...` — NOT emitted (0 occurrences).
- ❌ `Application started` / GenericWebHost lifetime message — NOT emitted (0 occurrences).
- ❌ Ports 5010 (http) / 7263 (https) — NOT LISTENING; `curl` → HTTP 000.
- ❌ Health / Swagger — unreachable (consequence of Kestrel not binding).

Interpretation: The STARTUP-3 target (the Redis singleton DI cycle) is definitively
eliminated — the forensic recursion no longer reproduces and startup advances far beyond
the previous freeze point. A SEPARATE, downstream startup blocker prevents Kestrel from
binding. It is not the Redis cycle and is out of STARTUP-3 scope.

---

## 11. Regression Results

- ✅ Redis enabled path: unchanged. Registration and `CreateConnection()` logic untouched;
  `IConnectionMultiplexer` still resolves via `RedisConnectionFactory.Connection`.
- ✅ Redis disabled path: unchanged (verified live — ran with `Enabled:false`, factory returns null gracefully).
- ✅ Tests using the constructor override: still compile — the 3-param public constructor is
  intact and tests call it via `new` (8 test files: ShippingMethodServiceCurrencyConsistency,
  StorefrontTenantIsolation, PaymobCallbackValidation, PaymentServiceCreatePaymentIntentGuard,
  ProductVariantPriceDistributedReliabilityIntegration, ProductInventorySqlAuthorityFallback,
  CommerceHubGovernanceCacheInvalidationService, FawryCertificationClosure5). The attribute
  only affects MEDI-driven activation, not direct `new`.
- ✅ DI graph validity: the singleton graph is now acyclic; `builder.Build()` succeeds.

---

## 12. Remaining Risks

The STARTUP-3 root cause is closed, but the application does not yet reach a listening state
because of a distinct downstream blocker:

- Kestrel never binds despite background services running. The most likely candidates
  (to be triaged in a follow-up phase, NOT here) are a hosted service whose `StartAsync`
  blocks the startup pipeline before `app.Run()` binds Kestrel, or an ordering/await issue
  in `Program.cs` startup. Note `MarketplaceReadModelIndexInitializer` logs a Mongo failure
  (external Mongo at 192.168.109.128 unreachable) but that is caught/logged, not fatal.
- This blocker is OUT OF SCOPE for STARTUP-3 (which was strictly the Redis singleton cycle).
  Recommend a STARTUP-4 phase to forensically identify the pre-Kestrel-bind stall using a
  managed stack capture of the live process at the stall point (dotnet-stack/dotnet-dump with
  a resolvable DAC), focusing on the hosted-service `StartAsync` sequence around Program.cs.

No risk introduced by the STARTUP-3 change itself.

---

## 13. Certification

**B — Root cause removed but runtime validation pending.**

Justification: The verified singleton DI cycle (`IConnectionMultiplexer` ⇄
`RedisConnectionFactory` 3-param ctor) is conclusively eliminated by
`[ActivatorUtilitiesConstructor]`. Static verification confirms no cycle, no constructor
ambiguity, and no recursive `IServiceProvider` resolution remain. Build is clean (0 errors)
and `Host.StartAsync()` now advances through the full hosted-service graph — the STARTUP-2
forensic recursion no longer reproduces. Full listen-state validation (Kestrel bind /
`Now listening` / health / Swagger) is pending because a SEPARATE, unrelated downstream
startup blocker prevents Kestrel from binding; remediating it is outside STARTUP-3's
mandated scope and is recommended as a follow-up phase.
