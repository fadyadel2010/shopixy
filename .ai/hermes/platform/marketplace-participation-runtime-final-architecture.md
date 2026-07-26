# Marketplace Participation Runtime — Final Canonical Architecture

Implementation-backed architecture specification for the runtime that governs how an
independent POS Admin tenant participates in the centralized CommerceHub Marketplace.

> Every statement in this document is derived from the C# implementation under
> `PointOfSale.API/`, `PointOfSale.Core/`, and `PointOfSale.Infrastructure/`. Where a
> conclusion is supported, the `file:line` evidence is cited. The CommerceHub canonical
> doc (`.ai/hermes/platform/commercehub-final-architecture.md`) and the vendor
> certification findings in `docs/architecture/` were used only for vocabulary; every
> normative claim here was re-verified against code. Prior markdown, controller names,
> DTO names, and UI labels are treated as secondary and never authoritative.

Canonical paths:
- Vendor authority boundary: `PointOfSale.Infrastructure/Reliability/CommerceHubVendorAuthority.cs`
- Governance profile (the authoritative vendor record): `PointOfSale.Core/Entities/UserGovernanceProfile.cs`
- Governance service: `PointOfSale.API/Services/Governance/UserGovernanceService*.cs`
- Vendor visibility: `PointOfSale.API/Services/CommerceHub/CommerceHubVendorVisibilityService.cs`
- Lifecycle resolver (session DTO): `PointOfSale.API/Controllers/AuthController.cs:125-200`
- Enums: `PointOfSale.Core/Enums/UserGovernanceStatus.cs`, `UserGovernanceRole.cs`, `UserGovernanceRiskLevel.cs`
- SuperAdmin approval/verification: `PointOfSale.API/SuperAdmin/Controllers/SuperAdminUsersController.cs:228`
- Registration: `PointOfSale.API/Services/Platform/TenantRegistrationService.cs:479`

---

## Executive Summary

The "Marketplace Participation Runtime" is **not** a marketplace, a seller-content
runtime, a financial runtime, or a settlement runtime. It is a **Vendor Governance,
Identity, and Participation Runtime**: the bounded context that decides *whether a given
POS Admin tenant is allowed to exist as a CommerceHub marketplace vendor* and keeps that
decision authoritative at every participation gate.

Reverse-engineered facts (all code-backed):

- **There is no `CommerceHubVendor` entity and no vendor marketplace-profile / settings /
  membership table.** A "vendor" is the tenant itself. Vendor identity == `Tenant.Id`.
  Confirmed: zero matches for `class CommerceHubVendor`, `VendorMarketplaceSettings`,
  `VendorProfile`, `MarketplaceSettings` across `PointOfSale.Core/Entities` and
  `PointOfSale.Core/DTOs/Marketplace` (terminal grep, 2026-07-19).
- **Vendor participation state is a set of flags on `UserGovernanceProfile`** scoped to the
  tenant (`SubjectId == Tenant.Id`, `Role == Vendor`): `IsKycVerified`,
  `IsMarketplaceApproved`, `Status` (`UserGovernanceStatus`), `RiskLevel`
  (`UserGovernanceProfile.cs:9-19`). This is the single authoritative "vendor record."
- **Marketplace eligibility is a pure derived predicate** over current SQL:
  `TenantExists && TenantActive && GovernanceProfileExists && GovernanceStatus==Active &&
  IsKycVerified && IsMarketplaceApproved` (`CommerceHubVendorAuthority.cs:16-22`). It is
  recomputed — never cached — at every gate (listing publish, order acceptance, visibility,
  search overlay, reindex).
- **Vendor lifecycle status is a derived STRING, not an enum.** `ResolveVendorLifecycleStatus`
  in `AuthController.cs:158-200` composes `Terminated/Disabled/Blocked/Suspended/
  DebtBlocked/Pending/Active` from the governance snapshot + debt snapshot. There is NO
  `VendorLifecycleStatus` enum and NO `VendorLifecycleXXXEvent`. The broader frontend
  allowlist also includes `Approved`/`Rejected` as presentation-only states
  (`posVendorSessionAuthority.js`), but the backend source of truth is the predicate above.
- **Approval/verification are SuperAdmin-only mutations** that write the governance flags in
  a serializable transaction and cascade to listing visibility
  (`UserGovernanceService.cs:777-815`; `CommerceHubVendorVisibilityService.cs:61-322`).
- **CommerceHub finance, settlement, earnings, payouts, and refunds are NOT owned by this
  runtime.** They are owned by the CommerceHub Finance services (see
  `commercehub-final-architecture.md`). This runtime only *gates* them via eligibility and
  the fail-closed readiness gate.

Runtime classification: **Participation + Governance (Identity) Runtime** — a thin,
SQL-authoritative gate over an existing tenant, coordinated with CommerceHub Finance /
Catalog / Orders / Search but owning none of their truth.

---

## Core Discovery

### What is a Marketplace Vendor?
A CommerceHub marketplace vendor is **the POS Admin tenant itself**. There is no independent
vendor identity row. Identification is by the immutable `Tenant.Id`
(`CommerceHubVendorAuthority.cs:37-47` loads the tenant by `item.Id == vendorId`;
`CommerceHubVendorAuthority.cs:54-65` loads the governance profile by
`item.SubjectId == vendorId && item.Role == UserGovernanceRole.Vendor`).

### What is a POS Admin?
A POS Admin tenant is a row in `Tenants` with its own `Store`, catalog, inventory,
payments, accounting, and (optionally) a `UserGovernanceProfile` of `Role.Vendor`.

### How are they connected?
**Same identity (1:1 projection / binding, not a separate entity).** The tenant `Id` is the
vendor `Id`. The vendor's marketplace state is carried as `UserGovernanceProfile` columns
whose `SubjectId` equals the tenant `Id`. `CommerceHubVendorAuthority.LoadAsync`
(`CommerceHubVendorAuthority.cs:37-75`) is the canonical recomposition: tenant row +
vendor-role governance profile, joined by `SubjectId == Tenant.Id`.

### Are they the same entity?
Yes. Vendor = tenant. There is no shadow object, no mirror, no duplicate. Governance
subject/role are immutable after creation (application save guards + a PostgreSQL trigger,
per `commercehub-vendor-authority-certification-findings.md`; `UserGovernanceProfile.cs`
has no setter that reassigns `SubjectId`/`Role`).

### Identity Mapping
`Tenant.Id` ⇄ `UserGovernanceProfile.SubjectId` (filtered by `Role == Vendor`). This is the
entire "vendor identity mapping." Nothing else exists.

---

## Runtime Purpose

The runtime exists to **authorize marketplace participation**: it answers, at every gate,
"is this tenant currently permitted to act as a CommerceHub vendor?" It converts
tenant-registration intent into a governed, SuperAdmin-certified marketplace-participation
state, and it refuses participation the instant that state becomes invalid — even if stale
listings, caches, search docs, or sessions say otherwise.

It does NOT create marketplace commerce truth. It is a guardrail over the tenant, not a
content/seller runtime.

Evidence: every participation path re-runs `CommerceHubVendorAuthority.LoadAsync`
(`PublishToCommerceHubHandler:162` via `EnsureVendorListingAllowedAsync`;
`CommerceHubVendorVisibilityService.cs:222`; `AuthController.cs:125`).

---

## Runtime Owns (Authoritative)

Implemented runtime truth owned by the participation/governance runtime (SQL):

| Concept | Owner (service) | SQL table / entity |
|---|---|---|
| Vendor identity (tenant) | Tenant services (`ITenantContext`) | `Tenants` |
| Vendor governance profile | `UserGovernanceService` | `UserGovernanceProfiles` |
| KYC verification state | SuperAdmin → `UserGovernanceService` | `UserGovernanceProfiles.IsKycVerified` + audit |
| Marketplace approval state | SuperAdmin → `UserGovernanceService` | `UserGovernanceProfiles.IsMarketplaceApproved` |
| Vendor governance status | SuperAdmin → `UserGovernanceService` | `UserGovernanceProfiles.Status` (`Active/Suspended/Banned/FinanciallyLocked`) |
| Vendor risk level | Governance service | `UserGovernanceProfiles.RiskLevel` |
| Marketplace eligibility (derived) | `CommerceHubVendorAuthority` | predicate over the above |
| Vendor lifecycle status (derived) | `AuthController.ResolveVendorLifecycleStatus` | STRING over governance + debt |
| Listing visibility trigger (governance-driven) | `CommerceHubVendorVisibilityService` | writes `CommerceHubListings` visibility flags |
| Suspension/ban → listing hide cascade | `CommerceHubVendorVisibilityService` | `CommerceHubListings` + search auto-heal queue |

What "ownership" means here: this runtime *writes governance flags and derives the
eligibility/visibility decision*. It owns the **participation gate**, not the marketplace
data behind it.

---

## Runtime Does NOT Own

Reverse-engineered from service dependencies and guards:

- **Marketplace listings** — owned by CommerceHub catalog (`CommerceHubListings`); this
  runtime only gates publish via `EnsureVendorListingAllowedAsync`
  (`CommerceHubCommandHandlers.cs:162`).
- **Marketplace orders / sub-orders** — owned by `CommerceHubCheckoutService` /
  `CommerceHubOrderManagementService`; this runtime only gates order acceptance
  (`UserGovernanceService.EnsureVendorOrderAcceptanceAllowedAsync`).
- **Vendor earnings / settlement / payouts / commission / ledger / GL** — owned by CommerceHub
  Finance services (see `commercehub-final-architecture.md`). This runtime does NOT own
  earnings, balance, holdbacks, reserve, risk holds, or payout eligibility truth. It only
  supplies the eligibility gate the readiness gate consults.
- **Payment lifecycle** — owned by `IPaymentLifecycleService`.
- **Inventory / catalog / pricing** — owned by POS Admin; CommerceHub listing references
  existing `Product`/`ProductVariant` (`CommerceHubCommandHandlers.cs:192-215`).
- **Storefront / public store** — presentation; visibility derived from this runtime's
  decision but the surface is owned elsewhere.
- **Accounting / treasury GL** — owned by the shared `AccountingOutboxProcessor`.
- **Shipping** — owned by `ShipmentService`/`CarrierShipmentService`.
- **Platform/global SuperAdmin scope** — SuperAdmin is the *caller* that mutates governance;
  this runtime is not "the SuperAdmin." It is the governed subject surface.

---

## Identity Relationship (POS Admin ↓ Marketplace Vendor)

```
POS Admin tenant (Tenants.Id, Store, catalog, ...)
        │
        │ 1:1 identity binding (SubjectId == Tenant.Id)
        ▼
UserGovernanceProfile (Role = Vendor, SubjectId = Tenant.Id)
        │
        │ participates-in Marketplace
        ▼
CommerceHub marketplace vendor == the SAME tenant (no new identity)
```

Determination: **Same Identity**, enforced by an immutable binding. Not a projection that
can drift, not a shadow object, not a duplicate. The "vendor" is the tenant viewed through
the `Role.Vendor` governance profile. Code: `CommerceHubVendorAuthority.LoadAsync`
(`CommerceHubVendorAuthority.cs:37-75`) loads the tenant and the vendor-role profile
together; the eligibility predicate requires both.

---

## Authority Discovery (who owns what)

| Prompt-assumed artifact | Actual owner (implementation) |
|---|---|
| Store Name / Business Identity / Store Logo / Store Description | POS Admin `Store` (tenant-owned), NOT a CommerceHub vendor artifact |
| Tax Information | Tenant/business config, NOT marketplace-participation-owned |
| Vendor Status | `UserGovernanceProfiles.Status` (governance runtime) |
| Marketplace Membership | Derived `IsMarketplaceApproved` flag on `UserGovernanceProfiles` |
| Vendor Marketplace Profile | DOES NOT EXIST as a table; "profile" = tenant `Store` + governance profile |
| Marketplace Configuration / Settings | DOES NOT EXIST as a vendor table; marketplace settings are CommerceHub-platform config |
| Marketplace Metadata | Listing-level only (`CommerceHubListings.AttributesJson`); no vendor-metadata table |
| Store Configuration | POS Admin `StoreConfig` (Builder/Storefront), NOT participation-owned |
| Currencies | Single tenant `Currency` field (`Tenant.cs:62`, default `"EGP"`); NO separate vendor currency |
| Languages | Tenant/localization config, NOT participation-owned |
| Shipping Settings | Tenant shipping methods; gate-checked at publish (`EnsureTenantHasActiveShippingAsync`, `CommerceHubCommandHandlers.cs:168`) |
| Marketplace Settings | CommerceHub platform-level; not owned by the vendor |

---

## Marketplace Lifecycle (implementation)

The prompt lists a linear lifecycle. Reverse-engineered against code:

```
POS Admin tenant registered
   └─ TenantRegistrationService creates Tenant + owner user
      + UserGovernanceProfile(Role=Vendor) with IsKycVerified=false,
        IsMarketplaceApproved=false  (TenantRegistrationService.cs:479;
        defaults in UserGovernanceProfile.cs:16,19)
        ↓
Marketplace Enrollment (implicit)
   └─ No separate "enrollment" entity. Tenant simply has a Vendor governance profile.
      Eligibility defaults to FALSE (unverified + unapproved).
        ↓
Vendor Approved (SuperAdmin)
   └─ SetVendorMarketplaceApprovalAsync: requires IsKycVerified first
      (else VENDOR_VERIFICATION_REQUIRED, 409)  (UserGovernanceService.cs:789-795)
      then sets IsMarketplaceApproved=true  (UserGovernanceService.cs:801)
        ↓
KYC Verified (SuperAdmin)
   └─ SetVendorVerificationAsync sets IsKycVerified  (UserGovernanceService.cs:777)
      Revoking KYC atomically revokes approval
      (UserGovernanceService.cs:784)
        ↓
Marketplace Configuration
   └─ NOT a vendor concept. Tenant configures Store / products; publishes
      CommerceHub listings (gated by EnsureVendorListingAllowedAsync)
        ↓
Products Published
   └─ CommerceHubListing created, tenant-owned variant referenced
      (CommerceHubCommandHandlers.cs:192-215)
        ↓
Orders Received
   └─ Split checkout groups by StoreId == vendor tenant
      (CommerceHubCheckoutService.cs:1801-1802); order acceptance gated by
      EnsureVendorOrderAcceptanceAllowedAsync
        ↓
Revenue Generated
   └─ CommerceHub Finance owns earnings/ledger (NOT this runtime)
        ↓
Settlement
   └─ CommerceHub Finance / debt service (NOT this runtime)
        ↓
Vendor Suspension
   └─ SuperAdmin sets Status=Suspended → CommerceHubVendorVisibilityService
      hides listings (CommerceHubVendorVisibilityService.cs:77-182)
        ↓
Vendor Reactivation
   └─ Status=Active → RestoreVendorListingsAsync re-derives visibility
      from SQL authority (CommerceHubVendorVisibilityService.cs:184-322)
        ↓
Marketplace Exit
   └─ Tenant deletion/ban (Status=Banned → lifecycle "Blocked"; tenant
      DeletedAtUtc != null → lifecycle "Terminated"). No dedicated "exit"
      entity; derived in ResolveVendorLifecycleStatus (AuthController.cs:158-200)
```

Note: "Vendor Activated" in the prompt maps to reaching `Active` lifecycle status
(KYC + approval both true), not a distinct stored state.

---

## Runtime State Machine

There is **no single stored vendor state machine**. Two layers exist:

1. **Governance state machine (stored)** — `UserGovernanceStatus`:
   `Active=0, Suspended=1, Banned=2, FinanciallyLocked=3`
   (`UserGovernanceStatus.cs:5-8`). Transitions are SuperAdmin mutations serialized in a
   transaction (`UserGovernanceService.cs:777-815`).

2. **Derived lifecycle status (computed per request)** — `ResolveVendorLifecycleStatus`
   (`AuthController.cs:158-200`), precedence:
   - `!TenantExists` → `Terminated`
   - `!TenantActive` → `Disabled`
   - `Status==Banned` → `Blocked`
   - `Status==Suspended` → `Suspended`
   - `debtAuthority.IsSuspendedFromNewOrders` → `DebtBlocked`
   - `Status==FinanciallyLocked` → `Blocked`
   - `!ProfileExists || !IsKycVerified || !IsMarketplaceApproved` → `Pending`
   - else → `Active`

   The frontend also surfaces `Approved`/`Rejected` as presentation states
   (`posVendorSessionAuthority.js`), but these are NOT stored lifecycle states — they are
   governance-flag reflections.

Invariant: approval cannot persist unless verification is current. Demonstrated by
`UserGovernanceService.cs:789-795` (throws if approving an unverified vendor) and by the
eligibility predicate requiring BOTH `IsKycVerified` and `IsMarketplaceApproved`
(`CommerceHubVendorAuthority.cs:16-22`).

---

## Marketplace Status (implementation)

The prompt's status list maps to implementation as follows:

| Prompt status | Implementation |
|---|---|
| Pending | Derived `Pending` when unverified/unapproved (`AuthController.cs:193`) |
| Approved | Presentation state reflecting `IsMarketplaceApproved` (frontend); not a stored lifecycle state |
| Rejected | Presentation only; no stored "rejected vendor" state |
| Suspended | Stored `UserGovernanceStatus.Suspended=1` |
| Hidden | Not a status — a listing-visibility flag set when governance hides listings (`CommerceHubVendorVisibilityService.cs:133 HideByGovernance`) |
| Active | Stored `UserGovernanceStatus.Active=0` AND derived `Active` lifecycle |
| Locked | `UserGovernanceStatus.FinanciallyLocked=3` (→ derived `Blocked`) |
| Archived | No vendor-archive status; products can be archived (`CommerceHubAuditEvents.cs ProductArchivedEvent`) but vendors are not |
| Disabled | Derived when `!TenantActive` (`AuthController.cs:170`) |
| Removed | No vendor-removal status; tenant deletion → derived `Terminated` |

---

## Financial Runtime — Scope Boundary (critical)

The prompt asks to "reverse engineer" vendor earnings, settlement, commission, balance,
holdbacks, reserve, risk holds, payout eligibility, payment requests, payment verification,
financial statements, ledger. **None of these are owned by the Marketplace Participation
Runtime.** They are owned by the CommerceHub Finance services:

- Vendor earnings: `CommerceHubEarningsService` (`CommerceHubVendorEarningStatus`:
  Pending/Available/PaidOut/InPayout/Reversed).
- Settlement / COD debt: `CommerceHubVendorDebtService` (`CommerceHubVendorSettlement`).
- Payouts: `CommerceHubPayoutService` (request) + `SuperAdminPayoutsService` (execution).
- Operational ledger: `CommerceHubFinancialLedgerService` (`CommerceHubFinancialTransaction`).
- GL bridge: `AccountingOutboxProcessor` (`CommerceHubLedgerPosting`).
- Readiness gate (fail-closed outward money): `CommerceHubFinancialReadinessGate`.
- Commission: `CommissionService` (stored on sub-order snapshot).

The Participation Runtime's only financial touchpoint is the **eligibility gate** the
readiness gate and lifecycle resolver consult (`IsDebtBlocked` in
`CommerceHubVendorLifecycleAuthorityDto`, `AuthController.cs:147`). It does NOT compute
balances, does NOT own payout eligibility truth, and does NOT verify payments. See
`commercehub-final-architecture.md` for the financial runtime itself.

---

## Payment / Orders / Earnings / Settlement / Finance Integration

| Peer | Interaction from Participation Runtime |
|---|---|
| Payment Runtime | None (read-only gate via lifecycle `BlocksPayments`) |
| Orders Runtime | Gated: `EnsureVendorOrderAcceptanceAllowedAsync` refuses orders to ineligible vendors |
| Refund Runtime | None directly; refund finance owned by CommerceHub Refund service |
| Commission Runtime | None; commission owned by CommerceHub Finance |
| Settlement Runtime | None; settlement owned by CommerceHub Finance/debt service |
| Accounting Runtime | None; GL owned by shared `AccountingOutboxProcessor` |
| Risk Runtime | `RiskLevel` is a governance column; risk-driven blocking is via governance status |

The participation runtime is a **pure gate** over these peers: it reads their truth only to
decide admission/visibility, it never writes their tables.

---

## Earnings / Finance Runtime Detail

Not owned here. (Pointer: `commercehub-final-architecture.md` § "Financial Runtime" and
§ "Vendor Earnings Runtime".) The only participation-runtime fact: earnings become
`Reversed` on refund by the refund service (`CommerceHubRefundService.cs:1513-1519`), and a
vendor blocked by debt (`DebtBlocked`) is excluded from *new* orders but retains existing
earnings — debt is derived from ledger replay (`CommerceHubVendorDebtService.cs:77-290`),
owned by CommerceHub Finance, not by this runtime.

---

## Marketplace Settings Runtime

**There is no Marketplace Settings runtime or entity owned by the vendor.** The prompt's
"Store Configuration / JSON Configuration / Normalization / Attribute Mapping / Category
Mapping / Publishing Settings / Marketplace Visibility / Capabilities" are:

- Store Configuration → POS Admin `StoreConfig` (Builder/Storefront runtime).
- Attribute Mapping / Category Mapping / Publishing Settings → CommerceHub listing
  publication (`CommerceHubCommandHandlers.cs:162-215`,
  `ValidateAndNormalizeAsync` attribute service).
- Marketplace Visibility → derived from `UserGovernanceProfile.Status` +
  `CommerceHubListings` flags via `CommerceHubVendorVisibilityService`.
- Capabilities → derived from lifecycle status (`BlocksCatalog/BlocksOrders/...` in
  `CommerceHubVendorLifecycleAuthorityDto`, `AuthController.cs:147-153`).

No JSON marketplace-config blob, no normalization table, no vendor attribute-mapping table
was found (terminal grep, 2026-07-19).

---

## Multi Currency

- **Single tenant currency.** `Tenant.Currency` (default `"EGP"`,
  `Tenant.cs:62`). There is NO vendor-level currency, NO settlement-currency vs
  vendor-currency split, NO commission-currency concept for the vendor.
- **All CommerceHub marketplace finance is EGP-only at the money boundaries** (`FinancialCurrencyGuard
  .PlatformBaseCurrencyCode = "EGP"`, enforced at ledger/payout/refund/settlement/search).
  Publication rejects non-EGP tenant currency
  (`CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(tenant.Currency)`,
  `CommerceHubCommandHandlers.cs:178`). Nuance: the `CommerceHubVendorEarning` row itself
  carries NO `Currency` column and NO DB-level EGP check constraint
  (`CommerceHubVendorEarning.cs:18-21`) — its currency is inherited from the parent
  sub-order, whose amount is EGP-validated at creation. EGP enforcement is structural at the
  ledger/settlement/payout/refund boundaries, not on the earning row.
- Exchange authority: none at the vendor level; EGP is the only settlement/display/commission
  currency in CommerceHub marketplace finance.

---

## Governance Runtime Integration

Governance is the *core* of this runtime, not a peer:

- Vendor Suspension / Ban / Activation / Deactivation: SuperAdmin mutations on
  `UserGovernanceProfiles.Status` (`UserGovernanceService.cs:777-815`).
- Vendor Approval / Verification: SuperAdmin-only, serializable, reason-required
  (`UserGovernanceService.cs:777-815`); revoking verification atomically revokes approval
  (`:784`).
- Marketplace Rules / Risk Holds: `RiskLevel` column + governance status; risk holds surface
  as `FinanciallyLocked` → derived `Blocked`.
- Compliance: every governance mutation writes audit context
  (`KycVerifiedByAdminId`, `MarketplaceApprovedByAdminId`, `*Reason`).
- Cascade: governance change → `CommerceHubVendorVisibilityService.SyncVendorVisibilityAsync`
  hides/restores listings + enqueues search auto-heal
  (`CommerceHubVendorVisibilityService.cs:61-407`).

---

## Identity Runtime Integration

- **Vendor identity = tenant identity.** `ITenantContext.RequiredTenantId` resolves the
  tenant; `CommerceHubTenantIsolationGuard.EnsureTenantAccess` enforces scope at publish
  (`CommerceHubCommandHandlers.cs:155`).
- **Authentication / Authorization**: standard POS auth (P1-A SQL user/tenant/session/refresh
  per `commercehub-vendor-authority-certification-findings.md`). Vendor login re-issues the
  lifecycle DTO on every session (`AuthController.cs:120,343,427`).
- **Tenant Resolution**: `CommerceHubVendorAuthority.LoadAsync` recomposes tenant + profile
  from SQL on every gate; X-Tenant-Id is stripped and never trusted from the client.
- **CommerceHub Customer identity**: separate `CommerceHubCustomer` (global, scoped to own
  orders); NOT the vendor.

---

## Orders Runtime Integration

- Marketplace orders: `CommerceHubMarketplaceOrder` + per-`StoreId` `CommerceHubSubOrder`
  (`CommerceHubCheckoutService.cs:1801-1802`); `StoreId` IS the vendor tenant id.
- Vendor Orders / Order Ownership: sub-order scoped by `StoreId` (app-level isolation;
  sub-orders are `IProjectionVersioned` not `ITenantOwned`,
  `commercehub-final-architecture.md` § Business Invariants #11).
- Settlement / Commission / Refund Ownership: CommerceHub Finance, NOT this runtime.
- Order acceptance gate: `EnsureVendorOrderAcceptanceAllowedAsync`.

---

## Catalog Runtime Integration

- Product Publication: `PublishToCommerceHubHandler` creates a `CommerceHubListing`
  referencing an existing `ProductId`/`VariantId`; it does NOT create product truth
  (`CommerceHubCommandHandlers.cs:192-215`).
- Catalog Projection / Inventory Authority / Pricing Authority / Category Authority: all
  owned by POS Admin / CommerceHub catalog. The participation runtime only supplies the
  publish gate (`EnsureVendorListingAllowedAsync`) and the visibility decision.

---

## Storefront Runtime Integration

- Marketplace Storefront / Vendor Storefront / Public Store / Visibility / Discovery /
  Search: presentation surfaces whose visibility is *derived* from this runtime's
  governance decision + SQL authority overlay
  (`CommerceHubSearchSqlAuthorityOverlay`). The participation runtime writes the listing
  visibility flags; it does not own the storefront surface.
- A suspended/banned/unverified/unapproved vendor is hidden from search and storefront via
  the visibility cascade + search overlay, not by any storefront-owned flag.

---

## Event Runtime

**There are NO vendor-named domain events** (terminal grep `PointOfSale.Core/Events/`,
2026-07-19: zero matches for `Vendor*Event` / `Marketplace*Event` as vendor events). The
real event surface relevant to participation:

| Prompt-assumed event | Implementation |
|---|---|
| VendorJoined | NONE — participation is implicit on registration; no event |
| VendorApproved | NONE as an event; it is a governance flag mutation (`IsMarketplaceApproved`) |
| VendorSuspended | NONE as an event; governance `Status` change cascades via `CommerceHubListingUnpublishedEvent` |
| VendorActivated | NONE as an event; derived lifecycle `Active` |
| Marketplace Settings Changed | NONE (no vendor settings entity) |
| Settlement Created | CommerceHub Finance event, NOT participation-owned |
| Payment Requested / Approved / Rejected | CommerceHub Finance events, NOT participation-owned |

The events that actually fire from participation-state changes are **listing events** emitted
by the visibility cascade: `CommerceHubListingUnpublishedEvent`
(`CommerceHubVendorVisibilityService.cs:139-151`) and `CommerceHubListingUpdatedEvent`
(`:277-290`), both `IntegrationEvent`s in `CommerceHubEvents.cs:41,26`. These are catalog
projection events, not "vendor" events.

CQRS note: participation state uses the generic integration-event outbox (`OutboxMessages`)
and the `ProcessedEvents` exactly-once fence (per `commercehub-final-architecture.md` §
Outbox). No dedicated vendor outbox/inbox exists.


---

## Persistence Runtime

- **SQL (system of record):** `Tenants` + `UserGovernanceProfiles` are the only
  participation-authority tables. `CommerceHubVendorAuthority.cs:37-75` reads both
  (no Mongo/Redis/ES involvement in the authority decision).
- **Mongo / Redis / Elasticsearch:** NOT used for vendor identity/eligibility. They hold
  derivative marketplace projections (listings, products, search) whose visibility is
  *written* by this runtime's cascade but whose authority is SQL. Search visibility is
  re-derived from SQL on every read via `CommerceHubSearchSqlAuthorityOverlay`.
- **Read Models / Write Models:** write = governance flags (SQL); read = derived snapshot
  (`CommerceHubVendorAuthoritySnapshot`) recomputed per call.
- **Snapshots:** `CommerceHubVendorAuthoritySnapshot` (`CommerceHubVendorAuthority.cs:7-23`)
  is an in-memory recomputation, not a stored snapshot.
- **Ledger / Financial Storage:** owned by CommerceHub Finance (see § Financial Runtime
  Scope Boundary). This runtime stores none of it.
- **Marketplace Metadata:** not a stored vendor artifact; only listing-level
  `AttributesJson`.

---

## CQRS

- **Commands:** governance mutations (`SetVendorVerificationAsync`,
  `SetVendorMarketplaceApprovalAsync`) — SuperAdmin-authorized, SQL writes
  (`UserGovernanceService.cs:777-815`). These are the only "write" operations this runtime
  owns.
- **Queries:** eligibility (`CommerceHubVendorAuthority.LoadAsync` /
  `LoadEligibleVendorIdsAsync`), lifecycle (`AuthController.ResolveVendorLifecycleStatus`).
  All recompute from SQL; no cached authority.
- **Events:** listing visibility events only (see § Event Runtime). No vendor commands emit
  vendor-domain events.
- **Projections:** none owned by this runtime. It *writes* a visibility flag consumed by the
  CommerceHub listing projection; it does not own a projection.
- **Outbox / Inbox:** uses the shared integration-event outbox (`OutboxMessages`) for the
  listing events it publishes; no dedicated vendor outbox/inbox/replay/repair.
- **Replay / Repair:** if a governance change is missed, the next read recomputes from SQL
  (fail-closed) — replay is unnecessary because the truth is always re-derived. Listing
  visibility repair is driven by the search auto-heal queue
  (`CommerceHubVendorVisibilityService.cs:371-407`).
- **Convergence:** SQL-authoritative by construction; the derived snapshot cannot diverge
  from SQL because it is computed from SQL on every call.

---

## Runtime Authority Matrix

| Concept | Owner (truth) | May mutate | May read | Consumes events | Creates projection | Runtime truth |
|---|---|---|---|---|---|---|
| Vendor identity | Tenant services (`Tenants`) | tenant admin | participation gate (scope) | tenant events | none | SQL |
| Vendor governance profile | `UserGovernanceService` | SuperAdmin + governance | visibility/eligibility gates | governance events | none | SQL |
| KYC verification | SuperAdmin → `UserGovernanceService` | SuperAdmin only | eligibility gate | none | none | SQL |
| Marketplace approval | SuperAdmin → `UserGovernanceService` | SuperAdmin only | eligibility gate | none | none | SQL |
| Vendor status | SuperAdmin → `UserGovernanceService` | SuperAdmin | visibility cascade | governance events | listing visibility flag | SQL |
| Eligibility (derived) | `CommerceHubVendorAuthority` | n/a (derived) | all gates | n/a | n/a | derived-from-SQL |
| Lifecycle status (derived) | `AuthController.ResolveVendorLifecycleStatus` | n/a (derived) | session/frontend | n/a | n/a | derived-from-SQL |
| Listing visibility | `CommerceHubVendorVisibilityService` | governance cascade | search/storefront | governance events | `CommerceHubListings` flag | SQL |
| Vendor earnings/settlement/payout/ledger | CommerceHub Finance | finance + SuperAdmin | vendor/finance | finance events | none | SQL (other runtime) |

---

## Runtime Authority Divergence

- **Finance/settlement/earnings/payout**: owned by CommerceHub Finance, NOT this runtime.
  The participation runtime only supplies the gate (eligibility + `IsDebtBlocked`).
- **Listings/catalog/orders/search**: owned by CommerceHub; this runtime only gates them.
- **Storefront/Builder**: owned by Merchant/Storefront; visibility is derived from this
  runtime's decision but the surface is not.
- **SuperAdmin**: the caller that mutates governance; this runtime is the governed subject,
  not the platform authority itself.

---

## Cross-Runtime Authority Graph

| Peer | Reads from peer | Writes to peer | Events | Runtime authority | Projection | Ownership | Sync |
|---|---|---|---|---|---|---|---|
| Identity/Tenant | reads `Tenants` | none | tenant events | identity services | none | separated | tenant guard |
| Governance | reads/writes `UserGovernanceProfiles` | governance flag | governance events | governance (this runtime) | none | owned | serializable tx |
| Catalog | none | none (gates publish) | listing events | catalog owns product | listing doc | separated | publish event |
| Orders | reads eligibility | none (gates acceptance) | order events | orders own sub-order | none | separated | StoreId scope |
| Finance | reads `IsDebtBlocked` (via lifecycle DTO) | none | finance events | finance owns money | none | separated | readiness gate |
| Search | reads SQL overlay | writes listing visibility flag | listing events | SQL overlay owns visibility | ES doc | projection | reindex from SQL |
| Storefront | reads visibility decision | none | none | storefront owns surface | storefront | separated | derived |
| Accounting | none | none | `CommerceHubLedgerPosting` (finance) | accounting owns GL | JournalEntry | separated | outbox |
| Shipping | gate-checks active shipping | none | shipment events | shipping owns | none | separated | publish check |
| SuperAdmin | reads all (gov) | executes verification/approval | governance events | SuperAdmin platform scope | n/a | isolated | lifecycle |
| Service Marketplace | none | none | none | SEPARATE internal POS-to-POS runtime (README) | none | unrelated | n/a |

NOTE (verified): the **Service Marketplace** (`.ai/hermes/systems/service-marketplace/README.md`)
is an internal POS-to-POS B2B collaboration platform and has **no business relationship**
with CommerceHub or with this participation runtime. It is NOT governed by CommerceHub
Super Admin and does NOT participate in the vendor lifecycle. Do not conflate the two.

---

## Business Invariants

Implementation-backed (cite code):

1. **Vendor identity == tenant identity.** `CommerceHubVendorAuthority.LoadAsync` loads the
   tenant by `Id == vendorId` and the profile by `SubjectId == vendorId`
   (`CommerceHubVendorAuthority.cs:37-75`). No separate vendor row.
2. **Eligibility requires six current SQL facts.** `TenantExists && TenantActive &&
   GovernanceProfileExists && Status==Active && IsKycVerified && IsMarketplaceApproved`
   (`CommerceHubVendorAuthority.cs:16-22`).
3. **Approval requires verification.** Approving an unverified vendor throws
   `VENDOR_VERIFICATION_REQUIRED` (409) (`UserGovernanceService.cs:789-795`).
4. **Revoking KYC revokes approval atomically.** `IsMarketplaceApproved = false` when
   `IsKycVerified` set false (`UserGovernanceService.cs:784`).
5. **Eligibility is recomputed, never cached.** Every gate reloads
   `CommerceHubVendorAuthority.LoadAsync` (publish `:162`, visibility `:222`, session
   `AuthController.cs:125`).
6. **Suspended/Banned vendors cannot publish or accept orders.** Publish gated by
   `EnsureVendorListingAllowedAsync`; orders gated by `EnsureVendorOrderAcceptanceAllowedAsync`
   (throws on `Suspended`/`Banned`, `UserGovernanceService.cs`).
7. **Governance change cascades to listing visibility.** `SyncVendorVisibilityAsync` hides
   (`HideByGovernance`) or restores listings and enqueues search auto-heal
   (`CommerceHubVendorVisibilityService.cs:61-407`).
8. **Visibility restore is SQL-authority gated.** Restore requires current eligibility +
   live product + live variant + live category (`CommerceHubVendorVisibilityService.cs:253-267`).
9. **Lifecycle status is derived, not stored.** `ResolveVendorLifecycleStatus`
   (`AuthController.cs:158-200`) composes status from governance + debt each call.
10. **Single tenant currency, EGP marketplace finance.** `Tenant.Currency` default `"EGP"`
    (`Tenant.cs:62`); non-EGP tenant cannot publish to CommerceHub
    (`CommerceHubCommandHandlers.cs:178`).
11. **SuperAdmin isolation.** Verification/approval mutation routes require `RequireSuperAdmin`
    (`SuperAdminUsersController.cs:228`). Vendor-controlled DTOs cannot invoke governance.
12. **Governance subject/role immutable.** `SubjectId`/`Role` have no reassignment setter;
    application guards + DB trigger reject reassignment/deletion.



---

## Runtime Timeline (one complete lifecycle, per-step ownership)

```
1. POS Admin tenant registered
   Owner: TenantRegistrationService  | Authority: SQL Tenants + UserGovernanceProfiles
   Sync: n/a (creation)  | Persistence: SQL  | Consistency: transactional
   Events: none  | Failure: uniqueness constraint fails → duplicate blocked
   Evidence: TenantRegistrationService.cs:479 (creates Vendor governance profile)

2. Marketplace Enrollment (implicit)
   Owner: n/a — no enrollment entity  | Authority: derived (IsMarketplaceApproved=false default)
   Sync: n/a  | Persistence: governance flag  | Events: none
   Failure: tenant exists but ineligible until SuperAdmin acts

3. Vendor KYC Verified (SuperAdmin)
   Owner: SuperAdmin → UserGovernanceService  | Authority: SQL IsKycVerified
   Sync: serializable tx  | Persistence: SQL + audit  | Retry: idempotent set
   Events: none (cascade later)  | Failure: reason required; serializable rejects race
   Evidence: UserGovernanceService.cs:777-789

4. Vendor Approved (SuperAdmin)
   Owner: SuperAdmin → UserGovernanceService  | Authority: SQL IsMarketplaceApproved
   Sync: requires IsKycVerified first (else 409)  | Persistence: SQL
   Events: none  | Failure: VENDOR_VERIFICATION_REQUIRED
   Evidence: UserGovernanceService.cs:789-815

5. Marketplace Configuration
   Owner: POS Admin (Store/products) — NOT participation runtime
   Authority: tenant StoreConfig  | Evidence: Builder/Storefront runtime

6. Product Published to CommerceHub
   Owner: CommerceHub catalog (PublishToCommerceHubHandler)
   Authority: SQL CommerceHubListings (refs existing variant)
   Gate: EnsureVendorListingAllowedAsync (eligibility)  | Evidence: CommerceHubCommandHandlers.cs:162-215
   Events: CommerceHubListingCreatedEvent  | Failure: ineligible → CommerceHubValidationException

7. Customer Order
   Owner: CommerceHubCheckoutService  | Authority: SQL marketplace order + sub-order
   Gate: EnsureVendorOrderAcceptanceAllowedAsync  | Evidence: CommerceHubCheckoutService.cs:1801-1802
   Sync: split by StoreId (== vendor tenant)  | Failure: ineligible vendor → 403

8. Commission Calculated
   Owner: CommerceHub Finance (CommissionService)  | Authority: SQL sub-order snapshot
   Sync: recorded in ledger  | Evidence: commercehub-final-architecture.md § Financial

9. Vendor Earnings Updated
   Owner: CommerceHubEarningsService  | Authority: SQL CommerceHubVendorEarnings
   Sync: Pending→Available on delivery  | Evidence: CommerceHubFinancialWorkflowService.cs:181-205

10. Settlement Generated
    Owner: CommerceHubVendorDebtService/finance  | Authority: SQL CommerceHubVendorSettlements
    Sync: ledger→outbox→GL  | Evidence: commercehub-final-architecture.md § Settlement

11. Vendor Requests Payout
    Owner: CommerceHubPayoutService (request)  | Authority: SQL CommerceHubPayouts
    Gate: readiness gate (eligibility + debt + unresolved payments)  | Evidence: CommerceHubPayoutService.cs

12. Proof Uploaded / Super Admin Verification
    Owner: SuperAdminPayoutsService (execution)  | Authority: SQL payout Completed + evidence
    Sync: external reference idempotency  | Evidence: SuperAdminPayoutsService.cs:198-351

13. Payout Completed
    Owner: SuperAdmin (execution)  | Authority: SQL  | Sync: ledger→GL
    Events: payout events  | Failure: readiness gate blocks incomplete evidence

14. Ledger Updated
    Owner: CommerceHub Financial Ledger + AccountingOutboxProcessor  | Authority: SQL GL
    Sync: transactional outbox  | Evidence: AccountingOutboxProcessor.cs (CommerceHubLedgerPosting)

15. Vendor Suspension (SuperAdmin)
    Owner: SuperAdmin → UserGovernanceService  | Authority: SQL Status=Suspended
    Cascade: CommerceHubVendorVisibilityService hides listings + search auto-heal
    Evidence: CommerceHubVendorVisibilityService.cs:77-182

16. Vendor Reactivation
    Owner: SuperAdmin → UserGovernanceService  | Authority: SQL Status=Active
    Cascade: RestoreVendorListingsAsync re-derives from SQL  | Evidence: CommerceHubVendorVisibilityService.cs:184-322

17. Marketplace Exit
    Owner: SuperAdmin (tenant ban/delete)  | Authority: SQL Status=Banned / DeletedAtUtc
    Derived: lifecycle Terminated/Blocked  | Evidence: AuthController.cs:158-200
```

Steps 8–14 are owned by CommerceHub Finance, NOT the participation runtime. This runtime's
only steps are 1–4, 6 (gate), 7 (gate), 15–17.

---

## Runtime Classification

Determination (each conclusion code-cited):

- **NOT a Marketplace Runtime.** It owns no listings, orders, cart, or search truth
  (those are CommerceHub). It only gates them.
- **NOT a Vendor (seller-content) Runtime.** There is no vendor profile/settings/catalog
  the vendor owns beyond the tenant's own POS Admin + CommerceHub listings.
- **NOT a Marketplace Membership Runtime** in the sense of a stored membership entity.
  "Membership" is a derived eligibility predicate (`CommerceHubVendorAuthority.cs:16-22`),
  not a table.
- **NOT a Marketplace Financial / Settlement Runtime.** All money is owned by CommerceHub
  Finance (see § Financial Runtime Scope Boundary).
- **NOT a Vendor Governance silo.** Governance IS its core, but governance is shared with
  the platform; this runtime is the *vendor-facing* governance surface.
- **NOT a Projection Runtime.** It writes no projection; it writes a SQL visibility flag
  consumed by CommerceHub's projection.

**Conclusion: the runtime is fundamentally a Marketplace Participation + Vendor Governance
(Identity) Runtime.** It is the SQL-authoritative gate that (a) binds a tenant to a
`Role.Vendor` governance profile, (b) derives marketplace eligibility from current SQL,
(c) recomputes a derived lifecycle status per request, (d) cascades governance changes to
listing visibility, and (e) fails closed at every participation boundary. It preserves
tenant autonomy because the tenant keeps owning its catalog, inventory, payments, and
storefront — CommerceHub never becomes the authority over the tenant; it only consults
this gate to decide whether the tenant may participate.

---

## Final Architecture Question

**"How does Shopixy allow an independent POS Admin tenant to participate in the centralized
CommerceHub Marketplace while preserving tenant autonomy, maintaining clear authority
boundaries, coordinating marketplace finance/settlements/governance/vendor identity, without
allowing CommerceHub to become the authority over the tenant itself?"**

Answer (implementation-backed):

1. **Tenant autonomy is structural.** A vendor IS the tenant (`CommerceHubVendorAuthority.cs:37-75`).
   CommerceHub creates no vendor identity, no vendor-owned catalog, no vendor money of its
   own. The tenant continues to own its `Store`, `Products`, `ProductVariants`, inventory,
   payments lifecycle, and accounting GL. CommerceHub only creates marketplace *pointers*
   (`CommerceHubListings` referencing existing variants) and marketplace *orders*
   (parent + per-`StoreId` sub-orders) — it never overwrites tenant source truth.

2. **Participation is a governance overlay, not a new authority.** Marketplace
   participation is a small set of flags on `UserGovernanceProfiles`
   (`IsKycVerified`, `IsMarketplaceApproved`, `Status`) scoped to the tenant. Eligibility is
   a pure derived predicate (`CommerceHubVendorAuthority.cs:16-22`). CommerceHub consults
   this gate; it does not own it.

3. **Clear authority boundaries.** This runtime owns ONLY the participation gate. Finance,
   settlement, earnings, payouts, refunds, orders, listings, inventory, catalog, shipping,
   accounting, and search are each owned by their own services (see Runtime Authority Matrix
   and Cross-Runtime Authority Graph). The participation runtime writes none of their tables.

4. **Coordinated governance.** SuperAdmin certifies KYC + approval (serializable,
   reason-audited) and any governance change cascades to listing visibility + search
   auto-heal (`CommerceHubVendorVisibilityService.cs:61-407`), so commerce surfaces stay
   consistent with participation state without CommerceHub owning the governance decision.

5. **Coordinated finance/settlement.** CommerceHub Finance owns all money and mirrors each
   operational ledger row to the tenant GL via the transactional `AccountingOutboxMessage`
   bridge (see `commercehub-final-architecture.md`). The participation runtime supplies only
   the eligibility/`IsDebtBlocked` input the fail-closed readiness gate uses — it never
   moves money or decides payout eligibility truth.

6. **Fail-closed everywhere.** Ineligible vendors cannot publish, accept orders, appear in
   search, or be restored by stale cache/session/projection. Eligibility is recomputed from
   SQL at every gate; the lifecycle status is derived per request
   (`AuthController.cs:158-200`).

Net: Shopixy achieves participation **without CommerceHub authority over the tenant** by
making the vendor a *view* of the tenant (1:1 binding) plus a *governed eligibility gate*
this runtime owns, while every marketplace domain (finance, orders, catalog, search) keeps
its own authority and merely consults the gate.

---

## Documentation Validation

Comparison of the prompt's architectural assumptions against implementation. Incorrect
assumptions are corrected with code citations.

| # | Prompt assumption | Verdict | Correction (implementation) |
|---|---|---|---|
| 1 | "Marketplace Participation Runtime" implies a distinct marketplace runtime | PARTIAL | It is a **Participation + Governance (Identity) Runtime**, not a marketplace runtime. It owns no marketplace data. |
| 2 | A "Marketplace Vendor" is its own entity | FALSE | Vendor == tenant (`CommerceHubVendorAuthority.cs:37-75`). No `CommerceHubVendor` entity exists. |
| 3 | "Vendor Marketplace Profile" / "Marketplace Settings" tables exist | FALSE | No such tables. "Profile" = tenant `Store` + `UserGovernanceProfile`; settings are CommerceHub-platform/tenant config. |
| 4 | Vendor has its own currency / settlement currency / commission currency | FALSE | Single `Tenant.Currency` (default EGP, `Tenant.cs:62`); CommerceHub finance is EGP-only. No vendor currency. |
| 5 | Vendor earnings/settlement/balance/holdbacks/reserve/risk holds/payout eligibility are owned by this runtime | FALSE | Owned by CommerceHub Finance. This runtime only supplies the eligibility/`IsDebtBlocked` gate. |
| 6 | Events `VendorJoined/Approved/Suspended/Activated`, `MarketplaceSettingsChanged`, `SettlementCreated`, `PaymentRequested/Approved/Rejected` exist | MOSTLY FALSE | No vendor-named events. Participation changes emit *listing* events (`CommerceHubListingUnpublishedEvent`/`UpdatedEvent`). Finance events are owned by CommerceHub Finance. |
| 7 | There is a stored vendor lifecycle state machine (Pending/Approved/Active/Suspended/Hidden/.../Archived/Removed) | PARTIAL | Stored state = `UserGovernanceStatus` (Active/Suspended/Banned/FinanciallyLocked). Lifecycle status (`Pending/Active/Suspended/Blocked/DebtBlocked/Disabled/Terminated`) is DERIVED per request (`AuthController.cs:158-200`). `Hidden/Archived/Removed` are not vendor states. |
| 8 | Marketplace "Membership" is a first-class concept | FALSE | Membership = derived `IsMarketplaceApproved` flag + eligibility predicate; no membership entity. |
| 9 | "Marketplace Membership Runtime" / "Vendor Governance Runtime" / "Marketplace Financial Runtime" / "Marketplace Settlement Runtime" are candidate classifications | MISLEADING | Only the Governance/Participation classification holds. Finance/Settlement are separate CommerceHub Finance runtimes. |
| 10 | Service Marketplace is part of CommerceHub vendor participation | FALSE | Service Marketplace is an internal POS-to-POS B2B runtime with NO relationship to CommerceHub (`.ai/hermes/systems/service-marketplace/README.md:185-256`). Do not conflate. |
| 11 | Store Name / Business Identity / Tax / Store Logo / Store Config are marketplace-participation-owned | FALSE | Owned by POS Admin `Store`/tenant config, not this runtime. |
| 12 | "Marketplace Configuration" / "Publishing Settings" / "Category Mapping" are vendor-owned runtime concerns | PARTIAL | These are CommerceHub catalog publication concerns (`CommerceHubCommandHandlers.cs`), gated by this runtime but not owned by it. |

Every correction above is supported by code read directly from the repository (2026-07-19).

---

## Certification

### Implementation Confidence
HIGH. Every claim is traced to a specific `file:line` in the backend. The vendor==tenant
binding, the governance-flag eligibility predicate, the derived lifecycle resolver, the
SuperAdmin approval/verification mutations, and the visibility cascade were read directly
from source.

### Completeness
HIGH for participation/identity/governance/lifecycle/visibility/eligibility and the authority
boundaries with Finance, Catalog, Orders, Search, Storefront, Identity, SuperAdmin, and
Service Marketplace. The financial internals (earnings/settlement/payout/ledger/GL) are
intentionally summarized by reference to `commercehub-final-architecture.md` because they are
NOT owned by this runtime.

### Verified
- Vendor identity == tenant (no separate entity).
- Eligibility = 6-fact SQL predicate, recomputed per gate.
- Approval requires verification; revoking KYC revokes approval.
- Lifecycle status is derived, not stored.
- Governance change cascades to listing visibility + search auto-heal.
- Single tenant currency; EGP-only CommerceHub marketplace finance.
- Fail-closed at every participation boundary.

### Missing / Out of Scope
- No vendor-named domain events (by design — participation is a gate, not an event source).
- No vendor marketplace profile/settings/membership tables (by design).
- Vendor financial truth lives in CommerceHub Finance (separate canonical doc).

### Architectural Observation
The Marketplace Participation Runtime is best understood as a **thin, SQL-authoritative
governance gate over an existing tenant** — the minimal machinery needed to say "this tenant
may / may not act as a CommerceHub vendor," recomputed everywhere and cascaded to commerce
visibility. It is the inverse of a heavyweight "vendor" runtime: it adds almost no new state,
and its entire job is to keep CommerceHub's marketplace surfaces consistent with the tenant's
current governed participation state without CommerceHub ever becoming the authority over the
tenant.

End of canonical Marketplace Participation Runtime architecture. This document supersedes
the need to read individual `docs/architecture/*commercehub-vendor*` certification files to
understand participation; those remain useful as evidence but are not authoritative over this
implementation-backed specification.
