# Manual COD / Carrier Remittance Settlement — certification status

## Outcome

**BLOCKED BEFORE IMPLEMENTATION — NOT A POSITIVE SETTLEMENT CERTIFICATION.**

Session date: 2026-10-09 Windows local; actual runtime UTC observations dated 2026-10-08. MAIN working copy: `C:\Users\fadya\source\repos\PointOfSale`; HEAD `256339621c0ea49f3602097cdacdf89ffe71754f`.

The requested forensic audit is delivered in [COD_SETTLEMENT_PRE_IMPLEMENTATION_FORENSIC_AUDIT.md](COD_SETTLEMENT_PRE_IMPLEMENTATION_FORENSIC_AUDIT.md). Implementation did not begin because the user explicitly required stopping on contradictions with the assumed architecture.

### Blocking findings

**C1 — Carrier collection truth is not adequately source-separated.**

`PaymentLifecycleController.MarkCodDelivered:90–113` accepts authorized operator requests and calls `PaymentLifecycleService.MarkOfflineCollectionDeliveredAsync:308–324`. `HandleSuccessAsync:2407–2419` checks COD identity and delivered fulfillment, but no manual/carrier ownership or shipment collection-evidence condition. The full lifecycle implementation does not check `FulfillmentMode`, `FulfillmentCarrierCode`, `Shipment.CodCollected`, or a carrier collection-evidence identity. `GetAllowedActionsAsync:1492–1494` advertises the same undifferentiated eligibility.

The carrier event consumer calls this same method. Thus a delivered carrier order may acquire Collected/posting truth from an operator command without proven carrier collection provenance. This was proved by source tracing; no customer/carrier order was changed as an exploit test.

**C2 — Post-settlement refund authority is Bank-only.**

`CommerceAccountingService.ResolveEcommerceRefundCreditAccountIdAsync:1804–1830` unconditionally chooses Bank1030 for a valid post-settlement refund snapshot. `PaymentLifecycleService.ResolveRefundAccountingContextAsync:4898–4960` reads existing batch/member lineage, which has no explicit destination. A new manual Dr1010/Cr1130 settlement must therefore include destination-aware refund lineage/replay. Reusing existing Settled membership unchanged would retain Bank-funded refund accounting; omitting membership would break canonical settled refunds.

Neither finding justifies a generic Clearing transfer or a fabricated remittance provider. The bounded prerequisite fixes and smallest recommended settlement architecture are documented in section G of the forensic report.

## Exact files changed by this session

Only these two reports were added:

1. `.ai/reports/COD_SETTLEMENT_PRE_IMPLEMENTATION_FORENSIC_AUDIT.md`
2. `.ai/reports/COD_MANUAL_AND_CARRIER_SETTLEMENT_IMPLEMENTATION_CERTIFICATION.md`

All application/test/frontend changes that existed at entry were preserved and are explicitly inventoried in the forensic report. They are not credited to this task. No commit was made.

## Exact migrations and schema status

- New migrations: **none**.
- Applied migrations in this session: **none**.
- Direct MAIN history: **45 applied**; latest `20261007190000_MAIN_HomepageMediaAssets`.
- Existing `PaymentSettlementBatches` and `PaymentSettlementOrders` columns were read directly from MAIN. They already exist in `20260723214642_Baseline_Recovery3` and current EF configuration/snapshot.
- `dotnet ef migrations list ... --no-build`: succeeded, no pending marker.
- `dotnet ef migrations has-pending-model-changes ... --no-build`: succeeded, “No changes have been made to the model since the last migration.”
- Checks use existing compiled artifacts; backend model source was not edited.
- New schema/index/constraint Up/Down verification: **not applicable; no implementation**.
- Direct runtime index/trigger inventory: **not executed**. Existing uniqueness was inspected in EF and baseline migration source; it must be checked directly before any future schema change.

## Exact API/endpoints

New endpoints: **none**.

Relevant current contracts (source-verified, not new live financial POST tests):

| Endpoint | Current behavior |
|---|---|
| POST `/api/payments/lifecycle/{orderId}/cod-delivered` | Canonical collection operation; C1 applies |
| POST `/api/payments/lifecycle/settlements/cod` | 409 `SETTLEMENT_EVIDENCE_REQUIRED` |
| POST `/api/payments/lifecycle/settlements/gateway` | 409 `SETTLEMENT_EVIDENCE_REQUIRED` |
| POST `/api/admin/accounting/treasury/settle` | 410 `SETTLEMENT_AUTHORITY_REQUIRED` |
| POST `/api/treasury/operations/transfer` | Valid active same-tenant mapped Cashbox/Bank Assets only |
| GET `/api/treasury/accounts`, `/balances`, `/balance/{accountCode}`, `/movements/{accountCode}` | Ledger-backed Treasury reads |
| GET `/api/financialreports/accounts`, `/general-ledger/{accountId}` | Official tenant ledger account discovery and journal-line reports |

## Accounting entries and workflows

### Manual

Current valid manual workflow: admitted manual COD → valid evidence-backed manual delivery → explicit canonical collection → Dr1130 / Cr4020. Delivery alone does not collect.

Required future manual settlement: exact posted, collected manual COD order set → dedicated immutable operation → **Dr1010 Main Cashbox / Cr1130 COD Clearing**. It is not implemented. No cashbox settlement journal, reference or order state transition was generated in this session.

### Carrier

Normal current carrier flow: authenticated tracking/webhook truth → persisted shipment collection flag → `ShipmentCodCollectedEvent` → lifecycle → Dr1130 / Cr4020. C1 is an alternative operator ingress that must be closed before relying on carrier collection provenance.

Retained historical COD remittance journal shape:

```text
Dr 1030 Bank               net = gross - fees
Dr 6020 Shipping Fees      fees (if positive)
Cr 1130 COD Clearing       gross
```

This positive operation remains contained. No verified remittance evidence or resulting remittance journal was created. Zero-net handling remains a dormant code gap because the existing generator constructs a zero Bank debit when fees==gross.

## Provider adapter design

Delivered implementation: **none**.

Verified existing shipping adapter interface handles quote/create/cancel/tracking/credential validation/webhook normalization; it does not supply authenticated COD remittance facts or remittance identifiers.

Recommended boundary: `ICodRemittanceEvidenceProvider`-equivalent adapter obtains/verifies tenant-owned provider evidence and normalizes it into canonical immutable tenant/provider/external-ID/currency/gross/fees/net/date/order-reference/provenance truth. Accounting resolves only canonical verified evidence. No provider-specific assumption or production fixture shortcut was introduced. A deterministic provider belongs in tests or an explicitly development-only disposable-tenant ingress, exercising the same domain verifier/executor.

## Idempotency/concurrency strategy

Existing inspected mechanisms:

- Collection journal key `COD_DELIVERED:{tenant}:{order}`.
- Historical remittance journal key `COD_SETTLEMENT:{tenant}:{settlementId}`.
- Unique tenant/journal key, tenant/type/settlement ID, active tenant/order membership.
- Lifecycle Serializable default, sorted order locking through PostgreSQL `FOR UPDATE`, transaction/savepoint handling.
- Account `xmin` optimistic concurrency; ledger posting participates in current transaction or owns Serializable transaction.
- SQL-authority accounting outbox recovery.

New settlement concurrency or retry implementation: **none**. Filtered active membership uniqueness and source-level dormant matching are not claimed as a newly certified successful concurrent remittance workflow. Future boundary must prevent order overlap, preserve immutable identities across soft delete, retry/re-read cleanly after serialization/unique races and return existing success for identical retries.

## Permission model

Existing server convention/policies were inspected and left intact:

- Treasury reads: `finance.view`.
- Generic operations: `finance.operate` plus type restrictions.
- Retired admin clearing route: `finance.admin`.
- Payment settlement routes: `storefront.payments.manage` + Owner/Admin.
- Collection route: payment-management policy plus controller Owner/Admin/Manager role metadata; current Manager grants do not include payment manage.
- General Ledger/official reports: `reports.financial.view`.

New permission classification, mutation grant, server enforcement test or employee login matrix: **not implemented/executed**. Future dedicated mutation should preserve current payment-management authority rather than borrow a broader generic-transfer grant.

## Tests and live verification results

No new backend tests, frontend tests/build, fixture provider, disposable order scenario or browser matrix was executed. Prior audit test results were read as history only and are not claimed as this session's certification.

Executed non-mutating verification:

| Check | Result | Evidence |
|---|---|---|
| MAIN executable identity | Confirmed | API PID13548 from primary copy; Web PID16784/5173; Storefront PID7400/5174 |
| API liveness | 200 Healthy | GET `/health/live`, trace `6d130ad3-551a-4567-8d73-274174a98e11`, UTC `2026-10-08T21:05:17.271863Z` |
| Aggregate health | 503 Unhealthy | GET `/health`, trace `c106069c-2bfd-43e7-bdc1-e2884e2b8ade`; payout_external_truth_readiness Critical/1 mismatch |
| Database identity/read-only transaction | Confirmed | Existing Npgsql probe: PosEcommerceDb_Baseline; transaction_read_only=on |
| AhramStore balances | Cashbox10 / Bank0 / COD Clearing0 EGP | Same-tenant account aggregates equal inspected line net |
| General ledger balance | 0 unbalanced journals | 13journals/30lines; Dr3,880 = Cr3,880 EGP |
| Existing COD truth | No eligible collected orders | Inspected Pending/Confirmed order and Failed/Cancelled order; neither has a collection journal |
| Settlement authority rows | Empty | 0batches/0members/0accounting-outbox rows for inspected tenant |
| Migration history | Confirmed | 45 applied, latest MAIN_HomepageMediaAssets |
| Pending model check | Passed | Existing compiled EF model has no pending change |

No new General Ledger entries or Treasury balance movements were produced. The observed zero COD balance reconciles with the absence of COD collection journals in the inspected tenant; this is not proof of a newly executed settlement invariant. The read-only investigation did not access LOAD-TEST or modify Dell.

## Remaining limitations and uncertified areas

- C1/C2 prerequisite fixes await resolution of the user's explicit contradiction gate.
- Manual settlement and canonical carrier remittance domain/UI/API are not implemented.
- No real remittance provider contract/authenticated evidence was available or invented.
- No deterministic test-provider ingress or evidence/ledger/state-transition tests were implemented.
- Concurrent successful settlement, replay, out-of-order events, tenant/currency isolation, fee/net mismatches and permission enforcement for a new operation remain uncertified.
- No new migration was created/applied or validated.
- Aggregate MAIN health is not green; liveness and database reachability are green. No unsupported COD causation claim is made for the payout reconciliation failure.
- No full repository, accounting suite, frontend build or positive live financial E2E certification is claimed.

## Concise certification table

| Area | Status | Evidence | Notes |
|---|---|---|---|
| Forensic audit | COMPLETE | Pre-implementation report; source traces and read-only MAIN SQL | Required A–G sections delivered |
| Collection ownership | BLOCKER C1 | Operator/controller and carrier consumer share unqualified OfflineCollection success path | Carrier evidence must be source-separated |
| Manual settlement | NOT IMPLEMENTED | No dedicated executor/API/journal added | Must be Dr1010/Cr1130 under canonical authority |
| Settled refund lineage | BLOCKER C2 | Post-settlement refund always resolves Bank1030 | Actual destination must survive settlement/refund replay |
| Carrier remittance | NOT IMPLEMENTED | Old 409 containment retained; no provider evidence adapter | No settlement without authenticated canonical evidence |
| Generic transfers | SOURCE VERIFIED | Exact Cashbox/Bank guards in both services/client catalog | No Clearing exception introduced |
| Ledger/read balances | READ-ONLY VERIFIED | MAIN 13journals/30lines; Dr=Cr3,880; COD0/Cashbox10/Bank0 | Existing data only |
| Idempotency/concurrency | EXISTING MECHANISMS AUDITED | Keys, unique-index source, locking, xmin/outbox | New positive settlement concurrency not certified |
| Permissions | SOURCE AUDITED | Current permission catalog/convention/role grants | No new endpoint authority or tests |
| Database/model | READ-ONLY VERIFIED | Existing schema columns, 45 migrations, no pending compiled model changes | No new migration |
| Backend/frontend tests | NOT RUN | Stop gate reached before implementation | Prior results not reused as current certification |
| Live development health | PARTIAL | Liveness200; aggregate503 | Payout reconciliation readiness unhealthy |
| Positive live manual/carrier E2E | NOT RUN / NOT CERTIFIED | No order/journal mutation | Requires contradiction resolution and disposable fixture implementation |
