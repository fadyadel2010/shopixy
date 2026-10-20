# Shared Storefront checkout HTTP 500 remediation

Requested report date: **2026-10-19**. Windows-local execution continued into **2026-10-20**; evidence timestamps also retain UTC.

## A. Executive verdict

**CONDITIONAL — the final Windows development implementation passes the four checkout cases, real payment-review workflows, financial readbacks, and interrupted COD recovery. The original Nissan failure was not directly reproduced or repaired. Three unrelated existing source/report assertions still fail.**

- Final ordinary COD and Manual Wallet checkouts, without coupons, each accepted and persisted **EGP 560.00**.
- Final coupon COD and Manual Wallet checkouts each accepted and persisted **EGP 532.50** using explicitly synthetic 5% QA coupons, not `1441`.
- Both successful wallet orders were approved through real Admin UI. A concurrent decision race produced **one 200 / one 409**, with one settlement journal per order.
- Genuine synthetic underpayment **550.00 against 560.00** produced **409**, remaining balance **560.00**, shortfall **10.00**, and no allocation/accounting effects before explicit rejection.
- An interrupted, already-committed COD checkout recovered its **same order ID**, reference, and payment-pending state. No duplicate was submitted to compensate for the failure.
- **235/235** selected backend regression tests passed, plus the separately authorized underpayment-admission fixture. **61 Admin tests** and **53 Storefront tests** passed. **116/116** final financial/preservation/projection checks passed over **10 retained QA orders, 4 posted balanced journals, and 10 Mongo projections**.
- Manual Wallet was safely restored to **disabled, empty public configuration, no encrypted recipient**. COD remains enabled/version13. Both task coupons are inactive; history is retained.

This certifies the code in the **current development environment**. No SSH, tester Firefox interaction, tester stack operation, or alternative tester database access occurred, including after the optional tester credentials were offered. Existing Windows-configured SQL/broker services at `192.168.1.5` were used normally; no other database or stack on that host was selected. The original Nissan order is **not claimed repaired**.

## B. Baseline and reproduction

### Workspace, processes, configuration, and authority

Evidence below is relative to **`.ai/evidence/shared-checkout-20261019/`** unless otherwise stated.

| Concern | Established baseline / final identity |
|---|---|
| Primary workspace | `C:\Users\fadya\source\repos\PointOfSale`; same directory throughout |
| Git HEAD | `5bd9bfc0e57c23116cd1c4fba456bf0c9f7ccad4`; existing substantial modified/untracked work and tracked artifact deletions preserved |
| Before-state evidence | `workspace-before.txt`, `existing-work-hashes.json`, `sql-before.json`, `browser-before.txt`, `runtime-before.json`, `environment.json` |
| Admin | `http://localhost:5173`; final authenticated store confirms `ahramco.software@gmail.com` |
| Storefront | `http://ahramstore.localhost:5174`; supplied customer `fadyadel2060@gmail.com`, customer ID `3e6129bf-5bac-4d26-86da-c7c42cb53833` |
| Tenant | AhramStore, `c9c5c1ae-949e-498e-bc63-07242daeb49d`, EGP; correct StoreConfig endpoint returned200 |
| API / routing | Development API `localhost:5010`; Vite `/api` proxy preserves Host; backend `CheckoutController` route `/api/storefront/checkout`; requests include `?lang=en` |
| Initial API | PID2836; normal `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`; initial API DLL SHA256 `38742F1BEF35272AD1AD4B37A673070655A61155D19682933A442A577833E6E5` |
| Final tested API | PID17124; normal Debug development output; API DLL SHA256 **`785D27CC1DE6247E0B4FF0078ADCBCD97BE3359146DEF0D268EC4D0F5F6BDC31`** |
| Fresh-build proof | `runtime-corrected.json`: process started after corrected DLL build. `runtime-closeout.json`: same live process, unchanged API hash and checkout source hash after final tests |
| SQL | Configured PostgreSQL `192.168.1.5:5432 / PosEcommerceDb_Baseline`; new API-created order IDs independently read there |
| Mongo | Configured `mongodb://192.168.109.128:27017`, `ecommerce_read_db`, `orders`; all10 QA IDs independently observed |
| Redis / broker | Configured Redis `192.168.1.5:6379`; RabbitMQ `192.168.1.5`, vhost `/`; existing application services retained |
| Storage | Existing Development object-storage implementation, `secure-uploads/development-object-storage`; actual marked PNG upload and Admin proof viewing, no real transfer/provider charge |
| Original payment methods | COD enabled/version13. Manual Wallet row `8596894a-df67-44c5-8e0e-f64372bc1d74`, disabled/version18, no encrypted configuration and no verified recipient. Other provider configuration untouched |

SQL observations use **REPEATABLE READ, READ ONLY**. Connection passwords, supplied account passwords and tokens are not included in this report or task scripts. Configuration-file hashes, not secret values, are recorded. No configured database was reset, replaced, recreated, migrated, or broadly restored.

Exact `ORD-A758-2026-01040`: **absent in SQL and Mongo**, including exact-number lookup without hiding a foreign-tenant match. Coupon `1441`: **absent in configured SQL**. No Nissan tenant or exact `IPHONE-18-BLACK` SKU was found by the narrow lookup. These absences do not diagnose the tester database and did not block shared-path investigation. No matching original records were invented.

### Actual local baseline

1. **Before implementation changes**, real no-coupon COD checkout created `ORD-C9C5-2026-01072`, HTTP200, total560; no wallet intent or journal.
2. After narrowly recording the original disabled/empty wallet, the real Admin configuration UI temporarily saved synthetic recipient `01000000000` and enabled it. Real no-coupon wallet checkout created **01073**, HTTP200, order/intent560 and actual test-only proof. Its pending state later expired naturally during diagnosis; it was not resurrected or force-approved.
3. Therefore, an ordinary deterministic pre-change HTTP500 was **not reproduced** for either method. The screenshot's missing original failure is not relabeled as a local reproduction.
4. Source inspection continued despite the passing baseline. A controlled configured-PostgreSQL/MassTransit probe reproduced the shared outbox lifecycle defect described below.
5. An **intermediate implementation regression**, its HTTP500, persisted order01074, and user-supplied exception are explicitly retained. It is not counted as a passing final test or as evidence that the tester's exact issue was reproduced.

Primary captures: `baseline-cod-submit.txt`, `baseline-wallet-submit.txt`, `sql-baseline-cod.json`, `sql-baseline-wallet.json`. Actual request captures include tenant/customer/session, cart, checkout-session, warehouse resolution/version, shipping selection, payment method, expected pricing, and response identity.

### Existing fixes inspected and preserved

Already present before this task: typed coupon reservation rejection and controller mapping; SQL-backed reservation-aware coupon preview; net `OrderItem.PayableAmount` / remaining balances and allocator; Manual Wallet full-order settlement guard; valid-recipient admission/configuration protections; pending-wallet ordinary-order exclusion; fresh scoped wallet admission retries; persisted-proof replay; explicit tenant-scoped background coupon release; durable order/idempotency binding and active-cart uniqueness protection. These were not blindly reverted or reimplemented.

## C. Root cause and fixes

### 1. Verified common order-creation retry defect

The shared `CreateOrderAsync` path used a serializable SQL transaction and bounded retry, but reused its **request-scoped MassTransit publisher** after rollback and `ChangeTracker.Clear()`.

MassTransit8.5.7's scoped EF bus context retains an outbox state entry. If rollback occurs **before the first outbox flush**, that entry is not a successfully saved `Unchanged` entry. Clearing EF tracking alone does not reset the publisher. The next publish can add an `OutboxMessage` referencing a missing `OutboxState`.

Configured-database evidence:

- `SharedCheckoutOutboxConfiguredTests.RollbackBeforeFirstOutboxFlush_CannotBeRecoveredByClearingOnlyEfTracking`: actual **PostgreSQL23503**, constraint **`FK_OutboxMessage_OutboxState_OutboxId`**.
- The fresh-scope counterpart succeeds and independently confirms the probe state IDs are absent after rollback.
- The simpler **flush-before-rollback control did not reproduce this failure**. Its initial failed expectation is retained in `outbox-verified.log`; it was refined to the actual vulnerable lifecycle, not presented as corroborating evidence.
- Final passing probe output, including the PostgreSQL exception, is in `trx/backend-corrected-regression.trx`. Earlier `outbox-before-flush.log` also records passing bounded/probe checks.

This is a common retry defect: stock/order/event creation uses it for COD and wallet, regardless of coupon presence. It is **not** proof of a naturally occurring40001 in this run or of the missing tester request's cause. The first rollback in the probe deliberately models interruption before flushing; it does not claim a real external provider operation failed.

**Fix:** each order-creation attempt resolves a fresh tenant-scoped checkout service, DbContext and publisher. The same planned order ID, request ownership, immutable quote checks and durable idempotency binding remain. Only typed PostgreSQL40001/40P01, or the existing typed SQL Server1205 deadlock case, qualify for at most3 local attempts with bounded delay. Cancellation, business/integrity failures and failed-rollbacks represented by aggregate exceptions are not retried. Exhaustion produces the existing actionable409 concurrency contract. No payment-provider execution is inside this retry.

### 2. COD post-commit gap and recovery

Previously, offline reference initialization and the payment-pending transition required a **second SaveChanges after order commit**. An interruption could leave a correct persisted COD order with no reference. The default replay branch only attempted repair when a reference already existed, so a reference-less order could not produce a safe recovery response.

**Fix:** persist the COD/offline reference, `PaymentPending` checkout state, and existing deterministic payment-session outbox event **inside the order transaction**. COD does not initialize a wallet intent or invoke a provider. Legacy interrupted orders resume under a tenant-scoped SQL row lock, using current persisted status and active inventory authority; expired/finalized orders are not resurrected. Recovery never creates a second order or a wallet intent.

**Real recovery proof:** committed01074 initially had no reference and an incomplete durable binding. After inspecting SQL/logs and loading the corrected implementation, one exact browser-originated replay returned **200, same `297dd7e8-be38-454f-8c80-b6872417d93b` / 01074**. SQL changed to `PaymentPending` with a persisted COD reference; no duplicate order, payment intent or financial posting. `interrupted-replay-readback.txt`, `sql-before-replay.json`, `sql-after-replay.json` establish this while its reservation was still live.

### 3. Intermediate regression caught and corrected, not concealed

The initial scope-isolation implementation attached the transferred committed aggregate to the request context. PostgreSQL's order concurrency token is an **EF shadow `xmin` property**; attaching that aggregate lost the token. The first post-commit COD update therefore produced `DbUpdateConcurrencyException` and HTTP500 despite committed01074.

Trace **`7e8f997c4f94c33d19e7ee95db8c7831`**; stage **PaymentIntent**; actual message: “The database operation was expected to affect1 row(s), but actually affected0 row(s).” Selected user-supplied console evidence is in `user-supplied-api-log-extract.md`; failed HTTP capture is `final-cod-submit.txt`, and committed state is `sql-final-cod.json`.

The final implementation **reloads SQL order/items/field snapshots and their concurrency authority**, rather than attaching a detached graph. A configured read-only regression verifies detached attach has zero shadow `xmin`, while a tracked SQL load has a valid token. The commit flag is established before post-commit validation, preventing that later failure from being misclassified as a rolled-back order.

### Files changed by this task

| File | Purpose |
|---|---|
| `PointOfSale.API/Services/Checkout/CheckoutService.cs` | Fresh tenant-scoped commit dependencies; reload committed SQL concurrency authority; atomic offline readiness; locked interrupted-offline replay; post-commit boundary correction. Existing coupon/wallet fixes preserved |
| `PointOfSale.API/Services/Checkout/CheckoutOrderCommitRetry.cs` | Small, bounded, typed local SQL-conflict policy; no external/provider retry |
| `PointOfSale.Tests/Payments/CheckoutOrderCommitRetryTests.cs` | Bounded serialization/deadlock retry, exhaustion409, nonretryable integrity failures, cancellation and failed-rollback safety |
| `PointOfSale.Tests/Payments/SharedCheckoutOutboxConfiguredTests.cs` | Real configured SQL outbox reproduction, fresh-scope rollback isolation, shadow-xmin load regression |
| `PointOfSale.Tests/Architecture/CheckoutConcurrencyHardeningSourceTests.cs` | Replace the directly affected tracker-reset expectation with fresh-scope/tenant/reload guarantees; add COD transaction/recovery assertions. Unrelated historical assertions unchanged |
| `PointOfSale.Tests/Payments/SharedCheckoutUnderpaymentConfiguredTests.cs` | Explicit opt-in new underpayment fixture through established runtime/evidence/review admission, followed by actual Admin decisions |

Task-only automation/readbacks: `tests/browser/shared-checkout-20261019-*.js`, `tests/shared-checkout-20261019-*.py`, `tests/quality-closeout/shared-checkout-20261019-*`. No migration, new financial evaluator, frontend production-source change, accounting rewrite, or provider redesign.

The authority flow remains: actual Storefront request → authenticated tenant/customer/session and owned cart → SQL variant/configuration/pricing revalidation → serializable order/stock/coupon/intent-authority/event/idempotency commit → local payment admission/recovery → durable response. Redis/Mongo are not financial authorities. Optional post-commit telemetry/messaging cannot roll back an existing order; projections are independently read back.

## D. Browser and automated results

### Passed — final headed Edge workflows

Reused registered **`opencode-ui-test`**, headed isolated msedge, finite CLI timeouts. No mocked checkout, coupon, review, approval, order creation or financial responses were used. User restarted the normal Windows API after source builds. All final cases below use the corrected PID17124 build; code/DLL identity remained unchanged through closeout.

| Case | Pricing / checkout HTTP | Persisted outcome | Primary capture / screenshots |
|---|---|---|---|
| A COD, no coupon |200 /200 |**01075**,560, Pending/unpaid; no wallet intent | `verified-cod-submit.txt`, `verified-cod-none-review.png`, `verified-cod-none-outcome.png` |
| B Wallet, no coupon |200 /200 |**01076**, order/intent560; pending review, then Processing/Collected after real approval | `verified-wallet-submit.txt`, `verified-wallet-approval.txt`, `shqa-wallet-none-pending.png`, `shqa-wallet-none-proof.png`, ordinary-before/after and decision screenshots |
| C COD, QA coupon |200 /200 |**01077**,532.50;27.50 discount once, unpaid, no wallet intent | `verified-coupon-cod-submit.txt`, `verified-cod-coupon-review.png`, outcome screenshot |
| D Wallet, QA coupon |200 /200 |**01079**, order/intent532.50; concurrent UI approval, Processing/Collected | `ordinary-coupon-wallet-submit.txt`, `coupon-concurrent-ui-approval.txt`, `verified-wallet-coupon-review.png`, `shqa-wallet-coupon-*` screenshots |
| Interrupted COD exact replay |200 |Same01074, live reservation, restored reference/state, no duplicate | `interrupted-replay-readback.txt`, before/after SQL, `interrupted-cod-recovered.png` |
| COD after wallet restoration |200 /200 |**01080**,35, Pending/unpaid, ordinary Orders visible; wallet absent from public methods | `restored-cod-submit.txt`, `restored-cod-none-*`, `restored-cod-ordinary-orders.png`, `browser-closeout-raw-isolation.txt` |

Additional executed workflows:

- **Coupon apply/refresh/remove/reapply/quantity:**10 actual UI assertions passed.550→27.50 discount; qty2 gives1100−55=1045 merchandise; qty1 restores27.50. Invalid and unavailable1441 submissions return400. Applying the coupon already reserved by01077 returns controlled400 with **`COUPON_RESERVATION_EXISTS`** and actionable cart feedback. `verified-coupon-revalidation.txt`, `coupon-reservation-conflict.png`, `coupon-revalidated.png`. The final-admission race's409 mapping is regression-tested; a new HTTP commit-time reservation race was not forced here.
- **Stale candidate experiment:** `conflicting-coupon-submit.txt` is deliberately **not** counted as a rejected conflict. A request-only negative test changed its coupon candidate to the reserved COD code; the real backend correctly used the owned cart's persisted **WALLET** coupon, creating01078 at532.50 with that coupon. No financial response was mocked. SQL proves no duplicate/foreign discount. That synthetic order was explicitly rejected through Admin, then a separate untouched ordinary UI submission created final01079. See `sql-conflicting-coupon.json`, `stale-candidate-order-rejected.txt`.
- **Pending exclusion:** exact Admin list searches return zero before wallet approval; actual pending row and marked proof visible in Manual Payments. After approval, navigation/refresh shows Processing/Collected in ordinary Orders. Real SQL/read projections agree.
- **Concurrent/stale decisions:** two actual Admin tabs confirming01079 produce one200 and one409. Subsequent stale confirm/reject each409. `coupon-concurrent-ui-approval.txt`, `replays-stale-tenant.txt`; no second settlement journal.
- **Checkout idempotency:** two concurrent exact replays return the same01079/order ID/532.50, including after its cart cleared and order progressed. SQL has one order, intent, confirmed usage and settlement business key.
- **Underpayment:** canonical fixture initializes a **new550** intent against560; Storefront has no transfer-amount field and none was invented. Real Admin approval409 reports remaining560.00/shortfall10.00. `underpayment-ui-decision.txt`, `shqa-underpayment-decision.png`, `sql-underpay-noeffects.json` prove still-awaiting-review/no allocation/journal/outbox before explicit UI rejection. No existing payment amount was rewritten.
- **Tenant isolation:** authenticated Admin foreign-header order/proof/decision requests403; two raw Storefront foreign-header requests403. The normal Storefront client itself overwrites a supplied foreign tenant with canonical AhramStore and therefore returns same-tenant200; raw `fetch` was used to test actual server spoofing rejection. Configured two-tenant rollback tests also passed. No second-tenant account UI login is claimed.
- **Restoration:** real confirmed reset UI200; wallet disabled/empty after reload, SQL encrypted value absent; only COD publicly available. With wallet disabled, the pending-review API correctly returns404 **`MANUAL_PAYMENT_PROVIDER_DISABLED`**, not a regression. Before reset, pending list was empty. `configuration-restored.txt`, `browser-closeout-raw-isolation.txt`, `final-wallet-disabled.png`.
- **Console/network:** actual Axios/network captures include quote and checkout status/body. Successful final submissions recorded no page exceptions. `final-browser-console.txt` records zero errors/two existing bootstrap/i18next warnings at that checkpoint. Negative400/403 probes intentionally generate network console errors; these are not described as an entirely error-free browser session. No submission500 was blindly repeated.

### Passed — automated checks

| Check | Actual result / evidence |
|---|---|
| Selected backend checkout, order/COD guards, pricing, coupon reservation/allocation, wallet lifecycle, idempotency, concurrency, tenant isolation and affected architecture regressions | **235passed,0failed,0skipped**; `backend-corrected-regression.log`, TRX and exact filter/assembly hash manifest |
| Separate explicitly authorized underpayment admission | **1passed**; `underpay-admission.log`, TRX, `underpay-manifest.json` |
| Admin ManualPayments / phase5 tests | **10passed,2files**; `admin-tests.log` |
| Admin Orders / adminOrdersApi / PaymentMethods tests | **51passed,3files**; `admin-orders-tests.log` |
| Storefront checkout validation/closeout/cart persistence | **53passed,3files**; `storefront-tests.log` |
| Corrected normal backend Debug build | **PASS,0errors**; `backend-corrected-development-build.log`; actually loaded for final UI tests |
| Latest Release backend/test build | **PASS,0errors**, existing warnings retained; `latest-tests-build-corrected.log` |
| Admin production build and separate TypeScript check | **PASS**; `admin-build.log`, `admin-typecheck.log`; existing large-chunk warnings retained |
| Storefront `tsc && vite build` and separate typecheck | **PASS**; `storefront-build.log`, `storefront-typecheck.log`; existing bundling/dependency warnings retained |
| Task browser-helper lint | **PASS**,13files,0errors/0warnings; `browser-lint-final.log`, JSON and command/source hashes; repository export-wrapper adapter/rules |
| New retry helper C# whitespace verification | **PASS**, absolute include path, no file changes; `backend-format-verified.log`. Not whole-repository formatter certification |
| Python helper syntax / changed-source Git whitespace | **PASS**; `python-syntax.log`; actual `git diff --check` execution; normal LF/CRLF notices retained |
| Financial/projection/configuration/preservation checks | **116passed**; `financial-check-verified.log`, `financial-consistency-summary.json`, `mongo-closeout.json`, `preservation-check.json` |

Counts are not added to overlapping intermediate backend runs. Earlier235-related probes and232-test runs are historical intermediate evidence, not additional unique tests. The first frontend commands also named nonexistent optional files; only the actually discovered files/counts above are claimed.

### Failed — retained separately

`historical-source-contracts.log` / TRX: **4passed,3failed**. Unchanged existing failures:

1. `CouponFinancialAuthorityCertificationTests.FrontendCouponFinancialPresentation_MustSubmitCandidatesOnly_AndRenderBackendPricing`: obsolete literal response-variable string expected in unchanged `checkoutStore.ts`.
2. `CouponFinancialAuthorityCertificationTests.CertificationArtifacts_MustRecordCouponFinancialAuthorityTruth`: expects `PASS SF-CPN5` in an unrelated historical report.
3. `AdminPaymentMethodReadAuthorityCertificationTests.PaymentMethodReadRuntime_MustRemainBackendOwnedAndTenantScoped`: expects already-absent `SetNoStoreHeaders();` in unchanged Storefront payment-method controller.

Those source/report files were not rewritten to manufacture green certification.

Task development failures also remain visible: initial probe namespace/closure setup errors; initial flushed-rollback expectation did not fail as hypothesized; first underpayment-harness missing imports; intermediate attach/xmin COD500; initial financial capture reader mishandled Windows CRLF/BOM; browser automation stopped on a cart-clear confirmation and made an early cart read before bootstrap; final read-only helper initially treated the deliberately disabled provider's404 as unexpected. They were corrected or explained, not counted as passing runs. Persisted state was checked before any checkout replay.

### Blocked / unexecuted — not passes

- Exact Nissan order/coupon1441/iPhone18 screenshot reproduction: unavailable in the selected environment; tester access deliberately not used in this run.
- Suites that provision/reset disposable PostgreSQL databases, including original COD/wallet/coupon-concurrency integration fixtures: not run under the database restriction. Configured rollback probes, focused safe tests and real UI concurrency/COD were used instead.
- Full repository suite/lint, exhaustive process/broker/storage faults, every mixed eligibility/promotion/currency variant, exhausted-coupon browser permutations, and a forced simultaneous first-submit double-click race: not executed. Existing UI busy-disable, bounded-policy tests and actual concurrent exact replays do not certify all such interleavings.
- A new live HTTP serialization/deadlock fault was not injected; controlled real-SQL bus-outbox lifecycle reproduction and retry-policy regressions establish that narrower fix.
- Second-tenant account browser login, external provider charges/transfers, carrier bookings and unrelated integrations: not executed. No absence of external charges is used as a substitute for internal financial testing.

## E. Financial consistency

The screenshot arithmetic110000−5500+25=104525 is consistent, but does not establish unavailable original persisted financial values. Local tests use real current catalog prices, not a fabricated screenshot-scale product.

All values below EGP; tax/payment fees0. Accepted quotes, SQL totals and intents agree.

| Final case | Gross | Coupon / net line | Shipping | Quote = order = intent if wallet | Allocated line after decision | Remaining payable |
|---|---:|---:|---:|---:|---:|---:|
| COD01075 |550 |0 /550 |10 |560; no wallet intent |0 |560, unpaid |
| Wallet01076 |550 |0 /550 |10 |560 |550 |**0** |
| Coupon COD01077 |550 |27.50 /522.50 |10 |532.50; no wallet intent |0 |532.50, unpaid |
| Coupon wallet01079 |550 |27.50 /522.50 |10 |532.50 |522.50 |**0** |
| Restored-config COD01080 |25 |0 /25 |10 |35; no wallet intent |0 |35, unpaid |

Main product: iPhone17 Pro Max blue/256, SKU `IPHONE-17-PRO-MAX-BLUE-256`, variant `60f8b79a-42a0-4f44-8271-507054044ba9`; SQL price550, OnlinePrice null. Final independent restored-COD regression uses existing `CERT-MEDIA-S-20261007`,25. No product price or stock quantity was edited to obtain these results. Standard Shipping `0f1a22f9-e017-4443-b94e-d37053fa7bca` charges10 and uses manual fulfillment. Existing authoritative currency rounding and proportional line allocation remain unchanged.

Successful wallet accounting:

| Order | Wallet journal / amount | COGS journal / amount |
|---|---|---|
|01076 |`01a1560a-8253-7790-8706-f6c5b2993bf3`,560.00 |`01a1560a-aa3d-7c44-bd48-db607e55296c`,291.42 |
|01079 |`01a1560e-1cc7-7901-929c-ecab19d8023f`,532.50 |`01a1560e-6a31-7e44-a54e-63eb99431977`,291.42 |

Each journal is Posted with debit=credit and a unique business idempotency key. Wallet settlement uses **Dr1140 / Cr4020** at accepted order total; actual stock commit separately uses **Dr5010 / Cr1310** for291.42. These are different postings, not duplicate settlement. Corresponding accounting outbox receipts are processed and point to the same journal IDs. Retried/stale/concurrent decisions and configuration restoration preserve these posted histories.

Merchandise allocations stop at net line due. Shipping remains an order-level charge, covered by the accepted payment; it is not allocated a second time to merchandise. After each valid approval, accepted Paid intent total covers the full order and authoritative remaining payable is0. The previous unused item-only `Order.BalanceDue` compatibility utility was not redesigned; it is not used as settlement authority here.

Underpayment fixture `SHQA-20261019-UNDERPAY-2d59dc7c` has authoritative560 but a deliberately **new550** intent. Before explicit rejection it remains AwaitingVerification after approval409, itemPaid0, no accounting outbox/journal. Stated amount is unchanged. COD/rejected/expired QA orders have no successful allocation or settlement journal. Coupon usage is confirmed exactly once on01079; pending COD01077 retains a reservation, not a confirmed usage. Every observed task custom outbox row was processed at closeout; no task operator-reconciliation flag/pending custom row was found. Ten SQL/Mongo order identities and financial states agree.

## F. Data preservation and restoration

### Configuration and coupons

| Stage | Wallet enabled | Recipient | Version |
|---|---|---|---:|
| Original |false |none; no encrypted configuration |18 |
| Controlled QA |true |synthetic `01000000000` |20 |
| Final confirmed reset |**false** |**none; public{} and encrypted value absent** |**22** |

There was no verified original recipient to restore. Reset-and-disable used the actual Admin UI and normal version history; versions/audits were not rewritten backward. COD remains enabled/version13. Provider secrets/configuration were not broadened or substituted.

Synthetic unrestricted5% coupons, both final inactive:

- `SHQA20261019COD`, ID `45dd0837-0945-4c59-bff9-6b5afdb5d37f`, CurrentUsage0; pending01077 reservation/history retained.
- `SHQA20261019WALLET`, ID `88376219-3793-47a4-9a16-6a869057ff83`, CurrentUsage1; rejected01078 reservation released, accepted01079 confirmed exactly once.

### Retained task records

| Purpose / number | Order ID | Final observed state |
|---|---|---|
| Baseline COD01072 |`7b829518-102e-4797-bb16-8e3832018e55` |Pending/Pending,560 |
| Baseline wallet01073 |`599e4784-1b0a-4630-b5b4-d08b4a8732fb` |Cancelled/Expired,560; no postings; cleanup observed existing terminal state |
| Interrupted/recovered COD01074 |`297dd7e8-be38-454f-8c80-b6872417d93b` |Pending/Pending, PaymentPending/reference restored,560 |
| Final COD01075 |`0a680841-adb3-4cd7-84d3-335d7e175078` |Pending/Pending,560 |
| Genuine underpayment |`2d59dc7c-4b3c-4e0b-81eb-50ec3e6e88b0` |Cancelled/Rejected;550 intent unchanged against560; no postings |
| Final no-coupon wallet01076 |`9f10fe3f-8e61-446a-a00f-4078667c34df` |Processing/Collected,560 |
| Final coupon COD01077 |`4d16b930-1830-4e20-87bc-0dc1c3b31d9f` |Pending/Pending,532.50 |
| Stale-candidate experiment01078 |`e479f45a-f793-4c8e-8b27-73e8954af163` |Cancelled/Rejected,532.50; no postings |
| Final coupon wallet01079 |`0aa6a73a-c116-4f34-adfe-00428b34ae76` |Processing/Collected,532.50 |
| Restored-config COD01080 |`5ab9f08d-b87a-4136-b321-bcd07c4411af` |Pending/Pending,35 |

Final wallet intent IDs:01076 **`eb2fe5fd-6a3f-4852-bce6-aad830cfc5d5`**;01079 **`38eed3c2-ff1e-4eb2-ad26-4ec570f7f3ad`**. Underpayment **`c531fe1b-2a72-4523-80fa-41510c165c38`**. All other intent/proof/reservation/session/outbox IDs remain in `sql-closeout.json` and the financial summary.

Synthetic image **`TEST-ONLY-NOT-A-REAL-RECEIPT.png`** visibly states **TEST-ONLY — NOT A REAL RECEIPT**, no transfer/no delivery. Four actual checkout proof uploads are retained under the tenant's `manual-wallet-proofs/checkout-sessions/` paths for01073/01076/01078/01079; the underpayment fixture explicitly reuses this task's01073 synthetic proof through real evidence admission. No original receipt was altered or represented as a real transfer.

Pending synthetic COD orders are clearly marked TEST ONLY/no transfer/no shipment; no fulfillment action or real carrier booking was initiated. Successful journal/history/proof/audit records are retained, not deleted or rewritten to conceal QA. Temporary wallet recipient removal and coupon deactivation are the scoped cleanup; no broad database or storage restore occurred.

`preservation-check.json` verifies the previously captured modified/untracked source/report files stayed byte-identical except the intentionally extended `CheckoutService.cs` and this task's own observer. The directly related concurrency source-test update is separately listed above. Pre-existing wallet/coupon fixes, unrelated frontend/chat/scheduler/test work, historical reports/evidence and artifact deletions remain. No staging, commit, reset, clean, stash, discard or broad deletion was performed. This is a narrow preservation attestation, not a claim that normal asynchronous workers made no database changes.

## G. Remaining limitations and handoff boundary

1. The original Nissan request, tenant configuration, order history, coupon1441 and104525 screenshot total remain **unverified here**. Their absence is not proof of a tester-side data condition. The optional remote access offer was not used; deployment/retest of that specific environment remains later work.
2. The three unrelated historical/source-string failures in sectionD remain failed. Full-repository green certification is not claimed.
3. The shared retry defect was reproduced through the actual configured SQL/MassTransit preflush rollback lifecycle, not a naturally recurring initial HTTP500. The intermediate xmin regression was introduced during this task, caught by mandatory UI testing, corrected, and independently regression-tested; it is not attributed to the original tester build.
4. Exhaustive failure injection and every coupon/provider/interleaving remain outside the executed coverage. In particular, the cart-level reservation conflict was exercised; a fresh live commit-time conflict race was not forced. No unexecuted case is called passed.
5. A persistent “Unsaved changes” autosave banner was visible during some rapid form edits even though authoritative final checkout/session admission succeeded. One early helper read before bootstrap was rejected403. These are retained UI/harness observations, not an invented shared500 root cause; no unrelated frontend persistence redesign was undertaken.

**Final established outcome:** ordinary COD and Manual Wallet checkout work with and without the selected QA coupons in the current Windows development application/database; valid wallet approval, genuine underpayment rejection, concurrency, stale decisions, idempotency, tenant boundaries, financial read projections and safe configuration restoration have actual post-fix evidence. The Windows API remains running on the verified corrected build. The missing original Nissan order was not directly repaired.
