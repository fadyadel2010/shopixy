# POS-ADMIN-CERTIFICATION-01 PASS 11.1 Super Admin Identity & Session Security Certification

Date: 2026-05-29

## Scope

This pass certifies the platform-operator identity boundary after PASS 10.6. The reviewed authority model remains:

```text
Current SQL SuperAdmin Authority
+
Current Governance State
+
Current Token Version
```

JWTs, cookies, refresh credentials, role claims, request paths, Redis state, and session cache state are evidence only. PostgreSQL `SuperAdmins`, `UserGovernanceProfiles`, and `SuperAdmins.TokenVersion` decide platform authority.

## Identity Inventory Findings

- Authority owner: `SuperAdmins` in PostgreSQL owns platform actor existence, password hash, current refresh-token hash, refresh expiry, and `TokenVersion`.
- Revocation owner: `SuperAdminUser.ClearRefreshToken()` clears refresh authority and increments `TokenVersion`; logout, password update, governance restriction, and refresh replay use this boundary.
- Validation owner: `SuperAdminPlatformAuthorityMiddleware` revalidates every non-anonymous `RequireSuperAdmin` endpoint through current SQL super-admin authority and current governance state.
- Login owner: `SuperAdminAuthService.LoginAsync(...)` verifies credentials, checks governance login eligibility, rotates refresh authority, and issues access tokens with `token_version`.
- Refresh owner: `SuperAdminAuthService.RefreshAsync(...)` requires the current refresh hash, current expiry, current governance login eligibility, and matching token version before rotation.
- Logout owner: `SuperAdminAuthService.LogoutAsync(...)` clears current refresh authority and increments token version, invalidating outstanding access tokens.
- Password rotation owner: `SuperAdminManagementService.UpdatePasswordAsync(...)` updates the password hash and clears refresh authority.
- Governance owner: `UserGovernanceService.ApplyActionAsync(...)` writes SQL governance status and clears platform refresh authority when a super-admin moves out of `Active`.

## Certification Findings

- Access tokens: certified as evidence only. `SuperAdminJwt` validates signature, issuer, audience, lifetime, role, auth type, and `token_version`; `SuperAdminPlatformAuthority` then reloads SQL and rejects missing rows, stale token versions, and non-active governance state.
- Refresh tokens: certified as current-SQL session evidence. Stored refresh material is hash-only, rotation increments token version, stale version reuse revokes current refresh authority, and governance-restricted refresh fails closed before token issuance.
- Session revocation: logout, forced governance restriction, password update, account deletion, and refresh replay remove authority by clearing refresh state, incrementing token version, deleting SQL authority, or rejecting against governance SQL.
- Password change/reset: reviewed production super-admin password rotation is management-owned and revokes existing sessions. No separate public super-admin password-reset flow was found.
- Governance restriction: suspended, banned, and financially locked super-admins fail login, refresh, and request-time platform authority. Governance restriction also revokes stored refresh authority.
- Token replay: old access tokens fail on token-version mismatch. Old refresh tokens fail hash lookup after rotation/logout; replay with a current refresh credential and stale version revokes the session.
- Middleware: `SuperAdminPlatformAuthorityMiddleware` now explicitly honors `[AllowAnonymous]` so inherited `SuperAdminBaseController` policy metadata cannot block login/refresh endpoints. Non-anonymous `RequireSuperAdmin` endpoints still reload SQL authority before controllers execute.
- Legacy endpoints: legacy service-marketplace global reindex no longer depends on role-string `SuperAdmin` detection inside a tenant-admin endpoint. Tenant admin reindex is tenant-scoped, and global service reindex is a dedicated `RequireSuperAdmin` route. Manual-wallet admin routing no longer lists `SuperAdmin` as a tenant role.
- Tenant isolation: super-admin authority remains a separate authentication scheme and policy. Tenant-admin session hardening does not grant platform authority, and platform authority no longer inherits tenant-admin semantics on the reviewed legacy routes.
- Operational audit: non-read `RequireSuperAdmin` operations remain request-audited after SQL authority recomposition. Authority failures emit bounded convergence telemetry. Allow-anonymous auth endpoints are not incorrectly audited as platform operations.
- Chaos behavior: replay storms, logout races, governance changes during active sessions, password rotation during active sessions, account deletion, and restart with stale tokens converge through SQL token-version/governance checks and fail closed on reviewed boundaries.

## Runtime Hardening Performed

- `SuperAdminPlatformAuthorityMiddleware` and `SuperAdminOperationalAuditMiddleware` now skip endpoints carrying `IAllowAnonymous`, preventing inherited controller policy metadata from turning login/refresh into platform-authority checks.
- `MarketplaceController` now exposes tenant service reindex as tenant-admin only and global service reindex as `RequireSuperAdmin` only.
- `MarketplaceController` tenant diagnostics now filter SQL and Elasticsearch counts by current tenant instead of reading all tenants from a tenant-admin route.
- `AdminManualPaymentController` no longer advertises `SuperAdmin` as a tenant-admin role.

## Monitoring Improvements

- Existing bounded platform-authority telemetry remains the rejection signal for invalid claims, missing SQL authority, stale token version, governance restriction, and actor-scope rejection.
- Existing request-level super-admin operational audit remains the durable breadcrumb for non-read platform operations after authority recomposition.
- This pass did not add a new metric family; it tightened middleware routing and legacy endpoint attribution.

## Governance Updates

- PASS 11.1 is now documented as the super-admin identity/session authority boundary.
- Future platform-operator identity changes must preserve SQL `SuperAdmins` + SQL governance + token-version validation and update certification tests/docs in the same change.
- Legacy platform endpoints must use `RequireSuperAdmin` for platform authority and must not branch on raw `SuperAdmin` role strings inside tenant-admin routes.

## Honest Remaining Debt

- No enterprise IAM, SSO, MFA, ABAC, device inventory, refresh-token family table, immutable SIEM store, or support break-glass ceremony was introduced.
- Old rotated refresh tokens fail closed but cannot always identify and revoke a successor session unless a current credential plus stale token version is presented; adding token-family history remains future IAM debt.
- Super-admin account deletion is management-service protected for last/system admins, but broader delete ceremonies remain simple SQL removal rather than a multi-step deprovisioning workflow.
- Some read-only public preview helpers still use role checks as convenience gates; they do not create platform mutation authority but should receive path-specific cleanup if preview semantics expand.
