# SHOPIXY Manual Wallet Fresh Order and Admin Visibility C5.7

Date: 2026-08-14  
Project: SHOPIXY / PointOfSale  
Certification: PASS WITH EXTERNAL VERIFICATION BLOCKED

## 1. Fresh Order Evidence

The API and Storefront were restarted from the C5.6/C5.7 source. The runtime baseline query initially contained one Manual Wallet order only:

- old order: `ORD-83B3-2026-01010`
- old order id: `85df9844-4b82-4345-9374-d4ad3972a9f3`
- old payment: `Collected`
- old checkout: `Paid`

That order was not used for C5.7.

The final post-remediation checkout used a new guest session, cart, checkout session, proof object, request, order, and payment intent:

- guest session: `c5-7-9d3372944dd84299bb492c5dbb99b6c4`
- cart: `0c62a297-f63c-4e0a-8a0c-04a85325b61b`
- checkout session: `087bdf42-d30e-45fc-a115-b02765863f58`
- order id: `87054a3e-d4a3-4d05-9e8c-5bd0120b177d`
- order number: `ORD-83B3-2026-01012`
- payment intent: `3c051c9a-f51f-423f-93c3-62b81ce15ab3`

`ORD-83B3-2026-01012` is distinct from `ORD-83B3-2026-01010` by order id, order number, cart, checkout session, proof, payment intent, and creation time.

Storefront persistence review confirmed `orderResult` is not included in the persisted Zustand partial state. Submission also clears any in-memory stale `orderResult` before validation.

## 2. C5.6 Review

`requireStorefrontPaymentSessionAuthority()` remains enabled and is still called before the response enters Storefront order state.

The C5.6 contract remains:

- backend `paymentMethodCode` must equal the selected method
- cart id must equal the submitted cart
- fresh Manual Wallet requires `paymentIntentId`, `isManualWallet`, pending/awaiting status, and wallet type
- cross-provider artifacts are rejected
- terminal Manual Wallet replay is allowed only when backend method identity is Manual Wallet and the reusable payment intent id is absent

C5.7 tightened terminal replay rather than relaxing it. Terminal Manual Wallet responses now also reject reusable `checkoutToken`, `expiresAtUtc`, `redirectUrl`, and `paymentReference` artifacts.

## 3. Checkout Response

Final fresh response:

```json
{
  "httpStatus": 200,
  "requestId": "c6487f30-f04a-4856-be63-27fe59af64a0",
  "orderId": "87054a3e-d4a3-4d05-9e8c-5bd0120b177d",
  "orderNumber": "ORD-83B3-2026-01012",
  "status": "PaymentPending",
  "paymentStatus": "Pending",
  "paymentMethodCode": "MANUALWALLET",
  "paymentIntentId": "3c051c9a-f51f-423f-93c3-62b81ce15ab3",
  "cartId": "0c62a297-f63c-4e0a-8a0c-04a85325b61b",
  "expiresAtUtc": "2026-08-15T22:26:30.1448468Z",
  "isManualWallet": true,
  "walletType": "VODAFONECASH",
  "totalAmount": 410.00,
  "currency": "EGP"
}
```

The response did not expose a separate checkout token/session identity for Manual Wallet. The canonical server identities are checkout session, order, payment intent, cart, and durable idempotency row. The HTTP response supplied `X-Request-Id`; ASP.NET activity trace ids were captured on validation failures, while the successful response did not expose a separate `X-Trace-Id` header.

## 4. DB Order

Direct runtime PostgreSQL evidence for the exact returned order id proved:

- `Orders.Id = 87054a3e-d4a3-4d05-9e8c-5bd0120b177d`
- `OrderNumber = ORD-83B3-2026-01012`
- `TenantId = 83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`
- `CartId = 0c62a297-f63c-4e0a-8a0c-04a85325b61b`
- `PaymentMethodCode = MANUALWALLET`
- guest `SessionId` present
- one Manual Wallet payment intent
- proof object reference present
- future payment expiration present
- one checkout session for the cart
- eleven immutable checkout field snapshots
- one completed HTTP 200 idempotency response bound to the order

After approval, enum values resolved to:

- order status `Confirmed`
- payment status `Collected`
- checkout state `Paid`
- payment intent status `Paid`
- payment method `BankTransfer`

Checkout-session conversion is asynchronous. The direct post-approval probe observed the session row but `ConvertedOrderId` had not yet been projected. This did not affect proof, payment, order, queue, or Admin identity authority.

## 5. Manual Payment Pending

Immediately before approval:

- `GET /api/admin/payments/manual-wallet/pending?page=1&pageSize=100` returned HTTP 200
- total pending count: `1`
- exact order matched once
- exact payment intent matched once
- `hasProofFile = true`
- status authority was `AwaitingVerification`
- expiration was in the future

The canonical queue predicates remained tenant id, `ManualWallet` provider, future expiration, and reviewable status. No tenant or status predicate was widened.

## 6. Admin Orders Visibility

Immediately before approval:

- `GET /api/admin/orders?page=1&pageSize=50&sortBy=createdAt&sortDirection=desc` returned HTTP 200
- exact order matched once
- `OrderId = 87054a3e-d4a3-4d05-9e8c-5bd0120b177d`
- `OrderNumber = ORD-83B3-2026-01012`
- `Status = Pending`
- `PaymentStatus = AwaitingVerification`
- `Source = Ecommerce`
- `TenantId = 83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`

Classification for the original Admin visibility question: no Admin Orders filtering defect was present. The fresh order was visible through the standard SQL tenant-owned storefront-order projection with no source/status/pagination/cache/frontend exclusion.

One Admin detail projection defect was found: Manual Wallet had fallen through checkout payment-method parsing to `Card`. It is now mapped to `BankTransfer`, and the final live Admin detail returned provider `ManualWallet` plus payment method `BankTransfer`.

## 7. Identity Consistency

The final run proved:

```text
checkout response orderId
  = pending payment orderId
  = Admin Orders orderId
  = PaymentIntent.OrderId
  = database Order.Id
  = 87054a3e-d4a3-4d05-9e8c-5bd0120b177d
```

```text
checkout response orderNumber
  = pending payment orderNumber
  = Admin Orders orderNumber
  = database Order.OrderNumber
  = ORD-83B3-2026-01012
```

```text
checkout response paymentIntentId
  = pending payment paymentIntentId
  = Admin detail paymentIntentId
  = database PaymentIntent.Id
  = 3c051c9a-f51f-423f-93c3-62b81ce15ab3
```

## 8. Replay/Idempotency

The exact same checkout request was submitted twice.

- first submission: HTTP 200, order `ORD-83B3-2026-01012`
- retry: HTTP 200, same order id, same order number, same payment intent
- database: one idempotency response and one Manual Wallet intent for the order
- no duplicate order was created

Root replay gap found: the order was durable before the Storefront performed a best-effort cart delete. A browser refresh/crash or failed client delete could leave the old cart available and make a legitimate idempotent response look like a new checkout.

Fix: after durable order creation and completed idempotency persistence, the backend now consumes Manual Wallet carts. Client clearing remains as a fallback. The final runtime run proved:

- submitted cart: `0c62a297-f63c-4e0a-8a0c-04a85325b61b`
- immediate cart read after checkout: new cart `5b716980-6beb-4077-9d76-d89960189aa3`
- item count after checkout: `0`
- retry still returned the original order through durable request replay

A genuinely fresh session/cart can therefore create a new order, while the same request remains idempotent.

## 9. Tenant Security

Verified security properties:

- Storefront cart and checkout session were bound to the authenticated tenant and new guest session.
- Proof upload used the checkout-session-bound `X-Manual-Wallet-Upload-Token`.
- Checkout revalidated proof key against tenant, cart, owner, session, and stored proof authority.
- Pending payment and Admin Orders calls used the same DB-verified Admin tenant token.
- Pending queue, Admin Orders, and proof endpoints retained tenant predicates.
- Proof view resolved the object key from the tenant-owned payment intent/order and returned HTTP 200 image bytes.
- Added pending-queue regression includes an other-tenant payment and proves exclusion.
- Existing proof tests prove payment/order tenant mismatch and wrong tenant-scoped proof key do not touch storage.
- Approval remains restricted to Owner/Admin and tenant-owned Manual Wallet intent.

No Admin access or tenant predicate was widened.

## 10. Root Causes

Root cause A, stale replay presentation:

- first failing boundary: successful Manual Wallet order commit followed by browser-owned best-effort cart clearing
- effect: refresh/crash/clear failure could retain a checked-out cart and replay the old order
- classification: H/J, surviving client cart state plus incomplete server post-order cleanup

Root cause B, Admin detail payment identity:

- first failing boundary: `CheckoutService.ParsePaymentMethod`
- effect: Manual Wallet provider was projected with generic payment method `Card`
- classification: F, projection/canonical enum mapping

Hardening gap C, C5.6 terminal replay:

- terminal early return rejected payment intent id but did not explicitly reject every reusable payment-session field
- normal backend replay already scrubbed those fields, but the Storefront guard did not fully encode the documented contract

No defect was found in pending queue tenant/status predicates or Admin Orders visibility for a fully materialized fresh Manual Wallet checkout.

## 11. Fixes

- Added server-side Manual Wallet cart consumption after durable idempotency completion.
- Preserved Storefront cart clear as a fallback and retained successful checkout semantics if cleanup storage is temporarily unavailable.
- Mapped `MANUALWALLET` to canonical `PaymentMethod.BankTransfer` instead of the parser default `Card`.
- Tightened terminal C5.6 replay checks for checkout token, expiry, redirect, and payment reference artifacts.
- Did not disable or bypass `requireStorefrontPaymentSessionAuthority()`.
- Did not alter Admin tenant authorization or pending queue predicates.

## 12. Regression Tests

Added or extended coverage:

- fresh Manual Wallet pending row with matching order/payment identity
- pending queue tenant isolation
- proof-present projection
- Manual Wallet order visible in Admin Orders SQL projection
- Admin order number/payment status identity
- canonical Manual Wallet `BankTransfer` mapping
- Storefront Manual Wallet cart clear
- terminal replay rejection for reusable artifacts
- architecture assertion for server-side Manual Wallet cart consumption

Existing focused coverage also validates:

- duplicate checkout idempotency
- checkout proof tenant/cart/owner/session binding
- converted-session/order proof binding
- proof storage tenant isolation
- approval concurrency
- C5.3 post-order identity
- C5.5 proof and lifecycle behavior
- C5.6 authority guard

## 13. Live Verification

An automated live HTTP/browser-equivalent run was performed against the restarted local API and Storefront source using a completely new guest session and cart.

Verified in sequence:

1. new cart with one product
2. new checkout session
3. valid PNG proof upload, HTTP 200
4. checkout, HTTP 200
5. new order id and order number
6. pending Manual Wallet row with proof
7. Admin Orders row with same identity
8. protected proof view, HTTP 200, `image/png`, 2,337,620 bytes
9. exact request retry returned the same order/payment intent
10. original cart was consumed server-side and replaced by an empty cart
11. Admin approval returned HTTP 200
12. pending row disappeared
13. Admin order became `Confirmed`
14. payment became `Collected`
15. payment intent became `Paid`
16. checkout state became `Paid`

An interactive human browser connector was not available. The Storefront dev server itself was restarted and returned HTTP 200; the lifecycle was exercised through the same public HTTP contracts with real runtime PostgreSQL and development object storage.

## 14. Remaining Limitations

- Interactive visual browser evidence was unavailable; HTTP/browser-equivalent runtime evidence was captured instead.
- Aggregate `/health` returned 503 because optional external dependencies were degraded, while all C5.7 endpoints were available and succeeded.
- Checkout session conversion is asynchronous and had not populated `ConvertedOrderId` at the final immediate DB probe.
- A broad 266-test backend filter completed with 261 passes and five unrelated existing failures: two Fawry checkpoint-service test fixture failures and three missing architecture certification markers in `.ai-context/ARCHITECTURE_TRUTH.md`. These were not changed because they are outside Manual Wallet fresh-order/Admin visibility authority.
- Existing compiler/analyzer and frontend chunk warnings remain.

## 15. Build/Test Evidence

Passed:

- C5.7 focused backend/Admin tests: 12/12
- Manual Wallet checkout forensic tests: 7/7
- final certified backend Manual Wallet/checkout/Admin/idempotency suite: 36/36
- Storefront focused tests: 40/40
- Storefront authority/cart tests after edits: 26/26
- Admin Manual Payments/Orders tests: 12/12
- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore`: passed, 0 errors
- `dotnet build PointOfSale.sln --no-restore`: passed, 0 errors
- Storefront `npm run build`: passed
- Admin `npm run build`: passed
- `dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure --startup-project PointOfSale.API --no-build`: no pending model changes
- `git diff --check`: exit code 0, line-ending warnings only

## 16. Final Certification

`PASS WITH EXTERNAL VERIFICATION BLOCKED`

C5.7 core criteria pass:

- a new Manual Wallet order was created
- the old approved order was not reused
- the new pending payment appeared
- Admin Orders showed the same order
- proof was viewable through the protected endpoint
- approval worked
- payment became `Collected`
- order became `Confirmed`
- pending row disappeared because payment became terminal
- duplicate retry produced one order and one payment
- cart was consumed after successful checkout
- identity remained consistent across response, queue, Admin, intent, and DB
- tenant/proof/Admin authority was not widened
- C5.6 authority was strengthened, not disabled

External limitation: no interactive browser connector was available. Repository-wide broad test certification also remains limited by five unrelated pre-existing failures listed above; the C5.7 focused suites and all requested builds/EF/diff gates pass.
