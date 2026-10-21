# Affiliate Phase 4 — commission eligibility, calculation and ledger

Date: 2026-10-21. Scope: primary development workspace/database only; no tester-machine access. Evidence paths below are relative to `.ai/evidence/affiliate-phase4-20261021/` unless otherwise stated.

## Verdict

**BLOCKED for complete live qualifying Phase 4 certification.** Commission hardening and focused executable verification are complete, but no new live order was delivered, collected, approved or refunded. Automated qualifying/refund results must not be confused with headed live results.

- **PASS:** final Phase 4 tests **31/31**, including real PostgreSQL concurrent first recognition, real EF bus-outbox rollback, same-scope retry and cumulative-refund concurrency.
- **PASS:** selected Phase 1–3 backend regressions **260/260** and focused financial regressions **214/214**; no skips in these runs.
- **PASS:** headed Shopixy and Greatone checkouts preserve attribution and immutable candidates without creating earnings for unpaid COD orders. Authoritative read-only affiliate availability remains **EGP 0**.
- **BLOCKED:** supplied POS credentials are scoped to **AhramStore**, not Shopixy/Greatone; the Greatone order is correctly inaccessible. Attributed manual delivery also requires trusted verification that operator-entered proof cannot supply. Neither authority boundary was bypassed.
- **NOT GREEN repository-wide:** source-contract selection **75 passed / 13 failed**; a broader regression selection timed out at 300 seconds. Details and classifications appear below.

**Stop point: before withdrawal requests, payouts or settlement execution.** No commit, staging, stash, reset, cleanup, migration of the development database, historical financial repair, real payment, collection, transfer, notification or carrier booking was performed.

## Financial rules and authority

### Admission and recognition

1. Checkout owns current admission: valid merchant/product/link/global-credential/profile lineage and an admitted product policy. It captures eligibility, policy ID, rate, per-unit cap, base, commission and capture timestamp on each order line.
2. Attribution is not money. Neither referral navigation, order creation, a COD payment-method code, uploaded evidence nor delivery alone makes an unpaid order commission-bearing.
3. Recognition requires canonical SQL attribution matching the order's affiliate/profile, referral and promotion-link snapshots. Product/policy ownership must belong to the originating merchant. A promotion link, when present, must retain the matching merchant and global credential/profile lineage. Existing legacy attribution without a promotion-link ID remains supported.
4. Pending recognition accepts only `Collected`, `Settled` or `PartiallyRefunded` payment authority, with accounting `Posted` or the existing `NotRequired` exception. Other states do not earn; unposted accounting defers recognition. A treasury clearing-to-main transfer is not an additional earnings prerequisite.
5. Eligible collected lines append **Earned**, which is pending and **not available**. Qualifying persisted physical delivery appends **Approved**, linked to the original earning. This preserves the established two-stage lifecycle rather than inventing a new release rule.
6. Cancelled/fully refunded orders cannot create new earnings. Missing/deleted affiliate profiles cannot create new earnings. Later profile suspension or policy disablement/rate changes do **not** retrospectively recalculate or erase a valid checkout-time candidate; this is different from admitting a new checkout while suspended/disabled.
7. Approval revalidates current SQL order/payment/accounting authority, canonical attribution, line snapshot/ownership and original earning equivalence. Fully reversed items cannot reactivate through delivery replay.

Authorities: `PointOfSale.API/Services/Checkout/CheckoutService.cs:7969–8021`; `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs:1330–1659`, `:1937`, `:2260–2677`; new `AffiliateService.CommissionAuthority.cs`.

### Calculation and rounding

```text
lineSubtotal = Round(UnitPrice × Quantity, 2, AwayFromZero)
base = Round(lineSubtotal − allocatedDiscount + allocatedTax, 2, AwayFromZero)
commission = Round(min(base × capturedRate, capturedCap × Quantity), 4, ToZero)
```

The `min` cap is omitted when the cap is null. Rates are decimal fractions, not percentage integers. Shipping and payment fees are excluded because they are not part of the merchandise line total. Tax **is included** under the existing implementation; Phase 4 does not silently change that commercial rule.

- Order discounts and tax are allocated by the existing decimal largest-remainder algorithm, with line-index tie-breaking; discounts are capped to merchandise subtotals. Allocations preserve exact rounded totals. Three equal lines sharing EGP 1 allocate **0.34 / 0.33 / 0.33** in the executable allocation test.
- Quantity is already included in the line base; commission is not multiplied by quantity a second time. Only a per-unit cap is multiplied by quantity.
- Example coverage: `12.37 × 3 − 2.11 + 1.27 = 36.27`; at `0.123456`, commission truncates to **4.4777**. Three EGP-100 units at 10%, cap EGP 4/unit, produce **12**, not 4 or 30. EGP 100 less 25 discount plus 7 tax at 10% produces **8.2**.
- Recognition reproduces the snapshot formula and requires the captured base to equal immutable `OrderItem.Total`; mismatching captured amounts fail rather than being trusted or recalculated using today's policy.

### Reversals and available balance

Refund consumers retain their durable refund-line admission. Commission reversal now converges on **persisted cumulative `OrderItem.RefundedAmount`**, under the order lock, rather than treating each event-supplied amount/key as a new financial delta.

```text
cumulativeBase = min(SQL RefundedAmount, originalEarnedBase)
newBaseReversal = max(0, cumulativeBase − previouslyReversedBase)
cumulativeCommission = Round(originalCommission × cumulativeBase / originalBase, 4, ToZero)
newCommissionReversal = bounded(cumulativeCommission − previouslyReversedCommission)
```

The final reversal consumes the exact remaining commission, avoiding rounding residue. Each append links `OriginalEntryId` to the original Earned row. Event redelivery, a synchronous reconciliation followed by the refund event, and a different event key cannot reverse the same cumulative refund twice. A fabricated refund amount without persisted SQL refund authority cannot reduce the balance. Order, item and merchant ownership are checked before reversal.

Example: EGP 100 base / 10 commission; cumulative refund 25 reverses **2.5** once, leaving **7.5** approved availability. A later cumulative refund 100 reverses only the remaining **7.5**, leaving zero. A capped commission is reversed proportionally from its original amount, not recomputed from an uncapped current policy.

The existing `AffiliatePayoutService.GetAvailableBalanceAsync` computes:

```text
netApproved = Σ per item max(Approved − Reversed, 0)
rawAvailable = netApproved + negative corrections + withdrawals (already negative)
               − active pending/approved request reservations
displayAvailable = max(rawAvailable, 0)
```

`Earned` alone, positive adjustments and Mongo projections are not withdrawal authority. `Paid` lifecycle markers do not deduct the same money a second time. **No earnings-credit rows were added to `AffiliateBalanceLedger`**: it records account movements, while commerce earnings availability derives from the commission ledger. Global affiliation is keyed by `AffiliateAccountId`, with merchant liability retained in `StoreTenantId`; same-currency store amounts can aggregate without merging merchant ownership. Existing currency-conflict guards remain in place; no FX rule was introduced.

## Implementation and SQL integrity

Only these application/test source files belong to Phase 4:

| File | Change |
|---|---|
| `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs` | Lock before financial reads; verify equivalent earned replay; cumulative reversal; revalidate approval and reconcile refunds; restore owned failed recognition/outbox retry state. |
| `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.CommissionAuthority.cs` (new) | Fresh no-tracking SQL order reads/locks, canonical attribution/link lineage, formula and policy/product ownership checks, shared original-manual-shipment guard. |
| `PointOfSale.Tests/Affiliate/AffiliatePhase4CommissionLifecycleTests.cs` (new) | 30 executable decision/calculation/replay/evidence/global-affiliate cases. |
| `PointOfSale.Tests/Affiliate/AffiliatePhase4PostgresTests.cs` (new) | One real PostgreSQL concurrency/atomic-outbox/refund scenario. |

Phase 4 also adds this report and its evidence directory. The many other existing changes shown by `git status` are **not** claimed as Phase 4 work. `working-tree-before.txt` and `working-tree-final.txt` record that boundary. No earlier Phase report/evidence was rewritten.

### Defects addressed

- Recognition formerly read financial authority before owning its transaction/lock and could reuse tracked authority. Recognition/reversal now lock the SQL order before reading current items/ledger; approval SQL authority is also no-tracking.
- A duplicate financial operation must match the original affiliate, merchant, attribution, item, currency, base, rate and commission; it is not accepted merely because an idempotency collision occurred.
- Old reversal arithmetic could apply cumulative refund value as another delta, and different synchronous/event keys could reverse the same refund again. Both entry paths now calculate the missing cumulative amount from SQL.
- Delivery reconciliation and direct approval now share original manual-shipment validation; approval additionally verifies the earning against canonical line authority and reconciles already-persisted refunds before exposing value.
- Failed owned recognition rolls back ledger and real bus-outbox state, then clears the rolled-back attempt using the existing retry-state utility when the context started clean. No unrelated pending caller changes are discarded.

These are code-audit findings plus final executable regression evidence. Early files named `functional-before`/`runtime-before` include fixture-construction failures and are **not valid pre-fix runtime reproductions** of these defects.

### Existing constraints and retained boundaries

- PostgreSQL recognition/reversal uses `SELECT … FOR UPDATE`; the SQL Server branch uses update/hold locks. **SQL Server was not executed here.** Relational locking/rollback claims come from PostgreSQL, not InMemory.
- Existing global unique keys `UQ_AffiliateCommLedger_Idempotency` and `UQ_AffiliateBalanceLedger_Idempotency`, plus `UQ_AffiliateAttributions_Tenant_Order`, remain unchanged. Generated financial identities retain item/entry or cumulative-refund identity. No new schema migration was needed.
- `ApplicationDbContext.GuardAffiliatePayoutAuthority` rejects EF update/delete of commission and balance rows; immutable order/line snapshots and canonical attribution guards remain unchanged. Corrections append reversal rows rather than editing originals.
- **No database ledger trigger was returned by the development trigger inventory.** Append-only protection described here is the application save boundary, not a claim that unrestricted raw SQL administrators are prevented from rewriting rows.
- Commission and EF bus-outbox writes stage before the same transaction commits. The test bus was never started; no real broker/provider or payout was invoked by the PostgreSQL fixture.

Constraint inventory, hashes of the four source files and built API/infrastructure assemblies: `final-review.json`. The API output's infrastructure assembly hash matches the built infrastructure output. The updated API was built and restarted before headed verification; later edits were tests/evidence only.

## Headed browser and live SQL evidence

Headed Microsoft Edge session **`opencode-ui-test`** was reused, with real local Storefront/API requests and supplied existing logins. No fake successful response, tenant-header override, forged delivery event, direct SQL status change or manual ledger insert was used to obtain a qualifying result.

Both new orders resolve to global profile **`01a03e11-e234-7d40-b77e-99f9463c7a40`**. These are additional Phase 4 QA orders, not the protected Phase 3 orders.

| Field | Shopixy | Greatone |
|---|---|---|
| Merchant tenant | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` | `c43aebf2-4cc4-406a-a2db-4df06d40bce5` |
| Order | **`ORD-83B3-2026-01094`** | **`ORD-C43A-2026-01095`** |
| Order ID | `40b22f7f-09ac-4c60-b779-f14cf3734fe2` | `2654c8f2-c859-41ad-b637-212affd7b1b4` |
| Item ID | `3051be3d-d890-4e47-bb2a-8917b92f4cd7` | `ae36b5ea-af5f-4782-a1fc-3b6fd9c4fb6a` |
| Attribution ID | `01a15fed-5d8c-7e2c-8a7a-4cbb16e285cd` | `01a15fef-65dd-733f-a762-1baac2438dd2` |
| Saved promotion link | `01a15f25-4119-7d7e-a868-e1e38d8e3c75` | `01a15f25-746b-72c1-b20d-c29516b4e3c4` |
| Referral | `K8YUSVCRWF` | `2T9LXU8E2I` |
| Product / quantity | Samsung Galaxy S26 Ultra / 1 | Dell Latitude 7420 Laptop / 1 |
| Base / rate / cap | EGP 770 / 20% / null | EGP 2,200 / 10% / null |
| **Candidate only** | **EGP 154** | **EGP 220** |
| Tax / discount / shipping / payment fee | All zero | All zero |
| Final state | Pending, COD, unpaid, unfulfilled | Pending, COD, unpaid, unfulfilled |
| Canonical attribution rows | 1 | 1 |
| Earned / Approved / Reversed rows | 0 / 0 / 0 | 0 / 0 / 0 |
| Payment intent / shipment / financial journal | None | None |
| Reservation ID | `53a97a5e-efbe-437e-9c65-6f9a07ba26ab` | `16bb9b9a-7a91-49bd-8a97-02f73b6936d8` |

Both order notes begin **`AFFILIATE_PHASE4_20261021_`** and identify synthetic/no-physical-dispatch/no-real-money use. Each retains one uncommitted reserved unit. **Do not fulfill, contact, collect, cancel or rewrite either order as follow-up cleanup.** No synthetic accounting history was erased.

Primary SQL evidence: `orders-readback.sql`, `orders-final.json`. Browser evidence: `shopixy-product.png`, both `*-review.png`, both `*-confirmation.png`, preparation/review/submit text files, `browser-requests-after-shopixy.txt`.

### What the headed checks proved — and did not

| Scenario | Result / evidence |
|---|---|
| Saved referral → checkout → retained candidate and one attribution in each merchant | PASS; real confirmation pages and SQL above. |
| Unpaid COD creates no live commission or availability | PASS; zero new ledger rows; authoritative availability readback below. |
| POS login → foreign Greatone order | Correctly denied: UI “The requested record was not found.” `admin-foreign-order-denied.txt` / `.png`. Account displays AhramStore Owner. No scope override attempted. |
| Attributed manual delivery | BLOCKED by source-authoritative trusted-verification guard; not claimed as a live delivery response for these pending QA orders. |
| Collected + delivered → new approved live value | NOT EXECUTED; blocked before progressing order B. |
| Cross-merchant approved live aggregation | NOT EXECUTED; isolated executable coverage only. |
| Live partial/full refund and reversal | NOT EXECUTED; isolated executable/PostgreSQL coverage only. |
| Successful live coupon scenario | NOT EXECUTED; no eligible coupon was introduced. Formula/allocation tested in isolation. |

The existing trusted manual-delivery guard is in `OrderFulfillmentService.cs:895–902`, before status mutation or downstream facts. It rejects attributed manual delivery **regardless of the legacy override**. The commission approval guard also requires retained matching proof and original delivered manual shipment where applicable. No safe trusted-verification workflow was found; this report does not authorize adding a break-glass bypass or fabricating carrier evidence.

### Authoritative balance readback

Headed existing portal navigation caused **GET** `/api/affiliate/payouts?page=1&pageSize=10`, HTTP 200:

```json
{"totalApproved":0,"totalPaid":0,"totalPendingRequests":0,"availableForWithdrawal":0,"minimumThreshold":100,"canWithdraw":false,"currency":"EGP","history":[]}
```

Evidence: `affiliate-availability-after.txt` / `.png`, `browser-requests-final.txt`. The Withdrawals tab was **read only**; Request Withdrawal was not clicked and no request/execution endpoint was called.

The pre-existing dashboard displays gross Earned/Approved **EGP 80**, but `financial-summary.sql` / `.json` shows its two historical EGP-40 items each fully reversed. Net approved is correctly **zero**. Gross lifecycle history is not a second wallet credit or proof of a new availability defect. No historical row or dashboard formula was changed to manufacture agreement.

### Retained automation errors

Shopixy's successful submit navigated to `/checkout/pending`, while the script expected `/orders/`; Greatone's successful submit then hit an unavailable `URL` global during post-submit evidence code. Both errors are retained. **SQL was checked before any retry; neither committed checkout was submitted again.** Scripts were corrected for future use, and actual pending-page screenshots/readbacks establish the two committed orders. The earlier Shopixy review wait similarly expected an outdated heading; it was a locator error, not a failed checkout.

## Executable coverage and quality gates

| Gate | Final result | Evidence |
|---|---|---|
| Phase 4 focused tests | **31 passed, 0 failed, 0 skipped** | `phase4-isolated.log` / `.trx`, `run-phase4-isolated.ps1` |
| Phase 1–3 selected backend regressions | **260 passed, 0 failed, 0 skipped** | `phase123.log` / `.trx`, `run-phase123.ps1` |
| Financial/domain/consumer/accounting/manual-delivery regressions | **214 passed, 0 failed, 0 skipped** | `financial-regressions.log` / `.trx`, `run-financial-regressions.ps1` |
| Affiliate portal selection | **64 tests / 16 files passed** | `portal-tests.log` |
| Expanded Storefront checkout/store/bootstrap/API/cart selection | **403 tests / 43 files passed** | `storefront-regressions.log` |
| Initial Storefront selection | **59 tests / 10 files passed** | `storefront-tests.log`; overlaps other selections, not an extra unique-test total |
| Admin orders selection | **39 tests / 3 files passed** | `admin-order-tests.log` |
| API build | PASS, **0 warnings / 0 errors** | `backend-build.log` |
| Storefront / Admin production builds | PASS | `storefront-build.log`, `admin-build.log` |
| Admin TypeScript check | PASS | `admin-typecheck.log` (successful command, no diagnostics) |
| Focused frontend lint | PASS; legacy ESLint config deprecation remains | `focused-lint.log` |
| Working-tree whitespace check | PASS, exit 0; Git CRLF conversion warnings only | `diff-check.log` |
| Source contracts | **75 passed / 13 failed**, no skips | `source-contracts.log` / `.trx` |
| Broader financial/payment/fulfillment regression selection | **INCOMPLETE**, timeout at 300 seconds | `regressions.log`, `run-regressions.ps1`; not a full-suite pass |
| Final preservation / artifact review | PASS within inventoried scope | `preservation-comparison.json`, `stock-deltas.json`, `final-review.json` |

Full backend counts/failure messages are in `test-summary.json`; frontend counts are in `frontend-test-summary.json`. Selections are not advertised as an exhaustive repository run or summed into a unique repository test total.

### What the 31 Phase 4 cases cover

- Non-recognition for Pending (including delivered/unpaid), COD-uncollected, AwaitingVerification, Authorized, Rejected, Refunded, Expired and AmountMismatch; unattributed/cancelled orders; deleted profile.
- Collected-undelivered pending Earned with zero availability; delivered-collected linked Approved exactly once; multiple lines; repeated calls.
- Captured quantity, allocated discount/tax, truncation and per-unit cap; actual checkout largest-remainder allocator; rejection of a forged commission snapshot and foreign-merchant policy.
- Existing canonical-save rejection of affiliate redirection; forged refund without SQL authority; wrong refund merchant/item; synchronous/event duplicate convergence; partial → full reversal; no full-refund reactivation.
- Checkout terms retained after policy change/disable and profile suspension; one global affiliate across two merchants yields 10 + 30 = 40 availability while merchant views retain 10 and 30 and an unrelated affiliate sees zero.
- Real fulfillment-service proof trim/persist/reload/conflict behavior on an **unattributed isolated fixture**. This is not a bypass or successful attributed manual-delivery claim.

The 30 decision cases use EF InMemory and do not prove relational transaction behavior. The remaining **real PostgreSQL** test uses the existing disposable test-database infrastructure on the primary development environment, not live merchant orders. It:

1. Injects failure after Earned and its real bus-outbox message are staged, before approval completes; independent SQL readback finds **zero commission and zero outbox rows**.
2. Races the first successful recognition across **eight handlers**, including a retry using the same failed scope/publish endpoint. Then repeats eight concurrent deliveries. Exactly **one Earned + one Approved and two outbox messages** remain.
3. Persists cumulative refund 25 and races eight recognition reconciliations; exactly **2.5** is reversed, total outbox messages become **three**, and authoritative availability is **7.5**.
4. Confirms no balance-ledger movement or payout request exists. The bus is not started; provider/withdrawal activity is not involved.

Final focused run uses an isolated output directory under approved private temporary storage and `POINT_OF_SALE_SOLUTION_ROOT`; concurrent ordinary-output test runs had locked the shared test DLL. Earlier fixture ID errors, nullable-theory conversion, missing fixture posting metadata, compiler/file-lock failures and intermediate partial results are retained, **not presented as final application failures or red/green runtime defect demonstrations**. The final 31-case run passed in 57 seconds.

### Source-contract failures — unchanged assertions

The following 13 failures remain visible. All except the cumulative-reversal assertion were already listed in the preserved Phase 3 report; that establishes prior observation, not blanket proof that their underlying behavior is correct.

| Test (class / method) | Classification / observed failure |
|---|---|
| `AffiliateCommissionLifecycleCertificationTests.ReversalBehavior_MustUseRefundLineAmountsAndBoundPartialReversals` | **New consequence of Phase 4 fix:** expects literal old delta expression `Math.Min(refundedBaseAmount, remainingBase)`. New cumulative subtraction is intentional and executable-tested. Assertion was not weakened or replaced. |
| `AffiliateCommissionApprovalWithdrawableBalanceCertificationTests.ManualDeliveryEvidence_MustBePersistedForLaterFinancialRevalidation` | Stale direct-assignment shape: expects `normalized…` on the order; runtime persists the event-carried normalized proof at `OrderFulfillmentService.cs:928–931`. Reload/conflict behavior passes executable coverage; attributed trusted-verification boundary remains blocked. |
| `CrossSystemCriticalRiskHardeningTests.AffiliateAttributionPlaceholder_MustNotExposePrePaymentOrderAsPaid` | Expects empty PaymentMethod. Existing consumer instead writes actual method code while retaining `PendingPayment`, zero PaidAmount and zero commission (`AffiliateOrderAttributionConsumer.cs:173–187`). A method code is not paid authority. No successful paid-placeholder behavior is claimed. |
| `DataAuthorityViolationHardeningTests.AffiliatePayoutDecisions_MustUseSqlLedgersNotMongoMoneyProjections` | Expects a direct controller balance call. Existing controller delegates request creation to SQL-backed service, which owns transaction/balance validation (`AffiliatePayoutService.cs:430–478`). No payout workflow was exercised here. |
| `AffiliateGovernanceCertificationTests.AffiliateTokenGeneration_MustNotEmitTenantIdClaim` | Missing method end marker `private async Task IssueVerificationEmailAsync`; source-extraction failure, not a reproduced token leak. Unresolved here. |
| `AffiliateWithdrawalRequestAuthorityCertificationTests.CertificationArtifacts_MustRecordPassAfPa61` | Historical certification artifact lacks literal `PASS AF-PA6.1`; artifact deliberately not rewritten. Unresolved. |
| `AffiliateOrderLineageAdminPresentationAuthorityCertificationTests.FrontendOrders_MustAdmitAffiliateEvidenceBeforeRendering` | Missing literal “Affiliate Attribution” in frontend source. Presentation contract remains unresolved. |
| `AffiliateA11GapClosureCertificationTests.InventoryReservationOutbox_MustBeGuardedAndCommittedWithOrderTransaction` | Missing expected inline inventory-reservation guard expression. Retained as unresolved; live reservations do not certify every outbox failure case. |
| `SearchSeoPublicIndexCertificationTests.LegacyAffiliatePortalDiscoveryStaysSqlAuthorityBound` | Missing expected inline recomposition expression. Unresolved, not waived as safe. |
| `GlobalReliabilityInfrastructureExtractionTests.SearchAndAffiliateRepairPaths_MustRecomposeCurrentAuthorityBeforeVisibilityMutation` | Missing `parent.Level == 1` literal. Unresolved. |
| `AffiliateProductEligibilityLifecycleCertificationTests.AffiliateDiscovery_ReadAdmission_RequiresActiveCategoryAuthority` | Missing inline `AffiliateCategories` marker. Unresolved. |
| `AdminAffiliateCategoryGovernanceTests.GlobalAffiliateCategoryMutations_MustRequireSuperAdminPolicy` | `Assert.NotNull` failure. Authorization contract remains unresolved; no unauthorized mutation attempted. |
| `AffiliateFrontendAuthorityCertificationTests.AffiliateFrontendSessionCache_MustRemainProfileCacheOnly` | Missing exact direct login-navigation literal. Unresolved source-shape contract. |

Separately, the timed-out broader run reports `PaymentLifecycleTests.Controllers_ShouldNotDirectlySetPaymentStatus`: its regex matches `order.PaymentStatus == …` comparisons at `AdminManualPaymentController.cs:112`, treating equality as assignment. Source inspection confirms comparisons, not mutations, at the reported expression. The test was not edited; the broader run still has **no complete final count**.

## Preservation and retained side effects

Read-only full-row SQL digests: `baseline-expanded.json` → refreshed `final-fingerprints.json`; comparison script `compare_preservation.py` → `preservation-comparison.json`.

| Authority | Before → after | Preservation result |
|---|---|---|
| Orders / items | 62 → 64 / 62 → 64 | All 62 original rows in each unchanged; exactly two Phase 4 additions. |
| Attributions | 9 → 11 | All nine original rows unchanged; one new row per new order. |
| Saved links / profiles / policies | 5 → 5 / 4 → 4 / 3 → 3 | Every inventoried row unchanged. |
| Commission / balance / payout-request rows | 10 → 10 / 0 → 0 / 0 → 0 | Every original unchanged; no additions. |
| Accounting journals / ledger lines / accounting outbox | 220 → 220 / 459 → 459 / 30 → 30 | Every original unchanged; no additions. |
| Inventory reservations | 55 → 57 | All 55 originals unchanged; two new one-unit reservations. |
| Variant stock | 127 → 127 | Only the two participating stock rows changed: reserved 1 → 2, mutation version +1 and update timestamp. Physical quantity unchanged. |
| Shipments / payment intents | 28 → 28 / 34 → 34 | Every original unchanged; no additions. |
| Refunds / refund lines | 6 → 6 / 6 → 6 | Every original unchanged; no additions. |
| Phase 3 report/evidence | 121 SHA-256 comparisons | **All unchanged**. |

Protected Phase 3 orders **`ORD-83B3-2026-01091`** and **`ORD-C43A-2026-01093`**, including their original item/attribution/reservation rows, remain untouched. No fulfillment, collection, cancellation, customer contact or rewriting was performed. The protected-order comparison is true; stock increases above belong to the **additional** Phase 4 reservations, not mutations of Phase 3 reservations.

The requested settlement-table names `CodSettlementOrders`/`ManualWalletSettlementOrders` were not present in the fingerprint query's returned table inventory; **no global settlement-table equality claim is made for absent tables**. Uninventoried audit/session/click/projection tables are not certified row-for-row. Ordinary checkout/session/idempotency/click/projection side effects may exist and were retained, not cleaned up. Full-row original order/item/attribution/reservation checks and financial-ledger fingerprints are the preservation evidence actually captured.

The API was restarted with the existing Phase 1 safe launcher, restricting process-local SMTP to **`127.0.0.1:2527`**; notification readiness query returned no participating WhatsApp accounts. No external provider or notification dispatch was initiated. Existing credentials were used only for local authentication. Credential-bearing CLI echoes remain in approved private temporary storage, not repository evidence; `affiliate-before.txt` is sanitized. Final evidence-pattern scan found no JWT values or password-fill literals in scanned Phase 4 text artifacts (`final-review.json`); this is a bounded scan, not a repository-wide secret audit.

## Remaining decisions and safe handoff

- To complete headed qualifying certification, provide appropriately scoped merchant access and an approved **trusted, non-external simulation/verification workflow**. Do not replace either with a forged claim/header, raw SQL update, manual ledger insert, fake carrier fact or override switch.
- Live earning/approval/refund/accounting convergence, positive cross-merchant approved availability and successful live discount coverage remain unexecuted. Synthetic pending orders and isolated SQL fixtures do not certify those live workflows.
- The established tax-inclusive base, snapshot entitlement after later suspension, per-unit cap, and proportional refund of capped commission are documented commercial rules, not newly negotiated policy. Any requested alternative needs an explicit policy decision and separate implementation/migration review; do not retroactively rewrite existing snapshots.
- No new cooling-off/release delay, cancellation-without-refund clawback policy, currency conversion or post-payout recovery policy was invented. Physical return alone must not be confused with authoritative refunded amounts; the existing refund/payment lifecycle remains responsible for durable allocation.
- Existing source-contract failures and incomplete broad run prevent an unconditional repository-wide green verdict. Build chunk-size/circular-import/Browserslist/legacy ESLint warnings were not remediated as unrelated work.
- The headed session was retained. The updated API was launched for verification with loopback SMTP; it was not deliberately stopped as cleanup. No unrelated user-owned process was terminated.

**Final status: scoped fixes and evidence complete; complete live Phase 4 qualification BLOCKED. All work remains unstaged and uncommitted. Stop before withdrawals/payouts.**
