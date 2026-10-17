# Carrier Integration — Comprehensive Source, Financial and MAIN Activation Certification

Date: **2026-10-10 local time**; execution evidence uses UTC on 2026-10-09.
Workspace: `C:\Users\fadya\source\repos\PointOfSale` (MAIN only).
HEAD: `256339621c0ea49f3602097cdacdf89ffe71754f`.

## Verdict

**PASS for the reviewed source, isolated financial workflows and authorized MAIN schema/runtime activation.**

- **613 distinct selected backend cases passed**, with zero failures or skipped cases.
- **112 frontend cases passed** in 14 files, including 32 EN/AR carrier-remittance cases.
- Real Microsoft Edge/Chromium verification passed **22 intercepted workflow steps**, **2 actor/tenant isolation steps**, and **7 authenticated, unmocked MAIN read-only steps**.
- The genuine scheduled refund reconciliation is now **Healthy / 0 mismatches**.
- Exactly the two authorized migrations were applied in order. MAIN now has **49 applied migrations / 0 pending / no pending model changes**.
- All **23 protected MAIN business-table content fingerprints** remained unchanged through backup, rehearsal, migration and final browser verification. The four new carrier tables are empty.

This verdict does not certify a live carrier exchange or a browser-to-API-to-SQL financial write on MAIN. Complete financial writes, race conditions and rollback behavior were exercised on isolated PostgreSQL; MAIN browser verification was authenticated and read-only. Aggregate infrastructure health remains **Degraded**, with intermittent Redis-related 503 responses recorded during execution; see section 8.

## 1. Entry baseline and working-tree preservation

The initial working tree contained **56 tracked modifications and 69 untracked files**. Its tracked diff SHA-256 was:

```text
4e95c1347dc4b7f8541a705f6c43a725a9b921a64963700ab834abe181cfac5e
```

Existing cancellation, tracking, Bosta collection, Manual COD, carrier remittance, accounting, refund and Treasury work was inspected rather than replaced. File-by-file entry hashes were retained. This execution changed these eight existing files:

1. `PointOfSale.Infrastructure/Migrations/20261009182344_ManualCarrierCashRemittance.cs`
2. `PointOfSale.Infrastructure/Accounting/CarrierRemittanceAuthority.cs`
3. `PointOfSale.Tests/Integration/CarrierRemittancePostgresTests.cs`
4. `PointOfSale.Web/src/services/carrierRemittanceApi.js`
5. `PointOfSale.Web/src/pages/treasury/TreasuryCarrierRemittance.jsx`
6. `PointOfSale.Web/src/pages/treasury/TreasuryCarrierRemittance.test.tsx`
7. `PointOfSale.API/Services/CommerceHub/FinancialReconciliationService.cs`
8. `PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialReadinessGate.cs`

It added `RefundCompletionEvidenceAuthority.cs`, `RefundCompletionEvidencePostgresTests.cs`, and this report. Every other pre-existing tracked/untracked file was preserved byte-for-byte. No pre-existing file was removed. The prior cancellation report remains unchanged; its three formerly stale architecture assertions were already corrected in the entry tree and were independently verified by fresh execution.

No subagents, staging, commits, resets, stashes, clean/discard operations, secondary workspaces or other deployment targets were used. Credentials are omitted from this report; the temporary credential-bearing browser login script was removed after successful authentication.

## 2. Defects found and remediated

| Severity | Verified defect | Production correction and evidence |
|---|---|---|
| P1 | Direct SQL could move a generic settlement allocation into a carrier batch, or reclassify a generic batch as carrier history. | Shared-table triggers now reject both UPDATE laundering paths. Non-carrier UPDATE returns NEW and DELETE returns OLD, retaining legitimate non-carrier behavior. Added real PostgreSQL regressions. |
| P1 | The deferred carrier commit guard did not independently require each order's Settled payment state and matching durable payment-status event. | COMMIT now checks `PaymentStatus=9` and the matching `OrderPaymentStatusChangedEvent` payment reference alongside the receipt event, journal, membership and accounting receipt. Added a rollback regression. |
| P1 | Accounting receipt recovery could acknowledge a Posted journal header without proving complete actual financial lineage. | Recovery loads memberships and uses `SettlementRefundLineage.ResolveDestinationAccountAsync` to validate actual Posted history before acknowledging the original journal. It does not create a replacement posting. Added a corrupted-lineage regression. |
| P2 | The UI could admit malformed eligibility or a nominal Posted response with money/binding facts different from the reviewed selection. | Eligibility and receipt admission validate uniqueness, finite amounts, currency, carrier/configuration/account/environment, operation/membership, timestamps and original receipt facts. Expected receipt facts and the immutable request survive recovery. Receipt references use NFKC normalization. New submission clears stale success. |
| P2 | Binary floating-point accumulation could reject a valid multi-order Posted receipt: `100.10 + 200.20` differed from server `300.30`. | Reviewed totals now sum integer minor units at SQL's `numeric(18,2)` precision. Both EN/AR regressions failed before the correction, then passed in the complete 112-case frontend run and real Chromium verification. |
| P1 | Reconciliation incorrectly required provider-refund confirmation for a legitimate, independently attested manual payout of an originally online-paid purchase. | Added strict read-only manual refund authority qualification, distinct report evidence, refund-specific provider statuses and an immediate Completed-refund readiness check. Nineteen isolated PostgreSQL cases passed; MAIN's original evidence qualified and its scheduled report became Healthy without rewriting financial history. |

Four new backend carrier regressions increased `CarrierRemittancePostgresTests` from 53 to **57**. Sixteen new EN/AR UI cases increased the remittance suite from 16 to **32**. The refund classifier has **19** new PostgreSQL cases.

## 3. Reviewed authority chain and retained contracts

| Boundary | Current source-level and regression conclusion |
|---|---|
| Carrier creation/registration | Canonical current carrier configuration, account and environment are resolved before an external operation. Persisted registration identity and immutable Bosta collection binding are not fabricated from later configuration or historical shipment state. |
| Official Bosta parsing | Official lowercase webhook fields, exact tracking identity, explicit numeric actual money, bounded UTC observation time, schema ambiguity and deterministic observation fingerprints are handled independently of lifecycle/financial authority. Parser cases and the endpoint/trust suites passed. |
| Webhook trust/tenant routing | Parsing or Delivered state does not bypass configured trust, persisted target identity, tenant routing or current locked SQL revalidation. Duplicate, invalid, contradictory and historical/unbound observations remain fail-closed/reviewable. |
| Immutable collection | `CarrierCollectionEvidence` and `CarrierCodShipmentBinding` retain restrictive identities, explicit account/environment/currency provenance and permanent review barriers. Existing shipments are not backfilled with invented bindings. |
| Customer COD collection | `ShipmentCodCollectedEvent` identifies qualified immutable evidence. Lifecycle, consumer, accounting and recovery independently validate it. Canonical `Order_COD` / `COD_DELIVERED:{tenant}:{order}` accounting is **Dr 1130 / Cr 4020**. Delivered, expected COD or a mutable collected flag alone cannot authorize money. |
| Merchant carrier cash receipt | Dedicated `CARRIER_CASH_RECEIPT` is **operator-attested physical cash**, using qualified whole orders and already Posted collection accounting. It posts **Dr 1010 / Cr 1130**, zero fees, one carrier/account/environment/currency, with permanent shared settlement allocation. It is not provider-confirmed remittance. |
| Financial writer/transaction | The existing financial posting service remains the ledger writer. Source-specific SQL reconstruction, outer Serializable transaction, actual lines/balances, receipt/membership, processed accounting receipt, order Settled state and durable domain events must commit together. Failure/race tests verify rollback and one permanent winner. |
| Replay/recovery | Same operation and normalized immutable facts recover the original Posted result. Changed facts, reused references and competing allocations conflict. Current operator permission is rechecked while original operator identity remains preserved. Recovery acknowledges complete original history, not a second cash receipt. |
| Refund destination | A settled carrier refund follows the complete original receipt lineage to the original receiving account. Unsettled COD and Manual COD retain their separate existing accounting contracts; no generic clearing balance is substituted for lineage. |
| Shipment cancellation | Fresh tenant-scoped SQL and external outcome evidence remain authoritative. Unknown/refused provider outcomes do not become local successful cancellation, and irreversible money/stock effects are not inferred from labels. The 59 cancellation cases are included in the authority group below. |
| Tracking/concurrency | Tenant context, current SQL binding, chronology, stale tracked-state handling and concurrent tracking persistence safeguards passed their real PostgreSQL suites. Tracking observations remain independent of cancellation and money authority. |
| Authorization/UI | Tenant and actor come from authenticated authority. Owner/Admin, current active SQL operator and financial permissions are required for selection, execution, replay and retrieval. EN/AR display, loss-of-response recovery and tenant/actor remount isolation passed. Client request DTOs do not author amount, currency, actor, tenant or ledger accounts. |
| Legacy/automated settlement | Existing legacy COD/gateway ingress remains contained with `409 SETTLEMENT_EVIDENCE_REQUIRED`; its T-10 regressions passed. Automated remittance was not enabled. The COD reference foundation remains disabled with execution, evidence admission, lifecycle mutation and settlement execution all false. |

Bosta observed-customer-money evidence is the money contract certified here. Other adapter cancellation/logistics safeguards were reviewed within the existing integration boundaries; no live provider behavior or unsupported carrier-money contract is inferred from that review.

## 4. Genuine refund reconciliation correction on MAIN

The blocking `EXTERNAL_REFUND_CONFIRMATION_MISSING` finding identified refund:

```text
ea49ca3e-3549-4426-8d0b-3dca4bd18e23
```

The original purchase used Paymob, but the customer refund was an operator-attested manual Vodafone Cash payout for **2200.00 EGP**, completed **2026-09-09T14:09:05.225722Z**. Read-only SQL inspection found the original Paid refund request, completed financial refund, unique successful execution, and matching `REFUND_PAYOUT_INITIATED` / `REFUND_PAYOUT_CONFIRMED` SuperAdminAPI audit records. The audit records attested money sent/external movement, rejected development simulation, and matched the actual execution/refund/actor/reference.

`RefundCompletionEvidenceAuthority` requires matching request/item/suborder/order/vendor/payment lineage, exact quantity/amount/currency/completion time/account/reference, one successful nondeleted execution, a valid method and actor, and two matching action/actor-bound audit documents within the bounded execution window. Missing, malformed, contradictory or simulated evidence does not qualify. A bare `manual-confirmed` flag is insufficient.

Provider-refund statuses are restricted to `confirmed`, `succeeded`, `success`, `completed`, `complete`, and `refunded`. Payment-only `paid`/`settled` labels are not refund finality. Completed external refunds lacking qualified evidence are checked at operation time, even before the next scheduled report; they block with `EXTERNAL_REFUND_RECONCILIATION_REQUIRED`.

Actual MAIN classification was executed inside READ ONLY transactions and returned:

```text
qualifiedOperatorAttestedManualPayout=True
providerConfirmationClaimed=False
financialMutation=False
```

The existing scheduled runtime job then naturally appended its **2026-10-09T22:00:15.106932Z** report:

- Status **Healthy**; mismatch count **0**; errors/warnings/affected entities empty.
- `operatorAttestedManualRefundIds` contains the original refund ID.
- Checked 23 suborders and 5 refunds; 442 stored aggregate rows validated, zero invalid/missing rows.
- No compensation action required.

The earlier Critical reports were preserved. No provider reference was invented, no refund or ledger row was rewritten, and no alert/status was manually suppressed. Normal scheduled report/audit and aggregate-validation metadata refresh is distinct from immutable financial history; the protected refund/execution/request/payment/transaction fingerprints remained identical.

## 5. Fresh verification evidence and counts

All four backend groups ran from the compiled `CarrierCancellationVerification` source using `dotnet test ... --no-build --no-restore`. Actual discovery, each TRX counter and every individual Passed result were checked, and test identities were deduplicated across groups.

| Disjoint backend group | Passed | Coverage |
|---|---:|---|
| New carrier money boundary | 132 | Carrier remittance PostgreSQL 57, API 5; Bosta evidence PostgreSQL 50, parser 20 |
| Financial/lifecycle regressions | 107 | Manual COD 35, COD lifecycle 32, prerequisite lineage 13, refund lifecycle 7, Bosta endpoints 10, webhook trust/routing 8, COD consumer 2 |
| Authority/security/recovery | 308 | Accounting writer/outbox, cancellation, tracking PostgreSQL, tenant isolation, registration, order/shipment authority, permissions and legacy settlement containment |
| Refund/reconciliation regressions | 66 | New refund evidence PostgreSQL 19, reconciliation service 16, execution 10, customer refunds 9, Treasury reconciliation 5, financial reconciliation/reversal certification 7 |
| **Total distinct** | **613** | **0 failed / 0 skipped** |

The prior targeted 59 cancellation and 120 regression closeout runs independently passed, but their overlapping identities are **not added** to 613.

Fresh final frontend execution passed **112 / 112** cases, zero failed/pending, across Treasury, cancellation, shipping administration and Orders. Carrier remittance contributes **32** cases. The earlier 110-case run is superseded by the final run after the decimal regression fix.

Build/model checks:

- `dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" -c CarrierCancellationVerification --no-restore --no-incremental -v:quiet /clp:ErrorsOnly`: **succeeded, 452 warnings / 0 errors**. This builds the API, Core, Infrastructure and Tests dependency chain.
- Fresh MAIN Debug activation build: **187 warnings / 0 errors**.
- Fresh final `npm run build`: **succeeded**, 5246 modules transformed; existing bundle-size warning retained.
- `dotnet ef migrations has-pending-model-changes --project "PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj" --startup-project "PointOfSale.API/PointOfSale.API.csproj" --configuration CarrierCancellationVerification --no-build`: **no pending model changes**. Actual post-migration SQL/model inspection independently agrees.
- `git diff --check`: passed. Existing Git LF/CRLF notices are not whitespace-check failures.

## 6. Real Chromium browser verification

The registered **`opencode-ui-test`** headed Microsoft Edge session was reused. Browser evidence identifies **Edge/Chromium 152**. `playwright-cli list`, `--help` and `run-code --help` were checked; a registered session is controlled with navigation/inspection commands rather than repeatedly opening another browser.

### Deterministically intercepted workflows: 22 steps, plus 2 remount steps

The actual application rendered in Chromium in both EN/LTR and AR/RTL. All workflow API traffic was intercepted with synthetic fixtures, including 16 receipt POSTs; none reached MAIN financial execution.

Verified: Treasury/Clearing Aging navigation; whole-order and single-binding selection; no editable money/account authority; required receipt/time/attestation/final review; future-time rejection; Unicode-normalized references; exact Posted admission; decimal multi-order receipts; Conflict/NotPosted/ReconciliationRequired/mismatched responses remaining unconfirmed; exact-request retry after timeout; GET-only reload recovery; empty eligibility; Manual COD presentation; unauthorized and unauthenticated presentation.

Held-response tests additionally verified that tenant changes discard the old workspace/result, actor-only changes isolate recovery state, and the new actor does not GET the old operation. Both remount cases had zero page exceptions and zero old-operation GETs.

Recorded workflow console/network errors correspond to deliberately injected 409, 401 and timeout responses. They are retained as negative-case evidence, not described as an error-free console.

### Authenticated MAIN, no response mocking: 7 steps

Using the supplied POS credentials, the actual server established an Owner session for AhramStore tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`. A read-only browser network guard continued real GET/auth traffic and would abort an unexpected business write; none was attempted.

- Real carrier eligibility GET: **200**, **0 qualified orders**, EGP, destination **1010**.
- Real unknown-operation recovery GET: **404**.
- EN and AR empty-state rendering with receipt review disabled.
- EN and AR real Clearing Aging navigation to carrier remittance.
- EN and AR real Manual COD availability, with real eligibility GET **200**.
- **0 page exceptions / 0 financial POSTs / 0 unexpected blocked writes / no API 5xx** in the completed smoke run.

The unauthenticated, unmocked check also redirected to login and rejected carrier eligibility with the existing security response **400 `TENANT_REQUIRED`**; auth-session requests returned 401. Its initial test expected only 401 and was corrected to recognize the actual tenant middleware contract. A browser helper using unavailable sandbox-global `URL` was also corrected; the subsequent real login and smoke checks passed.

MAIN has no new immutable carrier collection/receipt evidence; empty eligibility is expected and was not populated with test records. Financial write proof is supplied by the isolated PostgreSQL suites, not by claiming a MAIN end-to-end posting.

## 7. MAIN backup, rehearsal and authorized migrations

Runtime environment/user-secrets resolution, public health identity and READ ONLY SQL agreed on the actual MAIN target:

```text
192.168.1.5:5432/PosEcommerceDb_Baseline
```

Despite its database name, this is the independently verified active MAIN database, not a guessed alternate target.

Pre-application history was **47 applied**, ending `20261009121429_ShipmentCancellationAuthority`. Exactly these migrations were pending and authorized:

1. `20261009162710_CarrierObservedCodCollectionEvidence`
2. `20261009182344_ManualCarrierCashRemittance`

The exact reviewed SQL is additive tables/indexes/restrictive FKs/immutability and admission/commit functions/triggers, plus the two EF history entries. It contains no business-row DML. SQL SHA-256:

```text
D6500A0DD7DCE8C33B8CEC8184E903020A7B6CD088A87C4765B412E8E294FCDA
```

### Final pre-migration recoverable backup

```text
C:\Users\fadya\AppData\Local\Temp\opencode\MAIN-carrier-before-20261009220638.dump
Bytes: 10126292
SHA-256: BE000964E48A6543229BA3AD3F8E2CFEAFE3B9A59E6CD81D09D39A2FE079582E
Archive entries: 2273
Verified restore: pointofsale_carrier_restore_20261009220638
```

Official PostgreSQL binary `pg_dump`/`pg_restore` tools were used from the approved temporary directory. Archive listing was checked, the custom backup was restored into the isolated database, all 23 business fingerprints matched, and the exact reviewed SQL was successfully rehearsed there: **49 applied / 0 pending**, with unchanged business rows. MAIN fingerprints also remained unchanged during the backup/rehearsal gate.

Application preflight rechecked the exact 47/2 model/history, fresh genuine Healthy refund report and runtime financial health, verified backup/SQL hashes, verified target and baseline fingerprints. One preflight attempt stopped before migration SQL because aggregate health was 503 from Redis latency; no migration had begun. After runtime health returned 200 and financial readiness remained Healthy, preflight passed and each authorized migration was applied in order.

The actual migration result was recorded at **2026-10-09T22:10:02.092375Z**. Post-application catalog inspection verified **146 index/constraint/trigger facts**, all **9 carrier triggers**, including the `DEFERRABLE INITIALLY DEFERRED` Posted commit constraint. All four new tables are empty:

- `CarrierCodShipmentBindings`
- `CarrierCollectionEvidence`
- `CarrierRemittances`
- `CarrierRemittanceOrders`

The original cancellation identities/restrictive FKs remain intact. No other MAIN migration was applied.

### Business-row preservation

Full row-content fingerprints were compared before restore, after restore/rehearsal, immediately before/after MAIN migration and after final Chromium QA for:

`Orders`, `Shipments`, `OrderRefunds`, `PaymentIntents`, `PaymentSettlementBatches`, `PaymentSettlementOrders`, `LedgerJournalEntries`, `LedgerLines`, `LedgerAccounts`, `StockMovements`, `ShipmentCancellationOperations`, `AccountingOutboxMessages`, `PaymentCancellationOutboxMessages`, `OutboxMessages`, `ShippingWebhookLogs`, `CarrierConfigurations`, `CommerceHubRefunds`, `CommerceHubRefundExecutions`, `CommerceHubRefundRequests`, `CommerceHubPayments`, `CommerceHubFinancialTransactions`, `CommerceHubVendorEarnings`, and `CommerceHubPayouts`.

All **23** matched. The original `Id:xmin` signatures for the 12 cancellation/financial preservation tables also match the entry baseline. Actual retained counts include 15 orders, 8 shipments, 190 journal entries, 399 ledger lines, 353 ledger accounts, 197 stock movements, 5 CommerceHub refunds, 5 refund executions, 45 CommerceHub financial transactions, and zero settlement batches, carrier receipts or payouts.

## 8. Activated runtime and health observations

Only the verified MAIN API process was stopped for controlled fresh Debug activation. The final API is **PID 20340**, launcher **8676**, Development, working directory `PointOfSale.API`, using the established `dotnet run --launch-profile http --no-build --no-restore` launch. Its HTTP port is **5010**.

Reviewed active Debug DLL hashes:

| Artifact | SHA-256 |
|---|---|
| API | `9534b869a0dd11453bb71f9b3ee5b33c9ea275fe5a37d2aedf0f681963f86579` |
| Infrastructure | `91b2cfc769a5de7b8e9cf268ba3d3b99216d042753f5e089150a80f4affe899f` |
| Core | `803de6a2d22fb1fba59e8145a153ba39b438b10d21c0b80c02eab75f66c23174` |

The running API's Swagger exposes the current cancellation contract. Liveness returned **200 Healthy**. After migration, `CommerceHubPlatform` and PostgreSQL are Healthy and no migration readiness warning remains. `payout_external_truth_readiness` and external-outcome recovery are Healthy with the fresh Healthy/0 reconciliation report.

Three final bounded aggregate-health samples at **22:21:13, 22:21:16 and 22:21:20 UTC** all returned **HTTP 200 / Degraded**, with financial readiness, PostgreSQL and CommerceHubPlatform Healthy. Remaining degradations included Redis latency and preserved foreign-tenant projection documents. Disabled certificate-provider/Caddy execution is also reported as Degraded by the existing checks.

Intermittent **503** aggregate responses were observed from Redis latency (and once projection-drift check failure under load), including after otherwise successful migration/browser checks. These observations are retained; this report does not certify uniformly Healthy infrastructure or hide those checks. They did not recur as a refund/ledger mismatch, and financial/schema readiness stayed Healthy in the final samples.

No live carrier requests, real shipment operations, refunds, payouts or merchant financial POSTs were performed by this execution. Carrier configurations and financial history were preserved. Automated remittance and legacy settlement execution remain contained.

## 9. Evidence index and remaining verification boundary

Evidence is retained under the approved directory:

```text
C:\Users\fadya\AppData\Local\Temp\opencode\
```

| Evidence | Artifact |
|---|---|
| Backend discovery/results/execution | `carrier-comprehensive-{new,financial,authority,refund}-discovery.log`, corresponding `.trx` and `-execution.log` files |
| Final frontend results/build | `carrier-comprehensive-vitest.json`, `carrier-comprehensive-final-frontend-tests.log`, `carrier-comprehensive-final-frontend-build.log` |
| Fresh refund Debug runtime activation | `carrier-cancellation-final-closeout-refund-debug-build.log`, `-refund-activation.json`, `-activated-refund-runtime.json` |
| Browser workflow/isolation/actual MAIN | `carrier-comprehensive-browser-{workflow,remount,main}.json` and corresponding `.log` files |
| Browser screenshots | `carrier-qa-{en,ar}-{posted,empty}.png`, `carrier-qa-tenant-remount.png`, `carrier-qa-main-{en,ar}-empty.png`, `carrier-qa-main-unauthenticated.png` |
| Backup/rehearsal and exact SQL | `carrier-comprehensive-backup-gate.json`, the final custom dump and `.list`, `carrier-comprehensive-main.sql` |
| Actual application/catalog | `carrier-comprehensive-migration-result.json`, `carrier-comprehensive-post-schema.json` |
| Final preserved data | `carrier-comprehensive-final-preservation.json`, `carrier-cancellation-final-closeout-post-sql.json` |
| Runtime/health observations | `carrier-cancellation-final-closeout-deployed-runtime.json`, `-final-verification-runtime.json`, `carrier-comprehensive-final-health-samples.json` |
| Entry/final tree integrity | `carrier-cancellation-final-closeout-initial-tree.json`, `-final-tree.json` |
| Artifact hashes and final gate cross-check | `carrier-comprehensive-evidence-manifest.json` |

The historical implementation reports accurately described their own earlier, unapplied state. This report supplies the later activation evidence; those reports were not rewritten to imply that deployment happened during their original tasks.

**Closed:** the reviewed carrier financial defects, refund authority classification defect, authorized migrations, real read-only MAIN UI verification, and preservation gates.

**Verification boundary:** live provider integration and a financial browser-write workflow on MAIN were not executed. Infrastructure Redis latency/projection-health volatility remains visible. None of those observations is represented as a fabricated provider receipt, manufactured MAIN test allocation or suppressed financial alert.
