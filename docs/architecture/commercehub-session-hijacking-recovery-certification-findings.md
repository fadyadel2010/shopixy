# PASS 16.1B-E Session Hijacking & Recovery Certification

Date: 2026-06-02

## Verdict

CommerceHub session hijacking and recovery resistance is production-certifiable for the inspected runtime paths after this pass.

Runtime hardening was required and performed for POS privileged tenant authority: status-only tenant suspension now fails closed for POS access validation and POS login/refresh gates. `TenantLineageAuthority.IsActiveTenantAsync` now requires `TenantStatus.Active`, and `UserGovernanceService.EnsureTenantLoginAllowedAsync` rejects missing, inactive, deleted, pending, suspended, or deleted-status tenants before POS tokens can be issued or refreshed.

Public launch remains not ready until the broader PASS 15.6/16.1B operational blockers are closed: deployed alert packs, session/recovery dashboards, external WORM/SIEM retention proof, PostgreSQL-backed chaos reports, and customer/privileged session visibility UX.

## Session Inventory

| Artifact | Storage | Lifetime | Revocation |
|---|---|---|---|
| Customer JWT | `commercehub_customer_access_token` HttpOnly cookie, path `/api/customers`; browser response strips raw token | `CustomerJwt.AccessTokenExpirationMinutes` | Current JTI in `RevokedCustomerAccessTokens`; all-session cutoff in `CommerceHubCustomers.TokensValidAfterUtc`; current governance validation |
| Customer auth cookie | Browser cookie only; no customer refresh cookie exists | JWT lifetime | Cleared on logout, revoke-all, password reset, password change, email change, login/register boundary cleanup |
| POS access token | `access_token` HttpOnly cookie or issuer-gated bearer token | `JwtSettings.AccessTokenExpirationMinutes` | Request-time SQL authority through `PosSessionAuthority`; token `pos_session` must equal current `Users.RefreshToken` hash |
| POS refresh token | Raw `refresh_token` HttpOnly cookie; hash stored in `Users.RefreshToken` | `JwtSettings.RefreshTokenExpirationDays` | Login/refresh rotation, logout, role/user/tenant/governance authority reduction, replay rejection |
| Super Admin access token | `super_admin_access_token` HttpOnly cookie or issuer-gated bearer token | `SuperAdminJwt.AccessTokenExpirationMinutes` | Request-time SQL `SuperAdmins.TokenVersion` and governance validation |
| Super Admin refresh token | Raw `super_admin_refresh_token` HttpOnly cookie; hash stored in `SuperAdmins.RefreshTokenHash` | `SuperAdminJwt.RefreshTokenExpirationDays` | Login/refresh rotation, logout, password update, governance restriction, token-version mismatch reuse detection |
| Password reset token | Raw email link token; SHA-256 hash in `CommerceHubCustomerPasswordResetTokens` | 30 minutes | Single-use conditional SQL consumption; new request invalidates previous live reset tokens |
| Email verification token | Raw email link token; SHA-256 hash in `CommerceHubCustomerEmailVerificationTokens` | 24 hours | Single-use conditional SQL consumption; resend/email change invalidates previous live verification tokens |
| Order access recovery cookie | `commercehub_order_access` HttpOnly cookie, path `/api/commercehub` | Order-access token lifetime | Cleared at customer auth/recovery boundaries through `ClearCommerceHubRecoveryCookie` |

## Cookie Security Findings

Auth cookies use `HttpOnly = true`, `Secure = true`, `SameSite = Strict`, explicit paths, `MaxAge` for live cookies, and Unix-epoch expiry for clearing. JavaScript cannot read auth cookies. Cross-site requests cannot carry these cookies under normal Strict SameSite behavior. Cookie-authenticated unsafe requests are also protected by the CSRF middleware/header token pattern.

The CSRF cookie is intentionally not HttpOnly because it is the double-submit value. It carries no session authority.

## Token Theft Findings

Stolen customer JWTs survive only until natural expiry unless a security event occurs first. Logout kills the current JTI. Revoke-all, password reset, password change, and email change advance `TokensValidAfterUtc`, so any token issued at or before the cutoff is rejected.

Stolen POS access tokens are candidate evidence only. They must match current SQL user, role, tenant, tenant status, tenant governance, and refresh binding. Stolen POS refresh tokens do not survive role change, user disable/delete, tenant suspension/deactivation/deletion, vendor governance suspension/ban, logout, login, refresh rotation, or replay.

Stolen Super Admin access tokens are candidate evidence only. They must match current SQL token version and governance. Stolen Super Admin refresh tokens do not survive logout, password update, governance restriction, refresh rotation, or token-version mismatch detection.

## Replay Findings

Customer access-token replay is blocked by signed JTI revocation, durable cutoff, current governance, and zero clock skew. CommerceHub customers have no refresh-token replay surface because no CommerceHub customer refresh-token endpoint, cookie, DTO field, or persistence exists.

POS refresh replay is rejected when the hash no longer matches current SQL authority and writes `REFRESH_TOKEN_REPLAY_DETECTED` evidence. Super Admin refresh replay or token-version mismatch clears authority and writes `TOKEN_REUSE_DETECTED` or `TOKEN_REFRESH_FAILED` audit evidence.

## Session Fixation Findings

Customer login generates a fresh JWT with a new `jti` and precise issued-at ticks. POS login and refresh generate a new raw refresh token, store only its hash, and bind the access token to that current hash through `pos_session`. Super Admin login and refresh generate a new refresh token, store only its hash, and advance `TokenVersion`. No inspected login path reuses an attacker-controlled session identifier as authority.

## Recovery Findings

Registration, email verification, forgot password, password reset, and email change do not create authenticated sessions. Password reset returns only a message and requires the customer to sign in again. Email verification only marks email state and does not call `BuildAuthResponse` or set auth cookies.

Password reset and email change clear browser customer cookie authority and clear CommerceHub order-access recovery cookies. Password reset, password change, and email change revoke existing customer sessions through the durable cutoff.

## Recovery Token Findings

Password reset and email verification tokens are raw email-link tokens with SHA-256 hashes stored in SQL. They are expiring, unique by hash, and consumed through conditional `ExecuteUpdateAsync` checks requiring matching id, unused state, and non-expiry. Concurrent reuse collapses to `TOKEN_ALREADY_USED`.

New password-reset requests invalidate previous live reset tokens for the same customer. Verification resend and email change invalidate previous live verification tokens for the same customer.

## Privileged Hijacking Findings

POS privileged authority is now aligned across access and refresh paths. Stolen POS refresh authority dies on role changes, user disable/delete, tenant authority reduction, and vendor governance restriction. This pass closed the status-only tenant suspension gap by requiring `TenantStatus.Active` in both tenant lineage and tenant-login gates.

Super Admin privileged authority is aligned to current SQL token version and governance. Token-version-changing operations and governance restrictions clear refresh authority and make older access tokens stale.

## Session Recovery UX Findings

Customers can revoke all sessions and recover from suspected compromise through password reset/change and email change. Customers cannot list active devices or revoke one remote device. POS Admin and Super Admin do not have per-device session inventory. This is remaining UX debt, not hidden session authority.

## Monitoring Findings

Durable evidence exists for customer identity events, POS refresh replay, privileged refresh revocation, and Super Admin refresh/reuse events. Dedicated dashboards, alert packs, and PostgreSQL-backed chaos reports for hijacking/recovery incidents remain operational debt.

## Chaos Findings

Reviewed failure modes:

- Stolen customer JWT after revoke-all: rejected by cutoff.
- Stolen customer JWT after password reset: rejected by cutoff.
- Stolen customer JWT after password change: rejected by cutoff.
- Stolen customer JWT after email change: rejected by cutoff and email unverified state.
- Customer refresh replay: not applicable; no CommerceHub customer refresh authority exists.
- POS refresh replay: rejected and audited.
- POS refresh after role change: rejected by interceptor-cleared binding.
- POS refresh after tenant suspension: rejected; this pass hardened status-only suspension.
- Super Admin refresh after token-version change: rejected by current hash/version authority.
- Recovery token replay: rejected by conditional single-use consumption.
- Recovery-created sessions: not present.

## Runtime Hardening Performed

- `TenantLineageAuthority.IsActiveTenantAsync` now requires `tenant.Status == TenantStatus.Active`.
- `TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync` now requires `tenant.Status == TenantStatus.Active`.
- `UserGovernanceService.EnsureTenantLoginAllowedAsync` now rejects missing, inactive, deleted, pending, suspended, or deleted-status tenants before POS login/refresh can issue authority.

## Regression Freeze Added

- `CommerceHubSessionHijackingCertificationTests`
- `CommerceHubRecoverySecurityCertificationTests`
- `CommerceHubRecoveryTokenCertificationTests`

These freeze cookie security, customer token theft survival, frontend token storage posture, customer access-token-only scope, recovery non-session behavior, recovery token lifecycle, replay handling, privileged SQL-current authority, and status-only tenant suspension rejection.

## Architecture Updates

The PASS 16.1B-E authority model is now recorded in `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, `.ai-context/KNOWN_RISKS.md`, and `.ai-context/CODEX_RULES.md`.

## Remaining Debt

- Customer active-device inventory and individual remote-session revocation.
- POS/Super Admin per-device privileged-session inventory.
- Dedicated hijacking/recovery dashboards and alert packs.
- PostgreSQL-backed chaos harness for stolen token, replay, recovery-token, tenant-status, and governance races.
- External WORM/SIEM evidence export and deployment proof.

## Required Answers

- Can stolen customer JWTs survive revoke-all? No.
- Can stolen customer JWTs survive password reset? No.
- Can stolen customer JWTs survive email change? No.
- Can stolen POS refresh tokens survive role changes? No.
- Can stolen POS refresh tokens survive tenant suspension? No, including status-only suspension after this pass.
- Can stolen Super Admin refresh tokens survive token-version changes? No.
- Can recovery tokens be reused? No.
- Can recovery paths create sessions? No.
- Can session hijacking persist after security events? No for inspected runtime authority; only natural-expiry windows remain absent a security event.
- Is session hijacking resistance production-certifiable? Yes for inspected runtime paths after this pass; operational launch readiness remains blocked by the broader monitoring, evidence, UX, and chaos debt listed above.
