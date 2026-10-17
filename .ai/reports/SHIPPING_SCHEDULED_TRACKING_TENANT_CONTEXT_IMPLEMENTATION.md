# Scheduled Shipment Tracking — Trusted Tenant Context Implementation

## 1. Verdict and bounded scope

**PASS — scheduled tracking now establishes a trusted per-tenant context before
resolving tenant-dependent services. Fresh verification: 20/20 new PostgreSQL
cases and 81/81 existing regressions; zero failed, skipped or not-executed cases.**

- Date: **2026-10-09**.
- Workspace: exclusively MAIN/PRIMARY Windows,
  `C:\Users\fadya\source\repos\PointOfSale`.
- HEAD: `256339621c0ea49f3602097cdacdf89ffe71754f`; existing uncommitted cancellation,
  Manual COD, refund-lineage, Treasury and other changes were preserved.
- No subagents, commits, resets, reverts, migrations, live carrier calls or merchant
  shipment processing were used in this phase.
- Runtime verification used deterministic isolated test DI providers and PostgreSQL
  fixtures. The MAIN API was not restarted and its real scheduled job was not invoked.
  The fresh verification configuration is not a claim that the running Debug API has
  activated this new tracking build.

This closes only the tenant-context finding in
`SHIPPING_CARRIERS_END_TO_END_FORENSIC_AUDIT.md` sections 1 and 2.4. It does not
certify complete provider tracking, catch-up progression, COD collection or remittance.

## 2. Actual execution trace and root cause

### Scheduler and host

1. `PointOfSale.API/Program.cs` registers scoped `ITrackingSyncJob` →
   `TrackingSyncJob`. The job constructor requires only `IServiceScopeFactory` and
   its logger, so scheduler activation itself does not resolve tenant-owned data.
2. When Hangfire is enabled, Program registers
   `PointOfSale.API/Jobs/HangfireInitializationHostedService.cs`, which registers
   recurring job **`shipping-tracking-sync`**, expression
   `job => job.SyncAllAsync(CancellationToken.None)`, cron **`*/30 * * * *`**.
   The scheduler/host registration and cron were not changed.
3. Previously `SyncAllAsync` created an ordinary scope, resolved
   `ApplicationDbContext` and tried to discover tenants from `Shipments`.
   `SyncTenantAsync` and every `SyncShipmentAsync` worker independently created
   another scope without initializing tenant context.

### Canonical tenant authority

- `PointOfSale.Core/Interfaces/ITenantContext.cs` is the canonical tenant abstraction.
- `PointOfSale.Infrastructure/MultiTenancy/TenantContext.cs` is its scoped concrete
  implementation. Program registers the interface as the **same scoped instance**.
- The authoritative tenant directory is PostgreSQL **`Tenants`**, represented by
  `Core/Entities/Tenant.cs` and the existing `ApplicationDbContext.Tenants` set.
  Tenant is a global directory entity, not `ITenantOwned`, and its EF mapping has
  no mandatory tenant-owned query filter. No existing tenant-directory interface
  was found; this task adds a scheduler-specific read-only projection boundary over
  that existing table rather than another tenant system.
- Eligible identity requires a nonempty ID, `IsActive`, `Status == Active` and
  `DeletedAtUtc == null`, preserving both lifecycle authority and the legacy mirror
  as a fail-closed condition.

### Why the old path could not discover or process shipments

`ApplicationDbContext` receives `ITenantContext`; `CurrentTenantId` reads that
instance's `TenantId`. The mandatory shipment filter is:

```csharp
modelBuilder.Entity<Shipment>().HasQueryFilter(e =>
    !e.IsDeleted && e.TenantId == CurrentTenantId);
```

An uninitialized fresh scope has no tenant. Its tenant-owned shipment reads return
no rows. A `Where(s => s.TenantId == suppliedId)` predicate does not override the
global filter. Fixing discovery alone would still leave both selection and the
parallel service scopes unbound.

## 3. Implementation and tenant initialization order

### Narrow directory bootstrap

`Shipping/BackgroundJobs/TrackingTenantDirectory.cs` introduces
`ITrackingTenantDirectory.ListEligibleTenantIdsAsync` and its implementation.
The interface returns only materialized tenant IDs, not a DbContext, IQueryable,
shipment, carrier configuration or credential.

The implementation uses the existing `DbContextOptions<ApplicationDbContext>` to
create a short-lived context **without tenant affinity only for directory bootstrap**.
Its sole query is an `AsNoTracking` projection of eligible SQL `Tenants.Id`, ordered
by primary key. It disposes the context and the job disposes the bootstrap scope
before tenant-owned execution begins. It never queries shipments to discover tenants,
never writes and never suppresses query filters. Tenant-owned queries would remain
fail-closed in that context. The primary-key directory projection supplies each
eligible tenant once, including tenants whose scoped shipment query will be empty.

Program adds scoped registration for this directory next to the existing tracking
job registration. Ordinary application DbContext/service registrations are unchanged.

### Every selection and worker scope

Both `SyncTenantAsync` and `SyncShipmentAsync` use the same private
`CreateTenantScopeAsync` lifecycle:

1. Honor cancellation and reject `Guid.Empty` before resolving tenant-dependent data.
2. Create a **new async DI scope**.
3. Resolve `TenantContext` and `ITenantContext`; verify reference equality and reject
   any prepopulated context. A foreign alias or unexpected inherited identity cannot
   silently authorize execution.
4. Call **`TenantContext.SetTenantId(tenantId)`**.
5. **Only then resolve `ApplicationDbContext`**, and therefore its scoped options,
   repositories/interceptors and service dependencies.
6. Revalidate current SQL `Tenants` eligibility for that locator before any shipment
   selection or carrier-service resolution. Repeat this check in **every worker**,
   so suspension between batch selection and worker creation fails closed.
7. Perform the ordinary filtered shipment selection, or resolve the scoped
   `ICarrierShipmentService` for that worker.
8. Await execution and asynchronously dispose the scope, including on initialization
   or query failures.

The public tenant-specific job argument is a locator, not independent authority;
the SQL lifecycle check still applies. All-tenants execution takes IDs only from the
directory, not request headers, shipment payloads or ambient request context.

### Selection, configuration and persistence

- Selection retains the tenant predicate and normal global filters, nonempty tracking
  requirement, original terminal-state list, **15-minute** sync age, oldest-first
  ordering, **50**-shipment batch cap and chunks of at most **5** workers.
- Each worker passes its qualified tenant and selected shipment ID to the existing
  `CarrierShipmentService.SyncTrackingAsync`.
- That service resolves the shipment by tenant/ID, selects the registered adapter,
  calls canonical `ICarrierIntegrationAuthority.ResolveCredentialsAsync` with
  `CarrierIntegrationPurpose.Tracking`, invokes the adapter, then reloads and saves
  through its same tenant-bound ApplicationDbContext and existing transaction/policy.
- Canonical configuration resolution, decryption, provider mapping and persistence
  algorithms were not changed. The existing configuration loader has its own explicit
  tenant/code/nondeletion predicates; no filter bypass was added or adopted as a
  workaround by this job or directory. Tests use the actual canonical configuration
  authority and prove that the mock provider receives the correct tenant's account,
  key and environment.
- `CarrierTrackingResponse`, sync timestamp and any admitted lifecycle behavior stay
  within that worker's tenant scope. This task introduces no financial evidence model.

### Failure and cancellation handling

Tenant initialization/selection failures are logged by the all-tenants loop and
do not prevent the next eligible tenant's independent scope. Individual worker
failures retain the existing per-shipment failure result/logging. Failed initialization
disposes its scope without resolving a carrier service or falling back to another
identity. Genuine scheduler cancellation propagates instead of being swallowed as
an ordinary tenant/worker failure; the job also checks the token after the existing
carrier service returns, because that service currently catches some cancellations.

No automatic provider retry, new recurring job or changed concurrency policy was added.

## 4. Changed files

| File | This phase's change |
|---|---|
| `PointOfSale.Infrastructure/Shipping/BackgroundJobs/TrackingTenantDirectory.cs` | New narrow read-only SQL directory projection |
| `PointOfSale.Infrastructure/Shipping/BackgroundJobs/TrackingSyncJob.cs` | Directory discovery, trusted context-before-services scopes, current tenant lifecycle checks and cancellation propagation |
| `PointOfSale.API/Program.cs` | One new scoped directory registration; pre-existing cancellation registration preserved |
| `PointOfSale.Tests/Shipping/TrackingSyncTenantContextPostgresTests.cs` | 20 relational cases against the real job/scope/configuration/persistence path |
| `PointOfSale.Tests/Architecture/TenantIsolationCertificationFreezeTests.cs` | Align one stale ordinary-request assertion with current trusted-context recovery ownership |
| `.ai/reports/SHIPPING_SCHEDULED_TRACKING_TENANT_CONTEXT_IMPLEMENTATION.md` | This focused implementation and verification report |

The existing freeze expected `admin_tenant_scope_mismatch` and a comparison against
`request.TenantId` in `AdminAbandonedCartRecoveryController`. Inspection and a clean
scoped diff showed the controller was unchanged by this phase and already derives
settings identity exclusively from `RequiredTenantId`. The test now freezes that
trusted assignment, `TenantId = tenantId`, the same-ID service call and absence of
`request.TenantId`. Recovery product behavior and all other freeze assertions were
preserved; no obsolete body-tenant authority was restored.

## 5. Meaningful PostgreSQL coverage

All new cases use `TestDatabaseScope`, with its existing separate database
creation/reset behavior. DI uses real `TenantContext`, `TrackingTenantDirectory`,
`TrackingSyncJob`, `ApplicationDbContext`, `CarrierShipmentService`,
`CarrierIntegrationAuthority` and `CarrierCapabilityRegistry`. External adapters,
credential encryption and unrelated domain authorities are mocked; fixture credentials
are synthetic. No web host or merchant scheduler is started.

The DbContext factory rejects missing/empty context **at construction** and verifies
the concrete/interface alias. Recorded SQL identifies bootstrap and bound scopes.
Inside the mock provider, ordinary unqualified shipment/configuration queries prove
the global filter returns only that scope's tenant; credential/account assertions
then prove real configuration resolution uses the same identity. Persisted results
are reread in independent tenant-bound contexts.

| Test method (class `TrackingSyncTenantContextPostgresTests`) | Cases | Evidence |
|---|---:|---|
| `SyncAll_RealScopesDiscoverAndPersistOnlyTheirOwnShipmentsAndCredentials_WithoutRepeatedWork` | 1 | Two tenants, separate due shipments/accounts, exact per-tenant persisted response/timestamp; four independently bound DbContexts; immediate second cycle makes no extra provider calls |
| `ConcurrentWorkerScopes_KeepTheirOwnTenantAndAccount_AndDoNotRepeatRejectedCallsWithinCycle` | 1 | Two tenants, five simultaneously held worker responses each, ten distinct calls/scopes, correct account/context, unsuccessful replies cannot write or trigger events |
| `TenantWithNoEligibleShipments_NeverResolvesCarrierServiceOrTouchesOtherTenant` | 1 | Empty tenant selection cannot read/process the other tenant's due shipment |
| `DbConstructionFailure_IsIsolatedAndOtherEligibleTenantStillPersistsUnderItsOwnIdentity` | 1 | Tenant-local construction failure; other tenant still updates only its own row |
| `ProviderFailure_DoesNotLeakContextOrBlockAnotherTenant` | 1 | First tenant's injected provider exception cannot update its row or prevent the other's correct update |
| `SuppliedTenantLocator_MustBeNonemptyAndCurrentlyEligibleBeforeShipmentReads` | 5 | Empty, missing, inactive, suspended and soft-deleted tenant locators fail before shipment reads/provider resolution |
| `Directory_ExcludesInactiveSuspendedDeletedTenants_EvenIfTheyHaveDueShipments` | 1 | SQL directory excludes ineligible tenant IDs regardless of shipment eligibility |
| `TenantSuspendedBetweenSelectionAndWorker_IsRevalidatedBeforeProviderResolution` | 1 | Current SQL lifecycle is checked again in the fresh worker scope |
| `InvalidScopeTenantRegistration_FailsClosedBeforeDbOrProviderResolution` | 3 | Missing concrete context, foreign interface alias, or prepopulated context fails before dependent construction/SQL/provider work |
| `OrdinaryRequestScopeAndMissingContextFilters_RemainUnchangedWhileJobUsesIndependentScopes` | 1 | Request scope retains its identity while job runs; ordinary shipment/order/configuration filters remain present and unbound reads return no rows |
| `EmptyDirectory_PerformsNoTenantOwnedDiscoveryOrServiceResolution` | 1 | Bootstrap reads only `Tenants`; no shipment or carrier service resolution |
| `CancellationBeforeOrDuringTracking_PropagatesAndStopsFurtherChunks` | 2 | Pre-cancel and provider-time cancellation propagate; no later chunk/persisted tracking response |
| `ExistingDueAndTerminalSelectionSemantics_ArePreservedWithoutExpandingStatusProgression` | 1 | Due-only work, soft-delete/nontracking/recent/Delivered/Returned filtering; existing Failed short-circuit explicitly preserved |
| **Total** | **20** | Actual PostgreSQL query/update behavior, not a tenant-assignment-only mock |

Fixtures reload the persisted PostgreSQL baseline after insertion to compare
unchanged timestamps exactly at SQL microsecond precision. This corrected initial
test-only precision failures rather than loosening preservation checks.

## 6. Fresh build, discovery and final execution counters

Final build after all source/test changes:

```powershell
dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" -c ScheduledTrackingVerification --no-restore --no-incremental -v:quiet /clp:ErrorsOnly
```

**Succeeded: 444 warnings, 0 errors, 23.96s.** Referenced API/Core/Infrastructure
projects were built from current source in a separate configuration, preserving the
running Debug binaries. No project/test exclusions or package changes were made.

Actual `--list-tests` discovery from the final
`PointOfSale.Tests/bin/ScheduledTrackingVerification/net9.0/PointOfSale.Tests.dll`
listed **20** new expanded cases and **81** selected existing cases. Discovery used
the same filters as execution below, with `--no-build --no-restore --list-tests`.

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c ScheduledTrackingVerification --no-build --no-restore --filter "FullyQualifiedName~TrackingSyncTenantContextPostgresTests" --logger "trx;LogFileName=scheduled-tracking-tenant-context-certified.trx" --results-directory "C:\Users\fadya\AppData\Local\Temp\opencode" -v:minimal
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c ScheduledTrackingVerification --no-build --no-restore --filter "FullyQualifiedName~ShipmentTrackingAuthorityCertificationTests|FullyQualifiedName~ShipmentLifecycleAuthorityCertificationTests|FullyQualifiedName~CarrierIntegrationAuthorityRemediationTests|FullyQualifiedName~ShippingWebhookTenantResolutionTests|FullyQualifiedName~StorefrontTenantIsolationTests|FullyQualifiedName~TenantSaveChangesInterceptorTests|FullyQualifiedName~AdminTenantAuthorityTests|FullyQualifiedName~TenantIsolationCertificationFreezeTests|FullyQualifiedName~HangfireRecurringJobCronTests|FullyQualifiedName~O43FulfillmentShippingRemediationTests|FullyQualifiedName~ShipmentExternalRegistrationSafetyTests" --logger "trx;LogFileName=scheduled-tracking-tenant-context-certified-regressions.trx" --results-directory "C:\Users\fadya\AppData\Local\Temp\opencode" -v:minimal
```

| Final TRX | Total | Executed | Passed | Failed | Skipped | Not executed | Runner duration |
|---|---:|---:|---:|---:|---:|---:|---|
| `scheduled-tracking-tenant-context-certified.trx` | 20 | 20 | **20** | **0** | **0** | **0** | 1m40s |
| `scheduled-tracking-tenant-context-certified-regressions.trx` | 81 | 81 | **81** | **0** | **0** | **0** | 8s |
| **Disjoint selected scope** | **101** | **101** | **101** | **0** | **0** | **0** | |

Final XML `ResultSummary` counters were inspected: errors, timeouts, aborted,
inconclusive and notRunnable are also **0**. New-case TRX finish:
`2026-10-09T14:17:18.0408470Z`; regression finish:
`2026-10-09T14:15:45.3898946Z`. Initial fixture/diagnostic TRX files are retained
as history and are not counted as passing certification evidence. No whole-suite,
live-provider or live-merchant certification is claimed.

## 7. Filter and working-tree preservation evidence

Scoped diffs and initial/final working-tree status were reviewed. This phase added
only the files/edits listed above; pre-existing work remains. Program's prior scoped
cancellation executor registration remains alongside the new directory registration.
`git diff --check` passed for the edited tracked source/test files; Git emitted only
its existing LF-to-CRLF normalization notices.

The following source hashes matched before/final verification:

| File | Unchanged Git content hash |
|---|---|
| `Infrastructure/Data/ApplicationDbContext.cs` | `aa890534bcf1bf40f7e99b37fe82c248bb800774` |
| `Infrastructure/Shipping/CarrierShipmentService.cs` | `a59a8ca0a72919229c68630f1221965559cb5956` |
| `Infrastructure/Shipping/CarrierIntegrationAuthority.cs` | `09e09cd88bf74d5d0d6f1763b7e12afad60ca0fb` |
| `Core/Policies/ShipmentLifecycleTransitionPolicy.cs` | `a86070b54c59d3644160ec8a31ddb07940a3f96c` |
| `Infrastructure/Payments/PaymentLifecycleService.ManualCodSettlement.cs` | `7b72ac580f30224df1e0a0276554c4a4f08cb64e` |
| `Infrastructure/Accounting/ManualCodSettlementAuthority.cs` | `3cebf62c2cabba0849793a0b138e2a31767bf601` |
| `Infrastructure/Accounting/SettlementRefundLineage.cs` | `f2482c510b76022e4ed6ede3da26bf42194a2c52` |
| `Core/Interfaces/Payments/ManualCodSettlement.cs` | `70f9c189cd579053236615e80b4dac44e7259c12` |
| `Tests/Integration/ManualCodSettlementTests.cs` | `44649d4a3c765ad95b8f4d51555bdfe782085d29` |
| `Infrastructure/Migrations/20261008223819_ManualCodPermanentSettlementIdentity.cs` | `b7b3469ef455a3ded367567f3a4dd5d4be5f2352` |
| `Web/src/pages/treasury/TreasuryManualCodSettlement.jsx` | `45de756de165a067a16e4c9554983d2bb72049e3` |
| `Web/src/services/manualCodSettlementApi.js` | `c530cead64685e56b1202ef79c9ffcece704a9a8` |

Project prefixes in the table are abbreviated `PointOfSale.*`.
ApplicationDbContext's mandatory shipment/order/configuration filters and tenant
save guards are intact. New job/directory code contains no `IgnoreQueryFilters`,
global-scope clearing, filter-disable fallback or cross-tenant shipment discovery.
There are no schema, provider mapping, payment, accounting, inventory, order,
Manual COD, refund-lineage or remittance-executor edits.

## 8. Independent outstanding tracking findings and limitations

1. **Delivered/Returned observation gap:** the job still excludes Delivered, while
   the shared service/policy permits a Delivered → Returned evidence transition.
   Scheduled post-delivery return discovery is therefore not solved by this phase.
2. **Failed scheduling mismatch:** Failed remains selectable by the job, but the
   shared service short-circuits it without a provider call or sync-time update.
   The new preservation case confirms that behavior. Repeated old Failed rows can
   consume batch capacity; a separately reviewed terminal/catch-up strategy is needed.
3. **Missed intermediate status progression:** the shared service still uses the
   canonical transition table; an inadmissible jump retains metadata/sync time without
   advancing lifecycle. No replay/history-based catch-up authority was implemented.
4. **Confirmed concurrent persistence failure:** an exploratory two-shipments-per-
   tenant success case reproduced PostgreSQL **`40001: could not serialize access
   due to read/write dependencies among transactions`** at the unchanged
   `CarrierShipmentService.SyncTrackingAsync` commit. Diagnostic artifact:
   `C:\Users\fadya\AppData\Local\Temp\opencode\scheduled-tracking-concurrency-diagnostic.trx`.
   The service's Serializable transaction/catch behavior predates this phase and its
   source hash is unchanged. This task did not add transaction retries or repeat
   provider tracking calls to hide that failure. The final tests prove deterministic
   positive persistence per tenant and separately hold five unsuccessful responses
   concurrently per tenant to prove concurrent scope/account isolation. **They do
   not certify successful concurrent tracking commits or fix that independent defect.**
5. Tenant eligibility is revalidated at selection/worker initialization, not held
   under a tenant lifecycle lock across external HTTP. No new promise of atomic
   suspension-versus-in-flight-provider execution is made.
6. The directory materializes eligible IDs for a cycle; there is no new cross-host
   lease or overlap guard. Primary-key iteration and existing sync age prevent new
   within-cycle duplicates and immediate repeats of successful work, not pre-existing
   overlap/recovery races between distinct scheduler executions.

Carrier Remittance remains disabled. Bosta collection parsing, remittance enablement,
Manual COD Settlement/refund lineage and provider status mapping remediation were not
implemented. **STOP:** this bounded tenant-context implementation and verification
are complete; no next carrier/provider remediation task was started.
