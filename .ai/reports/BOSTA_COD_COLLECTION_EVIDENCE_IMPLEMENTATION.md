# Bosta Official COD Collection — Immutable Observed-Money Evidence

Date: 2026-10-09. Workspace: MAIN only, `C:\Users\fadya\source\repos\PointOfSale`.

## Verdict

**PASS — product implementation and isolated PostgreSQL verification completed.**
Official Bosta observations can recognize whole-order customer collection only
after configured webhook trust, exact target identity, immutable registration
account/environment provenance, explicit currency and exact actual-money admission.
The canonical financial transaction posts **Dr 1130 COD Clearing / Cr 4020
E-commerce Sales**. It does not recognize merchant remittance or move Main Cashbox.

**485 distinct selected backend cases passed, 0 failed, 0 skipped**, including
**70 new cases: 50 isolated PostgreSQL cases and 20 parser cases**. Actual discovery,
every final TRX counter and every individual result were checked.

This certifies current source and isolated fixtures. The additive migration is
**not applied to MAIN**, and the MAIN API was **not restarted** during this task.
No live carrier request, provider activation or merchant financial operation occurred.

## 1. Initial source inspection and task boundary

Initial `git status --short`, actual working diffs and current source were inspected.
The starting tracked diff fingerprint was `96312a51dafe15879b18f5b8d37e93e44079eeb6`.
The prior cancellation closeout did not implement Bosta collection:

- `BostaAdapter.ParseWebhook` still required uppercase `EventType` and
  `TrackingNumber`; its `COD_COLLECTED` branch was unreachable through the whitelist.
- Financial admission used mutable `Shipment.CodCollected`/time, expected
  `Shipment.CodAmount`, and `Order.Total`, rather than actual immutable observed money.
- No observed collection entity or explicit enforceable currency provenance existed.

Existing uncommitted cancellation, tracking concurrency/tenant context, Manual COD,
refund-lineage and Treasury changes were retained. No subagents, commits, resets,
reverts or secondary workspace operations were used. The cancellation report was
not changed or used as Bosta implementation proof.

## 2. Actual chain traced and implementation

```text
ShippingWebhooksController / ShippingWebhookRequestPipeline
  → ShippingWebhookTenantResolver: persisted carrier + exact tracking, tenant/ID constraints
  → BostaAdapter.ParseWebhook: explicit official schema + parsed observation facts
  → CarrierShipmentService.ProcessWebhookInternalAsync: mandatory configured trust
  → ProcessObservedMoneyWebhookAsync: fresh order/shipment/config locks + trust recheck
      → immutable CarrierCollectionEvidence (Qualified or Review)
      → SQL logistics transition when independently admissible
      → ShipmentCodCollectedEvent(CollectionEvidenceId): locator to qualified evidence
  → ShipmentCodCollectedConsumer
  → PaymentLifecycleService.RecordCarrierCodCollectionAsync(..., evidenceId)
      → ValidateCodCollectionAdmissionAsync + current SQL evidence revalidation
      → payment lifecycle + accounting outbox + Posted COD journal, atomically
  → CommerceAccountingService.PostCodDeliveredAsync: evidence-qualified actual money
  → AccountingOutboxProcessor: independent evidence revalidation for recovery
```

### Official contract parsing

Contract evidence: `.ai/reports/SHIPPING_CARRIERS_END_TO_END_FORENSIC_AUDIT.md`,
sections 4.1 and 8: Bosta's documented delivery status webhook uses `_id`,
`trackingNumber`, numeric `state`, millisecond `timeStamp`, numeric `cod`, `type`
and optional boolean `isConfirmedDelivery`; delivered state is **45**. The body
does not independently specify currency. `_id` identifies the delivery, not a transition.

`PointOfSale.Infrastructure/Shipping/Adapters/BostaAdapter.cs` now:

- Parses lowercase official keys explicitly, with exact numeric kinds; rejects
  duplicate/case-conflicting keys and mixtures with the legacy schema.
- Preserves string tracking identifiers, including leading zeroes. Numeric
  tracking uses its exact integer JSON token, never floating-point conversion,
  rounding, exponent expansion or identity trimming.
- Parses integer Unix milliseconds deterministically to UTC. Bounds are
  2000-01-01 through server-now + 5 minutes; collection also cannot predate its
  registration binding by more than 5 minutes.
- Separates `MoneyObservation` from the legacy `CodCollected` flag. Parsing alone
  never sets financial authority and never bypasses trust.
- Supports the narrowly documented delivery-context states 41 (out for delivery),
  45 (delivered), 46 (returned); other ambiguous states remain unsupported. No
  create/tracking-operation mapping was changed.
- Accepts only bounded unsigned plain JSON decimal money: at most 12 integer
  digits and 6 fractional digits. Negative, string, non-finite, overflowed,
  exponent-form or overprecision values are reviewable invalid observations.
  Invalid JSON is rejected before target mutation.
- Builds a deterministic SHA-256 **observation** fingerprint over contract,
  delivery/tracking identity, state/type/time, normalized exact amount or invalid
  amount hash and confirmation fact. Whitespace/property order and equivalent
  decimal formatting do not create a new economic observation. Later distinct
  states/times/amounts remain distinct. No provider event ID is invented from `_id`.

### Explicit account currency and registration provenance

Canonical runtime configuration is `CarrierConfiguration`, resolved by
`CarrierIntegrationAuthority`; administrative carrier-account entities are separate
runtime governance objects. The inspected models did not supply an enforced
provider-observation denomination. Tenant/order/shipment currency alone is expected
economics, not provider money provenance.

The narrow explicit binding uses the existing authenticated carrier configuration
JSON: **`codCollectionCurrency`**, an exact uppercase ISO code present in the
canonical `Currencies` table with supported precision. Example configuration shape:

```json
{ "codCollectionCurrency": "EGP" }
```

This is an explicit operator/account setting, **not an EGP default**. No MAIN
configuration was edited. Missing, duplicate, malformed, lowercase, unknown or
precision-unsupported bindings fail closed.

`CarrierShipmentService.CollectionEvidence.cs` captures an immutable
`CarrierCodShipmentBinding` only during an admitted fresh registration using the
pre-call canonical resolution and still-matching SQL configuration. It stores
tenant/order/shipment, configuration ID, account ID, Sandbox/Production, explicit
currency, exact tracking, actual provider delivery reference, external reference,
creation time and fingerprint. It never manufactures a delivery reference from
tracking or uses a later credential resolution to backfill historical provenance.

Both existing fresh registration callers pass their pre-call resolution into the
existing persistence helper. Provider creation contracts/routes/mapping remain
unchanged. Recovery lookup and historical shipments do not acquire fabricated
financial bindings.

**Existing shipments without an immutable binding remain logistics observations
only and produce `COD_ACCOUNT_BINDING_REQUIRED` review for delivered money.**
A separate reviewed historical-binding/reconciliation authority would be needed
to admit them. This phase adds no such mutation endpoint.

### Immutable observed money and reconciliation

New carrier-neutral entities:

- `PointOfSale.Core/Entities/CarrierCollectionEvidence.cs`
- `PointOfSale.Core/Entities/CarrierCodShipmentBinding.cs`

Evidence stores exact tenant/order/shipment/carrier, canonical configuration and
binding IDs, account/environment, delivery/tracking references, actual observed
amount (nullable for invalid/absent money), explicit configured currency (nullable
when unavailable), provider time, server receipt time, source contract/trust
method, payload SHA-256, deterministic observation fingerprint, immutable outcome/
reason and server audit identity. No credentials or arbitrary raw payload are retained.
The official webhook path stores only the bounded normalized evidence and hash
summary; it does not populate `CarrierTrackingResponse` with the sensitive body.

`CarrierCollectionMoneyAuthority.cs` qualifies only supported type-10 delivered
customer collection with positive exact actual amount, allowed currency precision,
matching whole-order total/expected COD and explicit binding. Tenant/base-ledger
currency must agree; this is a comparison, not a currency source. False confirmed
delivery, prepaid/non-COD, wrong account/environment/reference, active/deletion/
lifecycle mismatches, missing/zero/short/excess/invalid amounts and unsupported
currency become permanent review outcomes with no collection event or journal.

Financial revalidation appends `RevalidationReview` linked to the original immutable
qualified evidence. It never edits historical evidence. A prior review permanently
blocks automatic recognition: restoring mutable account facts is not a reconciliation
authority. Contradictory observations also block pending recognition. The consumer
acknowledges durable review without falsely logging it as recognized collection.

Logistics delivery and money qualification are independent. A trusted exact delivery
reference can advance valid shipment lifecycle even if COD currency/amount cannot
qualify. Delivery alone never emits the financial collection event.

### Transaction, replay and financial authority

The official observation path locks **order → shipment → canonical configuration**
at PostgreSQL ReadCommitted and revalidates trust/identity before evidence or state
mutation. Its webhook log, evidence, admissible logistics state and transactional
collection event commit together. This bounded path does not use the older
pre-transaction Processing acknowledgement. Publication failure rolls everything
back and permits a real retry. The generic webhook recovery design is unchanged.

Permanent identities:

- Binding: unique **(TenantId, ShipmentId)**, unfiltered by deletion.
- Observation: unique **(TenantId, CarrierCode, ObservationFingerprint)**, unfiltered.
- Whole-order qualification: unique **(TenantId, OrderId)** where outcome is
  Qualified, independent of deletion. Other immutable review observations coexist.
- Existing `COD_DELIVERED:{tenant}:{order}` journal/accounting outbox identity remains.

`ShipmentCodCollectedEvent` adds `CollectionEvidenceId`; its money is diagnostic.
The consumer passes tenant/order/shipment/evidence locators only. The legacy
shipment-only lifecycle overload fails closed, including previously stored mutable
flags. The former positive flags-only regression was updated to assert rejection;
the real positive financial proof is now in the new PostgreSQL suite.

Inside `ValidateCodCollectionAdmissionAsync`, current SQL tenant, active order/
shipment, COD/carrier ownership, exact binding, currency, whole-order amount and
lifecycle are revalidated before terminal replay or financial/inventory repair.
`CommerceAccountingService.PostCodDeliveredAsync` independently requires a live
transaction and current immutable evidence for Carrier Fulfillment and posts the
actual qualified amount. Manual Fulfillment retains its existing authority.

`PaymentLifecycleService.CarrierCollection.cs` makes carrier recognition atomic:
payment lifecycle, Posted journal, ledger balances, processed accounting outbox
receipt and accounting-confirmed events are committed in one transaction. An
injected posting failure rolls back payment/outbox/journal/balances while the earlier
observation evidence remains available for retry. The existing post-commit helper
sees the already processed carrier receipt and no-ops. Manual collection/settlement
behavior was not repurposed.

`AccountingOutboxProcessor.ProcessCodDeliveredAsync` revalidates carrier evidence
on recovery and persists review for changed bindings. Real isolated recovery tests
prove qualified recovery posts 100 despite hostile payload money and changed-account
recovery posts nothing. No generic Clearing transfer or settlement route was enabled.

## 3. Additive migration and model

Migration: **`20261009162710_CarrierObservedCodCollectionEvidence`**.

Reviewed Up creates only `CarrierCodShipmentBindings` and `CarrierCollectionEvidence`,
restrictive order/shipment/configuration/evidence FKs, permanent unique indexes,
qualified-money checks and PostgreSQL **BEFORE UPDATE OR DELETE** immutability
triggers. EF source guards prohibit edits/deletes and require an internal server
money-authority scope for additions. Both new global filters are tenant-only so
soft-delete cannot hide an identity. Existing shipment and financial tenant filters
remain in place. Designer and model snapshot are included; pre-existing Manual COD
and cancellation model additions were retained.

Fresh `dotnet ef migrations has-pending-model-changes` reports **no changes**.
The migration was generated, reviewed and applied **only by isolated test fixtures**.
No `database update`/Migrate was run against MAIN.

Read-only MAIN inspection with the pre-existing schema utility, without `--apply`,
confirmed **47 applied migrations**, latest cancellation migration, and exactly
**1 pending migration**, the new Bosta evidence migration. Pending model changes
False. All eleven inspected business-row counts/`(Id,xmin)` fingerprints still
match the pre-task preservation evidence, including Orders 15, Shipments 8,
OrderRefunds 6, PaymentIntents 7, Journals 190, LedgerLines 399, StockMovements 197,
zero settlement batches/memberships and zero accounting outbox rows.

## 4. Fresh build, discovery and final tests

Final affected API/Core/Infrastructure/Tests build:

```powershell
dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" -c BostaCodEvidenceVerification --no-restore --no-incremental -v:quiet /clp:ErrorsOnly
```

**Succeeded: 452 warnings, 0 errors, 47.91s.** Separate verification configuration;
no running MAIN Debug binary was replaced or restarted. Following final product/
test edits, all three selected runs were rerun from this fresh source build.

Each selection was actually discovered with `--list-tests --no-build --no-restore`.
`verify-bosta-cod-evidence.py` in the approved temporary directory asserts exact
discovery, all final XML counters, every individual Passed result, class expansion
and disjoint identities. `git diff --check` and EF snapshot consistency also pass.

TRX root: **`C:\Users\fadya\AppData\Local\Temp\opencode`**.

| Final TRX | Total/executed/passed | Failed/skipped | Runner duration | Finish UTC |
|---|---:|---:|---|---|
| `bosta-cod-evidence-complete-new.trx` | **70 / 70 / 70** | **0 / 0** | 5m16s | 17:44:42.0568747Z |
| `bosta-cod-evidence-final-financial-regressions.trx` | **107 / 107 / 107** | **0 / 0** | 7m56s | 17:47:21.6414150Z |
| `bosta-cod-evidence-final-authority-regressions.trx` | **308 / 308 / 308** | **0 / 0** | 8m14s | 17:47:39.7490716Z |

Every other emitted counter (errors, timeouts, aborted, inconclusive, notRunnable,
notExecuted, disconnected, warning and pending) is **0**. **485 distinct cases**.
An overlapping final consumer diagnostic recheck also passed 2/2 and is not added.

New suite: **20** official parser cases; **50** PostgreSQL evidence/admission cases.
Coverage includes exact resolver identity and foreign ambiguity; actual immutable
provenance; complete Posted journal/lines/balances/outbox receipts; delivery-only;
missing/unknown/mismatched currency; actual short/excess/invalid/overprecision money;
prepaid; missing/bad trust; identical and semantic replay; distinct later return;
wrong tenant/evidence/target; current changed references/account/environment/
carrier/deletion/lifecycle; restored-account review barrier; direct SQL immutability
triggers and actual uniqueness constraints; pre-call registration binding; publication
rollback; posting rollback/retry; concurrent webhook/financial processing; and
real accounting outbox recovery with hostile payload facts.

Financial regression expansion: Manual COD Settlement **35**, refund prerequisite
lineage **13**, e-commerce refunds **7**, COD lifecycle **32**, Bosta endpoint **10**,
webhook trust **7**, tenant resolution **1**, consumer **2** = **107**.

Authority regression expansion includes accounting outbox **28**, commerce
accounting **50**, cancellation **59**, tracking persistence concurrency **27**,
scheduled tracking tenant context **20**, tenant-filter freeze **4**, and the
existing **120** cancellation/authority regression slice = **308**.

Intermediate fixture failures are not counted as successful verification: attempted
immutable order edits, missing aggregate version/pending timestamp, and a computed
journal total queried without its lines were corrected in isolated fixtures.
No product guard was relaxed. Final runs above supersede those failed fixture runs.

## 5. Exact changed product paths and preservation

Actual scoped `git diff --name-only` tracked output for this task:

```text
PointOfSale.Core/Events/ShipmentEvents.cs
PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs
PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs
PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs
PointOfSale.Infrastructure/Consumers/ShipmentCodCollectedConsumer.cs
PointOfSale.Infrastructure/Data/ApplicationDbContext.cs
PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs
PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs
PointOfSale.Infrastructure/Shipping/Adapters/BostaAdapter.cs
PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs
PointOfSale.Infrastructure/Shipping/IShippingCarrierAdapter.cs
PointOfSale.Tests/Infrastructure/ShipmentCodCollectedConsumerTests.cs
PointOfSale.Tests/Integration/CodPaymentLifecycleTests.cs
```

New files are still untracked (no staging/commit) and therefore do not appear in
plain `git diff --name-only`:

```text
PointOfSale.Core/Entities/CarrierCodShipmentBinding.cs
PointOfSale.Core/Entities/CarrierCollectionEvidence.cs
PointOfSale.Infrastructure/Data/Configurations/CarrierCollectionEvidenceConfiguration.cs
PointOfSale.Infrastructure/Shipping/CarrierCollectionMoneyAuthority.cs
PointOfSale.Infrastructure/Shipping/CarrierMoneyPersistenceAuthority.cs
PointOfSale.Infrastructure/Shipping/CarrierShipmentService.CollectionEvidence.cs
PointOfSale.Infrastructure/Payments/PaymentLifecycleService.CarrierCollection.cs
PointOfSale.Infrastructure/Migrations/20261009162710_CarrierObservedCodCollectionEvidence.cs
PointOfSale.Infrastructure/Migrations/20261009162710_CarrierObservedCodCollectionEvidence.Designer.cs
PointOfSale.Tests/Shipping/BostaOfficialCollectionParserTests.cs
PointOfSale.Tests/Shipping/BostaCodCollectionEvidencePostgresTests.cs
.ai/reports/BOSTA_COD_COLLECTION_EVIDENCE_IMPLEMENTATION.md
```

The full diff/status contains substantial pre-existing work; it is not attributed
to this task. Existing hunks in the shared files were inspected and retained, including
Manual authority, settlement indexes, cancellation guards and the completed tracking
ReadCommitted lock correction. All original working-tree entries remain present.
No frontend, cancellation executor, tracking job, retired settlement controller,
generic Clearing restriction or refund-lineage file was edited by this task.

Protected initial/final Git source hashes match exactly:

| Protected source | Hash |
|---|---|
| Infrastructure/Payments/PaymentLifecycleService.ManualCodSettlement.cs | `7b72ac580f30224df1e0a0276554c4a4f08cb64e` |
| Infrastructure/Accounting/ManualCodSettlementAuthority.cs | `3cebf62c2cabba0849793a0b138e2a31767bf601` |
| Infrastructure/Accounting/SettlementRefundLineage.cs | `f2482c510b76022e4ed6ede3da26bf42194a2c52` |
| Core/Interfaces/Payments/ManualCodSettlement.cs | `70f9c189cd579053236615e80b4dac44e7259c12` |
| Tests/Integration/ManualCodSettlementTests.cs | `44649d4a3c765ad95b8f4d51555bdfe782085d29` |
| Infrastructure/Migrations/20261008223819_ManualCodPermanentSettlementIdentity.cs | `b7b3469ef455a3ded367567f3a4dd5d4be5f2352` |
| Web/src/pages/treasury/TreasuryManualCodSettlement.jsx | `45de756de165a067a16e4c9554983d2bb72049e3` |
| Web/src/services/manualCodSettlementApi.js | `c530cead64685e56b1202ef79c9ffcece704a9a8` |

MAIN port 5010 remains owned by **PID 19436**, executable under MAIN,
started **15:54:32.6110040Z**, the same process present before this implementation.
It was not stopped/restarted. No new migration or carrier config was activated.

## 6. Remaining independent findings and stop

- Bosta creation/open-package/state-object and tracking-response mapping findings
  remain independent; this task does not claim they are fixed. Financial provenance
  is captured only after whatever registration is genuinely admitted by current code.
- Historical shipments lack immutable account provenance and fail closed until a
  separate reviewed reconciliation/binding operation exists. No backfill is supplied.
- Currency configuration must be explicitly set before qualified fresh registration;
  no country/order/tenant heuristic fills it in.
- Reviews are durable SQL outcomes; a review UI/resolution command is future work.
- Generic legacy webhook Processing recovery remains an independent finding; only
  the bounded official observation path has atomic log/evidence/event persistence.
- No live Bosta positive certification, provider account legal-identity verification,
  external receipt/payout retrieval, partial/split collection, fees or FX is claimed.
- No Aramex/DHL/FedEx/Mylerz collection activation occurred.

**STOP:** customer collection and COD Clearing evidence boundary only. Carrier
Remittance remains disabled; no Main Cashbox transfer, settlement endpoint or
operator-attested receipt authority was implemented. The next financial phase must
separately attest actual merchant receipt against already-Posted qualified clearing.
