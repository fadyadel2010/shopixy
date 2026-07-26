# POSADMIN-CHECKOUT-3 - Storefront Checkout Frontend Certification

Date: 2026-07-08  
Scope: POS Admin Storefront Checkout frontend only  
Status: FRONTEND CERTIFIED AFTER API-CONSUMPTION HARDENING  
Runtime impact: none  
Backend impact: none  
Frontend impact: minimal Storefront checkout API-consumption hardening

## Executive Summary

POS Admin Storefront Checkout frontend was reviewed and implemented against the frozen checkout architecture, runtime authority, certified Storefront Checkout APIs, and certified UX/navigation flow.

The review was limited to `PointOfSale.Storefront` POS Admin Storefront Checkout. Affiliate Checkout, CommerceHub Checkout, and POS Sales Checkout were excluded.

One true frontend API-consumption inconsistency was found and fixed: `checkoutStore.ts` called `/api/payments/cod/confirm/{orderId}` after checkout submission. That endpoint is outside the certified Storefront Checkout API set and gave the browser a payment-confirmation action. The call, endpoint constant, and action were removed. Checkout now submits only to the certified checkout API and then presents backend-owned payment/order state through the pending/status flow.

Certification decision: POS Admin Storefront Checkout frontend is production-certifiable after this hardening.

## Frontend Architecture

The frontend owns presentation, current step, loading/error state, animation, temporary draft state, navigation, autosave status, and expanded/collapsed panels only.

The backend remains authoritative for cart, pricing, inventory, shipping, coupons, promotions, payment, orders, checkout sessions, recovery, idempotency, and authority admission.

Certified frontend surfaces:

| Surface | File | Certification |
|---|---|---|
| Checkout page | `PointOfSale.Storefront/src/pages/CheckoutPage.tsx` | Certified |
| Customer/shipping screen | `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx` | Certified |
| Payment screen | `PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx` | Certified |
| Review screen | `PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx` | Certified |
| Pending screen | `PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx` | Certified |
| Recovery screen | `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx` | Certified |
| Success presentation | `PointOfSale.Storefront/src/components/checkout/CheckoutSuccess.tsx` | Certified as presentation only |
| Autosave banner | `CheckoutAutosaveBanner` inside `CheckoutPage.tsx` | Certified |
| Sticky/collapsible summary | `PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx` | Certified |

## Component Structure

Checkout is composed as a single `/checkout` route with internal step state:

```text
CheckoutPage
  -> CheckoutStepper
  -> CheckoutAutosaveBanner
  -> ShippingStep
  -> PaymentStep
  -> ReviewStep
  -> OrderSummary
  -> StripePaymentForm / PaymobPaymentForm only after backend handoff evidence
  -> Pending redirect for non-inline handoffs
```

Recovery and pending are explicit route boundaries:

- `/recover-checkout/:token` calls the certified recovery API before returning to checkout.
- `/checkout/pending?orderId=...` polls canonical backend payment/order status.
- Legacy `/checkout/success` redirects to `/checkout/pending`.

## Navigation

Navigation is certified as deterministic and frontend-owned only.

- `goToNextStep` validates the current step before advancing.
- `goToPreviousStep` allows deterministic backward movement.
- Completed stepper steps are clickable; future steps remain disabled.
- Review edit actions route to `shipping` or `payment`.
- `isSubmitting` prevents duplicate submit interaction.
- Accepted checkout responses route to inline backend handoff or pending status, not client-side success authority.

## State Ownership

Frontend state ownership is limited to:

- Current step.
- Address candidate fields.
- Selected shipping method ID candidate.
- Selected payment method code candidate.
- Manual wallet candidate fields before backend admission.
- Autosave status and last-save presentation.
- Loading, retry, stale-selection, and error presentation.
- Mobile summary expanded/collapsed state.

Frontend state does not own:

- Cart truth.
- Pricing/totals.
- Shipping cost or provider selection.
- Payment success, collection, verification, or settlement.
- Order identity or lifecycle authority.
- Inventory, coupons, promotions, taxes, geography, customs, warehouse, or shipment authority.

Persisted browser state is candidate-only. Local storage persists selected candidate IDs and current step. Session storage can restore sensitive address candidates for browser refresh. Pricing, accepted payment authority, order authority, checkout tokens, and SQL truth are not persisted as browser authority.

## API Consumption

Certified Storefront Checkout API consumption:

| API | Frontend use | Certification |
|---|---|---|
| `POST /api/storefront/checkout` | Submit checkout candidates and backend expected-pricing echo | Certified |
| `POST /api/storefront/checkout/pricing` | Refresh backend pricing preview | Certified |
| `POST /api/storefront/checkout-session` | Create/resume autosave session | Certified |
| `PATCH /api/storefront/checkout-session/{id}` | Autosave draft candidates | Certified |
| `GET /api/storefront/checkout-session/recover/{token}` | Backend-admitted recovery | Certified |
| `GET /api/storefront/shipping-methods` | Backend-loaded shipping methods | Certified |
| `GET /api/storefront/payment-methods` | Backend-loaded payment methods | Certified |
| Canonical payment-status read | Pending presentation only after checkout handoff | Certified as payment authority presentation |

The removed `/api/payments/cod/confirm/{orderId}` call is no longer consumed by Storefront Checkout.

## Shipping UI

Shipping UI is certified.

- Shipping methods load dynamically through `fetchShippingMethods`.
- Storefront admits DTO shape before state through `admitStorefrontShippingMethods`.
- Method cards render backend `name`, `description`, `price`, and `estimatedDays` evidence.
- No provider names are hardcoded in `ShippingStep`.
- FedEx appears automatically when the backend returns a FedEx shipping method DTO.
- Stale selected methods are marked stale and block progression.
- Shipping totals render only backend `pricing.shipping` from pricing preview.
- No frontend shipping calculation or provider selection exists.

## Payment UI

Payment UI is certified.

- Payment methods load dynamically through `fetchPaymentMethods`.
- Storefront admits DTO shape before state through `admitStorefrontPaymentMethods`.
- Payment cards render backend display/configuration evidence.
- Provider-specific icons and handoff forms are presentation only.
- `requireStorefrontPaymentSessionAuthority` admits backend handoff evidence before rendering Stripe, Paymob, Fawry, or ManualWallet presentation.
- Payment secrets are not accepted from payment-method reads.
- Payment success, failure, collection, verification, and settlement remain backend/payment authority.

## Recovery UI

Recovery UI is certified.

- Autosave is debounced, retryable, and visible through `CheckoutAutosaveBanner`.
- Recovery uses backend `/recover-checkout/:token` admission and fails closed on expired, converted, corrupted, or unrevalidated sessions.
- Browser refresh restores only candidates and re-enters backend cart/pricing/shipping/payment reads.
- Pending status polls canonical backend state and offers manual retry/refresh.
- Duplicate submit protection is enforced by frontend `isSubmitting` plus backend idempotency.

## Responsive UX

Responsive UX is certified.

- Desktop checkout uses a two-column layout with `lg:grid-cols-3` and sticky summary.
- Mobile checkout uses a single-column flow.
- Order summary is collapsible on mobile and expanded/sticky on desktop.
- Primary actions use large touch targets.
- Loading, retry, stale-selection, and pending states are mobile-safe.

## Accessibility

Accessibility is certified for the reviewed frontend.

- Stepper has `aria-label` and `aria-current="step"`.
- Future steps are disabled.
- Inputs have labels, `aria-invalid`, and error descriptions.
- Error banners use `role="alert"` and live regions.
- Autosave banners use `role="status"` or `role="alert"`.
- Step changes focus the current heading.
- Reduced motion is respected for scroll behavior.
- Mobile order summary uses `aria-expanded` and `aria-controls`.

## Regression Coverage

Focused frontend certification tests were added:

- `PointOfSale.Tests/Architecture/POSAdminCheckout3FrontendCertificationTests.cs`

Coverage freezes:

- Certified route/screen surfaces.
- Checkout API consumption.
- Removal of direct COD confirmation API consumption.
- Dynamic shipping methods and FedEx dynamic rendering.
- Dynamic payment methods and backend handoff delegation.
- Autosave, recovery, pending, duplicate submit protection, and browser refresh candidate persistence.
- Review edit actions and backend pricing display.
- Responsive layout, sticky/collapsible summary, stepper, ARIA, focus, and reduced-motion affordances.
- Presence of this certification document.

## Verification

Preparation verified:

- All five `.ai-context` files were reviewed.
- `POSADMIN_CHECKOUT_0_ARCHITECTURE.md` was reviewed and treated as frozen.
- `POSADMIN_CHECKOUT_1_RUNTIME_AUTHORITY.md` was reviewed and treated as frozen.
- `POSADMIN_CHECKOUT_2_ADMINISTRATION_STOREFRONT_APIS.md` was reviewed and treated as frozen.
- `POSADMIN_CHECKOUT_2_5_UX_NAVIGATION_CERTIFICATION.md` was reviewed and treated as frozen.
- Shipping Method final authority, Shipping Method read/session authority, Carrier Integration authority, and FedEx final/operational certifications present in this workspace were reviewed as frozen context.

Frontend verification performed:

- Source review of checkout page, checkout store, shipping/payment/review/summary components, pending page, recovery page, success component, routes, and payment-session admission.
- Minimal frontend hardening removed the non-certified COD confirmation API call.
- Focused source-level frontend certification tests were added.

AI context update decision:

- No `.ai-context` files were updated.
- Reason: this frontend implementation/certification did not discover new permanent frontend/platform architecture truth beyond the certification recorded here.

## Certification Decision

Frontend Certified: YES.

API Consumption Certified: YES.

Shipping UI Certified: YES.

Payment UI Certified: YES.

Recovery UI Certified: YES.

Responsive UI Certified: YES.

Accessibility Certified: YES.

Production-certifiable frontend: YES, for POS Admin Storefront Checkout frontend after removing the non-certified COD confirmation call.

## Explicit Success Answers

- Reviewed ONLY POS Admin Checkout Frontend? **Yes.** Affiliate Checkout, CommerceHub Checkout, and POS Sales Checkout were excluded.
- Runtime modified? **No.**
- Backend modified? **No.**
- Frontend modified? **Yes.** Minimal Storefront checkout frontend hardening only.
- Frontend Certified? **Yes.**
- API Consumption Certified? **Yes.** Storefront Checkout no longer calls the non-certified COD confirmation endpoint.
- Shipping UI Certified? **Yes.**
- Payment UI Certified? **Yes.**
- Recovery UI Certified? **Yes.**
- Responsive UI Certified? **Yes.**
- Accessibility Certified? **Yes.**
- FedEx rendered dynamically? **Yes.** Any backend-returned FedEx shipping method renders as a normal shipping method card.
- Business logic inside React? **No.** React owns presentation/candidates only; backend owns authority and calculations.
- Architecture preserved? **Yes.** POSADMIN-CHECKOUT-0, POSADMIN-CHECKOUT-1, POSADMIN-CHECKOUT-2, and POSADMIN-CHECKOUT-2.5 remain preserved.
- Backward compatible? **Yes.** No API contracts or persisted data shapes changed; checkout now routes payment status through the certified pending flow.
- Production-certifiable frontend? **Yes.**
- Recommend next phase: **POSADMIN-CHECKOUT-4 frontend operational verification**, focused on browser/device smoke tests, accessibility audit evidence, payment-provider sandbox UX drills, recovery drills, and production monitoring without changing checkout authority.
