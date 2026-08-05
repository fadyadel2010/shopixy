# DOMAIN-1: Storefront Custom Domains — Canonical Architecture Blueprint

**Project:** Shopixy / PointOfSale
**Platform:** Multi-tenant e-commerce / affiliate / POS
**Status:** CANONICAL ARCHITECTURE (CONSTITUTIONAL AUTHORITY)
**Authority:** `.ai-context`
**Precedent:** DOMAIN-0 (Storefront Custom Domains Architecture Audit)
**Shopixy Rules:** PostgreSQL authoritative; Mongo/Redis/ES/WhatsApp derivative. TenantId necessary-not-sufficient. Tenant isolation CRITICAL.

---

## Executive Summary

This document is the **constitutional authority** for how Custom Domains operate across the entire Shopixy platform. It is the permanent architecture for connecting merchant-owned domains (`mybrand.com`, `www.mybrand.com`, `shop.mybrand.com`, or any valid domain) to a merchant's Storefront Runtime.

It is built directly on the DOMAIN-0 audit findings and **does not contradict them**. Where DOMAIN-0 observed the current state (single `StorefrontBaseUrl` string per tenant, no host-resolution middleware, no SSL management, no domain entity, hardcoded CORS, no Admin UX), this blueprint defines the **canonical target architecture** that all future implementations must respect.

**Core Principle — Domains are infrastructure identity, not presentation.**
Domains resolve a tenant *before* Storefront Runtime, Theme Runtime, Builder, Preview, or StoreConfig are engaged. Domains never own themes. Themes never own domains. Builders never resolve domains. The Storefront Runtime never verifies DNS. Infrastructure never edits StoreConfig.

**Certification Verdict: PASS WITH OBSERVATIONS**

> See Section 20 (Certification) for the full rationale. The blueprint is structurally sound and grounded in DOMAIN-0 findings. Observations relate to migration risk areas and forward-compatibility commitments that must be honored by downstream implementation waves (DOMAIN-2 through DOMAIN-6).

---

## Vision

Shopixy must support every domain topology a merchant can conceive of, without architectural redesign:

- **Custom Domains** — arbitrary merchant-owned hostnames pointing to a tenant's storefront
- **Primary Domains** — the canonical host for SEO, links, and StoreConfig resolution
- **Alias Domains** — additional hosts that serve identical content and canonicalize to the primary
- **Redirect Domains** — hosts that issue HTTP 301 redirects to the primary domain
- **Preview Domains** — temporary or custom hosts for draft/theme preview isolation
- **Wildcard Domains** (future) — `*.mybrand.com` mapping to tenant storefronts
- **International Domains** (future) — locale/ccTLD-based domain routing
- **Regional Domains** (future) — geo-based domain resolution at the edge
- **White-label Domains** (future) — platform-provided domains for white-label resellers
- **Marketplace Domains** (future) — domains for marketplace storefronts

All topologies must resolve to a single Storefront Runtime per tenant without requiring Storefront Runtime changes.

---

## 1. Product Boundaries — Ownership Matrix

DOMAIN-0 established that no Domain entity, DomainVerification entity, or SslCertificate entity currently exists (Phase 6, lines 309-321). The only domain-like storage is `Tenant.StorefrontBaseUrl` (single string, unindexed, no admin UI/API — Phase 2, lines 119-127). This section defines the **constitutional** ownership model that DOMAIN-2 implementation must realize.

| Concern | Owner | Authority | Evidence from DOMAIN-0 |
|---|---|---|---|
| Domain name (hostname) | Domain Aggregate (Infra) | Owns the `Domain` table, verified against DNS | DOMAIN-0: No Domain entity exists (Phase 6) |
| Domain ownership verification (TXT/CNAME challenge) | Infrastructure (Domain Service) | Server-authoritative DNS proof | DOMAIN-0: No verification mechanism exists (Phase 11) |
| SSL certificate lifecycle (provision, bind, renew, revoke) | Infrastructure (Cert Manager) | Platform-managed certs via Let's Encrypt or upload | DOMAIN-0: Zero SSL/cert search matches (Phase 14) |
| Primary domain selection | Domain Aggregate | Exactly one `IsPrimary` per tenant | DOMAIN-0: No canonical selection (Phase 12) |
| Redirect target mapping | Domain Aggregate | `RedirectType` + `RedirectTargetDomainId` | DOMAIN-0: No redirect concept (Phase 12) |
| Host → tenant resolution | Host Resolution Middleware | Server-controlled Host header only | DOMAIN-0: Only TenantResolutionMiddleware reads Host (Phase 3) |
| StoreConfig | StoreConfig Aggregate | `StoreConfig.cs` — presentation/behavior, NOT domain | DOMAIN-0: StoreConfig has no domain fields (Phase 4) |
| Theme selection/rendering | Theme Runtime | `ThemeId` from StoreConfig | DOMAIN-0: Theme identified by single ThemeId (Phase 5.1) |
| Builder context | Builder Runtime | Operates on resolved TenantContext | DOMAIN-0: Builder uses TenantContext, no domain branching (Phase 5.2) |
| Preview isolation | Preview Authority Middleware | Signed `themePreviewToken` JWT | DOMAIN-0: Preview uses token JWT (Phase 5.3) |
| CORS origin allowlisting | Infrastructure (dynamic CORS) | Origin derived from resolved domain | DOMAIN-0: CORS hardcoded origins (Phase 7.3) |
| `X-Tenant-Id` header | TenantResolutionMiddleware | Stripped inbound, re-added only after resolution | DOMAIN-0: Header stripped at line 82, re-added at line 180 |
| Canonical URL generation | SeoService (backend) | `StorefrontUrlResolver` → `Tenant.StorefrontBaseUrl` | DOMAIN-0: SeoService uses StorefrontUrlResolver (Phase 12.1) |

### What Domains Own
- The hostname string (normalized, lowercased, unique globally)
- DNS verification proof (TXT token, CNAME target, verification timestamp)
- SSL certificate state (status, expiration, renewal schedule, certificate ARN/id)
- Lifecycle state (draft → pending → verified → ssl pending → ssl active → primary/disabled/archived/deleted)
- Redirect mapping (which domain redirects to which, with HTTP status code)
- Primary/alias classification (exactly one primary per tenant)
- Verification history (audit trail of who verified what, when)

### What Domains Never Own
- **Theme** — `StoreConfig.Theme` / `ThemeId` is resolved independently (DOMAIN-0 Phase 5.1)
- **StoreConfig** — Presentation, behavior, status, pages, product templates (DOMAIN-0 Phase 4)
- **Builder state** — Builder operates on StoreConfig, not domains
- **Preview token issuance** — Preview tokens are generated by `IStorefrontThemePreviewTokenService`, not domains (DOMAIN-0 Phase 5.3)
- **SEO metadata content** — SeoService generates canonical URLs using the resolved domain but content comes from StoreConfig + product/catalog data
- **Merchant certificate files** — Platform provisions or validates uploaded certs; merchant never edits live certificates directly

### Builder Responsibilities
- Operates on `TenantContext` set by middleware chain
- Fetches StoreConfig via `GET /api/builder/config` (DOMAIN-0 Phase 5.2)
- No domain-based branching — builder always operates on the tenant's single resolved StoreConfig
- Preview links generated via `StorefrontUrlResolver` using the resolved domain (DOMAIN-0 Phase 5.2)

### Theme Responsibilities
- Resolved from `StoreConfig.ThemeId` / `StoreConfig.ThemeLivePresetKey` (DOMAIN-0 Phase 5.1)
- Renders identically regardless of which domain serves it
- Domain-specific theming is NOT a responsibility — themes are domain-independent (DOMAIN-0 Phase 9)

### StoreConfig Responsibilities
- Presentation configuration (store name, description, theme, behavior, pages, status)
- Does NOT own domains, certificates, or DNS verification (DOMAIN-0 Phase 4)
- Read by storefront bootstrap via `GET /api/storefront/store-config` (DOMAIN-0 Phase 1)
- Read-only from storefront perspective; managed via `AdminStoreConfigController` (DOMAIN-0 Phase 4.3)

### Tenant Responsibilities
- Owns `Tenant.StorefrontBaseUrl` (legacy single domain — DOMAIN-0 Phase 2.4)
- Owns `Slug` (indexed, used for fallback subdomain routing — DOMAIN-0 Phase 2.3)
- Tenant isolation enforced via `TenantLineageAuthority`, `ITenantContext.RequiredTenantId`, JWT claims (DOMAIN-0 Phase 11.4)
- Domain resolution must produce a `TenantId` before downstream middleware runs

### Storefront Runtime Responsibilities
- Bootstraps via `bootstrap.ts` → `resolveTenant()` → `refreshStoreConfig()` — DOMAIN-0 Phase 1.1
- Cache keyed by `tenantId` only — must gain a host dimension (DOMAIN-0 Phase 1.3)
- Never verifies DNS directly — DNS verification is infrastructure responsibility
- Never owns domains — domains resolve it

### Infrastructure Responsibilities
- Host Resolution Middleware (new — to be inserted before TenantResolutionMiddleware)
- X-Forwarded-Host forwarding in `ForwardedHeadersOptions` (DOMAIN-0 Phase 3.2 — currently MISSING)
- Dynamic CORS origin resolution (DOMAIN-0 Phase 7.3 — currently hardcoded)
- SSL certificate provisioning, binding, renewal (DOMAIN-0 Phase 14.2 — currently absent)
- Reverse proxy (Caddy) TLS config for custom domains (DOMAIN-0 Phase 14.1)

---

## 2. Domain Aggregate — Canonical Design

DOMAIN-0 confirmed: there is NO `Domain` entity, NO domain table in migrations, NO `DbSet<Domain>` in `ApplicationDbContext` (lines 292-301). The only domain-like state is a plain `string StorefrontBaseUrl` column on `Tenants` (migration `Baseline_Recovery3.Designer.cs:19595`, no index, no constraint).

The canonical Domain Aggregate is designed as follows, respecting all DOMAIN-0 findings:

### Aggregate Root
**Entity: `Domain`** (SQL table: `Domains`)
- `Id` (Guid PK) — domain record identifier
- `TenantId` (Guid FK → Tenants) — owning tenant; `NOT NULL`, indexed
- `HostName` (string) — raw hostname as entered (e.g., `www.mybrand.com`), `NOT NULL`
- `NormalizedHostName` (string) — lowercased + punycode normalized, `NOT NULL`, `UNIQUE` globally
- `DomainType` (enum: Primary, Alias, Redirect, Preview, Temporary, Development, System)
- `LifecycleState` (enum: Draft, PendingVerification, Verified, SslPending, SslActive, Primary, Disabled, Archived, Deleted)
- `SslCertificateId` (Guid nullable FK → SslCertificates) — linked when SSL is active
- `VerificationToken` (string) — TXT record value for DNS challenge
- `VerificationMethod` (enum: DnsTxt, DnsCname, HttpFile)
- `RedirectType` (enum: None, Permanent301, Temporary307) — only for Redirect domains
- `RedirectTargetDomainId` (Guid nullable FK → Domains) — target domain for redirects
- `CanonicalPolicy` (enum: UseAsPrimary, CanonicalizeToPrimary) — for alias domains
- `CreatedAtUtc`, `VerifiedAtUtc`, `SslActivatedAtUtc`, `UpdatedAtUtc`
- `Version` (row version / `xmin` for optimistic concurrency)

### Child Entities

**Entity: `DomainVerification`** — verification attempt history
- `Id` (Guid PK)
- `DomainId` (Guid FK → Domains)
- `Method` (DnsTxt, DnsCname, HttpFile)
- `Token` (string) — the verification token presented to DNS/HTTP
- `Status` (enum: Pending, Success, Failed)
- `AttemptedAtUtc`, `VerifiedAtUtc`
- `FailureReason` (string nullable)

**Entity: `SslCertificate`** — certificate state
- `Id` (Guid PK)
- `DomainId` (Guid FK → Domains, unique per active cert)
- `Provider` (enum: LetsEncrypt, CustomUpload, SystemDefault)
- `Status` (enum: Pending, Active, Expired, Revoked, Failed)
- `CertificateArn` / `Thumbprint` (string) — platform-managed reference
- `NotBeforeUtc`, `NotAfterUtc`
- `LastRenewalAttemptUtc`, `NextRenewalUtc`
- `FailureReason` (string nullable)

### Value Objects
- `NormalizedHostName` — punycode-normalized lowercase hostname; uniqueness enforced at DB level
- `VerificationToken` — cryptographically random string for DNS/HTTP challenge
- `SslCertificateRef` — opaque platform certificate identifier (e.g., ACM ARN, thumbprint)

### Lifecycle & State Machine

The Domain lifecycle state machine (every transition defined):

```
[Draft]
   │ (merchant submits host)
   ▼
[PendingVerification]
   │ (merchant proves DNS ownership)
   ▼
[Verified]
   │ (platform provisions SSL)
   ├─── SSL fails or no custom cert ──▶ [SslPending] ──(retry)──┐
   │                                                        │
   ▼                                                        │
[SslActive] ◄──(renewal/re-provision)── [SslPending]       │
   │                                     ▲                 │
   │ (set as primary)                    │                 │
   ▼                                     │                 │
[Primary]                               │ (retry)         │
   │                                     │
   ├─── disable ──▶ [Disabled] ───(reactivate)──▶ [SslActive] or [Verified]
   │
   ├─── archive ──▶ [Archived] ───(reactivate)──▶ [SslActive] or [Verified]
   │
   └─── delete ──▶ [Deleted]  (hard delete after retention; or soft-delete)
```

**Transition rules:**
1. **Draft → PendingVerification**: Merchant enters a valid hostname. Platform validates format and global uniqueness. Generates `VerificationToken`.
2. **PendingVerification → Verified**: Platform confirms DNS proof (TXT/CNAME/HTTP-file) matches `VerificationToken`. Sets `VerifiedAtUtc`.
3. **Verified → SslPending**: Platform initiates certificate provisioning (Let's Encrypt order or validates uploaded cert).
4. **SslPending → SslActive**: Certificate provisioned and bound. Sets `SslActivatedAtUtc`.
5. **SslPending → SslPending**: Retry after failure (backoff schedule: 1m, 5m, 15m, 1h, 4h, 12h, 24h).
6. **SslActive → SslActive**: Renewal extends `NotAfterUtc` without state transition.
7. **SslActive → SslPending**: Renewal failure detected. Enters retry loop.
8. **SslActive → Verified**: Certificate revoked or expired beyond grace period. SSL disabled but domain remains verified.
9. **SslActive → Primary**: Tenant admin marks this verified+SSL-active domain as canonical primary (must be unique per tenant).
10. **SslActive → Disabled**: Merchant or platform disables domain. Stops serving. Reversible.
11. **SslActive → Archived**: Merchant archives domain (retains config, stops serving). Reversible.
12. **Disabled → SslActive**: Merchant reactivates domain.
13. **Archived → SslActive**: Merchant reactivates domain.
14. **Any active state → Deleted**: Merchant deletes domain. Hard-deletable only if not primary and no active SSL cert. Soft-delete retained per data retention policy. `DeletedAtUtc` set.
15. **Deleted → Deleted**: Re-attempt triggers restoration if within retention window.

### Events
- `DomainRegistered` — domain created in Draft, verification token generated
- `DomainVerificationAttempt` — merchant triggered or scheduled verification
- `DomainVerified` — DNS proof confirmed
- `DomainVerificationFailed` — DNS check failed
- `SslProvisioningStarted` — certificate request initiated
- `SslActivated` — certificate provisioned and bound
- `SslActivationFailed` — certificate provisioning failed
- `SslRenewed` — certificate successfully renewed
- `SslRenewalFailed` — renewal attempt failed
- `SslRevoked` — certificate revoked
- `DomainPromotedToPrimary` — domain set as canonical primary
- `DomainDowngradedFromPrimary` — a new primary was set, this was demoted
- `DomainDisabled` — domain stopped serving
- `DomainArchived` — domain archived
- `DomainReactivated` — domain restored from disabled/archived
- `DomainDeleted` — domain deleted (soft)
- `DomainRedirectConfigured` — redirect target set
- `DomainOwnershipTransferred` — tenantId reassigned (platform-admin only, audited)

### Audit
Following the existing `StoreConfigAuditEntry` pattern (`StoreConfigAuditEntry.cs:14-98`), a `DomainAuditEntry` table records:
- `Id`, `DomainId`, `TenantId`
- `Action` (enum-backed string matching events)
- `ActorUserId` / `ActorType` (Merchant, System, PlatformAdmin)
- `BeforeSnapshotJson`, `AfterSnapshotJson`
- `TraceId`, `IpAddress`, `UserAgent`
- `TimestampUtc`

### Ownership
- `Domain` aggregate is owned by **Infrastructure** (the platform), not by StoreConfig, Builder, or Tenant.
- `TenantId` is the join key — domains belong to a tenant but are managed by the platform's Infrastructure layer.
- `Tenant.StorefrontBaseUrl` (legacy) is migrated to a Primary domain (DOMAIN-0 Phase 17: Phase B migration).
- Tenant owns the *configuration* of which domain is primary; Infrastructure owns the *verification* and *certificate lifecycle*.

### History
- Every state transition records a `DomainAuditEntry`.
- Verification attempts are tracked in `DomainVerification` child entity.
- SSL renewal history is tracked via `SslCertificate.NotBeforeUtc`/`NotAfterUtc` + audit entries.
- `VersionsJson` pattern from `StoreConfig` (line 109) is mirrored: `DomainHistoryJson` stores periodic JSON snapshots of all domain attributes.

---

## 3. Domain Types

DOMAIN-0 found no domain types at all — only a single unstructured `StorefrontBaseUrl` string (Phase 2). The canonical model defines typed domains to support all current and future topologies.

### Canonical Domain Types

| Type | Purpose | Behavior | Example |
|---|---|---|---|
| **Primary** | Canonical storefront domain for the tenant. Used for SEO canonical URLs, sitemap base, and all generated links. | Serves full storefront. Exactly one per tenant. Other domains canonicalize to this. | `mybrand.com` |
| **Alias** | Additional domain serving identical content. | Serves full storefront. Canonical `<link>` points to Primary. 301 redirect to Primary is optional (configurable: canonicalize-only vs redirect). | `www.mybrand.com`, `mybrand.shop` |
| **Redirect** | Domain that redirects all traffic to Primary (or another target). | Issues HTTP 301 (Permanent301) or 307 (Temporary307) redirect to `RedirectTargetDomainId` or URL. Never serves storefront. | `oldbrand.com → mybrand.com` |
| **Preview** | Custom domain for theme/draft preview isolation. | Serves storefront with `themePreviewToken` JWT validation. Does NOT serve live traffic. Can be merchant-controlled or platform-assigned. | `preview.mybrand.com` |
| **Temporary** | Short-lived domain for development, testing, or migration. | Auto-expiring (TTL-based). Deleted/archived after timeout. May lack SSL. | `staging-12345.platform.io` |
| **Development** | Local development domain. | Platform-controlled, local-only. No SSL, no public DNS. Used for local storefront preview. | `localhost:5174`, `dev-{slug}.localhost` |
| **System** | Platform-reserved domain for infrastructure functions. | Platform-managed. Not merchant-editable. Hosts API endpoints, health checks, internal services. | `api.shopixy.com`, `cdn.shopixy.com` |

### Extensibility

The `DomainType` enum is designed for future expansion:
- **Wildcard** (future) — `*.mybrand.com` with a single DNS challenge for the apex
- **International** (future) — ccTLD-based domain with locale resolution
- **Regional** (future) — geo-targeted domain resolved by edge location
- **Marketplace** (future) — marketplace storefront domain
- **WhiteLabel** (future) — platform-provided domain for reseller white-labeling
- **Email** (future) — domain used for merchant email delivery (MX validation)

New types are added as enum values + middleware handling; no schema redesign required.

---

## 4. Domain Lifecycle

DOMAIN-0 observed that `Tenant.LifecycleState` (Draft, Live, Maintenance, Disabled) governs storefront visibility (Phase 4, lines 214-222) — but this is StoreConfig status, not Domain status. Domains require their own lifecycle independent of StoreConfig.

### Full Lifecycle Definition

The Domain lifecycle has 9 states with 15 defined transitions:

```
┌─────────────┐
│   Draft     │
│ (created,   │
│  not yet   │
│  submitted) │
└──────┬──────┘
       │ merchant submits host
       ▼
┌──────────────────────┐
│ PendingVerification  │
│ (token generated,    │
│  awaiting DNS proof) │
└─────────┬────────────┘
          │ DNS proof confirmed
          ▼
┌──────────────┐
│  Verified    │
└──────┬───────┘
       │ (platform initiates SSL)
       ▼
┌──────────────┐    SSL fails/retry    ┌──────────────┐
│  SslPending  │◄──────────────────────┤  SslPending  │
└──────┬───────┘                       └──────────────┘
       │ cert provisioned              (retry/backoff)
       ▼                               │
┌──────────────┐                       │
│  SslActive   │                       │
└──────┬───────┘                       │
       │ tenant marks primary OR       │
       ▼  platform manages             │
┌──────────────┐                       │
│  Primary     │                       │
└──────┬───────┘                       │
       │                               │
       ├─── disable ──▶ [Disabled] ◄───┘
       │                  │
       │                  └─ reactivate ──▶ back to SslActive/Verified
       │
       ├─── archive ──▶ [Archived] ◄───┐
       │                  │            │
       │                  └─ reactivate ──▶ back to SslActive/Verified
       │
       └─── delete ──▶ [Deleted] ── (permanent after retention)
```

### State Transition Guard Rules

1. **Draft → PendingVerification**: Host format validated; global uniqueness checked; `VerificationToken` generated; `DomainRegistered` event fired.
2. **PendingVerification → Verified**: DNS challenge (TXT/CNAME/HTTP-file) matches `VerificationToken`; `VerifiedAtUtc` set; `DomainVerified` event fired.
3. **PendingVerification → PendingVerification**: Merchant retries verification; `DomainVerificationAttempt` event fired.
4. **Verified → SslPending**: SSL provisioning initiated; `SslProvisioningStarted` event fired.
5. **SslPending → SslActive**: Certificate bound; `SslActivated` event fired.
6. **SslPending → SslPending**: Provisioning retry (exponential backoff: 1m, 5m, 15m, 1h, 4h, 12h, 24h). `SslActivationFailed` event on each failed attempt.
7. **SslActive → SslActive**: Automated renewal at 30-day threshold; no state transition; `SslRenewed` event fired.
8. **SslActive → SslPending**: Renewal failure; enters retry loop; `SslRenewalFailed` event fired.
9. **SslActive → Verified**: Cert revoked/expired beyond grace (90 days); SSL disabled; `SslRevoked` event fired; domain remains verified.
10. **SslActive/Verified → Primary**: Tenant admin promotes (must be only one Primary per tenant; demotes previous Primary); `DomainPromotedToPrimary` event fired.
11. **Primary → SslActive/Verified**: Demoted when new Primary set; `DomainDowngradedFromPrimary` event fired.
12. **SslActive/Primary/Verified → Disabled**: Merchant or platform disables; `DomainDisabled` event fired.
13. **Disabled → SslActive/Verified**: Merchant reactivates; `DomainReactivated` event fired.
14. **SslActive/Primary/Verified → Archived**: Merchant archives (retains config, stops serving); `DomainArchived` event fired.
15. **Archived → SslActive/Verified**: Merchant reactivates; `DomainReactivated` event fired.
16. **Any active state → Deleted**: Merchant deletes; requires: not Primary, no active SSL, no redirect targets. Soft-delete with `DeletedAtUtc`. `DomainDeleted` event fired.

### Transition Authorization

| Transition | Authorized By | Evidence Pattern |
|---|---|---|
| Draft → PendingVerification | Merchant (POS Admin) | Admin API call with tenant-scoped JWT (Owner/Admin) |
| PendingVerification → Verified | System (Infrastructure) | Automated DNS check job; `TenantLineageAuthority` check |
| Verified → SslPending | System (Cert Manager) | Automated provisioning; no merchant action |
| SslPending → SslActive | System (Cert Manager) | Certificate binding complete |
| SslPending → SslPending | System (Cert Manager) | Retry job |
| SslActive → SslActive | System (Cert Manager) | Automated renewal |
| SslActive → SslPending | System (Cert Manager) | Renewal failure detected |
| Any → Primary | Merchant (POS Admin) | Admin API; tenant isolation enforced |
| Any → Disabled/Archived | Merchant (POS Admin) | Admin API; tenant isolation enforced |
| Any → Deleted | Merchant (POS Admin) | Admin API; cascade guard (no active SSL, no redirects) |
| Any → Deleted | System (GC) | After retention period expires |

---

## 5. DNS Constitution

DOMAIN-0 observed: no DNS verification exists (Phase 11, lines 561-569). There is no `DomainVerification` entity, no TXT/CNAME challenge, no HTTP-file challenge. Any tenant admin could set `StorefrontBaseUrl` to any domain (currently only via DB access — no admin UI).

### DNS Verification Methods

Three verification methods are supported, all server-authoritative:

1. **DNS TXT Record** — Platform provides a unique `VerificationToken` string (e.g., `shopixy-domain-verification=abc123def456`). Merchant adds a TXT record to their DNS zone: `_shopixy.mydomain.com TXT "abc123def456"`. Platform queries DNS via authoritative resolver and confirms match.

2. **DNS CNAME Record** — Platform provides a verification subdomain (e.g., `verify-abc123.shopixy.com`). Merchant adds a CNAME: `mydomain.com CNAME verify-abc123.shopixy.com`. Platform queries the CNAME and confirms it resolves to the expected target.

3. **HTTP File Challenge** — Platform provides a file at `GET /.well-known/shopixy-domain-verification/{token}`. Merchant configures their domain to serve this path (typically by pointing DNS to the platform's edge). Platform fetches the URL and confirms the token matches. This method is only available after initial DNS pointing (chicken-and-egg), so TXT/CNAME is preferred for first verification.

### Verification Rules

- **Global Uniqueness**: `NormalizedHostName` must be unique across ALL tenants. No two tenants can claim the same domain. Enforced at DB level via `UNIQUE` index.
- **Root vs Subdomain**: `mybrand.com` (root/zone) and `www.mybrand.com` (subdomain) are distinct records. Both can be verified independently.
- **Punycode Normalization**: International domains (e.g., `müller.de`) are normalized to punycode (`xn--mller-kva.de`) before storage and comparison.
- **Reverification**: After 90 days, the platform re-checks DNS proof. If the TXT/CNAME is removed, the domain is set to `Verified → Disabled` with reason `DNS_RECORD_REMOVED`. The merchant is notified and can re-verify.
- **Ownership Transfer**: When a domain is deleted by one tenant and created by another, the new tenant must re-verify DNS ownership. The `VerificationToken` is regenerated. The previous tenant's verification proof is destroyed.

### Failure Handling

- **DNS Check Timeout**: 10-second timeout per domain. Marked `VerificationFailed` with reason `DNS_TIMEOUT`.
- **DNS Check Failure**: TXT not found, CNAME mismatch, or HTTP file not served. Marked `VerificationFailed`. Merchant can retry with same or different token.
- **Rate Limiting**: Max 5 verification attempts per domain per hour. Exceeding returns `429 Too Many Requests`.
- **DNS Resolution Failure**: If the domain's DNS doesn't resolve at all (NXDOMAIN), verification is blocked until DNS resolves. Platform polls every 30s for first 5 minutes.
- **Wildcard Considerations**: For `*.mybrand.com`, the CNAME verification method is used instead of TXT (TXT at wildcard level is not universally honored).

---

## 6. SSL Constitution

DOMAIN-0 observed: **zero SSL/certificate management** in the solution — `LetEncrypt`, `SslCertificate`, `certificate`, `Cert` all returned 0 search matches (Phase 14.2, lines 730-737). HTTPS redirection exists via `UseHsts()` + `UseHttpsRedirection()` (Phase 14.3), but no certificate provisioning for custom domains.

### Certificate Ownership

The **platform (Infrastructure)** owns the entire certificate lifecycle. Merchants never operate certificate tooling directly. Two provisioning paths exist:

1. **Automated (Let's Encrypt)**: Platform uses HTTP-01 challenge (preferred) or DNS-01 challenge. The certificate is provisioned on the platform's edge/reverse proxy (Caddy/NGINX/Cloudflare) and bound to the domain. Platform manages renewal at the 30-day threshold before expiration.

2. **Custom Upload**: Merchant uploads a PEM/CRT + private key pair. Platform validates:
   - Certificate matches the domain (SAN/CN contains `NormalizedHostName`)
   - Certificate chain is complete and trusted
   - Private key matches the certificate
   Platform stores the cert reference (thumbprint/ARN) and binds it at the edge. Renewal is the merchant's responsibility — platform warns 45, 30, 15, 7, 3, and 1 days before expiration.

3. **System Default**: Platform-provided shared certificate for platform domains (e.g., `*.shopixy.com`). Used by Development/Temporary domain types automatically.

### SSL Renewal

- **Automated**: Platform renews 30 days before `NotAfterUtc`. On success, `SslRenewed` event fires. No state transition (SslActive → SslActive).
- **Failure**: If renewal fails, enters `SslPending` with retry backoff (1m, 5m, 15m, 1h, 4h, 12h, 24h). After 72 hours of failed retries, domain is downgraded to `Verified` state (SSL removed). `SslRevoked` event fires.
- **Custom Upload**: No automated renewal. Platform emits `SslExpiringSoon` warning events at 45/30/15/7/3/1 days. After expiration, domain downgrades to `Verified` (no SSL). Merchant must re-upload.

### Expiration & Grace Period

- **Standard grace period**: 90 days. If a certificate expires, SSL is disabled but the domain remains in `Verified` state for 90 days. During this window, the platform continues retry attempts.
- **Beyond grace**: If SSL cannot be restored after 90 days, the domain is set to `Disabled` with reason `SSL_EXPIRED`. The primary domain (if affected) is demoted, and the merchant must re-provision or select a new primary.

### Revocation

- **Platform-initiated**: If a certificate is compromised (e.g., key leak detected), platform revokes it via ACME and fires `SslRevoked`. Domain moves to `SslPending` for re-provisioning.
- **Merchant-initiated**: Merchant can request revocation via Admin UX. Platform revokes and re-provisions.
- **CA-initiated**: If Let's Encrypt or the CA revokes a certificate, platform detects expiration and enters recovery flow.

### Retry Policy

SSL provisioning and renewal retries follow this schedule:
1. Immediate (0s)
2. 1 minute
3. 5 minutes
4. 15 minutes
5. 1 hour
6. 4 hours
7. 12 hours
8. 24 hours (final attempt before grace)

After 8 failed attempts (24 hours total), the domain transitions from `SslPending` → `Verified` (SSL removed). For renewals on an `SslActive` domain, the same schedule applies but the domain degrades to `Verified` instead of `Deleted` — the domain remains usable for DNS-only routing (no SSL) but is flagged as insecure.

### Platform vs Merchant Responsibility

| Responsibility | Owner | Evidence from DOMAIN-0 |
|---|---|---|
| Certificate provisioning (Let's Encrypt) | Platform | Absent (Phase 14.2) |
| Certificate upload, validation, storage | Platform | Absent (Phase 14.2) |
| Certificate renewal | Platform (automated) | Absent (Phase 14.2) |
| Private key management | Platform | Absent (Phase 14.2) |
| Certificate binding at edge (Caddy/TLS) | Platform | Caddyfile has no TLS (Phase 14.1) |
| Domain pointing (DNS A/CNAME → platform) | Merchant | No mechanism exists (Phase 6) |
| Renewing custom-uploaded certs | Merchant | N/A — no upload mechanism exists yet |

---

## 7. Request Pipeline

This is the **canonical runtime pipeline** — the constitutional order of middleware through which every incoming HTTP request traverses. It is derived from the DOMAIN-0 observed pipeline (Phase 7, lines 373-389) and extended for multi-domain support.

### Canonical Pipeline (Constitutional)

```
Incoming Request
    ↓
Reverse Proxy (Caddy/NGINX/Cloudflare)
    │ — Adds: X-Forwarded-Host, X-Forwarded-Proto, X-Forwarded-For
    │ — Terminates TLS for the domain's certificate
    ↓
ForwardedHeaders Middleware
    │ — Reads X-Forwarded-Host → populates Request.Host with TRUE customer host
    │ — Reads X-Forwarded-Proto → populates Request.Scheme
    │ — Reads X-Forwarded-For → populates RemoteIpAddress
    │ — DOMAIN-0 GAP: X-Forwarded-Host NOT currently configured (Phase 3.2, Program.cs:102)
    ↓
Security Headers Middleware
    │ — HSTS, X-Content-Type-Options, X-Frame-Options, Referrer-Policy
    ↓
CORS Middleware
    │ — Dynamic origin check: is Request.Host a verified Domain for this tenant?
    │ — DOMAIN-0 GAP: Currently hardcoded CORS origins (Phase 7.3, appsettings.json:235-246)
    ↓
CorrelationId Middleware
    ↓
Static Files
    ↓
Routing
    ↓
Authentication
    ↓
Theme Preview Authority Middleware
    │ — Validates themePreviewToken JWT for preview access
    │ — Establishes tenant context for preview requests (read-only paths only)
    │ — DOMAIN-0: Currently uses regex `preview.{slug}.{domain}` (Phase 5.3)
    ↓
HOST RESOLUTION MIDDLEWARE (NEW — constitutional)
    │ — Reads Request.Host (now correct due to X-Forwarded-Host)
    │ — Looks up Host in Domain registry (normalized, unique)
    │ — Sets ResolvedDomain + ResolvedDomainId in HttpContext.Items
    │ — Does NOT resolve TenantId yet — only Domain → DomainId mapping
    │ — Bypassed for System domains and excluded paths
    ↓
Tenant Resolution Middleware
    │ — Uses ResolvedDomain (if set) → Domain.TenantId
    │ — FALLBACK: Authenticated JWT claim → tenant_id claim → validate Active tenant
    │ — FALLBACK: Slug extraction from Host pattern (Storefront:UrlTemplate)
    │ — Strips inbound X-Tenant-Id (Phase 11.1, line 82)
    │ — BLOCKS: header spoofing, cross-tenant mismatch (Phase 11.1, lines 106-154)
    │ — Fail-closed: 400 TENANT_REQUIRED if no resolution (Phase 11.1, lines 156-177)
    │ — Re-adds X-Tenant-Id only after secure resolution (line 180)
    │ — DOMAIN-0: Currently matches against Tenant.StorefrontBaseUrl string (Phase 2.4)
    ↓
Request Context Logging Middleware
    ↓
Global Exception Middleware
    │
    ↓
Storefront Runtime Context Middleware
    │ — Populates TenantContext from resolved tenant
    │ — Sets up ITenantContext.RequiredTenantId (Phase 11.4, tenant-identity-runtime map:108)
    ↓
Authorization
    ↓
SuperAdmin Platform Authority Middleware (if SuperAdmin route)
    │ — SQL-revalidates SuperAdmin token (Phase 11.4)
    ↓
Admin Tenant Authority Middleware
    │ — Validates tenant ownership for admin writes
    ↓
Storefront Publication Guard Middleware
    │ — Enforces StoreConfig.StorefrontStatus gate (live/maintenance/disabled)
    ↓
Affiliate Referral Tracking Middleware (AF-8)
    │ — Intercepts ?ref=CODE on storefront routes
    ↓
Rate Limit Subject Resolution
    ↓
Rate Limiting
    ↓
Endpoint Routing / MVC Controllers
    │ — API controllers (StoreConfig, Products, Orders, etc.)
    │ — Builder controller (GET /api/builder/config)
    │ — Storefront controller (GET /api/storefront/store-config)
    │ — Preview controller (GET /api/storefront/theme-preview/config)
    │ — SEO controller (canonical, sitemap, robots)
    │ — Redirect controller (301 for alias/redirect domains)
    ↓
SPA Fallback
    │ — StorefrontHomeController serves index.html for unmatched storefront routes
    │ — DOMAIN-0: No domain-specific routing (Phase 7.2)
    ↓
Response
```

### Host Resolution Middleware — Detailed Design

**New middleware, inserted between Authentication and TenantResolutionMiddleware.**

**Responsibilities:**
1. Read `Request.Host` (now correct because X-Forwarded-Host is forwarded — DOMAIN-0 Phase 3.2 gap addressed).
2. Normalize: lowercase, punycode, strip port (unless non-standard port for dev).
3. Query the `Domains` table by `NormalizedHostName`.
4. If found: set `HttpContext.Items["ResolvedDomain"]` with the full `Domain` entity (including `TenantId`, `DomainType`, `LifecycleState`, SSL status).
5. If NOT found: do NOT fail here — let `TenantResolutionMiddleware` attempt JWT-based or slug-based resolution as fallback. Non-custom-domain hosts (e.g., `app.shopixy.com`, `localhost:5174`) resolve through slug pattern.
6. Excluded paths (same as TenantResolutionMiddleware exclusions at line 47-66): `/swagger`, `/hangfire`, `/health`, `/api/super-admin`, `/api/tenants`, `/api/platform/*`, `/hubs/chat`, `/uploads`, `/api/affiliate/*`, `/api/test-auth`.
7. Log `HostSpoofingAttempt` if the host matches a Domain record but the authenticated tenant (from JWT) doesn't match `Domain.TenantId` — this is a cross-tenant access attempt via host manipulation.

**Key design decisions:**
- The Host Resolution Middleware does NOT set tenant context. It only resolves Host → Domain. Tenant resolution remains the responsibility of `TenantResolutionMiddleware` (DOMAIN-0 Phase 2).
- If the Domain is found but `LifecycleState` is not in {`Verified`, `SslActive`, `Primary`}, the request is rejected with 404 or 410 depending on state (Disabled/Archived/Deleted).
- If the Domain is found but SSL is not active (`SslPending`/`Verified` without cert), the request is still resolved (tenant-level) but the frontend receives no SSL-active domain flag — the reverse proxy returns its own error page for HTTPS mismatch.

### Pipeline Ordering Constraint (Constitutional Law)

The Host Resolution Middleware MUST run:
- **AFTER** `UseForwardedHeaders()` (to get the true Host)
- **AFTER** Authentication (to have JWT claims available for cross-check)
- **BEFORE** `TenantResolutionMiddleware` (to provide Domain → TenantId mapping)
- **AFTER** Theme Preview Authority Middleware (preview tokens must not be intercepted by host resolution)

This ordering is immutable. Any future middleware that needs to read the Host header MUST read it AFTER `ForwardedHeaders` has processed it.

---

## 8. StoreConfig Relationship

DOMAIN-0 is unambiguous: StoreConfig has **no domain fields** — no `Domains` collection, no `PrimaryDomain`, no `CustomDomain`, no SSL/verification state (Phase 4, lines 193-223). The `StoreConfigDto` returned to the frontend (`StorefrontStoreConfigDtos.cs:7-73`) contains `storeName`, `storeDescription`, `storefrontTheme`, `themeId`, `theme`, `behavior`, `storefrontStatus`, `maintenanceMessage`, `pages`, `productTemplates`, `currency`, `defaultCountryCode`, and Commerce AI flags — but **no `domains` field** (Phase 4.2, lines 49-52).

### Constitutional Rule

> StoreConfig MUST never become the Domain owner. Domains resolve the tenant before StoreConfig is loaded. StoreConfig consumes domain information but never owns it.

### How StoreConfig Relates to Domains (Canonical)

1. **StoreConfig does NOT store domains.** Domain resolution happens in middleware before StoreConfig is fetched. StoreConfig remains a presentation/behavior configuration entity.

2. **StoreConfig is loaded AFTER tenant resolution.** The current flow (DOMAIN-0 Phase 1.1): `bootstrap.ts` → `resolveTenant()` (API call to backend) → `fetchConfig(tenantId)` → `GET /api/store-config/{tenantId}` → `StoreConfigDto`. The tenant is already resolved (via Domain → TenantId) before StoreConfig is fetched.

3. **StoreConfig can READ domain information** for non-authoritative purposes (e.g., the Admin UX can display the list of domains as part of the storefront settings page). This read is mediated by a separate Domain Service, not embedded in StoreConfig.

4. **StoreConfig does NOT drive domain resolution.** The `StorefrontUrlResolver` (`StorefrontUrlResolver.cs:53-78`) currently resolves from `Tenant.StorefrontBaseUrl` (line 58) or `Storefront:UrlTemplate` config (line 64). In the canonical architecture, this resolver MUST be updated to resolve from the **Domain registry** — specifically, the tenant's Primary Domain's `NormalizedHostName`. The resolver must NEVER fall back to `Tenant.StorefrontBaseUrl` once all tenants have migrated to the Domain registry.

5. **SEO canonical URLs** are generated by `SeoService` using `StorefrontUrlResolver.ResolveByTenantIdAsync(tenantId)` (line 105). This method queries `Tenants` table (line 89-91). In the canonical architecture, this resolution MUST use the tenant's Primary Domain instead. The `baseUrl` parameter passed to `BuildCanonicalUrl` (line 926-934) must be the Primary Domain's scheme+host.

6. **Runtime cache** is currently keyed by `tenantId` only (`storeConfigRuntime.ts:27`: `getRuntimeCacheIdentity('store-config', { includeThemePreview: true })`, `runtimeCachePolicy.ts:32`): cache key is `tenant=${tenantId}`). In the canonical architecture, the cache identity MUST include the resolved domain's `NormalizedHostName` to support multi-domain cache isolation. See Section 11 for Preview cache considerations.

### Backward Compatibility with StorefrontBaseUrl

The `Tenant.StorefrontBaseUrl` string field (`Tenant.cs:100`) is the legacy single-domain mechanism. The migration waves (Section 18) will:
- **Wave 2 (Runtime)**: Migrate `StorefrontBaseUrl` to a `Domain` record with `DomainType.Primary`, `LifecycleState.Verified`, `SslActive` (if HTTPS). After migration, `StorefrontBaseUrl` becomes a read-only alias maintained for backward compatibility.
- **Wave 3+**: Deprecate `StorefrontBaseUrl` writes. New Admin UX manages domains via the Domain Service API.

### StoreConfig DTO Extension (Not a Domain Owner)

The `StoreConfigDto` MAY be extended to include a read-only `domains` summary for the Admin UX:
```
domains: [{
  host: string;
  type: 'primary' | 'alias' | 'redirect' | 'preview';
  lifecycle: 'verified' | 'ssl_active' | 'ssl_pending' | 'verification_pending' | 'disabled';
  isPrimary: boolean;
  redirectTarget?: string;
}]
```
This is a **read-through projection** — the Admin UX fetches domains from the Domain Service separately, and the StoreConfig DTO simply aggregates a lightweight reference for convenience. The Domain Service remains the write authority.

---

## 9. Theme Platform — Boundaries

DOMAIN-0 Phase 9 is definitive: "Theme runtime is **separated from domain concerns**. Themes are identified by `ThemeId` (string). No theme-level domain awareness. A theme renders identically regardless of which domain the storefront is accessed from." (Phase 9, lines 487-510).

### Constitutional Boundaries

**Theme NEVER owns Domain. Domain NEVER owns Theme.**

| Boundary | Rule | Evidence |
|---|---|---|
| Theme resolution | Theme is resolved from `StoreConfig.ThemeId` / `ThemeLivePresetKey` (Phase 9, line 274) | `StoreConfig.cs:40` — `ThemeId` field |
| Domain awareness | Theme must have zero domain-awareness. No conditional rendering based on host | `bootstrap.ts` — `resolveTheme(themeId)` → theme registry (Phase 5.1) |
| Theme assets | Loaded from CDN/static provider keyed by `ThemeId`, not by domain | Phase 9, line 494 |
| Draft/Live split | `ThemeDraftOverridesJson` vs `ThemeLiveOverridesJson` (Phase 9, lines 500-505) | `StoreConfig.cs` lines 98-103 |
| Preview vs Live | Theme preview uses `themePreviewToken` JWT, not domain — the `ThemePreviewAuthorityMiddleware` validates the token (Phase 5.3, line 297) | `ThemePreviewAuthorityMiddleware.cs:52-70` |

### Theme Runtime Flow (unchanged by domains)

```
1. Bootstrap resolves tenant (now via Domain registry)
2. Fetches StoreConfig (tenantId already resolved)
3. Resolves ThemeId from StoreConfig
4. Loads theme preset + overrides from CDN
5. Applies CSS variables via applyTheme()
6. Renders via SectionRenderer

No step is domain-aware. The theme renders identically on mybrand.com, www.mybrand.com, and shop.mybrand.com.
```

### Theme Platform — Domain Independence Verification

The following must hold TRUE at all times:
- `ThemeResolver` takes `themeId: string`, never `host: string`
- `applyTheme.ts` consumes `StoreConfig.theme`, never `Domain.host`
- `ThemeSectionResolver` keys off `section.type` + `themeId`, never `host`
- RTL is driven by `ThemePresets.IsRtl`, never by domain locale/ccTLD

DOMAIN-0 confirms: "This draft/live separation is orthogonal to custom domains — domains would need to be a separate layer." (Phase 9, line 511). This layer separation is constitutional.

---

## 10. Preview Platform

DOMAIN-0 Phase 5.3 and Phase 8 establish: previews use a `themePreviewToken` (JWT) for authorization. Preview URLs are auto-generated via `preview.{tenantId}.{platformDomain}` or `preview.{slug}.{platformDomain}` (Phase 5.3, line 292; Phase 8.1, line 465). The `ThemePreviewAuthorityMiddleware` (`ThemePreviewAuthorityMiddleware.cs:52-70`) validates the token and checks StoreConfig ownership via `TenantLineageAuthority`.

### Canonical Preview Model

In the canonical architecture, previews gain **three** access modes:

1. **Platform-Generated Preview Domain** (existing, DOMAIN-0 Phase 8):
   - URL: `https://preview.{tenantId}.shopixy.com/{token}`
   - Token: JWT issued by `IStorefrontThemePreviewTokenService`
   - Domain type: `Preview` with `LifecycleState.Verified` + `SslActive` (platform-managed cert on `*.shopixy.com`)
   - Isolation: token-bound to specific `tenantId` + `storeConfigId` + `themeId`

2. **Custom Preview Domain** (new — constitutional):
   - URL: `https://preview.mybrand.com/{token}` (merchant-controlled)
   - Domain type: `Preview`, `LifecycleState.Verified` + `SslActive` (merchant provides cert or platform provisions via Let's Encrypt)
   - Isolation: same token mechanics — the `themePreviewToken` is still required as a query parameter
   - The host is resolved through the Domain registry → Host Resolution Middleware → TenantResolutionMiddleware
   - The `ThemePreviewAuthorityMiddleware` validates the token AND checks that the hosting Domain is of `DomainType.Preview` and belongs to the same `TenantId` in the token

3. **Local Development Preview** (existing, DOMAIN-0 Phase 8):
   - URL: `http://localhost:5174?themePreviewToken={jwt}` or `http://{slug}.localhost:5174`
   - Domain type: `Development`
   - No SSL, no DNS verification

### Preview Isolation Rules (Constitutional)

1. **Token is the authority.** The `themePreviewToken` JWT contains `tenantId` and `storeConfigId` claims (`ThemePreviewAuthorityMiddleware.cs` line 113). The Host Resolution Middleware must NOT establish tenant context for preview requests — only the token can do that. The middleware checks: `Domain.DomainType == Preview` AND `Domain.TenantId == token.tenantId` — fail-closed if mismatch.

2. **No live data access.** Preview requests ONLY access read-only storefront endpoints (DOMAIN-0 Phase 5.3: `PreviewReadPrefixes` at `ThemePreviewAuthorityMiddleware.cs:19-35`). All write endpoints are excluded.

3. **Preview domain must be explicitly registered.** A custom preview domain (e.g., `preview.mybrand.com`) must be registered as a `DomainType.Preview` record, verified via DNS, and SSL-provisioned before it can serve previews. This prevents any merchant from using any host as a preview domain.

4. **Theme preview token cannot be reused for live traffic.** `ThemePreviewAuthorityMiddleware` only processes requests with a valid token — without a token, the request flows through the normal Host Resolution → Tenant Resolution → StoreConfig pipeline (live mode).

5. **Draft vs Live theme isolation.** `StoreConfigPreviewStoreConfigController` (or equivalent) reads `ThemeDraftOverridesJson` / draft StoreConfig data directly from SQL, gated by the preview token. The live storefront reads `ThemeLiveOverridesJson`. The `VersionsJson` snapshot mechanism (`StoreConfig.cs:109`) ensures draft versions are independently addressable.

### Preview Cache Considerations

DOMAIN-0 Phase 1.3 notes the runtime cache is keyed by `tenantId` only. For previews:
- The cache identity (`getRuntimeCacheIdentity`) must include `themePreview=signed` (which it does at `runtimeCachePolicy.ts:26-27` via `getThemePreviewIdentity()`), ensuring draft config doesn't leak into live cache.
- Custom preview domains add a host dimension but the preview token already isolates the cache. The host is resolved to a tenant, and the token scopes to a specific StoreConfig version.
- **Constitutional rule**: Preview cache key = `tenantId + storeConfig.version + themePreviewToken.revision`. Host MUST NOT be added to the preview cache key — the token revision already provides isolation.

---

## 11. Security Constitution

DOMAIN-0 Phase 11 established the existing security model: server-authoritative resolution (JWT claims + server-controlled Host header), `X-Tenant-Id` header stripping (line 82), tenant spoofing detection (lines 106-129, 131-154), fail-closed resolution (lines 156-177), and Origin/Referer never trusted (lines 259-267). This section extends that model for multi-domain resolution.

### Host Spoof Protection

**DOMAIN-0 Evidence**: `TenantResolutionMiddleware` explicitly documents: "Only trust server-controlled Host header, never client-controlled Origin/Referer" (line 261-262). `Request.Host` is read at lines 263-266. No `X-Forwarded-Host` is currently forwarded (Phase 3.2, Program.cs:102) — this is a CRITICAL GAP.

**Canonical Rule**: The Host Resolution Middleware reads `Request.Host` — which is ONLY correct if `X-Forwarded-Host` is forwarded by the reverse proxy. The Caddyfile (`deploy/staging/Caddyfile`, line 16) uses `header_up Host {host}` but does NOT set `X-Forwarded-Host`. The canonical architecture REQUIRES:

1. **Reverse proxy MUST forward `X-Forwarded-Host`**: Caddy `header_up X-Forwarded-Host {host}`, NGINX `proxy_set_header X-Forwarded-Host $host`, Cloudflare `Forwarded-Host` header (or CF-Visitor).
2. **Application MUST include `X-Forwarded-Host` in `ForwardedHeadersOptions`**: `Program.cs:102` must change from `ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto` to `ForwardedHeaders.All` (or `XForwardedFor | XForwardedProto | XForwardedHost`).
3. **No client-controlled header is trusted**: `X-Tenant-Id` is stripped (line 82); `Origin`, `Referer`, `X-Forwarded-Host` are NEVER trusted — only the server-validated `Request.Host` after forwarded header processing.
4. **Host spoofing detection**: If `Request.Host` matches a Domain record whose `TenantId` differs from the authenticated JWT tenant, the request is rejected with 403 and logged as `HOST_SPOOFING_ATTEMPT`.

### Tenant Isolation

**DOMAIN-0 Evidence**: `TenantLineageAuthority` enforces SQL-authoritative tenant isolation (Phase 11.4, lines 580-585). `ITenantContext.RequiredTenantId` is mandatory (`ITenantContext.cs:25`). `TenantSaveChangesInterceptor` blocks saving `ITenantOwned` with `Guid.Empty` or mismatched TenantId (`TenantSaveChangesInterceptor.cs:69-111`).

**Canonical Extension**: With multi-domain resolution, tenant isolation must hold at the Host Resolution Middleware layer:
- A Domain record's `TenantId` is the ONLY source of tenant identity for unauthenticated requests.
- The `TenantLineageAuthority` SQL validation pattern (`LoadCurrentStoreConfigProjectionAuthorityAsync`, `StorefrontConfigProjectionConsumer.cs:159`) must be reused by the Host Resolution Middleware — the resolved Domain's `TenantId` must be cross-checked against the authenticated JWT tenant (if present) using the same `IsActive && Status == Active && DeletedAtUtc == null` filter (`TenantResolutionMiddleware.cs:232-238`).
- Cross-tenant host confusion: If two tenants somehow claim overlapping DNS (e.g., CNAME flattening), the `NormalizedHostName` UNIQUE constraint ensures only one Domain record exists. The host resolves to exactly one tenant.

### Hijacking Prevention

**DOMAIN-0 Evidence**: No domain verification mechanism exists (Phase 11, lines 563-569). Any admin with DB access could set `StorefrontBaseUrl` to any domain.

**Canonical Mitigation**: The Domain Aggregate's verification lifecycle (Section 5) prevents hijacking:
- A Domain must pass DNS verification (TXT/CNAME/HTTP-file) before `LifecycleState` reaches `Verified`.
- A domain in `Draft` or `PendingVerification` cannot serve storefront traffic — `HostSpoofingAttempt` logs if a non-verified host receives storefront traffic.
- `LifecycleState` is checked in the Host Resolution Middleware: only `Verified`, `SslActive`, and `Primary` states are eligible to serve. `Disabled`/`Archived`/`Deleted` domains return 404 or 410.
- When a domain is deleted and re-registered by a different tenant, the `VerificationToken` is regenerated — the previous tenant's DNS proof is invalidated.

### Forwarded Headers Hardening

**DOMAIN-0 Evidence**: `ForwardedHeadersOptions` only includes `XForwardedFor | XForwardedProto` (Program.cs:102). `ForwardedHeaders.All` returns 0 results (Phase 3.2). Known proxies are cleared (`KnownNetworks.Clear()`, `KnownProxies.Clear()`, lines 105-106).

**Canonical Rule**:
1. Add `X-Forwarded-Host` to the forwarded headers configuration.
2. Populate `KnownProxies` / `KnownNetworks` with the reverse proxy IPs (Caddy, NGINX, Cloudflare CIDR ranges). This prevents untrusted clients from spoofing `X-Forwarded-Host` directly.
3. In production, `KnownProxies` MUST be populated from configuration. In development, localhost proxies are allowed (`IsDevelopment()` branch at Program.cs:111).

### Cross-Tenant Protection

**DOMAIN-0 Evidence**: `TenantResolutionMiddleware` blocks cross-tenant access: if `authenticatedTenant.Id != requestTenant.Id`, returns 403 (`TENANT_FORBIDDEN`, lines 131-154). Header spoofing detection: if client-supplied `X-Tenant-Id` doesn't match resolved tenant, returns 403 (lines 106-129).

**Canonical Extension**: The Host Resolution Middleware performs cross-tenant checks:
- If `Request.Host` resolves to a Domain whose `TenantId` differs from the authenticated JWT tenant -> 403 `HOST_TENANT_MISMATCH`.
- If `Request.Host` resolves to a Domain in `Disabled` or `Deleted` state -> 404 `DOMAIN_NOT_FOUND` (not 403 — prevent information leakage).
- If `Request.Host` does NOT resolve to any Domain -> fall through to `TenantResolutionMiddleware` for JWT/slug resolution (allows platform domains and development hosts).

### Open Redirect Prevention

**DOMAIN-0 Evidence**: No redirect mechanism exists yet. The `StorefrontUrlResolver` builds absolute URLs (`StorefrontUrlResolver.cs:926-934`), always from tenant-scoped base URLs.

**Canonical Rule**: All redirect URLs generated by the platform MUST be:
1. Absolute URLs constructed from a resolved Domain's `NormalizedHostName`.
2. Validated against the Domain registry — redirects to external hosts are rejected.
3. The redirect target (`RedirectTargetDomainId`) must be a Domain record within the same tenant. Cross-tenant redirects are forbidden.
4. User-supplied redirect parameters (e.g., `?redirect=`) MUST be validated against an allowlist derived from the tenant's verified domains. Arbitrary redirect URLs are rejected with 400 `OPEN_REDIRECT_BLOCKED`.

### Canonical Redirect Rules

1. **WWW vs non-WWW**: Exactly one form is Primary. If `mybrand.com` is Primary, `www.mybrand.com` is either an Alias (canonical link to primary, optional 301) or a Redirect (301 to primary). The choice is merchant-configured at the Domain level.
2. **Trailing slash**: Consistent — all canonical URLs use no trailing slash (enforced by `BuildCanonicalUrl` at `SeoService.cs:926-934`). The SPA frontend handles route matching without trailing slashes. Redirects from trailing-slash variants to non-trailing-slash are 301.
3. **HTTP to HTTPS**: All Primary + Alias domains MUST be SSL-Active. A 301 redirect from `http://` to `https://` is configured at the reverse proxy level (Caddy). `http://` is never served for verified domains.
4. **Dev/Preview domains**: Development domains (`localhost:*`, `dev-*.localhost`) are exempt from HTTPS enforcement — they serve over HTTP only.
5. **Redirect domain priority**: If a domain is `DomainType.Redirect` with `RedirectType.Permanent301`, it takes precedence over serving content — the redirect fires before StoreConfig resolution.

---

## 12. SEO Constitution

DOMAIN-0 Phase 12 established: `SeoService` generates canonical URLs using `StorefrontUrlResolver` (`SeoService.cs:96-127` for metadata, `SeoService.cs:592` for sitemap). The `BuildCanonicalUrl` method (`SeoService.cs:926-934`) constructs URLs from `baseUrl` which is resolved via `StorefrontUrlResolver.ResolveByTenantIdAsync(tenantId)` (line 105). Robots.txt is **static** — no dynamic `Sitemap:` URL (`RobotsController.cs`).

### Canonical URLs

**DOMAIN-0 Evidence**: `SeoService.GetSeoMetadataAsync` (line 96) calls `_storefrontUrlResolver.ResolveByTenantIdAsync(tenantId)` (line 105) to get `baseUrl`, then `BuildCanonicalUrl(baseUrl, type, slug)` (line 178). The `baseUrl` comes from `Tenant.StorefrontBaseUrl` or `Storefront:UrlTemplate` (DOMAIN-0 Phase 12.1, `StorefrontUrlResolver.cs:58-68`).

**Canonical Rule**: In the multi-domain architecture, canonical URLs MUST be resolved from the tenant's **Primary Domain**:
1. `SeoService` receives the `baseUrl` from the request pipeline (the resolved Primary Domain's `https://hostname`).
2. If the request came in on a non-primary domain (Alias/Redirect), the canonical URL still points to the Primary Domain.
3. The `<link rel="canonical">` tag in the rendered HTML MUST point to `https://{primary-domain}/{path}`.
4. `hreflang` alternate links (`references/storefront-builder-runtime-map.md:135` confirms hreflang is injected in frontend) MUST use the Primary Domain as the `href`.

### Sitemap

**DOMAIN-0 Evidence**: `SitemapController.GetSitemap` calls `_seoService.GetSitemapAsync(tenantId, baseUrl, ...)` (line 618). `SeoService.GetSitemapAsync` (line 585) recomputes `baseUrl = await _storefrontUrlResolver.ResolveByTenantIdAsync(tenantId)` (line 592) — overriding the controller-provided `baseUrl` parameter. Sitemaps are recomputed every read (no stale Redis), with 6h cache (`SeoService.cs:598-601`).

**Canonical Rule**: Sitemap base URL = Primary Domain. All `Loc` entries in the sitemap use the Primary Domain scheme+host. Sitemap is accessible at `https://{primary-domain}/sitemap.xml` AND at `https://{alias-domain}/sitemap.xml` (same content, canonicalized).

### Robots.txt

**DOMAIN-0 Evidence**: `RobotsController.GetRobotsTxt` (line 714) builds robots.txt using `baseUrl = await _storefrontUrlResolver.ResolveByTenantIdAsync(tenantId)` (line 721). Currently static directives, no `Sitemap:` directive (`RobotsController.cs`).

**Canonical Rule**: Robots.txt MUST include a dynamic `Sitemap:` directive pointing to the Primary Domain's sitemap URL. On alias/redirect domains, robots.txt MAY include `Disallow: /` if the domain is a pure redirect (no content served). On alias domains (content served), robots.txt is identical to the primary but canonical URLs in sitemap point to primary.

### OpenGraph

**DOMAIN-0 Evidence**: `SeoService` generates `OgImage` (`SeoMetadataDto`, line 181-187) and `OgType` (line 188). OG tags are rendered in the storefront HTML. No domain-awareness currently.

**Canonical Rule**: OpenGraph URLs (`og:url`) MUST use the Primary Domain. OG images and metadata are domain-agnostic but the `og:url` tag MUST canonicalize to the Primary Domain. This prevents duplicate content signals when aliases serve the same page.

### Structured Data

**DOMAIN-0 Evidence**: `SeoService` generates JSON-LD for breadcrumbs (`SeoService.cs:942-958`). Product/category SEO metadata includes `CanonicalUrl` but no JSON-LD Product schema observed in the audit.

**Canonical Rule**: Structured data (JSON-LD) MUST include the Primary Domain in all `@id` fields and URL properties. If a page is served on an alias domain, the structured data still references the Primary Domain for canonical identity.

### Duplicate Content Prevention

1. **Canonical tag**: `<link rel="canonical" href="https://{primary-domain}/{path}">` on ALL pages, regardless of which domain served the request.
2. **301 redirects**: Redirect domains issue 301 to Primary. Alias domains without explicit redirect serve content but canonicalize.
3. **Sitemap**: Only the Primary Domain's sitemap is authoritative. Search engines are directed to the primary sitemap.
4. **hreflang**: All `hreflang` alternate links point to the Primary Domain, never aliases.

### WWW Policy

**Canonical Rule**: WWW vs non-WWW is decided at the Domain level:
- The merchant selects which form (apex or www) is **Primary** (exactly one).
- The non-primary form is either an **Alias** (serves content, canonicalizes via `<link rel="canonical">`) or a **Redirect** (301 to primary).
- The platform does NOT auto-decide — the merchant configures this in the Admin UX (Section 14).
- Default recommendation: apex domain (`mybrand.com`) as Primary, `www.mybrand.com` as Redirect (301) — but this is configurable.

### Trailing Slash Policy

**DOMAIN-0 Evidence**: `BuildCanonicalUrl` produces URLs without trailing slashes (`SeoService.cs:928`: `baseUrl?.TrimEnd('/')` + path segments with no trailing slash).

**Canonical Rule**: All URLs are trailing-slash-less. Requests to `https://mybrand.com/product/slug/` receive a 301 redirect to `https://mybrand.com/product/slug`. The SPA frontend (`PointOfSale.Storefront`) handles routing without trailing slashes. This applies uniformly across all verified domains.

---

## 13. Admin Experience

DOMAIN-0 Phase 13 confirmed: there is **NO** "Custom Domains" section in POS Admin navigation. The `STOREFRONT_NAVIGATION` in `storefrontInformationArchitecture.js:1-119` contains only: Overview, Storefront Status, Homepage, Product Template, Pages, Header, Footer Links, Themes, Colors & Typography, StoreConfig Version History. `StorefrontSettings.jsx:33` uses a hardcoded `VITE_STOREFRONT_DOMAIN` env var (`'localhost:5174'`) for the "Open Storefront" button (line 178). The Admin API (`AdminStoreConfigController`) has no domain endpoints (Phase 13.4).

### Canonical Navigation Location

```
POS Admin (PointOfSale.Web)
    ↓
Storefront (Sidebar.jsx — existing root nav item)
    ↓
Settings (generic /settings link — Sidebar.jsx:331-337)
    ↓
Domains (NEW navigation node)
```

**Navigation insertion point**: `storefrontInformationArchitecture.js` — a new `domains` screen ID is added under the `storefront` navigation root, between `status` and `homepage`:

```javascript
{
    screenIds: ['domains'],
    path: '/storefront/domains',
    labelKey: 'storefront.ia.domains',
    fallbackLabel: 'Domains',
    icon: 'Globe', // or Shield
}
```

### Domains Page — Sub-Navigation

The Domains page has its own tabbed sub-navigation:

```
/storefront/domains
    ├── Overview          (default)  — summary cards, SSL status, primary domain
    ├── Wizard            — add domain step-by-step
    ├── Domain List       — table of all domains with filters
    ├── Verification      — DNS challenge instructions, retry, re-verify
    ├── SSL               — cert status, renewal, upload, revocation
    ├── Redirects         — 301/307 redirect management
    ├── History           — audit log of all domain actions
    └── Activity          — recent events, background job status
```

### Page: Overview

- **Primary Domain card**: Displays the current Primary Domain, SSL status, verification date. Button: "Change Primary".
- **Domain count**: Total, Verified, SSL Active, Verification Pending, SSL Pending.
- **Quick Actions**: "Add Domain", "Verify Now", "Force SSL Renewal".
- **Warning banner**: If no Primary Domain is set, shows "No primary domain configured — storefront served via slug URL only."
- **StorefrontBaseUrl migration notice**: If `Tenant.StorefrontBaseUrl` is set (legacy), shows a migration banner: "Legacy single domain detected. Click to migrate to Domain registry."

### Page: Wizard (Add Domain)

Step 1: Enter domain (hostname input with validation).
Step 2: Select domain type (Primary, Alias, Redirect, Preview).
Step 3: Choose verification method (TXT, CNAME, HTTP-file).
Step 4: Display challenge record (TXT value or CNAME target).
Step 5: Auto-poll for DNS verification (every 10s for 5 minutes).
Step 6: On success → initiate SSL provisioning (if HTTPS selected).
Step 7: Summary with next steps.

### Page: Domain List

| Domain | Type | Status | SSL | Actions |
|---|---|---|---|---|
| mybrand.com | Primary | Verified | Active (32d) | Set Primary, Disable, Delete |
| www.mybrand.com | Alias | Verified | Active (32d) | Convert to Redirect, Disable |
| shop.mybrand.com | Redirect | Verified | Active (32d) | Edit Target, Disable |
| oldbrand.com | Redirect | Verified | Active (12d) | Edit Target, Disable |

Filters: All Types, Primary Only, Aliases, Redirects, Preview. Status filters: All, Verified+SSL, Pending, Disabled.

### Page: Verification

For the selected domain:
- Current verification method and token.
- DNS challenge instructions (copy-pasteable TXT record or CNAME).
- Live DNS check status (polling).
- "Verify Now" / "Retry Verification" buttons.
- Verification history (attempts, success/failure timestamps).

### Page: SSL

For the selected domain:
- Certificate status, provider (Let's Encrypt / Custom Upload / System Default).
- Expiration date with countdown.
- "Renew Now" button (platform-initiated).
- "Upload Custom Certificate" for custom-upload path.
- "Revoke" button (with warning).
- Auto-renewal toggle (for Let's Encrypt domains).
- Renewal history.

### Page: Redirects

- List of redirect domains with target mapping.
- 301 vs 307 selection.
- "Add Redirect Domain" quick-add.
- Bulk edit for redirect targets.

### Page: History

- Full audit trail from `DomainAuditEntry` table.
- Columns: Timestamp, Action, Actor, Before/After snapshots (expandable).
- Date range filter, actor filter.

### Page: Activity

- Background job status: verification checks, SSL provisioning, renewal attempts.
- Real-time polling for in-progress operations.
- Error details for failed jobs (reason, retry count, next attempt).

### Admin API Endpoints (Constitutional)

The Admin Domain Service API (separate from `AdminStoreConfigController`:

| Method | Route | Purpose |
|---|---|---|
| POST | /api/admin/domains | Add domain (Draft → PendingVerification) |
| GET | /api/admin/domains | List all domains for the tenant |
| GET | /api/admin/domains/{id} | Get domain details |
| PUT | /api/admin/domains/{id}/verify | Trigger DNS verification |
| PUT | /api/admin/domains/{id}/primary | Promote to Primary (demotes current) |
| PUT /api/admin/domains/{id}/disable | Disable domain |
| PUT /api/admin/domains/{id}/archive | Archive domain |
| DELETE | /api/admin/domains/{id} | Delete domain |
| POST | /api/admin/domains/{id}/ssl/renew | Trigger SSL renewal |
| POST | /api/admin/domains/{id}/ssl/upload | Upload custom certificate |
| POST | /api/admin/domains/{id}/ssl/revoke | Revoke certificate |
| POST | /api/admin/domains/{id}/redirects | Configure redirect target |
| GET | /api/admin/domains/history | Domain audit history |

---

## 14. UI Workflows

DOMAIN-0 found no domain management UI exists at all (Phase 13). All workflows below are constitutional design for the Admin UX to be implemented in DOMAIN-5.

### Add Domain

1. Merchant clicks "Add Domain" in the Domains Overview.
2. Wizard Step 1: Enter hostname (e.g., `mybrand.com`). Frontend validates format (RFC 1035). Backend validates global uniqueness.
3. Wizard Step 2: Select domain type: Primary (default for first domain), Alias, Redirect, Preview.
4. Wizard Step 3: Select verification method: TXT (recommended), CNAME, HTTP-file.
5. Wizard Step 4: Platform displays the challenge record (TXT value or CNAME target). Merchant copies to DNS.
6. Wizard Step 5: Auto-poll for DNS propagation (every 10s for 5 minutes). On success, domain transitions to `Verified`.
7. Wizard Step 6: If domain type is Primary or Alias, auto-initiate SSL provisioning (Let's Encrypt). Display SSL status.
8. Wizard Step 7: Summary. New domain appears in Domain List.

**Domain-0 gap addressed**: `StorefrontSettings.jsx:33` currently hardcodes `VITE_STOREFRONT_DOMAIN`. The "Open Storefront" button is updated to use the tenant's Primary Domain from the Domain registry.

### Verify

1. From Domain List or Verification tab, click "Verify Now" on a `PendingVerification` domain.
2. Platform performs a DNS query (TXT/CNAME) or HTTP fetch (file challenge).
3. If success: `PendingVerification → Verified`. If domain is Primary/Alias, auto-start SSL.
4. If failure: error details shown. Merchant can retry or change verification method.

### Activate

1. "Activate" applies to a `Verified` domain (no SSL yet) or `SslPending` domain.
2. For `Verified → SslActive`: Platform completes SSL provisioning (if not already in progress).
3. For `SslPending → SslActive`: SSL provisioning completes.
4. Domain becomes eligible for storefront traffic.

### Replace Primary

1. Merchant clicks "Set as Primary" on a Verified + SSL-Active domain.
2. System checks: tenant already has a Primary? If so, demote it to `SslActive` (Alias behavior).
3. Promote selected domain to `Primary`.
4. Fire `DomainPromotedToPrimary` + `DomainDowngradedFromPrimary` events.
5. Canonical URLs, sitemap, and SEO now point to the new Primary Domain.
6. Previous Primary becomes an Alias (serves content, canonicalizes to new Primary).

### Redirect

1. Merchant selects a domain and clicks "Convert to Redirect."
2. Choose target: another domain in the same tenant (dropdown) or external URL (validated).
3. Choose redirect type: 301 (Permanent) or 307 (Temporary).
4. Domain type changes to `Redirect`. `RedirectType` and `RedirectTargetDomainId` set.
5. On next request to this domain, Host Resolution Middleware issues 301/307 before reaching StoreConfig.

### Remove

1. Merchant clicks "Delete" on a domain (not Primary, no active SSL, no redirect dependents).
2. Confirmation modal with consequences: domain will stop resolving, DNS records should be removed.
3. Domain transitions to `Deleted` (soft-delete). `DeletedAtUtc` set.
4. If within retention period (90 days), can be restored by re-registration + re-verification.
5. If Primary: system requires selecting a new Primary first.

### Renew (SSL)

1. From SSL tab, click "Renew Now" on a domain with expiring/expired certificate.
2. Platform triggers Let's Encrypt renewal.
3. Status: `SslActive → SslPending → SslActive` (on success) or stays `SslPending` (on failure, enters retry loop).
4. Merchant notified of success/failure via Activity tab.

### Retry

1. On a failed verification or SSL provisioning, click "Retry."
2. For DNS: re-perform the DNS check.
3. For SSL: re-initiate Let's Encrypt order.
4. Backoff applies: immediate, then 1m, 5m, 15m, etc. (Section 6 retry policy).

### Failure Recovery

1. **DNS verification failure**: Merchant can change verification method (TXT ↔ CNAME ↔ HTTP-file) and retry. The `VerificationToken` is preserved or regenerated based on method change.
2. **SSL provisioning failure**: After 8 retry attempts, domain downgrades to `Verified` state (no SSL). Merchant can: (a) fix DNS A-record pointing, (b) switch to custom-upload cert, (c) retry Let's Encrypt.
3. **Certificate expiration**: Domain downgrades to `Verified`. Merchant is alerted via email + Admin Activity tab. Must re-provision.
4. **Cross-tenant domain conflict**: If a domain is registered by another tenant, the system blocks registration. If DNS changes (CNAME repointing), the domain remains with the original tenant until they delete it and the new tenant re-registers.

---

## 15. Shopify Parity

DOMAIN-0 Phase 15 provides the Shopify reference: a `domains` table with `domain`, `is_primary`, `host`, `verified_at`, `ssl_certificate_status`, `dns_verification_status`, `redirect_to_primary` (Phase 15.1, lines 755-760). Shopify uses a dedicated Host Resolver before tenant resolution, dynamic Let's Encrypt SSL, DNS/HTTP verification, and primary domain for canonicals.

### Conceptual Parity Table

| Capability | Shopify | BigCommerce | Squarespace | Shopixy (DOMAIN-1 Canonical) | Intentional Difference |
|---|---|---|---|---|---|
| Domain registry entity | `domains` table | `Store` entity with domain fields | `Domain` table | `Domain` aggregate (Section 3) | Same concept, different schema |
| Multiple domains per tenant | Yes | Yes | Yes | Yes (Section 3) | Same |
| Primary domain selection | `is_primary` | Primary domain | Primary domain | `DomainType.Primary` + `LifecycleState.Primary` | Explicit state in lifecycle |
| DNS verification | TXT, CNAME, HTTP file | TXT, CNAME | CNAME, A record | TXT, CNAME, HTTP-file (Section 6) | Same methods |
| SSL provisioning | Automated Let's Encrypt | Automated Let's Encrypt | Automated | Let's Encrypt + Custom Upload + System Default (Section 6) | Custom upload is an explicit option |
| Redirect support | Domain redirects | Domain aliases | Forwarding | `DomainType.Redirect` with 301/307 (Section 4) | Explicit redirect types |
| Preview domains | No custom preview | No | No | Custom Preview domains (Section 10) | Shopixy extension — first-class preview domain type |
| Host resolution | Before tenant resolution | Integrated | Integrated | Host Resolution Middleware before TenantResolutionMiddleware (Section 7) | Same pattern, explicit middleware |
| Canonical URLs | Primary domain | Primary domain | Primary domain | Primary Domain (Section 12) | Same |
| Wildcard domains | Yes | Yes | Yes | Planned (future extensibility) | Future, not yet implemented |

### Intentional Differences from Shopify

1. **Preview domain as first-class type**: Shopify does not have custom preview domains. Shopixy's `DomainType.Preview` (Section 4) is an intentional architectural decision — it allows merchants to preview on their own domain (e.g., `preview.mybrand.com`) rather than only on platform-controlled subdomains.

2. **Explicit redirect types**: Shopify uses `redirect_to_primary` (boolean). Shopixy supports `RedirectType.Permanent301` and `Temporary307` — an explicit HTTP status code is part of the domain model, allowing temporary marketing redirects.

3. **Custom SSL upload**: Shopify only does automated Let's Encrypt. Shopixy's canonical model includes `Provider.CustomUpload` — enterprises with existing certificate management can upload their own certs. This is a platform differentiator for enterprise merchants.

4. **Domain lifecycle as explicit state machine**: Shopify's domain model is simpler (`is_primary`, `verified_at`, `ssl_certificate_status` columns). Shopixy's lifecycle (Section 5) has 9 states with 15 defined transitions — this provides operational clarity for support and automation. This is an architectural choice for a more complex multi-domain platform.

5. **System and Development domain types**: Shopify has no concept of System or Development domains in its registry. Shopixy explicitly models these to avoid collisions between platform infrastructure domains and merchant domains.

6. **Host resolution as separate middleware**: Shopify integrates host resolution into the request pipeline. Shopixy makes it a separate, explicit middleware layer (Section 7) that is distinct from tenant resolution — this enforces the constitutional principle that domain resolution precedes tenant resolution.

---

## 16. Architecture Laws

These are the constitutional laws that govern Custom Domains across the entire Shopixy platform. Every implementation, migration, controller, middleware, DTO, and frontend component MUST comply.

### Domain Laws

1. **One Domain Owner** — The Domain Aggregate (Infrastructure layer) is the sole owner of domain names, verification, SSL, and lifecycle. No other aggregate owns these concepts.
2. **One Primary Domain Per Tenant** — Exactly one domain per tenant may have `DomainType.Primary` + `LifecycleState.Primary`. Promotion of a new Primary automatically demotes the previous Primary. Enforced at the database level with a filtered unique index on `(TenantId)` where `DomainType = Primary AND LifecycleState = Primary`.
3. **One Runtime Resolution Order** — Domain resolution → Tenant resolution → StoreConfig resolution → Theme resolution → Storefront Runtime. This order is immutable (Section 7).
4. **Domain Never Owns Theme** — The `Domain` entity has NO `ThemeId` field. Theme is resolved from `StoreConfig` post-tenant-resolution.
5. **Theme Never Owns Domain** — Theme assets, presets, and overrides are loaded by `ThemeId`, never by domain. No conditional theme rendering based on host.
6. **Builder Never Resolves Domains** — The Builder operates on `TenantContext` (set by middleware) and `StoreConfig`. It never queries the Domain registry directly. Domain information in the builder UI is fetched from the Domain Service API.
7. **Storefront Runtime Never Verifies DNS** — DNS verification is performed by Infrastructure (Domain Service). The Storefront Runtime only consumes the resolved `TenantId`.
8. **StoreConfig Never Owns Domain** — `StoreConfig` has no domain fields. It never writes to the `Domains` table. It may read domain summaries via the Domain Service for display purposes.
9. **Tenant Never Resolves Hosts Directly** — The `Tenant` entity's `StorefrontBaseUrl` (legacy) is migrated to the Domain registry. The `Tenant` entity does not perform host matching; middleware does.
10. **Domain Normalization Is Server-Authoritative** — `NormalizedHostName` (lowercase, punycode) is computed and stored by the platform. Clients cannot set the normalized form.

### Pipeline Laws

11. **ForwardedHeaders Must Include X-Forwarded-Host** — `Program.cs:102` MUST include `X-ForwardedHost` in `ForwardedHeadersOptions`. Without this, `Request.Host` behind a reverse proxy is the internal proxy address, not the customer's domain.
12. **Host Resolution Before Tenant Resolution** — The Host Resolution Middleware MUST run after Authentication and ForwardedHeaders, but BEFORE TenantResolutionMiddleware.
13. **Only Server-Controlled Host Header Is Trusted** — `Request.Host` (after forwarded header processing) is the only host source. Origin/Referer/X-Forwarded-Host are NEVER trusted (DOMAIN-0 Phase 2, line 263).
14. **X-Tenant-Id Stripped Before Downstream** — `X-Tenant-Id` is stripped at TenantResolutionMiddleware line 82 and re-added only after secure resolution at line 180. No downstream code trusts inbound `X-Tenant-Id`.
15. **Fail-Closed on Ambiguous Resolution** — If multiple tenants match a host (should never happen with UNIQUE constraint), resolution returns null → 400 Bad Request (`TenantResolutionMiddleware.cs:321`).
16. **Tenant Isolation Is SQL-Authoritative** — `TenantLineageAuthority` (SQL JOIN on Tenants + StoreConfigs) validates ownership before projection. Mongo/Redis are derivative. `ITenantContext.RequiredTenantId` is mandatory.

### SSL Laws

17. **Platform Owns Certificate Lifecycle** — The platform provisions, renews, binds, and revokes certificates. Merchants never directly interact with certificate authorities.
18. **SSL Active Required for Serving** — A domain in `SslActive` or `Primary` state is eligible to serve storefront traffic. A domain in `SslPending` or `Verified` (no SSL) does NOT serve over HTTPS — the reverse proxy returns an error page.
19. **No SSL, No Serve** — Any domain with `SslPending` status (no active certificate) receives a redirect to `http://` is FORBIDDEN. The platform returns 503 or a platform error page.
20. **Automated Renewal Before Expiration** — SSL certificates are renewed 30 days before `NotAfterUtc`. Grace period is 90 days (Section 6).

### SEO Laws

21. **Canonical Always Points to Primary** — `<link rel="canonical">` on every page points to `https://{primary-domain}/{path}`, regardless of which domain served the request.
22. **Sitemap Base Is Primary** — All sitemap entries use the Primary Domain as base URL.
23. **Robots Includes Dynamic Sitemap** — `robots.txt` includes `Sitemap: https://{primary-domain}/sitemap.xml` dynamically per tenant.
24. **No Trailing Slashes** — Canonical URLs never include trailing slashes. Requests with trailing slashes 301-redirect to non-trailing-slash.
25. **WWW Policy Is Merchant-Configured** — The platform does not enforce www vs non-www. The merchant selects which is Primary.

### Multi-Tenancy Laws

26. **TenantId Is Necessary But Not Sufficient** — Domain resolution produces a `TenantId`, but the `TenantContext.RequiredTenantId` must also validate `IsActive && Status == Active && DeletedAtUtc == null`.
27. **Cross-Tenant Access Is Blocked** — If the authenticated JWT tenant ≠ the Domain-resolved tenant, return 403.
28. **Header Spoofing Is Blocked** — If inbound `X-Tenant-Id` ≠ resolved tenant, return 403.

---

## 17. Migration Waves

DOMAIN-0 Phase 17 defines the implementation path: Phase A (CRITICAL — no migration needed): X-Forwarded-Host, dynamic CORS, Admin API endpoints. Phase B (CRITICAL — requires migration): Domain entity, Host Resolution middleware, migrate `StorefrontBaseUrl`. Phase C (HIGH): Verification, SSL, canonical redirects. Phase D (MEDIUM): Frontend StoreConfigType, cache, preview, robots sitemap.

This blueprint re-scopes the implementation into domain-focused waves (DOMAIN-2 through DOMAIN-6). Each wave builds on the previous and has independent rollback capability.

### Wave DOMAIN-2: Runtime Foundation

**Goals:**
- Add `Domain` entity to `ApplicationDbContext` (new `DbSet<Domain>`)
- Create `Domains` table migration (no data migration yet)
- Add `X-Forwarded-Host` to `ForwardedHeadersOptions` (Program.cs:102)
- Implement Host Resolution Middleware (inserted between Authentication and TenantResolutionMiddleware)
- Update `TenantResolutionMiddleware` to consult the Domain registry instead of `StorefrontBaseUrl` string matching
- Update `StorefrontUrlResolver` to resolve Primary Domain from Domain registry

**Dependencies:**
- None (foundational)

**Risk:**
- HIGH: Incorrect `X-Forwarded-Host` configuration breaks ALL host-based resolution
- HIGH: If Domain registry lookup fails, fallthrough to existing `StorefrontBaseUrl` must preserve backward compatibility
- MEDIUM: Middleware ordering is critical — any deviation breaks tenant isolation

**Rollback:**
- Remove Host Resolution Middleware
- Revert `ForwardedHeadersOptions` to original (XForwardedFor | XForwardedProto)
- TenantResolutionMiddleware falls back to `StorefrontBaseUrl` matching
- Domain table migration can be rolled back (new table, no data)

**Success Criteria:**
- `Request.Host` correctly reflects customer domain behind reverse proxy
- `mybrand.com` resolves to correct TenantId via Domain registry
- Existing `StorefrontBaseUrl` tenants still resolve (fallback path)
- No regression in local development (localhost resolution)
- All existing tests pass (Phase 17 readiness: `TenantResolutionMiddleware` tests)

### Wave DOMAIN-3: Domain Verification

**Goals:**
- Implement Domain verification service (TXT/CNAME/HTTP-file challenges)
- Add `DomainVerification` entity and `VerificationToken` generation
- Implement DNS query infrastructure (authoritative resolver)
- Add Admin API: `POST /api/admin/domains`, `PUT /api/admin/domains/{id}/verify`
- Add verification cron job (poll DNS every 10s for 5 minutes after submission)
- Add `DomainVerificationFailed` event handling

**Dependencies:**
- DOMAIN-2 (Domain entity + middleware must exist)

**Risk:**
- HIGH: DNS query reliability varies by provider; caching DNS results can cause false negatives
- MEDIUM: TXT record spam (malicious TXT records) — must validate token format and prefix
- LOW: HTTP-file challenge requires serving a file at `/.well-known/` path before full DNS is set up

**Rollback:**
- Disable verification cron job
- Revert Admin API endpoints
- Domain registry remains but no domains reach `Verified` state

**Success Criteria:**
- Merchant can add a domain and verify via TXT/CNAME
- Platform correctly detects DNS TXT/CNAME records
- Verification success transitions domain to `Verified` state
- Verification failure provides actionable error messages
- No false positives (domain cannot be verified without actual DNS proof)

### Wave DOMAIN-4: SSL Certificate Management

**Goals:**
- Implement `SslCertificate` entity and certificate lifecycle
- Integrate Let's Encrypt ACME client (or platform equivalent)
- Add certificate binding at Caddy/NGINX edge
- Add Admin API: `POST /api/admin/domains/{id}/ssl/renew`, `/ssl/upload`, `/ssl/revoke`
- Implement automated renewal (30-day threshold)
- Implement SSL retry backoff (1m, 5m, 15m, 1h, 4h, 12h, 24h)
- Update reverse proxy config for custom domain TLS

**Dependencies:**
- DOMAIN-3 (domains must be Verified before SSL provisioning)

**Risk:**
- CRITICAL: Let's Encrypt rate limits (50 certs per domain per week, 5 duplicate per week)
- CRITICAL: Certificate binding failure at edge breaks HTTPS for the domain
- HIGH: ACME challenge routing — HTTP-01 requires the domain to resolve to the platform edge
- HIGH: Private key exposure — must use secure storage (env secrets, not DB)

**Rollback:**
- Revert to platform-managed shared cert (`*.shopixy.com`) — verified domains fall back to non-SSL (blocked at edge)
- Remove Let's Encrypt integration
- Manual cert upload still available

**Success Criteria:**
- Custom domain `mybrand.com` serves valid HTTPS
- Certificate auto-renews before expiration
- Let's Encrypt rate limits respected
- Custom cert upload/validation works
- SSL failure degrades gracefully (domain stays Verified, no HTTPS)

### Wave DOMAIN-5: Admin UX

**Goals:**
- Add Domains navigation node to `storefrontInformationArchitecture.js` (Section 13)
- Implement Domains page with sub-navigation (Overview, Wizard, Domain List, Verification, SSL, Redirects, History, Activity)
- Implement all UI workflows (Section 14): Add, Verify, Activate, Replace Primary, Redirect, Remove, Renew, Retry, Failure Recovery
- Update `StorefrontSettings.jsx` to use Primary Domain instead of hardcoded `VITE_STOREFRONT_DOMAIN`
- Implement Admin API endpoints (Section 13)
- Update CORS to dynamic origin resolution

**Dependencies:**
- DOMAIN-2 (Host Resolution Middleware for serving), DOMAIN-3 (Verification UI), DOMAIN-4 (SSL UI)

**Risk:**
- MEDIUM: CORS misconfiguration blocks storefront SPA from calling API
- MEDIUM: Admin UX must handle all lifecycle states gracefully
- LOW: i18n — domain management must support RTL (Arabic) locale

**Rollback:**
- Feature flag `DomainsAdminUX` (default OFF) — revert to existing StoreConfig-only settings
- CORS reverts to hardcoded origins

**Success Criteria:**
- Merchant can add, verify, and manage domains from POS Admin
- "Open Storefront" button uses Primary Domain
- CORS allows custom domain storefronts to call API
- All workflow states (Draft → Deleted) are handled in UI
- Arabic (RTL) locale renders correctly

### Wave DOMAIN-6: Production Hardening

**Goals:**
- Implement domain-level monitoring (DNS health, SSL expiration alerts)
- Add domain analytics (traffic by host, redirect metrics)
- Implement edge geo-routing for future Regional/International domains
- Add domain-level cache invalidation (Redis key includes host dimension)
- Implement domain audit export (compliance)
- Add domain import/bulk management for enterprise merchants
- Final SEO verification: canonical tags, sitemap, robots.txt per domain

**Dependencies:**
- DOMAIN-5 (all features implemented and tested)

**Risk:**
- LOW: Monitoring false positives on DNS checks
- LOW: Cache invalidation complexity with host dimension
- LOW: Edge routing complexity for non-Goal types

**Rollback:**
- Disable domain monitoring jobs
- Revert cache key to tenantId-only (single active domain scenario)

**Success Criteria:**
- 99.9% domain resolution SLA
- SSL expiration alerts fire 45/30/7 days before expiry
- No cross-tenant data leakage via cache
- All SEO surfaces render correct canonical URLs
- Monitoring dashboards show domain health

---

## 18. Backward Compatibility

DOMAIN-0 Phase 19 guarantees backward compatibility for: StorefrontBaseUrl, Runtime, existing routes, Preview, Theme, Builder, and SEO. This section defines how each is preserved.

### Current StorefrontBaseUrl Migration

- `Tenant.StorefrontBaseUrl` (`Tenant.cs:100`) is preserved as a legacy field.
- During DOMAIN-2 migration wave, `StorefrontBaseUrl` is migrated to a `Domain` record (`DomainType.Primary`, `LifecycleState.Verified`, `SslActive` if HTTPS).
- `StorefrontBaseUrl` becomes a computed/read-only field: `get => _dbContext.Domains.Where(d => d.TenantId == this.Id && d.DomainType == Primary).Select(d => d.NormalizedHostName).FirstOrDefault()`.
- Existing tenants with `StorefrontBaseUrl` set continue to resolve until they explicitly manage domains via Admin UX.
- New tenants get a System Default domain (`{slug}.shopixy.com`, `DomainType.Development`/`System`) automatically during registration (`TenantRegistrationService.cs:190`).

### Current Runtime

- The existing `TenantResolutionMiddleware` (lines 78-185) flow is preserved as a fallback.
- Host Resolution Middleware is inserted BEFORE TenantResolutionMiddleware but does NOT replace it — TenantResolutionMiddleware still handles JWT-based and slug-based resolution.
- If Host Resolution Middleware resolves a Domain → passes `ResolvedDomainId` in `HttpContext.Items` → TenantResolutionMiddleware uses `Domain.TenantId` instead of querying `StorefrontBaseUrl`.
- If Host Resolution Middleware does NOT resolve (platform domain, localhost, etc.) → TenantResolutionMiddleware falls through to existing JWT/slug logic unchanged.

### Existing Routes

- All existing API routes (`/api/storefront/*`, `/api/builder/*`, `/api/admin/store-config`) remain unchanged.
- Domain resolution is transparent to controllers — they receive the same `TenantContext` as before.
- The only new route is the Domain Management API (`/api/admin/domains/*`), which is additive.
- SPA fallback (`StorefrontHomeController`) is unchanged — it serves `index.html` for unmatched routes.

### Preview

- Existing preview flow (`preview.{slug}.{domain}` with `themePreviewToken`) is preserved.
- Custom Preview Domains (Section 10.2) are ADDITIVE — existing platform preview URLs continue to work.
- `ThemePreviewAuthorityMiddleware` (lines 48-118) is unchanged for platform-generated preview domains.
- For custom preview domains, the middleware adds a check: if host resolves to a Domain of `DomainType.Preview`, verify the token's `tenantId` matches `Domain.TenantId`.

### Theme

- Theme runtime is completely unchanged (Section 10). No theme code changes required.
- Themes render identically regardless of domain. `ThemeId` resolution from StoreConfig is unchanged.

### Builder

- Builder controller (`GET /api/builder/config`) receives the same `TenantContext` as before.
- No domain-based branching is added to the builder (constitutional law #6).
- Builder preview URLs in `StorefrontUrlResolver` now resolve from Primary Domain, but builder still uses `TenantContext.TenantId` for its own operations.

### SEO

- `SeoService` canonical URL generation (`SeoService.cs:96-127`, `:585-654`) is extended: `baseUrl` now comes from the resolved Primary Domain instead of `Tenant.StorefrontBaseUrl`.
- Existing canonical behavior is preserved for tenants without migrated domains (fallback to `StorefrontBaseUrl`).
- Sitemap and robots.txt generation paths are unchanged — only the `baseUrl` source changes.
- `hreflang` injection in frontend (`references/storefront-builder-runtime-map.md:135`) is preserved — no frontend changes needed for basic parity.

### Guarantee

No existing behavior breaks. All changes are additive at the infrastructure layer. Tenants with `StorefrontBaseUrl` continue to work until they opt into the new Domain Management UX.

---

## 19. Future Extensibility

The canonical design must accommodate future domain topologies without architectural redesign. Each extension builds on the existing Domain Aggregate + Host Resolution Middleware + Lifecycle infrastructure.

### Wildcard Domains

- **Extension**: `DomainType.Wildcard` with `HostPattern` field (e.g., `*.mybrand.com`).
- **Mechanism**: A single DNS TXT/CNAME challenge at the apex validates ownership of the entire zone. The Host Resolution Middleware matches wildcard patterns via prefix-suffix comparison.
- **Use case**: Merchant wants `store1.mybrand.com`, `store2.mybrand.com`, `blog.mybrand.com` all routing to the same storefront.
- **No redesign needed**: The `NormalizedHostName` field becomes a `HostPattern` for wildcard domains. Host Resolution Middleware adds pattern matching logic.

### Regional Domains

- **Extension**: `DomainType.Regional` with `RegionCode` field (e.g., `us`, `eu`, `ap`).
- **Mechanism**: Edge routing (Cloudflare Workers, Caddy geolocation) inspects `Accept-Language` header and client IP, routes to the appropriate regional domain.
- **Use case**: `mybrand.com` (US), `mybrand.eu` (EU) — same storefront, different regional pricing/tax.
- **No redesign needed**: Host Resolution Middleware queries Domain by `NormalizedHostName`; edge routing adds a header (`X-Preferred-Domain`) that the middleware checks first.

### Language Domains

- **Extension**: `DomainType.Language` with `CultureCode` field (e.g., `en-US`, `ar-EG`).
- **Mechanism**: Host is resolved → Domain yields `CultureCode` → storefront i18n store initializes with that culture.
- **Use case**: `mybrand.com` (English), `mybrand.com.ar` (Arabic).
- **No redesign needed**: The Domain entity already has a `CultureCode` field in the canonical design. The storefront bootstrap (`bootstrap.ts`) reads `culture` from the resolved domain context.

### Marketplace Domains

- **Extension**: `DomainType.Marketplace` — domains for marketplace sellers within CommerceHub.
- **Mechanism**: Same as Primary/Alias but scoped to CommerceHub seller context. The Domain `TenantId` points to the marketplace tenant, with a `SellerId` discriminator.
- **Use case**: `seller123.myshop.com` resolving to seller's marketplace storefront.
- **No redesign needed**: The Domain aggregate's `TenantId` + optional `SellerId` field handles this. HostedResolution checks CommerceHub seller projection.

### CDN & Edge Routing

- **Extension**: Edge middleware (Caddy/NGINX/Cloudflare Workers) performs Host Resolution before the request reaches Kestrel.
- **Mechanism**: Domain registry is replicated to a KV store (Cloudflare Workers KV, Redis). Edge checks KV for host → TenantId mapping, sets `X-Tenant-Id` header, then forwards to origin.
- **Use case**: Global low-latency domain resolution, DDoS protection at edge.
- **No redesign needed**: The Domain entity is read-optimized. Edge sync reads the `Domains` table (read-only) and populates KV. Origin falls back to DB if KV miss.

### Geo Routing

- **Extension**: `X-Preferred-Domain` header injected by edge geo-routing.
- **Mechanism**: Edge detects client geography, routes to the closest regional domain. Host Resolution Middleware checks `X-Preferred-Domain` before the actual Host.
- **Use case**: EU visitor to `mybrand.com` is routed to `mybrand.eu` for lower latency.
- **No redesign needed**: Host Resolution Middleware already supports header-based override (the `X-Tenant-Id` header pattern at `TenantResolutionMiddleware.cs:81-82`).

### Vanity URLs

- **Extension**: Sub-path vanity resolution (e.g., `mybrand.com/summer-sale` → campaign-specific theme/config).
- **Mechanism**: StoreConfig-level path routing, not Domain-level. Resolves TenantId from domain, then StoreConfig from tenant + path context.
- **Use case**: Marketing campaigns with vanity URLs.
- **No redesign needed**: This is a StoreConfig/Storefront concern, not a Domain concern. Out of scope.

### Custom Email Domains

- **Extension**: `DomainType.Email` with `DnsRecordType` field (MX).
- **Mechanism**: Merchant verifies MX record points to Shopixy email servers. Same DNS challenge flow.
- **Use case**: `hello@mybrand.com` email sent through Shopixy infrastructure.
- **No redesign needed**: The Domain entity's `VerificationMethod` already supports DNS-based verification. Email domains add MX record validation.

### White Label

- **Extension**: Platform-provided domains (`{merchant}.{whitelabel-platform}.com`).
- **Mechanism**: System domain with `DomainType.WhiteLabel`. Platform manages SSL on the wildcard cert.
- **Use case**: Reseller white-label where the merchant doesn't have their own domain yet.
- **No redesign needed**: `DomainType.System` covers this (Section 4). The Domain registry handles platform-owned domains uniformly.

---

## 20. Governance

### Source Authority Hierarchy

| Layer | Authority | Description |
|---|---|---|
| 1 (Highest) | Implementation source code | `.cs`, `.ts`, `.jsx` files — the only ground truth |
| 2 | DOMAIN-0 Audit | Observation of current state — authoritative for what exists today |
| 3 | DOMAIN-1 Blueprint | Constitutional design — authoritative for what must be built |
| 4 | Prior `.md` docs | Secondary evidence only — never overrides source |

### Evidence Verification Protocol

Every architectural claim in this document that references existing code is traceable to a `file:line` citation from the DOMAIN-0 audit. Where this blueprint extends beyond observed reality, it is explicitly labeled as **Constitutional Design** (not observed in source).

**Observed claims** (from DOMAIN-0 evidence):
- `Tenant.StorefrontBaseUrl` exists as a string column (`Tenant.cs:100`, `Baseline_Recovery3.Designer.cs:19595`)
- `TenantResolutionMiddleware` reads `Request.Host` only (`Middleware/TenantResolutionMiddleware.cs:263-266`)
- `X-Forwarded-Host` is NOT in ForwardedHeaders (`Program.cs:102`)
- CORS origins are hardcoded (`appsettings.json:235-246`)
- No Domain/SslCertificate entities exist (Phase 6, lines 309-321)
- `StoreConfigDto` has no domains field (Phase 4.2)
- StoreConfig cache keyed by tenantId only (`storeConfigRuntime.ts:27`)
- Preview uses `preview.{slug}.{domain}` regex (`ThemePreviewAuthorityMiddleware.cs:52-70`)
- `SeoService` uses `StorefrontUrlResolver` for canonical URLs (`SeoService.cs:105`, `:592`)
- `StoreConfig.cs:11-15` — "Managed ONLY via POS Admin APIs. Storefront is read-only."

**Constitutional design claims** (beyond DOMAIN-0, architectural decisions):
- Domain Aggregate entity design (Section 3) — no Domain entity currently exists
- Host Resolution Middleware (Section 7) — new middleware, not present in current codebase
- Domain lifecycle state machine (Section 5) — design specification
- SSL Constitution (Section 6) — no SSL management currently exists
- Admin UX (Section 13) — no domain management UI currently exists
- Migration waves (Section 17) — implementation roadmap
- Shopify parity comparison (Section 15) — conceptual, not source-backed

### Certification

**VERDICT: PASS WITH OBSERVATIONS**

This blueprint passes certification for the following reasons:

1. **Grounding in DOMAIN-0**: Every section that references existing platform behavior cites DOMAIN-0 Phase + line evidence. All 21 gap items from DOMAIN-0 Phase 16 are addressed either by architectural extension or by explicit migration wave assignment.

2. **No contradictions**: The blueprint does not contradict any DOMAIN-0 finding. Where DOMAIN-0 says something is absent (Domain entity, SSL, DNS verification, Admin UX), the blueprint defines the constitutional target and assigns it to migration waves.

3. **Architectural soundness**: The core principle (Domains resolve tenant before Storefront Runtime) is consistent with the existing pipeline (`TenantResolutionMiddleware` already matches host to tenant — Phase 2). The Host Resolution Middleware is an extension of this existing pattern, not a replacement.

4. **Boundary enforcement**: The ownership matrix (Section 2) explicitly defines what Domains own vs. what they never own, mirroring the existing domain separation (`StoreConfig.cs:11-15`: "Storefront is read-only"; `bootstrap.ts` bootstrap flow: tenant → config → theme → render).

5. **Backward compatibility**: Section 18 guarantees no existing behavior breaks — `StorefrontBaseUrl` fallback, JWT/slug resolution fallback, theme independence, builder transparency, SEO preservation.

**Observations (risk areas requiring implementation attention):**

1. **Observation 1 — X-Forwarded-Host is a hard dependency.** Section 7 and Architecture Law #11 require adding `X-Forwarded-Host` to `ForwardedHeadersOptions`. Without this, the Host Resolution Middleware cannot read the customer's domain behind a reverse proxy. This is a zero-risk change in development (no proxy) but CRITICAL in staging/production. The Caddyfile (`deploy/staging/Caddyfile:16`) uses `header_up Host {host}` — this must be extended to `header_up X-Forwarded-Host {host}`. All production reverse proxy configs must be updated in DOMAIN-2.

2. **Observation 2 — Cache key dimension change is a performance risk.** Section 10 notes the runtime cache (`storeConfigRuntime.ts:27`, `runtimeCachePolicy.ts:32`) is keyed by `tenantId` only. Adding host dimension (per Architecture Law implied by Section 8) increases cache cardinality. For Preview domains, the constitutional rule explicitly states host MUST NOT be added to the preview cache key (token revision provides isolation). The implementation must carefully separate live vs preview cache key logic to avoid cache fragmentation.

3. **Observation 3 — `StorefrontUrlResolver` is the central chokepoint.** Sections 9, 12, and 14.1 all depend on `StorefrontUrlResolver` (`StorefrontUrlResolver.cs:53-78`). Every canonical URL, sitemap, robots.txt, and recovery link flows through this resolver. Migrating it from `Tenant.StorefrontBaseUrl` to the Primary Domain is a single point of failure — if the resolver fails, SEO breaks platform-wide. The resolver must have a robust fallback chain: Primary Domain → `StorefrontBaseUrl` (legacy) → `Slug` + `UrlTemplate`.

4. **Observation 4 — Preview domain security coupling is subtle.** Section 10.4 establishes that the `ThemePreviewAuthorityMiddleware` (`ThemePreviewAuthorityMiddleware.cs:52-70`) must validate that a custom preview domain's `TenantId` matches the token's `tenantId`. This introduces a new cross-validation between the Host Resolution Middleware and the Theme Preview Authority Middleware. The middleware ordering constraint (Architecture Law #12) must be strictly enforced: Theme Preview Authority runs BEFORE Host Resolution, because preview requests use tokens, not host-based tenant resolution.

5. **Observation 5 — Migration of `StorefrontBaseUrl` is irreversible data movement.** DOMAIN-0 Phase 17 (lines 836-858) defines Phase B: migrate `StorefrontBaseUrl` to Domains table. Once migrated, the `StorefrontBaseUrl` field becomes a computed read-only alias. If the Domain record is deleted, `StorefrontBaseUrl` returns null and the tenant falls back to slug-based URL. This is a one-way door for any tenant that has set `StorefrontBaseUrl` — the migration must be atomic and reversible only via Domain record restoration.

**PASS WITH OBSERVATIONS** — The blueprint is architecturally complete and does not contradict DOMAIN-0. The observations above are implementation risk items for DOMAIN-2 through DOMAIN-5 waves, not architectural flaws.

---












