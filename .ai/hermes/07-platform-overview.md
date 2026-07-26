# Shopixy Platform Overview — Senior QA Onboarding (07)

Audience: a Senior QA Engineer (human or AI) joining Shopixy / PointOfSale and needing the
whole-platform mental model before testing any single feature.

This is the "map of the map." It does NOT replace the per-domain guides:
- 02-architecture.md   — authority model, SQL/derivative split, QA rules, risk vocabulary
- 03-inventory.md      — stock, warehouses, reservations, movements, lineage
- 04-payments.md       — payment lifecycle, providers, webhooks, refunds, reconciliation
- 05-orders.md         — order/checkout/fulfillment/cancellation/return/refund/projection
- 06-security-identity.md — identity, tenant isolation, authority boundaries, fail-closed

Read those first. This document explains how the pieces fit together and how a customer request
travels across the entire platform. When two documents' durable claims conflict, the per-domain
guide and the .ai-context authority files win; this overview intentionally omits per-domain
mechanics and points you at the owning guide.

When in doubt, the .ai-context files are the truth. The README is STALE and must not be trusted.

----------------------------------------------------------------
# Purpose

Give a QA engineer a durable, whole-platform mental model so that:

- Any feature being tested can be located inside the platform and traced to the subsystems and
  shared domains it touches.
- Authority is never mistaken for a derivative surface (a read model, cache, projection, webhook
  payload, or frontend state).
- A cross-system journey (cart -> checkout -> payment -> inventory -> fulfillment -> accounting ->
  notification) is understood as one coherent flow, not seven independent ones.
- Cross-tenant leakage, replay/duplicate resurrection, and fail-closed financial gating are
  recognized as first-class concerns regardless of which UI originated the request.

----------------------------------------------------------------
# Platform Vision

Shopixy is one multi-tenant commerce operating platform that runs several distinct customer-facing
surfaces under ONE backend authority model:

- A tenant back office + point of sale (POS Admin).
- A tenant-branded online storefront for that tenant's own customers.
- A shared multi-vendor marketplace (CommerceHub) where many tenants sell through one discovery/cart.
- An affiliate/referral network that drives traffic into storefront checkout and earns commissions.
- A Super Admin / platform-operations surface for global authority, governance, and scoped repair.
- A WhatsApp messaging channel that reuses the same backend authority.

The unifying idea: there is exactly ONE system of record (PostgreSQL) and ONE set of lifecycle
owners (the services that may legally mutate protected state). Every customer surface — admin UI,
storefront, marketplace, affiliate portal, WhatsApp — is a thin client over that shared authority.
None of them own truth. The platform's job is to let very different journeys converge onto the same
orders, inventory, payments, identity, and accounting truth without ever letting one tenant, one
channel, or one stale read model corrupt another.

----------------------------------------------------------------
# Major Systems

## POS Admin (tenant back office + point of sale)
The operating surface for a tenant. It initiates catalog, pricing, tenant config, CMS draft/live
publication, order/payment lifecycle operations, inventory state, accounting views, vendor-facing
operations, and tenant-scoped payout workflows through the owning lifecycle services. It does NOT own
outward-money readiness, marketplace finance, or platform/global authority. Crucially, POS Admin is not
"a different database" — it is one of the client surfaces in front of the shared SQL authority. See
02-architecture.md (authoritative data sources), 03-inventory.md (POS warehouse/transfer/adjustment
authority), and 06-security-identity.md (tenant vs platform authority).

## Storefront
A tenant-branded customer SPA for that tenant's OWN products. It is a read-oriented customer surface:
it consumes storefront APIs plus projected read models and does not own business truth. Checkout is
real, but the authoritative order, pricing, reservation, payment, and inventory decisions are all
recomputed server-side from SQL (serializable checkout transaction). See 05-orders.md (Storefront
checkout) and 04-payments.md (Stripe/Paymob reference-provider coverage for POS Admin + Storefront).

## CommerceHub (multi-vendor marketplace)
A shared marketplace where many tenant-vendors sell through one discovery, cart, and checkout. It has
its OWN order authority model (marketplace order + per-vendor sub-orders) that is deliberately separate
from the generic storefront Order — do not conflate them. CommerceHub currency is platform-authority
EGP only. CommerceHub payment paths are explicitly excluded from the Stripe/Paymob reference-provider
certification, so their unresolved-webhook / suspense / payout-readiness rules are first-class. See
05-orders.md (Marketplace sub-order lifecycle, split-order shipping) and 04-payments.md (CommerceHub
exclusion, unresolved-payment webhooks, EGP reconciliation).

## Affiliate
A SEPARATE non-POS-admin identity surface plus the attribution/commission economics. An affiliate
conversion is fundamentally a storefront Order (Source = Ecommerce) carrying an immutable
AffiliateAttributions row stamped in the same checkout transaction. Do NOT apply this storefront
affiliate model to CommerceHub marketplace sub-orders unless a separate marketplace-affiliate authority
model is explicitly certified. Affiliate identity/portal has its own session certification and must not
be assumed to share the POS operational boundary. Commission/balance/payout are SQL-authoritative and
distinct from inventory reservations (which share the word "reservation"). See 05-orders.md
(Affiliate-origin order, CommerceHub separation) and 03-inventory.md (reservation-type conflation warning).

## Super Admin / Platform Operations
The global authority surface for platform actors, governance, dangerous operations, marketplace-wide
moderation, and scoped repair. It is NOT a tenant-admin shortcut and cannot be inferred from a tenant
role. Every platform operation must pass `RequireSuperAdmin` plus current SQL SuperAdmin / TokenVersion /
governance validation, and emergency/break-glass work remains operational convergence authority — not
business truth. See 06-security-identity.md (Super Admin Rules, Dangerous Operations, platform vs tenant
authority).

## Shared Platform Services
The backend substrate every surface depends on, owned by PointOfSale.API / Core / Infrastructure:
- Authoritative write host (REST, auth, webhooks, SignalR, Hangfire, hosted workers).
- Async backbone (RabbitMQ + MassTransit) with multiple SQL outboxes (integration, accounting,
  marketplace, stock-sync, payment-compensation).
- Projection/derivative layers (MongoDB, Redis, Elasticsearch) that lag by design.
- Background reconciliation/repair services that close gaps when projections or provider callbacks lag.
- Lifecycle owners (payment, order, fulfillment, inventory, accounting, identity, affiliate) that are
  the ONLY legal mutation paths for protected state.
- WhatsApp transport (Baileys Node service) — transport/session state only, not business truth.

----------------------------------------------------------------
# Shared Domains

These are the durable capabilities that span every customer surface. Each is owned by one authority
and consumed (read/triggered) by many. Full mechanics live in the named guide; here we explain the
SHARING.

## Orders
Owned by the order lifecycle service (+ marketplace sub-order service for CommerceHub, + fulfillment
and refund/return services). Both the generic storefront Order and the CommerceHub marketplace order
are SQL-authoritative. Every channel (POS Admin, storefront, marketplace, affiliate-origin) produces
an order through the same architectural principles — SQL authority, lifecycle-owner mutation, tenant
isolation, idempotency, and fail-closed replay — but NOT the same lifecycle graph. Generic `Order` and
CommerceHub sub-orders are separate authority models. Channel identity is fixed at creation and never
leaks across channels. See 05-orders.md.

## Inventory
Owned by the inventory consistency service against PostgreSQL. Stock, warehouses, reservations, and
movements are shared by ALL channels: a POS sale, a storefront checkout, a marketplace sub-order, and
an affiliate-driven storefront order all reserve/consume the SAME SQL stock lineage. Availability is
derived from constrained SQL quantities; no client or channel may submit an authoritative balance.
Tenant co-ownership is mandatory on every inventory row. See 03-inventory.md.

## Payments
Owned by the payment lifecycle service; provider callbacks are evidence only. The payment aggregate is
shared across channels but gated per surface: Stripe/Paymob are certified reference providers for
POS Admin + Storefront only; CommerceHub/marketplace is excluded and treated as a lower-assurance
surface. A single unresolved captured-payment row with no tenant scope is GLOBALLY blocking for outward
money. See 04-payments.md.

## Identity
Four backend-owned identity surfaces — POS/Admin tenant actors, Super Admin platform actors, CommerceHub
customers, and Vendors (POS actors operating marketplace surfaces) — plus the separate affiliate portal
identity. PostgreSQL is identity authority; JWTs/claims/cookies are evidence only and are recomputed at
request time. Authority is recomputed, never trusted from a token. See 06-security-identity.md.

## Accounting
The immutable double-entry ledger in PostgreSQL is the SOLE financial reporting authority. Payment,
refund, settlement, payout, vendor earning, affiliate commission, and COD receivable effects all post
into this ledger through the accounting outbox. CommerceHub reconciliation currency is EGP only. See
04-payments.md (Accounting Integration, Reconciliation) and 06-security-identity.md (Business Invariants).

## Treasury, Settlements & Outward Money
Vendor earnings, affiliate payouts, refunds, settlements, COD receivables, and payout readiness are
shared financial authority, not POS Admin-owned presentation state. Outward money movement is gated by
readiness checks; scope-less unresolved external-money ambiguity blocks outward money until reconciled.
Settlement membership is live SQL `PaymentSettlementOrders` lineage, not denormalized header counts. See
04-payments.md (Refund Rules, Accounting Integration) and 06-security-identity.md (Business Invariants).

## Refunds & Returns
Refund authority belongs to payment/refund lifecycle services; return state can gate refund execution but
cannot forge refund success. Physical return evidence, manual refund transfer proof, and provider refund
responses are evidence until admitted into SQL authority. Post-settlement refunds must prove live settlement
lineage. See 04-payments.md (Refund Rules) and 05-orders.md (Return Rules, Refund Integration).

## Shipping & Shipments
Shipment identity, tracking, carrier assignment, and carrier callbacks are backend-owned evidence paths,
not order or fulfillment truth by themselves. Shipment progress may synchronize fulfillment only through
the fulfillment owner; split-order shipping remains immutable per sub-order after creation. See
05-orders.md (Fulfillment Rules, shipment authority).

## Marketplace Governance & Vendor Visibility
Marketplace governance owns SQL-backed visibility/suppression decisions and vendor eligibility, not merchant
price approval or payment/order state. Suspended/banned vendors, COD-debt hard limits, and governance
suppression must be applied across search, discovery, detail, homepage, recommendation, and checkout
eligibility. See 06-security-identity.md (Authority Boundaries, Business Invariants, High Risk Areas).

## Notifications
Derived/triggered by lifecycle events (order, payment, shipment, fulfillment, refund, payout). They are
outputs of authoritative state, never inputs to it. WhatsApp delivery is a transport concern; the
business decision (e.g. COD collection recognized, order delivered) stays in the lifecycle owner. A
notification firing or not firing does not change truth. Treat notification state as presentation, not
authority.

## Search
Elasticsearch is a non-authoritative projection of catalog/marketplace/public-affiliate discovery. Every
search surface (storefront, CommerceHub, affiliate discovery) must recompose current SQL
product/listing/category/variant/price/stock/publication authority through a SQL authority overlay before
serving or indexing. A search result or ranking can never restore visibility that SQL has removed.
See 02-architecture.md (Projection rules) and 03/04/05 guides' projection sections.

## Projections
MongoDB read models, Redis caches, Elasticsearch indices, affiliate/CommerceHub visibility, and rebuild
paths are TRIGGERS ONLY. They must reload current SQL authority and tombstone/delete when SQL no longer
proves visibility. They lag by design (SQL commit -> outbox intent is atomic; outbox -> projection is
asynchronous). No projection may become authority; a missing/duplicate/stale projection is an accepted
tradeoff unless the projection path itself violates a SQL-recomposition rule. See 02-architecture.md (R6)
and the projection sections of 03/05.

----------------------------------------------------------------
# System Relationships

The platform is a hub-and-spoke around one SQL authority, with derivative spokes that always converge
back to SQL.

- Every client surface (POS Admin, Storefront, CommerceHub, Affiliate portal, WhatsApp) issues requests
  to the shared API host. None of them hold truth.
- A mutation request is validated and recomputed server-side from current SQL authority, then committed
  inside a serializable/locked transaction that re-locks the relevant rows (store config, shipping
  methods, tenant payment methods, product/variant, inventory, order, payment).
- On commit, the SQL outbox stage records the intent ATOMICALLY with the business mutation. Downstream
  projection/outbox delivery is asynchronous and may lag.
- Protected state changes (payment, order, fulfillment, inventory, accounting, identity, affiliate) go
  ONLY through their lifecycle owner; direct writes bypass guards and are architectural violations.
- Authoritative integration events travel via the event publisher (never raw bus publish); per-aggregate
  ordering only, no global total order; consumers must be idempotent (ProcessedEvents / ProjectionEventFence).
- External providers and carrier/webhook callbacks feed EVIDENCE back in. That evidence is admitted by the
  lifecycle owner and reconciled into SQL; it never overrides SQL.
- Background reconciliation/repair hosted services scan derivative/provider/ledger evidence for candidates,
  then reload current locked SQL truth before any mutation (recovery rule). They converge by apply / delete /
  tombstone / no-op / retry / reconciliation-required.
- Tenant context is resolved once, by trusted middleware (authenticated claim, then verified host/domain);
  X-Tenant-Id is stripped and never trusted as authority. Every downstream write enforces tenant scope, and
  financial/inventory rows additionally require tenant CO-OWNERSHIP of referenced records.

Data-flow summary for any write path:
  Client surface -> trusted tenant resolution -> server-side recompute from SQL -> lifecycle owner ->
  SQL commit (atomic outbox) -> async projection -> (later) provider/webhook evidence -> lifecycle admission
  -> SQL reconciliation -> projections converge.

----------------------------------------------------------------
# Authority Map

For each subsystem: Owner (who may legally mutate), Authority (what SQL decides), Evidence (what is
derivative/non-authoritative), Integration Points (how it connects to the rest).

## POS Admin
- Owner: tenant admin actors via the API host; catalog/pricing/publication/inventory/order/payment/
  accounting owned by their respective lifecycle services.
- Authority: PostgreSQL (catalog, pricing, tenant config, inventory, orders, payments, accounting,
  tenant-scoped workflow state; finance/payout authority remains with payment/accounting/platform owners).
- Evidence: Web UIs, Mongo/Redis/ES read models, provider callbacks, cached totals.
- Integration Points: writes the shared SQL truth that storefront/marketplace/affiliate read; emits
  publication/inventory/pricing events; operates CommerceHub vendor surfaces as a POS actor.

## Storefront
- Owner: the backend (checkout service, order/payment/inventory lifecycle owners). Storefront SPA never
  owns truth.
- Authority: PostgreSQL order/payment/inventory/customer/tenant rows.
- Evidence: storefront Mongo product/order read models, Redis, ES, frontend DTOs/route state, client totals.
- Integration Points: checkout re-locks SQL and reserves inventory via the shared inventory service;
  payment goes through the shared payment lifecycle; projections repair from SQL; shares generic Order
  model with affiliate-origin orders.

## CommerceHub
- Owner: marketplace order/sub-order service, payment lifecycle (excluded from reference-provider cert),
  fulfillment, refund/return, marketplace governance (SQL-backed visibility/suppression only; not price approval).
- Authority: PostgreSQL marketplace orders/sub-orders, CommerceHub finance, vendor earnings, EGP
  reconciliation, unresolved-payment evidence.
- Evidence: CommerceHub Mongo order/listing read models, ES marketplace search, listing images, customer
  portal state, provider webhooks (often unmatched -> persisted as unresolved evidence).
- Integration Points: reuses shared inventory reservation authority (exact per-tenant ownership/quantity);
  shared payment lifecycle with stricter unresolved-webhook/suspense rules; shared ledger for vendor
  earnings/payouts; shared search overlay (EGP-only); split-order shipping derived per store.

## Affiliate
- Owner: affiliate portal identity service + commission/attribution owned by the affiliate/order services.
- Authority: PostgreSQL affiliate identity, AffiliateAttributions (immutable at conversion), commission/
  balance/payout-request rows, payout-allocation idempotency (global per payout reference).
- Evidence: affiliate dashboards/earnings Mongo read models, discovery docs, portal session state.
- Integration Points: an affiliate order IS a storefront Order + immutable attribution row in the same
  checkout transaction; commission policy is row-locked and recomputed at checkout; payout is outward money
  gated by the shared readiness/readiness reconciliation rules. Distinct from inventory reservations.

## Shared Platform Services
- Owner: PointOfSale.API/Core/Infrastructure; lifecycle services; reconciliation/repair hosted workers.
- Authority: PostgreSQL for all business truth; the SQL commit boundary is the durability boundary.
- Evidence: MongoDB, Redis, Elasticsearch, RabbitMQ payloads, provider/carrier state, frontend/client state.
- Integration Points: event publisher + multiple SQL outboxes; tenant resolution middleware; webhook trust
  pipeline ([AllowAnonymousTenant] + signed payload + SQL record); WhatsApp transport over HTTP; lifecycle
  interceptors; projection fences/guards; reconciliation jobs.

## Super Admin / Platform Operations
- Owner: platform actors behind `RequireSuperAdmin` and current SQL SuperAdmin / TokenVersion / governance
  validation.
- Authority: platform/global operational scope, governance, marketplace-wide moderation, scoped repair,
  dangerous primitives under runbook authority.
- Evidence: JWT role claims, frontend controls, cached admin state, repair candidates, derivative diagnostics.
- Integration Points: global repair/reindex/cache/search controls; marketplace governance; emergency operations;
  platform audit; never a tenant-admin escalation path.

----------------------------------------------------------------
# Customer Journeys

Each journey is shown as the sequence of shared domains it traverses. The key QA insight: the journey's
ORIGIN (which UI) changes, but the AUTHORITY (SQL + lifecycle owners) does not.

## POS Sale
1. POS Admin operator creates/sells an order in the back office.
2. Inventory: reserve/consume through the shared inventory service against SQL stock lineage.
3. Payments: COD or an online provider; payment state via the shared payment lifecycle. COD delivery and
   cash collection are separate facts.
4. Orders: generic Order lifecycle; fulfillment via the fulfillment service.
5. Accounting: ledger posts the sale/COD receivable.
6. Notifications/Projections: triggered, derivative.
Authority note: this is the highest-assurance path (POS Admin + Stripe/Paymob certified). See 03-inventory.md
(POS warehouse authority) and 04-payments.md (reference providers).

## Storefront Checkout
1. Customer (guest or authenticated) adds to cart on the tenant storefront.
2. Checkout runs a SERIALIZABLE SQL transaction: re-locks store config/shipping/payment-methods/
   product-variant, recomputes pricing, re-runs stale-client + payment-method-limit + coupon/promotion +
   affiliate-commission policy from the in-transaction snapshot, reserves inventory, persists order + items
   + immutable snapshots.
3. Payments: backend creates the Payment Intent (frontend cannot); provider gated per tenant; Stripe/Paymob
   certified here.
4. Orders: generic Order created with idempotency binding only after claim-token ownership proven.
5. Inventory: reservation consumed on fulfillment through current lineage.
6. Accounting: ledger posts on collected payment.
7. Fulfillment -> Notifications -> Projections (Mongo/ES/Redis), all converging from SQL.
Authority note: client totals/discounts are stale-client assertions; server recomputes. See 05-orders.md
(Storefront checkout) and 04-payments.md (Charge Registration).

## CommerceHub Marketplace Purchase
1. Customer discovers across vendors on CommerceHub; cart groups items by StoreId.
2. Marketplace checkout recomputes per-store listing/product/variant/category/vendor/payment/shipping/promo/
   price/stock from SQL; creates marketplace order + per-store sub-orders (SQL-unique per MarketplaceOrderId,
   StoreId); reserves inventory with EXACT per-tenant ownership/quantity (not "at least enough").
3. Currency is EGP-only (platform authority); a non-EGP group is an anomaly, never reconciled.
4. Payments: provider session fenced before external boundary; unmatched webhooks persisted as unresolved
   SQL evidence; suspense/scope-less captured money blocks outward money. NOT covered by reference-provider cert.
5. Fulfillment: per sub-order via the marketplace sub-order service; multi-vendor partial cancellation does
   not cancel unrelated stores.
6. Accounting: vendor earnings/payouts through the shared ledger; EGP reconciliation only.
7. Refund/Return: refund-owned; return-dependent refunds gated on return execution trigger.
Authority note: marketplace order model is separate from generic Order; exact reservation ownership and EGP
are first-class. See 05-orders.md (Marketplace) and 04-payments.md (CommerceHub exclusion, EGP reconciliation).

## Affiliate Checkout
1. A referred customer lands on the storefront via an affiliate link/attribution.
2. Checkout proceeds as a normal storefront checkout, BUT in the same transaction an immutable
   AffiliateAttributions row is stamped and the affiliate commission policy is row-locked and recomputed
   from the in-transaction snapshot.
3. The resulting order is a generic storefront Order (Source = Ecommerce) + attribution; channel identity is
   fixed and never leaks.
4. Inventory/Payment/Order/Accounting traverse the SAME shared domains as any storefront order.
5. Commission/balance/payout live in SQL; payout is outward money gated by the shared readiness rules; payout
   transfer idempotency is global per payout reference (one real rail transfer cannot fund duplicate local
   withdrawals).
Authority note: affiliate economics are SQL-authoritative and distinct from inventory reservations; the
affiliate portal is a separate identity surface. See 05-orders.md (Affiliate-origin order) and 03-inventory.md
(reservation conflation warning).

## Vendor Governance / Marketplace Visibility
1. A POS tenant actor operates merchant/vendor surfaces, but vendor eligibility is recomputed from current
   SQL governance and tenant/vendor lineage.
2. Marketplace governance may suppress visibility, suspend/ban vendors, trigger scoped reindex/repair, and
   enforce COD-debt order-acceptance limits.
3. Public surfaces (search, detail, homepage, recommendations, autocomplete, checkout eligibility) re-apply
   current SQL product/listing/category/vendor-governance/pricing/stock authority before visibility.
4. Governance does NOT approve merchant prices and does NOT mutate order/payment/fulfillment truth.
Authority note: vendor visibility is platform/marketplace governance authority, not a search projection. See
06-security-identity.md (Marketplace governance authority, COD hard-limit).

## Cancellation Before Fulfillment
1. Cancellation intent enters the order lifecycle boundary, not a fulfillment DTO or frontend state machine.
2. Recognized money or unresolved provider evidence blocks unsafe direct cancellation; payment cancellation /
   expiration must be safe before reservations are restored.
3. Marketplace partial cancellation is per sub-order and must not cancel unrelated stores.
Authority note: cancellation coordinates order, payment, inventory, and projection convergence. See
05-orders.md (Cancellation Rules) and 04-payments.md (Payment Lifecycle).

## Return / Refund / Post-Settlement Refund
1. Customer/operator refund intent enters the refund/return lifecycle, not order or shipment authority.
2. Return-dependent refunds wait until the return execution trigger milestone is reached; physical receipt
   evidence cannot forge refund success.
3. Refund execution verifies collected money, current order/payment/refund authority, readiness gates, and
   cumulative refundable balance under SQL locks.
4. Manual/provider refund handoff is external-money evidence until local financial recognition completes;
   uncertain outcome remains Processing/ReconciliationRequired, not false Failed.
5. Post-settlement refunds prove live `PaymentSettlementOrders` lineage before settlement/bank-account posting.
Authority note: returns gate refund execution; Payment/refund authority decides money. See 04-payments.md
(Refund Rules) and 05-orders.md (Return Rules, Refund Integration).

## Vendor / Affiliate Payout Readiness
1. Vendor earnings and affiliate balances are SQL-authoritative financial facts, but payout execution is
   outward money and must pass readiness gates.
2. Unresolved external-money ambiguity, critical accounting backlog, refund overlap, or reconciliation-required
   payout state blocks outward movement until cleared by the owning lifecycle/reconciliation path.
3. One real-world transfer reference cannot fund duplicate local withdrawal recognition; payout allocation
   idempotency is global per payout reference.
Authority note: dashboards and payout requests are evidence/workflow surfaces, not cash authority. See
04-payments.md (Accounting Integration, Refund Rules) and 06-security-identity.md (Business Invariants).

## Reconciliation / Unmatched External Money
1. Provider/carrier/webhook evidence arrives as evidence, sometimes before a matching local row or after local
   terminal state.
2. The webhook trust pipeline authenticates evidence, resolves scope from signed payload + SQL records, and
   persists unresolved external-money evidence when it cannot be matched.
3. Reconciliation workers reload and lock current SQL truth before clearing suspense, releasing holds, moving
   payout state, or routing to manual review.
4. Scope-less unresolved external-money ambiguity blocks outward money until scope is materialized or cleared.
Authority note: reconciliation detects and routes uncertainty; it does not create money. See 04-payments.md
(Webhook/Reconciliation/Accounting) and 06-security-identity.md (Webhook Security, Fail Closed).

----------------------------------------------------------------
# Cross-System Invariants

These hold across every system and every journey. They are the things a platform-level QA plan must
always defend (mechanics and the owning guide are cited):

1. One system of record. PostgreSQL is the authoritative write model; SQL commit is the durability
   boundary. Mongo/Redis/ES/RabbitMQ/provider/frontend are derivative. If two systems disagree, SQL wins.
   (02-architecture.md, section 0.)
2. No shadow authority. Every protected state change goes through its lifecycle owner (payment, order,
   fulfillment, inventory, accounting, identity, affiliate). A second write path is an architectural
   violation. (02, R2/R5/R10; 06 Authority Boundaries.)
3. Recompute, never trust. Identity, price, discount, tax, shipping, payment-method eligibility, and
   payment split are recomputed server-side from current SQL. Client values are stale-client assertions.
   (02 R1; 05 Checkout Rules.)
4. Provider/webhook = evidence, not authority. External success/failure is admitted by the lifecycle owner
   and reconciled into SQL; it never overrides SQL; unknown events are not made executable. (04 Webhook/
   Evidence Rules; 06 Webhook Security.)
5. Fail closed under uncertainty. Reconciliation/suspense/quarantine/processing/unknown-outcome are
   intentional safe states. Integrity is preferred over availability when external-money or backlog scope
   is uncertain. (02 R9; 04; 06 Fail Closed.)
6. Idempotent and replay-safe. No global total order; ordering is per-aggregate only; consumers collapse
   duplicates via fences/ProcessedEvents. Replay/recovery/rebuild revalidate current SQL before mutating.
   (02 R6; 05 Event Publishing; 06 Replay authority.)
7. Tenant isolation is necessary-not-sufficient. A TenantId column is not enough; scope requires trusted
   resolution + current SQL active tenant lineage + tenant co-ownership of referenced records. Cross-tenant
   leakage is CRITICAL. (02 R7; 06 Multi-Tenant Rules.)
8. Currency sovereignty per surface. CommerceHub marketplace is EGP-only platform authority; non-EGP is an
   anomaly, never reconciled. Storefront/tenant currency is a separate, non-overriding surface. (04/05/06
   Currency authority.)
9. Outward money is gated. Refunds/payouts/gateway refunds run the readiness check inside the same serializable
   transaction as the local authority mutation; scope-less unresolved captured-payment rows are globally
   blocking. (04 Accounting Integration; 06 Business Invariants.)
10. Projections converge to SQL. Derivative surfaces reload SQL authority and tombstone/delete when SQL no
    longer proves visibility; they may lag (accepted) but must never resurrect stale visibility. (02 R6; 03
    Projection Rules; 05 Projection Rules.)

----------------------------------------------------------------
# Never Assume

Platform-level assumptions that lead QA astray:

- Do NOT assume the UI that originated a request owns its truth. POS Admin, storefront, marketplace, and
  affiliate all sit in front of the SAME SQL authority.
- Do NOT assume a generic Order rule applies to a CommerceHub sub-order, or vice versa. They are separate
  authority models with different transition graphs. (05 Order Lifecycle.)
- Do NOT assume CommerceHub payment is as assured as storefront payment. It is explicitly excluded from the
  Stripe/Paymob reference-provider certification. (04 Conflicts.)
- Do NOT assume "affiliate reservation" means inventory reservation. They are different concepts owned by
  different domains. (03 reservation conflation warning.)
- Do NOT assume a read model (Mongo/Redis/ES), a cache, a webhook payload, a frontend total, or a provider
  callback reflects current truth. They lag or are evidence by design.
- Do NOT assume a missing/duplicate/stale projection is a bug. Read-model lag and projection rebuild are
  accepted tradeoffs unless the projection path violates a SQL-recomposition rule.
- Do NOT assume a provider "success" webhook means local payment is paid, or a frontend "succeeded" means
  money is collected. Both are evidence until lifecycle admission. (04 Never Assume.)
- Do NOT assume reconciliation/suspense/quarantine states are limbo to be cleared. They are intentional safe
  states; forcing false terminal states is a defect. (02 R9.)
- Do NOT assume X-Tenant-Id, a JWT claim, or a client tenant id establishes tenant scope. Scope is proven by
  trusted resolution + current SQL lineage. (06 Tenant Resolution.)
- Do NOT assume a non-EGP CommerceHub group is legitimate because a tenant configured that currency. Marketplace
  reconciliation currency is EGP only. (04/05/06 Currency authority.)
- Do NOT assume a marketplace reservation "is enough" if quantity >= order quantity. Exact ownership and exact
  quantity per current order item are required. (03/05 Marketplace reservation.)
- Do NOT assume the frozen Payment Runtime is live. It is certified but disabled; live flows use SQL-backed
  lifecycle services. (04 Conflicts.)

----------------------------------------------------------------
# QA Mental Model

Before testing any feature, locate it in the platform:

1. Which customer surface originated it? (POS Admin / storefront / CommerceHub / affiliate / WhatsApp)
   The origin changes the UI and the assurance level, not the authority.

2. Which shared domains does it touch? (orders, inventory, payments, identity, accounting, notifications,
   search, projections) Trace the full chain — a "checkout bug" is usually an orders + inventory + payments +
   accounting story.

3. Who is the authority? Find the single lifecycle owner / SQL table that decides truth for each domain. If
   your test asserts on a Mongo/Redis/ES value, remember it can be stale by design — assert on SQL or on a
   SQL-repaired read.

4. Is the tenant scope proven, not claimed? Verify trusted resolution plus current SQL active tenant lineage,
   and tenant co-ownership of every referenced record. Cross-tenant leakage is CRITICAL.

5. If a provider callback, a replayed event, or a stale projection arrives late or contradictory, does the
   system recompose current SQL truth and fail closed — instead of resurrecting old visibility or faking a
   terminal state? If it does, that is correct behavior, not a bug.

6. Is external-money truth uncertain? Then expect reconciliation/suspense/quarantine and a fail-closed
   readiness gate on outward money — never a forced terminal success.

7. Does the change go through the correct lifecycle boundary / outbox, or does it sneak around it? A direct
   write that "works" is still an architectural violation.

The one-sentence version: PostgreSQL decides; lifecycle owners mutate; everything else converges. When you
test, prove the system fails closed when truth is uncertain, and prove no replay/duplicate/projection can
create, resurrect, or double-count money or inventory.

----------------------------------------------------------------
# Source Documents

Primary (authority — read before any platform work):
- .ai-context/PROJECT_CONTEXT.md            (subsystems, ownership boundaries, datastores, tech)
- .ai-context/ARCHITECTURE_TRUTH.md        (authoritative data sources, lifecycle, global rules)
- .ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md (cross-system hardening + recovery rules)
- .ai-context/KNOWN_RISKS.md               (confirmed/mitigated/accepted risk vocabulary + entries)
- .ai-context/CODEX_RULES.md               (mandatory rules + workflow + risk vocabulary)

Per-domain QA guides (this series):
- .ai/hermes/02-architecture.md            (authority model, QA rules, risk vocabulary)
- .ai/hermes/03-inventory.md               (stock, warehouses, reservations, lineage)
- .ai/hermes/04-payments.md                (payment lifecycle, providers, webhooks, refunds, reconciliation)
- .ai/hermes/05-orders.md                  (order/checkout/fulfillment/cancellation/return/refund/projection)
- .ai/hermes/06-security-identity.md       (identity, tenant isolation, authority boundaries, fail-closed)

Deep dives (implementation/evidence, referenced by the guides above):
- docs/architecture/*                      (per-subsystem certification/freeze findings)

Conflict note: this overview is deliberately relationship-level and points at the owning guide for mechanics.
Where a durable claim here appears to conflict with a per-domain guide or a .ai-context file, the per-domain
guide and .ai-context win; do not "fix" this overview by inventing mechanics — extend the owning guide.
================================================================
