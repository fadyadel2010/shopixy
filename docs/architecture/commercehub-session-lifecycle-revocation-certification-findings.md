# PASS 16.1B-B Session Lifecycle & Revocation Certification Findings

Date: 2026-06-02

## Verdict

CommerceHub customer session lifecycle is runtime production-certifiable for the inspected access-token-only model.

Public launch remains **NOT READY** until the broader PASS 16.1B operational blockers are closed: deployment proof, SMTP proof, identity/session dashboards and alerts, external WORM/SIEM retention, and PostgreSQL-backed chaos validation. CommerceHub customer refresh-token rotation and reuse detection remain future work only if refresh-token sessions are introduced; no CommerceHub customer refresh token is currently issued, persisted, or accepted.

## Session Inventory

| Authority surface | Authority owner | Source of truth | Derivative consumers |
|---|---|---|---|
| CommerceHub customer access JWT | `CommerceHubCustomerAuthService.LoginAsync` | Signed `CustomerJwt` with JTI, `session_iat_ticks`, expiry, issuer, and audience | HttpOnly `commercehub_customer_access_token` cookie, backend `CustomerJwt` authorization |
| CommerceHub customer refresh tokens | None | Not used for CommerceHub customers | None |
| Legacy storefront online-customer refresh tokens | Storefront auth services | PostgreSQL `OnlineCustomerRefreshTokens` | Storefront customer auth only; not CommerceHub customer session authority |
| POS/Admin access and refresh authority | POS auth services / `PosSessionAuthority` | PostgreSQL `Users.RefreshToken` current binding | POS/admin SPA cookies |
| Super Admin access and refresh authority | `SuperAdminAuthService` | PostgreSQL `SuperAdminUsers.RefreshTokenHash` and token version | Super Admin SPA cookies |
| Customer JTI revocation | `CustomerTokenRevocationService` | PostgreSQL `RevokedCustomerAccessTokens` | Redis blacklist cache |
| Customer session cutoff | `CustomerTokenRevocationService`, identity mutation flows | PostgreSQL `CommerceHubCustomers.TokensValidAfterUtc` | `CustomerJwt` validation |
| Customer cookie authority | `CustomersAuthController` / `AuthCookieConfiguration` | HttpOnly Secure SameSite Strict cookie options | Browser request authentication |
| Recovery/order access cookie | `CommerceHubOrderAccessCookie` | Server-issued cookie scoped to order recovery/access | Customer order recovery; cleared on auth-boundary logout/security events |
| Identity evidence | `CommerceHubCustomerAuthService` and `CustomersAuthController` | PostgreSQL `CommerceHubCustomerIdentityEvents` append-only evidence | Forensics, future dashboards, audit exports |

## Lifecycle Findings

Login is the only CommerceHub customer session creation path. It requires verified email, valid password, and current governance approval, then issues a signed `CustomerJwt` with JTI, precise issued-at ticks, natural expiry, and the `Customer` role.

The controller stores the access JWT in `commercehub_customer_access_token` using HttpOnly, Secure, SameSite Strict cookie options scoped to `/api/customers`; browser responses return an empty `AccessToken` field.

Protected customer endpoints validate the cookie/bearer token under the `CustomerJwt` scheme. Validation requires issuer, audience, signature, lifetime with zero clock skew, JTI presence, non-revoked JTI, durable session cutoff acceptance, and current customer governance approval.

Logout revokes the current JTI when the cookie is present, clears the customer access cookie, clears the CommerceHub recovery cookie, and records logout evidence. If JTI persistence fails, logout attempts fallback to advancing the customer cutoff when the subject can be resolved; otherwise the browser cookie authority is still cleared and the response fails closed.

Revoke-all, password reset, password change, and email change advance `CommerceHubCustomers.TokensValidAfterUtc`, which invalidates every prior access JWT whose issued-at is at or before the cutoff.

## Refresh-Token Findings

CommerceHub customer refresh tokens are not currently used. No CommerceHub customer refresh cookie, refresh endpoint, refresh-token DTO field, refresh-token table, or refresh-token issuance path was found.

No old CommerceHub customer refresh token can be reused because none is issued or persisted. The active CommerceHub customer risk is stale access JWT reuse during the access-token lifetime, controlled by JTI revocation and the durable SQL cutoff.

Legacy storefront online-customer refresh tokens, POS admin refresh bindings, and Super Admin refresh-token hashes are separate authority models and are not CommerceHub customer session authority.

## Rotation Findings

There is no CommerceHub customer refresh-token rotation because there is no CommerceHub customer refresh token. Access JWTs are not rotated by refresh; a new access JWT is issued only by a new successful login.

Future CommerceHub refresh-token support must add hash-only persistence, one-time rotation, replay detection, reuse audit evidence, revoke-all/password-reset/password-change/email-change invalidation, cookie clearing, and chaos coverage before launch certification.

## Revocation Findings

Logout revokes only the current access JWT JTI and clears browser cookie authority. Revoke-all revokes all prior sessions through `TokensValidAfterUtc`.

Password reset, password change, and email change all revoke all existing CommerceHub customer access JWTs through the durable cutoff. Email change also marks the account unverified and issues a new verification token.

JTI revocation is durable in PostgreSQL and cached in Redis. Redis is a derivative acceleration layer; database fallback is used, and revocation uncertainty fails closed.

## Expiration Findings

CommerceHub customer access JWTs default to 60 minutes and are validated with `ValidateLifetime = true` and `ClockSkew = TimeSpan.Zero`.

Sessions cannot outlive the natural JWT expiry unless a new login creates a new token. Sessions also cannot survive a durable cutoff if validation can reach PostgreSQL.

## Protected-Endpoint Findings

Customer profile, revoke-all, profile update, password change, email change, customer order/account surfaces using `CustomerJwt`, and other protected APIs reject revoked or cutoff-invalid customer sessions through the shared `CustomerJwt` validation event.

Frontend route guards are UX only and do not create backend authorization.

## Cookie Findings

Customer access cookies are HttpOnly, Secure, SameSite Strict, essential, and scoped to `/api/customers`.

The CSRF cookie is Secure and SameSite Strict but intentionally not HttpOnly so the frontend can echo it in `X-CSRF-Token`.

The CommerceHub recovery/order-access cookie is cleared on login, logout, revoke-all, password reset, password change, and email change where the flow crosses an auth boundary.

## Revocation Authority Matrix

| Actor or event | Current session JTI | All prior customer access JWTs | Browser customer cookie | Recovery cookie | Evidence |
|---|---:|---:|---:|---:|---|
| Customer logout | Yes | No, except fallback cutoff if JTI persistence fails with subject | Cleared | Cleared | `LoggedOut` when customer is resolvable |
| Customer revoke-all | Via cutoff | Yes | Cleared | Cleared | `AllSessionsRevoked` |
| Password reset | Via cutoff | Yes | Cleared by controller | Cleared | `PasswordResetCompleted` |
| Password change | Via cutoff | Yes | Cleared | Cleared | `PasswordChanged` |
| Email change | Via cutoff | Yes | Cleared | Cleared | `EmailChanged`, verification requested |
| Governance suspension/ban | Validation fail | Effective while governance state remains | Not directly cleared | Not directly cleared | Governance evidence |
| System cleanup | Removes expired blacklist rows only | No | No | No | Logs |

## Auditability Findings

Session incidents can be reconstructed for inspected runtime flows from `CommerceHubCustomerIdentityEvents`, `RevokedCustomerAccessTokens`, and `CommerceHubCustomers.TokensValidAfterUtc`. Evidence exists for registration, login, logout, revoke-all, password reset request/completion, password change, email verification request/completion, email change, and profile update.

Remaining audit debt is operational: dashboards, alert packs, external retention, and PostgreSQL-backed chaos run evidence.

## Chaos Findings

No active CommerceHub customer stale-refresh-token survival was found because refresh tokens do not exist for CommerceHub customers.

Stale access JWT reuse is controlled by JTI blacklist on logout, durable cutoff on revoke-all/security events, natural expiration, and governance validation. Revocation-cache drift fails to PostgreSQL fallback and then fails closed if revocation state cannot be determined.

## Monitoring Findings

Runtime logging exists for login, logout/revocation, revocation cache hits, Redis fallback, cutoff revocation, and identity lifecycle events. Dedicated identity/session metrics dashboards, session-failure alert packs, and chaos dashboards remain launch debt.

## Runtime Hardening Performed

No runtime code hardening was required in this pass. The inspected CommerceHub customer path already uses access-JWT-only sessions with signed JTI revocation, durable SQL cutoff, cookie clearing, current governance validation, and append-only identity evidence.

## Regression Freeze Added

- `CommerceHubSessionLifecycleCertificationTests`
- `CommerceHubSessionRevocationCertificationTests`
- `CommerceHubRefreshTokenCertificationTests`

These freeze session creation, expiration, cookie security, logout behavior, revoke-all behavior, password reset revocation, password change revocation, email change revocation, protected endpoint enforcement, refresh-token non-use, and non-CommerceHub refresh-token separation.

## Architecture Updates

`.ai-context` now records PASS 16.1B-B as an access-token-only CommerceHub customer session certification. Future CommerceHub customer refresh-token support must update this certification, the regression freezes, and the session findings in the same change.

## Remaining Debt

- Customer-visible device/session inventory.
- Identity/session-specific metrics, dashboards, and alert packs.
- External WORM/SIEM retention proof for identity/session evidence.
- PostgreSQL-backed chaos validation for stale access JWT reuse, logout, revoke-all, password reset, password change, email change, governance suspension, and revocation-cache outage.
- Refresh-token rotation and reuse detection only if CommerceHub customer refresh-token sessions are introduced.

## Required Answers

| Question | Answer |
|---|---|
| Are refresh tokens used? | No for CommerceHub customers. Refresh tokens exist in other auth systems, but not in the CommerceHub customer session model. |
| Are refresh tokens rotated? | Not applicable for CommerceHub customers because none are issued. |
| Can old refresh tokens be reused? | No old CommerceHub customer refresh token can be reused because none is issued or persisted. |
| Does logout revoke all required authority? | Yes for the current access JWT and browser cookie authority. It does not revoke every other session; revoke-all/security events handle global cutoff. |
| Does revoke-all revoke all required authority? | Yes. It advances `TokensValidAfterUtc` and clears browser cookies. |
| Does password reset revoke all required authority? | Yes. Successful reset advances the durable cutoff and clears browser cookies. |
| Does email change revoke all required authority? | Yes. Successful email change advances the durable cutoff, clears browser cookies, marks the account unverified, and issues new verification. |
| Can revoked sessions still access APIs? | No validated path was found. Revoked JTI, cutoff-invalid, expired, and governance-restricted sessions fail backend validation. |
| Can session incidents be reconstructed? | Yes for inspected runtime flows through SQL revocation rows, customer cutoff, and append-only identity evidence; operational dashboards and external retention remain debt. |
| Is session lifecycle production-certifiable? | Yes for the inspected access-token-only runtime. Public launch remains blocked by operational evidence, monitoring, retention, and chaos-proof debt. |
