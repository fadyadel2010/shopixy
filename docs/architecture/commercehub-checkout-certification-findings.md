# CommerceHub Checkout Certification Findings

Scope: PASS 16.4 Checkout Certification

## Checkout Inventory

- Customer checkout entrypoint: `POST /api/commercehub/checkout`, authenticated through the CommerceHub customer scheme.
- Pricing preview and checkout handoff: CommerceHub cart pricing revalidates SQL listing, product, variant, stock, price, shipping, promo, and payment-method authority before frontend submission.
- Checkout form: customer name, phone, address line, city, area, notes, payment method, promo state, and cart-derived store/item payload.
- Shipping selection: backend chooses and revalidates active store shipping options, bypassing the short-lived cache at final checkout.
- Payment selection: COD, Paymob, and Stripe are allowed only when every store in the order exposes the selected active method.
- Order placement: one marketplace order is split into store sub-orders with immutable item, shipping, payment, commission, promo, and financial snapshots.
- Payment preparation: online orders return a backend payment session marker and then use `api/commercehub/payments/initiate` to create or resume provider handoff.
- Recovery: payment-pending frontend recovery context, order access cookie, payment recovery page, SQL reservation authority checks, unresolved-provider reconciliation, and idempotent checkout replay.

## Authority Findings

Checkout cannot rely on frontend cart state as authority. The inspected path requires a customer JWT, request validation, idempotency key, customer governance approval, per-customer checkout lock, idempotency lock, persisted request-hash-bound idempotency record, saved cart lock/version match, and final SQL recomputation.

Listing, product, variant, tenant, payment method, shipping, promo, vendor governance, financial readiness, price, and inventory authority are reloaded inside the checkout service before order creation. Deleted listings, invalid variants, offline/draft products, inactive tenants, suspended vendors, unavailable payment methods, stale cart versions, stale prices, stale shipping, and invalid stock fail closed.

## Pricing Findings

Checkout item price comes from `ICommerceHubPriceAuthority`, not the client price snapshot. The client expected unit price is treated as stale-client protection only and must match the rounded SQL-authoritative price. PASS 13.7 merchant-owned override policy remains the price resolver boundary.

Shipping is recomputed server-side with `bypassCache: true`; the requested total and per-store selection price must match the recalculated result. Promo validation and consumption run before commit and abort checkout if final consumption fails. Final order totals are built from persisted sub-order snapshots.

## Inventory Reservation Findings

Reservation authority is backend-only through `IInventoryConsistencyService.ReserveOrderItemAsync`. Reservations are created per order item before the marketplace order is committed, carry order/sub-order/item identity, have a 30 minute TTL, and abort checkout if any line cannot reserve the requested quantity.

Payment recovery blocks retry when active SQL reservation authority is lost, forcing a fresh checkout instead of letting a stale pending order continue to payment.

## Payment Findings

Payment method availability is recomputed from tenant SQL payment-method authority across every store. Emergency and financial readiness gates run before order creation. COD orders are created with COD payment state; online orders create an order with pending payment state and return a payment session requiring provider initiation through the payment service. Provider callbacks and recovery remain lifecycle/reconciliation-owned, not checkout-owned.

## Address And Shipping Findings

The current CommerceHub checkout uses an inline customer address snapshot, not a saved address book. Because there is no client-provided address id, cross-customer saved-address access was not found on this path. Address fields are validated and persisted as order snapshots.

Shipping authority is store-scoped SQL shipping methods plus backend calculation. Missing, inactive, ineligible, changed, or repriced shipping selections fail closed with explicit refresh errors.

## Order Creation Findings

Order creation is inside a serializable SQL transaction. The service creates a marketplace order, store sub-orders, order items, shipping snapshots, payment snapshots, commission snapshots, promo snapshots, order access token, idempotency response JSON, inventory reservations, and a marketplace-order-created event before commit.

Partial order creation is contained by transaction rollback. Duplicate creation is contained by customer/idempotency locks, SQL idempotency record, request hash, and order idempotency index.

## Idempotency Findings

Checkout requires an idempotency key. The key is normalized, locked per customer, persisted with a deterministic request hash, and replayed only when the payload hash matches. Legacy or mismatched idempotency records fail closed. The frontend rotates the idempotency key when the checkout fingerprint changes and freezes it while a submission is in flight.

## Concurrency Findings

Multi-tab and multi-device checkout are guarded by a customer checkout lock, idempotency lock, saved-cart row lock, cart version check, serializable transaction, request-hash-bound idempotency, and inventory reservation authority. Concurrent different checkout attempts for the same customer are rejected as checkout-in-progress instead of last-write-wins.

## Recovery Findings

If checkout fails before commit, cleanup removes the pending idempotency claim and unlocks the saved cart. If order creation succeeds but payment initiation is interrupted, the order remains authoritative and the frontend stores a payment recovery context. Payment retry is blocked if reservation authority has expired or external payment truth is ambiguous.

## Frontend UX Findings

The checkout page refreshes backend cart state before submission, re-prices immediately before order placement, handles stale payment methods, displays cart/pricing/shipping/payment errors, blocks unauthenticated checkout, disables submission while pricing is refreshing, prevents double-click submission with an in-flight guard, and presents payment-pending recovery actions after online order creation.

## Financial Findings

Checkout totals are trustworthy on the inspected path because item price, shipping, promo discount, commission, vendor earning, payment method, and total amount are recomputed and snapshotted server-side. Frontend totals are display and stale-client evidence only.

## Auditability Findings

Core incident reconstruction is possible from SQL marketplace orders, sub-orders, sub-order items, payment rows, reservation rows, financial snapshots, order-created events, checkout idempotency rows, logs, and payment/recovery evidence. Full launch-grade audit posture still inherits PASS 15.6 and PASS 16.1B deployment evidence, alert, and external retention blockers.

## Chaos Findings

- Price change during checkout: blocked by expected unit price comparison against `ICommerceHubPriceAuthority`.
- Inventory change during checkout: blocked by final reservation failure.
- Suppressed/deleted/offline listing or product: blocked by SQL authority reload.
- Deleted/invalid variant: blocked by variant authority reload.
- Shipping change: blocked by cache-bypassed recalculation and per-store selection comparison.
- Payment method disabled: blocked by payment-method authority.
- Double click/network retry: same idempotency key replays or waits; different same-customer attempts hit checkout-in-progress.
- Multi-tab/multi-device cart drift: blocked by saved-cart version and lock.
- Payment interruption: order remains pending and recovery resumes payment only while SQL reservation/external truth allows it.

## Monitoring Findings

Checkout has structured logs for blocked/unexpected checkout failures, idempotency replay, successful order creation, shipping refreshes, promo consumption failure, and payment recovery decisions. Reservation telemetry exists for reservation commit/release/stale health, and broader payment, finance, projection, and readiness health checks support incident detection.

Remaining monitoring debt: a dedicated CommerceHub checkout conversion/failure dashboard, alert pack, and PostgreSQL-backed chaos drill for checkout races are not yet present.

## Runtime Hardening Performed

No runtime service code hardening was required in PASS 16.4. The inspected runtime already contained the required checkout authority, pricing, reservation, idempotency, concurrency, recovery, frontend alignment, and financial controls from prior passes.

Test alignment fixes were applied in `CheckoutConcurrencyHardeningSourceTests.CheckoutCon08_OutOfOrderVisibilityPaths_MustRemainDerivativeAndFreshnessFenced`:

- `StorefrontConfigProjectionConsumer` assertion updated from direct `FirstOrDefaultAsync` lookup to `TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync` to match refactored SQL authority loading.
- `AffiliateProductPolicyProjectionConsumer` assertion updated from `ResolveTenantCurrencyAsync` to `NormalizeCurrency(` to match the refactored tenant currency resolution path through `LoadActiveAffiliateTenantProfileAsync`.

## Regression Freeze Added

Added:

- `CommerceHubCheckoutCertificationTests`
- `CommerceHubCheckoutFinancialCertificationTests`
- `CommerceHubCheckoutReservationCertificationTests`

Frozen:

- checkout authority
- pricing authority
- inventory authority
- reservation authority
- idempotency
- order creation
- financial totals
- concurrency handling
- recovery behavior
- frontend/backend alignment

## Architecture Updates

The PASS 16.4 certification is registered in project context, architecture truth, known risks, cross-system hardening notes, and Codex rules so future checkout work must preserve these controls and update the same certification artifacts.

## Remaining Debt

- No dedicated checkout-specific metrics/alert dashboard.
- No PostgreSQL-backed checkout chaos harness for double-submit, stale price, stale shipping, inventory exhaustion, reservation expiry, and provider interruption.
- Inline address snapshots are certified, but saved address-book ownership is not implemented or certified.
- Public launch remains blocked by broader PASS 15.6 and PASS 16.1B deployment, evidence, SMTP, monitoring, alerting, and rehearsal prerequisites.

## Required Answers

- Can customers reliably place orders? Yes, on the inspected authenticated CommerceHub checkout path when broader platform dependencies are healthy.
- Can checkout bypass pricing authority? No. Checkout uses `ICommerceHubPriceAuthority` and rejects stale expected prices.
- Can checkout bypass inventory authority? No. Checkout reserves through backend SQL inventory authority before commit.
- Can duplicate orders occur? Not on the certified path with the same idempotency key or concurrent same-customer checkout; persisted idempotency, locks, request hashes, and SQL indexes contain duplicates.
- Can overselling occur? Not through the certified checkout path when the reservation service and SQL transaction boundary are available.
- Are checkout totals trustworthy? Yes, item, shipping, promo, commission, and final totals are backend recomputed and snapshotted.
- Are checkout incidents auditable? Partially launch-grade: SQL order/reservation/payment/idempotency evidence exists, but dedicated checkout dashboards and broader external retention blockers remain debt.
- Are critical checkout risks present? No active runtime corruption risk was validated in PASS 16.4; operational monitoring/chaos maturity debt remains.
- Is checkout production-ready? Runtime checkout is production-certifiable, subject to platform launch blockers.
- Is CommerceHub checkout launch-ready? Not for public launch until broader PASS 15.6 and PASS 16.1B deployment, monitoring, evidence, and rehearsal blockers are closed.
