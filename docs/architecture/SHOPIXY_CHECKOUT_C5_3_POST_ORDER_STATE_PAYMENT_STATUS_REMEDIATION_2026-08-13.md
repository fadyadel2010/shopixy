# SHOPIXY Checkout C5.3 Post-Order State and Payment Status Remediation

Date: 2026-08-13
Status: PASS WITH EXTERNAL VERIFICATION BLOCKED

## 1. Executive Summary

C5.3 reviewed the Storefront lifecycle immediately after a successful checkout. The backend checkout response contract already returns authoritative `OrderId` and `OrderNumber`, but the Storefront post-order path did not have a single enforced identity contract, COD success did not reliably clear the active cart/order state, and the payment-status authority path could reject a legitimate authenticated Storefront customer because the Storefront access cookie was scoped to `/api/storefront` while the status endpoint lived under `/api/payments`.

The remediation keeps authorization intact. It does not remove `ORDER_ACCESS_DENIED`, does not trust browser tenant/order data, and does not expose payment secrets. It makes the checkout response the only source for the pending route, carries the order number as checkout-response evidence, uses a Storefront-scoped status URL, preserves `/api/payments` compatibility, fixes multi-path access-cookie clearing, and stops polling on true authorization failure.

Fresh live verification created order:

- OrderId: `4ef04598-4aae-4bea-92ad-4aaef1479344`
- OrderNumber: `ORD-83B3-2026-01008`
- Total: `410.00 EGP`
- Payment method: `COD`
- Pending route: `/checkout/pending?orderId=4ef04598-4aae-4bea-92ad-4aaef1479344&orderNumber=ORD-83B3-2026-01008`
- Pending page: displayed `ORD-83B3-2026-01008`
- Backend status shown by Storefront: `PENDING`

The only external verification limitation is that a separate live Tenant B browser/customer environment was not available in this single local Storefront runtime. Cross-customer, guest-token, tampered-order, and tenant-bound denial behavior is covered by backend regression tests.

## 2. Current Live Symptom

Initial observed symptom:

- Browser route: `/checkout/pending?orderId=04ca9737-00c8-4daf-bd5c-6b34ba0d8515`
- UI: `checkout.pending.orderUnavailable`
- Polling: `GET /api/payments/status/04ca9737-00c8-4daf-bd5c-6b34ba0d8515`
- Result: HTTP `403`, code `ORDER_ACCESS_DENIED`

The known C5.2 order was:

- `c481589a-1a46-4c3e-a8e6-33bf8c44dbc3`
- `ORD-83B3-2026-01005`

C5.3 did not assume the observed pending order was stale. A fresh checkout was used to reproduce the post-order lifecycle and to prove the first current bad points.

## 3. Previous Fix Review

| Fix | Defect solved | Active now | Correct now | C5.3 dependency | Stale-state risk | New C5.3 regression |
| --- | --- | --- | --- | --- | --- | --- |
| C4 checkout state sequencing | Cart, checkout session, shipping, pricing, payment, and submit sequencing | Yes | Yes | Pending state depends on C4 final success state | Success `orderResult` could remain live after navigation | `checkoutStore.validation.test.ts`, `postOrderNavigation.test.ts` |
| C4 payment availability | Only executable Storefront payment methods are selectable | Yes | Yes | COD availability used in live proof | None found | Existing payment-method tests plus COD checkout path |
| C5 error classification | Warehouse resolution conflicts no longer collapse to order-creation 500 | Yes | Yes | Fresh product had valid same-tenant warehouse resolution | None found | No new C5.3 test needed |
| C5.1 PostgreSQL xmin locks | Raw PostgreSQL full-entity locks include mapped `xmin` | Yes | Yes | Checkout and payment lifecycle can execute under PostgreSQL | None found | Existing C5.1 tests remain sufficient |
| C5.2 order number sequence | `order_number_seq` exists and order numbers are generated | Yes | Yes | Pending page must display server order number | Missing order number would break UX | `postOrderNavigation.test.ts`, `paymentFlow.test.ts` |
| C5.2 idempotency cleanup | Completed checkout replay is deterministic and cleanup avoids unrelated dirty snapshot writes | Yes | Yes | Post-order flow receives deterministic checkout result | If COD cart was not cleared, same cart could replay previous order | `checkoutStore.validation.test.ts` |
| C5.2 snapshot ordering | Checkout snapshots are captured after order identity exists | Yes | Yes | Payment status/order display refers to real persisted order | None found | Existing snapshot tests remain sufficient |
| C5.2 OrderCreatedEvent version | Event version uses tracked aggregate version | Yes | Yes | No direct post-order dependency | None found | Existing outbox tests remain sufficient |
| COD payment recovery | COD reference recovery updates order/payment reference | Yes | Yes | Status lookup must remain order-id based | Status must not depend on recovered provider reference | `paymentFlow.test.ts`, backend payment tests |

## 4. Checkout Success Response

Backend contract inspected:

- `CheckoutResponseDto` includes `OrderId`, `OrderNumber`, `PaymentStatus`, `OrderStatus`, `PaymentMethod`, `CartId`, payment fields, and guest access token fields where applicable.
- `CheckoutService` returns `OrderId = order.Id` and `OrderNumber = order.OrderNumber`.
- `CheckoutController` applies guest-order cookies/tokens for anonymous checkout and nulls token values before normal response serialization where appropriate.

Live response evidence is indirect but authoritative through the Storefront success state: `CheckoutPage` now builds the pending URL only from `orderResult`, and the fresh COD flow navigated to `orderId=4ef04598-4aae-4bea-92ad-4aaef1479344&orderNumber=ORD-83B3-2026-01008`.

## 5. OrderId Propagation

Required contract after remediation:

`Checkout response OrderId == checkoutStore orderResult.orderId == pending route orderId == payment status orderId == persisted order`.

Implementation:

- `PointOfSale.Storefront/src/features/checkout/postOrderNavigation.ts`
  - `buildPendingCheckoutUrl(order)` requires `orderId` and `orderNumber`.
  - It throws instead of navigating with incomplete/stale success evidence.
  - `readPendingCheckoutRouteEvidence(...)` reads only route evidence and does not consult local storage.
- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
  - Uses `buildPendingCheckoutUrl(orderResult)`.
  - Clears `orderResult` before `window.location.replace(...)`.
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
  - Clears old `orderResult` at submit start.
  - Clears COD cart after an authoritative COD checkout response.

First bad point found:

- COD post-checkout state could leave the active cart/order state available for C5.2 deterministic idempotency replay, making stale order identity visible after a later checkout attempt.

Regression:

- `postOrderNavigation.test.ts`
- `checkoutStore.validation.test.ts`

## 6. OrderNumber Propagation

Root cause:

- The pending page had no enforced checkout-response order-number contract and could render unavailable state when status authorization failed before a canonical status payload was loaded.
- Missing localization also allowed `checkout.pending.orderUnavailable` to appear as a raw key.

Implementation:

- Pending route now carries `orderNumber` from the checkout response.
- Pending page displays server status `orderNumber` when available, otherwise the route order number for non-authorized pending states.
- Unauthorized views intentionally hide the order number.
- English and Arabic locale entries were added for unavailable and access-denied states.

Live evidence:

- Pending page displayed `Order Number` as `ORD-83B3-2026-01008`.
- No raw localization key appeared.

## 7. Guest/Anonymous Authority

Existing guest authority is tenant and order bound:

- Browser proves guest order ownership with `X-Session-Id` plus a checkout payment token from header or cookie.
- Token validation is tenant-bound.
- Token order id must match the requested order id.
- Mismatched token/order and cross-tenant token validation return `403 ORDER_ACCESS_DENIED`.

C5.3 did not weaken this. It added regression coverage for:

- guest token in cookie succeeds for same order,
- guest token for another order is denied,
- tenant-mismatched guest token is denied.

Authenticated customer authority is handled through `StorefrontJwt` or `CustomerJwt`, matching the order owner/customer id.

## 8. Payment Status Endpoint

First authorization bad point:

- Storefront customer login set the access cookie at `/api/storefront`.
- Pending page polled `/api/payments/status/{orderId}`.
- The browser did not send the Storefront access cookie to `/api/payments`.
- `PaymentRequestAuthorizationService` saw no authenticated customer and returned `ORDER_ACCESS_DENIED` for a legitimate customer-owned order.

Implementation:

- `PointOfSale.API/Services/Storefront/StorefrontCookieHelper.cs`
  - Sets Storefront access token cookies for `/api/storefront` and `/api/payments`.
  - Clears both scoped paths and the legacy broad `/api` path.
- `PointOfSale.API/Controllers/PaymentsController.cs`
  - Adds `/api/storefront/payments/status/{orderId:guid}` to the same canonical status action.
  - The same authorization service runs; no ownership check was bypassed.
- `PointOfSale.Storefront/src/services/api/paymentStatusApi.ts`
  - Polls `/api/storefront/payments/status/{orderId}` so current Storefront auth cookies are sent.

Regression:

- `CookieAuthSecurityTests`
- `PaymentsControllerTests`
- `paymentStatusApi.test.ts`

## 9. Tenant Isolation

Tenant isolation remains server-side:

- Tenant is resolved by middleware/context, not trusted from browser tenant fields.
- Status authorization receives the resolved tenant id and requested order id.
- Payment access info is loaded by tenant and order.
- Guest token validation is tenant-bound.

Regression evidence:

- Guest cross-tenant token denial is covered in `PaymentRequestAuthorizationServiceTests`.
- Unauthorized direct route probe to `/api/storefront/payments/status/4ef04598-4aae-4bea-92ad-4aaef1479344` without browser authority returned HTTP `403`, proving the alias did not expose status anonymously.

External limitation:

- A second live Tenant B Storefront browser was not available in this local runtime; automated tenant-bound denial tests cover the contract.

## 10. COD Payment Lifecycle

Observed live COD semantics:

- Checkout created a COD order for `410.00 EGP`.
- Pending page remained in processing state.
- Canonical backend status displayed by Storefront: `PENDING`.

Conclusion:

- COD remains an order/payment lifecycle pending state for Storefront post-checkout.
- The status read is keyed by `OrderId`, not by a mutable provider reference.
- COD recovery/reference updates do not invalidate the customer status lookup.

No Paymob, Stripe, Fawry live execution, or payment-method redesign was performed.

## 11. Pending Page / Polling

Inspected and remediated:

- Initial request uses route `orderId`.
- Route `orderNumber` is preserved.
- Polling stops on access denial.
- Polling stops on terminal status through existing resolver behavior.
- Manual Refresh reuses the same canonical `orderId`.
- Transient failures remain safe and retryable.
- Unauthorized failure no longer loops indefinitely.

Implementation:

- `PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx`
- `PointOfSale.Storefront/src/features/checkout/paymentFlow.ts`

Live evidence:

- Correct pending state showed `Your payment is being processed`.
- Correct order number showed `ORD-83B3-2026-01008`.
- Backend status showed `PENDING`.
- Access-denied copy appeared only before relogin/authority restoration.

## 12. Cache / Persisted State

Findings:

- `activeCheckoutOrderContext.ts` is intentionally not a persisted authority store.
- Pending route parsing does not consult localStorage/sessionStorage.
- `checkoutStore` could retain in-memory `orderResult` after navigation and COD cart state could remain active after success.

Fix:

- Clear stale `orderResult` at submit start.
- Clear `orderResult` before pending navigation.
- Await COD cart cleanup after authoritative COD response.

This avoids using stale browser state as post-order identity while preserving user checkout input during normal pre-submit steps.

## 13. Root Causes

### Defect 1: No canonical post-order navigation contract

- Symptom: pending route could diverge from the latest successful checkout identity or miss order number.
- First bad point: `CheckoutPage` consumed raw `orderResult` without a dedicated required identity contract.
- Root cause: no helper enforced `OrderId + OrderNumber` as the only success navigation source.
- File/symbol: `CheckoutPage.tsx`, new `buildPendingCheckoutUrl`.
- Fix: added `postOrderNavigation.ts`; pending URL is built only from checkout response identity.
- Regression: `postOrderNavigation.test.ts`.
- Runtime evidence: fresh route used `4ef04598-4aae-4bea-92ad-4aaef1479344` and `ORD-83B3-2026-01008`.

### Defect 2: Authenticated customer status cookie did not reach status endpoint

- Symptom: legitimate customer-owned status request returned `403 ORDER_ACCESS_DENIED`.
- First bad point: browser request to `/api/payments/status/{orderId}` did not include `/api/storefront` scoped auth cookie.
- Root cause: route/cookie scope mismatch.
- File/symbol: `StorefrontCookieHelper`, `PaymentsController.GetCanonicalPaymentStatus`, `paymentStatusApi.ts`.
- Fix: dual access-cookie scope plus Storefront-scoped status alias.
- Regression: `CookieAuthSecurityTests`, `PaymentsControllerTests`, `paymentStatusApi.test.ts`.
- Runtime evidence: after relogin, pending page showed backend `PENDING` for the same order.

### Defect 3: COD success left stale cart/order replay risk

- Symptom: old successful COD cart/order could remain available to deterministic idempotency replay.
- First bad point: `checkoutStore.submitCheckout` did not clear active COD cart after success.
- Root cause: C5.2 idempotency replay is correct, but Storefront post-order cleanup did not close the cart lifecycle.
- File/symbol: `checkoutStore.submitCheckout`.
- Fix: clear stale `orderResult` at submit start, await COD cart clear after response, expose `clearOrderResult`.
- Regression: `checkoutStore.validation.test.ts`.
- Runtime evidence: fresh cart was empty before C5.3 live checkout; after successful checkout header no longer showed a cart count.

### Defect 4: Unauthorized polling retried like a transient failure

- Symptom: pending page repeatedly polled after `403 ORDER_ACCESS_DENIED`.
- First bad point: pending page did not classify access denial as terminal.
- Root cause: payment-status error handling treated authorization failure like backend uncertainty.
- File/symbol: `CheckoutPendingPage.tsx`, `isPaymentStatusAccessDeniedFailure`.
- Fix: terminal access-denied state, safe copy, no infinite polling.
- Regression: `paymentFlow.test.ts`.
- Runtime evidence: unauthorized probe/page state stopped with access-denied UX instead of request storm.

### Defect 5: Multi-path access-cookie clearing was incomplete

- Symptom: after adding multiple cookie paths, ASP.NET `Cookies.Delete` collapsed same-name deletes to one path.
- First bad point: `ClearAccessTokenCookie` used repeated `Delete` calls for the same cookie name with different paths.
- Root cause: Set-Cookie header de-duplication for same name in response.
- File/symbol: `StorefrontCookieHelper.ClearAccessTokenCookie`.
- Fix: append explicit expired cookies for `/api/storefront`, `/api/payments`, and legacy `/api`.
- Regression: `StorefrontCookieHelper_ClearAccessTokenCookie_ExpiresStorefrontPaymentsAndBroadApiCookiePaths`.
- Runtime evidence: relogin after restart restored authenticated Storefront status access.

## 14. Fixes Implemented

Backend:

- `PointOfSale.API/Services/Storefront/StorefrontCookieHelper.cs`
- `PointOfSale.API/Controllers/PaymentsController.cs`
- `PointOfSale.API/appsettings.json`
- `PointOfSale.API/appsettings.Test.json`
- `PointOfSale.API/appsettings.Staging.json`

Storefront:

- `PointOfSale.Storefront/src/features/checkout/postOrderNavigation.ts`
- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- `PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx`
- `PointOfSale.Storefront/src/features/checkout/paymentFlow.ts`
- `PointOfSale.Storefront/src/services/api/paymentStatusApi.ts`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/i18n/locales/en.json`
- `PointOfSale.Storefront/src/i18n/locales/ar.json`

Tests:

- `PointOfSale.Tests/Security/CookieAuthSecurityTests.cs`
- `PointOfSale.Tests/Payments/PaymentRequestAuthorizationServiceTests.cs`
- `PointOfSale.Tests/Payments/PaymentsControllerTests.cs`
- `PointOfSale.Storefront/src/features/checkout/postOrderNavigation.test.ts`
- `PointOfSale.Storefront/src/features/checkout/paymentFlow.test.ts`
- `PointOfSale.Storefront/src/services/api/paymentStatusApi.test.ts`
- `PointOfSale.Storefront/src/store/checkoutStore.validation.test.ts`

## 15. Security Review

Maintained:

- `ORDER_ACCESS_DENIED` remains for unauthorized access.
- Status endpoint still calls `PaymentRequestAuthorizationService`.
- Other customer and guest-token mismatch are denied.
- Tenant-bound guest token mismatch is denied.
- Browser-supplied tenant id is not trusted.
- Browser-supplied order number is display evidence only; canonical status must still prove the order id and server order number.
- Payment secrets are not returned by status.

Changed safely:

- Storefront status polling uses a Storefront-scoped URL so Storefront auth cookies are available.
- `/api/payments/status/{orderId}` remains available for compatibility and remains authorized.
- Cookie cleanup now removes every scoped access cookie path.

## 16. Regression Tests

Storefront:

```text
npm test -- checkoutStore.validation.test.ts postOrderNavigation.test.ts paymentFlow.test.ts paymentStatusApi.test.ts
4 files passed, 19 tests passed
```

Backend:

```text
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~CookieAuthSecurityTests|FullyQualifiedName~PaymentRequestAuthorizationServiceTests|FullyQualifiedName~PaymentsControllerTests"
31 tests passed
```

Coverage mapping:

- Checkout response route identity: `postOrderNavigation.test.ts`
- Missing order number rejected: `postOrderNavigation.test.ts`
- Pending route uses response identity: `CheckoutPage.tsx` integration through helper and tests
- Pending page displays order number: route/status tests plus live proof
- Legitimate guest status: `PaymentRequestAuthorizationServiceTests`, `PaymentsControllerTests`
- Another guest/tampered token: `PaymentRequestAuthorizationServiceTests`
- Cross-tenant guest token: `PaymentRequestAuthorizationServiceTests`
- COD pending status: `paymentFlow.test.ts`
- COD completed terminal behavior: `paymentFlow.test.ts`
- Old order result cannot override new checkout: `checkoutStore.validation.test.ts`
- Unauthorized polling stops: `paymentFlow.test.ts`
- Refresh status uses same order id: pending page implementation and live refresh proof

## 17. Live Browser Verification

Fresh browser flow:

1. Cart was empty.
2. Added `Nokia N73`, variant `yellow / 32`, quantity `1`, subtotal `400.00 EGP`.
3. Opened checkout.
4. Filled required customer/address/custom fields.
5. Server shipping authority loaded `Standard Shipping`, `10.00 EGP`.
6. COD was selected.
7. Review showed total `410.00 EGP`.
8. Placed order.

Result:

- Route: `http://shopixy.localhost:5175/checkout/pending?orderId=4ef04598-4aae-4bea-92ad-4aaef1479344&orderNumber=ORD-83B3-2026-01008`
- Pending page after relogin/refresh:
  - `Your payment is being processed`
  - `Order Number`
  - `ORD-83B3-2026-01008`
  - `Backend status: PENDING`

Additional runtime proof:

- Direct unauthenticated route probe to `/api/storefront/payments/status/4ef04598-4aae-4bea-92ad-4aaef1479344` returned `403`, proving the new alias did not bypass authorization.

## 18. Build/EF

Completed:

```text
dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore
Build succeeded. 0 warnings, 0 errors.

dotnet build PointOfSale.sln --no-restore
Build succeeded. 0 warnings, 0 errors.

npm run build
Build succeeded.

dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj --context ApplicationDbContext --no-build
No changes have been made to the model since the last migration.
```

Storefront build warnings were pre-existing non-blocking Vite warnings: chunk size, mixed static/dynamic imports, and stale Browserslist data.

## 19. Remaining Limitations

- A separate live Tenant B Storefront environment was not available for browser-based cross-tenant probing. Automated backend tests cover tenant-bound guest authority and unauthorized status denial.
- Existing unrelated product variant UX warning `product.invalidVariantCombination` appeared while selecting product variants. It did not block checkout and is outside C5.3.
- The worktree contains broad pre-existing C1-C5.2, payment, shipping, admin, and test changes. C5.3 did not revert or rewrite unrelated work.

## 20. Worktree Safety

Before and during C5.3:

- The worktree was already dirty from previous streams.
- No `git reset`, `git clean`, `git restore`, `git checkout`, stash, commit, or push was performed.
- Unrelated Admin Orders and unrelated shipping/payment method redesign paths were not modified for C5.3.

After C5.3:

- `git diff --check` exited `0`.
- It reported only LF-to-CRLF warnings for existing working-copy files.
- `git status --short` still shows broad pre-existing modifications plus C5.3 additions.

## 21. Final Certification

PASS WITH EXTERNAL VERIFICATION BLOCKED

Certified C5.3 results:

- Successful checkout response identity is correct.
- Pending route uses the checkout response order id.
- Pending route carries and displays the checkout response order number.
- Legitimate authenticated customer can retrieve payment status after auth restoration.
- Unauthorized access remains blocked.
- Guest, tampered-order, and tenant-bound denial rules remain covered by regression tests.
- COD lifecycle is coherent as `PENDING` for Storefront post-order status.
- Polling does not retry authorization failure indefinitely.
- Regression tests pass.
- Backend builds, Storefront build, EF pending-model check, and diff hygiene pass.

The external verification block is limited to live second-tenant/browser availability. The local single-tenant live checkout and automated tenant/authority regressions pass.
