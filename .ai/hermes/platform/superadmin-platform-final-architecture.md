# SuperAdmin Platform — Final Canonical Architecture

> Implementation-backed. Every claim is cited to `file:line` in the backend
> (`PointOfSale.API`, `PointOfSale.Core`, `PointOfSale.Infrastructure`). Frontends
> (`PointOfSale.Web`, `PointOfSale.*` node projects) are NOT runtime authority and are
> excluded. Markdown, comments, and prior certification docs were treated as secondary
> and re-verified against code.

---

## Executive Summary

The SuperAdmin Platform is the **platform governance runtime** of Shopixy. It is not an
admin dashboard. It is a bounded context that owns platform-level authority over tenant
lifecycle, user/account governance, marketplace visibility governance, platform
operations/recovery, platform audit, and platform-level read models (financial
dashboards, forensics).

The single most important architectural fact, verified in code: **SuperAdmin does not
own most domain data — it owns platform authority over it.** When SuperAdmin suspends a
tenant or suppresses a listing, it writes into the *domain's own SQL authority*
(`Tenants`, `CommerceHubListings`, `UserGovernanceProfiles`) inside the same
transaction as the governance decision, and then defers projection/recompute to the
domain's lifecycle owners (CommerceHub vendor-visibility, auto-healing queue, outbox).
This is the "necessary-not-sufficient tenant check + SQL authority" pattern applied at the
platform layer.

Key verified facts:
- SuperAdmin identity is **cryptographically isolated** from all other realms: separate
  `SuperAdmins` table, `SuperAdminJwt` scheme, dedicated secret/issuer/audience, and a
  `TokenVersion` revocation counter
  (`PointOfSale.Core/SuperAdmin/Models/SuperAdminUser.cs:9-17`,
  `PointOfSale.API/Program.cs:2190-2201`, `:2340-2386`, `:2756-2763`).
- Every SuperAdmin request is re-validated against current SQL state **after** JWT
  authorization, via `SuperAdminPlatformAuthority` (claims are evidence, SQL is
  authority) — fail-closed (`SuperAdminPlatformAuthority.cs:48-103`,
  `SuperAdminPlatformAuthorityMiddleware.cs:20-63`).
- Tenant lifecycle is governed through `Tenant.Status` (active/suspended/deleted) with
  `IsActive` retained only for backwards compatibility
  (`PointOfSale.Core/Entities/Tenant.cs:24-26,52`).
- Marketplace governance writes `CommerceHubListings.RestorePublicationOnActivate`
  (suppression) and re-derives visibility from CommerceHub SQL authority before allowing
  restore (`SuperAdminMarketplaceGovernanceController.cs:222-712`).
- User governance runs its mutations inside a **Serializable transaction** and fans out to
  CommerceHub vendor-visibility sync + cache invalidation
  (`UserGovernanceService.cs:551-623`).
- Platform operations (reindex, replay, repair) are **manually triggered SuperAdmin
  endpoints** that drive the shared CommerceHub reindex/search services and persist a
  recovery record (`SuperAdminMarketplaceOperationsController.cs:108-200`).

---

## Architecture Vision

A single, isolated, fail-closed platform runtime that can govern every tenant and every
account on the platform **without bypassing domain lifecycle owners**. SuperAdmin is the
"constitutional" layer: it can suspend, suppress, ban, and reindex, but it does so by
writing domain SQL authority and letting domains recompute their own projections. This
keeps a single source of truth (PostgreSQL) while allowing platform-wide control.

## SuperAdmin Platform Purpose

Govern the platform as a whole: tenant lifecycle, account/user governance (admin,
vendor, customer), marketplace listing visibility, platform financial oversight
(dashboards, payouts, commission, refunds, vendor finance), marketplace content
(CMS pages, footer, homepage sections, translations, reviews), platform operations and
recovery (reindex, replay, repair, forensics), and platform audit/operational logging.

## SuperAdmin Platform Scope

In scope (platform authority):
- Tenant creation/activation/suspension/deletion/recovery.
- SuperAdmin identity, authentication, authorization, session/refresh governance.
- User governance: suspend/ban/activate/financial-lock of Admin/Vendor/Customer
  accounts; blacklist (email/phone/ip/device); risk evaluation.
- Marketplace governance: listing suppression/restore; tenant suspend/restore;
  dashboard counts.
- Platform financial oversight: financial dashboard, vendor finance, payouts,
  commission rules, promo codes, refunds, returns, orders, reviews.
- Marketplace content governance: CMS pages, footer, homepage sections, translations.
- Platform operations/recovery: global/tenant reindex, search verification, auto-healing
  metrics, forensics.
- Platform audit: every SuperAdmin action is logged via `ISuperAdminAuditService`.
- Platform security middleware: request logging, exception mapping, operational audit,
  platform authority, rate limiting on governance writes.

Out of scope (owned by other bounded contexts; SuperAdmin only reads or writes their
SQL authority under governance):
- CommerceHub marketplace transaction lifecycle (order/payment/settlement): owned by
  CommerceHub lifecycle services; SuperAdmin reads and governs visibility/payouts only.
- Payments / Treasury / Accounting GL: owned by their lifecycle services; SuperAdmin
  reads financial read models.
- Catalog/inventory/pricing owned by Merchant Platform; SuperAdmin reads for governance
  visibility recomputation only.
- Affiliate transaction lifecycle: owned by Affiliate Platform.

## SuperAdmin Platform Owns

| Concept | SQL authority (table) | Mutating service | Verification |
|---|---|---|---|
| SuperAdmin identity | `SuperAdmins` | `SuperAdminAuthService` | `SuperAdminUser.cs:9-17` |
| Tenant lifecycle status | `Tenants` (`Status`,`IsActive`,`DeletedAtUtc`,`SuspendedReason`) | `SuperAdminMarketplaceGovernanceController.ApplyTenantGovernanceAsync` | `SuperAdminMarketplaceGovernanceController.cs:414-497` |
| User governance status | `UserGovernanceProfiles` (`Status`) | `UserGovernanceService.ApplyActionAsync` | `UserGovernanceService.cs:534-623` |
| Blacklist | `UserGovernanceBlacklistEntries` | `UserGovernanceService.UpsertBlacklistAsync/RevokeBlacklistAsync` | `SuperAdminUsersController.cs:288-348` |
| Marketplace listing suppression | `CommerceHubListings` (`RestorePublicationOnActivate`) | `SuperAdminMarketplaceGovernanceController.SuppressListing/RestoreListing` | `SuperAdminMarketplaceGovernanceController.cs:222-412` |
| Platform audit trail | `SuperAdminAuditLogs` (via `ISuperAdminAuditService`) | all SuperAdmin controllers | `SuperAdminAuthService.cs:151-164` |
| Recovery operations log | `SuperAdminRecoveryOperations` (persisted) | `SuperAdminMarketplaceOperationsController.PersistRecoveryOperationAsync` | `SuperAdminMarketplaceOperationsController.cs:129-138` |

## SuperAdmin Platform Does NOT Own

| Concept | Owner | How SuperAdmin touches it |
|---|---|---|
| CommerceHub order/payment lifecycle | CommerceHub lifecycle services (`IPaymentLifecycleService`, order mgmt) | Reads; **does NOT write payment intents / order rows** (0 refs to `IPaymentLifecycleService` in SuperAdmin) |
| CommerceHub **financial control** (commission, payouts, refunds, vendor earnings) | **SuperAdmin owns** — writes `CommerceHubFinancialTransactions`, `CommerceHubPayouts`, `CommerceHubVendorEarnings`, `CommissionRules`, `CommerceHubPayoutItems`, `CommerceHubRecoveryOperationLedgerEntries` via `ICommerceHubFinancialLedgerService` + `CommerceHubBoundaryMutationScope.BeginPayout()` in Serializable tx (`SuperAdminPayoutsService.cs:226,325`; `SuperAdminVendorFinanceService.cs`; `SuperAdminCommissionManagementService.cs`) | Direct DbContext writes; in-process finance mutation, **no outbox events** |
| Tenant-scoped settings | `SystemSettings` (tenant-scoped via `ITenantContext.RequiredTenantId`) | `SettingsController` is tenant-scoped, NOT SuperAdmin (`SettingsController.cs:79,161`) |
| Catalog/Product/Variant | Merchant Platform | Reads SQL authority to recompute listing visibility (`SuperAdminMarketplaceGovernanceController.cs:591-672`) |
| CommerceHub category/brand | CommerceHub (`CommerceHubCategories`/`CommerceHubBrands`) | Reads for governance listing/restore validation; does not mutate |
| Accounting GL / Journal | Accounting outbox/processor | **0 refs** to `AccountingOutbox`/`IJournalEntry` — SuperAdmin does NOT push journal/ledger out; reads financial read models only |
| Affiliate ledger/payout | Affiliate Platform | **0 references** to `Affiliate*` anywhere in SuperAdmin — no authority/linkage |

---

## Architecture Principles (all code-cited)

1. **Cryptographic realm isolation.** SuperAdmin JWT uses a dedicated secret/issuer/
   audience, fail-fast if unconfigured. `Program.cs:2190-2201` (fail-fast on missing
   secret), `:2340-2386` (scheme registration), `:2756-2763` (`RequireSuperAdmin`
   policy requires scheme + `SuperAdmin` role + `AuthType` claim).
2. **Claims are evidence, SQL is authority.** `SuperAdminPlatformAuthority` re-queries
   `SuperAdmins` and `UserGovernanceProfiles` after JWT authz; a stale `TokenVersion`
   or non-Active governance status rejects. `SuperAdminPlatformAuthority.cs:48-103`;
   `SuperAdminPlatformAuthorityMiddleware.cs:20-63`.
3. **Fail-closed session revocation.** `TokenVersion` increments on refresh-token set
   and on `ClearRefreshToken`; refresh checks version match to detect replay/theft and
   revokes all sessions. `SuperAdminUser.cs:45-58`;
   `SuperAdminAuthService.cs:269-306`.
4. **Governance mutations are transactional and Serializable.** User governance state
   change executes in `IsolationLevel.Serializable`. `UserGovernanceService.cs:551-557`.
5. **Write domain authority, defer projections.** Governance writes the domain's own SQL
   (e.g. `CommerceHubListings.RestorePublicationOnActivate`) and enqueues the domain's
   auto-healing/recompute rather than touching Mongo/ES directly.
   `SuperAdminMarketplaceGovernanceController.cs:247-274,355-389`;
   `UserGovernanceService.cs:620-623`.
6. **Governance restore is SQL-authority gated.** Restore of a suppressed listing is
   rejected if tenant/product/variant/category SQL authority is not live.
   `SuperAdminMarketplaceGovernanceController.cs:591-672`.
7. **Single root, protected system admin.** Exactly one `IsSystem` SuperAdmin; it
   cannot be suspended/banned/locked and cannot self-ban. `SuperAdminRootSeeder.cs:39-48`;
   `UserGovernanceService.cs:573-589`.
8. **Every platform action is audited.** `ISuperAdminAuditService.LogActionAsync` is
   called by every governance/ops/auth path. `SuperAdminAuthService.cs:151-164`;
   `SuperAdminMarketplaceGovernanceController.cs:95-104,277-284`.
9. **Rate-limited governance writes.** All governance mutation endpoints carry
   `EnableRateLimiting(GovernanceWriteRateLimitPolicyName)`.
   `SuperAdminMarketplaceGovernanceController.cs:223,301,415`;
   `SuperAdminUsersController.cs:163,173,183,193,202,213,225`.
10. **Recovery is manual-but-logged.** Reindex/replay/repair are SuperAdmin-triggered
    and persist a recovery operation record. `SuperAdminMarketplaceOperationsController.cs:108-200`.

---

## Core Aggregates

| Aggregate | Table | Key authority | Lifecycle owner |
|---|---|---|---|
| SuperAdminUser | `SuperAdmins` | `SuperAdminAuthService` | SuperAdmin |
| Tenant | `Tenants` | Governance controller (suspend/restore) + registration | Platform/Registration |
| UserGovernanceProfile | `UserGovernanceProfiles` | `UserGovernanceService` | Platform Governance |
| BlacklistEntry | `UserGovernanceBlacklistEntries` | `UserGovernanceService` | Platform Governance |
| CommerceHubListing (governance view) | `CommerceHubListings` | Governance controller (suppress/restore) | CommerceHub + SuperAdmin governance overlay |
| SystemSetting (platform-level) | `SystemSettings` (`TenantId` null) | platform config path | Platform Config |

---

## Platform Runtime

The SuperAdmin "platform runtime" is the set of hosted services, middleware, and
singletons that keep the platform layer live:

- **`SuperAdminRootSeeder` (IHostedService)** — bootstraps the single system Super
  Admin from env vars `SUPERADMIN_EMAIL`/`SUPERADMIN_PASSWORD`; refuses multiple system
  users; skips gracefully if PostgreSQL is unavailable. `SuperAdminRootSeeder.cs:10-96`;
  registered `Program.cs:926`.
- **Pipeline middleware (order verified in `Program.cs`):**
  `CorrelationIdMiddleware` (3220) → `SuperAdminRequestLoggingMiddleware` (3221) →
  `CookieCsrfProtectionMiddleware` (3263, conditional) →
  `TenantResolutionMiddleware` (3276, conditional; strips X-Tenant-Id) →
  `RequestContextLoggingMiddleware` (3279) → `GlobalExceptionMiddleware` (3280) →
  `SuperAdminExceptionHandlingMiddleware` (3281) →
  `StorefrontJwtBlacklistMiddleware` (3294, conditional) →
  `SuperAdminPlatformAuthorityMiddleware` (3297) →
  `SuperAdminOperationalAuditMiddleware` (3298) →
  `AdminTenantAuthorityMiddleware` (3301, conditional).
- **`ISuperAdminLoginRateLimiter` (singleton)** — Redis-backed login rate limiting.
  `Program.cs:923`; used `SuperAdminAuthService.cs:58-67`.
- **`ISuperAdminAuditService`** — platform audit writer. `Program.cs:909`.

## Tenant Runtime

Tenant lifecycle is expressed through `Tenant.Status` (`TenantStatus` enum) plus the
legacy `IsActive` flag kept for compatibility (`Tenant.cs:24-26,52`).

- **Creation:** bootstrapped by `TenantRegistrationService`
  (`PointOfSale.API/Services/Platform/TenantRegistrationService.cs`) — initial status
  `PendingSetup` (`Tenant.cs:52`); dual-writes `IsActive`+`Status`
  (`:207-209,469-470`). The registration endpoint is tenant-scoped and routes
  through normal tenant creation, not a SuperAdmin controller.
- **Activation:** governance `RestoreTenant` sets `Status=Active`,
  `IsActive=true`, clears `SuspendedReason`. `SuperAdminMarketplaceGovernanceController.cs:423-497`.
- **Deletion (schema-only, NOT wired):** `Tenant.DeletedAtUtc` exists in the
  schema and `PrivilegedRefreshTokenRevocationInterceptor` keys on `Status is
  Suspended or Deleted`, but a full search found **0 runtime code paths that set
  `DeletedAtUtc` or `Status=Deleted`** — the "Deleted"/recovery lifecycle is
  spec-defined but **unimplemented in code** (`Tenant.cs:72`;
  `TenantStatus` enum `PendingSetup/Active/Suspended/Deleted`). Governance queries
  use `IgnoreQueryFilters()` + `DeletedAtUtc != null` checks purely as a
  defensive read pattern, not because a delete path populates them.
- **Recovery:** governance restore re-activates; reindex/replay endpoints repair
  projections (`SuperAdminMarketplaceOperationsController.cs:108-200`).
- **Configuration:** `Country`, `Currency` (default `EGP`), `Slug`,
  `StorefrontBaseUrl`, `UseBaileys` feature gate, `BootstrapVersion`.
  `Tenant.cs:37-109`.
- **Isolation (defense-in-depth, verified):** enforced by
  `ITenantContext.RequiredTenantId` (throws if unset, `ITenantContext.cs:25`);
  `X-Tenant-Id` is **stripped** by `TenantResolutionMiddleware` (`:82`) and
  re-resolved ONLY from the authenticated `tenant_id` claim or the **server-controlled
  `Host` header** — `Origin`/`Referer` are explicitly distrusted (`:259-267`);
  header-spoofing and auth/request tenant mismatch are detected and rejected
  (`:106-154`). A second layer, `TenantSaveChangesInterceptor`
  (`Infrastructure/Data/TenantSaveChangesInterceptor.cs:69-111`), blocks saving any
  `ITenantOwned` entity with an empty or mismatched `TenantId`. `TenantService.
  IsTenantActiveAsync` requires `IsActive && Status==Active && DeletedAtUtc==null`.
  (Cross-tenant leak = CRITICAL per platform rules.)
- **Ownership:** a tenant is simultaneously the isolation boundary and the governed
  "vendor" (`UserGovernanceRole.Vendor` subject = tenant id).

## Identity Runtime

See "SuperAdmin Identity" and "Platform Security Runtime" below. Roles: a single
platform role `SuperAdmin` (`SuperAdminSecurityConstants.Role`);
governed subjects span `UserGovernanceRole` enum {Admin, Vendor, Customer}. No stored
permission system — enforcement is scheme + role claim + SQL governance gate + computed
capability strings (UI hints only).

## Platform Governance Runtime

Two cooperating services:

1. **`UserGovernanceService`** (`PointOfSale.API/Services/Governance/`) — the shared
   cross-realm governance engine. It evaluates status for Admin/Vendor/Customer, applies
   blacklist checks, computes risk (refund/dispute rates, failed payments, velocity,
   debt), and is the **single mutator** of `UserGovernanceProfiles.Status` and
   `UserGovernanceBlacklistEntries`. It depends on CommerceHub debt/visibility services
   to fan out marketplace effects. `UserGovernanceService.cs:1-623`.

2. **`SuperAdminMarketplaceGovernanceController`** — the SuperAdmin write surface for
   marketplace governance: listing suppress/restore (writes
   `CommerceHubListings.RestorePublicationOnActivate` and publishes
   `CommerceHubListingUnpublishedEvent`/`CommerceHubListingUpdatedEvent`), tenant
   suspend/restore (writes `Tenants`), and dashboard counts.
   `SuperAdminMarketplaceGovernanceController.cs:1-750`.

Governance status vocabulary (`UserGovernanceStatus`): `Active`, `Suspended`, `Banned`,
`FinanciallyLocked`. Thresholds: `HighRefundRateThreshold=40`,
`HighDisputeRateThreshold=25`, `MediumFailedPaymentsThreshold=3`,
`HighVelocityRefundThreshold=3` (`UserGovernanceService.cs:19-22`).

## Platform Security Runtime

- **Authentication:** `SuperAdminAuthService.LoginAsync` verifies password hash, then
  `IUserGovernanceService.EnsureAdminLoginAllowedAsync` (banned/suspended/financially
  locked → reject). `SuperAdminAuthService.cs:50-171,203-231`.
- **Authorization:** `RequireSuperAdmin` policy (scheme + role + auth-type claim),
  then `SuperAdminPlatformAuthority` revalidation. `Program.cs:2756-2763`;
  `SuperAdminPlatformAuthority.cs:48-103`.
- **Session management:** refresh tokens hashed (`RefreshTokenHash`), versioned
  (`TokenVersion`), replay-detected. `SuperAdminAuthService.cs:188-340`;
  `SuperAdminUser.cs:45-58`.
- **Break glass / emergency access:** the `IsSystem` root SuperAdmin is the
  protected emergency identity; cannot be suspended/banned/locked.
  `UserGovernanceService.cs:583-589`; `SuperAdminRootSeeder.cs`.
- **Audit:** `ISuperAdminAuditService` + `SuperAdminOperationalAuditMiddleware`.
- **Rate limiting:** login limiter + governance-write policy.
- **Secrets:** `SuperAdminJwt:Secret` must be explicitly configured or the app
  fail-fast refuses to start with a privilege-escalation warning. `Program.cs:2190-2201`.
- **Key rotation:** `ClearRefreshToken`/`SetRefreshToken` rotate by bumping
  `TokenVersion`, invalidating all prior tokens.

## Platform Configuration Runtime

Platform configuration lives in the generic `SystemSetting` key/value table
(`PointOfSale.Core/Entities/SystemSetting.cs`), which carries an **optional**
`TenantId`. Rows with `TenantId == null` are platform-level; rows with a tenant id are
tenant-scoped. Tenant-scoped settings are mutated through the tenant-scoped
`SettingsController` (requires `ITenantContext.RequiredTenantId`), **not** a SuperAdmin
controller. `SettingsController.cs:73-570`. No dedicated `PlatformConfiguration` entity
was found — configuration is the `SystemSetting` table plus environment variables (e.g.
root SuperAdmin bootstrap). Platform-level emergency stop is config-only via
`CommerceHubEmergencyStopOptions` (`CommerceIntakeStopped`/`PayoutsStopped`), not a DB
aggregate.

## Platform Policy Runtime

Platform policies are **enforced in code + configuration thresholds, not stored as
documents**:
- Blacklist policy (email/phone/ip/device) — `UserGovernanceService.EnsureRegistrationAllowedAsync`,
  `:89-129`.
- Login/status policy — `Ensure*LoginAllowedAsync` family (`:203-281`).
- Marketplace eligibility policy — `EnsureVendorListingAllowedAsync` etc., which call
  `CommerceHubVendorAuthority.LoadAsync` (`:283-394`).
- Governance write rate-limit policy — `GovernanceWriteRateLimitPolicyName`
  (`Program.cs:3044`).

There is **no stored "Policy" aggregate**; policies are code + config thresholds.
Authorization is **binary** — the only role is `SuperAdmin` — and there is **no
stored RBAC/permission system**. The only granular control surface is
`UserGovernanceProfile.Status` (Active/Suspended/Banned/FinanciallyLocked) and a
static, **server-unenforced** capability string list (`SuperAdminAuthController.
NavigationCapabilities`, e.g. `"super-admin.finance.view"`) returned to the SPA as
UI hints (`SuperAdminAuthController.cs:17-34,262,303`). Spec implies a permission
system; only role-gate + static capability list + governance status exist.

## Feature Flag Runtime

**No feature-flag *service*, no `IFeatureFlag`, no DB table, no tenant overrides,
no rollout/experiment engine exist.** Two distinct mechanisms, NEITHER SQL-backed and
NEITHER SuperAdmin-owned:

- **(a) Payment-provider feature flags** (the real runtime ones): per-provider
  `FeatureFlags` sub-objects bound from `appsettings.json` (e.g. Stripe `FeatureFlags`
  at `appsettings.json:108` — `Enabled/SandboxEnabled/ProductionEnabled/
  PaymentIntentsEnabled/...`; also ManualPayment `:124`, Fawry). Consumed in
  `FawryFoundationService.cs:35`, `ManualPaymentRuntimeApplicationService.cs:198-237`,
  `FawryTenantConfigurationResolver.cs:167-204`, `CodFoundationService.cs:34`.
  **Mutation authority = deployment environment config only** — no tenant/platform override.
- **(b) `PaymentRuntimeFeatureFlags`** (`PointOfSale.Core/Payments/Runtime/
  Configuration/PaymentRuntimeFeatureFlags.cs`): static class with const keys
  (`NewPaymentRuntime`, `NewPolicyEngine`, `DualRunPayments`, `ReplayEngine`,
  `ProviderAdapterV2`) + `Defaults` all `false`. **Only referenced in tests**
  (`PaymentRuntimeFoundationCertificationTests.cs:92-96`); no production resolver found —
  effectively dead/unwired config keys.

Conclusion: feature flags are environment/runtime *config* (appsettings), not
governance-managed and not a SuperAdmin runtime. (See Technical Debt #3.)

## Subscription Runtime

**No first-class `Subscription`/`Plan`/`TenantSubscription` entity exists** in
`PointOfSale.Core` (search returns only the generic `SystemSetting` KV table and
tenant fields such as `Tenant.BootstrapVersion`/`Tenant.Currency`). Subscription/plan
state, if modeled at all, is **implicit** — there is no dedicated SQL authority, no
subscription service, and no subscription events observed in SuperAdmin. This is a
verified gap: the platform currently enforces tenant standing through `Tenant.Status`
alone, not through a modeled subscription aggregate. (See Technical Debt #3.)

## Licensing Runtime

**No first-class `License`/`TenantLicense` entity exists** in `PointOfSale.Core`
(search for `License` returns only payment-option configs, not a tenant-license table).
Licensing, if present, is **not** a dedicated SQL aggregate; treat as a gap. Tenant
entitlement is expressed via `Tenant.Status` and configuration, not a license record.
(See Technical Debt #3.)

## Global Category Runtime

**No `GlobalCategory` entity exists** in this codebase (search returns 0 matches). The
marketplace taxonomy is `CommerceHubCategories`
(`PointOfSale.Core/Entities/CommerceHubCategory.cs`), owned by the CommerceHub
bounded context. SuperAdmin governance **reads** categories (e.g.
`CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync` to validate listing restore,
`SuperAdminMarketplaceGovernanceController.cs:516-518,664-671`) but does **not**
mutate them and does not own a global-category aggregate. The brief's assumption of a
SuperAdmin-owned global category table is **not backed by implementation** — this is a
documentation/architecture drift finding (see Technical Debt #3).

## Global Brand Runtime

Same as Global Category: **no `GlobalBrand` entity exists.** Brands are a
CommerceHub-owned concept (`CommerceHubBrands`); SuperAdmin reads them for governance
and does not own mutation.

## Global Search Runtime

Global/marketplace search authority is Elasticsearch (single authority,
`ICommerceHubSearchIndexService`), with MongoDB as the projection store
(`CommerceHubProductDocument`). SuperAdmin operations drive reindex and verify search
sync status (`SearchSyncStatus` on the Mongo doc: Pending/Failed/TerminalFailure).
`SuperAdminMarketplaceOperationsController.cs:51-106,108-157`. Search repair is
**manual**: SuperAdmin triggers `ICommerceHubReindexService.ReindexAsync`, which
rebuilds ES from Mongo (`CommerceHubReindexService.cs:81-101`), reconciles
Mongo→ES, catches up missing SQL listings (`ReconcileMissingSqlListingsAsync`,
`:93`), and promotes the alias on match (`:119-125`). The
`SearchReindexController` (tenant admin) blocks index delete/recreate (`:67,87`).

## Platform Analytics Runtime

SuperAdmin owns platform-level **read models** only, served from **eventually-consistent
daily SQL aggregates** (`CommerceHubDailyFinancialAggregates` via
`ICommerceHubDailyFinancialAggregateService`, `CommerceHubDailyFinancialAggregateService.cs:46,71`):
- `SuperAdminFinancialDashboardService` (+ `.Aggregates.cs`) — platform financial
  dashboards (overview, trends, promo, vendor perf, refund analysis, integrity check).
- `SuperAdminFinanceOverviewService` — delegates to
  `ICommerceHubFinancialDashboardService.GetPlatformOverviewAsync`
  (`SuperAdminFinanceOverviewService.cs:23,42`).
- `SuperAdminVendorFinanceService`, `SuperAdminPayoutsService`,
  `SuperAdminCommissionManagementService` — vendor finance/payout/commission oversight.
- `UserGovernanceService` risk/metrics aggregates.
These read from SQL (CommerceHub financial tables, governance profiles, debt service)
and do not own the underlying financial authority. `Program.cs:911-915`.

## Notification Runtime

**No `PlatformNotification` / `SuperAdminNotification` / `INotificationService` exists**
(0 file matches). The closest platform notification mechanism is **infrastructure-level
dead-letter alerting**, not SuperAdmin-owned: `IMarketplaceEventAlertHook`
(`MarketplaceEventAlerting.cs:16`) with `LoggingMarketplaceEventAlertHook` (`:21`),
`WebhookMarketplaceEventAlertHook` (`:44`, posts to configured `WebhookUrl`), and
`CompositeMarketplaceEventAlertHook` (`:123`), triggered by
`MarketplaceFaultMonitorConsumer` on MassTransit `Fault<>` events
(`NotifyDeadLetterAsync`, `:71`). SuperAdmin writes governance actions to
`UserGovernanceAuditLogs` and emits operational alerts (`VendorGovernanceActionAlerts`)
on governance actions; end-user notifications are delegated to the Notifications bounded
context (outside SuperAdmin authority).

## Monitoring Runtime

- `SuperAdminMarketplaceOperationsController.GetDashboard` — three-system reconciliation:
  SQL counts (`CommerceHubListings`, `:77-84`), Mongo counts
  (`_mongoDbContext.CommerceHubProducts`, `:85-88`), and Elasticsearch active-doc
  count (`_searchIndexService.CountAsync`, `:89`) + search verification.
- `ICommerceHubAutoHealingMetrics.GetSnapshot()` — auto-healing queue health (`:92`).
- **Platform health checks** (`PointOfSale.API/HealthChecks/`):
  `CommerceHubPlatformHealthCheck.cs:12` (Postgres tables/columns, Mongo indexes, ES
  index status), `MarketplaceHealthCheck.cs:11` (drift counters from
  `MarketplaceTelemetry` — `TrustDriftCount`/`ElasticsearchDriftCount`, `:32-42` — plus
  ES `PingAsync`, `:49`; degraded on any drift), and `SystemDependencyHealthChecks`
  (Postgres/S3/RabbitMQ/Redis/ES/Npgsql connectivity).
- `SuperAdminMarketplaceForensicsController.cs:144` — forensic health reads
  `_auditQueue.QueueDepth`, `RetryCount`, lifecycle/recovery evidence counts (24h) from SQL.
- NOTE: there is **no `OperationalConvergenceTelemetry` symbol** in the codebase;
  platform-authority rejections are recorded via the actual telemetry classes
  (`MarketplaceTelemetry`, `StripeOperationalTelemetry`, etc.).

## Recovery Runtime

- **Reindex:** `POST reindex/global`, `POST reindex/tenants/{id}`,
  `reindex/catch-up`, `listings/{id}/verify`, `tenants/{id}/verify`
  (`SuperAdminMarketplaceOperationsController.cs:51-384`, all rate-limited) →
  `ICommerceHubReindexService` (zero-downtime ES rebuild, `CommerceHubReindexService.cs:30`),
  persisted to `CommerceHubRecoveryOperationLedgerEntries` via
  `CommerceHubForensicEvidenceWriter.AppendRecoveryOperation` (`:434`→
  `CommerceHubForensicEvidenceWriter.cs:30-62`); requires `Reason` ≥3 chars.
- **Auto-healing:** `CommerceHubAutoHealingService` (BackgroundService, `:51`) —
  bounded channel (MaxQueueSize 1000), circuit breaker (threshold 20), background
  Mongo→ES reconciliation every 60s, dedup TTL 3 min; draft reason
  `ES_MISS_MONGO_HIT` (`:56-70`).
- **Projection repair:** `CommerceHubOrderProjectionRepairService`,
  `StorefrontOrderProjectionRepairService`, `DiscountReadModelRepairService`.
- **Replay:** outbox `OutboxPublisherJob` (5s, critical-ordered, operator-reconcile
  on poison).
- **Break-glass / emergency-access / disaster-recovery:** **NOT present as a
  platform/SuperAdmin concept.** No `BreakGlass`/`EmergencyAccess`/`DisasterRecovery`
  implementation exists at the SuperAdmin layer. The only related mechanism is
  `CommerceHubEmergencyStopOptions` (config-only `CommerceIntakeStopped`/
  `PayoutsStopped` flags — a fail-safe kill-switch, not a structured break-glass
  audit trail) and an unrelated Affiliate "break-glass" settlement guard.

## Operations Runtime

SuperAdmin *is* the platform operations surface: governance dashboards, marketplace
operations dashboard, reindex, search verification, auto-healing metrics, forensics,
and recovery logging. All mutation endpoints are rate-limited and audited.

---

## CQRS

- **Write authority:** PostgreSQL is the single system of record. SuperAdmin writes
  domain SQL authority (tenants, listings, governance profiles, blacklist) inside
  explicit transactions (`UserGovernanceService.cs:551-557`). CommerceHub financial
  control writes (`CommerceHubFinancialTransactions`/`CommerceHubPayouts` etc.) also
  run in Serializable transactions via `CommerceHubBoundaryMutationScope`.
- **Read authority:** SuperAdmin reads are SQL-authoritative for governance decisions
  (e.g. restore gating recomputes from `Tenants`/`Products`/`ProductVariants`/
  `CommerceHubCategories` SQL). `SuperAdminMarketplaceGovernanceController.cs:591-672`.
  Platform dashboards read from eventually-consistent daily aggregates.
- **Projection (Mongo/ES):** SuperAdmin does **not** write Mongo/ES directly for
  governance; it writes SQL and lets domain consumers/projectors update projections.
  The operations controller *reads* Mongo/ES counts for diagnostics only
  (`SuperAdminMarketplaceOperationsController.cs:61-92`).
- **Replay/Repair:** manual reindex + auto-healing queue; no automatic
  projection-repair on SuperAdmin read (repair-on-read lives in the domain
  projectors, not here).
- **Projection freshness:** surfaced via `SearchSyncStatus`, `MarketplaceTelemetry`
  drift counters, and `IEventLagTracker`, and the operations dashboard diff
  (SQL count vs Mongo count vs ES count).

## Projection Model

SuperAdmin governance state (`UserGovernanceProfiles`, listing suppression flags) is
itself SQL authority. Downstream projections (CommerceHub Mongo product docs, ES index)
are recomputed by CommerceHub consumers after SuperAdmin publishes
`CommerceHubListingUnpublishedEvent`/`CommerceHubListingUpdatedEvent` or after the
auto-healing queue runs. `SuperAdminMarketplaceGovernanceController.cs:250-262,363-376`.

## Mongo Integration

Mongo (`CommerceHubProducts`) is read by SuperAdmin **only for diagnostics/verification**
(`CountDocumentsAsync` for dashboard counts, `SearchSyncStatus` inspection).
`SuperAdminMarketplaceOperationsController.cs:61-92`. SuperAdmin does not own Mongo
writes.

## Redis Integration

- `ISuperAdminLoginRateLimiter` — Redis-backed login rate limiting.
  `SuperAdminAuthService.cs:58-67`.
- Redis is the distributed cache for domain read models; SuperAdmin governance actions
  trigger `ICommerceHubGovernanceCacheInvalidationService` to invalidate.
  `UserGovernanceService.cs:29-45,620-623`.

## Elasticsearch Integration

Elasticsearch is the single search authority. SuperAdmin operations verify and reindex
it; there is no Mongo fallback for search (search unavailability is a hard error in the
domain, surfaced here as `SearchIndexStatus`). `SuperAdminMarketplaceOperationsController.cs:70-92`.

## Outbox

The outbox is **generic AND per-domain — not SuperAdmin-specific.** Multiple outbox
tables exist (all in `PointOfSale.Core/Entities/`):
- `OutboxMessage` (`:19`) — generic transactional outbox; `RequiresOperatorReconciliation`
  flag; at-least-once, ordered per aggregate.
- `MarketplaceEventOutboxMessage` (`:9`) — CommerceHub backbone; published via
  MassTransit to RabbitMQ (`MarketplaceEventOutboxProcessor.cs:95,383`); has
  `RowVersion` concurrency, claim/retry with exponential backoff (`:156-202`).
- `MarketplaceProjectionOutboxMessage` (`:13`) — CQRS **read-model handoff**:
  the consumer stages the row inside the authoritative SQL transaction; the processor
  writes Mongo/secondary read models *after* commit.
- Others: `AccountingOutboxMessage`, `CommerceHubPaymentCompensationOutboxMessage`,
  `PaymentCancellationOutboxMessage`, `StockSyncOutboxMessage`,
  `MarketplaceEventOutboxDeadLetterArchive`.

SuperAdmin publishes only **7 event types** (all marketplace/CMS/review-scoped:
`CommerceHubListingUnpublishedEvent`, `CommerceHubListingUpdatedEvent`,
`ReviewApprovedEvent`, `ReviewRejectedEvent`, `HomepageSectionsPublishedEvent`,
`MarketplaceFooterUpdatedEvent`, `StorefrontFooterUpdatedEvent`) via `IEventPublisher`;
they flow through the marketplace outbox. Generic `OutboxPublisherJob.cs` classifies
critical vs non-critical via `IntegrationEventAuthority.CriticalEventNameFragments`,
orders critical events by `(TenantId, AggregateId, EventVersion)` so stale versions
cannot overtake newer state (`HasUnprocessedEarlierCriticalAggregateMessageAsync`),
sets `RequiresOperatorReconciliation` on critical poison after `MaxRetries=5`, and runs
every 5s via Hangfire. `OutboxPublisherJob.cs:26,68,83,104,151-168`.

## Event Flow

SuperAdmin governance action → write domain SQL authority (transactional) →
`IEventPublisher.PublishAsync` (enqueue OutboxMessage) → OutboxPublisherJob dispatch →
CommerceHub consumers update Mongo/ES + `ICommerceHubAutoHealingQueue` verify.
Example: `SuppressListing` writes `RestorePublicationOnActivate` + publishes
`CommerceHubListingUnpublishedEvent` + appends forensic evidence + enqueues
auto-healing. `SuperAdminMarketplaceGovernanceController.cs:247-274`.

## Read Models

SuperAdmin read models = SQL queries (governance dashboards, listings query joining
`CommerceHubListings`/`Tenants`/`UserGovernanceProfiles`/`Products`/`ProductVariants`/
`CommerceHubCategories`), plus Mongo/ES *diagnostic* counts.
`SuperAdminMarketplaceGovernanceController.cs:499-583`.

## Write Models

SQL aggregates written by SuperAdmin: `SuperAdmins`, `Tenants`,
`CommerceHubListings`, `UserGovernanceProfiles`, `UserGovernanceBlacklistEntries`,
`SuperAdminAuditLogs`, recovery-log table. All mutations are audited and (for governance)
rate-limited and SQL-authority revalidated.

---

## Runtime Authority Matrix

| Concept | Owner (mutator) | Mutation authority | Read authority | Projection authority | Runtime truth |
|---|---|---|---|---|---|
| SuperAdmin identity | SuperAdminAuthService | SQL `SuperAdmins` | SQL | n/a | `SuperAdmins` |
| Tenant lifecycle | SuperAdminMarketplaceGovernanceController | SQL `Tenants` | SQL (IgnoreQueryFilters) | CommerceHub projectors | `Tenants` |
| User governance status | UserGovernanceService | SQL `UserGovernanceProfiles` (Serializable tx) | SQL | CommerceHub vendor-visibility | `UserGovernanceProfiles` |
| Blacklist | UserGovernanceService | SQL `UserGovernanceBlacklistEntries` | SQL | n/a | `UserGovernanceBlacklistEntries` |
| Listing suppression | SuperAdminMarketplaceGovernanceController | SQL `CommerceHubListings` | SQL | CommerceHub projectors | `CommerceHubListings` |
| Platform config | platform config path | SQL `SystemSettings` (TenantId null) | SQL | n/a | `SystemSettings` |
| Category/Brand | CommerceHub | CommerceHub services | SQL (SuperAdmin reads) | CommerceHub projectors | `CommerceHubCategories`/`Brands` |
| Search index | CommerceHub search svc | ES (via reindex) | ES+Mongo (diag) | ES | Elasticsearch |
| Financials | CommerceHub/Payments/Accounting | domain lifecycle | SQL read models | domain projectors | domain SQL |
| Subscription/License | **not a first-class aggregate** | — | — | — | (gap) |

## Runtime Authority Divergence

- **Tenant `IsActive` vs `Status`:** `IsActive` is deprecated but still written
  alongside `Status` for compatibility (`Tenant.cs:24-26,460`). Dual-source of truth =
  drift risk.
- **Governance status vs domain visibility:** `UserGovernanceProfiles.Status` is the
  governance truth; CommerceHub listing visibility is a *derived* projection that must be
  re-synced (`UserGovernanceService.cs:620-623`).
- **Mongo/ES vs SQL:** search/projection counts can diverge; surfaced as a diagnostics
  gap, repaired manually via reindex.

## Cross-Domain Authority Graph

SuperAdmin runs **in-process against the single shared `ApplicationDbContext`**
(`ApplicationDbContext.cs:140` `DbSet<SuperAdminUser> SuperAdmins`) — it does NOT call
peer contexts over HTTP/gRPC; it writes peer tables *directly*. Its event bus is
`IEventPublisher` (Core `Interfaces/IEventPublisher.cs:8`) → MassTransit/InMemory.
It publishes **only 7 event types** (all marketplace/CMS/review-scoped) and has
**0 inbound consumers** (`IConsumer<>`) — SuperAdmin is a pure *authority writer*, never
an event subscriber. Verified zero references to `Affiliate*`,
`IPaymentLifecycleService`, `AccountingOutbox`, `IJournalEntry`, `Treasury`,
`Notification`/`IEmailService`, `OnlineCustomer`, or any Analytics SDK/gRPC client.

| Peer | Reads | Writes | Events In | Events Out | Ownership | Sync |
|---|---|---|---|---|---|---|
| Merchant Platform / POS Admin (User realm) | `UserGovernanceProfiles`, `UserGovernanceBlacklistEntries`, `UserGovernanceAuditLogs`, `Tenants` (`UserGovernanceService.cs:104,237,495,612`) | `UserGovernance*` tables, `Tenants` (governance) | none | none | SuperAdmin **governs** User identity/risk via dedicated governance tables (not the `User` table) | Direct shared-DbContext writes + `UserGovernanceAuditLogs` |
| CommerceHub (Marketplace lifecycle) | `CommerceHubListings`, `CommerceHubSubOrders`, `CommerceHubMarketplaceOrders`, `CommerceHubCustomers`, `CommerceHubCategories`, `CommerceHubRefunds`/`RefundRequests`, `Products`/`ProductVariants` (`SuperAdminMarketplaceGovernanceController.cs:238,358`; `SuperAdminProductReviewService.cs:222,274`) | `CommerceHubListings`, `CommerceHubRefundRequests`, `CommerceHubReturnRequests`, `CommerceHubCustomers` (direct) | none | `CommerceHubListingUnpublishedEvent` (`:251`), `CommerceHubListingUpdatedEvent` (`:364`) | SuperAdmin owns listing suppression/restore & review moderation; CommerceHub owns order/catalog lifecycle | Direct DbContext writes + outbox events → `CommerceHubCommandHandlers.cs` & `CommerceHubVendorVisibilityService.cs` |
| CommerceHub Finance (Payouts/Commission/Refunds/Vendor) | `CommerceHubFinancialTransactions` (28), `CommerceHubSubOrders`, `CommerceHubVendorEarnings`, `CommerceHubPayouts`, `CommerceHubPayments`, `CommerceHubVendorPayoutMethods`, `CommissionRules` (`SuperAdminFinanceOverviewService.cs:70,229`) | `CommerceHubFinancialTransactions`, `CommerceHubPayouts`, `CommerceHubVendorEarnings`, `CommissionRules` (7 writes), `CommerceHubPayoutItems`, `CommerceHubRecoveryOperationLedgerEntries`, `CommerceHubMerchantLifecycleLedgerEntries` | none | none (finance mutations in-process) | SuperAdmin **owns** commission rules, vendor payout execution & refund approval; writes the CommerceHub financial ledger directly via `ICommerceHubFinancialLedgerService` + `CommerceHubBoundaryMutationScope.BeginPayout()` (`SuperAdminPayoutsService.cs:226,325`) in Serializable tx | Direct DbContext + serializable tx + `CommerceHubVendorFinancialControlException` guards |
| Affiliate Platform | — | — | — | — | **No authority / no linkage** — 0 references to `Affiliate`/`AffiliateUser` anywhere in SuperAdmin | N/A (separate bounded context) |
| Payments | read-only via `CommerceHubPayments`/`CommerceHubFinancialTransactions` | **never** writes payment intents | — | — | **Defers** — 0 refs to `IPaymentLifecycleService`; payment lifecycle owned by Payments context | Reads CommerceHub finance projection only |
| Treasury | — | — | — | — | **Defers** — 0 references to `Treasury` | N/A |
| Accounting | — | — | — | — | **Defers** — 0 refs to `AccountingOutbox`/`IJournalEntry`; no journal/ledger push-out from SuperAdmin | N/A |
| Shipping | read-only `ShippingTotal`/`ShippingMethodName` on orders (`SuperAdminOrderDtos.cs:119,173`; `SuperAdminOrdersService.cs:182,246`) | none | — | — | **Defers** — shipping only a display field on orders | Read-only projection of order data |
| Identity (5 realms) | `SuperAdmins` (own realm) (`SuperAdminAuthService.cs:92`; `SuperAdminPlatformAuthority.cs:48`); `CommerceHubCustomers` for review author lookups | `SuperAdmins` (create/delete admins) (`SuperAdminManagementService.cs:52,58`) | — | — | SuperAdmin is its **own realm** (`SuperAdminUser`, table `SuperAdmins`); distinct JWT issuer/claims validator (`SuperAdminTokenClaimsValidator.cs`) from `User`/`OnlineCustomer`/`CommerceHubCustomer`/`AffiliateUser` | Separately seeded root (`SuperAdminRootSeeder.cs:35,63`); authority enforced by `SuperAdminPlatformAuthorityMiddleware` |
| Analytics | reads aggregates from `CommerceHubDailyFinancialAggregates`, `CommerceHubFinancialTransactions` | none | — | — | **Defers** — 0 Analytics SDK/dispatcher refs; no event emission | Read-only financial aggregates |
| Notifications | — | — | — | — | **Defers** — 0 refs to `Notification`/`IEmailService` | N/A |

**Synchronization mechanisms (observed):**
1. **Shared `ApplicationDbContext`** — dominant; SuperAdmin mutates CommerceHub and
   governance tables in-process (no API hop).
2. **Outbox-backed `IEventPublisher`** (MassTransit) — 7 event types, all
   marketplace/CMS/review scoped: `CommerceHubListingUnpublishedEvent`,
   `CommerceHubListingUpdatedEvent`, `ReviewApprovedEvent`
   (`SuperAdminProductReviewService.cs:133`), `ReviewRejectedEvent` (`:172`),
   `HomepageSectionsPublishedEvent` (`:395`), `MarketplaceFooterUpdatedEvent` (`:156`),
   `StorefrontFooterUpdatedEvent` (`:133`). Consumed by `CommerceHubCommandHandlers.cs`,
   `CommerceHubVendorVisibilityService.cs`, and `RedisInvalidationConsumer.cs:32,204`.
3. **Background auto-healing queue** — `EnqueueListingVerification` →
   `CommerceHubAutoHealingTask` on `_autoHealingQueue`
   (`SuperAdminMarketplaceGovernanceController.cs:690,702`) for post-governance
   reconciliation.
4. **Forensic evidence writer** — `CommerceHubForensicEvidenceWriter.AppendMerchantLifecycle`
   / `AppendRecoveryOperation` (`:263,330,434`) writes immutable audit rows synchronously
   with the mutation.

## Business Invariants (implementation-backed)

1. **Tenant isolation is CRITICAL.** `X-Tenant-Id` stripped at boundary; every tenant
   query is scoped by `ITenantContext.RequiredTenantId`. Cross-tenant read = violation.
2. **Platform security is fail-closed.** `SuperAdminPlatformAuthority` rejects on
   missing SQL row, stale `TokenVersion`, or non-Active governance status.
   `SuperAdminPlatformAuthority.cs:59-95`.
3. **Single protected root.** Exactly one `IsSystem` SuperAdmin; cannot be
   suspended/banned/locked; no self-ban. `SuperAdminRootSeeder.cs:39-48`;
   `UserGovernanceService.cs:573-589`.
4. **Governance mutations are Serializable and audited.** `UserGovernanceService.cs:551-557`;
   every action logs to audit.
5. **Restore is SQL-authority gated.** A suppressed listing cannot be restored unless
   tenant/product/variant/category SQL authority is live.
   `SuperAdminMarketplaceGovernanceController.cs:591-672`.
6. **Refresh-token replay is detected and revokes all sessions.**
   `SuperAdminAuthService.cs:269-306`.
7. **SuperAdmin secret must be explicitly configured** or the app refuses to start.
   `Program.cs:2190-2201`.

## Architectural Decisions (WHY)

1. **Why SuperAdmin is isolated.** A forged or leaked tenant/storefront/customer token
   must never escalate to platform control. Hence a separate `SuperAdmins` table,
   `SuperAdminJwt` scheme with a dedicated secret, and `TokenVersion` revocation —
   cryptographically disjoint from the five other realms.
2. **Why tenant isolation exists.** Multi-tenant SaaS requires hard boundaries; `X-Tenant-Id`
   from the client is untrusted and stripped, replaced by server-resolved
   `RequiredTenantId`.
3. **Why SQL remains authority.** Projections (Mongo/ES) can drift; governance and
   financial decisions must be made on the system of record. SuperAdmin re-derives
   visibility from SQL before any restore and recomputes dashboards from SQL.
4. **Why CQRS exists.** High-volume marketplace reads (search, listings) are served from
   Mongo/ES projections while writes stay in PostgreSQL; SuperAdmin governs by writing SQL
   and letting projectors catch up.
5. **Why replay/repair exists.** Projections drift; manual reindex + auto-healing queue
   + outbox replay restore convergence without manual SQL surgery.
6. **Why recovery/break-glass exists.** The `IsSystem` root is the protected emergency
   identity so platform access survives admin bans/suspensions.
7. **Why governance writes domain SQL (not a parallel table).** To keep one source of
   truth: a suppressed listing is just `CommerceHubListings.RestorePublicationOnActivate`,
   not a separate "superadmin_overrides" table that could diverge.

## Technical Debt (evidence-backed)

1. **Partial Migration — `IsActive` vs `Status` (HIGH).** `Tenant.IsActive` is
   deprecated but still written alongside `Tenant.Status`
   (`Tenant.cs:22-26`; `SuperAdminMarketplaceGovernanceController.cs:459-461`).
   Concrete split-brain: `TenantsController.SetTenantStatus` (`:216-228`) writes **only**
   `tenant.IsActive = request.IsActive;` and never touches `Tenant.Status` — the
   platform-facing mutate endpoint is stale (`TenantsController.cs:225`). Meanwhile
   `TenantResolutionMiddleware` gates access on **both** `IsActive && Status==Active`
   (`:236,289,313`), so a tenant with `IsActive=true, Status=PendingSetup` still
   passes — inconsistent authoritative gate. `TenantRegistrationService.cs:207-209,
   469-470` and `DevelopmentCommerceHubSeedService.cs:205-206` dual-write both. Risk:
   divergent tenant truth between the two flags.
2. **Duplicate authority over Tenant lifecycle (MED).** `TenantsController`
   (SuperAdmin) PATCHes `Tenant.IsActive`; `TenantRegistrationService` (Platform)
   controls `Status`; `ServiceDiscoveryProjectionConsumer.cs:309-352` reads governance
   `Status` to derive vendor servability. Three owners of one concept, no single
   lifecycle service coordinating them.
3. **No first-class Subscription / License / FeatureFlag aggregates (HIGH).** Searched
   `PointOfSale.Core` — only the generic `SystemSetting` KV table (optional
   `TenantId`) and payments-specific `PaymentRuntimeFeatureFlags` exist. `GlobalCategory`,
   `GlobalBrand`, `Subscription`, `License` entities return **0 matches** across the repo
   (`Controllers/Legacy/` exists but those names do not). Platform subscription/licensing/
   feature-flag state, if modeled, is implicit in `SystemSetting` rows or tenant fields —
   fragile and unverifiable as authority. This is itself documentation/architecture drift:
   the brief's assumed governance tables do not exist.
4. **Legacy runtime — `auth_token` cookie (MED).** `AuthCookieConfiguration.cs:13`
   `LegacyPosAccessTokenCookieName = "auth_token"` is still accepted in
   `Program.cs:2280-2282` (`OnMessageReceived` falls back to legacy cookie); stale
   sessions persist until natural expiry.
5. **Projection Drift + Manual Repair (HIGH).** `AdminSyncController.cs:14-24` —
   "Admin synchronization endpoints for CQRS data repair… ONE-TIME operations to fix
   historical data issues where variants exist in PostgreSQL but not in MongoDB." Manual
   trigger, no automation. `ProductsController.cs:463-467,659` — event version overflow
   requires "Manual intervention required." `CommerceHubSearchSyncHealthCheck.cs:83-102`
   — drift detected at ratio <0.95 but only *telemetered* (`RecordDriftDiagnostic`);
   **no auto-heal**.
6. **Projection version/ordering handling (MED).** `MassTransitEventPublisher.cs:360-663`
   — manual `ProjectionMutationVersion` advancement via `IProjectionVersioned`;
   `IsProjectionVersionManuallyAdvanced` (`:659`) detects hand-bumped versions —
   out-of-order/duplicate replay risk in Mongo/ES vs SQL.
7. **Mixed responsibilities in SuperAdmin (MED).** `SuperAdminMarketplaceGovernanceController`
   injects `ICommerceHubAutoHealingQueue` + `IEventPublisher` and reaches into
   CommerceHub ops internals (`:21-41`); finance spans
   `SuperAdminFinanceController` + `SuperAdminVendorFinanceController` +
   `SuperAdminCommissionController` + `SuperAdminPayoutsController` — governance +
   finance + vendor payouts in one bounded context.
8. **Hidden coupling — SuperAdmin → CommerceHub internals (MED).**
   `SuperAdminMarketplaceGovernanceController.cs:25` calls `ICommerceHubAutoHealingQueue`
   directly rather than purely via lifecycle events.
9. **Dead/legacy / fail-open surface (LOW/MED).** `TenantsController.cs:273-280`
   `EnableBaileysForAllTenants()` is a `[DEV ONLY] DANGER` endpoint shipped in a
   production controller — fail-open risk if the route is not excluded.
10. **Break-glass / recovery is config-only (LOW/MED).** `CommerceHubEmergencyStopOptions.cs:7-15`
    (`CommerceIntakeStopped`, `PayoutsStopped`) are pure config flags (also in
    `appsettings.json:57,60`), not a structured break-glass audit trail.

## Implementation Follow-up Items

| Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|
| Tenant dual-status consolidation | `IsActive` + `Status` both written | Backwards compat | Migrate all reads to `Status`; drop `IsActive` | High | Open |
| First-class Subscription/License/FeatureFlag model | Implicit in `SystemSetting`/tenant fields; `GlobalCategory`/`GlobalBrand`/`Subscription`/`License` entities return 0 matches | Not modeled as authority | Introduce explicit aggregates + outbox | High | Open (verified gap) |
| Automated reindex/replay | Manual SuperAdmin endpoints | Operator-triggered only | Scheduled/healing-driven auto-repair | Medium | Open |
| Governance status → projection auto-sync | Manual reindex after drift | Drift detected, not auto-fixed | Event-driven auto-heal on divergence | Medium | Open |
| SuperAdmin secret config hardening | Fail-fast if unset | Prevents escalation | Secret-manager + rotation | Medium | Done (fail-fast exists) |

## Certification

- **Implementation Confidence:** High for identity, governance, tenant lifecycle,
  marketplace governance, operations/recovery, outbox, CQRS/SQL-authority pattern —
  all read directly from primary source.
- **Architecture Completeness:** High. Every owned runtime (identity, governance,
  tenant lifecycle, marketplace governance, CommerceHub financial control, operations/
  recovery, outbox, CQRS/SQL-authority pattern) is read directly from primary source.
  Subscription/Licensing/FeatureFlag/GlobalCategory/GlobalBrand specifics are resolved
  as verified **gaps** (no first-class entities), not open questions.
- **Runtime Authority Verification:** Verified — SuperAdmin owns platform authority
  over domain SQL; defers projections to domains.
- **Cross-Domain Verification:** Verified for CommerceHub (SuperAdmin owns
  listing/review governance + CommerceHub *financial control* — commission, payouts,
  refunds, vendor earnings — writing the CommerceHub financial ledger directly via
  `ICommerceHubFinancialLedgerService` + `CommerceHubBoundaryMutationScope`),
  Identity (own realm), Merchant (governs tenant + user governance). **Verified zero
  authority/linkage** to Affiliate (0 refs), Payments lifecycle (0 refs to
  `IPaymentLifecycleService`), Treasury (0 refs), Accounting (0 refs to `AccountingOutbox`/
  `IJournalEntry`), Shipping-write (read-only), Analytics (read-only aggregates),
  Notifications (0 refs). SuperAdmin publishes only 7 outbox event types and
  **consumes 0 inbound events** — it is a pure authority writer.
- **Verified Capabilities:** isolated auth + revocation; tenant lifecycle governance;
  user governance (suspend/ban/lock + blacklist); marketplace listing suppression/
  restore with SQL gating; CommerceHub financial control (payouts/commission/refunds/
  vendor earnings); reindex/replay/repair ops; platform financial dashboards; full
  audit trail; rate-limited governance writes.
- **Missing / Not-Backed Capabilities:** no first-class `Subscription`, `License`,
  `GlobalCategory`, or `GlobalBrand` aggregates (verified 0 matches — the brief's
  assumed governance tables do not exist); no dedicated FeatureFlag/experiment/override
  runtime; no structured break-glass audit trail (emergency stop is config-only via
  `CommerceHubEmergencyStopOptions`); no automated projection healing; no SuperAdmin
  notification ownership; no SuperAdmin inbound event consumption.
- **Technical Debt Summary:** tenant dual-status, missing subscription/license/flag
  aggregates, manual-only recovery, projection drift not auto-healed.
- **Architectural Observations:** SuperAdmin is a *constitutional* layer — it governs by
  writing domain SQL authority under a Serializable, audited, fail-closed, rate-limited
  runtime, then lets domains recompute projections. This is the cleanest expression of
  the platform's "SQL is authority, projections are derivative" principle at the
  platform level.

---

---

## SuperAdmin vs Merchant Platform

- **Merchant (POS Admin) owns:** tenant-scoped operations, the `User` operator identity,
  tenant settings (`SettingsController`, tenant-scoped via `ITenantContext.RequiredTenantId`
  — `SettingsController.cs:79,161`), catalog/inventory/pricing, orders, payments intake.
- **SuperAdmin owns:** platform authority over the tenant as a whole (suspend/restore via
  `Tenants.Status`), and over the operator accounts (`UserGovernanceProfiles` for
  `UserGovernanceRole.Admin`).
- **Shared:** the `Tenants` table and the `UserGovernanceProfiles` table are written by
  both Merchant (normal lifecycle) and SuperAdmin (governance override) — but SuperAdmin
  writes only under the `RequireSuperAdmin` policy + `SuperAdminPlatformAuthority`
  revalidation. The `IsActive`/`Status` dual flag is the shared-surface drift risk.
- **Synchronized:** governance status changes fan out to domain recompute
  (`UserGovernanceService.cs:620-623`) rather than direct cross-writes.

## SuperAdmin vs CommerceHub

- **Marketplace governance (SuperAdmin owns):** listing visibility suppression/restore,
  tenant marketplace suspend/restore, vendor/customer governance status, blacklist.
- **Marketplace lifecycle (CommerceHub owns):** order/payment/settlement, listing
  publication mechanics, category/brand definitions (`CommerceHubCategories`/
  `CommerceHubBrands`), search indexing.
- **Shared runtime:** `CommerceHubListings` — SuperAdmin writes the governance overlay
  (`RestorePublicationOnActivate`) and publishes `CommerceHubListing*Event`s; CommerceHub
  consumers update Mongo/ES.
- **Synchronization:** `ICommerceHubAutoHealingQueue` (high-priority verify tasks) +
  outbox events + `ICommerceHubGovernanceCacheInvalidationService`
  (`UserGovernanceService.cs:29-45,620-623`).
- **Ownership:** CommerceHub owns the marketplace; SuperAdmin owns *whether* it is visible/
  active at the platform level.

## SuperAdmin vs Affiliate Platform

- **Verified: no authority / no linkage.** A full search of all 78 SuperAdmin `.cs`
  files returns **0 references** to `Affiliate`/`AffiliateUser`/`AffiliateAccount`.
  SuperAdmin does not read, write, or emit events for the Affiliate Platform.
- **Affiliate governance:** there is no SuperAdmin affiliate controller or governance
  surface. Affiliate identity is a separate, cryptographically isolated realm
  (`AffiliateUser`/`AffiliateAccount`, `AffiliateJwt`); any affiliate governance is
  owned by the Affiliate Platform itself, not SuperAdmin.
- **Categories:** CommerceHub categories/brands are the marketplace taxonomy; affiliate
  taxonomy is owned by the Affiliate Platform. No SuperAdmin-owned global
  category/brand aggregate exists (verified: 0 matches).
- **Identity:** SuperAdminUser ≠ AffiliateUser — disjoint realms.
- **Synchronization:** none at the SuperAdmin layer. If affiliate lifecycle intersects
  platform governance, it flows through the shared outbox/event backbone owned by
  the Affiliate Platform, not via SuperAdmin.

---

## Platform Governance Synthesis (one complete lifecycle)

Step-by-step platform → tenant → store → ... → recovery, with runtime owner, authority,
SQL authority, projection authority, events, synchronization, replay, failure handling,
and availability gates — **all implementation-backed**.

1. **Platform bootstrap (root seed)**
   - *Owner:* `SuperAdminRootSeeder` (IHostedService).
   - *Authority:* env vars `SUPERADMIN_EMAIL`/`SUPERADMIN_PASSWORD`; fails if >1 system
     user; skips if PostgreSQL down. (`SuperAdminRootSeeder.cs:10-96`)
   - *SQL:* `SuperAdmins` (IsSystem=true). *Projection:* none. *Events:* none.
   - *Failure:* throws if multiple roots; warns + continues if DB unavailable.
   - *Gate:* app fails-fast if `SuperAdminJwt:Secret` unset (`Program.cs:2190-2201`).

2. **Tenant registration / creation**
   - *Owner:* `TenantRegistrationService`. *SQL:* `Tenants` (Status=`PendingSetup`).
   - *Authority:* platform registration; tenant is the isolation boundary + governed vendor.
   - *Projection:* tenant read models. *Events:* registration events → outbox.
   - *Gate:* `UserGovernanceService.EnsureRegistrationAllowedAsync` blacklist check
     (`UserGovernanceService.cs:89-129`).

3. **Store / catalog creation (Merchant Platform)**
   - *Owner:* Merchant Platform. *SQL:* `Products`/`ProductVariants`. SuperAdmin does not
     own this; it only reads SQL authority later for visibility recomputation.

4. **Configuration**
   - *Owner:* tenant-scoped `SettingsController` (currency, country) writes
     `SystemSettings` + publishes `TenantCurrencyChangedEvent` to outbox
     (`SettingsController.cs:155-262`). Platform-level config = `SystemSettings` with
     `TenantId=null`.

5. **Policies / governance profile**
   - *Owner:* `UserGovernanceService` creates `UserGovernanceProfiles` on first access
     (`EnsureProfileExistsAsync`, `:49-57`). *SQL:* `UserGovernanceProfiles`.
   - *Authority:* evaluated on every login/checkout/order/payout
     (`Ensure*AllowedAsync` family).

6. **Merchant Platform publication to CommerceHub**
   - *Owner:* CommerceHub publish handler. *SQL:* `CommerceHubListings`,
     `CommerceHubCategories`. *Projection:* Mongo `CommerceHubProductDocument` + ES.
   - *Events:* `CommerceHubListingUpdatedEvent` → outbox → consumers.

7. **SuperAdmin marketplace governance (suppress)**
   - *Owner:* `SuperAdminMarketplaceGovernanceController.SuppressListing`.
   - *SQL authority:* `CommerceHubListings.RestorePublicationOnActivate=true`
     (transactional, `:247-273`).
   - *Events out:* `CommerceHubListingUnpublishedEvent` (`IEventPublisher`).
   - *Forensics:* `CommerceHubForensicEvidenceWriter.AppendMerchantLifecycle`.
   - *Sync:* `ICommerceHubAutoHealingQueue.Enqueue` (high-priority verify).
   - *Projection authority:* CommerceHub consumers update Mongo/ES from the listing event.
   - *Failure:* if listing not found → 404; idempotent (changed=false if already hidden).

8. **SuperAdmin marketplace governance (restore) — SQL-authority gated**
   - *Owner:* `RestoreListing`. *SQL authority:* clears governance restore state only if
     `GetRestoreAuthorityFailureAsync` passes: tenant active, vendor not suspended/banned,
     product visible, variant current, category lineage live
     (`:591-672`).
   - *Events out:* `CommerceHubListingUpdatedEvent`. *Failure:* 409
     `RESTORE_AUTHORITY_REJECTED` if any SQL authority rejects.
   - *Availability gate:* restore is blocked unless the full SQL-authority chain is live —
     fail-closed visibility.

9. **CommerceHub order / payment lifecycle**
   - *Owner:* CommerceHub lifecycle services (`IPaymentLifecycleService`, order mgmt).
   - *SQL:* order/payment tables. *Projection:* financial read models.
   - *SuperAdmin role:* read-only oversight (payouts, commission, refunds, vendor finance);
     never writes order/payment rows.

10. **Payments / Treasury / Accounting**
    - *Owners:* respective lifecycle services. *SQL:* payment/treasury/GL tables.
    - *SuperAdmin role:* reads financial dashboards (`SuperAdminFinancialDashboardService`,
      `SuperAdminVendorFinanceService`); governs payout/refund gating via
      `EnsureVendorPayoutAllowedAsync` etc.

11. **Monitoring**
    - *Owner:* `SuperAdminMarketplaceOperationsController.GetDashboard` + health checks
      (`CommerceHubPlatformHealthCheck.cs:12`, `MarketplaceHealthCheck.cs:11` with
      `MarketplaceTelemetry` drift counters) + `ICommerceHubAutoHealingMetrics`.
      *Reads:* three-system reconciliation — SQL vs Mongo vs ES counts, `SearchSyncStatus`.
    - *Note:* there is no `OperationalConvergenceTelemetry` symbol; platform-authority
      rejections are recorded via the real telemetry classes (`MarketplaceTelemetry`,
      `StripeOperationalTelemetry`, etc.).

12. **Recovery (reindex / replay / repair)**
    - *Owner:* SuperAdmin ops endpoints → `ICommerceHubReindexService` +
      `ICommerceHubAutoHealingQueue`. *SQL authority:* re-derived from `Tenants`/
      `CommerceHubListings`/products. *Persistence:* recovery op log.
    - *Replay:* outbox `OutboxPublisherJob` (5s, critical-ordered, operator-reconcile on
      poison). *Failure:* manual reindex on divergence; no automated self-heal in SuperAdmin.

13. **Analytics**
    - *Owner:* SuperAdmin financial/risk read models (SQL-backed). Cross-tenant
      aggregation for platform oversight.

**Cross-cutting invariants per step:** every SuperAdmin mutation is audited
(`ISuperAdminAuditService`), rate-limited on governance writes, and re-validated against
SQL by `SuperAdminPlatformAuthority` (claims=evidence, SQL=authority). Projection drift
is repaired by domain consumers/outbox replay, not by SuperAdmin writing Mongo/ES.

---

## Architectural Synthesis (shared patterns across the platform)

The SuperAdmin Platform is the cleanest expression of the platform-wide architectural
contract. The same patterns that govern CommerceHub, Merchant, Affiliate, Treasury,
Accounting, Payments, Shipping, and Identity all converge here:

- **Runtime Authority (necessary-not-sufficient):** `RequiredTenantId` + SQL revalidation.
  SuperAdmin extends this to platform actors via `SuperAdminPlatformAuthority` +
  `TokenVersion`.
- **SQL Authority:** PostgreSQL is the single system of record; Mongo/Redis/ES are
  derivative. SuperAdmin *writes domain SQL* and lets domains project.
- **CQRS:** writes in SQL, reads from projections (Mongo/ES) or SQL read models; SuperAdmin
  favors SQL reads for governance decisions (fail-closed).
- **Outbox:** generic, shared, critical-ordered, operator-reconcile on poison. SuperAdmin
  events flow through it.
- **Replay / Repair-on-Read:** projections recomputed from SQL; manual reindex +
  auto-healing queue for marketplace; outbox replay for events.
- **Fail-Closed Design:** stale token, non-Active governance, rejected restore authority,
  missing JWT secret → all reject/deny by default.
- **Platform Governance:** a constitutional layer that governs by writing domain SQL
  authority under Serializable, audited, rate-limited, re-validated runtime — never
  bypassing lifecycle owners.
- **Cross-Domain Isolation:** five cryptographically disjoint identity realms; SuperAdmin
  is one of them, sharing only the governance *table* (`UserGovernanceProfiles`) across
  Admin/Vendor/Customer subjects.
- **Availability Gates:** marketplace visibility restore, payouts, refunds, and login are
  all gated on the live SQL-authority chain — never on projection state alone.
- **Ownership Boundaries:** SuperAdmin owns *platform authority*; every domain owns its
  *lifecycle*. The boundary is enforced by who writes which SQL table and which policy
  guards the endpoint.

*Document generated from implementation evidence. All `file:line` citations point to the
backend as of the current working tree. Re-verify citations if the tree changes.*
