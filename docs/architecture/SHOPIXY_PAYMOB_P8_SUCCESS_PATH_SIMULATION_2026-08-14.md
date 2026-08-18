# SHOPIXY Paymob P8 Success-Path Simulation — 2026-08-14

## 1. Executive Summary

P8 exercised the SHOPIXY application-side Paymob success lifecycle using a deterministic Paymob webhook-shaped event submitted through the real `PaymobWebhooksController` path. The signed SUCCESS event finalized payment, order, inventory reservation, webhook replay/idempotency records, storefront status, and admin visibility assertions in the focused P8 suite.

P8 certification is **FAILED** because the required failed → retry → success scenario is not currently supported by the real retry entry point after a Paymob failure. After a realistic failed Paymob webhook, `PaymobPaymentService.CreatePaymentIntentAsync` rejects the same order with `Order:Pending/Payment:Failed`. The success path itself passed.

No Paymob credentials, HMAC secrets, client secrets, access tokens, encrypted credentials, or customer-sensitive payment data were printed. All identifiers below are test fixture or redacted values.

## 2. Scope

In scope:

- Paymob application-side success webhook handling.
- HMAC verification using a test-only HMAC secret inside the harness.
- Payment intent lookup and lifecycle finality.
- Order state transition.
- Inventory reservation commit/release semantics.
- Duplicate and concurrent duplicate webhook safety.
- Failure webhook lifecycle.
- Security/finality boundaries for forged HMAC, wrong amount, wrong currency, wrong integration, and wrong order reference.
- Storefront payment status read model.
- POS/Admin visibility through normal order data access.

Out of scope:

- Real Paymob Sandbox card authorization.
- Paymob Live authorization.
- Live acquirer/bank approval.
- Live settlement.
- Any change to Paymob credentials, integration ID, iframe ID, environment, tenant configuration, or migrations.

## 3. Existing Paymob Architecture

Observed source path:

1. `POST /api/storefront/checkout` enters `CheckoutController` and `CheckoutService`.
2. Checkout payment method resolution selects Paymob for payment method code `PAYMOB`.
3. Paymob initialization flows through `PaymobPaymentService.CreatePaymentIntentAsync` and Paymob runtime/application services.
4. A local `PaymentIntent` is persisted with provider `Paymob`, integration routing metadata, provider intent/order reference, payment reference, amount, currency, and protected session metadata.
5. Browser handoff uses Paymob Unified Checkout client secret/redirect data; the client secret is transient handoff evidence, not finality evidence.
6. Browser return enters `PaymobWebhooksController.HandleCallback`. That callback validates routing and redirects to storefront pending/status UI; it does not mark payment successful.
7. Real provider finality enters `PaymobWebhooksController.HandleWebhook` at `POST /api/webhooks/paymob`.
8. `PaymentWebhookRequestPipeline` reads the raw body, requires a Paymob HMAC header/query value, resolves tenant server-side via `PaymentWebhookTenantResolver`, rejects claimed-tenant mismatch, and establishes tenant context.
9. `PaymobPaymentService.ProcessWebhookAsync` resolves Paymob runtime credentials via `IPaymobTenantConfigurationResolver.ResolveCredentialsAsync(..., PaymobOperationalCapability.Webhook)`.
10. HMAC canonicalization uses `PaymobWebhookPayloadParser`/`PaymobWebhookHmacVerifier` over the Paymob `obj` fields expected by the implementation.
11. Integration ID is validated against configured Paymob integration IDs.
12. `PaymobWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync` verifies callback integrity, classifies terminality, performs runtime evidence admission where applicable, resolves local `PaymentIntent`, checks replay evidence, and records `PaymobWebhookReplayRecord` for admitted terminal success evidence.
13. `PaymentWebhookLog` records provider event processing status, protected/redacted payload summary, HMAC digest, payment intent ID, and order ID.
14. `ProcessPaymobTransactionAsync` resolves the local `PaymentIntent` by provider order ID / provider payment ID / merchant order ID fallback.
15. `PaymobTransactionFinalityClassifier` classifies captured success, authorization, pending, failed, voided, and refunded states.
16. Success calls `PaymentLifecycleService.HandlePaymentResultAsync` with outcome `Succeeded`, source `GatewayWebhook`, gateway `paymob`, provider event ID, provider transaction ID, amount, and currency.
17. Failure calls the same lifecycle service with outcome `Failed` or `Cancelled` depending on Paymob finality.
18. `PaymentLifecycleService` is the authority for mutating `Order.PaymentStatus`, `PaymentIntent.Status`, paid fields, inventory reservation commit/release, accounting outbox, and related state.
19. Storefront payment status is read through `StorefrontPaymobPaymentsController` / `PaymobPaymentService.GetPaymentStatusAsync`.
20. Admin visibility is through normal order/payment read surfaces backed by `Orders` and `PaymentIntents`.

## 4. Exact Injection Point

The test injection point is a test fixture that submits a realistic Paymob `TRANSACTION` webhook JSON body to `PaymobWebhooksController.HandleWebhook` with an HMAC generated by the same canonical parser used in production.

The simulation does not directly update `PaymentIntent.Status`, `Order.Status`, `Order.PaymentStatus`, inventory rows, or webhook logs. It enters through the real controller, request-body parser, HMAC validation, tenant resolver, payment lookup, idempotency/replay handling, and `PaymentLifecycleService` finality path.

## 5. Fresh Order Evidence

Primary success fixture:

- Order number: `ORD-P8-SUCCESS-0001`
- Product: `Nokia N73`
- Variant: `yellow / 32`
- SKU: `NOKIA-N73-YELLOW-32`
- Quantity: `1`
- Amount: `410.00`
- Currency: `EGP`
- Payment method code: `PAYMOB`
- Provider: `Paymob`
- Integration ID: `3153058`
- Merchant/MID owner: `645875`
- Provider Paymob order/intention reference: `9100***0001` (redacted)
- Provider transaction reference after success: `8800***0001` (redacted)

## 6. Initial State

Before success injection, the focused P8 test asserted:

- `OrderStatus.Pending`
- `PaymentStatus.Pending`
- `CheckoutState.PaymentPending`
- `PaymentIntentStatus.Pending`
- `PaymentIntentCount = 1`
- `OrderCount = 1`
- `ReservationCount = 1`
- `StockMovementCount = 1`
- Stock quantity after reservation: `9`
- Reserved quantity: `1`

The test fixture includes protected Paymob session metadata as handoff evidence. It does not persist or print a real Paymob `client_secret`.

## 7. Simulated Paymob SUCCESS Contract

The simulated event matches the Paymob transaction shape expected by current code:

- `type = TRANSACTION`
- `obj.id = 880000001`
- `obj.pending = false`
- `obj.amount_cents = 41000`
- `obj.success = true`
- `obj.is_auth = true`
- `obj.is_capture = true`
- `obj.is_standalone_payment = false`
- `obj.is_voided = false`
- `obj.is_refunded = false`
- `obj.error_occured = false`
- `obj.currency = EGP`
- `obj.integration_id = 3153058`
- `obj.owner = 645875`
- `obj.order.id = <redacted provider order reference>`
- `obj.order.merchant_order_id = <local order guid>`
- `obj.source_data.pan = redacted test PAN mask`

The HMAC is generated inside the test harness from the canonical payload string. The test HMAC secret is a non-production literal used only by the test fixture and is not printed in logs or the report.

## 8. Webhook/HMAC Processing

Validated paths:

- Valid HMAC SUCCESS: accepted through controller and finalized.
- Invalid HMAC SUCCESS: returns `401 Unauthorized` and leaves order/payment pending.
- Valid HMAC wrong amount: accepted but quarantined as amount mismatch; no paid finality.
- Valid HMAC wrong currency: accepted but quarantined as amount mismatch; no paid finality.
- Valid HMAC wrong integration: rejected before finality; no paid finality.
- Valid HMAC wrong order reference: rejected/no local target; no paid finality.

## 9. Payment Finality

After the signed success webhook:

- `Order.PaymentStatus = Collected`
- `PaymentIntent.Status = Paid`
- `PaymentIntent.Provider = Paymob`
- `PaymentIntent.ProviderIntentId` remains unchanged.
- `PaymentIntent.ProviderPaymentId = 880000001`
- `PaymentIntent.Amount = 410.00`
- `PaymentIntent.Currency = EGP`
- `PaymentIntentCount = 1`

## 10. Order Finality

After the signed success webhook:

- `Order.Status = Confirmed`
- `Order.CheckoutState = Paid`
- `Order.IsPaid = true`
- `Order.PaidAt` is set.
- `Order.PaymentGateway = paymob`
- Order number remains `ORD-P8-SUCCESS-0001`.
- Order does not remain pending and does not become failed.

## 11. Inventory Effects

Initial reservation model:

- One unit was reserved before webhook finality.
- Stock quantity after reservation was `9`.
- Reserved quantity was `1`.

After success:

- `InventoryReservationStatus = Committed`
- Stock quantity remains `8` after one logical commit.
- Reserved quantity becomes `0`.
- Committed quantity becomes `1`.
- `StockMovementCount = 2`.

After duplicate webhook submissions, these counts remained unchanged. No double decrement, negative stock, duplicate reservation finalization, or duplicate stock mutation was observed.

After failure:

- `PaymentStatus = Failed`
- `PaymentIntentStatus = Failed`
- `OrderStatus = Pending`
- `CheckoutState = Failed`
- Stock quantity restored to `9`.
- Reserved quantity became `0`.
- Released quantity became `1`.

## 12. Fulfillment Effects

The seeded order uses the current canonical initial fulfillment state:

- `FulfillmentStatus = Created`
- `FulfillmentMode = Manual`

The P8 tests did not introduce a new fulfillment transition. No fulfillment completion was created by either success or failure simulation. This matches the observed manual fulfillment mode: payment finality confirms the order but does not invent shipping completion.

## 13. POS/Admin Verification

The focused P8 test verifies admin visibility through normal order data access by querying `Orders` after finality and requiring the paid order row to exist with `PaymentStatus.Collected`.

Broader `AdminOrder` filtered tests were not clean in the current worktree: 28/31 passed, 3 failed on existing certification artifact/source-string expectations unrelated to the P8 Paymob success webhook.

## 14. Storefront Verification

The focused P8 test calls `PaymobPaymentService.GetPaymentStatusAsync` after finality and asserted:

- `PaymentStatus = Collected`
- `IsPaid = true`
- `PaymobStatus = captured`

Broader `StorefrontPayment` filtered tests were not clean in the current worktree: 30/32 passed, 2 failed on existing source-certification expectations unrelated to the P8 Paymob webhook finality path.

## 15. Duplicate Webhook Test

The same signed SUCCESS payload was submitted three times:

1. First request finalized payment/order/inventory.
2. Second request returned OK and was idempotent.
3. Third request returned OK and was idempotent.

Final counts:

- `PaymentIntentCount = 1`
- `OrderCount = 1`
- `ReservationCount = 1`
- `WebhookLogCount = 1`
- `ReplayRecordCount = 1`
- `StockMovementCount = 2`

## 16. Concurrent Webhook Test

Five concurrent identical signed SUCCESS submissions were issued through separate controller/context instances sharing the same SQLite connection.

Final counts:

- `PaymentIntentCount = 1`
- `OrderCount = 1`
- `WebhookLogCount = 1`
- `ReplayRecordCount = 1`
- `StockMovementCount = 2`
- Stock quantity `8`
- Reserved quantity `0`
- Committed quantity `1`

Exactly one logical finalization occurred.

## 17. Failure Test

A fresh order `ORD-P8-FAILURE-0001` received a realistic Paymob failure event:

- `success = false`
- `pending = false`
- `error_occured = true`
- `amount_cents = 41000`
- `currency = EGP`
- `integration_id = 3153058`

A production defect was found and fixed: terminal-negative Paymob events were being forced into runtime admission even though the canonical runtime adapter models a valid provider callback as verified evidence, not as a provider-decline decision. The fix keeps terminal-negative Paymob events in the real application lifecycle and excludes them from runtime-success admission requirements.

After the fix, failure finality passes:

- `PaymentStatus = Failed`
- `PaymentIntentStatus = Failed`
- `OrderStatus = Pending`
- `CheckoutState = Failed`
- Stock reservation released.
- No paid finality and no fulfillment completion.

## 18. Retry Test

The required failed → retry → success path did not pass.

Observed sequence:

1. Fresh Paymob order receives failure webhook.
2. Failure finality succeeds.
3. Real retry path attempts `PaymobPaymentService.CreatePaymentIntentAsync` for the same order.
4. The application rejects the retry with `PaymentOrderInvalidStateException` containing `Order:Pending/Payment:Failed`.

Current result: **Retry path FAIL**.

No second order was created to fake retry success, and no direct database mutation was used to bypass this boundary.

## 19. Security/Finality Tests

Validated:

- Browser callback alone is not a success authority; real finality is webhook/lifecycle-driven.
- Forged SUCCESS without valid HMAC cannot finalize.
- Wrong integration cannot finalize.
- Wrong amount cannot finalize and produces amount-mismatch state.
- Wrong currency cannot finalize and produces amount-mismatch state.
- Wrong order reference cannot finalize.
- Duplicate SUCCESS cannot duplicate finality effects.
- Concurrent duplicate SUCCESS cannot duplicate finality effects.

Not fully validated in P8:

- SUCCESS after terminal refund/void/failure beyond the current failure/retry boundary.
- Different merchant/MID rejection as a standalone assertion. The success fixture carries owner `645875`; current P8 focused tests cover integration and order/amount/currency/HMAC boundaries explicitly.

## 20. Browser UX

No live browser was launched in this phase. Backend/frontend evidence reviewed:

- Paymob browser callback endpoint redirects to pending/status UX and does not finalize success.
- Storefront status service reports `Collected` / `captured` after webhook finality.
- Storefront production build passed.

Because P8 certification is already failed on retry semantics and broader tests, browser UX is not certified as a complete end-to-end browser run.

## 21. Regression Results

Executed verification:

- Focused P8 Paymob simulation tests: **5/5 passed**.
- Paymob filtered tests: **185/185 passed**.
- PaymentLifecycle filtered tests: **59/60 passed**; 1 existing provider-refund source-string architecture assertion failed.
- Checkout filtered tests: **456/483 passed**; 27 existing failures include CommerceHub/category behavior, missing migration SQL files, frontend source-string guards, and EF service-provider warning failures.
- Webhook filtered tests: **181/184 passed**; 3 existing provider-refund/unresolved-payment source-string or missing migration SQL checks failed.
- AdminOrder filtered tests: **28/31 passed**; 3 existing certification artifact/source-string checks failed.
- StorefrontPayment filtered tests: **30/32 passed**; 2 existing source-certification checks failed.
- API build: **passed**.
- Full solution build: **passed**, latest run with 93 warnings and 0 errors.
- Storefront production build: **passed** with existing Vite/Browserslist/chunk warnings.
- EF pending model check: **passed**; no pending model changes.
- `git diff --check`: **passed**; only line-ending warnings printed.

## 22. Provider-vs-Application Boundary

P8 proves the SHOPIXY application can consume a deterministic, valid, signed Paymob success event through the real webhook/finality pipeline and safely apply downstream state transitions.

P8 does not prove Paymob Sandbox card authorization. Paymob currently rejects the official test card externally as well as through SHOPIXY, including through a Paymob Payment Link. That is outside this phase.

## 23. Known Limitations

- Failed → retry → success is not currently certifiable because the real retry entry point rejects same-order Paymob retry after failure.
- Browser UX was not run as a live browser automation; backend status and production build were validated.
- Broad non-P8 filtered suites are not green in the current dirty worktree.
- No real Paymob Sandbox authorization was attempted or certified.
- No live payment or settlement was attempted or certified.

## 24. Remaining Risks

- Same-order retry semantics need an explicit product/architecture decision and implementation if Paymob retry after failure should be supported.
- Existing broad test failures in checkout/webhook/admin/storefront payment filters need separate cleanup before full-system certification can be claimed.
- Merchant/MID mismatch should be added as a standalone regression if the current production contract requires owner/MID enforcement beyond integration and tenant credential boundaries.
- Terminal refund/void-after-success finality should be separately exercised after the retry boundary is resolved.

## 25. Final Certification

P8 Certification: **FAILED**

Reason: Paymob success webhook lifecycle passed, but the required failed → retry → success path failed through the real application retry entry point, and broader verification suites are not clean in the current worktree.

Status matrix:

- Fresh order: `ORD-P8-SUCCESS-0001`
- Paymob intention: `9100***0001`
- Payment finality: PASS
- Order finality: PASS
- Inventory: PASS
- Fulfillment: PASS for no unintended fulfillment completion in current manual fulfillment model
- POS Admin: PASS in focused P8 data-access assertion; broader AdminOrder filter FAIL due unrelated existing certification checks
- Storefront: PASS in focused backend status assertion; broader StorefrontPayment filter FAIL due unrelated existing certification checks
- HMAC: PASS
- Duplicate webhook: PASS
- Concurrent webhook: PASS
- Failure path: PASS after terminal-negative admission fix
- Retry path: FAIL
- Security/finality: PASS for HMAC, wrong amount, wrong currency, wrong integration, wrong order reference, duplicate, concurrent duplicate; partial for merchant/MID and refund/void-after-terminal variants
- Tests: P8 5/5, Paymob 185/185, PaymentLifecycle 59/60, Checkout 456/483, Webhook 181/184, AdminOrder 28/31, StorefrontPayment 30/32
- Build: PASS

Paymob Sandbox card authorization was NOT certified by this phase because Paymob itself currently rejects the official test card, including through a Paymob Payment Link. P8 certifies only the SHOPIXY application-side success lifecycle.
