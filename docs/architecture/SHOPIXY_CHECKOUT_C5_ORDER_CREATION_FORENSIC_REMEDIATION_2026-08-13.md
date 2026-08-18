# SHOPIXY Checkout C5 Order Creation Forensic Remediation

Date: 2026-08-13

## 1. Executive Summary

The observed `POST /api/storefront/checkout` failure was not proven to be an order insert, attachment, inventory, payment provider, or CheckoutSession PATCH failure.

The first checkout-path defect found in code was an error-classification and observability defect at the final order transaction boundary: `WarehouseResolutionException` raised during `CreateOrderAsync` was caught by the broad order-creation wrapper and converted to `CheckoutOrderCreationException`, producing `ORDER_CREATION_FAILED` / HTTP 500 instead of the controller's warehouse-resolution 409/422 contract.

Paymob was intentionally excluded.

## 2. Live Failure Evidence

Observed browser response:

- `POST /api/storefront/checkout?lang=en`
- HTTP 500
- response code: `ORDER_CREATION_FAILED`
- failing trace: `3e60f814a9f4977d2d6c27f38a4cf575`

The separate preceding request was:

- `PATCH /api/storefront/checkout-session/51934f3f-c6de-4b4b-9c04-337863a5e4c6`
- HTTP 200
- PATCH trace: `267e600f12dd1d99d404d099bc47f913`

These traces were treated as separate operations.

## 3. Trace / Correlation Analysis

Local server logs and repo artifacts did not contain the failing POST trace `3e60f814a9f4977d2d6c27f38a4cf575`. The available local runtime logs were C4 startup/bind artifacts and did not contain the C5 checkout POST sequence.

Database/runtime probing for the known session showed:

- checkout session persisted with COD selected
- cart bound to the session
- no order created for the checkout cart
- no checkout idempotency completion row for the tenant
- latest warehouse resolution for that session/cart had expired by the time of investigation

Because the authoritative POST log was unavailable, the exact historic stack was reconstructed from code and runtime state rather than from the original trace log.

## 4. CheckoutSession Analysis

The CheckoutSession PATCH is not the direct root cause. It completed with HTTP 200 and persisted checkout state.

Final checkout reads SQL/session state, request authority, cart authority, and warehouse resolution authority. It does not depend on the Mongo abandoned-checkout projection document.

The warning:

`[AbandonedCheckout] Cannot update proof fields - document not found. CheckoutSessionStartedEvent may not have been projected yet`

is classified as `NON-BLOCKING EVENTUAL CONSISTENCY`. `CheckoutSessionProjectionConsumer` logs the missing Mongo document and continues; `CheckoutSessionService.TryPublishSensitiveProofEventAsync` is best-effort proof event publishing after SQL persistence.

## 5. First Failing Operation

The first failing operation proven in the final checkout code path is the final warehouse-resolution validation/marking phase inside order creation:

- pre-order check: `CheckoutService.ProcessCheckoutAsync` calls `ValidateSubmittedResolutionAsync(... lockForCheckout: false)`
- transactional final check: `CheckoutService.CreateOrderAsync` calls `ValidateSubmittedResolutionAsync(... lockForCheckout: true)`
- final lifecycle mark: `CreateOrderAsync` calls `MarkOrderedAsync(...)`

Before C5, any `WarehouseResolutionException` from this transactional section was wrapped as `CheckoutOrderCreationException`.

## 6. Root Cause

Root cause: expected final checkout warehouse-resolution conflicts were hidden behind the generic order-creation wrapper.

When a resolution expires, becomes stale, changes version, no longer matches the cart/session/tenant, or cannot be marked ordered, the correct error is the existing warehouse-resolution contract, not `ORDER_CREATION_FAILED` / 500.

## 7. Order Creation Trace

Current execution order:

1. `CheckoutController.Checkout`
2. `CheckoutService.ProcessCheckoutAsync`
3. request/cart/session/dynamic-field validation
4. shipping/warehouse prevalidation
5. payment method validation
6. idempotency claim
7. `CreateOrderAsync`
8. transaction-scoped revalidation
9. final warehouse resolution lock validation
10. inventory reservation
11. order/order item construction
12. `_dbContext.Orders.Add(order)` and `SaveChangesAsync`
13. attachment validation/claim and checkout field snapshot refresh with real `Order.Id`
14. bind reservations to order
15. shipping/payment/order lifecycle/outbox state
16. `MarkOrderedAsync`
17. commit/idempotency completion/response

## 8. Dynamic Fields / Attachments

C3 ordering remains correct in current code:

- dynamic/attachment references are prevalidated with `OrderId = null`
- order is inserted and flushed
- checkout field definitions are revalidated with the real `Order.Id`
- attachments are claimed through `ValidateAndAttachAsync`
- order checkout field snapshots are refreshed after claim-capable validation

Dynamic field validation exceptions derive from checkout-specific exceptions and are not converted into the order-creation 500 wrapper.

## 9. Shipping Integration

Shipping was not redesigned. C5 verified the final checkout consumes:

- selected `ShippingMethodId`
- fulfillment warehouse from `WarehouseResolution`
- final warehouse-resolution authority in checkout

The live known resolution had expired by the time of investigation, which is consistent with a final warehouse-resolution business conflict path.

## 10. Inventory

Inventory reservation is inside the checkout transaction path and uses the authoritative cart and final warehouse resolution. C5 did not bypass inventory or change reservation semantics.

Relevant backend inventory/checkout tests passed in the 99-test slice.

## 11. COD Payment Integration

COD was kept in scope; Paymob was not inspected or modified.

COD selection was persisted on the known checkout session. The C5 fix does not create or resolve any Paymob/Stripe/Fawry intent and does not change COD provider behavior.

## 12. Pricing

No browser-provided totals are trusted by the checkout service. Pricing mismatch remains handled through the existing checkout total mismatch contract and compensation path.

C5 did not alter pricing.

## 13. Transaction Boundary

Final order creation uses a transaction around warehouse finalization, inventory reservation binding, order creation, attachment snapshot/claim integration, outbox-relevant state, and commit.

The remediation preserves rollback behavior. It changes classification only after the transactional method has thrown.

## 14. Outbox/Event Findings

`CheckoutSessionProofEvent` is session/proof publishing, not the final order transaction dependency.

Sensitive proof event publishing may bypass the outbox by design and can race with the abandoned-checkout projection. The missing Mongo abandoned-checkout document warning does not block final checkout.

## 15. Error Classification

Expected classification after C5:

- warehouse resolution stale/expired/conflict: existing `WarehouseResolutionException` controller path, 409/422 as encoded by the exception
- dynamic field validation: checkout-specific validation contract
- unexpected order persistence defect: `ORDER_CREATION_FAILED` / 500

The generic wrapper is now reserved for unexpected order creation defects.

## 16. Observability

Added server-side diagnostic logging before rethrowing `WarehouseResolutionException` from order creation:

- cart id
- checkout session id
- warehouse resolution id
- failure phase
- status code
- error code
- correlation id

No secrets, payment credentials, HMAC, tokens, file contents, or sensitive personal data are logged.

## 17. Fix Implemented

Production change:

- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- preserve `WarehouseResolutionException` in the order-creation catch
- log final checkout warehouse-resolution rejection with safe context
- rethrow to the controller's specific handler

No schema migration was added.

## 18. Regression Tests

Added regression coverage in:

- `PointOfSale.Tests/Architecture/StorefrontCheckoutSubmissionAuthorityCertificationTests.cs`

The new test proves:

- final warehouse-resolution conflicts are not wrapped as order creation 500
- `CheckoutController` catches `WarehouseResolutionException`
- the specific catch precedes `CheckoutOrderCreationException`

The source-test root discovery helper was hardened so artifact-based test runs do not mistake build output folders for the repo root.

## 19. Browser Verification

Live browser COD Place Order was not completed in this session.

Direct API probing against the running local API returned `TENANT_REQUIRED`, because the current environment did not admit a secure storefront tenant context through the tested host/header combinations. The original session also became abandoned/expired during investigation.

Therefore C5 cannot be certified as browser-pass.

## 20. Build/EF Verification

Completed:

- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore -p:OutDir=...` passed
- `dotnet restore PointOfSale.sln --artifacts-path .codex-temp/c5-sln-artifacts` passed after network approval
- `dotnet build PointOfSale.sln --no-restore --artifacts-path .codex-temp/c5-sln-artifacts` passed
- focused C5 regression: 5/5 passed
- relevant backend checkout/session/COD/inventory slice: 99/99 passed
- `npm run test -- --run` in `PointOfSale.Storefront`: 299/299 passed
- `npm run build` in `PointOfSale.Storefront`: passed
- `dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --no-build`: no pending model changes

Initial default-bin `dotnet test`/build attempts were blocked by a running `PointOfSale.API` process locking debug binaries, so isolated artifact/output paths were used.

## 21. Remaining Limitations

The historic failing POST server trace was unavailable locally.

The exact live browser COD order placement remains unverified because secure storefront tenant admission could not be completed from this session.

## 22. Paymob Deferral

Paymob intentionally excluded from this step.

C6 should handle Paymob runtime readiness and provider verification.

## 23. Worktree Safety

The worktree was dirty before C5. Existing unrelated modifications were not reverted, stashed, reset, committed, or pushed.

C5 touched only checkout service behavior, checkout authority test coverage, and this report.

## 24. Final Certification

`BLOCKED`

Reason: internal checkout remediation and regression/build verification passed, but actual browser COD Place Order was not successfully completed and verified. The fix is ready for a fresh verified storefront COD checkout run; C5 cannot honestly use `PASS WITH EXTERNAL VERIFICATION BLOCKED` because that status requires an actual browser COD Place Order success.
