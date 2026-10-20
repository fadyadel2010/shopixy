# Manual Wallet lifecycle remediation — 2026-10-19

## A. Executive conclusion

**Overall verdict: CONDITIONAL.** The verified configuration, operational-order visibility and independent non-coupon financial-safety defects are fixed. Real headed Edge browser testing exercised the configured application/database, including Storefront checkout/proof upload, approval, rejection, insufficient payment, concurrent approvals, stale decisions, COD and configuration restoration. The latest focused backend regression run passed **222/222, zero skipped**.

Conditions preventing an unqualified acceptance of every requested check:

1. `ORD-A758-2026-01040` does not exist in the configured PostgreSQL database or Mongo order projection. Its persisted financial records cannot be certified here. No similarly numbered AhramStore order was substituted.
2. An existing Admin payment-method architecture assertion fails against an already-absent Storefront cache helper. It was not changed to manufacture a pass.
3. Relevant unchanged Storefront files have existing lint errors; the normal lint installation also lacks its TypeScript plugin. The configured alternative ran and reported the errors rather than suppressing them.
4. Storefront has **no customer-entered transfer-amount field**: it creates intents for the authoritative order total. The intentionally underpaid case was therefore a separately identified new configured-database fixture admitted through the actual production runtime/evidence/lifecycle services, then decided through the real Admin UI. No existing or submitted payment amount was overwritten.

**KNOWN — DEFERRED: Coupon-related checkout completion and its associated amount discrepancy.** No coupon, discount-arithmetic, checkout-total, coupon-eligibility or coupon-order-creation implementation was changed.

Final merchant configuration: **Manual Wallet disabled, recipient configuration empty**, matching the safe original state. There was no verified original recipient number to restore. The synthetic QA recipient was removed through the existing confirmed reset workflow. Merchant activation now requires deliberately supplying a valid recipient.

## B. Baseline, authority trace and implementation

### Configured environment and before-state

- Admin: `http://localhost:5173/ecommerce/payment-methods`, `/ecommerce/manual-payments`, `/ecommerce/orders`, `/ecommerce/orders/{orderId}`.
- Storefront: `http://ahramstore.localhost:5174/products/cert-media-simple-20261007`, `/checkout`, `/checkout/pending`, order tracking.
- API: normal Development configuration, `http://localhost:5010`; PostgreSQL `192.168.1.5:5432 / PosEcommerceDb_Baseline`. Existing configured Mongo/Redis/storage/broker were retained. No disposable database, reset, recreation or migration was introduced.
- Authenticated using the supplied AhramStore Admin/customer accounts. Passwords/tokens are not included in this report or task scripts.
- Baseline evidence, captured before code/configuration mutations: `.ai/evidence/manual-wallet-20261019/sql-before.json` and `browser-baseline.txt`.
- Tenant: `c9c5c1ae-949e-498e-bc63-07242daeb49d`.
- Wallet row `8596894a-df67-44c5-8e0e-f64372bc1d74`: disabled, version **6**, **no encrypted configuration**, invalid because recipient missing. Existing display name `P5QA20261017-817 TEST ONLY Wallet` was preserved.
- COD row `5fd1c166-73cd-4235-a127-9efffad10b23`: enabled, version **13**. Stripe/Paymob tenant rows absent and displayed Not Configured. These states were not changed.
- WhatsApp account `138379f5-7754-4c20-8863-b3b7aad28888` remained disabled in both observed readbacks. No account-setting mutation or OTP/message-send action was performed.
- The screenshot tenant `nissan.localhost:5174` is not the authenticated AhramStore tenant. The supplied empty-editor/enabled-list condition was not falsely claimed as the current persisted baseline.

### Authority map

| Concern | Established path and observed contract |
|---|---|
| Configuration | `PaymentMethods.jsx` → `PaymentMethodsAdminController` → SQL `TenantPaymentMethods`; Data Protection purpose `PaymentMethodSecrets`; shared `PaymentMethodConfigurationValidator` |
| Availability | `PaymentMethodReadRepository` rehydrates SQL configuration; `StorefrontPaymentMethodsController` filters invalid/unready methods; checkout independently reloads/locks SQL method authority and validates configuration/runtime |
| Checkout | `checkoutStore.ts` → `CheckoutService` → `PaymentService`; immutable variant pricing snapshots, tenant-owned session/proof keys, SQL order and reviewable intent |
| Proof/review admission | Real upload endpoint; `CreateReviewablePaymentIntentAsync`; runtime initialization and receipt evidence admission; durable evidence bound to tenant/order/intent/reference/amount/currency/proof; `AdmitPaymentReviewAsync` |
| Decisions | `AdminManualPaymentController` → `ManualPaymentReviewApplicationService` → `PaymentLifecycleService`; current SQL state, proof, expiry, retention, currency and tenant revalidated |
| Ordinary Orders | `AdminOrdersController` → `AdminOrderService.ListOrdersAsync`; SQL, **not Mongo**, is authoritative; count and rows share the filtered query |
| Financial transition | Existing order/intent locks and concurrency tokens; allocation/status/events/accounting outbox in the lifecycle transaction; committed accounting posting with durable idempotent retry, then financial-posted operational progression |
| Public/history projection | `OrderProjectionConsumer` rebuilds from SQL, applies freshness guards and invalidates order/customer Redis keys. Final task-owned Mongo readback agrees with SQL |

Relevant operations: `GET /api/admin/payment-methods[/MANUALWALLET]`, `PUT .../MANUALWALLET[/configure]`, `POST .../enable`, `.../disable`; `GET /api/storefront/payment-methods`; `POST /api/storefront/checkout`; `GET /api/admin/payments/manual-wallet/pending`; `GET .../proof-image/{intent}`; `POST .../{intent}/confirm`, `.../{intent}/reject`; `GET /api/admin/orders[/{order}]`.

### 1. Configuration: confirmed causes and fix

**Confirmed:** the Admin DTO omitted all configuration, including the already-public merchant recipient. `handleConfig` always initialized `{}`. A genuinely configured wallet therefore reopened as an empty ordinary text input. The dialog toggle is **Sandbox Mode**, not activation; confusing it with activation is not evidence of a missing persisted recipient.

**Confirmed:** the shared wallet validator required only non-whitespace presence. Arbitrary text such as `not-a-wallet` could satisfy readiness. Existing backend enable checks, explicit reset-and-disable, SQL checkout admission, version headers and frontend mutation/read guards already existed; these were preserved rather than replaced.

Changes:

- `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs`: apply the existing Manual Wallet phone-shaped contract: 8–20 digits, phone-number punctuation/spacing only, no new country/operator prefix restriction. Count digits explicitly. Shared use protects enable, readiness and checkout.
- `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs`: return **allow-listed public configuration only**; reject explicit invalid/empty recipient replacements on both configure and upsert; reject metadata-only/empty-object mutations that would retain an enabled legacy invalid configuration. Effective stored configuration is still evaluated server-side. Confirmed reset remains the atomic clear-and-disable mechanism.
- `PointOfSale.Web/src/pages/PaymentMethods.jsx`: prefill the public saved recipient, explain Sandbox versus Enable, validate before sending, display validation/server feedback inside the open dialog, and preserve edits on failure. Fix feedback being cleared when Enable opens an invalid configuration dialog.
- `PointOfSale.Web/src/pages/PaymentMethods.d.ts`: declare the testable validation helper.

Before: a saved number looked absent; malformed nonempty text could be marked valid. After: list/editor/SQL/public availability agree; malformed mutations fail without altering the accepted configuration; clearing requires confirmed reset and disables the method. Gateway secrets remain encrypted/write-only. No recipient or credential logging was added.

### 2. Pending/rejected order visibility: confirmed cause and fix

**Confirmed:** `AdminOrderService.ListOrdersAsync` selected every tenant Ecommerce order regardless of wallet review/payment state. Checkout already correctly persisted Pending/AwaitingVerification. This was a backend workflow-classification defect, not premature successful payment posting.

`PointOfSale.API/Services/Admin/AdminOrderService.cs` now excludes unaccepted Manual Wallet orders from the ordinary operational list. Identification uses the method code **or tenant-owned ManualWallet intent evidence**, preventing a misleading method label from bypassing the boundary. Eligibility requires the financial accepted/refund-history states and a completed transition away from Pending. COD/online and Marketplace queries retain their existing contract. Pending/rejected details and audit/history remain readable; nothing is deleted to hide a row.

Verified transitions:

```text
Storefront submit -> SQL Pending / AwaitingVerification -> Manual Payments only
approval -> intent Paid; order Confirmed / Collected; allocated item amount; durable accounting outbox
posted accounting -> established Processing / Collected workflow
rejection -> order Cancelled / Rejected; intent Rejected; no settlement journal
```

Navigation, refresh, explicit API searches and SQL readbacks confirmed the boundary before and after decisions. Mongo was not made an operational authority or used to hide a SQL classification error.

### 3. Approval integrity: independently confirmed non-coupon defect

The valid no-coupon browser approval succeeded. The screenshot does **not** establish a general approval-failure defect.

However, independent testing established a different financial gap: `ValidateSuccessfulAmount` checked only remaining item balances. For a no-coupon order with merchandise **25**, shipping **10**, total **35**, it accepted **25** at that guard. `LegacyItemGuard_IndependentlyDemonstratesNonCouponShippingGap` proves this without approving an underpaid order.

`PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs` adds a **Manual Wallet approval-only** check inside the existing locked transaction, before any successful intent/allocation mutation:

```text
accepted = same-tenant/order/currency previous Paid intents, excluding current intent
recognized = max(accepted, existing item PaidAmount sum)
remaining = max(existing remaining-item sum, max(0, persisted Order.Total - recognized))
require positive submitted amount and amount + existing currency tolerance >= remaining
```

EGP tolerance remains the existing half minor unit, `0.005`. Failure includes actual balance/shortfall. The submitted amount is not inflated, replaced or rewritten. **The original item guard and allocation code remain intact**: this does not repair discount allocation, adjust coupon totals, or make the screenshot case approve. Other provider/COD validators are unchanged.

The existing atomic/idempotent lifecycle and accounting outbox were reused. Posting is not falsely described as a single distributed synchronous transaction: state/allocation/outbox are committed locally, accounting has its existing durable retry boundary. Both successful QA orders actually reached Posted accounting; each has one processed outbox receipt and one balanced journal.

### Test-source changes

- `PointOfSale.Tests/Payments/PaymentMethodConfigurationValidatorTests.cs`: missing/invalid/valid provider-neutral recipient cases.
- `PointOfSale.Tests/Admin/PaymentMethodsAdminSafetyTests.cs`: invalid replacements through both routes, preserved stored public recipient, unsafe legacy effective-configuration mutations.
- `PointOfSale.Tests/Admin/AdminOrderListDeterministicTests.cs`: pending, rejected, inconsistent confirmed/unpaid, accepted and refund-history visibility; details retained.
- `PointOfSale.Tests/Admin/AdminOrderListPostgresTests.cs` and `AdminOrdersConfiguredPostgresTests.cs`: replace the obsolete pending-wallet-list expectation; keep independent unpaid COD/affiliate ownership coverage.
- `PointOfSale.Tests/Payments/ManualWalletConfiguredLifecycleTests.cs`: independent shipping-gap proof, amount guard matrix, explicitly opt-in new underpayment fixture using real runtime/evidence/review admission.
- `PointOfSale.Tests/Payments/ManualWalletConfiguredTenantTests.cs`: two configured-Postgres tenant configurations and foreign decisions, transaction rollback plus independent absence check.
- `PointOfSale.Web/src/pages/PaymentMethods.test.tsx`: saved recipient rendering, invalid values, preserved edits/no unsafe post.
- Task-only replay/evidence helpers: `tests/browser/manual-wallet-20261019-*.js`, `tests/manual-wallet-20261019-observer.py`, `tests/manual-wallet-20261019-mongo-observer.py`. No original Phase8 scripts or historical evidence were rewritten.

## C. Read-only investigation of ORD-A758-2026-01040

SQL observer used a **REPEATABLE READ, READ ONLY** transaction and exact `OrderNumber` lookup across configured records, without tenant/deletion filters hiding the case. Result: **zero rows** at baseline and closeout. Independent configured Mongo exact-number lookup also returned **zero**. No attempt was made to recreate, edit, approve or substitute this order.

| Requested persisted fact | Finding |
|---|---|
| Merchandise subtotal/lines | Unknown: order absent |
| Coupon/discount and related state | Unknown: order absent |
| Shipping/tax/fees/final payable/currency | Unknown: order absent; screenshot currency cannot certify this persisted order |
| Submitted/captured amount/source | Screenshot reports `10450.00`; no authoritative intent record available for this order |
| Previous accepted payments/captures/refunds/reversals | Unknown, not certified as “none” |
| Status | Unknown: no authoritative order record |
| Reported item balance | Screenshot reports `11000.00`; actual persisted inputs unavailable |

Confirmed source formula for the reported old error:

```text
item.RemainingAmount = max(0, UnitPrice * Quantity - PaidAmount)
remaining item balance = sum(item.RemainingAmount)
```

It is an **item balance**, not an order-total balance. It does not subtract order coupon discounts and does not include shipping. `PaymentService` normally initializes Manual Wallet intent amount from `order.Total`; review passes the persisted intent amount to the lifecycle. These source facts explain why differing persisted representations could produce such an error, but **do not establish this missing order's actual values or classify it as genuine underpayment**.

Evidence: `sql-before.json`, `sql-closeout.json`, `mongo-closeout.json` under `.ai/evidence/manual-wallet-20261019/`.

## D. Executed tests and evidence

### Automated checks

All paths below are under `.ai/evidence/manual-wallet-20261019/` unless stated otherwise. Counts are execution results, not inferred coverage; overlapping intermediate runs are not added together.

| Check | Result | Evidence |
|---|---|---|
| Latest backend focused/regression + Manual Payment architecture + configured tenant/order rollback + amount guards | **222 passed, 0 failed, 0 skipped** | `backend-verified-regression.log`, `trx/verified-regression.trx` |
| Initial backend focused run including broader Admin payment read source contract | **72 passed, 1 failed** | `backend-focused.log`, `trx/focused.trx` |
| Explicit underpayment fixture/runtime admission and amount tests | **6 passed, 0 skipped**, including seed | `manual-guards-and-seed.log`, `underpay-manifest.json` |
| Admin Payment Methods/phase5 selected run | **18 passed, 3 files** | `admin-tests.log` |
| Admin Orders/API regression | **39 passed, 3 files** | `admin-regression.log` |
| Actual ManualPayments `.test.ts` and phase5 `.test.tsx` | **10 passed, 2 files** | `admin-manual-regression.log` |
| Storefront checkout validation/payment-method mapping | **35 passed, 2 files** | `storefront-tests.log` |
| Storefront checkout closeout | **17 passed, 1 file** | `storefront-closeout.log` |
| Backend latest Release build | **PASS**, warnings retained | `backend-verified-build.log` |
| Admin Vite production build | **PASS**, chunk warnings | `admin-build.log` |
| Storefront `tsc && vite build` | **PASS**, chunk/dependency warnings | `storefront-build.log` |
| Admin `npx tsc --noEmit` | **PASS** | `admin-typecheck.log` |
| Admin maintained lint wrapper, changed frontend files | **PASS** | `admin-lint-supported.log` |
| Storefront relevant-file lint | **FAIL**: 16 existing errors, 2 warnings in unchanged files | `storefront-lint-configured-alternative.log` |
| Targeted `git diff --check` | **PASS**, line-ending warnings only | executed against changed implementation/test files |

Unresolved source-contract failure: `AdminPaymentMethodReadAuthorityCertificationTests.PaymentMethodReadRuntime_MustRemainBackendOwnedAndTenantScoped` expects `SetNoStoreHeaders();` in `StorefrontPaymentMethodsController`. That helper was already absent; this controller was not edited. This is **not counted as passed**, and its broader cache-source contract needs separate review.

Execution/setup failures retained, with alternatives/results:

- Initial raw Admin ESLint command lacked flat config; the repository's maintained `npm run lint -- <files>` wrapper succeeded. No ESLint migration was made.
- Storefront lint first lacked `@typescript-eslint/eslint-plugin`. Resolving the already-installed Admin plugin ran the same Storefront rules and exposed the 16 errors/2 warnings. No packages installed or unrelated files fixed.
- First broad architecture run: **110 passed, 26 failed** because legacy root discovery mistook artifact sibling project directories for source root. Copied the compiled runtime unchanged to a task-owned four-level evidence runtime, following the repository's existing closeout approach. Rerun: **136/136 passed**, followed by the combined **222/222** verified run. The initial failures remain in `tenant-and-manual-architecture.log`/TRX.
- Intermediate new harness build missed a namespace import: build failed, import corrected, subsequent builds passed (`backend-final-build.log`, later rerun/verified logs retained).
- Some initial CLI attempts timed out on session restoration, response matching that omitted `?lang=en`, wrong dialog button labels, or response bodies lost during navigation. One listener used unavailable `URL` outside page context. These are failed automation attempts, not passing workflows; helpers were corrected and persisted state was checked before continuing. Successful order creation was never blindly repeated to compensate for a response-capture timeout.
- Some initial test command arguments named nonexistent optional `.test.tsx` files. Only actually discovered file/test counts above are claimed; the real ManualPayments `.test.ts` was then run explicitly.

### Browser scenarios A–D: real configured application, headed Edge

Used `playwright-cli list`, then the isolated `opencode-ui-test` session with `--browser=msedge --headed`, finite command timeouts, no persistent/default Edge profile. No response mocking was used to make financial/configuration assertions pass.

| Scenario | Result and actual evidence |
|---|---|
| A: retrieve/list/editor | PASS. Original missing state verified against SQL. Saved synthetic recipient displayed accurately after save/refresh. `browser-config-rerun.txt`, `config-saved-recipient.png` |
| A: empty/whitespace/malformed/too-short/too-long | PASS. UI prevented posts and retained edits; direct configure API returned 400; direct missing-recipient enable returned 400; version/state unchanged on rejection. `config-invalid-edit.png` |
| A: valid activation; unsafe removal | PASS. QA-only `01000000000` saved/enabled; invalid active replacements returned 400 and preserved the accepted stored number/version |
| A: stale checkout | PASS. UI revalidation marked selection stale and refused to submit after safe reset. Independent direct checkout returned **400 INVALID_PAYMENT_METHOD**, no order created by the rejected request. `browser-direct-stale.txt`, `stale-checkout-rejected.png` |
| A: restoration | PASS. Confirmed reset removed synthetic recipient and disabled method. Reload and public read showed only COD available; missing-recipient enable still 400. `browser-restored-config.txt`, `final-wallet-disabled.png` |
| B: real no-coupon product/cart/checkout/proof | PASS. EGP25 merchandise + EGP10 manual shipping = EGP35. Uploaded actual marked PNG; no transfer. `checkout-initial.yml`, `proof-review.png`, `pending-review.png`, `browser-pending-review.txt` |
| B: pending exclusion before approval | PASS. Ordinary Orders UI and API excluded pending order; refresh/navigation did not promote it. `browser-before-approve-rerun.txt`, `orders-before-approval.png`, `sql-pending-valid.json` |
| B: valid approval via UI | PASS. Confirm click → **200**, Paid intent/Collected order, becomes ordinarily visible only after transition; refresh remains truthful. `browser-approved.txt`, `orders-after-approval.png`, `browser-approved-readback.txt` |
| B: separate rejection via UI | PASS. Cancelled/Rejected audit details retained, absent from ordinary Orders, zero journals/outbox. `browser-reject-decision.txt`, `browser-reject-readback.txt`, `rejected-audit-details.png` |
| C: explicit underpayment via actual review UI | PASS using the disclosed canonical fixture alternative. EGP25 intent against EGP35 payable → **409**, balance35/shortfall10 toast, still AwaitingVerification, zero allocation/journals/outbox. `browser-underpayment.txt`, `underpayment-rejected.png`, `sql-financial-checked.json` |
| C: retry/concurrency | PASS. Two independent concurrent confirm requests: **one 200, one 409**. Stale reject and stale real-screen approval: **409**. One journal only. `browser-concurrency.txt`, `stale-decision-refused.png` |
| D: COD | PASS. New browser COD checkout remains Pending/unpaid and visible/actionable in ordinary Orders; no payment intent/journal. `browser-cod-state.txt`, `cod-remains-actionable.png` |
| D: other provider configuration | PASS preservation: COD unchanged version13, Stripe/Paymob remain unconfigured, no charges/provider mutations |
| D: tenant isolation | PASS for tested boundaries. Six Admin foreign-header read/write/proof/decision requests and two Storefront foreign-header requests → **403**. Two actual configured-Postgres tenant configurations remained separate; foreign decision context returned **404**, review service never called; test tenants/configurations rolled back and independently absent. `browser-tenant-isolation.txt`, verified TRX |
| D: final SQL/Mongo readback | PASS. Approved=Processing/Collected; rejected=Cancelled/Rejected; COD=Pending/Pending. All five projections agree. `sql-closeout.json`, `mongo-closeout.json`, `browser-verified-build-readback.txt` |

Task proof image: `.ai/evidence/manual-wallet-20261019/TEST-ONLY-NOT-A-REAL-RECEIPT.png`, visibly **TEST-ONLY — NOT A REAL RECEIPT**, states no wallet/bank transfer occurred. The underpayment fixture reuses that clearly synthetic task proof; its authoritative amount is explicitly25, not a claimed genuine35 receipt.

Unexecuted/limited checks, **not passed**:

- Original screenshot-order persisted diagnosis, because its identity is absent in both configured stores.
- Customer typing an underpayment amount in Storefront: no such UI/control/API amount contract exists. Canonical fixture + live Admin UI alternative executed instead.
- Separate second-tenant account browser sign-in: no second account credentials supplied. Real configured tenant-context rollback tests and actual HTTP tenant mismatch tests executed instead.
- Full repository backend suites that provision disposable databases (including the original `ManualWalletPaymentConcurrencyIntegrationTests`/`CodPaymentLifecycleTests` fixtures) were not run, honoring the database restriction. Actual current-database concurrency/browser COD and safe focused tests ran instead.
- Exhaustive accounting-storage/network-fault injection and every possible decision interleaving were not executed. Observed insufficient-payment/rejection/concurrent/retry cases are certified; untested failure modes are not claimed.
- External Stripe/Paymob authentication/charges, real wallet transfers, carrier bookings, WhatsApp delivery and model execution were deliberately not performed.

## E. Data and financial consistency

### Original/final configuration

| State | Enabled | Stored recipient | Version |
|---|---|---|---|
| Original AhramStore Manual Wallet | false | none; encrypted value absent | 6 |
| Controlled QA | true | synthetic `01000000000`, explicitly test-only | through 12 |
| Final, confirmed reset | **false** | **none; synthetic removed**, encrypted value absent | **14** |

Display text, sandbox flag, fees and other merchant settings were preserved. COD stayed enabled/version13; Stripe/Paymob stayed absent/unconfigured. Wallet version/audit timestamp changes are legitimate configuration mutation history, not rewritten back to hide testing.

### Retained QA identities

All amounts EGP; all orders have merchandise25, shipping10, no coupon/discount/tax/payment fee, payable35.

| Purpose | Order number / order ID | Payment intent | Final state |
|---|---|---|---|
| Valid UI approval | `ORD-C9C5-2026-01059` / `1ddbaaff-0ef9-49fa-8d6b-fac625f03942` | `3d6f8c27-3c0f-4864-aa0b-ad48cf59be43`, amount35 | Processing / Collected; intent Paid |
| Separate UI rejection | `ORD-C9C5-2026-01060` / `2c909e2d-8da6-40c1-b9c5-1e1a6c06f849` | `8aa876c9-14dc-4494-8fb5-622a10f0f70e`, amount35 | Cancelled / Rejected |
| COD regression | `ORD-C9C5-2026-01061` / `87d302a7-afba-4714-96a6-d8fa55fd7878` | none | Pending / Pending, unpaid |
| Concurrent approval | `ORD-C9C5-2026-01062` / `b58e0c99-08e4-401c-9c1a-4ad19cc5b0e8` | `005921f5-4cd6-46c9-8c21-df4d2823647f`, amount35 | Processing / Collected; intent Paid |
| Explicit underpayment fixture | `MWQA-20261019-UNDERPAY-82af88bc` / `82af88bc-bfce-478d-b323-7d37706b6e66` | `4c614307-2ba1-4e8e-b0f4-e38342f1807e`, amount**25** unchanged | Pending after failed approval; then deliberately rejected via UI for safe cleanup; final Cancelled / Rejected |

Persistent successful accounting was retained, not deleted/reversed to conceal QA:

| Order suffix | Journal | Accounting outbox |
|---|---|---|
| 01059 | `01a1557f-4f57-7aff-b813-d8279740e030` | `01a1557f-49da-70cd-a4cd-9e812cca24d7` |
| 01062 | `01a15589-8804-7411-9caf-1bacdb8ddbda` | `01a15589-8504-7909-b5d8-10535fc906dc` |

Each is Posted `Order_Wallet`, **debit1140=35 / credit4020=35**, one unique `WALLET_APPROVED:{tenant}:{order}` key and processed outbox receipt pointing to its journal. Merchandise item PaidAmount=25; the extra10 is the existing order-level shipping accounting treatment, not an item over-allocation. Rejected/underpaid/COD rows have zero settlement journals and zero approval accounting outbox. Accepted journals remained unchanged after retries/cleanup. Evidence: `financial-consistency-summary.json`, `sql-closeout.json` including individual ledger-line IDs/accounts.

Retained task proof object keys (all under `tenants/c9c5c1ae-949e-498e-bc63-07242daeb49d/manual-wallet-proofs/checkout-sessions/`):

- `9cc371de-d882-4bdd-996d-1cd63a79a8dc/9620519161c14850b4b6ca29c18245ef.png` — initial upload before stale-selection exercise, superseded; left to established retention, not broad storage deletion.
- `9cc371de-d882-4bdd-996d-1cd63a79a8dc/fc0771ca56aa4c0dbe28f73e41fc5b53.png` — valid UI order and explicitly reused underpayment fixture.
- `93710fb8-6a5f-4781-af36-aa2d0e97b1c3/d53290ecd84743668c001951a42bb6b5.png` — rejected UI order.
- `52c81987-3a04-46c4-ae38-9f9bddc16029/85c650ac41ca48ce80736b14899ffd73.png` — concurrent order.

Canonical order/session/proof/audit/projection history is retained. No real fulfillment was started. Two tenant-isolation fixtures and configured order-query fixtures used rollback, not database replacement. No synthetic recipient remains configured.

## F. Deferred items

**KNOWN — DEFERRED: Coupon-related checkout completion and its associated amount discrepancy.**

User-supplied screenshot description: `nissan.localhost:5174`, iPhone18 Black, subtotal **EGP110,000**, coupon `1441` at5% = **EGP5,500**, shipping **EGP25**, tax **EGP0**, displayed total **EGP104,525**, error **Checkout could not be completed**. Separate Manual Payments screenshot reports **10450.00** versus **11000.00** for `ORD-A758-2026-01040`.

These are reported screenshot facts, not independently retrieved persisted values. Original image binaries were not available as repository artifacts; no fabricated screenshot path is supplied. Different numeric scales are not silently reconciled or assumed to describe the same accepted pricing snapshot.

Separate remediation must obtain that tenant/order's actual configured records, inspect accepted pricing/discount/line allocation and coupon checkout completion, then repair only under its own authorization. This task did not change the coupon implementation or the protected screenshot order.

Other separate follow-ups: existing Admin cache-source architecture assertion; unchanged Storefront lint debt/plugin setup; generic offline “Pay on Delivery” label also shown on Manual Wallet in checkout (observed but not expanded into a Storefront presentation refactor).

## G. Final integrity statement

- No Git staging, commit, reset, clean, stash or restore was run. Unrelated existing modifications were preserved: `StorefrontChatController.cs`, Storefront `httpClient.ts`, AbandonedCart scheduler tests, ProviderDelivery tests, POS/Storefront authority tests, CouponProjection tests, Phase5HttpCache tests and variant distributed-reliability tests. Existing untracked Phase8 scripts/reports and pre-existing `.opencode-tmp` deletions were not overwritten/restored.
- No schema migration or configured database replacement/reset was introduced. No pre-existing order/payment/journal was selected as a mutation target. The exact screenshot order was read-only/absent. Existing coupons, provider settings, WhatsApp-disabled state and historical QA artifacts were not rewritten by task cleanup.
- Evidence is narrowly task-owned under `.ai/evidence/manual-wallet-20261019/`. SQL reads explicitly use read-only transactions; Mongo observers only read the manifested QA identities and diagnostic number. No broad snapshot/redaction utility ran.
- Preservation statements describe task actions and the narrow configuration/order evidence, **not** a fabricated whole-database before/after fingerprint. The normal running application's asynchronous workers continue to operate; a database-wide immutability attestation was not performed.
- The updated API was running against the same configured services during final browser readback, showing wallet disabled/version14 and only the two accepted QA wallet orders plus the legitimate pending COD order in the QA ordinary-order search. **Subsequent process-exit update:** the verified API background command later exited with code1; a follow-up check found no listener on port5010. Its final log entries show ordinary requests/jobs, not a demonstrated fatal cause. The process has not been automatically restarted after task completion; current API availability is therefore down, independently of the recorded successful test results.
- No skipped/unexecuted check is counted as passed. The missing diagnostic case, remaining test/lint failures and alternative-fixture limitations are explicit above.
