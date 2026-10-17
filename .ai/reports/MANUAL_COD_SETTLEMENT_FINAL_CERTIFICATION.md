# Manual COD Settlement — final implementation certification

## 1. Executive verdict

**Final certification status: PASS**

The positive **Manual COD Settlement** workflow is implemented and certified within this bounded phase. This is **not a whole-repository production certification**.

- Working copy: `C:\Users\fadya\source\repos\PointOfSale`, MAIN/PRIMARY Windows only.
- Session date: 2026-10-09 local. Actual live evidence below is dated 2026-10-08 UTC.
- MAIN API listener verified as PID `15248`, primary-copy `PointOfSale.API.exe`, port `5010`, after the user restarted it.
- No subagents, commit, LOAD-TEST access, Dell changes, carrier remittance adapter, remittance evidence admission, or payout-readiness modification.
- All three requested authoritative reports were read completely before application edits. Existing prerequisite and Treasury/General Ledger work was preserved.
- Final focused backend run: **266 passed, 0 failed, 0 skipped**. Frontend selected run: **11 passed, 0 failed**. Live positive collection/settlement/refund, lost-response recovery, role policies, ledger reads, and disposable financial-data cleanup succeeded.

## 2. Exact business contract

An authenticated merchant financial operator settles a duplicate-free set of **whole Manual Fulfillment COD orders whose customer cash was already collected and whose collection journal is Posted**.

Domain identity:

```text
Batch.SettlementType = MANUAL_COD_COLLECTION
Journal.SourceType  = ManualCodSettlement
Operation identity  = normalized nonempty SettlementId GUID
IdempotencyKey      = COD_MANUAL_SETTLEMENT:{TenantId}:{SettlementId}
```

Maximum selection is 100 orders per operation. The server resolves tenant, actor, currency, whole-order amounts, current collection authority, prior allocations, destination mapping, and actual journal result. Manual fees are zero. Partial, split-destination, caller-priced, Bank-destination, and carrier orders are not admitted.

## 3. Collection authority and forensic pre-check

Canonical mutation owner remains `PaymentLifecycleService : IPaymentLifecycleService`. Its existing lifecycle transaction and `PaymentStatusSaveChangesInterceptor` authorization scope own the Collected/Settled transitions.

Manual collection remains the prerequisite-certified route:

```text
POST /api/payments/lifecycle/{orderId}/cod-delivered
  → MarkOfflineCollectionDeliveredAsync
  → SQL-qualified Manual fulfillment admission
  → canonical OfflineCollection success
  → collection accounting/outbox
```

The prerequisite separates `RecordCarrierCodCollectionAsync` and requires SQL shipment collection truth. This phase did not change that authority or add a carrier settlement ingress.

Re-inspected infrastructure and reuse decisions:

| Concern | Current authority / implementation decision |
|---|---|
| Collected money | SQL order payment state plus exact Posted `Order_COD` journal; never delivery alone |
| Batch/header | Existing `PaymentSettlementBatch`: tenant, type/ID, gross, zero fees, currency, count, timestamp, `CreatedBy` |
| Whole-order allocations | Existing `PaymentSettlementOrder`: tenant/order/batch/whole amount/currency; shared cross-workflow allocation index |
| Journal / balance writer | Existing `CommerceAccountingService` → `FinancialPostingService`; no direct Cashbox balance assignment |
| Permanent receiving account | Existing immutable `LedgerLines.AccountId` on the batch-owned posted journal |
| Refund destination | Existing `SettlementRefundLineage`; no new destination cache or Manual→1010 refund rule |
| Transaction | Existing lifecycle Serializable scope, sorted order row locks, posting-engine transaction participation, account xmin concurrency |
| Event delivery | Existing `IEventPublisher` and MAIN MassTransit transactional outbox; existing payment-status event fanout |
| Accounting receipt / recovery | Existing `AccountingOutboxMessage`, recorded Processed with its journal in the same transaction; source-specific recovery only acknowledges verified posted history |
| Treasury authority | Active default tenant Cashbox mapping, linked active non-clearing Asset ledger account, constrained to Main Cashbox1010 |
| Authorization | Existing `storefront.payments.manage`, existing Owner/Admin canonical grants, server controller/convention policies |
| Disabled ingress | Old COD/gateway settlement409, retired admin410, generic Clearing transfer rejection retained |

**Internal implementation note:** no new settlement entity/table, audit framework, mutable destination snapshot, pending-settlement state machine, or generic transfer exception was necessary. Existing header/member audit fields and immutable posted journal supply the domain identity and terminal result. A two-index migration was necessary to make settlement identities and allocations permanent even across soft deletion. The dedicated Manual operation posts synchronously in the retained transaction rather than adopting the dormant carrier coordinator's post-commit best-effort accounting semantics.

## 4. Exact eligibility rules

Inside the settlement transaction, authoritative orders are sorted, row-locked through the existing lifecycle loader, and reloaded. Admission requires:

1. Nonempty current tenant and current active same-tenant authorized operator.
2. Nonempty GUID operation ID; 1–100 distinct, nonempty order IDs.
3. All selected orders exist in that tenant and are not deleted.
4. COD payment-method identity.
5. `FulfillmentMode.Manual`, with no carrier ownership code.
6. `PaymentStatus.Collected`, `IsPaid`, and `PaidAt` present.
7. Delivered fulfillment; order not Cancelled, Refunded, or OnHold.
8. `FinancialPostingStatus.Posted` and persisted collection-journal locator present.
9. Exact same-tenant nondeleted Posted journal with key `COD_DELIVERED:{tenant}:{order}`, source `Order_COD`, source ID=order ID, source reference=order reference.
10. Exactly two valid same-tenant collection lines: Dr actual COD Clearing1130 and Cr E-commerce Sales4020, each equal to SQL `order.Total`.
11. No prior settlement allocation, including soft-deleted membership.
12. No persisted refund row for the selected orders; refunded/residual balances cannot be settled as whole original orders.
13. Each order currency matches the active tenant ledger currency. Currency reference precision must be supported by the existing two-decimal settlement schema.
14. Each authoritative amount is positive, representable at two decimals, below numeric(18,2) capacity; sum also within capacity.
15. Valid fixed Main Cashbox mapping and valid active Asset COD Clearing account.

The eligible-order projection repeats these financial qualifications and excludes invalid rows. Selection is advisory: POST always revalidates SQL truth. Caller amounts are not financial authority because the dedicated API does not accept them at all.

## 5. Exact API

Dedicated routes implemented in the existing `PaymentLifecycleController`:

| Method / route | Purpose |
|---|---|
| GET `/api/payments/cod/manual-settlements/eligible-orders?page=1` | Tenant-owned eligible selection, currency and fixed destination; pages of at most100 |
| POST `/api/payments/cod/manual-settlements` | Atomic whole-order Manual COD settlement |
| GET `/api/payments/cod/manual-settlements/{settlementId}` | Fresh durable result read for reload/recovery;404 if not found in current tenant |

Minimal POST:

```json
{
  "settlementId": "d77f1029-2176-4192-bbc8-78b7068da924",
  "orderIds": ["e5f8d3b1-a683-4084-83ca-2a05c88965e9"]
}
```

`JsonUnmappedMemberHandling.Disallow` rejects attempted amount, gross, currency, destination, tenant, actor, provider, or other extra facts. Live amount/destination/currency tampering returned400 before lifecycle mutation. Domain conflicts return409 with a stable code. The existing `FinancialWrite` rate limiter and CSRF/session pipeline apply.

Stable successful result contains batch ID, operation ID, `status=Settled`, count, authoritative gross/currency, exact receiving ledger account ID/code/name, journal ID/reference, `accountingOutcome=Posted`, and replay flag. No success is returned for a pending/absent journal.

## 6. Exact UI route and behavior

`/treasury/manual-cod-settlements`, permission-guarded by `storefront.payments.manage`, linked from Clearing Aging only for that permission.

`TreasuryManualCodSettlement.jsx` shows whole-order references/amounts, Collected+Posted eligibility, selected total/currency, **fixed Main Cashbox1010**, and Dr Cashbox / Cr COD Clearing preview. Review precedes explicit confirmation. There is no destination selector or carrier remittance control. Settled orders disappear from eligibility.

The exact in-flight operation request is stored in tenant/user-scoped session storage **before sending**. Duplicate-click protection uses an immediate ref. Unknown/failed responses retain the same request; retry never generates a new operation ID. Reload uses the dedicated GET to restore an existing posted result. English/Arabic, LTR/RTL, desktop1440 and mobile390 were exercised in both eligible and posted states: **8/8 layout-state checks**, no page-level overflow after layout transitions.

## 7. Exact permission / actor authority

`PosEndpointPermissionCatalog` explicitly classifies both dedicated GET actions as `storefront.payments.manage`; POST inherits the controller's payment mutation rule. All three dedicated actions also require existing Owner/Admin role metadata. Authenticated claims supply the user GUID; caller display names are not used.

SQL qualifications additionally require an active nondeleted same-tenant Owner/Admin operator. No canonical permission grant was added or broadened.

Live synthetic normal-login role matrix:

| Role | Eligibility GET | Settlement POST replay |
|---|---:|---:|
| Owner | 200 | 200 |
| Admin | 200 | 200 |
| Manager | 403 | 403 |
| Cashier | 403 | 403 |
| Warehouse | 403 | 403 |

Role handler tests and actual lifecycle actor tests also pass. Cookie-free Manual settlement returned400 `TENANT_REQUIRED` before financial authority.

## 8. Exact accounting entries

```text
Existing collection of G:
    Dr 1130 COD Clearing        G
    Cr 4020 E-commerce Sales    G

Dedicated Manual settlement:
    Dr actual Main Cashbox1010 G
    Cr actual COD Clearing1130 G
    Fees = 0
    Debit = Credit = G
```

The settlement composer and posting-engine source validation independently reload SQL batch, memberships, collection journals, currency, actor and fixed mapping. Even an attempted substituted non-cash account is subject to dedicated-source validation; a zero mapped-cash inflow cannot evade it.

## 9. Exact destination authority and persisted lineage

Resolve exactly one active nondeleted default same-tenant `TreasuryAccount.Type=Cashbox`. Validate its linked ledger account is active, nondeleted, same tenant, financial `Asset`, non-clearing, and code1010. Account name is display only. Requests cannot choose Bank/Clearing.

Posting uses this account's **ID**. The exact receiving ID is permanently stored in the posted settlement debit's `LedgerLines.AccountId`. Result and refund destination reads resolve that actual posted debit. There is no independent destination field that can disagree with accounting history.

## 10. Exact idempotency design

- GUID normalized with `ToString("D")`.
- Permanent unique SQL batch index on `(TenantId, SettlementType, SettlementId)`.
- Permanent unique allocation index on `(TenantId, OrderId)` across all settlement types.
- Existing permanent unique journal index on `(TenantId, IdempotencyKey)`.
- Key: `COD_MANUAL_SETTLEMENT:{tenant}:{settlementId}`.
- Same operation and order set returns the original SQL result/journal. Order ordering is not a financial difference.
- Changed order set under the same operation returns `COD_MANUAL_IDENTITY_CONFLICT`.
- Amount/currency/destination request tampering fails strict API binding; none can rebind a committed operation.
- New operation containing an allocated order returns `COD_MANUAL_ALREADY_SETTLED`.

UI UUID generation is only a selection identity; SQL constraints and immutable posted source history enforce financial idempotency.

## 11. Exact concurrency and atomicity protection

The Manual path requires an actual relational **Serializable** transaction. It reuses sorted existing PostgreSQL `FOR UPDATE` order locking; foreign-tenant rows are not admitted. Source/account reads and all financial writes occur inside that scope.

Batch, memberships, journal, ledger balance mutations, processed accounting receipt, Settled transitions, and transactional event outbox emission commit together. `FinancialPostingService` joins the current transaction. If any posting/fanout/result-validation step fails, rollback clears all those writes and discards tracked state.

Account xmin and permanent unique indexes remain active. Nested Npgsql serialization/deadlock/unique exceptions and EF concurrency failures map to deterministic `COD_MANUAL_CONCURRENT_CONFLICT`; the client retains and retries the same identity. Separate-context relational same/different-identity overlap tests produce exactly one new batch/member/journal movement and converge on retry. No optimistic account read is described as an account `FOR UPDATE` lock.

## 12. Actor/audit and journal lineage

Persisted successful operation contains:

- `PaymentSettlementBatch.TenantId`, `Id`, `SettlementType`, `SettlementId`, `CreatedBy`, `CreatedAt`, `SettledAtUtc`, gross/currency/count.
- Membership tenant/order/batch/whole amount/currency and `CreatedBy`.
- Journal tenant/source/batch/reference/key/timestamp and `CreatedByUserId`.
- Actual receiving ledger-account ID on immutable posted journal line.
- Processed accounting-outbox row with source identity, key, actor and journal ID.

Existing prerequisite header/member/ledger-line immutability guards remain. Replays preserve the original actor, even when another currently authorized Admin retrieves the same result.

## 13. Refund lineage verification

The prerequisite resolver is retained. Its MANUAL_COD_COLLECTION/ManualCodSettlement source contract now has a real executor. Refunds still independently re-read the batch/member/posted-journal/debit-account lineage, not fulfillment type or a fixed Bank/Cashbox rule.

- New real Manual settlement to Cashbox → refund **Cr1010**, proven relationally and live.
- Historical OFFLINE_COLLECTION and GATEWAY Bank1030 settlement histories → refund **Cr1030**, freshly rerun in the final relational certification.
- Historical manual-kind model whose actual receiving debit is Bank1030 still resolves Bank, proving the destination is not selected from the settlement kind.
- Missing/reversed/ambiguous/foreign/inconsistent history remains fail-closed.

## 14. Tests and exact pass/fail counts

Final selected backend command:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --no-restore --filter "FullyQualifiedName~ManualCodSettlementTests|FullyQualifiedName~CodPaymentLifecycleTests|FullyQualifiedName~CodSettlementPrerequisiteLineageTests|FullyQualifiedName~ShipmentCodCollectedConsumerTests|FullyQualifiedName~EcommerceRefundLifecycleTests|FullyQualifiedName~PaymentLifecycleRefundAuthorityTests|FullyQualifiedName~T10SettlementContainmentTests|FullyQualifiedName~PosPermissionAuthorizationTests|FullyQualifiedName~CommerceAccountingServiceTests|FullyQualifiedName~FinancialReportServiceTests|FullyQualifiedName~AccountingOutboxProcessorTests|FullyQualifiedName~TreasurySettlementAuthorityCertificationTests" --logger "trx;LogFileName=manual-certification.trx" --results-directory ".artifacts/cod-prerequisites" -v:q -clp:ErrorsOnly
```

TRX counters: **266 executed / 266 passed / 0 failed / 0 skipped**, duration5m49s. `ManualCodSettlementTests` supplies **35** of those checks. The prerequisite historical refund tests are fresh executions, not copied prior counts.

Final review strengthened the foreign-order fixture to submit another real tenant's order using a valid current-tenant operator, specifically asserting `COD_MANUAL_ORDER_NOT_FOUND` rather than merely rejecting missing actor authority. The complete16-case eligibility subset was recompiled in `ManualCodVerification` and rerun: **16 passed / 0 failed / 0 skipped** (`manual-final-eligibility.trx`,1m38s). These are the same16 cases already counted in266; they are not counted twice. Final backend evidence therefore contains282 passing executions covering266 unique checks.

Frontend:

```powershell
npx vitest run src/pages/treasury/TreasuryManualCodSettlement.test.tsx src/pages/treasury/TreasuryGeneralLedger.test.tsx
```

**2 files passed / 11 tests passed / 0 failed**: five new settlement UI tests and six existing General Ledger tests. These cover fixed destination, confirmation, exact POST shape, lost response/retry identity, in-flight reload, last-result GET restoration and eligibility-read failure honesty.

Required scenario coverage:

| Scenarios | Evidence |
|---|---|
| One/multiple orders, duplicate retry, same-ID replay | Real lifecycle collection + settlement + fresh Treasury/GL tests; live7EGP one-order flow |
| Concurrent overlapping new operations / same-ID attempts | Separate PostgreSQL contexts; one financial movement; original-ID retry |
| Manual refund / historical Bank refund | Real refund engine + destination-lineage regression; live Dr4020/Cr1010 |
| Actor, balanced journal, exact balances | SQL headers/journal actor; official fresh reports; live evidence below |
| Carrier/uncollected/pending-posting/already-settled/foreign tenant | Relational rejection fixtures and relevant live409 probes |
| Currency/duplicates/wrong destination/zero/negative/tampering | Relational authority checks and strict JSON binding tests; live400 extra-field probes |
| Identity collision / unauthorized role | Relational changed-order-set test; five-role policy and lifecycle tests; live five-role matrix |
| Inactive mapping / Clearing substitution / wrong default mapping | Isolated relational malformed-mapping fixtures fail closed; normal mapping mutation guard remains |
| Deleted/cancelled/refunded/unsupported precision/missing journal | Relational fail-closed fixtures |
| Batch→posting failure / posting→event publication failure / cancellation | Injected failures roll back batch, membership, journal, Settled state and balances; same-ID retries succeed |
| Permanent DB identity/membership | Soft-deleted duplicate inserts rejected by unfiltered unique constraints |
| Accounting recovery | Verified processed receipt recovery returns same posted journal; collection/refund/outbox regression suite remains green |

Rejected financial scenarios compare baseline journals/balances against fresh state; no additional settlement journal, membership, balance movement or falsely successful batch is admitted. Where a fixture already contains collection/refund history, those pre-existing fixture journals are the comparison baseline.

Verification corrections, not hidden final failures: initial focused29-case run had19passed/10failed; next25passed/4failed; corrected run29passed/0failed; final expanded35-case suite is included in266/266. Corrections addressed SQL microsecond snapshot precision, intentionally malformed fixture mappings interacting with existing immutable/unique mapping guards, and nested Npgsql transient-exception classification. The final report counts only the final selected green checks.

## 15. Build results

- API/Core/Infrastructure final isolated `ManualCodVerification` configuration build: **succeeded, 185 warnings, 0 errors**. Separate output configuration avoided interfering with the user-owned running MAIN API.
- Tests project compiled successfully as part of final test run.
- `npm run build`: **succeeded**, 5242 transformed modules; existing large-chunk warning.
- `git diff --check`: no whitespace errors. No whole-repository test or lint certification is claimed.

## 16. Migration/schema results

Applied to MAIN development database `PosEcommerceDb_Baseline`:

`20261008223819_ManualCodPermanentSettlementIdentity`

Exactly two existing unique indexes were recreated without the IsDeleted filter:

1. `IX_PaymentSettlementBatches_Tenant_Type_SettlementId`.
2. `IX_PaymentSettlementOrders_Tenant_Order`.

No new table/column, data backfill, settlement reclassification, or receiving-account rewrite. Direct precheck found zero settlement rows and zero permanent identity/member collisions in MAIN. Post-application `pg_indexes` verified both permanent definitions. Historical Bank lineage is unaffected and its real refund regressions pass.

MAIN applied migration count: **45 → 46**. Final rebuilt EF migration list has no pending marker. Final rebuilt `has-pending-model-changes`: **“No changes have been made to the model since the last migration.”** Snapshot diff is limited to the same two index filters. Migration Down was not applied to MAIN.

## 17. MAIN live E2E evidence

Safe synthetic fixture in a **new isolated tenant** in MAIN, not real customer/order data:

| Identity | Value |
|---|---|
| Disposable tenant | `94bc28ab-95d0-487a-8c47-926d7cb5e821` |
| Marker | `MAIN-MANUAL-COD-DISPOSABLE-20261009` |
| Manual order | `e5f8d3b1-a683-4084-83ca-2a05c88965e9` |
| Carrier reject-only order | `eb5f7116-d257-4481-9f5b-73a726ba5ff1` |
| Settlement operation | `d77f1029-2176-4192-bbc8-78b7068da924` |
| Batch | `573ee5a6-2b4b-4dfe-aff5-bfc89399285e` |
| Membership | `f754de6f-6dbf-477b-bd74-827e0f3dd49f` |
| Successful settlement actor | `eda2e9bf-ef1d-4900-90eb-40cfd2465b7d` |
| Receiving ledger account | `01a11db4-7c61-7401-8d4c-554687193257`,1010 |
| COD Clearing ledger account | `01a11db4-7c68-7868-b957-32743acd203d`,1130 |

Fixture contained no real contact/customer/product/shipment or remittance evidence. Delivered Manual ownership was seeded as disposable fulfillment state; collection itself used the real prerequisite-certified HTTP collection path. Normal synthetic role logins exercised the existing permission/session pipeline. Supplied real POS login was used only for AhramStore shared accounting reads/session restoration; no real financial mutation was requested.

Collection200 → eligible Posted Manual order → UI confirmation → server settlement200 → deliberately lost client response → same-request UI retry200/WasIdempotent → reload same result → fresh SQL and official Treasury/GL reads → second new operation409 → valid2EGP refund200 → fresh SQL/official Cashbox ledger.

Live rejection matrix: **11/11 expected statuses**, plus unauthenticated rejection; five-role matrix **5/5** expected outcomes; two simultaneous committed-ID replays both200 with the same journal. Rejected requests left balances unchanged.

## 18. Balance before / after

Disposable tenant, EGP:

| Stage | Cashbox1010 | COD Clearing1130 | Bank1030 |
|---|---:|---:|---:|
| Before collection | 0 | 0 | 0 |
| After Posted collection / before settlement | 0 | 7 | 0 |
| After settlement | 7 | 0 | 0 |
| After retries/rejections | 7 | 0 | 0 |
| After valid2EGP settled-order refund | 5 | 0 | 0 |

Fresh `TreasuryReadService` / `FinancialReportService`, fresh API requests, persisted SQL orders/batches/members/journals, and actual GL entries agreed. Collection state stayed linked to its original collection journal; settlement journal is separately batch-owned.

## 19. Journal IDs / references

| Operation | Journal ID | Reference | Actual entries |
|---|---|---|---|
| Collection | `01a11db7-5bb4-7ee6-9893-129c0fd9273a` | `JE-2026-000001` | Dr1130=7 / Cr4020=7 |
| Manual settlement | `01a11db8-df78-794d-be4b-5c817a3a15dc` | `JE-2026-000002` | Dr1010=7 / Cr1130=7 |
| Settled-order refund | `01a11dbb-bb00-7586-8c1d-97db61222f0a` | `JE-2026-000003` | Dr4020=2 / Cr1010=2 |

Settlement timestamp: `2026-10-08T22:53:37.162002Z`. Batch and journal contain the same authoritative operator GUID. Settlement header/member/journal each existed exactly once after all retries and role probes. All three journals balanced exactly.

## 20. Cleanup proof and shared AhramStore preservation

Disposable tenant teardown was scoped to the exact fixture identity, marker and tenant, in a relational transaction. It removed its orders, users, accounts, payments/mappings, settlement and refund data, journal/line data, accounting/event-outbox rows, and associated disposable SQL identities/projections. No FK, audit trigger, or application authority guard was disabled. No shared account balance was reassigned/reconstructed.

Fresh read-only MAIN SQL after cleanup:

```text
fixture tenants=0; users=0; orders=0; accounts=0;
journals=0; ledger lines=0; settlement batches=0; members=0;
accounting outbox=0; refunds=0.
```

Tenant-qualified Mongo cleanup removed: marketplace_order_read_models1, analytics_events1, order_attributions2, orders2. Fresh repeated Mongo scan returned no fixture documents.

**Canonical security retention:** one immutable `UserGovernanceProfile` for the deleted synthetic tenant remains; zero governance audit-log rows for that subject. MAIN's existing database guard prohibits deleting durable governance profiles. This non-financial security record was intentionally preserved, not bypassed or represented as deleted. It has no surviving tenant/user/order/account/settlement/journal or active login. Financial/business fixture cleanup is complete.

AhramStore `c9c5c1ae-949e-498e-bc63-07242daeb49d`, before/after read-only SQL and official Treasury API:

| Measure | Before | After cleanup |
|---|---:|---:|
| Cashbox1010 | 10.0000 | 10.0000 |
| COD Clearing1130 | 0.0000 | 0.0000 |
| Bank1030 | 0.0000 | 0.0000 |
| Journals / ledger lines | 13 / 30 | 13 / 30 |
| Settlement batches / members | 0 / 0 | 0 / 0 |
| Accounting outbox rows | 0 | 0 |
| Total journal debit / credit | 3880 / 3880 | 3880 / 3880 |
| Unbalanced journals | 0 | 0 |

Both original AhramStore COD orders retain their original Pending/Confirmed-or-processing and Failed/Cancelled financial facts and have no collection journal. Fresh Manual eligibility in AhramStore is empty. Real financial history was not altered by the disposable fixture. The browser was restored to the real AhramStore Owner session.

## 21. Failure safety and Carrier guarantee

Failure evidence:

- **Batch creation → posting:** injected posting failure rolls everything back; same operation retry posts once.
- **Posting → outbox/event publication:** injected canonical event publisher failure after real journal posting rolls back journal/balances/batch/order states; same operation retry posts once.
- **Cancellation before commit:** no partial settlement; retry succeeds.
- **Commit → lost client response:** live route interception forwarded the real request, obtained server200/Posted, then aborted the browser response. UI claimed no success, retained operation identity, and retry returned the original journal with WasIdempotent=true. Reload used fresh GET successfully.
- **Accounting receipt recovery:** no missing journal is manufactured. Recovery independently verifies posted SQL history and returns the original journal for acknowledgement. Manual settlement never commits a pending financial batch.

Carrier containment remains source- and runtime-proven:

- `settlementExecutionEnabled=false`, `evidenceAdmissionEnabled=false`, `lifecycleMutationEnabled=false` in COD foundation health.
- No remittance evidence ingress/provider adapter was added.
- Carrier orders cannot enter Manual eligibility/execution; live carrier attempt409 `COD_MANUAL_FULFILLMENT_REQUIRED`.
- Old carrier COD route409 `SETTLEMENT_EVIDENCE_REQUIRED`, trace `91213690-85e5-4a88-a13e-908918b978a6`.
- Retired admin route410 `SETTLEMENT_AUTHORITY_REQUIRED`, trace `ea404895-2857-4c0e-9dee-e3f55536deab`.
- Generic Clearing1130→Cashbox1010 transfer400, trace `935835b1-c8cc-4f43-b0a3-e6daf9ea7053`.
- Existing gateway containment also remains green in the T10 regression suite.

## 22. Remaining limitations / evidence boundaries

- Whole-order, one-currency, one receiving Cashbox operation only; no partial allocation, fee, split destination, remittance, settlement reversal or residual-refund settlement support.
- Active tenant ledger currencies requiring more than two decimal places fail closed under existing settlement precision.
- Selection is paginated and bounded; invalid candidates are omitted after financial qualification, so a candidate page may have fewer than100 eligible rows.
- Receipt replay/refund lineage deliberately fails closed if the actual destination/history becomes inactive, reversed, ambiguous or inconsistent; no fallback Bank/Cashbox guess.
- One immutable non-financial governance profile remains as described in cleanup, consistent with canonical security-history retention.
- Relational test harness stores are isolated, not the shared merchant database. Existing harness database-retention behavior was preserved; no broad prefix-based database teardown was performed.
- Live positive fixture exercised one order. Multiple-order and concurrent-new-operation scenarios were proven with separate-context PostgreSQL fixtures, not real merchant orders.
- Live aggregate `/health` was503 at `2026-10-08T22:56:45.96152Z` because existing `payout_external_truth_readiness` remained unhealthy. No change/fix to payout readiness is claimed. This bounded workflow certification does not certify overall deployment readiness.
- Initial live fixture user IDs required version4 GUIDs to match the existing POS frontend session GUID validator; synthetic users were corrected before any collection/settlement. No unrelated session-security code was modified.
- Live probe corrections supplied required legacy DTO fields and respected the existing financial rate limiter; a raw request without the normal CSRF header was rejected and repeated through the official API client. These rejections did not mutate financial state.

## 23. Deliverables and genuine certification boundary

Principal implementation files:

- `PointOfSale.Core/Interfaces/Payments/ManualCodSettlement.cs`
- `PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs`
- `PointOfSale.Core/Interfaces/Accounting/ICommerceAccountingService.cs`
- `PointOfSale.Core/Enums/PaymentStatus.cs` (destination-neutral Settled documentation)
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs` (partial declaration)
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.ManualCodSettlement.cs`
- `PointOfSale.Infrastructure/Accounting/ManualCodSettlementAuthority.cs`
- `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`
- `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Accounting/SettlementRefundLineage.cs` (manual contract documentation only in this phase)
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` (two permanent indexes)
- `PointOfSale.Infrastructure/Migrations/20261008223819_ManualCodPermanentSettlementIdentity.cs`, its Designer, and model snapshot
- `PointOfSale.API/Controllers/PaymentLifecycleController.cs`
- `PointOfSale.API/Security/PosEndpointPermissionCatalog.cs`
- `PointOfSale.Tests/Integration/ManualCodSettlementTests.cs`
- `PointOfSale.Tests/Security/PosPermissionAuthorizationTests.cs`
- `PointOfSale.Tests/Infrastructure/PaymentCancellationOutboxOperationalScalabilityTests.cs` (interface-compatible stub only)
- `PointOfSale.Web/src/services/manualCodSettlementApi.js`
- `PointOfSale.Web/src/pages/treasury/TreasuryManualCodSettlement.jsx` and its test
- `PointOfSale.Web/src/App.jsx`, treasury barrel, Clearing Aging link, English/Arabic locale keys
- This report.

Ignored local evidence: `.artifacts/cod-prerequisites/manual-certification.trx`, `manual-final-eligibility.trx`; `.artifacts/manual-cod/` fixture tools, schema/cleanup probes, live browser scripts and eight screenshots. Real supplied credentials are not included in this report or application/test source.

The phase is genuinely certifiable for the explicit positive Manual COD contract: server-owned financial facts, actual destination lineage, permanent allocation/idempotency, atomic posting, concurrent conflict safety, actor/permissions, bilingual UI, real refund compatibility, fresh official financial reads and MAIN disposable economic cleanup are established. Carrier remittance remains unavailable.

**STOP CONDITION OBSERVED: Manual COD Settlement work ends here. Carrier Remittance is a separate future phase.**
