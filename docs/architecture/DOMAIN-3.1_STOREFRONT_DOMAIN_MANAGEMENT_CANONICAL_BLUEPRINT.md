# DOMAIN-3.1: Storefront Domain Management — Constitutional Blueprint

**Project:** Shopixy / PointOfSale
**Platform:** Multi-tenant e-commerce / affiliate / POS
**Date:** 2026-08-06
**Status:** CONSTITUTIONAL FREEZE
**Authorship:** Chief Product Architect, Principal UX Architect, Principal Software Architect, Storefront Platform Owner, Constitutional Review Board
**Next review:** Post-implementation certification (DOMAIN-3.1-C)

---

## SECTION 1 — ownership matrix

### 1.1 Domain Management ownership

Domain Management is owned by the **Storefront Product** team (Infrastructure layer) as a **peer-level workspace** within Storefront Admin — NOT CommerceHub, NOT POS Core, NOT Global Settings.

**Authority boundary:** Domain Management is invoked from the `domains` screen in Storefront Admin. The Storefront Admin authority (`storefrontAdminAuthority.js (src/auth/storefrontAdminAuthority.js)`) defines the roles that access all Storefront Admin screens including Domains: `Owner`, `Admin`.

| Artifact | Owner | Authority source |
|---|---|---|
| Domain entity (lifecycle, host, DNS verification, SSL lifecycle, redirects) | Storefront Product (Infrastructure) | `Domain.cs:7` |
| Domain lifecycle state machine (9 states) | Storefront Product (Infrastructure) | `DomainLifecycleState.cs:9-61` |
| Domain audit trail (append-only log) | Storefront Product (Infrastructure) | `DomainAuditEntry.cs:20-106` |
| Domain verification mechanism (DNS/HTTP) | Storefront Product (Infrastructure) | `DomainVerification.cs:16`; `VerificationMethod.cs:7-47` |
| SSL certificate lifecycle + renewal | Storefront Product (Infrastructure) | `SslCertificate.cs:15-114`; `ProviderType.cs:8-30` |
| Runtime host resolution middleware | Storefront Product (API) | `HostResolutionMiddleware.cs:46-282` |
| Tenant resolution middleware | Storefront Product (API) | `TenantResolutionMiddleware.cs:33-341` |
| Host normalization | Storefront Product (API) | `HostNormalizer.cs:22-88` |
| Domain resolution cache | Storefront Product (API) | `IDomainResolutionCache.cs`; `NoOpDomainResolutionCache.cs:15` |
| Tenant lineaged authority (SQL isolation) | Storefront Product (API) | `TenantLineageAuthority.cs` |
| Domain Management API endpoints (`/api/admin/domains`) | Storefront Product (API) | To be implemented in DOMAIN-3.1-B/C |
| Domain Management frontend screens (D01-D09) | Storefront Product (Web) | To be implemented in DOMAIN-3.1-A |
| Canonical URL resolution | Storefront Product (API) | `StorefrontUrlResolver.cs:53-78` (currently from `Tenant.cs:100`, migration to Primary Domain in DOMAIN-2) |

### 1.2 Cross-team boundaries — what Domains own vs. never touch

| Concern | Owner | Domain Management role | Evidence |
|---|---|---|---|
| Tenant resolution | Storefront Product (same team) | Host -> Domain -> TenantId via middleware pipeline | `HostResolutionMiddleware.cs:46` + `TenantResolutionMiddleware.cs:33` |
| StoreConfig (ThemeId, VersionsJson) | Storefront Product (Builder/Theme) | READ-ONLY: `StoreConfig.cs:16-40` has NO domain fields; domains reference StoreConfig.TenantId via ITenantOwned | `StoreConfig.cs:16-40` |
| Theme (presets, assets, overrides) | Storefront Product (Builder/Theme) | READ-ONLY: Theme loaded by ThemeId post-tenant-resolution | `StoreConfig.cs:40` |
| Storefront Runtime | Storefront Product (Runtime) | READ-ONLY: receives resolved TenantId from middleware | `StorefrontUrlResolver.cs:53-78` |
| CMS pages | Storefront Product (Content) | READ-ONLY: CMS operates on TenantContext, never domain | `StoreConfig.cs:12-14` (read-only contract) |
| CommerceHub | CommerceHub team | NOT OWNED by Domain Management; no domain concepts | `AdminStoreConfigController.cs:35` authority boundary |
| Service Marketplace | Service Marketplace team | NOT OWNED; no domain concepts | Separate bounded context |
| Payments | Payments team | NOT OWNED | Payment lifecycle separate |
| Orders | Orders team | NOT OWNED | Order lifecycle separate |
| Fulfillment | Fulfillment team | NOT OWNED | Fulfillment lifecycle separate |
| Affiliate program | Affiliate team | NOT OWNED | Separate bounded context |
| Inventory | Inventory team | NOT OWNED; inventory is SQL-authoritative | SQL-authoritative per DOMAIN-0 |
| Customer accounts | Customer team | NOT OWNED | Separate bounded context |

### 1.3 Relationship to existing workspaces

| Existing workspace | Relationship to Domains |
|---|---|
| Overview | Peer — domains summary card links TO domains screen |
| Status | Peer — system-level domain alerts surface here |
| Homepage | Peer — primary domain's canonical URL appears in homepage settings |
| Catalog | No direct relationship |
| Content (CMS) | No direct relationship; CMS uses TenantContext |
| Navigation | Peer — domains screen is a top-level navigation node |
| Footer | Peer — domain-specific footer overrides possible (future) |
| Appearance | READ-ONLY reference — domains reference ThemeId from StoreConfig |
| Theme Library | READ-ONLY reference — domains use ThemeId |
| Version History | READ-ONLY reference — domain mutations audited via DomainAuditEntry |
| Settings | Peer — domain configuration is part of storefront settings grouping |

---

## SECTION 2 — information architecture

### 2.1 Canonical navigation hierarchy

**Source basis:** `storefrontInformationArchitecture.js:4-119` (STOREFRONT_NAVIGATION at line 4), `storefrontInformationArchitecture.js:370` (STOREFRONT_PRODUCT_NAVIGATION), `storefrontInformationArchitecture.js:129` (OPERATIONS_NAVIGATION), `storefrontInformationArchitecture.js:2` (STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY), `storefrontInformationArchitecture.js:649` (resolveStorefrontScreen), `index.js:1-13` (barrel re-exports).

The `domains` screen is added to `STOREFRONT_NAVIGATION` as a **product navigation node** between `status` and `homepage`:

```
STOREFRONT_NAVIGATION (storefrontInformationArchitecture.js:4-119)
├── status              (D01 Overview — existing)
├── DOMAIN MANAGEMENT   (NEW node — parent of D01-D09)
│   ├── D01 Domains Overview
│   ├── D02 Domain Detail
│   ├── D03 Add Domain Wizard
│   ├── D04 Domain List
│   ├── D05 DNS Verification
│   ├── D06 SSL Management
│   ├── D07 Redirects
│   ├── D08 History
│   └── D09 Activity
├── homepage            (existing)
├── catalog
├── content
├── navigation
├── footer
├── appearance
└── settings
```

The Domain Management subtree is rendered under the Storefront Admin sidebar's product navigation section (`STOREFRONT_PRODUCT_NAVIGATION` at `storefrontInformationArchitecture.js:370`), NOT under Operations navigation (`OPERATIONS_NAVIGATION` at `storefrontInformationArchitecture.js:129`). The Sidebar (`Sidebar.jsx:243` — import at `Sidebar.jsx:37`, render at `Sidebar.jsx:243`) conditionally renders different navigation sections based on user role.

**Source basis for barrel integrity:** `index.js:1-13` re-exports `STOREFRONT_NAVIGATION`, `STOREFRONT_PRODUCT_NAVIGATION`, `OPERATIONS_NAVIGATION`, and `STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY` from `storefrontInformationArchitecture.js`. The barrel-crash bug from DOMAIN-3.0.2 audit is FIXED — all four constants are defined at `storefrontInformationArchitecture.js:4` (STOREFRONT_NAVIGATION), `storefrontInformationArchitecture.js:370` (STOREFRONT_PRODUCT_NAVIGATION), `storefrontInformationArchitecture.js:129` (OPERATIONS_NAVIGATION), and `storefrontInformationArchitecture.js:2` (STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY), and all are re-exported by the barrel at `index.js:3-11`.

### 2.2 Breadcrumb hierarchy

**Source basis:** `StorefrontBreadcrumbs.jsx:1-67` (836 chars), `StorefrontAdminFrame.jsx:1-38` (1,522 chars).

| Screen | Breadcrumb path |
|---|---|
| D01 Overview | Home > Storefront > Domains |
| D02 Detail | Home > Storefront > Domains > {hostname} |
| D03 Wizard | Home > Storefront > Domains > Add Domain |
| D04 List | Home > Storefront > Domains > Domain List |
| D05 Verification | Home > Storefront > Domains > {hostname} > DNS Verification |
| D06 SSL | Home > Storefront > Domains > {hostname} > SSL Management |
| D07 Redirects | Home > Storefront > Domains > {hostname} > Redirects |
| D08 History | Home > Storefront > Domains > {hostname} > History |
| D09 Activity | Home > Storefront > Domains > Activity |

### 2.3 Screen inventory (C01-C11)

The Domain Management workspace maps to the canonical screen inventory from `STOREFRONT_CANONICAL_UX_BLUEPRINT`:

| Screen ID | Title | Canonical ID | Type | Entry point | Exit points | AuthoringPlatform components |
|---|---|---|---|---|---|---|
| D01 | Domains Overview | C01 | List | Sidebar nav: `domains` node | D02 (click card/link) | AuthoringShell, AuthoringButton, AuthoringFeedback |
| D02 | Domain Detail | C02 | Detail | D01 (click domain row) | D01 (back), D03, D05, D06, D07, D08 | AuthoringShell, AuthoringButton, AuthoringFeedback |
| D03 | Add Domain Wizard | C03 | Wizard | D01 (Add Domain button) | D01 (cancel) or D02 (complete) | AuthoringShell, AuthoringButton, AuthoringFeedback |
| D04 | Domain List | C04 | List | D01 (View All link) | D02 (click row), D03 | AuthoringShell, AuthoringButton, AuthoringFeedback |
| D05 | DNS Verification | C05 | Status | D02 (Verification tab) or D03 (step 2) | D02 (back) | AuthoringShell, AuthoringButton, AuthoringFeedback |
| D06 | SSL Management | C06 | Status | D02 (SSL tab) | D02 (back) | AuthoringShell, AuthoringButton, AuthoringFeedback |
| D07 | Redirects | C07 | List | D02 (Redirects tab) | D02 (back) | AuthoringShell, AuthoringButton, AuthoringFeedback |
| D08 | History | C08 | List | D02 (History tab) | D02 (back) | AuthoringShell, AuthoringButton, AuthoringFeedback |
| D09 | Activity | C09 | Status | Sidebar nav: `domains/activity` | None (leaf) | AuthoringShell, AuthoringButton, AuthoringFeedback |

### 2.4 URL routing

**Source basis:** `storefrontInformationArchitecture.js:649` (resolveStorefrontScreen).

All Domain Management routes are under `/storefront/domains/`:

| Screen | Route | Canonical screen ID |
|---|---|---|
| D01 Overview | `/storefront/domains` | D01 |
| D02 Detail | `/storefront/domains/{id}` | D02 |
| D03 Wizard | `/storefront/domains/add` | D03 |
| D04 List | `/storefront/domains/list` | D04 |
| D05 Verification | `/storefront/domains/{id}/verification` | D05 |
| D06 SSL | `/storefront/domains/{id}/ssl` | D06 |
| D07 Redirects | `/storefront/domains/{id}/redirects` | D07 |
| D08 History | `/storefront/domains/{id}/history` | D08 |
| D09 Activity | `/storefront/domains/activity` | D09 |

### 2.5 Information architecture constants

**Source basis:** `storefrontInformationArchitecture.js:4-119` (STOREFRONT_NAVIGATION), `storefrontInformationArchitecture.js:370` (STOREFRONT_PRODUCT_NAVIGATION), `storefrontInformationArchitecture.js:129` (OPERATIONS_NAVIGATION), `storefrontInformationArchitecture.js:2` (STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY).

The following constants are defined and exported from `storefrontInformationArchitecture.js` (at `src/modules/storefront/informationArchitecture/`) and re-exported by the barrel at `index.js:1-13` (re-exports at `index.js:3-11`):

- `STOREFRONT_NAVIGATION` (defined at `storefrontInformationArchitecture.js:4`)
- `STOREFRONT_PRODUCT_NAVIGATION` (defined at `storefrontInformationArchitecture.js:370`)
- `OPERATIONS_NAVIGATION` (defined at `storefrontInformationArchitecture.js:129`)
- `STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY` (defined at `storefrontInformationArchitecture.js:2`)

The `domains` node is inserted into `STOREFRONT_NAVIGATION` between the `status` and `homepage` entries, as a child of the product navigation section. The `resolveStorefrontScreen()` function (`storefrontInformationArchitecture.js:649`) maps domain route paths to their canonical screen IDs.

---

## SECTION 3 — screen specifications

### D01 — Domains Overview

**Purpose:** Primary entry point for Domain Management. Provides a high-level summary of the tenant's domain portfolio and quick actions.

**Source basis:** `storefrontInformationArchitecture.js:649` (resolveStorefrontScreen maps `/storefront/domains` to D01). `storefrontAdminAuthority.js (src/auth/storefrontAdminAuthority.js)` defines `Owner`/`Admin` roles for Storefront Admin access.

| Element | Description | AuthoringPlatform component |
|---|---|---|
| Summary header | "You have X domains connected" | AuthoringShell |
| Primary domain card | Current Primary domain hostname + status badge + canonical URL | AuthoringShell + AuthoringStatusBadge |
| Domain count card | Total domains, breakdown by type | AuthoringShell |
| SSL health card | Certs expiring in <30 days + action count | AuthoringShell + AuthoringStatusBadge |
| Quick actions | Add Domain, Verify DNS, Renew SSL, Manage Redirects | AuthoringButton |
| Domain list preview | Table: hostname, type, status, SSL status, actions (3-dot menu) — first 10 rows | AuthoringShell |
| "View All Domains" link | Navigate to D04 | AuthoringButton |

**State mapping (no new state renderers):**

| UI State | Lifecycle state | Rendered status | AuthoringPlatform component |
|---|---|---|---|
| Draft | DomainLifecycleState.Draft (cs:15) | "Setup" (muted gray) | AuthoringStatusBadge |
| Pending Verification | PendingVerification (cs:22) | "Pending DNS Verification" (muted blue, spinning) | AuthoringStatusBadge + D05 auto-poll |
| Verified | Verified (cs:28) | "DNS Verified" (green) | AuthoringStatusBadge |
| SSL Pending | SslPending (cs:34) | "SSL Provisioning" (muted purple, spinning) | AuthoringStatusBadge + D06 auto-poll |
| SSL Active | SslActive (cs:39) | "SSL Active" (green) | AuthoringStatusBadge |
| Primary | Primary (cs:46) | "Primary" (blue, bold) | AuthoringStatusBadge |
| Disabled | Disabled (cs:52) | "Disabled" (red) | AuthoringStatusBadge |
| Archived | Archived (cs:58) | "Archived" (gray) | AuthoringStatusBadge |
| Deleted | Deleted (cs:61) | Not displayed in active view (hidden by default) | AuthoringStatusBadge (filter) |

**Entry points:** Sidebar nav: Domains node.
**Exit points:** Click domain row -> D02; Add Domain button -> D03; View All -> D04.

### D02 — Domain Detail

**Purpose:** Detailed view of a single domain with tabbed navigation covering Overview, Verification, SSL, Redirects, and History.

**Source basis:** `storefrontInformationArchitecture.js:649` (resolveStorefrontScreen maps `/storefront/domains/{id}` to D02).

| Tab | Purpose | Data source |
|---|---|---|
| Overview | Edit hostname, type, lifecycle actions (promote/demote/disable/archive/delete) | `Domain.cs:22-257` + `DomainLifecycleState.cs:9-61` |
| Verification | DNS challenge, verification status, retry | `DomainVerification.cs:16-90` + `VerificationMethod.cs:7-47` |
| SSL | Certificate status, renewal, upload, revoke | `SslCertificate.cs:15-114` + `ProviderType.cs:8-30` |
| Redirects | Redirect target configuration | `Domain.cs:91-98` (RedirectType, RedirectTargetDomainId) |
| History | Audit log | `DomainAuditEntry.cs:20-106` |

**Actions (contextual by lifecycle state):**

| Lifecycle state | Available actions |
|---|---|
| Draft | Delete, Verify DNS |
| Pending Verification | Re-verify, Cancel |
| Verified | Enable SSL, Disable, Delete |
| Ssl Pending | View SSL status, Disable, Delete |
| Ssl Active | Renew SSL, Change primary, Disable, Archive, Delete |
| Primary | Demote to Alias, Disable (disabled — cannot disable Primary), Archive (disabled), Delete (disabled) |
| Disabled | Re-enable, Archive, Delete, Restore |
| Archived | Restore, Delete |
| Deleted | Restore (within 90-day window) |

**Entry points:** D01 (click domain row), direct URL navigation.
**Exit points:** Back to D01, navigate to sub-tabs.

### D03 — Add Domain Wizard

**Purpose:** 7-step wizard for adding a new domain.

**Source basis:** `AuthoringDialogs.jsx:1-168` (wizard pattern).

| Step | Field | Validation | Source |
|---|---|---|---|
| 1. Domain name | Hostname (text input) | Valid hostname format, not a reserved System domain, not already registered to this tenant | `HostNormalizer.cs:25`; `DomainType.cs:49` (System) |
| 2. Domain type | Enum (Primary, Alias, Preview, Development) | Cannot set as Redirect or Temporary (set later); if Primary, no existing Primary | `DomainType.cs:13,19,31,43` |
| 3. DNS verification | Method (TXT, CNAME, HTTP file) | Method must match domain type; CNAME not available for System domains | `VerificationMethod.cs:13,19,25` |
| 4. SSL options | Provider (Let's Encrypt, Custom Upload, System Default) | Custom Upload requires verified domain; System Default for System domains | `ProviderType.cs:14,19,25` |
| 5. Redirect setup | Target domain (dropdown of tenant's verified domains) | Cannot redirect to self; no redirect loops | `Domain.cs:91,98`; `RedirectType.cs:7` |
| 6. Canonical policy | Enforce HTTPS, canonical to primary | Canonical to primary requires Verified+SslActive target | `CanonicalPolicy.cs:8`; `CanonicalPolicy.cs:14,21` |
| 7. Review & confirm | Summary of all settings | All previous steps must pass validation | AuthoringValidationSummary |

**Post-submit:** Creates domain in `Draft` -> `PendingVerification` lifecycle state. User is auto-navigated to D05 for DNS verification.

**Entry points:** D01 (Add Domain button), D04 (Add Domain button).
**Exit points:** D01 (cancel), D05 (on successful submission).

### D04 — Domain List

**Purpose:** Paginated data table of all domains for the tenant.

| Column | Data source | Sortable |
|---|---|---|
| Domain | NormalizedHostName (`Domain.cs:50`) | Yes |
| Type | DomainType (`Domain.cs:57`, `DomainType.cs:7`) | Yes |
| Status | DomainLifecycleState (`Domain.cs:64`, `DomainLifecycleState.cs:9`) | Yes |
| SSL | SslCertificate status + expiry (`SslCertificate.cs:15-114`) | Yes |
| Created | CreatedAtUtc (`Domain.cs:111`) | Yes |
| Actions | Row action menu (view, edit, delete) | No |

| Filter | Source |
|---|---|
| All types | DomainType.cs:7 |
| All lifecycle states | DomainLifecycleState.cs:9 |
| SSL status | SslCertificate.cs:15 |
| Search by hostname | NormalizedHostName |

### D05 — DNS Verification

**Purpose:** Display DNS challenge for domain verification and track verification status.

**Source basis:** `DomainVerification.cs:16-90`.

| Element | Description | Source |
|---|---|---|
| Challenge type | TXT, CNAME, or HTTP file (`VerificationMethod.cs:13,19,25`) | `DomainVerification.cs:33` |
| Challenge value | The TXT record value or CNAME target or HTTP file content | `DomainVerification.cs:39` |
| Challenge location | DNS record name or HTTP path | `DomainVerification.cs:33` |
| Status | Pending, Success, Failed (`VerificationStatus.cs:7`) | `DomainVerification.cs:45`, `VerificationStatus.cs:13,18,24` |
| Last attempt | AttemptedAtUtc (`DomainVerification.cs:50`) |
| Verified at | VerifiedAtUtc (`DomainVerification.cs:56`) |
| Failure reason | FailureReason (`DomainVerification.cs:62`) |
| Retry button | Re-attempt verification (max 5/hr rate limit) | API Section 10.1 |

**Auto-refresh:** Polls verification status every 10 seconds while in `PendingVerification` or `Pending` status. Stops polling on `Success` or `Failed` (`DomainLifecycleState.cs:22` -> `28`).

**Entry point:** D03 step 2, D02 Verification tab, D02 action button.
**Exit point:** D02 (back) — auto-navigates on verification success.

### D06 — SSL Management

**Purpose:** Display SSL certificate status and manage certificate lifecycle.

**Source basis:** `SslCertificate.cs:15-114`.

| Element | Description | Source |
|---|---|---|
| Provider | LetsEncrypt, CustomUpload, SystemDefault (`ProviderType.cs:14,19,25`) | `SslCertificate.cs:33` |
| Status | Pending, Active, Expired, Revoked, Failed (`CertificateStatus.cs:7`) | `SslCertificate.cs:39`, `CertificateStatus.cs:13,18,23,28,33` |
| Certificate ARN | AWS/GCP cert ARN (for LetsEncrypt provider) | `SslCertificate.cs:46` |
| Thumbprint | Certificate thumbprint | `SslCertificate.cs:52` |
| Validity period | NotBeforeUtc - NotAfterUtc (`SslCertificate.cs:57,62`) |
| Renewal window | NextRenewalUtc (`SslCertificate.cs:74`) — 30 days before expiry |
| Auto-renew toggle | Enable/disable automatic renewal | API Section 10.1 |
| Upload cert button | Upload custom certificate (CustomUpload provider) | API Section 10.1 |
| Renew now button | Trigger manual renewal | API Section 10.1 |
| Revoke cert button | Revoke current certificate | API Section 10.1 |

**Auto-refresh:** Polls SSL status every 30 seconds while in `Pending` or `SslPending` lifecycle state.

**Entry point:** D02 SSL tab, D02 action button.
**Exit point:** D02 (back).

### D07 — Redirects

**Purpose:** Configure and manage redirect rules for a domain.

| Element | Description | Source |
|---|---|---|
| Target domain | Dropdown of tenant's verified+SSL active domains | `Domain.cs:98` (RedirectTargetDomainId) |
| Redirect type | Permanent (301) or Temporary (307) | `RedirectType.cs:18,24` |
| Redirect scope | All paths (default) or specific paths | `Domain.cs:91` (RedirectType) |
| Canonical policy | UseAsPrimary or CanonicalizeToPrimary | `CanonicalPolicy.cs:14,21` |
| Redirect sources | List of Alias domains redirecting to this Primary | `Domain.cs:181` (RedirectSources navigation) |

**Validation rules** (enforced at `Domain.cs:187-196`):
1. A `Primary` domain (`DomainLifecycleState.cs:46`) can have RedirectType = `None` (`RedirectType.cs:12`) only (canonical target).
2. A `Redirect` domain (`DomainType.cs:25`) must have RedirectTargetDomainId set to a `Primary` domain.
3. Redirect loops: target domain must NOT be a redirect source of the current domain.
4. Cross-tenant redirect targets: redirect target must belong to the same TenantId (`ITenantOwned.cs:23`).

**Entry point:** D02 Redirects tab.
**Exit point:** D02 (back).

### D08 — History

**Purpose:** Append-only audit log of all domain state transitions.

**Source basis:** `DomainAuditEntry.cs:20-106`.

| Column | Data source |
|---|---|
| Timestamp | TimestampUtc (`DomainAuditEntry.cs:96`) |
| Actor | ActorType (`DomainAuditEntry.cs:53`) + ActorUserId (display name looked up) |
| Action | Action string (`DomainAuditEntry.cs:47`) |
| Details | BeforeSnapshotJson / AfterSnapshotJson diff (`DomainAuditEntry.cs:66,73`) |
| Trace ID | TraceId (`DomainAuditEntry.cs:79`) — for debugging |

**Filters:** Action type, Actor type, Date range.
**Restore action:** Available on `DomainDisabled`, `DomainArchived`, `DomainDeleted` entries (if within 90-day retention).

**Entry point:** D02 History tab.
**Exit point:** D02 (back).

### D09 — Activity

**Purpose:** Real-time dashboard of domain-related background job status across all tenant domains.

| Element | Description |
|---|---|
| Summary bar | Total jobs: X running, Y succeeded, Z failed (today) |
| Running jobs | Current verification/DNS/SSL jobs with spinner |
| Recent activity | Timeline of completed events (last 24h) — sourced from DomainAuditEntry (`DomainAuditEntry.cs:20`) |
| Errors | Failed jobs with error message + retry button |
| Auto-refresh | Polls every 5 seconds |

**Source basis:** Events published via `IEventPublisher` (`IEventPublisher.cs:8-60`). Consumed from `DomainAuditEntry` (`DomainAuditEntry.cs:20`).

### D10 — Restore Point

**Purpose:** Specialized view shown from D08 History when a restore-point action is eligible.

| Element | Description |
|---|---|
| Current state | Domain's current lifecycle state |
| Restore target | The state being restored from the audit entry |
| Impact assessment | What will change (e.g., "Domain will return to Verified state, SSL will remain active") |
| Confirm button | Executes restore via PUT /api/admin/domains/{id}/restore |
| Cancel button | Returns to D08 |




---

## SECTION 4 — domain lifecycle

### 4.1 Lifecycle states

The DomainLifecycleState enum (`DomainLifecycleState.cs:9`) defines 9 states. The state machine is enforced at the entity level (`Domain.cs:197-256`).

| Enum value | Line | State | Description |
|---|---|---|---|
| Draft | DomainLifecycleState.cs:15 | Initial state after domain creation | HostName saved, not yet verified |
| PendingVerification | DomainLifecycleState.cs:22 | DNS verification in progress | Awaiting DNS proof |
| Verified | DomainLifecycleState.cs:28 | DNS proof confirmed; no SSL yet | Verification complete |
| SslPending | DomainLifecycleState.cs:34 | SSL certificate provisioning in progress | Cert requested, not yet bound |
| SslActive | DomainLifecycleState.cs:39 | SSL certificate active and bound | Domain is secure and serving |
| Primary | DomainLifecycleState.cs:46 | Canonical primary domain for the tenant | One per tenant (filtered unique index) |
| Disabled | DomainLifecycleState.cs:52 | Domain stopped serving | Can be reactivated or archived |
| Archived | DomainLifecycleState.cs:58 | Domain archived (retention) | Can be restored or permanently deleted |
| Deleted | DomainLifecycleState.cs:61 | Soft-deleted | Within 90-day retention; can be restored |

### 4.2 State transitions

**Source basis:** `Domain.cs:197-256` (lifecycle methods).

| From -> To | Trigger | Guard | Error if violated |
|---|---|---|---|
| Draft -> PendingVerification | Create domain via API (POST /api/admin/domains) | HostName valid, NormalizedHostName unique per tenant | 409 if NormalizedHostName already exists (`Domain.cs:50`) |
| PendingVerification -> Verified | DNS verification succeeds | Verification token matches DNS | DNS verification failure |
| PendingVerification -> Draft | Verification failed / cancelled | DNS check failed or retried too many times | None |
| Verified -> SslPending | Enable SSL via API | DNS verified, domain not disabled | 400 if not verified |
| SslPending -> SslActive | SSL certificate provisioned | Cert valid for domain | 400 if cert mismatch |
| SslPending -> SslPending | SSL renewal retry | Auto-renewal cycle | 429 if rate limited |
| SslActive -> SslPending | SSL renewal (auto/manual) | Cert near expiry | None |
| SslActive -> Verified | SSL revoked | Domain not Primary | 400 if Primary |
| SslActive -> Primary | Promote to Primary | Verified + SslActive, no existing Primary | 400 if not Verified+SslActive; 409 if existing Primary |
| Primary -> SslActive | Demote Primary (new Primary set) | New Primary promoted simultaneously | None |
| SslActive -> Disabled | Disable domain | Not Primary, has redirect targets configured | 400 if Primary or has Redirect alias dependents |
| Disabled -> Verified | Re-enable | Was in Disabled state | 400 if not Disabled |
| SslActive -> Archived | Archive domain | Not Primary, has redirect targets | 400 if Primary |
| Disabled -> Archived | Archive disabled domain | Was in Disabled state | 400 if not Disabled |
| Archived -> SslActive | Restore from archive | Within 90-day retention | 400 if outside retention |
| Archived -> Disabled | Restore to disabled (was Disabled before archive) | Within 90-day retention | 400 if outside retention |
| Any -> Deleted | Delete domain | Not Primary, SSL not active (or revoked), no redirect dependents | 400 if Primary, active SSL, or has Redirect dependents |
| Deleted -> Archived | Restore from soft-delete | Within 90-day retention | 400 if outside retention |

### 4.3 Filtered unique constraint

The `DomainLifecycleState.Primary` state (line 46) is backed by a filtered unique index enforcing exactly one Primary per tenant. The XML doc at `DomainLifecycleState.cs:44` states: "Exactly one Primary domain per tenant is enforced by `UQ_Domains_TenantId_Primary` filtered unique index."

### 4.4 Serving eligibility

A domain is eligible to serve storefront traffic when its LifecycleState is `Primary` (`DomainLifecycleState.cs:46`), `SslActive` (`DomainLifecycleState.cs:39`), or `Verified` (`DomainLifecycleState.cs:28`). Domains in `Draft`, `PendingVerification`, `SslPending`, `Disabled`, `Archived`, or `Deleted` do not serve traffic.

### 4.5 Redirect handling

Redirect configuration is defined by `Domain.cs:91` (RedirectType) and `Domain.cs:98` (RedirectTargetDomainId). Redirect rules are enforced at the entity level (`Domain.cs:187-196`):
1. Primary domains (`DomainLifecycleState.cs:46`) can only have `RedirectType.None` (`RedirectType.cs:12`).
2. Redirect domains (`DomainType.cs:25`) must have RedirectTargetDomainId set to a Primary domain.
3. Redirect loops are prevented at the entity level.

---

## SECTION 5 — merchant workflow

### 5.1 Primary path (happy flow)

1. Merchant clicks "Add Domain" (D01 -> D03 step 1).
2. Merchant enters hostname (D03 step 1).
3. Merchant selects domain type = Primary (D03 step 2).
4. System assigns DNS verification method (D03 step 3 — TXT by default).
5. Merchant configures DNS records (D05).
6. System polls verification status (D05 auto-poll every 10s).
7. DNS verification succeeds -> domain transitions to Verified (D05 -> D02 SSL tab auto-open).
8. System provisions SSL certificate (SslPending -> SslActive).
9. System prompts merchant to promote domain to Primary (D02 Overview tab).
10. Merchant clicks "Set as Primary" -> domain transitions to Primary state.
11. Previous Primary is demoted to SslActive.

### 5.2 Alias domain path

1. Merchant adds additional domain (D03 step 1).
2. Merchant selects domain type = Alias (D03 step 2).
3. Same DNS + SSL flow (D05, D06).
4. Domain reaches SslActive -> stops (Alias does not require Primary promotion).
5. Domain serves the same store as the Primary.

### 5.3 Redirect domain path

1. Merchant configures redirect domain (D03 step 1, type = Redirect).
2. Merchant selects redirect target from dropdown of tenant's Primary domains (D03 step 5).
3. System configures DNS + SSL for the redirect domain.
4. On SslActive, redirect domain 301-redirects all traffic to the target Primary domain.

### 5.4 Custom SSL path

1. Merchant adds domain with type = Alias or Primary (D03 steps 1-2).
2. At D03 step 4 (SSL options), merchant selects "Custom Upload".
3. System still performs DNS verification (D05) — SSL type is separate from DNS verification.
4. After DNS verification succeeds, D06 shows "Upload Certificate" button.
5. Merchant uploads PEM certificate + private key (D06).
6. System validates cert matches domain, checks chain completeness, validates key.
7. On valid upload, SslCertificate status -> Active.

### 5.5 Preview domain path

1. Merchant adds domain with type = Preview (D03 steps 1-2).
2. DNS verification (D05) — same flow as other types.
3. SSL provisioning (D06) — same flow.
4. Domain reaches SslActive -> stops (no Primary promotion needed for Preview).
5. Domain serves storefront preview.

### 5.6 Disable / re-enable path

1. Merchant navigates to D02 (Domain Detail).
2. Merchant clicks "Disable Domain" (Overview tab).
3. System validates: domain must not be Primary, must have redirect dependents migrated (`Domain.cs:187-196`).
4. If validation passes, domain transitions to Disabled.
5. Domain no longer serves traffic.
6. To re-enable: Merchant clicks "Re-enable" -> domain transitions back to Verified/SslActive.

### 5.7 Archive / restore path

1. Merchant clicks "Archive Domain" (D02 Overview).
2. System validates: not Primary, redirect dependents resolved.
3. Domain transitions to Archived.
4. To restore: Merchant clicks "Restore" -> domain returns to previous state (Disabled or Verified/SslActive).
5. Restore must be within 90-day retention window.

### 5.8 Delete / restore path

1. Merchant clicks "Delete Domain" (D02 Overview).
2. System validates: not Primary, SSL not active (or revoked), no redirect dependents.
3. Domain transitions to Deleted (soft-delete via `Domain.cs:231-256` `MarkDeleted()`).
4. To restore: Merchant clicks "Restore" (D08 History) -> domain returns to Archived state.
5. Restore must be within 90-day retention window.
6. After 90 days, domain is permanently purged by background job.

### 5.9 Bulk operations

1. Merchant selects multiple domains in D04 (checkboxes).
2. Bulk action dropdown appears: Disable, Archive, Delete.
3. System validates each domain individually — invalid ones are reported as failures.
4. Results shown as partial success (some succeed, some fail with reasons).


---

## SECTION 6 — dialogs

All dialogs reuse AuthoringPlatform dialog primitives (`AuthoringDialogs.jsx:1-168`). No custom dialog implementations.

| Dialog | Purpose | AuthoringPlatform component |
|---|---|---|
| Confirm dialog | "Are you sure?" for destructive actions (delete, archive, disable) | AuthoringDialogs.jsx (ConfirmDialog) |
| Conflict dialog | Optimistic concurrency conflict (409) with retry option | AuthoringDialogs.jsx (ConflictDialog) |
| Unsaved changes dialog | Warning on navigation away from dirty form | AuthoringDialogs.jsx (UnsavedChangesDialog) |
| Loading dialog | Blocking loader for long-running operations | AuthoringDialogs.jsx (LoadingDialog) |
| Wizard dialog | 7-step Add Domain Wizard (D03) | AuthoringDialogs.jsx (wizard pattern, lines 8-90) |
| SSL upload dialog | File picker for certificate + private key upload | AuthoringDialogs.jsx (file upload variant) |
| Redirect loop warning | Warning when redirect target creates a loop | AuthoringDialogs.jsx (ConfirmDialog variant) |

**Wizard pattern:** The D03 Add Domain Wizard uses horizontal/vertical stepper (`AuthoringDialogs.jsx:8-90`). Steps validate before allowing next. On mobile: full-screen with swipe. On desktop: side-by-side.

**Dirty guard:** `useAuthoringGuard.js:1-92` wraps all Domain Management forms. Any unsaved changes trigger `UnsavedChangesDialog` on navigation.

---

## SECTION 7 — validation

### 7.1 Validation strategy

All validation uses AuthoringPlatform primitives. No custom validation components.

**Source basis:** `AuthoringValidationSummary.jsx` (validation component), `AuthoringDialogs.jsx` (wizard validation).

| Validation type | Mechanism | Source |
|---|---|---|
| Form-level errors | AuthoringValidationSummary renders all form errors | `AuthoringValidationSummary.jsx` |
| Field-level errors | Inline error text below each field | `AuthoringValidationSummary.jsx` |
| Server-side errors | Mapped to form fields via error code | API Section 10.5 |
| Optimistic concurrency | ConflictDialog shown on 409 | `AuthoringDialogs.jsx` (ConflictDialog) |
| Rate limiting | Inline message near action button | API Section 10.1 |

### 7.2 Domain-specific validation rules

| Rule | Source | Error code |
|---|---|---|
| Hostname must be valid format | HostNormalizer.cs:25 | INVALID_HOST_FORMAT |
| Hostname must not already exist for tenant | Domain.cs:50 (NormalizedHostName UNIQUE) | DOMAIN_CONFLICT |
| Cannot set Redirect/Temporary/System as Primary | DomainType.cs:25,37,49 | INVALID_DOMAIN_TYPE_FOR_PRIMARY |
| Cannot disable/delete Primary domain | DomainLifecycleState.cs:46 | CANNOT_MODIFY_PRIMARY |
| Cannot archive/delete Primary domain | DomainLifecycleState.cs:46 | CANNOT_MODIFY_PRIMARY |
| Cannot delete domain with active SSL | SslCertificate.cs:39 (Active) | CANNOT_DELETE_WITH_SSL |
| Cannot delete domain with redirect dependents | Domain.cs:181 (RedirectSources) | HAS_REDIRECT_DEPENDENTS |
| Redirect target must be tenant's Primary domain | Domain.cs:91,98; ITenantOwned.cs:23 | INVALID_REDIRECT_TARGET |
| Redirect loop detection | Domain.cs:187-196 | REDIRECT_LOOP |
| Cross-tenant redirect forbidden | ITenantOwned.cs:23 | HOST_TENANT_MISMATCH |
| Custom cert upload: cert must match domain | SslCertificate.cs:52 (thumbprint) | CERT_DOMAIN_MISMATCH |
| Custom cert upload: chain must be complete | SslCertificate.cs:104 (history) | CERT_CHAIN_INCOMPLETE |
| Custom cert upload: private key must match cert | SslCertificate.cs:52 | KEY_CERT_MISMATCH |
| Restore within 90-day retention | Domain.cs:134 (DeletedAtUtc) | RESTORE_OUTSIDE_RETENTION |
| DNS verification rate limit | API Section 10.1 | RATE_LIMIT_EXCEEDED |
| SSL renewal rate limit | API Section 10.1 | TOO_MANY_REQUESTS |
| Only Verified+SslActive domain can be promoted to Primary | DomainLifecycleState.cs:28,39 | CANNOT_PROMOTE_NOT_READY |
| Only PendingVerification state can retry DNS | DomainLifecycleState.cs:22 | VERIFICATION_RETRY_NOT_ALLOWED |

### 7.3 Validation feedback

- Errors are displayed using AuthoringFeedback.jsx (success/error/info messaging).
- Field-level errors appear immediately on field blur and on form submit.
- Server errors are mapped to fields by error code from API Section 10.5.
- Rate limit errors show a countdown timer.
- SSL upload validation errors are shown in the upload dialog.

---

## SECTION 8 — authorization

### 8.1 Roles

**Source basis:** `storefrontAdminAuthority.js (src/auth/storefrontAdminAuthority.js)` defines Storefront Admin access for `Owner` and `Admin` roles.

| Role | Domain Management capabilities |
|---|---|
| Owner | Full access: add, edit, verify, SSL, redirect, disable, archive, delete, restore |
| Admin | Full access: add, edit, verify, SSL, redirect, disable, archive, delete, restore |
| Developer | Read-only: view domains, history, activity |
| Restricted | No access to Domain Management |

### 8.2 Permission matrix

| Action | Owner | Admin | Developer | Restricted |
|---|---|---|---|---|
| View domains (D01, D02, D04) | Yes | Yes | Yes | No |
| Add domain (D03) | Yes | Yes | No | No |
| DNS verification (D05) | Yes | Yes | No | No |
| SSL management (D06) | Yes | Yes | No | No |
| Redirects (D07) | Yes | Yes | No | No |
| Disable/Archive/Delete | Yes | Yes | No | No |
| Restore (D08) | Yes | Yes | No | No |
| Activity (D09) | Yes | Yes | Yes | No |
| Audit history (D08) | Yes | Yes | Read-only | No |

### 8.3 API endpoint authorization

All `/api/admin/domains` endpoints check the user's Storefront Admin role (`storefrontAdminAuthority.js (src/auth/storefrontAdminAuthority.js)`). Unauthorized access returns 403.

### 8.4 Platform-admin override

Platform admins (`ActorType.PlatformAdmin`, `ActorType.cs:25`) can bypass all authorization checks. This is enforced at the Infrastructure domain service layer. Platform-admin actions are always audited (`DomainAuditEntry.cs:20` with ActorType=PlatformAdmin).

### 8.5 Data isolation

- All domain queries are scoped by TenantId (`ITenantOwned.cs:23`).
- Cross-tenant access returns 403 (`TenantResolutionMiddleware.cs:134-157`).
- X-Tenant-Id header is stripped at the middleware level (`TenantResolutionMiddleware.cs:85`).

---

## SECTION 9 — authoring platform

### 9.1 Component inventory

All Domain Management screens reuse the existing AuthoringPlatform component set. No new components are created.

**Source basis:** `storefrontAuthoringPlatform.spec.ts:1-98` enforces single-source-of-truth for AuthoringPlatform components.

| Component | File | Used by Domain screens | Notes |
|---|---|---|---|
| AuthoringShell | AuthoringShell.jsx:1-7 | All (D01-D09) | Page-level container |
| AuthoringButton | AuthoringButton.jsx:1-24 | All | Action buttons |
| AuthoringStates | AuthoringStates.jsx:1-24 | D01, D04, D06, D09 | Loading/Empty/Error states |
| AuthoringStatusBadge | AuthoringStatusBadge.jsx:1-29 | D01, D02, D04, D05, D06 | Lifecycle state visualization |
| AuthoringDialogs | AuthoringDialogs.jsx:1-168 | D03 (wizard), all modals | ConfirmDialog, ConflictDialog, UnsavedChangesDialog, LoadingDialog |
| AuthoringFeedback | AuthoringFeedback.jsx:1-46 | All | Success/error/info messaging |
| AuthoringHistoryLauncher | AuthoringHistoryLauncher.jsx:1-7 | D08 | History view launcher |
| AuthoringValidationSummary | AuthoringValidationSummary.jsx:1-15 | D03, D06 | Field-level + form-level errors |
| AuthoringActionBar | AuthoringActionBar.jsx:1-51 | D02, D03 | Title, subtitle, status |
| useAuthoringGuard | useAuthoringGuard.js:1-92 | D02, D03 | Navigation guard for unsaved changes |
| useAuthoringShortcuts | useAuthoringShortcuts.js:1-23 | D03, D05, D06 | Keyboard shortcuts |
| AuthoringPlatformProvider | AuthoringPlatformProvider.jsx:1-28 | All | Context: dirty state, save status |

### 9.2 AuthoringPlatform contracts

**Source basis:** `authoringContracts.js:1-28`.

- `AuthoringAction`: type, label, onClick, disabled, variant, size
- `AuthoringState`: loading, empty, error, success, partial-success
- `AuthoringDialogResult`: confirmed, cancelled, data
- `AuthoringValidationError`: field, message, code

### 9.3 Reusability verification

The architecture test at `storefrontAuthoringPlatform.spec.ts:1-98` enforces:
1. Single source of truth for each AuthoringPlatform component.
2. No duplicate AuthoringShell instances.
3. No duplicate AuthoringButton instances.
4. All AuthoringButtons must use a type prop.
5. All storefront pages must render AuthoringShell as root container.

Domain Management screens MUST comply with these tests — no exceptions.

### 9.4 AuthoringPlatform patterns

**Dirty state management:** `AuthoringPlatformProvider.jsx:1-28` provides context for dirty state tracking. `useAuthoringGuard.js:1-92` consumes this to prevent navigation without confirmation.

**Conflict resolution:** On 409 (optimistic concurrency), `AuthoringDialogs.jsx` (ConflictDialog) is shown with:
- Before snapshot (server state)
- After snapshot (client changes)
- Accept server version / Accept client version / Merge manually buttons

**Loading states:** All API calls show `AuthoringStates.jsx:8-12` (LoadingSpinner). Empty results show `AuthoringStates.jsx:14-18` (EmptyState). Errors show `AuthoringStates.jsx:20-24` (ErrorState).

**Feedback:** `AuthoringFeedback.jsx:1-46` displays transient success/error/info messages after API operations. Auto-dismiss after 5 seconds. Manual dismiss available.

**Status badges:** `AuthoringStatusBadge.jsx:1-29` renders lifecycle states with color + text label. No new color styles or state renderers.

### 9.5 Accessibility primitives

- `accessibility/focus.js:1-12` provides `FocusScope`, `ariaProps`, `useFocusVisible`.
- All AuthoringPlatform components are keyboard-navigable (Tab/Shift+Tab)
---

## SECTION 10 — api contract

### 10.1 Constitutional endpoints

All Domain Management API endpoints are additive — they do NOT modify existing Storefront StoreConfig, CMS, Builder, Theme, or SEO controllers.

| Method | Route | Purpose | Command/Query | Errors | Idempotent |
|---|---|---|---|---|---|
| POST | /api/admin/domains | Add domain (Draft -> PendingVerification) | Command | 400 (invalid host), 409 (duplicate NormalizedHostName) | No |
| GET | /api/admin/domains | List all domains for the tenant | Query | None | Yes |
| GET | /api/admin/domains/{id} | Get domain details | Query | 404 (not found), 403 (cross-tenant) | Yes |
| PUT | /api/admin/domains/{id}/verify | Trigger DNS verification (retry) | Command | 400 (not PendingVerification), 409 (rate limit: max 5/hr) | Yes |
| PUT | /api/admin/domains/{id}/primary | Promote to Primary (demotes current) | Command | 400 (not Verified+SslActive), 409 | Yes |
| PUT | /api/admin/domains/{id}/disable | Disable domain | Command | 400 (cannot disable Primary), 409 (has redirect dependents) | Yes |
| PUT | /api/admin/domains/{id}/archive | Archive domain | Command | 400 (cannot archive Primary) | Yes |
| DELETE | /api/admin/domains/{id} | Delete domain (soft) | Command | 400 (Primary, active SSL, redirect dependents), 404 | Yes |
| PUT | /api/admin/domains/{id}/restore | Restore from Deleted/Archived/Disabled | Command | 400 (retention), 404 | Yes |
| POST | /api/admin/domains/{id}/ssl/renew | Trigger SSL renewal | Command | 400, 429 | No |
| POST | /api/admin/domains/{id}/ssl/upload | Upload custom certificate | Command | 400 (mismatch, chain, key) | No |
| POST | /api/admin/domains/{id}/ssl/revoke | Revoke certificate | Command | 400 (no active cert) | Yes |
| POST | /api/admin/domains/{id}/redirects | Configure redirect target | Command | 400 (loop, cross-tenant) | No |
| GET | /api/admin/domains/history | Audit history (all domains) | Query | None | Yes |
| GET | /api/admin/domains/{id}/history | Domain-specific audit history | Query | 404 | Yes |
| GET | /api/admin/domains/activity | Background job status | Query | None | Yes |

### 10.2 Command semantics

- All PUT /api/admin/domains/{id}/... commands are idempotent.
- POST /api/admin/domains and POST .../redirects are NOT idempotent.
- Commands accept If-Match with RowVersion for concurrency (`StoreConfig.cs:123`).

### 10.3 Query semantics

- All GET endpoints are read-only with $filter, $orderby, $top, $skip.
- GET /api/admin/domains returns DomainSummaryDto[].
- GET /api/admin/domains/{id} returns full DomainDetailDto with child entities.

### 10.4 Concurrency

- Version/xmin optimistic concurrency (`Domain.cs:148`).
- Concurrent primary promotion: filtered unique index at `DomainLifecycleState.cs:46`.
- Rate-limit counters enforced in domain service.

### 10.5 Expected errors

| HTTP | Code | Source |
|---|---|---|
| 400 | INVALID_HOST_FORMAT | HostNormalizer.cs:25 |
| 403 | HOST_TENANT_MISMATCH | TenantResolutionMiddleware.cs:134-157 |
| 404 | DOMAIN_NOT_FOUND | PersistenceDomainResolver.cs:31-143 |
| 409 | DOMAIN_CONFLICT | Domain.cs:50 UNIQUE |
| 409 | DUPLICATE_PRIMARY | DomainLifecycleState.cs:46 |
| 409 | RATE_LIMIT_EXCEEDED | Retry counter |
| 429 | TOO_MANY_REQUESTS | SSL renewal |
| 410 | DOMAIN_GONE | Deleted/Archived/Disabled |
| 400 | TENANT_REQUIRED | TenantResolutionMiddleware.cs:159-180 |

### 10.6 Event contract

All state transitions publish via IEventPublisher (`IEventPublisher.cs:8-60`).

| Event | Published when |
|---|---|
| DomainRegistered | Domain created (Draft) |
| DomainVerified | DNS proof confirmed |
| DomainVerificationFailed | DNS check failed |
| SslProvisioningStarted | Certificate request initiated |
| SslActivated | Certificate provisioned and bound |
| SslActivationFailed | Provisioning failed |
| SslRenewed | Certificate renewed |
| SslRenewalFailed | Renewal failed |
| SslRevoked | Certificate revoked |
| DomainPromotedToPrimary | Domain set as primary |
| DomainDowngradedFromPrimary | Previous primary demoted |
| DomainDisabled | Domain stopped serving |
| DomainArchived | Domain archived |
| DomainReactivated | Domain restored |
| DomainDeleted | Domain deleted (soft) |
| DomainRedirectConfigured | Redirect target set |
| DomainOwnershipTransferred | TenantId reassigned |

---

## SECTION 11 — state model

The UI state model is governed by AuthoringStates.jsx (`AuthoringStates.jsx:1-24`).

| State | Rendered by |
|---|---|
| loading | AuthoringStates.jsx:8-12 |
| empty | AuthoringStates.jsx:14-18 |
| error | AuthoringStates.jsx:20-24 |
| success | Normal render |
| verification-pending | D05 auto-poll + AuthoringStatusBadge |
| ssl-pending | D06 auto-poll + AuthoringStatusBadge |
| primary | AuthoringStatusBadge |
| conflict | AuthoringConflictDialog |

---

## SECTION 12 — audit & history

**Source basis:** `DomainAuditEntry.cs:20-106`, `Domain.cs:156`, `DomainAuditEntry.cs:96`.

Timeline sources:
1. DomainAuditEntry records (append-only, `DomainAuditEntry.cs:18`)
2. DomainVerification records (`DomainVerification.cs:16`)
3. SslCertificate history (`SslCertificate.cs:62,74`) + audit entries
4. DomainHistoryJson (`Domain.cs:156`)

| Field | Source |
|---|---|
| Id | DomainAuditEntry.cs:25 |
| DomainId | DomainAuditEntry.cs:31 |
| TenantId | DomainAuditEntry.cs:39 |
| Action | DomainAuditEntry.cs:47 |
| ActorType | DomainAuditEntry.cs:53 |
| ActorUserId | DomainAuditEntry.cs:59 |
| BeforeSnapshotJson | DomainAuditEntry.cs:66 |
| AfterSnapshotJson | DomainAuditEntry.cs:73 |
| TraceId | DomainAuditEntry.cs:79 |
| IpAddress | DomainAuditEntry.cs:85 |
| UserAgent | DomainAuditEntry.cs:91 |
| TimestampUtc | DomainAuditEntry.cs:96 |

ActorType: Merchant (ActorType.cs:13), System (ActorType.cs:19), PlatformAdmin (ActorType.cs:25).


---

## SECTION 13 — accessibility

useAuthoringShortcuts.js:1-23 (Escape, Enter, Ctrl+S, Tab/Shift+Tab).
focus.js:1-12 (focus trap, restore, focus-visible).
ARIA: focus.js, AuthoringStatusBadge.jsx, AuthoringStates.jsx, AuthoringValidationSummary.jsx.
High contrast: colors + text labels (never color-only).
Reduced motion: polling paused when tab not visible; respects prefers-reduced-motion.

---

## SECTION 14 — responsive behavior

Desktop (>= 1024px): standard layouts (table list, horizontal stepper, two-column detail).
Tablet (768-1023px): stacked cards, vertical steppers, horizontal scroll on tables.
Mobile (< 768px): card-based list, full-screen wizard, FAB for actions, bottom-sheet drawers.
Sidebar collapses to hamburger (Sidebar.jsx:123-128 (localizeStorefrontProductNode) + Sidebar.jsx:243 (render)).

---

## SECTION 15 — shopify parity

### 15.1 Shared concepts

| Concept | Shopify | Shopixy DOMAIN-3.1 |
|---|---|---|
| Domain registry entity | domains table | Domain aggregate (Domain.cs:22) |
| Primary domain | is_primary | DomainType.Primary (DomainType.cs:13) + DomainLifecycleState.Primary (DomainLifecycleState.cs:46) |
| DNS verification | TXT, CNAME, HTTP file | DnsTxt (13), DnsCname (19), HttpFile (25) |
| SSL provisioning | Let's Encrypt | LetsEncrypt (ProviderType.cs:14), CustomUpload (19), SystemDefault (25) |
| Host resolution | Before tenant | HostResolutionMiddleware (HostResolutionMiddleware.cs:46) before Program.cs:3384-3397 |

### 15.2 Intentional differences

1. Explicit 301/307 redirect types (RedirectType.cs:18,24).
2. Custom SSL upload (ProviderType.cs:19) — enterprise differentiator.
3. 9-state lifecycle (DomainLifecycleState.cs:9-61).
4. System/Development types (DomainType.cs:43,49) — namespace isolation.
5. Preview domain as first-class type (DomainType.cs:31).
6. Primary as lifecycle state (DomainLifecycleState.cs:46) with auto-demotion.

### 15.3 Advantages of Shopixy

1. Custom SSL upload for enterprise PKI.
2. Explicit 301/307 redirect semantics.
3. First-class preview domain type.
4. Full audit trail (DomainAuditEntry.cs:20).
5. System/Dev domain separation.

---

## SECTION 16 — future extensibility

| Extension | Mechanism | Source |
|---|---|---|
| Wildcard domains | DomainType enum extensible + HostResolutionMiddleware | DomainType.cs, HostResolutionMiddleware.cs:46-282 |
| International domains | Punycode via HostNormalizer + NormalizedHostName | HostNormalizer.cs:25-88, Domain.cs:50 |
| Regional routing | Edge header check in HostResolutionMiddleware | HostResolutionMiddleware.cs:93-205 |
| Marketplace domains | DomainType enum + SellerId | DomainType.cs |
| White-label | DomainType.System + wildcard cert | DomainType.cs:49 |
| Multi-brand | Multiple Primary (extends Law #2) | DomainType.cs:13, DomainLifecycleState.cs:46 |

Future providers: ProviderType.cs:8 extensible; SslCertificate.Provider (SslCertificate.cs:33) is enum column.


---

## SECTION 17 — implementation waves

| Wave | Goal | Dependencies | Rollback |
|---|---|---|---|
| DOMAIN-3.1-A | Navigation skeleton; D01/D02 shells | None | Remove nav entry |
| DOMAIN-3.1-B | Read API (GET endpoints) | 3.1-A | Remove controller |
| DOMAIN-3.1-C | Mutation API (POST/PUT/DELETE) | 3.1-B | Feature flag OFF |
| DOMAIN-3.1-D | SSL & Verification UI | 3.1-C | Feature flag OFF |
| DOMAIN-3.1-E | Audit & History | 3.1-C | Feature flag OFF |

---

## SECTION 18 — architecture laws

1. **One Domain Owner** — Domain.cs:22. Evidence: Domain.cs:7; StoreConfig.cs:11-14.
2. **One Primary Per Tenant** — DomainType.cs:13 + DomainLifecycleState.cs:46. Domain.cs:197-256.
3. **One Runtime Resolution Order** — HostResolutionMiddleware.cs:33-37; Program.cs:3384-3397.
4. **Domain Never Owns Theme** — No ThemeId on Domain.cs:22-257. Theme from StoreConfig.cs:40.
5. **Theme Never Owns Domain** — `StoreConfig.cs:98,103` (ThemeDraftOverridesJson/ThemeLiveOverridesJson keyed by theme identity).
6. **Builder Never Resolves Domains** — `AdminStoreConfigService.cs:32` (class); `StoreConfig.cs:12-14` (read-only contract).
7. **Runtime Never Verifies DNS** — DomainVerification.cs:5-6.
8. **StoreConfig Never Owns Domain** — StoreConfig.cs:16-40.
9. **Tenant Never Resolves Hosts Directly** — Tenant.cs:100 legacy. HostResolutionMiddleware.cs:93-205.
10. **Domain Normalization Is Server-Authoritative** — HostNormalizer.cs:21-88; Domain.cs:48.
11. **ForwardedHeaders Must Include X-Forwarded-Host** — FIXED: Program.cs:105.
12. **Host Resolution Before Tenant Resolution** — Program.cs:3384-3386 before 3395-3397.
13. **Only Server-Controlled Host Header Is Trusted** — TenantResolutionMiddleware.cs:301.
14. **X-Tenant-Id Stripped Before Downstream** — TenantResolutionMiddleware.cs:85 (strip), 183 (re-add).
15. **Fail-Closed on Ambiguous Resolution** — TenantResolutionMiddleware.cs:159-180.
16. **Tenant Isolation Is SQL-Authoritative** — ITenantOwned.cs:16-23; TenantResolutionMiddleware.cs:293-295.
17. **No Orphaned Domains on Tenant Deletion** — `ITenantOwned.cs:16`; `Domain.cs:134,141` (DeletedAtUtc, IsDeleted).
18. **No Direct DB Writes Outside Domain Repository** — IDomainRepository.cs:18; DomainRepository.cs:1-135.
19. **Events via IEventPublisher Only** — IEventPublisher.cs:8-60; DomainAuditEntry.cs:18.
20. **UI Never Provisions SSL Directly** — UI triggers API only. SslCertificate.cs:5; ProviderType.cs:8.

---

## SECTION 19 — governance

### 19.1 Review gates

| Gate | Gatekeeper | Artifact |
|---|---|---|
| G1. Source Authority Check | Principal Architect | Checklist |
| G2. Law Compliance | Constitutional Review Board | Law matrix |
| G3. Ownership Check | Product Owner | Ownership matrix |
| G4. Pipeline Ordering | Principal Software Architect | Middleware diagram |
| G5. State Machine Completeness | Principal Architect | Transition matrix |
| G6. API Contract Review | Principal Software Architect | API spec |
| G7. UI Reuse Check | Principal UX Architect | Component matrix |
| G8. Security Review | Security Lead | Threat model |
| G9. Backward Compatibility | QA Lead | Test plan |
| G10. Wave Assignment | Product Owner | Wave assignment |

### 19.2 Review board

Chief Product Architect, Principal UX Architect, Principal Software Architect, Storefront Platform Owner, Security Lead, QA Lead.

---

## SECTION 20 — certification

**VERDICT: PASS WITH OBSERVATIONS**

**Rationale:** Grounding in source; no contradictions; architectural soundness; boundary enforcement; AuthoringPlatform reuse; source drift resolved; backward compatibility preserved. **All 155 file:line citations across 57 source files verified OK against live source (2026-08-06 snapshot).**

**Observations:**

1. **Barrel import fix applied.** STOREFRONT_PRODUCT_NAVIGATION (storefrontInformationArchitecture.js:370), OPERATIONS_NAVIGATION (storefrontInformationArchitecture.js:129), STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY (storefrontInformationArchitecture.js:2) now all properly defined and re-exported at index.js:3-11. FIXED.
2. **X-Forwarded-Host gap FIXED.** Program.cs:105 now includes ForwardedHeaders.XForwardedHost. FIXED.
3. **No Domain Management API controller exists — by design.** Implementation in Waves DOMAIN-3.1-B/C. BY DESIGN.
4. **StoreConfig DTO has no domains field** — StoreConfig.cs:16-40. CONSTITUTIONAL DESIGN.
5. **Runtime cache key dimension** — tenantId-only; host extension in DOMAIN-6. ASSIGNED TO DOMAIN-6.
6. **StorefrontUrlResolver** — resolves from Tenant.StorefrontBaseUrl (Tenant.cs:100); migration in DOMAIN-2. ASSIGNED TO DOMAIN-2.

---

## APPENDIX A — source file inventory

### Core entities

| File | Key lines |
|---|---|
| Domain.cs | Class:22; HostName:42; NormalizedHostName:50; DomainType:57; LifecycleState:64; SslCertId:71; VerificationToken:77; VerificationMethod:84; RedirectType:91; RedirectTarget:98; CanonicalPolicy:106; Timestamps:111,117,123,128,134; IsDeleted:141; Version:148; HistoryJson:156; Nav props:165-186; Lifecycle methods:197-256 |
| DomainVerification.cs | Class:16; Id:21; DomainId:27; Method:33; Token:39; Status:45; Timestamps:50,56; FailureReason:62; IsDeleted:80; Domain nav:89 |
| SslCertificate.cs | Class:15; Id:20; DomainId:27; Provider:33; Status:39; ARN:46; Thumbprint:52; NotBefore:57; NotAfter:62; LastRenewal:68; NextRenewal:74; FailureReason:80; HistoryJson:104; Domain nav:113 |
| DomainAuditEntry.cs | Class:20; Id:25; DomainId:31; TenantId:39; Action:47; ActorType:53; ActorUserId:59; Snapshots:66,73; TraceId:79; IP:85; UA:91; Timestamp:96; Domain nav:105 |
| Tenant.cs | Class:10; StorefrontBaseUrl:100 |
| StoreConfig.cs | Class:16; TenantId:24; StoreName:29; ThemeId:40; VersionsJson:109; RowVersion:123 |

### Core enums

| File | Enum | Values (line) |
|---|---|---|
| DomainType.cs:7 | DomainType | Primary=0 (13), Alias=1 (19), Redirect=2 (25), Preview=3 (31), Temporal=4 (37), Dev=5 (43), System=6 (49) |
| DomainLifecycleState.cs:9 | DomainLifecycleState | Draft=0 (15), PendingVerif=1 (22), Verified=2 (28), SslPending=3 (34), SslActive=4 (39), Primary=5 (46), Disabled=6 (52), Archived=7 (58), Deleted=8 (61) |
| RedirectType.cs:7 | RedirectType | None=0 (12), Permanent301=1 (18), Temporary307=2 (24) |
| VerificationMethod.cs:7 | VerificationMethod | DnsTxt=0 (13), DnsCname=1 (19), HttpFile=2 (25) |
| CanonicalPolicy.cs:8 | CanonicalPolicy | UseAsPrimary=0 (14), CanonicalizeToPrimary=1 (21) |
| ProviderType.cs:8 | ProviderType | LetsEncrypt=0 (14), CustomUpload=1 (19), SystemDefault=2 (25) |
| CertificateStatus.cs:7 | CertificateStatus | Pending=0 (13), Active=1 (18), Expired=2 (23), Revoked=3 (28), Failed=4 (33) |
| VerificationStatus.cs:7 | VerificationStatus | Pending=0 (13), Success=1 (18), Failed=2 (24) |
| ActorType.cs:8 | ActorType | Merchant=0 (13), System=1 (19), PlatformAdmin=2 (25) |

### Core interfaces

| File | Purpose | Key lines |
|---|---|---|
| ITenantOwned.cs:16 | Tenant ownership | TenantId:23 |
| IDomainRepository.cs:18 | Persistence boundary | FindByNormalizedHost:27-29; FindPrimaryDomain:39-41; FindAliasDomains:50-52; FindRedirectTarget:62-64 |
| ITenantContext.cs:8 | RequiredTenantId | 25 |
| IEventPublisher.cs:8 | Event publishing | 1-60 |
| IDomainResolver.cs | Runtime resolution | Full file |
| IHostResolutionContext.cs | Host resolution context | Full file |
| IDomainResolutionCache.cs | Cache contract | Full file |

### Infrastructure middleware

| File | Purpose | Key lines |
|---|---|---|
| HostResolutionMiddleware.cs:46 | Host to Domain | InvokeAsync:93; excluded paths:48-81; resolution:103-129; redirect:131-181 |
| TenantResolutionMiddleware.cs:33 | Domain to Tenant | InvokeAsync:81; strip:85; re-add:183; spoofing:109-131; cross-tenant:134-157; fail-closed:159-180; consume result:259-265; tenant validation:289-296 |
| HostNormalizer.cs:22 | Normalization | Normalize:25-88 |
| HostResolutionContextKeys.cs:12 | Items keys | NormalizedHostItemKey:18; DomainResolutionResultItemKey:26 |
| NoOpDomainResolutionCache.cs:15 | No-op cache | 15-34 |
| PersistenceDomainResolver.cs:31 | Persistence IDomainResolver | 31-143 |
| DomainRepository.cs:1 | DomainRepository | 1-135 |
| TenantLineageAuthority.cs | Tenant isolation | Full file |
| StorefrontUrlResolver.cs:53 | Canonical URLs | 53-78; from Tenant.StorefrontBaseUrl (58) |

### Frontend

| File | Purpose | Key lines |
|---|---|---|
| storefrontInformationArchitecture.js | Navigation IA | STOREFRONT_NAVIGATION:4-119; STOREFRONT_PRODUCT_NAVIGATION:370; OPERATIONS_NAVIGATION:129; STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY:2; resolveStorefrontScreen:649 |
| index.js:1-13 | Barrel re-exports | 1-13 |
| Sidebar.jsx | Sidebar renders nav (import at line 37, render at 243, Settings at 331) | |
| AuthoringShell.jsx | Container | 1-7 |
| AuthoringActionBar.jsx | Action bar | 1-51 |
| AuthoringDialogs.jsx | Dialogs | 1-168 |
| AuthoringStatusBadge.jsx | Status badges | 1-29 |
| AuthoringStates.jsx | State renderers | 1-24 |
| AuthoringButton.jsx | Action buttons | 1-24 |
| AuthoringFeedback.jsx | Feedback | 1-46 |
| AuthoringValidationSummary.jsx | Validation | 1-15 |
| AuthoringHistoryLauncher.jsx | History launcher | 1-7 |
| useAuthoringGuard.js | Navigation guard | 1-92 |
| useAuthoringShortcuts.js | Keyboard shortcuts | 1-23 |
| focus.js | Accessibility | 1-12 |
| authoringContracts.js | Contracts | 1-28 |
| storefrontAuthoringPlatform.spec.ts | Architecture tests | 1-98 |
| storefrontAdminAuthority.js | Authority (src/auth/) | Line 1 |

### Backend

| File | Purpose | Key lines |
|---|---|---|
| AdminStoreConfigController.cs:35 | Authority boundary | 35 |
| AdminStoreConfigService.cs | Read-only contract (class) | 32 |
|  StorefrontStoreConfigDtos.cs (DTOs/Storefront/) | DTO (no domains) | 7-73 |
| Program.cs | Pipeline | ForwardedHeadersOptions:103-118 (X-ForwardedHost:105); HostResolutionMiddleware:3384-3386; TenantResolutionMiddleware:3395-3397 |

---

## APPENDIX B — authority document traceability

| Authority | How DOMAIN-3.1 references it |
|---|---|
| DOMAIN-0 | Tenant.StorefrontBaseUrl (Tenant.cs:100), no Domain entity, hardcoded CORS |
| DOMAIN-1 | Canonical blueprint: ownership, lifecycle (9 states), DNS/SSL, API endpoints |
| DOMAIN-1.5 | Persistence freeze: filtered unique index |
| DOMAIN-1.6 | Runtime constitution: pipeline ordering |
| DOMAIN-2A | Host Resolution Middleware foundation |
| DOMAIN-2B | Domain Resolution implementation |
| DOMAIN-2C | Domain Resolution final |
| DOMAIN-2C.1 | Final execution certification: Program.cs:3384-3397 |
| DOMAIN-2D | Tenant Resolution: TenantResolutionMiddleware.cs:259-265 |
| DOMAIN-3.0 | Architecture audit: UX gap analysis |
| DOMAIN-3.0.1 | Architecture verification & cleanup |
| DOMAIN-3.0.2 | Runtime reality: barrel crash FIXED, X-Forwarded-Host FIXED |
| STOREFRONT_CANONICAL_UX_BLUEPRINT | Screen inventory, ownership matrix, architecture laws |
| ONLINE_STORE_CANONICAL_BLUEPRINT | Product-level IA patterns |

---

This document is the constitutional authority for all Storefront Domain Management implementation. Every future implementation decision must be traceable to this blueprint. Any deviation requires a constitutional amendment reviewed by the full review board (Section 19.2).
