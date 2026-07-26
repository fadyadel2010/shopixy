# PASS P1-A POS Authentication & Session Authority Certification

Date: 2026-06-07

## Verdict

POS authentication and session authority is production-certifiable for the inspected runtime scope after hardening ordinary POS user registration to create identity only.

PostgreSQL is the authority for POS identity, current session binding, user lifecycle, tenant lifecycle, and vendor governance. JWTs, cookies, refresh credentials, request headers, and frontend state are candidate evidence only.

## Findings

### Authentication

- Login is the approved ordinary POS session creation flow.
- Tenant onboarding remains an approved bootstrap flow for the initial owner session and idempotent onboarding-session restoration, but restoration revalidates current SQL owner, tenant ownership, active tenant lineage, and vendor governance before issuing replacement cookies.
- Ordinary `POST /api/auth/register` now creates a tenant-scoped Cashier identity only. It does not call `GenerateTokens`, store `Users.RefreshToken`, or set POS cookies.
- Email verification and verification-status paths do not create sessions.

### Session Creation

- POS sessions are created by `AuthController.Login`, `AuthController.RefreshToken`, `TenantRegistrationService` owner bootstrap issuance, and guarded `PlatformController` onboarding-session restoration.
- Session creation writes one current hash-only `Users.RefreshToken` binding plus `RefreshTokenExpiryTime`, and access JWTs carry `pos_session` as a non-credential binding to that SQL row.
- Non-login recovery flows for ordinary POS users were not found in the inspected runtime.

### Session Revocation

- Logout clears the current SQL refresh binding by matching the already SQL-validated access token `pos_session`; if revocation cannot persist, logout returns `503 SESSION_REVOCATION_FAILED` instead of pretending cookie clearing is sufficient.
- Login and refresh rotate the single current session binding, making prior access tokens and refresh credentials stale.
- `PrivilegedRefreshTokenRevocationInterceptor` clears POS refresh bindings on POS role change, user disablement/deletion, tenant suspension/deactivation/deletion, and vendor governance suspension/ban.

### Refresh Tokens

- POS refresh tokens are random raw credentials shown only to the client as HttpOnly cookie/body fallback evidence and persisted only as `RefreshTokenHasher.Hash(...)`.
- Refresh rotates the stored hash on every success.
- Replayed or stale refresh credentials fail closed with `REFRESH_REPLAY_REJECTED` and durable `REFRESH_TOKEN_REPLAY_DETECTED` audit evidence.
- `Users.RefreshToken` is an EF concurrency token, so concurrent login/refresh/logout writes cannot silently resurrect stale authority.

### SQL Authority

- Default POS JWT validation calls `PosSessionAuthority.ValidateAccessTokenAsync`.
- `PosSessionAuthority` revalidates current SQL user existence, active/not-deleted state, tenant claim match, current role, current unexpired session binding, active tenant lineage, and vendor governance.
- Tenant authority requires `Tenants.IsActive`, `TenantStatus.Active`, and `DeletedAtUtc == null`.

### User Lifecycle

- Disabled or deleted users cannot authenticate or keep access.
- Role changes invalidate stale access tokens through current-role revalidation and clear refresh authority through the interceptor.
- No separate POS permission entity was found; current role remains the certified privilege boundary.

### Tenant Lifecycle

- Suspended, inactive, deleted-status, or deleted tenants cannot login, refresh, or pass access-token validation.
- Tenant lifecycle reductions clear stored POS refresh bindings for tenant users.
- Vendor governance suspension/ban blocks login/refresh/access and clears refresh authority.

### Adversarial

- Refresh replay: rejected and audited.
- Session theft: stolen access tokens die after logout, login/refresh rotation, role/user/tenant/governance changes, refresh expiry, or natural JWT expiry.
- Concurrent refresh: stale writer rejected by SQL concurrency.
- Role-change, disablement, and tenant-suspension bypass: rejected by SQL-current validation and refresh revocation.
- Session fixation: login/refresh/onboarding issue fresh random session bindings; ordinary registration no longer creates a session.

## Runtime Hardening Performed

- Removed POS token generation and cookie issuance from `AuthController.Register`.
- Added runtime freeze proving registration leaves `Users.RefreshToken`/`RefreshTokenExpiryTime` null and sets no POS access/refresh cookies.
- Updated logout lifecycle tests to exercise revocation through approved login-issued sessions.
- Tightened POS session test tenant fixtures to explicitly use `TenantStatus.Active`, matching runtime tenant authority.

## Files Modified

- `PointOfSale.API/Controllers/AuthController.cs`
- `PointOfSale.Tests/Security/PosAuthenticationLifecycleTests.cs`
- `PointOfSale.Tests/Security/PosSessionAuthorityTests.cs`
- `PointOfSale.Tests/Architecture/AdminAuthenticationSessionCertificationTests.cs`
- `docs/architecture/pos-authentication-session-authority-certification-findings.md`

## Regression Freezes

- `PosAuthenticationLifecycleTests.Register_CreatesIdentityOnlyWithoutPosSessionAuthority`
- `AdminAuthenticationSessionCertificationTests.PosUserRegistration_MustNotCreateSessionAuthority`
- Existing POS/session freezes revalidated: `PosAuthenticationLifecycleTests`, `PosSessionAuthorityTests`, `CommerceHubPrivilegedAuthorityCertificationTests`, `CommerceHubAdminSessionSecurityCertificationTests`, `CommerceHubPrivilegedSessionCertificationTests`, `CommerceHubPrivilegedRefreshCertificationTests`, `CommerceHubRefreshReplayCertificationTests`, and `CommerceHubGovernanceRefreshRevocationTests`.

## Required Answers

- Can registration create POS authority? No. Ordinary `api/auth/register` creates identity only after this pass.
- Can onboarding create POS authority? Yes, tenant-owner onboarding is an approved bootstrap authority path with SQL-current restoration checks.
- Can non-login flows create sessions? Only the approved tenant onboarding bootstrap/restoration flow and refresh rotation; verification/recovery paths do not.
- Are refresh tokens hashed? Yes.
- Are refresh tokens rotated? Yes.
- Can refresh replay succeed? No; replay is rejected and audited.
- Can disabled users authenticate? No.
- Can suspended tenants authenticate? No.
- Can stale sessions survive role changes? No.
- Can stale sessions survive tenant suspension? No.
- Were runtime code changes required? Yes.
- Is POS Authentication & Session Authority production-certifiable? Yes, for the inspected runtime scope.

## Remaining Debt

- No POS MFA/SSO/ABAC.
- No POS per-device session inventory or individual remote-session termination.
- No ordinary POS password-reset/change/session-management ceremony was found in inspected runtime.
- Refresh replay rejects stale credentials and audits evidence, but there is no token-family compromise table that revokes a successfully rotated successor merely because an older credential was replayed.
- Dedicated operational dashboards, alert packs, external WORM/SIEM proof, and PostgreSQL-backed chaos reports remain launch-readiness debt.
