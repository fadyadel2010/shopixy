# PASS SF-PA5.3 Shipping Method Authority Certification

Date: 2026-06-29

Runtime scope: PointOfSale.Storefront shipping-method runtime only.

Out of scope and not certified here: PointOfSale.CommerceHub, PointOfSale.Web, Affiliate runtime, Marketplace runtime, Payment, Order Submission beyond the shipping-method admission/snapshot boundary, shipment execution, fulfillment, warehouse allocation, carrier integration, and tracking.

## Storefront files reviewed

- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx`
- `PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/store/cartStore.ts`

## Backend endpoints reviewed

- `GET /api/storefront/shipping-methods`
- `POST /api/storefront/checkout/pricing`
- `POST /api/storefront/checkout`
- `POST /api/storefront/checkout-session`
- `PATCH /api/storefront/checkout-session/{id}`
- `GET /api/storefront/checkout-session/recover/{token}`

## Backend services reviewed

- `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs`
- `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`
- `PointOfSale.API/Services/Storefront/IShippingMethodService.cs`
- `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs`
- `PointOfSale.API/Controllers/CheckoutController.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/ICheckoutService.cs`
- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`
- `PointOfSale.API/Controllers/CheckoutSessionController.cs`
- `PointOfSale.API/DTOs/Checkout/CheckoutSessionDtos.cs`
- `PointOfSale.API/Services/Pricing/StorefrontPricingService.cs`
- `PointOfSale.API/Services/Pricing/ShippingCalculationService.cs`

## Findings

Storefront never creates authoritative shipping methods. `ShippingStep` renders the backend method DTO list and calls `setShippingMethod(method.id)` only. `checkoutStore` stores `shippingMethodId` as selected candidate state, not authority, and fetches the available method list from `GET /api/storefront/shipping-methods`.

Shipping Method Identity is backend-owned. Storefront can persist or recover a `shippingMethodId` candidate, but final authority starts only when `CheckoutService.ValidateShippingMethodAsync` loads the method through `LoadShippingMethodAuthorityAsync` with tenant scope, non-deleted SQL state, active state, and carrier availability.

Shipping Eligibility is backend-owned. The shipping-method list endpoint first loads the current backend cart through `ICartService.GetCartAsync`, then `ShippingMethodService.GetShippingMethodsAsync` reads active tenant-scoped shipping projection/cache evidence with tenant validation. Final checkout re-enters SQL and carrier configuration authority, and repeats that check inside the transaction with `lockAuthorityRow: true`.

Shipping Cost is backend-owned. Storefront method cards display `method.price` from the backend shipping-method endpoint, while review and totals display `pricing.shipping` from `POST /api/storefront/checkout/pricing`. Final checkout recomputes shipping through `StorefrontPricingService.CalculateCheckoutAsync` and `ShippingCalculationService`; client `expectedPricing.shipping` is stale-detection evidence only and mismatch returns `CHECKOUT_TOTAL_MISMATCH`.

Shipping ETA is backend-owned. Storefront displays `estimatedDays` from the backend method DTO. Final order snapshot uses `EstimatedDeliveryDays = FormatEstimatedDelivery(shippingMethod)` from the backend-admitted shipping method.

Shipping Admission is backend-owned. The frontend submits `shippingMethodId` in pricing preview and final checkout as a candidate. Backend `CheckoutRequestDto.Validate`, `CheckoutController`, `CheckoutService.ValidateShippingMethodAsync`, carrier availability predicates, authoritative pricing calculation, and final transaction revalidation admit or reject it.

Shipping Snapshot is backend-owned. It begins only when backend order creation/refresh writes `ShippingMethodId`, `ShippingMethodName`, `ShippingCost`, `EstimatedDeliveryDays`, fulfillment mode, and carrier code from backend-admitted shipping/pricing authority.

Recovery converges to backend authority. LocalStorage persists only `shippingMethodId`, payment choice, and step. It does not persist `shippingMethods`, shipping cost, ETA list, or pricing. Recovery can restore a saved method ID candidate from the backend checkout session, but live cart revalidation is mandatory; then Storefront must refetch methods/pricing and final checkout must re-admit the candidate.

Failure fails closed. Invalid, missing, inactive, foreign-tenant, deleted, disabled-carrier, stale-price, cart-owner mismatch, expired-cart, and promotion/pricing failures are rejected by backend 400/401/403/404/409/503 paths. Storefront marks missing restored selections as stale and disables review/submit while pricing or stale selection evidence is invalid.

## Pipeline

Authoritative Checkout Session
-> Authoritative Checkout Address
-> Backend Shipping Eligibility
-> Shipping Methods DTO
-> Shipping Candidate
-> Backend Shipping Admission
-> Checkout Shipping Snapshot
-> Presentation

- Shipping Candidate begins in Storefront when `ShippingStep` calls `setShippingMethod(method.id)` or recovery restores `session.shippingMethod?.id`.
- Shipping Authority begins in backend `CheckoutService.ValidateShippingMethodAsync` / `LoadShippingMethodAuthorityAsync`; for final commit it begins again inside `RevalidateCheckoutAuthorityInsideTransactionAsync` with locked SQL authority.
- Shipping Snapshot begins when backend order creation/refresh persists `ShippingMethodId`, `ShippingMethodName`, `ShippingCost`, and `EstimatedDeliveryDays`.
- Storefront presentation begins after backend DTOs reach `ShippingStep`, `ReviewStep`, or order presentation components.

## Boundary review

- Shipping Boundary: backend SQL shipping method plus backend cart/method DTO admission; recovery source is current backend read/revalidation; failure behavior is withhold/refetch or 400/503.
- Eligibility Boundary: backend active tenant method, current cart subtotal/weight, carrier availability, final SQL revalidation; recovery source is backend cart and SQL; failure behavior is stale selection, 400, or conflict.
- Cost Boundary: backend pricing/shipping calculation services; recovery source is pricing preview/final checkout; failure behavior is no review submit or `CHECKOUT_TOTAL_MISMATCH`.
- Identity Boundary: backend tenant-owned SQL `ShippingMethods.Id`; recovery source is final SQL lookup; failure behavior is `INVALID_SHIPPING_METHOD`.
- Snapshot Boundary: backend order transaction; recovery source is persisted order snapshot after admission; failure behavior is no order snapshot.
- Presentation Boundary: Storefront components render backend DTOs and formatted backend pricing only; recovery source is refetch; failure behavior is stale/empty/error UI.
- Recovery Boundary: checkout-session recovery plus live cart revalidation plus method/pricing refetch plus final checkout re-admission; failure behavior is cart review or stale selection.

## Runtime hardening performed

No runtime code change was required. The current Storefront shipping-method runtime already treats selected/restored IDs as candidates, uses backend DTOs for method list/ETA, uses backend pricing preview/final checkout for cost, and relies on backend final checkout for admission and immutable snapshot.

## Exact runtime files modified

None.

## Regression freezes added

- `PointOfSale.Tests/Architecture/StorefrontShippingMethodAuthorityCertificationTests.cs`

## Architecture documentation updates

- `docs/architecture/storefront-shipping-method-authority-certification-findings.md`

## AI Context updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit certification answers

- Did you review only PointOfSale.Storefront runtime? Yes, with backend review limited to Storefront shipping endpoints/services directly consumed by that runtime.
- Which frontend files were modified? None.
- Which backend files were modified? None.
- Who owns Shipping Method Identity? Backend SQL shipping-method authority.
- Who owns Shipping Eligibility? Backend shipping method, cart, tenant, and carrier authority.
- Who owns Shipping Cost? Backend pricing and shipping calculation services.
- Who owns Shipping ETA? Backend shipping-method DTO/order snapshot authority.
- Who owns Shipping Admission? Backend checkout service.
- Who owns Shipping Snapshot? Backend order transaction.
- Can Storefront frontend invent shipping methods? No.
- Can frontend calculate shipping costs? No authoritative calculation; it only formats backend values.
- Can stale Shipping survive recovery? No as authority; recovered IDs are candidates and must re-enter backend method/pricing/final checkout authority.
- Can LocalStorage become Shipping Authority? No.
- Can Redis become Shipping Authority? No; cache/projection evidence is tenant-validated for list display and final checkout re-enters SQL authority.
- Where does Shipping Candidate begin? In `ShippingStep`/`checkoutStore.shippingMethodId` or recovery-restored `shippingMethodId`.
- Where does Shipping Authority begin? In backend `ValidateShippingMethodAsync` / `LoadShippingMethodAuthorityAsync`, repeated inside final transaction.
- Where does Shipping Snapshot begin? In backend order creation/refresh persistence of shipping method, cost, ETA, fulfillment mode, and carrier code.
- Where does Storefront presentation begin? After backend shipping/pricing/order DTOs reach Storefront components.
- Were runtime changes required? No.
- Is PointOfSale.Storefront Shipping Method Authority production-certifiable? Yes.
