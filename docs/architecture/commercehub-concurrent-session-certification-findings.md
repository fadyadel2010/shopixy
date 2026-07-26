# PASS 16.1B-C Cross-Device & Concurrent Session Certification

Date: 2026-06-02

## Verdict

CommerceHub concurrent-session behavior is production-certifiable for the inspected access-token-only customer model and the inspected POS Admin / Super Admin privileged-session models.

Public launch remains **NOT READY** for the broader previously documented reasons: deployment proof, SMTP proof, monitoring dashboards, alert packs, external retention, and PostgreSQL-backed chaos evidence are still required.

## Concurrent-Session Inventory

| Surface | Authority owner | Session identity model | Durable revocation |
|---|---|---|---|
| CommerceHub Customer | `CommerceHubCustomers`, `RevokedCustomerAccessTokens` | Independent signed `CustomerJwt` access tokens with unique JTI, precise issued-at claim, HttpOnly `commercehub_customer_access_token` cookie | Per-JTI SQL blacklist plus `CommerceHubCustomers.TokensValidAfterUtc` cutoff |
| POS Admin | `Users.RefreshToken` / `Users.RefreshTokenExpiryTime` | Single current SQL refresh-token hash bound into access token as `pos_session` | Login/refresh rotation overwrites current binding; logout clears current binding |
| Super Admin | `SuperAdmins.RefreshTokenHash`, `RefreshTokenExpiresAtUtc`, `TokenVersion` | Single current refresh-token hash plus access-token `token_version` claim | Login/refresh/logout/password/governance increments or clears token version/refresh authority |

## Multi-Device Findings

CommerceHub customer sessions are allowed concurrently. Phone plus laptop plus tablet, or Browser A plus Browser B, can all hold valid access JWTs until natural expiry or revocation. New customer login does not revoke prior customer tokens.

POS Admin sessions are not concurrent for the same user. A new login or refresh rotates the one SQL session binding and invalidates older access tokens.

Super Admin sessions are not concurrent for the same platform actor. A new login or refresh advances token version through the single refresh-token slot, making older access tokens stale.

## Session-Isolation Findings

Customer logout revokes the current cookie token JTI during normal operation and clears browser cookie authority. Other customer devices are not revoked by normal logout. If single-token revocation persistence fails, logout may intentionally fall back to the customer-wide cutoff for fail-closed safety.

POS Admin and Super Admin do not have independent per-device sessions. Rotation or logout revokes the single current session authority, so older devices are intentionally invalidated.

## Logout Findings

| Logout action | Device A | Device B | Device C |
|---|---|---|---|
| Customer logout on A | A cookie cleared and A JTI revoked | B remains valid | C remains valid |
| POS Admin logout on A | Shared SQL session binding cleared | B invalid if it used same current binding | C invalid if it used same current binding |
| Super Admin logout on A | Refresh authority cleared and token version advanced | B stale | C stale |

## Revoke-All Findings

Customer revoke-all advances `CommerceHubCustomers.TokensValidAfterUtc`, clears the current browser cookie, and invalidates every existing customer access token whose issued-at timestamp is at or before the cutoff.

POS Admin and Super Admin have no separate user-facing revoke-all endpoint because their runtime model is single-current-session. Security events rotate or clear the one current privileged authority.

## Password-Reset Findings

CommerceHub customer password reset consumes the reset token atomically, changes the password hash, and calls `customer.RevokeAllSessions(now)`. All active customer devices die by cutoff validation.

Super Admin password update clears refresh authority and advances token version. POS Admin password-reset/change behavior was not expanded in this pass; POS runtime access remains protected by single-session SQL binding, current user active state, role, tenant, and governance checks.

## Email-Change Findings

CommerceHub customer email change requires the current password, changes the normalized email, marks the account unverified, invalidates old verification tokens, issues a new verification token, and calls `customer.RevokeAllSessions(now)`. Existing customer devices cannot survive.

No equivalent self-service POS Admin or Super Admin email-change session surface was certified in this pass.

## Session-Visibility Findings

CommerceHub customers can revoke all sessions from settings, but they cannot list active devices, inspect device metadata, or terminate one remote device. That absence is explicit remaining debt, not an authority bypass in the current access-token-only model.

POS Admin and Super Admin do not expose per-device session inventory. Legacy tenant storefront online-customer admin session management uses `OnlineCustomerRefreshTokens` and is separate from CommerceHub customer authority.

## Admin-Session Findings

POS Admin access tokens are subordinate to current SQL `Users` state, tenant activity, tenant governance, current role, and the current refresh/session binding. Stale admin sessions fail closed as `revoked_or_rotated_pos_session`.

Super Admin access tokens are subordinate to current SQL `SuperAdmins.TokenVersion` and current governance state. Stale privileged sessions fail closed as `STALE_SUPER_ADMIN_TOKEN`, and governance restrictions clear refresh authority.

## Authority Matrix

| Action | Customer | POS Admin | Super Admin |
|---|---|---|---|
| Multiple Sessions Allowed | Yes, uncapped until JWT expiry/cutoff | No, single current SQL session binding | No, single current refresh slot plus token version |
| Logout Current Session | Revokes current JTI and clears cookie | Clears current SQL session binding | Clears refresh token and advances token version |
| Logout All Sessions | Yes via `revoke-all-sessions` cutoff | Equivalent effect through single-session logout/rotation only | Equivalent effect through logout/rotation only |
| Password Reset Kills All | Yes, cutoff | Not certified as a separate self-service flow in this pass | Password update clears privileged authority |
| Email Change Kills All | Yes, cutoff and reverification | Not certified as a separate self-service flow | Not certified as a separate self-service flow |
| Session Visibility | No customer device inventory | No per-device inventory | No per-device inventory |

## Chaos Findings

No active cross-device bypass was found in the inspected paths.

Controls found:

- Customer JTI blacklist is PostgreSQL-authoritative with Redis as derivative cache.
- Customer cutoff validation fails closed if revocation state cannot be verified.
- Customer governance is rechecked during JWT validation.
- POS Admin stale role, tenant, inactive tenant, banned tenant, missing binding, expired binding, logout, and refresh rotation are rejected.
- Super Admin stale token version and governance restrictions are rejected before platform operations.

## Monitoring Findings

Existing evidence and telemetry include customer identity events, Super Admin audit events, POS session authority failure telemetry, and Super Admin platform authority failure telemetry.

Dedicated concurrent-session dashboards, customer device inventory telemetry, revoke-all incident dashboards, and production chaos reports remain incomplete.

## Runtime Hardening Performed

No runtime code hardening was required. The inspected runtime already enforced the certified model.

Regression hardening was added in:

- `CommerceHubConcurrentSessionCertificationTests`
- `CommerceHubSessionIsolationCertificationTests`
- `CommerceHubPrivilegedSessionCertificationTests`

## Architecture Updates

This certification is recorded in `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md`.

## Remaining Debt

- CommerceHub customer-visible active-session/device inventory.
- Customer termination of one remote device/session.
- Dedicated concurrent-session telemetry dashboards and alert packs.
- PostgreSQL-backed chaos validation for multi-device login, revoke-all, password reset, email change, privileged role change, and governance restriction races.

## Required Answers

- Are concurrent sessions allowed? Customer: yes. POS Admin: no. Super Admin: no.
- How many concurrent sessions are allowed? Customer: uncapped by server-side count, bounded by JWT expiry and cutoff. POS Admin and Super Admin: one current session authority per actor.
- Does logout kill only the current session? Customer: yes during normal revocation. POS Admin and Super Admin: logout kills the single current privileged authority.
- Does revoke-all kill every session? Customer: yes through durable cutoff. POS Admin and Super Admin: no separate multi-session revoke-all is needed under the current single-session model.
- Does password reset kill every session? Customer: yes. Super Admin password update clears privileged authority. POS password reset was not separately certified here.
- Does email change kill every session? Customer: yes. POS Admin and Super Admin equivalent flows were not found.
- Can users see active sessions? CommerceHub customers: no. POS Admin and Super Admin: no.
- Can users terminate individual sessions? CommerceHub customers: current session logout only, no remote individual device termination. POS Admin and Super Admin: no per-device model.
- Can privileged sessions survive security events? No for inspected POS Admin and Super Admin authority events.
- Is concurrent-session behavior production-certifiable? Yes for inspected runtime behavior; broader launch readiness remains blocked by previously documented operational proof and monitoring debt.
