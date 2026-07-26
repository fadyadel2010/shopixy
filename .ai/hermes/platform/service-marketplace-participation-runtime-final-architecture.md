121# Service Marketplace Participation Runtime — Final Canonical Architecture

Implementation-backed reverse engineering of how one POS Admin tenant (and the users inside it)
can offer professional services to other users inside the same tenant through the Service
Marketplace, and how that runtime coordinates requests, chat, trust, and evidence.

> Implementation is the ONLY source of truth. Every normative claim below is cited to
> `file:line` in `PointOfSale.Core`, `PointOfSale.Infrastructure`, and `PointOfSale.API`.
> Prior markdown/controllers/DTO names/UI labels were used only to locate code. Where a prior
> assumption (including the prompt's framing) contradicted code, the code wins and the
> contradiction is recorded in "Documentation Validation" at the end.

Canonical paths inspected:
- Provider identity: `PointOfSale.Core/Entities/MarketplaceProfile.cs`
- Service catalog: `PointOfSale.Core/Entities/Service.cs`, `PointOfSale.Infrastructure/Services/ServiceCatalogService.cs`
- Request/engagement state machine: `PointOfSale.Core/Entities/ServiceRequest.cs`, `PointOfSale.Infrastructure/Services/ServiceRequestService.cs`, `PointOfSale.Infrastructure/Data/MarketplaceInvariantInterceptor.cs`
- Chat runtime: `PointOfSale.Infrastructure/Services/ChatService.cs`, `PointOfSale.API/Hubs/ChatHub.cs`, `PointOfSale.Core/Entities/ServiceRequestMessage.cs`
- Trust runtime: `PointOfSale.Core/Entities/ProviderTrustAggregate.cs`, `PointOfSale.Infrastructure/Services/ProviderTrustService.cs`, `PointOfSale.Infrastructure/Consumers/ProviderTrustConsumer.cs`
- Reviews/evidence: `PointOfSale.Core/Entities/ServiceReview.cs`, `MarketingReviewEvidence.cs`, `PointOfSale.Infrastructure/Services/ServiceReviewService.cs`
- Search/read projection (CQRS): `PointOfSale.Infrastructure/Services/MarketplaceSearchService.cs`, `PointOfSale.Infrastructure/Consumers/ServiceDiscoveryProjectionConsumer.cs`, `PointOfSale.Infrastructure/Elasticsearch/Documents/MarketplaceServiceDocument.cs`
- HTTP surface: `PointOfSale.API/Controllers/MarketplaceController.cs`
- Enforcement interceptor: `PointOfSale.Infrastructure/Data/MarketplaceInvariantInterceptor.cs`

---

## Executive Summary

The Service Marketplace is **NOT** a cross-tenant B2B commerce runtime, **NOT** a payments
runtime, **NOT** an escrow/settlement runtime, and **NOT** a second marketplace parallel to the
CommerceHub product marketplace. It is an **internal Service-Participation + Engagement +
Trust Runtime that lives inside a single POS Admin tenant**:

- A **Provider** is a `MarketplaceProfile` — a *user-level profile* owned by a POS `User` inside
  one tenant (`MarketplaceProfile.cs:7-33`). It is **not** a tenant, **not** a separate identity,
  **not** an external entity, and **not** a vendor-governance subject.
- A **Consumer (Buyer)** is simply *another `User` in the same tenant* — there is no buyer
  entity, no buyer profile, no buyer aggregate. The buyer is the `ServiceRequest.BuyerUserId`.
- A **Service** belongs to a `MarketplaceProfile` and is a `TenantEntity` scoped to the tenant
  (`Service.cs:9-11`).
- A **ServiceRequest (engagement)** is a `TenantEntity` scoped to the *provider's tenant*. Buyer
  and Provider are two `User` rows inside that *same* tenant (`ServiceRequest.cs:9-13`,
  `ServiceRequestService.cs:120-135`).
- **Global discovery is deliberate but read-only.** The Elasticsearch service index
  (`marketplace_services`) is intentionally tenant-UNscoped for *search visibility*
  (`MarketplaceSearchService.cs:344-345` — "Tenant filter REMOVED for Marketplace (Global
  visibility)"), so services from any tenant are globally discoverable. But the *engagement*
  (request/chat/review/trust) is always re-scoped to current SQL tenant/profile authority before
  it is shown or mutated (`MarketplaceSearchService.cs:114-235`; every request/chat path filters
  by `request.TenantId == tenantId`).
- **No money is owned here.** There is no payment, escrow, settlement, commission, invoice, or
  fee path anywhere in this bounded context (repo-wide search for `Escrow|Settlement|Commission|
  PaymentIntent|Refund` across the service-marketplace entities returns zero hits). The only
  "financial" touch is *read-only* `POS` order aggregation used to self-verify marketing campaign
  evidence (`ServiceReviewService.cs:139-207`).
- **Trust is derived, never set.** `ProviderTrustAggregate` is recomputed from `ServiceReview`
  SQL truth; the `TrustBadge` is a pure function of `AverageRating`/`TotalReviews`
  (`ProviderTrustAggregate.cs:195-210`); a `ProcessedTrustEvent` idempotency marker prevents
  double-counting (`ProviderTrustConsumer.cs:57-70`).
- It is a **thin CRUD + state-machine + projection runtime** over `TenantEntity` rows, with one
  authoritative SQL write store (PostgreSQL), one derivative search read model (Elasticsearch),
  and SignalR as a *delivery-only* realtime layer. There is **no Mongo and no Redis** ownership
  for this runtime (those belong to the separate CommerceHub product marketplace
  `MarketplaceReadController`, which is a different subsystem — see Cross-Runtime Authority Graph).

Runtime classification (thesis): **Service Participation + Engagement + Trust Runtime (single-tenant,
globally discoverable, financially inert).** It is the runtime that lets a *user inside a tenant*
become a *provider* to *other users inside the same tenant*, while the tenant boundary — enforced by
`MarketplaceProfile.UserId` ownership, `TenantEntity.TenantId` scoping, the
`MarketplaceInvariantInterceptor`, and per-action `userId/tenantId` checks — is never crossed by a
write. Global visibility is a *read projection* only; it never grants another tenant write
authority over a provider's data.

---

## Core Discovery — What Is a Provider? What Is a Consumer?

| Question | Implementation answer | Evidence |
|---|---|---|
| Is a Provider a Tenant? | **No.** It is a `MarketplaceProfile` row owned by a `User`. The tenant is the *container*; the provider is a *user inside it*. | `MarketplaceProfile.cs:7-21` (`UserId`, `TenantId` on a `TenantEntity`) |
| Is a Provider a Profile? | **Yes (primary answer).** `MarketplaceProfile` is the provider's identity/identity-record. | `MarketplaceProfile.cs:1-33` |
| Is a Provider a separate Identity? | **No.** It reuses the POS `User` JWT/identity. There is no separate affiliate-style auth. The profile is created for an *existing* `UserId` (`MarketplaceProfileService.CreateProfileAsync` keys on `UserId`). | `MarketplaceProfileService.cs:21-37`; `MarketplaceController.cs:80-98` (`GetUserId()` from JWT) |
| Is a Provider an External Entity? | **No.** It is internal to the tenant. The runtime explicitly excludes `superadmin` actors from the marketplace workflow surfaces (cert findings: "Marketplace runtime requires authenticated tenant/user identity and excludes `superadmin` actors"). | `docs/architecture/service-marketplace-frontend-final-certification-findings.md:33-39` |
| Is a Provider a Projection? | **No.** It is a first-class `TenantEntity` aggregate with its own lifecycle (`IsActive`, `Deactivate/Activate`), not a projection of `User` or `Tenant`. | `MarketplaceProfile.cs:35-57`; `MarketplaceProfileService.cs:70-98` |

**What is a Service Consumer (Buyer)?** There is no buyer entity. The consumer is the
`ServiceRequest.BuyerUserId` — another `User` in the *same tenant*. The request factory enforces
`buyerUserId != providerUserId` (`ServiceRequest.cs:48-49`) and stores both IDs on the request.

**Relationship (Provider ↔ Consumer):** Both are `User` rows in one tenant. The provider is the
user who owns a `MarketplaceProfile` and the `Service`; the consumer is any other user in that
tenant who opens a `ServiceRequest` against the service. They are *peers inside one tenant*, not
two tenants, not two legal entities.

**Relationship (Provider ↔ POS Admin tenant):** `MarketplaceProfile.TenantId == User.TenantId`.
A profile is scoped to exactly one tenant and cannot be created for a user in another tenant
(`MarketplaceProfileService.cs:23` reads `ITenantContext.TenantId`). A user can have at most one
profile (`MarketplaceProfileService.cs:25-29` throws `ProfileAlreadyExists`).

---

## Runtime Purpose

The runtime exists to let **users inside a single POS Admin tenant** offer and consume
professional services from each other — publish a service, discover it, request it, negotiate via
chat, deliver, review, and build a trust reputation — **without ever transferring tenant authority
or money** to another party. It is an *intra-tenant collaboration surface*, deliberately made
globally discoverable at the read layer so a tenant's providers can be found from anywhere, while
every write stays pinned to the provider's owning tenant.

Evidence: every engagement mutation re-resolves `ITenantContext.TenantId` and compares it to
`request.TenantId`/`service.TenantId` (`ServiceRequestService.cs:112`; `ChatService.cs:88-91`;
`ServiceReviewService.cs:35`); the search layer explicitly drops the tenant filter only for
*discovery candidates* and then recomposes visibility from per-tenant SQL
(`MarketplaceSearchService.cs:344-345`, `:114-235`).

---

## Runtime Owns (Authoritative)

The runtime is the **sole authoritative writer** of:

1. **Provider Identity (profile):** `MarketplaceProfile` — display name, bio, images, location,
   skills, portfolio links, `IsActive`. Only the owning `UserId` may mutate it.
   (`MarketplaceProfileService.cs:39-98`; invariant: `profile.UserId != userId` →
   `Unauthorized`.)
2. **Provider Profile lifecycle:** `IsActive` boolean (the ONLY provider status implemented —
   there is **no** Pending/Verified/Suspended/Blocked/Rejected/Closed/Archived state machine for
   profiles; see Provider Status section).
3. **Service Catalog:** `Service` rows — title, description, category, pricing, images, packages.
   Only the owning profile's `UserId` may create/update/deactivate.
   (`ServiceCatalogService.cs:34-103`, `:105-161`, `:208-270`.)
4. **Service Categories:** a free-form `string? Category` column on `Service`
   (`Service.cs:14`). There is **no** `ServiceCategory` entity, no taxonomy table, no category
   FK. Categories are untyped strings; the only special one is `"Marketing"` (case-insensitive),
   which triggers evidence enforcement (`ServiceReviewService.cs:66-73`,
   `IsMarketingCategory` `:213-214`).
5. **Service Pricing / Availability / Packages / Media:** all stored as columns / JSON on
   `Service` (`Service.cs:17-22`). Pricing is **display-only** — `PricingType` enum documents
   "Display-only - backend never computes prices" (`PricingType.cs:5-6`). Availability is the
   boolean `IsActive` (`Service.cs:15`). There is no calendar/availability model.
6. **Service Requests (Engagement):** `ServiceRequest` — the single authoritative engagement
   aggregate and its status machine (`ServiceRequest.cs`, `MarketplaceInvariantInterceptor.cs`).
7. **Conversation / Chat:** `ServiceRequestMessage` rows (1:1 with the request via
   `ServiceRequestId`; `ConversationId == ServiceRequestId`). Immutable create-only messages,
   tenant-scoped storage keys for attachments. (`ServiceRequestMessage.cs:9-13`,
   `ChatService.cs:46-175`.)
8. **Reviews:** `ServiceReview` — one per `ServiceRequest`, immutable after creation.
   (`ServiceReview.cs:1-52`; interceptor forbids update/delete `MarketplaceInvariantInterceptor.cs:58-65`.)
9. **Campaign Evidence:** `MarketingReviewEvidence` — immutable, server-classified
   (`SelfReported`/`PosVerified`), server-computed order/revenue metrics.
   (`MarketingReviewEvidence.cs:1-146`.)
10. **Ratings / Trust / Reputation:** `ProviderTrustAggregate` — derived from reviews, optimistic
    concurrency via `Version`, recomputable via `Reconcile`. (`ProviderTrustAggregate.cs`.)
11. **Trust Badge:** derived function `ComputeBadge()` (`ProviderTrustAggregate.cs:195-210`).
12. **Provider Settings / Metadata:** all columns on `MarketplaceProfile` (skills, portfolio,
    location, images). (`MarketplaceProfile.cs`.)
13. **Verification (of marketing evidence):** classification of `EvidenceType` — server-determined,
    never client-supplied. (`MarketingReviewEvidence.cs:42-45`,
    `ServiceReviewService.cs:139-207`.)
14. **Request lifecycle timestamps:** `AcceptedAt/RejectedAt/CompletedByProviderAt/
    CompletedByBuyerAt/CancelledAt` — immutable except with their transition
    (`MarketplaceInvariantInterceptor.cs:138-155`).
15. **Search/read projection of services (Elasticsearch `marketplace_services`):** converged from
    SQL authority by `ServiceDiscoveryProjectionConsumer` and re-validated at query time by
    `MarketplaceSearchService`.

---

## Runtime Does NOT Own

The runtime deliberately leaves the following to other runtimes and only *observes* or *borrows
read-only*:

- **Payments, Escrow, Settlement, Invoices, Commissions, Fees:** none exist here (search returns 0
  hits across service-marketplace entities). Pricing is display metadata only.
- **The POS `Order` / `Customer` / CommerceHub commerce truth:** the only touch is a *read-only*
  aggregation of a provider's own tenant `Order` rows to self-verify marketing evidence
  (`ServiceReviewService.cs:155-168` — `IgnoreQueryFilters` then filter `o.TenantId ==
  providerTenantId`). It never writes orders, never owns them.
- **Tenant existence / activation / governance:** visibility gates on `Tenants.IsActive`,
  `Status == Active`, `DeletedAtUtc == null`, and *absence* of a Vendor governance suspension/
  ban (`ServiceRequestService.cs:71-86`, `ServiceCatalogService.cs:163-187`,
  `MarketplaceSearchService.cs:201-219`). It reads these; it does not mutate tenant governance.
- **Authentication / JWT issuance:** delegated to the Identity runtime; the marketplace reads
  `UserId`/`tenant_id` claims (`ChatHub.cs:248-259`, `MarketplaceController` `GetUserId()`).
- **Cross-tenant product marketplace (CommerceHub) catalog/orders/payouts:** a *separate*
  subsystem (`MarketplaceReadController`, Mongo, Redis) — NOT this runtime. This runtime's only
  cross-tenant action is *publishing a service document to a global Elasticsearch index*.
- **User/profile directory, notifications delivery bus, media blob storage:** it references users
  by `Id` and stores tenant-scoped storage keys for attachments; it does not own the user
  directory or the blob store.
- **Analytics compute beyond what SQL truth gives:** analytics cards are derived from current SQL
  counts (`MarketplaceController.cs:141-170`); there is no separate analytics store.

---

## Identity Relationship — POS Admin ↓ Provider

```
Tenant (POS Admin)                         User (POS Admin, in-tenant)
   │                                              │
   │ TenantId (owns the container)                │ UserId (authenticated via POS JWT)
   ▼                                              ▼
MarketplaceProfile  ◀── created BY UserId ──┐
   │ ProfileId                                   │
   │ (Provider identity)                         │  (the SAME tenant's other users are Consumers)
   ▼                                            │
Service (ProfileId)                            │
   │                                            │
   ▼                                            │
ServiceRequest (TenantId = tenant,            │
   BuyerUserId = other User in SAME tenant, ───┘
   ProviderUserId = profile.UserId)
```

- **Same Identity?** No. Provider is a `MarketplaceProfile`, not the `User` and not the `Tenant`.
- **Separate Identity?** No — it is *not* a separately-authenticated identity (unlike the Affiliate
  runtime). It rides on the POS `User` identity.
- **Projection?** No. It is a first-class `TenantEntity` aggregate, not a read projection of `User`
  or `Tenant`.
- **Binding?** Yes — a 1:1 binding: one `MarketplaceProfile` per `UserId` per tenant
  (`MarketplaceProfileService.cs:25-29`). The binding is `MarketplaceProfile.UserId` → `User.Id`,
  both under the same `TenantId`.
- **Reference?** The profile references the user by `UserId`; services reference the profile by
  `ProfileId`; requests snapshot both `BuyerUserId` and `ProviderUserId`
  (`ServiceRequest.cs:11-14`).
- **Duplicate?** No. Exactly one profile per user (enforced at create).

Implementation-resolved: `MarketplaceProfile` is the provider. `Tenant.Id` is the container.
`User.Id` is the authenticated actor. The marketplace never creates or mutates `Tenant` or `User`.

---

## Authority Discovery — Who Owns What

| Concept | Owner | Evidence |
|---|---|---|
| Provider (profile) | Service Marketplace (this runtime), scoped to `UserId` | `MarketplaceProfileService.cs:39-98` |
| Consumer (buyer) | Identity runtime (the `User`); marketplace only references `BuyerUserId` | `ServiceRequest.cs:12` |
| Service | This runtime (profile owner `UserId`) | `ServiceCatalogService.cs:105-161` |
| Service Request | This runtime (participants only) | `ServiceRequestService.cs:214-343` |
| Conversation | This runtime (`ServiceRequestMessage`, tenant-scoped) | `ChatService.cs` |
| Evidence | This runtime (immutable, server-classified) | `MarketingReviewEvidence.cs`, `ServiceReviewService.cs` |
| Ratings | This runtime (`ServiceReview`, 1/request) | `ServiceReviewService.cs:33-131` |
| Trust | This runtime (`ProviderTrustAggregate`, derived) | `ProviderTrustService.cs`, `ProviderTrustConsumer.cs` |
| Verification | This runtime (EvidenceType server-determined) | `ServiceReviewService.cs:139-207` |
| Tenant / governance | Platform/Identity/Governance runtime (marketplace only reads) | `ServiceRequestService.cs:71-86` |
| Orders / Payments | Orders/Payments runtimes (marketplace only reads for evidence) | `ServiceReviewService.cs:155-168` |
| Search index | This runtime converges it; ES is a read model | `ServiceDiscoveryProjectionConsumer.cs` |

---

## Service Lifecycle (Implementation)

```
Provider creates MarketplaceProfile          MarketplaceProfileService.CreateProfileAsync
        │                                     (1 per UserId; throws if exists)
        ▼
Provider creates Service (ProfileId)         ServiceCatalogService.CreateServiceAsync
        │                                     -> publishes ServiceCreatedEvent (outbox)
        ▼
Service published to global ES index         ServiceDiscoveryProjectionConsumer (upsert doc)
  (visibility recomposed from SQL tenant      MarketplaceSearchService.BuildQuery:
   authority at query time)                    "Tenant filter REMOVED for Global visibility"
        ▼
Consumer discovers Service (search)          MarketplaceSearchService.SearchServicesAsync
        │                                     -> ES candidates, SQL authority recomposed
        ▼
Consumer requests Service                    ServiceRequestService.RequestServiceAsync
        │                                     -> Serializable tx; ServiceRequest.Create(Pending)
        ▼                                     -> UX_ServiceRequests_Active_Service_Buyer unique
Provider Accepts                             ServiceRequestService.AcceptRequestAsync
        │                                     -> Status=Accepted; system chat msg
        ▼
Conversation starts (chat)                   ChatService.SendMessageAsync (AllowsInteraction)
        │                                     SignalR ChatHub broadcasts to group sr:{requestId}
        ▼
Provider Delivers (marks complete)           ServiceRequestService.MarkCompletedByProviderAsync
        │                                     -> Status=CompletedByProvider
        ▼
Consumer confirms completion                 ServiceRequestService.ConfirmCompletedByBuyerAsync
        │                                     -> Status=CompletedByBuyer
        │                                     -> publishes ServiceRequestCompletedEvent (outbox)
        ▼
Consumer submits Review                      ServiceReviewService.CreateReviewAsync
        │                                     -> 1 review/request; marketing evidence enforced
        │                                     -> publishes ServiceReviewCreatedEvent (outbox)
        ▼
Trust updated (async consumer)               ProviderTrustConsumer.Consume
        │                                     -> ApplyReview + ProcessedTrustEvent marker
        │                                     -> publishes ProviderTrustUpdatedEvent
        ▼
Trust/Stats projected to ES                  ServiceDiscoveryProjectionConsumer (trust+stats)
```

### "Provider Suspended / Restored" — what actually happens
There is **no provider suspend/restore state machine** in this runtime. What exists:
- Profile `IsActive` is a manual on/off by the owning user (`Deactivate/Activate`).
- **Tenant-level** suspension/ban (Vendor governance) *indirectly* hides the provider's services
  and blocks new requests: the visibility gate checks `UserGovernanceProfiles` for
  `Suspended`/`Banned` Vendor status and refuses (`ServiceRequestService.cs:71-86`,
  `MarketplaceController.cs:116-138`, `MarketplaceSearchService.cs:215-219`). So "Provider
  Suspended" in the prompt maps to **tenant vendor-governance suspension**, owned by the
  Governance/Participation runtime — *not* a marketplace-owned provider status.
- `Reject` and `Cancel` are *request*-level transitions, not provider-status transitions
  (`ServiceRequest.cs:87-126`).

---

## Runtime State Machines

### 1. ServiceRequest (Engagement) — authoritative
Enum `ServiceRequestStatus` (`ServiceRequestStatus.cs:11-19`):
`Pending(0) → Accepted(1) → CompletedByProvider(2) → CompletedByBuyer(3)`, plus
`Cancelled(4)`, `Rejected(5)`.

Valid transitions (enforced in **two** places — the entity methods and the EF interceptor):
- `Pending → Accepted` (`ServiceRequest.Accept`, `:77-85`)
- `Pending → Rejected` (`ServiceRequest.Reject`, `:87-95`)
- `Pending → Cancelled` (`ServiceRequest.Cancel` when Pending/Accepted, `:117-126`)
- `Accepted → CompletedByProvider` (`MarkCompletedByProvider`, `:97-105`)
- `Accepted → Cancelled`
- `CompletedByProvider → CompletedByBuyer` (`ConfirmCompletedByBuyer`, `:107-115`)

Interceptor (`MarketplaceInvariantInterceptor.cs:95-110`) is the hard gate: any other direct
`Status` mutation throws. Immutable properties (`TenantId, ServiceId, BuyerUserId,
ProviderUserId, ProviderProfileId, all snapshots, RequestMessage, CreatedAt`) cannot change after
creation (`:112-136`). Lifecycle timestamps can only change with their transition (`:138-155`).
DB-level `FOR UPDATE` pessimistic lock + `Serializable` isolation prevent races
(`ServiceRequestService.cs:120`, `:381-398`; `ChatService.cs:74-81`).

### 2. MarketplaceProfile (Provider) — NOT a status machine
Only `IsActive` boolean: `Active ↔ Inactive` via `Activate/Deactivate`
(`MarketplaceProfile.cs:47-57`). No Pending/Verified/Suspended/Blocked/Rejected/Closed/Archived/
Removed states exist. (See Provider Status below.)

### 3. Service — active/inactive + immutable snapshots
`IsActive` boolean; creation snapshots service fields into the request at request time
(`ServiceRequest.Create` copies Title/Category/Price/... snapshots, `ServiceRequest.cs:34-75`).

### 4. Trust badge — pure derived function (no state machine)
`ComputeBadge()` from `TotalReviews`/`AverageRating` (`ProviderTrustAggregate.cs:195-210`):
`New (<5)`, `Rising (≥4.0, ≥5)`, `Trusted (≥4.3, ≥20)`, `TopRated (≥4.6, ≥50)`, `Standard`.

### 5. Evidence classification — server-determined
`EvidenceType` (`SelfReported=0`, `PosVerified=1`), set by backend verification only
(`EvidenceType.cs`; `ServiceReviewService.cs:170-206`).

---

## Provider Status — Implementation Reality

The prompt lists Pending/Verified/Active/Suspended/Blocked/Rejected/Closed/Archived/Removed.
**None of these are implemented as a provider status enum or state machine.** What is actually
implemented:

| Prompt state | Implementation reality | Evidence |
|---|---|---|
| Pending | No pending state. Profile is `Active` immediately on create. | `MarketplaceProfile.Create` sets `IsActive = true` (`MarketplaceProfile.cs:30`) |
| Verified | No verification status. "Verification" exists only for *marketing evidence* (`EvidenceType`), not the provider. | `MarketingReviewEvidence.cs:42-45` |
| Active | `IsActive == true` (the only "active"). | `MarketplaceProfile.cs:17` |
| Suspended | No provider suspend. *Tenant vendor-governance* suspension hides services/blocks requests (owned elsewhere). | `MarketplaceSearchService.cs:215-219`; `ServiceRequestService.cs:78-82` |
| Blocked | No provider block; only tenant `Banned` governance status affects visibility. | same as above |
| Rejected | No provider reject. `Rejected` is a *request* status only. | `ServiceRequestStatus.cs:18` |
| Closed / Archived / Removed | No such states. Soft-delete via `IsDeleted` (global `TenantEntity` pattern) — not a status. | `MarketplaceProfileService` uses `!p.IsDeleted` |

So "Provider Status" in this runtime = a single `IsActive` boolean + soft-delete, plus
*indirect* visibility governed by tenant Vendor status. Any richer provider lifecycle lives in the
Governance/Participation runtime, not here.

---

## Service Runtime

- **Definition:** `Service.Create(tenantId, profileId, title, description, category, price,
  pricingType, currency, images, packages)` (`Service.cs:28-57`). Owner = `ProfileId`.
- **Pricing:** `StartingPrice?`, `PricingType?` (Fixed/Hourly/Package), `Currency` (ISO 4217).
  Display-only; backend never computes prices (`PricingType.cs:5-6`, `Service.cs:17-19`). If a
  price is set, `PricingType` and `Currency` are required and currency must be 3 upper-case
  letters (`ServiceCatalogService.cs:289-308`).
- **Availability:** the boolean `IsActive` (`Service.cs:15`). No calendar/slots.
- **Capabilities:** free-text `Skills` JSON array on the profile (`MarketplaceProfile.cs:15`), not
  on the service.
- **Packages:** JSON array column `Packages` on `Service` (`Service.cs:22`); validated as a JSON
  array, no server-computed semantics (`ServiceCatalogService.cs:346-384`).
- **Media:** JSON array `ServiceImages` of *tenant-owned storage keys*
  (`marketplace/services/...`), never absolute URLs (`ServiceCatalogService.cs:386-396`).
- **Evidence (campaign):** not on the service; on the *review* for `Category == "Marketing"`
  (`ServiceReviewService.cs:65-102`).
- **Publishing:** implicit. Creating/activating a service converges the global ES document
  (`ServiceDiscoveryProjectionConsumer.cs`). No explicit "publish" action distinct from create/
  activate.

---

## Engagement Runtime

`ServiceRequest` is the engagement aggregate. Methods: `Accept`, `Reject`, `MarkCompletedByProvider`,
`ConfirmCompletedByBuyer`, `Cancel` (`ServiceRequest.cs`). Authority:
- Accept/Reject/MarkCompleted: only `ProviderUserId` (`ServiceRequestService.cs:224-278`).
- ConfirmCompleted: only `BuyerUserId` (`:288-317`).
- Cancel: either participant while Pending/Accepted (`:319-343`).
- Request entry gated: no self-request, no duplicate active request, tenant must be active and not
  vendor-suspended (`GetRequestEntryAuthorityAsync`, `:41-108`; `RequestServiceAsync`, `:110-212`).
- `CanEnableChat()` = `AllowsInteraction()` = Accepted or CompletedByProvider
  (`ServiceRequest.cs:128-142`). Chat is disabled in Pending/Rejected/Cancelled/CompletedByBuyer.
- `CanBeReviewed()` = CompletedByBuyer only (`:133-136`).
- Negotiation: there is **no explicit negotiation state machine**; "negotiation" is the chat
  conversation under an Accepted (or Pending, pre-accept) request. The request message body carries
  free text.
- Dispute: **not implemented** as a state. There is no dispute state/flag in the model.

---

## Trust Runtime

- **Trust Badge:** derived `ComputeBadge()` (`ProviderTrustAggregate.cs:195-210`).
- **Ratings:** `ServiceReview.Rating` 1–5; exactly one per request; immutable
  (`ServiceReview.cs:27-51`; interceptor `:58-65`).
- **Reviews:** created only after `CompletedByBuyer`; marketing category forces evidence
  (`ServiceReviewService.cs:56-73`).
- **Campaign Evidence:** `MarketingReviewEvidence`, server-classified `PosVerified`/`SelfReported`,
  server-computed `OrdersCount`/`RevenueAmount` from POS orders in the campaign window
  (`ServiceReviewService.cs:139-207`; `MarketingReviewEvidence.cs`).
- **Provider Reputation:** `ProviderTrustAggregate` — `AverageRating`, star distribution,
  `MarketingWeightedScore = Stars*0.5 + log(Revenue+1)*0.3 + log(Orders+1)*0.2`
  (`ProviderTrustAggregate.cs:151-164`). Recomputed from SQL truth in `ProviderTrustService`
  (`ComposeSqlTrustAggregateAsync`, `:114-168`) and incrementally in `ProviderTrustConsumer`
  (`:90-122`).
- **Verification:** of *evidence*, server-only (`EvidenceType`). No provider KYC/verification
  status exists in this runtime.

---

## Chat Runtime

- **Conversation:** `ServiceRequestMessage` where `ServiceRequestId == ConversationId` (1:1)
  (`ServiceRequestMessage.cs:9-13`). Messages are create-only, never updated/deleted.
- **Messages:** text + optional attachment (tenant-scoped storage key, never a URL)
  (`ServiceRequestMessage.cs:27-68`, `:70-91`). System messages sent on lifecycle transitions
  (`ServiceRequestService.cs:236`, `:281`, `:312`, `:340`).
- **Attachments:** validated as server-issued tenant-scoped object keys
  (`MarketplaceChatAttachmentKey.TryNormalizeTenantScopedObjectKey`); absolute URLs rejected
  (`ChatService.cs:116-148`). Max 10 MB (`MarketplaceController.cs:27`).
- **Realtime:** SignalR `ChatHub`. Server-authoritative tenant from JWT claims; cookie auth only;
  periodic re-auth every 15 min; join/leave SignalR group `sr:{requestId}`; broadcast
  `ReceiveMessage` on send (`ChatHub.cs:73-163`, `:248-259`).
- **History:** cursor-paginated `GetMessagesAsync` (newest-first window, deterministic
  `CreatedAt/Id` cursor) — bounded, never unbounded load (`ChatService.cs:270-327`;
  `IChatService.cs:21-22`).
- **Notifications:** **not implemented** as a chat feature. SignalR is delivery-only; there is no
  inbox/notification model inside this runtime. (Notifications runtime is external.)
- **Presence:** **not implemented.** No presence/typing methods exist; only connection metrics
  (`ChatHub.cs:199-214` increments `ChatSignalRConnections`). No `Typing`/`Presence` signal.
- **Connection Status:** implicit via SignalR `OnConnected/OnDisconnected` counters only
  (`ChatHub.cs:199-214`). No persisted connection-status entity.

---

## Financial Runtime

**There is no financial runtime inside the Service Marketplace.** Concretely:
- No `Payment`, `Escrow`, `Settlement`, `Invoice`, `Commission`, `Fee` entity or service in this
  bounded context (repo-wide search across service-marketplace entities: 0 hits).
- `PricingType`/`StartingPrice`/`Currency`/`Packages` are **display metadata** on `Service`
  (`PricingType.cs:5-6`); the backend never prices, quotes, charges, or settles.
- The only money-adjacent code is *read-only* aggregation of a provider's own POS `Order.Total`
  during the campaign window to self-verify marketing evidence — best-effort, falls back to
  `SelfReported`, and never writes orders or money (`ServiceReviewService.cs:139-207`).
- All real payments/settlement/commissions belong to the Payments / CommerceHub Finance /
  Affiliate payout runtimes (separate), which this runtime does not call.

Conclusion: the runtime is **financially inert by design**. It coordinates service work and trust,
not money.

---

## Identity Runtime Integration

- **Authentication:** delegated to POS Identity. Marketplace reads `UserId` (NameIdentifier) and
  `tenant_id` claims (`MarketplaceController` `GetUserId()`; `ChatHub.cs:248-259`). `superadmin`
  actors are excluded from the workflow (`service-marketplace-frontend-final-certification-findings.md:33-39`).
- **Authorization:** per-action `userId/tenantId` checks against the owned entity
  (`MarketplaceProfileService.cs:47-48`, `ServiceCatalogService.cs:125-126`,
  `ServiceRequestService.cs:224-330`, `ServiceReviewService.cs:50-57`). Buyer/provider capability
  is derived from request participant identity + lifecycle state, never from client flags
  (`MarketplaceController.cs:489-527`).
- **Tenant Resolution:** `ITenantContext.TenantId` is the server-authoritative scope; never
  client-supplied for writes, and re-checked under locks (`ChatService.cs:88-91`;
  `ServiceRequestService.cs:112`).
- **Provider Identity:** `MarketplaceProfile` bound to `UserId`+`TenantId` (see Identity section).
- **Consumer Identity:** the POS `User` (`BuyerUserId`); no separate consumer identity.

---

## Orders Runtime Integration

- The marketplace **observes** Orders only to verify marketing evidence, and only the *provider's
  own tenant* orders, read-only (`ServiceReviewService.cs:155-168`). It never creates, mutates, or
  owns orders, customers, refunds, or fulfillment.
- Customers: referenced by `ReviewerUserId`/`BuyerUserId` for display only
  (`MarketplaceController.cs:199-218`); no customer ownership.
- CommerceHub / Affiliate / Payments / Notifications / Analytics / Governance: see Cross-Runtime
  Authority Graph. This runtime is **not** the CommerceHub product marketplace and does not feed
  its order/payout pipeline.

---

## Persistence Runtime

- **SQL (authoritative write + read truth):** PostgreSQL via EF Core `ApplicationDbContext`. All
  aggregates are `TenantEntity` (`MarketplaceProfile`, `Service`, `ServiceRequest`,
  `ServiceRequestMessage`, `ServiceReview`, `MarketingReviewEvidence`, `ProviderTrustAggregate`,
  `ProcessedTrustEvent`). Confirmed by `TenantEntity.cs` base and the entity declarations.
- **Mongo:** **none** for this runtime. (Mongo is used by the *CommerceHub product* read models
  under `PointOfSale.Infrastructure/MongoDB/Services/Marketplace/...` — a different subsystem;
  search for ServiceMarketplace/ServiceRequest/MarketplaceProfile/ProviderTrust in MongoDB returned
  0 hits.)
- **Redis:** **none** for this runtime. (Redis cache is for `MarketplaceReadController`/CommerceHub
  product read — different subsystem.)
- **Realtime:** SignalR (in-memory groups per `sr:{requestId}`), delivery-only
  (`ChatHub.cs:91-93`).
- **Search:** Elasticsearch index `marketplace_services` — **derivative read model** converged
  from SQL by `ServiceDiscoveryProjectionConsumer` and re-validated per query
  (`MarketplaceSearchService.cs`).
- **Media:** R2/object storage referenced by tenant-scoped keys; marketplace stores keys, not URLs
  (`ServiceCatalogService.cs:386-396`; `ServiceRequestMessage.cs:19-21`).
- **Read Models:** Elasticsearch document + SQL-derived trust/service aggregates
  (`ProviderTrustService.ComposeSqlTrustAggregateAsync`). No separate read DB.
- **Write Models:** the `TenantEntity` SQL rows (authoritative).

### CQRS
- **Commands:** `ServiceCatalogService`, `ServiceRequestService`, `ServiceReviewService`,
  `MarketplaceProfileService`, `ChatService` mutate SQL directly in `Serializable` transactions
  with `FOR UPDATE` locks.
- **Queries:** `ProviderTrustService`, `MarketplaceSearchService`, controller read endpoints —
  recomposed from current SQL authority (never trust a stale projection as truth).
- **Events (outbox):** EF outbox via `IEventPublisher.PublishAsync` *before* `SaveChangesAsync`
  so the entity and the outbox row commit atomically:
  - `ServiceCreatedEvent`, `ServiceUpdatedEvent`, `ServiceActivationChangedEvent`
    (`ServiceCatalogService.cs:70-92`, `:138-154`, `:224-233`, `:256-265`)
  - `ServiceReviewCreatedEvent` (`ServiceReviewService.cs:104-124`)
  - `ServiceRequestCompletedEvent` (`ServiceRequestService.cs:303-310`)
  - `ProviderTrustUpdatedEvent` (`ProviderTrustConsumer.cs:106-122`)
- **Outbox:** MassTransit `UseBusOutbox` EF outbox (per `ServiceCatalogService.cs:70-74`
  comment). DB-level unique-constraint `UX_ServiceRequests_Active_Service_Buyer` enforces
  one-active-request and provides idempotent conflict detection
  (`ServiceRequestService.cs:19`, `:194-203`).
- **Inbox / idempotency:** `ProcessedTrustEvent` (tenant-scoped) marks each
  `ServiceReviewCreatedEvent` as processed so trust is applied exactly once
  (`ProviderTrustConsumer.cs:57-70`, `:97-104`; `ProcessedTrustEvent.cs`).
- **Replay:** `ServiceDiscoveryProjectionConsumer` treats events as triggers and re-loads live SQL
  authority before upsert/delete (`LoadLiveServiceDocumentAsync`, `:289-326`); stale docs are
  deleted if SQL authority is gone. `ProviderTrustAggregate.Reconcile` force-recomputes from
  source (`ProviderTrustAggregate.cs:169-193`).
- **Repair:** `ElasticsearchDriftReconciliationJob` (referenced in search files) reconciles ES
  against SQL; `ProcessedTrustEventsCleanupJob` prunes idempotency markers.
- **Projections:** Elasticsearch `marketplace_services` (search + trust + stats) and the
  SQL-derived trust/compliance aggregates. Fail-closed: if ES is unavailable, search throws and the
  caller returns 503 ("Marketplace search is temporarily unavailable")
  (`MarketplaceSearchService.cs:62-70`, `MarketplaceController.cs:283-295`).

---

## Runtime Authority Matrix

| Artifact | Authoritative owner | Cross-tenant? | Evidence |
|---|---|---|---|
| MarketplaceProfile (provider) | Service Marketplace | No (tenant-scoped) | `MarketplaceProfile.cs:7` |
| Service | Service Marketplace | No (tenant-scoped) | `Service.cs:9` |
| ServiceRequest | Service Marketplace | No (request.TenantId = provider tenant) | `ServiceRequest.cs:9`, `ServiceRequestService.cs:120-135` |
| ServiceRequestMessage | Service Marketplace | No | `ServiceRequestMessage.cs:11` |
| ServiceReview | Service Marketplace | No | `ServiceReview.cs:7` |
| MarketingReviewEvidence | Service Marketplace | No | `MarketingReviewEvidence.cs:20` |
| ProviderTrustAggregate | Service Marketplace | No | `ProviderTrustAggregate.cs:10` |
| Service search visibility | Service Marketplace (ES read model) | **Yes (global, read-only)** | `MarketplaceSearchService.cs:344-345` |
| Tenant / governance | Platform/Governance | n/a | `ServiceRequestService.cs:71-86` |
| Orders / Payments | Orders/Payments | n/a | `ServiceReviewService.cs:155-168` (read-only) |

The single cross-tenant surface is **discovery visibility only**. Every write and every
shown/hidden decision for engagement, chat, review, and trust is pinned to the provider's tenant.

---

## Cross-Runtime Authority Graph

- **Identity:** provides `UserId`/`tenant_id` claims; marketplace consumes them. (Marketplace owns
  no auth.)
- **Notifications:** not owned here; SignalR chat is delivery-only. Notification delivery is an
  external runtime.
- **Media (R2/blob):** marketplace stores tenant-scoped keys; the media runtime owns the bytes.
- **Analytics:** derivative from SQL truth in-controller; no separate analytics store
  (`MarketplaceController.cs:141-170`).
- **CommerceHub (product marketplace):** **separate subsystem** (`MarketplaceReadController`,
  Mongo, Redis, CommerceHub finance). The Service Marketplace does **not** integrate with
  CommerceHub order/payout pipelines; it only shares the Elasticsearch *naming* convention and the
  global-visibility idea. Do not conflate the two.
- **Affiliate:** separate participation runtime (global affiliate + store-scoped money). No
  integration with Service Marketplace.
- **Payments:** not integrated. (No payment calls anywhere in this runtime.)
- **Recovery:** `ElasticsearchDriftReconciliationJob`, `ProcessedTrustEventsCleanupJob` are the
  repair paths.
- **Builder / Search:** Elasticsearch is the search runtime; converged, not sourced, by this
  runtime.
- **Governance:** tenant Vendor `Suspended`/`Banned` status (owned by Governance/Participation
  runtime) is *read* to gate provider visibility and new requests.

---

## Business Invariants (Implementation-Backed)

1. **A Provider must** be a `MarketplaceProfile` owned 1:1 by a `UserId` in the tenant; one profile
   per user (`MarketplaceProfileService.cs:25-29`).
2. **A Service must** belong to an active profile in an active, non-suspended tenant
   (`ServiceCatalogService.cs:163-187`).
3. **A Request must** have `BuyerUserId != ProviderUserId`
   (`ServiceRequest.cs:48-49`; `ServiceRequestService.cs:172-173`).
4. **A Request must** be unique-active per (tenant, service, buyer):
   `UX_ServiceRequests_Active_Service_Buyer` + `ActiveServiceRequestExists`
   (`ServiceRequestService.cs:19`, `:156-169`, `:194-203`).
5. **A Request transition must** be one of the allowed edges; all others throw
   (`MarketplaceInvariantInterceptor.cs:95-110`; `ServiceRequest.cs` methods).
6. **A Conversation must** be between the two request participants only; tenant must match
   (`ChatService.cs:88-91`, `:102-114`, `:288-291`).
7. **A Message must** be text or a server-issued tenant-scoped attachment key; absolute URLs
   rejected (`ServiceRequestMessage.cs:36-54`; `ChatService.cs:116-148`).
8. **Evidence must** be present iff `Category == "Marketing"`; forbidden otherwise
   (`ServiceReviewService.cs:65-73`).
9. **Ratings must** be 1–5, one per request, created only after `CompletedByBuyer`, immutable
   (`ServiceReview.cs:27-51`; `ServiceReviewService.cs:50-63`; interceptor `:58-65`).
10. **Trust must** be derived from review SQL truth; badging is a pure function; the aggregate is
    optimistic-concurrency guarded and idempotent per review (`ProviderTrustAggregate.cs`,
    `ProviderTrustConsumer.cs:57-122`).
11. **Verification (evidence) must** be server-determined; `OrdersCount`/`RevenueAmount` are
    server-computed, never client-supplied (`MarketingReviewEvidence.cs:13-18`, `:86-119`;
    `ServiceReviewService.cs:139-207`).
12. **Visibility must** recompute from current SQL tenant/profile/governance authority at every
    read; ES is never trusted as truth (`MarketplaceSearchService.cs:114-235`,
    `:344-345`; `ServiceDiscoveryProjectionConsumer.cs:289-326`).

---

## Runtime Timeline — One Complete Lifecycle (Step Authority Matrix)

| Step | Owner | Authority | Sync | Persistence | Consistency | Recovery | Retry | Failure | Events |
|---|---|---|---|---|---|---|---|---|---|
| Profile created | MarketplaceProfileService | `UserId`==owner | Sync | SQL insert | Tenant-scoped | n/a | n/a | dup → `ProfileAlreadyExists` | — |
| Service created | ServiceCatalogService | profile `UserId` | Sync | SQL + Outbox | Atomic (outbox) | replay consumer | — | validation ex | `ServiceCreatedEvent` |
| Service indexed (ES) | ServiceDiscoveryProjectionConsumer | SQL authority | Async | ES upsert | Converged from SQL | delete if SQL gone | MassTransit retry | throw → retry | (trigger) |
| Consumer discovers | MarketplaceSearchService | tenant recompute | Sync (ES+SQL) | ES candidates + SQL authority | Fail-closed 503 if ES down | drift job | — | 503 | — |
| Request submitted | ServiceRequestService | buyer; tenant active | Serializable tx | SQL insert | `FOR UPDATE`+unique | unique conflict → ex | — | dup/self → ex | — |
| Provider accepts | ServiceRequestService | `ProviderUserId` | Serializable tx | SQL update + system msg | interceptor-guarded | — | — | unauthorized → ex | — |
| Chat starts | ChatService/ChatHub | participants; `AllowsInteraction` | Sync (SQL) + SignalR broadcast | SQL message | tenant recheck under lock | — | rate-limit 60/h | 429/403 | — |
| Provider delivers | ServiceRequestService | `ProviderUserId` | Serializable tx | SQL update | guarded | — | — | — | — |
| Buyer confirms | ServiceRequestService | `BuyerUserId` | Serializable tx | SQL update | guarded | — | — | — | `ServiceRequestCompletedEvent` |
| Review submitted | ServiceReviewService | `BuyerUserId`, `CompletedByBuyer` | Sync | SQL + Outbox + evidence | Atomic; marketing enforced | — | — | `ReviewAlreadyExists`/Forbidden | `ServiceReviewCreatedEvent` |
| Trust updated | ProviderTrustConsumer | SQL review truth | Async | SQL aggregate + `ProcessedTrustEvent` | Optimistic (3 retries) + idempotent | `Reconcile` | 3 attempts/100ms | concurrency → retry/throw | `ProviderTrustUpdatedEvent` |
| Trust projected | ServiceDiscoveryProjectionConsumer | SQL authority | Async | ES trust update (version-guarded) | noop if stale | replay | MassTransit | warn+continue | (trigger) |

---

## Runtime Classification

This runtime is fundamentally a **Service Participation + Engagement + Trust Runtime (single-tenant,
globally discoverable, financially inert).** Evidence:

- **Participation:** a user becomes a provider by creating a `MarketplaceProfile` (1:1 with
  `UserId`); the tenant is the container, not the provider (`MarketplaceProfile.cs`,
  `MarketplaceProfileService.cs`).
- **Engagement:** `ServiceRequest` is the authoritative request/accept/deliver/complete aggregate
  with a hard-guarded state machine (`ServiceRequest.cs`, `MarketplaceInvariantInterceptor.cs`).
- **Collaboration:** `ServiceRequestMessage` chat + SignalR delivery, strictly between the two
  participants, tenant-pinned (`ChatService.cs`, `ChatHub.cs`).
- **Trust:** derived `ProviderTrustAggregate` + `TrustBadge` + server-classified evidence
  (`ProviderTrustAggregate.cs`, `ServiceReviewService.cs`).
- **Single-tenant writes:** every aggregate is `TenantEntity`; every mutation re-checks
  `ITenantContext.TenantId` against the row's `TenantId` (`ServiceRequestService.cs:112`,
  `ChatService.cs:88-91`).
- **Globally discoverable reads only:** ES tenant filter deliberately removed for visibility
  (`MarketplaceSearchService.cs:344-345`), but engagement/chat/review/trust stay tenant-scoped.
- **Not** a Marketplace (CommerceHub product) runtime, **not** a financial/escrow runtime, **not** a
  cross-tenant B2B commerce runtime, **not** a provider-governance runtime (that is the
  Participation/Governance runtime which this one reads).

It is best described as an **intra-tenant service-exchange runtime**: it lets one tenant's users
serve each other, with global findability of their offerings, while no money, no tenant authority,
and no cross-tenant write ever changes hands.

---

## Final Architecture Answer

> How does Shopixy let one independent POS Admin tenant provide professional services to another
> independent POS Admin tenant while preserving tenant autonomy, authority boundaries, coordinating
> requests/collaboration/communication/trust/delivery, without one tenant becoming authority over
> another?

**Implementation-corrected answer:** The Service Marketplace does **not** connect *two independent
tenants* in the B2B sense assumed by the prompt. It operates **inside a single POS Admin tenant**:
the "provider" is a `MarketplaceProfile` owned by one of that tenant's `User`s, and the "consumer"
is another `User` in the *same* tenant (`MarketplaceProfile.cs:7-21`, `ServiceRequest.cs:11-14`).
The tenant boundary is therefore never crossed by a write — `MarketplaceProfile.TenantId`,
`Service.TenantId`, and `ServiceRequest.TenantId` are all the *same* tenant, and every mutation
re-validates `ITenantContext.TenantId` against the row
(`MarketplaceProfileService.cs:23`, `ServiceRequestService.cs:112`, `ChatService.cs:88-91`).

What the prompt calls "another independent POS Admin tenant" is, in implementation, **global
discoverability of a tenant's services**: the Elasticsearch `marketplace_services` index is
intentionally tenant-unscoped for *search candidates* so any tenant's services can be found from
anywhere (`MarketplaceSearchService.cs:344-345`). But this visibility is **read-only and
re-derived**: before any service is shown, the runtime recomposes it from current SQL tenant/
profile/governance authority, and a suspended/banned vendor tenant hides its services
(`MarketplaceSearchService.cs:114-235`, `:215-219`). Discovery never grants another tenant write
authority.

Coordination is achieved by:
- **Requests:** a tenant-scoped `ServiceRequest` state machine with immutable snapshots and a DB
  unique constraint preventing duplicate active requests (`ServiceRequest.cs`,
  `MarketplaceInvariantInterceptor.cs`, `ServiceRequestService.cs:156-203`).
- **Collaboration/communication:** `ServiceRequestMessage` chat (1:1 with the request), SignalR
  delivery-only realtime, participant- and tenant-checked, rate-limited, attachment-key-validated
  (`ChatService.cs`, `ChatHub.cs`, `ServiceRequestMessage.cs`).
- **Trust/delivery:** reviews are 1-per-request, immutable, gated on `CompletedByBuyer`; trust is
  derived (never set) with idempotent, optimistic-concurrency-guarded aggregation; marketing
  evidence is server-verified against the provider's own POS orders
  (`ServiceReviewService.cs`, `ProviderTrustConsumer.cs`, `ProviderTrustAggregate.cs`).
- **Authority boundaries:** no payments, escrow, settlement, or commissions exist here
  (financially inert); tenant governance (vendor suspend/ban) is *read* to gate visibility, not
  owned; orders are *observed read-only* for evidence only.

So the runtime preserves tenant autonomy by (a) scoping every authoritative write to one tenant,
(b) making the only cross-tenant surface a *global, recomputed, fail-closed read index*, and
(c) owning no money or commerce truth. One tenant never becomes the authority over another because
the marketplace simply never lets a write cross a tenant boundary — the provider and consumer are
peers inside the *same* tenant, and "cross-tenant" reach is limited to findability of publicly
published service listings.

---

## Documentation Validation

Assumptions in the prompt vs. implementation:

1. **"independent POS Admin tenant provides services to another independent POS Admin tenant"**
   → **Incorrect.** Provider and consumer are users *inside the same* tenant. Cross-tenant reach is
   *discovery visibility only* (global ES index). Evidence: `ServiceRequest.TenantId` equals the
   provider's tenant; buyer/provider are `User` rows in that tenant (`ServiceRequest.cs:9-14`,
   `ServiceRequestService.cs:120-135`). Replace with: intra-tenant service exchange with
   globally-discoverable listings.

2. **"Provider = Tenant / Projection / External Entity"** → **Incorrect.** Provider =
   `MarketplaceProfile`, a user-level `TenantEntity` (not the tenant, not external, not a
   projection). Evidence: `MarketplaceProfile.cs:1-33`. Replace with: provider is a profile owned
   1:1 by a `UserId`.

3. **"Service Consumer = separate identity/entity"** → **Incorrect.** There is no buyer entity;
   the consumer is `ServiceRequest.BuyerUserId` (a `User`). Evidence: `ServiceRequest.cs:12`.

4. **"Provider lifecycle: Pending/Verified/Active/Suspended/Blocked/Rejected/Closed/Archived/
   Removed"** → **Incorrect.** No such enum/state machine exists for providers. Only `IsActive`
   boolean + soft-delete; "suspension" is tenant Vendor governance (owned elsewhere). Evidence:
   `MarketplaceProfile.cs:17,47-57`; `MarketplaceSearchService.cs:215-219`. Replace with:
   provider status = `IsActive` + tenant governance visibility.

5. **"Service Categories" as a managed taxonomy** → **Incorrect.** `Category` is a free-form
   `string?` on `Service`; no category entity/FK. Only `"Marketing"` is special. Evidence:
   `Service.cs:14`; `ServiceReviewService.cs:213-214`.

6. **"Pricing … backend computes prices / Financial Runtime (Escrow/Settlement/Commissions/Fees)"**
   → **Incorrect.** `PricingType` is display-only; no pricing/escrow/settlement/commission/fee code
   exists in this runtime. Evidence: `PricingType.cs:5-6`; repo-wide 0 hits for
   `Escrow|Settlement|Commission` in service-marketplace entities. Replace with: financially inert;
   pricing is display metadata.

7. **"Chat Runtime: Notifications / Presence / Typing / Connection Status"** → **Partially
   incorrect.** SignalR supports join/leave/send/getMessages + connection counters only. No
   presence/typing/notification model. Evidence: `ChatHub.cs` (no Typing/Presence methods;
   `OnConnected/OnDisconnected` only increment a counter, `:199-214`). Replace with: delivery-only
   realtime; presence/typing/notifications not implemented.

8. **"Persistence: Mongo / Redis owned by this runtime"** → **Incorrect.** This runtime uses SQL
   (authoritative) + Elasticsearch (read model) only. Mongo/Redis belong to the *CommerceHub
   product* `MarketplaceReadController` subsystem. Evidence: MongoDB search for
   ServiceMarketplace/ServiceRequest/MarketplaceProfile/ProviderTrust = 0 hits;
   `MarketplaceReadController.cs:16-22` imports CommerceHub Mongo/Redis.

9. **"CommerceHub Marketplace = Service Marketplace"** → **Incorrect.** These are two subsystems.
   `MarketplaceReadController`/Mongo/Redis/CommerceHub finance are the *product* marketplace; the
   Service Marketplace is `MarketplaceController` + `TenantEntity` services/requests/chat/trust.
   Evidence: `MarketplaceController.cs` vs `MarketplaceReadController.cs` imports.

10. **"Verification = provider KYC/verification status"** → **Incorrect.** "Verification" here means
    `EvidenceType` classification of *marketing campaign evidence* (SelfReported/PosVerified),
    server-determined; there is no provider verification status. Evidence:
    `MarketingReviewEvidence.cs:42-45`; `ServiceReviewService.cs:139-207`.

11. **"Dispute" as an engagement state** → **Not implemented.** No dispute state/flag exists.
    Evidence: `ServiceRequestStatus.cs` has no Dispute; `ServiceRequest.cs` has no dispute method.

12. **"Negotiation" as a distinct runtime phase** → **Not a state machine.** Negotiation is free-text
    chat under a request; no dedicated negotiation state. Evidence: `ChatService.cs`,
    `ServiceRequest.cs` (no Negotiation transition).

Every replaced assumption above is now backed by the cited implementation; prior documentation,
controller names, and DTO labels were treated as secondary per the critical rule, and implementation
wins throughout.
