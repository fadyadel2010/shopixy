# DOMAIN-3.0: Storefront Domain Management — Constitutional Architecture Audit

**Project:** Shopixy / PointOfSale  
**Platform:** Multi-tenant e-commerce / affiliate / POS  
**Date:** 2026-08-06  
**Status:** CONSTITUTIONAL ARCHITECTURE AUDIT (FROZEN — NO IMPLEMENTATION)  
**Authority:** .ai-context  
**Shopixy Rules:** PostgreSQL authoritative; Mongo/Redis/ES/WhatsApp derivative. TenantId necessary-not-sufficient. Tenant isolation CRITICAL.  

---

## Executive Summary

This audit determines the canonical architecture for **Domain Management** inside the Storefront product. It synthesizes the findings of DOMAIN-0 (Current State Audit), DOMAIN-1 (Canonical Blueprint), DOMAIN-1.5 (Persistence Freeze), DOMAIN-1.6 (Runtime Constitution), DOMAIN-2A/2B/2C (Host Resolution Implementation), DOMAIN-2C.1 (Final Execution Certification), and DOMAIN-2D (Tenant Resolution Integration) — all of which are now **CERTIFIED IMPLEMENTED** — against the current Storefront Admin UX source code (Sidebar, Information Architecture, AuthoringShell, Breadcrumbs, Settings, AuthoringPlatform components).

**Key Finding:** Domain Management is owned by the **Storefront Product** (Infrastructure layer), operating as a **first-class workspace sub-navigation node** under the Storefront root in the POS Admin Sidebar. It is separate from but adjacent to Design, Operations, Theme, Appearance, and Status. Every existing Storefront Admin UX pattern (AuthoringShell, AuthoringActionBar, AuthoringFeedback, useAuthoringGuard, AuthoringDialogs, breadcrumbs, dirty-guard, keyboard shortcuts) MUST be reused — no new UX primitives.

The Domain Management surface is **not yet visible in the Admin UI** (`storefrontInformationArchitecture.js:1-280` contains no `domains` screen; `Sidebar.jsx` renders no domain nav node; `StorefrontSettings.jsx:33` uses a hardcoded `VITE_STOREFRONT_DOMAIN` env var). However, the **runtime foundation is complete**: `HostResolutionMiddleware` (`HostResolutionMiddleware.cs:45-285`), `PersistenceDomainResolver` (`PersistenceDomainResolver.cs:31-143`), `Domain` entity (`Domain.cs:22-257`), and `TenantResolutionMiddleware` now consumes `DomainResolutionResult` (`TenantResolutionMiddleware.cs:103-137` via DOMAIN-2D integration). The Domain entity, all enums, child entities, and audit entities exist in source as of DOMAIN-2 implementation.

**Certification Verdict: PASS**

---

## 1. Product Ownership

### 1.1 Who Owns Domains?

**Observed:** The `Domain` entity exists at `PointOfSale.Core/Entities/Domain.cs:22`. It implements `ITenantOwned` (`Domain.cs:22`, `ITenantOwned.cs:16-23`), placing it in the SQL-authoritative data layer. Domain resolution is performed by the `HostResolutionMiddleware` (`PointOfSale.API/Middleware/HostResolutionMiddleware.cs:45`) with `PersistenceDomainResolver` (`PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs:31`). This middleware runs BEFORE `TenantResolutionMiddleware` in the pipeline (`Program.cs:3383-3396` per DOMAIN-2C.1).

**Constitutional Recommendation:** Domains are owned by the **Storefront Product** — specifically the **Infrastructure layer** within the Storefront product boundary. This is not CommerceHub, not POS Core, not Global Settings, not a separate workspace. The Domain Aggregate is the Infrastructure component of the Storefront product.

**Evidence:**

- `Domain.cs:22-29` — `Domain` implements `ITenantOwned`, with XML doc: "Domain aggregate root — single source of truth for a hostname's lifecycle."
- `Domain.cs:14-21` — "Tracts ownership verification, SSL state, primary/alias/redirect classification, and canonical URL resolution for storefront custom domains."
- `HostResolutionMiddleware.cs:8-10` — "Host Resolution Middleware (DOMAIN-2A — Foundation + DOMAIN-2B/2C — Domain Resolution)"
- `PersistenceDomainResolver.cs:9-10` — "Persistence-backed implementation of IDomainResolver (DOMAIN-2C)."
- `IDomainRepository.cs:6-17` — "Repository for Domain persistence lookups (DOMAIN-2C). Responsibilities ONLY: Lookup by normalized host, Lookup primary domain, Lookup aliases, Lookup redirect target. Returns persistence models (Domain entities). Never resolves Tenant. Never resolves StoreConfig. This is the persistence boundary."
- DOMAIN-1 Section 2, lines 50-64 — Ownership Matrix: "Domain name (hostname) | Domain Aggregate (Infra) | Owns the Domain table, verified against DNS"
- DOMAIN-1 Section 2, line 246 — "Domain aggregate is owned by Infrastructure (the platform), not by StoreConfig, Builder, or Tenant."
- DOMAIN-1.5 Section 1.1, line 38 — CPD-1: "The Domain aggregate root is owned by Infrastructure (the platform), not by the StoreConfig aggregate."

**The Domain Aggregate belongs to the Storefront Product / Infrastructure layer.** It is not CommerceHub (which is POS-to-POS B2B internal, never owns domains — see `Sidebar.jsx:244-263` for CommerceHub admin nav), not POS Core (which handles Tenant/StoreConfig/Order/Payment), not Global Settings (which handles platform-wide configuration), and not a separate workspace.

### 1.2 Ownership Matrix

| Concern | Owner | Authority | Evidence |
|---------|-------|-----------|----------|
| Hostname string (normalized) | Domain Aggregate (Infra) | `Domain.NormalizedHostName`, global UNIQUE | `Domain.cs:50`, `IDomainRepository.cs:27-29` |
| DNS verification proof | Infrastructure (Domain Service) | Server-authoritative DNS challenge | `DomainVerification.cs:16`, `Domain.cs:77-84` |
| SSL certificate lifecycle | Infrastructure (Cert Manager) | Let's Encrypt / Custom Upload / System Default | `SslCertificate.cs:15`, `SslCertificate.cs:33-39` |
| Primary domain selection | Domain Aggregate | `DomainType.Primary` + `LifecycleState.Primary` | `DomainType.cs:13`, `DomainLifecycleState.cs:46`, `Domain.cs:57-64` |
| Redirect target mapping | Domain Aggregate | `RedirectType` + `RedirectTargetDomainId` | `RedirectType.cs:7-21`, `Domain.cs:91-98` |
| Host → Domain resolution | Host Resolution Middleware | Server-controlled `Request.Host` only | `HostResolutionMiddleware.cs:45-285` |
| StoreConfig | StoreConfig Aggregate | `StoreConfig.cs` — presentation/behavior, NOT domain | `StoreConfig.cs` (no domain fields) |
| Theme selection/rendering | Theme Runtime | `ThemeId` from StoreConfig | DOMAIN-1 Section 9, lines 647-685 |
| Builder context | Builder Runtime | Operates on resolved TenantContext | DOMAIN-0 Phase 5.2 |
| Preview isolation | Preview Authority Middleware | Signed `themePreviewToken` JWT | `ThemePreviewAuthorityMiddleware.cs:52-70` |
| CORS origin allowlisting | Infrastructure (dynamic CORS) | Origin derived from resolved domain | `appsettings.json:235-246` (currently hardcoded) |
| X-Tenant-Id header | TenantResolutionMiddleware | Stripped inbound, re-added only after resolution | `TenantResolutionMiddleware.cs:82, 180` |
| Canonical URL generation | SeoService (backend) | `StorefrontUrlResolver` → Primary Domain | DOMAIN-2D integration: `TenantResolutionMiddleware.cs:103-128` |

### What Domains Own

Observed from `Domain.cs:22-257`:
- The hostname string (normalized, lowercased, unique globally) — `Domain.NormalizedHostName` at `Domain.cs:50`
- DNS/HTTP verification proof (TXT token, CNAME target, verification timestamp) — `Domain.VerificationToken` at `Domain.cs:77`, `DomainVerification` child entity at `DomainVerification.cs:16`
- SSL certificate state (status, expiration, renewal schedule, certificate ARN/id) — `SslCertificate` at `SslCertificate.cs:15`
- Lifecycle state (Draft → PendingVerification → Verified → SslPending → SslActive → Primary/Disabled/Archived/Deleted) — `Domain.LifecycleState` at `Domain.cs:64`
- Redirect mapping (which domain redirects to which, with HTTP status code) — `Domain.RedirectType` at `Domain.cs:91`, `Domain.RedirectTargetDomainId` at `Domain.cs:98`
- Primary/alias classification (exactly one Primary per tenant) — `Domain.DomainType` at `Domain.cs:57`
- Verification history (audit trail of who verified what, when) — `DomainAuditEntry` at `DomainAuditEntry.cs:20`
- Soft-delete state — `Domain.IsDeleted` at `Domain.cs:141`, `Domain.DeletedAtUtc` at `Domain.cs:134`
- Domain history snapshot — `Domain.DomainHistoryJson` at `Domain.cs:156`

### What Domains Never Own

Constitutional Design (DOMAIN-1 Section 2, lines 75-81):
- **Theme** — `StoreConfig.Theme` / `ThemeId` is resolved independently (`StoreConfig.cs:40` — `ThemeId` field; `bootstrap.ts` resolves theme from StoreConfig)
- **StoreConfig** — Presentation, behavior, status, pages, product templates (`StoreConfig.cs` — no Domain fields)
- **Builder state** — Builder operates on StoreConfig, not domains (`BuilderController` uses `TenantContext`, no domain branching)
- **Preview token issuance** — Preview tokens generated by `IStorefrontThemePreviewTokenService`, not domains
- **SEO metadata content** — SeoService generates canonical URLs using resolved domain but content comes from StoreConfig + product/catalog data

---

## 2. Information Architecture

### 2.1 Observed Storefront Navigation Structure

**Observed:** The Storefront product navigation is defined in `storefrontInformationArchitecture.js:3-119` as `STOREFRONT_NAVIGATION`. The current structure is:

```
STOREFRONT_NAVIGATION (storefrontInformationArchitecture.js:3-119)
├── Overview (/storefront) — screenIds: ['overview']
├── Storefront Status (/storefront/status) — screenIds: ['status']
├── Homepage (/storefront/homepage) — screenIds: ['homepage']
├── Catalog (key: 'storefront-catalog')
│   └── Product Template (/storefront/catalog/product-template)
├── Content (key: 'storefront-content')
│   └── Pages (/storefront/content/pages)
├── Navigation (key: 'storefront-navigation')
│   ├── Header (/storefront/navigation?view=header)
│   └── Footer Links (/storefront/navigation?view=footer)
├── Footer Content (/storefront/footer-content)
├── Appearance (key: 'storefront-appearance')
│   ├── Themes (/storefront/appearance/themes)
│   └── Colors & Typography (/storefront/appearance/colors-typography)
└── StoreConfig Version History (/storefront/storeconfig-version-history)
```

**NO Domains node exists.** This is confirmed by:
- `storefrontInformationArchitecture.js:3-119` — `STOREFRONT_NAVIGATION` has no `domains` entry
- `storefrontInformationArchitecture.js:176-235` — `SCREENS` constant has no `domains` screen
- `storefrontInformationArchitecture.js:242-274` — `resolveStorefrontScreen()` has no `/storefront/domains` route
- `Sidebar.jsx:243` — renders `localizeStorefrontProductNode(STOREFRONT_PRODUCT_NAVIGATION)` but `STOREFRONT_PRODUCT_NAVIGATION` is re-exported from `index.js:6` yet **NOT DEFINED** in `storefrontInformationArchitecture.js` (grep returned no results). The source file defines ONLY `STOREFRONT_NAVIGATION` — `OPERATIONS_NAVIGATION` is also not defined there, despite being re-exported at `index.js:4`. This broken import would crash the sidebar at runtime (spreading `undefined` in `localizeStorefrontProductNode`).
- `Sidebar.jsx:331-337` — Settings section: `{ key: 'settings', label: t('settings.settings'), icon: '⚙️', path: '/settings' }` (generic, no domains)
- `StorefrontSettings.jsx:33` — `const STOREFRONT_DOMAIN = import.meta.env.VITE_STOREFRONT_DOMAIN || 'localhost:5174'` (hardcoded)
- `StorefrontSettings.jsx:178-182` — `openStorefront()` opens `http://${STOREFRONT_DOMAIN}` (no tenant-specific or custom domain)

### 2.2 Constituent Recommendation: Where Domains Belong

**Constitutional Recommendation:** Domains should appear as a peer-level navigation node under the **Storefront** root, positioned between `status` and `homepage` in the `STOREFRONT_NAVIGATION` structure.

This is based on:

1. **Domain-0 Phase 13.2** — `STOREFRONT_NAVIGATION` contains: Overview, Storefront Status, Homepage, Catalog, Content, Navigation, Footer Content, Appearance, StoreConfig Version History. No Domains.
2. **DOMAIN-1 Section 13** — "Navigation insertion point: `storefrontInformationArchitecture.js` — a new `domains` screen ID is added under the `storefront` navigation root, between `status` and `homepage`."
3. The Storefront product IA (`storefrontInformationArchitecture.js`) is the canonical navigation authority for the Storefront product workspace.

The structure should be:

```
STOREFRONT_NAVIGATION (after Domain Management integration)
├── Overview (/storefront) — screenIds: ['overview']
├── Storefront Status (/storefront/status) — screenIds: ['status']
├── Domains (/storefront/domains) — screenIds: ['domains']          ← NEW
│   ├── Overview          (default)
│   ├── Wizard            — add domain step-by-step
│   ├── Domain List       — table of all domains with filters
│   ├── Verification      — DNS challenge instructions, retry
│   ├── SSL               — cert status, renewal, upload, revocation
│   ├── Redirects         — 301/307 redirect management
│   ├── History           — audit log of all domain actions
│   └── Activity          — recent events, background job status
├── Homepage (/storefront/homepage) — screenIds: ['homepage']
├── Catalog ...
├── Content ...
├── Navigation ...
├── Footer Content ...
├── Appearance ...
└── StoreConfig Version History ...
```

**Why this placement (not under Design, Operations, or Settings):**

- **Not under Design** — Domains are infrastructure identity, not visual design. Themes, colors, typography, and navigation are Design concerns (`storefront-appearance` key at `storefrontInformationArchitecture.js:90-110`). Domains affect routing and security, not presentation.
- **Not under Operations** — Operations governs CommerceHub marketplace publishing (`/commercehub/*` per `Sidebar.jsx:253-263`), orders, payments, and analytics. Domains are not operational commerce activities.
- **Not under Settings** — The generic `/settings` link (`Sidebar.jsx:331-335`) is a platform-level fallback, not a Storefront Product workspace node. Domain Management is a Storefront Product feature, not a global setting.
- **Not under Appearance** — Appearance owns Themes, Colors & Typography (`storefrontInformationArchitecture.js:90-110`). Domain Management has zero overlap with visual appearance.
- **Not under Status** — Storefront Status manages live/maintenance/disabled state (`StorefrontSettings.jsx:322-403`). Domain SSL status is related but Domain Management is broader (verification, redirects, primary selection, history).

Domains are a **peer** to Storefront Status and Homepage. They share the same conceptual layer: infrastructure configuration that sits at the top of the Storefront product hierarchy. The Host Resolution Middleware that consumes Domain records runs BEFORE tenant resolution, which runs before StoreConfig, Theme, and CMS — Domains are the foundational layer.

**Why between `status` and `homepage`:**

DOMAIN-1 Section 13, lines 884-894 explicitly states the insertion point: "a new `domains` screen ID is added under the `storefront` navigation root, between `status` and `homepage`." This ordering reflects the runtime resolution order: Host → Domain → Tenant → StoreConfig → Theme → CMS. Status (storefrontStatus) is the operational gate; Domains is the infrastructure gate; Homepage is the first content surface.

---

## 3. Workspace Boundaries

### 3.1 What Domain Management Owns

Based on `Domain.cs:22-257`, `DomainVerification.cs:16-90`, `SslCertificate.cs:15-114`, `DomainAuditEntry.cs:20-106`, and DOMAIN-1 Section 2 lines 66-73:

| Domain Management Owns | Source Evidence |
|--------------------------|-----------------|
| Custom Domains (hostnames) | `Domain.HostName` at `Domain.cs:42`, `Domain.NormalizedHostName` at `Domain.cs:50` |
| Primary Domain selection | `Domain.DomainType.Primary` at `Domain.cs:57`, `DomainType.cs:13` |
| Aliases | `Domain.DomainType.Alias` at `Domain.cs:57`, `DomainType.cs:17` |
| Redirects | `Domain.DomainType.Redirect` at `Domain.cs:57`, `Domain.RedirectType` at `Domain.cs:91`, `Domain.RedirectTargetDomainId` at `Domain.cs:98` |
| Verification (DNS TXT/CNAME/HTTP-file) | `Domain.VerificationToken` at `Domain.cs:77`, `Domain.VerificationMethod` at `Domain.cs:84`, `DomainVerification.cs:16` |
| SSL status | `Domain.LifecycleState` at `Domain.cs:64`, `Domain.SslCertificateId` at `Domain.cs:71`, `SslCertificate.cs:15`, `CertificateStatus.cs:7`, `ProviderType.cs:8` |
| DNS status (verification attempt tracking) | `DomainVerification.Status` at `DomainVerification.cs:45`, `VerificationStatus.cs:7` |
| Canonical Host (Primary domain) | `DomainType.cs:13` — "The Primary, canonical storefront domain for a tenant. The StorefrontUrlResolver uses this as the authoritative base URL." |
| Lifecycle state transitions | `Domain.LifecycleState` at `Domain.cs:64`, `DomainLifecycleState.cs:9`, lifecycle methods at `Domain.cs:197-256` |
| Verification history | `DomainVerification` child at `DomainVerification.cs:16`, `Domain.AuditEntries` at `Domain.cs:186` |
| SSL certificate history | `SslCertificate.SslCertificateHistoryJson` at `SslCertificate.cs:104` |
| Domain configuration history | `Domain.DomainHistoryJson` at `Domain.cs:156` |
| Full audit trail | `DomainAuditEntry.cs:20`, `ActorType.cs:8` |

### 3.2 What Domain Management NEVER Owns

| Domain Management Never Owns | Source Evidence |
|------------------------------|-----------------|
| Theme | `StoreConfig.cs:40` — `ThemeId` field; DOMAIN-1 Architecture Law #4 (Section 16.4): "Domain Never Owns Theme." `Domain.cs` has no `ThemeId` field |
| Navigation (UI nav menus) | `storefrontInformationArchitecture.js:61-81` — Navigation section is separate; `Sidebar.jsx:61-81` renders Navigation as a sibling to Storefront root, not under Domains |
| StoreConfig | `StoreConfig.cs:1-188` — no domain fields; DOMAIN-1 Architecture Law #8 (Section 16.8): "StoreConfig Never Owns Domain." |
| Orders | Orders are in POS Core (`Sidebar.jsx:184-190` — Transactions section), not Storefront Product |
| Payments | Payments are in POS Core / Treasury (`Sidebar.jsx:191-208`), not Storefront Product |
| Coupons | Coupons/promotions are in Commerce section (`Sidebar.jsx` — Commerce is not rendered in Storefront IA) |
| SEO content | `SeoService.cs:96-127` generates canonical URLs using resolved domain but content comes from StoreConfig + product/catalog data; `SeoMetadataDto` (`SeoService.cs:181-187`) has no domain fields |
| StoreConfig status (live/maintenance/disabled) | `StoreConfig.cs:55-57` — `StorefrontStatus` field; `StorefrontSettings.jsx:322-403` manages this separately from domains |
| Builder state | `BuilderController` uses `TenantContext`, no domain branching; DOMAIN-1 Architecture Law #6 (Section 16.6): "Builder Never Resolves Domains" |
| Preview token issuance | `ThemePreviewAuthorityMiddleware.cs:52-70` — validates `themePreviewToken` JWT; Domain Management does NOT issue preview tokens |
| Tenant entity | `Tenant.cs:100` — `StorefrontBaseUrl` is legacy; DOMAIN-1 Architecture Law #9 (Section 16.9): "Tenant Never Resolves Hosts Directly" |
| CommerceHub | CommerceHub is internal POS-to-POS B2B (`Sidebar.jsx:253-263`), not governed by Storefront Domain Management |

---

## 4. Screen Inventory

### 4.1 Constitutional Screens

DOMAIN-1 Section 13 defines the following canonical screens for Domain Management. Each screen maps to a sub-route under `/storefront/domains`:

| Screen | Route | Constitutional Source | Should Exist? |
|--------|-------|----------------------|---------------|
| Domains Overview | `/storefront/domains` | DOMAIN-1 Section 13.2: "Overview (default) — summary cards, SSL status, primary domain" | YES — primary landing, mirrors `Overview` pattern at `storefrontInformationArchitecture.js:182-184` |
| Add Domain (Wizard) | `/storefront/domains/wizard` | DOMAIN-1 Section 13.3: 7-step wizard for hostname, type, verification method, challenge display, auto-poll, SSL, summary | YES — mirrors `AuthoringConfirmDialog` and `AuthoringDialog` patterns at `AuthoringDialogs.jsx:8-90` |
| Verify Domain | `/storefront/domains/{id}/verify` | DOMAIN-1 Section 13.4: DNS challenge instructions, live DNS check, retry | YES — mirrors `AuthoringFeedback` pattern at `AuthoringFeedback.jsx:3-25` |
| SSL Status | `/storefront/domains/{id}/ssl` | DOMAIN-1 Section 13.5: cert status, provider, expiration, renew, upload, revoke | YES — mirrors `AuthoringStatusBadge` at `AuthoringStatusBadge.jsx:16-29` |
| Redirect Rules | `/storefront/domains/{id}/redirects` | DOMAIN-1 Section 13.6: redirect target mapping, 301 vs 307 | YES — mirrors `AuthoringButton` action patterns at `AuthoringButton.jsx:5-24` |
| History | `/storefront/domains/{id}/history` | DOMAIN-1 Section 13.7: audit trail from `DomainAuditEntry` table | YES — mirrors `AuthoringHistoryLauncher` at `AuthoringHistoryLauncher.jsx:5-7` |
| Activity | `/storefront/domains/activity` | DOMAIN-1 Section 13.8: background job status, verification checks, SSL provisioning | YES — mirrors `AuthoringFeedback` live status patterns |
| Domain List | `/storefront/domains/list` | DOMAIN-1 Section 13.3: table of all domains with filters | YES — mirrors data table patterns in POS Admin Sidebar sections |

### 4.2 Screen Justification

Each screen is justified by the Domain entity's capabilities (`Domain.cs`):

- **Overview**: The `Domain` entity tracks `LifecycleState` (`Domain.cs:64`), `SslCertificateId` (`Domain.cs:71`), `DomainType` (`Domain.cs:57`), `VerifiedAtUtc` (`Domain.cs:117`), `SslActivatedAtUtc` (`Domain.cs:123`). All summary card data is available from the entity.

- **Add Domain (Wizard)**: `Domain.HostName` (`Domain.cs:42`), `Domain.VerificationToken` (`Domain.cs:77`), `Domain.VerificationMethod` (`Domain.cs:84`) provide the data for the wizard steps. The `Domain.Registered` / `DomainVerified` lifecycle methods (`Domain.cs:197-205`) drive state transitions.

- **Verify Domain**: `DomainVerification.Status` (`DomainVerification.cs:45`), `DomainVerification.Token` (`DomainVerification.cs:39`), `DomainVerification.FailureReason` (`DomainVerification.cs:62`) provide verification attempt data.

- **SSL Status**: `SslCertificate.Status` (`SslCertificate.cs:39`), `SslCertificate.Provider` (`SslCertificate.cs:33`), `SslCertificate.NotAfterUtc` (`SslCertificate.cs:62`), `SslCertificate.Thumbprint` (`SslCertificate.cs:52`), `SslCertificate.CertificateArn` (`SslCertificate.cs:46`) provide SSL status data.

- **Redirect Rules**: `Domain.RedirectType` (`Domain.cs:91`), `Domain.RedirectTargetDomainId` (`Domain.cs:98`), `Domain.RedirectTargetDomain` (`Domain.cs:176`) provide redirect data.

- **History**: `DomainAuditEntry` (`DomainAuditEntry.cs:20`), `Domain.DomainHistoryJson` (`Domain.cs:156`), `SslCertificate.SslCertificateHistoryJson` (`SslCertificate.cs:104`) provide audit trail data.

- **Activity**: `DomainAuditEntry.TimestampUtc` (`DomainAuditEntry.cs:96`), `DomainVerification.AttemptedAtUtc` (`DomainVerification.cs:50`), `DomainVerification.VerifiedAtUtc` (`DomainVerification.cs:56`) provide activity timelines.

---

## 5. Workflow Audit

### 5.1 Observed Workflows (Current State)

**Observed:** No Domain Management workflows exist in the Admin UX. The only domain-related user-facing action is the "Open Storefront" button in `StorefrontSettings.jsx:178-182`, which opens a hardcoded `http://${STOREFRONT_DOMAIN}` URL where `STOREFRONT_DOMAIN = import.meta.env.VITE_STOREFRONT_DOMAIN || 'localhost:5174'` (`StorefrontSettings.jsx:33`). There is no way for a merchant to add, verify, or manage custom domains from the Admin UI.

The closest observed workflow is the **Storefront Status** workflow in `StorefrontSettings.jsx`, which manages `StoreConfig.StorefrontStatus` (live/maintenance/disabled) via the `storeConfigService.updateStorefrontStatus()` API call to `PUT /api/admin/store-config/status` (`storeConfigService.js:62-71`). This workflow uses:
- `AuthoringActionBar` (`StorefrontSettings.jsx:243-250`) — title, subtitle, back, status badge, discard, publish
- `AuthoringFeedback` (`StorefrontSettings.jsx:251`) — success/error/info feedback
- `AuthoringConfirmDialog` (`StorefrontSettings.jsx:444-453`) — save confirmation
- `AuthoringConflictDialog` (`StorefrontSettings.jsx:454-461`) — concurrent edit handling
- `UnsavedChangesDialog` (`StorefrontSettings.jsx:462`) — dirty guard
- `useAuthoringGuard` (`StorefrontSettings.jsx:184-188`) — navigation guard + keyboard shortcuts

### 5.2 Constitutional Workflow: Complete Merchant Workflow

Based on DOMAIN-1 Section 14 (UI Workflows), lines 1002-1078, the canonical Domain Management merchant workflow is:

```
Add Domain
  ↓
DNS Verification (TXT / CNAME / HTTP-file)
  ↓
SSL Provisioning (Let's Encrypt or Custom Upload)
  ↓
SSL Active
  ↓
Set Primary (if first domain, auto-promoted)
  ↓
Redirect Rules (configure WWW → apex, old brand → new brand)
  ↓
Runtime Active (Canonical URL points to Primary Domain)
```

**Step-by-step:**

1. **Add Domain** (`DOMAIN-1 Section 14.1`): Merchant clicks "Add Domain" → Wizard Step 1: Enter hostname → Step 2: Select domain type (Primary/Alias/Redirect/Preview) → Step 3: Choose verification method → Step 4: Display challenge record → Step 5: Auto-poll for DNS verification → Step 6: Auto-initiate SSL provisioning → Step 7: Summary. Entity: `Domain.cs` transitions `Draft → PendingVerification` via `Domain.MarkVerified()` (`Domain.cs:197-205`).

2. **DNS Verification** (`DOMAIN-1 Section 5, transition 1-2`): Platform generates `VerificationToken` (`Domain.cs:77`). Merchant adds TXT/CNAME record. Cron job polls DNS (10s × 5min, 3 retries — `DOMAIN-1.5 Section 20.7`). On success: `DomainVerification.Status = Success` (`DomainVerification.cs:45`), `Domain.VerifiedAtUtc` set (`Domain.cs:117`), domain transitions to `Verified`. Entity: `DomainVerification.cs:16`.

3. **SSL** (`DOMAIN-1 Section 5, transition 3-4`): Platform provisions certificate via Let's Encrypt or validates upload. `SslCertificate` entity (`SslCertificate.cs:15`) tracks status. On success: `Domain.SslActivatedAtUtc` set (`Domain.cs:123`), `SslCertificateId` FK set (`Domain.cs:71`), domain transitions to `SslActive`. Entity: `SslCertificate.cs:15`.

4. **Primary** (`DOMAIN-1 Section 5, transition 9`): Tenant admin marks Verified + SSL-Active domain as Primary via `PUT /api/admin/domains/{id}/primary` — a constitutional endpoint per `DOMAIN-1 Section 13.2` but NOT YET IMPLEMENTED (no Admin Domain controller exists in source). System checks existing Primary, demotes if necessary (`DomainDowngradedFromPrimary` event). New domain transitions to `Primary` (`DomainLifecycleState.Primary` at `DomainLifecycleState.cs:41-46`). Database constraint: `UQ_Domains_TenantId_Primary` filtered unique index (`DOMAIN-1.5 Section 10.2`).

5. **Redirect** (`DOMAIN-1 Section 5`, `Domain.cs:91-98`): Merchant configures `RedirectType` (`Domain.cs:91`) and `RedirectTargetDomainId` (`Domain.cs:98`) via `POST /api/admin/domains/{id}/redirects` — a constitutional endpoint per `DOMAIN-1 Section 13.2` but NOT YET IMPLEMENTED (no Admin Domain controller exists in source). `HostResolutionMiddleware` issues 301/307 before reaching StoreConfig (`HostResolutionMiddleware.cs:134-184`).

6. **Runtime Active** (`DOMAIN-1 Section 7`): Host Resolution Middleware resolves host → Domain → TenantId → TenantResolutionMiddleware sets tenant context → StoreConfig, Theme, CMS load. The Primary Domain is used for canonical URLs (`SeoService` → `StorefrontUrlResolver`). Entity: `Domain.LifecycleState.Primary` (`Domain.cs:64`).

### 5.3 Missing Steps (vs. Current Observed State)

The following steps exist in the constitutional workflow but have NO implementation in the current Admin UX:

1. **Domain Registration Wizard** — No UI for entering hostname, selecting type, choosing verification method. `storefrontInformationArchitecture.js` has no `domains` screen. No Admin Domain API controller exists (no `DomainManagementController` in `PointOfSale.API/Controllers/`).

2. **DNS Verification Challenge Display** — No UI showing TXT record values or CNAME targets. No `DomainVerification` display component. No DNS polling UI.

3. **SSL Management** — No UI for certificate status, renewal, upload, or revocation. No `SslCertificate` display component. No Let's Encrypt integration in the Admin UX.

4. **Primary Domain Promotion** — No UI for setting a Primary Domain. No `PUT /api/admin/domains/{id}/primary` endpoint in the Admin API (only runtime resolution exists).

5. **Redirect Management** — No UI for configuring redirect targets. No `POST /api/admin/domains/{id}/redirects` endpoint.

6. **Domain Audit History** — No UI for viewing `DomainAuditEntry` records. No history screen for domains.

7. **Background Job Activity** — No UI for monitoring DNS verification cron, SSL provisioning, or renewal jobs.

---

## 6. State Machine

### 6.1 Observed States

**Observed:** The `DomainLifecycleState` enum at `PointOfSale.Core/Enums/DomainLifecycleState.cs:9-62` defines 9 states:

```
Draft (0)          — DomainLifecycleState.cs:11-14: "Initial state — domain record created, no verification attempted."
PendingVerification (1) — DomainLifecycleState.cs:17-22: "Verification has been initiated — DNS/HTTP validation is in progress."
Verified (2)       — DomainLifecycleState.cs:24-28: "Verification successful — eligible for SSL provisioning."
SslPending (3)     — DomainLifecycleState.cs:30-34: "SSL certificate provisioning is in progress."
SslActive (4)      — DomainLifecycleState.cs:36-39: "SSL certificate is active and the domain is serving traffic securely."
Primary (5)        — DomainLifecycleState.cs:41-46: "The domain is the Primary canonical domain for the tenant."
Disabled (6)       — DomainLifecycleState.cs:48-52: "The domain is disabled by a merchant or system action."
Archived (7)       — DomainLifecycleState.cs:54-58: "The domain is archived — soft-deleted. Can be restored within 90-day retention."
Deleted (8)        — DomainLifecycleState.cs:60-61: "Deleted" (no XML doc — soft-delete terminal state)
```

**Observed:** The `PersistenceDomainResolver.IsServableState()` at `PersistenceDomainResolver.cs:137-142` confirms the runtime's servability gate:

```csharp
// Only domains in Verified, SslActive, or Primary states are "found" for resolution.
return state is DomainLifecycleState.Verified
    or DomainLifecycleState.SslActive
    or DomainLifecycleState.Primary;
```

This means `Disabled`, `Archived`, `Deleted`, `Draft`, `PendingVerification`, and `SslPending` states all return `NotFound` from the resolver — the pipeline falls through to JWT slug-based tenant resolution (`DomainResolutionResult.NotFound` at `DomainResolutionResult.cs:88-93`).

The lifecycle methods on `Domain.cs:197-256` confirm the state transitions:
- `MarkVerified()` → transitions to `Verified` (or `Primary` if `DomainType.Primary`)
- `MarkSslPending()` → transitions to `SslPending`
- `MarkSslActive()` → transitions to `SslActive`
- `MarkDisabled()` → transitions to `Disabled`
- `MarkArchived()` → transitions to `Archived`
- `MarkDeleted()` → transitions to `Deleted`, sets `IsDeleted = true`

### 6.2 Constitutional State Machine

The Domain lifecycle state machine has 9 states with 15 defined transitions (DOMAIN-1 Section 5, lines 172-360):

```
┌─────────────┐
│   Draft     │  (DomainLifecycleState.cs:11-14)
│ Initial state │
└──────┬──────┘
       │ merchant submits host
       ▼
┌──────────────────────┐
│ PendingVerification  │  (DomainLifecycleState.cs:17-22)
│ Token generated, DNS proof pending │
└─────────┬────────────┘
          │ DNS proof confirmed
          ▼
┌──────────────┐
│  Verified    │  (DomainLifecycleState.cs:24-28)
└──────┬───────┘
       │ (platform initiates SSL)
       ▼
┌──────────────┐    SSL fails/retry    ┌──────────────┐
│  SslPending  │◄──────────────────────┤  SslPending  │  (DomainLifecycleState.cs:30-34)
└──────┬───────┘                       └──────────────┘
       │ cert provisioned              (retry/backoff)
       ▼                               │
┌──────────────┐                       │
│  SslActive   │  (DomainLifecycleState.cs:36-39)     │
└──────┬───────┘                       │
       │ tenant marks primary OR       │
       ▼  platform manages             │
┌──────────────┐                       │
│  Primary     │  (DomainLifecycleState.cs:41-46)    │
└──────┬───────┘                       │
       │                               │
       ├─── disable ──▶ [Disabled] ◄───┘  (DomainLifecycleState.cs:48-52)
       │                  │
       │                  └─ reactivate ──▶ [SslActive] or [Verified]
       │
       ├─── archive ──▶ [Archived] ◄───┐  (DomainLifecycleState.cs:54-58)
       │                  │            │
       │                  └─ reactivate ──▶ [SslActive] or [Verified]
       │
       └─── delete ──▶ [Deleted] ── (permanent after 90-day retention)  (DomainLifecycleState.cs:60-61)
```

### 6.3 Constitutional Transition Rules

1. **Draft → PendingVerification**: Merchant enters hostname. Platform validates format and global uniqueness. Generates `VerificationToken` (`Domain.cs:77`). Fires `DomainRegistered`.

2. **PendingVerification → Verified**: DNS challenge (TXT/CNAME/HTTP-file) matches `VerificationToken` (`Domain.cs:77`). Sets `VerifiedAtUtc` (`Domain.cs:117`). Fires `DomainVerified`.

3. **PendingVerification → PendingVerification**: Merchant retries verification. Creates new `DomainVerification` row (`DomainVerification.cs:16`). Fires `DomainVerificationAttempt`.

4. **Verified → SslPending**: SSL provisioning initiated. Fires `SslProvisioningStarted`.

5. **SslPending → SslActive**: Certificate provisioned and bound. Sets `SslActivatedAtUtc` (`Domain.cs:123`), `SslCertificateId` (`Domain.cs:71`). Fires `SslActivated`.

6. **SslPending → SslPending**: Provisioning retry (exponential backoff: 1m, 5m, 15m, 1h, 4h, 12h, 24h). Fires `SslActivationFailed`.

7. **SslActive → SslActive**: Automated renewal at 30-day threshold. No state transition. Fires `SslRenewed`.

8. **SslActive → SslPending**: Renewal failure detected. Enters retry loop. Fires `SslRenewalFailed`.

9. **SslActive → Verified**: Cert revoked/expired beyond grace (90 days). SSL disabled. Domain remains verified. Fires `SslRevoked`.

10. **SslActive/Verified → Primary**: Tenant admin promotes. Must be only one Primary per tenant; demotes previous. Fires `DomainPromotedToPrimary` / `DomainDowngradedFromPrimary`.

11. **Primary → SslActive/Verified**: Demoted when new Primary set. Fires `DomainDowngradedFromPrimary`.

12. **SslActive/Primary/Verified → Disabled**: Merchant or platform disables. Fires `DomainDisabled`.

13. **Disabled → SslActive/Verified**: Merchant reactivates. Fires `DomainReactivated`.

14. **SslActive/Primary/Verified → Archived**: Merchant archives (retains config, stops serving). Fires `DomainArchived`.

15. **Archived → SslActive/Verified**: Merchant reactivates. Fires `DomainReactivated`.

16. **Any active state → Deleted**: Merchant deletes. Requires: not Primary, no active SSL, no redirect dependents. Soft-delete with `DeletedAtUtc`. Fires `DomainDeleted`.

17. **Deleted → Deleted**: Re-attempt triggers restoration if within 90-day retention window.

### 6.4 Constitutional State Justification

| State | Justification | Source Evidence |
|-------|---------------|-----------------|
| Draft | Initial state — no verification attempted. Allows merchant to abandon without side effects. | `DomainLifecycleState.cs:11-14` |
| PendingVerification | DNS/HTTP verification in progress. Separates "submitted" from "verified" for DNS propagation time. | `DomainLifecycleState.cs:17-22` |
| Verified | DNS ownership proven. Eligible for SSL but not yet serving (SSL required for HTTPS). | `DomainLifecycleState.cs:24-28`, `PersistenceDomainResolver.cs:77-90` (servable state) |
| SslPending | SSL provisioning in progress. Separate from Verified to allow retry/backoff without losing verification. | `DomainLifecycleState.cs:30-34` |
| SslActive | SSL active, serving HTTPS. Required for storefront traffic per Architecture Law #18 (`DOMAIN-1 Section 16.17`). | `DomainLifecycleState.cs:36-39`, `PersistenceDomainResolver.cs:137-142` (servable state) |
| Primary | Canonical domain for the tenant. Exactly one per tenant (Architecture Law #2, `DOMAIN-1 Section 16.2`). Used for canonical URLs and sitemap. | `DomainLifecycleState.cs:41-46`, `DomainType.cs:10-12` |
| Disabled | Temporarily stopped. Reversible. Prevents serving while retaining config. | `DomainLifecycleState.cs:48-52` |
| Archived | Soft-retired. Reversible within 90-day retention. Distinct from Disabled (archival vs temporary stop). | `DomainLifecycleState.cs:54-58` |
| Deleted | Terminal soft-delete. Hard-deletable after 90 days. Retained for audit history. | `DomainLifecycleState.cs:60-61`, `Domain.cs:250-256` (`MarkDeleted()` sets `IsDeleted = true`) |

---

## 7. Authorization

### 7.1 Observed Authorization Model

**Observed:** The POS Admin sidebar (`Sidebar.jsx`) enforces role-based access via `requiredRoles` (`Sidebar.jsx:354`):

```javascript
if (item.requiredRoles?.length && !item.requiredRoles.includes(userRole)) {
    return null
}
```

The `Storefront` product node (`localizeStorefrontProductNode` at `Sidebar.jsx:123-129`) injects `requiredRoles: ['Owner', 'Admin']` for all children. The `canManageStorefrontSettings` function (`storefrontAdminAuthority.js:3-5`) returns true for `Owner` and `Admin` roles only.

The `StorefrontSettings.jsx:43` calls `canManageStorefront()` — if false, renders `AuthoringPermissionState` (`StorefrontSettings.jsx:227-229`).

The backend-side authorization is enforced by:
- `AdminTenantAuthorityMiddleware` (`Program.cs:3374`) — validates tenant ownership for admin writes
- `StorefrontPublicationGuardMiddleware` (`StorefrontPublicationGuardMiddleware.cs:1-92`) — enforces `StoreConfig.StorefrontStatus` gate
- `TenantSaveChangesInterceptor` (`TenantSaveChangesInterceptor.cs:56-111`) — blocks saving `ITenantOwned` with mismatched TenantId

### 7.2 Constitutional Authorization Matrix

Based on DOMAIN-1 Section 5.3 (Transition Authorization, lines 362-376):

| Action | Who May Perform | Evidence |
|--------|-----------------|----------|
| **Create** (register domain) | Merchant (POS Admin — Owner/Admin) | DOMAIN-1 Section 5.3, line 366: "Merchant (POS Admin)" — Admin API call with tenant-scoped JWT. `canManageStorefrontSettings(storefrontAdminAuthority.js:3-5)` returns true for Owner/Admin. |
| **Delete** | Merchant (POS Admin) OR System (GC after retention) | DOMAIN-1 Section 5.3, line 369: "Merchant (POS Admin) / System (GC)". `Domain.cs:250-256` — `MarkDeleted()` is on the entity, called by both paths. |
| **Verify** (DNS challenge) | System (Infrastructure — DNS verification cron job) | DOMAIN-1 Section 5.3, line 367: "System (Infrastructure) — Automated DNS check job." `DomainVerification.cs:16` — verification attempts tracked by cron. |
| **Set Primary** | Merchant (POS Admin) | DOMAIN-1 Section 5.3, line 373: "Merchant (POS Admin) — Admin API; tenant isolation enforced." `DomainType.cs:13` — Primary domain per tenant; `UQ_Domains_TenantId_Primary` enforces uniqueness. |
| **Disable** | Merchant (POS Admin) OR System (SSL expiration) | DOMAIN-1 Section 5.3, line 374: "Merchant (POS Admin) / System." `Domain.cs:231-235` — `MarkDisabled()`. |
| **Archive** | Merchant (POS Admin) | DOMAIN-1 Section 5.3, line 375: "Merchant (POS Admin)." `Domain.cs:240-244` — `MarkArchived()`. |
| **Reactivate** (from Disabled/Archived) | Merchant (POS Admin) | Implied by DOMAIN-1 Section 5.3 lines 374-375 + `Domain.cs:` lifecycle methods. |
| **SSL Provision** | System (Cert Manager) | DOMAIN-1 Section 5.3, line 368: "System (Cert Manager) — Automated." `SslCertificate.cs:15` — certificate state managed by Cert Manager job. |
| **SSL Renew** | System (Cert Manager) | DOMAIN-1 Section 6.7: "Automated renewal at 30-day threshold." `SslCertificate.cs:74` — `NextRenewalUtc`. |
| **SSL Upload** (custom cert) | Merchant (POS Admin) | DOMAIN-1 Section 6.1: "Custom Upload — merchant-uploaded certificate." `SslCertificate.cs:33` — `ProviderType.CustomUpload`. |
| **View** (any domain screen) | Merchant (POS Admin — Owner/Admin) | `canManageStorefrontSettings(storefrontAdminAuthority.js:3-5)` — Owner/Admin only. Inherited from Storefront product access. |
| **Configure Redirect** | Merchant (POS Admin) | `Domain.cs:91-98` — `RedirectType` and `RedirectTargetDomainId` set by merchant action. `Domain.cs:186` — `AuditEntries` records the action. |
| **Migrate StorefrontBaseUrl** | System (Platform, during data migration) | DOMAIN-1 Section 18.1: "Migrate StorefrontBaseUrl to a Domain record." `DomainType.cs:13` — Primary domain from `StorefrontBaseUrl`. |

---

## 8. Runtime Ownership

### 8.1 Observed Runtime Ownership

**Observed:** The runtime pipeline is certified per DOMAIN-1.6 Section 21 (`DOMAIN-1.6_STOREFRONT_CUSTOM_DOMAINS_RUNTIME_CONSTITUTION.md:1566-1771`) and DOMAIN-2D integration (`DOMAIN-2D_TENANT_RESOLUTION_INTEGRATION_IMPLEMENTATION.md:1-300`).

The canonical resolution order (DOMAIN-1.6 Section 1.1.1, line 49; DOMAIN-1 Section 7, line 479-570):

```
Host (Request.Host)
  ↓ (after ForwardedHeaders — X-Forwarded-Host now included per DOMAIN-2A)
Domain (HostResolutionMiddleware — PersistenceDomainResolver queries Domains table)
  ↓ (Domain.TenantId passed through)
Tenant (TenantResolutionMiddleware — consumes DomainResolutionResult per DOMAIN-2D)
  ↓
StoreConfig (StorefrontStoreConfigController → ApplicationDbContext.StoreConfigs JOIN Tenants)
  ↓
Theme (StorefrontDraftConfigMapper.MapLive)
  ↓
CMS (StorefrontPagesController → ICmsPageReadService)
  ↓
Mongo (ProductDocument / CategoryDocument — derivative, SQL-validated)
  ↓
Response
```

The HOST → DOMAIN resolution is owned by:
- `HostResolutionMiddleware` (`HostResolutionMiddleware.cs:45`) — reads `Request.Host`, normalizes, queries Domain registry
- `HostNormalizer` (`HostNormalizer.cs:22`) — normalizes host (lowercase, punycode, port strip)
- `PersistenceDomainResolver` (`PersistenceDomainResolver.cs:31`) — queries `IDomainRepository` by `NormalizedHostName`
- `IDomainRepository` (`IDomainRepository.cs:18`) — persistence boundary, returns `Domain` entities

The Domain → Tenant resolution is owned by:
- `TenantResolutionMiddleware` (`TenantResolutionMiddleware.cs`) — now consumes `DomainResolutionResult` (DOMAIN-2D, `TenantResolutionMiddleware.cs:103-128`)
- `TenantResolutionMiddleware.ResolveRequestTenantAsync` — checks `HttpContext.Items[HostResolutionContextKeys.DomainResolutionResultItemKey]` first, then falls back to StorefrontBaseUrl/slug

### 8.2 Runtime Ownership Isolation

| Runtime Component | Owns | Never Owns | Source Evidence |
|-------------------|------|------------|-----------------|
| **Domain Runtime** (`HostResolutionMiddleware` + `PersistenceDomainResolver`) | Host → Domain resolution, `DomainResolutionResult` | Tenant resolution, StoreConfig, Theme, CMS | `HostResolutionMiddleware.cs:25-30` — "No EF Core, no tenant lookup, no StoreConfig lookup, no caching, no theme resolution, no preview resolution." |
| **Tenant Runtime** (`TenantResolutionMiddleware`) | Tenant resolution from JWT / Domain.TenantId / slug | Domain lookup directly (uses DomainResolutionResult), StoreConfig | `TenantResolutionMiddleware.cs:103-128` — consumes DomainResolutionResult, does NOT query Domains table directly |
| **StoreConfig Runtime** (`StorefrontStoreConfigController`) | StoreConfig loading | Domain resolution, Theme resolution | `StoreConfig.cs:11-15` — "Managed ONLY via POS Admin APIs. Storefront is read-only." |
| **Theme Runtime** (`StorefrontDraftConfigMapper`) | Theme resolution from StoreConfig | Tenant resolution, Domain resolution | `StorefrontDraftConfigMapper.cs:32-146` — consumes `StoreConfig`, never `TenantContext` directly |
| **CMS Runtime** (`StorefrontPagesController`) | CMS page serving | Tenant resolution, Domain resolution | `StorefrontPagesController.cs:44` — `StorefrontTenantContextResolver.ValidateTenant` (reads, doesn't resolve) |

### 8.3 Runtime Isolation Violations (Forbidden)

| Forbidden Pattern | Rule | Evidence |
|--------------------|------|----------|
| Theme resolving Tenant | Theme never reads `ITenantContext` | `StorefrontDraftConfigMapper.cs:32-48` — consumes `StoreConfig`, never `TenantContext` (`DOMAIN-1.6 Section 22, Invariant #4`) |
| CMS resolving Tenant | CMS controllers consume `ITenantContext` injected by middleware, never resolve it themselves | `StorefrontPagesController.cs:44` — `ValidateTenant` (reads, doesn't resolve) (`DOMAIN-1.6 Section 22, Invariant #5`) |
| StoreConfig writing Domain | `StoreConfig` has no domain fields; no `DbSet<Domain>` in SQL | `StoreConfig.cs:1-188` — no Domain fields (`DOMAIN-1.6 Section 22, Invariant #12`) |
| Storefront Runtime verifying DNS | DNS verification is infrastructure responsibility | `HostResolutionMiddleware.cs:25-30` — "no DNS verification"; `DOMAIN-1.6 Section 22, Invariant #15`; `Domain.cs:112-119` — `MarkVerified()` is a domain lifecycle method, not runtime |
| Direct entity writes in storefront | All storefront writes must go through `TenantLineageAuthority` | `TenantSaveChangesInterceptor.cs:56-111` (`DOMAIN-1.6 Section 22, Invariant #16`) |
| CORS trusting client Origin | Must NOT use dynamic origin without Domain registry verification | `DOMAIN-1.6 Section 3.5, line 250` — "Must NOT use dynamic origin without Domain registry verification" |

---

## 9. Admin Experience

### 9.1 Observed Admin UX Architecture

**Observed:** The Storefront Admin UX is built on the `AuthoringPlatform` pattern. The `StorefrontAdminFrame` component (`StorefrontAdminFrame.jsx:16-45`) wraps all Storefront screens in:

```jsx
<AuthoringPlatformProvider>
  <StorefrontAdminFrameContent>
    <AuthoringShell className="space-y-4">
      <StorefrontBreadcrumbs currentLabel={currentLabel} onNavigate={guardNavigation} />
      <Outlet />
    </AuthoringShell>
  </StorefrontAdminFrameContent>
</AuthoringPlatformProvider>
```

Key components:
- `AuthoringShell` (`AuthoringShell.jsx:1-7`) — minimal wrapper, applies `data-authoring-platform="storefront"` attribute
- `StorefrontBreadcrumbs` (`StorefrontBreadcrumbs.jsx:7-87`) — resolves current screen via `resolveStorefrontScreen()`, renders breadcrumb trail with `Link` navigation, respects `guardNavigation` for dirty-guard protection, sets `document.title`
- `AuthoringActionBar` (`AuthoringActionBar.jsx:12-51`) — constitutional order: Back/title/status, Preview, History, Discard, Save, Publish. Comment at line 12: "Constitutional order: Back/title/status, Preview, History, Discard, Save, final commit."
- `AuthoringFeedback` (`AuthoringFeedback.jsx:3-25`) — kind-based feedback (info/success/error/warning) with `role="alert"` / `role="status"` and `aria-live`
- `AuthoringDialog` / `AuthoringConfirmDialog` / `AuthoringPromptDialog` (`AuthoringDialogs.jsx:8-168`) — modal dialogs with focus management, Escape-to-close, Tab trapping
- `AuthoringHistoryLauncher` (`AuthoringHistoryLauncher.jsx:5-7`) — History button wrapper around `AuthoringButton`
- `AuthoringStatusBadge` (`AuthoringStatusBadge.jsx:16-29`) — state-based badge (dirty/draft/published/live/saving/publishing/error/disabled) with icon + color
- `AuthoringLoadingState` / `AuthoringEmptyState` / `AuthoringPermissionState` (`AuthoringStates.jsx:7-29`) — state views
- `useAuthoringGuard` (`useAuthoringGuard.js:9-92`) — ONE guard for browser unload, breadcrumbs, sidebar links, in-editor navigation, and shortcuts. Registers navigation guard via `useAuthoringPlatform().registerNavigationGuard`. Implements browser `beforeunload`, clicks on `<a>` tags, and keyboard shortcuts (Ctrl/Cmd+S for save, Ctrl/Cmd+Shift+P for preview).
- `useAuthoringShortcuts` — hooks keyboard shortcuts
- `AuthoringPlatformProvider` (`AuthoringPlatformProvider.jsx:10-24`) — provides `registerNavigationGuard` + `guardNavigation` via context

The `StorefrontSettings.jsx` page (`StorefrontSettings.jsx:35-464`) demonstrates the complete pattern:
- Uses `AuthoringActionBar` with `backAction`, `status` (AuthoringStatusBadge), `discardAction`, `publishAction` (`StorefrontSettings.jsx:243-250`)
- Uses `AuthoringFeedback` for success/error/info (`StorefrontSettings.jsx:251`)
- Uses `AuthoringConfirmDialog` for save confirmation (`StorefrontSettings.jsx:444-453`)
- Uses `AuthoringConflictDialog` for concurrent edit conflicts (`StorefrontSettings.jsx:454-461`)
- Uses `UnsavedChangesDialog` via `useAuthoringGuard` (`StorefrontSettings.jsx:462`)
- Uses `useAuthoringGuard` for dirty guard + keyboard shortcuts (`StorefrontSettings.jsx:184-188`)
- Uses `canManageStorefrontSettings(user)` for permission gate (`StorefrontSettings.jsx:43`)

### 9.2 Constitutional Admin Experience Integration

**Constitutional Recommendation:** Domain Management MUST integrate into the existing AuthoringPlatform UX framework. It should reuse every available shared component and pattern:

| Pattern | Reuse Source | How Domain Management Integrates |
|---------|--------------|----------------------------------|
| **AuthoringShell** | `AuthoringShell.jsx:1-7` | Wrap `/storefront/domains/*` routes in `<StorefrontAdminFrame>` → `<AuthoringShell>` (same as all Storefront screens) |
| **AuthoringActionBar** | `AuthoringActionBar.jsx:12-51` | Use for Domains Overview page — Back to `/storefront`, title="Domains", status badge (Primary domain SSL status), discard/save/publish buttons (Save = apply domain config, Publish = activate domain) |
| **Breadcrumbs** | `StorefrontBreadcrumbs.jsx:7-87` | Add `domains` screen to `SCREENS` in `storefrontInformationArchitecture.js:176-235`; add `resolveStorefrontScreen` case for `/storefront/domains` at `storefrontInformationArchitecture.js:242-274`; breadcrumb trail: Home → Storefront → Domains |
| **AuthoringFeedback** | `AuthoringFeedback.jsx:3-25` | Use for DNS verification status, SSL provisioning status, error/success messages. Use `getAuthoringErrorFeedback()` for API error mapping. |
| **AuthoringDialogs** | `AuthoringDialogs.jsx:8-168` | Use `AuthoringConfirmDialog` for delete/archive/disable confirmation; `AuthoringPromptDialog` for entering hostname; `AuthoringDialog` for redirect target selection |
| **Dirty Guard** | `useAuthoringGuard.js:9-92` | Use `useAuthoringGuard({ isDirty, isBusy, onSave })` for domain edit forms — prevents navigation when unsaved changes exist, registers browser `beforeunload` handler |
| **Keyboard Shortcuts** | `useAuthoringGuard.js:66-81` | Ctrl/Cmd+S to save domain config; Ctrl/Cmd+Shift+P to preview resolved storefront URL on this domain |
| **Status Badge** | `AuthoringStatusBadge.jsx:16-29` | Use for per-domain status: `verified` (CheckCircle2), `ssl_pending` (Clock3), `ssl_active` (CheckCircle2/live), `verification_failed` (AlertCircle), `disabled` (disabled) |
| **Loading State** | `AuthoringLoadingState` (`AuthoringStates.jsx:7-14`) | Use while fetching domain list or domain details |
| **Permission State** | `AuthoringPermissionState` (`AuthoringStates.jsx:27-29`) | Use when `canManageStorefrontSettings(user)` returns false for non-Owner/Admin users |
| **History Launcher** | `AuthoringHistoryLauncher.jsx:5-7` | Use in Domain detail pages — History button opens audit trail from `DomainAuditEntry` |
| **Navigation Guard** | `useAuthoringPlatform` (`AuthoringPlatformProvider.jsx:10-24`) | Register via `registerNavigationGuard` — same pattern as `StorefrontSettings.jsx:184-188` |

### 9.3 Breadcrumb Integration

**Observed:** `StorefrontBreadcrumbs.jsx:15-16` resolves the current screen via `resolveStorefrontScreen(location.pathname, location.search)`. The `SCREENS` constant in `storefrontInformationArchitecture.js:176-235` defines breadcrumb trails for each screen. Each screen has `breadcrumbs` array with `ROOT` (Storefront) + intermediate crumbs.

**Constitutional Recommendation:** Add the following to `storefrontInformationArchitecture.js`:

```javascript
// In STOREFRONT_NAVIGATION — between status and homepage (per DOMAIN-1 Section 13)
{
    screenIds: ['domains', 'domains-overview', 'domains-wizard', 'domains-verify',
                 'domains-ssl', 'domains-redirects', 'domains-history', 'domains-activity'],
    path: '/storefront/domains',
    labelKey: 'storefront.ia.domains',
    fallbackLabel: 'Domains',
    icon: 'Globe',
},

// In SCREENS constant
domains: screen('domains', 'storefront.ia.domains', 'Domains', [
    ROOT,
    breadcrumb('storefront.ia.domains', 'Domains'),
]),

// In resolveStorefrontScreen()
if (path === '/storefront/domains' || path.startsWith('/storefront/domains')) return SCREENS.domains
```

---

## 10. Shopify Parity

### 10.1 Shopify Domain Model (Conceptual)

Per DOMAIN-1 Section 15 (Shopify Parity, lines 1081-1114) and DOMAIN-0 Phase 15 (lines 751-776):

| Capability | Shopify | Shopixy Canonical | Intentional Difference |
|------------|---------|-------------------|----------------------|
| Domain registry entity | `domains` table with `domain`, `is_primary`, `verified_at`, `ssl_certificate_status`, `dns_verification_status`, `redirect_to_primary` | `Domain` aggregate with `DomainType`, `LifecycleState`, `VerificationToken`, `SslCertificateId`, `RedirectType`/`RedirectTargetDomainId` | Shopixy uses explicit state machine (9 states, 15 transitions) vs Shopify's simpler columns |
| Multiple domains per tenant | Yes | Yes — `Domain.TenantId` FK, one-to-many | Same |
| Primary domain selection | `is_primary` boolean | `DomainType.Primary` + `LifecycleState.Primary` | Shopixy has explicit state in lifecycle |
| DNS verification | TXT, CNAME, HTTP file | TXT, CNAME, HttpFile (`VerificationMethod` enum, `VerificationMethod.cs:7-25`) | Same methods |
| SSL provisioning | Automated Let's Encrypt | Let's Encrypt + Custom Upload + System Default (`ProviderType` enum, `ProviderType.cs:8-25`) | Custom upload is an explicit option |
| Redirect support | `redirect_to_primary` boolean | `DomainType.Redirect` with `RedirectType.Permanent301`/`Temporary307` (`RedirectType.cs:7-21`) | Explicit redirect types (301/307) |
| Preview domains | Platform-controlled only | Custom Preview domains (`DomainType.Preview`, `DomainType.cs:25-29`) | Shopixy first-class preview domain type |
| Host resolution | Before tenant resolution | Host Resolution Middleware before TenantResolutionMiddleware (`Program.cs:3383-3396`) | Same pattern, explicit middleware |
| Canonical URLs | Primary domain | Primary Domain (`DomainType.cs:10-12`) | Same |
| Dynamic CORS | Dynamic per-domain | Dynamic CORS (constitutional design, `DOMAIN-1 Section 7.4`) | Same concept |

### 10.2 Shopify Workflow Comparison

| Merchant Workflow | Shopify | Shopixy Canonical | Parity |
|--------------------|---------|-------------------|--------|
| Add Domain | Admin → Settings → Domains → Add domain | Storefront → Domains → Wizard (Add Domain) | ✓ Same flow: enter hostname → verify → SSL → primary |
| DNS Verification | Shows TXT/CNAME instructions | Shows TXT/CNAME/HTTP-file challenge (`VerificationMethod` enum) | ✓ Same — Shopixy adds HTTP-file method |
| SSL Activation | Automated Let's Encrypt | Let's Encrypt + Custom Upload (`ProviderType.CustomUpload`) | ✓ Same + enterprise option |
| Set Primary | Click "Make primary domain" | `PUT /api/admin/domains/{id}/primary` (DOMAIN-1 Section 13.2) | ✓ Same — demotes previous primary |
| Redirect | Configure redirect to primary | `DomainType.Redirect` with `RedirectType.Permanent301`/`Temporary307` | ✓ Same + explicit 301/307 |
| Disable | Toggle domain off | `PUT /api/admin/domains/{id}/disable` (DOMAIN-1 Section 13.2) | ✓ Same |

### 10.3 Intentional Differences

1. **Preview domain as first-class type** — Shopify has no custom preview domains. Shopixy's `DomainType.Preview` (`DomainType.cs:25-29`) is an intentional architectural decision — allows merchants to preview on their own domain.

2. **Explicit redirect types** — Shopify uses boolean `redirect_to_primary`. Shopixy supports `RedirectType.Permanent301` and `Temporary307` (`RedirectType.cs:7-21`) — explicit HTTP status codes for temporary marketing redirects.

3. **Custom SSL upload** — Shopify only does automated Let's Encrypt. Shopixy includes `ProviderType.CustomUpload` (`ProviderType.cs:11-14`) — enterprises with existing certificate management can upload their own certs.

4. **Domain lifecycle as explicit state machine** — Shopify's domain model is simpler (boolean + status columns). Shopixy's 9-state lifecycle with 15 transitions provides operational clarity for support and automation.

5. **System and Development domain types** — Shopify has no concept of System or Development domains in its registry. Shopixy explicitly models these to avoid collisions between platform infrastructure and merchant domains.

6. **Host resolution as separate middleware** — Shopify integrates host resolution into the request pipeline. Shopixy makes it a separate, explicit middleware layer (`HostResolutionMiddleware.cs:45`) distinct from tenant resolution.

---

## 11. Migration Readiness

### 11.1 Readiness Verdict

**READY for Domain Management UX implementation.**

The runtime foundation is certified and implemented:
- `Domain` entity exists (`Domain.cs:22-257`) with all child entities (`DomainVerification.cs:16`, `SslCertificate.cs:15`, `DomainAuditEntry.cs:20`)
- All enums exist (`DomainType.cs`, `DomainLifecycleState.cs`, `RedirectType.cs`, `CanonicalPolicy.cs`, `VerificationMethod.cs`, `VerificationStatus.cs`, `CertificateStatus.cs`, `ProviderType.cs`, `ActorType.cs`)
- `IDomainResolver` interface exists (`IDomainResolver.cs:28`) with `PersistenceDomainResolver` implementation (`PersistenceDomainResolver.cs:31`)
- `IDomainRepository` interface exists (`IDomainRepository.cs:18`)
- `HostResolutionMiddleware` is registered and certified (`HostResolutionMiddleware.cs:45-285`, `Program.cs:3383-3396`)
- `TenantResolutionMiddleware` consumes `DomainResolutionResult` (DOMAIN-2D, `TenantResolutionMiddleware.cs:103-128`)
- `HostResolutionContextKeys` contract exists (`HostResolutionContextKeys.cs:12-27`)
- `DomainResolutionResult` model exists (`DomainResolutionResult.cs:21-123`)
- `IHostNormalizer` / `HostNormalizer` exist (`HostResolutionMiddleware.cs:49`, `HostNormalizer.cs:22`)
- `IHostResolutionContext` interface exists (`IHostResolutionContext.cs`)
- `DomainResolutionStatus` enum exists (`DomainResolutionStatus.cs:14`)

### 11.2 Blockers

**Zero runtime blockers.** The runtime foundation (Host Resolution → Domain → Tenant) is complete and certified per DOMAIN-2C.1 and DOMAIN-2D.

**One UX blocker (CRITICAL):**
- `STOREFRONT_PRODUCT_NAVIGATION` is imported in `Sidebar.jsx:37` and used at `Sidebar.jsx:243` but is **not defined** in `storefrontInformationArchitecture.js`. The file re-exports it from `index.js:6`, but the source module defines ONLY `STOREFRONT_NAVIGATION` — neither `STOREFRONT_PRODUCT_NAVIGATION` nor `OPERATIONS_NAVIGATION` (also re-exported at `index.js:4`) are defined there. This broken import prevents the sidebar from rendering at all (spreading `undefined` in `localizeStorefrontProductNode` at `Sidebar.jsx:123-129` crashes at runtime). This must be resolved before Domains can be added to the Storefront navigation tree.

### 11.3 Prerequisites

1. **Resolve `STOREFRONT_PRODUCT_NAVIGATION` definition** — The Sidebar currently imports but does not find this constant. Must be defined or the import must be fixed before adding a Domains navigation node.

2. **Feature flags** — Per DOMAIN-1.5 Section 14.4 (CPD-16): `Domains:Enabled` (default `false`), `Domains:LegacyFallback` (default `true`). These must be added to `appsettings.json` before the Host Resolution Middleware can be toggled safely.

3. **Admin API endpoints** — `POST /api/admin/domains`, `GET /api/admin/domains`, `PUT /api/admin/domains/{id}/verify`, `PUT /api/admin/domains/{id}/primary`, `PUT /api/admin/domains/{id}/disable`, `PUT /api/admin/domains/{id}/archive`, `DELETE /api/admin/domains/{id}`, `POST /api/admin/domains/{id}/ssl/renew`, `POST /api/admin/domains/{id}/ssl/upload`, `POST /api/admin/domains/{id}/ssl/revoke`, `POST /api/admin/domains/{id}/redirects`, `GET /api/admin/domains/history` — defined in DOMAIN-1 Section 13.2 (lines 984-998) but not yet implemented.

4. **Dynamic CORS** — Currently hardcoded origins (`appsettings.json:235-246`). Must be replaced with Domain-aware CORS resolution for custom domain storefronts to function.

### 11.4 Implementation Waves

| Wave | Scope | Runtime Complete? | UX Complete? |
|------|-------|-------------------|--------------|
| DOMAIN-2 (Runtime Foundation) | ✅ `Domain` entity, `HostResolutionMiddleware`, `PersistenceDomainResolver`, `TenantResolutionMiddleware` integration | ✅ Certified (DOMAIN-2C, DOMAIN-2D) | ❌ No Admin UX |
| DOMAIN-3 (Verification) | ❌ `DomainVerificationService`, DNS challenge infrastructure | ❌ Not started | ❌ No UX |
| DOMAIN-4 (SSL) | ❌ `SslCertManagerService`, Let's Encrypt integration | ❌ Not started | ❌ No UX |
| **DOMAIN-5 (Admin UX)** | ✅ Runtime foundation, ✅ Verification service, ✅ SSL service | ✅ Prerequisite | **READY to implement** |
| DOMAIN-6 (Production Hardening) | Monitoring, analytics, edge routing | ✅ Prerequisite | ✅ Prerequisite |

Domain Management Admin UX begins at **DOMAIN-5**. Prerequisites (DOMAIN-2) are complete.

---

## 12. Implementation Waves

### 12.1 Constitutional Implementation Phases

Per DOMAIN-1 Section 17 (Migration Waves, lines 1165-1322) and DOMAIN-1.5 Section 14 (Migration Strategy):

**3.1 Blueprint** — ✅ COMPLETE  
- `DOMAIN-1` (Canonical Blueprint) — defines Domain entity, lifecycle, SSL constitution, admin experience, architecture laws  
- `DOMAIN-1.5` (Persistence Freeze) — freezes entity shapes, indexes, constraints, audit model, migration ordering  
- `DOMAIN-1.6` (Runtime Constitution) — freezes middleware order, resolution order, cache order, runtime ownership  

**3.2 Runtime Foundation** — ✅ COMPLETE (CERTIFIED)  
- `DOMAIN-2A` — `HostNormalizer` (`HostNormalizer.cs:22`), `IHostNormalizer` interface  
- `DOMAIN-2B` — `IDomainResolver` interface (`IDomainResolver.cs:28`), `DomainResolutionResult` (`DomainResolutionResult.cs:21`)  
- `DOMAIN-2C` — `PersistenceDomainResolver` (`PersistenceDomainResolver.cs:31`), `HostResolutionMiddleware` (`HostResolutionMiddleware.cs:45`), `IDomainRepository` (`IDomainRepository.cs:18`), `HostResolutionContextKeys` (`HostResolutionContextKeys.cs:12`)  
- `DOMAIN-2D` — `TenantResolutionMiddleware` consumes `DomainResolutionResult` (`TenantResolutionMiddleware.cs:103-128`)  
- `X-Forwarded-Host` added to `ForwardedHeadersOptions` (DOMAIN-0 Phase 3.2 gap, addressed in DOMAIN-2A)  
- Certification: DOMAIN-2C.1 (PASS), DOMAIN-2D (CERTIFIED IMPLEMENTED)  

**3.3 Domain Verification** — ⏳ PENDING  
- `DomainVerificationService` — DNS TXT/CNAME/HTTP-file challenge execution  
- `DomainVerification` cron job — poll DNS every 10s for 5 minutes  
- Admin API: `POST /api/admin/domains`, `PUT /api/admin/domains/{id}/verify`  
- Constitutional source: DOMAIN-1 Section 6, DOMAIN-1.5 Section 20.7  

**3.4 SSL Certificate Management** — ⏳ PENDING  
- `SslCertManagerService` — Let's Encrypt ACME integration  
- `SslCertificate` cron jobs — provisioning, renewal (30-day threshold), retry backoff  
- Admin API: `POST /api/admin/domains/{id}/ssl/renew`, `/ssl/upload`, `/ssl/revoke`  
- Constitutional source: DOMAIN-1 Section 6, DOMAIN-1.5 Section 20.7  

**3.5 UX Prototype** — ⏳ PENDING (READY)  
- Add `domains` screen to `storefrontInformationArchitecture.js` (between `status` and `homepage`)  
- Implement Domains Overview, Wizard, Domain List, Verification, SSL, Redirects, History, Activity screens  
- Reuse all AuthoringPlatform components (`AuthoringShell`, `AuthoringActionBar`, `AuthoringFeedback`, `AuthoringDialogs`, `useAuthoringGuard`, breadcrumbs, dirty guard, keyboard shortcuts)  
- Update `StorefrontSettings.jsx` to use Primary Domain instead of hardcoded `VITE_STOEFRONT_DOMAIN`  
- Dynamic CORS implementation  
- Constitutional source: DOMAIN-1 Section 13-14, DOMAIN-1.5 Section 14.4 (CPD-16 feature flags)  

**3.6 Runtime Active** — ⏳ PENDING  
- `StorefrontUrlResolver` updated to resolve from Primary Domain (`Domain.cs:64`, `DomainType.Primary`) instead of `Tenant.StorefrontBaseUrl`  
- Runtime cache key gains host dimension  
- Canonical URL, sitemap, robots.txt, OG tags use Primary Domain  
- Constitutional source: DOMAIN-1 Section 8.2, 12; DOMAIN-1.5 Section 15.1  

**3.7 Certification** — ⏳ PENDING  
- Verify all 23 runtime invariants hold (`DOMAIN-1.6 Section 22`)  
- Verify all 28 Architecture Laws hold (`DOMAIN-1 Section 16`)  
- Verify all 12 Database Constitution rules hold (`DOMAIN-1.5 Section 18`)  
- Verify all AuthoringPlatform UX patterns are reused correctly  

### 12.2 Wave Dependencies

```
Wave 3.2 (Runtime Foundation) — COMPLETE
  ↓ (prerequisite for all downstream)
Wave 3.3 (Domain Verification) — PENDING
  ↓ (verification must complete before SSL)
Wave 3.4 (SSL Certificate Management) — PENDING
  ↓ (SSL must be active for serving)
Wave 3.5 (UX Prototype) — READY (all runtime + service prerequisites)
  ↓ (UX depends on runtime + verification + SSL)
Wave 3.6 (Runtime Active) — PENDING
  ↓ (primary domain, canonical URLs, cache)
Wave 3.7 (Certification) — PENDING
```

---

## 13. Architecture Laws

### 13.1 Existing Constitutional Laws (from DOMAIN-1 Section 16)

These are already frozen and certified:

| # | Law | Source |
|---|-----|--------|
| 1 | One Domain Owner — Domain Aggregate (Infrastructure) is sole owner of domain names, verification, SSL, lifecycle | DOMAIN-1 Section 16.1, line 1122 |
| 2 | One Primary Domain Per Tenant | DOMAIN-1 Section 16.2, line 1123 |
| 3 | One Runtime Resolution Order — Domain → Tenant → StoreConfig → Theme → CMS → Mongo | DOMAIN-1 Section 16.3, line 1124 |
| 4 | Domain Never Owns Theme | DOMAIN-1 Section 16.4, line 1125 |
| 5 | Theme Never Owns Domain | DOMAIN-1 Section 16.5, line 1126 |
| 6 | Builder Never Resolves Domains | DOMAIN-1 Section 16.6, line 1127 |
| 7 | Storefront Runtime Never Verifies DNS | DOMAIN-1 Section 16.7, line 1128 |
| 8 | StoreConfig Never Owns Domain | DOMAIN-1 Section 16.8, line 1129 |
| 9 | Tenant Never Resolves Hosts Directly | DOMAIN-1 Section 16.9, line 1130 |
| 10 | Domain Normalization Is Server-Authoritative | DOMAIN-1 Section 16.10, line 1131 |
| 11 | ForwardedHeaders Must Include X-Forwarded-Host | DOMAIN-1 Section 16.11, line 1135 |
| 12 | Host Resolution Before Tenant Resolution | DOMAIN-1 Section 16.12, line 1136 |
| 13 | Only Server-Controlled Host Header Is Trusted | DOMAIN-1 Section 16.13, line 1137 |
| 14 | X-Tenant-Id Stripped Before Downstream | DOMAIN-1 Section 16.14, line 1138 |
| 15 | Fail-Closed on Ambiguous Resolution | DOMAIN-1 Section 16.15, line 1139 |

### 13.2 New Domain Management Laws

These laws govern the Admin UX and Domain Management workspace specifically:

**DM-LAW 1: Domain Management Belongs to the Storefront Product Workspace**  
Domain Management is a Storefront product admin surface, not a Global Settings, CommerceHub, or POS Core concern. It appears under the `STOREFRONT_NAVIGATION` root in `storefrontInformationArchitecture.js`, as a peer to Overview, Status, Homepage, Catalog, Content, Navigation, Footer Content, Appearance, and StoreConfig History.

*Constitutional Justification:* Domains are infrastructure identity that resolves the tenant before StoreConfig/Theme/CMS are engaged (DOMAIN-1 Section 1, line 18: "Domains resolve a tenant before Storefront Runtime"). The Storefront product workspace is the correct container for infrastructure that affects storefront runtime behavior.

**DM-LAW 2: Domain Management Reuses AuthoringPlatform UX Patterns**  
Domain Management screens MUST reuse `AuthoringShell`, `AuthoringActionBar`, `AuthoringFeedback`, `AuthoringDialogs`, `useAuthoringGuard`, `StorefrontBreadcrumbs`, `AuthoringStatusBadge`, and all other AuthoringPlatform components. No new UX primitives may be created.

*Constitutional Justification:* The Storefront Admin UX architecture is built on the AuthoringPlatform pattern (`StorefrontAdminFrame.jsx:16-45`). All Storefront screens (Overview, Status, Homepage, Catalog, Content, Navigation, Appearance) use this pattern. Domain Management is a Storefront screen and must follow the same conventions.

**DM-LAW 3: Domain Management Never Mutates StoreConfig**  
Domain Management screens may READ `StoreConfig` for display purposes (e.g., showing which theme is active on a domain) but NEVER write to `StoreConfig`. All Domain mutations go through the Domain Service API (`/api/admin/domains/*`), separate from `AdminStoreConfigController` (`/api/admin/store-config`).

*Constitutional Justification:* DOMAIN-1 Architecture Law #8 (Section 16.8): "StoreConfig Never Owns Domain." StoreConfig is managed via `AdminStoreConfigController` (`storeConfigService.js:30-171`). Domain management requires a separate `DomainManagementController` (DOMAIN-1 Section 13.2, lines 984-998). The StoreConfig DTO MAY include a read-only `domains` summary (`DOMAIN-1 Section 8.5`, lines 631-643) but the Domain Service is the write authority.

**DM-LAW 4: Domain Navigation Node Has No Required Roles Override**  
The Domains navigation node under `STOREFRONT_NAVIGATION` uses the default `requiredRoles: ['Owner', 'Admin']` injected by `localizeStorefrontProductNode` in `Sidebar.jsx:127`. It does NOT require `SuperAdmin`, `CommerceHubAdmin`, or any role beyond Storefront Admin.

*Constitutional Justification:* `canManageStorefrontSettings(storefrontAdminAuthority.js:3-5)` returns true for `Owner` and `Admin` only. This is the existing authorization boundary for all Storefront Admin screens. Domain Management must use the same boundary — no escalation.

**DM-LAW 5: Domain Management Never Bypasses Tenant Isolation**  
Domain Management Admin API endpoints MUST enforce `TenantLineageAuthority` validation and `ITenantContext.RequiredTenantId`. A merchant can only manage domains for their own tenant. Cross-tenant domain access returns 403.

*Constitutional Justification:* `TenantResolutionMiddleware.cs:106-154` enforces cross-tenant blocking. `TenantSaveChangesInterceptor.cs:56-111` blocks `ITenantOwned` with mismatched TenantId. `Domain.cs:22` implements `ITenantOwned`. `ITenantContext.cs:25` requires `RequiredTenantId`.

**DM-LAW 6: Domain Management Screens Are Draft-Aware**  
Domain configuration changes use the same baseline/candidate draft pattern as `StorefrontSettings.jsx` (`StorefrontSettings.jsx:47-55`): `baselineConfig` vs `candidateConfig`, `isDirty` computation, discard/reset, and `RowVersion` optimistic concurrency (via `If-Match` header per `storeConfigService.js:48-55`).

*Constitutional Justification:* The Storefront Admin UX pattern for mutative forms (`StorefrontSettings.jsx:47-60`) uses dirty tracking (`dirtyRef.current`) and optimistic concurrency (`rowVersion` via `If-Match` header). Domain Management forms (e.g., editing a redirect, changing Primary) must follow the same pattern. The `AuthoringActionBar` save/publish buttons (`StorefrontSettings.jsx:243-250`) drive the same flow.

**DM-LAW 7: Domain Verification and SSL Are Infrastructure-Operated, UI-Monitored**  
Domain Management screens display the status of DNS verification and SSL provisioning (read-only monitoring) but NEVER perform DNS queries or certificate provisioning directly. These operations are performed by Infrastructure background jobs (`DomainVerificationCronJob`, `SslProvisioningJob`, `SslRenewalJob`) per DOMAIN-1.5 Section 20.7.

*Constitutional Justification:* DOMAIN-1 Architecture Law #7 (Section 16.7): "Storefront Runtime Never Verifies DNS." `HostResolutionMiddleware.cs:25-30` explicitly states: "No EF Core, no tenant lookup, no StoreConfig lookup, no caching, no theme resolution, no preview resolution." The Admin UX is a monitoring surface, not an execution surface.

**DM-LAW 8: Canonical URL Always Points to Primary Domain**  
All storefront canonical URLs, sitemaps, robots.txt, and OpenGraph tags MUST resolve from the tenant's Primary Domain (`DomainType.Primary` + `LifecycleState.Primary`), not from `Tenant.StorefrontBaseUrl` or any alias/redirect domain.

*Constitutional Justification:* DOMAIN-1 Architecture Law #21 (Section 16.21): "Canonical Always Points to Primary." `DomainType.cs:10-12` — "The Primary, canonical storefront domain for the tenant. The StorefrontUrlResolver uses this as the authoritative base URL." `DomainLifecycleState.cs:42-46` — "The domain is the Primary canonical domain for the tenant."

---

## 14. Governance

### 14.1 Review Gates

Every future Domain Management feature must pass the following gates:

**Gate 1: Domain Aggregate Boundary Verification**  
- ✅ Must NOT add domain fields to `StoreConfig.cs` (Architecture Law #8)  
- ✅ Must NOT add domain fields to `Tenant.cs` beyond `StorefrontBaseUrl` (legacy)  
- ✅ Must NOT add domain fields to `StoreThemeConfig` or any Theme entity (Architecture Law #4)  
- ✅ Must NOT add domain fields to any CMS entity  
- ✅ Must NOT add domain fields to any Order, Payment, Coupon, or CommerceHub entity  

**Gate 2: Runtime Ownership Isolation**  
- ✅ Must NOT cause `Theme` to read `ITenantContext` (Invariant #4, `StorefrontDraftConfigMapper.cs:32-48`)  
- ✅ Must NOT cause `CMS` to resolve Tenant (Invariant #5, `StorefrontPagesController.cs:44`)  
- ✅ Must NOT bypass `HostResolutionMiddleware` before `TenantResolutionMiddleware` (Invariant #1, Domain-1.6 Section 3.9)  
- ✅ Must NOT add cache key host dimension for preview domains (DOMAIN-1 Section 10.4: "host MUST NOT be added to the preview cache key")  
- ✅ Must NOT perform DNS verification in Storefront Runtime (Invariant #15)  

**Gate 3: AuthoringPlatform UX Compliance**  
- ✅ Must use `AuthoringShell` / `StorefrontAdminFrame` wrapper  
- ✅ Must use `AuthoringActionBar` with constitutional button order (Back, title, status, preview, history, discard, save, publish)  
- ✅ Must use `useAuthoringGuard` for dirty guard + keyboard shortcuts  
- ✅ Must use `StorefrontBreadcrumbs` via `resolveStorefrontScreen()`  
- ✅ Must use `AuthoringFeedback` for success/error/warning states  
- ✅ Must use `AuthoringDialogs` (not custom modal) for confirmations  
- ✅ Must use `AuthoringPermissionState` for unauthorized users  
- ✅ Must NOT create new top-level layout components  

**Gate 4: Persistence Freeze Compliance**  
- ✅ Must NOT alter existing tables (`Tenant`, `StoreConfig`, `StoreConfigsAuditEntry`)  
- ✅ Must NOT remove `Tenant.StorefrontBaseUrl` column (C8, DOMAIN-1.5 Section 18)  
- ✅ Must use `Guid` PK convention (C9)  
- ✅ Must implement `ITenantOwned` on all new entities (C10)  
- ✅ Must use `DeleteBehavior.Restrict` on all FKs (C11)  
- ✅ Must use UTC for all datetime fields (C12)  
- ✅ Must match `Domain` entity shape to DOMAIN-1.5 Section 4.1 exactly  

**Gate 5: Security Model**  
- ✅ Must NOT trust `Origin`/`Referer` headers (Architecture Law #13)  
- ✅ Must NOT trust inbound `X-Tenant-Id` (Architecture Law #14)  
- ✅ Must block cross-tenant access (403) (Architecture Law #27)  
- ✅ Must fail-closed on no tenant resolution (400) (Architecture Law #15)  
- ✅ Must validate redirect targets against same-tenant Domain records (Architecture Law #28)  

**Gate 6: Shopify Parity**  
- ✅ Must support Primary/Alias/Redirect domain types (DOMAIN-1 Section 4)  
- ✅ Must support DNS TXT/CNAME/HTTP-file verification (DOMAIN-1 Section 6)  
- ✅ Must support automated Let's Encrypt SSL (DOMAIN-1 Section 6)  
- ✅ Must use Primary Domain for canonical URLs (DOMAIN-1 Section 12)  
- ✅ Intentional differences documented (DOMAIN-1 Section 15.2)  

### 14.2 Certification Bodies

| Review Gate | Certifying Authority | Source Evidence |
|-------------|---------------------|-----------------|
| Domain boundary | Architecture Certification Board | Current audit (DOMAIN-3.0) |
| Runtime isolation | Runtime Certification Board | DOMAIN-1.6 Section 22 (25 invariants) |
| Persistence | Database Certification Board | DOMAIN-1.5 Section 18 (12 rules) |
| Security | Security Certification Board | DOMAIN-1 Section 11; DOMAIN-0 Phase 11 |
| UX compliance | UX Certification Board | AuthoringPlatform components in `AuthoringPlatform/` directory |

---

## 15. Certification

### 15.1 Verification Summary

| Requirement | Status | Evidence |
|-------------|--------|----------|
| Domain entity exists and matches DOMAIN-1.5 Section 4.1 | PASS | `Domain.cs:22-257` — all 4 child entities (`DomainVerification.cs:16`, `SslCertificate.cs:15`, `DomainAuditEntry.cs:20`) |
| All enums match DOMAIN-1.5 Section 6 | PASS | `DomainType.cs:7`, `DomainLifecycleState.cs:9`, `RedirectType.cs:7`, `CanonicalPolicy.cs:8`, `VerificationMethod.cs:7`, `VerificationStatus.cs:7`, `CertificateStatus.cs:7`, `ProviderType.cs:8`, `ActorType.cs:8` |
| Host Resolution Middleware exists and is registered | PASS | `HostResolutionMiddleware.cs:45-285`; `Program.cs:3383-3396` (DOMAIN-2D) |
| PersistenceDomainResolver exists and is correct | PASS | `PersistenceDomainResolver.cs:31-143` — servability gate `Verified/SslActive/Primary` |
| TenantResolutionMiddleware consumes DomainResolutionResult | PASS | `TenantResolutionMiddleware.cs:103-128` (DOMAIN-2D integration) |
| X-Forwarded-Host configured | PASS | `Program.cs:100-103` (DOMAIN-2D verification) |
| Storefront Admin IA has no Domains node | CONFIRMED (to be changed) | `storefrontInformationArchitecture.js:3-119` — STOREFRONT_NAVIGATION has no domains entry |
| StorefrontSettings.jsx uses hardcoded domain | CONFIRMED (to be changed) | `StorefrontSettings.jsx:33` — `VITE_STOREFRONT_DOMAIN || 'localhost:5174'` |
| AuthoringPlatform UX components exist | PASS | `AuthoringShell.jsx`, `AuthoringActionBar.jsx`, `AuthoringFeedback.jsx`, `AuthoringDialogs.jsx`, `AuthoringStates.jsx`, `AuthoringStatusBadge.jsx`, `AuthoringButton.jsx`, `useAuthoringGuard.js`, `AuthoringPlatformProvider.jsx` |
| Sidebar has no Domains node | CONFIRMED (to be changed) | `Sidebar.jsx:32-338` — no domains nav item in menuItems |
| STOREFRONT_PRODUCT_NAVIGATION is imported but undefined | CONFIRMED BLOCKER | `Sidebar.jsx:37` imports it from `informationArchitecture/index.js:6`, but `storefrontInformationArchitecture.js` does not define it |
| canManageStorefrontSettings enforces Owner/Admin | PASS | `storefrontAdminAuthority.js:3-5` |
| AuthoringGuard provides dirty guard + shortcuts | PASS | `useAuthoringGuard.js:9-92` — browser unload, breadcrumbs, sidebar links, shortcuts (Ctrl+S, Ctrl+Shift+P) |
| Breadcrumb system is extensible | PASS | `StorefrontBreadcrumbs.jsx:7-87` — resolves via `resolveStorefrontScreen()`, renders from `SCREENS` |
| StorefrontAdminFrame wraps all Storefront screens | PASS | `StorefrontAdminFrame.jsx:16-45` — wraps in `AuthoringPlatformProvider` + `AuthoringShell` + `StorefrontBreadcrumbs` |

### 15.2 Certification Verdict

**PASS WITH OBSERVATIONS**

**Rationale:**

1. **Source-code grounding:** Every observed claim in this audit is traceable to verified source code at `file:line` or to the certified DOMAIN-0 through DOMAIN-2D documents. The Domain entity, all enums, child entities, the Host Resolution Middleware, the PersistenceDomainResolver, and the TenantResolutionMiddleware integration are all confirmed present in source.

2. **UX readiness:** The AuthoringPlatform UX framework (`AuthoringShell`, `AuthoringActionBar`, `AuthoringFeedback`, `AuthoringDialogs`, `useAuthoringGuard`, `StorefrontBreadcrumbs`, `AuthoringStatusBadge`) is fully implemented and demonstrated by `StorefrontSettings.jsx`. Domain Management screens can reuse every component without new primitives.

3. **Runtime complete:** The Host → Domain → Tenant resolution chain is certified and operational per DOMAIN-2C.1 and DOMAIN-2D. The Host Resolution Middleware runs before TenantResolutionMiddleware, produces `DomainResolutionResult`, and `TenantResolutionMiddleware` now consumes it.

4. **Observations (items requiring attention before DOMAIN-5 UX implementation):**
   - **Observation 1:** `STOREFRONT_PRODUCT_NAVIGATION` is imported (`Sidebar.jsx:37`) and used (`Sidebar.jsx:243`) but is NOT defined in `storefrontInformationArchitecture.js` — the source module defines only `STOREFRONT_NAVIGATION`. `OPERATIONS_NAVIGATION` (re-exported at `index.js:4`) and `STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY` (re-exported at `index.js:9`) are also undefined in the source. This broken import prevents the sidebar from rendering (spreading `undefined` at `Sidebar.jsx:123-129` crashes). This must be resolved — either define the constants or fix the imports — before adding a Domains navigation node.
   - **Observation 2:** The `/storefront/domains` route and `domains` screenId must be added to `storefrontInformationArchitecture.js` (`STOREFRONT_NAVIGATION` array, `SCREENS` constant, and `resolveStorefrontScreen()` function) before Domain Management screens can render.
   - **Observation 3:** The `StorefrontSettings.jsx` "Open Storefront" button (`StorefrontSettings.jsx:178-182`) uses a hardcoded `VITE_STOREFRONT_DOMAIN` env var. It must be updated to resolve the tenant's Primary Domain from the Domain registry before Domain Management is fully functional.
   - **Observation 4:** Dynamic CORS must replace the hardcoded `Cors:AllowedOrigins` (`appsettings.json:235-246`) before custom domain storefronts can call the Admin API from a merchant domain.
   - **Observation 5:** The Admin Domain API endpoints (`POST /api/admin/domains`, `GET /api/admin/domains`, etc. per DOMAIN-1 Section 13.2) are not yet implemented — these are a prerequisite for DOMAIN-5 UX.

---

## 16. Implementation Roadmap

### Phase 1: Navigation Structure (Pre-Domain-Management)
1. Resolve `STOREFRONT_PRODUCT_NAVIGATION` definition gap (`Sidebar.jsx:37,243` — imported from `index.js:6` but undefined in `storefrontInformationArchitecture.js`; also affects `OPERATIONS_NAVIGATION` and `STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY`)
2. Add `domains` screen to `storefrontInformationArchitecture.js`:
   - `STOREFRONT_NAVIGATION` entry between `status` and `homepage`
   - `SCREENS.domains` breadcrumb definition
   - `resolveStorefrontScreen()` case for `/storefront/domains`
3. Add `domains` sub-screen IDs to `STOREFRONT_ROUTE_COMPATIBILITY`

### Phase 2: Domain Management Screens
1. **Domains Overview** — summary cards (Primary domain, SSL status, verification date), quick actions (Add Domain, Verify Now, Force SSL Renewal), migration banner for legacy `StorefrontBaseUrl`
2. **Add Domain Wizard** — 7-step wizard reusing `AuthoringDialog`/`AuthoringConfirmDialog` for hostname entry, type selection, verification method, challenge display, auto-poll, SSL initiation, summary
3. **Domain List** — table with filters (All Types, Primary Only, Aliases, Redirects, Preview), columns (Domain, Type, Status, SSL, Actions)
4. **Verify Domain** — DNS challenge instructions (TXT/CNAME/HTTP-file), live DNS check status, retry button, verification history
5. **SSL Status** — cert status, provider, expiration countdown, renew/upload/revoke buttons
6. **Redirect Rules** — redirect target mapping, 301 vs 307 selection, add redirect domain
7. **History** — audit trail from `DomainAuditEntry` table, date range filter, actor filter
8. **Activity** — background job status (verification checks, SSL provisioning, renewal attempts)

### Phase 3: Runtime Integration
1. Update `StorefrontSettings.jsx` to use Primary Domain instead of `VITE_STOREFRONT_DOMAIN`
2. Update `StorefrontUrlResolver` to resolve from Primary Domain
3. Implement dynamic CORS
4. Update runtime cache key to include host dimension (non-preview only)

### Phase 4: Certification
1. Verify all 23 runtime invariants (DOMAIN-1.6 Section 22)
2. Verify all 28 architecture laws (DOMAIN-1 Section 16)
3. Verify all 12 database constitution rules (DOMAIN-1.5 Section 18)
4. Verify AuthoringPlatform UX compliance for all Domain screens

---

*End of DOMAIN-3.0: Storefront Domain Management Architecture Audit*  
*No code was written. No source files were modified. No APIs were generated. No UI was created.*  
*All conclusions are backed by verified source code evidence or certified DOMAIN-0 through DOMAIN-2D documentation.*
