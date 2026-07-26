# Service Marketplace System Guide

Audience: Senior QA Engineers, AI Agents, and new contributors.

This is a SYSTEM guide.
It is not a Core guide, feature guide, implementation guide, API guide, or UI walkthrough.

The Core Guides own the durable domain rules:

- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/08-qa-philosophy.md`

This guide explains how Service Marketplace operates as a business capability inside POS Admin.

The most important rule:

Service Marketplace is an internal POS-to-POS collaboration platform.
It is not CommerceHub.
It is not Storefront.
It is not customer-facing.
It is not governed by CommerceHub Super Admin governance.
It exists only for POS Admin tenants.

---

# Purpose

Service Marketplace exists so one POS tenant can offer professional services to another POS tenant inside Shopixy.

A Senior QA Engineer or AI Agent should use this guide to understand:

- who participates in Service Marketplace;
- how services are published, discovered, requested, discussed, completed, and reviewed;
- which responsibilities Service Marketplace owns as a system capability;
- which Core domains remain authoritative for identity, tenant scope, payments, accounting, orders, inventory, search, projections, and QA reasoning;
- why Service Marketplace must never be confused with CommerceHub, Storefront, Affiliate, or CommerceHub Super Admin governance.

Service Marketplace is a POS Admin extension. It coordinates inter-tenant service collaboration. It does not replace POS operations and it does not become a Core authority.

The safest QA question is:

```text
Is this an internal POS-tenant-to-POS-tenant service collaboration, and did it remain inside the Service Marketplace authority boundary without becoming CommerceHub, Storefront, Affiliate, or Core authority?
```

---

# What is Service Marketplace?

Service Marketplace is an internal B2B service exchange.

It enables POS tenants to provide professional services to other POS tenants:

```text
POS Admin tenant acting as provider
↓
publishes a service
↓
internal Service Marketplace
↓
POS Admin tenant acting as consumer
↓
requests the service
```

It is an extension of POS Admin capabilities. The actors are authenticated POS Admin tenant actors. A tenant may act as a Service Provider for one service engagement and as a Service Consumer for another.

Service Marketplace is NOT:

- CommerceHub;
- Storefront;
- Affiliate;
- a customer-facing marketplace;
- a customer checkout surface;
- a CommerceHub vendor-governance surface;
- a replacement for POS Admin operations;
- an authority over payments, accounting, orders, inventory, or Storefront commerce.

There is no external customer in Service Marketplace. Storefront shoppers, CommerceHub customers, affiliate visitors, and anonymous users do not participate.

---

# Main Actors

## Service Provider (POS Admin)

A Service Provider is a POS tenant actor who offers a professional service to other POS tenants.

The provider owns provider-side operational intent:

- maintain a provider profile;
- publish service listings;
- receive service requests;
- accept or reject requests;
- communicate through the service-request chat;
- perform the agreed service;
- mark provider-side completion;
- receive rating/reputation outcomes after buyer-confirmed completion;
- coordinate payment evidence or payment steps through the owning payment/financial authority where payment is involved.

The Service Provider does not own payment finality, accounting truth, order truth, inventory truth, or platform governance. Provider actions are tenant-scoped POS Admin actions that must be admitted by current backend authority.

## Service Consumer (POS Admin)

A Service Consumer is a POS tenant actor who discovers and requests a professional service from another POS tenant.

The consumer owns buyer-side operational intent:

- browse internal service discovery;
- open a service detail;
- evaluate provider profile and reputation;
- submit a service request;
- chat with the provider after acceptance;
- receive the service;
- confirm final completion;
- coordinate payment where required through the owning payment/financial authority;
- submit a rating/review after final completion.

The Service Consumer is still a POS Admin actor, not a Storefront customer, CommerceHub customer, or affiliate visitor.

## Platform

The Platform provides the shared authority boundaries that keep Service Marketplace safe:

- current POS/Admin identity and tenant scope;
- SQL-backed service listing, request, chat, completion, rating, and reputation authority;
- derivative search/discovery convergence;
- projection repair and global recovery boundaries;
- payment, accounting, order, inventory, security, and QA rules through the Core Guides.

Platform governance may exist for global recovery, dangerous operations, or system-level integrity. That is separate platform authority. It must not be confused with CommerceHub Super Admin governance.

## Support (if applicable)

Support may assist with investigation, dispute triage, operational recovery, or evidence collection when a service interaction is unclear or blocked.

Support does not become a business actor in the service engagement. Support does not own service request lifecycle, payment truth, accounting truth, inventory truth, ratings, or provider reputation. Support actions must remain governed, audited, tenant-safe, and subordinate to the owning authorities.

---

# Relationship with POS Admin

Service Marketplace is part of POS Admin.

It extends tenant capabilities by allowing POS tenants to collaborate with one another professionally. It does not replace POS operations such as catalog, purchasing, inventory, POS sales, Storefront configuration, accounting, customer management, affiliate operations, or CommerceHub vendor participation.

Inside POS Admin, Service Marketplace is a separate internal collaboration capability:

```text
POS Admin
├── Tenant operations
│   ├── catalog
│   ├── inventory
│   ├── purchasing
│   ├── sales
│   ├── payments
│   ├── accounting
│   ├── Storefront configuration
│   ├── Affiliate operations
│   └── CommerceHub vendor participation
│
└── Service Marketplace
    ├── provider profile
    ├── service publication
    ├── service discovery
    ├── service requests
    ├── chat
    ├── completion
    └── ratings / trust
```

Service Marketplace exists only for inter-tenant service collaboration. It depends on POS Admin identity and tenant boundaries, but it does not turn one tenant into the owner of another tenant's POS operations.

A QA engineer should treat Service Marketplace as POS Admin-originated intent over its own service/request/chat/review authority, not as a general POS operating authority.

---

# Relationship with CommerceHub

There is no business relationship between Service Marketplace and CommerceHub.

CommerceHub is a customer-facing multi-vendor marketplace. Service Marketplace is an internal POS-to-POS collaboration platform.

They differ in every major business dimension:

| Dimension | CommerceHub | Service Marketplace |
|---|---|---|
| Actors | CommerceHub customers, POS tenants as vendors, platform governance | POS Admin tenants only: provider tenant and consumer tenant |
| Business model | Multi-vendor product marketplace | Internal professional-service collaboration between POS tenants |
| Customer participation | External marketplace customers participate | No external customers participate |
| Orders | CommerceHub marketplace orders and vendor sub-orders; see `05-orders.md` | Service requests / engagements; not CommerceHub orders |
| Payments | CommerceHub payment and finance rules; CommerceHub payment paths are separate and lower-assurance than POS Admin + Storefront reference-provider paths | Payment coordination only; payment truth remains with `04-payments.md` and financial authorities |
| Governance | CommerceHub marketplace governance and CommerceHub Super Admin system guide | Separate Service Marketplace/platform authority; not CommerceHub governance |
| Visibility | Customer-facing marketplace listing/discovery | Internal POS Admin service discovery |
| Currency/settlement | CommerceHub finance has its own rules, including EGP-only marketplace finance where applicable | Service Marketplace does not create CommerceHub finance or settlement authority |

Do not copy CommerceHub assumptions into Service Marketplace.

A Service Marketplace request is not a CommerceHub cart, checkout, order, sub-order, vendor earning, COD debt, settlement, payout, or customer support case.

---

# Relationship with Storefront

None.

Storefront customers never access Service Marketplace.

Storefront is the tenant-branded customer shopping surface. Service Marketplace is an internal POS Admin tenant collaboration surface.

A Storefront customer cannot browse Service Marketplace, request a service, join a service chat, complete a service request, rate a provider, or influence Service Marketplace trust. Storefront traffic, product cards, checkout sessions, carts, customer accounts, reviews, and order history do not become Service Marketplace authority.

---

# Relationship with Affiliate

None.

Affiliate traffic never enters Service Marketplace.

Affiliate is the traffic, attribution, commission, and payout-readiness system for referred Storefront conversions. Service Marketplace is not an affiliate referral surface, does not create affiliate attribution, and does not turn a service request into an affiliate-attributed order.

Affiliate links, campaign codes, visitor referrals, commission snapshots, affiliate payout requests, and affiliate dashboards do not participate in Service Marketplace unless a future authoritative document explicitly creates and certifies such a relationship. No such relationship is assumed by this guide.

---

# Relationship with Super Admin

CommerceHub Super Admin governance does not govern Service Marketplace.

Super Admin, as documented in the Super Admin System Guide, is the CommerceHub governance system. It governs CommerceHub marketplace participation, vendor visibility, marketplace risk, CommerceHub payout workflows, and CommerceHub emergency controls. That scope must not be copied into Service Marketplace.

Service Marketplace may still require separate platform governance for global recovery, dangerous operations, service-search repair, tenant-safe diagnostics, or trust-and-safety escalation. That authority is platform/system authority for Service Marketplace integrity, not CommerceHub Super Admin business governance.

Do not confuse:

```text
CommerceHub Super Admin governance
↓
CommerceHub vendors, listings, orders, marketplace finance, CommerceHub customers
```

with:

```text
Service Marketplace platform governance
↓
internal POS-to-POS service collaboration integrity and recovery
```

A CommerceHub governance decision does not automatically approve, reject, suppress, complete, rate, pay, or account for a Service Marketplace request.

---

# Primary Responsibilities

Service Marketplace owns the business coordination of internal service collaboration. It does not own the Core domains it depends on.

## Service Discovery

Why it exists: POS tenants need to find services offered by other POS tenants.

Service Marketplace responsibility: expose internal service listings to eligible POS Admin actors through current service, tenant, provider profile, and visibility authority.

Core guide references: `02-architecture.md`, `06-security-identity.md`, `07-platform-overview.md`, `08-qa-philosophy.md`.

Authority boundary: discovery candidates, search documents, cards, cached lists, and frontend state are derivative. Current SQL service listing, active tenant, active provider profile, and provider lifecycle/trust boundaries decide whether a service can be shown.

## Service Details

Why it exists: a consumer tenant needs enough information to decide whether to request a provider's internal professional service.

Service Marketplace responsibility: present the provider profile, service description, pricing presentation, ratings, and trust indicators for a currently visible service.

Core guide references: `02-architecture.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Authority boundary: Service Details is a presentation surface. It never becomes business authority. Provider display fields, descriptions, pricing labels, rating widgets, trust badges, route state, and cached detail data are evidence or presentation only. Current SQL service, tenant, provider profile, request, review, and trust authority remain the business truth. Service Details does not create a request, approve payment, complete a service, or publish anything to CommerceHub, Storefront, or Affiliate.

## Service Publication

Why it exists: a provider tenant must describe what it offers.

Service Marketplace responsibility: admit provider intent to publish a service listing with title, category, pricing presentation, media, and active visibility.

Core guide references: `02-architecture.md`, `06-security-identity.md`.

Authority boundary: publication is service-listing authority only. It does not publish a Storefront product, CommerceHub listing, affiliate campaign, inventory item, order, or payment method.

## Service Request

Why it exists: a consumer tenant needs a durable engagement request for a provider tenant.

Service Marketplace responsibility: create and maintain a service request / engagement lifecycle between the SQL buyer and provider participants.

Core guide references: `02-architecture.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Authority boundary: SQL service request authority owns request identity, buyer ownership, provider ownership, request snapshots, and lifecycle. Route ids, DTOs, cached capabilities, search results, and frontend state cannot create or reassign request authority.

## Service Communication

Why it exists: provider and consumer need a private, tenant-safe channel to coordinate service work.

Service Marketplace responsibility: provide chat after a valid service request reaches the allowed engagement state.

Core guide references: `02-architecture.md`, `06-security-identity.md`.

Authority boundary: chat membership derives from the SQL service request buyer/provider participants. SignalR, browser state, chat groups, cached messages, attachment URLs, and frontend state are transport/presentation evidence only.

## Provider Profile

Why it exists: a provider tenant needs a profile that other POS tenants can evaluate.

Service Marketplace responsibility: present provider identity, description, trust signals, and service ownership for internal service collaboration.

Core guide references: `06-security-identity.md`, `07-platform-overview.md`, `08-qa-philosophy.md`.

Authority boundary: provider profile authority is Service Marketplace profile authority. It is not CommerceHub vendor governance, Storefront merchant identity, Affiliate identity, or accounting authority.

## Ratings

Why it exists: consumers need to share post-completion feedback about completed service engagements.

Service Marketplace responsibility: admit one eligible review for a completed service request and expose rating/review presentation.

Core guide references: `02-architecture.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Authority boundary: ratings derive from final completed service requests. Provider self-review, duplicate review creation, stale completion evidence, hidden projection data, or frontend-calculated averages cannot become rating authority.

## Trust

Why it exists: consumers need confidence signals when choosing providers.

Service Marketplace responsibility: derive trust and reputation from completed service requests and valid service reviews.

Core guide references: `02-architecture.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Authority boundary: trust is derivative of SQL review/request authority. Search ranking fields, cached aggregates, dashboards, profile badges, and frontend rollups do not own reputation truth.

## Request Lifecycle

Why it exists: a service engagement needs clear business states: request, provider decision, collaboration, provider completion, buyer confirmation, rating.

Service Marketplace responsibility: maintain the service-request lifecycle and expose allowed next actions to provider and consumer actors.

Core guide references: `02-architecture.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Authority boundary: request lifecycle is not order lifecycle. It does not create a generic `Order` or CommerceHub marketplace order. See `05-orders.md` for order authority when actual commerce orders are involved elsewhere.

## Service Completion

Why it exists: service work needs explicit provider-side completion and consumer-side final confirmation.

Service Marketplace responsibility: coordinate the business completion of the service engagement.

Core guide references: `02-architecture.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Authority boundary: final service completion requires the consumer's confirmation after provider completion. Completion does not create payment finality, accounting finality, inventory effects, order fulfillment, or CommerceHub settlement.

## Payment Coordination

Why it exists: some professional services may require payment or proof of payment between POS tenants.

Service Marketplace responsibility: coordinate business-side payment intent or evidence presentation only where an authoritative payment/financial path admits it.

Core guide references: `04-payments.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Authority boundary: Service Marketplace never owns payment finality, provider gateway evidence, refund truth, ledger truth, payout readiness, or settlement. Provider receipts, uploaded proof, chat messages, UI badges, and request status are evidence only until the owning payment/financial authority admits them.

## Business Relationships

Why it exists: POS tenants need an internal collaboration network, not just sales channels.

Service Marketplace responsibility: help tenants discover each other as professional collaborators and build reputation through completed engagements.

Core guide references: `07-platform-overview.md`, `08-qa-philosophy.md`.

Authority boundary: business relationship does not mean cross-tenant data ownership. Each tenant remains isolated. Collaboration occurs through the service request boundary only.

## Knowledge Sharing

Why it exists: service providers may communicate expertise, deliverables, recommendations, or professional advice.

Service Marketplace responsibility: provide a controlled engagement and communication context for professional knowledge exchange.

Core guide references: `06-security-identity.md`, `08-qa-philosophy.md`.

Authority boundary: shared knowledge in chat or service descriptions does not mutate POS catalog, inventory, orders, payments, accounting, Storefront, Affiliate, or CommerceHub state.

## Professional Collaboration

Why it exists: tenants may need services such as consulting, setup, design, operational support, or other tenant-to-tenant assistance.

Service Marketplace responsibility: coordinate the collaboration lifecycle from discovery through request, chat, delivery, completion, and review.

Core guide references: `02-architecture.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Authority boundary: collaboration remains internal POS-to-POS. It does not become a customer purchase journey.

## Analytics

Why it exists: providers and operators need derived visibility into service activity, completion, ratings, and engagement health.

Service Marketplace responsibility: present derived analytics from service listings, requests, final completions, ratings, and trust authority.

Core guide references: `02-architecture.md`, `06-security-identity.md`, `08-qa-philosophy.md`.

Authority boundary: analytics are derived. Analytics never determine request authority, completion authority, payment authority, accounting authority, provider eligibility, service visibility, or lifecycle ownership. Dashboards, KPI cards, trend lines, rollups, exports, cached aggregates, and search statistics are presentation or reporting evidence only. They must not create requests, mark services complete, admit ratings, declare payment success, post ledger truth, or override current SQL state.

---

# Service Provider Journey

```text
Publish Service
↓
Discovery
↓
Receive Request
↓
Review
↓
Accept
↓
Chat
↓
Perform Service
↓
Completion
↓
Receive Payment
↓
Rating
```

## Ownership by step

1. Publish Service
   - Provider submits service-listing intent from POS Admin.
   - Service Marketplace owns service listing authority.
   - It does not publish CommerceHub products or Storefront products.

2. Discovery
   - Eligible POS Admin actors discover the service through internal marketplace discovery.
   - Discovery uses service listing visibility and derivative search only as candidate evidence.

3. Receive Request
   - A consumer POS tenant submits a request.
   - Service request authority records immutable request and provider snapshots.

4. Review
   - Provider evaluates the request.
   - Provider profile/request ownership decides available actions.

5. Accept
   - Provider accepts the request through the service request lifecycle.
   - Acceptance enables engagement and chat.

6. Chat
   - Provider and consumer coordinate inside request-bound chat.
   - Chat membership is SQL request participant authority.

7. Perform Service
   - Provider performs the professional service outside the system or through supporting communication.
   - Service Marketplace coordinates status and evidence; it does not become the work product authority for another POS tenant's business records.

8. Completion
   - Provider marks provider-side completion.
   - Final completion still requires consumer confirmation.

9. Receive Payment
   - Payment is coordinated only through the owning payment/financial authority.
   - Service Marketplace status, chat, proof, or provider claim cannot create payment finality.

10. Rating
   - Consumer rating becomes eligible only after final completed request authority.
   - Rating feeds Service Marketplace trust, not CommerceHub vendor reputation or Storefront product reviews.

---

# Service Consumer Journey

```text
Browse Marketplace
↓
Open Service
↓
Evaluate Provider
↓
Submit Request
↓
Chat
↓
Receive Service
↓
Completion
↓
Payment
↓
Rating
```

1. Browse Marketplace
   - Consumer POS Admin actor browses internal services.
   - There is no Storefront or external customer browsing.

2. Open Service
   - Service detail must be current and visible under service listing authority.
   - Cached detail or search hit is not final authority.

3. Evaluate Provider
   - Consumer evaluates provider profile, ratings, trust, and service description.
   - Trust is SQL-composed from completed requests and valid reviews.

4. Submit Request
   - Consumer submits service request intent.
   - Request authority owns the request identity, snapshot, buyer, and provider.

5. Chat
   - Chat becomes available only after the request lifecycle allows it.
   - Non-participants and foreign tenants cannot participate.

6. Receive Service
   - Consumer receives the agreed professional service.
   - Service Marketplace does not mutate the consumer's POS operations as a side effect.

7. Completion
   - Provider completion alone is not final.
   - Consumer confirmation produces final completed service request authority.

8. Payment
   - Payment truth belongs to `04-payments.md` and financial authorities.
   - Service Marketplace may coordinate business evidence or intent but cannot declare money final.

9. Rating
   - Consumer may rate only when final completion authority exists.
   - Rating is Service Marketplace trust/reputation input only.

---

# Marketplace Operational Flow

```text
POS Admin A
↓
Publishes Service
↓
Marketplace
↓
POS Admin B
↓
Requests Service
↓
Chat
↓
Agreement
↓
Service Delivery
↓
Completion
↓
Payment
↓
Review
```

Interpretation:

- POS Admin A is a provider tenant.
- POS Admin B is a consumer tenant.
- The Marketplace in this diagram is Service Marketplace only.
- The interaction is internal POS-to-POS collaboration.
- No external customer participates.
- Payment is coordinated, but payment truth remains outside Service Marketplace.
- Review affects Service Marketplace trust, not Storefront reviews or CommerceHub governance.

---

# Service Request Lifecycle

Service Request lifecycle is the durable business lifecycle for an internal POS-to-POS service engagement.

Example lifecycle:

```text
Draft
↓
Submitted
↓
Under Review
↓
Accepted
↓
In Progress
↓
Completed by Provider
↓
Accepted by Consumer
↓
Closed
↓
Rated
```

This is a business model for QA reasoning. The exact presentation label may vary, but the durable authority rule does not: current SQL service request state owns lifecycle truth.

## Transition ownership

| Lifecycle stage | Who owns the business transition | Durable rule |
|---|---|---|
| Draft | Consumer-side POS Admin intent before submission, if a draft concept is present | Draft intent is not a provider obligation and does not enable chat, payment finality, completion, or rating. |
| Submitted | Service Consumer (POS Admin) | A submitted request records consumer intent against current visible service/provider authority. |
| Under Review | Service Provider (POS Admin) reviews the submitted request | Review is provider-side decision work; it does not create acceptance until the provider accepts. |
| Accepted | Service Provider (POS Admin) | Acceptance requires provider authority over the service/provider profile and current request authority. |
| In Progress | Both parties collaborate after acceptance | Work may proceed only inside the accepted engagement boundary. Chat can support this stage but does not create it by itself. |
| Completed by Provider | Service Provider (POS Admin) | Provider completion is provider-side evidence that work is done; it is not final closure. |
| Accepted by Consumer | Service Consumer (POS Admin) | Final business acceptance requires consumer confirmation after provider completion. This is the key two-party completion boundary. |
| Closed | Current SQL lifecycle authority after valid completion/termination | Closed state cannot be reopened by chat, cached buttons, route state, or replayed presentation evidence. |
| Rated | Service Consumer (POS Admin), after final completion | Rating is eligible only after final completed/accepted-by-consumer authority. |

## Two-party transitions

The durable collaboration boundary is that acceptance and completion are not single-screen assumptions:

- provider acceptance is required before the engagement becomes active;
- provider-side completion is not final completion;
- consumer acceptance/confirmation is required before the service is finally complete;
- rating depends on final completed request authority, not provider-only completion.

Lifecycle authority belongs to current SQL state. Chat messages, attachments, notification delivery, frontend capability flags, cached detail screens, analytics cards, and search results never change lifecycle by themselves.

Service Marketplace lifecycle is not order lifecycle. It is not Storefront checkout. It is not CommerceHub marketplace order/sub-order lifecycle. It is an internal POS Admin tenant-to-tenant service engagement lifecycle.

---

# Communication Model

Communication is request-bound.

```text
Service Request
↓
Chat Enabled
↓
Attachments
↓
Negotiation
↓
Agreement
↓
Delivery
↓
Completion
```

Rules:

- Chat never exists as business collaboration without a Service Request.
- Chat participants derive from the request: the provider participant and the consumer participant.
- Attachments are evidence only. They may support discussion, delivery, proof, or clarification, but they do not mutate business authority.
- Messages do not mutate service listing authority, request lifecycle authority, payment authority, accounting authority, inventory authority, order authority, or trust authority by themselves.
- Closing chat does not complete the service.
- Provider messages do not replace provider completion.
- Consumer messages do not replace final consumer acceptance.
- Chat is a collaboration surface, not an authority surface.

Chat may help the two POS Admin tenants negotiate scope, timing, deliverables, and evidence. The business state still changes only when the owning lifecycle authority admits a valid transition.

No external customer can join Service Marketplace chat. Storefront customer chat, CommerceHub customer support, Affiliate communication, and WhatsApp transport are separate concepts and must not be imported into this model.

---

# Trust & Reputation

Trust and reputation help POS Admin tenants decide which provider to work with in the internal B2B Service Marketplace.

Trust can influence:

- discovery presentation;
- provider selection;
- ranking and sorting;
- consumer confidence;
- provider comparison;
- internal marketplace credibility.

Trust never overrides:

- identity authority;
- tenant authority;
- payment authority;
- accounting authority;
- request authority;
- lifecycle ownership;
- service visibility authority;
- rating eligibility authority.

Trust is derived from current SQL-composed request, completion, review, and reputation facts. A badge, rating average, review count, ranking signal, analytics rollup, or cached reputation summary is never enough to accept a request, complete a service, admit payment, post accounting, bypass tenant isolation, or override current provider/service eligibility.

Trust belongs to Service Marketplace only. It is not Storefront product review authority, not CommerceHub vendor governance, not Affiliate credibility, and not customer-facing marketplace trust.

---

# Discovery & Search

Service Marketplace discovery is an internal POS Admin discovery mechanism.

```text
Discovery
↓
Search
↓
Filtering
↓
Ranking
↓
Candidate Results
↓
Current SQL Validation
↓
Open Service
```

Search is not business authority. Search helps a POS Admin tenant find candidate services offered by other POS Admin tenants.

Rules:

- Search results are candidate evidence.
- Filtering and ranking are presentation/discovery mechanisms.
- Cached discovery rows and search documents may lag.
- Final authority always comes from current SQL validation.
- Opening a service must re-enter current service detail authority.
- Hidden, deleted, inactive, tenant-invalid, provider-profile-invalid, or provider-governance-invalid services must fail closed even if search still contains a stale candidate.

Discovery and search do not create service requests, admit payment, complete services, calculate accounting, or govern CommerceHub. They are internal Service Marketplace tools for POS-to-POS service discovery only.

---

# Notifications

Notifications are derivative communication outputs from Service Marketplace authority.

Example notification flow:

```text
Request Submitted
↓
Notify Provider
↓
Accepted
↓
Notify Consumer
↓
Completed
↓
Notify Consumer
↓
Rating Available
```

Rules:

- Notifications are derivative.
- Notification delivery never becomes business authority.
- Missing notification does not invalidate current business state.
- Duplicate notification does not create duplicate request, chat, completion, payment, or rating authority.
- A notification about acceptance does not accept a request; current SQL lifecycle does.
- A notification about completion does not complete a service; current SQL lifecycle does.
- A notification about rating availability does not make an ineligible review valid; final completed request authority does.

Notifications may help provider and consumer tenants act promptly. They do not replace request lifecycle, chat membership, payment authority, accounting truth, or rating eligibility.

---

# Operational Recovery

Operational recovery restores collaboration and convergence. It never invents authority.

Examples:

```text
Chat unavailable
↓
Recover communication
```

```text
Search stale
↓
Reindex
```

```text
Notification failed
↓
Retry
```

```text
Projection stale
↓
Rebuild
```

Recovery philosophy:

- Recover communication from current request participant authority.
- Reindex discovery from current SQL service, tenant, and provider profile authority.
- Retry notifications from current lifecycle state and event authority.
- Rebuild projections from current SQL truth.
- Delete, suppress, tombstone, no-op, or fail closed when current authority no longer proves visibility or action eligibility.
- Preserve tenant isolation during repair.
- Keep global/platform recovery separate from CommerceHub Super Admin governance.

Operational recovery can restore collaboration surfaces, search visibility, notifications, projections, and derived reads. It cannot create service listings without authority, create requests, accept requests, complete services, fabricate ratings, admit payments, post accounting, create orders, mutate inventory, or import CommerceHub/Storefront/Affiliate behavior.

---

# Comparison with Other Platform Systems

This table prevents future architectural confusion. Service Marketplace is an internal B2B collaboration platform between POS Admin tenants only. It is not Storefront, not CommerceHub, and not customer-facing.

| Capability | Storefront | CommerceHub | Service Marketplace |
|---|---|---|---|
| Primary Actors | External Storefront customers interacting with one tenant's online store | CommerceHub customers, POS tenants as vendors, CommerceHub/platform governance | POS Admin tenants only: Service Provider and Service Consumer |
| External Customer | Yes | Yes | No |
| POS Admin | Configures tenant Storefront inputs through POS Admin but Storefront is customer-facing | POS tenants participate as CommerceHub vendors through vendor operations | Native operating surface; Service Marketplace exists inside POS Admin capabilities |
| Products | Tenant products presented to Storefront customers | Marketplace product listings from participating vendors | No product commerce; professional services only |
| Services | Not a service marketplace | Not a service marketplace | Core business object: services offered by one POS tenant to another |
| Inventory | Storefront checkout consumes SQL inventory through Core authority | CommerceHub checkout consumes vendor stock through Core authority | Does not own or consume inventory |
| Orders | Generic Storefront orders; see `05-orders.md` | CommerceHub marketplace orders and vendor sub-orders; see `05-orders.md` | Service requests / engagements, not orders |
| Service Requests | None | None | Core collaboration lifecycle object |
| Payments | Customer payment through certified Storefront/POS payment authority where applicable | CommerceHub payment/finance model, separate from Storefront | Payment coordination/evidence only; payment truth remains Core financial authority |
| Accounting | Ledger effects from Storefront orders/payments through Core authority | CommerceHub finance, vendor earnings, settlements, payouts through Core/CommerceHub finance authority | Does not own ledger/accounting truth |
| Chat | Customer communication may exist elsewhere but not Service Marketplace chat | Customer/vendor/support communication is CommerceHub-scoped if present | Request-bound provider/consumer collaboration chat |
| Reviews | Product/order/customer review concepts where certified | Marketplace/customer/vendor review concepts where certified | Service review after final completed service request |
| Governance | Tenant Storefront configuration plus Core authority | CommerceHub marketplace governance and Super Admin scope | Separate Service Marketplace/platform integrity; not CommerceHub governance |
| Primary Purpose | Tenant-branded customer shopping | Customer-facing multi-vendor product marketplace | Internal POS-to-POS professional-service collaboration |

---

# Marketplace Dependency Matrix

| Business Area | Authoritative Source | Presentation Surface | Does NOT Control |
|---|---|---|---|
| Services | SQL service listing authority: active service, active tenant, active same-tenant provider profile, provider lifecycle | POS Admin Service Marketplace discovery, cards, service detail | Storefront products, CommerceHub listings, Affiliate campaigns, inventory, orders |
| Requests | SQL service request authority: buyer, provider, provider profile, immutable request snapshots, lifecycle | POS Admin request list/detail, action capabilities | CommerceHub orders, generic orders, payment finality, accounting |
| Chat | SQL service request participants and persisted SQL messages | POS Admin chat, SignalR invalidation/transport, attachment presentation | Request lifecycle by itself, payment truth, external customer support, tenant data outside the request |
| Ratings | SQL service reviews joined to final completed service requests | Rating widgets, review lists, trust summaries | Storefront product reviews, CommerceHub vendor governance, payment/accounting truth |
| Provider Profiles | SQL provider profile authority and current tenant/user ownership | Provider cards, profile summary, profile details | CommerceHub vendor profile, Storefront merchant identity, Affiliate identity, accounting identity |
| Payments | Payment lifecycle and financial authorities; see `04-payments.md` | Service Marketplace payment coordination/evidence presentation if present | Payment status, gateway success, refunds, settlement, payout readiness, ledger posting |
| Reviews | SQL review identity, reviewer/provider/request lineage, one-review-per-engagement uniqueness | Review list, review count, trust badges | Service request lifecycle after creation, payment finality, provider governance outside Service Marketplace |
| Trust | SQL-composed trust from completed requests and valid reviews | Trust badges, ratings, provider reputation cards, analytics summaries | Core identity, financial truth, CommerceHub governance, search authority |
| Analytics | SQL-derived counts from service listings, requests, completion, ratings, and trust | Provider stats, dashboard cards, trend presentation | Revenue authority, ledger truth, service lifecycle, reviews, payments |
| Search / Discovery | Current SQL service/tenant/profile visibility; Elasticsearch is candidate evidence | Internal discovery results and filters | Business truth, service publication, global governance, Storefront/CommerceHub search |

---

# Operational Change Propagation

```text
Provider updates service
↓
Validation
↓
Marketplace
↓
Discovery
↓
Request
↓
Provider
↓
Consumer
```

Meaning:

1. Provider updates service
   - A POS Admin provider submits service-listing intent.

2. Validation
   - Current tenant, provider profile, service ownership, media, pricing presentation, category, and lifecycle authority must be accepted.

3. Marketplace
   - The internal Service Marketplace state changes only after authoritative validation.

4. Discovery
   - Discovery/search may update asynchronously and may lag.
   - Search remains derivative candidate evidence and must converge from SQL authority.

5. Request
   - New requests use current service authority.
   - Existing requests preserve immutable service/provider snapshots and are not rewritten by later service edits.

6. Provider
   - Provider sees request and engagement state according to current SQL request authority.

7. Consumer
   - Consumer sees request, chat, completion, payment coordination, and review capability according to current SQL authority.

QA implication: later service listing edits must not rewrite historical request snapshots, change completed engagement history, fabricate trust, or resurrect hidden discovery visibility from stale search documents.

---

# Authority Boundaries

Service Marketplace orchestrates service interactions. It never becomes a Core authority.

Service Marketplace never becomes:

## Inventory Authority

It does not reserve, release, consume, restore, or move stock. Inventory truth belongs to PostgreSQL inventory authority and the rules in `03-inventory.md`.

## CommerceHub Authority

It does not own CommerceHub marketplace listings, carts, checkout, orders, sub-orders, vendor earnings, COD debt, settlement, payout readiness, or CommerceHub governance.

## Storefront Authority

It does not own Storefront customer identity, Storefront product discovery, Storefront cart, checkout, payment, order, or product review behavior.

## Accounting Authority

It does not post ledger entries, declare revenue, produce financial reporting truth, settle funds, or own balances. Accounting truth remains SQL ledger authority; see `04-payments.md`.

## Payment Authority

It does not admit provider evidence, declare payment success/failure, issue refunds, settle payouts, or clear reconciliation. Payment authority remains with the payment lifecycle and financial readiness boundaries in `04-payments.md`.

## Order Authority

It does not create generic `Order` authority or CommerceHub marketplace order/sub-order authority. A service request is a service engagement, not an order lifecycle. See `05-orders.md` when actual order behavior is involved elsewhere.

## Security / Identity Authority

It depends on current POS Admin identity, tenant, user, profile, and participant authority. It cannot trust browser state, route ids, cached capabilities, or stale claims. See `06-security-identity.md`.

## Search / Projection Authority

Search documents and cached discovery are derivative. Service Marketplace discovery must converge from current SQL service, tenant, and provider profile authority. See `02-architecture.md` and `08-qa-philosophy.md`.

---

# Business Rules

Only durable business rules are listed here.

1. Service Marketplace is internal POS-to-POS only.
2. Every actor is a POS Admin tenant actor.
3. There is no external customer actor.
4. A service provider is a POS tenant offering a professional service.
5. A service consumer is a POS tenant requesting a professional service.
6. Service Marketplace is part of POS Admin capabilities, not CommerceHub.
7. Service Marketplace has no business relationship with Storefront.
8. Service Marketplace has no business relationship with Affiliate.
9. CommerceHub Super Admin governance does not govern Service Marketplace.
10. Service listings are Service Marketplace listings only; they are not Storefront products or CommerceHub listings.
11. Service request identity is owned by SQL service request authority.
12. Request snapshots preserve historical service/provider economics and identity for the engagement.
13. Later service listing edits must not rewrite historical request snapshots.
14. Duplicate active requests for the same buyer/service lane must fail closed or collapse according to request authority.
15. Provider acceptance is required before engagement/chat behavior becomes active.
16. Chat membership derives from request buyer/provider participants.
17. Chat transport, SignalR groups, cached messages, route IDs, and browser state do not own chat authority.
18. Provider completion alone is not final completion.
19. Final completion requires consumer confirmation.
20. Rating eligibility derives from final completed service request authority.
21. One service engagement must not produce duplicate authoritative reviews.
22. Provider self-review is not valid business feedback.
23. Reputation and trust derive from completed requests and valid reviews, not frontend averages or cached aggregates.
24. Payment coordination does not equal payment authority.
25. Service Marketplace does not own accounting, ledger, settlement, refunds, or payout readiness.
26. Discovery/search is derivative and must converge from current SQL service, tenant, and provider profile authority.
27. Tenant isolation remains mandatory: one tenant cannot use Service Marketplace to access another tenant's POS operations outside the request boundary.
28. Support or platform recovery may investigate or repair within governed boundaries, but cannot become a service participant or Core authority.
29. Stale projections, cached capabilities, frontend state, and event payloads are evidence only.
30. Under uncertainty, Service Marketplace must fail closed rather than exposing foreign tenant data, resurrecting hidden services, reopening terminal requests, or fabricating payment/review/trust truth.

---

# Never Assume

Never assume:

- Service Marketplace equals CommerceHub.
- Service Marketplace is part of CommerceHub.
- Service Marketplace is customer-facing.
- Customers use Service Marketplace.
- Storefront participates in Service Marketplace.
- Affiliate participates in Service Marketplace.
- CommerceHub Super Admin governance governs Service Marketplace.
- CommerceHub vendor governance decisions automatically affect Service Marketplace requests.
- Service Marketplace service listings are CommerceHub product listings.
- Service Marketplace service listings are Storefront products.
- Service Marketplace requests are CommerceHub orders.
- Service Marketplace requests are generic orders.
- Service Marketplace chat is customer support chat.
- Service Marketplace ratings are Storefront product reviews.
- Service Marketplace trust is CommerceHub vendor governance.
- Service Marketplace owns payments.
- Service Marketplace owns accounting.
- Service Marketplace owns inventory.
- Service Marketplace owns orders.
- Service Marketplace owns settlement, payouts, refunds, or ledger truth.
- Search documents can restore hidden or invalid service visibility.
- Frontend route state or cached capabilities can authorize provider/consumer actions.
- Provider completion alone means the service is finally complete.
- Uploaded proof, chat text, or a UI status means money is final.
- Platform recovery scope is the same as CommerceHub Super Admin governance.

---

# QA Checklist

A QA Engineer should be able to answer these before testing Service Marketplace behavior.

## Actor and scope

- [ ] Who is the actor: Service Provider POS Admin, Service Consumer POS Admin, Platform, or Support?
- [ ] Is the actor a current authenticated POS Admin tenant actor?
- [ ] Is there any external customer involved? The expected answer is no.
- [ ] Is any Storefront, CommerceHub customer, Affiliate visitor, or anonymous user being treated as a Service Marketplace actor? That is a defect.
- [ ] Is tenant scope proven by current backend authority, not route ID, browser state, cached tenant, or raw header?

## Service listing

- [ ] Does current SQL service, tenant, and provider profile authority prove the service is visible?
- [ ] Are search/discovery results treated as candidate evidence only?
- [ ] Does hidden, deleted, inactive, suspended, tenant-lost, or profile-lost service visibility fail closed?
- [ ] Do service edits avoid rewriting historical request snapshots?

## Service request

- [ ] Who owns service requests? SQL service request authority.
- [ ] Is requester ownership the consumer POS Admin actor?
- [ ] Is provider ownership derived from current service/provider profile authority at request creation?
- [ ] Are duplicate active requests blocked or safely collapsed?
- [ ] Are accept/reject/cancel/complete actions lifecycle-valid and participant-valid?

## Chat

- [ ] Does chat membership come from SQL request buyer/provider participants?
- [ ] Can a third party, foreign tenant, Storefront customer, CommerceHub customer, or affiliate join? The expected answer is no.
- [ ] Is SignalR treated as transport/invalidation, not authority?
- [ ] Is closed history visible only where participant authority allows it?

## Completion and rating

- [ ] Does provider completion require an accepted request?
- [ ] Does final completion require consumer confirmation?
- [ ] Is rating allowed only after final completed request authority?
- [ ] Is one review per engagement enforced?
- [ ] Are reputation and trust SQL-composed rather than frontend-calculated?

## Payments and accounting

- [ ] Who owns payments? The payment lifecycle / financial authorities in `04-payments.md`, not Service Marketplace.
- [ ] Who owns accounting? SQL ledger/accounting authority in `04-payments.md`, not Service Marketplace.
- [ ] Does payment evidence remain evidence until admitted by the owning authority?
- [ ] Does unresolved payment evidence fail closed rather than becoming final status?

## System separation

- [ ] How is this different from CommerceHub? Internal POS-to-POS services, no external customer, service requests not orders, no CommerceHub governance.
- [ ] How is this different from Storefront? Storefront is customer shopping; Service Marketplace is internal POS tenant collaboration.
- [ ] How is this different from Affiliate? Affiliate is traffic/attribution/commission; Service Marketplace is provider/consumer service collaboration.
- [ ] Is any test copying CommerceHub, Storefront, or Affiliate assumptions into Service Marketplace? If yes, stop and reframe.

---

# Common Failure Scenarios

## Wrong actor type

Symptom: a Storefront customer, CommerceHub customer, affiliate user, anonymous user, or Super Admin-only actor is treated as a Service Marketplace participant.

Correct behavior: fail closed. Service Marketplace actors are POS Admin tenants only.

## External customer access

Symptom: public/customer routes, customer tokens, Storefront state, or CommerceHub customer sessions can browse, request, chat, complete, or rate services.

Correct behavior: fail closed. No external customer participates.

## Provider suspension or profile loss

Symptom: a service remains discoverable, requestable, or trust-visible after current provider profile, tenant, or lifecycle authority no longer permits it.

Correct behavior: discovery/detail/request entry fail closed; derivative search and cached presentation converge by removal or absence.

## Duplicate requests

Symptom: the same consumer/provider/service lane creates multiple active requests through double-submit, retry, stale UI action, or concurrent tabs.

Correct behavior: request authority blocks or collapses duplicate active requests; no duplicate engagement authority is created.

## Chat disconnected from request authority

Symptom: chat can be joined, read, or written from route ID, SignalR group, cached membership, or browser state without current request participant proof.

Correct behavior: fail closed. Chat membership and send authority derive from SQL service request participants and lifecycle.

## Payment evidence mismatch

Symptom: chat proof, uploaded evidence, provider claim, frontend status, or request status is treated as payment success or accounting truth.

Correct behavior: payment evidence remains evidence until admitted by the payment/financial authority; mismatches or uncertainty fail closed under `04-payments.md`.

## Rating inconsistencies

Symptom: provider self-review, duplicate review, review before final buyer completion, frontend-calculated average, or stale aggregate changes reputation.

Correct behavior: ratings derive from SQL review authority joined to final completed request authority; trust is recomposed from valid reviews.

## Request lifecycle mismatch

Symptom: pending requests allow chat, rejected/cancelled requests reopen, provider-only completion counts as final completion, or buyer confirmation occurs without provider completion.

Correct behavior: lifecycle transitions remain constrained; final completion requires `Accepted -> CompletedByProvider -> CompletedByBuyer` business progression.

## CommerceHub confusion

Symptom: QA expects CommerceHub cart/order/sub-order/payment/governance behavior in Service Marketplace.

Correct behavior: reject the assumption. Service Marketplace requests are internal service engagements, not CommerceHub commerce.

## Storefront confusion

Symptom: QA expects Storefront customers, carts, checkout, product reviews, or tenant product publication to participate.

Correct behavior: reject the assumption. Storefront has no relationship to Service Marketplace.

## Affiliate confusion

Symptom: QA expects affiliate traffic, attribution, commission, campaign links, or payout logic to participate.

Correct behavior: reject the assumption. Affiliate has no relationship to Service Marketplace.

## Platform governance confusion

Symptom: CommerceHub Super Admin governance is used as the mental model for Service Marketplace operations.

Correct behavior: separate the systems. CommerceHub Super Admin governs CommerceHub. Service Marketplace may have separate platform integrity/recovery boundaries, but not CommerceHub business governance.

---

# Source Documents

Primary Knowledge Base and Core Guides:

- `.ai/hermes/INDEX.md`
- `.ai/hermes/02-architecture.md`
- `.ai/hermes/03-inventory.md`
- `.ai/hermes/04-payments.md`
- `.ai/hermes/05-orders.md`
- `.ai/hermes/06-security-identity.md`
- `.ai/hermes/07-platform-overview.md`
- `.ai/hermes/08-qa-philosophy.md`

Authoritative `.ai-context` files:

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `.ai-context/KNOWN_RISKS.md`

System guides used for boundary separation:

- `.ai/hermes/systems/pos-admin/README.md`
- `.ai/hermes/systems/super-admin/README.md`

Service Marketplace certification and authority documents:

- `docs/architecture/service-marketplace-listing-authority-certification-findings.md`
- `docs/architecture/service-marketplace-request-engagement-authority-certification-findings.md`
- `docs/architecture/service-marketplace-chat-authority-certification-findings.md`
- `docs/architecture/service-marketplace-completion-authority-certification-findings.md`
- `docs/architecture/service-marketplace-rating-reputation-authority-certification-findings.md`
- `docs/architecture/service-marketplace-analytics-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-final-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-actor-navigation-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-discovery-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-listing-card-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-service-detail-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-request-entry-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-visibility-lifecycle-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-request-creation-workflow-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-contract-presentation-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-request-state-machine-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-request-timeline-audit-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-chat-identity-access-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-message-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-attachment-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-realtime-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-chat-history-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-completion-eligibility-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-completion-workflow-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-completion-evidence-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-request-lifecycle-presentation-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-completion-state-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-completion-timeline-audit-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-rating-eligibility-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-rating-submission-workflow-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-rating-state-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-reputation-presentation-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-rating-reputation-timeline-audit-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-analytics-identity-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-kpi-metrics-presentation-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-analytics-aggregation-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-analytics-dashboard-trend-authority-certification-findings.md`
- `docs/architecture/service-marketplace-frontend-trust-reputation-presentation-authority-certification-findings.md`

Adjacent operational/search authority documents:

- `docs/architecture/marketplace-governance-certification-findings.md`
- `docs/architecture/operational-tooling-certification-findings.md`
- `docs/architecture/elasticsearch-freshness-search-convergence-certification-findings.md`
- `docs/architecture/global-search-reindex-recovery-certification-findings.md`
- `docs/architecture/global-scope-tenant-boundary-certification-findings.md`
- `docs/operations/operational-recovery-runbooks.md`
