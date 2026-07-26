# PASS SF-PA5.2 Checkout Address Authority Certification

Date: 2026-06-29

PointOfSale.Storefront Checkout Address Authority is production-certifiable for the inspected
Storefront checkout-address runtime. Storefront frontend owns address candidates, form state,
loading/error presentation, validation hints, step navigation, and temporary UI state only. Backend
`CheckoutController`, `CheckoutService`, `CheckoutRequestDto.Validate`, `AddressDto.Validate`,
current tenant/customer/session context, phone risk/OTP checks, final checkout transaction
revalidation, and SQL `Orders` own Checkout Address admission, address validation, customer/cart
ownership binding, and immutable checkout/order address snapshots.

## Storefront Files Reviewed

- `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx`
- `PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/services/api/httpClient.ts`

## Backend Endpoints Reviewed

- `POST /api/storefront/checkout`
- `POST /api/storefront/checkout/pricing`
- `GET /api/storefront/checkout-session/recover/{token}`
- `POST /api/storefront/checkout/phone-verification-status`
- `POST /api/storefront/checkout/request-phone-otp`
- `POST /api/storefront/checkout/verify-phone-otp`

## Backend Services Reviewed

- `PointOfSale.API/Controllers/CheckoutController.cs`
- `PointOfSale.API/Controllers/CheckoutSessionController.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`
- `PointOfSale.API/DTOs/Checkout/CheckoutSessionDtos.cs`
- `PointOfSale.Core/Entities/CheckoutSession.cs`

## Findings

Address findings: Storefront freeform address fields begin as candidate form state in
`ShippingStep`; `checkoutStore` maps them into `shippingAddress` for final checkout. The frontend
does not accept or submit `addressId`, saved-address identity, default-address authority, or
address-book ownership evidence. Backend final checkout admits the candidate through
`CheckoutRequestDto.Validate`, `AddressDto.Validate`, `CheckoutService.ProcessCheckoutAsync`, phone
risk/OTP checks, current cart/customer/session ownership, pricing revalidation, and the final
transaction before persisting SQL order address fields.

Saved address findings: No saved-address or default-address selection exists in the inspected
PointOfSale.Storefront checkout-address runtime. Because the runtime submits no saved-address ID,
the frontend cannot select another customer's saved address. Any future saved-address feature must
add backend-owned identity and ownership checks before presentation or submission.

Validation findings: Storefront validation is UX-only. Backend validation requires recipient name,
phone, street, city, and country, normalizes country to ISO alpha-2, validates billing address when
present, checks merchant phone blocking, and enforces OTP where tenant policy requires it.
Governorate/state and postal/ZIP are optional fields in the current Storefront checkout contract;
they remain candidate evidence unless future backend tenant policy makes them mandatory.

Recovery findings: Browser refresh may restore draft address form state from `sessionStorage`, but
that state is not authority and is not persisted in localStorage. Checkout recovery can restore only
a backend checkout-session address candidate after signed tenant-bound token validation and live cart
revalidation. Final checkout must resubmit the restored address candidate through backend admission.

Failure findings: 400/401/403/404/409/422/503, expired sessions, invalid address candidates,
inactive customers, blocked phones, unverified phones, stale pricing, and unavailable checkout/cart
authority fail closed before SQL order address persistence. Failure may preserve editable candidate
form state for retry, but cannot create a checkout address snapshot.

## Checkout Address Pipeline

Checkout Session

↓

Address Candidate begins in `ShippingStep` and `checkoutStore.address`.

↓

Backend Address Validation begins in `CheckoutController.Checkout` and `CheckoutRequestDto.Validate`.

↓

Checkout Address Authority begins in `CheckoutService.ProcessCheckoutAsync` after current
tenant/customer/session/cart ownership and address validation pass.

↓

Checkout Address Snapshot begins when `CheckoutService` creates the SQL `Order` and sets
`GuestPhone`, `GuestName`, `ShippingAddress`, `ShippingCity`, `ShippingState`, `ShippingZipCode`,
`ShippingCountry`, and billing address fields.

↓

Checkout DTO / Order Events

↓

Storefront presentation begins only after accepted backend checkout/order DTOs return.

## Boundary Review

- Address Boundary: backend owns admission; recovery source is current checkout/order service
  admission; failure rejects with validation or checkout errors before persistence.
- Identity Boundary: backend owns customer/session/cart owner identity; recovery source is current
  authenticated Storefront JWT or exact guest session; failure returns 401/403/404 or tenant mismatch.
- Validation Boundary: backend owns business validation; recovery source is request re-entry through
  `AddressDto.Validate` and checkout service guards; failure returns 400/409/422-style checkout
  errors.
- Snapshot Boundary: backend SQL order creation owns the address snapshot; recovery source is SQL
  order/session authority; failure creates no order address snapshot.
- Presentation Boundary: Storefront owns rendering and editable form state only; recovery source is
  backend DTOs or empty form defaults; failure withholds or keeps candidate state for retry.
- Recovery Boundary: signed checkout recovery plus live cart revalidation owns recovery admission;
  stored browser state and Redis bytes outside backend service admission are candidates only.

## Runtime Hardening Performed

No runtime code change was required for SF-PA5.2. Regression freezes and authority documentation
were added to preserve the current backend-owned address admission model.

## Exact Runtime Files Modified

None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StorefrontCheckoutAddressAuthorityCertificationTests.cs`

## Architecture And AI Context Updates

- `docs/architecture/storefront-checkout-address-authority-certification-findings.md`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Did you review only PointOfSale.Storefront runtime? Yes. Backend review was limited to endpoints
  and services directly consumed by PointOfSale.Storefront checkout-address runtime.
- Which frontend files were modified? None.
- Which backend files were modified? None.
- Who owns Checkout Address? Backend checkout admission and SQL order snapshot.
- Who owns Address Identity? Backend; no Storefront saved-address identity exists in this runtime.
- Who owns Address Admission? Backend `CheckoutController` and `CheckoutService`.
- Who owns Saved Address ownership? Backend must own it; no saved-address Storefront runtime exists
  in this pass.
- Who owns Default Address? Backend must own it; no default-address Storefront runtime exists in this
  pass.
- Can Storefront frontend create Checkout Addresses? No. It submits address candidates only.
- Can frontend validate business address ownership? No.
- Can stale Address survive recovery? It can survive only as editable candidate form state; it cannot
  become a checkout address snapshot without backend admission.
- Can LocalStorage become Address Authority? No. Address is not persisted in the localStorage
  partialized checkout state.
- Can Redis become Address Authority? No. Redis/cart data must re-enter backend services and cannot
  admit checkout addresses.
- Where does Address Candidate begin? `ShippingStep` and `checkoutStore.address`.
- Where does Checkout Address Authority begin? `CheckoutService.ProcessCheckoutAsync` after request,
  owner, cart, address, pricing, phone, and transaction revalidation.
- Where does Checkout Address Snapshot begin? SQL `Order` address fields created by
  `CheckoutService`.
- Where does Storefront presentation begin? After backend DTO admission reaches Storefront checkout
  and order presentation.
- Were runtime changes required? No.
- Is PointOfSale.Storefront Checkout Address production-certifiable? Yes.
