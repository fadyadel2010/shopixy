# COD settlement prerequisites — bounded remediation certification

## Final status

**PASS — prerequisites fully established and safe for the next design/implementation phase.**

**This is prerequisite certification only. Positive COD settlement remains disabled and is not certified.**

Date: 2026-10-09 Windows local; actual API/TRX UTC observations on 2026-10-08. Working copy: `C:\Users\fadya\source\repos\PointOfSale`, MAIN/PRIMARY only. Base HEAD: `256339621c0ea49f3602097cdacdf89ffe71754f`. No subagents or commit.

The two prior forensic reports were re-read before editing. This phase addresses only their collection-admission and refund-destination-lineage blockers. Existing uncommitted Treasury/General Ledger presentation work and its reports/tests were preserved.

No LOAD-TEST or Dell development copy was changed or targeted. MAIN's configured database/dependencies were accessed from this primary Windows copy only.

## A. Collection authority before / after

| Concern | Before | After |
|---|---|---|
| Operator command | Shared `MarkOfflineCollectionDeliveredAsync`; delivered carrier orders also admitted | Operator admission requires Manual Fulfillment and no carrier ownership snapshot |
| Carrier consumer | Called the operator/shared wrapper with event tracking/actor strings | Calls dedicated `RecordCarrierCodCollectionAsync(tenant, order, shipment)` |
| Carrier financial truth | Delivered order alone could qualify; no shipment collection check in lifecycle | Lifecycle re-reads tenant-owned shipment collection truth; validates order/provider/COD/amount/currency |
| Event payload amount/currency | Not independently checked as carrier collection authority | Consumer forwards locators only; lifecycle ignores request/provider amounts and derives money from SQL order truth |
| Replay | Successful terminal replay checked before ownership qualification | Admission ownership/evidence checked before terminal replay or repair; valid replay remains idempotent |
| Allowed actions | Pending + delivered COD, irrespective of fulfillment ownership | Operator collection advertised only for eligible manual COD |
| Financial state machine | `PaymentLifecycleService` | Same canonical owner and existing OfflineCollection state/event/journal path |

No second state machine or new carrier collection HTTP endpoint was introduced. `CarrierCollectionShipmentId` is an internal request locator, not a caller-authored proof or monetary fact.

## B. Exact manual / carrier admission rules

### Manual COD

Active operator route remains:

```text
POST /api/payments/lifecycle/{orderId}/cod-delivered
  → MarkOfflineCollectionDeliveredAsync
  → canonical OfflineCollection success
```

Admission now requires:

1. Current tenant owns a nondeleted COD order; existing tenant and SQL row-lock rules remain.
2. `FulfillmentMode == Manual` and `FulfillmentCarrierCode` is empty/null.
3. First collection requires Delivered fulfillment. Existing legitimate manual fulfillment/delivery evidence rules remain in their original fulfillment authority.
4. Financial amount is `order.Total`; currency is `order.Currency`. Optional request/provider amount/currency fields cannot override COD financial truth.
5. Existing valid successful-terminal replays retain idempotent behavior. The ownership gate also applies on replay, preventing an operator from using an already-collected carrier order as an alternate admission path.
6. Existing reservation/payment-state/collection outbox/posting rules remain.

Main ownership rejection: `COD_MANUAL_FULFILLMENT_REQUIRED`. Non-COD/deleted orders reject with `COD_ORDER_REQUIRED`; missing first-collection delivery rejects with `COD_DELIVERY_REQUIRED`.

### Carrier COD

Trusted application path remains the registered `ShipmentCodCollectedConsumer`, now calling:

```text
RecordCarrierCodCollectionAsync(tenantId, orderId, shipmentId)
  → canonical OfflineCollection success
  → SQL shipment authority qualification
```

Admission requires:

1. Nonempty explicit tenant authority and shipment locator; lifecycle rejects cross-tenant order authority.
2. `FulfillmentMode == Carrier` with a carrier ownership snapshot.
3. A nondeleted SQL shipment matching **shipment ID + order ID + tenant ID**.
4. Shipment is carrier-managed and its normalized carrier identity matches the order's immutable fulfillment carrier snapshot.
5. `IsCashOnDelivery`, `CodCollected`, and `CodCollectedAtUtc` are present/valid.
6. Persisted `Shipment.CodAmount == order.Total`; persisted shipment currency equals order currency.
7. Delivered order truth for first collection. Existing retry behavior permits collection events to be retried until valid order delivery truth exists.
8. Tracking/reference and carrier actor are reconstructed from the shipment; event actor, event amount/currency and similar caller facts are not forwarded as financial authority.

Rejections include `COD_CARRIER_FULFILLMENT_REQUIRED`, `COD_CARRIER_EVIDENCE_REQUIRED`, and `COD_CARRIER_EVIDENCE_MISMATCH`.

The existing shipping authority that writes collection flags from trusted tracking/webhooks was not changed. No fake provider assertion, remittance source, signature rule or production fixture adapter was added. Shipment documentation was corrected to distinguish customer collection from merchant remittance.

### Permissions

The existing server policy convention remains authoritative: `PaymentLifecycle.MarkCodDelivered` requires `storefront.payments.manage` and existing controller role metadata. Canonical role grants allow Owner/Admin and deny Manager/Cashier/Warehouse for this mutation. Five-role handler regression passes; a live cookie-free request also rejects before collection authority with 400 `TENANT_REQUIRED`.

No permission grant or policy was weakened. Separate employee browser logins were not performed; the server role-policy test and live Owner/unauthenticated probes are the precise evidence.

## C. Settlement / refund destination lineage before / after

### Reused persisted model — no new field required

Inspection found that the domain already persists the actual receiving account through:

```text
Order
  → PaymentSettlementOrders (tenant/order/batch membership)
  → PaymentSettlementBatches (tenant, identity, gross/fees/currency/date)
  → LedgerJournalEntries (tenant, SourceId=batch.Id, SourceReference=settlement ID)
  → LedgerLines.AccountId (the actual destination debit)
```

Previously `ResolveEcommerceRefundCreditAccountIdAsync` ignored those actual journal destinations and always selected Bank1030 after settlement.

New read-only `SettlementRefundLineage.ResolveDestinationAccountAsync` uses the existing history rather than adding a nullable destination field that could duplicate or disagree with posted ledger truth. No “Manual →1010 / Carrier →1030” destination rule exists.

It independently re-reads current SQL through no-tracking, explicitly tenant-qualified order/batch/journal/account queries. It verifies:

- Refund context identity/type/reference/date/payout fields match the current batch.
- Order, batch and current tenant currencies agree.
- Membership has the recorded count, no duplicate orders, no foreign-tenant or mixed-currency member, matching whole-order amount for the target order, and totals equal batch gross.
- Exactly one matching posted, nondeleted journal exists, on the settlement date, with the expected settlement source contract.
- Lines belong to the same tenant/journal, have valid positive single-sided amounts, and total debit = total credit = gross.
- One gross clearing credit exists.
- Exactly one receiving debit is an active same-tenant mapped non-clearing Cashbox/Bank Asset and equals net = gross − fees; remaining debit legs are expenses.

It returns that journal line's actual AccountId. Missing, reversed, ambiguous, inconsistent, foreign or unposted history fails closed with `SETTLEMENT_DESTINATION_LINEAGE_REQUIRED`; it never guesses Bank or Cashbox.

Existing lineage source contracts are OFFLINE_COLLECTION/CODSettlement and GATEWAY/GatewaySettlement. The reserved **read-only** MANUAL_COD_COLLECTION/ManualCodSettlement contract permits testing a future manual history model. It has no executor, endpoint, feature flag or cashbox inflow authority. A manual-model test whose actual historical debit is Bank1030 correctly refunds Bank1030, proving that settlement kind does not choose the account.

### Enforcement points

1. `PaymentLifecycleService.ResolveRefundAccountingContextAsync` validates actual destination lineage before accepting/stamping a post-settlement refund.
2. `CommerceAccountingService` re-resolves it from current SQL when generating/refetching the refund journal, including accounting recovery. Production DI supplies ApplicationDbContext; construction without SQL authority fails closed for post-settlement refunds.
3. `FinancialPostingService` binds the cash outflow to the exact persisted refund identity/amount and re-resolves its settlement destination before posting. It retains the cumulative refund cap and existing journal idempotency.
4. `ApplicationDbContext` now prohibits updating/deleting settlement headers, membership and ledger lines. Existing journal financial-identity guards and account classification/balance authority remain. Accounting retries operate on the outbox/journal rather than editing the original settlement facts.

Refund snapshots continue using their existing immutable settlement batch/type/reference/date fields; the destination is re-derived from their immutable posted history. No additional request-authored cached destination can override that history.

### Demonstrated accounting behavior in isolated fixtures

| Synthetic pre-existing history | Valid refund | Verified result |
|---|---|---|
| Historical OFFLINE_COLLECTION to Bank, gross100/fees2/net98 | Dr4020 25 / Cr actual Bank1030 25 | Bank73; Clearing0; one refund journal |
| Historical GATEWAY to Bank, gross100/fees2/net98 | Dr4020 25 / Cr actual Bank1030 25 | Same Bank destination retained |
| Future manual history model to Cashbox1010, gross100/fees0 | Dr4020 25 / Cr actual Cashbox1010 25 | Cashbox75; Bank0; Clearing0 |
| Future manual history model whose actual destination is Bank1030 | Dr4020 25 / Cr actual Bank1030 25 | No manual→cashbox hard-code |

Historical fixture journals are **test-only seeded models** in managed isolated stores; no settlement executor or external provider was invoked. Real refund posting, ledger balance updates, lifecycle transitions and idempotency are exercised against PostgreSQL. No real settlement journal was executed or admitted into MAIN shared accounting.

## D. Schema / migrations

**New migration: none. Applied migration in this phase: none. EF schema/model changes: none.**

The existing posted account reference is sufficient persisted lineage. Direct MAIN inspection verified:

- `LedgerJournalEntries.SourceId`, SourceType and required tenant; nullable SourceReference as before.
- `LedgerLines.AccountId`, JournalEntryId and TenantId are NOT NULL; no new defaults/nullability introduced.
- Existing source lookup index `IX_LedgerJournal_Source`.
- Unique tenant/journal idempotency and number indexes.
- Existing filtered unique tenant/type/settlement-ID and tenant/order-membership indexes.
- LedgerLines→LedgerAccounts RESTRICT FK, LedgerLines→JournalEntries FK, membership→batch/order/payment-intent FKs.
- Existing ledger account tenant/code and tenant/ID alternate-key indexes.

These inspected historical FKs are ID-based rather than composite tenant FKs; new read authority checks tenant consistency at each relationship and rejects mixed-tenant membership/lines/accounts. It does not rely on a join's account ID alone to prove tenant ownership.

Direct schema trigger inventory returned no non-internal triggers on the four inspected lineage tables. Immutability certification here is through the application guards and immutable journal contract; no direct privileged-SQL mutation or DB-trigger guarantee is claimed.

MAIN history remains **45 applied migrations**, latest `20261007190000_MAIN_HomepageMediaAssets`. Current rebuilt EF checks:

```powershell
dotnet ef migrations list --project "PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj" --startup-project "PointOfSale.API/PointOfSale.API.csproj" --no-build
dotnet ef migrations has-pending-model-changes --project "PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj" --startup-project "PointOfSale.API/PointOfSale.API.csproj" --no-build
```

Both passed, no pending marker and **“No changes have been made to the model since the last migration.”** All MAIN settlement tables were empty before this phase and remain empty after. No historical row was reclassified/backfilled or accounting balance repaired.

## E. Exact files changed by this bounded phase

### Application

1. `PointOfSale.Core/Entities/Shipment.cs` — collection/remittance documentation distinction.
2. `PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs` — manual command contract, distinct carrier command, internal shipment locator.
3. `PointOfSale.Infrastructure/Consumers/ShipmentCodCollectedConsumer.cs` — trusted carrier admission with locators only.
4. `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs` — pre-replay collection authority gates, manual allowed-action rule, authoritative amounts, refund lineage qualification.
5. `PointOfSale.Infrastructure/Accounting/SettlementRefundLineage.cs` — new read-only SQL destination authority.
6. `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs` — actual historical destination for post-settlement refund journal generation/recovery.
7. `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs` — exact refund identity/amount/destination cash-outflow authority.
8. `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` — immutable settlement/header/member/ledger-line guards.

### Tests

9. `PointOfSale.Tests/Architecture/PaymentLifecycleTests.cs` — carrier authority bridge assertion follows the distinct method.
10. `PointOfSale.Tests/Infrastructure/PaymentCancellationOutboxOperationalScalabilityTests.cs` — interface-compatible test stub only.
11. `PointOfSale.Tests/Infrastructure/ShipmentCodCollectedConsumerTests.cs` — distinct admission and untrusted payload-money regression.
12. `PointOfSale.Tests/Infrastructure/SettlementHistoryFixture.cs` — new test-only synthetic historical ledger fixture, no settlement executor/provider.
13. `PointOfSale.Tests/Integration/CodPaymentLifecycleTests.cs` — ownership/evidence/replay/tenant/money collection regressions added to existing relational suite.
14. `PointOfSale.Tests/Integration/EcommerceRefundLifecycleTests.cs` — existing settled-refund fixture now supplies actual posted historical lineage; SQLite account-xmin compatibility for that fixture.
15. `PointOfSale.Tests/Integration/CodSettlementPrerequisiteLineageTests.cs` — new PostgreSQL destination/refund/immutability/concurrency regressions.
16. `PointOfSale.Tests/Security/PosPermissionAuthorizationTests.cs` — five-role operator collection server-policy regression.

### Documentation / local evidence

17. `.ai/reports/COD_SETTLEMENT_PREREQUISITES_CERTIFICATION.md` — this report.
18. `.artifacts/cod-prerequisites/.gitignore` — keeps local logs/probes/build/TRX evidence out of versioned changes.

Local ignored evidence/utilities include `schema.json`, `LiveFixture.csproj`, `Program.cs`, `live-probes.js`, `prerequisites.trx`, `prerequisites-final.trx`, `authority-bridge.trx`, and temporary API stdout/stderr logs. They contain no credentials in their source; local settings supply credentials at execution.

Pre-existing modified Treasury UI/locales, Treasury architecture/containment tests and earlier reports/browser script are not this phase's implementation. No controller, generic Treasury transfer service, carrier adapter, settlement foundation configuration/flag, or frontend production file was changed by this phase.

## F. Tests / build results

Final selected backend slice:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --no-build --no-restore --filter "FullyQualifiedName~CodPaymentLifecycleTests|FullyQualifiedName~CodSettlementPrerequisiteLineageTests|FullyQualifiedName~ShipmentCodCollectedConsumerTests|FullyQualifiedName~EcommerceRefundLifecycleTests|FullyQualifiedName~PaymentLifecycleRefundAuthorityTests|FullyQualifiedName~T10SettlementContainmentTests|FullyQualifiedName~PosPermissionAuthorizationTests|FullyQualifiedName~CommerceAccountingServiceTests|FullyQualifiedName~FinancialReportServiceTests|FullyQualifiedName~AccountingOutboxProcessorTests" --logger "trx;LogFileName=prerequisites-final.trx" --results-directory ".artifacts/cod-prerequisites"
```

**219 passed, 0 failed, 0 skipped**, duration 3m26s. TRX counters inspected directly.

Changed carrier-bridge architecture assertion separately: **1 passed** (`authority-bridge.trx`). Total final unique checks: **220 passed**. This is a focused certification, not a full repository certification.

| Required regression | Evidence / result |
|---|---|
| Manual COD + manual fulfillment allowed | Existing manual delivered collection and real-posting tests passed |
| Operator COD + carrier fulfillment rejected | New Pending and Collected/replay cases passed; live reject probe below |
| Carrier without trusted collection truth rejected | Missing/uncollected/wrong-provider/wrong-amount/wrong-currency/foreign-shipment/foreign-tenant/not-delivered cases passed |
| Valid carrier truth remains valid | Real PostgreSQL collection journal and balances; canonical carrier admission replay passed |
| Duplicate collection one event/journal | Both manual real-posting and new carrier test; Dr1130=Cr4020=100; Cashbox/Bank unchanged |
| Payload is not monetary authority | Consumer forwards locators; request999/USD against persisted100/EGP produces one100/EGP collection |
| Wrong tenant | Collection and refund destination reader reject foreign tenant authority |
| Unauthorized actor | Owner/Admin allowed; Manager/Cashier/Warehouse denied by actual permission handler; live unauthenticated rejects |
| Historical Bank refund | OFFLINE_COLLECTION and GATEWAY histories resolve to their Bank debit; existing SQLite settled-refund lifecycle also passed |
| Future manual destination model | Cashbox history refunds Cashbox; manual-model Bank history refunds Bank |
| Missing/ambiguous/reversed/mismatched history | Fail-closed reader tests and batch-without-posted-history test passed; no refund journal mutation |
| Stale tracked state | SQL no-tracking reader ignores unpersisted changed batch identity; persistence rejects mutation |
| Immutable lineage | Settlement edit, membership soft-delete and ledger-line account edit rejected |
| Concurrent refund/retry | Separate PostgreSQL contexts; one refund row/journal, Cashbox75 after one25 refund; subsequent retry converges |
| Ledger and recovery preservation | Commerce accounting, financial reports and accounting outbox suites passed |
| Settlement containment | T10 tests passed; current live 409/410/400 results below |

API/Core/Infrastructure/Tests build succeeded with **0 errors**; final test project build reported 256 existing analyzer/compatibility warnings. The initial build hit Windows DLL locks from the identified MAIN API. That process was stopped for rebuilding; the user restarted MAIN on5010. Its primary-copy executable PID15304 was verified before live probes.

Diagnostic fixture corrections during verification:

- Two initial test invocations timed out before a completed report. The new fixture originally used standalone IAsyncDisposable, which xUnit v2 did not dispose; it now follows the repository's IDisposable/TestDatabaseScope pattern. No production code was changed to solve this.
- A SQLite historical fixture required the account xmin default already used for other SQLite concurrency fixtures; corrected in test model only.
- First completed run: 97passed/1failed; the reversed-history fixture referenced an unassigned EF journal ID. Test-only insertion order corrected. Final run above is fully green.
- A browser cookie-free fetch was CORS-blocked; it was repeated through a Playwright request with an empty Cookie header. The resulting server rejection is recorded below.

Frontend production source was not changed by this phase; no new frontend build/browser-layout matrix or frontend tests are claimed. Existing presentation work was not recertified.

## G. MAIN live verification

### Runtime

- API primary-copy process PID15304, port5010.
- Reused registered headed Edge session `opencode-ui-test`; no new browser profile, worker or provider execution.
- Existing tenant-bound Owner session used; no credentials persisted in reports/probes.
- `GET /health/live`: **200 Healthy**, UTC `2026-10-08T21:53:51.8045938Z`, trace `0bd4f2fe-33d4-4b93-81e9-70251df6e10e`.
- `GET /health`: **503 Unhealthy**, UTC `2026-10-08T21:56:54.8824123Z`; `payout_external_truth_readiness` remains the unhealthy check. This was present before remediation and is not claimed fixed or caused by COD.
- COD foundation remains disabled; runtimeExecutionEnabled, evidenceAdmissionEnabled, lifecycleMutationEnabled and settlementExecutionEnabled all false.

### Disposable negative-only fixture

Created one new isolated non-financial order fixture in the known MAIN development AhramStore tenant:

- ID `a49869d1-cbfb-4a62-9a96-1e815b6b276c`.
- Marker `COD-PREREQUISITE-REJECT-ONLY-20261009`.
- Carrier ownership / Delivered state solely for rejection qualification; Pending COD1EGP, not paid, no collection assertion.
- No customer/contact data, shipment, remittance evidence, batch, journal or refund.
- No carrier API/webhook was called.

The fixture's exact ID/marker/tenant/Pending/unpaid state was checked before cleanup, and cleanup refused if it had gained journal, settlement, refund or shipment lineage. Cleanup returned **remaining=0**. Existing customer/order rows were not mutated. The final read-only order query again returns only the original two AhramStore COD orders.

### Reject-only HTTP evidence

| Probe | Result | Trace |
|---|---|---|
| Operator collection on disposable delivered carrier order | 400 `COD_MANUAL_FULFILLMENT_REQUIRED` | `67863cc0-19d2-4056-af4f-441053e43cd7` |
| Allowed actions for that order | 200; `availableActions=[]` | No operator collection advertised |
| Two simultaneous COD settlement requests | Both409 `SETTLEMENT_EVIDENCE_REQUIRED` | `0930e589-83d2-41a1-96c1-005a5aaea56f`, `36ec1ae9-be9c-4465-8716-6e58b81440f5` |
| Gateway settlement | 409 `SETTLEMENT_EVIDENCE_REQUIRED` | `416d5030-0967-4b13-b0fe-791ea506dce0` |
| Retired admin clearing settlement | 410 `SETTLEMENT_AUTHORITY_REQUIRED` | `f2b55331-0222-4838-96cf-002fa83925d0` |
| Generic1130→1010 Treasury transfer | 400; Cashbox/Bank authority required | `645cc41b-a5c8-4915-8665-f796e309610b` |
| Cookie-free collection request | 400 `TENANT_REQUIRED` | No authenticated tenant; no mutation |

Live refund lineage was exercised only in disposable relational test databases through the actual lifecycle/composer/posting services. No shared AhramStore refund or positive carrier collection was executed.

### Before / after read-only SQL and Treasury API

Database `PosEcommerceDb_Baseline`; SELECT probe transaction read-only=on. Tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`:

| Measure | Before | After |
|---|---:|---:|
| 1130 COD Clearing | 0.0000 EGP | 0.0000 EGP |
| 1010 Main Cashbox | 10.0000 EGP | 10.0000 EGP |
| 1030 Bank | 0.0000 EGP | 0.0000 EGP |
| Journals | 13 | 13 |
| Ledger lines | 30 | 30 |
| Settlement batches / members | 0 / 0 | 0 / 0 |
| Accounting outbox rows | 0 | 0 |
| Total debit / credit | 3,880 / 3,880 EGP | 3,880 / 3,880 EGP |
| Unbalanced journals | 0 | 0 |
| Applied migrations | 45 | 45 |

Browser API balances before/after probes match this table. SQL account aggregates equal line net. No settlement or other financial balance movement occurred in shared AhramStore.

## H. Proof settlement remains disabled

1. Live COD/gateway settlement still409; retired admin clearing settlement still410; generic Clearing transfer still400.
2. Foundation health proves settlementExecutionEnabled=false.
3. T10 containment tests, accounting/outbox/report regressions and changed carrier bridge assertion pass.
4. Controllers, generic Treasury services and settlement foundation configuration were not edited.
5. The new lineage reader is a read operation. The reserved future manual source is not added to `FinancialPostingService` cash inflow authority and has no settlement executor.
6. Shared settlement batches/journals remain zero before/after. Test fixtures seed synthetic history models only, never run a settlement provider/executor.

## I. Remaining contradictions / honest limitations

- The two prerequisite blockers are removed at the application authority boundary. No new contradiction requiring stop remains within this bounded remediation.
- Overall MAIN readiness is not green because of the existing payout external-truth reconciliation check. Prerequisite PASS is not deployment/full-system readiness certification.
- Real carrier remittance evidence remains unavailable. No live external provider collection/remittance authenticity was tested; collection integration still uses its existing trusted shipping-state owner.
- Zero-net settlements have no unique positive receiving debit for this whole-order refund lineage contract; they fail closed. Existing zero-net settlement generation remains dormant and requires explicit design in a future settlement phase.
- Split receiving destinations, partial settlement allocation and settlement reversal/reconciliation lifecycles remain unsupported and fail closed rather than selecting an arbitrary account.
- No direct database-admin/raw-SQL immutability guarantee or full employee-login matrix is claimed.
- No full repository/frontend suite or positive settlement E2E certification is claimed.

## J. Exact prerequisites still required before positive settlement implementation

Positive settlement requires a separate user-authorized design/implementation phase covering:

1. Canonical authenticated carrier remittance evidence ingress/provider adapter and immutable external identity/provenance/membership verification.
2. Dedicated manual COD settlement operation, source-owned accounting inflow authority, exact posted collection/order allocation, actor/audit and journal result linkage.
3. Transactional/concurrency/idempotency design for initial settlement execution, including permanent allocation/external-ID protection, crash recovery and pending-accounting visibility.
4. Explicit zero-net/refund/reversal/partial-or-split unsupported-case policy; preserve this fail-closed destination contract.
5. Dedicated bilingual UI/API and explicit existing server permission classification, without using generic Clearing transfers.
6. Production-inaccessible deterministic evidence fixtures and positive relational/live disposable settlement tests for both workflows.
7. New schema changes only if that future domain needs them, with directly verified MAIN migration/schema compatibility.

**STOP GATE OBSERVED: this remediation ends here. No positive settlement implementation or ingress enablement follows automatically.**

| Area | Status | Evidence | Notes |
|---|---|---|---|
| Manual collection authority | PASS | Manual relational tests; carrier operator rejection live400 | Single canonical lifecycle |
| Carrier collection authority | PASS | SQL shipment qualification; valid/missing/mismatched/replay regressions | Payload money is not authority |
| Refund destination lineage | PASS | Historical Bank and future-model Cashbox/Bank real refund tests | Destination derived from actual posted account |
| Tenant/currency/immutable history | PASS | Explicit relationship checks and rejection/immutability tests | No account-code destination guess |
| Refund concurrency/idempotency | PASS | Separate PostgreSQL contexts; one journal/row on concurrent retry | Existing ledger/lifecycle mechanisms preserved |
| Schema/model | PASS | Direct indexes/FKs/nullability; EF no pending change | No migration required |
| Focused tests | PASS | 219 + 1 unique final checks | Not repository-wide certification |
| MAIN accounting safety | PASS | Exact before/after balances/counts; fixture remaining0 | No shared financial movement |
| Settlement containment | PASS | Live409/410/400 and disabled foundation flag | Positive settlement remains unavailable |
| Overall development readiness | EXISTING LIMITATION | Liveness200 / aggregate503 | Payout reconciliation check remains unhealthy |
