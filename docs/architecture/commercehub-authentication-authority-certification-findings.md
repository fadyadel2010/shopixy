# PASS 16.1B-A Authentication & Session Authority Certification Findings

Date: 2026-06-02

## Verdict

CommerceHub authentication authority is production-certifiable at the inspected runtime boundary.

Public launch remains **NOT READY** until the broader PASS 16.1B deployment blockers are closed: migration application and verification, SMTP proof, refresh-token rotation and reuse detection, identity dashboards and alerts, external retention, and PostgreSQL-backed chaos validation.

## Authentication Inventory

| Authority surface | Owner | Source of truth | Derivative consumers |
|---|---|---|---|
| CommerceHub customer accounts | `CommerceHubCustomerAuthService` | PostgreSQL `CommerceHubCustomers` | JWT claims, HttpOnly cookie, frontend auth store, cart adoption |
| POS/Admin accounts | `AuthController` / POS auth services | PostgreSQL `Users` plus refresh-token binding | POS admin SPA auth store, current-session endpoint |
| Super Admin accounts | `SuperAdminAuthService` | PostgreSQL `SuperAdminUsers` plus token version/governance | Super Admin SPA session store |
| Email verification tokens | `CommerceHubCustomerAuthService` | PostgreSQL `CommerceHubCustomerEmailVerificationTokens` hashed token rows | Verification email links only |
| Password reset tokens | `CommerceHubCustomerAuthService` | PostgreSQL `CommerceHubCustomerPasswordResetTokens` hashed token rows | Reset email links only |
| Customer access JWTs | `CommerceHubCustomerAuthService.LoginAsync` | Signed `CustomerJwt` with JTI and issued-at claims, subordinate to SQL cutoff | HttpOnly cookie, backend authorization |
| Customer token revocation | `CustomerTokenRevocationService` | PostgreSQL `RevokedCustomerAccessTokens` plus `CommerceHubCustomers.TokensValidAfterUtc` | Redis blacklist cache |
| Identity lifecycle evidence | `CommerceHubCustomerAuthService` and customer auth controller | PostgreSQL `CommerceHubCustomerIdentityEvents` append-only evidence | Forensics, audit reports, future dashboards |

## Findings

Registration creates a SQL customer, password hash, governance profile, verification token, and identity evidence. It returns only a message and does not issue a JWT or set an auth cookie.

Email verification consumes a hashed single-use token through a conditional SQL update and marks the account verified. Verification does not issue a JWT or set an auth cookie, and token replay collapses to failure.

Login is the only CommerceHub customer session creation path. Login requires verified email, valid password, current governance approval, then issues a `CustomerJwt` with JTI and precise issued-at claims. The controller writes that JWT only to the HttpOnly customer access cookie and returns an empty `AccessToken` field to browser clients.

Logout revokes the current JTI when present, clears the customer access cookie, clears the CommerceHub recovery cookie, and records logout evidence. Revoke-all advances the durable SQL session cutoff and clears browser cookie authority.

Password reset uses hashed, expiring, single-use SQL tokens. Successful reset changes the password and advances `TokensValidAfterUtc`, invalidating existing sessions.

Email change requires the current password, changes the normalized SQL email, marks the account unverified, invalidates outstanding verification tokens, issues a new verification token, advances `TokensValidAfterUtc`, clears browser cookies, and records evidence.

Protected customer routes now explicitly require the `CustomerJwt` scheme and `Customer` role for `me`, revoke-all, profile update, password change, and email change. Frontend route guards are UX only; backend authorization remains decisive.

Auditability is sufficient for inspected runtime reconstruction: registration, login, logout, password reset request/completion, password change, verification request/completion, email change, profile update, and all-session revocation have durable SQL identity evidence.

## Required Answers

| Question | Answer |
|---|---|
| Can registration create authenticated access? | No. Registration returns no token and sets no auth cookie. |
| Can unverified users authenticate? | No. Login fails with `EMAIL_NOT_VERIFIED`. |
| Can verification tokens be reused? | No. Consumption requires `!IsUsed` and unexpired token state in a conditional SQL update. |
| What creates sessions? | CommerceHub customer login only: `LoginAsync` builds a `CustomerJwt`, and the controller writes it to the HttpOnly cookie. |
| What destroys sessions? | Logout revokes the current JTI and clears cookies; revoke-all, password reset, password change, and email change advance `TokensValidAfterUtc`. |
| Does password reset invalidate sessions? | Yes. Successful reset calls `customer.RevokeAllSessions(now)`. |
| Does email change invalidate sessions? | Yes. Successful email change calls `customer.RevokeAllSessions(now)` and clears browser cookies. |
| Can protected routes be bypassed? | No validated backend bypass found. Customer auth routes are explicitly `CustomerJwt` authorized; frontend guards are not treated as authority. |
| Can authentication incidents be reconstructed? | Yes for inspected runtime events through append-only SQL identity evidence; production dashboards and external retention remain launch debt. |
| Is authentication authority production-certifiable? | Yes for runtime authority; public launch remains blocked by PASS 16.1B deployment and monitoring debt. |

## Runtime Hardening Performed

- Hardened `GET /api/customers/auth/me` from manual claim-only handling to explicit `CustomerJwt` plus `Customer` role authorization.

## Regression Freeze Added

- `CommerceHubAuthenticationAuthorityCertificationTests`
- `CommerceHubEmailVerificationCertificationTests`
- `CommerceHubSessionCreationCertificationTests`

These freeze registration authority, verification authority, login/session creation, logout/session destruction, password reset and email change cutoff behavior, protected route enforcement, and audit evidence.

## Remaining Debt

- CommerceHub refresh-token rotation and reuse detection.
- Customer-visible device/session inventory.
- Identity-specific metrics, dashboards, and alert packs.
- External WORM/SIEM retention proof for identity lifecycle evidence.
- PostgreSQL-backed chaos harness for registration, verification replay, login rejection, logout, password reset, email change, revoke-all, and stale-token reuse races.
