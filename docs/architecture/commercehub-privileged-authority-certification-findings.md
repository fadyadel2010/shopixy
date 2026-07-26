# PASS 16.1B-D Privileged Session Security Certification

Date: 2026-06-02

## Verdict

POS Admin and Super Admin privileged-session security is production-certifiable for the inspected runtime paths. Stale privileged access tokens are not authority by themselves; each privileged request is subordinate to current PostgreSQL actor, session, tenant, role, token-version, and governance state.

Public launch remains blocked only by previously documented operational proof debt: deployment-applied evidence, dashboards, alert packs, external retention, and PostgreSQL-backed chaos validation.

## Privileged Authority Inventory

| Surface | Authority owner | Source of truth | Runtime enforcement |
|---|---|---|---|
| POS Admin actor | `Users` | `Id`, `TenantId`, `Role`, `IsActive`, `IsDeleted` | `PosSessionAuthority.ValidateAccessTokenAsync` during default POS JWT validation |
| POS Admin session | `Users.RefreshToken`, `RefreshTokenExpiryTime` | Hash-only current refresh binding carried as access-token `pos_session` evidence | Login/registration/refresh rotate; logout clears; token validation compares current binding |
| POS tenant authority | `Tenants` | active lineage through `TenantLineageAuthority` | POS token validation and onboarding replay revalidation |
| POS governance authority | `UserGovernanceProfiles` for vendor/tenant subject | current banned state blocks privileged session | POS token validation, login, refresh, onboarding replay |
| POS permissions | `User.Role` | no separate POS permission entity was found in inspected code | role-change revalidation rejects stale role claims |
| Super Admin actor | `SuperAdmins` | current row, email, `TokenVersion` | `SuperAdminPlatformAuthority` middleware on `RequireSuperAdmin` |
| Super Admin session | `RefreshTokenHash`, `RefreshTokenExpiresAtUtc`, `TokenVersion` | single current refresh slot plus access-token token-version claim | login/refresh/password/logout/governance restriction advance or clear authority |
| Super Admin governance | `UserGovernanceProfiles` for admin subject | current non-active status blocks platform authority | login, refresh, request-time authority, governance mutation clear |
| Emergency controls | `CommerceHub:EmergencyStops` plus Super Admin governance/operations | config-driven readiness gates and audited Super Admin mutations | `CommerceHubFinancialReadinessGate`, Super Admin governance/operations controllers |

## POS Admin Findings

POS Admin login and refresh re-run tenant governance, generate a new random refresh credential, persist only its hash in SQL, and issue an access token containing `pos_session`. POS token validation re-reads current SQL user state, rejects stale role claims, inactive/deleted users, tenant mismatch, expired/rotated/logout-cleared bindings, inactive/deleted tenants, and banned tenant governance.

POS logout clears SQL session authority. If SQL revocation cannot be persisted, logout returns `SESSION_REVOCATION_FAILED` and does not claim containment from cookie clearing alone.

## Super Admin Findings

Super Admin login and refresh require current governance approval, store a hash-only refresh credential, and issue an access token with current `TokenVersion`. Request-time platform authority rejects missing/malformed claims, missing SQL actor rows, stale token versions, and governance-restricted actors.

Refresh-token reuse detection and governance-blocked refresh clear current refresh authority. Logout and password update call `ClearRefreshToken()`, which also increments token version.

## Authority Matrix

| Event | POS Admin Session | Super Admin Session |
|---|---|---|
| Logout | Rejected after SQL binding is cleared | Rejected after refresh clear and token-version increment |
| Password Change | No ordinary POS password-change/reset path found; future path must clear/rotate binding | Rejected after `SuperAdminManagementService.UpdatePasswordAsync` clears refresh authority |
| Role Change | Rejected by current SQL role comparison | Not role-based; platform role is scheme/claim plus current SQL Super Admin row |
| Permission Removal | No separate POS permission entity found; role removal is immediately rejected | No separate permission entity found; governance restriction/token-version change rejects |
| Tenant Suspension | Rejected through active tenant lineage/governance validation | Not tenant-scoped; platform operations still require current Super Admin authority |
| User Disable | Rejected through current `Users.IsActive` / `IsDeleted` checks | Delete removes SQL actor row; governance restriction rejects and clears refresh authority |
| Governance Restriction | Banned tenant governance rejects POS token validation/login/refresh | Non-active admin governance rejects login/refresh/request and clears refresh authority on mutation |
| New Login Elsewhere | Rotates single current SQL session binding; old token rejected | Advances token version; old token rejected |

## Chaos Findings

No stale privileged-session bypass was found in the inspected paths. Existing tests cover stale POS role, disabled POS actor, inactive tenant, banned governance, logout/refresh rotation, expired binding, stale Super Admin token version, governance-restricted Super Admin, deleted Super Admin, concurrent POS session rotation, and governance-restricted Super Admin refresh revocation.

## PASS 16.1B-D.1 Refresh-Authority Addendum

Follow-up refresh-specific hardening found that access-token rejection was not sufficient proof that stored privileged refresh authority died when SQL authority changed.

Runtime hardening now clears POS refresh bindings on POS role change, user disable/delete, tenant suspension/deactivation/deletion, and vendor governance suspension/ban through `PrivilegedRefreshTokenRevocationInterceptor`. Vendor suspension now blocks POS login/refresh through `EnsureTenantLoginAllowedAsync`. POS refresh replay writes durable `REFRESH_TOKEN_REPLAY_DETECTED` evidence to `AuditLogs`.

The authoritative follow-up findings are recorded in `docs/architecture/commercehub-privileged-refresh-token-hardening-findings.md`.

## Monitoring Findings

POS session authority failures emit bounded `OperationalConvergenceAuthority.PosSession` telemetry. Super Admin platform authority failures emit bounded `OperationalConvergenceAuthority.PlatformAdmin` telemetry. Super Admin write operations run through operational audit after platform authority. Domain-specific governance, finance, payout, refund, and operations flows also retain their own audit records.

Dedicated privileged-session dashboards and PostgreSQL-backed chaos reports remain operational debt.

## Runtime Hardening Performed

No runtime code hardening was required in this pass. The inspected implementation already continuously revalidates privileged authority.

Regression-freeze coverage was added in:

- `CommerceHubPrivilegedAuthorityCertificationTests`
- `CommerceHubAdminSessionSecurityCertificationTests`
- `CommerceHubSuperAdminSessionCertificationTests`

## Required Answers

- Can stale POS Admin sessions survive role changes? No.
- Can stale POS Admin sessions survive tenant suspension? No, active tenant lineage/governance is revalidated.
- Can stale POS Admin sessions survive user disablement? No.
- Can stale Super Admin sessions survive token-version changes? No.
- Can stale Super Admin sessions survive governance restrictions? No.
- Are privileged permissions revalidated? POS role authority is revalidated. No separate POS permission entity was found.
- Are stale privileged tokens rejected? Yes.
- Are authority failures audited? They are logged/telemetry-recorded; Super Admin mutations are operationally audited.
- Can privileged sessions bypass emergency controls? No inspected bypass was found. Emergency controls remain inside Super Admin authority and financial readiness gates.
- Is privileged-session security production-certifiable? Yes for inspected runtime behavior.

## Remaining Debt

- No enterprise IAM, SSO, MFA, ABAC, POS per-device session inventory, Super Admin per-device inventory, or immutable SIEM-grade identity lake was added.
- No ordinary POS password-change/reset/session-management endpoint was found. Future POS credential-rotation paths must clear or rotate the current SQL session binding and add tests.
- No separate POS permission entity was found; permissions are effectively current role authority. Future permission tables or capability flags must be checked by current SQL authority and invalidate stale privileged sessions.
- Dedicated privileged-session dashboards, alert packs, and PostgreSQL-backed chaos validation remain launch-readiness debt.
