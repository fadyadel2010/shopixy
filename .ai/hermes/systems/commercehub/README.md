# CommerceHub System Guide

Audience: Senior QA Engineers, AI Agents, and new contributors.

This is a SYSTEM guide, not a domain guide. The Core Guides (`02-architecture.md`, `03-inventory.md`, `04-payments.md`, `05-orders.md`, `06-security-identity.md`, `07-platform-overview.md`, `08-qa-philosophy.md`) own the durable business rules for each domain. This guide does not repeat them. It explains how **CommerceHub** operates as one complete business system and how it *uses* those domains.

Navigate from `.ai/hermes/INDEX.md`.

---

# Purpose

Teach a Senior QA Engineer, AI agent, or new contributor how CommerceHub works as a whole business system — its responsibilities, the people who participate in it, the operational flows that move money and stock, and the authority boundaries it may and may not cross.

CommerceHub is the shared multi-vendor marketplace surface (`PointOfSale.CommerceHub`) over the authoritative marketplace APIs (`api/commercehub/*`). It is the customer-facing marketplace where shoppers discover products from multiple vendors through one discovery/cart and where participating POS tenants sell through a unified marketplace checkout.

This guide answers: what CommerceHub owns, what it triggers, what it may never mutate directly, and how to test it as a system rather than as a screen.

---

# What is CommerceHub?

CommerceHub is the multi-vendor marketplace system where multiple POS tenants sell through one shared customer experience.

## Its purpose

CommerceHub exists to turn backend catalog, publication, inventory, pricing, and shared domain authority into a unified multi-vendor shopping experience:

1. Customer discovers across vendors on CommerceHub; cart groups items by `StoreId`.
2. Marketplace checkout recomputes per-store listing/product/variant/category/vendor/payment/shipping/promo/price/stock from SQL; creates marketplace order + per-store sub-orders (SQL-unique per `MarketplaceOrderId`, `StoreId`).
3. Split-order shipping groups by `StoreId` with deterministic cheapest-method selection.
4. Payments: provider session fenced before external boundary; unmatched webhooks persisted as unresolved SQL evidence.
5. Vendors receive sub-orders and fulfill them independently through POS Admin vendor surfaces.
6. Settlement and payouts flow through shared ledger with EGP-only reconciliation.

## What it owns

CommerceHub owns the marketplace orchestration and presentation:

- Marketplace discovery: search, categories, product listings, vendor visibility.
- Marketplace cart/basket: aggregating items from multiple vendors, presenting vendor-partitioned cart state.
- Split-order checkout: per-vendor sub-order creation, immutable shipping assignment per store, exact reservation ownership per tenant.
- Marketplace order coordination: parent order aggregates, sub-order lifecycle, vendor-scoped fulfillment actions.
- Marketplace payment orchestration: provider-session intent, exact provider selection per store, unresolved-payment evidence capture, and suspense routing. `IPaymentLifecycleService` owns payment truth.
- Marketplace currency constraint: CommerceHub marketplace financial operations are constrained by platform EGP authority for orders, settlements, payouts, refunds, and reconciliation.
- Vendor earnings/settlement visibility: earning snapshots, payout eligibility, settlement status.
- Marketplace notifications: order status, payment updates, fulfillment events.
- Marketplace governance visibility: vendor suppression, COD-debt suspension signals.

## What it does NOT own

CommerceHub does not own business truth:

- PostgreSQL is the single system of record. CommerceHub UI/API state, Mongo projections, Elasticsearch documents, Redis caches, and DTOs are evidence or presentation only.
- Product/variant/catalog authority: owned by POS Admin product system and replicated via publication events.
- Inventory truth: owned by `InventoryConsistencyService` against PostgreSQL. CommerceHub inventory consumers may use Mongo product documents for display metadata, but `InStock`, stock totals, and stock timestamps must be reloaded from PostgreSQL before propagation.
- Payment lifecycle truth: owned by `IPaymentLifecycleService`. Provider callbacks/webhooks are evidence only and are admitted through lifecycle boundaries.
- Order lifecycle truth: owned by marketplace order/sub-order service. Generic `Order` lifecycle rules do not apply to marketplace sub-orders.
- Vendor fulfillment intent: submitted from POS Admin vendor surfaces. Fulfillment mutation authority remains `CommerceHubOrderManagementService` under current SQL order/payment/reservation/vendor lifecycle validation.
- Outward-money readiness: owned by CommerceHub finance + Super Admin readiness gates.
- Platform/global authority: Super Admin scope only. CommerceHub vendor/customer surfaces never escalate to it.
- Cross-tenant seller data: each vendor operates within its tenant scope; customer cart cannot cross-tenant leak.

## How it differs from Storefront

CommerceHub and Storefront are architecturally distinct systems:

| Aspect | Storefront | CommerceHub |
|--------|------------|-------------|
| Tenant model | Single-tenant branded store | Multi-vendor marketplace |
| Order model | Generic `Order` (tenant-owned) | Marketplace order + per-vendor `SubOrder` |
| Currency | Tenant-configured currency | EGP-only platform authority |
| Payment certification | Stripe/Paymob certified | Explicitly excluded from certification |
| Payment uncertainty | Standard webhook handling | Unresolved captured payments block outward money |
| Shipping | Single shipping method | Split-order shipping per store (immutable) |
| Governance | Tenant admin controls visibility | Platform governance controls vendor suppression |
| Fulfillment | Tenant operates own orders | Vendor operates own sub-orders |

## How it differs from POS Admin

CommerceHub vendor operations are scoped to marketplace sub-orders only. POS Admin configures the source data (products, inventory, prices) but does not own or create marketplace orders. The CommerceHub Vendor Module embedded inside POS Admin is the vendor operational surface for tenant-scoped marketplace participation and sub-order intent. In this guide, "POS Admin vendor surface" and "CommerceHub Vendor Module" mean the same embedded operational surface; neither term creates marketplace authority. Vendor surfaces in POS Admin may:

- Review marketplace sub-orders
- Submit allowed fulfillment/cancellation/delivery intent
- View vendor earnings/settlement status

But POS Admin vendor operations cannot:

- Create marketplace orders (customer-facing checkout does)
- Mutate payment lifecycle state
- Execute outward-money payouts without readiness gates
- Access another vendor's sub-orders
- Bypass marketplace order/sub-order authority boundaries

Official POS Admin → CommerceHub relationship flow:

```text
POS Admin
    ↓
CommerceHub Vendor Module
    ↓
Backend SQL/Core/CommerceHub authority
    ↓
CommerceHub Marketplace
```

Reverse operational flow:

```text
CommerceHub Marketplace
    ↓
Backend SQL/Core/CommerceHub authority
    ↓
CommerceHub Vendor Module
    ↓
POS Admin tenant-vendor operator
```

---

# Primary Responsibilities

Each responsibility below says why it exists, which Core domain owns the durable rules, and which guide to consult.

## Marketplace Discovery

Why it exists: Customers must find products from multiple vendors through unified search and browse surfaces.

- Core domain owner: Search/catalog authority plus marketplace governance. SQL `CommerceHubListings`, `Products`, `Categories`, `ProductVariants` ownership; Elasticsearch projection plus SQL authority overlay.
- Consult: `02-architecture.md` (Search projection rules), `03-inventory.md` (inventory lineage for availability), `06-security-identity.md` (governance suppression), `commercehub-product-discovery-certification-findings.md`.
- CommerceHub uses it by: presenting search results filtered by current SQL vendor governance, COD-debt suspension, and product/variant/lineage validity.

## Marketplace Search

Why it exists: Converts customer query intent into multi-vendor product results.

- Core domain owner: Elasticsearch projection plus SQL authority overlay; product/category/variant/stock/publication authority decide visibility.
- Consult: `02-architecture.md`, `03-inventory.md`, `07-platform-overview.md`.
- CommerceHub uses it by: using SQL authority overlay to recompose visibility before search presentation; search cannot create product or listing authority.

## Marketplace Categories

Why it exists: Organizes discovery through category hierarchy and vendor-scoped visibility.

- Core domain owner: SQL category hierarchy and live parent lineage plus marketplace category governance.
- Consult: `02-architecture.md`, `category-lineage-certification-findings.md`, `commercehub-catalog-authority-certification-findings.md`.
- CommerceHub uses it by: presenting category trees where current SQL category/product/lineage and vendor governance admit visibility.

## Marketplace Product Visibility

Why it exists: Makes vendor products discoverable and purchasable with correct pricing/stock evidence.

- Core domain owner: SQL product/variant/stock/price/publication authority plus marketplace listing authority.
- Consult: `02-architecture.md` (projection rules), `commercehub-publication-lifecycle-certification-findings.md`, `commercehub-currency-authority-certification-findings.md`.
- CommerceHub uses it by: publishing products only when SQL proves current publication/stock/price/lineage; non-EGP prices cannot enter CommerceHub.

## Vendor Participation

Why it exists: POS tenants must be able to join and operate as CommerceHub vendors.

- Core domain owner: Marketplace governance authority (SQL-backed visibility/suppression, vendor eligibility).
- Consult: `06-security-identity.md` (governance authority), `commercehub-authority-scope-isolation-certification-findings.md`, `commercehub-actor-resolution-authority-certification-findings.md`.
- CommerceHub uses it by: letting POS Admin vendors publish eligible listings; governance suppression applies across all public surfaces.

## Vendor Governance

Why it exists: Ensures vendor quality, marketplace integrity, and financial viability across marketplace operations.

- Core domain owner: Marketplace governance is platform decision authority for vendor/listing suppression, restoration eligibility, COD-debt suspension, curation/ranking inputs, emergency visibility reduction, and marketplace integrity. Public visibility remains derivative and must pass current SQL listing/product/vendor/category/tenant/price/stock/governance authority. Governance does not approve/edit merchant prices and does not own payment/order/fulfillment truth.
- Consult: `06-security-identity.md`, `commercehub-cod-risk-control-certification-findings.md`.
- CommerceHub uses it by: suppressing financially suspended vendors (COD debt >= 1000 EGP), banned vendors, and governance-hidden listings from search/discovery/detail/homepage/recommendation/autocomplete/checkout eligibility; governance does NOT approve merchant prices.

## Marketplace Customers

Why it exists: Customers need identity and session for marketplace operations.

- Core domain owner: CommerceHub customer identity (`CommerceHubCustomers` + verification/governance state).
- Consult: `06-security-identity.md`, `commercehub-authentication-authority-certification-findings.md`.
- CommerceHub uses it by: requiring login for order history, cart persistence, and checkout; unverified customers cannot authenticate.

## Marketplace Cart

Why it exists: To collect purchase intent across multiple vendors in one shopping session.

- Core domain owner: `CommerceHubCartService` + customer/cart SQL + current listing evidence.
- Consult: `commercehub-cart-certification-findings.md`, `03-inventory.md` (inventory visibility), `05-orders.md` (checkout transition).
- CommerceHub uses it by: validating cart lines against current SQL listings, grouping by `StoreId`, and requiring current stock evidence.

## Split Checkout

Why it exists: To create separate sub-orders for each vendor while maintaining unified customer payment.

- Core domain owner: `CommerceHubCheckoutService` + serializable SQL + request-hash-bound idempotency.
- Consult: `COMMERCEHUB_MARKETPLACE_CHECKOUT_CERTIFICATION.md`, `COMMERCEHUB_MARKETPLACE_CHECKOUT_EDGE_CASES.md`, `05-orders.md` (split-order shipping), `shipping-split-order-authority-certification-findings.md`.
- CommerceHub uses it by: grouping by `StoreId`, recomputing per-store eligibility, creating immutable sub-orders with immutable shipping assignments.

## Marketplace Payments

Why it exists: To collect customer payment and distribute to vendors through settlement.

- Core domain owner: `IPaymentLifecycleService` owns payment lifecycle truth. CommerceHub orchestrates marketplace payment intent and evidence capture only. CommerceHub payment paths are **explicitly excluded** from Stripe/Paymob reference-provider certification.
- Consult: `04-payments.md` (CommerceHub exclusion, unresolved-payment webhooks), `commercehub-checkout-certification-findings.md`.
- CommerceHub uses it by: validating exact provider per store, fencing provider-session handoff before the external boundary, persisting unresolved webhooks as durable SQL evidence, and routing suspense/manual reconciliation for unmatched captured-money evidence. Provider callbacks remain evidence only until lifecycle admission.

## Marketplace Currency

Why it exists: To enforce single-currency settlement for financial clarity.

- Core domain owner: `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (`EGP`) is platform authority.
- Consult: `04-payments.md` (CommerceHub reconciliation), `commercehub-currency-authority-certification-findings.md`, `commercehub-catalog-currency-authority-certification-findings.md`.
- CommerceHub uses it by: rejecting non-EGP at publication/cart/checkout; EGP constraint enforced at database level for financial rows.

## Settlement Readiness

Why it exists: To ensure vendor earnings can be paid out only after financial certainty.

- Core domain owner: Settlement/ledger authority. Live settlement membership is current SQL settlement-order lineage; denormalized settlement header counts are recorded metadata only.
- Consult: `commercehub-payout-reconciliation-certification-findings.md`, `treasury-settlement-authority-certification-findings.md`.
- CommerceHub uses it by: computing vendor debt snapshots, enforcing 1000 EGP suspension threshold, gating payout eligibility on ledger-backed readiness, and failing closed when current settlement-order lineage cannot prove membership.

## Vendor Earnings

Why it exists: Vendors need to track and withdraw earnings from marketplace sales.

- Core domain owner: `CommerceHubEarningsService`, `CommerceHubFinancialWorkflowService`, SQL earnings/payout rows, ledger lineage.
- Consult: `vendor-earnings-financial-authority-certification-findings.md`, `vendor-earnings-lifecycle-authority-certification-findings.md`.
- CommerceHub uses it by: earning states (`Pending`, `Available`, `InPayout`, `PaidOut`, `Reversed`); payouts require readiness gates and external transfer evidence.

## Affiliate Participation

Why it exists: Affiliates can earn commissions from marketplace sales (where certified).

- Core domain owner: Affiliate portal identity plus store-scoped or platform-scoped commission economics.
- Consult: `07-platform-overview.md`, `05-orders.md` (Affiliate-origin order distinction), `03-inventory.md` (reservation conflation warning).
- CommerceHub uses it by: marketplace orders may carry affiliate attribution separate from internal affiliate reservations; commission/payout authority is SQL-authoritative.

## Marketplace Notifications

Why it exists: Communicate marketplace order/payment/fulfillment state to participants.

- Core domain owner: Event-driven notification projection surfaces.
- Consult: `07-platform-overview.md` (Notifications are outputs, not inputs).
- CommerceHub uses it by: triggering notifications from lifecycle events; message delivery cannot mutate commerce truth.

## Marketplace Ratings and Reviews

Why it exists: Customers and vendors need marketplace trust signals without letting review content become commerce authority.

- Core domain owner: CommerceHub marketplace moderation/governance authority for public review visibility, suppression, restoration, rating aggregation admission, and vendor-facing response eligibility.
- Consult: `06-security-identity.md` (governance authority), `07-platform-overview.md` (outputs and communication evidence), and the marketplace governance certification corpus.
- CommerceHub uses it by: presenting admitted ratings/reviews as marketplace trust evidence. POS Admin exposes the Vendor Module review visibility/response surface for the vendor's own marketplace reviews only; vendor review responses are intent/evidence until admitted by CommerceHub governance. Ratings/reviews cannot decide product publication, pricing, stock, checkout eligibility, payment finality, fulfillment state, payout readiness, COD debt, settlement, or tenant accounting.

## Marketplace Analytics

Why it exists: Provide operational insight on marketplace performance.

- Core domain owner: Analytics observes SQL order/payment/ledger truth; financial numerators derive from SQL authority.
- Consult: `02-architecture.md` (analytics are observation).
- CommerceHub uses it by: analytics events are traffic evidence; revenue/conversion authority derives from SQL commerce/payment/ledger truth.

## Marketplace Administration

Why it exists: Super Admin must govern marketplace health and resolve issues.

- Core domain owner: Super Admin platform authority (`RequireSuperAdmin` + SQL validation).
- Consult: `06-security-identity.md` (Super Admin Rules), `super-admin-platform-authority-certification-findings.md`.
- CommerceHub uses it by: enabling platform-governed marketplace moderation, global reindex/repair controls, emergency stops, payout/refund/payment-intake containment, dangerous primitives under governance, and vendor governance actions. Super Admin never bypasses lifecycle owners.

---

# Main Actors

## Customer

CommerceHub customer who shops across vendors. Uses CommerceHub SPA to discover, cart, checkout, pay, track orders, and request returns/refunds. Customer identity (`CommerceHubCustomers`) is global but scoped to their own orders/actions.

## Vendor

POS tenant actor operating merchant/vendor surfaces within CommerceHub. Access through POS Admin but scoped to marketplace sub-orders. Cannot access another vendor's orders/finances. Sub-order fulfillment uses `CommerceHubOrderManagementService`.

## POS Tenant

Tenant who may participate as a vendor in CommerceHub. Product/catalog/inventory/pricing originates in POS Admin; CommerceHub consumes eligible published data. Tenant cannot bypass `StoreId` boundaries on marketplace sub-orders.

## Marketplace Operations

CommerceHub operational services that own marketplace-specific lifecycles: checkout, order, payment (suspense/routing), earnings, settlement, payout readiness, vendor governance, publication lifecycle, listing visibility.

## Super Admin

Platform actor governing the entire marketplace. Can perform marketplace-wide moderation, global reindex/repair, vendor suspension/enforcement, payout execution oversight, and emergency operations. Requires `RequireSuperAdmin` plus current SQL SuperAdmin/TokenVersion/governance validation.

## Affiliate

Separate non-POS-admin identity surface. Can earn commissions from marketplace conversions where certified. Commission/balance/payout are SQL-authoritative; affiliate identity portal is separate from POS operational context.

---

# Customer Journey

## Anonymous browsing

Anonymous customer enters CommerceHub. Presented with public listings from active vendors. Tenant isolation enforced; vendor governance suppression applied. Discovery and search recombine from SQL authority overlay.

## Registration and authentication

Customer registration creates backend SQL customer/governance identity but does not create an authenticated session. Email verification is required before login. Login is the only CommerceHub customer session-creation path; protected routes require current backend customer/session/governance authority. JWTs, route guards, and browser state are evidence only.

## Marketplace discovery

Customer searches, browses categories, views vendor listings, and compares products. Search results filtered by: active vendor governance, COD-debt suspension, valid product/variant/lineage, available stock. Product visibility requires SQL publication + variant lifecycle proof.

## Vendor comparison

Products from different vendors shown together. Vendor info, pricing, stock levels, and shipping options compared. Each product retains its `StoreId` for later split-order grouping.

## Cart

Customer adds items from multiple vendors. Cart groups by `StoreId`. Stock availability validated against current SQL stock lineage. Unavailable items removed/stale-marked. Cart recovery re-enters live backend authority.

## Split checkout

Checkout triggers vendor partition. Per-store checkout recomposition validates: product/variant eligibility, current stock, shipping method per store, payment provider per store, pricing from SQL, promotion/coupon revalidation. EGP-only currency enforced.

## Payment

Customer proceeds to payment. CommerceHub orchestrates payment intent and provider handoff only. Provider session is fenced before the external boundary; provider callbacks are evidence only; unresolved webhooks are persisted to SQL. `IPaymentLifecycleService` owns payment truth. Suspense state blocks retry when external cash truth is uncertain. Stripe/Paymob certification does NOT cover CommerceHub marketplace paths.

## Order tracking

Customer tracks marketplace order and vendor sub-orders. Order status aggregates from sub-order states. Payment status, fulfillment status, shipping status displayed. Critical reads may repair from SQL if projection missing/stale.

## Returns

Customer initiates return. Return workflow requires backend approval and execution trigger. Physical return evidence cannot forge refund success. Return-refund coupling enforced by backend lifecycle.

## Refunds

Refund request validated against original order/payment. Cumulative bound enforced. Refund execution requires ledger-backed readiness. Settlement batch lineage revalidated for post-settlement refunds.

---

# Vendor Journey

## Vendor onboarding

POS tenant opts into marketplace through backend vendor eligibility and governance authority. POS session, tenant lineage, vendor status, debt state, and capability authority are revalidated before protected vendor operations. Marketplace profile creation and first listing publication are admitted by backend authority; frontend/vendor UI state is presentation only.

## Publication

Products configured in POS Admin. Publication flow validates: active tenant, live product/variant, valid stock, current pricing, EGP conversion. Successful publication creates marketplace listing. Unpublish removes listing visibility.

## Marketplace visibility

Vendor products appear in search/category pages. Visibility depends on: active governance, COD debt < 1000 EGP, valid publication lineage, current stock, EGP pricing. Suspended vendors hidden globally.

## Receiving orders

POS Admin vendor surface shows marketplace sub-orders. Sub-order status: `Pending` → `Confirmed` → `Processing` → `Shipped` → `Delivered` → `Completed`. Vendor cannot change parent marketplace order status.

## Fulfillment

Vendor submits fulfillment intent from the POS Admin vendor surface. `CommerceHubOrderManagementService` owns the fulfillment mutation under SQL lifecycle validation. Delivery commit consumes inventory through current lineage. Fulfillment requires paid/COD readiness. Cancellation allowed before terminal fulfillment states. Inventory restoration on cancel requires payment cancellation safety.

## Returns

Vendor receives return requests. Return logistics coordinated. Return completion triggers refund workflow. Return state cannot override sub-order/payment/ledger truth.

## Settlement readiness

Vendor earnings accumulate in `Available` state. COD debt tracked separately. When debt >= 1000 EGP, vendor suspended from new orders and visibility. Reactivation requires debt reduction through settlement payments.

## Payout lifecycle

Vendor requests payout when backend authority reports eligible available earnings. Vendor payout methods are destination intent only; vendor UI cannot execute payouts. Payout eligibility checked against: non-overlapping payouts, positive amount, EGP currency, readiness gates, unresolved external-money ambiguity, refund overlap, and current settlement-order lineage. Super Admin executes payout through the backend payout workflow; external transfer evidence is evidence until lifecycle admission. Payout state: `Pending` → `Processing` → `Completed` (or `ReconciliationRequired`/`Failed`).

---

# Cross-System Relationships

## POS Admin

POS Admin configures source data for marketplace: products, variants, categories, inventory, prices, publication. CommerceHub consumes published listings. Vendor operations in POS Admin are scoped to own sub-orders only. No POS Admin tenant may access another tenant's marketplace data.

## Storefront

Storefront is single-tenant branded commerce. CommerceHub is multi-vendor marketplace. They share some concepts but have separate order models, currency authority (tenant vs EGP-only), and payment certification scope.

## Affiliate

Certified affiliate-origin conversion produces a Storefront generic `Order` with immutable attribution. Do not assume that storefront affiliate authority applies to CommerceHub marketplace sub-orders unless a separate CommerceHub marketplace-affiliate certification explicitly covers it. Commissions/payouts are SQL-authoritative and distinct from inventory "reservations".

## Inventory

Shared inventory authority. CommerceHub reservation must EXACTLY match current SQL order-item authority. Inactive warehouse, superseded variant, or detached stock owner cannot create marketplace reservation. Exact per-tenant ownership/quantity required, not "at least enough".

## Orders

Generic Order (Storefront) vs Marketplace Order + SubOrders. Channel identity fixed at creation. Shared order lifecycle owner for generic orders; CommerceHub order/sub-order service for marketplace.

## Payments

Shared payment lifecycle owner. CommerceHub payment paths excluded from Stripe/Paymob reference certification. Unresolved captured payments block outward money regardless of tenant.

## Shipping

Shared shipment authority. Carrier webhooks authenticated through trust pipeline. Delivery completion feeds marketplace fulfillment. Split-order shipping immutable after creation.

## Accounting

Immutable double-entry ledger. All CommerceHub money effects (commissions, earnings, payouts, settlements) post to ledger through accounting outbox. EGP reconciliation enforced.

## Search

Elasticsearch projection. CommerceHub uses SQL authority overlay (`CommerceHubSearchSqlAuthorityOverlay`). Visibility converges from SQL before search presentation.

## Notifications

Output of lifecycle events. WhatsApp/email/SMS communicate order/payment/fulfillment state. Delivery cannot mutate business truth.

## Super Admin

Platform authority for marketplace governance, global repair, payout execution, and emergency operations. Cannot be reached from vendor/customer surfaces.

---

# How POS Admin Drives CommerceHub

Vendor catalog, inventory, pricing, publication intent, and vendor fulfillment intent originate in POS Admin. CommerceHub consumes those inputs only through the embedded CommerceHub Vendor Module and backend authority admission. The relationship flow is:

```
POS Admin
    ↓
CommerceHub Vendor Module
    ↓
Backend SQL/Core/CommerceHub Authority
    ↓
CommerceHub Marketplace
```

The end-to-end propagation flow is:

```
POS Admin
    ↓
CommerceHub Vendor Module
    ↓
Product/Variant/Inventory/Pricing Publication Intent
    ↓
Backend SQL/Core/CommerceHub Authority Admission
    ↓
CommerceHub Listing Creation (SQL)
    ↓
Marketplace Discovery/Search
    ↓
Customer Cart/Checkout
    ↓
Marketplace Order + SubOrders
    ↓
Vendor SubOrder (POS Admin)
```

Reverse operational flow:

```
CommerceHub Marketplace
    ↓
Marketplace order/sub-order/finance/governance evidence
    ↓
Backend SQL/Core/CommerceHub Authority
    ↓
CommerceHub Vendor Module
    ↓
POS Admin tenant-vendor operator
```

Key operational truths:

- Vendor configures products in POS Admin; CommerceHub lists them only when SQL proves publication/stock/lineage.
- Vendor adjusts inventory in POS Admin; CommerceHub cart/checkout revalidates current stock.
- Vendor prices in POS Admin; CommerceHub checkout converts to EGP-only for marketplace orders.
- Vendor cannot publish directly from CommerceHub; publication is POS Admin operation admitted by SQL.
- Vendor fulfillment of sub-orders happens in the CommerceHub Vendor Module / POS Admin vendor surfaces, scoped to own `StoreId`.

---

# How CommerceHub Differs from Storefront

## Single Tenant vs Multi-Vendor

| Characteristic | Storefront | CommerceHub |
|---------------|------------|-------------|
| Tenant model | One tenant per storefront | Multiple tenants selling through one marketplace |
| Order model | Generic `Order` | `CommerceHubMarketplaceOrder` + `CommerceHubSubOrder` |
| Currency | Tenant-configured | Platform EGP-only |
| Payment certification | Stripe/Paymob certified | Explicitly excluded |
| Governance | Tenant admin | Platform governance |
| Shipping | Single method | Split-order (immutable per store) |
| Payouts | Tenant treasury | Vendor earnings + Super Admin execution |

## Order vs Marketplace Order + Sub Orders

| Aspect | Storefront Order | CommerceHub Order |
|--------|------------------|-------------------|
| Lifecycle | Generic `Order.Status` | Parent aggregates, SubOrder statuses |
| Fulfillment | Direct | Per-vendor SubOrder fulfillment |
| Cancellation | Single order | Multi-vendor partial possible |
| Returns | Direct | Return → Refund coupling |
| Payment | Single payment | Per-store provider selection |

## Tenant Currency vs Marketplace Currency

| Aspect | Storefront/POS | CommerceHub |
|--------|----------------|-------------|
| Currency authority | Tenant `Tenant.Currency` | `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (EGP) |
| Non-EGP handling | Configurable | Rejected/withheld |
| Reconciliation | Tenant currency | EGP-only |

## Store Owner vs Marketplace Governance

| Aspect | Storefront | CommerceHub |
|--------|------------|-------------|
| Visibility control | Tenant admin | Platform governance |
| Price approval | Tenant admin | Governance does NOT approve prices |
| Suspension control | Tenant | Platform (COD debt threshold) |
| Moderation | Tenant scope | Platform-wide moderation |

---

# Marketplace Dependency Matrix

This is a dependency map only. It shows what each area depends on and what consumes its admitted evidence. It does not assign business authority; use the Core Guides for authority ownership.

| Marketplace Area | Depends On | Produces / Exposes | Consumed By |
|-----------------|------------|--------------------|-------------|
| Discovery/Search | SQL-visible listings, product/category/vendor governance, Elasticsearch projection | Candidate discovery results filtered through SQL overlay | CommerceHub SPA, customer views |
| Categories | SQL category lineage and marketplace taxonomy evidence | Navigation/filter candidates | CommerceHub navigation, search filters |
| Listings | POS Admin publication intent, SQL listing lineage, product/variant/price/stock/governance evidence | Public listing candidates after admission | CommerceHub product detail, search |
| Cart | Customer identity/session, current listing evidence, cart intent persistence | Vendor-partitioned cart evidence | CommerceHub cart UI, checkout |
| Checkout | Current cart/listing/product/variant/category/vendor/payment/shipping/promo/price/stock evidence | Marketplace order + sub-order creation result | CommerceHub checkout UI, order views |
| Marketplace Order | SQL marketplace order/sub-order lineage | Order tracking evidence and aggregate status | CommerceHub order views, payment/refund/return/finance flows |
| Sub Orders | SQL sub-order/item/vendor/shipping/payment readiness evidence | Vendor-scoped fulfillment/cancellation/delivery intent surface | POS Admin vendor order views |
| Payment | Backend payment lifecycle authority and provider evidence | Payment status/readiness evidence | CommerceHub payment UI, reconciliation, finance readiness |
| Fulfillment | Sub-order lifecycle validation, vendor scope, payment/COD readiness, inventory lineage | Fulfillment status evidence | POS Admin vendor surfaces, customer tracking |
| Earnings | Paid/delivered order evidence, refund/return effects, ledger workflow | Earnings/readiness evidence | Vendor earnings views, payout calculations |
| Settlements | Ledger-backed settlement evidence and live settlement-order lineage | Settlement readiness evidence | Super Admin payout queue, vendor earnings views |
| Currency | Platform EGP rule | EGP-only financial admission evidence | All marketplace financial operations |
| Payouts | Earnings eligibility, readiness gates, external-transfer evidence | Payout status evidence | Vendor payout history, Super Admin execution UI |
| Notifications | Lifecycle events and admitted status evidence | Delivery/presentation messages | All actors |
| Ratings / Reviews | Marketplace review governance, moderation state, customer order/review eligibility, vendor response admission | Public trust evidence and vendor response visibility | CommerceHub product/vendor surfaces, POS Admin Vendor Module review surface |

---

# Marketplace Operational Flow

This section separates business, authority, event/projection, and financial movement so readers do not treat one flow as another.

## Business Flow

```
Vendor
    ↓
Publish Products (POS Admin operational surface)
    ↓
Marketplace Discovery
    ↓
Customer Cart
    ↓
Split Checkout
    ↓
Marketplace Order + Vendor SubOrders
    ↓
Sub Orders (Per-vendor, immutable shipping)
    ↓
Vendor Fulfillment Intent (POS Admin vendor surfaces)
    ↓
Settlement / Earnings / Payout Journey
```

## Authority Flow

```
POS product/catalog/pricing/inventory SQL authority
    ↓
Publication admission + marketplace listing SQL authority
    ↓
Cart intent revalidated against current SQL evidence
    ↓
Checkout authority under serializable SQL transaction
    ↓
Marketplace order + sub-order SQL authority
    ↓
Payment lifecycle authority (`IPaymentLifecycleService`)
    ↓
Fulfillment authority (`CommerceHubOrderManagementService`)
    ↓
Refund/return/settlement/payout lifecycle and ledger authority
```

## Event Flow

```
SQL commit
    ↓
Outbox / marketplace event intent
    ↓
Async consumers / projections / notifications
    ↓
Replay-safe convergence from current SQL authority
```

## Projection Flow

```
SQL listing/order/payment/fulfillment truth
    ↓
Mongo / Elasticsearch / Redis / notification projections
    ↓
SQL overlay / critical-read repair / freshness selection
    ↓
CommerceHub presentation
```

## Financial Flow

```
Paid/delivered marketplace sub-order evidence
    ↓
Vendor earnings lifecycle (Pending → Available → InPayout → PaidOut/Reversed)
    ↓
Settlement readiness and live settlement-order lineage
    ↓
Payout readiness gates
    ↓
Super Admin payout execution through backend workflow + external transfer evidence
    ↓
Reconciliation / ledger convergence
```

---

# Marketplace Administration Map

This is a **DEPENDENCY MAP**, not an Authority Map. It shows operational surfaces, dependencies, produced evidence/intent, and consumers. Authority owners remain in the Core Guides and owning certifications.

## Operational Areas and Dependencies

| Marketplace Area | Operational Surface | Depends On | Produces / Exposes | Consumed By |
|---|---|---|---|---|
| Marketplace Dashboard | Super Admin UI | Current platform actor authority, marketplace configuration evidence | Operational dashboard evidence | CommerceHub administration UI |
| Vendor Management | POS Admin vendor surface | Vendor onboarding/governance evidence | Vendor profile/eligibility intent | POS Admin vendor surfaces, CommerceHub vendor profiles |
| Vendor Governance | Super Admin UI | Current platform governance, COD debt, suspension evidence | Suppression/reactivation/governance decisions | CommerceHub discovery/search, POS Admin vendor operations |
| Marketplace Categories | Super Admin UI / category services | SQL category lineage and taxonomy evidence | Navigation/filter candidates | CommerceHub navigation, search filters |
| Marketplace Listings | POS Admin vendor surface | Publication intent, listing/product/variant/category/vendor/price/stock evidence | Listing candidates after admission | CommerceHub product detail, search results |
| Marketplace Orders | CommerceHub customer surface | Checkout admission and SQL marketplace order/sub-order lineage | Order tracking evidence | CommerceHub order views, customer tracking |
| Marketplace Sub Orders | POS Admin vendor surface | SQL sub-order/vendor/payment/shipping evidence | Fulfillment/cancellation/delivery intent | POS Admin vendor order views |
| Marketplace Payments | CommerceHub customer surface | Payment lifecycle authority and provider evidence | Payment presentation/reconciliation evidence | CommerceHub payment UI, Super Admin reconciliation |
| Marketplace Settlements | Finance / Super Admin surfaces | Ledger evidence and live settlement-order lineage | Settlement readiness evidence | Super Admin payout queue, vendor earnings views |
| Vendor Earnings | Vendor finance surface | Order/payment/refund/return/ledger evidence | Earnings/readiness evidence | Vendor earnings views, payout calculations |
| Payout Queue | Super Admin UI | Earnings eligibility, readiness gates, external-transfer evidence | Payout execution intent/status evidence | Super Admin execution UI, vendor payout history |
| COD Debt Management | Super Admin / finance surfaces | COD receivable/debt evidence | Vendor suppression/payout blocking evidence | Vendor suppression, payout blocking |
| Marketplace Notifications | Notification surfaces | Lifecycle events | Delivery/presentation messages | All actors |
| Marketplace Ratings / Reviews | CommerceHub customer surface, POS Admin Vendor Module, Super Admin moderation | Review eligibility, moderation/governance evidence, vendor response admission | Rating/review trust evidence and vendor response visibility | CommerceHub public surfaces, POS Admin vendor review views, governance dashboards |
| Marketplace Analytics | Analytics dashboards | SQL order/payment/ledger truth and derivative traffic events | Observational metrics | CommerceHub analytics dashboards, Super Admin reports |
| Marketplace Search | CommerceHub search surface | Elasticsearch projection plus SQL overlay | Candidate search results | CommerceHub discovery UI, search results |
| Marketplace Moderation | Super Admin UI | Platform governance evidence | Suppression/restore/curation intent | CommerceHub discovery suppression, vendor visibility |
| Marketplace Reports | Super Admin reporting | SQL analytics/financial evidence | Platform oversight reports | CommerceHub reporting, platform oversight |

---

# How Super Admin Drives CommerceHub

CommerceHub has **TWO** operational drivers:

```
POS Admin
    ↓
Vendor Operations

Super Admin
    ↓
Platform Operations
```

These are separate operational paths. POS Admin is the vendor operational surface. Super Admin is the platform operational surface. Neither path bypasses backend lifecycle owners or SQL authority.

## Super Admin Influences

### Vendor Governance

- **Authority**: Platform governance decision authority for vendor eligibility, suppression, ban/reactivation, COD-debt controls, and marketplace integrity
- **Action**: Sets marketplace-wide vendor policies, reviews vendor quality metrics, issues suppression/reactivation decisions
- **Effect**: Governance decisions feed public visibility overlays; vendor suspension applies across all public surfaces

### Vendor Suspension

- **Authority**: Platform governance (COD debt threshold, violation enforcement)
- **Action**: Suspends vendors whose COD debt >= 1000 EGP or for policy violations
- **Effect**: Vendors immediately hidden from search/discovery; sub-orders still process but no new orders accepted

### Marketplace Visibility

- **Authority**: Visibility is derivative from current SQL listing/product/vendor/category/tenant/price/stock/governance authority
- **Action**: Curates, suppresses, restores, ranks, or reduces visibility only through governance decisions and SQL-recomposed overlays
- **Effect**: Global visibility changes propagate to discovery surfaces only after current SQL authority admits them

### Platform Repair

- **Authority**: Operational convergence authority under current Super Admin platform authority and runbook scope
- **Action**: Runs repair, reconciliation, projection rebuilds, diagnostics, and data-correction workflows that re-enter owning lifecycle/SQL authority
- **Effect**: Restores system consistency by recomposing from SQL; never copies derivative state back into truth

### Global Search Rebuild

- **Authority**: Operational reindex authority; SQL overlay decides visible search truth
- **Action**: Triggers full reindex of Elasticsearch from SQL authority
- **Effect**: Search results converge to current SQL state

### Emergency Operations

- **Authority**: Operational emergency authority only; Super Admin NEVER bypasses lifecycle owners
- **Action**: May freeze commerce/payment intake, suppress vendors, suppress listings, stop payout execution, stop refund execution, run repair, run reconciliation, reindex, diagnose, and execute dangerous primitives under governance/runbook scope
- **Effect**: Contains incidents and routes recovery through owning lifecycle/reconciliation/ledger/inventory authority

Super Admin may NEVER directly mutate:

- Marketplace Orders
- Marketplace Sub Orders
- Payment finality
- Refund success
- Fulfillment state
- Inventory truth
- Ledger truth
- Settlement truth
- Payout completion

### Marketplace Moderation

- **Authority**: Platform content and marketplace integrity governance
- **Action**: Reviews listings/content, handles violations, suppresses/restores only when current listing/product/vendor/category authority admits the action
- **Effect**: Moderation decisions feed suppression/restoration evidence; they do not create product, price, order, payment, or fulfillment truth

### Settlement Execution

- **Authority**: Settlement/ledger lifecycle authority; live settlement-order lineage is membership authority
- **Action**: Reviews settlement readiness and admitted settlement evidence through backend finance workflows
- **Effect**: Enables payout eligibility only when ledger-backed settlement evidence and live order membership are proven; `PaymentSettlementBatch.OrderCount` is metadata only

### Payout Execution

- **Authority**: Payout service + readiness gates + Super Admin operational execution
- **Action**: Executes vendor payouts through backend workflow and records external transfer evidence
- **Effect**: Vendor earnings move to completed/paid state only after lifecycle admission; external transfer evidence alone is not payout completion authority

### Platform-wide Configuration

- **Authority**: Platform configuration tables
- **Action**: Sets platform-wide settings (commission rates, thresholds, emergency stops, governance rules)
- **Effect**: Changes propagate to all vendor operations

### Dangerous Primitives and Audit

- **Authority**: Current SQL Super Admin authority plus documented runbook/governance scope
- **Action**: Direct SQL/bulk mutation, full projection replacement, raw search writes, Redis pattern invalidation, raw bus publish, all-tenant repair, support/tenant override, or manual financial repair only where explicitly reviewed
- **Effect**: Must be scoped, audited, replay-safe, and derivative-safe; dangerous primitives are operational tools, not business authority

## Authority Distinction

**Vendor Operational Surface** (POS Admin path):
- Submits product/variant/catalog configuration intent
- Submits inventory-management intent for owned listings through inventory authority
- Submits sub-order fulfillment/cancellation/delivery intent
- Submits pricing/publication intent within EGP constraints

**Platform Authority** (Super Admin path):
- Owns vendor eligibility and visibility
- Owns COD debt thresholds and suspension
- Owns emergency stops, governance decisions, global search/reindex operations, dangerous primitive governance, platform audit, and platform-wide configuration/repair scope
- Operates payout execution workflows, but payout completion, settlement truth, ledger truth, and outward-money readiness remain backend lifecycle/ledger authority

---

# Marketplace Operational Change Propagation

Business changes propagate through distinct authority layers. This is a propagation map, not a lifecycle or authority override path; authority and financial flows are separated in the Marketplace Operational Flow section above.

```
Vendor publishes product
    ↓
Marketplace Listing
    ↓
Search
    ↓
Discovery
    ↓
Customer
    ↓
Cart
    ↓
Checkout
    ↓
Marketplace Order
    ↓
Sub Orders
    ↓
Settlement
    ↓
Vendor Earnings
    ↓
Analytics
    ↓
Notifications
```

## Authority Categories

| Step | Category | Description |
|---|---|---|
| Vendor publishes product | Operational intent → authority admission | POS Admin submits publication intent; backend publication/listing authority admits or rejects it |
| Marketplace Listing | Authority | SQL listing becomes discoverable only when current product/category/vendor/price/stock/governance admission passes |
| Search | Projection | Elasticsearch indexed from SQL; search overlay recomputes visibility |
| Discovery | Presentation | CommerceHub presents search results filtered by current authority |
| Customer | Presentation | Customer selects from available discovery results |
| Cart | Intent/evidence boundary | Cart stores purchase intent and validates against current SQL listings; stock revalidated at checkout |
| Checkout | Authority | Serializable SQL creates marketplace order + sub-orders after recomputing totals/shipping/payment/stock eligibility |
| Marketplace Order | Authority | SQL marketplace order + sub-orders hold immutable snapshots and lifecycle lineage |
| Sub Orders | Authority + operational surface | Backend order/fulfillment services own transitions; POS Admin vendor surface submits fulfillment intent only |
| Settlement | Financial authority | Ledger-backed settlement consumes admitted order/payment/fulfillment/refund evidence and live settlement-order lineage |
| Vendor Earnings | Financial authority | SQL earnings rows created/transitioned with ledger linkage and payout-readiness gates |
| Analytics | Observation | Analytics events record business facts from SQL authority |
| Notifications | Projection/Event-driven | Notifications dispatched from lifecycle events |

---

# Vendor Operational Model

The complete operational loop for vendors in CommerceHub spans backend authority and frontend operational surfaces.

```
Vendor
    ↓
POS Admin
    ↓
Marketplace Publication
    ↓
CommerceHub
    ↓
Marketplace Customer
    ↓
Marketplace Order
    ↓
Vendor Fulfillment
    ↓
Settlement
    ↓
Payout
    ↓
Vendor Dashboard
```

## Stage Responsibilities

| Stage | Responsibility |
|---|
| Vendor | Owns POS tenant business intent: products, inventory, pricing, catalog configuration through backend authorities |
| POS Admin | Operational surface for vendor: configures/controls source data, publishes to marketplace |
| Marketplace Publication | SQL-backed publication service creates `CommerceHubListing` with governance admission |
| CommerceHub | Discovers, carts, checks out customer; creates marketplace order + sub-orders |
| Marketplace Customer | Purchases products, pays unified checkout, receives split-order shipping |
| Marketplace Order | Parent order aggregates; sub-orders delegated to owning vendors |
| Vendor Fulfillment | Vendor submits fulfillment intent through POS Admin; `CommerceHubOrderManagementService` owns fulfillment mutation and cannot access other vendors' sub-orders |
| Settlement | Ledger workflow posts vendor earnings/readiness; live settlement-order lineage proves membership; COD debt tracked separately |
| Payout | Super Admin executes payout workflow; vendor earnings become completed/paid only after lifecycle admission of transfer evidence |
| Vendor Dashboard | Presents vendor earnings, payout history, sub-order status, settlement eligibility |

---

# Customer Lifecycle

Customer identity and participation in CommerceHub progresses through these conceptual journey states. This is not a single backend lifecycle state machine.

```
Anonymous Visitor
    ↓
Verified Customer
    ↓
Buyer
    ↓
Return Request
    ↓
Refund
```

## Backend Ownership

| Transition | Backend Authority |
|---|
| Anonymous Visitor → Verified Customer | Customer identity/governance authority; registration creates no authenticated session and email verification is required before login (`06-security-identity.md`) |
| Verified Customer → Buyer | Current customer session/governance + marketplace order/payment authority (`05-orders.md`, `04-payments.md`) |
| Buyer → Return Request | Order lifecycle + return workflow authority (`05-orders.md`, `admin-order-lifecycle-authority-certification-findings.md`) |
| Return Request → Refund | Payment/refund lifecycle authority (`04-payments.md`) |

---

# Vendor Lifecycle

Vendor participation in CommerceHub follows conceptual participation stages. These stages combine governance, publication, operation, and finance readiness; each transition must re-enter its owning backend authority.

```
Prospective Vendor
    ↓
Approved Vendor
    ↓
Published Vendor
    ↓
Active Vendor
    ↓
Suspended Vendor
    ↓
Reactivated Vendor
    ↓
Settlement Eligible
    ↓
Paid Vendor
```

## Participation Transitions

| Transition | Trigger Condition | Authority Owner |
|---|---|---|
| Prospective → Approved | POS Admin opts tenant into marketplace | Vendor onboarding governance |
| Approved → Published | First listing published successfully | Publication service + SQL `CommerceHubListings` |
| Published → Active | First admitted marketplace order/fulfillment activity | Marketplace order lifecycle + `CommerceHubOrderManagementService` |
| Active → Suspended | COD debt >= 1000 EGP or policy violation | Platform governance authority |
| Suspended → Reactivated | COD debt reduced below threshold + manual review | Super Admin governance |
| Reactivated → Settlement Eligible | Earnings available + COD debt < threshold | Ledger/settlement authority |
| Settlement Eligible → Paid Vendor | Payout workflow completed after admitted external evidence | Payout service + Super Admin operational execution |

---

# Marketplace Listing Lifecycle

A listing progresses through distinct visibility states with different authority owners for each transition.

```
Draft
    ↓
Published
    ↓
Indexed
    ↓
Visible
    ↓
Suppressed
    ↓
Hidden
    ↓
Removed
```

## Authority Ownership

| Transition | Authority Type | Description |
|---|---|---|
| Draft → Published | SQL Authority | POS Admin publication creates SQL `CommerceHubListing` with governance admission |
| Published → Indexed | Projection Convergence | Elasticsearch projection consumes SQL listing via publication events |
| Indexed → Visible | SQL Authority | Search overlay recomputes visibility from SQL governance + stock authority |
| Visible → Suppressed | SQL Authority | Platform governance (COD debt, violations) suppresses listing in SQL |
| Suppressed → Hidden | Projection Convergence | Elasticsearch updates to hide suppressed listing |
| Hidden → Removed | SQL Authority | POS Admin unpublish or governance action removes listing from SQL |

---

# Common Developer Misconceptions

These complement the "Never Assume" section. They are common architecture mistakes when working with CommerceHub.

| Misconception | Reality |
|---|
| Treating Search as authority | Search is Elasticsearch projection; SQL `CommerceHubSearchSqlAuthorityOverlay` decides visibility |
| Treating Marketplace Orders as Storefront Orders | Marketplace uses parent/sub-order model; Storefront uses generic `Order`. Different lifecycle rules apply |
| Assuming Vendor can mutate another Vendor | `StoreId` scope enforced everywhere; SQL prevents cross-tenant access |
| Assuming Payment success equals settlement | Payment lifecycle owns status; settlement/payout require readiness gates and ledger proof |
| Assuming Projection equals Truth | Projections (Mongo, Redis, ES) are evidence/lag by design; SQL is the authority |
| Assuming COD debt suspends immediately | Suspension requires COD debt >= 1000 EGP threshold and SQL governance update |
| Assuming Payout = Paid Out | Payout moves to `InPayout` then `PaidOut` only after external transfer evidence |
| Assuming Vendor suspension kills sub-orders | Suspended vendors cannot accept NEW orders but existing sub-orders still fulfill |
| Assuming Search lag is a bug | Search lag is acceptable unless it resurrects stale visibility or leaks tenant data |
| Assuming Cart stock is reservable | Cart stock is presentation; checkout revalidates current SQL stock for reservation |

---

# Authority Boundaries

## What CommerceHub may present

- Backend-admitted product listings, categories, search results
- Vendor-partitioned cart state after backend validation
- Marketplace order/sub-order status aggregated from SQL
- Payment status from backend payment authority
- Vendor earnings/settlement values from financial workflow
- Customer order history, detail, and tracking views
- Governance suppression signals (vendor not visible)

## What CommerceHub may cache

- Route state and deep-link candidates
- Selected vendor/product candidates
- Cart UI state after backend admission
- Checkout step/progress state
- Recovery token candidates
- Theme/display assets
- Analytics/session presentation data
- Retry intent and user-friendly error context

Any cached value becomes unsafe when used to bypass current backend admission.

## What CommerceHub may never decide

- Product/variant/publication/marketplace listing authority
- Inventory stock availability, reservation, commit, or restoration
- Payment lifecycle state, provider outcome, or reconciliation
- Order/sub-order lifecycle transitions
- Vendor earnings, settlement eligibility, or payout state
- Affiliate commission or payout truth
- Notification delivery as business state
- Analytics/metrics as order/payment/ledger truth
- Currency authority (EGP-only platform rule)
- Tenant/vendor scope boundaries

## SQL vs Projection Authority

**Projection model:**
- MongoDB read models, Redis caches, Elasticsearch indices, browser state, queue payloads, provider payloads, and frontend DTOs are evidence or acceleration.
- They may lag, be rebuilt, be duplicated, be missing, or be stale by design.
- They must not create, restore, or override business truth.

**SQL authority model:**
- PostgreSQL and lifecycle owners decide durable business truth.
- SQL commit is the internal durability boundary.
- Critical CommerceHub reads may repair from SQL when projection is missing/stale.
- Stale projection is accepted unless it violates SQL-recomposition rule.

---

# Business Rules

High-level durable CommerceHub system rules:

1. CommerceHub is a marketplace orchestration and presentation system, not a business authority system.
2. Every CommerceHub mutation must be admitted by backend authority before it becomes durable business state.
3. Marketplace order model is distinct from generic Storefront order; sub-orders own fulfillment lifecycle.
4. Split-order shipping assignment is immutable after creation; Vendor A's method/cost cannot bind Vendor B.
5. Marketplace reservation quantity must EXACTLY match current SQL order-item authority; "at least enough" is rejected.
6. CommerceHub currency is EGP-only platform authority; non-EGP prices are rejected or withheld.
7. Stripe/Paymob reference-provider certification covers POS Admin + Storefront only, not CommerceHub marketplace payment paths.
8. Unresolved captured-payment evidence must be durably persisted as SQL evidence; it blocks retry/outward-money readiness until reconciliation resolves it.
9. COD debt >= 1000 EGP suspends vendor from new order acceptance and public visibility; reactivation requires debt reduction.
10. Vendor earnings/payouts are EGP-only and readiness-gated; outward money movement fails closed under uncertainty.
11. Tenant isolation applies to every CommerceHub read, cache, route, projection, search result, order, payment, and notification.
12. Fail-closed customer friction may be correct when current authority is missing, stale, contradictory, externally uncertain, or tenant-unsafe.
13. Accepted projection lag is not a defect unless it resurrects stale visibility, leaks tenant data, or bypasses required revalidation.
14. Settlement header counts are metadata only; live settlement-order lineage proves settlement membership.
15. Super Admin emergency operations contain, diagnose, repair, reindex, and stop intake/outward-money execution; they never directly mutate lifecycle truth.

---

# Never Assume

Common CommerceHub misconceptions when testing:

- Do not assume CommerceHub owns truth because the customer sees it first.
- Do not assume a merchant price override exists in CommerceHub; it is disabled until fully certified (PASS 13.6).
- Do not assume CommerceHub payment is as assured as storefront payment; marketplace paths are excluded from reference certification.
- Do not assume a marketplace reservation "is enough" if quantity >= order quantity; exact ownership and exact quantity required.
- Do not assume non-EGP CommerceHub order is legitimate; marketplace currency is EGP sovereign.
- Do not assume vendor suspension only hides products; search, autocomplete, suggestions, and recommendations must suppress suspended vendors.
- Do not assume sub-order shipping can be changed after creation; split-order shipping is immutable.
- Do not assume unresolved captured payment can be retried blindly; suspense state requires reconciliation.
- Do not assume a vendor can fulfill another vendor's sub-order; `StoreId` scope enforced.
- Do not assume marketplace governance can approve merchant prices; governance controls platform governance decisions and visibility inputs, not merchant price truth.
- Do not assume marketplace governance is only passive visibility; it is platform decision authority for suppression, restoration eligibility, COD-debt suspension, curation/ranking inputs, emergency visibility reduction, and marketplace integrity, while public visibility remains SQL-derivative.
- Do not assume frontend price/discount/amount can be trusted; backend recomputes and rejects stale-client assertions.
- Do not assume Mongo/Redis/ES stock values are current; they lag by design.
- Do not assume missing Mongo order doc means order does not exist; SQL-backed repair may be required.
- Do not assume Storefront affiliate authority applies to CommerceHub marketplace sub-orders unless a separate CommerceHub marketplace-affiliate certification explicitly covers it.
- Do not assume read-model lag is a bug; ask whether authority was protected and convergence is safe.
- Do not assume Super Admin can override order, payment, refund, fulfillment, inventory, ledger, settlement, or payout truth; emergency operations must route through lifecycle owners.
- Do not assume `PaymentSettlementBatch.OrderCount` proves settlement membership; current settlement-order lineage proves membership.
- Do not assume provider callback success is payment finality; provider callbacks are evidence until `IPaymentLifecycleService` admits the transition.
- Do not assume ratings, reviews, or vendor responses are order, payment, fulfillment, payout, pricing, stock, COD-debt, settlement, or tenant-accounting authority; they are marketplace trust/moderation evidence only.

---

# QA Checklist

System-understanding checklist before testing CommerceHub behavior:

- [ ] What marketplace journey is this? Discovery, cart, checkout, payment, order tracking, return/refund, vendor fulfillment, payout, notification, analytics, recovery?
- [ ] Is this behavior driven by POS Admin operational data? If yes, which backend catalog/inventory/pricing authority admitted it?
- [ ] What business fact is disputed or changing?
- [ ] Which Core domain owns that fact?
- [ ] Which Core guide should be consulted before judging it?
- [ ] Is CommerceHub presenting backend-admitted evidence, projection evidence, cached state, or local UI state?
- [ ] If state is projected/cached, is lag accepted, or is this a critical read requiring SQL repair?
- [ ] If CommerceHub submits a mutation, where does backend authority admit or reject it?
- [ ] Are client totals, selected method IDs, route params, storage values, or DTOs treated only as candidates?
- [ ] Does checkout re-enter current product, inventory, pricing, coupon/promotion, shipping, payment, customer, tenant, and idempotency authority?
- [ ] Does payment presentation distinguish evidence from finality?
- [ ] Does order tracking compose order/payment/fulfillment truth without letting one surface decide another domain?
- [ ] If ratings/reviews or vendor responses are involved, are they treated as marketplace trust/moderation evidence rather than commerce, finance, fulfillment, or tenant-accounting authority?
- [ ] Is tenant/vendor/customer scope proven by backend authority, not caller-supplied route/header/token/browser evidence?
- [ ] Does recovery converge to current authority or fail closed instead of reviving stale snapshots?
- [ ] Does the UI communicate blocked/pending/review-required/reconciliation states honestly rather than forcing false certainty?
- [ ] If behavior looks inconvenient, is it a correct fail-closed protection?
- [ ] If behavior looks correct, did it bypass a lifecycle/authority boundary?

---

# Common Failure Scenarios

## Stale search resurrects unavailable products

A product appears in CommerceHub search after SQL vendor governance/stock/lineage no longer allows it. Business failure: discovery projection became visibility authority. Expected behavior: search reindex triggers SQL recomposition and tombstones/deletes the result.

## Marketplace reservation "at least enough"

A reservation quantity >= order quantity but ownership/quantity don't exactly match current SQL order item. Business failure: marketplace reservation validation bypassed exact-match rule. Expected behavior: rejected; exact ownership + exact quantity required.

## Vendor shipping reassigned to wrong vendor

Checkout or later mutation reassigns split-order shipping from Vendor A to Vendor B. Business failure: split-order immutability violated. Expected behavior: rejected; sub-order shipping immutable after creation.

## Non-EGP CommerceHub order/reconciliation

USD/EUR marketplace order or reconciliation group appears. Business failure: currency control bypassed. Expected behavior: EGP-only; rejected at creation and withheld at read/projection.

## Unmatched CommerceHub webhook dropped

Provider callback with no linked local payment row is dropped, losing unresolved cash truth. Business failure: unresolved evidence not persisted. Expected behavior: durable SQL evidence is persisted before retryable failure and outward money remains blocked until reconciliation.

## Read-model shows "order not found"

Mongo order doc missing though SQL order exists. Business failure: derivative read became absence authority. Expected behavior: SQL-backed repair for critical reads; temporary unavailable presentation, not false absence.

## Suspended vendor still discoverable

Vendor COD debt >= 1000 EGP but still appears in search/suggestions/recommendations. Business failure: governance suppression not applied. Expected behavior: `CommerceHubSearchSqlAuthorityOverlay` and reindex suppress the vendor.

## Missing Mongo order docs

Two order projection docs for one SQL order. Business failure: duplicate projection can mislead. Expected behavior: critical reads select freshest by freshness markers.

## Vendor ban not suppressing listings

Banned vendor products still in CommerceHub search. Business failure: governance not enforced. Expected behavior: governance suppression before public return.

## Frontend role injection via DevTools

Role/permission changed via browser dev tools. Business failure: frontend became authority. Expected behavior: every API call crosses backend authority; role injection changes presentation only.

## Unresolved captured payment blocks payouts

Unmatched provider success webhook with no tenant scope blocks all outward money. Business failure: unresolved evidence affects readiness. Expected behavior: resolve through reconciliation or provide tenant scope.

## Currency mismatch in payout queue

Non-EGP CommerceHub orders in payout queue. Business failure: currency guard bypassed. Expected behavior: EGP-only validation; non-EGP treated as anomaly.

## Super Admin direct lifecycle override

Operator attempts to manually flip marketplace order/sub-order/payment/refund/fulfillment/inventory/ledger/settlement/payout truth during an incident. Business failure: operational emergency authority bypassed lifecycle owners. Expected behavior: rejected; Super Admin may stop intake/outward-money execution, suppress, diagnose, repair, reconcile, reindex, or run governed primitives only through owning authority/runbook paths.

## Settlement count treated as membership truth

Settlement header count matches expected order count but live settlement-order rows are missing, duplicated, deleted, or foreign. Business failure: recorded metadata became settlement authority. Expected behavior: fail closed to retry/reconciliation-required; live settlement-order lineage proves membership.

## Provider success treated as payment finality

Payment provider callback reports success but local payment/order lineage or lifecycle admission is missing. Business failure: external evidence became payment truth. Expected behavior: callback remains evidence, unresolved cash is persisted, retry/outward-money readiness is blocked, and `IPaymentLifecycleService`/reconciliation admits or rejects the transition.

## Vendor stale capability after suspension or debt block

Vendor remains able to publish, accept orders, or operate protected CommerceHub surfaces after suspension, ban, or COD debt block. Business failure: stale frontend/session capability became operating authority. Expected behavior: backend authority loss purges/revalidates protected presentation; new-order/public visibility and operating capabilities fail closed.

## Emergency rollback copies projection into SQL truth

Operator restores Mongo, Elasticsearch, Redis, or browser-visible state into SQL during recovery. Business failure: derivative state became authority. Expected behavior: recovery recomposes derivative state from SQL authority and lifecycle owners, not the reverse.

---

# Source Documents

Authority precedence: `.ai-context/*` > owning Core guide > `docs/architecture/*` certifications > source code > README/comments.

Core guides (read first):
- `.ai/hermes/INDEX.md`
- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/08-qa-philosophy.md`

Authoritative corpus:
- `.ai-context/ARCHITECTURE_TRUTH.md` — SQL authority, lifecycle, CommerceHub financial authority
- `.ai-context/PROJECT_CONTEXT.md` — subsystem ownership, financial/inventory/order authority tables
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md` — cross-system hardening and recovery rules
- `.ai-context/CODEX_RULES.md` — non-negotiable rules and freeze records
- `.ai-context/KNOWN_RISKS.md` — risk classifications and entries

CommerceHub architecture/certification sources:
- `docs/architecture/commercehub-authority-flow-review-findings.md`
- `docs/architecture/commercehub-domain-boundary-review-findings.md`
- `docs/architecture/commercehub-end-to-end-business-flow-review-findings.md`
- `docs/architecture/commercehub-final-production-architecture-certification-findings.md`
- `docs/architecture/commercehub-marketplace-order-lifecycle-authority-certification-findings.md`
- `docs/architecture/commercehub-marketplace-order-creation-authority-certification-findings.md`
- `docs/architecture/commercehub-marketplace-order-financial-authority-certification-findings.md`
- `docs/architecture/commercehub-marketplace-order-final-authority-certification-findings.md`
- `docs/architecture/commercehub-payment-authority-certification-findings.md`
- `docs/architecture/commercehub-refund-authority-certification-findings.md`
- `docs/architecture/commercehub-return-logistics-authority-certification-findings.md`
- `docs/architecture/commercehub-cod-risk-control-certification-findings.md`
- `docs/architecture/commercehub-currency-authority-certification-findings.md`
- `docs/architecture/commercehub-payout-reconciliation-certification-findings.md`
- `docs/architecture/commercehub-settlement-payout-authority-certification-findings.md`
- `docs/architecture/vendor-earnings-financial-authority-certification-findings.md`
- `docs/architecture/vendor-earnings-lifecycle-authority-certification-findings.md`
- `docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_CERTIFICATION.md`
- `docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_EDGE_CASES.md`
- `docs/architecture/commercehub-cart-certification-findings.md`
- `docs/architecture/commercehub-checkout-certification-findings.md`
- `docs/architecture/commercehub-catalog-authority-certification-findings.md`
- `docs/architecture/commercehub-search-discovery-authority-certification-findings.md`
- `docs/architecture/commercehub-vendor-lifecycle-authority-certification-findings.md`
- `docs/architecture/commercehub-vendor-visibility-authority-certification-findings.md`
- `docs/architecture/commercehub-vendor-scope-isolation-authority-certification-findings.md`
- `docs/architecture/marketplace-governance-authority-certification-findings.md`
- `docs/architecture/commercehub-emergency-operations-certification-findings.md`
- `docs/architecture/commercehub-actor-resolution-authority-certification-findings.md`
- `docs/architecture/commercehub-authority-scope-isolation-certification-findings.md`

POS Admin certifications for vendor operations:
- `.ai/hermes/systems/pos-admin/README.md`
- `docs/architecture/admin-order-lifecycle-authority-certification-findings.md`
- `docs/architecture/admin-order-fulfillment-shipment-authority-certification-findings.md`

Status: System guide v1.0 — CommerceHub. Core domain rules are owned by the Core Guides above; this guide explains how CommerceHub uses them.