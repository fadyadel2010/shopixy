# Shopixy Mental Model — How Hermes Should Think

This is the single most important navigation document for Hermes on the Shopixy /
PointOfSale platform. Read it FIRST, before any README, before any other guide,
before opening code.

It does not teach implementation. It teaches how to think so you reason correctly
before you read a single line of documentation.

If you remember nothing else: there is ONE system of record (PostgreSQL) and ONE
set of lifecycle owners. Every customer surface is a thin client over that shared
authority. None of them own truth.

---

# Purpose

This document exists to build the correct mental model of the entire platform in
Hermes's head before any browsing happens.

Its job:
- Tell you what every major system IS and why it exists.
- Show how systems relate, where authority lives, how information and business
  and governance flow.
- Teach the difference between evidence and authority.
- Teach you to reason correctly before you open any README or code file.

It is NOT architecture documentation. It is NOT a system guide. It is NOT a
certification. It is a thinking model. It points you at the owning documents; it
does not replace them.

When two documents conflict, follow the documented authority precedence:
`.ai-context/*` > owning Domain Guide (02–06) > `docs/architecture/*` >
source code > README/comments. Legacy or duplicated README content can become
stale over time, so System Guides and `.ai-context/*` remain the authoritative
sources. This does not mean every README is untrustworthy — evaluate each on its
own and defer to the higher-precedence source whenever they disagree.

---

# The Big Picture

Shopixy is one multi-tenant commerce operating platform. Many distinct customer
surfaces run on top of ONE backend authority model. The platform's entire job is
to let very different journeys — a POS sale, a storefront checkout, a marketplace
purchase, an affiliate referral, a service request — converge onto the SAME orders,
inventory, payments, identity, and accounting truth without ever letting one tenant,
one channel, or one stale read model corrupt another.

The major surfaces:

- Storefront: a tenant-branded online store for that tenant's OWN customers and
  products. A read-oriented customer surface.

- POS Admin: the tenant back office plus the point-of-sale terminal. The operating
  surface where a tenant manages its catalog, pricing, inventory, orders, payments,
  accounting, and runs its own POS sales.

- CommerceHub: a shared multi-vendor marketplace where many tenant-vendors sell
  through one discovery, cart, and checkout. It has its own order authority model.

- Affiliate: a referral/attribution network that drives traffic into storefront
  checkout and earns commissions. A separate identity surface plus commission
  economics.

- Service Marketplace: an internal POS-to-POS B2B space inside POS Admin only. It
  coordinates service requests, chat, completion, and ratings between tenants. It
  has NO external customers and NEVER owns payments, accounting, inventory, or orders.

- Super Admin / Platform Operations: the global authority surface for platform
  governance, CommerceHub-wide moderation, dangerous operations, and scoped repair.

The unifying truth: every surface is a client over ONE SQL authority. The platform
is Core architecture (shared backend) with independent business domains on top.

---

# Platform Layers

Shopixy is layered. Each system lives at a specific conceptual layer, and reasoning
works top-down: a surface produces a request, a business domain owns the truth,
infrastructure carries and stores it, and governance oversees without owning.

## Layer 1 — Customer Surfaces
- Storefront
- CommerceHub

Purpose: customer-facing interaction surfaces where end customers discover, browse,
and buy. They hold no truth; they express intent.

## Layer 2 — Business Operation Surfaces
- POS Admin

Purpose: the tenant operational surface where a tenant runs its own business.

Contains:
- Tenant Business — the tenant's own catalog, pricing, inventory, orders, payments,
  accounting.
- CommerceHub Vendor Module — the tenant's merchant presence inside the marketplace.
- Service Marketplace — internal POS-to-POS B2B service requests, chat, completion,
  ratings. No external customers, no money movement.

## Layer 3 — Business Domains
These are the authoritative business capabilities shared by every surface above.
Each is owned by a lifecycle service against SQL:
- Orders
- Payments
- Inventory
- Pricing
- Accounting
- Identity
- Notifications
- Search
- Analytics
- Affiliate
- Service Requests

## Layer 4 — Platform Infrastructure
These are the implementation and infrastructure services that store, transport,
project, and reconcile:
- PostgreSQL — sole system of record.
- Outbox — atomic intent recording (integration, accounting, marketplace,
  stock-sync, payment-compensation).
- RabbitMQ / MassTransit — async event backbone.
- MongoDB — derivative read models.
- Redis — cache, locks, sessions.
- Elasticsearch — search projection.
- Background Workers — reconciliation and repair.
- Projection Pipeline — rebuilds derivatives from SQL.

## Layer 5 — Governance
- Super Admin

Purpose:
- Platform governance.
- CommerceHub moderation.
- Emergency operations.
- Dangerous operations.

Never business ownership. Super Admin governs and repairs; it does not own the
business truth of any tenant.

How to reason:
  Surface
    ↓
  Business Domain
    ↓
  Infrastructure
    ↓
  Governance

NOT the reverse. Authority flows down to infrastructure and up to governance; it
never originates from a surface or a governance action.

---

# Mental Shortcuts

A compact lookup so you associate concepts with systems immediately and skip
needless browsing.

| Concept | System |
|---|---|
| Customer | Storefront |
| Vendor | CommerceHub |
| Tenant | POS Admin |
| Affiliate | Affiliate |
| Platform | Super Admin |
| Marketplace Governance | Super Admin |
| Marketplace Selling | CommerceHub |
| Service Provider | Service Marketplace |
| Service Consumer | Service Marketplace |
| POS Cashier | POS Admin |
| Order Truth | Shared Backend SQL |
| Payment Truth | Payment Lifecycle |
| Inventory Truth | Inventory Lifecycle |
| Accounting Truth | Accounting |
| Identity | Security / Identity |
| Notifications | Notification System |

Use this table to classify a task in one step before opening any document.

---

# Platform Mental Model

One canonical diagram. Everything below it is subordinate to the single SQL
authority at the top.

```text
                          Shopixy Platform
                    (ONE PostgreSQL system of record)
                                  │
      ┌───────────────────────────┼───────────────────────────┐
      │                           │                           │
 Storefront                 CommerceHub                  POS Admin
 (tenant customer           (multi-vendor               (tenant back office
  SPA — OWN products)        marketplace — EGP only)      + POS terminal)
      ▲                           │                           │
      │ affiliate traffic         │                           ├── Tenant Business
      │ lands here                │                           │   (catalog, pricing,
      │                           │                           │    inventory, orders,
      │                           │                           │    payments, accounting)
      │                           │                           ├── Vendor Module
      │                           │                           │   (CommerceHub merchant
      │                           │                           │    surface for this tenant)
      │                           │                           └── Service Marketplace
      │                           │       (internal POS→POS B2B:
      │                           │        requests, chat,
      │                           │        completion, ratings —
      │                           │        NO external customers)
      │                           │
      └───────────────┬───────────┴───────────────────────────┘
                      │
            Super Admin / Platform Operations
            (global governance, CommerceHub moderation,
             marketplace finance policy, scoped repair —
             NOT a tenant-admin escalation path)
```

Reading the diagram:
- Storefront, CommerceHub, and POS Admin are peer client surfaces over the shared
  SQL authority. None owns truth.
- POS Admin contains three sub-domains: Tenant Business (the tenant's own shop),
  Vendor Module (its CommerceHub merchant presence), and Service Marketplace
  (internal B2B only).
- Affiliate is not a separate top-level surface; it drives traffic INTO the
  storefront and attaches immutable attribution to a storefront order.
- Super Admin spans the whole platform and especially governs CommerceHub, but it
  is platform authority — not a shortcut into any tenant's admin.

---

# The Six Major Systems

## Storefront
- Why it exists: to give each tenant's own customers a branded online shopping
  surface for that tenant's own products.
- Who uses it: end customers (guest or authenticated) of a specific tenant.
- What it owns: nothing. It is a read-oriented client. Checkout is real, but every
  authoritative decision (pricing, reservation, payment, inventory) is recomputed
  server-side from SQL.
- What it never owns: truth. Order, pricing, reservation, payment, and inventory
  authority all live in the backend. Client totals and discounts are stale-client
  assertions.

## POS Admin
- Why it exists: to operate a tenant's business — back office management plus the
  point-of-sale terminal where staff make sales.
- Who uses it: tenant admin operators and POS cashiers.
- What it owns: the operating surface that initiates catalog, pricing, publication,
  order/payment lifecycle operations, inventory state, and accounting views for that
  tenant — all through their respective lifecycle services. It also hosts the Vendor
  Module (CommerceHub merchant surface) and Service Marketplace (internal B2B).
- What it never owns: outward-money readiness, marketplace finance, platform/global
  authority, or any other tenant's data. It is a client surface, not a separate
  database.

## CommerceHub
- Why it exists: to run a shared multi-vendor marketplace where many tenant-vendors
  sell through one discovery, cart, and checkout.
- Who uses it: CommerceHub customers (shopping across vendors) and vendors (tenant
  actors operating merchant surfaces).
- What it owns: its OWN order authority model — the marketplace order plus per-vendor
  sub-orders — deliberately separate from the generic storefront Order. Marketplace
  currency is platform-authority EGP only. It owns marketplace governance visibility
  decisions (suppression/suspension) as SQL-backed state.
- What it never owns: the generic storefront order model; approval of merchant
  prices; payment/order/fulfillment truth beyond its own sub-order lifecycle;
  non-EGP currency reconciliation. Its payment path is explicitly excluded from the
  Stripe/Paymob reference-provider certification.

## Affiliate
- Why it exists: to drive referral traffic into storefront checkout and pay
  commissions on attributed conversions.
- Who uses it: affiliate portal users (a SEPARATE identity surface) and the
  customers they refer.
- What it owns: affiliate identity/portal, plus the attribution and commission
  economics — AffiliateAttributions (immutable at conversion), commission/balance/
  payout-request rows, and payout-allocation idempotency. All SQL-authoritative.
- What it never owns: inventory reservations (the word "reservation" is shared but
  the concept is different), the storefront order truth (an affiliate order IS a
  storefront Order with an attribution row), or CommerceHub marketplace sub-orders
  unless a separate marketplace-affiliate model is explicitly certified.

## Service Marketplace
- Why it exists: an internal POS-to-POS B2B channel inside POS Admin only, letting
  one tenant request services from another, coordinate via chat, mark completion,
  and rate.
- Who uses it: tenant actors (operators) on both sides of a service request.
- What it owns: service requests, their chat, completion state, and ratings between
  tenants — all within the POS Admin boundary.
- What it never owns: external customers (it is not CommerceHub/Storefront/Affiliate),
  payments, accounting, inventory, or orders. It is governed by POS Admin, not by
  CommerceHub Super Admin. It never moves money.

## Super Admin
- Why it exists: to provide global platform authority — governance, CommerceHub-wide
  moderation, dangerous operations, and scoped repair.
- Who uses it: platform actors behind `RequireSuperAdmin` plus current SQL
  SuperAdmin / TokenVersion / governance validation.
- What it owns: platform/global operational scope, marketplace-wide moderation,
  scoped repair primitives, and marketplace governance policy — under runbook
  authority.
- What it never owns: the platform's business truth. It is NOT a tenant-admin
  shortcut and cannot be inferred from a tenant role. Emergency/break-glass work is
  operational convergence authority, not business truth.

---

# The Golden Rules

Durable truths. Memorize them; they resolve most ambiguity.

- Backend owns truth. The backend decides; surfaces display.
- SQL owns authority. PostgreSQL is the authoritative write model. The SQL commit
  is the only durability boundary.
- Presentation never owns authority. A UI, a DTO, a route, a frontend total — none
  are truth.
- Evidence never owns authority. MongoDB, Redis, Elasticsearch, RabbitMQ payloads,
  provider/carrier webhooks, JWT claims, cookies, and frontend state are derivative
  or evidence. They lag or are admitted; they never decide.
- Governance never equals ownership. Super Admin governs; it does not own the
  business truth it moderates.
- Visibility never equals inventory. A product shown in search or a listing is not
  the same as stock available to reserve.
- Settlement never equals payment. Settlement membership is live SQL lineage, not a
  denormalized header count.
- Operational intent never equals truth. A cancellation request, a refund intent, a
  payout request are intents until lifecycle admission writes SQL.
- Recovery restores authority. Reconciliation/repair reloads current locked SQL
  truth before any mutation; it converges by apply / delete / tombstone / no-op /
  retry / reconciliation-required. Recovery never invents money.
- Fail Closed. Under uncertainty about external money or backlog scope, integrity is
  preferred over availability. Processing / ReconciliationRequired / suspense /
  quarantine / manual-review are intentional safe states, not limbo.
- Tenant isolation is mandatory. A TenantId column is necessary, not sufficient.
  Scope requires trusted resolution + current SQL active tenant lineage + tenant
  co-ownership of referenced records. Cross-tenant leakage is CRITICAL.
- Never invent authority. Do not assume a behavior, boundary, or owner not present in
  the authoritative corpus.
- Never bypass lifecycle owners. Payment, order, fulfillment, inventory, accounting,
  identity, and affiliate protected states change ONLY through their lifecycle owner.
  A direct write that "works" is still an architectural violation.
- Recompute, never trust. Price, discount, tax, shipping, payment-method eligibility,
  and payment split are recomputed server-side from current SQL. Client values are
  stale-client assertions.
- Provider = evidence, not authority. External success/failure is admitted by the
  lifecycle owner and reconciled into SQL; it never overrides SQL.
- Idempotent and replay-safe. Ordering is per-aggregate only; no global total order.
  Duplicate/replayed events must collapse via fences/ProcessedEvents.
- Currency sovereignty per surface. CommerceHub is EGP-only platform authority; a
  non-EGP group is an anomaly, never reconciled.
- Outward money is gated. Refunds/payouts run the readiness check inside the same
  serializable transaction as the local authority mutation. A scope-less unresolved
  captured-payment row is GLOBALLY blocking for outward money.

---

# How Information Flows

The path every write takes, from surface to derivative view:

1. Operational Surface — a client (POS Admin, Storefront, CommerceHub, Affiliate
   portal, WhatsApp) issues a request. It holds no truth.
2. Backend Validation — trusted middleware resolves tenant scope once (authenticated
   claim, then verified host/domain). X-Tenant-Id is stripped; never trusted.
3. Lifecycle Owner — the request reaches the single legal mutation path for the
   protected state (payment/order/fulfillment/inventory/accounting/identity/affiliate).
   Direct writes are violations.
4. SQL — inside a serializable/locked transaction the backend recomputes authoritative
   values from current SQL and commits. The SQL commit is the durability boundary.
5. Outbox — the intent is written ATOMICALLY with the business mutation (integration,
   accounting, marketplace, stock-sync, payment-compensation outboxes).
6. Projection — downstream, asynchronously and possibly lagging, Mongo/Redis/
   Elasticsearch rebuild from SQL. They are triggers only; they recompose SQL
   authority and tombstone when SQL no longer proves visibility.
7. Presentation — the surface shows the repaired read model. It can be briefly stale;
   that is accepted, not a bug.

External providers and webhooks feed EVIDENCE back in at step 2/3. The lifecycle
owner admits it and reconciles into SQL. Evidence never overrides SQL.

---

# How Decisions Flow

The path a customer decision takes, end to end:

1. Customer Intent — a human wants to buy, sell, refer, request a service, or govern.
2. Operational Surface — the intent is entered through the relevant client surface.
3. Backend Validation — tenant scope resolved; request authenticated and authorized.
4. Business Rules — pricing, discounts, coupons, promotions, affiliate commission
   policy, payment-method limits, provider gating recomputed from the in-transaction
   SQL snapshot.
5. Lifecycle — the owning lifecycle service validates and transitions protected state.
6. Persistence — the serializable SQL transaction commits; outbox intent recorded
   atomically.
7. Projection — derivatives rebuild asynchronously from SQL.
8. Customer Sees Result — the surface reflects the repaired read model.

The origin surface changes the UI and the assurance level. The authority (SQL +
lifecycle owners) does not change.

---

# System Relationships

| System | Primary Responsibility | Reads From | Provides To | Never Owns |
|---|---|---|---|---|
| Storefront | Tenant-branded customer shopping + checkout | SQL authority (recomputed); Mongo/Redis/ES as derivative views | Checkout intent to backend; storefront read models | Any business truth; inventory/payment/order authority |
| POS Admin | Tenant back office + POS sales; hosts Vendor Module & Service Marketplace | SQL authority (recomputed); derivative read models | Shared SQL truth that other surfaces read; publication/inventory/pricing events | Outward-money readiness; marketplace finance; platform/global authority; other tenants' data |
| CommerceHub | Multi-vendor marketplace orders, sub-orders, EGP finance, governance visibility | SQL authority; CommerceHub Mongo/ES read models; unresolved webhook evidence | Marketplace orders/sub-orders; vendor earnings via shared ledger; search overlay (EGP) | Generic storefront Order model; merchant price approval; non-EGP reconciliation; reference-provider-certified payment |
| Affiliate | Referral attribution + commission economics; separate portal identity | SQL authority; affiliate Mongo read models; portal session state | Immutable attribution on storefront orders; payout requests (outward money, gated) | Inventory reservations; storefront order truth; CommerceHub sub-orders (unless separately certified) |
| Service Marketplace | Internal POS→POS B2B service requests, chat, completion, ratings | SQL authority within POS Admin boundary | Service request/chat/completion/rating state to POS Admin | External customers; payments; accounting; inventory; orders |
| Super Admin | Platform governance, CommerceHub moderation, scoped repair, dangerous ops | SQL platform/governance state; repair candidates; derivative diagnostics | Global repair/reindex/cache/search controls; governance decisions | Business truth of any tenant; tenant-admin escalation path |
| Shared Platform Services | Authoritative write host, async backbone, projections, reconciliation, lifecycle owners | PostgreSQL (sole truth); provider/carrier evidence via trust pipeline | Events via IEventPublisher; outbox-delivered projections; reconciliation convergence | Nothing customer-facing; no truth outside SQL |

---

# Ownership Matrix

For each capability, the owning system. "Shared Core" means the lifecycle service /
SQL authority owned by PointOfSale.API / Core / Infrastructure.

| Capability | Owning System |
|---|---|
| Products (tenant catalog) | POS Admin (tenant business) — SQL authority |
| Inventory | Shared Core (InventoryConsistencyService) — cross-channel SQL |
| Pricing | POS Admin (tenant pricing); CommerceHub listings for marketplace |
| Orders (generic) | Shared Core (Order lifecycle service) |
| Marketplace Orders (sub-orders) | CommerceHub (marketplace sub-order service) |
| Payments | Shared Core (Payment Lifecycle Service) |
| Settlement | Shared Core / Accounting (SQL `PaymentSettlementOrders` lineage) |
| Accounting | Shared Core (immutable double-entry ledger) |
| Affiliate | Affiliate subsystem (attribution/commission + portal identity) |
| Marketplace Governance | Super Admin / Platform Governance (SQL-backed visibility) |
| Service Requests | Service Marketplace (within POS Admin) |
| Identity | Shared Core (four surfaces + separate affiliate portal) |
| Notifications | Shared Core (triggered by lifecycle events; outputs only) |
| Analytics | Shared Core (analytics foundation) |
| Search | Shared Core (Elasticsearch projection; SQL overlay authority) |
| Ratings | Service Marketplace (service ratings) |
| Reviews | Storefront / CommerceHub (product reviews) |
| Recovery | Shared Core (reconciliation/repair workers; dangerous ops gated by Super Admin) |

---

# Typical Business Journeys

Summaries only. The origin UI changes; the authority does not.

## Storefront Purchase
1. Customer adds to cart on the tenant storefront.
2. Checkout runs a serializable SQL transaction: re-locks config/shipping/payment/
   methods/variant, recomputes pricing + policies from the snapshot, reserves
   inventory, persists order + immutable snapshots.
3. Backend creates the Payment Intent; provider gated per tenant (Stripe/Paymob
   certified here).
4. Generic Order created with idempotency binding after claim-token ownership proven.
5. Inventory reservation consumed on fulfillment through current lineage.
6. Ledger posts on collected payment; notifications + projections converge from SQL.

## Marketplace Purchase
1. Customer discovers across vendors; cart groups items by StoreId.
2. Marketplace checkout recomputes per-store listing/variant/price/stock from SQL;
   creates marketplace order + per-store sub-orders; reserves with EXACT per-tenant
   ownership/quantity.
3. Currency is EGP-only; non-EGP is an anomaly.
4. Payments fenced before the external boundary; unmatched webhooks persisted as
   unresolved SQL evidence; suspense blocks outward money. Not reference-certified.
5. Fulfillment per sub-order; partial cancellation does not cancel unrelated stores.
6. Vendor earnings/payouts through the shared ledger; EGP reconciliation only.

## Affiliate Purchase
1. Referred customer lands on storefront via affiliate link/attribution.
2. Normal storefront checkout, BUT in the same transaction an immutable
   AffiliateAttributions row is stamped and commission policy is row-locked +
   recomputed.
3. Result is a generic storefront Order (Source = Ecommerce) + attribution; channel
   identity fixed, never leaks.
4. Inventory/payment/order/accounting traverse the SAME shared domains.
5. Commission/balance/payout are SQL; payout is outward money gated by readiness;
   payout-transfer idempotency is global per payout reference.

## Service Marketplace Request
1. A tenant operator creates a service request to another tenant inside POS Admin.
2. Both sides coordinate via chat; completion is marked by the serving tenant.
3. Requester rates the completed service; ratings are SQL within POS Admin.
4. No external customers, no payment, no accounting, no inventory, no orders are
   involved. Entirely internal B2B.

## Vendor Marketplace Flow
1. A POS tenant actor operates its CommerceHub merchant (Vendor Module) surface.
2. Vendor eligibility is recomputed from current SQL governance and tenant/vendor
   lineage.
3. Marketplace governance may suppress visibility, suspend/ban, trigger scoped
   reindex, enforce COD-debt limits.
4. Public surfaces re-apply current SQL product/listing/vendor/pricing/stock
   authority before visibility.
5. Governance does NOT approve merchant prices and does NOT mutate order/payment/
   fulfillment truth.

## Super Admin Governance Flow
1. A platform actor authenticates and passes `RequireSuperAdmin` + current SQL
   SuperAdmin / TokenVersion / governance validation.
2. Exercises global repair/reindex/cache/search controls or CommerceHub-wide
   moderation.
3. May run scoped repair / dangerous primitives under runbook authority; recovery
   reloads locked SQL truth before mutating.
4. Never a tenant-admin escalation; never business truth of any tenant.

---

# Common AI Mistakes

Mistakes Hermes must never make:

- Confusing Storefront with CommerceHub. Different authority models, different order
  graphs, different currency rules.
- Confusing Vendor with Customer. A vendor is a tenant actor on a CommerceHub merchant
  surface, not a shopping customer.
- Thinking Super Admin owns the platform. It governs; it does not own business truth.
- Treating dashboards as authority. A number on a screen is a derivative view.
- Treating Mongo/Redis/ES as truth. They lag by design; SQL wins.
- Treating payment provider callbacks as payment truth. They are evidence until
  lifecycle admission.
- Treating Service Marketplace as customer-facing. It is internal POS→POS B2B with no
  external customers and no money movement.
- Treating Affiliate payout as a bank transfer. Payout is outward money gated by
  readiness; the transfer is a separate external rail.
- Treating Visibility as Publication. A visible/searchable listing is not the same as
  stock available to reserve or an order created.
- Treating a generic Order rule as a CommerceHub sub-order rule, or vice versa.
- Treating CommerceHub payment as assured as storefront payment. It is excluded from
  the Stripe/Paymob reference certification.
- Treating "affiliate reservation" as inventory reservation. Different concepts,
  different owners.
- Treating reconciliation/suspense/quarantine as limbo to be cleared. They are
  intentional safe states; forcing a false terminal state is a defect.
- Trusting X-Tenant-Id, a JWT claim, or a client tenant id as tenant scope. Scope is
  proven by trusted resolution + current SQL lineage.
- Assuming a non-EGP CommerceHub group is legitimate. Marketplace reconciliation
  currency is EGP only.
- Inventing architecture, lifecycle owners, or boundaries not present in the
  authoritative corpus.

---

# Before Opening Code

Checklist — answer these before you read any implementation:

1. Which system owns this? (Storefront / POS Admin / CommerceHub / Affiliate /
   Service Marketplace / Super Admin / Shared Core)
2. Which lifecycle owns the protected state? (payment / order / fulfillment /
   inventory / accounting / identity / affiliate)
3. Which Core Guide owns the rule? (02-architecture / 03-inventory / 04-payments /
   05-orders / 06-security-identity)
4. Which platform overview section frames the journey? (07-platform-overview)
5. Which certification applies? (.ai/hermes/certification/* or CROSS_SYSTEM_
   CERTIFICATION.md)
6. Am I looking at evidence or authority? (Mongo/Redis/ES/webhook/claim = evidence;
   SQL commit = authority)
7. Is the tenant scope proven, not claimed? (trusted resolution + SQL lineage +
   co-ownership)
8. If uncertain external money or a late/stale replay arrives, does the system fail
   closed and recompose SQL — instead of resurrecting visibility or faking a terminal
   state?

If you cannot name the owning system and the owning lifecycle, do not judge the
behavior yet. Find the authority owner first. When in doubt, the `.ai-context/*`
files are the truth and the System Guides remain authoritative.

---

# When Lost

When you cannot classify a task or locate its authority, do NOT start with random
browsing. Follow this path instead:

  Unknown Task
    ↓
  Read TASK_ROUTING.md
    ↓
  Identify Task Category
    ↓
  Follow Routing
    ↓
  Open only required READMEs
    ↓
  Read Core Guides (02–06)
    ↓
  Open Code

Random browsing should never be the first strategy. Route first, then read the
owning guide, then open code — in that order. If routing still leaves the task
ambiguous, return to the authority precedence and the owning lifecycle rather than
guessing from a surface or a projection.
