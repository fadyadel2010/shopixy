# POSADMIN-CHECKOUT-0 - Storefront Checkout Architecture Certification

Date: 2026-07-08  
Scope: POS Admin Storefront Checkout only  
Status: ARCHITECTURE CERTIFIED  
Runtime impact: none  
Backend impact: none  
Frontend impact: none

## Executive Summary

POS Admin Storefront Checkout was reviewed as an architecture and authority certification only. The review covered the current Storefront cart-to-order checkout pipeline, checkout session/recovery path, pricing preview path, DTO/API ownership, frontend ownership, backend ownership, and integrations with Shipping, Payment, Coupon, Warehouse, Inventory, Order, Geography, Customs, Carrier Mapping, Currency, Totals, Fraud/Risk, and Audit authorities.

The review was limited to POS Admin Storefront Checkout. Affiliate Checkout, CommerceHub Marketplace Checkout, and POS Sales Checkout were not certified by this document.

No true architecture inconsistency was found. Runtime code, backend behavior, and frontend behavior were not modified. This document is the only change for POSADMIN-CHECKOUT-0.

Certification decision: POS Admin Storefront Checkout architecture is production-certifiable as an authority-preserving architecture. Operational production readiness still depends on the frozen operational gates from Shipping, FedEx/carriers, Geography, Customs, Payment, Coupon, Inventory, and Order certifications.

## Architecture Overview

POS Admin Storefront Checkout is the tenant storefront checkout path exposed through `POST /api/storefront/checkout`, `POST /api/storefront/checkout/pricing`, and checkout-session endpoints. The frontend collects customer intent and renders backend evidence. Backend checkout admission owns final checkout authority by re-entering the current domain authorities before order creation.

Certified architecture:

```text
Storefront cart/session intent
  -> backend checkout/session admission
  -> live cart revalidation
  -> customer/session ownership validation
  -> address and optional Geographic admission
  -> shipping method candidate admission
  -> payment method candidate admission
  -> backend pricing/tax/discount recalculation
  -> expected-pricing mismatch guard
  -> serializable order transaction
  -> inventory reservation
  -> coupon/promotion reservation
  -> immutable order snapshots
  -> payment intent/session handoff
  -> idempotency/audit/projection events
```

Checkout does not own Shipping runtime, Shipment lifecycle, Carrier Mapping publication, Geographic catalog identity, Customs evidence publication, Payment lifecycle truth, Inventory authority, Warehouse authority, Coupon authority, Promotion authority, or payment settlement. Checkout consumes their admitted evidence and snapshots the accepted checkout/order facts.

## Checkout Pipeline

1. Storefront initializes or resumes a checkout session using current tenant, customer/session owner, and live cart authority.
2. Storefront collects address, shipping method candidate, payment method candidate, notes, optional coupon/cart state, and expected pricing evidence.
3. Backend validates tenant/customer/session/cart ownership and checkout idempotency.
4. Backend revalidates live cart and product/variant eligibility from SQL authority.
5. Backend admits canonical geographic candidates through `IAddressAdmissionService` when supplied.
6. Backend validates shipping method from SQL `ShippingMethods` and enabled carrier configuration without invoking shipment registration.
7. Backend validates payment method from SQL `TenantPaymentMethods` and configuration policy.
8. Backend recalculates subtotal, discounts, shipping, payment fee, taxes, currency, and total.
9. Backend rejects stale or forged expected pricing.
10. Backend opens a serializable SQL transaction for final revalidation and order creation.
11. Backend revalidates storefront availability, cart/pricing, shipping/payment authority, affiliate evidence when present, and coupon/promotion authority.
12. Backend reserves inventory through variant stock authority and binds reservations to the order.
13. Backend persists immutable order, order item, address, shipping, payment, pricing, currency, coupon, promotion, geography, and fulfillment ownership snapshots.
14. Backend creates payment intent/session evidence through the Payment authority boundary.
15. Backend stores durable idempotency and emits audit/projection/outbox evidence.

## Authority Matrix

| Concern | Authoritative owner | Checkout role | Forbidden checkout authority |
|---|---|---|---|
| Cart | Backend cart service and tenant/customer/session ownership | Reads/revalidates current cart snapshot before checkout | Browser cart, stale recovery JSON, DTO totals |
| Checkout Session | `CheckoutSessionController`, `CheckoutSessionService`, SQL `CheckoutSessions` | Owns session identity, lifetime, recovery token, candidate autosave | Frontend/session storage as authority |
| Customer Identity | Storefront auth claims, tenant/customer SQL ownership, guest session header | Resolves owner and fails closed on mismatch | `X-Customer-Id` spoofing by untrusted caller |
| Shipping Address | Backend checkout address validation and order snapshot | Admits candidate and persists snapshot | Frontend form or recovery as final address truth |
| Billing Address | Backend checkout address validation and order snapshot | Uses billing candidate or shipping fallback, then snapshots | Frontend-only validation |
| Shipping Method | SQL `ShippingMethods` plus carrier config eligibility | Admits selected method and snapshots name/cost/fulfillment mode | Frontend selector, carrier adapter, cache |
| Carrier Selection | Shipping Method/Shipping Platform configuration | Captures method/carrier snapshot only | Dynamic carrier routing or provider choice at checkout |
| Shipping Cost | Backend pricing/shipping calculation | Recalculates and snapshots cost | Client expected pricing, UI-calculated cost |
| Taxes | Backend tax calculation service and tenant settings | Recalculates and snapshots tax | Frontend country text as tax authority |
| Coupons | Coupon SQL/services | Revalidates and reserves through coupon authority | Coupon code or cart discount as final truth |
| Promotions | Promotion engine/services | Recalculates/reserves accepted promotion evidence | Frontend promotion display or stale cart calculation |
| Gift Cards | Not present in certified checkout path | Future extension only | No current authority |
| Payment Method | SQL `TenantPaymentMethods` and Payment configuration validation | Admits method candidate and snapshots payment method/fee | Frontend code or payment selector as authority |
| Payment Authorization | Payment authority/services/provider evidence | Creates payment intent/session handoff after order commit | Checkout marking paid or recognizing money |
| Order Creation | SQL `Orders`, `OrderItems`, idempotency, serializable transaction | Creates Storefront order under final revalidation | Frontend order identity/number |
| Inventory Reservation | ProductVariantStocks / inventory reservation authority | Reserves variant stock transactionally with order | Checkout-owned inventory truth |
| Stock Validation | Variant stock authority and product/variant SQL state | Validates availability before reservation | Mongo projection or stale cart stock |
| Warehouse Resolution | Warehouse domain | Not resolved by Storefront Checkout | Checkout choosing warehouse origin |
| Shipping Registration | Shipping Platform / Shipment runtime | Not performed by checkout; order snapshots fulfillment ownership | Checkout owning shipment registration |
| Split Order ownership | Not part of POS Admin Storefront Checkout | None | Marketplace split-order authority |
| Customs admission | Customs publication/admission authority | Not admitted by checkout; future shipment/customs phase consumes evidence | Checkout owning customs truth |
| Carrier Mapping | Geographic/Carrier Mapping published releases and Shipping Platform snapshots | Not resolved by checkout | Checkout resolving provider IDs |
| Currency | Tenant/storefront/backend currency authority | Validates cart/order currency and snapshots | Client currency or checkout-session currency field |
| Totals | Backend pricing service and final transaction revalidation | Recomputes and snapshots totals | Expected-pricing DTO as authority |
| Fraud validation | Phone risk, phone OTP, order velocity signals where present | Blocks or records risk signals through backend services | Frontend fraud decisions |
| Audit | SQL order/session/idempotency/outbox/audit evidence | Emits checkout/order/payment/coupon/inventory evidence | Frontend logs as audit truth |

## Ownership Matrix

| Layer | Owns | Does not own |
|---|---|---|
| Storefront frontend | Candidate entry, step state, loading/error presentation, retry intent, formatting, navigation | Cart authority, checkout session authority, pricing, taxes, payment authority, order creation, inventory, shipment, geography, customs |
| Checkout API | Request admission, owner validation, no-store DTO responses, error boundary, idempotency entry | Provider payment truth, shipment lifecycle, carrier adapter transport, geographic publication |
| Checkout service | Checkout orchestration, final revalidation, order creation coordination, snapshots, idempotency result | Domain source truth for payment, inventory, shipping platform, customs, geography, carrier mapping |
| Cart domain | Cart identity, cart lines, cart owner, cart snapshot source | Order creation and payment authorization |
| Order domain | Order identity, order number, order lifecycle snapshot, order item snapshots | Payment collection truth, shipment truth, inventory ledger truth |
| Payment domain | Payment method truth, payment intent/session, provider verification, payment lifecycle | Order line construction, shipment lifecycle, inventory reservation |
| Shipping domain | Shipping method authority, later shipment registration/lifecycle | Checkout cart/order authority, payment truth |
| Geography domain | Canonical place identity and address admission | Checkout session, order creation, shipment lifecycle |
| Customs domain | Published customs evidence/admission | Checkout address/cart/order authority |
| Inventory domain | Stock validation, reservation, release/commit lifecycle | Checkout totals or payment status |
| Warehouse domain | Warehouse origin/address lifecycle | Storefront checkout selection in current path |

## Runtime Boundaries

Runtime was reviewed but not modified.

Certified runtime boundaries:

| Boundary | Certification |
|---|---|
| Checkout session runtime | Live backend cart is required for create/resume/update/recovery authority. Client item snapshots, totals, and currency are ignored for authority. |
| Checkout submission runtime | Backend creates order only after tenant/owner/cart/idempotency admission and final serializable revalidation. |
| Pricing runtime | Backend recalculates subtotal, discounts, shipping, tax, payment fee, and total; expected pricing is a mismatch guard only. |
| Address runtime | Backend validates address candidates and optionally admits geographic candidates through SQL Geographic authority. |
| Payment runtime | Checkout creates/requests payment intent evidence; payment lifecycle remains owned by Payment authority. |
| Inventory runtime | Variant stock authority validates and reserves; Redis reservation data is diagnostic/derivative. |
| Recovery runtime | Recovery re-enters signed checkout-session token and live cart authority or fails closed. |
| Idempotency runtime | Durable checkout idempotency and active-cart duplicate guards prevent duplicate orders. |

## Frontend Boundaries

The Storefront frontend owns presentation and candidates only:

| Frontend concern | Certified ownership |
|---|---|
| Checkout stepper/state | Presentation and navigation only. |
| Address form | Candidate state only. |
| Shipping method selection | Candidate ID selected from backend DTOs only. |
| Payment method selection | Candidate code selected from backend DTOs only. |
| Pricing display | Backend pricing DTO rendering only. |
| Expected pricing echo | Client echo used only for backend mismatch detection. |
| Payment handoff display | Admitted backend payment session evidence only. |
| Success/pending pages | Backend order/payment status presentation only. |
| Browser storage | Candidate recovery only; no pricing/order/payment authority. |

No frontend business logic was found that owns checkout, cart, shipping, payment, geography, customs, inventory, or order authority.

## Backend Boundaries

Backend checkout owns orchestration and final admission, not every downstream domain truth.

| Backend concern | Certified boundary |
|---|---|
| API ownership | `CheckoutController` and `CheckoutSessionController` own Storefront checkout API admission and no-store response boundaries. |
| DTO ownership | Checkout DTOs carry candidates and backend evidence. DTO totals/payment/shipping/address inputs are not source truth. |
| Snapshot ownership | SQL order/session/idempotency snapshots are backend-owned and emitted only after admission. |
| Transaction ownership | Order creation is serializable, revalidates authority, and rolls back on pre-commit failures. |
| Projection ownership | Mongo/Redis/read models are derivative; SQL/domain services are re-entered for authority. |
| Error ownership | Fail-closed typed errors prevent stale selection, stale totals, stock conflicts, owner mismatch, and unavailable authorities from producing orders. |

## Shipping Integration

Checkout integrates with Shipping only at the shipping-method candidate and order snapshot boundary.

Certified rules:

| Shipping concern | Certification |
|---|---|
| Shipping method | Selected `ShippingMethodId` is validated against tenant-owned SQL `ShippingMethods`. |
| Carrier availability | Non-manual carrier methods require enabled tenant carrier configuration. |
| Shipping cost | Recalculated by backend pricing/shipping calculation and persisted on order. |
| Fulfillment mode | Snapshot derived at checkout from admitted shipping method/carrier code. |
| Shipment identity | Not owned or created by checkout. |
| Carrier registration | Not performed by checkout. Shipping Platform owns later registration through shipment snapshots. |
| Carrier Mapping | Not resolved by checkout. Shipping Platform consumes published mapping snapshots later. |
| Shipping authority leakage | None found. Checkout does not mutate Shipment lifecycle, tracking, carrier registration, labels, provider transport, or mapping publication. |

## Payment Integration

Checkout integrates with Payment by validating payment-method candidates and creating payment intent/session evidence after order creation.

Certified rules:

| Payment concern | Certification |
|---|---|
| Payment method | SQL `TenantPaymentMethods` and configuration validation are authoritative. |
| Payment fee | Backend pricing calculates fee. |
| Payment amount/currency | Derived from order/pricing/currency authority and revalidated by Payment authority. |
| Payment authorization | Payment services/provider integrations own provider intent/session evidence. |
| Payment success | Not owned by checkout. Provider/manual/COD success must re-enter Payment lifecycle authority. |
| Manual wallet | Checkout submits proof evidence only; backend payment authority validates and later admin/payment lifecycle approves or rejects. |
| COD | Checkout can create pending payment/fulfillment intent but does not recognize collected money. |
| Payment authority leakage | None found. Checkout does not forge payment success, settlement, refund, ledger, or provider verification. |

## Coupon Integration

Checkout consumes coupon candidates from backend cart/coupon state and revalidates/reserves through Coupon authority.

Certified rules:

| Coupon concern | Certification |
|---|---|
| Coupon code | Candidate only until backend coupon authority validates. |
| Coupon discount | Recalculated by backend pricing and coupon services. |
| Coupon reservation | Created transactionally during checkout order creation. |
| Coupon usage | Payment/order lifecycle confirms or releases under Coupon authority. |
| Coupon projections | Derivative only; not checkout truth. |
| Coupon leakage | None found. Checkout does not create coupon lifecycle, eligibility, or audit authority. |

## Warehouse Integration

Current POS Admin Storefront Checkout does not resolve warehouse origin or assign fulfillment warehouse. Warehouse remains a separate authority for operational locations, admitted warehouse address versions, and future shipment origin snapshots.

Certified rules:

| Warehouse concern | Certification |
|---|---|
| Warehouse resolution | Outside current checkout runtime. |
| Warehouse address | Not mutated by checkout. |
| Shipment origin | Owned by Warehouse/Shipping Platform later, not checkout. |
| Future extension | Warehouse routing may be added only behind a backend warehouse/inventory authority boundary and snapshot before shipment registration. |

## Inventory Integration

Checkout integrates with Inventory for stock validation and reservation only.

Certified rules:

| Inventory concern | Certification |
|---|---|
| Stock validation | Variant stock authority is used; product/variant SQL state is revalidated. |
| Reservation | ProductVariantStocks reservation is performed transactionally with order creation. |
| Backorder/non-tracked policy | Product/variant SQL policy controls whether reservation is required. |
| Release/compensation | Backend inventory reservation release is invoked on failed pre-commit paths. |
| Inventory ownership | Checkout does not own inventory truth; it consumes inventory authority and records order-bound reservation evidence. |

## Order Integration

Checkout creates Storefront orders under Order authority and snapshots admitted facts.

Certified rules:

| Order concern | Certification |
|---|---|
| Order identity | Server-generated SQL order identity and order number. |
| Order items | Immutable SKU/variant snapshots from backend authority. |
| Address snapshots | Backend-admitted shipping/billing address snapshots, including geographic evidence when present. |
| Shipping/payment snapshots | Accepted shipping/payment method name/code/cost/fee snapshots. |
| Financial snapshots | Backend subtotal, discounts, tax, shipping, payment fee, total, currency, and pricing JSON snapshots. |
| Duplicate prevention | Durable idempotency, locks, active-cart order guard, and serializable transaction. |
| Order lifecycle | Checkout creates the initial order/payment-pending state; later lifecycle remains Order/Payment/Fulfillment authority. |

## Future Extension Points

Future phases must preserve the certified authority boundaries:

| Extension | Required authority boundary |
|---|---|
| Gift cards | Dedicated backend gift-card authority, reservation, redemption, release, audit, and order snapshot. |
| Saved addresses | Backend customer address ownership/admission before presentation or submission. |
| Warehouse routing | Backend Warehouse/Inventory authority with immutable routing snapshot before fulfillment/shipping. |
| Shipment creation at checkout | Must enter Shipping Platform, create shipment snapshots, resolve carrier mapping, admit customs if needed, and avoid checkout-owned shipment truth. |
| Customs-aware checkout | Checkout may collect candidates/evidence only; Customs authority must admit published evidence before shipment registration. |
| Canonical-only geography rollout | Continue SQL Geographic admission and first-class immutable order/shipment geographic snapshots. |
| Fraud/risk scoring | Backend risk authority must emit block/review evidence; frontend remains presentation only. |
| Split orders | Separate marketplace/commerce architecture; not part of POS Admin Storefront Checkout. |
| Multi-currency | Tenant/payment/order currency authority must remain backend-owned and fail closed on mismatch. |

## Certification Decision

Architecture Certified: YES.

No true architecture inconsistency was found in the current POS Admin Storefront Checkout architecture. Authority boundaries are preserved. Runtime ownership is backend/domain-owned. Frontend ownership is presentation/candidate-only. API ownership is Storefront checkout API admission. DTOs carry candidates and admitted evidence but do not own business truth. Snapshots are backend SQL/order/session snapshots. No duplicated business logic requiring correction was found in the reviewed architecture.

Production-certifiable architecture: YES, for POS Admin Storefront Checkout architecture. Operational production readiness remains subject to the separate frozen operational gates for Shipping Platform/carriers, Geography mapping coverage, Customs evidence coverage, Payment provider operations, Inventory/Order operational checks, monitoring, alerting, and rollback procedures.

## Explicit Success Answers

- Reviewed ONLY POS Admin Storefront Checkout? **Yes.** Affiliate Checkout, CommerceHub Marketplace Checkout, and POS Sales Checkout were excluded.
- Runtime modified? **No.**
- Backend modified? **No production backend code modified.**
- Frontend modified? **No.**
- Architecture Certified? **Yes.**
- Shipping authority preserved? **Yes.** Checkout validates shipping method/cost only and does not own Shipment, carrier registration, tracking, labels, or carrier mapping.
- Payment authority preserved? **Yes.** Checkout validates method and creates payment intent/session evidence only; Payment lifecycle owns authorization, success, collection, refund, settlement, and provider verification.
- Checkout owns Cart? **No.** Cart authority remains backend cart domain. Checkout consumes a live admitted cart snapshot.
- Checkout owns Orders? **No as a domain authority.** Checkout orchestrates initial Storefront order creation through Order authority and snapshots admitted checkout facts; Order lifecycle remains Order/Payment/Fulfillment authorities.
- Checkout owns Shipment? **No.** Shipment identity, lifecycle, carrier registration, tracking, and delivery remain Shipping Platform/Shipment authority.
- Checkout owns Inventory? **No.** Checkout consumes Inventory authority for stock validation/reservation; inventory truth remains ProductVariantStocks/inventory services.
- Production-certifiable architecture? **Yes.** Architecture is production-certifiable; operational rollout gates remain separate.
- Recommend next phase: **POSADMIN-CHECKOUT-1 runtime/operational readiness certification**, limited to evidence collection and focused tests for checkout idempotency, final revalidation, payment intent handoff, inventory reservation, coupon/promotion reservation, recovery fail-closed behavior, and monitoring/alerting coverage without changing authority boundaries.

## AI Context Decision

No `.ai-context`, `*.ai-context`, or `*ai-context*` files were found in this workspace during repository-wide search. No AI context files were updated.
