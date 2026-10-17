# COD settlement — pre-implementation forensic audit

## Verdict

**AUDIT COMPLETE; IMPLEMENTATION STOPPED AT THE USER'S CONTRADICTION GATE.**

Session date: 2026-10-09 Windows local. Observed API evidence timestamps are 2026-10-08 UTC; these are the timestamps actually returned by MAIN, not inferred dates. Repository HEAD: `256339621c0ea49f3602097cdacdf89ffe71754f`.

Two source-proven conflicts must be resolved before implementing the requested settlement boundary:

1. **Current operator collection authority does not distinguish manual collection from carrier collection.** An authorized operator can call the active COD collection endpoint for a delivered carrier COD order. The lifecycle checks COD and delivery, but does not check fulfillment ownership, a collected shipment, or carrier collection evidence. A persisted `Collected` state therefore does not by itself prove the claimed carrier collection provenance.
2. **Current settled-order/refund accounting assumes Bank, not a settlement-specific destination.** The existing post-settlement refund resolver always selects Bank 1030. Reusing the existing settlement membership/state for a new manual cashbox settlement without extending refund lineage would retain incorrect Bank-funded refund semantics.

The task explicitly requires stopping and documenting contradictions instead of silently forcing a design. No application code, test code, configuration, schema, migration, shared financial data, or existing working-copy changes were modified during the forensic phase. This report is its deliverable. These conflicts are not a proposal to restore generic Clearing transfers.

## Scope and evidence discipline

Primary working copy: `C:\Users\fadya\source\repos\PointOfSale`.

Read first: `docs/reports/MAIN_COD_CLEARING_SETTLEMENT_GENERAL_LEDGER_FINAL_CERTIFICATION_2026-10-08.md`. Its working-copy remediation already exists and was preserved. Prior test counts and browser certification in that document are historical evidence, **not tests executed in this session**.

The initial worktree already contained modifications to:

- `PointOfSale.Tests/Architecture/TreasurySettlementAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Integration/CodPaymentLifecycleTests.cs`
- `PointOfSale.Tests/Payments/T10SettlementContainmentTests.cs`
- `PointOfSale.Tests/Security/PosPermissionAuthorizationTests.cs`
- `PointOfSale.Web/src/i18n/locales/ar.json`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/pages/treasury/TreasuryAccountViewer.jsx`
- `PointOfSale.Web/src/pages/treasury/TreasuryClearingAging.jsx`
- `PointOfSale.Web/src/pages/treasury/TreasuryGeneralLedger.jsx`
- `PointOfSale.Web/src/pages/treasury/TreasuryHub.jsx`
- `PointOfSale.Web/src/pages/treasury/TreasuryTransfers.jsx`

Initial untracked files:

- `PointOfSale.Web/src/pages/treasury/TreasuryGeneralLedger.test.tsx`
- `docs/reports/MAIN_COD_CLEARING_SETTLEMENT_GENERAL_LEDGER_FINAL_CERTIFICATION_2026-10-08.md`
- `tests/browser/main-cod-ledger-certification.js`

They are pre-existing work, not this task's implementation.

Code evidence is cited below by repository-relative path, method, and line where useful. Runtime checks used localhost and the existing read-only Npgsql forensic executable. It opens `REPEATABLE READ`, executes `SET TRANSACTION READ ONLY`, and rolls back. Only its default schema inspection and `state.json` SELECT-query mode were executed; no cleanup mode was invoked. Credentials were loaded by that existing executable from local configuration and are not reproduced here.

No browser/login, storefront checkout, customer collection, settlement, external provider request, or reject-only financial POST was needed for this read-only investigation. LOAD-TEST and Dell were not accessed or changed.

## 1. Source and authority inventory

| Concern | Current source / meaning |
|---|---|
| Storefront order admission | `PointOfSale.API/Services/Checkout/CheckoutService.cs`, order construction around 6635–6649: Pending payment; fulfillment ownership/carrier copied into order snapshots |
| Payment lifecycle state | `PointOfSale.Core/Enums/PaymentStatus.cs`; canonical mutation owner `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs` |
| Order identity / payment / financial posting / delivery snapshots | `PointOfSale.Core/Entities/Order.cs`; immutable historical snapshot guards in `Infrastructure/Data/ApplicationDbContext.cs` |
| Storefront payment-method configuration | `Core/Entities/TenantPaymentMethod.cs`: enablement, provider settings, bilingual labels, customer-facing payment fees |
| Ledger payment-method mapping | `Core/Entities/Accounting/PaymentMethod.cs`: TargetAccountId vs ClearingAccountId vs FeesAccountId; separate from TenantPaymentMethod |
| Fulfillment ownership | `Core/Enums/FulfillmentMode.cs`: immutable Manual=0, Carrier=1; carrier code null for manual, required for carrier |
| Fulfillment transitions | `Infrastructure/Fulfillment/OrderFulfillmentService.cs`; `Core/Interfaces/Fulfillment/IOrderFulfillmentService.cs`; `Core/Enums/FulfillmentStatus.cs`; `FulfillmentStatusSaveChangesInterceptor.cs` |
| Collection HTTP ingress | `API/Controllers/PaymentLifecycleController.cs:90–113`, POST `{orderId}/cod-delivered` |
| Collection event ingress | `Infrastructure/Consumers/ShipmentCodCollectedConsumer.cs:27–72` |
| Delivery event observer | `Infrastructure/Consumers/OrderDeliveredEventConsumer.cs` (`FulfillmentDeliveredEventConsumer`); explicitly does not collect money on delivery |
| COD collection journal | `Infrastructure/Accounting/CommerceAccountingService.cs:1392–1427`, `PostCodDeliveredAsync` |
| Existing settlement headers / order membership | `Core/Entities/PaymentSettlementBatch.cs`, `PaymentSettlementOrder.cs`; EF configuration `ApplicationDbContext.cs:898–944` |
| Retained settlement coordinator | `PaymentLifecycleService.RecordOfflineSettlementAsync:327–561`; gateway sibling `RecordGatewaySettlementAsync:583–874` |
| Settlement HTTP containment | `PaymentLifecycleController.cs:206–259`; both routes return 409 `SETTLEMENT_EVIDENCE_REQUIRED` |
| Retired low-level clearing ingress | `API/Controllers/AdminAccountingController.cs:114–126`: 410 `SETTLEMENT_AUTHORITY_REQUIRED` |
| Journal generation / posting | `CommerceAccountingService`; `FinancialPostingService`; `Core/Models/Accounting/PostingRequest.cs` and posting models |
| SQL collection / settlement replay authority | `Infrastructure/Reliability/AccountingLedgerAuthority.cs`; `Infrastructure/Accounting/AccountingOutboxProcessor.cs` |
| Accounting outbox | `Core/Entities/Accounting/AccountingOutboxMessage.cs`; `IAccountingOutboxService` and accounting implementation; claimed/retried journal delivery |
| Treasury financial account | `Core/Entities/Accounting/Account.cs`, `AccountCodes`, account templates, `TenantAccountSeeder` |
| Treasury role / mapping | `Core/Entities/Accounting/TreasuryAccount.cs`; separate role Cashbox=1, Bank=2, Clearing=3 |
| Generic transfer executor | `Infrastructure/Treasury/TreasuryOperationService.cs`; legacy ID-based `Infrastructure/Accounting/TreasuryTransferService.cs` |
| Treasury reads | `Infrastructure/Treasury/TreasuryReadService.cs`; official ledger `Infrastructure/Accounting/FinancialReportService.cs` |
| Server permissions | `Core/Security/PosPermissions.cs`; `API/Security/PosEndpointPermissionCatalog.cs`, `PosPermissionApplicationModelConvention.cs`, permission handler/session authority |
| Carrier adapter boundary | `Infrastructure/Shipping/IShippingCarrierAdapter.cs`; `ShippingCarrierRegistry.cs`, `CarrierCapabilityRegistry.cs`, `CarrierIntegrationAuthority.cs`, adapter directory |
| Carrier webhook/tracking authority | `Infrastructure/Shipping/CarrierShipmentService.cs`; `ShippingWebhookLog`, shipment SQL truth and tracking result models |
| Reference COD foundation | `API/Services/Payments/Cod/*`, `API/Configuration/CodReferenceWorkflowOptions.cs`; evidence/settlement capability disabled |
| Other payment settlement concept | `Core/Payments/Runtime/Domain/PaymentSettlementRuntime.cs`: pure deterministic domain runtime object, not a carrier bank receipt or tenant/order ledger settlement batch |

## A. Current Manual COD flow

1. Checkout admits an order with `PaymentStatus.Pending`, its payment-method snapshot and `FulfillmentMode.Manual` where the admitted shipping method establishes manual ownership.
2. The valid manual fulfillment authority owns Confirm → Pack → OutForDelivery → Delivered transitions. Manual delivery requires a delivery reference and absolute HTTP(S) proof-of-delivery URL (`OrderFulfillmentService.MarkDeliveredAsync:151–255`). Evidence is persisted; delivery replay cannot overwrite conflicting evidence.
3. Delivery is **not collection**. `FulfillmentDeliveredEventConsumer:57–65` leaves Pending COD awaiting explicit collection authority. Comments in older test headers that say “auto-collected” do not override this executable behavior.
4. An authorized POS caller explicitly invokes `POST /api/payments/lifecycle/{orderId}/cod-delivered`. The controller supplies current tenant, optional delivery reference and operator name to `MarkOfflineCollectionDeliveredAsync`.
5. That wrapper builds an `OfflineCollection` success request. Lifecycle SQL-locks the order; checks explicit tenant authority, valid current state, COD payment identity, and `FulfillmentStatus.Delivered` (`PaymentLifecycleService:308–324, 2232–2419`).
6. The lifecycle allocates payment, sets Collected / IsPaid / PaidAt, commits inventory/reservation-related work, stages the accounting outbox and payment events, and commits (`2513–2604` and following transaction code).
7. Post-commit SQL-authority accounting creates Dr1130 / Cr4020; accounting failures remain recoverable through the outbox. Posted status/journal linkage is finalized by the subsequent accounting transaction (`6260–6338`). Collection state and journal are therefore not one indivisible initial transaction, notwithstanding some existing summaries. Settlement must wait for a posted collection journal.
8. Replays of successful terminal payment truth do not stage a second collection (`2282–2338`); journal key `COD_DELIVERED:{tenant}:{order}` is unique per tenant.
9. **No dedicated manual collection settlement currently exists.** The generic transfer route cannot receive 1130, the admin clearing endpoint is retired, and dormant offline settlement targets Bank, not Main Cashbox.

Manual evidence today is an authenticated operator assertion plus persisted delivery/payment/order lineage, not an independently persisted immutable COD collection-evidence entity. Actor is passed to lifecycle events; `PostCodDeliveredAsync` does not populate `PostingRequest.UserId`.

## B. Current Carrier COD flow

Intended normal path:

1. Checkout snapshots `FulfillmentMode.Carrier` and `FulfillmentCarrierCode`.
2. `CarrierShipmentService` owns carrier tracking/webhook changes. It resolves a tenant-owned shipment, verifies configured webhook trust, records a signature-verification/payload-hash log, rejects duplicate/conflicting event payloads, and rejects manual-fulfillment orders (`1499–1729`). Supported trust methods are implemented centrally; do not infer a provider's signature behavior from its name.
3. Carrier results contain `CodCollected` and `CodCollectedAt`, **not bank remittance**. The service persists these on the shipment and publishes `ShipmentCodCollectedEvent` when collection is newly applied (`1753–1758, 1783–1785, 1845–1847`). Tracking refresh has an equivalent collection-flag path.
4. `ShipmentCodCollectedConsumer` checks nonempty tenant and calls the same `MarkOfflineCollectionDeliveredAsync`, with tracking/shipment reference and carrier name/code as actor. It does not pass shipment ID, collected gross or currency as authority to that method. Failed lifecycle transitions throw for retries (1, 5, 15 seconds).
5. The lifecycle requires delivered SQL order truth and collects/postings exactly as for manual COD.
6. No adapter in `Shipping/Adapters` was found to retrieve/parse a COD remittance report, authenticated settlement ID, gross/provider fee/net bank receipt, or matched-order remittance membership.
7. The old COD settlement POST intentionally returns 409. No production API caller of the retained offline/gateway settlement service methods was found. Accounting outbox recovery still posts historical SQL settlement authority through the commerce journal methods.

### Blocking contradiction C1 — collection provenance is not actually separated

Exact call chain:

```text
PaymentLifecycleController.MarkCodDelivered (authorized operator)
  → MarkOfflineCollectionDeliveredAsync(tenant, order, optional reference, operator name)
  → HandlePaymentResultAsync(Source=OfflineCollection, Outcome=Succeeded)
  → HandleSuccessAsync
      COD identity check
      FulfillmentStatus == Delivered check
      no FulfillmentMode check
      no FulfillmentCarrierCode check
      no Shipment.CodCollected check
      no trusted carrier-evidence identity check
      Pending → Collected
      accounting outbox → Dr1130 / Cr4020
```

`GetAllowedActionsAsync:1492–1494` also advertises collection using only COD + Pending + Delivered, without manual ownership. A search of the whole lifecycle implementation found no `FulfillmentMode`, `FulfillmentCarrierCode`, `Shipment`, or `CodCollected` checks.

Thus the normal carrier consumer has credible transport lineage, but **the active operator route is an alternative ingress to the same recognition authority**. Neither generic role authorization nor the payment-status interceptor supplies the missing economic provenance: the authorized lifecycle writer itself lacks the ownership/evidence condition.

This is a source-proven admitted path, not a successful live exploitation claim. No carrier order was altered to demonstrate it. The current AhramStore orders are ineligible and were read-only.

The requested design assumes carrier delivery/collection truth is established before settlement. Simply filtering settlement to carrier + Collected + posted journal would inherit this gap. Preserve manual collection, introduce a distinct trusted carrier collection entry point, and recompose shipment/order/tenant/provider/amount/currency evidence at that boundary before enabling remittance settlement. Existing carrier Collected rows must not be automatically promoted to verified provenance.

## C. Current accounting postings and invariants

Account codes are source-owned (`AccountCodes`, seeder/templates) and verified in MAIN SQL:

- 1010 Main Cashbox: Asset, treasury Cashbox.
- 1030 Bank: Asset, treasury Bank.
- 1130 COD Clearing: Asset, `IsClearing=true`, treasury Clearing; not available cash.
- 4020 E-commerce Sales: collection revenue account.
- 6020 Shipping Fees: retained COD fee expense account.

Collection, manual or carrier:

```text
Dr 1130 COD Clearing       order.Total
Cr 4020 E-commerce Sales   order.Total
SourceType = Order_COD
SourceId = order.Id
SourceReference = order.OrderNumber
IdempotencyKey = COD_DELIVERED:{tenant}:{order}
```

The retained historical carrier/offline settlement generator `PostCodSettlementAsync:1954–2000`:

```text
Dr 1030 Bank               gross - fees
Dr 6020 Shipping Fees      fees (if positive)
Cr 1130 COD Clearing       gross
SourceType = CODSettlement
SourceId = settlement batch.Id
SourceReference = settlementId
IdempotencyKey = COD_SETTLEMENT:{tenant}:{settlementId}
```

The proposed dedicated manual journal Dr1010 / Cr1130 is **not implemented** and must not be presented as an existing posting.

`FinancialPostingService` validates balance, valid positive posting lines and accounts, verifies treasury inflow/outflow SQL source authority, creates immutable journal/lines, and applies account balances through `Account.ApplyEntry` in the ledger mutation scope. Treasury inflow recognizes `CODSettlement` only against a matching nondeleted `OFFLINE_COLLECTION` SQL batch (`691–704`). An arbitrary new journal source will not acquire cashbox inflow authority by calling the posting engine; an explicit source-owned manual boundary is required.

Important findings:

- Dormant settlement fees may equal gross; its generator unconditionally constructs a Bank debit of net. A zero-net debit violates the positive posting-line contract. Resolve this explicitly (e.g. omit a zero Bank line while retaining fees/clearing balance), rather than silently rejecting mathematically valid provider evidence or claiming existing support.
- Account/journal/ledger-line models do not carry an independent currency dimension here; account balances are scalar. Checking only that orders share a currency is insufficient to prevent another currency reaching the same tenant account balance. A new boundary must also enforce current ledger/tenant currency authority and supported persisted precision (existing settlement amount columns are numeric(18,2)).
- Pre-settlement COD refunds credit Clearing. Literal “all collected orders not settled” is not the full reconciliation amount in a domain that also permits refunds. Reconcile recognized collection debits **less valid refunds/reversals and settlement credits**; reject partially/refunded orders from whole-order settlement unless an explicit residual-collection model is designed. Do not invent partial settlement support.
- `Collected` plus `FinancialPostingStatus.Pending` can temporarily precede its journal. Financial claims must use posted lineage, not just payment labels.

### Blocking contradiction C2 — existing Settled semantics and refund destination

`PaymentStatus.cs:82–87` documents Settled as money in company Bank. More importantly, executable `CommerceAccountingService.ResolveEcommerceRefundCreditAccountIdAsync:1804–1830` returns Bank1030 for **every** post-settlement refund after checking a durable settlement snapshot. `EcommerceRefundAccountingContext` stores batch/type/reference/time, not the actual settlement destination.

`PaymentLifecycleService.ResolveRefundAccountingContextAsync:4898–4960` derives post-settlement authority from `PaymentSettlementOrders` and its batch. Therefore:

- reusing membership and setting Settled for a manual cashbox settlement would route subsequent refund accounting to Bank;
- omitting canonical membership would cause a settled order refund to fail for missing settlement snapshot;
- leaving a manually settled order Collected without an explicit settlement-aware downstream contract could cause pre-settlement refund accounting to credit Clearing already cleared.

The implementation boundary must include settlement-kind/destination-aware refund lineage and replay, even though the requested feature is called settlement. This is a necessary bounded dependency, not an unrelated rewrite.

## D. Existing reusable infrastructure

### Persisted settlement concepts

`PaymentSettlementBatch` already stores tenant, settlement type/ID, optional provider/payout ID, gross, fees, currency, bank reference, order count, settlement time and audit base fields. `PaymentSettlementOrder` stores tenant, batch, order, optional payment intent, whole order total and currency.

Current schema was directly inspected in MAIN and matches those columns. Both entities already exist in `20260723214642_Baseline_Recovery3`, with matching current model snapshot and EF configuration. Do not create another independent settlement-order allocation table that bypasses their cross-workflow uniqueness.

Existing EF/baseline constraints:

| Index | Purpose | Limitation |
|---|---|---|
| `IX_PaymentSettlementBatches_Tenant_Type_SettlementId` | Unique tenant + settlement type + ID | Provider not part of identity; filtered on IsDeleted=false |
| `IX_PaymentSettlementOrders_Tenant_Order` | Unique tenant + order across batches | Filtered on IsDeleted=false; no partial-settlement allocation |
| `IX_PaymentSettlementOrders_Tenant_Batch_Order` | Unique tenant + batch + order | Same soft-delete filter |
| `IX_LedgerJournal_Idempotency` | Unique tenant + journal idempotency key | Ledger uniqueness does not authenticate evidence |
| `IX_LedgerJournal_Number` | Unique tenant + journal number | Concurrency on numbering still requires transaction strategy |
| `IX_LedgerAcct_TenantCode` | Unique tenant + account code | Account code alone is not treasury permission |

Index definitions above were verified in source, EF configuration and baseline migration; a direct `pg_indexes`/trigger inventory was not executed in this read-only phase. No claim is made that a new migration's constraints have been checked.

The entity comment calls settlement membership immutable, but no dedicated settlement-batch/membership update/delete guard was found in `ApplicationDbContext` or its inspected interceptors. Both expose setters and soft-delete fields. Immutable evidence, allocation and terminal settlement guards need implementation; do not treat comments or filtered unique indexes as permanent immutability.

### Lifecycle, posting and recovery

- Preserve `IPaymentLifecycleService` ownership and `PaymentStatusSaveChangesInterceptor` (`AsyncLocal` authorized context). Do not let a new controller/service open an interceptor bypass independently to assign payment states.
- Existing lifecycle isolation defaults to Serializable, requires supported row locking, uses PostgreSQL `FOR UPDATE` on orders (`3094–3117`), sorts settlement order IDs (`3168–3188`), and supports joining a transaction through a savepoint (`3191–3308`).
- Existing account balance configuration uses PostgreSQL `xmin` optimistic concurrency (`AccountConfiguration:95–100`). Posting sorts account loads; it does not issue an explicit account `FOR UPDATE` query in the inspected method. Do not overstate sorted reads as pessimistic row locks.
- Ledger authority forbids direct account-balance writes and unauthorized journal/line creation (`ApplicationDbContext.GuardTreasuryCashAuthority`); posted journal financial identity is immutable. These safeguards must remain.
- Retained settlement validation checks tenant, duplicate-free nonempty order set, currency, gross equal to summed order totals, fees bounds, Collected status, and posted collection keys (`RecordOfflineSettlementAsync`, `EnsureCollectionAccountingPostedForCodSettlementAsync`). It has **no manual/carrier ownership or remittance authentication test**.
- Settlement retry compares batch type, ID, provider, payout ID, gross, fees, currency and order set (`SettlementBatchMatches:1706–1729`). Bank reference/date are not all part of that comparison; provider evidence identity needs a canonical immutable fingerprint.
- Settlement commits batch/membership, Settled state and outbox before best-effort journal posting. Failures return/log appropriately and outbox recovery reloads SQL authority; a committed state alone is not proof of a posted settlement journal.
- `AccountingLedgerAuthority.LoadCurrentSettlementAsync` verifies tenant/type/ID and active membership count; `EvaluateSettlementPosting` checks amount/fee bounds and count. It is SQL-replay authority for existing batches, not external remittance verification.
- Accounting processor retains CodSettlement and GatewaySettlement handling (`AccountingOutboxProcessor:3521–3552` for COD). New manual kind must have its own replay journal shape and source authority; cannot reuse CODSettlement and accidentally post to Bank.

### Canonical reference runtimes

The COD reference workflow has evidence source/type contracts and an empty `ICodSettlementAuthority` foundation contract. `CodCollectionEvidenceCatalog` reports all runtime admission/mutation/settlement capabilities false. `CodWorkflowOrchestrator` emits intents only. Live health also reports this foundation disabled.

The separate pure `Core.Payments.Runtime.Domain.PaymentSettlement` object includes OfflineCollectionSettlement and deterministic replay concepts, but no persisted COD order allocation, carrier remittance authenticity, fee/net bank receipt or executed ledger mutation. Reuse useful value-object conventions only; do not confuse it with the SQL settlement authority.

## E. Missing domain concepts

1. Distinct manual and trusted carrier COD **collection authority/provenance**, including SQL-verifiable collection journal lineage.
2. Dedicated manual collection settlement command/read model with whole-order selection, expected gross/currency, actor, immutable operation identity and Main Cashbox1010 destination authority.
3. Canonical authenticated `RemittanceEvidence`, external settlement identity scoped by tenant/provider, remittance date, gross/fees/net, complete order-reference membership, provider reference/metadata and verification provenance/status.
4. `ICodRemittanceEvidenceProvider` (or codebase-equivalent) and trusted ingress that fetches/verifies evidence using tenant-owned integration authority. The browser must not submit a `verified=true` assertion as authority.
5. Verified evidence → allocation/settlement/journal linkage with whole-order double-settlement prevention **shared with existing membership**.
6. Actual settlement destination/kind recorded for refund/replay source authority.
7. Terminal immutability and unfiltered identity protection across soft-delete/replay.
8. Explicit mismatch/failure/pending evidence states so remittance arriving before delivery/collection can be stored or rejected without recognizing cash.
9. Production-inaccessible deterministic fixture provider/ingress for disposable verification.
10. Bilingual Manual Collections / Carrier Remittances / Settled COD UI and paginated tenant-owned evidence/settlement reads.

### Required proof questions

| Question | Evidence-backed answer |
|---|---|
| How does an order become Collected? | Canonical lifecycle success with OfflineCollection source; delivered COD validation; payment allocation/status transaction; collection outbox and journal recovery. Manual POST and carrier consumer share this path. |
| Are collection and remittance separate today? | Yes, payment states/outbox/journal types distinguish them. Delivery consumer does not collect. Remittance execution is contained. Collection ownership/provenance is not adequately distinguished. |
| Does a current provider expose a remittance ID? | No production COD remittance adapter or mapping found. Caller-authored `SettlementId` and `ShippingCompany` in dormant request DTOs are not provider evidence. Shipment external reference/tracking IDs identify shipment creation/tracking, not bank remittance. |
| Are fees represented? | Yes: dormant settlement batch `Fees`, 6020 fee line, gateway FeesAccountId; storefront TenantPaymentMethod.Fee/FeeType and order PaymentFee/ShippingCost are customer-facing pricing facts, not authenticated carrier deductions. COD payment mapping currently has null FeesAccountId. |
| Can one order be collected twice? | Normal recognized terminal replay does not collect again and unique tenant/key prevents a second COD collection journal. This is source evidence plus prior tests; no new live/concurrent-success test was executed. Replayed carrier events can re-enter lifecycle safely, but source authenticity is a separate unresolved issue. |
| Can one order be settled twice? | Active HTTP settlement cannot settle once or twice (409). Dormant Collected-only gate and active unique tenant/order membership constrain duplicate settlement; identical batch retries return idempotent success. Filtered uniqueness and absent immutability guards are insufficient for a new permanent evidence domain. Concurrent positive settlement was not newly tested or certified. |

## F. Risks of implementing settlement incorrectly

- Cashbox collection or carrier receipt recognized from delivery labels instead of economic source truth.
- Carrier collection promoted from the operator bypass described in C1.
- Caller-authored remittance totals, text reference or confirmation dialog promoted to authenticated provider truth.
- Manual settlement journal implemented via generic transfer exception, removing source/order allocation checks.
- Manual settlement marked terminal while refunds remain Bank/Clearing based (C2).
- Scalar account balances contaminated with mixed or non-tenant currency.
- Reusing Bank CODSettlement journal/replay for manual allocation.
- Marking Settled and reporting journal success while post-commit accounting is pending/failed.
- Partial provider payload accepted as a complete remittance batch; silently losing unmatched orders or fees.
- Soft-delete reopens an external reference or order membership identity.
- Two requests allocate overlapping orders; serializable/unique exceptions are exposed as unrecoverable internal errors rather than converging to existing success or explicit conflict.
- Zero-net remittance generates an illegal zero Bank debit.
- Gross reconciliation ignores pre-settlement refund/reversal credits.
- New controller misses convention-based permissions or grants Manager payment-management powers by borrowing generic finance.operate.
- Fixture provider registered in production or real merchant tenant can mint remittance evidence.

## G. Exact recommended implementation boundary

This is a **recommendation after the contradiction gate**, not delivered code.

### Required prerequisite fixes

1. Keep the existing operator COD collection command valid for **manual fulfillment only**. Make allowed-action projection agree.
2. Introduce/reuse a trusted carrier collection command under the same canonical lifecycle owner. Resolve the tenant-owned shipment and require its recorded collection truth, correct provider/order relationship, delivery truth, amount and currency. Carrier events are triggers to re-read this authority, not freely authored amount instructions. Preserve retry/out-of-order behavior.
3. Add durable settlement kind/destination lineage used by refunds and SQL recovery. Keep historical COD/Gateway settlements mapped to Bank1030. Decide/document post-manual-settlement refund source under existing source-owned refund authority; never implicitly select Bank or Clearing for a cashbox receipt.

### Settlement implementation

- Extend/reuse `PaymentSettlementBatch` + `PaymentSettlementOrder` instead of introducing a second unrestricted allocator. A distinct manual kind, immutable destination/journal/actor/provenance linkage and carrier evidence linkage require an explicit migration after direct index/schema review.
- Manual command accepts operation ID, exact order IDs, currency, expected gross and selected destination. Tenant/actor are derived server-side. Under the canonical lifecycle transaction, verify eligible delivered manual COD, posted Dr1130/Cr4020 journals and whole-order unallocated amount, tenant currency, active mapped Asset Cashbox1010. Post dedicated Dr1010/Cr1130 through `FinancialPostingService` with source-owned inflow authority. Persist immutable batch/member/result lineage and return the same result for the same content/identity retry.
- Carrier command accepts a **provider evidence locator or persisted evidence ID**, never browser-authored verified remittance facts. A registered provider adapter obtains/verifies tenant-owned authenticated evidence and emits the canonical model; accounting consumes only that internal model. No real provider adapter is implementable from the currently discovered evidence contract alone.
- Canonical evidence contains tenant, normalized provider, external settlement ID, currency, gross, fees, net, remittance timestamp, complete order IDs/external matchable references, raw provider reference/metadata and verified authentication provenance/status. Store immutable economic facts and verification results; reject incomplete payloads, identity conflicts and net != gross-fees.
- Lock batch identity and sorted whole-order memberships, verify posted collection authority/provider/tenant/currency, and allocate once. Post Bank1030 net + ShippingFees6020 fees against Clearing1130 gross. Preserve zero-net arithmetic without zero-value posting lines.
- Prefer a single durable source/journal/state transaction where existing posting authority can join it. If the existing outbox pattern is retained, expose a separate pending/posted accounting result and certify crash recovery; do not label pending accounting as completed cash receipt.
- Strengthen permanent identity constraints and terminal immutability. Resolve concurrent same-identity requests to the same result; overlapping different identities must reject. Retry serialization/unique races by re-reading committed authority in a clean context.
- Keep generic Treasury transfer eligibility unchanged. Keep old caller-authored carrier/gateway POST containment and retired admin clearing containment intact unless a new route is explicitly backed by trusted evidence.
- Server policies: preserve existing payment-management authority for settlement mutation (Owner/Admin in current role grants); dedicated read permissions must be explicitly classified in `PosEndpointPermissionCatalog`. Do not grant mutation to a finance-only role by relying on the UI.
- UI: dedicated `/treasury/cod`-style domain screen with Manual Collections, Carrier Remittances, Settled COD; manual selection/total/Main Cashbox/journal preview/reference result; evidence provider/external ID/gross/fees/net/orders/verification/journal/errors. Translate English/Arabic and render pending/unavailable states honestly.
- Fixture adapter must be test-only, or a explicitly development-only, disposable-tenant-allowlisted evidence ingress with no production DI/route availability. It must exercise the same canonical verifier and ledger/lifecycle executor. Do not fabricate Bosta/Aramex/Mylerz authentication behavior.

### Required certification tests after prerequisites

Use relational PostgreSQL, real posting/resolver/seeder/reporting, separate request contexts and the same server permission pipeline. Test manual collection/settlement, trusted carrier collection/remittance, duplicate identity same/different facts, already-settled/uncollected/non-COD/nonmanual/wrong-provider orders, mixed tenants/currencies, tenant-currency mismatch, gross/negative-or-excess-fee/net mismatch, zero fees, zero net, incomplete membership payload, replay, out-of-order remittance and collection/delivery, overlapping concurrent requests, same-request concurrent retries, rollback/crash recovery, immutable economic records, ledger sums/account balances and exact state transitions. Include destination-aware refund regressions. Confirm denied roles cannot invoke financial authority.

Existing test project excludes most `Accounting/*.cs` and selectively re-includes supported files (`PointOfSale.Tests.csproj:38–48`). Test discovery must be checked rather than counting file names as execution. Existing FinancialPostingServiceTests/TreasuryTransferServiceTests are not currently compiled by that configuration.

## 2. Existing UI/API and permission inventory

| Existing route | Server/client authority | Behavior |
|---|---|---|
| GET `/api/payments/lifecycle/{orderId}/actions` | storefront.payments.view + controller role gate | Contextual actions; collection projection lacks ownership condition |
| POST `/api/payments/lifecycle/{orderId}/cod-delivered` | storefront.payments.manage + Owner/Admin/Manager class role gate; current grants exclude Manager from payments.manage | Explicit collection; gap C1 |
| POST `/api/payments/lifecycle/settlements/cod` | storefront.payments.manage + Owner/Admin | 409 SETTLEMENT_EVIDENCE_REQUIRED |
| POST `/api/payments/lifecycle/settlements/gateway` | storefront.payments.manage + Owner/Admin | 409 SETTLEMENT_EVIDENCE_REQUIRED |
| POST `/api/payments/lifecycle/{orderId}/refund` | storefront.payments.manage + Owner/Admin; actual action RecordRefund has no refunds.manage override | Canonical refund; gap C2 for a new manual destination |
| GET `/api/treasury/accounts`, `/balances`, `/balance/{accountCode}`, `/movements/{accountCode}`, `/dashboard` | finance.view | Tenant ledger-backed catalog/balances/movements |
| POST `/api/treasury/operations/transfer` | finance.operate + Treasury role restrictions | Active same-tenant mapped Cashbox/Bank Asset only |
| POST `/api/admin/accounting/treasury/transfer` | finance.admin (server convention write policy) | Legacy account-ID transfer, same type restrictions |
| POST `/api/admin/accounting/treasury/settle` | finance.admin (server convention write policy) | 410 SETTLEMENT_AUTHORITY_REQUIRED |
| GET `/api/financialreports/accounts` | reports.financial.view | All ledger account summaries, not period movements |
| GET `/api/financialreports/general-ledger/{accountId}?from=&to=` | reports.financial.view | Tenant-owned immutable journal lines |
| GET `/api/financialreports/trial-balance`, `/clearing-aging` | reports.financial.view | Official financial reads |

Permission convention adds server `AuthorizeFilter` policies; controller attributes alone are not the whole permission model. Owner has all permissions; Admin has all except finance.initialize and ownership transfer; Manager has finance.view/operate and payment view, **not** payment manage/finance.admin/reports.financial.view; Cashier/Warehouse have no Treasury financial mutation grant.

Existing client routes in `Web/src/App.jsx:519–534`:

- reports.financial.view: `/treasury`, `/treasury/trial-balance`, `/treasury/profit-loss`, `/treasury/balance-sheet`, `/treasury/cash-flow`, `/treasury/general-ledger`, `/treasury/clearing-aging`.
- finance.operate: `/treasury/transfers`.
- finance.view: `/treasury/hub`, `/treasury/account/:accountCode`.

`treasuryAccountCatalog.js` partitions exact Cashbox/Bank roles as available cash and Clearing as in transit. Transfer pages filter that catalog; no 1130 whitelist exists. Existing Arabic/English presentation describes settlement as unavailable. No active dedicated Manual Collections / Carrier Remittances / Settled COD screen or canonical remittance API was found.

## 3. MAIN runtime and direct database evidence

### Process and dependency identity

`Get-NetTCPConnection` / `Get-CimInstance Win32_Process` established:

| Listener | PID | Executable/command identity |
|---|---|---|
| localhost:5010 | 13548 | primary copy `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe` |
| localhost:5173 | 16784 | primary `PointOfSale.Web/node_modules/.../vite.js` |
| localhost:5174 | 7400 | primary `PointOfSale.Storefront/node_modules/.../vite.js` |

`GET /health` returned **503 Unhealthy**, trace `c106069c-2bfd-43e7-bdc1-e2884e2b8ade`, timestamp `2026-10-08T21:03:08.0667029Z`.

- PostgreSQL reachable, database `PosEcommerceDb_Baseline`, host `192.168.1.5:5432`.
- MassTransit ready; fulfillment-delivered and shipment-cod-collected endpoints registered/healthy.
- Accounting outbox healthy, CriticalPendingCount=0, RepeatedFailureCount=0, CriticalLagCount=0.
- COD reference workflow healthy **as disabled**: settlementExecutionEnabled=false, lifecycleMutationEnabled=false, evidenceAdmissionEnabled=false.
- `payout_external_truth_readiness` Unhealthy, latest reconciliation Critical with one mismatch. No claim that this is caused by COD; it is the aggregate health blocker.
- Some unrelated projection/latency/disabled optional checks Degraded.

`GET /health/live` returned **200 Healthy**, trace `6d130ad3-551a-4567-8d73-274174a98e11`, timestamp `2026-10-08T21:05:17.271863Z`. Liveness is not all-dependencies-ready certification.

### Read-only SQL results

Database `PosEcommerceDb_Baseline`; transaction_read_only=`on`. Tenant under audit: AhramStore `c9c5c1ae-949e-498e-bc63-07242daeb49d`, EGP. Existing probe commands:

```powershell
dotnet ".artifacts/cod-ledger-audit/bin/Debug/net9.0/Audit.dll"
dotnet ".artifacts/cod-ledger-audit/bin/Debug/net9.0/Audit.dll" ".artifacts/cod-ledger-audit/state.json"
```

| Account | Account role | Aggregate | Posted/reversed line net | Lines |
|---|---|---:|---:|---:|
| 1010 | Asset / Cashbox | 10.0000 | 10.0000 | 4 |
| 1030 | Asset / Bank | 0.0000 | 0 | 0 |
| 1110 | Asset / Clearing | 0.0000 | 0 | 0 |
| 1120 | Asset / Clearing | 0.0000 | 0 | 0 |
| 1130 | Asset / Clearing | 0.0000 | 0 | 0 |
| 1140 | Asset / Clearing | 0.0000 | 0 | 0 |

All six inspected mappings/accounts are active and nondeleted. COD payment mapping has ClearingAccountId pointing to 1130, null TargetAccountId, null FeesAccountId. COD clearing PreventNegative=false does not make it transfer-eligible.

| Measure | Observed |
|---|---:|
| Settlement batches | 0 |
| Settlement members | 0 |
| Accounting outbox rows | 0 |
| Journals / lines | 13 / 30 |
| COD or settlement or generic-transfer journals in inspected source set | 0 |
| Total debit / total credit | 3,880.0000 / 3,880.0000 EGP |
| Unbalanced journals | 0 |

Current COD orders:

| Order | Amount | Payment | Fulfillment | Journal |
|---|---|---|---|---|
| `a1f201e7-6124-4d63-bd88-fd2fe1cc8e88`, ORD-C9C5-2026-01039 | 560 EGP | Pending | Confirmed, not delivered | none |
| `5ca9b832-e702-43cb-aed1-587ed2b01b9f`, ORD-C9C5-2026-01037 | 560 EGP | Failed / order cancelled | Created, not delivered | none |

Neither is eligible for collection/settlement. No production data or existing order was mutated.

### Migration/model checks

Direct `__EFMigrationsHistory`: **45 applied**, latest `20261007190000_MAIN_HomepageMediaAssets`.

```powershell
dotnet ef migrations list --project "PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj" --startup-project "PointOfSale.API/PointOfSale.API.csproj" --no-build
dotnet ef migrations has-pending-model-changes --project "PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj" --startup-project "PointOfSale.API/PointOfSale.API.csproj" --no-build
```

Both succeeded: list has no pending markers; model check says **“No changes have been made to the model since the last migration.”** These checks use existing compiled artifacts. No backend source/model edits were made in this session. No migration was created/applied or rollback attempted.

## 4. Honest certification boundary

Certified here: source trace and specific read-only runtime/database observations. Not certified here: positive manual/carrier settlement, new provider evidence, concurrent successful allocation, destination-aware refunds, new permissions/UI, test-provider production exclusion, migration Up/Down, or full repository tests.

No new backend tests, frontend tests/build, or browser matrix were run during the stop-gated audit. Existing prior report results are not re-labelled as current certification.

**Required next action:** explicitly resolve C1 and C2 and authorize their bounded canonical collection/refund-lineage fixes as prerequisites to settlement implementation. Then implement and test the architecture in section G against disposable MAIN development fixtures. Until then, positive settlement must remain unavailable and the existing generic transfer boundary must remain intact.
