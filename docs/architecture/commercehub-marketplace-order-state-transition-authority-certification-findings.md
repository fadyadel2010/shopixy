# PASS M2-H Marketplace Order State Transition Authority Certification Findings

## Certification result

Marketplace Order State Transition Authority is production-certifiable for the inspected frontend runtime. Current backend Order, Split Order, Lifecycle, Transition, Fulfillment, Payment, Settlement, Refund, Return, and Governance authorities own transition truth, eligibility, and progression. The frontend is a derivative presenter and may submit only backend-bound intent.

## Transition findings

- Customer order/detail, recovery, checkout payment handoff, vendor shipment/fulfillment, Super Admin order monitoring, split-order, payment/refund/return/settlement, tracking, notifications, bootstrap, refresh, query cache, route/history, and failure paths were reviewed.
- Lifecycle-shaped DTOs must pass current backend identity, split, lifecycle, and action-evidence boundaries before rendering. Status labels, transition menus, timeline entries, notifications, and tracking data do not grant an action.
- No inspected frontend path derives a legal next state, repairs an invalid state, advances a parent or child order, or treats a mutation response as new lifecycle authority.

## Customer findings

- Customer cancellation has no frontend transition executor in the inspected CommerceHub path.
- Retry and refund entry remain backend-owned: customer action evidence is validated against current order/payment/item evidence, and a retry now refetches the exact order/recovery authority immediately before requesting provider handoff.
- URL recovery tokens, session recovery context, route/history state, and prior query data can locate a record only; they cannot preserve retry eligibility after backend withdrawal.

## Vendor findings

- POS/Admin shipment cancellation is presented only when `/orders/{orderId}/fulfillment/actions` grants `cancel` for the exact shipment order.
- Cancellation refetches that backend action document immediately before execution. Missing, malformed, foreign-target, stale, or revoked action evidence withholds execution.
- The legacy generic client-selected shipment status endpoint is fail-closed; it cannot be used to skip, jump, or replay a fulfillment transition.

## Admin, split, payment/refund, tracking, bootstrap, cache, and failure findings

- Super Admin Orders is monitoring/detail presentation in the inspected path; it has no transition executor, so selection and visibility cannot become administrative override authority.
- Parent/child split lifecycle consistency is validated before presentation. Neither child state nor parent state can be reconciled or repaired by the frontend.
- Payment recovery, refunds, returns, settlement, tracking, notifications, banners, and emails are derivative evidence. None creates an order transition.
- Initial load, focus, visibility, reconnect, refresh, and navigation re-enter backend query authority. During refetch/error, action presentation is withheld rather than restored from cached state.
- 401, 403, 404, malformed DTOs, route/actor/tenant replacement, query replay, mutation replay, and history replay fail closed. Backend authorization remains the final execution gate.

## Runtime hardening performed

- Added execution-time backend revalidation to customer order-detail and recovery payment retry before payment initiation.
- Required failed revalidation to block use of any stale cached retry payload.
- Added execution-time refetch of shipment fulfillment action authority before cancellation.
- Disabled the generic frontend-selected shipment status mutation path.

## Exact runtime files modified

- `PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx`
- `PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx`
- `PointOfSale.Web/src/modules/shipping/hooks/useShipmentsApi.js`
- `PointOfSale.Web/src/modules/shipping/components/ShipmentActions.jsx`
- `PointOfSale.Web/src/modules/shipping/pages/ShipmentDetailsPage.jsx`

## Regression freezes

- Added `PointOfSale.Web/src/auth/marketplaceOrderTransitionAuthority.test.ts`.
- Retained and ran `PointOfSale.Web/src/modules/shipping/shipmentActionAuthority.test.ts`.

## Explicit answers

- Who owns order state transition authority? Current backend Order, Split Order, Lifecycle, Transition, Fulfillment, Payment, Settlement, Refund, Return, and Governance authorities.
- Can frontend fabricate transitions? No.
- Can customer bypass lifecycle rules? No.
- Can vendor skip lifecycle states? No.
- Can admin monitoring create transitions? No.
- Can split transitions be fabricated? No.
- Can payment/refund state create transitions? No.
- Can tracking/notifications create transitions? No.
- Can stale transitions survive refresh? No; execution revalidates current backend authority and unavailable evidence fails closed.
- Can cache restore transitions? No.
- Can tenant leakage occur? No inspected frontend path bypasses backend customer/vendor/tenant authorization; foreign target evidence is rejected.
- Were runtime changes required? Yes.
- Is Marketplace Order State Transition Authority production-certifiable? Yes, for the inspected frontend runtime paths.

## Verification

- `npm run build` passed in `PointOfSale.CommerceHub`.
- `npm test -- --run src/auth/marketplaceOrderTransitionAuthority.test.ts src/modules/shipping/shipmentActionAuthority.test.ts` passed in `PointOfSale.Web` (6 tests).
- `npm run build` passed in `PointOfSale.Web`.
