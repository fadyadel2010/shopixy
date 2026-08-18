# SHOPIXY Manual Wallet Payment-Session Evidence C5.6 Remediation

## 1. Executive Summary

Storefront Manual Wallet checkout was returning HTTP 200, but the storefront rejected the response in `requireStorefrontPaymentSessionAuthority()` because a retry/replay of the already accepted Manual Wallet order returned terminal order evidence instead of pending Manual Wallet payment-session evidence.

The remediation preserves the authority guard, adds backend-authored `paymentMethodCode` to checkout responses, keeps fresh Manual Wallet checkouts on the pending evidence contract, and allows terminal Manual Wallet replays only when the backend proves the payment method and returns no reusable payment-session artifact.

Follow-up live testing also proved the old Manual Wallet cart could remain reusable after order creation. The storefront now clears the cart after successful Manual Wallet checkout, matching COD offline checkout behavior and preventing accidental resubmission of an already-created order.

## 2. Live Failure Evidence

User browser evidence showed:

- `POST /api/storefront/checkout`
- HTTP 200
- Frontend exception: `Backend checkout response did not prove pending ManualWallet payment-session evidence.`
- Stack: `paymentSessionAuthority.ts` -> `checkoutStore.ts` -> `CheckoutPage.tsx`

Runtime DB probe found only the previously approved Manual Wallet order:

- Order: `ORD-83B3-2026-01010`
- Payment status: `Collected`
- Checkout state: `Paid`
- Payment intent present and already terminal

## 3. Actual Checkout Response

The replay path for terminal orders intentionally scrubs payment artifacts:

- `PaymentIntentId = null`
- `IsManualWallet = false`
- `WalletType = null`
- `MerchantWalletNumber = null`
- `WalletInstructions = null`
- `PaymentStatus = Collected`

That is correct for not returning stale payment-session data, but the response did not previously carry server-authored payment-method identity.

## 4. Frontend Authority Guard

Owner: `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`.

The guard requires base order identity, cart identity, amount, currency, payment status, selected provider, and provider-specific artifacts. For fresh Manual Wallet, it still requires:

- `paymentIntentId`
- `isManualWallet === true`
- `paymentStatus` in pending/awaiting-verification set
- `walletType`
- no cross-provider artifacts

## 5. Backend Manual Wallet Lifecycle

Owner: `PointOfSale.API/Services/Checkout/CheckoutService.cs`.

Fresh Manual Wallet creates a server-side payment intent/session and returns pending evidence. Admin approval later transitions payment to `Collected` and order to `Confirmed`. Manual Wallet approval does not skip fulfillment/shipment lifecycle.

## 6. Response Contract

Added `CheckoutResponseDto.PaymentMethodCode` as canonical backend-owned method identity. The backend now populates it for:

- fresh checkout response
- stored idempotency replay payload
- terminal replay from existing order
- non-terminal replay from existing order

## 7. Payment Session Authority

The storefront now requires `response.paymentMethodCode` to match the selected provider before accepting any checkout response. This prevents the browser-selected payment method from being the only method discriminator.

Fresh Manual Wallet still requires pending payment-session evidence. Terminal Manual Wallet replay is accepted only when:

- backend `paymentMethodCode` is `MANUALWALLET`
- payment status is terminal, including `Collected`
- reusable payment-session artifacts are absent

## 8. Order/Payment Identity

The replay acceptance remains bound to backend order identity:

- `orderId`
- `orderNumber`
- `cartId`
- `paymentStatus`
- `paymentMethodCode`

No browser-generated payment session ID is trusted or fabricated.

## 9. Tenant/Guest Security

Tenant, customer/guest, checkout session, cart, and order binding remain backend-owned. The frontend does not gain any ability to override tenant, order, payment intent, or proof identifiers.

## 10. C5.3/C5.5 Regression Review

C5.3 post-order identity remains intact because `/checkout/pending` still receives canonical `orderId` and `orderNumber`.

C5.5 Manual Payment approval remains intact because terminal replay does not expose proof URLs, storage keys, wallet credentials, or approval authority.

## 11. Root Cause

First failing point: frontend Manual Wallet authority predicate.

Root cause: terminal checkout replay for a previously approved Manual Wallet order returned correct terminal order evidence, but the response lacked backend-authored payment method identity, so the frontend had no safe way to distinguish a legitimate terminal Manual Wallet replay from a malformed Manual Wallet pending response.

## 12. Fix

Files changed for C5.6:

- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.test.ts`
- `PointOfSale.Tests/Architecture/VodafoneCashPaymentAuthorityCertificationTests.cs`

Implementation:

- Added `PaymentMethodCode` to checkout DTO.
- Populated `PaymentMethodCode` from server-side order/payment method authority.
- Required frontend backend-method match.
- Kept pending Manual Wallet evidence checks.
- Accepted terminal Manual Wallet replays without reusable artifacts.
- Updated storefront wire types for backend enum strings and nullable scrubbed Manual Wallet fields.

## 13. Regression Tests

Passed:

- `npm test -- --run src/features/checkout/paymentSessionAuthority.test.ts src/store/checkoutStore.validation.test.ts`
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~ManualPaymentAuthorityCertificationTests|FullyQualifiedName~VodafoneCashPaymentAuthorityCertificationTests|FullyQualifiedName~ManualWalletCheckoutValidationForensicTests|FullyQualifiedName~CheckoutRequestIdempotencyCoordinatorTests" -p:UseAppHost=false -p:OutputPath=.codex-temp\test-bin\c56-focused -clp:ErrorsOnly`

Coverage added/validated:

- valid pending Manual Wallet evidence accepted
- missing pending evidence rejected
- backend payment method mismatch rejected
- terminal Manual Wallet replay accepted without reusable artifacts
- architecture certification for backend method-code contract

## 14. Live Browser Verification

Pre-fix live evidence was provided by the user screenshot and debug log.

Post-fix live browser verification is externally blocked until the running API/storefront processes are restarted or hot-reloaded and the user retries. Local static/build verification passed.

User follow-up live test confirmed the frontend authority exception is gone. The browser landed on confirmation for `ORD-83B3-2026-01010`; DB evidence showed this was the previously approved order (`Collected` / `Paid`), so Admin Manual Payments was empty by design. A final storefront fix now clears Manual Wallet carts after successful checkout to prevent this replay from looking like a new order attempt.

## 15. Build/EF

Passed:

- `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore -clp:ErrorsOnly`
- `dotnet build PointOfSale.sln --no-restore -clp:ErrorsOnly`
- `npm run build`
- `dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API`
- `git diff --check`

EF result: no pending model changes.

`git diff --check` returned exit code 0 with existing LF/CRLF warnings only.

## 16. Remaining Limitations

The live browser must be retried against restarted/hot-reloaded services. The attached debug log also contained unrelated background recovery errors (`CommerceHubRefundService` translation and shipment `xmin` query shape) that were not modified in C5.6 because they are outside this Manual Wallet response-contract failure.

## 17. Worktree Safety

Captured during the run:

- `git status --short`
- `git diff --stat`
- `git diff --check`

No reset, clean, restore, stash, commit, or push was performed.

## 18. Final Certification

PASS WITH EXTERNAL VERIFICATION BLOCKED

Reason: local source, tests, builds, and EF checks pass; the authority guard remains enabled; backend method authority was added to the response contract; terminal Manual Wallet replay is handled without exposing reusable payment-session evidence. Final live browser confirmation requires the running services to pick up these source changes and the user to retry the Manual Wallet flow.
