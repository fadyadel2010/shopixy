# Identity & Access Management — Implementation-Backed Architecture

> Source of truth: implementation only. Every claim below is grounded in the code at the cited
> path. No documentation, markdown specs, or README statements were treated as authoritative.
> Where the code contradicts narrative docs, the code wins.

Status: REVERIFIED AGAINST CODE — HIGH CONFIDENCE (all sections read directly from source).

---

## 0. Executive Summary

Shopixy/PointOfSale runs **five account-backed identity realms**, each with its own entity,
JWT signing secret/issuer/audience, cookie set, refresh-token mechanism, and revocation
strategy. They are intentionally cryptographically isolated — a token minted by one account
realm cannot authenticate another. The implementation also registers narrow non-account token
schemes (for example `ManualWalletUploadTokenAuthenticationHandler`) for checkout/session
workflows; those are capability/session tokens, not sixth user/account realms.

| Realm | Entity | JWT Scheme / Issuer | Audience | Cookie scope | Role claim |
|-------|--------|---------------------|----------|--------------|-----------|
| POS operator | `User` (`PointOfSale.Core/Entities/User.cs`) | `Jwt` / `PointOfSaleAPI` | `PointOfSaleClient` | `access_token`+`refresh_token` (+legacy `auth_token`) | `UserRole` |
| Storefront customer | `OnlineCustomer` (`PointOfSale.Core/Entities/OnlineCustomer.cs`) | `StorefrontJwt` / `StorefrontAPI` | `StorefrontClient` | `storefront_access_token`+`storefront_refresh_token` | (none enforced) |
| CommerceHub customer | `CommerceHubCustomer` (`PointOfSale.Core/Entities/CommerceHubCustomer.cs`) | `CustomerJwt` / `CommerceHubCustomerAPI` | `CommerceHubCustomerClient` | `commercehub_customer_access_token` | `Customer` |
| Affiliate | `AffiliateUser`/`AffiliateAccount` (`PointOfSale.Core/Entities/Affiliate/`) | `AffiliateJwt` / `AffiliateAPI` | `AffiliatePortal` | `affiliate_access_token`+`affiliate_refresh_token` | `Affiliate` |
| Super Admin | `SuperAdminUser` (`PointOfSale.Core/SuperAdmin/Models/SuperAdminUser.cs`) | `SuperAdminJwt` / (config) | (config) | `super_admin_access_token`+`super_admin_refresh_token` | `SuperAdmin` |

The single most important architectural fact: **JWT claims are evidence only; PostgreSQL
current-state rows are the authority.** Runtime re-validation is implemented in several places,
not only the three middleware authorities: POS (`PosSessionAuthority`) runs during JWT
`OnTokenValidated`, tenant admin roles are re-checked by `AdminTenantAuthority`, Super Admin is
re-checked by `SuperAdminPlatformAuthority`, Storefront `OnlineCustomer` access is re-checked by
`StorefrontJwtBlacklistMiddleware`, CommerceHub customer access is re-checked by
`CustomerTokenRevocationService.EnsureTokenSessionStillValidAsync`, and Affiliate access is
re-checked inline in the `AffiliateJwt` `OnTokenValidated` handler. A stale or forged claim is
rejected even if the JWT signature is valid.

---

## 1. Identity Model

### 1.1 Realms and their entities

1. **POS `User`** (`PointOfSale.Core/Entities/User.cs`) — tenant-owned operator.
   `User : BaseEntity, ITenantOwned`. Fields: `Username`, `Email`, `PasswordHash`,
   `FirstName`, `LastName`, `Phone`, `UserRole Role`, `IsActive`, `LastLoginAt`,
   `RefreshToken` (SHA-256 hash, not raw), `RefreshTokenExpiryTime`, `TenantId` (NOT NULL),
   `EmailVerificationStatus`. Registered with `Role = Cashier` default
   (`AuthController.Register`, `PointOfSale.API/Controllers/AuthController.cs:104`).

2. **Storefront `OnlineCustomer`** (`PointOfSale.Core/Entities/OnlineCustomer.cs`) —
   tenant-owned (`ITenantOwned`), `TenantId` NOT NULL, `Guid.Empty` rejected at all layers.
   Supported `AuthProvider` enum: `Local`, `Google`, `Facebook`
   (`PointOfSale.Core/Entities/OnlineCustomer.cs:8`). Has `TokensValidAfterUtc` cutoff
   (password-change / deactivation revocation).

3. **CommerceHub `CommerceHubCustomer`** (`PointOfSale.Core/Entities/CommerceHubCustomer.cs`)
   — **GLOBAL** (no `TenantId`; `: BaseEntity`). Fields: `Email`, `PasswordHash`, `Name`,
   `PhoneNumber`, `TokensValidAfterUtc`, `IsEmailVerified`, `EmailVerifiedAtUtc`.
   Linked to marketplace orders/payouts.

4. **Affiliate** (`AffiliateUser` + `AffiliateAccount`) — **GLOBAL** per AF-19
   (`PointOfSale.API/Controllers/AffiliateAuthController.cs:32` "NO tenant_id claim -
   affiliates are truly global"). `AffiliateUser` carries `PasswordHash`, `EmailVerified`,
   `RefreshToken` hash, `RefreshTokenExpiryTime`, `TokensValidAfterUtc`
   (`PointOfSale.Core/Entities/Affiliate/AffiliateUser.cs:65`).

5. **Super Admin `SuperAdminUser`** (`PointOfSale.Core/SuperAdmin/Models/SuperAdminUser.cs`)
   — platform-global. `RefreshTokenHash`, `RefreshTokenExpiresAtUtc`, `TokenVersion`
   (replay detection), `IsSystem` flag.

### 1.2 Role taxonomy (POS only)

`UserRole` enum (`PointOfSale.Core/Enums/UserRole.cs`): `Owner` (highest), `Admin`,
`Manager`, `Cashier`, `Warehouse`. Default on self-registration = `Cashier`
(`AuthController.cs:104`). Role is stored on the `User` row and re-checked at request time
by `PosSessionAuthority` / `AdminTenantAuthority`.

### 1.3 Relationship between identity types

- A **Tenant** (`PointOfSale.Core/Entities/Tenant.cs`) owns POS `User`s, `OnlineCustomer`s,
  inventory, orders, ledger. Tenant is the unit of isolation.
- A **Vendor** in governance == a **Tenant** (`UserGovernanceService.ResolveSubjectAsync`
  resolves `UserGovernanceRole.Vendor` to the `Tenants` table;
  `PointOfSale.API/Services/Governance/UserGovernanceService.Evaluation.cs:406`).
- An **Owner** `User` is the tenant creator; tenant + owner row together establish owner
  authority. The first owner is created by `TenantRegistrationService`.
- **CommerceHubCustomer** and **OnlineCustomer** are overlapping but not identical customer
  bounded contexts. `OnlineCustomer` is tenant-scoped storefront identity; `CommerceHubCustomer`
  is a global marketplace/customer-account identity. Orders can carry both (`CustomerId` and
  `OnlineCustomerId`), and canonical identity links reconcile the overlap (see §18 Divergence #1).
- **Affiliate** and **SuperAdmin** are global and never tenant-scoped.
- **Anonymous / guest** identities exist for storefront browsing, checkout recovery, and
  CMS preview (see §11).

### 1.4 "Managers / Administrators / Super Administrators / Anonymous / Service accounts"

- **Administrators** = POS `User` with `Role == Admin` (and `Owner`). Authorized by
  `AdminTenantAuthority` against the SQL `Role` column.
- **Super Administrators** = `SuperAdminUser`; authorized by the `SuperAdmin` policy and
  `SuperAdminPlatformAuthorityMiddleware` (required claims + SQL `TokenVersion` + governance
  status). `IsSystem` exists on the model but is not part of the authority check in
  `SuperAdminPlatformAuthority.cs`.
- **Service accounts / System identities** = `SuperAdminUser.IsSystem` flag
  (`UserGovernanceService.Evaluation.cs:445` surfaces `item.IsSystem`); background hosts
  (`CanonicalIdentityReconciliationHostedService`, outbox workers) run with infrastructure
  identity, not a per-request JWT.
- **Anonymous users** = unauthenticated requests on `[AllowAnonymous]` + `[AllowAnonymousTenant]`
  endpoints (guest storefront, recovery tokens, public CMS preview).

---

## 2. Authentication

### 2.1 Login flow (POS — representative; others analogous)

`AuthController.Login` (`AuthController.cs:262`):
1. Looks up `User` with `IgnoreQueryFilters()` keyed by `Username` only (global auth lookup
   before tenant is known — `AuthController.cs:272`).
2. `IPasswordHasher<User>.VerifyHashedPassword` (`AuthController.cs:281`).
3. `IsActive` check (`AuthController.cs:289`); on failure → 401 "Account is disabled".
4. `IUserGovernanceService.EnsureTenantLoginAllowedAsync` gate (`AuthController.cs:296`).
5. `GenerateTokens(user)` → access JWT + rotating refresh token; persist `RefreshToken`
   (hash) + `RefreshTokenExpiryTime` (`AuthController.cs:309`).
6. `AuthCookieConfiguration.SetSessionCookies(Pos, …)` sets HttpOnly+Secure+SameSite=Strict
   cookies (`AuthController.cs:327`).

### 2.2 Password validation / hashing

`Microsoft.AspNetCore.Identity.IPasswordHasher<T>` is registered **separately per entity type**
(`Program.cs:345-348`):
- `PasswordHasher<User>`, `PasswordHasher<SuperAdminUser>`,
  `PasswordHasher<AffiliateUser>`, plus `OnlineCustomer` and `CommerceHubCustomer` hashers.
This is ASP.NET Core Identity's PBKDF2 hasher. Raw passwords are never persisted; only
`PasswordHash` (and, for sessions, a SHA-256 **hash** of the refresh token).

### 2.3 Password reset / forgot password

- POS: `AuthController` does not expose reset directly; storefront/customer/affiliate do.
- Storefront customer: `StorefrontPasswordResetService`, SQL-authoritative hashed single-use
  expiring token (`StorefrontAuthController` → `IStorefrontPasswordResetService`).
- CommerceHub customer: `CommerceHubCustomerAuthService.ForgotPasswordAsync` (privacy: always
  returns success; `CommerceHubCustomerAuthService.cs:343`). Reset/change-password paths advance
  `CommerceHubCustomer.TokensValidAfterUtc` through `RevokeAllSessions`, invalidating older access
  tokens (`CommerceHubCustomerAuthService.cs`, `CustomerTokenRevocationService.cs:254-295`).
- Affiliate: `AffiliateAuthController.reset-password` consumes a hashed
  `AffiliatePasswordResetToken`, then `RevokeSessions(now)`
  (`AffiliateAuthController.cs:472-474`).
- Password strength enforced client/server-side; affiliate requires 8+ chars with upper,
  lower, digit, special (`AffiliateAuthController.cs:103-110`).

### 2.4 Email / phone verification

- POS: `EmailVerificationService.VerifyEmailAsync`, `User.EmailVerificationStatus`
  (`User.cs:61`). Unverified POS users "cannot publish storefront or invite users"
  (`User.cs:56`).
- Storefront: `StorefrontEmailVerification` config; `StorefrontAuthController.verify-email`.
- CommerceHub customer: `CommerceHubCustomer.IsEmailVerified` gate — unverified email blocks
  login (`CommerceHubCustomerAuthService.cs:200`).
- Affiliate: `AffiliateEmailVerificationToken`; login requires `EmailVerified`
  (`AffiliateAuthController.cs:252`).

### 2.5 MFA

No MFA implementation found in the authentication paths reviewed. (Not implemented in IAM.)

### 2.6 Refresh tokens

Four distinct mechanisms (see §18 divergence #5):

- **POS** (`AuthController.GenerateTokens`, `AuthController.cs:538`): refresh token is 64 bytes
  CSPRNG, URL-safe Base64 (`RefreshTokenGenerator`, `PointOfSale.Core/Security/RefreshTokenSecurity.cs:25`).
  Only its **SHA-256 hash** is stored as `User.RefreshToken`; the same hash is embedded as the
  `pos_session` claim (session binding). Refresh rotates: a presented token is re-hashed and
  compared to the stored hash; mismatch → 401 `REFRESH_REPLAY_REJECTED` + audit
  (`AuthController.cs:447-468`).
- **Storefront** (`StorefrontRefreshTokenService`, `PointOfSale.API/Services/Storefront/StorefrontRefreshTokenService.cs`):
  `OnlineCustomerRefreshTokens` rows (hash, `IsRevoked`, expiry, tenant-scoped). Rotation
  issues new token, **reuse detection** (`IsRevoked` hit) revokes ALL tokens + publishes
  `OnlineCustomerTokenReuseDetectedEvent` (`StorefrontRefreshTokenService.cs:385-421`).
- **CommerceHub customer**: no refresh-token table; uses **access-token jti blacklist**
  (`RevokedCustomerAccessTokens`, `CustomerTokenRevocationService`).
- **Affiliate**: `AffiliateUser.RefreshToken` hash + expiry; rotation on refresh
  (`AffiliateAuthController.cs:333`).
- **SuperAdmin**: `SuperAdminUser.RefreshTokenHash` + `TokenVersion`; rotation increments
  version; **token-version mismatch = reuse detected → revoke all**
  (`SuperAdminAuthService.cs:269-306`).

### 2.7 JWT generation / signing / lifetime / revocation

Covered in §7. Logout revokes the SQL session (POS `AuthController.Logout`
`AuthController.cs:348`) and/or blacklists the jti (customer `CustomersAuthController.Logout`
`CustomersAuthController.cs:123`). Forced logout = `RevokeAllTokensAsync` advancing
`TokensValidAfterUtc` cutoff.

---

## 3. Authorization

There is **no stored permission table, permission enum, or permission service**. Authorization
is enforced by a layered scheme:

1. **JWT authentication scheme** — each realm's endpoints are bound to its scheme via
   `[Authorize(AuthenticationSchemes = "…")]` (e.g. `StorefrontJwt`, `CustomerJwt`,
   `AffiliateJwt`) or the `SuperAdmin` policy. This prevents cross-realm token reuse.
2. **Role claim** — coarse role check via `RequireRole` / `[Authorize(Roles=…)]`.
3. **SQL actor/session authority (the real gate)** — the exact location differs by realm. POS,
   CustomerJwt, and AffiliateJwt re-check current SQL state during JWT `OnTokenValidated`;
   Storefront re-checks in blacklist middleware before authorization; tenant-admin and SuperAdmin
   re-check after authorization:
   - `PosSessionAuthority` (`PointOfSale.API/Security/PosSessionAuthority.cs`) — checks
     `User` still exists, `IsActive`, `TenantId` matches, `Role` matches the token claim,
     refresh/session binding current, tenant active, not governance-banned/suspended
     (`PosSessionAuthority.cs:46-124`).
   - `AdminTenantAuthority` (`PointOfSale.API/Security/AdminTenantAuthority.cs`) — extracts
     required role sets from endpoint `[Authorize(Roles=…)]` metadata and re-checks the SQL
     `User.Role` + tenant match + tenant active (`AdminTenantAuthority.cs:52-142`).
   - `SuperAdminPlatformAuthority` (`PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminPlatformAuthorityMiddleware.cs`)
     — re-validates `SuperAdminUser` via `ISuperAdminPlatformAuthority` (token version and
     governance status; not `IsSystem`).
   - `StorefrontJwtBlacklistMiddleware` (`PointOfSale.API/Middleware/StorefrontJwtBlacklistMiddleware.cs`)
     — re-authenticates `StorefrontJwt`, checks jti blacklist and the SQL `OnlineCustomer` row
     (`IsActive`, `TokensValidAfterUtc`) before authorization.
   - `CustomerTokenRevocationService` (`PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs`)
     — invoked from `CustomerJwt` `OnTokenValidated`, checks jti revocation, SQL customer row,
     email verification, `TokensValidAfterUtc`, and governance.
   - `AffiliateJwt` `OnTokenValidated` (`PointOfSale.API/Program.cs:2636-2709`) — checks SQL
     `AffiliateUser` + `AffiliateAccount` state and session hash.
4. **Governance gates** — `IUserGovernanceService` login/operation gates
   (`EnsureTenantLoginAllowedAsync`, `EnsureVendorListingAllowedAsync`,
   `EnsureVendorOrderOperationAllowedAsync`, `EnsureVendorRepaymentAllowedAsync`,
   `EnsureAdminLoginAllowedAsync`) gate login and commercehub operations. Governance status
   (`Banned`/`Suspended`/`FinanciallyLocked`) can block login and lock catalog/orders/payouts
   (`AuthController.BuildCommerceHubVendorLifecycleAuthorityAsync`,
   `AuthController.cs:125-200`).

### 3.1 Policies (Program.cs:2749-2776)

- **Fallback policy**: `RequireAuthenticatedUser()` (all endpoints require auth unless
  `[AllowAnonymous]`).
- **`SuperAdmin` policy**: scheme `SuperAdminJwt` + role `SuperAdmin` + `auth_type` claim.
- **`AffiliateOnly` policy**: scheme `AffiliateJwt` + claim `role=Affiliate`.
- `options.InvokeHandlersAfterFailure = true` (diagnostic, AF-10).

### 3.2 Role hierarchy / inheritance

No runtime role hierarchy engine. `Owner` > `Admin` > `Manager` > `Cashier` > `Warehouse` is
**convention expressed only in capability computations** (e.g. `AuthController.cs:208`
`Owner or Admin` get commercehub.finance.view) and in per-endpoint `Roles=` allow-lists. There
is no transitive "Owner implies Admin" resolver; each endpoint lists the exact roles it allows.

### 3.3 Custom authorization handlers

Authorization is implemented as **middleware** (`AdminTenantAuthorityMiddleware`,
`SuperAdminPlatformAuthorityMiddleware`, `StorefrontJwtBlacklistMiddleware`), not
`IAuthorizationHandler`. The only `IAuthorizationHandler` found is
`SuperAdminAuthorizationMiddlewareResultHandler`
(`PointOfSale.API/SuperAdmin/Infrastructure/`).

### 3.4 "Permissions" / NavigationCapabilities

The closest thing to permissions are **capability strings** computed at login and returned to
the frontend as `NavigationCapabilities` (e.g. `commercehub.finance.view`,
`commercehub.catalog.manage`). These are **client UI hints only** — generated in
`AuthController.BuildCommerceHubNavigationCapabilitiesAsync` (`AuthController.cs:202-245`) and
`CommerceHubCustomerAuthService.MapProfile` (`customer.account.view`, etc.). They are **not
stored, not in a permission table, and not enforced server-side** as a permission system.
Server enforcement is scheme + role + SQL authority only.

---

## 4. Multi-Tenancy

### 4.1 Tenant resolution — `TenantResolutionMiddleware`
(`PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`)

Server-authoritative order:
1. Strip any inbound `X-Tenant-Id` header (never trust client-supplied tenant)
   (`TenantResolutionMiddleware.cs:82`).
2. **Authenticated tenant**: read `tenant_id`/`TenantId`/`tenantId` claim from the principal;
   look up `Tenants` with `IsActive && Status == Active && DeletedAtUtc == null`
   (`TenantResolutionMiddleware.cs:222-240`).
3. **Request tenant**: from the server-controlled `Host` header only (Origin/Referer rejected
   as spoofable) (`TenantResolutionMiddleware.cs:259-267`). Resolves via:
   - `StorefrontBaseUrl` custom-domain match (`ResolveTenantFromStorefrontBaseUrlAsync`,
     `:295`), or
   - slug extracted from `Storefront:UrlTemplate` (e.g. `http://{slug}.localhost:5174`),
     matched to `Tenant.Slug` (`:324-375`).
4. `effectiveTenant = authenticatedTenant ?? requestTenant`.
5. **Cross-tenant protection**:
   - Header spoofing (inbound `X-Tenant-Id` != resolved) → 403 `TENANT_FORBIDDEN`
     (`:106-129`).
   - Authenticated tenant != request tenant → 403 `TENANT_FORBIDDEN` (`:131-154`).
6. If unresolved and endpoint is **not** `[AllowAnonymousTenant]` → 400 `TENANT_REQUIRED`
   (`:156-177`).
7. On success: `tenantContext.SetTenantId(id)` + re-add `X-Tenant-Id` header for legacy
   controllers (`:179-180`).

### 4.2 Excluded paths (bypass tenant resolution)

`/swagger`, `/hangfire`, `/health`, `/api/super-admin`, `/api/tenants`, `/api/admin/finance`,
`/api/platform/register`, `/api/platform/subdomain/check`, `/api/platform/email/check`,
`/hubs/chat`, `/uploads`, `/api/affiliate/auth`, `/api/public/affiliate`, `/api/affiliate`,
`/api/test-auth` (`TenantResolutionMiddleware.cs:47-66`).

### 4.3 Tenant Context — `ITenantContext` / `TenantContext`
(`PointOfSale.Core/Interfaces/ITenantContext.cs`, `PointOfSale.Infrastructure/MultiTenancy/TenantContext.cs`)

Scoped service. `TenantId` (nullable), `HasTenant`, `RequiredTenantId` (throws
`InvalidOperationException` if null). Only `TenantResolutionMiddleware` may call
`SetTenantId`. Repositories/business logic call `RequiredTenantId` for tenant-mandatory ops.

### 4.4 Tenant filters / isolation

EF global query filters + `ITenantOwned` enforce tenant scoping on write. DB interceptor
`TenantSaveChangesInterceptor` blocks saving `ITenantOwned` entities without a valid `TenantId`
(`Program.cs:318, 330`). Auth lookups intentionally use `IgnoreQueryFilters()` for
cross-tenant credential resolution *before* tenant is known, then re-scope.

### 4.5 Guest / anonymous tenant handling

`[AllowAnonymousTenant]` (`PointOfSale.API/Attributes/AllowAnonymousTenantAttribute.cs`) opts an
endpoint out of the 400 `TENANT_REQUIRED` rejection. Used by login/refresh/register/verify-email
endpoints. Guest storefront browsing resolves tenant from Host header only (no auth).

---

## 5. Request Runtime (complete trace)

```
HTTP Request
  │
  ▼
CorrelationIdMiddleware
SuperAdminRequestLoggingMiddleware
UseHttpsRedirection (branch: skipped for health)
Security headers inline middleware (nosniff, DENY frame, API CSP)
uploads/chat deny middleware
UseStaticFiles → UseRouting → UseCors("AllowFrontend")
CookieCsrfProtectionMiddleware (branch: unsafe methods with auth cookies, before authentication)
  │
  ▼  UseAuthentication()  (JwtBearer per scheme; cookie-first for SignalR/REST)
  │     • POS: access_token → legacy auth_token → Authorization header
  │     • Storefront/Customer/Affiliate/SuperAdmin: scheme-specific cookies/headers
  │     • OnTokenValidated: POS/Customer/Affiliate re-check SQL session authority
  │     • TokenValidationParameters: ValidateIssuer/Audience/Lifetime/SigningKey,
  │       ClockSkew = TimeSpan.Zero  (JwtMiddlewareConfiguration.cs:38-49, 77-94)
  ▼
TenantResolutionMiddleware (branch)
  │     • strips X-Tenant-Id, resolves tenant (claim → Host), sets ITenantContext
  │     • 403 on spoofing / cross-tenant; 400 TENANT_REQUIRED if unresolved & not [AllowAnonymousTenant]
  ▼
RequestContextLoggingMiddleware (branch)
GlobalExceptionMiddleware
SuperAdminExceptionHandlingMiddleware
  ▼
StorefrontJwtBlacklistMiddleware (branch — after auth, before authz)
  │     • authenticates "StorefrontJwt" explicitly, checks jti blacklist (Redis→PG)
  │     • also checks OnlineCustomer.IsActive + TokensValidAfterUtc cutoff
  ▼
UseAuthorization()  (fallback policy = authenticated; SuperAdmin/AffiliateOnly policies)
  ▼
SuperAdminPlatformAuthorityMiddleware        (re-validates SuperAdmin SQL + TokenVersion)
SuperAdminOperationalAuditMiddleware
  ▼
AdminTenantAuthorityMiddleware (branch — after authz)
  │     • re-checks SQL User.Role / tenant match / tenant active (POS admin/ops)
  ▼
StorefrontPublicationGuardMiddleware
AffiliateReferralMiddleware (after authz+tenant)
  ▼
RateLimitSubjectResolutionMiddleware (after authz; binds rate-limit subject to identity)
UseRateLimiter (branch, includes server-issued storefront rate-limit session cookie)
  ▼
Controller action → business logic → repository → PostgreSQL
```

Ordering verified from `PointOfSale.API/Program.cs:3219-3343`.

---

## 6. Session Lifecycle

```
Login
  └─ GenerateTokens → access JWT (short) + refresh token (rotating)
       • POS: RefreshToken = SHA-256(refresh); same hash = "pos_session" claim
       • Storefront: OnlineCustomerRefreshTokens row (hash, IsRevoked)
       • Affiliate/SuperAdmin: RefreshToken hash + TokenVersion
  └─ SetSessionCookies (HttpOnly, Secure, SameSite=Strict) + CSRF cookie
        │
Refresh (rotation)
  └─ present refresh → re-hash/lookup → issue new access + new refresh; old invalidated
  └─ Reuse detection:
       • POS: hash mismatch → 401 REFRESH_REPLAY_REJECTED + audit
       • Storefront: IsRevoked hit → revoke ALL + OnlineCustomerTokenReuseDetectedEvent
       • SuperAdmin: TokenVersion mismatch → revoke ALL ("SESSION_REVOKED")
        │
Expiration
  └─ Access token: ClockSkew=0; rejected at validation
  └─ Refresh: RefreshTokenExpiryTime checked per realm
        │
Revocation / Logout / Forced logout
  └─ POS: AuthController.Logout clears User.RefreshToken + cookies
  └─ Customer: CustomersAuthController.Logout → CustomerTokenRevocationService.RevokeTokenAsync(jti)
  └─ Storefront: password change / deactivate → TokensValidAfterUtc = now (all tokens < cutoff invalid)
  └─ RevokeAllSessions: advance TokensValidAfterUtc; blacklist all jtis
  └─ Admin forced logout: governance status (Banned/Suspended) blocks re-login
```

---

## 7. JWT — Claims, Signing, Validation

### 7.1 Five independent JWT configurations (appsettings.json + Program.cs)

| Config key | Issuer | Audience | Access exp | Refresh exp |
|-----------|--------|----------|-----------|------------|
| `Jwt` (POS) | `PointOfSaleAPI` | `PointOfSaleClient` | 60 min | 7 days |
| `SuperAdminJwt` | (config) | (config) | (config) | (config) |
| `StorefrontJwt` | `StorefrontAPI` | `StorefrontClient` | 30 min | 7 days |
| `CustomerJwt` | `CommerceHubCustomerAPI` | `CommerceHubCustomerClient` | 60 min | — (jti blacklist) |
| `AffiliateJwt` | `AffiliateAPI` | `AffiliatePortal` | 60 min | 7 days |

### 7.2 POS claims (`AuthController.GenerateTokens`, `AuthController.cs:551-561`)

`ClaimTypes.NameIdentifier`=UserId, `Name`=username, `Email`, `Role`=UserRole, `firstName`,
`lastName`, `tenant_id`=TenantId, `pos_session`=session-binding hash.

### 7.3 Storefront (OnlineCustomer) claims

Issued via `StorefrontJwt` scheme (`Program.cs:2409`). Includes `CustomerId`, `TenantId`,
`Jti`, `iat`, `session_iat_ticks` (`StorefrontSecurityConstants.SessionIssuedAtTicksClaim`).

### 7.4 CommerceHub customer (CustomerJwt) claims
(`CommerceHubCustomerAuthService.cs:282-292`)

`CustomerId`, `ClaimTypes.NameIdentifier`, `Email`, `Role=Customer`, `sub`, `JwtRegisteredClaimNames.Email`,
`iat` (unix), `session_iat_ticks`, `Jti` (Guid). **No tenant_id in the customer token used by
`CustomersAuthController`** — the storefront customer controller resolves tenant from cookie/header.

### 7.5 Affiliate claims (`AffiliateAuthController.GenerateTokens`)

`sub`=AffiliateUserId, `role=Affiliate`, `affiliate_account_id`. **No `tenant_id`** (global).

### 7.6 SuperAdmin claims (`SuperAdminAuthService.GenerateAccessToken`)

`NameIdentifier`, `Name`, `Email`, `Role=SuperAdmin`, `role=SuperAdmin`, `sub`, `JwtRegisteredClaimNames.Email`,
`Jti`, `iat`, `auth_type` claim, `token_version` claim.

### 7.7 Signing / validation

- Algorithm: `HmacSha256Signature` (HMAC-SHA256) with `SymmetricSecurityKey` from each realm's
  `Secret` (`JwtMiddlewareConfiguration.cs:46-47`, `AuthController.cs:569-571`).
- Validation: `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`,
  `ValidateIssuerSigningKey`, `ClockSkew = TimeSpan.Zero` (no tolerance)
  (`JwtMiddlewareConfiguration.cs:40-48`).
- POS `OnTokenValidated` requires a valid GUID `tenant_id` claim
  (`JwtMiddlewareConfiguration.cs:77-94`).
- Secrets injected via env/user-secrets/gitignored `*.local.json`;
  `SensitiveConfigurationValidator.ValidateOrThrow` at startup (`Program.cs:83`).

---

## 8. Permission Engine

**Does not exist as a stored system.** Verified: no permission table, no permission enum, no
`IPermissionService`. "Permissions" in this codebase are:
- **Capability strings** computed at login time from role + governance (UI hints only).
- **Endpoint role allow-lists** (`[Authorize(Roles="Owner,Admin")]`) re-checked by SQL
  authority.

Origins of "what a user can do": **code** (capability computation) + **DB** (role column,
governance status) + **claims** (evidence). Evaluation = scheme auth + role claim +
`AdminTenantAuthority`/`PosSessionAuthority`/`SuperAdminPlatformAuthority` SQL re-check +
governance gates. Cache: **none** for permissions (governance aggregates are computed per
evaluation, `UserGovernanceService.Evaluation.cs`).

---

## 9. Role Engine

- **Definition**: `UserRole` enum in code (`PointOfSale.Core/Enums/UserRole.cs`). Single source.
- **Storage**: `User.Role` column (PostgreSQL), NOT NULL by convention.
- **Assignment**: `Cashier` on self-registration (`AuthController.cs:104`); promotion by admin
  flows (tenant-scoped). No separate role-assignment table.
- **Evaluation**: at request time, `AdminTenantAuthority` reads the live `User.Role` and
  compares against the endpoint's required role set extracted from `[Authorize]` metadata
  (`AdminTenantAuthority.ExtractTenantRoleSets`, `AdminTenantAuthority.cs:144-159`).
- **Inheritance**: none. Each endpoint enumerates allowed roles explicitly.
- **Role-model consistency**: `AdminTenantAuthority.TenantActorRoles` mirrors the assignable
  `UserRole` enum (`Owner`, `Admin`, `Manager`, `Cashier`, `Warehouse`). CommerceHub finance and
  vendor-payment admin endpoints are limited to real assignable roles (`Owner,Admin`).

---

## 10. Claims Engine

- **Generated by**: each realm's AuthService/Controller (`AuthController.GenerateTokens`,
  `SuperAdminAuthService.GenerateAccessToken`, `CommerceHubCustomerAuthService`,
  `AffiliateAuthController.GenerateTokens`, storefront service).
- **Consumed as evidence only** — never as authority. The authority services re-derive truth
  from SQL:
  - `PosSessionAuthority` re-checks `UserId`, `tenant_id`, `Role`, session binding, tenant
    active, governance status (`PosSessionAuthority.cs:46-124`).
  - `AdminTenantAuthority` re-checks `UserId`, tenant claim == trusted tenant, SQL `Role`,
    tenant active (`AdminTenantAuthority.cs:52-142`).
  - `SuperAdminPlatformAuthority` re-checks `TokenVersion` and governance status from SQL.
  - `StorefrontJwtBlacklistMiddleware` re-checks `OnlineCustomer` SQL state and token cutoff.
  - `CustomerTokenRevocationService` re-checks `CommerceHubCustomer` SQL state, email verification,
    token cutoff, jti revocation, and governance during `CustomerJwt` validation.
  - `AffiliateJwt` `OnTokenValidated` re-checks `AffiliateUser`/`AffiliateAccount` SQL state and
    the current refresh/session hash.
- **Runtime paths that trust claims after validation**: controllers/services frequently extract
  `CustomerId`, `TenantId`, `NameIdentifier`, or `affiliate_account_id` from `User` and pass those
  IDs into SQL queries (for example customer order/profile reads). This is acceptable only because
  the scheme-specific validation/middleware has already revalidated the same actor/session against
  SQL; the claim is an identifier, not standalone authority. Some read-model profile endpoints then
  read Mongo/Redis projections after SQL-backed token validation (`StorefrontOnlineCustomerService`).
- **`tenant_id` claim is server-authoritative** — required by `OnTokenValidated` for POS
  (`JwtMiddlewareConfiguration.cs:77-94`) and re-checked against `ITenantContext`.

---

## 11. Anonymous Runtime

- **Guest storefront browsing**: `[AllowAnonymous]` + `[AllowAnonymousTenant]` controllers
  resolve tenant from Host header only (no auth).
- **Guest checkout / recovery**: `CheckoutSession` recovery/payment/guest-tracking tokens are
  short-lived renewable session-bound tokens (`appsettings.json:197-204`,
  `GuestTrackingTokenTtlMinutes=20`). Not full identities.
- **Anonymous APIs**: affiliate discovery/public, platform register/subdomain/email check,
  CMS public pages, marketplace search (excluded from tenant resolution or `[AllowAnonymousTenant]`).
- **CMS preview**: `CmsPagePreviewTokenService` / `MarketplaceCmsPagePreviewTokenService`
  issue short-lived preview tokens (separate from auth).
- **Anonymous permissions**: none beyond public read; write requires auth + tenant + role.

---

## 12. Identity Persistence (PostgreSQL — authoritative)

| Concern | Table / Column | Notes |
|--------|---------------|-------|
| POS users | `Users` (`User`) | `Role`, `TenantId`, `RefreshToken` (hash), `RefreshTokenExpiryTime`, `EmailVerificationStatus` |
| Storefront customers | `OnlineCustomers` (`OnlineCustomer`) | `TenantId`, `TokensValidAfterUtc`, `EmailVerified` |
| Storefront refresh tokens | `OnlineCustomerRefreshTokens` | hash, `IsRevoked`, expiry, tenant-scoped |
| CommerceHub customers | `CommerceHubCustomers` (`CommerceHubCustomer`) | global, `TokensValidAfterUtc`, `IsEmailVerified` |
| Customer access-token revocations | `RevokedCustomerAccessTokens` | jti + `ExpiresAt` (Redis-cached) |
| Affiliates | `AffiliateUsers`, `AffiliateAccounts` | global, `RefreshToken` hash, `TokensValidAfterUtc` |
| Super admins | `SuperAdmins` | `RefreshTokenHash`, `TokenVersion`, `IsSystem` |
| Governance | `UserGovernanceProfiles` | status (Active/Banned/Suspended/FinanciallyLocked), KYC, marketplace approval |
| Audit | `AuditLogs` | login/logout/refresh-replay events |
| Tenants | `Tenants` (`Tenant`) | `Slug`, `StorefrontBaseUrl`, `Status`, `IsActive`, `DeletedAtUtc` |
| Email verification | `EmailVerificationTokens` (POS), affiliate equivalents | hashed tokens |
| Password reset | `PasswordResetTokens` (storefront/affiliate) | hashed single-use |

Refresh-token **raw material is never persisted** — only SHA-256 hashes
(`RefreshTokenSecurity.RefreshTokenHasher`). Sessions are SQL rows, not a separate session table
for POS (POS session = `User.RefreshToken` hash + binding claim).

---

## 13. Middleware Pipeline (identity/security)

Ordered (verified `Program.cs:3219-3343`):

1. `CorrelationIdMiddleware`
2. `SuperAdminRequestLoggingMiddleware`
3. `UseHttpsRedirection()` branch (`!IsHealthEndpoint`)
4. inline response security headers (`X-Content-Type-Options`, `X-Frame-Options`, API CSP)
5. inline `/uploads/chat` 404 block
6. `UseStaticFiles()`
7. `UseRouting()`
8. `UseCors("AllowFrontend")`
9. `CookieCsrfProtectionMiddleware` branch (`!IsTestAuthEndpoint && !IsHealthEndpoint`) — before auth
10. `UseAuthentication()`
11. `TenantResolutionMiddleware` (branch)
12. `RequestContextLoggingMiddleware` (branch)
13. `GlobalExceptionMiddleware`
14. `SuperAdminExceptionHandlingMiddleware`
15. `StorefrontJwtBlacklistMiddleware` (branch) — storefront revocation, after auth before authz
16. `UseAuthorization()`
17. `SuperAdminPlatformAuthorityMiddleware` — SuperAdmin SQL re-validation
18. `SuperAdminOperationalAuditMiddleware`
19. `AdminTenantAuthorityMiddleware` (branch) — POS admin/ops SQL re-validation, after authz
20. `StorefrontPublicationGuardMiddleware`
21. `AffiliateReferralMiddleware` — after authz + tenant
22. `RateLimitSubjectResolutionMiddleware` — after authz, binds rate-limit subject to identity
23. storefront rate-limit-session cookie helper + `UseRateLimiter()` branch

CSRF: `CookieCsrfProtectionMiddleware` guards unsafe methods only when any auth cookie is present
(`AuthCookieConfiguration.HasAnyAuthenticationCookie`, `IsValidCsrfToken`, fixed-time compare).

---

## 14. Security

- **Password hashing**: ASP.NET Core Identity `PasswordHasher<T>` (PBKDF2), per entity
  (`Program.cs:345-348`).
- **Secret management**: env/user-secrets/gitignored local; `SensitiveConfigurationValidator`
  throws at startup if any of the five JWT secrets are missing/unsafe or if any pair of JWT
  secrets is reused (`SensitiveConfigurationValidator.cs:29-47`, `Program.cs:83`).
- **JWT signing**: HMAC-SHA256, separate secret per realm; `ClockSkew = 0`.
- **Replay protection**: refresh-token rotation + reuse detection; customer jti blacklist;
  SuperAdmin `TokenVersion`.
- **Refresh rotation**: POS, Storefront, Affiliate, SuperAdmin all rotate.
- **Brute-force / rate limiting**: named policies `PublicLoginRateLimit`,
  `PublicRegisterRateLimit`, `PublicRefreshRateLimit`, `AffiliateAuthRateLimit`
  (`EnableRateLimiting` on auth endpoints); `PlatformSecurity` config
  `MaxFailedLoginAttempts=5`, `LoginLockoutMinutes=15`
  (`appsettings.json:286-287`); storefront per-tenant-per-email login limiter
  (`StorefrontAuthController.Login`, `:148`); SuperAdmin Redis-backed limiter
  (`SuperAdminAuthService.LoginAsync`, `:58`).
- **Lockout**: governance + rate limiter + `LoginLockoutMinutes`.
- **Session invalidation**: logout clears cookies + revokes SQL session / blacklists jti /
  advances `TokensValidAfterUtc`.
- **Privilege-escalation protection**: claims are evidence-only; scheme-specific SQL authority
  re-checks current role/tenant/status/session before privileged business logic; `TokenVersion`
  for SuperAdmin; `pos_session` binding for POS; `IsRevoked`/cutoff for storefront; jti blacklist
  + cutoff for CommerceHub customers; SQL refresh/session hash for affiliates.
- **Tenant isolation**: header stripping, Host-only resolution, cross-tenant 403s,
  `TenantSaveChangesInterceptor`, global query filters.

---

## 15. Events

- **POS**: `AuditLogs` rows for `LOGIN`, `LOGOUT`, `REFRESH_TOKEN_REPLAY_DETECTED`
  (`AuthController.RecordPosRefreshSecurityEventAsync`, `AuthController.cs:580-632`).
- **Storefront**: `OnlineCustomerTokenReuseDetectedEvent` published on refresh reuse
  (`StorefrontRefreshTokenService.cs:406`); identity events via event publisher to Mongo
  projection.
- **CommerceHub customer**: `CommerceHubCustomerIdentityEvent` (LoggedOut, AllSessionsRevoked,
  etc.) via `RecordIdentityEventAsync` (`CustomersAuthController.cs:183-188`).
- **SuperAdmin**: `ISuperAdminAuditService.LogActionAsync` for LOGIN/LOGOUT/TOKEN_REFRESH/
  TOKEN_REUSE_DETECTED.
- **Affiliate**: audit records for EMAIL_VERIFIED / PASSWORD_RESET_COMPLETED / refresh replay.
- **Reconciliation**: `CanonicalIdentityReconciliationHostedService` (background) repairs
  cross-system identity conflicts (`PointOfSale.Infrastructure/Identity/CanonicalIdentityReconciliationService.cs`).

---

## 16. Runtime Authority (who owns what)

- **Identity (POS User) write**: `AuthController` (register/login/logout/refresh),
  `TenantRegistrationService` (owner creation). Read: any service via `ITenantContext`.
- **Roles write**: registration default (`Cashier`); admin promotion flows. Read/enforce:
  `AdminTenantAuthority` + `PosSessionAuthority` (SQL `User.Role`).
- **Permissions**: none stored; computed in code at login. "Owner" = `AuthController` +
  governance.
- **Claims**: generated by each AuthService; consumed as evidence.
- **JWT**: minted by realm AuthServices; validated by per-scheme `JwtBearer`.
- **Refresh tokens**: owned per realm (POS `User.RefreshToken`, storefront
  `OnlineCustomerRefreshTokens`, affiliate `AffiliateUser.RefreshToken`, superadmin
  `SuperAdminUser.RefreshTokenHash`).
- **Tenant resolution**: `TenantResolutionMiddleware` (single owner). Tenant authority:
  `TenantLineageAuthority.IsActiveTenantAsync` + `AdminTenantAuthority`.
- **Session state**: per-realm session authority (`PosSessionAuthority`,
  `CustomerTokenRevocationService`, `StorefrontRefreshTokenService`,
  `SuperAdminAuthService`) + governance (`UserGovernanceService`).

---

## 17. Runtime Authority Matrix

| Concern | Write Authority | Runtime Read Authority | Verified |
|---------|----------------|------------------------|----------|
| Identity (POS User) | `AuthController`, `TenantRegistrationService` | `AdminTenantAuthority`, `PosSessionAuthority` | Yes |
| Roles | Registration (default Cashier) + admin promotion | `User.Role` re-checked by `AdminTenantAuthority`/`PosSessionAuthority` | Yes |
| Permissions | (none — computed at login in code) | Capability strings returned to client; not enforced server-side | Yes |
| Claims | Realm AuthServices | Consumed as evidence; re-validated by SQL authority | Yes |
| JWT | Realm AuthServices (realm secret) | Per-scheme `JwtBearer` validation | Yes |
| Refresh Tokens | Realm services (hash stored) | Realm session/revocation services | Yes |
| Tenant | `TenantRegistrationService` | `TenantResolutionMiddleware`, `TenantLineageAuthority`, `AdminTenantAuthority` | Yes |
| Session | Realm session authorities + governance | `PosSessionAuthority` / `CustomerTokenRevocationService` / `StorefrontRefreshTokenService` | Yes |
| Governance (bans/locks) | `UserGovernanceService` | Login gates + authority middleware | Yes |
| SuperAdmin | `SuperAdminAuthService`, `SuperAdminUsersController` | `SuperAdminPlatformAuthority` + `TokenVersion` | Yes |

---

## 18. Runtime Authority Divergence (MANDATORY)

1. **Overlapping customer identities, not a proven simple duplicate.** `OnlineCustomer`
   (tenant-scoped, `StorefrontJwt`, refresh tokens + blacklist) and `CommerceHubCustomer`
   (global, `CustomerJwt`, jti blacklist) are separate bounded contexts: tenant storefront
   account vs global marketplace/customer account. They overlap when orders contain both
   `CustomerId` and `OnlineCustomerId`, and `CanonicalIdentityReconciliationService` links those
   subjects into one canonical identity. The implementation proves overlap and reconciliation;
   it does not prove they are merely historical duplicates. (`OnlineCustomer.cs` vs
   `CommerceHubCustomer.cs`; `CheckoutEvents.cs`; `CanonicalIdentityReconciliationService.cs:340-425`.)
2. **Assignable role model is the tenant role boundary.** `AdminTenantAuthority.TenantActorRoles`
   contains only roles present in the `UserRole` enum, and CommerceHub finance/payment endpoint
   metadata declares only assignable roles (`Owner,Admin`). A regression test freezes this
   consistency so a future role extension must update the enum, assignment paths, endpoint
   metadata, authority discovery, and documentation together.
3. **"Vendor" = Tenant in governance but Operator = User.** `UserGovernanceRole.Vendor`
   resolves to the `Tenants` table (`UserGovernanceService.Evaluation.cs:406`), while POS
   operators are `User`s. A tenant is simultaneously the隔离 boundary and a governed "vendor"
   identity — two notions of vendor coexist.
4. **Claims vs SQL authority.** JWT `role`/`tenant_id` claims are treated as evidence only and
   are re-derived from SQL on every privileged request. This is intentional fail-closed design,
   but it means the JWT is not self-authoritative — a divergence between token authority and
   database authority by design.
5. **Four different refresh/revocation mechanisms.** POS (session-binding hash + rotation),
   Storefront (`OnlineCustomerRefreshTokens.IsRevoked` reuse detection), CommerceHub customer
   (`RevokedCustomerAccessTokens` jti blacklist, Redis+PG), SuperAdmin (`TokenVersion` rotation).
   No shared revocation abstraction.
6. **Capability strings are non-authoritative.** `NavigationCapabilities` /
   `VendorLifecycleAuthority` computed at login are sent to the client as hints but are NOT the
   server enforcement surface. Frontend-visible capabilities can diverge from what the server
   actually permits.
7. **CanonicalIdentityReconciliationService is runtime identity-graph maintenance.** A background
   hosted service runs after a 30-second startup delay and then every 5 minutes. It creates missing
   `CanonicalIdentityLink` rows for POS users/customers, tenants, online customers, CommerceHub
   customers, affiliates, and CommerceHub store tenants, and links CommerceHub marketplace buyers
   to OnlineCustomers when an order contains both IDs. It also logs conflicts when existing links
   disagree. This is runtime synchronization/repair and conflict detection; by itself it is not
   proof of a one-time migration-only path (`CanonicalIdentityReconciliationService.cs:28-60,
   :340-425, :618-680`; `CanonicalIdentityLink.cs`).
8. **Legacy cookie `auth_token`.** Still read by `JwtBearer.OnMessageReceived` and explicitly
   deleted in favor of `access_token` (`AuthCookieConfiguration.cs:13,73`). Migration artifact
   retained for compatibility.
9. **Test-auth issuer.** `TestAuthController` / `TestingOnlyControllerFeatureProvider` is the
   token issuer for test harnesses, stripped from non-test environments (`Program.cs:150-158`).
   A parallel (test-only) identity issuer exists in the same codebase.
10. **Non-account token authentication scheme.** `ManualWalletUploadTokenAuthenticationHandler` is
    registered as an authentication scheme for manual wallet upload/session tokens. It validates a
    checkout-session token against `ICheckoutSessionTokenService` and `ITenantContext`; it is not a
    sixth account realm but it is a parallel authentication path that privileged upload endpoints
    can use.

---

## 19. CQRS / Storage Topology

- **PostgreSQL = write model and system of record** for all identity (users, customers,
  affiliates, superadmins, tenants, governance, refresh tokens, revocations, audit).
- **MongoDB = read model / projection** for storefront customers/products (e.g.
  `OnlineCustomer` projected to Mongo for storefront profile reads through
  `StorefrontOnlineCustomerService`). Mongo is derivative and not authoritative for token/session
  validation; token validation checks SQL first, then some profile/read paths consume Mongo/Redis
  projections.
- **Redis = cache + locks**: JWT blacklist cache (`commercehub:customer:jwt:blacklist`),
  rate-limiting counters, distributed locks (cart/checkout advisory locks).
- **Events (MassTransit/RabbitMQ)**: identity/security events
  (`OnlineCustomerTokenReuseDetectedEvent`, `CommerceHubCustomerIdentityEvent`, online-customer
  projection events). Canonical identity reconciliation is a hosted SQL repair/synchronization
  loop, not a message consumer in the code reviewed.
- **Elasticsearch**: product/search read index (not identity).
- No dedicated "identity read database" separate from Mongo projections. CQRS here = PG write +
  Mongo/Redis read caches + event projections.

---

## 20. Technical Debt

- Overlapping customer identity models (`OnlineCustomer` vs `CommerceHubCustomer`) require
  canonical links to correlate tenant-scoped storefront identity with global marketplace identity
  (Divergence #1, #7).
- No centralized permission system; authorization logic spread across controllers,
  `AdminTenantAuthority` (regex role extraction), policies, and `UserGovernanceService`.
- Capability computation duplicated across `AuthController`, `CustomersAuthController`,
  `StorefrontAuthController` (DRY violation; risk of drift between what UI shows and server enforces).
- Legacy `auth_token` cookie retained (Divergence #8).
- Four independent revocation implementations (Divergence #5) — no shared `IRevocationService`.
- Test-auth issuer coexisting with production issuers (Divergence #9).
- `CommerceHubCustomer` global vs `OnlineCustomer` tenant-scoped — intentional bounded-context
  separation in the implementation, but operationally overlapping where orders carry both IDs.

---

## 21. Certification

**Certification Verdict:** The IAM architecture is implementation-backed, verified by direct
source reading of the auth controllers, AuthServices, middleware, authority services, security
constants, entity definitions, `Program.cs` pipeline, and `appsettings.json`. All five identity
realms, the dual evidence/authority model, tenant resolution, and revocation paths were traced
end-to-end.

**Implementation Confidence:** HIGH. Every section above cites the concrete class/file/line that
was read.

**Verified Capabilities:**
- Five cryptographically isolated account realms with separate JWT secrets/issuers/audiences;
  additional checkout/session token schemes exist but are not account realms.
- Multi-tenant resolution from authenticated claim or server Host only; client header stripping;
  cross-tenant 403s.
- Claims-as-evidence, SQL-as-authority re-validation (`PosSessionAuthority`,
  `AdminTenantAuthority`, `SuperAdminPlatformAuthority`, `StorefrontJwtBlacklistMiddleware`,
  `CustomerTokenRevocationService`, AffiliateJwt `OnTokenValidated`).
- Refresh-token rotation + reuse detection (4 strategies).
- JWT blacklist (customer/storefront) with Redis cache + PG source of truth, fails closed.
- Rate limiting / lockout / brute-force protections on all auth endpoints.
- Governance-gated login (bans/suspension/financial lock).
- Full request-pipeline ordering verified.

**Missing Capabilities:**
- MFA — not implemented.
- Stored permission/role-assignment system — does not exist (roles are an enum + endpoint
  allow-lists; capabilities are client hints).
- A single customer account table for all customer contexts; instead the implementation uses
  tenant-scoped `OnlineCustomer`, global `CommerceHubCustomer`, and canonical links where they
  overlap.

**Technical Debt:** See §20 (overlapping customer identity models, no central permission engine,
duplicated capability computation, legacy cookie, 4 revocation impls, test-auth issuer,
non-account token auth paths).

**Architectural Observations:**
- The platform deliberately prefers *fail-closed* SQL re-validation over JWT self-authority.
  This is the dominant security pattern and explains the layered middleware.
- Tenant isolation is enforced at resolution (header strip), persistence (interceptor + global
  filters), and runtime (cross-tenant 403s) — defense in depth.
- "Vendor" is overloaded: a Tenant in governance, an Owner User in ops.

**Runtime Authority Verification:** Confirmed by reading `AdminTenantAuthority.cs`,
`PosSessionAuthority.cs`, `SuperAdminPlatformAuthorityMiddleware.cs`,
`CustomerTokenRevocationService.cs`, `StorefrontJwtBlacklistMiddleware.cs`, and
`TenantResolutionMiddleware.cs`. SQL current-state rows are the final authority for every
privileged action; JWT claims are advisory.

---

## Implementation Follow-up Items

### 1. Session and revocation mechanisms are realm-specific

- **Current implementation:** POS, Storefront, CommerceHub customer, Affiliate, and Super Admin each use different refresh/session/revocation mechanisms.
- **Why it is a concern:** Security behavior is harder to reason about consistently, and fixes must be duplicated across realm-specific code paths.
- **Recommended future action:** Introduce a shared revocation/session authority abstraction while preserving realm-specific storage where required.
- **Priority:** Medium

### 2. Capability strings are UI hints, not server permissions

- **Current implementation:** `NavigationCapabilities` and related capability strings are computed at login/profile time and returned to clients, while server enforcement remains scheme + role + SQL authority + governance gates.
- **Why it is a concern:** Frontend-visible capabilities can drift from backend-enforced permissions.
- **Recommended future action:** Either keep them explicitly documented as presentation hints with coverage tests, or promote them into a server-side permission authority.
- **Priority:** Medium

### 3. Legacy `auth_token` compatibility cookie remains supported

- **Current implementation:** POS JWT extraction still reads the legacy `auth_token` cookie, and `AuthCookieConfiguration` deletes it when setting current POS session cookies.
- **Why it is a concern:** Legacy compatibility increases the authentication surface and must remain tested until removed.
- **Recommended future action:** Define a removal window, add telemetry for remaining usage, then remove the legacy cookie read path after migration.
- **Priority:** Low

### 4. Non-account authentication scheme exists outside account realms

- **Current implementation:** `ManualWalletUploadTokenAuthenticationHandler` is registered as an authentication scheme for manual wallet upload/session tokens, separate from the five account-backed identity realms.
- **Why it is a concern:** Architecture discussions that count only account realms may miss a privileged authentication path.
- **Recommended future action:** Document all non-account token schemes in a dedicated capability/session-token section and keep endpoint coverage tests for them.
- **Priority:** Medium

### 5. Customer bounded contexts require canonical reconciliation

- **Current implementation:** Tenant-scoped `OnlineCustomer` and global `CommerceHubCustomer` are separate models; `CanonicalIdentityReconciliationHostedService` creates and repairs `CanonicalIdentityLink` rows, including links where marketplace orders contain both customer IDs.
- **Why it is a concern:** Cross-context customer identity depends on a background reconciliation loop and conflict detection.
- **Recommended future action:** Keep canonical-link health checks operational, add explicit conflict runbooks, and avoid treating either customer table as the universal customer identity.
- **Priority:** High

### 6. Authorization logic is distributed across multiple surfaces

- **Current implementation:** Authorization is implemented through JWT schemes, policies, controller role metadata, `AdminTenantAuthority`, `PosSessionAuthority`, `SuperAdminPlatformAuthority`, Storefront/Customer token services, Affiliate JWT validation, and governance services.
- **Why it is a concern:** Effective authorization requires tracing several layers, making regressions easier when new endpoints are added.
- **Recommended future action:** Create a unified authorization/authority map or abstraction that endpoint tests can assert against without changing runtime behavior immediately.
- **Priority:** Medium

### 7. Capability computation is duplicated across auth services

- **Current implementation:** Navigation/capability values are computed in multiple authentication/profile paths, including POS, CommerceHub customer, Storefront, and Super Admin flows.
- **Why it is a concern:** Duplicated capability computation can drift across realms and produce inconsistent frontend navigation behavior.
- **Recommended future action:** Centralize capability generation per realm or add shared contract tests that verify emitted capability sets.
- **Priority:** Medium
