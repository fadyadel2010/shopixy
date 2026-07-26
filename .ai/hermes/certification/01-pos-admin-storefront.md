# Certification Summary

Relationship:

POS Admin ↔ Storefront

Status:

PASS

The POS Admin ↔ Storefront relationship is certified as architecturally consistent.

This certification records the already-revalidated state of the relationship after the completed review and correction cycle. The two system guides now describe the same business reality from compatible perspectives:

- POS Admin is the tenant operational surface.
- Storefront is the tenant-branded customer presentation and intent surface.
- Backend Core authority is the shared decision boundary between them.
- PostgreSQL and the owning lifecycle/domain services remain authoritative for durable catalog, pricing, inventory, checkout, order, payment, shipping, fulfillment, customer, tenant, configuration, and financial truth.
- POS Admin operational changes reach Storefront only after backend authority admits them.
- Storefront customer activity reaches POS Admin operations only after backend authority admits customer intent and creates authoritative business state.
- Neither POS Admin nor Storefront is described as a competing source of truth.

The relationship therefore satisfies the cross-system certification standard: both system guides preserve compatible actors, ownership, terminology, lifecycle boundaries, evidence boundaries, authority direction, and cross references to the relevant Core Guides.

# Scope

This certification validates the architectural consistency of the POS Admin ↔ Storefront relationship in the Shopixy Knowledge Base.

It validates that the relationship is documented as a shared tenant commerce model in which POS Admin configures and operates tenant business inputs, Storefront presents customer-facing admitted results, and Backend Core authority owns the durable decisions that connect them.

This certification covers the documented relationship between:

- `.ai/hermes/systems/pos-admin/README.md`
- `.ai/hermes/systems/storefront/README.md`
- `.ai/hermes/CROSS_SYSTEM_CERTIFICATION.md`
- Relevant Core Guides:
  - `.ai/hermes/02-architecture.md`
  - `.ai/hermes/03-inventory.md`
  - `.ai/hermes/04-payments.md`
  - `.ai/hermes/05-orders.md`
  - `.ai/hermes/06-security-identity.md`
  - `.ai/hermes/07-platform-overview.md`
  - `.ai/hermes/08-qa-philosophy.md`

This certification validates documentation consistency only. It does not certify implementation correctness, UI behavior, API behavior, runtime production readiness, code quality, database schema, test execution results, feature completeness, or delivery status.

# Certified Relationship

The certified operational relationship is:

```text
POS Admin
    ↓
Backend Core Authority
    ↓
Storefront
    ↓
Customer Activity
    ↓
Backend Core Authority
    ↓
POS Admin Operations
```

POS Admin is the tenant operating surface. It submits operational intent for tenant catalog, products, categories, pricing inputs, inventory changes, coupons, promotions, Smart Offers, shipping methods, payment method configuration, CMS content, Storefront settings, theme, branding, store identity, currency and tenant settings, WhatsApp configuration, notification triggers, analytics configuration, Commerce AI configuration, and tenant operational processing.

Backend Core Authority admits, rejects, recomputes, persists, projects, or repairs that intent according to the owning Core domains. POS Admin actions do not become customer-facing truth merely because an operator changed a screen. The authoritative decision remains with PostgreSQL and the owning lifecycle/domain services.

Storefront consumes the admitted customer-facing result. It may present public catalog, product detail, pricing evidence, stock evidence, checkout candidates, coupons, promotions, Smart Offers, shipping options, payment methods, content, theme, branding, currency, store identity, notifications, WhatsApp verification, analytics events, and customer journey state. Storefront presentation is not authority. Storefront projections, browser state, route state, LocalStorage, Redis, MongoDB, Elasticsearch, analytics events, notification delivery, and provider/browser signals remain evidence or presentation.

The bidirectional flow is also certified in the customer direction:

- A customer browses, searches, views products, adds to cart, verifies phone where required, selects checkout candidates, chooses shipping/payment candidates, submits coupon/promotion intent, pays, tracks orders, requests returns/refunds, or manages account activity through Storefront.
- Storefront submits that customer intent to Backend Core Authority.
- Backend Core Authority recomputes current tenant, customer, product, pricing, coupon, promotion, inventory, shipping, payment, checkout, order, fulfillment, refund, and notification authority.
- If admitted, authoritative business state is created or changed in PostgreSQL through the proper lifecycle/domain owner.
- POS Admin then receives the operational view of admitted customer activity and may perform allowed tenant operations such as order review, fulfillment, shipping handoff, cancellation/refund initiation where permitted, customer support, reporting, and communication.

This flow is certified as bidirectional but not peer-authoritative. POS Admin does not push truth directly into Storefront. Storefront does not push truth directly into POS Admin. Both systems converge through Backend Core Authority.

# Certified Architectural Areas

The following architectural areas are certified as consistent across the POS Admin and Storefront guides.

## Catalog

POS Admin configures tenant catalog inputs. Storefront presents only backend-admitted public catalog evidence. SQL catalog, publication, category, media, variant, unit, and visibility authority decide eligibility.

## Products

POS Admin operates products, variants, units, media, and publication intent. Storefront presents product cards, product detail, variant choices, media, and purchasability evidence. Product visibility or detail presentation never overrides current backend product authority.

## Pricing

POS Admin configures prices and pricing-related inputs. Storefront presents admitted price and discount evidence. Backend authority recomputes final price, discount, tax, shipping, payment fee, currency, and payable total during cart/checkout/order admission.

## Inventory

POS Admin operates inventory, warehouses, stock adjustments, transfers, reservations, and availability inputs through inventory authority. Storefront presents availability evidence and submits cart/checkout quantity intent. Current SQL inventory authority decides stock, reservation, release, commit, restoration, and purchasability.

## Orders

Storefront customer checkout creates authoritative generic Storefront orders only after backend order authority admits the request. POS Admin receives the operational order view and performs allowed tenant operations through lifecycle owners. Storefront does not operate order lifecycle authority, and POS Admin does not create customer authority outside backend admission.

## Checkout

Storefront checkout is the customer orchestration surface. Backend authority recomputes tenant, customer, cart, product, pricing, coupon, promotion, shipping, payment, inventory, idempotency, and order authority. POS Admin configuration influences checkout only after backend admission.

## Payments

POS Admin configures tenant payment methods and may operate payment-at-sale or review surfaces where allowed. Storefront presents eligible payment methods and provider handoff states. Payment lifecycle authority owns finality, provider evidence admission, refunds, reconciliation, and payment state. Stripe/Paymob reference-provider coverage is documented for POS Admin + Storefront; provider/browser success remains evidence until lifecycle admission.

## Shipping

POS Admin configures shipping methods, zones, prices, carrier handoff inputs, and operational fulfillment/shipment intent. Storefront presents admitted shipping options, costs, ETA, and tracking evidence. Backend shipping/order/fulfillment authority decides eligibility, cost snapshots, carrier evidence admission, and fulfillment effects.

## Fulfillment

POS Admin may process allowed fulfillment operations after authoritative order creation. Storefront presents fulfillment and delivery progress to customers. Fulfillment state is owned by the backend fulfillment lifecycle owner, not by Storefront tracking presentation or POS Admin UI state.

## Coupons

POS Admin configures coupon inputs. Storefront may submit coupon code intent and present admission feedback. Backend promotion/coupon/pricing authority decides eligibility, stacking, usage, amount, reservation, and final applied value.

## Promotions

POS Admin configures promotion inputs. Storefront presents promotion messaging and candidates. Backend authority decides actual eligibility, financial effect, and checkout/order snapshots.

## Smart Offers

POS Admin configures Smart Offer inputs and presentation behavior. Storefront may display Smart Offer experiences and submit offer intent. Backend promotion/pricing authority decides financial eligibility, usage, recovery, and final amount.

## Commerce AI

POS Admin configures customer assistant presentation such as enabled state, personality, greeting, widget style, and position. Storefront loads the admitted assistant as a customer interaction layer. Commerce AI output is customer interaction evidence only and never decides catalog, price, stock, checkout, payment, order, refund, customer, or tenant authority.

## Analytics

POS Admin configures analytics integrations. Storefront emits customer observation events through those integrations. Analytics events, dashboards, pixels, and client-side metrics remain observation evidence and never become order, payment, ledger, refund, conversion, or customer authority.

## Store Identity

POS Admin configures tenant store identity inputs through backend tenant authority. Storefront presents admitted store identity. Route state, browser state, cached configuration, and analytics configuration cannot choose tenant/store identity.

## Theme

POS Admin configures theme inputs. Storefront renders the admitted presentation. Theme affects customer experience only; it does not override catalog, pricing, inventory, checkout, payment, order, identity, CMS, or currency authority.

## Branding

POS Admin configures branding, logo, colors, fonts, and social/public appearance inputs. Storefront presents admitted branding. Branding remains presentation and does not create business authority.

## Currency

POS Admin operates tenant currency configuration through tenant authority. Storefront presents admitted tenant currency and never selects currency from browser or route state. Tenant Storefront currency remains distinct from CommerceHub marketplace EGP-only finance; this certification preserves that boundary.

## Settings

POS Admin configures tenant settings and Storefront settings through backend authority. Storefront presents admitted configuration. Settings do not override tenant identity, role authority, pricing, payment, order, inventory, checkout, or lifecycle decisions.

## WhatsApp Verification

POS Admin configures admitted WhatsApp integration. Storefront may use it for checkout phone verification, OTP, notifications, or recovery workflows where admitted. WhatsApp transport, OTP, session, socket, and delivery evidence never create order, payment, inventory, pricing, checkout, refund, shipping, account, or tenant authority.

## Notifications

POS Admin and Storefront may trigger or present communication derived from admitted domain events. Notifications are derivative communication effects. Delivery, non-delivery, or message content does not create lifecycle, payment, order, shipment, fulfillment, refund, ledger, or customer truth.

## Customer Journey

Storefront owns the customer journey presentation and intent collection from browsing through checkout, payment, tracking, returns/refunds, and account activity. POS Admin owns tenant operational processing after backend admission. Backend Core Authority connects both perspectives into one SQL-authoritative business flow.

## Operational Change Propagation

Operational changes propagate from POS Admin through backend validation and projection/cache/read-model convergence before Storefront presents them. Storefront activity propagates through backend admission before POS Admin sees operational effects. Projection lag is accepted only while authority remains protected and critical reads revalidate or repair where required.

## Authority Boundaries

Both guides preserve the same boundary: POS Admin and Storefront are surfaces; Backend Core Authority is the decision boundary; PostgreSQL is the durable system of record; lifecycle/domain owners mutate protected state.

## Business Rules

Both guides preserve durable business rules: server-side recomputation, tenant isolation, SQL authority, derivative evidence boundaries, lifecycle ownership, fail-closed uncertainty, no direct lifecycle bypass, no stale-client finality, no cross-tenant leakage, and no projection/browser/provider evidence becoming truth.

## Never Assume

Both guides instruct readers not to assume UI visibility, projection state, provider/browser result, notification delivery, cached configuration, route state, LocalStorage, client totals, or analytics events are authority.

## QA Checklist

Both guides align QA reasoning around identifying the owning Core domain, distinguishing authority from evidence, verifying tenant scope, testing fail-closed behavior, checking projection convergence, and preserving lifecycle ownership.

# Authority Consistency

The certified relationship preserves the following shared architectural truths.

## SQL authority

PostgreSQL is the authoritative write model for durable Shopixy business truth. Catalog, products, pricing, inventory, orders, payments, refunds, fulfillment, customer identity, tenant settings, accounting, and operational state are authoritative only when admitted by the backend and persisted through the correct SQL/lifecycle boundary.

MongoDB, Redis, Elasticsearch, projections, browser state, route state, frontend DTOs, queue payloads, analytics events, notification delivery, provider redirects, provider callbacks, and UI screens are derivative evidence or presentation.

## Presentation

POS Admin presentation is operational UI. Storefront presentation is customer UI. Neither presentation surface decides business truth. A screen that shows a product, setting, price, order, payment, notification, shipment, stock level, coupon, offer, or report does not by itself prove current authority.

## Evidence

Evidence may be useful, but it must be admitted by the owning backend authority before it affects durable truth. Provider callbacks, payment redirects, WhatsApp OTPs, notification receipts, analytics events, search documents, Mongo projections, Redis cache entries, browser state, and customer/admin form values are candidate signals only.

## Operational intent

POS Admin submits tenant operational intent. Storefront submits customer intent. Intent becomes durable only after backend authority validates, recomputes, scopes, admits, persists, and stages the appropriate downstream effects.

## Backend lifecycle owners

Protected state remains owned by backend lifecycle/domain owners:

- Payment truth and payment status transitions remain with payment lifecycle authority.
- Order creation and order lifecycle truth remain with order authority.
- Fulfillment progression remains with fulfillment lifecycle authority.
- Inventory stock, reservations, releases, commits, and restoration remain with inventory authority.
- Tenant and customer identity remain with backend identity/tenant authority.
- Financial reporting and ledger truth remain SQL/ledger authority.
- Shipping and carrier evidence feed the appropriate backend order/fulfillment/shipping authority.
- Recovery, repair, reindex, and replay converge from current SQL authority and never invent truth.

# Certification Notes

Future reviewers must preserve these architectural decisions:

- POS Admin and Storefront are not separate systems of truth. They are two surfaces over the same backend authority model.
- POS Admin is the tenant operational surface; Storefront is the customer presentation and intent surface.
- Backend Core Authority is the mandatory decision boundary in both directions.
- POS Admin operational changes do not become Storefront truth until backend authority admits them and derivative surfaces converge.
- Storefront customer actions do not become POS Admin operational truth until backend authority admits them and creates authoritative SQL state.
- Storefront checkout is real authority-producing workflow only because backend checkout/order/payment/inventory/security authority admits it; Storefront UI itself does not own checkout truth.
- POS Admin order processing is operational only; protected order, payment, fulfillment, refund, inventory, and ledger transitions still belong to lifecycle/domain owners.
- Storefront Settings and Tenant Settings must remain distinct: customer-facing presentation settings do not override tenant operational configuration or currency/identity authority.
- Theme, branding, store identity, Commerce AI, analytics, notifications, and WhatsApp are presentation/interaction/communication surfaces unless admitted by the owning backend authority for a specific business effect.
- Projection/cache/read-model lag is acceptable only while critical authority remains protected and required revalidation/repair paths do not treat absence or stale evidence as truth.
- The certified relationship excludes CommerceHub marketplace authority from being collapsed into Storefront or POS Admin tenant operations. CommerceHub remains adjacent and distinct.
- Affiliate attribution may feed Storefront-origin orders where backend authority admits it, but Storefront affiliate attribution must not be copied into CommerceHub marketplace order authority.
- Payment provider reference-provider coverage for Stripe/Paymob is POS Admin + Storefront scoped; provider output remains evidence until payment lifecycle admission.
- Tenant isolation, lifecycle ownership, fail-closed uncertainty, evidence-vs-authority separation, and SQL authority are the governing preservation rules.

# Future Impact

Future changes to either guide should preserve this certification by maintaining the same certified relationship model.

A future POS Admin guide change should not describe POS Admin as directly pushing authoritative truth into Storefront. It may describe POS Admin submitting operational intent, configuring tenant inputs, operating tenant workflows, and processing admitted Storefront customer activity, but it must continue to route durable decisions through Backend Core Authority.

A future Storefront guide change should not describe Storefront as independently deciding catalog, pricing, inventory, checkout, payment, order, shipping, fulfillment, refund, customer identity, tenant identity, currency, settings, notification, analytics, or projection truth. It may describe Storefront presenting admitted evidence and submitting customer intent, but it must continue to route durable decisions through Backend Core Authority.

Future changes to either guide should preserve:

- the bidirectional POS Admin ↔ Backend Core Authority ↔ Storefront flow;
- PostgreSQL as authoritative system of record;
- backend lifecycle/domain services as protected mutation owners;
- Storefront as customer intent/presentation, not business authority;
- POS Admin as tenant operational intent/orchestration, not business authority by itself;
- projection/cache/search/read-model/browser/provider/notification/analytics evidence as non-authoritative;
- fail-closed behavior when authority is missing, stale, contradictory, or externally uncertain;
- explicit distinction between Storefront tenant commerce and CommerceHub marketplace commerce;
- cross references to the owning Core Guides rather than duplicated or divergent domain rules.

If a future edit changes any of these certified truths, the POS Admin ↔ Storefront relationship should be revalidated under `.ai/hermes/CROSS_SYSTEM_CERTIFICATION.md` before the certification is treated as current.

# References

- `.ai/hermes/CROSS_SYSTEM_CERTIFICATION.md`
- `.ai/hermes/systems/pos-admin/README.md`
- `.ai/hermes/systems/storefront/README.md`
- `.ai/hermes/INDEX.md`
- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/08-qa-philosophy.md`
