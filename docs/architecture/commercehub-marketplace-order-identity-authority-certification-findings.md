# PASS M2-A Marketplace Order Identity Authority Certification Findings

Scope: Frontend runtime Marketplace Order Identity Authority across Customer Orders, Order Recovery, POS/Admin vendor orders, Super Admin Orders, order cards, lists, details, search/filter navigation, URLs, references, and split-order presentation.

Status: Production-certifiable for inspected runtime paths after frontend order identity boundary hardening.

## Identity Findings

Backend SQL order authority owns order identity, order number, public order number, vendor order number, marketplace order identity, and split-order identity. The frontend now validates customer, recovery, and Super Admin DTO identity evidence before presentation and rejects missing, malformed, duplicate, or route-mismatched order identities.

## Ownership Findings

Customer ownership remains backend customer/session/order authority. Vendor ownership remains backend vendor/tenant/order authority. Super Admin order visibility remains backend Super Admin/order authority. Frontend route IDs, selected rows, caches, and storage can only request current backend reads; they cannot prove ownership.

## List Findings

Customer and Super Admin lists require accepted backend order rows before rendering cards/tables. Duplicate order IDs and malformed split rows fail closed. Super Admin list placeholder data was removed and list rows are withheld during current refetch or failed refresh so stale cached rows cannot restore identity.

## Detail Findings

Customer detail, recovery detail, and Super Admin detail responses must match the current route/selected order target before presentation. A route parameter is a lookup candidate only, not identity authority. Stale selected Super Admin detail state is cleared when the current accepted list no longer contains the selected order.

## Split Findings

Split-order presentation requires backend sub-order IDs, order numbers, vendor/store evidence, canonical lifecycle/payment status, and non-duplicate split IDs. The frontend does not infer split relationships from vendor names, item grouping, route state, or cached rows.

## Bootstrap Findings

Initial load, focus, reconnect, and visibility refetches re-enter backend order authority. Customer/recovery details and Super Admin details withhold cached presentation during current refetch. Session-storage payment recovery context is candidate navigation only and must revalidate through backend order/recovery authority.

## Cache Findings

React Query caches remain derivative. Super Admin cached list/detail rows are withheld during refresh and after current list refresh failure. Selected order React state is fenced to current accepted rows. Customer and recovery detail query keys remain route-target scoped and validated against backend-returned identity.

## Failure Findings

Malformed DTOs, duplicate IDs, unsupported statuses, missing split-order evidence, route/detail mismatches, 401/403/404 authority loss, and failed current refresh fail closed to error/loading presentation instead of preserving stale order identity as current truth.

## Adversarial Findings

Frontend storage, React state, contexts, caches, mutation responses, route state, history, cross-tab events, URL parameters, list search/filter parameters, stale detail responses, and malformed split-order DTOs cannot fabricate, preserve, replay, merge, infer, repair, widen, override, restore, or replace Marketplace Order Identity Authority on inspected paths.

## Runtime Hardening Performed

- Added CommerceHub customer/recovery `orderIdentityAuthority` validation.
- Added Super Admin `orderIdentityAuthority` validation.
- Routed customer order list/detail and order recovery reads through route-target and split-order validation.
- Routed Super Admin order list/detail reads through order/split identity validation.
- Removed Super Admin order placeholder list presentation.
- Withheld Super Admin list/detail presentation during current refetch and failed current refresh.
- Cleared Super Admin selected order state when current accepted list authority no longer contains the selected order.

## Exact Runtime Files Modified

- `PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts`
- `PointOfSale.CommerceHub/src/services/customer-orders-service.ts`
- `PointOfSale.CommerceHub/src/services/order-recovery-service.ts`
- `PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx`
- `PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx`
- `PointOfSale.Web/src/super-admin/orders/orderIdentityAuthority.ts`
- `PointOfSale.Web/src/super-admin/orders/ordersService.ts`
- `PointOfSale.Web/src/super-admin/orders/OrdersPage.tsx`

## Regression Freezes Added

- `PointOfSale.Web/src/super-admin/orders/orderIdentityAuthority.test.ts`

## Architecture Updates

Updated all five `.ai-context` authority files with M2-A identity truth, hardening, risk, and freeze rules.

## Required Answers

- Who owns order identity authority? Backend SQL order, split-order, customer, vendor, tenant, lifecycle, payment, settlement, and governance authorities own it.
- Can frontend fabricate order identity? No on inspected paths; frontend identity is derivative and route/storage/state/caches are lookup candidates only.
- Can list identity contradict detail identity? No on inspected hardened paths; detail must match the selected route/order target and lists reject malformed/duplicate identity evidence.
- Can route parameters create identity? No; they only select a backend lookup target and response identity must match.
- Can split order identity be fabricated? No; split presentation requires backend sub-order identity and vendor/store evidence.
- Can stale identity survive refresh? No on hardened customer/recovery/Super Admin detail paths and Super Admin list paths; stale presentation is withheld during current refresh.
- Can cache restore identity? No on inspected hardened paths; caches are derivative and fail closed during refresh/failure where identity would otherwise be preserved.
- Can tenant leakage occur? No inspected path allows frontend tenant/order state to bypass backend ownership and tenant authority.
- Were runtime changes required? Yes.
- Is Marketplace Order Identity Authority production-certifiable? Yes, for the inspected frontend runtime authority paths.

## Verification

- `npm test -- --run src/super-admin/orders/orderIdentityAuthority.test.ts` passed: 4 tests.
- `npm run build` passed in `PointOfSale.CommerceHub`.
- `npm run build` passed in `PointOfSale.Web`.
