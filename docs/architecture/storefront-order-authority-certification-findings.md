# PASS P2-A Storefront Order Authority Certification Findings

Date: 2026-06-08

Verdict: **Production-certifiable after the runtime hardening in this pass and deployment of
`20260608150000_StorefrontOrderAuthorityHardening`.**

## Authority Boundary

Tenant-owned PostgreSQL `Orders`, `OrderItems`, checkout idempotency records, payment lifecycle
rows, refund rows, inventory reservations, stock movements, accounting outbox rows, and current SQL
state are authoritative.

Storefront Orders own order identity and lifecycle coordination. They do not own inventory,
treasury, customer balance, POS sales invoice, or financial reporting truth. Those remain owned by
PASS P1-EB, P1-EE, P1-EF, P1-ED, and P1-EG respectively. MongoDB order documents, Redis, search,
frontend state, route values, DTO totals, callback payloads, and dashboard caches are evidence or
projections only.

## Findings

### 1. Order Creation

- Guest and authenticated checkout use durable checkout idempotency, cart execution locking,
  serializable final SQL execution, server-side price/discount/shipping/tax recomposition, and
  authoritative inventory reservation.
- The active-cart SQL unique index prevents concurrent duplicate active orders for the same tenant
  and cart.
- Admin/import creation now requires `Owner`, `Admin`, or `Manager`, validates current same-tenant
  customer authority, and fails closed when the selected/default ProductUnit is absent or has an
  invalid conversion factor.
- Client line prices, totals, discounts, tax, shipping cost, and unit conversion do not become
  authority.
- Imported external identity is now unique by active `(TenantId, Source, SourceOrderId)`.

### 2. Order Ownership

- POS/admin reads and mutations are tenant-filtered through required tenant context and EF query
  filters.
- Storefront customer reads prove SQL `OnlineCustomerId` ownership before serving or repairing a
  projection.
- Guest tracking requires the signed tracking token and matching guest session authority.
- Customer ownership cannot be reassigned through generic order status or payment patch routes.

### 3. State Machine

- Legacy direct order-status and payment-status patches remain retired with `410 Gone`.
- Fulfillment transitions use the explicit fulfillment state machine, row locking, replay collapse,
  payment/accounting gates, and now atomically advance the SQL order lifecycle.
- Outbound fulfillment is blocked when the order lifecycle is `Cancelled`, `Refunded`, or
  `Completed`.
- Fulfillment progression cannot regress order state. Cancelled or refunded orders cannot
  resurrect into outbound fulfillment.

### 4. Inventory Integration

- Checkout and privileged order creation validate and reserve through certified inventory
  authority.
- Payment, cancellation, expiry, and order lifecycle services own reservation conversion or
  release. Fulfillment no longer accepts client-selected inventory release behavior.
- Cancellation through the fulfillment compatibility route fails closed and directs callers to
  the authoritative admin cancellation service, which coordinates payment cancellation and
  reservation restoration.

### 5. Payment Integration

- Online payment state advances only through verified provider/payment lifecycle paths.
- COD uses its explicit lifecycle and cannot be treated as collected online payment.
- Direct payment-status patching remains disabled.
- Refund execution locks SQL order authority, verifies payment/refund state, bounds cumulative
  refund amount to collected money, and is unique by tenant/order/refund identity.
- Fulfillment return requests cannot claim or synthesize refund authority.

### 6. Sales Integration

- Storefront orders do not create or impersonate POS `SalesInvoice` authority.
- Storefront collection/accounting and stock effects are coordinated through the certified payment,
  accounting, and inventory boundaries.
- Replay identities prevent duplicate order collection/accounting intent. POS sales reporting
  remains ledger-owned under PASS P1-ED/P1-EG rather than derived from storefront projections.

### 7. Fulfillment

- Fulfillment mutation routes now require `Owner`, `Admin`, or `Manager`.
- Locked fulfillment loads retain tenant query filters; a guessed foreign order ID cannot be
  mutated.
- Manual fulfillment requires recognized payment/accounting authority before outbound progression.
- Fulfillment and SQL order lifecycle are committed together, and shipment events cannot directly
  replace order lifecycle authority.
- Replayed or stale outbound fulfillment transitions converge without applying the action twice.

### 8. Cancellation

- Cancellation authority is the admin order lifecycle service, not the fulfillment DTO.
- Recognized money blocks unsafe direct cancellation; pending payment cancellation is outbox-backed.
- Inventory reservations are restored only after payment cancellation is safe.
- Shipped/delivered state is not cancelable through the retired fulfillment path.

### 9. Refund

- Refund authority remains in `PaymentLifecycleService`.
- Refunds require recognized collected money, are cumulatively bounded, row-locked, tenant-scoped,
  replay-safe, and accounting-outbox backed.
- Physical return state cannot forge refund success. Refunded orders cannot resume fulfillment.

### 10. Tenant Isolation

- Order reads, searches, mutations, fulfillment locks, cancellation, payment, refunds, and reports
  retain required tenant authority.
- Storefront projection reads prove ownership against SQL before returning projection data.
- Admin roles are tenant-local and do not grant cross-tenant order authority.

### 11. Concurrency And Idempotency

- Checkout uses durable request idempotency plus serializable SQL execution.
- Active-cart and imported-source identities have SQL uniqueness.
- Payment callbacks, refunds, cancellation messages, and fulfillment transitions have durable
  replay identities or current-state convergence.
- Order and refund mutations lock SQL rows before lifecycle decisions.

### 12. Historical Consistency

- Order lines and totals are persisted from server-recomputed SQL authority.
- Generic completed-order rewrite routes are absent; direct status/payment mutation is retired.
- Fulfillment progression is forward-only at the order boundary.
- Archived/deleted rows are excluded from live unique identities while retained history remains SQL
  evidence.

### 13. Dashboard And Reporting

- Operational order lists and KPIs query tenant-owned SQL order state.
- Financial reports and dashboard money aggregates do not use MongoDB storefront order projections
  as authority; PASS P1-EG ledger and inventory sources remain authoritative.
- Projection repair is SQL-to-Mongo only. A stale projection cannot overwrite SQL order truth.

### 14. Adversarial Review

- Route tampering: privileged mutation roles and required tenant context block untrusted admin
  mutation.
- DTO tampering: totals, unit conversion, payment state, refund amount, inventory release, and
  fulfillment state are recomputed or rejected.
- Replay: durable idempotency, SQL uniqueness, row locks, and transition convergence prevent
  duplicate effects.
- Tenant guessing: SQL ownership proofs and tenant-filtered fulfillment locks prevent foreign
  access.
- State forcing: direct status/payment patches and fulfillment cancellation/refund shortcuts fail
  closed.

## Runtime Hardening Performed

- Privileged fulfillment and admin/import order creation.
- Same-tenant current customer validation for admin/import orders.
- Fail-closed ProductUnit/default-unit authority with no conversion-factor fallback.
- Tenant-filtered row locking for fulfillment mutation.
- Atomic fulfillment-to-order lifecycle convergence and terminal-order resurrection prevention.
- Retirement of fulfillment cancellation as a payment/inventory bypass.
- Rejection of fulfillment-driven refund claims.
- SQL uniqueness for active imported order identity with duplicate-data migration preflight.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/OrderFulfillmentController.cs`
- `PointOfSale.API/Controllers/OrdersController.cs`
- `PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `PointOfSale.Infrastructure/Migrations/20260608150000_StorefrontOrderAuthorityHardening.cs`

## Regression Freeze

- Added `PointOfSale.Tests/Architecture/StorefrontOrderAuthorityCertificationTests.cs`.
- Focused checkout, fulfillment, payment, lineage, concurrency, and P2-A architecture suite:
  118 passed, 0 failed.
- Full API build: 0 errors.

## Explicit Answers

- Who owns storefront order authority? Tenant-owned PostgreSQL order lifecycle state.
- Can duplicate orders be created? Not through certified checkout replay/concurrency or repeated
  imported external identity.
- Can replay attacks mutate orders? Replays converge or return stored results without duplicate
  lifecycle effects.
- Can order totals be client-controlled? No.
- Can inventory diverge from orders? Certified paths coordinate only through P1-EB reservations,
  stock locks, and movements.
- Can payment status be forged? No; direct patches are retired and provider/lifecycle verification
  is required.
- Can fulfillment bypass valid order states? No.
- Can cancelled orders resurrect? No.
- Can refunded orders become fulfilled? No.
- Can tenant leakage occur? Not through the inspected certified APIs and services.
- Can reports diverge from order truth? Operational order reports use SQL; financial reports use
  certified ledger/inventory authority, not storefront projections.
- Can reruns produce inconsistent order truth? Certified reruns converge through durable identity,
  locks, SQL uniqueness, and state checks.
- Were runtime code changes required? Yes.
- Is Storefront Order Authority production-certifiable? Yes, after applying the new migration.

## Deployment Requirement And Residual Risk

Migration `20260608150000_StorefrontOrderAuthorityHardening` must be applied before deployment. It
fails closed if duplicate active `(TenantId, Source, SourceOrderId)` rows already exist; such data
must be reconciled rather than silently deduplicated.

Production chaos evidence, provider callback drills, alerting, and large-volume order reconciliation
remain operational readiness work. These are not unresolved runtime authority gaps.
