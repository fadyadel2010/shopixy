# Manual Carrier Remittance — Operator-Attested Physical Cash Receipt

Date: 2026-10-09. MAIN only: `C:\Users\fadya\source\repos\PointOfSale`.
Branch `main`; HEAD `256339621c0ea49f3602097cdacdf89ffe71754f`.

## Verdict

**PASS — dedicated product workflow implemented and verified on isolated PostgreSQL.**
An authorized active merchant financial operator can attest physical cash already
received from a carrier, select qualified whole carrier-COD orders whose customer
collection accounting is already Posted, and atomically post:

```text
Dr 1010 Main Cashbox
Cr 1130 COD Clearing
```

**543 distinct selected backend cases passed; 96 frontend cases passed.** These
include **58 new backend cases: 53 real PostgreSQL cases and 5 API admission cases**,
plus **16 new EN/AR UI cases**. A final overlapping 53-case PostgreSQL run certified
the reviewed SQL guards; it is not added to the distinct total.

This is operator attestation, **not provider-confirmed merchant remittance**.
No electronic carrier statement is required. No fees, FX, partial allocations,
bank destination or automated/provider remittance authority was introduced.

Migration **`20261009182344_ManualCarrierCashRemittance` remains unapplied to MAIN**,
as does `20261009162710_CarrierObservedCodCollectionEvidence`. The running MAIN API
was not stopped, restarted or replaced. MAIN activation is outside this execution.

## 1. Inspected integration boundary

The actual current entities, observed-money authority, canonical collection posting,
settlement membership, refund reader, financial engine, outbox, model snapshot and
working-tree changes were inspected together with
`.ai/reports/BOSTA_COD_COLLECTION_EVIDENCE_IMPLEMENTATION.md`.

The existing Bosta boundary supplies immutable `CarrierCodShipmentBinding` and
`CarrierCollectionEvidence`, permanent Qualified/Review/RevalidationReview outcomes,
exact account/environment/currency provenance, and canonical customer collection
`COD_DELIVERED:{tenant}:{order}` / `Order_COD` **Dr 1130 / Cr 4020**. Its customer
collection transaction and processed accounting receipt are reused as prerequisites,
not reimplemented or treated as merchant receipt.

The existing `PaymentSettlementBatch` is a generic settlement snapshot, not a
Manual-only document. Its `PaymentSettlementOrders` table already has the permanent,
unfiltered unique `(TenantId, OrderId)` constraint. The new dedicated type
**`CARRIER_CASH_RECEIPT`** uses that shared allocation to prevent overlap with Manual,
historical and future settlement types. Separate carrier receipt/membership entities
hold physical receipt attestation and immutable observed-money lineage. Certified
`MANUAL_COD_COLLECTION` contracts and source authority were not loosened.

No cancellation/tracking closeout, tracking implementation or Bosta parsing change
was performed. No subagents, commits, resets, reverts, provider configuration edits,
secondary workspace access or merchant mutation commands were used.

## 2. Dedicated authority and complete transaction

Production path:

```text
Treasury Clearing Aging → Manual Carrier Remittance
  → TreasuryCarrierRemittance → carrierRemittanceApi
  → CarrierRemittancesController
  → ICarrierRemittanceService / CarrierRemittanceService
  → CarrierRemittanceAuthority (current SQL + immutable collection proof)
  → FinancialPostingService (independent source-specific request validation)
  → one outer Serializable PostgreSQL transaction
```

### Admission

The service validates server-resolved tenant scope and the current SQL active tenant,
current active same-tenant operator, Owner/Admin role, `storefront.payments.manage`
and `finance.operate` permissions. These checks also apply to selection, replay and
result retrieval. Actor and tenant are obtained from the authenticated principal and
trusted tenant context. Request bodies have no actor, tenant, ledger, currency or
amount authority; unmapped JSON fields are rejected.

Every selected order is revalidated within the transaction:

- Batch size **1–100**, nonempty, duplicate-free whole order IDs.
- Active same-tenant carrier-owned order/shipment and canonical carrier identity.
- Exact immutable qualified collection and shipment binding; current configuration,
  account, environment, target references and explicit currency must still agree.
- Existing `CarrierCollectionMoneyAuthority.RevalidateAsync` preserves its fail-closed
  historical-binding and permanent Review/RevalidationReview barriers.
- Payment must be Collected, paid, fulfilled as Delivered, financially Posted, and
  not held/cancelled/refunded. Delivery is not itself money authority.
- Exact canonical **Posted Order_COD journal**, matching journal pointer, order ID,
  reference, tenant and whole immutable amount; exactly **Dr 1130 / Cr 4020**.
- Its actual processed accounting receipt must reference that journal.
- Refund history, unsettled/reviewed/disputed or inconsistent payment intents,
  prior permanent membership, reversals and competing order-clearing journal lineage
  reject the request, including soft-deleted allocation history.
- Whole gross/currency reconcile across order, shipment, immutable evidence, journal,
  settlement allocation and carrier membership.
- All selected orders must share one carrier/configuration/account/environment/currency.
- Canonical active default Cashbox mapping must resolve to active Asset **1010**;
  active debit-normal Asset clearing must resolve to **1130**. Ledger balances must
  reconcile to persisted lines, with sufficient qualified clearing remaining.

Selection uses the same qualification checks and is never posting authority. It
does not select from `Shipment.CodCollected`, Delivered alone, `Order.Total` alone,
a generic clearing balance or client-authored money.

### Receipt and audit facts

`CarrierRemittance` permanently records operation ID, SHA-256 normalized request
fingerprint, original tenant/operator, carrier/configuration/account/environment,
explicit currency, whole gross, actual received amount, **zero fees**, actual receipt
time, separate server-recorded time, required merchant receipt/reference and its
normalized permanent key, optional notes/evidence reference, receiving/clearing
account IDs, actual receipt attestation, confirmation/reconciliation basis, final
Posted outcome, Posted timestamp and journal ID.

Receipt channel is **`PhysicalMerchantCash`**, confirmation basis
**`OperatorAttestedPhysicalReceipt`**, reconciliation **`WholeOrderZeroFeeMatched`**.
The operator must explicitly attest money already physically received; a promised
transfer, package delivery, customer collection or scheduled payout is insufficient.
Receipt time is explicit UTC, separate from recording/posting, cannot be future,
and cannot precede the immutable customer collection event.

### Atomic commit and replay

The command owns its transaction. Tenant/operator/mapping facts are locked; sorted
order locks precede shipment/configuration and sorted ledger-account locks, consistent
with the collection boundary. Current SQL is re-read rather than stale tracked data.

In one transaction it saves:

1. Dedicated immutable receipt document and evidence-qualified carrier membership.
2. Generic settlement snapshot plus shared permanent whole-order allocation.
3. Independently source-validated **Dr 1010 / Cr 1130** journal and actual balances.
4. Posted journal identity/outcome and processed accounting outbox receipt.
5. Settled payment state and durable generic domain outbox rows:
   `CarrierRemittanceAccountingPostedEvent` and each `OrderPaymentStatusChangedEvent`.

The existing financial posting engine remains the only ledger/balance writer. Its
new source-specific validation reconstructs every line from SQL and requires the
internal dedicated receipt scope. A substituted account, amount, date, actor, source
reference or idempotency key cannot gain that authority.

Success is returned only after COMMIT. Same operation + identical normalized receipt
facts/membership returns the original Posted result; changed facts conflict. Original
actor stays unchanged even when another currently authorized operator replays. New
operations cannot reuse allocated orders. Concurrent losers fail closed for conflict;
retry/retrieval of the committed identity recovers lost responses without cash posting.

Merchant receipt references are normalized (Unicode normalization, trimmed/collapsed
whitespace, uppercase key) and permanently unique per tenant across accounts/carriers.
Reused/conflicting references return reconciliation-required, not a second allocation.

Accounting recovery only acknowledges existing complete Posted history. It never
reconstructs or executes another receipt posting. Financial and domain outbox types
remain critical/durable under existing infrastructure.

## 3. Persistence constraints and migration

Migration: `PointOfSale.Infrastructure/Migrations/20261009182344_ManualCarrierCashRemittance.cs`
with Designer and current `ApplicationDbContextModelSnapshot.cs`.

- Adds **CarrierRemittances** and **CarrierRemittanceOrders**, restrictive FKs and
  reviewed receipt/zero-fee constraints.
- Permanent operation PK; unfiltered tenant/reference unique index; unfiltered
  carrier tenant/order and allocation unique indexes.
- Reuses existing unfiltered shared settlement tenant/order uniqueness.
- New entity tenant filters include permanent identities even if marked deleted.
- EF internal authority/immutability guards retain existing financial guards.
- PostgreSQL prevents receipt/membership mutation/deletion, carrier batch/allocation
  mutation and membership/allocation appended after Posted.
- A **DEFERRABLE INITIALLY DEFERRED** constraint trigger requires complete Posted
  journal, exact debit/credit, allocation/evidence reconciliation, processed accounting
  receipt and domain outbox at COMMIT. Preparing documents cannot become durable.
- Shared-table SQL triggers preserve non-carrier behavior; real tests explicitly
  verify non-carrier SQL updates are not suppressed. Manual EF immutability remains.

`dotnet ef migrations has-pending-model-changes` reports **no changes**.
The Bosta entities/migration were retained. Isolated fixtures apply the full EF chain
in order, including Bosta evidence then this migration, with existing supplemental
SQL prerequisites. Direct inspection confirmed **49 applied migrations** in the
latest isolated remittance database, last `20261009182344_ManualCarrierCashRemittance`.

## 4. Actual SQL journal and receipt evidence

Read-only inspection used the approved temporary `carrier-remittance-evidence`
utility; it contains no schema-application or financial mutation command. Both MAIN
business reads and isolated evidence reads use explicit READ ONLY SQL transactions.

Isolated fixture:
`pointofsale_tests_carrierremittancepostgreste_d04ec9ad_ee87a847`.

| Persisted fact | Actual SQL value |
|---|---|
| Operation | `6499efda-9a3e-4a2e-93fb-ebe8273c2335` |
| Request fingerprint | `9809248C74E4C6B07806E3F139ECBD728843B9441A603F21375ED46FE5DF2103` |
| Original operator | `1715dc9f-104b-459e-8c01-ef3c88750484` (isolated fixture identity) |
| Merchant receipt | `RECEIPT-d8ea9a7021c146f0bb3ac67e55efa2ea` |
| Binding | `bosta / account-1 / Sandbox / EGP` |
| Receipt channel/basis | PhysicalMerchantCash / OperatorAttestedPhysicalReceipt |
| Actual receipt UTC | `2026-10-09T19:39:28.471Z` |
| Server-recorded UTC | `2026-10-09T19:40:29.914445Z` |
| Posted journal | `01a1222e-6d95-741e-bfdc-32544ba41eeb` |
| Journal source | CarrierCashRemittance |
| Main Cashbox line | 1010: **Dr 100.0000 / Cr 0.0000** |
| COD Clearing line | 1130: **Dr 0.0000 / Cr 100.0000** |
| Persisted balances | **1010 = 100.0000; 1130 = 0.0000** |
| Processed accounting receipt | Same journal, `2026-10-09T19:40:30.504229Z` |
| Domain outbox | Receipt event for the operation (version 1), order payment event (version 4) |

Actual catalog inspection also confirmed permanent unfiltered tenant/reference,
tenant/order and operation PK identities. Tests separately assert multi-order gross
**300**, complete journal lines/balances, exact outbox/membership counts, replay,
and actual **25** refund leaving Main Cashbox **75**, COD Clearing **0**.

## 5. Refund lineage

`SettlementRefundLineage.cs` explicitly recognizes `CARRIER_CASH_RECEIPT` and
`CarrierCashRemittance`. It requires the dedicated Posted receipt, original actor,
immutable membership, zero fees, actual journal identity and recorded receiving/
clearing accounts, then resolves the **actual posted receiving debit** through the
existing canonical account reader. No guessed Cashbox or Clearing destination is used.

Existing payment lifecycle refund context already reads generic permanent settlement
membership, so it carries this dedicated type into the existing commerce accounting
and financial outflow validators. Their existing source-specific lineage read now
resolves the new receipt explicitly. Original collection/remittance journals and
membership are not rewritten for refunds.

Certified Manual COD reader behavior is unchanged. Its **35 tests**, **13 refund
prerequisite-lineage tests**, and **7 e-commerce refund tests** passed unchanged.

## 6. Treasury UI and API

UI route: **`/treasury/carrier-remittances`**, entered from Treasury Clearing Aging.
It uses canonical EN/AR locale JSONs and RTL conventions. It shows qualified whole
orders and exact account/environment/currency, prevents mixed-binding selection,
displays fixed **Dr 1010 / Cr 1130**, and requires explicit receipt time/reference,
actual-receipt checkbox and final confirmation. There is no editable amount,
destination, fee or bank-transfer field.

The immutable request is retained under a tenant/operator-scoped session key **before
POST**. Timeout/reload retries or retrieves that same operation. Only an admitted
Posted response with matching operation/membership/receipt facts and operator-attested
basis is displayed as success. Conflict/unverified/provider-confirmed-shaped responses
remain unresolved and never manufacture another operation. User changes remount the
workspace under the new tenant/operator scope.

API:

```text
GET  /api/treasury/carrier-remittances/eligible-orders?page=1
POST /api/treasury/carrier-remittances
GET  /api/treasury/carrier-remittances/{operationId}
```

All routes require current authorized financial operation permission, including
selection/recovery, and are no-store. POST uses existing FinancialWrite rate limiting.
Structured failures report ReconciliationRequired / NotPosted with stable codes.

## 7. Fresh builds, actual discovery and inspected final counters

Fresh source builds used a separate configuration, never MAIN Debug runtime binaries:

```powershell
dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" -c CarrierRemittanceVerification --no-restore --no-incremental -v:quiet /clp:ErrorsOnly
```

Final build **452 warnings / 0 errors / 26.68s**, rebuilding Tests and referenced
API/Core/Infrastructure. Product/SQL verification builds also succeeded before each
affected execution. After the SQL guard review, all 53 new PostgreSQL cases and
the 107-case financial set were re-executed. Later source inspection/build did not
change the certified product code.

Actual `--list-tests` discovery confirmed **128 / 107 / 308** disjoint cases with
the exact filters defined in `verify-carrier-remittance.py` in the approved temporary
directory. That verifier inspects every final XML counter and individual Passed
result, class expansion, disjoint identities and frontend JSON results.

TRX root: `C:\Users\fadya\AppData\Local\Temp\opencode`.

| Final TRX | Total / executed / passed | Failed / skipped | Duration | Finish UTC |
|---|---:|---:|---|---|
| `carrier-remittance-final-new.trx` | **128 / 128 / 128** | **0 / 0** | 14m27s | 19:25:24.9745846Z |
| `carrier-remittance-final-financial.trx` | **107 / 107 / 107** | **0 / 0** | 9m02s | 19:42:36.4422056Z |
| `carrier-remittance-final-authority.trx` | **308 / 308 / 308** | **0 / 0** | 9m58s | 19:04:02.4182066Z |
| `carrier-remittance-final-sqlguards.trx` (overlapping) | **53 / 53 / 53** | **0 / 0** | 6m57s | 19:40:31.6234840Z |

Errors, timeouts, aborted, inconclusive, notRunnable, notExecuted, disconnected,
warning and pending counters are all **0**. **543 distinct backend cases**.

New selection expansion: remittance PostgreSQL **53**, remittance API **5**, existing
Bosta PostgreSQL **50**, official parser **20**. New relational coverage includes
positive one/three-order posting, balances, exact journals/receipts/outboxes, every
admission class, prior/deleted allocations, current actor/tenant permission, immutable
bindings, receipt reuse, changed facts, concurrent overlap, lost-response retrieval,
actual refund destination, direct SQL uniqueness/immutability, and append rejection.

Injected failures cover journal SQL, balance SQL, accounting outbox, domain outbox,
final commit interception and **actual PostgreSQL deferred-constraint COMMIT failure**
after financial writes. Every case verifies no partial receipt/membership/journal/
balance/state/outbox survived, and retries with the original ID succeed.

Financial expansion: Manual **35**, COD lifecycle **32**, refund prerequisites **13**,
Bosta endpoint **10**, trust pipeline **7**, e-commerce refunds **7**, consumer **2**,
tenant resolution **1**.

Authority expansion: commerce accounting **50**, accounting outbox **28**, cancellation
**59**, tracking persistence concurrency **27**, tracking tenant context **20**, tenant
filter freeze **4**, and the existing **120-case** cancellation/permission/Treasury/
shipment-authority regression slice. Existing tests/project exclusions were not changed.

Frontend command from MAIN Web:

```powershell
npx vitest run src/pages/treasury src/modules/shipping/cancellationSafety.test.tsx src/modules/shipping/shipmentActionAuthority.test.ts src/auth/marketplaceOrderTransitionAuthority.test.ts src/pages/Orders.test.ts src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx --reporter=default --reporter=json --outputFile="C:\Users\fadya\AppData\Local\Temp\opencode\carrier-remittance-final-vitest.json"
npm run build -- --outDir "C:\Users\fadya\AppData\Local\Temp\opencode\carrier-remittance-final-web-build"
```

**14 files / 96 tests passed**, **11.40s**, including **16 new EN/AR receipt cases**,
Manual COD **5**, Treasury ledger/catalog/transfers and **22 cancellation UI cases**.
Production build **succeeded, 5246 modules, 34.30s**. Existing React Router,
external-output-directory and large-chunk warnings remain. Expected negative-test
catalog/read diagnostics are not product failures.

Intermediate fixture/compiler diagnostics were corrected rather than weakening
guards: evidence timestamp admission, valid financial-posting metadata, foreign-filter
expectation and the existing one-configuration-per-tenant/carrier constraint. The
mixed-account fixture now changes the canonical account without rewriting immutable
old bindings. These intermediate failures are not counted as certification passes.

## 8. Exact implementation paths

New files (untracked, deliberately not staged/committed):

```text
PointOfSale.Core/Entities/CarrierRemittance.cs
PointOfSale.Core/Interfaces/Payments/CarrierRemittance.cs
PointOfSale.Core/Events/CarrierRemittanceAccountingPostedEvent.cs
PointOfSale.Infrastructure/Accounting/CarrierRemittanceAuthority.cs
PointOfSale.Infrastructure/Accounting/CarrierRemittancePersistenceAuthority.cs
PointOfSale.Infrastructure/Payments/CarrierRemittanceService.cs
PointOfSale.Infrastructure/Data/Configurations/CarrierRemittanceConfiguration.cs
PointOfSale.Infrastructure/Migrations/20261009182344_ManualCarrierCashRemittance.cs
PointOfSale.Infrastructure/Migrations/20261009182344_ManualCarrierCashRemittance.Designer.cs
PointOfSale.API/Controllers/CarrierRemittancesController.cs
PointOfSale.Tests/Integration/CarrierRemittancePostgresTests.cs
PointOfSale.Tests/Payments/CarrierRemittanceApiTests.cs
PointOfSale.Web/src/services/carrierRemittanceApi.js
PointOfSale.Web/src/pages/treasury/TreasuryCarrierRemittance.jsx
PointOfSale.Web/src/pages/treasury/TreasuryCarrierRemittance.test.tsx
.ai/reports/MANUAL_CARRIER_REMITTANCE_IMPLEMENTATION.md
```

Additive integrations in existing files:

```text
PointOfSale.API/Program.cs                                      (scoped service)
PointOfSale.API/Security/PosEndpointPermissionCatalog.cs        (all-route permission)
PointOfSale.Infrastructure/Data/ApplicationDbContext.cs        (sets, filters, guards)
PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs
PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs (dedicated source validation)
PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs (receipt-only recovery/critical type)
PointOfSale.Infrastructure/Accounting/SettlementRefundLineage.cs (explicit new source)
PointOfSale.Web/src/App.jsx
PointOfSale.Web/src/pages/treasury/index.js
PointOfSale.Web/src/pages/treasury/TreasuryClearingAging.jsx
PointOfSale.Web/src/i18n/locales/en.json
PointOfSale.Web/src/i18n/locales/ar.json
```

`git diff --name-only` **and** `git ls-files --others --exclude-standard` were
inspected. New product files do not appear in plain tracked diff output. Existing
Manual/Bosta/cancellation/tracking/Treasury hunks in shared files were retained and
not attributed to this task. `git diff --check` passed.

## 9. Preservation and bounded stop

Protected source hashes still match the initial preserved versions:

| Source (project prefix abbreviated) | Preserved Git hash |
|---|---|
| Infrastructure/Payments/PaymentLifecycleService.ManualCodSettlement.cs | `7b72ac580f30224df1e0a0276554c4a4f08cb64e` |
| Infrastructure/Accounting/ManualCodSettlementAuthority.cs | `3cebf62c2cabba0849793a0b138e2a31767bf601` |
| Core/Interfaces/Payments/ManualCodSettlement.cs | `70f9c189cd579053236615e80b4dac44e7259c12` |
| Tests/Integration/ManualCodSettlementTests.cs | `44649d4a3c765ad95b8f4d51555bdfe782085d29` |
| Infrastructure/Migrations/20261008223819_ManualCodPermanentSettlementIdentity.cs | `b7b3469ef455a3ded367567f3a4dd5d4be5f2352` |
| Web/src/pages/treasury/TreasuryManualCodSettlement.jsx | `45de756de165a067a16e4c9554983d2bb72049e3` |
| Web/src/services/manualCodSettlementApi.js | `c530cead64685e56b1202ef79c9ffcece704a9a8` |

`SettlementRefundLineage.cs` was intentionally extended as requested: original hash
`f2482c510b76022e4ed6ede3da26bf42194a2c52`, current
`ca534cf17cc10df541b23669d49e2dc37c9033bd`. Its existing Manual branch remains intact;
the new type/source mapping and carrier-only validation are additive. Unchanged
Manual/refund regression tests passed; no unchanged-hash claim is made for this reader.

Read-only MAIN verification confirmed **47 applied / 2 pending migrations**, no EF
model drift and no new remittance table. The eleven original business-table counts
and sorted `(Id,xmin)` fingerprints still match the prior preservation baseline:
Orders **15**, Shipments **8**, OrderRefunds **6**, PaymentIntents **7**, settlement
batches/memberships **0**, cancellation outbox **1**, accounting outbox **0**, journals
**190**, lines **399**, stock movements **197**. Cancellation operation rows remain **0**.
No merchant financial operation or live carrier API was invoked by this task.

MAIN port 5010 still belongs to **PID 13820**, parent `dotnet run` **10876**, executable
under MAIN Debug. This task did not stop/restart it or replace its Debug binaries.
Carrier automated remittance flags and disabled legacy settlement routes/coordinator
remain unchanged; T10 **8** cases pass. Generic Clearing-transfer rejection is retained,
including real relational source/destination rejection cases and Treasury UI assertions.

### Remaining scope limits

- This is source + isolated transaction certification; MAIN migration/runtime
  activation and deployment were deliberately not performed.
- Current observed-money qualification is the completed Bosta authority. Other
  providers/historical shipments without admitted immutable provenance remain closed.
- Physical receipt truth is explicitly operator-attested, not externally verified.
  No provider automation, statement ingestion, bank transfer, fees, FX, partial or
  split allocation, correction/reconciliation mutation or historical backfill is supplied.
- Conflicts remain review-required. No automatic rewrite/reallocation of financial
  history or invented provider confirmation is available.
- API tests cover controller/authenticated principal/current SQL service execution
  and permission admission; UI tests use DOM rendering with mocked network calls.
  No live merchant browser submission or live provider certification is claimed.

**Definition of done demonstrated:** qualified Posted carrier COD can be physically
received by explicit authorized operator attestation and moved atomically, permanently
and idempotently from COD Clearing to Main Cashbox, with actual debit-account refund
lineage, durable receipts/outboxes and no double allocation. **STOP here.**
