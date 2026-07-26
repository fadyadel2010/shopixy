# Storefront System Guide

Audience: Senior QA Engineers, AI Agents, and new contributors.

This is a SYSTEM guide. It explains how the Storefront uses Shopixy's Core domains to deliver a tenant-branded customer experience. It is not a domain guide, UI guide, feature list, implementation walkthrough, or replacement for the Core guides.

Use this guide after reading:

- `.ai/hermes/INDEX.md`
- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/08-qa-philosophy.md`

When Storefront behavior touches a Core domain, the owning Core guide decides the rule. This guide explains the Storefront relationship to those rules.

---

# Purpose

The purpose of this guide is to teach how the Storefront operates as one complete business system.

Storefront is the tenant-branded customer surface where a shopper discovers products, evaluates offers, builds intent, checks out, pays, tracks orders, and returns to manage account/order activity. It does not own the durable business rules behind those activities. It presents and orchestrates customer intent while the Core domains decide truth.

A QA engineer should use this guide to answer system questions such as:

- What journey is the customer in?
- Which backend authority owns the business fact being shown or requested?
- Is Storefront presenting current authority, acceptable projection lag, or unsafe derivative state?
- Is Storefront submitting intent, or is it incorrectly deciding pricing, inventory, payment, order, customer, or shipping truth?
- When a journey crosses domains, where should QA look for the owning rule?

The central Storefront mental model:

Storefront collects and presents customer intent; backend Core authority admits, recomputes, persists, and reconciles that intent.

---

# What is Storefront?

Storefront is the tenant-branded customer commerce system for one tenant's own online store.

It exists to turn backend catalog, content, pricing, inventory, promotion, checkout, payment, fulfillment, and customer-account authority into a coherent shopping experience.

## Its purpose

Storefront provides the customer-facing business journey:

1. Enter a tenant store through a domain, slug, route, campaign, search result, referral, or direct link.
2. Discover products and content through homepage, navigation, categories, search, recommendations, CMS, and SEO surfaces.
3. Evaluate product detail, media, reviews, price, promotion, and availability presentation.
4. Build purchase intent through wishlist and cart.
5. Convert intent through checkout, shipping selection, payment selection, coupon/promotion admission, and order creation.
6. Continue the relationship through account, order tracking, delivery updates, notifications, returns, refund requests, and support-facing communication.

## What it owns

Storefront owns presentation and customer intent:

- Customer-facing route and page composition.
- Display of backend-admitted catalog, content, pricing, stock, shipping, payment, order, customer, and notification evidence.
- Candidate selections such as selected variant, quantity, shipping method, payment method, coupon code, address form values, wishlist action, and account action.
- Journey continuity such as loading states, retry intent, route state, local UI state, and user-friendly error/empty/blocked states.
- Safe communication of uncertainty: pending payment, unavailable cart, stale selection, review required, unavailable projection, or fail-closed checkout.

## What it does NOT own

Storefront does not own business truth.

It may not decide:

- Tenant identity, store live status, domain authority, currency authority, or configuration truth.
- Product publication, category lineage, media authority, variant identity, purchasability, or price authority.
- Search visibility, homepage eligibility, recommendation eligibility, or SEO visibility independent from SQL-backed publication authority.
- Inventory availability, reservation, release, commit, or restoration.
- Promotion/coupon eligibility, discount amount, stacking, usage, reservation, or final applied value.
- Cart authority after backend admission, checkout session authority, checkout totals, tax, shipping cost, payment fee, or expected pricing.
- Payment intent identity, payment method eligibility, provider finality, payment status, refund status, reconciliation, settlement, or outward-money readiness.
- Order creation, order status, fulfillment status, cancellation, return, refund, delivery state, or order ownership.
- Customer identity/session authority, current account status, role/scope, or tenant/customer ownership.
- Notification, WhatsApp, analytics, or projection state as business truth.

## Relationship with POS Admin

POS Admin is the tenant's operating surface. It configures and operates the tenant's business through the same Core authority that Storefront presents to customers.

POS Admin can create or mutate tenant catalog, pricing, content, navigation, theme/configuration, shipping methods, payment configuration, inventory, promotions, and operational order workflows through the owning domains. Storefront consumes the published/customer-eligible result of those decisions.

Do not treat POS Admin and Storefront as separate systems of truth. POS Admin is usually the operational source of tenant intent; PostgreSQL and lifecycle owners are the authority; Storefront is the customer presentation and intent surface.

If POS Admin changes a product, category, coupon, shipping method, payment method, CMS page, theme, or store configuration, Storefront may temporarily lag through projections/caches, but customer-visible decisions must converge to the current Core authority.

Consult:

- `.ai/hermes/systems/pos-admin/README.md` for POS Admin as an operating system.
- `02-architecture.md` for SQL/projection authority.
- The owning Core guide for the affected domain.

## Relationship with CommerceHub

CommerceHub is the shared multi-vendor marketplace. Storefront is a tenant's own branded store.

They may share product, inventory, payment, order, search, projection, customer-experience, and affiliate concepts, but they are not the same system:

- Storefront checkout creates the generic Storefront/POS/Affiliate order model.
- CommerceHub checkout creates marketplace orders and vendor sub-orders with separate authority rules.
- Storefront tenant currency is tenant authority; CommerceHub marketplace finance/reconciliation is EGP-only platform authority.
- Stripe/Paymob reference-provider coverage applies to POS Admin + Storefront, not CommerceHub marketplace payment paths.
- Storefront affiliate attribution produces a Storefront-origin order with attribution; do not apply that model to marketplace sub-orders unless a separate certified authority model exists.

Consult:

- `05-orders.md` for generic order versus CommerceHub marketplace order distinction.
- `04-payments.md` for provider-reference scope and CommerceHub payment exclusions.
- `07-platform-overview.md` for cross-system relationships.

---

# Primary Responsibilities

Storefront responsibilities are customer-experience responsibilities. Each responsibility exists to present or submit customer intent; the Core domain owns the business rules.

| Responsibility | Why it exists in Storefront | Core business-rule owner | Guide to consult |
| --- | --- | --- | --- |
| Product Discovery | Helps shoppers find tenant products through public catalog surfaces. | Catalog/publication/search projection authority; SQL decides product/category/variant/media/stock visibility. | `02-architecture.md`, `03-inventory.md`, `05-orders.md`, `07-platform-overview.md` |
| Search | Converts query intent into product results, filters, autocomplete, and suggestions. | Search is Elasticsearch projection plus SQL authority overlay; product/category/media/stock/publication authority decide visibility. | `02-architecture.md`, `03-inventory.md`, `07-platform-overview.md` |
| Navigation | Presents store menus, footer links, CMS/category/product targets, and public routes. | Tenant-owned navigation/content/category authority; stale/fallback menus cannot create target authority. | `02-architecture.md`, `06-security-identity.md`, Storefront navigation certification |
| Categories | Organizes discovery through category hierarchy, filters, and landing pages. | SQL category hierarchy and live parent lineage; invalid/deleted/missing parent lineage removes eligibility. | `02-architecture.md`, `03-inventory.md`, Storefront navigation/content certifications |
| Homepage | Presents curated shelves, sections, offers, featured products, and entry points. | SQL product/category/media/promotion/publication authority; homepage projections are derivative. | `02-architecture.md`, `07-platform-overview.md`, Storefront final/recovery certifications |
| Recommendations | Presents suggested or related products as discovery aids. | Discovery/search projection may rank, but SQL authority decides whether a product may be shown or bought. | `02-architecture.md`, `03-inventory.md`, Storefront final certification |
| CMS | Presents store pages, localized/SEO text, builder sections, and published content. | Tenant-owned CMS/page publication authority; drafts, previews, cache, and builder state are not public truth. | `02-architecture.md`, `06-security-identity.md`, Storefront content certification |
| SEO | Exposes indexable title/meta/structured/public route evidence. | SEO content is subordinate to current page/product/category publication and store configuration authority. | `02-architecture.md`, Storefront content/configuration certifications |
| Pricing Presentation | Shows backend-admitted price, discount, tax, shipping, fees, totals, and currency. | Pricing, coupon, promotion, tax, shipping, payment fee, and currency are recomputed by backend authority. | `04-payments.md`, `05-orders.md`, Storefront pricing/promotions certifications |
| Inventory Presentation | Shows stock availability, purchasability, out-of-stock, and availability warnings. | Inventory authority is SQL stock/reservation/warehouse/variant/product lineage; projections are advisory. | `03-inventory.md`, `05-orders.md` |
| Product Detail | Presents product identity, variants, unit/default-unit choice, media, price, stock, reviews, and add-to-cart eligibility. | SQL product/variant/unit/media/category/publication/inventory authority; detail DTOs are admitted evidence. | `02-architecture.md`, `03-inventory.md`, `05-orders.md`, Storefront final certification |
| Media | Displays gallery, primary image, variant image, thumbnails, and CDN assets. | SQL `ProductMedia` and variant-media linkage decide media ownership/order; CDN/search thumbnails are derivative. | `02-architecture.md`, Storefront final/recovery certifications |
| Reviews | Presents customer trust/social-proof evidence for product evaluation. | Review visibility must remain subordinate to tenant/product/customer/order authority and must not create product/order truth. | `02-architecture.md`, `05-orders.md`, `06-security-identity.md` |
| Wishlist | Captures non-binding purchase interest for a customer. | Customer identity/session and product visibility authority decide whether interest may be recorded; wishlist is not inventory reservation or checkout authority. | `06-security-identity.md`, `03-inventory.md`, `05-orders.md` |
| Cart | Holds purchase intent and backend-admitted cart snapshot. | Backend cart authority revalidates product, variant, quantity, stock, price, customer/guest ownership, coupons, and recovery. | `03-inventory.md`, `05-orders.md`, Storefront cart certification |
| Coupons | Lets customers submit coupon codes, smart-offer tokens, and promotion candidates. | Coupon/promotion authority validates eligibility, reservation, usage, stacking, amount, customer, tenant, and order/payment confirmation. | `04-payments.md`, `05-orders.md`, Storefront promotions/financial certifications |
| Checkout | Converts cart intent into SQL order/payment/inventory authority. | Orders, inventory, payments, promotions, shipping, tax, identity, tenant config, and idempotency are recomputed in backend authority. | `05-orders.md`, `04-payments.md`, `03-inventory.md`, `06-security-identity.md` |
| Customer Accounts | Provides registration/login/session/account profile/order access journeys. | Current tenant-owned SQL customer/session authority decides identity; tokens/cookies/frontend route guards are evidence. | `06-security-identity.md`, Storefront customer certification |
| Orders | Presents order confirmation, detail, history, cancellation/return/refund intent, and tracking entry points. | Generic SQL order lifecycle owns order truth; projections may be repaired from SQL for critical reads. | `05-orders.md`, `04-payments.md`, Storefront order certification |
| Payments | Presents available methods, fees, provider handoff, pending/success/failure/retry states, and manual-wallet/COD experiences. | Payment lifecycle owns payment truth; provider/browser results are evidence only. | `04-payments.md`, `05-orders.md`, Storefront payment/payment-method certifications |
| Delivery Tracking | Shows shipment, carrier, ETA, delivery, and tracking evidence to the customer. | Shipping/fulfillment/order lifecycle authority owns delivery progress; carrier/customer payloads are evidence. | `05-orders.md`, `04-payments.md`, Storefront shipping certifications |
| Notifications | Sends or displays order/payment/shipping/customer communication. | Notifications are outputs of admitted lifecycle state; WhatsApp/email/SMS delivery does not mutate commerce truth. | `05-orders.md`, `04-payments.md`, Storefront WhatsApp certification |

System interpretation:

- If the responsibility helps the customer see something, Storefront may present backend-admitted or projected evidence.
- If the responsibility changes business state, Storefront submits intent and the owning Core domain decides.
- If a responsibility touches money, inventory, tenant scope, identity, or lifecycle state, use the owning Core guide before judging behavior.

---

# Customer Journeys

## Anonymous browsing

Anonymous browsing begins with store entry. The customer may arrive by tenant domain, slug, direct product link, CMS page, category link, search result, referral link, or campaign URL.

Storefront should:

- Resolve and present the tenant store identity/configuration admitted by backend authority.
- Present public catalog/content/navigation/theme evidence for that tenant.
- Avoid treating browser route, query string, referral code, cached store config, or projection document as tenant/store authority.
- Fail closed or show unavailable/empty/error presentation when current backend authority cannot prove store visibility.

Core owners:

- Architecture for SQL/projection boundary.
- Security/Identity for tenant resolution.
- Orders/Inventory/Payments only when browsing becomes cart/checkout/payment intent.

## Searching

Searching is discovery, not authority.

Storefront may render Elasticsearch/search-projection results, filters, autocomplete, suggestions, and ranked products. Those results are valid customer presentation only when the read path is subordinate to current SQL product/category/variant/media/promotion/publication/stock authority.

QA should distinguish:

- Accepted search lag: projection has not caught up yet.
- Search authority bug: stale search resurrects hidden/deleted/unpublished/invalid-lineage products, crosses tenant scope, or overrides SQL visibility.

Consult `02-architecture.md`, `03-inventory.md`, and Storefront search/recovery certifications.

## Viewing products

Product detail turns discovery into evaluation.

Storefront may present backend-admitted product identity, title, description, media, variants, units, price, promotion display, availability, reviews, and add-to-cart candidates. It may not decide that a product is purchasable from cached or projected data alone.

Important system rule:

A visible product card is not proof that checkout may succeed. Final cart/checkout still revalidates current product, variant, unit, price, inventory, promotion, shipping, payment, tenant, customer, and store-live authority.

## Adding to cart

Add to cart starts as customer intent and becomes a backend-admitted cart snapshot.

Storefront owns:

- selected product/variant/unit candidate;
- selected quantity candidate;
- UI loading/error/retry state;
- presentation of the returned cart snapshot.

Core authority owns:

- current product/variant/unit eligibility;
- stock/quantity validation;
- cart ownership and merge behavior;
- coupon/promotion revalidation;
- recovery and stale-cart handling.

LocalStorage, sessionStorage, React state, browser cache, and Redis-backed cart persistence cannot become cart authority. Recovery must re-enter backend cart authority or fail closed.

## Checkout

Checkout is the Storefront's highest-risk orchestration journey.

The customer checkout journey is:

```text
Cart
    ↓
Checkout
    ↓
Phone Verification
    ↓
Payment
    ↓
Order Creation
```

Phone verification is an admission step before order creation. It is not payment, order creation, payment confirmation, or customer session authority. It is customer identity evidence used by the checkout process before backend authority decides whether checkout may continue.

Storefront collects candidates:

- address form values;
- shipping method candidate;
- payment method candidate;
- phone verification evidence;
- coupon/promotion candidate;
- expected-pricing evidence;
- customer/guest context;
- confirmation intent.

Backend authority recomputes and admits:

- store live/configuration status;
- customer/guest/cart ownership;
- product/variant/unit eligibility;
- current prices, coupons, promotions, tax, shipping, payment fees, currency, and totals;
- shipping/payment method availability and amount limits;
- phone verification admission as customer identity evidence;
- inventory reservation;
- order snapshots;
- idempotency/replay safety;
- payment intent/session handoff.

Client totals, discount values, shipping cost, tax, payment fee, selected method eligibility, and payment split are stale-client assertions. A mismatch should fail closed, not be silently accepted.

## Payment

Payment begins after backend payment authority admits the payable/order context.

Storefront may:

- show available payment methods admitted for the current cart/pricing context;
- submit a payment method candidate;
- redirect or hand off to a provider flow;
- present pending, action-required, success-like, failure-like, manual-review, or retry guidance based on backend-admitted status.

Storefront may not:

- create payment intent authority;
- decide provider success/failure finality;
- trust frontend SDK result, redirect query, iframe return, QR receipt, browser state, or provider payload as final payment truth;
- report terminal failed/success guidance when backend payment authority is in suspense, quarantine, amount mismatch, manual review, late-success, or reconciliation-required territory.

Consult `04-payments.md` before testing payment presentation.

## Order tracking

Order tracking presents an admitted order journey after creation.

Storefront order reads may use read models, but critical order reads must be understood as SQL-authority-backed or SQL-repairable. A missing Mongo projection is not proof that the order does not exist.

Tracking may include:

- order status;
- payment status;
- fulfillment status;
- shipment/tracking evidence;
- refund/return state;
- notifications.

Each displayed fact has a different owner. The order page is a composition surface, not the owner of those facts.

Consult `05-orders.md`, `04-payments.md`, and Storefront order/shipping certifications.

## Returns

A return journey begins as customer intent, not refund authority.

Storefront may present return eligibility and allow a customer to request return/review. The owning order/return/fulfillment/payment/refund authorities decide:

- whether the order line is eligible;
- whether fulfillment/delivery state supports return;
- whether returned goods/evidence has been accepted;
- whether refund execution may begin;
- whether inventory may be restored;
- whether money movement must remain blocked or reconciliation-required.

Physical return evidence cannot forge refund success.

Consult `05-orders.md`, `04-payments.md`, and `03-inventory.md`.

## Refund requests

Refund request presentation is not refund finality.

Storefront may allow a customer to request or view refund progress. Payment/refund authority owns refundable balance, refund state, external provider/manual execution evidence, settlement lineage, ledger effects, and uncertainty states.

Important Storefront presentation rule:

A refund that is processing, manual-review, reconciliation-required, or externally uncertain must not be presented as safely failed or fully completed unless backend payment/refund authority says so.

Consult `04-payments.md` first.

## Account management

Storefront account management covers customer registration, login, logout, session refresh, profile activity, password/reset-like journeys where available, order ownership, address candidates, and customer-specific cart/account surfaces.

Current SQL customer/session authority decides whether the actor is valid. JWTs, cookies, local account state, route guards, and cached profile DTOs are evidence only.

For QA:

- Verify stale/revoked/blocked/inactive sessions fail closed.
- Verify guest/customer ownership cannot be hijacked during cart merge, checkout recovery, order tracking, or return/refund requests.
- Verify account state never crosses tenant scope.

Consult `06-security-identity.md` and Storefront customer certification.

---

# Cross-System Relationships

## POS Admin

POS Admin operates the tenant business; Storefront presents the customer-facing published result.

Storefront should be understood as the customer-facing side of a tenant operation that is configured and governed from POS Admin. POS Admin submits operational intent; backend authority admits that intent; Storefront presents the admitted customer-facing result.

Relationship examples:

- POS Admin publishes products/categories/media/CMS/navigation/theme/configuration; Storefront presents current public authority.
- POS Admin changes inventory/warehouse/pricing/shipping/payment methods/promotions; Storefront projections may lag, but cart/checkout must revalidate current authority.
- POS Admin may process operational order/fulfillment/refund workflows; Storefront presents customer-safe order status and allowed customer intent.

Failure risk: treating POS Admin UI state, cached admin DTOs, or Storefront projection state as the authority instead of the shared Core domain.

## CommerceHub

CommerceHub is adjacent but distinct.

Storefront and CommerceHub share some catalog/inventory/payment/search ideas but use different customer system boundaries. Storefront is tenant-owned branded commerce; CommerceHub is marketplace commerce with platform/vendor-specific order and finance authority.

Failure risk: copying CommerceHub marketplace rules into Storefront or copying Storefront generic-order rules into CommerceHub sub-orders.

## Affiliate

Affiliate can drive customers into Storefront.

The storefront entry/referral journey admits affiliate evidence only after backend resolution against current tenant, active link, product/category/store eligibility, and protected attribution evidence. If admitted, checkout creates a normal Storefront-origin order with immutable affiliate attribution in the same authority boundary.

Failure risk: treating referral query parameters, headers, browser storage, or repeated clicks as attribution authority.

## Payments

Storefront payment presentation and provider handoff are subordinate to payment lifecycle authority.

Stripe and Paymob reference-provider coverage applies to POS Admin + Storefront surfaces. Even there, provider success remains evidence until backend lifecycle admission.

Failure risk: UI claims success/failure directly from provider/browser state.

## Inventory

Storefront stock display, cart quantity validation, checkout reservation, fulfillment consumption, and returns/restoration all depend on current inventory authority.

Failure risk: treating visible stock or raw projection stock as reservable stock, or treating a raw reservation row as consumable without current lineage.

## Shipping

Storefront shipping-method selection, shipping cost/ETA presentation, shipment tracking, and delivery status are subordinate to shipping/order/fulfillment authority.

Delivery evidence may synchronize fulfillment only through the owning lifecycle authority. COD cash collection is payment evidence, not shipment truth by itself.

Failure risk: tracking or delivery presentation mutates order/payment truth.

## Notifications

Notifications communicate admitted state. They do not create state.

WhatsApp/email/SMS/in-app messages may tell a customer about order, payment, shipping, or refund progress, but message delivery/failure cannot decide those facts.

Failure risk: assuming a sent notification means the underlying lifecycle transition occurred, or using notification delivery as commerce authority.

## Search

Search powers Storefront discovery. It is a projection and ranking surface, not source of truth.

Search must converge from SQL product/category/media/variant/stock/promotion/publication authority and must delete/tombstone when authority disappears.

Failure risk: stale search resurrects products that SQL no longer allows.

## CMS

CMS provides pages, content blocks, SEO text, and navigation targets for Storefront.

CMS publication is tenant-owned authority. Builder state, preview state, localization text, cache, and projection cannot publish or expose content without current authority.

Failure risk: draft/preview/stale CMS projection becomes public authority.

## Analytics

Analytics observes and reports Storefront behavior.

Traffic evidence may be spoofable; purchase/revenue/conversion numerator authority must derive from SQL commerce/payment/order/ledger authority where financial correctness matters.

Failure risk: treating client analytics events, KPI cards, dashboards, or cached analytics as financial/order truth.

---

# How POS Admin Drives Storefront

Storefront does not exist independently. It is the customer-facing surface of a tenant whose operational control lives primarily inside POS Admin.

The relationship is:

```text
POS Admin
        ↓
Backend Authority
        ↓
Storefront Presentation
```

POS Admin configures, publishes, governs, and operates the tenant's store. Backend authority validates those operational changes through the owning Core domains. Storefront consumes the admitted public result as customer presentation and customer interaction.

This does not make POS Admin the source of truth. POS Admin is the operational surface. Backend Core authority is the decision boundary. Storefront is the presentation and customer-intent surface.

The operational relationship is bidirectional. POS Admin drives Storefront through admitted tenant configuration, catalog, pricing, inventory, shipping, payment, content, and appearance changes. Storefront drives POS Admin through customer-generated business activity: Customer → Storefront → Backend Authority → Authoritative Order Created → Order Appears inside POS Admin → POS Admin Operational Processing.

Storefront never creates operational authority. POS Admin never creates customer authority. Backend Core domains remain the shared authority for both systems: they admit POS Admin operational intent before Storefront presents it, and they admit Storefront customer intent before POS Admin can process it.

## Catalog

POS Admin owns the operational responsibility for products, categories, variants, units, product media, and publication.

Storefront consumes the published catalog only: product lists, product detail, category pages, media, variant choices, and public catalog navigation. If catalog authority does not admit a product/category/variant/media item as public and purchasable, Storefront may not treat it as customer-available merely because it appears in a projection or cache.

Core rule owner: `02-architecture.md` for authority/projection boundaries, `03-inventory.md` for variant/inventory lineage where availability is involved, and `05-orders.md` where catalog state affects checkout/order admission.

## Pricing

POS Admin configures prices, discounts, promotions, coupons, and Smart Offers.

Storefront consumes only currently admitted pricing: displayed price, discount presentation, promotion messaging, coupon acceptance feedback, Smart Offer display, and checkout totals after backend recomputation. Storefront may submit coupon/offer intent, but it does not own discount eligibility, stacking, amount, currency, usage, or confirmation.

Core rule owner: `04-payments.md` for financial/payment consequences, `05-orders.md` for checkout/order snapshots, and `02-architecture.md` for stale-client authority.

## Inventory

POS Admin controls the operational inventory world: warehouses, stock, reservations, adjustments, transfers, and availability inputs.

Storefront presents availability only. It may show in-stock/out-of-stock/limited-availability evidence, but it never owns inventory, reserves stock by itself, releases stock, consumes stock, or restores stock. Cart and checkout must re-enter current backend inventory authority.

Core rule owner: `03-inventory.md`.

## Orders

The customer-facing order journey begins in Storefront but does not stay inside Storefront.

```text
Customer places order
        ↓
Backend creates authoritative Order
        ↓
Order appears inside POS Admin Orders
```

Storefront submits checkout/order intent. Backend authority creates the authoritative Order, binds pricing/inventory/payment/shipping/customer snapshots as allowed by the Core domains, and then the order becomes operationally visible inside POS Admin.

POS Admin operates the post-order business workflow: processing, fulfillment, cancellation, and refund initiation where allowed. Storefront presents order confirmation, tracking, and allowed customer intent, but it never operates the order lifecycle. Storefront cannot process, fulfill, cancel, refund, or advance an order merely from customer UI state.

Core rule owner: `05-orders.md`, with `04-payments.md` for payment/refund effects and `03-inventory.md` for reservation/stock effects.

## Payments

POS Admin configures tenant payment options such as Stripe, Paymob, COD, Fawry, and Manual Wallet.

Storefront presents only payment methods currently eligible for the customer/cart/checkout context. Selection in Storefront is a payment-method candidate; final eligibility, fees, provider handoff, payment intent identity, status, refundability, and reconciliation are backend/payment authority.

Core rule owner: `04-payments.md`, with `05-orders.md` for checkout/order integration.

## Shipping

POS Admin configures shipping methods, zones, prices, and delivery estimates.

Storefront allows customer selection only. It may display admitted methods, cost, and ETA evidence, but final shipping method eligibility, cost, snapshot, fulfillment effects, carrier evidence, and delivery state belong to backend shipping/order/fulfillment authority.

Core rule owner: `05-orders.md`, with `04-payments.md` where COD collection or refund/payment readiness is affected.

## Theme & Branding

POS Admin configures theme, colors, fonts, branding, logo, and social links.

Storefront renders the admitted tenant presentation. Theme and branding can change customer experience, but they do not override catalog, pricing, inventory, checkout, payment, order, identity, CMS, or currency authority.

Core rule owner: `02-architecture.md` for authority boundaries and `06-security-identity.md` for tenant/configuration scope.

## CMS

POS Admin owns homepage content, landing pages, hero banners, content blocks, and SEO metadata.

Storefront renders published content. Drafts, previews, builder state, stale content projections, and cached SEO data are not public authority. Published CMS content must still respect current tenant, page, category, product, and publication authority.

Core rule owner: `02-architecture.md`, with `06-security-identity.md` for tenant scope.

## Commerce AI

POS Admin configures the customer assistant experience: enable/disable, personality, greeting, widget style, and position.

Storefront loads the configured assistant as a customer interaction layer. Commerce AI may help customers navigate, ask questions, or receive guided shopping support, but it does not own catalog, pricing, inventory, checkout, payment, order, refund, customer identity, or tenant authority.

Core rule owner: `02-architecture.md` for evidence-versus-authority and `06-security-identity.md` for tenant/customer boundaries.

## Analytics

POS Admin configures analytics integrations such as Google Analytics, Facebook Pixel, TikTok Pixel, and Snapchat Pixel.

Storefront emits customer events through those configured integrations. Analytics events are observation and attribution evidence; they are not order, payment, ledger, conversion, refund, or customer authority.

Core rule owner: `02-architecture.md` for derivative evidence and `04-payments.md` where financial/revenue truth is involved.

## WhatsApp

The primary Storefront purpose of WhatsApp is customer phone verification during checkout.

```text
Customer
    ↓
Checkout
    ↓
Enter Phone Number
    ↓
WhatsApp OTP
    ↓
Phone Verification
    ↓
Order Submission
```

POS Admin configures the tenant WhatsApp integration. Storefront uses that admitted tenant integration to send the checkout OTP and verify the customer's phone before checkout completion.

Phone verification is customer identity evidence. It never creates Order authority, Payment authority, Inventory authority, Pricing authority, or Checkout authority. It must not bypass inventory validation, pricing validation, checkout validation, payment lifecycle admission, order lifecycle admission, or tenant/customer ownership checks.

WhatsApp is a communication and verification transport only. Backend Core authority still decides whether checkout succeeds. The same integration may also support approved notification or recovery workflows where admitted by the owning Core domains, but those workflows remain communication only and must not become lifecycle, payment, order, refund, shipping, account, or inventory truth.

Core rule owner: `05-orders.md` for checkout/order admission, `06-security-identity.md` for customer identity evidence and tenant/customer scope, and `07-platform-overview.md` for notification/transport relationships.

## Currency & Tenant Settings

POS Admin controls tenant currency, locale, store settings, branding, and tenant identity inputs through the tenant operational surface.

Storefront never chooses currency. It presents tenant configuration admitted by backend authority. Currency, locale, branding, and tenant identity affect presentation and customer expectations, but cannot be selected from browser state, route state, analytics integration, or Storefront preference when backend tenant authority says otherwise.

Core rule owner: `02-architecture.md` for tenant/store configuration authority, `04-payments.md` for payment/currency consequences, and `06-security-identity.md` for tenant identity/scope.

---

# POS Admin → Storefront Dependency Matrix

| POS Admin Area | Storefront Effect |
| --- | --- |
| Products | Products become visible when backend publication/catalog authority admits them. |
| Categories | Navigation, category pages, filters, and product grouping change. |
| Coupons | Customer may submit/apply coupon intent when backend coupon authority admits it. |
| Promotions | Pricing and promotion presentation change after backend admission. |
| Smart Offers | Popup/offer experiences appear as configured and admitted. |
| Theme | Store appearance changes. |
| CMS | Homepage, landing pages, hero banners, content blocks, and SEO content change. |
| Payment Methods | Checkout payment options change. |
| Shipping Methods | Checkout shipping options, costs, and delivery estimates change. |
| Analytics | Tracking events become active through configured integrations. |
| Commerce AI | Assistant becomes available with configured personality/style/position. |
| Blocked Phones | Customer/account/checkout eligibility changes through backend identity authority. |
| Abandoned Cart Recovery | Recovery workflows become active, but must re-enter live backend authority. |
| Manual Payments | Customer payment proofs become reviewable through backend payment authority. |
| Orders | Incoming customer orders appear in POS Admin for operational processing. |

Use this matrix as dependency mapping only. It is not authority mapping. The Core domains still decide whether a visible Storefront effect is valid.

---

# Storefront Administration Map

This map helps QA understand which POS Admin screen affects which customer-facing Storefront area. It is a dependency map, not an authority map: POS Admin screens submit operational intent, backend authority admits or rejects it, and Storefront presents the admitted result.

| POS Admin Screen | Storefront Effect |
| --- | --- |
| Products | Product Catalog |
| Categories | Navigation & Category Pages |
| Inventory | Availability |
| Orders | Incoming Storefront Orders |
| Coupons | Coupon Engine |
| Promotions | Automatic Discounts |
| Smart Offers | Popup Offers |
| Storefront Settings | Theme, Branding, SEO, Store Identity, Social Links, and Storefront Appearance |
| Payment Methods | Checkout Payment Options |
| Shipping Methods | Shipping Options |
| CMS | Pages & Homepage |
| Commerce AI | Shopping Assistant |
| Analytics Settings | Tracking Integrations |
| Storefront Analytics | Reporting |
| Manual Payments | Wallet Review Workflow |
| Blocked Phones | Checkout Eligibility |
| Abandoned Cart Recovery | Recovery Automation |
| Settings | Tenant Configuration, Currency, Locale, Regional Configuration, and Operational Tenant Settings |

Use this table when planning Storefront QA from the admin side: identify the POS Admin screen that changed the operational input, then verify Storefront presents the admitted customer-facing effect without bypassing the Core authority that owns the business rule.

Do not collapse Storefront Settings into global Settings. Storefront Settings control customer-facing presentation: theme, branding, SEO, store identity, social links, and Storefront appearance. Settings control global tenant configuration: currency, locale, regional configuration, and operational tenant settings. Both are admitted by backend authority before Storefront presents them, but they answer different business questions and must be tested separately.

---

# Operational Change Propagation

Operational changes travel from tenant operations to customer experience through authority and projection:

```text
POS Admin Change
        ↓
Backend Validation
        ↓
Projection / Cache / Read Model Convergence
        ↓
Storefront Update
```

Examples:

- A product is published in POS Admin; backend publication/catalog authority admits it; projections/search/homepage converge; Storefront may show it.
- A coupon is configured in POS Admin; backend promotion/coupon authority admits it; Storefront may present or accept coupon intent; checkout still recomputes final eligibility.
- Stock changes in POS Admin; backend inventory authority admits the mutation; availability projections converge; Storefront may show updated availability; cart/checkout still revalidates current inventory.
- A payment method is disabled in POS Admin; backend payment-method authority changes; Storefront should stop presenting it when current eligibility is loaded; final checkout must reject stale selections.
- A shipping method changes in POS Admin; backend shipping authority admits it; Storefront options update; final checkout must reject stale shipping candidates.
- A theme/CMS/branding change is published in POS Admin; backend tenant/content authority admits it; Storefront presentation updates after convergence.
- A customer order is placed in Storefront; backend creates the authoritative order; POS Admin receives the operational order view and may process allowed lifecycle steps.
- A customer completes checkout; backend validates checkout; inventory reservation is admitted; payment admission occurs; the authoritative Order is created; accounting/events are staged from committed authority; the order appears inside POS Admin; POS Admin begins operational processing; Storefront presents the customer-facing lifecycle.

The customer-checkout example is intentionally cross-domain because checkout is where the largest number of Core domains converge. Inventory, Payment, Orders, and Accounting are admitted by their owning lifecycle services. Storefront and POS Admin consume the resulting authority from different perspectives: Storefront as customer-facing lifecycle presentation, POS Admin as tenant operational processing.

Visibility is not authority. A Storefront page, card, menu, banner, payment option, shipping option, offer, or assistant widget becoming visible only proves that presentation changed. It does not prove the underlying business fact is currently valid until the owning backend authority admits it.

QA should therefore test both sides of propagation:

- Did POS Admin operational intent reach the correct backend authority?
- Did Storefront consume the admitted result without becoming authority?
- If projection/cache/read-model convergence lagged, did cart/checkout/payment/order/customer operations still revalidate current authority?
- If backend authority rejected the operational change, did Storefront withhold, stale-mark, fail closed, or repair instead of presenting false authority?

---

# Customer Request Lifecycle

A customer request moves through Storefront, Core authority, operational projections, and POS Admin workflows. Storefront is the journey surface; the authoritative steps are the backend domain admissions and SQL-backed lifecycle decisions.

```text
Customer
    ↓
Storefront
    ↓
Checkout
    ↓
Inventory Validation
    ↓
Payment Validation
    ↓
Order Creation
    ↓
Accounting / Events
    ↓
Notifications
    ↓
POS Admin Operations
    ↓
Fulfillment / Shipping
    ↓
Customer Delivery
```

| Step | System work | Core guide owner | Authority / evidence role |
| --- | --- | --- | --- |
| Customer | Shopper browses, selects, enters address/payment/coupon intent, and submits actions. | `06-security-identity.md` for identity/tenant/customer authority when authenticated; `08-qa-philosophy.md` for evidence handling. | Presentation/input only. Customer input is intent, not authority. |
| Storefront | Presents public catalog, content, pricing evidence, availability evidence, cart state, checkout steps, payment handoff, order tracking, and account views. | `02-architecture.md`, `07-platform-overview.md`, plus the owning domain guide for the displayed fact. | Presentation and intent orchestration. Storefront is not authoritative. |
| Checkout | Collects cart, address, shipping, payment, coupon, customer/guest, and expected-pricing candidates, then submits them for backend admission. | `05-orders.md`, with `03-inventory.md`, `04-payments.md`, and `06-security-identity.md` for joined authority. | Backend checkout admission is authoritative; Storefront checkout UI is candidate presentation. |
| Inventory Validation | Confirms current product/variant/warehouse/stock/reservation availability before order/payment progression. | `03-inventory.md`. | Authoritative when current backend inventory authority admits the reservation/availability; displayed stock is evidence only. |
| Payment Validation | Confirms method eligibility, amount/currency/payment-fee authority, provider handoff readiness, and payment lifecycle admission. | `04-payments.md`. | Authoritative when payment lifecycle authority admits the payment state; provider/browser output is evidence only. |
| Order Creation | Creates the durable Storefront order and order-item snapshots after checkout authority succeeds. | `05-orders.md`. | Authoritative order step. Storefront confirmation presents the result; it does not create lifecycle authority. |
| Accounting / Events | Stages financial/event consequences from committed authority so projections, ledger, notifications, and operations can converge. | `04-payments.md` for ledger/payment/accounting; `02-architecture.md` for outbox/projection/event boundaries. | Authoritative when derived from committed SQL/lifecycle state; event delivery itself is trigger/evidence. |
| Notifications | Communicates admitted order/payment/shipping/customer state to the customer. | `07-platform-overview.md`, plus `04-payments.md` or `05-orders.md` for the originating fact. | Evidence/presentation only. A sent message does not create commerce truth. |
| POS Admin Operations | Tenant operators see incoming Storefront orders and perform allowed processing, fulfillment, cancellation, refund initiation, review, and support work. | `.ai/hermes/systems/pos-admin/README.md`, `05-orders.md`, `04-payments.md`, `03-inventory.md`. | Operational surface. Authoritative only when backend lifecycle/domain authority admits the operation. |
| Fulfillment / Shipping | Moves eligible orders through fulfillment, carrier/shipping evidence, delivery tracking, and delivery completion where allowed. | `05-orders.md`, with `04-payments.md` for COD/payment effects. | Fulfillment/shipping lifecycle admission is authoritative; carrier/customer-facing tracking is evidence/presentation. |
| Customer Delivery | Customer receives goods and sees delivered/tracking/return/refund-eligible presentation. | `05-orders.md`, `04-payments.md`, `03-inventory.md` where returns/refunds/stock restoration are involved. | Customer-visible completion is presentation unless the owning lifecycle has admitted delivery/payment/refund/return truth. |

QA should read this lifecycle from left to right, but judge authority from the Core domain outward. A customer-facing success page, notification, tracking update, or POS Admin order row is not enough by itself; the disputed fact must be admitted by the owning Core authority.

---

# Authority Boundaries

## What Storefront may present

Storefront may present:

- backend-admitted store configuration, theme, CMS, navigation, SEO, and published content;
- projected catalog/search/homepage/recommendation results when the read path remains subordinate to SQL authority;
- product detail, media, variants, price, promotion, and availability evidence returned by backend reads;
- backend-admitted cart, checkout, pricing, shipping, payment-method, and payment-session DTOs;
- order, payment, shipment, delivery, return, refund, and notification evidence admitted by backend reads;
- customer account/session/profile/order ownership evidence admitted by backend identity authority;
- safe loading, empty, error, stale, pending, retry, review-required, unavailable, and fail-closed states.

## What Storefront may cache

Storefront may cache or store presentation/intent only:

- route state and deep-link candidates;
- selected variant/quantity candidates;
- cart UI state after backend admission, subject to revalidation;
- selected shipping/payment candidates;
- editable address form state;
- checkout step/progress state;
- recovery token candidates or continuation metadata where allowed;
- theme/CMS/navigation/display assets where stale presentation is safe;
- analytics/session presentation data;
- retry intent and user-friendly error context.

Any cached value becomes unsafe when it is used to bypass current backend admission.

## What Storefront may never decide

Storefront may never decide:

- tenant/store authority;
- whether a POS Admin operational change is valid before backend authority admits it;
- customer identity/session validity;
- product/category/publication/media/variant/unit authority;
- product price, discount, tax, shipping cost, payment fee, total, currency, or payable amount;
- stock availability, reservation, release, commit, or return restoration;
- coupon/promotion/smart-offer eligibility, usage, stacking, financial amount, or confirmation;
- shipping method eligibility, final cost, delivery truth, or carrier authority;
- payment method availability, provider success/failure, payment lifecycle state, refund state, settlement, or reconciliation;
- order creation, order status, fulfillment, cancellation, return, refund, or order ownership;
- affiliate attribution admission or commission/payout truth;
- notification delivery as business state;
- analytics/dashboard metrics as order/payment/ledger truth.

## Projection vs SQL authority

Projection model:

- MongoDB read models, Redis caches, Elasticsearch indices, browser state, queue payloads, provider payloads, and frontend DTOs are evidence or acceleration.
- They may lag, be rebuilt, be duplicated, be missing, or be stale by design.
- They must not create, restore, or override business truth.

SQL authority model:

- PostgreSQL and lifecycle owners decide durable business truth.
- SQL commit is the internal durability boundary.
- Critical Storefront reads may repair from SQL when projection is missing/stale and the owning domain allows it.
- Stale projection is not automatically a defect; projection becoming authority is a defect.

---

# Business Rules

High-level durable Storefront system rules:

1. Storefront is a customer intent and presentation system, not a business authority system.
2. Every Storefront mutation must be admitted by backend authority before it becomes durable business state.
3. POS Admin operational changes drive Storefront only after backend authority admits them; Storefront visibility is not proof of operational authority.
4. Storefront checkout must recompute current backend authority; client totals and selections are stale-client assertions.
5. Storefront cart and checkout recovery must re-enter live backend authority or fail closed.
6. Product visibility, search visibility, homepage eligibility, recommendations, CMS links, SEO, and navigation targets must remain subordinate to current SQL publication/configuration/content/category/product authority.
7. Storefront inventory display is advisory until inventory authority validates current stock/reservation/warehouse/variant/product lineage.
8. Storefront payment display must not convert provider/browser evidence into payment finality.
9. Storefront order reads may use projections but must not treat missing/stale projection as missing SQL order authority when a critical read/repair path is required.
10. Storefront customer identity is current backend customer/session authority; JWT/cookies/frontend state are evidence only.
11. Affiliate referral evidence becomes Storefront attribution only after backend admission; URL/header/browser evidence cannot fabricate attribution.
12. Notifications and analytics are outputs/observations; they do not decide orders, payments, inventory, refunds, or ledger truth.
13. Tenant isolation applies to every Storefront read, cache, route, projection, search result, account action, cart, checkout, order, payment, return, and notification.
14. Fail-closed customer friction may be correct when current authority is missing, stale, contradictory, externally uncertain, or tenant-unsafe.
15. Accepted projection lag is not a defect unless it misrepresents authority in a critical read, leaks tenant data, resurrects stale visibility, or bypasses required revalidation.

---

# Never Assume

Common Storefront misconceptions:

- Do not assume Storefront owns truth because the customer sees it first.
- Do not assume Storefront stands alone; most customer-visible configuration begins as POS Admin operational intent admitted by backend authority.
- Do not assume a POS Admin change is visible immediately, or that a visible Storefront effect proves the operational change is still valid.
- Do not assume a product card means checkout must accept the item.
- Do not assume search result visibility means SQL publication/purchasability is current.
- Do not assume homepage/recommendation/CMS/navigation visibility can bypass product/category/content authority.
- Do not assume LocalStorage, sessionStorage, React state, route state, browser history, BFCache, or cached DTOs can restore cart/checkout/payment/order authority.
- Do not assume Redis cart persistence is cart authority.
- Do not assume a selected shipping/payment method is still eligible at checkout submission.
- Do not assume a displayed price, discount, stock count, shipping cost, payment fee, tax, or total is final until backend authority recomputes it.
- Do not assume a coupon code or smart-offer token is valid because the UI accepted it as input.
- Do not assume a provider redirect, SDK success, iframe return, receipt, QR flow, or callback means local payment is paid.
- Do not assume terminal failed guidance is safe for payment states involving late success, suspense, manual review, amount mismatch, quarantine, or reconciliation-required evidence.
- Do not assume a missing order projection means the order does not exist.
- Do not assume delivery means COD cash collection unless Payment admits collection evidence.
- Do not assume notification delivery proves the underlying order/payment/shipping state.
- Do not assume customer token/cookie evidence proves current account authority.
- Do not assume guest cart, customer cart, checkout recovery, or order tracking can transfer ownership without backend authority.
- Do not assume Storefront currency rules apply to CommerceHub marketplace finance, or CommerceHub EGP rules rewrite tenant Storefront currency.
- Do not assume Storefront affiliate attribution applies to CommerceHub marketplace orders.
- Do not assume read-model lag is a bug; ask whether authority was protected and convergence is safe.

---

# QA Checklist

This is a system-understanding checklist, not a feature checklist.

Before testing a Storefront behavior, answer:

- [ ] What customer journey is this? Browsing, search, product view, cart, checkout, payment, order tracking, return/refund, account, notification, analytics, or recovery?
- [ ] Is this behavior driven by a POS Admin operational change? If yes, which backend authority admitted it before Storefront presented it?
- [ ] What business fact is disputed or changing?
- [ ] Which Core domain owns that fact?
- [ ] Which Core guide should be consulted before judging it?
- [ ] Is Storefront presenting backend-admitted evidence, projection evidence, cached state, or local UI state?
- [ ] If the state is projected/cached, is lag accepted, or is this a critical read that must repair/revalidate from SQL?
- [ ] If Storefront submits a mutation, where does backend authority admit or reject it?
- [ ] Are client totals, selected method IDs, route params, referral codes, storage values, or DTOs treated only as candidates?
- [ ] Does checkout re-enter current product, inventory, pricing, coupon/promotion, shipping, payment, customer, tenant, and idempotency authority?
- [ ] Does payment presentation distinguish evidence from finality?
- [ ] Does order tracking compose order/payment/fulfillment/shipping/refund truth without letting one surface decide another domain?
- [ ] Is tenant/customer ownership proven by backend authority, not caller-supplied route/header/token/browser evidence?
- [ ] Does recovery converge to current authority or fail closed instead of reviving stale snapshots?
- [ ] Does the UI communicate blocked/pending/review-required/reconciliation states honestly rather than forcing false certainty?
- [ ] If behavior looks inconvenient, is it a correct fail-closed protection?
- [ ] If behavior looks correct, did it bypass a lifecycle/authority boundary?

---

# Common Failure Scenarios

## Stale search resurrects unavailable products

A product appears in search after SQL publication/category/variant/stock authority no longer allows it.

Business failure: discovery projection became visibility authority.

Expected system behavior: search/rebuild/replay revalidates SQL authority and deletes/tombstones or withholds the result.

## Product detail permits intent from stale purchasability

A customer can add a deleted, unpublished, inactive, invalid-lineage, superseded, or unavailable variant because detail/cart trusted stale product DTOs.

Business failure: product presentation bypassed catalog/inventory authority.

Expected system behavior: cart/checkout revalidates current product/variant/unit/stock authority and rejects or removes the line.

## Cart recovery restores abandoned JSON as authority

A stale abandoned-cart snapshot returns unavailable products, old prices, or old quantities.

Business failure: recovery became cart authority.

Expected system behavior: recovery re-enters live backend cart/product/variant/stock/pricing authority or fails closed.

## Checkout accepts stale client totals

The frontend submits old price, discount, shipping, tax, payment fee, or total values and checkout succeeds.

Business failure: Storefront stale-client evidence became financial authority.

Expected system behavior: backend recomputes and rejects mismatches before order/payment/accounting authority is created.

## Shipping or payment selection remains valid after configuration changes

A selected method is disabled/deleted/foreign-tenant/out-of-limit/stale, but checkout accepts it.

Business failure: candidate selection bypassed current method eligibility authority.

Expected system behavior: preview/final checkout reloads method authority and fails closed or marks selection stale.

## Provider/browser result is presented as payment finality

The UI shows paid/failed from redirect state, SDK result, QR receipt, iframe result, or cached payment status without backend lifecycle admission.

Business failure: external/browser evidence became payment authority.

Expected system behavior: Storefront presents only backend-admitted payment state and preserves pending/review/reconciliation uncertainty where appropriate.

## Missing order projection produces false missing order

Order creation committed in SQL, but Mongo/read model is missing or stale and Storefront reports the order as nonexistent.

Business failure: derivative order read became absence authority on a critical read.

Expected system behavior: critical reads use SQL-backed repair/freshest projection rules where required, or fail closed with temporary unavailable rather than false absence.

## Return/refund request treats customer intent as money authority

A customer return/refund action updates refund/payment state directly or presents refund completion before refund authority admits it.

Business failure: Storefront request flow bypassed return/refund/payment lifecycle ownership.

Expected system behavior: customer intent enters backend return/refund authority; money state remains pending/processing/review/reconciliation until admitted.

## Account/session state survives revocation or tenant mismatch

A stale token/cookie/local profile lets a customer view or mutate cart/order/account data after account disabled, session revoked, tenant changed, or ownership mismatch.

Business failure: frontend/session evidence became identity authority.

Expected system behavior: current backend customer/session/tenant/order ownership authority rejects the action.

## Affiliate referral is forged by URL or storage

A modified referral code, product id, tenant URL, header, or browser storage creates attribution.

Business failure: referral evidence bypassed backend affiliate/storefront admission.

Expected system behavior: only current backend SQL active referral/promotion-link authority admits attribution.

## Notifications mislead about commerce state

A WhatsApp/email/in-app message claims order/payment/shipping completion that the owning lifecycle has not admitted.

Business failure: notification text or delivery became customer-visible false authority.

Expected system behavior: notification content is rebuilt from current SQL/lifecycle authority at send/read time and remains communication only.

## Analytics reports become business truth

Dashboard, KPI, client event, or cached analytics claims revenue/conversion/order truth inconsistent with SQL order/payment/ledger authority.

Business failure: observation/reporting became financial authority.

Expected system behavior: financial/conversion numerators derive from SQL commerce/payment/ledger truth; client traffic remains observation only.

---

# Source Documents

Primary authority and Core guides read:

- `.ai/hermes/INDEX.md`
- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/08-qa-philosophy.md`
- `.ai/hermes/systems/pos-admin/README.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/CODEX_RULES.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/KNOWN_RISKS.md`

Storefront architecture/certification sources read:

- `docs/architecture/storefront-final-end-to-end-authority-certification-findings.md`
- `docs/architecture/storefront-cart-intent-authority-certification-findings.md`
- `docs/architecture/storefront-checkout-authority-certification-findings.md`
- `docs/architecture/storefront-checkout-address-authority-certification-findings.md`
- `docs/architecture/storefront-shipping-method-authority-certification-findings.md`
- `docs/architecture/storefront-payment-method-selection-authority-certification-findings.md`
- `docs/architecture/storefront-checkout-pricing-totals-authority-certification-findings.md`
- `docs/architecture/storefront-checkout-submission-authority-certification-findings.md`
- `docs/architecture/storefront-order-authority-certification-findings.md`
- `docs/architecture/storefront-payment-authority-certification-findings.md`
- `docs/architecture/storefront-customer-authority-certification-findings.md`
- `docs/architecture/storefront-shipping-authority-certification-findings.md`
- `docs/architecture/storefront-promotions-authority-certification-findings.md`
- `docs/architecture/storefront-promotion-financial-authority-certification-findings.md`
- `docs/architecture/storefront-smart-offer-financial-authority-certification-findings.md`
- `docs/architecture/storefront-smart-offer-recovery-audit-authority-certification-findings.md`
- `docs/architecture/storefront-configuration-authority-certification-findings.md`
- `docs/architecture/storefront-content-authority-certification-findings.md`
- `docs/architecture/storefront-theme-authority-certification-findings.md`
- `docs/architecture/storefront-navigation-authority-certification-findings.md`
- `docs/architecture/storefront-versioning-authority-certification-findings.md`
- `docs/architecture/storefront-recovery-rebuild-certification-findings.md`
- `docs/architecture/storefront-whatsapp-authority-certification-findings.md`
- `docs/architecture/storefront-analytics-authority-certification-findings.md`
- `docs/architecture/storefront-order-lineage-admin-presentation-authority-certification-findings.md`
- `docs/architecture/affiliate-promotion-link-resolution-storefront-entry-attribution-admission-certification-findings.md`

Related Core/domain documents referenced rather than rewritten:

- `02-architecture.md` owns SQL authority, derivative state, projection/replay, lifecycle boundaries, tenant resolution, and QA-critical platform rules.
- `03-inventory.md` owns stock, reservation, warehouse, movement, variant/inventory lineage, and projection convergence rules.
- `04-payments.md` owns payment lifecycle, provider evidence, webhooks, refunds, settlement, reconciliation, duplicate prevention, accounting, and outward-money readiness.
- `05-orders.md` owns generic Storefront order authority, checkout, fulfillment, cancellation, returns, refunds, order projections, and the distinction from CommerceHub marketplace orders.
- `06-security-identity.md` owns identity, tenant isolation, current actor/session authority, authorization, dangerous operations, and fail-closed security behavior.
- `07-platform-overview.md` owns cross-system platform mapping.
- `08-qa-philosophy.md` owns the QA reasoning model for authority, evidence, fail-closed behavior, and accepted tradeoffs.
