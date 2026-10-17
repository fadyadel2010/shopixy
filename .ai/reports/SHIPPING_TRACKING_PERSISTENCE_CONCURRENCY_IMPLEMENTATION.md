# Shipment Tracking — PostgreSQL 40001 Persistence Correction

## 1. Verdict and bounded scope

**PASS — the reproduced concurrent successful tracking commit failure is corrected
in the actual `CarrierShipmentService.SyncTrackingAsync` persistence path. Fresh
verification: 27 new PostgreSQL cases, 20 established tenant-context cases, 81
tracking/lifecycle/scheduler/tenant regressions and 84 cancellation/authority/
containment cases: 212 distinct cases passed, zero failed/skipped/not executed.**

- Date: **2026-10-09**.
- Workspace: exclusively MAIN/PRIMARY Windows,
  `C:\Users\fadya\source\repos\PointOfSale`.
- This closes finding **8.4** in
  `SHIPPING_SCHEDULED_TRACKING_TENANT_CONTEXT_IMPLEMENTATION.md`.
- No subagents, commits, resets, reverts, production migration, API restart,
  live carrier requests, merchant scheduler execution or real financial operations.
- Existing tenant-context, cancellation, Manual COD, refund-lineage and other
  working-tree changes were preserved. This does not claim the running Debug API
  has activated the new build; the user explicitly prohibited a restart.

## 2. Deterministic reproduction and actual cause

### Original execution and transaction boundaries

The original method loaded the shipment, resolved canonical tracking credentials
and obtained a successful adapter observation **before** beginning a transaction.
It then called `BeginCarrierMutationTransactionAsync`, which uses Serializable,
and `Entry(shipment).ReloadAsync`. It re-evaluated lifecycle progression, updated
tracking metadata/sync timestamps, optionally synchronized fulfillment and staged
events, called SaveChanges and committed. The outer exception handler logged an
exception and returned `Success=false / INTERNAL_ERROR`; there was no tracking
persistence retry or provider retry.

The status-change branch also calls `PrepareShipmentAuthorityContextAsync`, which
stamps the linked order and advances its projection version, then the canonical
fulfillment service and status/COD event publishers. The real fulfillment service
can perform intermediate SaveChanges calls, but joins an existing caller transaction
and does not independently commit it. `MassTransitEventPublisher` stages through
the scoped EF bus outbox; these writes must remain inside the final tracking commit.

### Isolated PostgreSQL evidence

Before changing product code, the first new relational test ran the real service
with **two distinct InTransit shipments in one tenant, each with its own order**.
Both mocked observations reported the same existing status. Consequently, this
reproduction did **not** require a lifecycle event, shared order update or outbox
write to cause the failure: it updated only each independent shipment's metadata
and timestamps.

A command interceptor held each transaction **after its shipment reload executed
on PostgreSQL and before application writes**. Both had to arrive at that boundary
before either proceeded. The fixture locally disabled index/bitmap scans for these
transactions to deterministically exercise the legal sequential-scan plan that
small tables can naturally choose. No application planner setting or index changed.
The PostgreSQL monitoring connection inspected the actual participating backend PIDs:

| Reproduction backend | Isolation | Actual `pg_locks` evidence |
|---|---|---|
| **1398363** | Serializable | `locktype=relation`, `mode=SIReadLock`, relation `Shipments` |
| **1398364** | Serializable | `locktype=relation`, `mode=SIReadLock`, relation `Shipments` |

The executed EF reload query was:

```sql
SELECT s."Id", ...
FROM "Shipments" AS s
WHERE s."Id" = @__p_0
LIMIT 1
```

The logged SQL also established that this EF Entry.Reload was a primary-key reload,
rather than an ordinary composed tenant-filtered query. The initial pre-provider
query remained tenant-filtered. The correction below uses filtered fresh queries
for the persistence admission as well.

Both transactions had read the shipment relation before writing their distinct
rows. PostgreSQL SSI's relation-level predicate locks created reciprocal read/write
dependencies between these otherwise independent tracking updates. One commit was
canceled as a pivot. Thus the demonstrated cause is **Serializable SSI predicate
coupling**, not a duplicated tracking target or a shared financial/order mutation.

Captured exception:

```text
Npgsql.PostgresException (0x80004005)
SqlState: 40001
MessageText: could not serialize access due to read/write dependencies among transactions
Detail: Reason code: Canceled on identification as a pivot, during commit attempt.
Hint: The transaction might succeed if retried.
Routine: PreCommit_CheckForSerializationFailure
File: predicate.c; Line: 4723
NpgsqlTransaction.Commit
  → RelationalTransaction.CommitAsync
  → CarrierShipmentService.SyncTrackingAsync, original line 1236
```

This commit exception was a direct PostgresException, not a DbUpdateException
wrapper. The service logger's complete exception/async stack and both transaction
queries/locks are retained in the fresh reproduction artifact:

`C:\Users\fadya\AppData\Local\Temp\opencode\tracking-persistence-40001-reproduction.trx`

That intentional red run executed **1 case / 1 failed**, with one of the two service
results failing at commit. It reproduced the same SQLSTATE/message/pivot-at-commit
finding as the older exploratory diagnostic, rather than reusing that old binary.

## 3. Implemented persistence correction

Only the tracking method and its new private admission helpers changed in product
code. Registration, webhook, cancellation and the shared Serializable transaction
helper remain byte-for-byte unchanged from the pre-task working file.

1. Validate the canonical tenant identity. Reject an existing caller transaction or
   pending caller mutations before HTTP, so this method owns its commit boundary and
   cannot clear/commit unrelated pending work.
2. Obtain the tenant-filtered **AsNoTracking** shipment snapshot, canonical credential
   resolution and **one** adapter observation outside SQL.
3. After a successful observation, recheck tenant affinity and pending work, clear
   only the clean tracker, and start a tracking-owned transaction. **PostgreSQL alone
   uses ReadCommitted**; other relational providers retain Serializable.
4. Lock and load the linked **order first, then shipment**, using PostgreSQL
   `SELECT ... FOR UPDATE`. This matches cancellation/registration lock order.
   The new composed queries retain mandatory EF tenant/nondeletion filters and
   explicit tenant/identity predicates. The SQL Server fallback uses row/update locks
   with its retained Serializable isolation; PostgreSQL is the relationally verified
   target. No IgnoreQueryFilters or cross-tenant lookup was introduced.
5. Reject an observation if its current SQL target/lifecycle/freshness has changed:
   shipment/tenant/order identity, carrier, tracking/AWB/external/reference request
   identifiers, status, last sync time, UpdatedAt or deletion. A row-lock waiter reads
   the newly committed row before admission; an old response is not applied to it.
6. Validate current locked order ownership and cancellation/refund state; re-resolve
   canonical credentials and compare configuration identity, account, environment
   and API key against those actually used. This re-resolution performs no HTTP.
7. Re-evaluate the existing `ShipmentLifecycleTransitionPolicy` through the unchanged
   progression method. The established metadata-only behavior for an inadmissible
   transition is retained; this task does not implement catch-up progression.
8. Keep existing shipment metadata, status/timestamps, COD handling, fulfillment
   authority and transactional event/outbox staging inside the same transaction.
   Return success only after the final SaveChanges/commit completes.

### Concurrency, failure and provider-call guarantees

- Independent shipment/order rows can persist concurrently, without Serializable
  relation-wide SIREAD coupling. The four overlapping same-/different-tenant tests
  observe ReadCommitted and **zero participating SIReadLock entries** after the fix.
- Competing executions for the same order/shipment serialize through row locks.
  Only the still-current observation can commit; the loser returns
  `TRACKING_OBSERVATION_STALE` and emits no second lifecycle event/outbox row.
- Changed current order/account authority returns stable rejection codes. Stale
  diagnostics log shipment ID, original/observed/current status and SHA-256 evidence
  hash, without writing the stale provider body over newer shipment evidence.
- **No automatic retry was added.** The demonstrated failure is prevented by the
  narrower isolation/locking strategy. Each tested execution makes at most one
  provider call and one persistence transaction; no commit rejection can jump back
  into HTTP or regenerate a lifecycle/outbox attempt. There is no unbounded retry.
- A forced real PostgreSQL `40001` at the final commit boundary returns failure and
  rolls back all transaction writes, including fulfillment's earlier SaveChanges and
  actual EF bus-outbox messages/state. The exception remains logged; it is not
  suppressed or represented as successful persistence.
- No global scheduler serialization, new concurrency limit, job lease, schema/index
  change or new financial authority was introduced. Separate scheduled executions
  can still make their own provider observations; this is not a cross-host lease or
  lifetime exactly-once provider claim.

## 4. Deterministic relational tests and actual rows/outbox

New suite:
`PointOfSale.Tests/Shipping/TrackingPersistenceConcurrencyPostgresTests.cs`.

It uses existing `TestDatabaseScope` isolated PostgreSQL databases, real
TenantContext/global filters, CarrierShipmentService, CarrierIntegrationAuthority,
OrderFulfillmentService, MassTransitEventPublisher and EF bus outbox. Only carrier
observations, synthetic credential decryption and unrelated customs/geographic
dependencies are mocked. The MassTransit harness is never started, so no messages
are externally dispatched and no live carrier/network integration is invoked.

| Method | Cases | Relational proof |
|---|---:|---|
| `DistinctShipments_OverlappingSqlSnapshots_PersistWithoutRepeatingProvider` | 4 | Both SQL reloads executed before either write proceeds; same/different tenants, unchanged/eligible changed status, exact raw response, ETA/sync/status timestamp, one provider call per target, actual shipping outbox rows and order version |
| `SameShipment_RowLocksSerializeAuthority_AndOnlyOneObservationCommitsEvents` | 1 | Both transactions reach order-lock execution; one commits and the stale competitor rejects; two provider calls for two executions, only two shipping messages and one order-version advance |
| `ChangedDuringProviderCall_RejectsStaleObservation_AndPreservesSqlEvidence` | 10 | Committed status, tracking, AWB, external reference, request ID, carrier, order, newer observation, UpdatedAt and deletion changes remain intact; no stale metadata/event commit |
| `OlderInFlightObservation_CannotOverwriteNewerSuccessfulExecution` | 1 | Old HTTP response explicitly held until a newer execution commits; newer response/status/time retained and no duplicate lifecycle messages |
| `UnsuccessfulOrThrowingProvider_NeverPersistsTrackingOrOutbox` | 2 | Rejected and throwing carrier observations leave shipment/order rows and outbox unchanged |
| `PostgreSqlCommitRejection_RollsBackShipmentOrderAndRealBusOutbox_WithoutHttpRetry` | 1 | PostgreSQL raises 40001 at commit boundary after writes; one HTTP call, no committed status/metadata/timestamps/order-version/outbox/state |
| `ChangedCarrierAccount_RejectsObservationWithoutFinancialOrTrackingMutation` | 1 | Canonical account changed during HTTP; old-account observation rejected |
| `WaitingShipmentLock_ReloadsCommittedIdentity_AndRejectsPreviouslyObtainedObservation` | 1 | Actual `pg_stat_activity.wait_event_type=Lock` verified while identity writer holds uncommitted row; after commit, waiter sees/rejects new target |
| `ForeignShipmentOrMissingTenantContext_CannotReachProviderOrWrite` | 2 | Foreign shipment ID and missing trusted context fail before HTTP or mutation |
| `ExistingCallerTransaction_FailsBeforeHttp_WithoutRollingBackCallerWork` | 1 | Caller transaction remains owned by caller; no provider invocation |
| `SkippedLifecycleTransition_RetainsExistingMetadataOnlyPolicy_WithoutEvents` | 1 | Original regression protection/metadata-only policy retained; no lifecycle/outbox promotion |
| `RealFulfillmentBridge_IntermediateSaveAndEvents_AreAtomicWithTrackingCommit` | 2 | Actual Packed → OutForDelivery fulfillment/order bridge with four concrete bus-outbox events; either all commit once or all intermediate saves/events roll back |
| **Total** | **27** | Real persisted PostgreSQL rows and bus-outbox counts, not publisher mock-call certification |

Successful status changes assert one ShipmentStatusChangedEvent and one
ShipmentOutForDeliveryEvent per shipment. The real fulfillment bridge additionally
asserts one OrderStatusChangedEvent and one OrderOutForDeliveryEvent. Distinct message
IDs, order projection increments and failure/stale zero-message cases are inspected
from independent SQL contexts. Payment/refund/financial outbox/journal/stock and
cancellation-operation tables remain empty in these tracking fixtures.

Fixture development initially exposed incorrect assumptions that seeded order
versions start at zero and that two shipments may share one order. Actual audit
version baselines were reloaded, and the existing unique tenant/order shipment
constraint was respected by testing two executions against the same shipment.
Missing-context testing was also corrected to reach the service guard without first
invoking the fixture registry's own required-tenant construction guard. Those
intermediate fixture failures are retained in development TRX files; they are not
passing certification evidence and no production constraint/authority was weakened.

## 5. Fresh build, discovery and final TRX evidence

Final current-source build after all product/test edits:

```powershell
dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" -c TrackingConcurrencyVerification --no-restore --no-incremental -v:quiet /clp:ErrorsOnly
```

**Succeeded: 451 warnings, 0 errors, 36.70s.** Tests and referenced API/Core/
Infrastructure rebuilt in a separate configuration, without updating running Debug
binaries. No project exclusions/packages/configuration changes were made.

Actual `dotnet test --list-tests` discovery against this configuration was inspected
and asserted: **47** (27 new + 20 tenant-context), **81** and **84** expanded cases.
The temporary `verify-tracking-concurrency.py` script validated final XML counters,
every individual Passed outcome, class expansion and disjoint test identities.
All executions used the fresh DLL with `--no-build --no-restore`.

| Final TRX | Total | Executed | Passed | Failed | Skipped | Not executed | Runner duration | Finish UTC |
|---|---:|---:|---:|---:|---:|---:|---|---|
| `tracking-persistence-concurrency-certified.trx` | 47 | 47 | **47** | **0** | **0** | **0** | 4m03s | `15:35:43.2974736Z` |
| `tracking-persistence-concurrency-certified-regressions.trx` | 81 | 81 | **81** | **0** | **0** | **0** | 12s | `15:31:51.4928167Z` |
| `tracking-persistence-concurrency-preservation.trx` | 84 | 84 | **84** | **0** | **0** | **0** | 3m29s | `15:40:46.1369858Z` |
| **Distinct selected scope** | **212** | **212** | **212** | **0** | **0** | **0** | | |

Inspected error, timeout, aborted, inconclusive, notRunnable and disconnected
counters are also **zero**. This is selected-scope certification, not a whole-suite
or live-provider claim. Artifacts/script reside under the preapproved
`C:\Users\fadya\AppData\Local\Temp\opencode` directory.

Exact final execution commands:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c TrackingConcurrencyVerification --no-build --no-restore --filter "FullyQualifiedName~TrackingPersistenceConcurrencyPostgresTests|FullyQualifiedName~TrackingSyncTenantContextPostgresTests" --logger "trx;LogFileName=tracking-persistence-concurrency-certified.trx" --results-directory "C:\Users\fadya\AppData\Local\Temp\opencode" -v:minimal
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c TrackingConcurrencyVerification --no-build --no-restore --filter "FullyQualifiedName~ShipmentTrackingAuthorityCertificationTests|FullyQualifiedName~ShipmentLifecycleAuthorityCertificationTests|FullyQualifiedName~CarrierIntegrationAuthorityRemediationTests|FullyQualifiedName~ShippingWebhookTenantResolutionTests|FullyQualifiedName~StorefrontTenantIsolationTests|FullyQualifiedName~TenantSaveChangesInterceptorTests|FullyQualifiedName~AdminTenantAuthorityTests|FullyQualifiedName~TenantIsolationCertificationFreezeTests|FullyQualifiedName~HangfireRecurringJobCronTests|FullyQualifiedName~O43FulfillmentShippingRemediationTests|FullyQualifiedName~ShipmentExternalRegistrationSafetyTests" --logger "trx;LogFileName=tracking-persistence-concurrency-certified-regressions.trx" --results-directory "C:\Users\fadya\AppData\Local\Temp\opencode" -v:minimal
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c TrackingConcurrencyVerification --no-build --no-restore --filter "FullyQualifiedName~ShipmentCancellationPostgresTests|FullyQualifiedName~CarrierCancellationEvidenceTests|FullyQualifiedName~AdminShipmentCancellationContractTests|FullyQualifiedName~CarrierIntegrationAuthorityCertificationTests|FullyQualifiedName~T10SettlementContainmentTests|FullyQualifiedName~TreasurySettlementAuthorityCertificationTests" --logger "trx;LogFileName=tracking-persistence-concurrency-preservation.trx" --results-directory "C:\Users\fadya\AppData\Local\Temp\opencode" -v:minimal
```

The 84-case preservation slice comprises **59 cancellation safety cases**, **10
carrier integration authority**, **8 T10 settlement containment** and **7 Treasury
authority cases**, including generic Clearing-transfer containment.

## 6. Exact edits and preservation proof

Workspace edits for this task are limited to:

1. `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs` — tracking method
   and private tracking admission/locking helpers only.
2. `PointOfSale.Tests/Shipping/TrackingPersistenceConcurrencyPostgresTests.cs` — new
   isolated relational suite.
3. `.ai/reports/SHIPPING_TRACKING_PERSISTENCE_CONCURRENCY_IMPLEMENTATION.md` — this report.

The complete affected service diff (including pre-existing cancellation edits),
new test source and working-tree diff summary were reviewed. Initial/final status
were inspected. `git diff --check` passed for the tracked edit; only existing
LF-to-CRLF normalization notices appeared.

The temporary verification script inverses **only this task's tracking blocks and
helpers in memory**, then hashes that reconstructed text. Its Git content hash
exactly matches the captured pre-task working file:
`a59a8ca0a72919229c68630f1221965559cb5956`. Thus all pre-existing registration,
cancellation, webhook and other service edits are preserved without writing,
resetting or reverting any workspace file. Final service hash:
`f086c0f5e0c33651a04530f1026f0e7afeb83d8c`.

All **17** protected source hashes matched before/final verification:

| Protected file (project prefixes abbreviated) | Unchanged Git content hash |
|---|---|
| Infrastructure/Shipping/BackgroundJobs/TrackingSyncJob.cs | `dab90fd79f1b503c2f787eca5ba1dc84f16a4d44` |
| Infrastructure/Shipping/BackgroundJobs/TrackingTenantDirectory.cs | `7c0e62d59a9cb0a870e1629a266fc9d4dc69d58c` |
| Infrastructure/MultiTenancy/TenantContext.cs | `00315fbc57e225d280f08d997ed8c62d0decf5e8` |
| Tests/Shipping/TrackingSyncTenantContextPostgresTests.cs | `15bb8b5f1f0bce374ada9f187ff0705669d66333` |
| API/Program.cs | `f927f918c7458cb0bda9d2805e6b7c8259c93ed5` |
| Infrastructure/Data/ApplicationDbContext.cs | `aa890534bcf1bf40f7e99b37fe82c248bb800774` |
| Infrastructure/Shipping/CarrierIntegrationAuthority.cs | `09e09cd88bf74d5d0d6f1763b7e12afad60ca0fb` |
| Core/Policies/ShipmentLifecycleTransitionPolicy.cs | `a86070b54c59d3644160ec8a31ddb07940a3f96c` |
| Infrastructure/Payments/PaymentLifecycleService.ManualCodSettlement.cs | `7b72ac580f30224df1e0a0276554c4a4f08cb64e` |
| Infrastructure/Accounting/ManualCodSettlementAuthority.cs | `3cebf62c2cabba0849793a0b138e2a31767bf601` |
| Infrastructure/Accounting/SettlementRefundLineage.cs | `f2482c510b76022e4ed6ede3da26bf42194a2c52` |
| Core/Interfaces/Payments/ManualCodSettlement.cs | `70f9c189cd579053236615e80b4dac44e7259c12` |
| Tests/Integration/ManualCodSettlementTests.cs | `44649d4a3c765ad95b8f4d51555bdfe782085d29` |
| Infrastructure/Migrations/20261008223819_ManualCodPermanentSettlementIdentity.cs | `b7b3469ef455a3ded367567f3a4dd5d4be5f2352` |
| Web/src/pages/treasury/TreasuryManualCodSettlement.jsx | `45de756de165a067a16e4c9554983d2bb72049e3` |
| Web/src/services/manualCodSettlementApi.js | `c530cead64685e56b1202ef79c9ffcece704a9a8` |
| Infrastructure/Shipping/ShipmentCancellationService.cs | `50138babf1953c61f7dd1a8b8d268fed4f84db26` |

The established trusted per-tenant selection/worker scopes, global filters and
scheduler behavior are unchanged. Manual COD Settlement/refund lineage remain
unchanged. Carrier Remittance remains disabled; no Clearing/Main Cashbox journal
implementation or merchant financial evidence was created.

## 7. Independent remaining findings and stop

These separate findings remain unresolved and are **not** included in this verdict:

- Scheduled Delivered/Returned post-delivery observation.
- Failed shipments consuming tracking batch capacity.
- Missing intermediate-status catch-up/progression.
- Cross-host scheduler leases/overlap control.
- Bosta webhook/COD collection parsing.
- Carrier Remittance and Manual Carrier Remittance.
- COD Clearing/Main Cashbox journal changes.

The tenant-context report's separate eligibility-versus-in-flight-HTTP lifecycle
limitation also remains: this task does not add a tenant lifecycle lock across HTTP.
Real-provider positive cancellation certification/reconciliation remains governed
by the existing cancellation safety report.

**STOP:** the reproduced successful concurrent PostgreSQL tracking persistence
failure is fixed and relationally verified. No next carrier/financial remediation
task has been started. The MAIN API was not restarted or used to track merchant
shipments; runtime activation of this new source is outside this task's authorized
execution boundary.
