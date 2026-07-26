# TASK_ROUTING.md

The primary navigation and reasoning guide for Hermes inside the Shopixy Knowledge Base.

This file is NOT architecture documentation. It is NOT a system guide. It is a routing map. Its job is to help Hermes decide, as fast as possible, which documents to open, which systems are involved, and which domains to leave alone.

It does not replace any README, Core Guide, Index, or certification. It reduces browsing cost by telling you what to read first and what to skip.

---

# Legend (abbreviations used below)

All paths are relative to `.ai/hermes/`.

Core Guides:
- ARCH = `02-architecture.md`          (SQL authority, derivative state, lifecycle ownership, fail-closed)
- INV  = `03-inventory.md`             (stock, reservations, warehouses, movements)
- PAY  = `04-payments.md`              (payment lifecycle, provider evidence, settlement, reconciliation)
- ORD  = `05-orders.md`                (generic Order vs CommerceHub marketplace order/sub-order)
- SEC  = `06-security-identity.md`     (identity, auth, tenant isolation, dangerous operations)
- OVW  = `07-platform-overview.md`     (surface map, cross-system journeys)
- QA   = `08-qa-philosophy.md`         (authority before behavior, evidence != truth, fail closed)

System Guides:
- S-ST = `systems/storefront/README.md`
- S-CH = `systems/commercehub/README.md`
- S-AF = `systems/affiliate/README.md`
- S-SV = `systems/service-marketplace/README.md`
- S-SA = `systems/super-admin/README.md`
- S-PA = `systems/pos-admin/README.md`

Certifications (relationship evidence only):
- C-ST = `certification/01-pos-admin-storefront.md`
- C-CH = `certification/02-pos-admin-commercehub.md`
- C-AF = `certification/03-pos-admin-affiliate.md`
- C-SV = `certification/04-pos-admin-service-marketplace.md`
- C-SA = `certification/05-pos-admin-super-admin.md`
- C-X  = `CROSS_SYSTEM_CERTIFICATION.md`            (the certification standard, not a per-system doc)

Always start from `INDEX.md`. It owns the authoritative reading order and the authority-precedence chain.

---

# Purpose

This file is Hermes's first stop for any Knowledge Base task.

It is a navigation and reasoning aid. It tells Hermes:
- which documents to read,
- which systems are involved,
- which Core Guides are authoritative for the task,
- which System Guides to open,
- which certifications to consult (rarely),
- which domains are intentionally NOT related.

It exists to minimize blind browsing. It does not contain business rules. Every rule it points to lives in the documents it routes to.

---

# Browsing Strategy

Follow this order for every task. Do not skip ahead into a System Guide before identifying the task category.

1. Understand the task. What surface, actor, and lifecycle is involved?
2. Identify the domain. Use the Cross-System Decision Tree and the Task Routing Matrix.
3. Open the related System Guide(s) for the owning system(s).
4. Open the required Core Guides for the domain(s) the task touches.
5. Open related certifications ONLY if you must validate a cross-system relationship or investigate an architectural inconsistency.
6. Avoid unrelated systems. If a task is about Storefront, do not pre-load CommerceHub, Affiliate, or Super Admin unless the decision tree says so.

Rule of thumb: identify, then route, then read the minimum. Never open every README.

---

# Task Routing Matrix

Columns:
- Task Type
- Primary System (where the authority/owning logic lives)
- Secondary Systems (related, but not authoritative)
- Core Guides (authoritative domain rules)
- Related Certifications (open only to validate relationships)
- Do NOT Read Unless Necessary (common false starts)
- Expected Knowledge (the one-line authority fact to keep in mind)

| Task Type | Primary System | Secondary Systems | Core Guides | Related Certs | Do NOT Read Unless Necessary | Expected Knowledge |
|---|---|---|---|---|---|---|
| Storefront Bug | Storefront | POS Admin | ARCH, OVW | C-ST | CommerceHub, Affiliate, Service Marketplace, Super Admin | Storefront presents/intents; POS Admin configures; truth in Core. |
| Checkout | Storefront | Orders, Payments, Inventory, POS Admin | ORD, PAY, INV, SEC, ARCH | C-ST | CommerceHub (different order model), Affiliate | Backend recomputes totals; stale-client totals fail closed. |
| Orders | Orders (generic `Order`) | Storefront, POS Admin, Payments, Inventory | ORD, ARCH | C-ST | CommerceHub marketplace order model | Generic Order vs marketplace sub-order are distinct models. |
| Payments | Payments (`IPaymentLifecycleService`) | Storefront, POS Admin, CommerceHub | PAY, ARCH | C-ST | Affiliate (consumes only), Service Marketplace | Provider callbacks/webhooks are evidence; lifecycle owns truth. |
| Manual Payments | Payments | POS Admin, Storefront | PAY, ARCH, SEC | C-ST | CommerceHub marketplace paths | Manual-wallet/COD are payment evidence; backend admits finality. |
| Shipping | Orders / Fulfillment | Storefront, POS Admin | ORD, ARCH | C-ST | CommerceHub split-order (immutable) unless CH task | Shipment/delivery evidence feeds fulfillment; not order truth. |
| Coupons | Pricing/Promotions (backend) | Storefront, POS Admin, Payments, Orders | PAY, ORD, ARCH | C-ST | CommerceHub | Eligibility/stacking/amount backend-authoritative, not UI. |
| Promotions | Pricing/Promotions (backend) | Storefront, POS Admin, Payments | PAY, ORD, ARCH | C-ST | CommerceHub | Promotion truth recomputed server-side at checkout. |
| Smart Offers | Pricing (POS Admin input) | Storefront, Payments, Orders | ARCH, PAY, ORD | C-ST | CommerceHub | POS Admin configures; Storefront presents admitted offer. |
| Commerce AI | Storefront (AI surfaces) | POS Admin, Search, CMS | ARCH, OVW | C-ST | CommerceHub, Affiliate | AI outputs (recs/content) are derivative; SQL authority decides visibility/buyability. |
| Analytics | Analytics (derivative observation) | relevant domain | ARCH, OVW, QA | C-ST | dashboards as truth | Analytics observe; financial numerators derive from SQL. |
| Inventory | Inventory | Storefront, POS Admin, Orders, CommerceHub | INV, ARCH | C-ST, C-CH | Storefront "owns inventory" | SQL stock/reservation/warehouse lineage; Storefront presents only. |
| Products | Catalog/Products (POS Admin) | Storefront, CommerceHub, Search | ARCH, INV, ORD | C-ST, C-CH | Affiliate owns products | Publication/visibility is SQL lifecycle authority. |
| Pricing | Pricing (backend recompute) | Storefront, POS Admin, Payments | ARCH, PAY, ORD | C-ST | CommerceHub EGP rules unless marketplace | Backend recomputes totals; client totals are stale assertions. |
| Search | Search (Elasticsearch projection) | Storefront, CommerceHub, Products | ARCH, OVW | C-ST, C-CH | CommerceHub overlay unless CH task | Search converges from SQL; stale resurrect = authority bug. |
| SEO | CMS/SEO (POS Admin publishes) | Storefront | ARCH, OVW | C-ST | CommerceHub | SEO subordinate to current publication/store config. |
| CMS | CMS (POS Admin publishes) | Storefront | ARCH, OVW | C-ST | CommerceHub | Draft/preview/cache never becomes public truth. |
| CommerceHub Vendor | CommerceHub | POS Admin (Vendor Module), Super Admin | ORD, PAY, SEC, ARCH | C-CH, C-SA | Affiliate, Storefront | Vendor = POS tenant participating; Vendor Module is ops surface only. |
| Marketplace Orders | CommerceHub (marketplace order + sub-order) | POS Admin (vendor ops), Payments | ORD, ARCH | C-CH | Generic Order model | Marketplace order/sub-order distinct; channel fixed at creation. |
| Marketplace Finance | CommerceHub | Super Admin, Payments, Accounting | PAY, ARCH | C-CH, C-SA | POS Admin tenant treasury | EGP-only; CH payment paths excluded from Stripe/Paymob cert. |
| Marketplace Earnings | CommerceHub | Super Admin, Payments | PAY, ARCH | C-CH, C-SA | Affiliate earnings | Earning states Pending/Available/InPayout/PaidOut/Reversed; payouts gated. |
| Marketplace Payments | CommerceHub (orchestration) | Payments lifecycle, Super Admin | PAY, ARCH | C-CH, C-SA | Stripe/Paymob cert (excluded for CH) | Unresolved captured payments block outward money; lifecycle owns truth. |
| Marketplace Settlement | CommerceHub / Ledger | Super Admin, Payments, Accounting | PAY, ARCH, SEC | C-CH, C-SA | POS Admin treasury | Settlement-order lineage is SQL; header counts are metadata. |
| Marketplace Governance | Super Admin (CommerceHub governance) | CommerceHub | SEC, ARCH, OVW | C-SA | Storefront, POS Admin (outside CH), Service Marketplace | Super Admin governs CommerceHub only; not platform owner. |
| Super Admin | Super Admin | CommerceHub | SEC, ARCH, OVW | C-SA | POS Admin, Affiliate, Service Marketplace | Super Admin = CommerceHub governance; never whole platform. |
| Affiliate | Affiliate | Storefront, POS Admin, Payments | SEC, ORD, PAY, ARCH | C-AF | CommerceHub (unless certified), Service Marketplace | Affiliate drives Storefront traffic; conversion = Storefront generic Order. |
| Affiliate Attribution | Affiliate | Storefront checkout, Orders | ORD, SEC, ARCH | C-AF | CommerceHub | Durable attribution stamped at checkout; referral params not authority. |
| Affiliate Commission | Affiliate | Payments, Orders, Accounting | PAY, ORD, ARCH | C-AF | CommerceHub, Service Marketplace | Commission lifecycle candidate→earned→approved→reversed→paid; paid != external finality. |
| Affiliate Payout | Affiliate | Payments, POS Admin (review/proof), Finance | PAY, SEC, ARCH | C-AF | CommerceHub vendor payout (separate) | Payout readiness fails closed; dashboard balance not authority. |
| Affiliate Fraud | Affiliate + Security | Payments, Support, Super Admin | SEC, PAY, QA | C-AF | CommerceHub | URL/header/browser evidence never authority; block duplicate payout. |
| Service Marketplace | Service Marketplace (POS Admin) | POS Admin | SEC, ARCH, OVW, QA | C-SV | CommerceHub, Storefront, Affiliate, Super Admin (CH gov) | Internal POS-to-POS only; no external customers. |
| Service Requests | Service Marketplace | POS Admin | SEC, ARCH, QA | C-SV | Orders (request lifecycle != order lifecycle) | SQL service request authority; request→accept→chat→complete→confirm→rate. |
| Chat | Service Marketplace (request-bound) | POS Admin | SEC, ARCH | C-SV | CommerceHub, Affiliate | Chat membership from SQL request participants; transport state evidence only. |
| Ratings | Context-dependent (Service Marketplace internal OR CommerceHub marketplace) | POS Admin (if SM), Super Admin (if CH) | SEC, ARCH | C-SV or C-CH | Storefront product reviews (separate), Affiliate | Disambiguate system first; ratings derivative of completed request/order. |
| Reviews | Context-dependent (Storefront product / CommerceHub marketplace / Service Marketplace) | POS Admin, Super Admin | SEC, ARCH | C-ST, C-CH, C-SV | Affiliate | Confirm which surface; review visibility subordinate to owning authority. |
| Identity | Security/Identity | all systems (per-surface actors) | SEC, ARCH | C-X | any single System Guide as sole authority | Identity per-surface; actors isolated; JWT/claims evidence only. |
| Authentication | Security/Identity | Storefront, CommerceHub, Affiliate, POS Admin | SEC, ARCH | C-X | frontend route guard as authority | Login/session admitted by backend; stale/revoked fails closed. |
| Authorization | Security/Identity | all systems | SEC, ARCH | C-X | UI permission toggles as authority | Role claims evidence; SQL revalidated every request. |
| Notifications | Notifications (derivative output) | originating domain (Orders/Payments/CommerceHub) | OVW, ARCH, QA | C-ST | notifications as authority | Delivery cannot mutate commerce truth. |
| WhatsApp OTP | WhatsApp transport + Identity | Storefront (checkout verification), POS Admin | SEC, ARCH, OVW | C-ST | CommerceHub, Affiliate | OTP/session/delivery evidence only; never creates order/payment/tenant authority. |
| Tenant Settings | POS Admin (tenant settings) + Security/Identity | Storefront (presents) | SEC, ARCH | C-ST | Storefront as owner of settings | Two families: Storefront Settings (presentation) vs Tenant Settings (currency/locale); backend admits both. |
| Currency | Context-dependent (tenant currency = POS Admin; EGP = CommerceHub platform) | Storefront, CommerceHub, Payments | ARCH, PAY | C-ST, C-CH | one currency model across systems | Tenant currency tenant-authoritative; CommerceHub EGP platform-authoritative. |
| Localization | POS Admin (tenant locale) + CMS | Storefront | ARCH, OVW | C-ST | CommerceHub | Localization subordinate to tenant config + publication authority. |
| Accounting | Accounting / Ledger | Payments, POS Admin, CommerceHub, Affiliate | PAY, ARCH | C-CH, C-AF | report/dashboard views as ledger truth | Immutable double-entry ledger; everything posts via outbox; SQL ledger authoritative. |
| Recovery | Context-dependent (tenant-scoped POS Admin / global Super Admin) | owning lifecycle, POS Admin or Super Admin | ARCH, QA, OVW | C-X | invent authority | Recovery repairs from SQL; never finalizes money/inventory/identity by assumption. |

---

# Cross-System Decision Tree

Use the keywords in the task to choose the system. If a task mentions more than one keyword, follow every branch and combine the routed systems.

- Vendor
  → CommerceHub (+ POS Admin Vendor Module for tenant-side ops)
- Customer
  → Storefront (generic Order) — NOT CommerceHub unless "marketplace customer"
- Marketplace customer / multi-vendor
  → CommerceHub
- Affiliate / referral / commission / payout
  → Affiliate
- Service / internal B2B / provider / consumer tenant
  → Service Marketplace (POS Admin)
- Marketplace Governance / vendor suppression / COD debt / payout execution
  → Super Admin (CommerceHub governance only)
- Tenant Configuration / catalog / pricing input / treasury / back office
  → POS Admin
- Phone Verification / OTP
  → Storefront + POS Admin (WhatsApp transport, identity evidence)
- Manual Wallet Payment
  → Storefront + Payments + POS Admin
- COD Commission / COD debt
  → CommerceHub + Super Admin + POS Admin
- Marketplace Order / sub-order / split shipping
  → CommerceHub
- Settlement / outward money / payout readiness
  → CommerceHub + Payments + Super Admin
- Product / inventory configuration
  → POS Admin (then Storefront/CommerceHub present)
- External customer participating
  → Storefront or CommerceHub (never Service Marketplace)
- Review / rating visibility governance
  → CommerceHub (marketplace) — but product review → Storefront; internal service review → Service Marketplace
- Cross-tenant leak / tenant isolation / dangerous operation
  → Security/Identity (SEC) first, then the owning system

Hard boundaries the tree encodes:
- Service Marketplace is internal POS-to-POS. It has NO relationship with CommerceHub, Storefront customers, Affiliate, or CommerceHub Super Admin governance.
- Super Admin governs CommerceHub only. It does not own POS Admin, Storefront, or Affiliate outside CommerceHub.
- Affiliate attribution does not automatically apply to CommerceHub sub-orders.
- CommerceHub payment paths are excluded from the Stripe/Paymob reference certification that covers POS Admin + Storefront.

---

# Read Order

For each major system, read in this order. Open certifications only at the last step, and only when validating a relationship or investigating inconsistency.

## Storefront
```
INDEX.md
  ↓
systems/storefront/README.md
  ↓
systems/pos-admin/README.md        (POS Admin drives Storefront)
  ↓
05-orders.md
  ↓
04-payments.md
  ↓
03-inventory.md
  ↓
06-security-identity.md            (phone verification / accounts)
  ↓
certification/01-pos-admin-storefront.md   (only if validating relationship)
```

## CommerceHub
```
INDEX.md
  ↓
systems/commercehub/README.md
  ↓
systems/pos-admin/README.md        (Vendor Module)
  ↓
systems/super-admin/README.md      (governance)
  ↓
05-orders.md                       (marketplace order/sub-order)
  ↓
04-payments.md                     (EGP, cert exclusion, suspense)
  ↓
06-security-identity.md
  ↓
certification/02-pos-admin-commercehub.md   (only if validating relationship)
```

## Affiliate
```
INDEX.md
  ↓
systems/affiliate/README.md
  ↓
systems/storefront/README.md       (drives traffic)
  ↓
systems/pos-admin/README.md        (ops surface)
  ↓
05-orders.md
  ↓
04-payments.md
  ↓
06-security-identity.md
  ↓
certification/03-pos-admin-affiliate.md     (only if validating relationship)
```

## Service Marketplace
```
INDEX.md
  ↓
systems/service-marketplace/README.md
  ↓
systems/pos-admin/README.md
  ↓
06-security-identity.md
  ↓
02-architecture.md
  ↓
07-platform-overview.md
  ↓
08-qa-philosophy.md
  ↓
certification/04-pos-admin-service-marketplace.md   (only if validating relationship)
```

## Super Admin
```
INDEX.md
  ↓
systems/super-admin/README.md
  ↓
systems/commercehub/README.md
  ↓
05-orders.md
  ↓
04-payments.md
  ↓
06-security-identity.md
  ↓
certification/05-pos-admin-super-admin.md  (only if validating relationship)
```

## POS Admin
```
INDEX.md
  ↓
systems/pos-admin/README.md
  ↓
relevant Core Guides by domain:
    catalog/products     → 02-architecture.md, 03-inventory.md
    sales/orders         → 05-orders.md
    payments/treasury    → 04-payments.md
    identity/settings    → 06-security-identity.md
  ↓
embedded systems as needed:
    CommerceHub Vendor Module → systems/commercehub/README.md
    Affiliate operations      → systems/affiliate/README.md
    Service Marketplace       → systems/service-marketplace/README.md
  ↓
certification/01..05 as needed (only if validating relationship)
```

---

# Fast Exclusion Rules

Apply these before opening anything. They prevent the most common cross-system confusion.

- Task is about Storefront
  → Do NOT start with Affiliate.
- Task is about Service Marketplace
  → Do NOT start with CommerceHub.
- Task is about Marketplace Governance
  → Do NOT start with Storefront.
- Task is about Affiliate
  → Do NOT assume CommerceHub.
- Task is about Inventory
  → Do NOT assume Storefront owns inventory.
- Task is about Service Marketplace
  → Do NOT assume Storefront customers or any external customer participates.
- Task is about Super Admin
  → Do NOT assume it governs POS Admin, Storefront, or Affiliate outside CommerceHub.
- Task is about CommerceHub payments
  → Do NOT assume Stripe/Paymob certification covers it (explicitly excluded).
- Task is about Marketplace payment
  → Do NOT treat it as Storefront/POS payment-at-sale.
- Task is about Affiliate attribution
  → Do NOT copy Storefront attribution into CommerceHub sub-orders.
- Task is about COD debt / commission
  → Do NOT model it as a generic order; route to CommerceHub + Super Admin + POS Admin.
- Task is about Tenant Settings
  → Do NOT read Storefront as the owner of settings.
- Task is about Accounting
  → Do NOT treat reports/dashboards as ledger truth.

---

# Common Reasoning Patterns

Use these patterns whenever a task touches authority, evidence, or lifecycle.

```
Operational Surface
  ↓
Backend Authority
  ↓
Lifecycle Owner
  ↓
Projection (derivative)
  ↓
Presentation (derivative)
```

Key inequivalences (each arrow means "is not the same as"):

- Evidence ≠ Authority
  (provider callbacks, webhooks, OTP, analytics, Mongo/Redis, browser state)
- Presentation ≠ Authority
  (UI, dashboards, product cards, search results, admin panels)
- Governance ≠ Ownership
  (Super Admin governs CommerceHub; it does not own the platform)
- Visibility ≠ Inventory
  (a visible product is not proof of reservable stock)
- Settlement ≠ Payment
  (settlement lineage is SQL; payment finality is lifecycle-owned)
- Operational Intent ≠ Truth
  (POS Admin submits intent; Core lifecycle admits truth)
- Request Lifecycle ≠ Order Lifecycle
  (Service Marketplace request is not a generic `Order` or CommerceHub sub-order)
- Paid Evidence ≠ External Finality
  (Affiliate paid state is not bank/provider transfer finality)

---

# When To Read Certifications

Certification documents are normally unnecessary.

System Guides are the primary source of knowledge. They already describe how a system uses the Core domains. Read a certification only when:

- validating a cross-system relationship (e.g., does POS Admin really drive Storefront as described?),
- investigating an architectural inconsistency between two guides,
- reviewing or extending a cross-system boundary.

Which certification, when:
- C-ST (`01-pos-admin-storefront`) — Storefront ↔ POS Admin relationship.
- C-CH (`02-pos-admin-commercehub`) — CommerceHub ↔ POS Admin (Vendor Module) relationship.
- C-AF (`03-pos-admin-affiliate`) — Affiliate ↔ POS Admin / Storefront relationship.
- C-SV (`04-pos-admin-service-marketplace`) — Service Marketplace ↔ POS Admin relationship.
- C-SA (`05-pos-admin-super-admin`) — Super Admin ↔ CommerceHub / POS Admin governance relationship.
- C-X (`CROSS_SYSTEM_CERTIFICATION.md`) — the certification standard itself; read it to understand how consistency is judged, not for business rules.

If a task stays inside one system and one domain, you do not need any certification.

---

# Anti-Patterns

Browsing mistakes Hermes should avoid:

- Opening every README before identifying the task category.
- Assuming presentation equals authority (UI shows it, therefore it is true).
- Confusing Marketplace (CommerceHub) with Storefront.
- Confusing Vendor (POS tenant in CommerceHub) with Customer (Storefront/CommerceHub shopper).
- Confusing Super Admin (CommerceHub governance) with Platform Owner (whole Shopixy).
- Confusing Affiliate with CommerceHub.
- Treating notifications, WhatsApp delivery, or analytics as authority.
- Treating DTOs, cached balances, or dashboard totals as truth.
- Copying Storefront generic-order rules into CommerceHub sub-orders (or vice versa).
- Copying Storefront affiliate attribution into CommerceHub marketplace orders.
- Assuming Service Marketplace relates to CommerceHub, Storefront customers, or Affiliate.
- Assuming CommerceHub payment paths are covered by the Stripe/Paymob reference certification.
- Inferring truth from UI output, provider payloads, projection state, or memory instead of the authoritative document.
- Reading certifications as a first step instead of as relationship-validation evidence.

---

# Summary of sections in this file

1. Legend — abbreviation map for Core Guides, System Guides, and Certifications.
2. Purpose — what this file is and is not.
3. Browsing Strategy — the 6-step recommended order.
4. Task Routing Matrix — full table covering all required task types.
5. Cross-System Decision Tree — keyword → system branching, with hard boundaries.
6. Read Order — per-system reading sequences for the 6 major systems.
7. Fast Exclusion Rules — pre-browse exclusions to prevent confusion.
8. Common Reasoning Patterns — surface→authority→lifecycle→projection→presentation and key inequivalences.
9. When To Read Certifications — when (and which) certifications are needed.
10. Anti-Patterns — browsing mistakes to avoid.

This file is navigation-first and intentionally free of implementation details, code, and APIs. Every durable rule it points to lives in the routed documents.
