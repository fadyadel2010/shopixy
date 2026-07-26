# Shopixy AI QA Knowledge Base — System Guide: POS Admin

A SYSTEM guide, not a domain guide. The Core Guides (`02-architecture.md`, `03-inventory.md`, `04-payments.md`, `05-orders.md`, `06-security-identity.md`, `07-platform-overview.md`, `08-qa-philosophy.md`) own the durable business rules for each domain. This guide does not repeat them. It explains how **POS Admin** operates as one complete business system and how it *uses* those domains.

Navigate from `.ai/hermes/INDEX.md`.

---

# Purpose

Teach a Senior QA Engineer, AI agent, or new contributor how POS Admin works as a whole business system — its responsibilities, the people who operate it, the operating flows that move money and stock, and the authority boundaries it may and may not cross.

POS Admin is one of the tenant-operated client surfaces in front of the shared SQL authority. It is not "a database." It is the back office and point of sale where a tenant runs catalog, pricing, customers, suppliers, purchasing, inventory, sales, payments, returns, treasury, affiliate payout operations, accounting reads, CMS, tenant-scoped repair, communication, and reporting. POS Admin also contains an embedded CommerceHub Vendor Module for tenants that participate in CommerceHub, and a Service Marketplace capability for internal POS-to-POS professional-service collaboration. The CommerceHub Vendor Module is a vendor operational surface only; CommerceHub and platform repair remain distinct scopes. Service Marketplace is an internal B2B service ecosystem between POS Admin tenants only; it is not Storefront, CommerceHub, Affiliate, customer-facing commerce, or CommerceHub Super Admin governance.

This guide answers: what POS Admin owns, what it triggers, what it may never mutate directly, and how to test it as a system rather than as a screen.

---

# What is POS Admin?

POS Admin is the tenant back-office and point-of-sale surface (`PointOfSale.Web`) over the authoritative admin/POS write APIs (`api/admin/*`). It is the operating cockpit for one tenant.

## What it owns (the tenant back-office responsibility)

POS Admin owns the *operational intent* for a tenant: catalog setup, pricing, purchasing, warehouse/inventory state, customer and supplier records, POS sales and returns, cashbox/bank treasury operations, affiliate payout review/payment-proof intent, CMS publication, tenant configuration, tenant-scoped search/projection repair, reports, Service Marketplace provider/consumer intent, and the tenant-side participation in marketplace, shipping, notifications, and WhatsApp communication.

Crucially, "owns the responsibility" means POS Admin is the *entry point and orchestration surface* for these mutations. The durable business truth is always written by the lifecycle owners in PostgreSQL (see Authority Boundaries). POS Admin is never the authority itself.

## What it does NOT own

POS Admin does not own:

- The system of record. PostgreSQL is the single authoritative write model. POS Admin UI state, DTOs, cached balances, dashboards, and route ids are evidence or presentation only.
- Outward-money readiness, marketplace finance, or platform/global authority. Those belong to CommerceHub finance, affiliate payout authority, and Super Admin platform operations respectively.
- Storefront, CommerceHub, or Affiliate customer-facing state. Those are separate SPAs and identity surfaces over their own APIs.
- Customer-facing service commerce. Service Marketplace is internal POS-to-POS collaboration only; external Storefront customers, CommerceHub customers, affiliate visitors, and anonymous users do not participate.
- Cross-tenant data. POS Admin operates strictly within one tenant; it cannot read or mutate another tenant's records.

Read `02-architecture.md` for the SQL/derivative split and `07-platform-overview.md` for the surface map.

---

# Primary Responsibilities

Each responsibility below says why it exists, which Core domain owns the durable rules, and which guide to consult.

## Catalog

Why it exists: a tenant must define the products, variants, categories, and units it sells and publishes.

- Core domain owner: Catalog/Product authority (PostgreSQL `Products`, `ProductVariants`, `Categories`, `Units`, `ProductUnits`). See `02-architecture.md` (system ownership boundaries) and `03-inventory.md` (variant/purchasability lineage).
- Consult: `02-architecture.md` and `03-inventory.md`. Product deletion, variant retirement, category lineage, and SKU/barcode uniqueness are certified in `pos-product-authority-certification-findings.md`.
- POS Admin uses it by: creating/updating products and variants, binding units and categories, retiring products (soft-delete that also retires child variants), and publishing. Publication visibility is decided by SQL lifecycle state, not by the UI toggle alone.

## Orders / Sales

Why it exists: a tenant sells to walk-in and recorded customers through the POS and needs authoritative sales invoices, returns, and cancellations.

- Core domain owner: Orders / Sales authority. The generic `Order` model and the POS sales-invoice lifecycle. See `05-orders.md` and the sales authority certification `pos-sales-authority-certification-findings.md`.
- Consult: `05-orders.md`, `03-inventory.md` (stock reservation at sale), `04-payments.md` (payment-at-sale).
- POS Admin uses it by: preparing a sale (recomputes server-side totals, locks stock, claims idempotency, posts accounting), completing it, and processing returns. Completed invoices are not rewritten — reversal is through sales returns.

## Inventory

Why it exists: a tenant must hold stock in warehouses, move it, reserve it, adjust it, and see accurate availability.

- Core domain owner: Inventory authority (`ProductVariantStocks`, `InventoryReservations`, `StockMovements`, `Warehouses`). See `03-inventory.md` and `pos-warehouse-inventory-authority-certification-findings.md`.
- Consult: `03-inventory.md`.
- POS Admin uses it by: opening stock (one-time), transfers between warehouses, adjustments, reservations for admin order creation, and receiving/restoring stock on returns. All mutations go through the inventory consistency service; stock movements are append-only.

## Payments

Why it exists: a tenant collects payment at the point of sale and needs payment evidence reconciled into SQL truth.

- Core domain owner: Payment lifecycle (`IPaymentLifecycleService`). See `04-payments.md`.
- Consult: `04-payments.md`.
- POS Admin uses it by: accepting Cash, Card, or Mixed tender at sale (Card must equal server total; Mixed cash+card must equal server total; `PaymentMethod.Credit` and deferred sale execution fail closed), and by feeding provider callbacks/webhooks as evidence only. Stripe/Paymob are certified reference providers for POS Admin + Storefront; POS Admin payment-at-sale is covered by the sales certification, not a separate incremental endpoint.

## Customers / Suppliers / Party Ledger

Why it exists: a tenant tracks its buyers, suppliers, opening balances, adjustments, payments, outstanding balances, and statement of account.

- Core domain owner: Customer/Supplier party authority and ledger. See `06-security-identity.md` (identity) and the financial/treasury guides below.
- Consult: `pos-customer-supplier-authority-certification-findings.md` and `04-payments.md` (ledger integration).
- POS Admin uses it by: creating customers and suppliers, recording one-time idempotent opening balances, applying party adjustments, taking payments-at-sale, paying suppliers, and reading statements/outstanding balances. Party balances and SOA totals derive from `AccountLedgers` aggregation, never from stored `Customer.Balance` / `Supplier.Balance` shadows.

## Purchasing / Procurement

Why it exists: a tenant must buy stock from suppliers, receive it into warehouses, update cost authority, and reverse purchases safely when goods are returned.

- Core domain owner: Purchasing authority plus Inventory and Supplier/Ledger authority. See `03-inventory.md`, the customer/supplier authority above, and `pos-purchasing-authority-certification-findings.md`.
- Consult: `pos-purchasing-authority-certification-findings.md`, `pos-customer-supplier-authority-certification-findings.md`, `pos-warehouse-inventory-authority-certification-findings.md`, and `04-payments.md` for ledger integration.
- POS Admin uses it by: creating purchase invoices as complete receiving documents, validating current supplier/warehouse/product/variant/unit lineage, receiving stock through locked inventory mutation, updating average cost through SQL authority, and executing purchase returns from original purchase snapshots. A purchase cannot bypass inventory authority, supplier authority, unit conversion authority, or ledger posting rules.

## Pricing

Why it exists: a tenant sets unit prices, default units, and price-derived totals.

- Core domain owner: Catalog/Product authority for purchasability and unit price; Sales authority for server-computed invoice totals. See `03-inventory.md` and `05-orders.md`.
- Consult: `02-architecture.md`, `03-inventory.md`, `pos-product-authority-certification-findings.md`, `pos-sales-authority-certification-findings.md`.
- POS Admin uses it by: configuring `ProductUnits` (one live base unit, one live default unit per product), coupons, promotions, Smart Offers, and other tenant pricing inputs, then trusting server-side recomputation of sale and checkout totals. Storefront may present admitted price, discount, coupon, promotion, and Smart Offer evidence, but final eligibility, stacking, usage, currency, tax, shipping, payment fee, discount amount, and payable total remain backend authority. Client-submitted totals/unit-prices/tax-rates/discounts are overwritten or rejected.

## CMS

Why it exists: a tenant authors draft and live content/pages for its storefront and discovery surfaces.

- Core domain owner: Catalog/publication authority (SQL publication state decides visibility; CMS draft/live is owned by the back office). See `02-architecture.md` (system ownership boundaries).
- Consult: `02-architecture.md`, `07-platform-overview.md`.
- POS Admin uses it by: authoring draft content and publishing; publication visibility converges from SQL, not from the publish action alone.

## Accounting

Why it exists: every money mutation must post to an immutable double-entry ledger so reports and balances are trustworthy.

- Core domain owner: Accounting/ledger authority (immutable `LedgerJournalEntries`, `LedgerLines`, `LedgerAccounts.CurrentBalance`). See `04-payments.md` (Accounting Integration) and the treasury/reporting certifications.
- Consult: `04-payments.md`, `pos-financial-treasury-authority-certification-findings.md`, `pos-financial-reporting-authority-certification-findings.md`.
- POS Admin uses it by: performing treasury operations (cashbox open/float, bank, transfer, expense, income) that post balanced double-entry journals; it never writes the ledger directly. Reports read posted ledger history.

## Reports

Why it exists: a tenant needs trial balance, P&L, balance sheet, cash flow, receivables, payables, inventory valuation, and dashboards.

- Core domain owner: Financial reporting authority (reads posted ledger and SQL stock truth). See `pos-financial-reporting-authority-certification-findings.md`.
- Consult: `04-payments.md`, `pos-financial-reporting-authority-certification-findings.md`.
- POS Admin uses it by: requesting reports; report totals are recomputed from current SQL authority on every request. Cached dashboard aggregates and stored balance shadows are not report authority.

## Settings

Why it exists: a tenant configures its own operational parameters.

- Core domain owner: Tenant authority (current SQL tenant lifecycle truth). See `06-security-identity.md` and `pos-tenant-authority-certification-findings.md`.
- Consult: `06-security-identity.md`, `pos-tenant-authority-certification-findings.md`.
- POS Admin uses it by: reading/writing tenant-scoped settings through `RequiredTenantId`; settings cannot change tenant ownership or cross-tenant scope. Distinguish two customer-facing configuration families when testing Storefront effects:
  - Storefront Settings: theme, branding, logo, store identity, SEO, social links, public appearance, and customer-facing presentation.
  - Tenant Settings: currency, locale, regional configuration, operational defaults, and tenant business configuration.
  Both are admitted by backend tenant authority before Storefront presents them. Storefront may render the admitted configuration, but browser state, route state, analytics configuration, or cached settings never choose tenant identity, currency, locale, branding, or store authority.

## Marketplace participation

Why it exists: a tenant may operate as a CommerceHub vendor, selling through the shared marketplace.

- Core domain owner: CommerceHub marketplace authority (separate from generic POS order/inventory authority). See `05-orders.md`, `07-platform-overview.md`, and `04-payments.md` (CommerceHub exclusion from reference-provider certification).
- Consult: `05-orders.md`, `07-platform-overview.md`, `04-payments.md`, `02-architecture.md` (CommerceHub finance is EGP-only, outward-money gated).
- POS Admin uses it by: operating the tenant-vendor surface for marketplace participation: vendor visibility/configuration, marketplace sub-order review, allowed fulfillment/cancellation/delivery intent, and vendor-facing operational reads. Vendor operations are a CommerceHub marketplace scope with separate marketplace order/sub-order authority; they do NOT grant POS Admin platform authority, and CommerceHub finance/payout readiness is owned elsewhere (Super Admin + CommerceHub finance).


## Service Marketplace participation

Why it exists: POS Admin tenants may collaborate with other POS Admin tenants by offering and requesting professional services inside Shopixy.

- Core domain owner: Service Marketplace authority plus the shared Core authority boundaries for identity, tenant scope, payments, accounting, orders, inventory, search, projections, and QA reasoning. See `.ai/hermes/systems/service-marketplace/README.md`, `02-architecture.md`, `04-payments.md`, `06-security-identity.md`, `07-platform-overview.md`, and `08-qa-philosophy.md`.
- Consult: `.ai/hermes/systems/service-marketplace/README.md` and the Service Marketplace certification findings under `docs/architecture/service-marketplace-*`.
- POS Admin uses it by: allowing an authenticated POS Admin tenant actor to act as a Service Provider for one engagement and a Service Consumer for another. Provider-side operations include provider profile maintenance, service publication, request review, request acceptance/rejection, request-bound chat, provider-side completion, and trust/reputation outcomes after final completion. Consumer-side operations include service discovery, service detail review, request submission, request-bound chat, consumer final completion, and eligible rating/review submission. POS Admin remains the operational surface only: current SQL Service Marketplace authority owns service listings, service request identity, participant lineage, lifecycle state, chat membership, attachments, completion, ratings, reviews, trust, analytics, search convergence, and recovery.
- Boundary: Service Marketplace is internal B2B POS-to-POS collaboration only. It is not Storefront, not CommerceHub, not Affiliate, not customer-facing commerce, not CommerceHub Super Admin governance, not a generic `Order`, not a CommerceHub marketplace order/sub-order, not inventory authority, not accounting authority, and not payment finality. Service Marketplace may coordinate payment intent or evidence where admitted by the owning financial authority, but `04-payments.md` and SQL financial authorities decide payment truth, refunds, settlement, payout readiness, and accounting.


## Affiliate operations

Why it exists: a tenant may enable affiliate referral traffic into its storefront and earn/pay commissions.

- Core domain owner: Affiliate authority (separate identity surface + store-scoped economics). See `07-platform-overview.md`, `05-orders.md`.
- Consult: `.ai/hermes/systems/affiliate/README.md`, `07-platform-overview.md`, `05-orders.md`, `04-payments.md` (affiliate payout readiness is outward-money gated), `pos-admin-payout-review-approval-authority-certification-findings.md`, and `pos-admin-payout-payment-execution-proof-settlement-authority-certification-findings.md`.
- POS Admin uses it by: configuring store-scoped campaign, commission, product/category eligibility, and payout-review inputs; managing affiliate participation where tenant operation is allowed; reviewing payout requests; approving/rejecting review intent; and submitting payment-reference/proof intent for approved payouts. POS Admin is the operational surface only: attribution admission, referral tracking, immutable commission snapshots, commission ledger lifecycle, payout request snapshots, approval state, paid execution, transfer evidence allocation, affiliate balance withdrawal, commission paid rows, fraud holds, accounting, recovery, and settlement are admitted by backend Affiliate/Core/settlement authority under SQL locks and readiness gates. Affiliate commission/payout is SQL-authoritative and distinct from inventory "reservations."

## Shipping

Why it exists: a tenant fulfills sold goods through carriers.

- Core domain owner: Shipment authority (shipment rows + projection events); fulfillment lifecycle owned by `IOrderFulfillmentService`. See `05-orders.md`, `02-architecture.md` (System Ownership Boundaries).
- Consult: `05-orders.md`, `02-architecture.md`.
- POS Admin uses it by: creating shipment intent, assigning carrier/tracking evidence where allowed, and triggering carrier handoff. Shipment rows, carrier callbacks, and tracking events are evidence paths; delivery events feed COD/accounting/affiliate flows but do not own fulfillment truth.

## Notifications

Why it exists: a tenant communicates with customers and operators.

- Core domain owner: Notification/event projection surfaces; business truth stays in the core domains. See `07-platform-overview.md`.
- Consult: `07-platform-overview.md`, the relevant originating domain guide.
- POS Admin uses it by: triggering notifications from sale, payment, fulfillment, shipment, return, payout, and operational events. Delivery/non-delivery is communication evidence only; a notification is a derivative effect, never an authority.

## WhatsApp

Why it exists: a tenant can reach customers over WhatsApp.

- Core domain owner: WhatsApp transport (`PointOfSale.WhatsApp.Baileys`) owns session/socket state only; business rules stay in the platform. See `02-architecture.md`, `07-platform-overview.md`.
- Consult: `07-platform-overview.md`, `02-architecture.md`.
- POS Admin uses it by: configuring and initiating customer/operator messaging through the transport service. Storefront may consume the admitted tenant WhatsApp configuration for checkout phone verification / OTP and approved notification or recovery workflows. WhatsApp session/socket/delivery/OTP state is communication or identity evidence only; it never creates Order, Payment, Inventory, Pricing, Checkout, Refund, Shipping, Account, or tenant authority. Tenant/account/business rules are enforced by the main platform, not the transport.

## Tenant-scoped search and projection repair

Why it exists: a tenant may need product/search/read-model convergence after catalog, publication, inventory, or projection lag.

- Core domain owner: SQL publication/catalog/inventory authority plus reviewed projection/rebuild boundaries. See `02-architecture.md`, `03-inventory.md`, and `07-platform-overview.md`.
- Consult: `02-architecture.md`, `03-inventory.md`, `07-platform-overview.md`.
- POS Admin uses it by: triggering tenant-scoped reindex/repair/rebuild operations that recompose from current SQL authority before touching MongoDB, Redis, or Elasticsearch. POS Admin cannot delete/recreate global indices, run all-tenant repair as tenant authority, or turn a derivative projection into truth; global search controls and dangerous repair primitives remain platform/Super Admin authority.

---

# CommerceHub Vendor Module

POS Admin has three operational responsibility families.

Responsibility 1: operate the tenant business.

Responsibility 2: participate inside CommerceHub as a Vendor.

Responsibility 3: collaborate with other POS Admin tenants through Service Marketplace.

The CommerceHub functionality inside POS Admin is an embedded Vendor operational surface. It is not CommerceHub itself. It is not Marketplace authority. It is not Marketplace governance. It simply allows the tenant to participate in CommerceHub.

```text
POS Admin
├── Tenant Business
│
├── Products
├── Inventory
├── Orders
├── Customers
├── Purchasing
├── Sales
├── Accounting
├── Storefront
├── Settings
│
└── CommerceHub Vendor Module
      ├── Earnings
      ├── Payments
      ├── Finance
      └── Settings
```

The Earnings, Payments, Finance, and Settings marketplace screens exist only because the tenant participates in CommerceHub.

Outside CommerceHub, these screens have no purpose. They do not become tenant accounting, Storefront payment configuration, POS order authority, product authority, or platform governance.

The Vendor Module is an operational participation surface. CommerceHub remains a separate business system, Super Admin remains the CommerceHub governance surface, and Core lifecycle owners remain the mutation authorities.

Service Marketplace is a separate internal collaboration capability inside POS Admin. It is not CommerceHub and it is not the Vendor Module. It lets one POS Admin tenant provide a professional service to another POS Admin tenant, while current SQL Service Marketplace authority owns the service listing, request, chat, completion, rating, trust, analytics, and recovery truth.

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

# Service Marketplace Operational Model

Service Marketplace is the internal B2B service ecosystem between POS Admin tenants.

It is not a product marketplace, not customer-facing commerce, and not CommerceHub governance.

```text
POS Admin
├── Tenant Business Operations
│
├── CommerceHub Vendor Module
│     └── customer-facing marketplace participation as a vendor
│
└── Service Marketplace
      ├── Provider Profile
      ├── Service Publication
      ├── Service Discovery
      ├── Service Requests
      ├── Request-bound Chat
      ├── Attachments
      ├── Completion
      ├── Ratings / Reviews
      ├── Trust / Reputation
      └── Analytics / Recovery
```

## Service Provider

A Service Provider is an authenticated POS Admin tenant actor offering a professional service to other POS Admin tenants.

POS Admin role: submit provider-side operational intent — maintain the provider profile, publish service listings, review requests, accept or reject requests, communicate through request-bound chat, perform the service, mark provider-side completion, and receive eligible reputation outcomes after final completion.

Authority boundary: provider actions do not own payment finality, accounting truth, order truth, inventory truth, customer-facing commerce, Storefront visibility, CommerceHub governance, Affiliate attribution, or another tenant's POS operations. Provider actions must be admitted by current backend Service Marketplace authority and the relevant Core authority where money, identity, or recovery is involved.

## Service Consumer

A Service Consumer is an authenticated POS Admin tenant actor requesting a professional service from another POS Admin tenant.

POS Admin role: browse internal service discovery, open service details, evaluate provider profile/trust evidence, submit service request intent, communicate through request-bound chat after the lifecycle allows it, receive the service, confirm final completion, and submit an eligible rating/review after final completion.

Authority boundary: the consumer is not a Storefront customer, CommerceHub customer, affiliate visitor, anonymous user, or Super Admin actor. Consumer-side UI state, route ids, cached service cards, search results, chat text, attachments, notifications, and analytics are evidence or presentation only.

## Service Marketplace lifecycle

Service Marketplace request lifecycle is not generic order lifecycle and not CommerceHub order/sub-order lifecycle.

```text
Consumer POS Admin intent
↓
Current SQL service / provider / tenant validation
↓
Submitted Service Request
↓
Provider review
↓
Accepted or Rejected
↓
Request-bound chat and service execution when accepted
↓
Completed by Provider
↓
Accepted / Confirmed by Consumer
↓
Closed
↓
Rated / Reviewed when eligible
```

Lifecycle ownership belongs to current SQL Service Marketplace request authority. POS Admin screens expose allowed provider and consumer actions, but they do not own request admission, acceptance, rejection, completion, final closure, rating eligibility, or lifecycle recovery.

Provider completion alone is not final completion. Final service completion requires consumer confirmation after provider-side completion. Rating/review eligibility derives from final completed request authority, not from provider claims, chat messages, notifications, attachments, search results, analytics, frontend capability flags, or cached request DTOs.

## Communication, attachments, notifications, analytics, and recovery

- Chat is request-bound. Chat participants derive from the SQL request's provider and consumer participants. Chat is a collaboration surface, not lifecycle authority.
- Attachments are evidence only. They may support discussion, delivery, proof, or clarification, but they do not mutate service listing, request lifecycle, payment, accounting, inventory, order, rating, or trust authority.
- Notifications are derivative communication outputs. A notification about request acceptance, completion, or rating availability does not accept a request, complete a service, or authorize a review.
- Analytics, dashboards, trust badges, rating averages, review counts, and KPI cards are derived presentation. They never decide service visibility, request authority, completion, payment, accounting, provider eligibility, rating eligibility, or lifecycle ownership.
- Service discovery and search are candidate evidence only. Opening a service, creating a request, or rendering service details must converge through current SQL service, tenant, provider profile, visibility, and trust authority.
- Recovery restores collaboration and convergence from current SQL truth. It may recover chat, retry notifications, rebuild projections, or reindex service discovery, but it cannot invent services, requests, completion, ratings, payment, accounting, orders, inventory, Storefront, CommerceHub, Affiliate, or tenant authority.

## Dispute / support boundary

Support may assist with Service Marketplace investigation, evidence collection, dispute triage, or operational recovery when a service engagement is unclear or blocked. Support does not become a service participant and does not own service lifecycle, payment truth, accounting truth, inventory truth, ratings, reviews, or reputation. Any dispute outcome must remain subordinate to current Service Marketplace SQL authority and the relevant Core lifecycle owners. Support judgment, chat text, uploaded proof, screenshots, notification delivery, analytics, or dashboard state cannot finalize service completion, payment, accounting, refunds, ratings, trust, cross-tenant access, or lifecycle recovery.

Official POS Admin → Service Marketplace relationship flow:

```text
POS Admin tenant actor
↓
Service Provider / Service Consumer operational intent
↓
Backend SQL/Core/Service Marketplace authority
↓
Service Marketplace listing, request, chat, completion, rating, trust, analytics, and recovery state
↓
POS Admin provider/consumer presentation
```

---

# Relationship with Super Admin

Super Admin does not manage POS Admin.

Super Admin governs only the CommerceHub Vendor Module. Everything else inside POS Admin remains tenant-owned.

Super Admin may:

- approve vendor participation;
- suspend vendor participation;
- ban vendor participation;
- block marketplace visibility;
- manage marketplace commission;
- review marketplace payouts;
- manage marketplace governance.

Super Admin may not:

- manage tenant inventory;
- manage tenant accounting;
- manage storefront configuration;
- manage POS payment methods;
- manage tenant orders;
- manage tenant products.

Outside CommerceHub, POS Admin remains fully autonomous under tenant authority and the owning Core lifecycle rules.

---

# CommerceHub Vendor Operational Flow

```text
POS Tenant
↓
Vendor Registration
↓
Vendor Approval
↓
Marketplace Participation
↓
Marketplace Orders
↓
Marketplace Earnings
↓
Marketplace Finance
↓
Marketplace Payments
↓
Marketplace Recovery
```

## POS Tenant

Ownership: tenant identity, tenant operations, and POS Admin business authority remain tenant-owned.

POS Admin role: operate the tenant business and submit vendor participation intent when the tenant wants to join CommerceHub.

## Vendor Registration

Ownership: tenant/vendor eligibility is validated by CommerceHub participation authority and current SQL identity/tenant authority.

POS Admin role: submits vendor participation intent. Registration does not transfer ownership of POS Admin to CommerceHub or Super Admin.

## Vendor Approval

Ownership: CommerceHub vendor governance and Super Admin marketplace governance.

POS Admin role: receives the approved or rejected participation state. Approval means the tenant may participate as a marketplace vendor; it does not approve every product, price, payment, order, or inventory fact.

## Marketplace Participation

Ownership: CommerceHub marketplace participation, visibility, publication, and governance authority.

POS Admin role: operates the Vendor Module for marketplace participation. Tenant Business operations remain separate.

Operational flow: POS Admin submits marketplace-vendor intent through the CommerceHub Vendor Module; backend SQL/Core/CommerceHub authority admits or rejects it; CommerceHub Marketplace presents admitted listings, order/sub-order state, finance evidence, ratings/reviews, and governance state back to customers, vendors, and governance surfaces.

## Marketplace Orders

Ownership: CommerceHub marketplace order and sub-order lifecycle authority.

POS Admin role: reviews vendor-facing marketplace orders and submits allowed fulfillment, cancellation, or delivery intent. It must not treat marketplace orders as Storefront orders or POS sales.

## Marketplace Earnings

Ownership: CommerceHub earnings, commission snapshots, settlement lineage, and ledger/finance authority.

POS Admin role: views vendor earnings evidence. It does not compute tenant accounting truth from marketplace earnings screens.

## Marketplace Finance

Ownership: CommerceHub finance, COD debt, settlement, payout readiness, refund impact, and reconciliation authority.

POS Admin role: consumes vendor-facing marketplace finance state. Tenant accounting remains separate.

## Marketplace Payments

Ownership: Payment lifecycle authority and CommerceHub payment/finance readiness.

POS Admin role: views vendor-facing marketplace payment evidence. Marketplace payments are not Storefront payment methods and are not POS payment-at-sale configuration.

## Marketplace Recovery

Ownership: CommerceHub governance, current SQL eligibility, debt authority, listing authority, and Core lifecycle owners where protected state is involved.

POS Admin role: responds to recovery requirements as a vendor. Recovery restores CommerceHub participation only when current authority allows it; it does not restore or alter tenant business state outside CommerceHub.

---

# CommerceHub Dependency Matrix

This is a dependency-oriented map. It prevents confusion between Marketplace responsibilities and Tenant responsibilities.

| Vendor Area | Authoritative Source | Vendor Module Surface | Does NOT Control |
|---|---|---|---|
| Earnings | CommerceHub earnings, commission snapshots, settlement lineage, ledger authority | Earnings view | Tenant accounting |
| Payments | Payment lifecycle + CommerceHub payment evidence/readiness | Payments view | Storefront payment methods or POS payment-at-sale methods |
| Finance | CommerceHub finance, COD debt, settlement, payout readiness, reconciliation | Finance view | Tenant treasury or tenant ledger posting |
| Settings | CommerceHub vendor participation/governance settings | Vendor marketplace settings | Tenant settings or Storefront configuration |
| Marketplace Visibility | CommerceHub listing, vendor, category, product, stock, price, and governance authority | Visibility/participation status | Storefront visibility |
| Marketplace Commission | CommerceHub commission policy and authoritative order/sub-order snapshots | Commission/earnings evidence | Product pricing |
| Vendor Status | CommerceHub vendor governance and Super Admin marketplace governance | Vendor status indicator | POS user status or tenant ownership |
| Marketplace Orders | CommerceHub marketplace order/sub-order lifecycle | Vendor order view | Storefront orders or POS sales invoices |
| Marketplace Refunds | Refund/payment authority, marketplace order lineage, settlement lineage, ledger readiness | Vendor refund visibility | POS refunds |
| Marketplace Payouts | CommerceHub payout readiness, earnings, external evidence, ledger authority | Payout/earnings visibility | Tenant treasury |
| Marketplace Reviews | CommerceHub marketplace moderation/governance authority | Review visibility/response surface | Storefront reviews outside CommerceHub |

The Vendor Module consumes CommerceHub governance and marketplace authority. It does not create a second tenant accounting system, payment-method system, order system, refund system, pricing system, or Super Admin governance system.

---

# User Roles

POS Admin uses a single privilege axis: `Users.Role` in current SQL. There is no separate permission/capability entity; the role is the certified privilege boundary, and JWT role claims are evidence only (revalidated against SQL on every request). Roles are responsibilities, not UI permission toggles.

## Cashier

Operates the point of sale: creates sales, accepts payment, processes returns within the sale/return lifecycle, and serves customers. A cashier cannot mutate structured transfer lifecycle state, warehouse opening stock, or adjustments — those require higher roles.

## Store Manager

Oversees daily store operation: sales oversight, returns, inventory adjustments/transfers (requires Owner/Admin/Manager), and staff-facing configuration. Acts as the operational lead for the flows below.

## Warehouse

Operates warehouse/inventory responsibilities: opening stock, transfers, reservations, adjustments, receiving. These mutations require an active same-tenant warehouse and the appropriate role (Owner/Admin/Manager for transfers and adjustments).

## Finance

Owns treasury and accounting-facing operations: cashbox/bank management, transfers, expenses, income, supplier payments, customer/supplier opening balances and adjustments, and financial reporting. All money movement posts to the ledger through the treasury service.

## Support

Assists customers: looks up customers/orders, may process returns within the sale/return lifecycle, and reads reports. Support authority is scoped to customer-facing service actions, not financial or lifecycle overrides.

## Marketing

Manages catalog presentation, pricing configuration inputs, CMS content, and affiliate/storefront discovery. Marketing configures visibility inputs; SQL publication state remains the authority for what actually becomes visible.

## Owner

The tenant owner: highest in-tenant role, authorized for warehouse transfers/adjustments, finance operations, settings, and tenant configuration. Even the owner cannot cross tenant boundaries or mutate protected lifecycle state directly — lifecycle owners own that.

## Super Admin interaction

Super Admin is a *platform* role, not a POS Admin role. POS Admin never becomes Super Admin. When platform-level operations are needed (marketplace-wide moderation, global repair, global index control, CommerceHub/platform payout execution, governance), the request leaves POS Admin and enters Super Admin platform operations, which require `RequireSuperAdmin` plus current SQL SuperAdmin/TokenVersion/governance validation. Tenant-scoped affiliate payout review/payment-proof operations may originate from POS Admin, but their durable settlement still belongs to affiliate/settlement authority, not to frontend or platform override authority. POS Admin operators must not assume any tenant role confers platform authority. See `06-security-identity.md` and `07-platform-overview.md`.

## Operator access and sessions

Operator access is a POS Admin operational concern but not a UI truth. Login, refresh, logout, and tenant-owner onboarding bootstrap are admitted by backend session authority and current SQL user/tenant/session state. Ordinary POS registration creates a Cashier identity only; it does not create session authority. Disabled/deleted users, role changes, stale refresh bindings, tenant suspension, and vendor-governance restrictions fail closed even when an old JWT or frontend role still exists.

---

# Daily Business Flows

These flows are described as business workflows, not implementation steps. Each names the domains involved and the authority owner.

## Morning opening

The store opens a cashbox (opening float) and confirms active warehouse/tenant state. Cashbox open and float route money mutation through the treasury service and post to the ledger. A suspended/inactive tenant or cashbox cannot become treasury authority.

## Receiving stock

New stock arrives: opening stock (one-time, positive-only, duplicate-free, row-locked) or a purchase/inbound transfer completion. Stock mutation locks the authoritative SQL row, validates lineage, and appends a movement. Available quantity is derived, never submitted.

## Purchasing goods

A supplier purchase is created as a complete receiving document: current same-tenant supplier, active warehouse, product/variant/unit lineage, positive quantity/cost, and valid tax are proven before receiving. Purchase creation locks product/stock/cost authority, appends purchase stock movements, updates average cost through SQL authority, and posts supplier/ledger effects through the owning services. Completed purchases are not rewritten; correction uses certified reversal/replacement or purchase-return authority.

## Paying suppliers / party ledger operations

Supplier payments, customer/supplier opening balances, and party adjustments require current same-tenant party authority, durable idempotency, positive validated amounts, active treasury funding authority where money moves, and ledger-backed posting. Statement-of-account and outstanding-balance reads derive from `AccountLedgers`; party shadow balances are compatibility evidence only.

## Selling

A sale is prepared: customer resolved from active same-tenant SQL authority, stock locked and reserved, server-side totals/taxes recomputed, idempotency claimed, accounting outbox staged, ledger posted, and committed as one authority boundary. Payment tender (Cash/Card/Mixed) is reconciled to the server total. The completed invoice is authoritative; it is not rewritten.

## Refund / Return

A return requires the original invoice `Completed`, derives line snapshots from that invoice, ignores client price/tax overrides, and cumulatively bounds quantity/total against prior returns. Stock restoration locks and appends movement history. Refund totals derive from historical snapshots, not DTO totals.

## Closing shift

The cashier/manager closes the shift: cashbox closeout, reconciliation of tender to ledger, and end-of-day reporting. Dashboard/reports recompute from posted ledger and SQL stock; cached aggregates are not authority.

## Marketplace vendor fulfillment

A tenant acting as a CommerceHub vendor reviews its marketplace sub-orders from POS Admin and submits allowed fulfillment/cancellation/delivery intent. The mutation stays inside CommerceHub sub-order authority: `StoreId` must match the current tenant, payment/COD readiness gates must pass before outbound/delivery progression, and inventory side effects execute through current SQL reservation/stock authority. POS Admin vendor operations cannot mutate another tenant's sub-order, cannot treat a marketplace sub-order as a generic POS sale, and cannot grant platform finance or payout authority.

## Shipping / carrier handoff

The operator creates shipment intent or carrier handoff from an eligible order/fulfillment state. Shipment identity, carrier assignment, tracking number, carrier-managed updates, and callbacks are evidence. Fulfillment truth changes only through the fulfillment owner; shipment status mapping cannot directly create delivered/completed order truth.

## Inventory adjustment

A correction to stock (damage, count variance) requires Owner/Admin/Manager, active same-tenant lineage, a durable idempotency key, and a non-negative result. Movements are append-only. Adversarial replay is bounded by idempotency/fingerprint.

## Publishing products

A product is authored and published; SQL lifecycle state (`PublishStatus`, `IsOnlineVisible`, `IsActive`, deletion/archive) decides storefront/affiliate/CommerceHub/search visibility. The publish action triggers convergence; it does not itself prove visibility if SQL lineage is invalid.

## Tenant-scoped search / projection repair

A tenant operator may trigger tenant-scoped search or product/read-model repair after publication, inventory, category, media, or projection lag. Repair reloads current SQL product/category/media/variant/price/stock/publication authority and then upserts, tombstones, deletes, or invalidates derivative state. POS Admin repair cannot use stale Mongo/Redis/Elasticsearch payloads as truth and cannot perform global index deletion/recreation.

## Changing prices

Unit/default-unit configuration changes are made in SQL; sale totals are always server-recomputed at transaction time. A tampered client price cannot alter the authoritative invoice total.

## Running reports

Financial and operational reports are requested; totals are recomputed from current tenant-owned SQL authority (ledger history, stock, party ledgers) on every run. Date filters select committed history; they cannot create entries.

## Affiliate payout review and payment proof

A tenant reviews affiliate payout requests, approves/rejects pending requests, and records payment-reference/proof intent for approved payouts. The frontend cannot submit amount, wallet, affiliate, store, currency, paid state, or executor truth. Backend authority reloads the persisted payout request snapshot, validates current admin identity and immutable wallet evidence, row-locks state, admits external transfer evidence/allocation, posts withdrawal/paid ledger effects, and transitions to paid inside the settlement boundary. Failed or duplicate execution cannot partially settle.

## Notifications and WhatsApp communication

Customer/operator communication is initiated from domain events or explicit operator intent: sale receipt, payment/return/fulfillment/shipment status, payout or support communication. Notifications and WhatsApp delivery state are derivative communication evidence. A delivered, failed, or missing message cannot create or change order, payment, shipment, fulfillment, affiliate, or ledger truth.

---

# Cross-System Relationships

POS Admin is one surface over shared backend authority. The other systems consume the same truth.

POS Admin owns tenant operations. It consumes CommerceHub governance only through the embedded CommerceHub Vendor Module.

## Super Admin

Super Admin is the CommerceHub governance surface. It does not manage POS Admin and does not own the tenant business.

Super Admin may approve, suspend, suppress, recover, or govern the tenant only while the tenant participates as a CommerceHub Vendor. Those decisions affect the Vendor Module and CommerceHub marketplace participation only. Tenant inventory, tenant accounting, Storefront configuration, POS payment methods, tenant orders, tenant products, and tenant settings remain POS Admin/Core authority outside CommerceHub.

## Storefront

Storefront is the tenant-branded customer SPA for the tenant's own products. POS Admin configures the catalog/pricing/publication/customer-facing configuration that Storefront reads through admitted backend responses and projected models. Storefront checkout is real but all authoritative decisions (pricing, reservation, payment, inventory, shipping, customer identity, coupon/promotion admission, and order creation) are recomputed server-side. POS Admin does not "push" truth to Storefront; both converge from SQL. CommerceHub marketplace visibility and Storefront visibility are separate; a Vendor Module state must not be treated as Storefront authority. See `.ai/hermes/systems/storefront/README.md`, `07-platform-overview.md`, and `05-orders.md`.

POS Admin → Storefront dependency map:

| POS Admin operational area | Storefront-facing effect | Authority interpretation |
| --- | --- | --- |
| Products / variants / units | Product lists, product detail, variant choices, purchasability | Storefront presents only backend-admitted catalog evidence; SQL catalog, publication, media, unit, variant, and inventory lineage decide eligibility. |
| Categories | Navigation, category pages, filters, product grouping | SQL category lineage and publication authority decide whether targets remain public. |
| Coupons / promotions / Smart Offers | Coupon input, automatic discount messaging, offer popups, checkout discount candidates | Storefront may submit or display candidates; backend pricing/promotion authority decides eligibility, stacking, usage, amount, and final snapshots. |
| Inventory / warehouses | In-stock, low-stock, unavailable, cart and checkout availability | Displayed availability is evidence; checkout/reservation re-enters current inventory authority. |
| Payment methods / manual payments | Checkout payment options, provider handoff, manual proof workflow | Payment lifecycle and tenant payment-method authority decide eligibility, status, provider evidence admission, refundability, and reconciliation. |
| Shipping methods | Shipping options, costs, ETA, delivery candidates | Backend shipping/order authority decides eligibility, cost snapshot, carrier evidence, and fulfillment effects. |
| CMS / homepage / SEO / navigation | Pages, hero sections, content blocks, links, metadata | Published content remains subordinate to tenant, page, category, product, and publication authority. |
| Storefront Settings | Theme, branding, logo, store identity, social links, public appearance | Presentation changes only; they never override catalog, pricing, inventory, checkout, payment, order, identity, or currency authority. |
| Tenant Settings | Currency, locale, regional configuration, operational tenant settings | Tenant SQL authority decides configuration; Storefront presents admitted values and cannot choose them from browser or route state. |
| Analytics settings | Customer event tracking integrations | Analytics is observation/attribution evidence only; it is not order, payment, ledger, refund, or customer authority. |
| Commerce AI | Customer assistant personality, widget style, guided shopping layer | Assistant output is customer interaction evidence only; it cannot decide catalog, price, stock, checkout, payment, order, refund, or tenant authority. |
| WhatsApp | Checkout phone verification / OTP, notifications, recovery communication where admitted | WhatsApp transport and OTP are communication or identity evidence only; Core authority still decides checkout, order, payment, refund, and account state. |
| Blocked phones / customer eligibility | Customer/account/checkout eligibility presentation | Backend identity/customer authority decides eligibility; Storefront cannot block or admit customers from frontend state alone. |
| Abandoned cart recovery | Recovery links, reminders, continuation flows | Recovery restores intent only and must re-enter live backend cart/checkout/pricing/inventory/payment authority. |
| Orders | Incoming customer orders become visible for POS Admin processing | Backend creates the authoritative order; POS Admin processing still uses order/payment/inventory/fulfillment lifecycle owners. |

Use this map as a relationship map, not an authority map. POS Admin submits operational intent, backend Core authority admits or rejects it, and Storefront presents the admitted customer-facing result.

## CommerceHub

CommerceHub is the shared multi-vendor marketplace. A POS Admin tenant may participate as a vendor through the embedded Vendor Module, but CommerceHub remains a separate business system with its own order authority model (marketplace order + per-vendor sub-orders) and its own EGP-only financial authority. CommerceHub payment paths are excluded from the Stripe/Paymob reference-provider certification. Outward-money readiness (payouts, unresolved captured payments) is gated by CommerceHub finance and Super Admin, not POS Admin. See `05-orders.md`, `04-payments.md`, `07-platform-overview.md`.

## Affiliate

Affiliate is a separate identity surface plus store-scoped commission economics. An affiliate conversion is fundamentally a Storefront `Order` carrying immutable affiliate attribution. POS Admin configures store-scoped affiliate settings and operates the tenant-side payout review/payment-proof surface, but affiliate payout readiness, immutable request snapshots, evidence allocation, balance withdrawal, commission paid rows, settlement, and accounting are admitted by backend affiliate/settlement authority. Affiliate is not CommerceHub vendor participation, and CommerceHub Vendor Module finance must not be treated as Affiliate commission authority. See `07-platform-overview.md`, `05-orders.md`, `04-payments.md`.

Official POS Admin → Affiliate relationship flow:

```text
POS Admin tenant operator
↓
Affiliate Operational Surface
↓
Backend SQL/Core/Affiliate authority
↓
Affiliate Platform
```

Reverse operational flow:

```text
Affiliate Platform
↓
Backend SQL/Core/Affiliate authority
↓
Affiliate Operational Surface
↓
POS Admin tenant operator
```

The Affiliate Operational Surface inside POS Admin is not a second Affiliate authority. It lets tenant operators configure campaign/commission inputs, manage tenant-side affiliate participation, review payout requests, approve/reject review intent, submit payment-reference/proof intent, consume affiliate reports, and respond to readiness or fraud blockers. Backend authority still decides referral admission, canonical attribution, commission snapshots, commission ledger progression, refund/chargeback reversals, affiliate balances, payout request admission, wallet snapshot binding, transfer-evidence allocation, paid commission evidence, settlement participation, accounting effects, negative-balance recovery, suspension effects, and fraud protection.

Use this map as a relationship map, not an authority map:

| POS Admin Affiliate operation | Backend authority that decides truth | Affiliate-facing effect | Does NOT Control |
| --- | --- | --- | --- |
| Campaign / commission configuration | Current tenant, product, category, price, stock, affiliate policy, and role authority | Eligible campaign/link/policy candidates for Affiliate and Storefront revalidation | Storefront checkout, final attribution, order totals, or historical commission snapshots |
| Affiliate management | Affiliate identity/session/governance authority plus tenant scope | Affiliate list/detail/status evidence and tenant-side participation state | Affiliate session authority, customer identity, POS user identity, or Super Admin authority |
| Attribution / referral review | Backend Affiliate admission + Storefront checkout/order authority | Attribution evidence visible in orders, reports, and support workflows | Canonical attribution rewrite, self-referral admission, identity-collision admission, or multi-attribution winner selection |
| Payout request review | Backend payout request authority, current balance, active request reservations, wallet snapshot, readiness gates | Review queue state, approve/reject intent, readiness blockers | Amount, wallet, affiliate, store, currency, balance, or request lifecycle truth from frontend DTOs |
| Payment proof / transfer reference | Backend Affiliate evidence allocation + Payments/Accounting settlement authority + external rail evidence | Bound transfer evidence and paid-commission evidence when admitted | Bank/provider finality, accounting settlement, duplicate withdrawal funding, or external-money certainty |
| Reports / analytics / notifications | SQL attribution, commission, payout, order, payment, accounting authority plus derivative projections | Operational reporting and communication evidence | Commission, balance, payoutable amount, ledger truth, or notification-as-finality |
| Fraud / suspension / recovery response | Affiliate governance, Security/Identity, Payments/Reconciliation, Accounting, and current SQL lineage | Holds, suspension effects, negative-balance/recovery evidence, support escalation | Manual rewrite of attribution, commission, payout, payment, order, accounting, or settlement truth |

Affiliate operational change propagation:

```text
POS Admin intent
↓
Backend validation against current tenant / identity / product / category / campaign / payment / accounting authority
↓
SQL Affiliate/Core authority admission or rejection
↓
Affiliate Portal, Storefront entry, payout queues, reports, analytics, and notifications converge from admitted authority
```

Never treat Affiliate dashboards, payout queues, proof uploads, notification delivery, analytics, referral URLs, browser cookies, copied links, route ids, or frontend DTO values as truth. They are operational evidence until the owning backend authority admits them.

## Service Marketplace

Service Marketplace is an internal POS-to-POS B2B service ecosystem inside POS Admin capabilities. One POS Admin tenant may act as a Service Provider and another POS Admin tenant may act as a Service Consumer. The same tenant can be a provider in one engagement and a consumer in another.

Service Marketplace is not Storefront, not CommerceHub, not Affiliate, not customer-facing commerce, and not CommerceHub Super Admin governance. Storefront shoppers, CommerceHub customers, affiliate visitors, anonymous users, and Super Admin-only actors do not participate as Service Marketplace provider/consumer actors.

Use this relationship map as a boundary map, not an authority map:

| POS Admin Service Marketplace operation | Backend authority that decides truth | Service Marketplace-facing effect | Does NOT Control |
| --- | --- | --- | --- |
| Provider profile / service publication | Current POS Admin actor, tenant, provider profile, SQL service listing, service visibility, category/pricing/media validation | Internal service profile, service cards, service details, discovery candidates | Storefront products, CommerceHub listings, Affiliate campaigns, inventory, orders, payment methods |
| Service discovery / service details | Current SQL service, tenant, provider profile, visibility, review/trust authority; search is candidate evidence only | Internal service search, filters, ranking, detail presentation | Business truth from Elasticsearch, Storefront discovery, CommerceHub search, Affiliate link discovery |
| Service request admission | SQL service request authority, current service/provider/tenant authority, requester/provider lineage, immutable request snapshots | Submitted request / engagement candidate | Generic `Order`, CommerceHub marketplace order/sub-order, checkout, inventory reservation, payment finality |
| Provider acceptance / rejection | SQL request lifecycle authority plus provider participant ownership | Accepted engagement or rejected request | Chat by itself, notification delivery, frontend capability flags, provider dashboard state |
| Service execution / chat / attachments | SQL request participant authority and request lifecycle; attachments are evidence only | Request-bound provider/consumer collaboration | Service lifecycle by chat text, payment/accounting truth, tenant data outside the request boundary |
| Provider completion / consumer final completion | SQL request lifecycle authority; provider completion must precede consumer confirmation | Final completed service request when consumer confirms | Provider-only closure, buyer-only closure, Storefront order completion, CommerceHub delivery, accounting finality |
| Ratings / reviews / trust | SQL review authority joined to final completed SQL request authority | Eligible review, rating, trust/reputation presentation | Storefront product reviews, CommerceHub vendor governance, Affiliate credibility, payment/accounting truth |
| Notifications / analytics / recovery | SQL-derived events and recovery from current Service Marketplace authority | Communication, dashboards, reindex/rebuild/retry, operational visibility | Request creation, acceptance, completion, ratings, payment, accounting, inventory, orders, or platform governance |

Operational change propagation:

```text
POS Admin provider/consumer intent
↓
Backend validation against current identity / tenant / provider profile / service / request authority
↓
SQL Service Marketplace/Core authority admission or rejection
↓
Service Marketplace discovery, detail, request list, chat, notification, analytics, trust, and recovery surfaces converge from admitted authority
```

Never treat Service Marketplace UI cards, search hits, service details, chat messages, attachments, notifications, analytics, trust badges, route ids, frontend capability flags, or cached DTOs as truth. They are presentation or evidence until current backend Service Marketplace authority admits them.

## Accounting

Accounting is the immutable tenant ledger. Every POS Admin money mutation (sale, return, supplier payment, treasury operation, adjustment) posts through the accounting outbox to the ledger. POS Admin reads reports from the ledger but never writes ledger rows directly. Marketplace finance is separate from tenant accounting: Vendor Module earnings, payouts, commission, and COD debt are CommerceHub marketplace finance surfaces, not tenant ledger posting authority. See `04-payments.md`, `pos-financial-treasury-authority-certification-findings.md`.

## Payments

POS Admin payment-at-sale belongs to tenant operations and follows the Payment lifecycle owner. Marketplace payments belong to CommerceHub marketplace participation and are shown through the Vendor Module as evidence or vendor-facing state.

Marketplace payments are not Storefront payment methods, are not POS payment-at-sale methods, and do not let POS Admin decide payment finality.

## Orders

Tenant orders and POS sales belong to the generic tenant order/sales authority. Marketplace orders belong to CommerceHub marketplace order and sub-order authority.

POS Admin may operate both surfaces, but it must not merge them. A marketplace sub-order shown in the Vendor Module is not a Storefront order and not a POS sales invoice.

## Inventory

Tenant inventory remains Inventory authority. CommerceHub marketplace visibility may depend on current inventory evidence, but the Vendor Module does not own stock, reservations, warehouses, movements, or available quantity.

POS Admin inventory actions remain tenant operations. Marketplace vendor operations may submit fulfillment intent only through the owning CommerceHub/order/inventory authority chain.

## Notifications

Notifications are triggered derivative effects of domain events. POS Admin initiates them; they carry no authority and never change business truth.

## Search

Elasticsearch is a derivative discovery surface. Product/search visibility converges from SQL catalog/publication/stock/price/media authority. POS Admin publication actions and tenant-scoped repair requests trigger reindex/overlay recomputation from current SQL; stale search documents do not prove or block SQL truth. Global index deletion/recreation, all-tenant rebuilds, and dangerous repair primitives are platform/Super Admin operations, not POS Admin tenant authority. See `02-architecture.md`, `03-inventory.md`.

## Shipping

Shipment rows and carrier handoff are created from POS Admin fulfillment actions; fulfillment lifecycle truth is owned by `IOrderFulfillmentService`. Delivery events feed downstream financial/affiliate flows but are not themselves authority. See `05-orders.md`.

## WhatsApp

WhatsApp transport handles session/socket state only. POS Admin messaging goes through the transport service; tenant/account/business rules are enforced by the platform. See `02-architecture.md`, `07-platform-overview.md`.

---

# Authority Boundaries

POS Admin may initiate mutations, but protected state is owned by lifecycle services. This is the single most important QA concept for POS Admin.

Tenant Operations, Marketplace Vendor Operations, and Service Marketplace Operations are different operational surfaces.

Tenant Operations are the POS Admin business responsibilities: products, inventory, orders, customers, purchasing, sales, accounting, Storefront inputs, settings, tenant reports, tenant repair, and tenant communications.

Marketplace Vendor Operations are the CommerceHub Vendor Module responsibilities: marketplace participation, marketplace order review, vendor earnings, marketplace finance, marketplace payments, marketplace payout visibility, marketplace refunds, vendor status, and recovery participation.

Service Marketplace Operations are the internal POS-to-POS collaboration responsibilities: provider profiles, service publication, service discovery, service requests, request-bound chat, attachments, completion, ratings/reviews, trust/reputation, analytics, notifications, and recovery.

No surface replaces Core lifecycle owners. Tenant operations must still enter catalog, inventory, order, payment, accounting, security, and tenant authority. Marketplace Vendor Operations must still enter CommerceHub marketplace authority, payment/refund authority, settlement/ledger readiness, and Super Admin governance where required. Service Marketplace Operations must still enter current SQL Service Marketplace authority for listing, request, chat, completion, review, trust, search, analytics, and recovery truth, and must enter Core payment/accounting authority where money is involved.

## What POS Admin may mutate (through owning lifecycle services)

- Catalog: products, variants, categories, units, publication — via catalog authority in PostgreSQL.
- Purchasing: purchase invoices, receiving, purchase returns, supplier liability effects — via purchasing, inventory, supplier, and ledger authority.
- Inventory: opening stock, transfers, adjustments, reservations, returns restoration — via the inventory consistency service.
- Sales/Returns: sale preparation and return execution — via the sales/returns lifecycle boundary (idempotency-bounded, stock-locked, ledger-posted).
- Treasury: cashbox/bank/transfer/expense/income — via `ITreasuryOperationService` / `IFinancialPostingService`, posting balanced double-entry journals.
- Party: customer/supplier records, opening balances, adjustments, supplier payments — via ledger/party services.
- Affiliate payout operations: review, approve/reject, and payment-proof intent — via backend affiliate payout and settlement authority, never frontend-submitted money fields.
- Marketplace Vendor Operations: vendor-facing participation intent, marketplace sub-order intent, and vendor finance/payout visibility — via CommerceHub marketplace authority, not tenant business authority.
- Service Marketplace Operations: provider/consumer service intent, service publication, request submission, provider acceptance/rejection, request-bound chat, provider completion, consumer final completion, ratings/reviews, trust presentation, analytics reads, and recovery requests — via Service Marketplace SQL authority, not Storefront, CommerceHub, Affiliate, order, inventory, payment, accounting, or frontend authority.
- CMS/Settings/Reports/Search repair: tenant-scoped configuration, read-only reporting, and tenant-scoped projection repair that recomposes from SQL.

## What POS Admin cannot mutate directly

- Payment lifecycle state — only `IPaymentLifecycleService` may write it. Provider callbacks/webhooks are evidence only.
- Order/fulfillment status — only the order lifecycle boundary / `IOrderFulfillmentService` may write it.
- Inventory stock/reservation truth — only the inventory consistency service may write it; direct SQL or convenience shortcuts are architectural violations.
- Ledger journal entries — immutable; only the accounting outbox posts them.
- Purchase receiving/return economics — only purchasing authority may admit them from current supplier/warehouse/product/unit lineage and original purchase snapshots.
- Affiliate payout settlement — only affiliate payout/settlement authority may approve, allocate evidence, withdraw balances, mark commission paid, and post accounting; frontend payout DTOs are evidence only.
- Global search/index controls and all-tenant repair — platform/Super Admin scope only; tenant POS Admin may trigger only tenant-scoped SQL-recomposed convergence.
- Cross-tenant records — tenant co-ownership guards reject foreign edges before commit.
- Platform/global authority — Super Admin scope only; POS Admin roles never escalate to it.
- CommerceHub finance and platform/global outward money — owned by CommerceHub finance + Super Admin readiness gates. Tenant-side affiliate payout payment-proof operations still pass through affiliate payout/settlement authority and external-money readiness checks.
- CommerceHub marketplace governance — owned by CommerceHub/Super Admin governance; the Vendor Module consumes it and cannot override it.
- Tenant ownership from marketplace state — vendor approval, suspension, or recovery does not change tenant ownership, tenant settings, POS roles, Storefront operation, tenant orders, tenant inventory, or tenant accounting.
- Service Marketplace lifecycle truth — owned by SQL service listing/request/chat/completion/review/trust authority. POS Admin provider/consumer screens, chat text, attachments, notifications, analytics, search documents, route ids, frontend capability flags, and cached DTOs do not create, accept, reject, complete, rate, reopen, recover, pay, account for, or govern a service request.

A behavior that "works on screen" but bypassed a lifecycle owner is an authority violation, not a harmless shortcut. See `02-architecture.md` (System Boundaries) and `06-security-identity.md` (Dangerous Operations, Business Invariants).

---

# Business Rules

High-level durable rules only (detailed mechanics live in the owning Core guides and certifications):

- PostgreSQL is the single system of record; POS Admin UI/API state is evidence or presentation.
- Every tenant is isolated; POS Admin operates within exactly one tenant and cannot read or mutate another tenant's data.
- `Users.Role` (current SQL) is the only POS privilege axis; JWT role claims are revalidated, never trusted.
- Sales totals, taxes, and prices are server-recomputed; client-submitted money values are overwritten.
- Completed sales invoices are not rewritten; reversal is through returns.
- Purchase invoices are complete receiving documents; completed purchase corrections use certified reversal/replacement or purchase-return authority, not invoice rewriting.
- Returns are bounded by the original invoice and prior returns; they derive from SQL snapshots, not DTOs.
- Stock mutations are row-locked, lineage-validated, and append movement history; available quantity is derived.
- Supplier payments, party adjustments, and opening balances are idempotent ledger-backed operations; party shadow balances are not authority.
- Treasury operations post balanced double-entry journals through the treasury service; balances are ledger-owned.
- Outstanding balances and financial reports derive from `AccountLedgers`/posted ledger, never from stored shadow balances.
- Opening balances (customer/supplier) and adjustments are one-time/idempotent; replay converges, it does not re-execute money.
- Payment-at-sale supports Cash/Card/Mixed only; Credit and deferred sale execution fail closed.
- Publication/visibility converges from SQL lifecycle state, not from UI actions or stale projections.
- Marketplace participation never changes tenant ownership.
- Marketplace governance never replaces tenant authority.
- Vendor suspension removes Marketplace participation only; it does not disable the tenant business.
- Vendor Ban is a CommerceHub Marketplace governance restriction only. It does not disable Tenant Business, Storefront, POS Operations, Tenant Inventory, Tenant Accounting, POS Payment Methods, Tenant Products, Tenant Orders, or Tenant Settings.
- Storefront continues independently from CommerceHub vendor suspension, visibility, finance, and recovery state.
- Service Marketplace continues independently from Storefront, CommerceHub, Affiliate, and CommerceHub Super Admin governance. Service listings are not Storefront products or CommerceHub listings; service requests are not generic orders or CommerceHub orders/sub-orders.
- Service Marketplace is internal POS-to-POS only; no external customer, CommerceHub customer, affiliate visitor, anonymous actor, or Super Admin-only actor participates as provider or consumer.
- Service Marketplace provider completion alone is not final completion; final completion requires consumer confirmation.
- Service Marketplace ratings/reviews derive from final completed service request authority, not provider claims, frontend averages, duplicate review attempts, chat messages, or stale projections.
- Service Marketplace chat, attachments, notifications, analytics, search, and recovery are evidence/presentation/convergence surfaces only; they never own lifecycle, payment, accounting, inventory, order, Storefront, CommerceHub, Affiliate, or tenant authority.
- Tenant accounting remains authoritative for tenant operations.
- Marketplace finance is separate from tenant accounting.
- Marketplace payments are separate from Storefront payment methods and POS payment-at-sale methods.
- Marketplace orders are separate from Storefront orders and POS sales invoices.
- Affiliate payout review/payment-proof may originate in POS Admin, but amount/wallet/affiliate/store/currency/paid state/settlement are backend-owned and fail closed on stale or duplicate evidence.
- Outward-money readiness (marketplace payouts, affiliate payouts, unresolved captured payments) is lifecycle/readiness gated and fails closed; POS Admin presentation never bypasses those gates.

---

# Never Assume

Common misconceptions when testing POS Admin:

- That the POS Admin UI is the source of truth. It is a client over SQL authority; a rendered success is not proof an order/payment/ledger row exists.
- That a cached balance, dashboard card, or `Customer.Balance`/`Supplier.Balance`/`Cashbox.CurrentBalance` shadow is financial truth. The ledger aggregation is.
- That a tampered client total will be accepted. Server recomputes and rejects mismatches.
- That a completed invoice can be edited. It is reversed through returns, not rewritten.
- That a published product is visible regardless of SQL lifecycle. Invalid lineage blocks visibility even after a publish click.
- That POS Admin owns CommerceHub. CommerceHub is a separate business system.
- That CommerceHub owns POS Admin. POS Admin remains the tenant business system.
- That Marketplace Finance equals Tenant Accounting.
- That Marketplace Payments equal Storefront Payment Methods.
- That Marketplace Orders equal Storefront Orders.
- That Vendor Module equals Marketplace Authority.
- That Super Admin owns POS Admin.
- That a POS role grants platform authority. Only Super Admin does, and only through `RequireSuperAdmin` + SQL governance validation.
- That CommerceHub vendor operations are just "POS orders." Marketplace order/sub-order authority is a separate model with EGP-only finance.
- That purchase receiving is just an inventory adjustment. Purchasing has its own supplier, purchase invoice, cost, return, inventory, and ledger authority.
- That affiliate payout approval or mark-paid can trust frontend amount, wallet, affiliate, store, currency, status, or executor fields. Backend payout/settlement authority derives them from SQL and admitted evidence.
- That Affiliate attribution/fraud questions can be settled from POS Admin screens, referral URLs, cookies, dashboards, or operator judgment. Self-referral, identity collision, campaign/category/product eligibility abuse, suspicious payout proof, and affiliate suspension/recovery must re-enter backend Affiliate/Core authority.
- That a provider callback finalizes payment. It is evidence; `IPaymentLifecycleService` admits it.
- That a route id or DTO tenant field selects tenant ownership. Trusted tenant resolution and `TenantSaveChangesInterceptor` reject it.
- That registration creates a session. Ordinary `api/auth/register` creates a Cashier identity only.
- That storefront/affiliate/marketplace behavior belongs to POS Admin. Each has its own authority surface.
- That Service Marketplace equals CommerceHub or the CommerceHub Vendor Module. It is a separate internal POS-to-POS service collaboration capability.
- That Service Marketplace is customer-facing. Storefront shoppers, CommerceHub customers, affiliate visitors, anonymous users, and Super Admin-only actors do not participate.
- That Service Marketplace service listings are Storefront products, CommerceHub listings, Affiliate campaigns, inventory items, orders, or payment methods.
- That Service Marketplace requests are generic orders, CommerceHub marketplace orders, or CommerceHub sub-orders.
- That Service Marketplace chat is customer support chat, CommerceHub customer/vendor chat, Affiliate communication, or WhatsApp authority.
- That Service Marketplace provider completion is final completion. Consumer confirmation is required before final completion and rating eligibility.
- That Service Marketplace ratings are Storefront product reviews or CommerceHub vendor governance.
- That Service Marketplace trust/reputation can override identity, tenant, payment, accounting, request, lifecycle, visibility, or rating authority.
- That Service Marketplace search hits, service details, notifications, analytics, attachments, route ids, or cached capability flags can authorize request creation, acceptance, completion, payment, accounting, or recovery.
- That tenant search repair can delete global indices or rebuild all tenants. POS Admin repair is tenant-scoped and SQL-recomposed; global search controls are platform authority.

---

# QA Checklist

System-understanding checklist for every QA engineer before testing POS Admin (not a feature checklist):

- Can I name the authority owner for the state under test (catalog, inventory, payment, order, ledger, tenant, role)?
- Can I identify which operations belong to the tenant?
- Can I identify which operations belong to the CommerceHub Vendor Module?
- Can I identify which operations belong to Service Marketplace provider/consumer collaboration?
- Can I identify which operations belong to Super Admin?
- Can I identify which operations belong to CommerceHub?
- Can I identify which operations belong to Core Domains?
- Do I treat PostgreSQL as truth and projections/caches/DTOs as evidence?
- Do I test against current SQL state, not UI rendering or cached totals?
- Do I verify tenant isolation: no cross-tenant read or write is possible from this surface?
- Do I verify role authority is enforced server-side, not just hidden in the UI?
- Do I test fail-closed behavior: deferred credit sales, invalid lineage, replayed keys, missing tenant/session, unresolved external money?
- Do I confirm money flows post to the ledger and that balances derive from ledger aggregation?
- Do I confirm sales/returns are idempotent and bounded (no double-execution, no over-return)?
- Do I confirm purchasing/receiving/purchase returns are idempotent, supplier-scoped, warehouse-scoped, and bounded by original purchase authority?
- Do I confirm affiliate payout approval/payment-proof flows derive amount, wallet, affiliate, store, currency, executor, ledger, and settlement from backend SQL/evidence authority?
- Do I confirm Service Marketplace provider/consumer actions derive service listing, request, chat, completion, rating, trust, analytics, search, and recovery authority from current SQL rather than UI/search/chat/notification evidence?
- Do I confirm Service Marketplace remains internal POS-to-POS only, with no Storefront customer, CommerceHub customer, Affiliate visitor, anonymous actor, or CommerceHub Super Admin governance imported into the flow?
- Do I confirm Service Marketplace completion requires provider completion followed by consumer confirmation, and that rating/review eligibility depends on final completed request authority?
- Do I confirm protected lifecycle state is mutated only through its owner, never directly?
- Do I distinguish tenant-scoped repair/reindex from platform/global repair authority?
- Do I distinguish accepted tradeoffs (read lag, reconciliation states, fail-closed blocks) from real defects?
- Do I follow the owning Core guide and certifications rather than re-deriving rules from code?

---

# Common Failure Scenarios

Business-level failures to reason about (mechanics in the owning guides):

- Cross-tenant leakage: a POS Admin action reads or writes another tenant's customer, product, stock, ledger, or supplier row. Critical defect.
- Role escalation: a lower role performs a higher-role action because the UI permitted it but backend role checks were assumed absent. Defect.
- Double sale / over-return: a retried or replayed sale/return creates duplicate inventory or money effects. Bounded by idempotency and row locks.
- Stale balance drift: a report or screen shows a shadow balance that disagrees with ledger aggregation. The ledger is truth; the shadow is the bug surface.
- Tampered totals accepted: a client-submitted invoice total is honored. Server must reject.
- Invalid visibility: a deleted/archived/invalid-lineage product appears published because a projection was not reconverged. SQL lifecycle decides.
- Purchase drift: purchase receiving, average cost, stock movement, or supplier liability is created from stale supplier/product/unit/warehouse evidence or rewritten after completion. Purchasing authority must revalidate lineage and use reversal/return paths.
- Affiliate payout double execution: approval or mark-paid replay creates duplicate withdrawal, paid commission, or transfer allocation. Backend row locks, evidence uniqueness, and settlement idempotency must collapse replay.
- Tenant repair escalation: a tenant POS Admin endpoint performs global search/index deletion, all-tenant rebuild, or derivative repair from stale projection payloads. Tenant repair must be SQL-recomposed and tenant-scoped.
- Service Marketplace actor confusion: a Storefront customer, CommerceHub customer, affiliate visitor, anonymous user, or Super Admin-only actor is treated as a Service Marketplace provider/consumer. Correct behavior: fail closed; Service Marketplace actors are authenticated POS Admin tenant actors only.
- Service Marketplace lifecycle confusion: provider-only completion, chat text, uploaded proof, notification delivery, analytics, search, or frontend capability flags are treated as final completion, payment, accounting, rating, trust, or recovery authority. Correct behavior: current SQL Service Marketplace and Core authority decide; evidence-only surfaces cannot finalize truth.
- Unresolved external money: a captured payment or refund with no tenant scope blocks outward money. This is intentional fail-closed, not a POS Admin defect.
- Direct lifecycle bypass: a script or convenience path writes payment/order/inventory/ledger state outside the owner. Architectural violation.
- Session/tenant resurrection: a stale JWT, refresh credential, or deleted-tenant request regains access. Current SQL revalidation must reject it.

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

Related system guide for cross-system consistency:
- `.ai/hermes/systems/commercehub/README.md`
- `.ai/hermes/systems/super-admin/README.md`
- `.ai/hermes/systems/storefront/README.md`
- `.ai/hermes/systems/affiliate/README.md`
- `.ai/hermes/systems/service-marketplace/README.md`

Authoritative corpus used for this system guide:
- `.ai-context/ARCHITECTURE_TRUTH.md` (SQL authority, lifecycle, financial/outward-money readiness, CommerceHub emergency operations)
- `.ai-context/PROJECT_CONTEXT.md` (subsystem ownership boundaries, tenant co-ownership, publication visibility authority)
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md` (PASS* hardening narratives)
- `.ai-context/CODEX_RULES.md` (non-negotiable rules)
- `.ai-context/KNOWN_RISKS.md` (risk vocabulary)

POS Admin certifications under `docs/architecture/`:
- `pos-sales-authority-certification-findings.md` (P1-ED)
- `pos-purchasing-authority-certification-findings.md` (P1-EC)
- `pos-warehouse-inventory-authority-certification-findings.md` (P1-EB)
- `pos-product-authority-certification-findings.md` (P1-EA)
- `pos-customer-supplier-authority-certification-findings.md` (P1-EF)
- `pos-financial-treasury-authority-certification-findings.md` (P1-EE)
- `pos-financial-reporting-authority-certification-findings.md` (P1-EG)
- `pos-admin-payout-review-approval-authority-certification-findings.md` (AF-PA6.2)
- `pos-admin-payout-payment-execution-proof-settlement-authority-certification-findings.md` (AF-PA6.3)
- `pos-user-role-authority-certification-findings.md` (P1-C)
- `pos-authentication-session-authority-certification-findings.md` (P1-A)
- `pos-tenant-authority-certification-findings.md` (P1-B)

Service Marketplace certifications under `docs/architecture/`:
- `service-marketplace-listing-authority-certification-findings.md`
- `service-marketplace-request-engagement-authority-certification-findings.md`
- `service-marketplace-chat-authority-certification-findings.md`
- `service-marketplace-completion-authority-certification-findings.md`
- `service-marketplace-rating-reputation-authority-certification-findings.md`
- `service-marketplace-analytics-authority-certification-findings.md`
- `service-marketplace-frontend-final-certification-findings.md`

---

Status: System guide v1.0 — POS Admin. Core domain rules are owned by the Core Guides above; this guide explains how POS Admin uses them.
