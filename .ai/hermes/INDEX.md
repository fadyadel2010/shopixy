# Shopixy AI QA Knowledge Base

The Shopixy AI QA Knowledge Base is the official navigation hub for the durable QA documentation under `.ai/hermes/`.

It is written for Senior QA Engineers, AI agents, and new contributors who need a reliable starting point before testing or changing the Shopixy / PointOfSale platform. It does not replace the guides. It explains what each guide owns, how the guides relate to each other, and which source wins when documents disagree.

---

# How to use this library

Use this library as a map, not as another onboarding guide.

- Read the Core Guides in order before relying on any single domain rule.
- Use domain guides as the owners of their domain rules.
- Use the Platform Overview to connect domains into complete customer and operational journeys.
- Use QA Philosophy to decide how to reason when behavior, evidence, authority, and user experience disagree.
- Follow cross-references instead of copying rules between guides.
- If documents disagree, apply Authority Precedence. Do not guess, merge conflicting claims, or silently pick the most convenient rule.

The standard flow is:

1. Start here.
2. Read the Core Guides in order.
3. Read the owning domain guide for the feature under test.
4. Follow related-guide references when the feature crosses domains.
5. Resolve conflicts by authority precedence.

---

# Core Guides

## 02-architecture.md

File: `.ai/hermes/02-architecture.md`

Purpose:
Establish the foundation of Shopixy's authority model: what is truth, what is derivative, which boundaries may not be bypassed, and how QA should treat system-wide invariants.

What it covers:
- PostgreSQL as the authoritative write model.
- Derivative systems: MongoDB, Redis, Elasticsearch, RabbitMQ, projections, caches, and frontend state.
- System boundaries for payments, orders, fulfillment, events, tenant resolution, and health/metrics surfaces.
- Repo-wide QA-critical rules and risk vocabulary.
- The baseline interpretation of stale reads, async projection lag, lifecycle ownership, and fail-closed behavior.

What it intentionally does NOT cover:
- Full domain mechanics for inventory, payments, orders, or identity.
- Whole-platform journey mapping across user surfaces.
- The general QA reasoning philosophy beyond the architecture-specific rules.
- Temporary implementation status or code-level walkthroughs.

When a QA engineer should read it:
- First, before any other guide.
- Before judging whether a behavior is an authority violation or accepted eventual consistency.
- Before testing features that touch multiple stores, projections, queues, or lifecycle services.
- Whenever a source, cache, projection, frontend response, or provider callback appears to disagree with SQL authority.

Related guides:
- `03-inventory.md`
- `04-payments.md`
- `05-orders.md`
- `06-security-identity.md`
- `07-platform-overview.md`
- `08-qa-philosophy.md`

## 03-inventory.md

File: `.ai/hermes/03-inventory.md`

Purpose:
Define the durable QA rules for inventory authority, stock, warehouses, reservations, movements, inventory lineage, and projection convergence.

What it covers:
- SQL authority for inventory stock, warehouses, reservations, and stock movements.
- Inventory mutation ownership and lineage checks.
- Reservation creation, release, expiry, cleanup, and replay safety.
- Quantity invariants, movement history, and projection behavior.
- Tenant co-ownership requirements for inventory-bearing records.
- Distinction between inventory reservations and financial or payout reservations.

What it intentionally does NOT cover:
- Payment lifecycle or external provider behavior.
- Order lifecycle rules except where orders reserve or consume inventory.
- Identity/session authority except where tenant scope affects inventory ownership.
- General platform surface mapping.

When a QA engineer should read it:
- When testing stock, warehouses, transfers, adjustments, reservations, returns, inventory visibility, or inventory projections.
- When checkout, order, or marketplace behavior depends on stock availability.
- When investigating oversell, negative stock, stale stock visibility, duplicate reservations, or stock resurrection.

Related guides:
- `02-architecture.md`
- `05-orders.md`
- `07-platform-overview.md`
- `08-qa-philosophy.md`

## 04-payments.md

File: `.ai/hermes/04-payments.md`

Purpose:
Define the durable QA rules for payment authority, provider evidence, lifecycle state, refunds, settlement, reconciliation, accounting integration, and financial fail-closed behavior.

What it covers:
- Payment authority and the payment lifecycle owner.
- Provider responsibilities and the evidence-only role of external callbacks, redirects, SDK outcomes, and webhooks.
- Payment lifecycle states, forbidden transitions, retries, recoverability, and finality.
- Refunds, captures, settlement, duplicate prevention, idempotency, reconciliation, and accounting effects.
- Provider certification boundaries and surface-specific confidence levels.
- Financial uncertainty states such as quarantine, suspense, and reconciliation-required behavior.

What it intentionally does NOT cover:
- Full order lifecycle mechanics except where payment state gates order or money movement.
- Inventory reservation rules except where payment interacts with checkout or fulfillment.
- Identity/session rules except where tenant/provider authority is required.
- Provider implementation internals as temporary code detail.

When a QA engineer should read it:
- When testing payment admission, provider flows, webhooks, refunds, captures, settlements, COD collection, manual payments, reconciliation, payout readiness, or ledger-adjacent behavior.
- When any UI or provider reports success but SQL payment authority is uncertain.
- When a feature can create, recognize, reverse, hold, or move money.

Related guides:
- `02-architecture.md`
- `05-orders.md`
- `06-security-identity.md`
- `07-platform-overview.md`
- `08-qa-philosophy.md`

## 05-orders.md

File: `.ai/hermes/05-orders.md`

Purpose:
Define the durable QA rules for order authority across Storefront, POS/Admin, CommerceHub marketplace, and affiliate-origin orders.

What it covers:
- Generic order authority and CommerceHub marketplace order authority as distinct models.
- Checkout, order lifecycle, fulfillment, cancellation, return, refund, projection, and recovery behavior.
- Channel identity, order lineage, tenant scope, idempotency, and duplicate/replay safety.
- Integration points with inventory reservations, payment lifecycle, fulfillment ownership, refunds, and accounting.
- Marketplace sub-order behavior, split-order concerns, and channel-specific authority boundaries.

What it intentionally does NOT cover:
- Full payment provider rules beyond order/payment integration.
- Full inventory internals beyond reservation and consumption effects.
- Full identity/session model beyond order authorization and tenant scope.
- General QA philosophy except where order correctness depends on it.

When a QA engineer should read it:
- When testing checkout, order creation, order state transitions, fulfillment, cancellation, returns, refunds, marketplace sub-orders, affiliate-attributed orders, or order projections.
- When determining whether a behavior belongs to the generic order model or CommerceHub marketplace model.
- When order behavior crosses payment, inventory, fulfillment, or accounting boundaries.

Related guides:
- `02-architecture.md`
- `03-inventory.md`
- `04-payments.md`
- `06-security-identity.md`
- `07-platform-overview.md`
- `08-qa-philosophy.md`

## 06-security-identity.md

File: `.ai/hermes/06-security-identity.md`

Purpose:
Define the durable QA rules for identity, authentication, authorization, tenant isolation, authority recomputation, dangerous operations, and security-sensitive business invariants.

What it covers:
- Identity surfaces for POS/Admin actors, Super Admins, CommerceHub customers, vendors, and affiliate portal users.
- Authentication and refresh authority rules.
- Authorization boundaries, platform authority, tenant authority, and governance validation.
- Tenant resolution and tenant co-ownership expectations.
- Fail-closed security behavior when identity, tenant scope, claims, tokens, sessions, or authority evidence are stale or invalid.
- Dangerous operations and security-relevant business invariants.

What it intentionally does NOT cover:
- Full domain lifecycle mechanics for inventory, payments, or orders.
- UI-only access control behavior except as non-authoritative evidence.
- Generic security checklist content that is not specific to Shopixy authority.
- Temporary implementation details or test-name trivia.

When a QA engineer should read it:
- When testing login, refresh, logout, session revocation, role access, Super Admin authority, tenant isolation, cross-tenant references, customer identity, vendor scope, affiliate identity, or dangerous operations.
- Whenever a feature trusts claims, headers, route ids, cached identity, frontend guards, or projected tenant state.
- Before classifying any cross-tenant or authority-bypass behavior.

Related guides:
- `02-architecture.md`
- `04-payments.md`
- `05-orders.md`
- `07-platform-overview.md`
- `08-qa-philosophy.md`

## 07-platform-overview.md

File: `.ai/hermes/07-platform-overview.md`

Purpose:
Provide the whole-platform map that connects surfaces, domains, shared services, and customer journeys without replacing the domain guides.

What it covers:
- Major Shopixy surfaces: POS Admin, Storefront, CommerceHub, Affiliate, Super Admin, shared platform services, and WhatsApp transport.
- Shared domains and how they interact across journeys.
- Cross-system flows such as checkout, payment, inventory, fulfillment, accounting, projection, notification, and recovery.
- How a QA engineer should locate a feature inside the platform before following the owning domain guide.

What it intentionally does NOT cover:
- Detailed domain rules that belong to inventory, payments, orders, or security.
- Source-code implementation walkthroughs.
- A replacement reading path for the Core Guides.
- Temporary platform status or in-progress implementation detail.

When a QA engineer should read it:
- After the domain foundation guides, when moving from isolated domain understanding to whole-platform testing.
- Before testing a feature that crosses user surfaces or multiple backend domains.
- When deciding which guide owns a behavior that appears to span several systems.

Related guides:
- `02-architecture.md`
- `03-inventory.md`
- `04-payments.md`
- `05-orders.md`
- `06-security-identity.md`
- `08-qa-philosophy.md`

## 08-qa-philosophy.md

File: `.ai/hermes/08-qa-philosophy.md`

Purpose:
Teach the durable reasoning model QA should apply when testing Shopixy under uncertainty.

What it covers:
- Authority before behavior.
- Evidence versus truth.
- Fail-closed reasoning.
- Testing uncertainty, replay, duplicate delivery, stale projections, and contradictory evidence.
- Severity thinking, regression thinking, bug classification, and common QA mistakes.
- How to distinguish accepted tradeoffs from real defects.

What it intentionally does NOT cover:
- Domain mechanics for architecture, inventory, payments, orders, security, or platform journeys.
- Checklist-style feature coverage.
- Implementation details, code trivia, or temporary project status.
- Repetition of domain-specific rules already owned by earlier guides.

When a QA engineer should read it:
- After the Core domain guides and Platform Overview.
- Before writing test strategy, classifying defects, or reviewing behavior where the correct result depends on authority, evidence, uncertainty, and tradeoffs.
- Whenever an AI agent is tempted to infer behavior from UI output, provider output, projection state, or memory instead of authoritative documentation.

Related guides:
- `02-architecture.md`
- `03-inventory.md`
- `04-payments.md`
- `05-orders.md`
- `06-security-identity.md`
- `07-platform-overview.md`

---

# Reading Order

Read the Core Guides in this order:

1. `02-architecture.md`
2. `03-inventory.md`
3. `04-payments.md`
4. `05-orders.md`
5. `06-security-identity.md`
6. `07-platform-overview.md`
7. `08-qa-philosophy.md`

The order matters because each guide assumes the mental model established before it:

- Architecture defines authority, derivative state, lifecycle ownership, and fail-closed expectations.
- Inventory introduces the first concrete domain where SQL lineage, tenant co-ownership, replay, projection lag, and recovery must be tested carefully.
- Payments adds external-money uncertainty, provider evidence, refunds, reconciliation, and accounting pressure.
- Orders connects customer action to inventory, payment, fulfillment, refund, channel identity, and projections.
- Security/Identity defines who may act, how tenant scope is proven, and why claims, headers, sessions, and frontend guards are evidence only.
- Platform Overview connects all domains into complete Shopixy journeys and explains which guide owns which part of a cross-system behavior.
- QA Philosophy turns the prior guides into a durable reasoning model for defect classification, severity, regression design, and AI-agent behavior.

Skipping ahead increases the risk of treating a derivative surface as truth, applying one domain's rule to another domain, or reporting an accepted tradeoff as a defect.

---

# Authority Precedence

When documents disagree, use this official precedence:

1. `.ai-context/*`
2. Domain Guide
3. `docs/architecture/*`
4. Source code
5. README / comments

Interpretation:

- `.ai-context/*` is the highest authority for durable platform truth.
- The owning Domain Guide is the maintained QA interpretation for its domain.
- `docs/architecture/*` provides architecture and certification evidence, but must be interpreted through `.ai-context/*` and the owning guide.
- Source code is evidence of current behavior, not automatically the intended rule.
- README files and comments are lowest authority and may be stale.

If a conflict remains unresolved after applying precedence, document the conflict and escalate it. Do not invent architecture, blend incompatible rules, or choose based on what is easiest to test.

---

# Knowledge Map

```text
Shopixy Platform
│
├── Architecture
│   ├── SQL authority
│   ├── Derivative systems
│   ├── Lifecycle ownership
│   └── Authority precedence
│
├── Security & Identity
│   ├── Actor identity
│   ├── Authentication / authorization
│   ├── Tenant isolation
│   └── Dangerous operations
│
├── Orders
│   ├── Checkout
│   ├── Fulfillment
│   ├── Returns / refunds
│   ├── Payments
│   │   ├── Provider evidence
│   │   ├── Reconciliation
│   │   └── Accounting effects
│   └── Inventory
│       ├── Stock
│       ├── Reservations
│       └── Movements
│
├── Platform Overview
│   ├── POS Admin
│   ├── Storefront
│   ├── CommerceHub
│   ├── Affiliate
│   ├── Super Admin
│   ├── WhatsApp transport
│   └── Shared services
│
└── QA Philosophy
    ├── Authority before behavior
    ├── Evidence is not truth
    ├── Fail closed
    ├── Test uncertainty
    └── Classify defects by risk
```

---

# Domain Guides (Future)

This section is reserved for future domain-specific guides. Do not treat these placeholders as existing documentation.

Planned or possible guides:

- POS Admin
- Storefront
- CommerceHub
- Affiliate
- Accounting
- CMS
- Notifications
- Search
- Shipping
- WhatsApp
- Analytics
- Treasury / Payouts
- Refunds / Returns
- Catalog / Products
- Pricing / Promotions
- Customer Accounts
- Vendor Operations
- Super Admin Operations
- Reporting
- Reconciliation
- Background Jobs / Outbox
- Observability / Audit

Create these only when they are explicitly requested and sourced from the authoritative corpus.

---

# Documentation Principles

The Knowledge Base follows these principles:

- Durable rules only: write rules that should remain true across implementations, refactors, and UI changes.
- No temporary implementation details: avoid transient code paths, migration trivia, in-flight status, and one-off bug history.
- Cross-reference instead of duplication: link to the owning guide instead of repeating domain rules in multiple places.
- Domain ownership: each guide owns its domain's rules; other guides should point to it rather than redefining it.
- Authority first: every claim should be traceable to the highest available authority, and conflicts must be resolved by precedence.
- Evidence is labeled as evidence: code, projections, provider payloads, comments, and README text do not become truth just because they are visible.
- No shadow architecture: documentation must not create new boundaries, flows, or ownership models not present in the authoritative sources.

---

# How AI Agents Should Use This Library

AI agents should treat this Knowledge Base as a navigation and authority system.

Required behavior:

- Read the minimum required guides for the task, starting with this index and the owning domain guide.
- Follow cross-references when a feature touches multiple domains.
- Prefer authoritative documents over memory, comments, README text, or guessed architecture.
- Never invent architecture, lifecycle owners, provider behavior, tenant rules, or domain boundaries.
- Do not infer truth from UI output, frontend validation, cache state, projection documents, provider callbacks, queue events, or source-code shape alone.
- Apply Authority Precedence when sources disagree.
- Escalate unresolved conflicts instead of guessing.
- When reviewing behavior, identify the authority owner before judging whether the behavior is correct.
- When writing future guides, add durable rules and cross-references; do not duplicate guide content into this index.

For AI agents, the safest default question is:

```text
Which document owns this rule, and what is the highest authority source behind it?
```

---

# Version

Core Version: v1.0

Status: Core Complete; Systems In Progress
