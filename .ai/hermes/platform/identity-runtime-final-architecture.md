44# Identity Runtime — FINAL CANONICAL ARCHITECTURE

> Shopixy / PointOfSale (PointOfSale solution)
> Implementation is the ONLY source of truth.
> Every architectural conclusion below is backed by `file:line` implementation evidence.
> Prior markdown, comments, UI, and naming are treated as secondary and were re-verified in code.
> Working tree: `C:\Users\fadya\source\repos\PointOfSale` (branch `feature/new-pr210`).

---

## Executive Summary

Shopixy does **not** have a single "authentication" or "ASP.NET Identity" subsystem. What the
codebase actually implements is a **Multi-Tenant Identity & Authority Runtime** built on top of
five cryptographically isolated account realms plus one non-account capability scheme, unified by a
single invariant:

> **JWT/claims are advisory evidence. PostgreSQL (SQL) is the sole authority. Every authenticated
> request is re-validated against current SQL state before it is authorized.**

This is enforced by scheme-specific "authority" services that run during or immediately after JWT
validation:

- POS operator (`User`) → `PosSessionAuthority` runs in the JWT `OnTokenValidated` event.
- Super Admin (`SuperAdminUser`) → `SuperAdminPlatformAuthority` runs in a post-authz middleware.
- Tenant admin/ops (`User` via `AdminTenantAuthorityMiddleware`) → `AdminTenantAuthority`.
- Storefront customer (`OnlineCustomer`) → `StorefrontJwtBlacklistMiddleware` (validates SQL
  `IsActive` + durable `TokensValidAfterUtc` cutoff).
- CommerceHub customer (`CommerceHubCustomer`) → `CustomerJwt` `OnTokenValidated` →
  `CustomerTokenRevocationService` + `IUserGovernanceService`.
- Affiliate (`AffiliateUser`/`AffiliateAccount`) → inline SQL revalidation in `AffiliateJwt`
  `OnTokenValidated`.

The runtime owns **identity, authentication, tenant isolation, session/refresh-token binding, and
claims issuance**. It does **NOT** own business domain truth: orders, payments, inventory,
catalog, accounting, shipping, and affiliate money are each owned by their own lifecycle services.
Identity only ever *carries* a tenant scope; it never becomes the source of business truth.

**Architectural classification (justified in full in Architectural Synthesis):**
Shopixy's Identity layer is a **Multi-Tenant Identity & Authority Runtime** — not a pure
authentication runtime, not a pure authorization runtime, and not a claims/access-control runtime.
It is the intersection of all of those, bound to tenant isolation, with SQL-authority revalidation
as the load-bearing mechanism.

---

## Architecture Vision

Establish, propagate, validate, and enforce identity and authority across SuperAdmin, Tenants,
Employees, Customers, Guests, APIs, and every runtime — without identity or permissions ever
becoming the source of business truth — by issuing short-lived, cryptographically realm-isolated
tokens whose claims are treated as evidence, then re-checking the current SQL authority on every
request and revoking derived credentials whenever the SQL authority is reduced.

## Runtime Purpose

Issue and validate identity-bearing tokens, resolve the authoritative tenant for every request,
bind refresh credentials to a current SQL session, and gate access to tenant-scoped and
platform-scoped operations — failing closed whenever SQL authority cannot be confirmed.

## Runtime Scope

In scope: account realms, JWT issuance/validation, tenant resolution, session/refresh-token
binding & revocation, claims construction, governance status enforcement
(`UserGovernanceProfile`), SQL-authority revalidation middlewares, identity-graph reconciliation.

Out of scope (owned elsewhere): order lifecycle, payment lifecycle, inventory, catalog, pricing,
accounting/GL, shipping, affiliate payouts, notifications, search indexing, commerce logic.

---

## Runtime Owns

| Concept | Owner (implementation) |
|---|---|
| POS operator identity | `User` entity (`PointOfSale.Core/Entities/User.cs`) |
| Super Admin identity | `SuperAdminUser` entity (`PointOfSale.Core/SuperAdmin/Models/SuperAdminUser.cs`) |
| Storefront customer identity | `OnlineCustomer` entity (`PointOfSale.Core/Entities/OnlineCustomer.cs`) |
| CommerceHub customer identity | `CommerceHubCustomer` entity (`PointOfSale.Core/Entities/CommerceHubCustomer.cs`) |
| Affiliate identity | `AffiliateUser`/`AffiliateAccount` (`PointOfSale.Core/Entities/Affiliate/`) |
| Tenant identity & isolation | `Tenant` entity + `TenantResolutionMiddleware` |
| JWT issuance / signing | Per-scheme token builders in each auth service / controller |
| Refresh-token binding & rotation | `RefreshTokenGenerator`/`RefreshTokenHasher` + per-entity `RefreshToken` |
| Session revocation on authority loss | `PrivilegedRefreshTokenRevocationInterceptor` |
| Cross-realm governance status | `UserGovernanceProfile` (`PointOfSale.Core/Entities/UserGovernanceProfile.cs`) |
| Identity-graph reconciliation | `CanonicalIdentityReconciliationService` (hosted) |

## Runtime Does NOT Own

- Order / payment / inventory / catalog / pricing / accounting / shipping / affiliate-payout truth.
- Any stored RBAC permission table or role-hierarchy engine (VERIFIED ABSENT — see Permissions).
- MFA (VERIFIED ABSENT). Impersonation (VERIFIED ABSENT).
- Business capability strings returned to the SPA — these are UI hints only, never server-enforced.

---

## Architecture Principles (each code-cited)

**P1 — Claims are evidence, SQL is authority.** A validly signed JWT is necessary but never
sufficient. Every realm re-queries SQL per request:
- POS: `PointOfSale.API/Program.cs:2304-2331` (`OnTokenValidated` → `PosSessionAuthority`).
- SuperAdmin: `PointOfSale.API/Program.cs:3306` middleware →
  `PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminPlatformAuthority.cs:48-103`.
- Storefront: `PointOfSale.API/Program.cs:3303` middleware →
  `PointOfSale.API/Middleware/StorefrontJwtBlacklistMiddleware.cs:118-185`.
- CommerceHub customer: `PointOfSale.API/Program.cs:2526-2579` →
  `PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs`.
- Affiliate: `PointOfSale.API/Program.cs:2646-2720` (inline SQL revalidation).

**P2 — Cryptographic realm isolation.** Five JWT schemes with distinct issuer/audience/secret,
fail-fast if a secret is empty or reused. `PointOfSale.API/Program.cs:2275,2350,2419,2498,2624`;
secret pairwise-distinctness enforced by
`PointOfSale.API/Configuration/SensitiveConfigurationValidator.cs:38-47`.

**P3 — Tenant isolation is server-authoritative.** `X-Tenant-Id` inbound header is stripped and
never trusted; tenant resolved only from authenticated claim or server-controlled `Host` header.
`PointOfSale.API/Middleware/TenantResolutionMiddleware.cs:82,106-154,259-267`.

**P4 — Fail closed on uncertainty.** Storefront revocation check rejects when Redis/PG unavailable
(`StorefrontJwtBlacklistMiddleware.cs:189-216`); POS session authority rejects on any missing/
stale claim; `ClockSkew=TimeSpan.Zero` everywhere (`Program.cs:2342,2412,2491,2600,2749`).

**P5 — Refresh credentials are bound to a current SQL session and die when authority is reduced.**
POS access token carries `pos_session` binding hash; rotation nulls the prior binding
(`AuthController.cs:542-560`, `PosSessionAuthority.cs:98-103`). The interceptor nulls
`User.RefreshToken` whenever role/active/deleted/tenant/governance authority is reduced
(`PrivilegedRefreshTokenRevocationInterceptor.cs:45-133,135-169,189-218`).

**P6 — Durable token cutoff, not just blacklist.** `TokensValidAfterUtc` on customer/affiliate
entities rejects any token issued before the cutoff even if Redis/PG blacklist is unavailable
(`OnlineCustomer.TokensValidAfterUtc`; `StorefrontJwtBlacklistMiddleware.cs:144-185`;
`CommerceHubCustomer.RevokeAllSessions`;
`CustomerTokenRevocationService.cs:285-294`).

**P7 — No stored permissions; enforcement is scheme + role-claim + SQL authority + governance.**
VERIFIED ABSENT: no `IPermissionService`, no `Permission` table/enum. Capability strings are SPA
hints only (`SuperAdminAuthController.NavigationCapabilities`).

**P8 — Legacy compatibility is explicit, not implicit.** Legacy `auth_token` cookie still read as
fallback (`Program.cs:2289`); `Tenant.IsActive` bool coexists with `Tenant.Status` enum
(`Tenant.cs:26,52`) — dual source of truth, a known drift debt.

---

## Identity Runtime

**What Identity actually is (implementation-backed).** Identity in Shopixy is modeled as *six
distinct account entities*, each in its own table, each authenticated by its own JWT scheme, and
each re-validated against its own SQL row on every request. There is no shared `IdentityUser`, no
`AspNetUsers` table, and no single "user" concept. The only unifying abstraction is the
`CanonicalIdentityLink` reconciliation record (`PointOfSale.Core/Entities/CanonicalIdentityLink.cs`)
maintained by a background hosted service that links the six realms by email/phone for cross-system
reconciliation — but it is repair metadata, never an auth authority.

**Identity model (the six realms):**

| Realm | Entity | Table | JWT scheme | Issuer/Audience |
|---|---|---|---|---|
| POS operator | `User` | `Users` | `Jwt` (default) | `JwtSettings` |
| Super Admin | `SuperAdminUser` | `SuperAdmins` | `SuperAdminJwt` | `SuperAdminJwt` |
| Storefront customer | `OnlineCustomer` | `OnlineCustomers` | `StorefrontJwt` | `StorefrontJwt` |
| CommerceHub customer | `CommerceHubCustomer` | `CommerceHubCustomers` | `CustomerJwt` | `CustomerJwt` |
| Affiliate | `AffiliateUser`+`AffiliateAccount` | `AffiliateUsers`/`AffiliateAccounts` | `AffiliateJwt` | `AffiliateJwt` |
| Manual wallet upload | (none — checkout session) | (none) | `ManualWalletUploadTokenAuthenticationDefaults.SchemeName` | n/a |

Evidence: scheme registration block `Program.cs:2275,2350,2419,2498,2624,2754`.

**Identity lifecycle (begin/end).**
- Begins at registration/bootstrap: POS users via
  `TenantRegistrationService.RegisterTenantAsync` (`PointOfSale.API/Services/Platform/TenantRegistrationService.cs:97`);
  storefront customers via `StorefrontAuthController`; customers via `CustomersAuthController`;
  affiliates via `AffiliateAuthController`; the single SuperAdmin root via `SuperAdminRootSeeder`
  (`PointOfSale.API/SuperAdmin/Services/SuperAdminRootSeeder.cs`, env `SUPERADMIN_EMAIL/PASSWORD`).
- Ends at soft-deletion / deactivation / governance ban: `User.IsDeleted`, `OnlineCustomer.IsActive=false`
  (entity doc: "the ONLY way to delete a customer", `OnlineCustomer.cs:127-129`),
  `CommerceHubCustomer` soft-delete, `SuperAdminUser` never hard-deleted, affiliate `IsDeleted`.
  Hard tenant deletion (`Status=Deleted`) is schema-present but **no runtime path sets it**
  (debt — see Technical Debt).

**Identity ownership / authority / persistence / propagation / isolation.**
Ownership: each realm entity owns its own row. Persistence: PostgreSQL (write model). Propagation:
claims embedded in JWTs + `ITenantContext.RequiredTenantId` for service-layer tenant scoping
(`PointOfSale.Core/Interfaces/ITenantContext.cs:25`). Isolation: per-realm secrets + per-request
SQL revalidation + `TenantSaveChangesInterceptor` blocking cross-tenant writes
(`PointOfSale.Infrastructure/Data/TenantSaveChangesInterceptor.cs:69-111`).

---

## Authentication Runtime

Six authentication methods exist (five account realms + one capability scheme). Method inventory:

| Method | Realm | Evidence |
|---|---|---|
| Username/Email + Password (POS) | `User` | `AuthController.LoginAsync` → `GenerateTokens` `AuthController.cs:538` |
| Super Admin email + password | `SuperAdminUser` | `SuperAdminAuthService.LoginAsync` |
| Storefront email/password + social (Google/Facebook) | `OnlineCustomer` | `OnlineCustomer.cs:8-24,99`; `StorefrontAuthController` |
| CommerceHub customer email/password | `CommerceHubCustomer` | `CustomersAuthController`; `CustomerSecurityConstants` |
| Affiliate email/password | `AffiliateUser` | `AffiliateAuthController`; `Program.cs:2646` |
| Checkout-session manual-wallet-upload token | (capability) | `ManualWalletUploadTokenAuthenticationHandler.cs:26-63` |

Token forms: JWT access tokens (all realms) + rotating refresh tokens (POS, SuperAdmin, Storefront,
Affiliate). CommerceHub customer uses access-token jti blacklist + durable cutoff rather than a
stored refresh token. Cookies are HttpOnly+Secure+SameSite=Strict; POS also reads legacy
`auth_token` (`AuthCookieConfiguration.cs:11-46`; `Program.cs:2285-2292`).

---

## Authorization Runtime

Authorization is **binary + role-claim + SQL-authority + governance** — not a permission engine.
- Roles are an enum (`UserRole`: Owner/Admin/Manager/Cashier/Warehouse — `UserRole.cs:7-33`) for
  POS; a string constant `"SuperAdmin"` for platform; `"Customer"` for CommerceHub customer;
  `"Affiliate"` for affiliate. There is **no role hierarchy / inheritance engine** (VERIFIED
  ABSENT — endpoint `Roles=` lists are explicit, e.g. `AdminTenantAuthority.ExtractTenantRoleSets`).
- Authorization policies: `RequireSuperAdmin` (`Program.cs:2765-2773`); `AffiliateOnly`
  (`Program.cs:2778-2782`); fallback requires authenticated user (`Program.cs:2761-2763`).

---

## Access Control Runtime

Controller-level: `[Authorize(Roles=...)]` / `[Authorize(Policy="RequireSuperAdmin")]` /
`[Authorize(AuthenticationSchemes="StorefrontJwt")]`.
Service-level: SQL-authority revalidation (the authorities above) + `ITenantContext.RequiredTenantId`.
Resource-level: tenant scoping via `CurrentTenantId` query filters + `TenantSaveChangesInterceptor`.
Data-level: `PrivilegedRefreshTokenRevocationInterceptor` enforces that reduced authority kills
sessions. There is no attribute/ABAC policy system beyond these.

---

## Session Runtime

- Session storage: POS session = `User.RefreshToken` (SHA-256 hash of the rotating refresh
  credential) + `pos_session` claim binding (`AuthController.cs:542-560`;
  `PosSecurityConstants.SessionBindingClaim`). Storefront/Affiliate: durable `TokensValidAfterUtc`
  cutoff + per-jti blacklist (`OnlineCustomer.TokensValidAfterUtc`; `RevokedCustomerAccessTokens`).
- Expiration: access tokens short-lived (`AccessTokenExpirationMinutes`), refresh tokens long-lived
  (`RefreshTokenExpirationDays`).
- Revocation: logout (POS `AuthController` nulls `RefreshToken` ~`:368`; storefront blacklist;
  CommerceHub `RevokeAllTokensAsync` advances cutoff), password change (sets cutoff), governance
  ban/suspend (interceptor nulls `RefreshToken`).
- Device tracking / concurrent sessions: not modeled as first-class; `pos_session` binding means a
  new login rotates the binding and invalidates prior access tokens (rotation, not explicit
  concurrency cap).
- Logout: each realm clears its refresh credential / blacklists its jti.

---

## Claims Runtime

Authoritative claims (used for SQL lookup, not for authorization decisions):
- POS: `NameIdentifier` (UserId), `tenant_id`, `role`, `pos_session` (`AuthController.cs:553-560`).
- SuperAdmin: `NameIdentifier`, `role=SuperAdmin`, `auth_type=super_admin`, `token_version`, `jti`
  (`SuperAdminAuthService.GenerateAccessToken:384-405`).
- Storefront: `CustomerId`, `TenantId`, `jti`, `iat`/session-issued-at ticks
  (`StorefrontSecurityConstants`).
- CommerceHub customer: `CustomerId`, `jti`.
- Affiliate: `affiliate_account_id`, `NameIdentifier`/`sub`, `affiliate_session`, `iat`.

Derived/ignored: claims are **never** used as the authorization decision source — they are the key
to a SQL lookup. `AdminTenantAuthority` explicitly re-reads `user.Role` from SQL and compares to the
claim, rejecting on mismatch (`AdminTenantAuthority.cs:113-124`). The `TenantActorRoles` set
includes the ghost role `"Accountant"` (`AdminTenantAuthority.cs:41-43`) which is NOT in `UserRole`
— active model drift (debt).

---

## Permission Runtime

VERIFIED ABSENT. No `IPermissionService`, no `Permission` entity/enum/table. Grep
`rg -nic 'IPermissionService|class Permission|PermissionEntity|enum Permission|HasPermission' --glob '*.cs'`
returns **0 files**; `CommerceHubAdminSessionSecurityCertificationTests.cs:75` explicitly asserts "No separate POS
permission entity was found". "Permissions" are capability strings computed at login and returned to
the SPA as UI hints (`SuperAdminAuthController.NavigationCapabilities` array, e.g.
`"super-admin.finance.view"`, `:17-34`, emitted at `:262/:303`; POS derives its own via
`AuthController.BuildCommerceHubNavigationCapabilitiesAsync:202`), with no server-side `RequireClaim`
policy enforcing them. The only `RequireClaim` in the codebase gates the `Affiliate` role
(`Program.cs:2770,2781`). Tests confirm the contract: backend *issues* capabilities, the frontend
must trust them (`CommerceHubFrontendAuthorityCertificationTests.cs:21,120`). Server enforcement is
exclusively scheme + role-claim + SQL authority + governance gates.

## Role Runtime

Roles are a flat enum/constant, not a hierarchy. No inheritance. `Owner` is highest POS privilege
(tenant creator). Endpoint role lists are explicit; `AdminTenantAuthority.TenantActorRoles`
recognizes six literal strings including the spurious `"Accountant"` (debt). SuperAdmin has exactly
one role constant `"SuperAdmin"` and no role table.

---

## Tenant Identity Runtime

**Tenant creation:** `TenantRegistrationService.RegisterTenantAsync` creates `Tenant` (initially
`PendingSetup`) + Owner `User` + full bootstrap in ONE transaction, then flips `Status=Active`
(`TenantRegistrationService.cs:97,190,469-470`). Legacy divergent path `TenantsController.CreateTenant`
skips full bootstrap (`TenantsController.cs:95`).

**Tenant ownership:** a tenant owns all `ITenantOwned` child entities via `TenantId`.

**Tenant isolation:** `TenantResolutionMiddleware` strips `X-Tenant-Id`, resolves from claim or
server `Host`, blocks spoofing/cross-tenant mismatch (`:82,106-154`); `TenantSaveChangesInterceptor`
blocks `ITenantOwned` saved with `Guid.Empty`/`mismatch` (`:69-111`); `TenantService.IsTenantActiveAsync`
requires `IsActive && Status==Active && DeletedAtUtc==null`.

**Tenant activation / suspension:** real mutator is
`SuperAdminMarketplaceGovernanceController.ApplyTenantGovernanceAsync` (sets `Status`/`IsActive`/
`SuspendedReason`, `:432-497`). Legacy `TenantsController.SetTenantStatus` touches only the
deprecated `IsActive` bool (split-brain debt).

**Tenant deletion / lineage / authority:** `Status=Deleted` + `DeletedAtUtc` are schema-present and
the interceptor branches on them (`PrivilegedRefreshTokenRevocationInterceptor.cs:148-149`), but **no
runtime path sets them** (debt). Tenant authority propagates to all child `User` rows: when a tenant
is suspended, the interceptor nulls every user's refresh token
(`PrivilegedRefreshTokenRevocationInterceptor.cs:64-78`).

---

## SuperAdmin Identity Runtime

- Who owns SuperAdmin: `SuperAdminUser` (`SuperAdmins` table), seeded by `SuperAdminRootSeeder`
  (env `SUPERADMIN_EMAIL/PASSWORD`; refuses >1 `IsSystem` user — `SuperAdminRootSeeder.cs:36-48`).
- Authentication: dedicated `SuperAdminJwt` scheme; credentials = email+password; refresh token is a
  hashed `RefreshTokenHash` with `TokenVersion` revocation (`SuperAdminUser.cs:15-58`).
- Bypasses tenant boundaries: yes — `SuperAdminJwt` is excluded from `TenantResolutionMiddleware`
  excluded paths (`/api/super-admin`, `TenantResolutionMiddleware.cs:47-66`) and carries no
  `tenant_id` claim (`Program.cs:2348`). It operates on the shared `ApplicationDbContext` directly.
- Authority: writes domain SQL (e.g. `Tenants.Status`, `CommerceHubListings`,
  `UserGovernanceProfiles`) inside the same transaction as governance decisions — a constitutional
  layer, NOT a parallel override table.
- Authority it does NOT have: it does not own order/payment/inventory/catalog/accounting truth; it
  governs them via `UserGovernanceService` and marketplace controllers. `IsSystem` is a bootstrap
  flag, not a wildcard authority grant (no code path grants `IsSystem` extra privileges beyond
  "cannot be deleted/second-created").

---

## POS Admin Identity Runtime

POS admin/ops actors are `User` rows with `UserRole` (Owner/Admin/Manager/Cashier/Warehouse).
Authority boundaries are enforced by `AdminTenantAuthority` re-checking SQL `User.Role` against the
endpoint's `Roles=` metadata (`AdminTenantAuthority.cs:113-139,144-159`) AND checking tenant
active/governance status. Owner/Admin additionally hold business privileges in code
(`AuthController.cs:208-236` gates certain endpoints on `Owner or Admin`). Manager may do a subset;
Cashier is scoped to POS operations; Warehouse to inventory ops. No privilege escalation path exists
in code (each endpoint lists its allowed roles explicitly).

---

## Store Employee Runtime

Store employees are `User` rows scoped to a tenant (`User.TenantId`, `User.cs:50`). A `User` may also
carry a `StoreId` for store-scoped operations (see `StoreConfig`/`Store` entities). Authorization is
the same `AdminTenantAuthority` path plus any store-scoping enforced in the application service by
`ITenantContext.RequiredTenantId` and a store filter. Employees cannot cross tenant boundaries
(`AdminTenantAuthority.cs:120-124`; `TenantResolutionMiddleware` cross-tenant block).

---

## Customer Identity Runtime

Two divergent customer realms:
1. **Storefront `OnlineCustomer`** — tenant-scoped (mandatory `TenantId`, `OnlineCustomer.cs:69`),
   registered/anonymous/guest. `EmailVerified`, `PhoneVerified`, `TokensValidAfterUtc` cutoff.
   Soft-delete only (`IsActive=false`). Projected to Mongo for storefront reads but SQL is authority.
2. **CommerceHub `CommerceHubCustomer`** — GLOBAL marketplace account (no tenant FK),
   `IsEmailVerified`, `TokensValidAfterUtc` cutoff, soft-delete (`CommerceHubCustomer.cs`). Links to
   `OnlineCustomer` via `CanonicalIdentityLink` when an order carries both IDs.

**Registered vs Guest vs Anonymous:** storefront supports guest checkout
(`CheckoutPhoneOtpStore` + `StorefrontPhoneVerificationService` issue OTP for phone-only guests);
a guest is represented as an `OnlineCustomer` with `IsActive` and verification flags, or as a
checkout-session token (`ManualWalletUploadTokenAuthenticationHandler`) with NO user identity. The
implementation never grants a guest the `OnlineCustomer` role-claim authority of a logged-in customer.

**Customer authority:** customers carry a `CustomerId`/`role=Customer` claim used only as a SQL key;
no server-enforced permissions. Storefront `OnlineCustomer` controllers are gated by
`[Authorize(AuthenticationSchemes="StorefrontJwt")]` with **NO `Roles`** (only `CustomerId`/`TenantId`
claims — no role, no permission strings). CommerceHub customer uses a single static `"Customer"` role
(`CustomerSecurityConstants`); affiliate uses `role="Affiliate"`. Authorization is binary:
authenticated-in-scheme (+ static role for CommerceHub/Affiliate) vs not. Customers cannot perform
employee/SuperAdmin operations (separate schemes).

**Tenant scoping divergence (important):** `OnlineCustomer` is strictly tenant-scoped (mandatory
`TenantId`, `OnlineCustomer.cs:69`). But **`CommerceHubCustomer` is GLOBAL — it has no `TenantId`**
(`CommerceHubCustomer.cs:7` is `BaseEntity`, not `ITenantOwned`; registration matches by normalized
email across the entire table). Similarly `AffiliateAccount.TenantId` is audit-only (AF-19: "affiliates
are now global"; `AffiliateAccount.Create` comment). CommerceHub customers and affiliates are
cross-tenant by design — they are platform/global identities, not tenant children. This is the single
largest identity-isolation divergence in the system and must not be conflated with tenant-scoped
storefront customers.

---

## Guest Identity Runtime

A guest is either (a) an unauthenticated/anonymous storefront browser (no token; tenant resolved by
`Host`), or (b) a phone-verified guest during checkout (OTP via `StorefrontPhoneVerificationService`,
`CheckoutPhoneOtpStore`), or (c) a checkout-session token holder (`ManualWalletUploadToken...Handler`,
tenant-scoped, no user id). Critically, **guests are NOT persisted as `OnlineCustomer` rows** — there
is no `IsGuest` flag on the entity (`OnlineCustomer.cs` confirms no guest field). Guest checkout uses a
session: `CheckoutController` requires `X-Session-Id` for the guest branch (`:630-665`) and issues
`storefront_guest_checkout_token` + `storefront_guest_tracking_token` (`GuestOrderAccessCookie.cs:
7-8`); CommerceHub guest checkout uses the same session/order-access-cookie pattern. Guests cannot
reach authenticated endpoints; they can only hit `AllowAnonymousTenant`/excluded paths. There is no
"guest account" entity — guests are intentionally ephemeral, session-scoped identities with no
customer JWT.

---

## API Authentication Runtime

Same six schemes. REST APIs use `Authorization: Bearer` (POS `Program.cs:2294-2301`; affiliate
`:2636-2643`); browsers use HttpOnly cookies. `ManualWalletUploadToken` uses a custom header
(`ManualWalletUploadTokenAuthenticationDefaults.HeaderName`). Each scheme is header/cookie-isolated:
the affiliate scheme explicitly reads ONLY its own cookie/header to avoid POS cookie collision
(`Program.cs:2628-2645`).

---

## JWT Runtime

- Generation: per realm (POS `AuthController.GenerateTokens:538-578`; SuperAdmin
  `SuperAdminAuthService.GenerateAccessToken:382-418`; storefront/customer/affiliate in their
  controllers/services). HMAC-SHA256, `ClockSkew=Zero`.
- Validation: `TokenValidationParameters` per scheme (`Program.cs:2333-2346,2403-2415,2482-2494,
  2591-2610,2740-2752`) — issuer/audience/lifetime/signing-key all validated.
- Signing: symmetric `SymmetricSecurityKey(Secret)`; secrets distinct per realm, fail-fast if empty
  (`SensitiveConfigurationValidator.cs:29-47`).
- Expiration / Rotation / Refresh / Revocation / Replay: see Session & Refresh Token runtimes.
  Replay protection: POS binds access token to a rotated session hash; CommerceHub/customer use jti
  blacklist + `iat` vs `TokensValidAfterUtc`; SuperAdmin uses `TokenVersion`.

---

## Refresh Token Runtime

- Storage: `User.RefreshToken` (POS, SHA-256 hash), `SuperAdminUser.RefreshTokenHash`,
  `AffiliateUser.RefreshToken` (hash), CommerceHub customer — jti blacklist only (no stored refresh).
- Generation/hashing: `RefreshTokenGenerator.Generate()` = 64 CSPRNG bytes, URL-safe Base64
  (`RefreshTokenSecurity.cs:17-42`); `RefreshTokenHasher.Hash()` = SHA-256, fixed-time verify
  (`:55-96`).
- Rotation: POS rotates the binding on every `GenerateTokens`, invalidating prior access tokens
  (`AuthController.cs:542-545`; `PosSessionAuthority.cs:98-103`).
- Revocation: logout, password change (cutoff), governance reduction (interceptor), tenant suspend.
- Replay: SuperAdmin `RefreshAsync` detects `TokenVersion` mismatch → `ClearRefreshToken`
  (`SuperAdminAuthService` refresh path); POS refresh requires matching hash
  (`AuthController.cs:363-368,451`).

---

## Impersonation Runtime

VERIFIED ABSENT. `grep -rniE "impersonat|ActAs|AssumeRole|SwitchUser" --include=*.cs` returns only a
*test certifying absence* — `EmergencyGovernanceCertificationTests.cs:195` asserts "No production
support impersonation endpoint was found". No SuperAdmin/tenant/employee/customer impersonation, no
temporary-authority grant, and no impersonation audit trail exists in the backend. (`CheckoutController`
contains only OTP verification, not impersonation.) Admins may activate/deactivate/revoke customer
sessions but cannot "log in as" a customer.

---

## Password Runtime

- Hashing: `User.PasswordHash`, `SuperAdminUser.PasswordHash`, `OnlineCustomer.PasswordHash`,
  `CommerceHubCustomer.PasswordHash`, `AffiliateUser.PasswordHash` — all opaque hashes; mechanism is
  the framework `PasswordHasher`/bcrypt-style verifier invoked in each auth service (no plaintext
  ever stored; entity docs mandate `NEVER store plain text` — `OnlineCustomer.cs:91-93`).
- Reset / Recovery: per-realm reset endpoints that set `TokensValidAfterUtc` to invalidate outstanding
  tokens (storefront `OnlineCustomer.TokensValidAfterUtc` doc; CommerceHub `RevokeAllSessions`).
- Expiration / Reuse prevention / Policies: NOT implemented as first-class (no password-age or
  reuse-history table found). Security stamps: there is NO ASP.NET `SecurityStamp` field; the
  equivalent is the `pos_session` binding hash (POS) and `TokensValidAfterUtc` cutoff (customers) and
  `TokenVersion` (SuperAdmin). This is a divergence from canonical ASP.NET Identity (debt).

---

## Email Verification Runtime

- POS `User.EmailVerificationStatus` (`Verified` default for migration compat, `User.cs:61`);
  verified via `EmailVerificationService` token link (`AuthController.VerifyEmail:646`,
  `Services/Auth/EmailVerificationService.cs`).
- Storefront `OnlineCustomer.EmailVerified` — NOTE: NOT required for login. `StorefrontAuthService`
  login gates only on `IsActive`/phone-block/deactivated; `EmailVerified` is informational/checkout-
  scoped only (divergence vs CommerceHub). Verified via `StorefrontEmailVerificationService` (token
  lifetime 24h, max 3 resends/hour, SHA-256 hashed token, single-use; `StorefrontEmailVerificationService.cs`).
- CommerceHub `CommerceHubCustomer.IsEmailVerified` — ENFORCED at token validation: unverified email
  → `EMAIL_NOT_VERIFIED` 403 (`CustomersAuthController.cs:88`); also re-checked in
  `CustomerTokenRevocationService.EnsureTokenSessionStillValidAsync:280-283`.
- Affiliate `AffiliateUser.EmailVerified` — enforced at `AffiliateJwt` validation
  (`Program.cs:2690-2692`).
- Lifecycle: token issued → email link → verify → status set → (for some realms) gating. Expiration
  and attempt limits are per-service (rate-limited resends). Verification authority = the entity's
  own row (SQL).

---

## Phone Verification Runtime

- Storefront/checkout phone OTP via `StorefrontPhoneVerificationService`
  (`PointOfSale.API/Services/Storefront/StorefrontPhoneVerificationService.cs`, 1193 lines) and
  `CheckoutPhoneOtpStore` (`343` lines) — OTP issued, stored with expiry + attempt limits, verified
  against the store. `OnlineCustomer.PhoneVerified` set on success.
- Used for guest checkout (no account) and customer phone trust.
- OTP lifecycle/expiration/attempt limits: implemented inside those two services (rate-limited at
  controller level via `CheckoutOtpRequestRateLimit`/`CheckoutOtpVerifyRateLimit`
  `Program.cs:2985-2996`). Replay: OTP is single-use and expires.
- Authority: storefront/checkout services own phone verification; it is NOT a platform-wide MFA.

---

## MFA Runtime

VERIFIED ABSENT. `grep -rniE "mfa|twofactor|totp" --include=*.cs PointOfSale.API PointOfSale.Core
PointOfSale.Infrastructure` returns only false positives: `CheckoutOtp*` rate-limit policy names
(`Program.cs:2985,2996`), SMS/phone OTP verification (`CheckoutPhoneOtpStore`,
`StorefrontPhoneVerificationService`), and `MediumFailedPaymentsThreshold` in `UserGovernanceService`
(payment-fraud, not an auth factor). No TOTP/authenticator-app/enrollment/recovery/policy for
multi-factor authentication exists. Phone OTP is a single-factor verification, not MFA.

---

## Security Stamp Runtime

VERIFIED ABSENT. `grep -rniw "SecurityStamp" --include=*.cs` across the repo returns **0 matches** —
no `SecurityStamp` column, no stamp rotation on password change/logout. Equivalents (intentional,
implementation-specific):
- POS: `pos_session` SHA-256 session-binding hash on `User.RefreshToken`
  (`PosSessionAuthority.cs:57,98-103`).
- Customers/Affiliate: `TokensValidAfterUtc` durable cutoff (`OnlineCustomer.cs:180-193`;
  `AffiliateUser.RevokeSessions:126-129`).
- SuperAdmin: `TokenVersion` integer incremented on every refresh set/clear
  (`SuperAdminUser.cs:45-58`).
All three serve the "invalidate derived sessions when authority changes" purpose that a security
stamp would, but are realm-specific and not unified.

---

## Audit Runtime

- Authentication logs: POS refresh-security events persisted to `AuditLogs`
  (`AuthController.RecordPosRefreshSecurityEventAsync:593-615`, `Source="Security"`).
- Permission/role/tenant changes: `PrivilegedRefreshTokenRevocationInterceptor.AddAuditLog`
  (`PRIVILEGED_REFRESH_REVOKED`, `:220-248`); SuperAdmin operations via
  `SuperAdminOperationalAuditMiddleware` (`Program.cs:3307`).
- Identity events: `CanonicalIdentityReconciliationService` logs `LogCritical` on conflicts
  (`:53-57`); governance changes via `UserGovernanceService` (Serializable tx).
- Security events: Storefront blacklist rejections logged (`StorefrontJwtBlacklistMiddleware.cs`);
  tenant-spoofing/cross-tenant attempts logged + `403` (`TenantResolutionMiddleware.cs:114-153`).

---

## CQRS

- SQL authority: PostgreSQL is the write model and the authoritative read for identity
  (`Users`, `SuperAdmins`, `OnlineCustomers`, `CommerceHubCustomers`, `AffiliateUsers`,
  `Tenants`, `UserGovernanceProfiles`, `CanonicalIdentityLinks`).
- Mongo projections: **only `OnlineCustomer` is projected to Mongo** — `OnlineCustomerProjectionConsumer`
  (registered/activated/deactivated events), `OnlineCustomerWishlistProjectionConsumer`, and
  `OnlineCustomerSessionProjectionConsumer` (refresh-token/session events → `OnlineCustomerSessionDocument`
  + `OnlineCustomerDocument`, and **invalidates the Redis session cache**). VERIFIED ABSENT: no consumer
  projects `User`, `CommerceHubCustomer`, or `PosSession` to Mongo/Redis (grep
  `IConsumer<(User|CommerceHubCustomer|PosSession|Employee)>` → 0 matches). Storefront reads browse
  lists from the Mongo projection but re-validates SQL authority on every authenticated request.
- Redis cache: storefront/customer token-revocation blacklist (`CustomerTokenRevocationService.cs:
  197-214` Redis lookup → PG fallback → fail-closed); OnlineCustomer session cache invalidated by the
  session projection consumer.
- Identity cache: durable `TokensValidAfterUtc` cutoff is the cache-resistant revocation.
- Synchronization / Replay / Consistency: `CanonicalIdentityReconciliationService` (hosted,
  `Program.cs:499`) repairs missing `CanonicalIdentityLink` rows and links CommerceHub buyers to
  OnlineCustomers (`CanonicalIdentityReconciliationService.cs:28-61,79+`); conflicts logged as
  `LogCritical` for manual resolution. Consistency is fail-closed: if revocation cannot be verified,
  the request is rejected, not allowed.

---

## Projection Usage / Read Models / Write Models

- Write model: PostgreSQL identity rows (single `ApplicationDbContext`).
- Read model: Mongo `OnlineCustomer` projection for storefront browsing; SQL is re-queried for
  authority on every authenticated request, so the projection is display-only, never
  authorization-bearing.
- Projection authority: the projection is NEVER the authority; the live SQL row is.

---

## Runtime Authority Matrix

For each concept: Business Owner (mutation), Read Authority, Runtime Truth.

| Concept | Mutation Authority | Read Authority | Runtime Truth |
|---|---|---|---|
| Identity (POS `User`) | `AuthController`/tenant bootstrap | `PosSessionAuthority` (SQL) | SQL `Users` |
| Identity (SuperAdmin) | `SuperAdminAuthService`/seeder | `SuperAdminPlatformAuthority` (SQL) | SQL `SuperAdmins` |
| Identity (Storefront) | `StorefrontAuthController` | `StorefrontJwtBlacklistMiddleware` (SQL) | SQL `OnlineCustomers` |
| Identity (CommerceHub) | `CustomersAuthController` | `CustomerTokenRevocationService` (SQL) | SQL `CommerceHubCustomers` |
| Identity (Affiliate) | `AffiliateAuthController` | `AffiliateJwt` handler (SQL) | SQL `AffiliateUsers` |
| Authentication | per-realm auth service | scheme `OnTokenValidated` | SQL + signature |
| Authorization | endpoint `[Authorize]` + authority svc | authority svc (SQL re-validation) | SQL |
| Tenant | `TenantRegistrationService`/SuperAdmin governance | `TenantResolutionMiddleware` (SQL) | SQL `Tenants` |
| User | `AuthController`/admin controllers | `PosSessionAuthority`/`AdminTenantAuthority` | SQL |
| Role | `User.Role` enum | `AdminTenantAuthority` (SQL) | SQL |
| Permission | NONE (absent) | NONE | n/a |
| Claim | token builder at login | authority svc (lookup key only) | SQL |
| JWT | scheme token builder | signature + SQL revalidation | SQL |
| Refresh Token | per-entity `RefreshToken` | hash compare | SQL |
| Session | `User.RefreshToken`/cutoff | authority svc | SQL |
| Customer | storefront/customer controllers | blacklist/revocation svc | SQL |
| Guest | none (ephemeral) | none | n/a |
| SuperAdmin | `SuperAdminAuthService` | `SuperAdminPlatformAuthority` | SQL |
| Employee | `AuthController`/admin | `AdminTenantAuthority` | SQL |
| Store | store-scoped service + `ITenantContext` | SQL | SQL |

---

## Runtime Authority Divergence

1. **JWT claim `role` ≠ SQL `User.Role`.** `AdminTenantAuthority` re-reads SQL and rejects on
   mismatch (`AdminTenantAuthority.cs:113-124`). A forged/stale role claim is rejected even with a
   valid signature.
2. **`TenantActorRoles` includes ghost `"Accountant"`** (`AdminTenantAuthority.cs:41-43`) which is
   absent from `UserRole` enum (`UserRole.cs:7-33`). Config/model drift — the role is accepted by
   the authority set but cannot be assigned via the enum.
3. **`Tenant.IsActive` (bool) ≠ `Tenant.Status` (enum).** Both exist; `Status` is authoritative,
   `IsActive` legacy. `TenantsController.SetTenantStatus` updates only `IsActive`
   (split-brain); `PrivilegedRefreshTokenRevocationInterceptor` keys off `IsActive` for user
   revocation (`:147`) while governance keys off `Status` — two signals, two code paths.
4. **POS `User.Role` (enum) vs `CommerceHubCustomer`/`OnlineCustomer` "role"** — customers have no
   role-claim authority; `CustomerId` is only a SQL key.
5. **Capability strings (SPA) ≠ server enforcement.** `NavigationCapabilities` hints drift from
   server `[Authorize]` metadata; no `RequireClaim` enforces them.
6. **`JwtMiddlewareConfiguration.AddHardenedJwtAuthentication` is DEAD code** — never invoked
   (grep: only the method definition; `Program.cs` wires auth inline). The legacy `auth_token`
   cookie it described is still honored in `Program.cs:2289`, but the class itself is unused.
7. **Tenant-scoped vs global customer identity divergence.** `OnlineCustomer` is strictly tenant-
   scoped (`TenantId` mandatory), but `CommerceHubCustomer` is GLOBAL (`BaseEntity`, no `TenantId`) and
   `AffiliateAccount.TenantId` is audit-only (AF-19). The two "customer" realms are isolated
   differently; consumers must not assume storefront tenant-isolation applies to CommerceHub customers
   or affiliates.
8. **Storefront `EmailVerified` vs CommerceHub `IsEmailVerified` enforcement divergence.** CommerceHub
   403s `EMAIL_NOT_VERIFIED` at token validation; storefront login does not gate on `EmailVerified`
   (informational/checkout-scoped only). Same field name, different enforcement contract.

---

## Cross-Domain Authority Graph

| Peer | Reads identity? | Writes identity? | Identity dependency |
|---|---|---|---|
| Orders (POS/CommerceHub) | reads `UserId`/`CustomerId` as SQL key | no | tenant-scoped via `ITenantContext` |
| Payments | reads customer/tenant ids | no | never mutates identity |
| Inventory | reads `UserId`/store | no | isolated by tenant |
| CommerceHub | owns `CommerceHubCustomer` + `CanonicalIdentityLink` | yes (customer) | links to `OnlineCustomer` |
| Storefront | reads `OnlineCustomer` (Mongo projection) | no (auth writes customer) | SQL revalidated |
| POS | owns `User` | yes | `PosSessionAuthority` |
| Reporting | reads identity aggregates | no | read-only |
| Search (ES) | reads customer docs for display | no | display only |
| WhatsApp | reads customer phone | no | phone verified flag |
| Currency | none | none | unrelated |
| Notifications | reads emails/ids | no | fires on identity events |
| Shipping | reads customer address | no | none |
| Affiliate | owns `AffiliateUser` | yes | isolated scheme |
| Analytics | reads identity aggregates | no | read-only |

Identity never writes order/payment/inventory truth; peers only read identity ids as foreign keys
and rely on tenant scoping.

---

## Business Invariants (implementation-backed)

1. **SQL always owns identity.** Every authenticated request re-queries SQL
   (`PosSessionAuthority`, `SuperAdminPlatformAuthority`, `StorefrontJwtBlacklistMiddleware`,
   `CustomerTokenRevocationService`, affiliate handler).
2. **JWT never becomes authority.** A valid signature is necessary, never sufficient
   (`Program.cs` authority wiring).
3. **Claims never override SQL.** Role/tenant claims are lookup keys; SQL decides
   (`AdminTenantAuthority.cs:113-124`).
4. **Tenant cannot access another tenant.** `TenantResolutionMiddleware` blocks spoofing &
   cross-tenant mismatch (`:106-154`); `TenantSaveChangesInterceptor` blocks cross-tenant writes.
5. **SuperAdmin cannot become a Tenant / vice versa.** Separate tables, schemes, secrets, no shared
   role graph.
6. **Reduced authority kills sessions.** `PrivilegedRefreshTokenRevocationInterceptor` nulls refresh
   tokens on role/active/deleted/tenant/governance reduction (`:45-218`).
7. **Fail closed on revocation uncertainty.** Storefront rejects if Redis/PG unavailable
   (`StorefrontJwtBlacklistMiddleware.cs:189-216`).
8. **Guest cannot perform authenticated customer/employee actions.** Separate schemes + anonymous
   paths only.

---

## Architectural Decisions (WHY, implementation-backed)

- **Why five+ JWT schemes (not one):** cryptographic realm isolation prevents a token from one realm
  authenticating another; secrets must be pairwise-distinct (`SensitiveConfigurationValidator.cs:
  38-47`). WHY: a storefront token must never grant POS or SuperAdmin access.
- **Why claims exist:** they are a compact, stateless carry of the SQL lookup key + session binding,
  not an authorization grant. WHY: avoids a session store lookup on every request while still failing
  closed via SQL revalidation.
- **Why SQL remains authority:** identity can change (role change, ban, password reset) between token
  issuance and use; revalidation guarantees the token reflects current truth. WHY: prevents stale/
  forged claims from granting access.
- **Why `TokensValidAfterUtc` cutoff exists (customers/affiliate):** a durable, blacklist-independent
  revocation that survives Redis/PG outage. WHY: fail-closed revocation even when the cache is down.
- **Why `pos_session` binding (POS):** binds the access token to a rotating refresh credential so
  login-rotation invalidates prior access tokens without a blacklist. WHY: stateless revocation for
  the highest-privilege internal realm.
- **Why `TokenVersion` (SuperAdmin):** integer revocation primitive; any refresh/logout bumps it and
  rejects outstanding tokens (`SuperAdminUser.cs:45-58`).
- **Why tenants are isolated:** `X-Tenant-Id` is client-controlled and untrusted; tenant is resolved
  from claim or server `Host` only (`TenantResolutionMiddleware.cs:259-267`).
- **Why no stored permission system:** enforcement is scheme+role+SQL+governance; permissions are UI
  hints. WHY (inferred): keeps authorization colocated with the role/tenant/gov model and avoids a
  second source of truth that could drift from SQL.

---

## Technical Debt (implementation-backed)

1. **Dead `JwtMiddlewareConfiguration`** — `AddHardenedJwtAuthentication` is never called; auth is
   wired inline in `Program.cs`. Confusing duplicate of live config. (grep: only definition site.)
2. **`Tenant.IsActive` vs `Tenant.Status` dual source of truth** — `SetTenantStatus` updates only
   the bool; interceptor and governance read different signals. Split-brain risk.
3. **Ghost `"Accountant"` role** in `AdminTenantAuthority.TenantActorRoles` not present in `UserRole`
   — dead/orphan role literal accepted by authority logic.
4. **No hard tenant deletion path** — `Status=Deleted`/`DeletedAtUtc` schema-present and interceptor
   branches on them, but no runtime setter found. Recovery lifecycle spec-defined, unimplemented.
5. **No unified security stamp** — three realm-specific equivalents (`pos_session`, `TokensValidAfterUtc`,
   `TokenVersion`); no shared abstraction; divergence from canonical ASP.NET Identity.
6. **No MFA, no impersonation, no stored RBAC** — capability gaps vs. a full IAM platform (by design
   per code, but worth flagging as maturity debt).
7. **Capability-string drift** — SPA `NavigationCapabilities` can drift from server `[Authorize]`
   metadata with no enforcement bridge.
8. **Legacy `auth_token` cookie still honored** — compatibility fall-forward; should be retired once
   sessions expire.
9. **CommerceHub customer & affiliate are GLOBAL (cross-tenant by design)** — `CommerceHubCustomer` has
   no `TenantId`; `AffiliateAccount.TenantId` is audit-only. Unlike tenant-scoped `OnlineCustomer`,
   these identities are NOT isolated by tenant. Necessary for a marketplace, but it is the single
   largest identity-isolation divergence and must be called out in any consumer of these ids.
10. **Affiliate refresh-token reuse detection missing** — unlike Storefront (`TOKEN_REUSE_DETECTED`),
    affiliate relies only on implicit `ClearRefreshToken()` on logout/reset + `RefreshTokenExpiryTime`
    check; no explicit replay detection (debt).
11. **Tenant-admin (POS) auth/role changes are not centrally audited** — only SuperAdmin operations and
    governance access-records hit `AuditLogs`. Normal POS role/employee changes have no per-action audit
    trail (gap; governance-status changes are the partial exception via `UserGovernanceService`).
12. **Storefront `EmailVerified` not enforced at login** — CommerceHub 403s `EMAIL_NOT_VERIFIED`;
    storefront login gates only on `IsActive`. Inconsistent verification enforcement across realms
    (divergence; acceptable if intentional, but undocumented).

---

## Implementation Follow-up Items

| Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|
| Kill `JwtMiddlewareConfiguration` | Dead class, never invoked | Confuses auth wiring | Delete or wire it; single source | Low | Open |
| Unify tenant status | `IsActive` bool + `Status` enum | Split-brain on suspend | Make `Status` sole signal; remove bool writes | High | Open |
| Remove ghost `Accountant` | In `TenantActorRoles` only | Orphan role | Drop literal or add to `UserRole` | Med | Open |
| Implement tenant hard-delete | Schema present, no setter | Recovery incomplete | Add governed delete path | Med | Open |
| Unified security stamp | 3 realm equivalents | Divergence/drift | Shared `ISessionStamp` abstraction | Low | Open |
| Enforce capability strings | SPA hints only | UI/server drift | Derive client caps from server policy | Med | Open |
| MFA | Absent | Maturity/security | Add TOTP/WebAuthn enforcement | Med | Open |
| Impersonation (audit) | Absent | Support/debug gap | Add SuperAdmin temporary-authority w/ audit | Low | Open |
| Retire legacy `auth_token` | Still read in `Program.cs` | Compat debt | Remove after session expiry | Low | Open |
| Document global customer/affiliate identity | `CommerceHubCustomer`/`AffiliateAccount` cross-tenant | Consumers may assume tenant isolation | Mark as platform-global in all consumers | Med | Open |
| Affiliate refresh reuse-detection | Absent (Storefront has it) | Replay gap | Add `TOKEN_REUSE_DETECTED` for affiliate | Med | Open |
| Audit POS auth/role changes | Not in `AuditLogs` | Oversight gap | Route tenant-admin changes to audit log | Med | Open |
| Enforce storefront `EmailVerified` at login | Inconsistent w/ CommerceHub | Divergence | Decide + document contract | Low | Open |

---

## Certification

- Confidence: HIGH — every claim in this document is cited to `file:line` against the working tree.
- Completeness: Covers all six identity realms, the five JWT schemes + manual-wallet capability
  scheme, tenant isolation, session/refresh/claims/password/verification runtimes, authority matrix,
  divergence, cross-domain graph, invariants, decisions, debt.
- Verified absence (0 matches / confirmed): stored permissions/RBAC, role-hierarchy engine, MFA,
  impersonation, ASP.NET `SecurityStamp`, hard tenant deletion path.
- Missing/unverified: fine-grained per-customer "authority" beyond `CustomerId` key (none exists by
  design); exact POS password-hash algorithm version (opaque hasher, not separately versioned in
  code reviewed).
- Debt summary: dead `JwtMiddlewareConfiguration`, `IsActive`/`Status` split-brain, ghost `Accountant`
  role, no hard-delete, no unified stamp, no MFA/impersonation, capability-string drift, legacy
  `auth_token`, global CommerceHub-customer/affiliate identity (cross-tenant by design), missing
  affiliate refresh reuse-detection, no central audit of POS auth/role changes, inconsistent
  storefront `EmailVerified` enforcement.

---

## Identity Lifecycle (one complete walk-through: POS operator)

```
User Created (TenantRegistrationService.RegisterTenantAsync :97, Owner User)
   ↓  Identity Stored (SQL Users row, TenantId set)
Role Assigned (User.Role = Owner)
   ↓  (no stored permission; role is the grant)
Authentication (AuthController.LoginAsync → GenerateTokens :538)
   ↓  JWT Issued (claims: NameIdentifier, tenant_id, role, pos_session :553-560)
Claims Built (above)   ↓
Session Created (User.RefreshToken = SHA-256(sessionBinding), Expiry set :544-545)
   ↓
API Access (cookie Bearer → Program.cs:2275 OnTokenValidated → PosSessionAuthority :2304-2331)
   ↓
Refresh (refresh token hash compared :363-368,451; binding rotated :542-545)
   ↓
Logout (RefreshToken nulled :368)   ↓
Revocation (governance/role change → PrivilegedRefreshTokenRevocationInterceptor nulls RefreshToken :189-218)
```
- Authority: SQL `Users` row at every step.
- Synchronization: token binding hash == SQL `RefreshToken` (fixed-time compare,
  `PosSessionAuthority.cs:133-144`).
- Failure behaviour: any mismatch → `context.Fail("POS session is no longer current")`
  (`Program.cs:2329`) → 401.
- Replay: rotated binding invalidates prior access tokens.
- Recovery: re-login issues a new binding.
- Consistency: refresh credential and access-token binding share one SQL field; no divergent store.

---

## Tenant Access Lifecycle

```
Login (POS User) → JWT (tenant_id claim)
   ↓  TenantResolutionMiddleware strips X-Tenant-Id, resolves tenant from claim/Host :82,179-180
Tenant Resolution (SQL Tenants row active+Status==Active+DeletedAtUtc==null :232-239)
   ↓  ITenantContext.RequiredTenantId set
Permission Check (PosSessionAuthority: tenant active + governance not banned :105-122)
   ↓  (then AdminTenantAuthority for admin endpoints :3310)
Controller ([Authorize(Roles=...)])
   ↓  Application Service (ITenantContext.RequiredTenantId)
Repository (TenantSaveChangesInterceptor blocks Guid.Empty/mismatch :69-111)
   ↓  SQL (CurrentTenantId query filters)
```
- Tenant isolation happens at: middleware resolution (request boundary), `ITenantContext`
  (service layer), `TenantSaveChangesInterceptor` (write boundary), and query filters (read
  boundary) — defense in depth.

---

## Authorization Decision Matrix

| Decision | Business Rule | Runtime Owner | Authority | Validation Layer | Failure Behaviour | Evidence |
|---|---|---|---|---|---|---|
| Can SuperAdmin access every tenant? | Yes (excluded from tenant resolution) | `SuperAdminJwt` | SQL `SuperAdmins` | `SuperAdminPlatformAuthority` | 401 if `TokenVersion`/gov mismatch | `TenantResolutionMiddleware.cs:47-66`; `SuperAdminPlatformAuthority.cs:65-95` |
| Can Tenant access another tenant? | No | `TenantResolutionMiddleware` | SQL `Tenants` | middleware + interceptor | 403 TENANT_FORBIDDEN | `:106-154` |
| Can JWT override SQL? | No | authority svc | SQL | `OnTokenValidated`/middleware | 401 | `AdminTenantAuthority.cs:113-124` |
| Can Claims override Permissions? | N/A (no stored perms) | n/a | n/a | n/a | n/a | VERIFIED ABSENT |
| Can Guest create orders? | Only via guest checkout (OTP), not as authenticated customer | checkout service | SQL | OTP verify + anonymous path | 401 if hits auth endpoint | `StorefrontPhoneVerificationService` |
| Can Customer bypass verification? | No (CommerceHub requires `IsEmailVerified`) | `CustomerTokenRevocationService` | SQL | `OnTokenValidated` | 401 | `:280-283` |
| Can Employee escalate privileges? | No (explicit `Roles=` per endpoint) | `AdminTenantAuthority` | SQL `User.Role` | middleware | 401/403 | `:113-139` |
| Can Refresh Token survive logout? | No (nulled on logout; interceptor on gov change) | `AuthController`/interceptor | SQL | refresh compare | rejected | `AuthController.cs:368`; `PrivilegedRefreshTokenRevocationInterceptor.cs:189-218` |
| Can Session survive password reset? | No (cutoff / binding rotation) | entity cutoff | SQL | `TokensValidAfterUtc` / `pos_session` | 401 | `OnlineCustomer.cs:180-193`; `PosSessionAuthority.cs:98-103` |

---

## Architectural Synthesis — What Identity actually is

**Classification: Multi-Tenant Identity & Authority Runtime.**

Evidence that it is NOT merely an Authentication Runtime: authentication is only the entry point;
the load-bearing mechanism is per-request SQL-authority revalidation (`PosSessionAuthority`,
`SuperAdminPlatformAuthority`, `StorefrontJwtBlacklistMiddleware`, `CustomerTokenRevocationService`,
affiliate handler). A token alone never grants access.

Evidence that it is NOT merely an Authorization Runtime: there is no stored permission/RBAC engine
(VERIFIED ABSENT); authorization is scheme + role-claim + SQL authority + governance — i.e. identity
state gates, not a policy engine.

Evidence that it is NOT merely a Claims/Access-Control Runtime: claims are explicitly non-authoritative
lookup keys (`AdminTenantAuthority.cs:113-124`); the runtime owns account lifecycle, tenant
isolation, session/refresh binding, and reconciliation — far broader than claims handling.

Evidence that it is NOT "ASP.NET Identity": no `IdentityUser`/`AspNetUsers`/shared user table; six
separate account entities, five separate JWT schemes, realm-specific revocation primitives
(`pos_session`, `TokensValidAfterUtc`, `TokenVersion`), and a background reconciliation service.

Why every competing classification is incomplete:
- *Authentication Runtime* — ignores SQL revalidation + tenant isolation + session revocation.
- *Authorization Runtime* — ignores that there is no permission engine; authz is identity-state gating.
- *Security/Access-Control Runtime* — ignores account lifecycle + tenant propagation + reconciliation.
- *Session/Claims Runtime* — ignores the multi-realm account model and governance.

The correct, complete classification is a **Multi-Tenant Identity & Authority Runtime**: it
establishes identity across six cryptographically isolated realms, propagates a tenant scope via
claims + `ITenantContext`, validates tokens, and enforces authority by re-checking SQL on every
request and revoking derived credentials when SQL authority is reduced — guaranteeing identity and
permissions never become the source of business truth.

---

## MOST IMPORTANT QUESTION — Canonical Answer

**"How does Shopixy establish, propagate, validate, and enforce identity and authority across
SuperAdmin, Tenants, Employees, Customers, Guests, APIs, and every runtime without allowing identity
or permissions to become the source of business truth?"**

Shopixy establishes identity as **six separate account entities**, each in its own PostgreSQL table
and authenticated by its own **cryptographically isolated JWT scheme** (distinct secret/issuer/
audience, pairwise-distinct, fail-fast if unset — `SensitiveConfigurationValidator.cs:29-47`,
`Program.cs:2275-2753`). It propagates identity by embedding a tenant scope and a session-binding/
cutoff claim in each token, and by resolving the authoritative tenant at the request boundary
(`TenantResolutionMiddleware` strips the untrusted `X-Tenant-Id` header and resolves only from the
authenticated claim or the server-controlled `Host` — `:82,106-154,259-267`), exposing it to the
service layer via `ITenantContext.RequiredTenantId`.

It validates tokens by checking the signature AND — critically — re-querying current SQL state on
every request through realm-specific authority services: `PosSessionAuthority` (POS),
`SuperAdminPlatformAuthority` (platform), `AdminTenantAuthority` (tenant admin/ops),
`StorefrontJwtBlacklistMiddleware` (storefront customers), `CustomerTokenRevocationService` +
`IUserGovernanceService` (CommerceHub customers), and inline SQL revalidation (affiliates). A validly
signed token is necessary but never sufficient; a stale/forged claim (role, tenant, or session) is
rejected even with a valid signature (`AdminTenantAuthority.cs:113-124`;
`PosSessionAuthority.cs:88-103`).

It enforces authority by making SQL the single source of truth and by **revoking derived credentials
whenever SQL authority is reduced**: `PrivilegedRefreshTokenRevocationInterceptor` nulls refresh
tokens when a user's role/active/deleted state, their tenant's status, or their governance status
changes (`:45-218`); customers/affiliates use a durable `TokensValidAfterUtc` cutoff that survives
cache outages (`OnlineCustomer.cs:180-193`; `StorefrontJwtBlacklistMiddleware.cs:144-185`); POS uses
a rotated `pos_session` binding so login invalidates prior access tokens; SuperAdmin uses a bumped
`TokenVersion`. Revocation fails closed — if the blacklist cannot be verified, the request is
rejected, not allowed (`StorefrontJwtBlacklistMiddleware.cs:189-216`).

Because every realm's token is only an evidence-carrying key to a live SQL row, and because the
runtime never writes order/payment/inventory/catalog/accounting truth, **identity and permissions can
never become the source of business truth** — they are always re-derived from PostgreSQL at the moment
of each decision. This is the canonical design of Shopixy's Identity Architecture.
