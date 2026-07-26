# AFFILIATE-CHECKOUT-2.5 UX / Navigation Certification

Date: 2026-07-08
Scope: Affiliate Checkout UX, navigation, branding, referral continuity, and Affiliate Store to Checkout flow only
Status: UX / NAVIGATION CERTIFIED

## Executive Summary

Affiliate Checkout UX was reviewed only as an Affiliate-specific presentation and referral-continuity overlay on the already-certified POS Admin Storefront Checkout journey.

No true Affiliate UX inconsistency was found. Production runtime, backend code, frontend code, checkout UX, shipping UX, payment UX, review UX, success UX, and recovery UX were not modified. This is a documentation-only certification.

Certification decision: **Affiliate Checkout UX and Navigation are certified**. Affiliate adds referral context, partner/affiliate visibility, affiliate landing/store entry, affiliate-aware links, and referral continuity only. It does not introduce a separate checkout journey, checkout page, payment page, shipping page, review page, shipping selector, payment authority, commission display, or customer-facing commission workflow.

## Journey

Certified Affiliate journey:

```text
Customer
  -> Affiliate Store / Landing Page
  -> Cart
  -> Checkout
  -> Payment
  -> Review
  -> Submit
  -> Pending / Success
  -> Order Tracking
```

The inspected storefront routes preserve the certified POS Admin Storefront Checkout structure:

- Affiliate entry is `/p/:referralCode/:slug`.
- Cart remains `/cart`.
- Checkout remains `/checkout`.
- Payment pending remains `/checkout/pending`.
- Legacy `/checkout/success` redirects to pending confirmation.
- Order tracking remains `/orders/:orderId/track`.

Checkout steps remain internal state on a single checkout route: `shipping`, `payment`, and `review`. Affiliate does not add an extra checkout, shipping, payment, or review page.

## Navigation

Affiliate navigation is preserved through `AffiliateLink`, `useAffiliateNavigate`, `useBuildAffiliateUrl`, and `affiliateRefTracker`.

Reviewed paths:

- Affiliate Store / Landing Page: template-driven affiliate landing renders product, affiliate, tenant/store, and CTA evidence without checkout logic.
- Product Page: existing storefront product pages continue to use storefront navigation and API admission.
- Cart: existing cart UX is reused; affiliate attribution is backend line evidence, not frontend authority.
- Checkout Entry: affiliate funnel endpoint creates or reuses cart context and redirects to the certified checkout URL.
- Checkout Resume: checkout recovery restores certified checkout candidates and re-enters backend cart/session authority.
- Back Navigation: checkout back buttons move only between certified internal steps.
- Browser Refresh: referral code can be restored from `?ref`, affiliate route, or session storage; accepted attribution still requires backend protected evidence and SQL revalidation.
- Recovery / Autosave: checkout autosave and recovery remain the certified POS Admin Storefront Checkout session UX.
- Success / Pending: payment completion enters backend-confirmed pending/success handling; affiliate-aware URLs preserve referral context where applicable.
- Order Tracking: tracking route remains the existing storefront tracking route.

## Affiliate Branding

Affiliate branding is presentation-only:

- Affiliate landing pages render backend-provided affiliate display name, referral code, tenant store name, store logo where available, product media, and campaign/template presentation.
- Header and banner indicators communicate partner referral / Affiliate Store context.
- Affiliate-aware links preserve referral context without changing destination ownership.
- Store identity remains tenant/storefront identity. Affiliate identity is shown as referral/partner context, not merchant, carrier, or payment-provider identity.

No customer-facing element was found that makes the affiliate appear to own the merchant store, carrier, payment provider, shipping decision, payment decision, or order authority.

## Referral UX

Referral continuity is candidate/evidence preservation only:

- `affiliateRefTracker` captures referral codes from `?ref=`, `/p/{referralCode}/{slug}`, or same-tab session storage.
- `httpClient` attaches `X-Affiliate-Ref` as a candidate header.
- Backend `AffiliateReferralMiddleware`, protected `AFF_ATTR` evidence, current SQL affiliate/promotion-link authority, and checkout final SQL revalidation decide accepted attribution.
- Frontend referral state may preserve navigation and user context, but it cannot create checkout, commission, shipping, payment, or order authority.

Commission evidence is not shown to customers in checkout. Customer-facing UX shows referral/partner context only.

## Checkout Reuse

Affiliate Checkout reuses the existing certified checkout UX:

- Existing `CheckoutPage`.
- Existing `CheckoutStepper`.
- Existing `ShippingStep`.
- Existing `PaymentStep`.
- Existing `ReviewStep`.
- Existing `OrderSummary`.
- Existing pending-payment and tracking UX.
- Existing checkout autosave/recovery UX.

Affiliate-specific additions are limited to a guest checkout banner, affiliate banner/badge context, affiliate-aware links, and referral-continuity URLs. These additions do not alter checkout authority or step ordering.

## Shipping UX

Shipping UX is reused.

Shipping methods come from existing checkout/storefront shipping APIs and `checkoutStore.fetchShippingMethods`. The UI maps over backend-returned shipping methods and renders ordinary method name, description, price, and estimated days.

Affiliate has no shipping selection logic, no shipping pricing logic, no carrier selection logic, and no provider-specific shipping rendering. FedEx can appear only as a normal backend-returned shipping method; Affiliate does not hardcode FedEx or any carrier.

## Payment UX

Payment UX is reused.

Payment methods come from existing storefront payment APIs and `checkoutStore.fetchPaymentMethods`. The UI renders backend-returned payment DTOs and validates stale/unavailable selections through existing checkout state.

Affiliate has no payment flow, no payment authority, no provider-specific payment branching, and no payment confirmation authority. Stripe, Paymob, Fawry, manual wallet, and other payment UX remain governed by the certified payment/checkout response admission paths.

## Recovery UX

Affiliate recovery preserves UX continuity without becoming authority:

- Browser refresh can restore referral context from session storage.
- Checkout autosave stores checkout candidates only.
- Recovery links restore cart, address, shipping, payment, coupon, and promotion candidates through backend recovery admission.
- Pending payment uses backend payment status and checkout tokens where applicable.
- Back navigation preserves normal checkout step state.

Referral evidence never becomes checkout authority during recovery. Accepted attribution still requires backend protected evidence and final checkout SQL revalidation. If affiliate evidence is missing, expired, stale, or rejected, checkout continues as ordinary storefront checkout and no commission authority is created.

## Responsive UX

Reviewed responsive behavior:

- Desktop uses a two-column checkout layout with sticky order summary.
- Tablet and mobile collapse to a single-column checkout layout.
- `OrderSummary` has a mobile collapsed summary with `aria-expanded` and `aria-controls`.
- Loading states use checkout/cart/payment skeletons.
- Slow network and stale backend selections show retryable banners and block unsafe continuation.
- Pending payment polling has refresh/timeout/failure states.

Affiliate additions are small banners/badges and do not create a separate responsive layout or overlap the certified checkout flow.

## Accessibility

Reviewed accessibility behavior:

- Checkout stepper uses `nav`, `aria-label`, `aria-current`, and disabled state.
- Form inputs use labels, required markers, `aria-invalid`, `aria-describedby`, and alert text for field errors.
- Error banners use `role="alert"` or live regions where needed.
- Autosave and stale-selection notices use status/alert semantics.
- Step transitions move focus to the current heading.
- Smooth scrolling respects `prefers-reduced-motion`.
- Mobile order summary exposes expanded/collapsed state.
- Buttons retain keyboard focus styles and disabled states.

Affiliate banners/badges remain presentation context and do not add keyboard traps or modal flows.

## Certification Decision

Affiliate Checkout UX / Navigation is **CERTIFIED**.

No production UX fix was required. No focused UX certification tests were added because the inspected implementation already preserves the frozen POS Admin Storefront Checkout UX and existing Affiliate runtime/API certification tests cover the authority boundaries. This pass adds only the requested certification document.

## Explicit Success Answers

- Reviewed ONLY Affiliate UX? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No.**
- Frontend modified? **No.**
- Affiliate UX Certified? **Yes.**
- Checkout UX Reused? **Yes.**
- Shipping UX Reused? **Yes.**
- Payment UX Reused? **Yes.**
- Affiliate Branding Certified? **Yes.**
- Referral UX Certified? **Yes.**
- Recovery UX Certified? **Yes.**
- Responsive UX Certified? **Yes.**
- Accessibility Certified? **Yes.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable UX? **Yes, for Affiliate UX / Navigation. Production rollout still depends on the already-frozen operational gates for checkout, shipping, payment, order, shipment, inventory, and warehouse authorities.**
- Recommend next phase: **Affiliate operational rollout evidence: execute referral-to-checkout-to-paid-order smoke tests through the certified POS Admin Storefront Checkout, including desktop/tablet/mobile UX, recovery, pending payment, and order tracking evidence.**

## AI Context Update Decision

No `.ai-context` files were updated.

Reason: this pass discovered no new permanent cross-platform UX architecture truth. It confirmed the frozen rule that Affiliate is a referral, branding, attribution-visibility, and continuity overlay on the certified POS Admin Storefront Checkout journey.
