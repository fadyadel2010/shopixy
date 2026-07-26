# POSADMIN-CHECKOUT-2 - Administration Storefront Checkout APIs

Date: 2026-07-08  
Scope: POS Admin Storefront Checkout APIs only  
Status: API CERTIFIED AFTER ERROR ENVELOPE HARDENING  
Runtime impact: none  
Backend impact: minimal API response-envelope hardening  
Frontend impact: none

## Executive Summary

The POS Admin Storefront Checkout API surface was reviewed against the frozen checkout architecture and runtime authority certifications. The review covered only Storefront Checkout, Checkout Session, Checkout Pricing, Checkout Recovery, and Checkout Submission APIs.

Affiliate Checkout APIs, CommerceHub Checkout APIs, and POS Sales Checkout were excluded.

One true API-surface inconsistency was found: selected checkout-session recovery failures and the checkout phone-block failure used anonymous error payloads instead of the standard `ApiErrorResponse` envelope with correlation `traceId`. This pass normalized those responses only. No checkout runtime, pricing, inventory, shipping, payment, order, coupon, promotion, warehouse, shipment, or frontend architecture was redesigned.

Certification decision: POS Admin Storefront Checkout APIs are production-certifiable after this response-envelope hardening.

## API Inventory

| API | Route | Authority role | Certification |
|---|---|---|---|
| Checkout submission | `POST /api/storefront/checkout` | Accepts checkout candidates, validates HTTP context, delegates final checkout authority to `ICheckoutService.ProcessCheckoutAsync` | Certified |
| Checkout pricing | `POST /api/storefront/checkout/pricing` | Accepts pricing candidates, validates HTTP context, delegates calculation to `ICheckoutService.PreviewPricingAsync` | Certified |
| Checkout session create/resume | `POST /api/storefront/checkout-session` | Resolves tenant/customer/session owner, reloads live cart authority, delegates session persistence | Certified |
| Checkout session autosave | `PATCH /api/storefront/checkout-session/{id}` | Verifies session owner, reloads live cart authority, delegates autosave persistence | Certified |
| Checkout recovery | `GET /api/storefront/checkout-session/recover/{token}` | Validates signed tenant-bound token, rejects converted/invalid sessions, reloads live cart authority | Certified |
| Phone verification status | `GET /api/storefront/checkout/phone-verification-status` | Admits tenant/customer/session context and delegates OTP status check | Certified as adjacent Storefront checkout admission API |
| Phone OTP request | `POST /api/storefront/checkout/request-phone-otp` | Admits tenant/session context and delegates OTP request | Certified as adjacent Storefront checkout admission API |
| Phone OTP verify | `POST /api/storefront/checkout/verify-phone-otp` | Admits tenant/session/customer context and delegates OTP verification | Certified as adjacent Storefront checkout admission API |

## Controller Review

`CheckoutController` owns HTTP routing, header admission, authenticated customer/header authorization, request DTO validation, response shaping, no-store response headers, guest access cookie shaping, rate-limit attributes, and exception-to-error-envelope mapping.

`CheckoutController` delegates pricing and submission authority to `ICheckoutService`. It does not own pricing, inventory, shipment, payment lifecycle, warehouse, coupon, promotion, or order lifecycle truth.

`CheckoutSessionController` owns HTTP routing, tenant admission, authenticated customer/session ownership admission, recovery token admission, generic not-found semantics for unauthorized session/cart access, no stale cart recovery, and response shaping.

`CheckoutSessionController` delegates session persistence to `ICheckoutSessionService`, token validation/creation to `ICheckoutSessionTokenService`, and live cart revalidation to `ICartService`. It does not accept client item snapshots, client cart totals, or client currency as authority.

## DTO Review

Frontend DTOs remain candidate or presentation evidence only.

Accepted candidates:

- `CheckoutRequestDto.CartId`
- `CheckoutRequestDto.ShippingMethodId`
- `CheckoutRequestDto.ShippingAddress` and optional billing/geographic candidates
- `CheckoutRequestDto.PaymentMethod`
- Optional coupon/manual-wallet/customer-note evidence
- `CheckoutRequestDto.ExpectedPricing` as stale-pricing guard only
- Checkout-session form fields for autosave and recovery UX only

Rejected as authority:

- Client order IDs and order numbers are not accepted in checkout submission DTOs.
- Client item snapshots, cart totals, and currency on checkout-session create/update are overwritten from live backend cart authority.
- Client totals and pricing are not accepted as final financial truth.
- Client inventory, shipping cost, payment status, and order lifecycle fields are not accepted.

Response DTOs carry admitted backend evidence for presentation and handoff. Payment handoff fields remain backend-produced evidence and do not make checkout the payment lifecycle authority.

## Authorization Review

Authorization is certified for the reviewed API surface.

- Tenant isolation starts from `X-Tenant-Id` or resolved tenant context and continues through service-layer tenant predicates.
- Authenticated customer identity is resolved from `CustomerJwt` or `StorefrontJwt` claims.
- `X-Customer-Id` is accepted only when it matches authenticated identity or trusted internal service claims.
- Unauthenticated or untrusted `X-Customer-Id` does not create customer ownership.
- Guest checkout requires `X-Session-Id` and live session-owned cart authority.
- Checkout-session update requires either matching session owner or authenticated customer owner.
- Recovery requires a signed tenant-bound token, matching recovery nonce, unconverted session, and live cart revalidation.

## Validation Review

Validation is certified.

- Required tenant/session/customer context is validated before service authority is entered.
- Submission DTOs reject missing cart, shipping method, address, payment method, malformed geography, malformed manual wallet evidence, and malformed expected pricing.
- Pricing DTOs reject missing cart, invalid country, and malformed geography.
- Expected pricing rejects negative values and inconsistent totals or discount breakdowns.
- Recovery rejects missing/expired/invalid tokens, converted sessions, tenant/nonce mismatches, and unrevalidated live cart authority.

## Error Model

The Storefront Checkout API error model is certified after this pass.

- Checkout submission and pricing errors use `ApiErrorResponse` with stable `code`, `message`, and `traceId`.
- Checkout-session create/update authorization errors use generic not-found envelopes to avoid owner/session enumeration.
- Checkout-session recovery failures now use `ApiErrorResponse` for missing token, missing tenant, expired token, not found, converted session, unrevalidated cart, and unexpected recovery failures.
- Phone-block submission failure now uses `ApiErrorResponse` instead of an anonymous payload.
- Provider-specific exception details, SQL details, internal exception messages, and stack traces are not returned by the reviewed controllers.

## Authority Matrix

| Concern | API accepts | Backend authority | Controller authority |
|---|---|---|---|
| Tenant | Header/resolved context | Tenant/domain/service predicates | Header admission and mismatch response |
| Owner | Customer claims or guest session header | Cart/session/customer SQL/service predicates | Auth/header/session admission |
| Cart | Cart ID candidate | Backend cart service and live cart snapshot | Delegation only |
| Session | Session ID/token candidate | `CheckoutSessions` and signed token service | Token/session owner admission |
| Address | Address/geography candidate | Checkout/address/geographic admission in runtime | Shape and DTO validation |
| Shipping | Shipping method ID candidate | SQL shipping method and shipping calculation services | Delegation only |
| Payment | Payment method/manual-wallet candidate | Tenant payment method/payment authority | Delegation and response shaping only |
| Pricing | Expected-pricing echo | Backend pricing/tax/coupon/promotion/shipping/payment-fee services | DTO validation and mismatch error shaping |
| Inventory | No accepted inventory input | Inventory/stock reservation authority | None |
| Order | No accepted order identity input | SQL order/idempotency/runtime authority | Response shaping only |
| Recovery | Signed token candidate | Token service, checkout-session SQL, live cart service | Admission and error shaping |

## Regression Coverage

Focused API certification tests were added:

- `PointOfSale.Tests/Architecture/POSAdminCheckout2AdministrationStorefrontApiCertificationTests.cs`

Coverage freezes:

- Storefront checkout/session route consistency.
- Authorization and owner-isolation gates.
- DTO candidate-only admission and no client order identity admission.
- Checkout-session live cart overwrite of client snapshots/totals/currency.
- Controller delegation to runtime/session/cart services.
- Standard `ApiErrorResponse` response shaping for recovery and phone-block failures.
- No domain business logic ownership inside controllers.
- Presence of this certification document.

Existing relevant coverage remains:

- `StorefrontCheckoutAuthorityCertificationTests`
- `StorefrontCheckoutPricingTotalsAuthorityCertificationTests`
- `StorefrontCheckoutSubmissionAuthorityCertificationTests`
- `StorefrontCheckoutAddressAuthorityCertificationTests`
- `CheckoutControllerIdempotencyHeaderTests`
- `CheckoutSessionAuthorizationHotfixTests`

## Verification

Preparation verified:

- Initial repository globbing did not surface `.ai-context` files. A later targeted status check revealed the five authority context files already modified in the worktree; they were reviewed for consistency and not edited in this pass.
- `POSADMIN_CHECKOUT_0_ARCHITECTURE.md` reviewed and treated as frozen.
- `POSADMIN_CHECKOUT_1_RUNTIME_AUTHORITY.md` reviewed and treated as frozen.
- Shipping, payment, inventory, order, storefront, and related authority certification documents present in `docs/architecture` were reviewed as frozen context.

Runtime verification performed in this pass:

- API controllers and DTOs were reviewed directly.
- API response-envelope hardening was limited to controller response shaping.
- Focused API certification tests were added to freeze the reviewed surface.

## Certification Decision

API Certified: YES.

Controllers Certified: YES.

DTOs Certified: YES.

Authorization Certified: YES.

Validation Certified: YES.

Error Model Certified: YES, after the response-envelope hardening in this pass.

Production-certifiable APIs: YES, for the reviewed POS Admin Storefront Checkout API surface.

## Explicit Success Answers

- Reviewed ONLY Storefront Checkout APIs? **Yes.** Affiliate Checkout, CommerceHub Checkout, and POS Sales Checkout were excluded.
- Runtime modified? **No.** Runtime authority and checkout orchestration were not changed.
- Backend modified? **Yes.** Minimal API response-envelope hardening and focused API certification tests only.
- Frontend modified? **No.**
- API Certified? **Yes.**
- Controllers Certified? **Yes.**
- DTOs Certified? **Yes.**
- Authorization Certified? **Yes.**
- Validation Certified? **Yes.**
- Error Model Certified? **Yes.**
- Runtime delegation preserved? **Yes.**
- Business logic inside controllers? **No.** Controllers own HTTP/admission/response shaping only.
- Architecture preserved? **Yes.** POSADMIN-CHECKOUT-0 and POSADMIN-CHECKOUT-1 boundaries remain intact.
- Backward compatible? **Yes.** Status codes and error codes were preserved; response shape is standardized.
- Production-certifiable APIs? **Yes.**
- Recommend next phase: **POSADMIN-CHECKOUT-3 operational observability/API runbook certification**, focused on dashboards, alerting, synthetic checkout probes, replay drills, and support runbooks without changing runtime authority.

## AI Context Decision

No `.ai-context` files were updated.

Reason: the `.ai-context` worktree modifications were pre-existing/unrelated to this pass, and the API hardening did not discover new permanent architectural truth beyond the certification recorded here.
