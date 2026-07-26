# AFFILIATE-CHECKOUT-3 Frontend Certification

Date: 2026-07-09
Scope: Affiliate Checkout Frontend only
Status: FRONTEND CERTIFIED

## Executive Summary

Affiliate Checkout Frontend was reviewed only as an Affiliate presentation, branding, attribution-visibility, and referral-continuity overlay on the already-certified POS Admin Storefront Checkout frontend.

No true Affiliate frontend inconsistency was found. Runtime, backend, and production frontend code were not modified. Focused source-level frontend certification tests were added to freeze the Affiliate frontend boundary.

Certification decision: **Affiliate Checkout Frontend is certified**. Affiliate routes and landing CTAs enter the certified Storefront checkout route and reuse the certified checkout page, steps, summary, pending-payment, tracking, autosave, and recovery frontend. Affiliate does not define a separate checkout UI, shipping UI, payment UI, review UI, pending UI, or tracking UI.

## Frontend Review

Reviewed Affiliate customer-facing frontend surfaces in `PointOfSale.Storefront`:

- Affiliate landing route `/p/:referralCode/:slug`.
- Affiliate landing page and landing templates.
- `AffiliateLink`, `useAffiliateNavigate`, `useBuildAffiliateUrl`, and `AffiliateContext`.
- `affiliateRefTracker` and `httpClient` referral header propagation.
- Affiliate funnel CTA entry through `POST /api/storefront/funnel/start`.
- Checkout entry, pending-payment, checkout recovery, cart recovery, and order tracking transitions.
- Affiliate banners, guest-checkout banner, referral badge/context, and storefront layout integration.

Excluded as frozen adjacent authorities: POS Admin Checkout frontend implementation, CommerceHub frontend, and POS Sales frontend.

## Component Reuse

Affiliate checkout reuses the certified Storefront checkout:

- `CheckoutPage`.
- `CheckoutStepper`.
- `ShippingStep`.
- `PaymentStep`.
- `ReviewStep`.
- `OrderSummary`.
- `CheckoutPendingPage`.
- `OrderTrackingPage`.
- Checkout autosave and recovery store behavior.
- Pending-payment polling and backend-confirmed completion transition.

No `AffiliateCheckoutPage`, `AffiliateShippingStep`, `AffiliatePaymentStep`, or Affiliate-owned order summary was found.

## Routing Review

Certified routing path:

```text
/p/:referralCode/:slug
  -> AffiliateLandingPage
  -> POST /api/storefront/funnel/start
  -> backend-issued checkoutUrl
  -> /checkout
  -> ThemePageRoute pageName="checkout"
  -> certified CheckoutPage
  -> /checkout/pending
  -> /orders/:orderId/track
```

`storefront/pages/default/checkout.tsx` directly re-exports `pages/CheckoutPage`. Affiliate routes do not replace checkout internals.

## Branding Review

Affiliate branding is presentation-only:

- Affiliate landing pages render backend-provided affiliate display name, referral code, product, tenant store name, logo, campaign/template, and CTA copy.
- Affiliate banners and guest-checkout context disclose partner referral state.
- Store identity remains tenant/storefront identity.
- Merchant, carrier, shipping, payment, order, and shipment authority are not assigned to the affiliate.

## Referral UX

Referral continuity is candidate/evidence preservation only:

- `affiliateRefTracker` captures `?ref=`, `/p/{referralCode}/{slug}`, and same-tab `sessionStorage`.
- `httpClient` sends `X-Affiliate-Ref` as backend-validation candidate evidence.
- `AffiliateLink` and affiliate navigation preserve `ref` in internal links.
- Checkout pending and tracking transitions use affiliate-aware links/navigation where applicable.
- Browser refresh and recovery retain referral candidate continuity through session-scoped frontend evidence, while accepted attribution still requires backend middleware and final checkout SQL revalidation.

Referral never becomes merchant, checkout, shipping, payment, order, shipment, or commission authority in the frontend.

## Shipping UI

Shipping UI is reused from certified checkout:

- Shipping methods are fetched from `/api/storefront/shipping-methods`.
- Response evidence is admitted before state through checkout-store admission.
- `ShippingStep` maps backend-returned methods dynamically.
- No Affiliate shipping calculation, provider branching, carrier selection, carrier authority, or FedEx hardcoding was found.

## Payment UI

Payment UI is reused from certified checkout:

- Payment methods are fetched from `/api/storefront/payment-methods`.
- Response evidence is admitted before state through checkout-store admission.
- `PaymentStep` maps backend-returned payment methods dynamically.
- Affiliate frontend does not create payment intents, confirm payment, branch by provider, or own payment lifecycle authority.

Provider-specific handoff rendering remains part of the already-certified POS Admin Storefront Checkout frontend and is not duplicated by Affiliate.

## Responsive UX

The certified checkout responsive behavior is reused:

- Desktop checkout uses a two-column layout with sticky order summary.
- Tablet/mobile collapse to single-column flow.
- `OrderSummary` supports mobile collapse with `aria-expanded` and `aria-controls`.
- Loading states use checkout/cart/payment skeletons.
- Slow-network and stale-selection states block unsafe continuation and expose retry controls.

Affiliate additions are small banners/badges and do not create a separate responsive checkout layout.

## Accessibility

The certified accessibility behavior remains reused:

- Checkout stepper uses `nav`, `aria-label`, `aria-current`, and disabled states.
- Inputs use labels, required markers, `aria-invalid`, `aria-describedby`, and field-error alerts.
- Error banners use `role="alert"` or live-region semantics.
- Autosave/stale-selection messages use status/alert semantics.
- Step transitions focus the current heading and respect reduced-motion preferences.
- Mobile summary exposes expanded/collapsed state.

Affiliate banners are informational and do not add modal flows, keyboard traps, or checkout authority.

## Regression Coverage

Added focused frontend certification tests:

```text
PointOfSale.Web/src/tests/affiliateCheckoutFrontendCertification.test.ts
```

Coverage verifies:

- Affiliate routing enters certified checkout.
- Checkout component reuse is preserved.
- No Affiliate duplicate checkout/shipping/payment/summary components exist.
- Referral candidate persistence across landing, navigation, checkout, refresh, pending, and tracking.
- Dynamic shipping rendering through certified Storefront shipping APIs.
- Dynamic payment rendering through certified Storefront payment APIs.
- No Affiliate provider hardcoding, shipping calculation, payment confirmation, or custom checkout APIs.

## Certification Decision

Affiliate Checkout Frontend is **FRONTEND CERTIFIED**.

No production frontend fix was required. The Affiliate frontend remains a presentation, branding, attribution-visibility, and referral-continuity overlay on the certified POS Admin Storefront Checkout frontend.

No `.ai-context` files were updated because this pass discovered no new permanent architecture truth.

## Explicit Success Answers

- Reviewed ONLY Affiliate Frontend? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No.**
- Frontend modified? **Only focused frontend certification tests and this document were added. No production frontend code modified.**
- Affiliate Frontend Certified? **Yes.**
- Checkout Frontend Reused? **Yes.**
- Shipping UI Reused? **Yes.**
- Payment UI Reused? **Yes.**
- Referral UX Certified? **Yes.**
- Branding Certified? **Yes.**
- Dynamic Rendering Certified? **Yes.**
- Accessibility Certified? **Yes.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable frontend? **Yes, for Affiliate Checkout Frontend. Production rollout remains governed by the already-frozen checkout, shipping, payment, order, shipment, and operational gates.**
- Recommend next phase: **Affiliate operational rollout evidence: run referral-to-checkout-to-pending-to-tracking smoke tests across desktop, tablet, and mobile with real shipping/payment/order gates and recorded monitoring evidence.**
