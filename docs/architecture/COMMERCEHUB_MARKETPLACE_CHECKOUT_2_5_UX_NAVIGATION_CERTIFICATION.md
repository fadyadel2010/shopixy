# COMMERCEHUB-MARKETPLACE-CHECKOUT-2.5 - UX / Navigation Certification

Date: 2026-07-09
Scope: CommerceHub Marketplace Checkout UX and Navigation only
Status: UX / NAVIGATION CERTIFIED

## Executive Summary

CommerceHub Marketplace Checkout UX was reviewed only as marketplace presentation around the already-certified Reference Checkout Engine experience.

Reviewed scope:

- Marketplace Cart UX.
- Merchant grouping UX.
- Split preview UX.
- Merchant summary UX.
- Marketplace notices.
- Marketplace checkout navigation, resume, pending, recovery, and order tracking/status presentation.

Excluded frozen scope:

- POS Admin Checkout UX.
- Affiliate Checkout UX.
- POS Sales Checkout.
- Checkout runtime redesign.
- Shipping runtime redesign.
- Payment runtime redesign.

One true UX inconsistency was found and fixed narrowly: the checkout summary grouped stores but showed placeholder dashes for store/item monetary lines and did not expose per-store shipping in the split preview. `PointOfSale.CommerceHub/src/checkout/CheckoutSummary.tsx` now renders backend/cart-issued store item totals, store subtotal, store shipping, and store total while preserving the existing checkout authority. Focused source-level certification tests were added.

Certification decision: Marketplace Checkout UX is certified. The Reference Checkout journey, payment UX, shipping UX, review/submit behavior, pending payment behavior, recovery behavior, and order tracking/status presentation remain reused.

## Journey

Certified journey:

```text
Customer
  -> Marketplace Cart
  -> Merchant Grouping
  -> Split Preview
  -> Checkout
  -> Payment
  -> Review
  -> Submit
  -> Pending
  -> Tracking / Order Details
```

Marketplace does not introduce new checkout, payment, shipping, review, pending, or tracking authority. The customer uses the existing `/cart`, `/checkout`, `/orders`, `/orders/:id`, and `/orders/:id/recover` navigation. Tracking/status presentation is visible through backend-issued customer order details, vendor sub-orders, item states, payment context, and timeline.

## Navigation

Marketplace navigation was reviewed across:

- Marketplace Cart: `/cart`.
- Checkout Entry: `/checkout`.
- Checkout Resume: active pending-payment recovery context and `/orders/:id/recover`.
- Pending: checkout success screen with payment-pending state and recovery actions.
- Tracking: `/orders/:id` order details with vendor sub-orders, item states, and timeline.
- Browser Refresh: cart state re-enters cart/pricing services; pending payment context is only a recovery pointer.
- Recovery: recovery URL/token locates an order, then current backend retry authority is refetched.
- Back Navigation: cart and checkout expose explicit back links without replacing checkout authority.
- Autosave / Persistence: cart sync and active recovery context persist candidates/pointers only.

No duplicate Marketplace checkout page, shipping page, payment page, review page, pending page, or tracking authority page was found.

## Merchant Grouping UX

Merchant grouping is certified.

The cart groups items by store using `StoreGroup`. Each group presents:

- Store name.
- Store group icon.
- Item count.
- Store subtotal.
- Collapsible item list.
- Item names, variants, quantities, and line totals.
- Remove-store and item quantity controls disabled while checkout lock is active.

The grouping makes it clear which items belong to which merchant. Merchant logos are not currently rendered in the inspected checkout/cart group component; this is acceptable because store identity is visible through store name, store iconography, item grouping, and order sub-order labels. Future logo/badge enhancement remains presentation-only.

## Split Preview UX

Split preview is certified after the narrow UX fix.

The checkout summary now presents, per store:

- Merchant/store name.
- Items.
- Item totals.
- Store subtotal.
- Store shipping.
- Store total.

The same summary also presents the marketplace-level subtotal, shipping total, payment method label, and total. This satisfies the Marketplace split-preview UX requirement without changing checkout authority: all durable totals are still recomputed by backend cart pricing and final checkout before order creation.

## Shipping UX

Shipping UX is reused and certified.

Marketplace UI does not expose a Marketplace shipping flow or provider-specific carrier picker. Shipping remains backend-owned through CommerceHub cart pricing and checkout. The UI displays per-store shipping evidence and the aggregated marketplace shipping total only after backend pricing evidence is available.

FedEx, or any other provider, appears only if backend shipping/payment/cart evidence produces it as ordinary backend-issued presentation data. No inspected Marketplace Checkout UX code hardcodes FedEx or provider-specific shipping behavior.

## Payment UX

Payment UX is reused and certified.

Payment methods come from the backend cart pricing response. The payment selector filters only supported frontend presentation metadata and then respects backend `isAvailable` and `unavailableReason` evidence. When a method becomes unavailable, checkout re-enters backend pricing authority and falls back only to a backend-available method.

Marketplace does not create a Marketplace payment flow, payment authority, or provider-specific payment branch. Online payment continues through certified payment initiation, pending, retry, and recovery behavior.

## Recovery UX

Recovery UX is certified.

Merchant grouping and split evidence remain candidate/presentation evidence until checkout admission. After order creation, recovery uses the authoritative order and payment recovery path:

- Pending payment stores only order id and order number as a recovery pointer.
- `/orders/:id/recover` fetches current recoverable order evidence.
- Retry payment refetches current backend order authority immediately before requesting provider handoff.
- Customer order details can resume current retryable payment after refetch.

Browser refresh, recovery links, local state, and stale mutation responses cannot create new checkout, payment, shipping, or order authority.

## Responsive UX

Responsive UX is certified.

Desktop uses sticky marketplace/cart and checkout summaries. Tablet/mobile use fixed bottom summaries for primary checkout action visibility. Merchant groups are collapsible, which keeps multi-merchant carts scannable on smaller screens. Loading and slow-network states are presented through refresh messages, disabled actions, skeleton cards, spinner icons, and retry/error states.

The split-preview fix keeps monetary labels short and uses existing responsive card layouts, avoiding new navigation or layout surfaces.

## Accessibility

Accessibility is certified for the inspected UX surface.

Evidence reviewed:

- Native buttons, links, radio inputs, and form inputs are used for keyboard navigation.
- Merchant group collapse controls expose `aria-expanded` and `aria-controls`.
- Payment method choices use radio inputs.
- Disabled states block invalid checkout/payment actions.
- Error and warning notices are rendered in visible text near the affected flow.
- Loading states use visible messages and spinner icons.
- Reduced-motion compatibility is preserved by relying on existing CSS transitions and native controls.
- Color-coded states are paired with text labels and messages.

Residual improvement opportunity: future browser-level accessibility certification should include automated axe and screen-reader smoke evidence. This is operational/browser evidence, not a blocker for the source-level UX certification.

## Regression Coverage

Focused certification coverage added:

- `PointOfSale.Tests/Architecture/CommerceHubMarketplaceCheckoutUxNavigationCertificationTests.cs`

The test freezes:

- Reuse of existing cart, checkout, order details, and recovery routes.
- Absence of duplicate Marketplace checkout/payment/shipping/review/pending pages.
- Store grouping collapse/accessibility markers.
- Split preview display of store items, store subtotal, store shipping, store total, and marketplace total.
- Backend-issued shipping/payment presentation reuse.
- Documentation section freeze.

## Certification Decision

Marketplace Checkout UX and Navigation are certified.

The certification preserves the Reference Checkout Engine experience. Marketplace UX adds merchant grouping, split preview, merchant summaries, and marketplace notices only. Checkout, Shipping, Payment, Review, Submit, Pending, Recovery, Tracking, backend runtime, APIs, and authority boundaries remain reused.

No `.ai-context` update was required because the fix did not discover new permanent cross-platform UX architecture truth. It corrected one Marketplace presentation inconsistency inside the already-certified marketplace presentation boundary.

## Success Answers

- Reviewed ONLY Marketplace UX? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No.**
- Frontend modified? **Yes, one Marketplace Checkout summary presentation fix only.**
- Marketplace UX Certified? **Yes.**
- Checkout UX Reused? **Yes.**
- Merchant Grouping UX Certified? **Yes.**
- Split Preview UX Certified? **Yes.**
- Shipping UX Reused? **Yes.**
- Payment UX Reused? **Yes.**
- Recovery UX Certified? **Yes.**
- Responsive UX Certified? **Yes.**
- Accessibility Certified? **Yes, source-level UX/accessibility certification; deployed assistive-tech smoke evidence remains operational.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable UX? **Yes, for the inspected Marketplace UX/navigation layer; operational rollout evidence remains inherited from dependent certified runtimes.**
- Recommend next phase: **CommerceHub Marketplace Checkout frontend certification for source/runtime presentation integrity, followed by operational browser-chaos and accessibility smoke evidence before production rollout.**
