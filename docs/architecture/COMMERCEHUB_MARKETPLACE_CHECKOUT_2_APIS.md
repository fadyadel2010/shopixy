# COMMERCEHUB-MARKETPLACE-CHECKOUT-2 - Marketplace API Certification

Date: 2026-07-09
Scope: CommerceHub Marketplace API surface only
Status: API CERTIFIED

## Executive Summary

CommerceHub Marketplace APIs were reviewed only as planning, orchestration, presentation, analytics, and audit APIs around the already-certified Reference Checkout APIs.

No true Marketplace API inconsistency was found. Runtime code, backend production code, frontend code, checkout execution, payment execution, shipping execution, order execution, shipment execution, inventory reservation, carrier execution, and `.ai-context` files were not modified.

Certification decision: Marketplace APIs are certified as API boundaries that reuse the certified CommerceHub checkout, cart, shipping, payment, order, inventory, warehouse, settlement, analytics, and audit authorities. No Marketplace Checkout execution API, Marketplace Payment API, Marketplace Shipping API, Marketplace Shipment API, or duplicate order-creation API was introduced.

## API Inventory

Reviewed Marketplace Checkout API-adjacent surface:

| API Surface | Route | Certification Result |
|---|---|---|
| Certified checkout submission | `POST /api/commercehub/checkout` | Reused checkout API; authenticated, idempotency-keyed, controller delegates to `ICommerceHubCheckoutService`. |
| Certified cart state | `GET /api/commercehub/cart` | Reused cart API; controller delegates to `ICommerceHubCartService`. |
| Certified cart upsert | `POST /api/commercehub/cart` | Reused cart API; request DTO validation then service delegation. |
| Certified cart clear | `DELETE /api/commercehub/cart` | Reused cart API; service delegation. |
| Certified split preview/pricing | `POST /api/commercehub/cart/price` | Reused pricing/preview API; controller delegates to `ICommerceHubCartService.CalculateAsync`. |
| Marketplace product reads | `GET /api/read/products`, `GET /api/read/products/{productId}` | Read-model presentation only; no checkout execution authority. |
| Marketplace homepage reads | `GET /api/read/homepage` | Read-model presentation only. |
| Marketplace customer order reads | `GET /api/read/orders` | Order read presentation only; order execution excluded from this phase. |

Excluded by scope: POS Admin Checkout APIs, Affiliate APIs, Payment APIs, Shipping APIs, Order APIs, provider APIs, carrier APIs, and shipment APIs.

## Controller Review

`CommerceHubCheckoutController` is the only checkout submission controller in scope. It remains a thin HTTP boundary:

- Validates request presence and DTO admission.
- Requires customer authentication.
- Requires an `Idempotency-Key`.
- Applies checkout and promo rate limits.
- Delegates durable behavior to `ICommerceHubCheckoutService.CheckoutAsync`.
- Returns the unified `ApiErrorResponse` envelope for validation, authorization, conflict, rate-limit, and unexpected failures.

`CommerceHubCartController` remains the split preview and cart-planning API boundary:

- Delegates cart load, save, clear, and pricing preview to `ICommerceHubCartService`.
- Accepts shipping selections and expected prices only as candidates or stale-detection evidence.
- Uses `ApiErrorResponse` for validation and service failures.
- Does not create orders, execute payments, create shipments, reserve inventory directly, or execute carrier calls.

No `MarketplaceCheckoutController`, `MarketplacePaymentController`, `MarketplaceShippingController`, `MarketplaceShipmentController`, or `api/marketplace/...` checkout/payment/shipping execution route exists in the inspected production source.

## DTO Review

Marketplace DTOs reviewed under `PointOfSale.API/DTOs/Marketplace` and marketplace review DTOs remain read/presentation evidence: product identity, category, language, name, price display, currency, rating, vendor display, images, availability, homepage sections, and read-model order presentation.

Checkout DTOs under `PointOfSale.API/DTOs/CommerceHub` remain the certified checkout API DTOs, not new Marketplace DTOs. They carry:

- Customer and cart version candidates.
- Store partitions.
- Item identity, quantity, and expected unit price candidates.
- Shipping selection candidates for every store.
- Payment method candidate constrained to certified checkout-supported methods.
- Checkout response evidence emitted by the certified checkout runtime.

DTOs do not give Marketplace authority over checkout totals, payment truth, shipping truth, inventory truth, order truth, shipment truth, carrier execution, provider secrets, or internal exception data.

## Authority Matrix

| Concern | Owner | Marketplace API Role |
|---|---|---|
| Marketplace context | Marketplace | Context evidence and presentation only. |
| Merchant grouping | Marketplace planning / certified cart service | Groups by current store/merchant evidence for planning. |
| Merchant partition planning | Marketplace planning / certified cart service | Store-scoped partition preview. |
| Split preview | Certified cart/pricing API | Preview/stale-detection evidence before checkout commit. |
| Merchant summary | Marketplace read/presentation | Display evidence only. |
| Marketplace notices | Marketplace presentation | Informational evidence only. |
| Settlement candidates | Marketplace/financial planning | Candidate evidence; ledger settlement remains finance authority. |
| Commission candidates | Marketplace/financial planning | Candidate evidence; final snapshots remain checkout/financial authority. |
| Marketplace analytics | Marketplace analytics | Derivative analytics only. |
| Marketplace audit | Marketplace audit | Append-only marketplace decision evidence. |
| Checkout submission | CommerceHub checkout / Reference Checkout | Reused only. |
| Checkout recovery | Certified checkout/idempotency authority | Reused only. |
| Checkout pricing | Certified checkout/cart pricing authority | Reused only. |
| Payment execution | CommerceHub payment authority | Reused only; Marketplace owns none. |
| Shipping execution | Shipping Platform / CommerceHub shipping | Reused only; Marketplace owns none. |
| Shipment creation | Shipment authority | Reused only; Marketplace owns none. |
| Inventory reservation | Inventory authority inside checkout | Reused only; Marketplace owns none. |
| Order creation | CommerceHub checkout/order authority | Reused only; Marketplace owns none. |
| Carrier execution | Shipping Platform carrier adapters | Marketplace owns none. |

## Reuse Matrix

| Required Reuse | Result |
|---|---|
| Certified Checkout APIs | Reused through `/api/commercehub/checkout` and `ICommerceHubCheckoutService`. |
| Certified Cart/Split Preview APIs | Reused through `/api/commercehub/cart` and `/api/commercehub/cart/price`. |
| Certified Shipping APIs/services | Reused through `CommerceHubShippingService` and shipping assignment boundaries. |
| Certified Payment APIs/services | Reused through `CommerceHubPaymentService` and payment lifecycle authority. |
| Certified Order APIs/services | Reused as order read/management authority; no duplicate order creation endpoint. |
| Certified Inventory authority | Reused inside checkout transaction. |
| Certified Settlement/Commission authority | Reused as financial workflow and ledger authority. |

## Security Review

Marketplace spoofing and merchant spoofing are blocked by current backend authority re-entry. The checkout runtime reloads listing, product, variant, tenant, vendor, payment method, cart, and shipping evidence before durable effects. Merchant partitions are store scoped, and cross-store listing/product/variant mismatch fails closed.

Tenant isolation remains backend-owned through authenticated customer/session context, trusted tenant context where applicable, current SQL predicates, vendor visibility/governance checks, and customer-specific cache keys on order reads.

Replay protection remains checkout-owned through customer authentication, request-hash-bound idempotency, checkout locks, persisted idempotency records, cart version validation, and serializable transaction boundaries.

DTO validation remains fail closed for missing customer, invalid phone/address, unsupported payment method, missing cart version, empty stores, missing shipping selections, invalid item identity, invalid quantity, missing expected unit price, invalid shipping option identity, negative shipping prices, and store/shipping-selection mismatch.

No provider leakage, provider secrets, payment client secrets, SQL errors, or internal exception payloads were found in the Marketplace Checkout API certification surface. Controllers log internal details server-side and return stable error codes/messages.

## Regression Coverage

Focused certification coverage added:

- `PointOfSale.Tests/Architecture/CommerceHubMarketplaceCheckoutApiCertificationTests.cs`

The test suite freezes:

- Marketplace controller delegation to certified checkout/cart services.
- Merchant grouping and split preview as planning over store-scoped evidence.
- Checkout API reuse through certified CommerceHub checkout and cart endpoints.
- Absence of duplicate Marketplace checkout/payment/shipping/shipment execution endpoints.
- Authorization, idempotency, DTO admission, and unified `ApiErrorResponse` usage on the certified checkout/cart API surface.
- Absence of Marketplace DTO authority over checkout, payment, shipping, inventory, order, shipment, carrier, or provider-secret truth.
- Required certification documentation sections.

Existing reused certification coverage remains part of the basis:

- `CommerceHubMarketplaceCheckoutRuntimeCertificationTests`
- `CommerceHubCheckoutCertificationTests`
- `CommerceHubCheckoutIdempotencyArchitectureTests`
- `CommerceHubMarketplaceOrderAuthorityCertificationTests`
- `MarketplaceSplitOrderAuthorityCertificationTests`
- `ShippingSplitOrderAuthorityCertificationTests`
- `ShippingMethodSelectionAuthorityCertificationTests`
- `ShippingCostAuthorityCertificationTests`
- `CommerceHubPaymentAuthorityCertificationTests`
- `CommerceHubSettlementCertificationTests`

## Certification Decision

Marketplace APIs are certified.

Marketplace APIs remain planning, orchestration, presentation, analytics, and audit APIs. Durable checkout effects enter only the certified CommerceHub checkout/cart APIs and the already-certified checkout, shipping, payment, order, inventory, warehouse, shipment, settlement, commission, analytics, and audit authorities.

No `.ai-context` update was required because no new permanent cross-platform architecture truth was discovered.

## Success Answers

- Reviewed ONLY Marketplace APIs? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No production backend code modified. Certification tests and documentation only.**
- Frontend modified? **No.**
- Marketplace APIs Certified? **Yes.**
- Merchant Grouping APIs Certified? **Yes.**
- Split Preview APIs Certified? **Yes.**
- Checkout APIs Reused? **Yes.**
- Shipping APIs Reused? **Yes.**
- Payment APIs Reused? **Yes.**
- Business logic inside controllers? **No. Controllers delegate to certified services.**
- Checkout authority introduced? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable APIs? **Yes, for the inspected Marketplace API planning/reuse surface; operational rollout evidence remains inherited from dependent certified runtimes.**
- Recommend next phase: **CommerceHub Marketplace Checkout frontend/UX certification for merchant grouping, split preview, merchant summaries, marketplace notices, analytics, and audit presentation while preserving certified checkout, payment, shipping, order, inventory, warehouse, shipment, and settlement reuse.**
