# POSADMIN-CHECKOUT-2.5 - UX / Navigation Certification

Date: 2026-07-08  
Scope: POS Admin Storefront Checkout UX and Navigation only  
Status: UX AND NAVIGATION CERTIFIED  
Runtime impact: none  
Backend impact: none  
Frontend impact: none

## Executive Summary

POS Admin Storefront Checkout UX and Navigation were reviewed as a UX architecture certification only. This pass reviewed the Storefront checkout entry, cart review presentation, customer/address capture, shipping selection, payment selection, order review, submission, pending/success handling, failure recovery, checkout resume, autosave, back navigation, refresh behavior, and responsive flow.

The review was limited to POS Admin Storefront Checkout. Affiliate Checkout, CommerceHub Checkout, and POS Sales Checkout were excluded except where the current storefront route preserves affiliate-aware links without recertifying Affiliate Checkout authority.

No React implementation, runtime behavior, backend behavior, API contract, payment authority, shipping authority, order authority, or checkout authority was modified. This document is the only change for POSADMIN-CHECKOUT-2.5.

Certification decision: POS Admin Storefront Checkout UX and Navigation are production-certifiable as a simple, deterministic, recoverable, fail-closed checkout journey. The current UI uses three visible steps, with customer/address and shipping-method selection grouped into one Shipping screen. That grouping is certified because it avoids an unnecessary extra screen while preserving the canonical logical journey: Customer -> Shipping -> Payment -> Review -> Submit.

## Journey

Canonical logical journey:

```text
Checkout entry from cart
  -> Customer/address information
  -> Shipping method selection
  -> Payment method selection
  -> Order review
  -> Submit
  -> Payment handoff or pending status
  -> Success/track order after backend-confirmed payment/order state
```

Certified visible screen flow:

| Logical stage | Current UX surface | Certification |
|---|---|---|
| Checkout entry | `/checkout`, reached from cart/breadcrumb/checkout entry | Certified |
| Cart review | Sticky/collapsible `OrderSummary` | Certified |
| Customer information | `ShippingStep` address fields | Certified |
| Address selection | Current runtime captures/editable address candidate; saved-address picker is not in this certified path | Certified for implemented path |
| Shipping selection | `ShippingStep` method cards from backend shipping API | Certified |
| Payment selection | `PaymentStep` method cards from backend payment API | Certified |
| Order review | `ReviewStep` summary, edit links, server pricing, phone verification where required | Certified |
| Submission | Single Place Order action with duplicate-submit prevention | Certified |
| Pending/success | `/checkout/pending`, payment status polling, legacy success redirect, track-order links | Certified |
| Recovery/resume | `/recover-checkout/:token`, autosave, local candidate restore, backend recovery | Certified |

The recommended four-step flow is preserved as logical UX architecture. The current visible three-step implementation is accepted because Customer and Shipping are tightly coupled in the implemented address-plus-method screen and adding a standalone Customer screen would add friction without adding authority or recovery value.

## Navigation

| Navigation concern | Reviewed behavior | Certification |
|---|---|---|
| Forward navigation | `goToNextStep` validates the current step before advancing | Certified |
| Back navigation | `goToPreviousStep`, review edit links, and completed-step stepper clicks allow deterministic backward movement | Certified |
| Stepper navigation | Completed steps are clickable; future steps remain disabled | Certified |
| Submit navigation | Submission disables duplicate interaction and routes to provider handoff or pending status | Certified |
| Browser refresh | Checkout state persists only candidate step/method IDs and sensitive address candidates in session storage; live cart/pricing/shipping/payment refetch on entry | Certified |
| Deep links | `/checkout`, `/checkout/pending?orderId=...`, `/recover-checkout/:token`, and legacy `/checkout/success` redirect are explicit route boundaries | Certified |
| Expired session | Recovery page fails closed to cart/start-over messaging | Certified |
| Recovery token | Recovery token is consumed through backend recovery API before returning to `/checkout` | Certified |
| Autosave | Checkout session create/update is debounced, retryable, and visible through autosave banner | Certified |
| Session timeout | Recovery and cart/session failures return recoverable UX instead of proceeding from stale state | Certified |
| Duplicate submit prevention | `isSubmitting`, backend idempotency, pending route, and retry-safe errors prevent double order UX | Certified |

## Screen Flow

Current screen flow:

```text
/checkout
  Shipping screen
    - customer name
    - phone
    - city / area / street / notes
    - backend-loaded shipping method cards
  Payment screen
    - backend-loaded payment method cards
    - manual-wallet fields only when backend-enabled MANUALWALLET is selected
  Review screen
    - address summary
    - shipping summary
    - payment summary
    - server pricing
    - phone OTP where tenant policy requires it
    - Place Order
  Provider handoff or /checkout/pending
  Success/track order after backend-confirmed status
```

The UX is deterministic: only completed steps can be revisited from the stepper, current-step validation gates forward navigation, review edit actions route to the owning step, and submission routes to backend-confirmed payment/order presentation rather than client-side success assumptions.

## UX Authority Matrix

| Concern | Frontend UX owns | Backend/domain owns | Certification |
|---|---|---|---|
| Presentation | Layout, cards, summaries, stepper, banners, buttons | Accepted DTO evidence | Certified |
| Navigation | Step state, forward/back/edit/pending navigation | Route/API admission and final state | Certified |
| Local draft state | Address candidates, selected method candidates, current step | Checkout session, cart, pricing, order, payment truth | Certified |
| Loading | Skeletons, spinners, pending messages, disabled buttons | Request admission and authoritative responses | Certified |
| Animations/progress | Step highlight, spinner, pending polling status | None | Certified |
| Cart | Read-only presentation of cart items | Backend cart authority | Certified |
| Pricing | Display only backend pricing preview | Pricing, discounts, taxes, shipping, payment fee, totals | Certified |
| Shipping | Candidate selection and display | Shipping methods, eligibility, shipping price calculation | Certified |
| Payment | Candidate selection and provider handoff presentation | Payment methods, provider sessions, payment lifecycle | Certified |
| Orders | Pending/success presentation and tracking link | Order identity, order number, lifecycle, status | Certified |
| Recovery | Recovery intent, redirect, user messaging | Signed token, checkout-session recovery, live cart revalidation | Certified |

## Error UX

Error UX is consistent and recoverable.

| Error class | UX behavior | Certification |
|---|---|---|
| Shipping load failure | Red error block with retry shipping options | Certified |
| Payment load failure | Red error block with retry | Certified |
| Stale shipping/payment selection | Amber warning with previous selection and blocked progression | Certified |
| Pricing unavailable | Amber warning with Retry totals | Certified |
| Checkout submission failure | Contextual review-page error helper | Certified |
| Out of stock / price changed | Review cart recovery CTA | Certified |
| Network error | Retry-safe message; details remain local and non-authoritative | Certified |
| Expired recovery | Recovery page sends user to cart | Certified |
| Missing pending order ID | Pending page sends user back to checkout | Certified |
| Payment pending timeout | Manual refresh and track-order options | Certified |

No reviewed UX exposes SQL details, stack traces, provider secrets, or technical backend exceptions to customers.

## Recovery UX

Recovery is certified as fail-closed and resumable.

| Recovery concern | UX behavior | Certification |
|---|---|---|
| Checkout resume | `/recover-checkout/:token` restores only after backend recovery succeeds | Certified |
| Autosave | Address, shipping, payment candidate state autosaves through checkout session API | Certified |
| Autosave failure | Banner warns that details are not saved and offers Retry save | Certified |
| Cart changed during recovery | Checkout displays recovery review warning and requires user review | Certified |
| Converted session | Recovery shows completed-session message and does not recreate checkout | Certified |
| Corrupt/unverified cart | Recovery routes to cart review | Certified |
| Pending payment refresh | `/checkout/pending` polls backend canonical payment status and offers manual refresh | Certified |
| Browser refresh | Candidate state can restore, but cart/pricing/shipping/payment authority refetches | Certified |

## Shipping UX

Shipping UX is certified.

| Shipping requirement | Reviewed behavior | Certification |
|---|---|---|
| Methods loaded from API | `fetchShippingMethods` reads `/api/storefront/shipping-methods` with no-store headers | Certified |
| No provider names hardcoded | Cards display backend `name`, `description`, `price`, and ETA only | Certified |
| FedEx appears naturally | Any backend-returned method name, including FedEx, renders as a normal method card | Certified |
| Pricing refreshed from backend | Pricing preview is refreshed on cart, address, shipping, or payment changes | Certified |
| Frontend never calculates shipping | Summary/review display `pricing.shipping`; selector displays backend method `price` only | Certified |
| Stale method recovery | Missing latest method becomes stale selection and blocks progression | Certified |

## Payment UX

Payment UX is certified.

| Payment requirement | Reviewed behavior | Certification |
|---|---|---|
| Methods loaded from backend | `fetchPaymentMethods` reads `/api/storefront/payment-methods` with no-store headers | Certified |
| No payment secrets | Payment method DTO admission accepts display/configuration evidence only | Certified |
| No provider-specific branching for authority | Provider-specific rendering appears only after backend checkout response admission | Certified |
| Payment handoff delegated | Stripe/Paymob/Fawry/manual-wallet presentation requires backend payment-session evidence | Certified |
| Unavailable/stale methods | Disabled/stale methods block progression and explain recovery | Certified |
| Duplicate submit prevention | `isSubmitting`, disabled buttons, and backend idempotency preserve recovery | Certified |

Provider-specific icons and handoff screens are presentational only. They do not make frontend payment authority.

## Responsive UX

Responsive UX is certified.

| Responsive concern | Reviewed behavior | Certification |
|---|---|---|
| Mobile layout | Single-column checkout flow with full-width primary actions | Certified |
| Desktop layout | Two-column grid with sticky order summary | Certified |
| Progress visibility | Stepper remains visible; labels collapse on small screens but step numbers/status remain | Certified |
| Button placement | Primary continue/place-order action appears before secondary back action on mobile; side-by-side on desktop | Certified |
| Order summary | Collapsible on mobile, sticky on desktop | Certified |
| Loading UX | Checkout skeleton, method skeletons, pricing states, pending polling spinner | Certified |
| Recovery UX | Recovery and pending pages use centered, narrow, mobile-safe cards | Certified |

## Accessibility Notes

Accessibility posture is production-certifiable for UX architecture.

Certified notes:

| Area | Evidence |
|---|---|
| Stepper | `aria-label`, `aria-current`, disabled future steps |
| Inputs | Labels, required indicators, `aria-invalid`, `aria-describedby`, inline errors |
| Error banners | `role="alert"`, `aria-live` where needed |
| Autosave | Status banners use `role="status"` or `role="alert"` |
| Focus management | Step changes focus the step heading; success screen focuses heading |
| Reduced motion | Smooth scrolling respects `prefers-reduced-motion` |
| Buttons | Disabled state and focus rings are present |
| Mobile summary | Collapsible summary uses `aria-expanded` and `aria-controls` |

Residual accessibility improvement opportunities remain non-blocking: some hardcoded English labels should continue moving through localization, and payment-method icon mapping should remain presentation-only if new methods are introduced.

## Certification Decision

UX Certified: YES.

Navigation Certified: YES.

Recovery Certified: YES.

Shipping UX Certified: YES.

Payment UX Certified: YES.

Responsive UX Certified: YES.

Production-certifiable UX: YES, for POS Admin Storefront Checkout UX and Navigation.

Architecture preserved: YES. POSADMIN-CHECKOUT-0, POSADMIN-CHECKOUT-1, POSADMIN-CHECKOUT-2, Shipping Platform, Shipping Administration, Storefront, Payment, Order, Inventory, Coupon, Promotion, Geography, Customs, and Carrier authority freezes remain unchanged.

Operational production rollout still depends on the already frozen runtime/provider/observability gates outside this UX-only certification.

## Explicit Success Answers

- Reviewed ONLY POS Admin Checkout UX? **Yes.** Affiliate Checkout, CommerceHub Checkout, and POS Sales Checkout were excluded.
- Runtime modified? **No.**
- Backend modified? **No.**
- Frontend modified? **No runtime/frontend code modified.**
- UX Certified? **Yes.**
- Navigation Certified? **Yes.**
- Recovery Certified? **Yes.**
- Shipping UX Certified? **Yes.** Shipping methods/pricing are backend-loaded and presentation-only.
- Payment UX Certified? **Yes.** Payment methods/handoffs are backend-loaded and presentation-only.
- Responsive UX Certified? **Yes.** Mobile and desktop flows are certified.
- Architecture preserved? **Yes.** Frozen authority boundaries were preserved.
- Production-certifiable UX? **Yes.** UX is production-certifiable for the reviewed POS Admin Storefront Checkout path.
- Recommend next phase: **POSADMIN-CHECKOUT-3 operational observability/API runbook certification**, focused on dashboards, alerts, synthetic checkout probes, replay drills, support runbooks, and production recovery evidence without changing checkout authority or UX architecture.

## AI Context Decision

No `.ai-context` files were updated.

Reason: this phase is UX/navigation certification only and discovered no new permanent cross-system authority truth requiring context-file mutation. The certification record is this document.
