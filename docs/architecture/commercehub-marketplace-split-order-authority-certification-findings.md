# PASS M2-D Marketplace Split Order Authority Certification Findings

Scope: Frontend runtime Marketplace Split Order Authority across Checkout, Order Creation, Order Details, Customer Orders, Customer Dashboard, Vendor Orders, Vendor Dashboards, Super Admin Orders, Order Recovery, Payment Recovery, Financial Views, Settlement Views, Notifications, bootstrap, refresh, cache, route/history, failure, and adversarial replay behavior.

Status: Marketplace Split Order Authority is production-certifiable for inspected frontend runtime paths after split composition and item-binding hardening.

## Split findings

Backend marketplace order, split-order, vendor-order/sub-order, vendor, customer, tenant, lifecycle, payment, settlement, inventory, and governance authorities own split truth. Frontend split presentation is derivative: it accepts only current backend sub-order identity, order numbers, vendor/store evidence, canonical lifecycle/payment statuses, finite backend financial values, and backend item evidence.

## Composition findings

Frontend cannot compose vendor groups as authority. Checkout, customer order, recovery, and Super Admin split presentation now reject empty, duplicate, malformed, unsupported, contradiction-shaped, or unbound split evidence. Super Admin detail item presentation derives item composition from backend-issued `subOrderId` relationships, not vendor-name grouping or frontend buckets.

## Ownership findings

Split ownership remains backend-owned through current customer, vendor, tenant, marketplace order, and split-order authority. Customer surfaces require customer order authority, POS/Admin vendor surfaces require current vendor/tenant authority, and Super Admin visibility does not become ownership.

## Lifecycle findings

Split lifecycle presentation uses backend canonical status evidence only. The frontend may badge and format parent/sub-order statuses but cannot repair, infer, advance, regress, or reconcile child lifecycle against parent lifecycle from cache, route state, mutation responses, or local state.

## Financial findings

Split financial presentation formats backend subtotal, shipping, commission, vendor earning, settlement, and total evidence only. Frontend code does not recompute split totals, commissions, settlement eligibility, vendor shares, taxes, or parent/child reconciliation authority.

## Presentation findings

Customer order list/detail/dashboard, checkout success, payment recovery, POS/Admin vendor orders, and Super Admin order list/detail render backend split rows only after authority boundaries accept them. Presentation no longer reconstructs Super Admin item groups by vendor name.

## Bootstrap findings

Initial load, refresh, focus, reconnect, visibility, and recovery links re-enter backend order/split authority. Customer list/dashboard, detail/recovery, POS vendor, and Super Admin list/detail paths withhold stale presentation during current refresh or authority failure where cached splits could otherwise survive.

## Cache findings

React Query caches remain derivative. Query keys are scoped by customer route/order, POS tenant/user, or Super Admin filters/selection. Cached rows are not rendered as current split truth during refetch/failure, and selected order details are fenced to current accepted backend authority.

## Failure findings

401, 403, 404, malformed DTOs, route/detail mismatches, duplicate order IDs, duplicate split IDs, empty split arrays, unsupported statuses, vendor-count contradictions, unbound split item rows, failed refreshes, and authority loss fail closed to loading/error/empty presentation rather than preserving stale split authority.

## Adversarial findings

Local storage, session storage, React state, contexts, caches, mutation responses, route state, browser history, cross-tab events, URL parameters, search parameters, actor replay, tenant replay, split replay, route replay, recovery replay, selected-row replay, vendor-name grouping, item regrouping, malformed DTOs, and cached rows cannot fabricate, preserve, replay, infer, merge, regroup, widen, repair, override, reconstruct, restore, or replace Marketplace Split Order Authority on inspected paths.

## Runtime hardening performed

- Customer order/recovery authority rejects empty split arrays and malformed or duplicate split item evidence.
- Checkout creation authority rejects duplicate item evidence within backend-created split rows.
- Super Admin order authority rejects empty split evidence, vendor-count/sub-order contradictions, malformed split financial evidence, and detail items whose `subOrderId` is not present in current backend sub-orders.
- Super Admin order detail presentation derives item composition from backend `subOrderId` relationships instead of frontend vendor-name grouping.
- Super Admin refresh-failure presentation states cached order rows are withheld until current backend authority is accepted.

## Exact runtime files modified

- `PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts`
- `PointOfSale.CommerceHub/src/features/checkout/orderCreationAuthority.ts`
- `PointOfSale.CommerceHub/src/pages/CustomerOrdersPage.tsx`
- `PointOfSale.CommerceHub/src/pages/CustomerDashboardPage.tsx`
- `PointOfSale.Web/src/super-admin/orders/orderIdentityAuthority.ts`
- `PointOfSale.Web/src/super-admin/orders/OrderDetailsDrawer.tsx`
- `PointOfSale.Web/src/super-admin/orders/OrdersPage.tsx`

## Regression freezes added

- `PointOfSale.Tests/Architecture/MarketplaceSplitOrderAuthorityCertificationTests.cs`
- `PointOfSale.Web/src/super-admin/orders/orderIdentityAuthority.test.ts`

## Architecture updates

Updated all five `.ai-context` authority files with M2-D split-order truth, hardening, risk, and freeze rules.

## Explicit answers

- Who owns split-order authority? Backend marketplace order, split-order, vendor-order/sub-order, vendor, customer, tenant, lifecycle, payment, settlement, inventory, and governance authorities own it.
- Can frontend fabricate split orders? No on inspected paths.
- Can frontend compose vendor groups? No as authority; presentation derives from backend sub-order rows and `subOrderId` item relationships only.
- Can split ownership be fabricated? No; customer/vendor/tenant ownership must come from current backend authority.
- Can child lifecycle contradict parent lifecycle? The frontend cannot authorize or repair contradiction; malformed/unsupported lifecycle evidence fails closed at presentation boundaries.
- Can split financials be fabricated? No; frontend formats backend financial values only and does not recompute split authority.
- Can stale split authority survive refresh? No on inspected hardened paths; stale rows are withheld during current refresh/failure.
- Can cache restore split authority? No; caches are derivative and selected/detail state is fenced to current accepted backend authority.
- Can tenant leakage occur? No inspected path allows tenant or vendor split scope to be widened by frontend state, route, cache, grouping, or search parameters.
- Were runtime changes required? Yes.
- Is Marketplace Split Order Authority production-certifiable? Yes, for inspected frontend runtime authority paths.

## Verification

- `npm test -- --run src/super-admin/orders/orderIdentityAuthority.test.ts` passed in `PointOfSale.Web`: 5 tests.
- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --filter MarketplaceSplitOrderAuthorityCertificationTests` passed: 3 tests.
- `npm run build` passed in `PointOfSale.CommerceHub`.
- `npm run build` passed in `PointOfSale.Web`.
