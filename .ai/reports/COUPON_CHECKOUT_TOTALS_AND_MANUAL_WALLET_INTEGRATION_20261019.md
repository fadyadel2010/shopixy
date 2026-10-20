# Coupon checkout totals and Manual Wallet integration — 2026-10-19

## A. Executive verdict

**CONDITIONAL — the demonstrated defects are fixed and the required financial/browser workflows ran against the configured application and database. The exact original screenshot case cannot be certified in this environment.**

The user subsequently specified **AhramStore, not `nissan.localhost`**, as the live testing target. AhramStore resolves to tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`. The configured records contain no `nissan` tenant/domain, no coupon `1441`, and no exact `ORD-A758-2026-01040` in either SQL or Mongo. No unrelated order was substituted as the original.

Confirmed defects repaired:

1. **Coupon preview/commit disagreement:** an unlimited coupon could be accepted while the same customer already held its active reservation on another pending order. SQL correctly rejected the second reservation, but checkout wrapped the domain rejection as an order-creation 500.
2. **Gross versus net item balance:** accepted line discounts were already persisted, but `RemainingAmount`, `IsFullyPaid`, and the public allocator still used gross merchandise. A correct EGP532.50 coupon payment was rejected against EGP550.00.
3. **Post-commit Manual Wallet admission/outbox failure:** the scoped MassTransit bus outbox was reused across separate committed/rolled-back units of work. Natural PostgreSQL `40001` serialization failure, followed by stale `OutboxState` foreign-key `23503`, left a committed order with an unadmitted intent. Local admission now uses a fresh tenant-scoped context/publisher per bounded retry. Incomplete checkout replay resumes the existing intent and its persisted proof, not a cleared checkout-session draft.
4. **Background coupon release:** tenant-agnostic expiration could silently miss the reservation because of the ambient tenant filter. Release now uses explicit tenant/order/deletion predicates, retaining confirmed usage and history.

Final implementation browser proof:

- Fresh coupon wallet order **ORD-C9C5-2026-01070**, EGP532.50: real Storefront checkout/proof, pending review, ordinary-order exclusion, two real Admin tabs approving concurrently (**one200, one409**), Processing/Collected, correct net line allocation, one wallet journal and one COGS journal.
- Fresh no-coupon wallet order **ORD-C9C5-2026-01069**, EGP560.00: real Storefront checkout and Admin approval, correct allocation and accounting.
- Fresh coupon COD order **ORD-C9C5-2026-01071**, EGP532.50: successful persisted checkout, Pending/unpaid, no wallet intent or journal.
- Genuine synthetic underpayment **522.50 against532.50**: Admin approval **409**, correct **10.00** shortfall, still pending immediately afterward, no allocation/journal/accounting outbox. Subsequently explicitly rejected through Admin UI.
- Concurrent checkout replays returned the same approved order and total. Stale decisions409; foreign-tenant read/approval/proof403.
- **140/140 selected backend tests**, **48 Admin tests**, **57 Storefront tests** passed. Separate underpayment admission and expired-QA cleanup tests each passed. Builds/typechecks passed. **89/89 financial/projection/configuration checks** passed across **9 QA orders, 8 balanced posted journals, 9 projections**.

Remaining certification limitations, not disguised as passes: exact original reproduction unavailable; two unchanged historical/source-string architecture assertions fail; exhaustive fault injection and a successful still-live interrupted-intent replay were not executed (the two interrupted QA orders expired safely while fixes/restarts were in progress). Details below.

**Final Manual Wallet state: disabled, public configuration empty, encrypted configuration absent, version18. No synthetic recipient remains.** All three task coupons are inactive; usage/history retained.

## B. Environment and reproduction

### Target and narrow before-state

| Concern | Observed authority |
|---|---|
| Admin | `http://localhost:5173`, authenticated AhramStore Owner session |
| Storefront | `http://ahramstore.localhost:5174`, authenticated supplied customer account |
| Host resolution | StoreConfig200: AhramStore, tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`, EGP; `hostname-tenant-closeout.txt` |
| Original screenshot host | Initial `nissan.localhost` StoreConfig400; no matching tenant slug/domain in configured SQL. No tenant ID could legitimately be assigned to that host here |
| API | Development API on `localhost:5010`, user-restarted after implementation changes; process/build-file evidence retained |
| PostgreSQL | `192.168.1.5:5432 / PosEcommerceDb_Baseline`; same configured database throughout |
| Mongo projection | Configured `ecommerce_read_db`, collection `orders` |
| Original wallet | Row `8596894a-df67-44c5-8e0e-f64372bc1d74`, disabled/version14, no encrypted configuration/recipient |
| COD | Row `5fd1c166-73cd-4235-a127-9efffad10b23`, enabled/version13; unchanged at closeout |
| Original coupon/order | `1441`: zero configured SQL matches. Exact `ORD-A758-2026-01040`: zero SQL and Mongo matches, without hiding another tenant's match |

`sql-before.json` was captured before task configuration/coupon/order mutations using **REPEATABLE READ, READ ONLY**. It includes the target connection identity, narrow tenant/domain/coupon/product/method observations, and exact-number order lookup. No original order was altered. Passwords and connection secrets are not written into task scripts/report.

The screenshot's iPhone18/110,000 terms are not present in the selected AhramStore catalog. Synthetic tests used its actual **iPhone17 Pro Max, blue/256**, product `cc20abdf-0a4f-0278-3c59-c4f10d17bc96`, variant `60f8b79a-42a0-4f44-8271-507054044ba9`, SKU `IPHONE-17-PRO-MAX-BLUE-256`. SQL variant Price550.00, OnlinePrice null, observed UI550.00. No catalog price was changed.

Shipping selected: **Standard Shipping**, `0f1a22f9-e017-4443-b94e-d37053fa7bca`, **EGP10**, manual fulfillment. Express25 was briefly selected/reverted to force fresh selection authority during form editing, not charged on retained orders. Tax0, `taxSource=tenant-setting`, `taxRate=0`, countryEG. Payment fee0; EGP; existing pricing rounding scale2, midpoint away from zero. Existing proportional/largest-remainder line allocation was retained. QA coupons are unrestricted whole-basket5%: all basket merchandise is eligible in these tests.

### Synthetic coupons — not `1441`

| Code | ID | Terms / final state |
|---|---|---|
| `CCQA20261019FIVE` | `da4bcdb5-af72-4b2e-bd2a-8e0a89ee119a` | 5%; no dates/limits/first-order/product/category restrictions; final inactive, usage0 |
| `CCQA20261019WALLET` | `a42fb624-167c-4196-9e29-66ab26614493` | Same5%; final inactive, usage1 |
| `CCQA20261019FINAL` | `28df2da4-6fab-418b-ab9a-cf26592e2456` | Same5%; final inactive, usage2 |

They were clearly marked TEST ONLY and created through the supported Admin coupon API/UI. No real coupon's limits, eligibility, dates or totals were changed.

### Confirmed reproduction sequence

1. Before fixes, apply FIVE to550: coupon27.50, net522.50. COD checkout succeeds and persists **01063**, total532.50. This disproves a universal claim that every coupon checkout fails.
2. In a second cart for the same customer, FIVE is accepted again despite the existing pending COD reservation. Wallet checkout quote532.50 is valid arithmetically, but submission returns **500 ORDER_CREATION_FAILED**, trace `4580132028e7f007c8de0973a0363c91`. SQL confirms no partial order from this failed attempt.
3. A rollback-only configured service probe independently reproduces the actual exception: `CouponAuthorityException` wrapping PostgreSQL23505, constraint `IX_CouponReservations_TenantId_CouponId_CustomerId`. Preview was valid. Evidence: `reservation-probe.json`; original live API console was not directly accessible for this first failure, so this is not falsely described as its captured stack trace.
4. Use independent WALLET coupon. Checkout creates **01065** with order532.50, intent532.50, persisted line gross550/discount27.50/net522.50. Real Admin approval returns409: remaining550, shortfall17.50. No successful financial effect. This establishes the gross/net defect with persisted data, not just a source hypothesis.
5. After net-balance fix, **the same unchanged synthetic01065 intent** approves through Admin UI at532.50. No price/payment rewriting.
6. Fresh01066 encounters a natural post-commit serialization failure. User-supplied API log establishes40001 during `ResumePersistedPaymentIntentInitiationAsync`; order exists, intent remains Created/Pending, checkout returns truthful pending recovery response. No blind new submission.
7. Independent no-coupon01068 exposes the same outbox problem under the initial retry implementation: log shows40001 then23503 `FK_OutboxMessage_OutboxState_OutboxId`, and a generic CHECKOUT_FAILED despite a persisted order. This intermediate failure is retained, not counted as passed. Fresh scoped units of work fix the underlying outbox lifetime problem; tracker clearing alone was not sufficient.
8. Exact request replays for01066/01068 eventually return their **same, safely Expired** order identities; no duplicates. They expired during investigation/restarts and were not resurrected or force-approved.
9. After the final fixes/build/restart, fresh01069/01070/01071 complete with real UI/API/SQL/Mongo/accounting proof. No financial/provider responses were mocked.

Facts versus unknowns: the above four defects and synthetic outcomes are confirmed. Whether any was the cause of the missing Nissan COD screenshot is **unknown**. The reported10450/11000 scale was not silently substituted for screenshot104525/110000. Focused domain tests cover11000−550=10450, but do not identify that missing original order's actual data.

## C. Root cause, changes and authoritative contract

### Authority flow retained

`CouponInput` → `cartStore` → Storefront cart apply/remove endpoints → `StorefrontCouponService` → **existing `ICouponValidationService` SQL evaluator**. Persisted `StorefrontCartCouponStates` supplies cart coupon identity; cache is not financial authority.

`checkoutStore` requests `/api/storefront/checkout/pricing`, renders the server breakdown, then submits `/api/storefront/checkout` with coupon candidate and `expectedPricing`. Expected pricing is an assertion, not trusted money. `CheckoutService` reloads SQL variant/configuration authority, reruns coupon/promotion evaluation, rechecks under Serializable transaction/locks, reserves stock/coupon, and persists immutable order/line pricing plus durable idempotency binding. Existing promotion priority, coupon uniqueness, usage limits, tenant predicates and stock/concurrency protections remain.

Checkout already persisted allocated `OrderItem.DiscountAmount`, `TaxAmount`, `Total`. **A second discount allocator/evaluator was neither needed nor introduced.** The defect was downstream use of gross `Subtotal` for collection.

Manual Wallet intent amount comes from SQL `Order.Total`, not a customer/client-supplied transfer amount. Real proof upload and existing runtime/evidence admission bind tenant/order/intent/reference/amount/currency/proof. Admin pending/proof/confirm/reject endpoints call the existing review application and `PaymentLifecycleService`, which locks current authority. Allocation/status/domain events/accounting outbox remain transactional; committed accounting processing remains durable/idempotent. SQL governs ordinary Orders; Mongo is a projection/readback.

### Financial basis at each boundary

| Stage | Authority |
|---|---|
| Accepted quote | SQL variant/rules/configuration → shared pricing evaluator; client cannot choose discount/total |
| Persisted price | `Order.Subtotal`, discount components, tax, shipping, fee, `Total`, pricing JSON and allocated line fields |
| Line due | `max(0, UnitPrice×Quantity − persisted DiscountAmount + persisted TaxAmount)` |
| Line remaining | `max(0, line due − PaidAmount)`; refunds retain their own history and do not reopen original collection |
| Intent | Existing payment contract initializes `Amount=Order.Total`, same currency |
| Manual review | Same-tenant/order/currency prior Paid intents, excluding current; recognized=max(prior accepted, item PaidAmount sum), avoiding double-counting the same collection |
| Remaining payable | max(net item remaining sum, max(0, accepted Order.Total − recognized)); order-level shipping/fees cannot be forgiven |
| Full approval | Positive submitted amount agrees with remaining within existing currency tolerance; genuine short and excess payments rejected, submitted amount never inflated/replaced |
| Allocation | Existing deterministic item order, capped to **net** remaining lines; shipping/fees remain order-level, not merchandise over-allocation |
| Accounting | Existing wallet settlement uses accepted order basis; one idempotent wallet journal plus actual committed-stock COGS journal, no historical rewrites |

An inconsistent legacy order with an unrepresented order discount still fails closed; the implementation does not arbitrarily repair existing financial snapshots. EGP half-minor-unit tolerance remains0.005. Shared line changes are justified by the directly proven defect; no Stripe/Paymob/COD provider implementation was redesigned.

### Production files changed

| File | Necessary change |
|---|---|
| `PointOfSale.Core/Entities/OrderItem.cs` | Add computed net `PayableAmount`; use it in fully-paid/remaining predicates. Derive from persisted pricing fields so legacy lines without populated Total retain their value. No column/migration |
| `PointOfSale.API/Services/Payments/PaymentAllocationService.cs` | Allocate via shared net `RemainingAmount`, not gross subtotal; refund logic/history unchanged |
| `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs` | Existing manual full-settlement guard now sees correct net line balances; retain order-charge protection; reject excess amount rather than accept incompatible totals |
| `PointOfSale.Core/Discounts/CouponErrorCodes.cs` | Stable `COUPON_RESERVATION_EXISTS` code |
| `PointOfSale.Infrastructure/Discounts/CouponValidationService.cs` | Match existing one-active-reservation SQL rule during preview regardless of configured usage limit; aggregate global/customer reservation counts in one query; preserve exclusion of current order on confirmation/replay. Explicit tenant-scoped background release bypasses only ambient filters, not tenant/deletion predicates; confirmed usage retained |
| `PointOfSale.API/Services/Checkout/ICheckoutService.cs` | Typed coupon-admission exception with original domain code/message |
| `PointOfSale.API/Services/Checkout/CheckoutService.cs` | Preserve final reservation rejection instead of generic500; emit coupon-specific validation failure; resume incomplete existing manual intent using its admitted SQL proof rather than requiring a converted/cleared draft proof |
| `PointOfSale.API/Controllers/CheckoutController.cs` | Coupon rejection mapping for pricing and submission: reservation conflict409, invalid/rule rejection400; no forced success |
| `PointOfSale.API/Services/Payments/PaymentService.cs` | Fresh tenant-scoped context/publisher per local manual admission attempt, re-lock/reload SQL; rollback with noncancelled cleanup token. Prevent stale scoped outbox reuse; other provider execution not retried by this policy |
| `PointOfSale.API/Services/Payments/ManualWalletAdmissionRetry.cs` | At most3 local attempts for actual PostgreSQL40001 through exception wrappers; cancellation/business failures not retried; no external charge retry |

Regression additions/updates: `CouponSettlementIntegrityTests.cs`, `CouponReservationAdmissionTests.cs`, `ManualWalletAdmissionRetryTests.cs`, `CouponConfiguredIntegrityTests.cs`, `CouponUnderpaymentConfiguredTests.cs`, `CheckoutControllerIdempotencyHeaderTests.cs`, `Architecture/CheckoutCouponCqrsConsistencyTests.cs`. The architecture change follows the combined reservation-count query rather than weakening behavior. New configured fixtures have explicit environment opt-ins and refuse accidental reseeding.

Task-only observers, financial checks, CLI function-expression scripts and lint adapter are under `tests/coupon-checkout-20261019-*.py`, `tests/browser/coupon-checkout-20261019-*.js`, and `tests/quality-closeout/coupon-checkout-20261019-lint.mjs`. No frontend production source change, migration or unrelated provider redesign.

## D. Financial proof

### Successful coupon orders — values at each stage

All values EGP. No prior accepted payment existed at initial approval. Tax/fees0. All coupon lines/products in these baskets are eligible.

| Order | Gross / eligible base | Coupon | Net merchandise | Shipping | Quote / SQL / intent / remaining at approval | Final line Paid / Remaining | Wallet posted |
|---|---:|---:|---:|---:|---:|---:|---:|
|01065, recovered net-balance baseline |550 /550 |27.50 |522.50 |10 |532.50 /532.50 /532.50 /532.50 |522.50 /0 |532.50 |
|01067, intermediate post-fix full UI |550 /550 |27.50 |522.50 |10 |532.50 /532.50 /532.50 /532.50 |522.50 /0 |532.50 |
|**01070, final implementation + concurrent UI** |**550 /550** |**27.50** |**522.50** |**10** |**532.50 /532.50 /532.50 /532.50** |**522.50 /0** |**532.50** |

The existing accounting policy posts each wallet settlement **Dr1140 / Cr4020**,532.50. Its stock commit separately posts **Dr5010 / Cr1310**,291.42. These are two distinct business postings, not duplicate settlement journals. Discount is not subtracted again in accounting or represented as fictitious cash paid.

No-coupon01069: gross/net550, discount0, shipping10, quote/order/intent560, linePaid550/remaining0, wallet560 and COGS291.42. The10 shipping is accounted at order level, not an extra line payment.

After successful approval, accepted Paid-intent total is532.50 (560 for01069), so the authoritative remaining payable is**0**, not10. `Order.TotalPaidAmount` intentionally remains the item-allocation sum. The legacy `Order.BalanceDue` utility still subtracts only that item sum and would produce10 for these shipping-bearing orders; repository search found no C# runtime call sites for that utility, and this checkout/review/accounting path does **not** use it as settlement authority. It was not broadened into a provider-wide accounting redesign. The machine summary explicitly labels that compatibility value separately from `acceptedPaymentTotal` and `remainingPayableAfterDecision`; do not use the legacy utility for future settlement/display integrations without handling non-item collections.

COD01063/01071: gross550, discount27.50, net522.50, shipping10, total532.50; paid0, no wallet intent, no settlement/COGS journals. They remain explicitly marked pending synthetic orders; no fulfillment was initiated.

### Genuine underpayment

Fixture `CCQA-20261019-UNDERPAY-190460e3`: gross550, coupon27.50, net522.50, shipping10, accepted532.50. A **new** intent was explicitly initialized at522.50 through the established configured runtime/evidence/review path. No existing payment's amount was overwritten; Storefront has no customer amount-entry field and no new UI contract was invented.

Real Admin Confirm409 message:

> Submitted amount 522.50 EGP does not cover remaining payable balance 532.50 EGP for order CCQA-20261019-UNDERPAY-190460e3 (shortfall 10.00). Full settlement is required for Manual Wallet approval.

`sql-underpay-noeffects.json`: still AwaitingVerification, original522.50 intent, PaidAmount0, no journal and no approval accounting outbox. Then an explicit real Admin rejection produced Cancelled/Rejected; amount/history retained. Separate valid orders, not an inflated replacement payment, establish successful approval.

### Retained journals

| Order | Wallet journal | COGS journal |
|---|---|---|
|01065 |`01a155b0-c286-707b-9e65-4e1dcf824990` |`01a155b0-d411-7a16-a7f5-e6b6282127bf` |
|01067 |`01a155bf-86fb-7ebd-aa22-14f7b8e54e76` |`01a155bf-f8a6-75c9-a8a7-1c4ff25d45f1` |
|01069 |`01a155d2-a331-70bd-b6d6-ce2dbd991b9d` |`01a155d3-178c-7c3a-8750-a8ec57e18166` |
|01070 |`01a155d3-e0c0-70da-9504-83b812f4e5f4` |`01a155d4-1824-744e-8ee4-967b8c42d4dc` |

All8 are Posted, debit=credit, unique business idempotency keys. Corresponding accounting outbox records are processed and point to the same journal IDs. Exact ledger-line/outbox IDs are in `sql-closeout.json` and `financial-consistency-summary.json`. Accepted journal histories agree before/after retries and cleanup. No rejected/expired/pending QA order has a successful allocation or financial posting.

`financial-machine-check-verified.txt`: **89/89 checks**,9orders,8journals,9unique Mongo projections. SQL and Mongo totals/currency/order/payment states agree. This is narrow manifested-record verification, not a fabricated whole-database immutability attestation.

## E. Browser and automated evidence

All paths in this section are relative to `.ai/evidence/coupon-checkout-20261019/`.

### Actual headed Edge execution

Reused the registered `opencode-ui-test` session after `playwright-cli list`; headed msedge, isolated in-memory profile, finite command timeouts. No mocked coupon/checkout/order/payment/approval responses. User restarted the API after successive implementation builds. Final financial workflows ran after the fresh-outbox/proof-replay/background-release fixes were loaded; process/file timestamps are in `certified-runtime-process.json` and `certified-runtime-build-files.json`. Subsequent production-source change was a documentation-only OrderItem comment; final Release build also passed.

| Scenario | Observed result | Main evidence |
|---|---|---|
| Coupon apply, eligibility base, exact5% | PASS, unrestricted eligible550→27.50 | `cart-before.txt`, `coupon-applied-before.png`, `postfix-coupon-ui.txt` |
| Refresh/revisit | PASS, same coupon27.50, not doubled | `postfix-coupon-ui.txt` |
| Remove/reapply | PASS, restores550 then27.50 once | `postfix-coupon-ui.txt`, `postfix-coupon-cart.png` |
| Invalid and missing1441 | PASS,400, no discount | `postfix-coupon-ui.txt` |
| Reservation conflict | PASS post-fix domain400 at apply; actionable cart banner; no discounted commit | `postfix-coupon-ui.txt`; pre-fix failed submission/probe files |
| Quantity revalidation | PASS: qty2 gross1100/discount55/net1045; qty1 restores27.50 | `postfix-coupon-ui.txt` |
| Final coupon wallet checkout | PASS200, SQL/order/intent532.50, real marked proof | `certified-coupon-cart.txt`, `certified-coupon-submit.txt`, `wallet-certified-review.png` |
| Pending review and proof | PASS, actual row/image/control | `certified-coupon-pending.png`, `certified-coupon-proof.png` |
| Ordinary Orders before approval | PASS, pending exact-order API search empty; screen captured | `certified-coupon-ui-concurrent-approval.txt`, `certified-coupon-ordinary-before.png` |
| Approval through Admin | PASS, two actual UI dialogs Confirm concurrently: one200/one409 | `certified-coupon-ui-concurrent-approval.txt`, `certified-coupon-concurrent-decision.png`, `certified-coupon-decision.png` |
| Ordinary Orders after, navigation/refresh | PASS, Processing/Collected visible | `certified-coupon-ordinary-after.png`, `certified-retry-tenant-visibility-verified.txt` |
| Exact repeated/concurrent checkout replay | PASS, both200 same01070/orderID/532.50; no duplicate SQL order/usage | `certified-retry-tenant-visibility-verified.txt`, final SQL/machine checks |
| Stale approve/reject | PASS409, no second journal | Same retry evidence and financial checks |
| Underpayment Admin decision | PASS409 real remaining532.50/shortfall10; unchanged pending/no effects, then explicit rejection | `underpayment-ui-rejected.txt`, `underpayment-decision.png`, `sql-underpay-noeffects.json`, `underpayment-explicit-cleanup-rejection.txt` |
| Independent no-coupon full wallet | PASS200 checkout and Admin approval; persisted560 | `final-nocoupon-submit-resumed.txt`, `wallet-nocoupon-final-review.png`, `final-nocoupon-approved.txt` |
| Final shared COD path | PASS200,532.50 persisted Pending/unpaid; zero financial postings | `certified-cod-submit.txt`, `cod-certified-review.png`, final SQL/Mongo |
| Tenant isolation | PASS3 Admin mismatch probes403; configured two-tenant rollback/foreign-decision tests pass | `certified-retry-tenant-visibility-verified.txt`, complete TRX |
| Final browser console | 0errors,2 existing bootstrap/i18next warnings; no page exceptions in final submissions | `final-browser-console.txt`, submission capture files |
| Restoration | PASS real reset UI200, disabled/empty after reload; public Storefront onlyCOD | `configuration-restored.txt`, `final-wallet-disabled.png`, final SQL |

HTTP evidence contains actual UI-generated request bodies, accepted expected-pricing assertions, server pricing responses, checkout response identities and subsequent supported Admin/SQL/Mongo readbacks. A200 alone was never taken as proof of completion.

### Automated results

| Check | Result | Evidence |
|---|---|---|
| Final selected backend regression, including new net-balance/reservation/retry/background-release tests, pricing/cart/coupon, checkout/controller/concurrency source contracts, payment/refund/settlement guards and configured tenant boundaries | **140 passed,0failed,0skipped** | `backend-complete-regression.log`, `trx/complete-regression.trx` |
| Explicit new underpayment runtime/evidence admission fixture | **1passed** | `underpay-seed-verified.log`, `trx/underpay-seed.trx`, `underpay-manifest.json` |
| Explicit expired task-only reservation release with canonical service | **1passed** | `expired-qa-release.log`, `trx/expired-qa-release.trx`, `expired-qa-coupon-release.json` |
| Admin selected Orders/ManualPayments/API tests | **48passed,4files** | `admin-tests.log` |
| Storefront checkout/cart persistence/guest checkout selected tests | **57passed,4files** | `storefront-tests.log` |
| Latest backend Release build | **PASS**,0errors,201warnings retained | `backend-closeout-build.log` |
| Admin production build | **PASS**, normal chunk warnings | `admin-build.log` |
| Admin typecheck | **PASS** | `admin-typecheck.log` |
| Storefront production build/typecheck | **PASS** | `storefront-build.log`, `storefront-typecheck.log` |
| Relevant changed browser helper lint, existing repository adapter/rules | **PASS**,8files,0errors/0warnings | `browser-lint.log`, `browser-lint.json`, command/hash manifest |
| Python observer/check syntax | **PASS** | `python-syntax.log` |
| Git diff whitespace, actual Git exit status | **PASS**,exit0 | `diff-check-result.json` |
| Final financial/projection/configuration assertions | **89passed** | `financial-machine-check-verified.txt`, summary and Mongo JSON |

These counts overlap intermediate runs and are not added together. Opt-in configured baseline probes ran **before** synthetic coupon deactivation; seed/cleanup fixtures are deliberate controlled operations, not an invitation to blindly rerun them.

**Failed checks retained separately:** initial focused69-test run had1 query-count failure caused by the additional customer-reservation read. Global/customer counts were combined in one SQL query; later140-suite includes the passing atomicity contract. Initial architecture/regression71-test run had3failures: the renamed reservation-count assertion was updated consistently and passes; two unrelated `CouponFinancialAuthorityCertificationTests` remain failed: `FrontendCouponFinancialPresentation_MustSubmitCandidatesOnly_AndRenderBackendPricing` expects an obsolete literal response-variable string in unchanged `checkoutStore.ts`, and `CertificationArtifacts_MustRecordCouponFinancialAuthorityTruth` expects `PASS SF-CPN5` in an unrelated historical report. Those files were not rewritten to manufacture a pass. Evidence: `backend-regression.log`, `trx/regression.trx`.

Initial harness compile/import errors, a concurrent backend build file-lock failure, and intermediate failed browser automation attempts are preserved in task logs. They were corrected or rerun, not counted as successful execution. Browser examples include a strict locator matching recommended-product buttons, waits with an incorrect response pattern, response bodies lost during navigation, restored checkout opening at Review rather than Shipping, and a wrong Orders search placeholder. Before retrying actual failed/timed-out submissions, SQL/request/log evidence was checked for a committed order; retained IDs are enumerated below. Later evidence captures the response at the actual Axios boundary without changing its financial contents.

### Blocked / not executed — not passes

- Exact Nissan/coupon1441/original1040 reproduction and original financial history: unavailable here; user directed AhramStore testing.
- Browser entry of a customer transfer amount: no such Storefront control exists. The disclosed configured runtime admission fixture was used instead, followed by actual Admin UI decisions.
- Exhaustive expired/exhausted/ineligible coupon browser matrix and every promotion/mixed-eligibility basket: not run. Relevant active/expired/inactive/missing/customer-rule/product-exclusion/atomicity/pricing automated tests ran; invalid/missing/reserved and quantity browser cases ran. No broader coupon redesign/certification is claimed.
- Live price edit and every variant permutation: not run; authoritative SQL price/readback, quantity repricing and relevant pricing tests executed. No catalog mutation to fabricate the screenshot price.
- Successful recovery of a still-live, intentionally interrupted payment intent through the new replay branch: not certified end-to-end. Natural interrupted01066/01068 expired during investigation; terminal exact-ID replays were verified. Fresh manual admission after the outbox fix, bounded serialization/cancellation tests and ordinary replay idempotency passed. Exhaustive broker/storage/process-fault interleavings were not injected.
- New simultaneous first-submit double-click race: not separately forced; UI disables submission while busy. Actual concurrent exact checkout replays and two concurrent real Admin confirmations were executed, with SQL/usage/journal uniqueness verification.
- Disposable PostgreSQL provisioning suites (`CouponUsageConcurrencyIntegrationTests`, original ManualWallet/COD disposable fixtures): not run. Configured read-only/rollback/authorized synthetic-runtime coverage and real browser concurrency/COD were used; configured database was never replaced/reset.
- External Stripe/Paymob charge/provider certification, transfers, carrier bookings and unrelated integrations: deliberately not executed.
- Full-repository lint/suite certification: not claimed. Relevant changed-helper lint and selected regression/build/typechecks ran; no unrelated architecture/lint cleanup.

## F. Data integrity and cleanup

### Configuration restoration

| Stage | Wallet enabled | Recipient | Version |
|---|---|---|---:|
| Task original |false |none; encrypted value absent |14 |
| Controlled QA |true |synthetic `01000000000` |16 (later observed mutation history retained) |
| Final confirmed reset |**false** |**none; public{} and encrypted value absent** |**18** |

No verified original recipient existed. The established reset-and-disable Admin UI was used; the synthetic number was not left configured. Metadata/sandbox/fees were not deliberately redesigned. COD remains enabled/version13. Coupons were deactivated through supported version-checked Admin endpoints; their reservations/usages are not deleted. Configuration versions/audit history were not rewritten backward.

### Retained QA identities

All are task-owned, synthetic, no transfer/no delivery markers; not original screenshot records.

| Purpose / number | Order ID | Intent ID | Final state |
|---|---|---|---|
| Baseline coupon COD01063 |`8ec5cde8-50e9-4a51-9e99-f90e497d88d0` |none |Pending/Pending,532.50 |
| Baseline coupon wallet01065, failed then valid approval after net fix |`882dfa81-d1e1-4cf5-838d-8bb3e2bca1cc` |`68f69596-f8c0-454f-98f2-aeac0d0c6a69` |Processing/Collected,532.50 |
| Interrupted admission01066 |`4ccce167-b11c-4caa-9ac9-d09dbb494fdf` |`405aa7e9-2387-4f8f-8633-a2de62593bd4` |Cancelled/Expired,532.50, no financial postings |
| Coupon full UI01067 |`c3511e27-7b06-4374-af91-4ff4a8f5ae90` |`b06ce9c2-2961-41b9-96d0-66cab7e1d90e` |Processing/Collected,532.50 |
| Underpayment `CCQA-20261019-UNDERPAY-190460e3` |`190460e3-9b58-4b54-9573-5fd8f2a412b6` |`3ddbcca9-817b-4a7d-8e20-044a586c335c` |Cancelled/Rejected; submitted522.50 unchanged; total532.50 |
| Interrupted no-coupon01068 |`84ecd02d-b2b1-47bc-a10f-7a6a99867be9` |`203153b0-82b5-485a-981c-cab56c14f1c5` |Cancelled/Expired,560, no postings |
| **Final no-coupon01069** |`c3c36598-b539-48b2-a23b-4f01ac5c036b` |`81345a3b-8120-4dbf-b2bb-7559961962ed` |Processing/Collected,560 |
| **Final coupon01070** |`a5a0b6b7-7926-4c6d-827a-ad362665b613` |`61ea6884-5342-4c2a-8dbe-a5aede2a8030` |Processing/Collected,532.50 |
| **Final coupon COD01071** |`f1d3772c-420b-4944-905f-75f6bdf38d3f` |none |Pending/Pending,532.50 |

Proof image: `TEST-ONLY-NOT-A-REAL-RECEIPT.png`, visibly **TEST-ONLY — NOT A REAL RECEIPT**, states no transfer/no delivery. Retained accepted proof keys under `tenants/c9c5c1ae-949e-498e-bc63-07242daeb49d/manual-wallet-proofs/checkout-sessions/`:

-01065: `4c451ca0-fda5-4ad0-95af-329e1c409cd4/277967495e00434eb3a5aebeaf96eb0b.png`.
-01066: `ea4271b8-3fa9-42be-92c8-ac42df5fe3f7/763f07e256ef417c8ea7fda68ea96b85.png`.
-01067 and disclosed underpayment fixture reuse: `34acd097-d132-49d0-9f14-470c66475974/749227578f0c48f696de0d71ecc6112f.png`.
-01068: `14f16e81-97d2-4169-811e-fdbe5f2a9338/aec57e9eb7ae401caf27e92944d00946.png`.
-01069: `5673d8dd-dd43-4521-a0c9-7e97a6c93e1f/62a5eb583e8d434681d9edcc91b721ba.png`.
-01070: `543beeb2-bd37-4194-8f04-a995f7487a46/46196bb3849f439395afdeb4bca0cdc3.png`.

Superseded uploads remain under established proof retention; no broad storage deletion.

01066's previously stranded reservation `18a6a70e-0b9f-4e14-b2cb-2b49ebb4702b` was released **only after verifying that exact task order was Cancelled/Expired and had no confirmed usage**, through the repaired canonical release service. History retained; no order, intent amount, pricing or journal was changed. Other pending COD reservations remain attached to their clearly identified QA orders. Accepted journals were not deleted/reversed to make the environment appear clean.

The configured database was not replaced, reset, recreated or broadly restored. No migration was required. No original customer/order/coupon/payment/journal was rewritten. Existing task-unrelated working-tree modifications, prior reports/evidence, tracked artifact deletions and historical Playwright output were preserved. No staging/commit/reset/clean/stash/checkout-discard operation was performed.

## G. Remaining work and certification boundary

The demonstrated valid coupon checkout → persisted pricing → manual intent → real review/approval → net allocation → accounting path now passes in the configured AhramStore environment. The missing original report remains **unverified**, not declared fixed by substitution.

To certify that exact case, obtain the actual Nissan environment/tenant records and original order/request/backend failure evidence. To certify additional reliability branches, exercise a controlled still-live interrupted admission/replay with configured broker/storage fault injection and the expanded negative coupon/variant matrix. The unrelated historical/source-string architecture failures remain separately actionable; they are not reasons to rewrite historical evidence.

Compatibility limitation: the unused item-only `Order.BalanceDue` utility described in sectionD is not a complete order-settlement API. Future consumers must use accepted payment authority for shipping/fees. Also, the new reservation error is actionable in the cart-level server message, but the unchanged inline coupon translation fallback can still say “Invalid coupon code”; no locale/UI redesign was included.

No unresolved failure is hidden behind a200-only assertion: intermediate500s, rollback/no-partial-order evidence, committed-but-interrupted orders, safe expiry, final successful persisted workflows, rejected underpayment, accounting uniqueness, restored configuration and unexecuted cases are all recorded explicitly.
