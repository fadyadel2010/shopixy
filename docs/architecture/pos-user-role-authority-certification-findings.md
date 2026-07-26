# PASS P1-C POS User & Role Authority Certification

Date: 2026-06-07

## Verdict

POS user and role authority is production-certifiable for the inspected runtime scope.

Current PostgreSQL user, role, active/deleted state, refresh-session binding, tenant
lineage, and vendor governance are authoritative. JWT claims, frontend state, route
visibility, and request DTO fields are evidence or presentation only.

## 1. User Authority Findings

- `Users` is the POS identity authority.
- Access requires a current SQL user row that is active, not deleted, and still owned
  by the token tenant.
- Missing, disabled, and deleted users fail request-time authority.
- Ordinary registration creates an active `Cashier` identity only and creates no
  session authority.

## 2. Role Authority Findings

- `Users.Role` is the current POS privilege authority.
- JWT role claims are compared with the current SQL role on every default POS bearer
  authentication.
- A stale or tampered role claim fails with `stale_actor_role_claim`.
- No separate POS permission or capability entity exists.

## 3. Runtime Validation Findings

- Default POS JWT validation verifies signature, issuer, audience, lifetime, and zero
  clock skew.
- `OnTokenValidated` calls `PosSessionAuthority`.
- SQL revalidation covers user state, tenant ownership, role, current refresh binding,
  tenant lifecycle, and vendor governance.

## 4. Role Mutation Findings

- No tenant POS role-assignment, promotion, demotion, or user-management API was found.
- EF-tracked `User.Role` changes clear the current refresh binding and write audit
  evidence through `PrivilegedRefreshTokenRevocationInterceptor`.
- Existing access tokens fail current-role comparison after a role change.
- Refresh cannot preserve the old role because the current binding is revoked.

## 5. Permission Findings

- Role-declared controller permissions are backend-enforced.
- `AdminTenantAuthorityMiddleware` extracts endpoint role metadata and rechecks the
  current SQL actor role and tenant before the action executes.
- Frontend `PermissionGuard` and sidebar visibility are presentation only.
- Endpoints using bare `[Authorize]` intentionally permit any authenticated current
  POS role; there is no finer permission table to evaluate.

## 6. Administrative Authority Findings

- Users cannot self-promote through registration or DTO injection.
- `RegisterRequest` has no role or tenant field and rejects `role` and `tenantId`
  JSON properties.
- Tenant owner creation is confined to tenant onboarding.
- Test-only Admin creation is removed from non-test/non-development controller
  discovery.
- No lower-role-to-higher-role mutation endpoint exists.

## 7. Adversarial Findings

- JWT role tampering: signature validation and SQL role comparison reject it.
- Frontend role tampering: changes presentation only and cannot change SQL authority.
- DTO role injection: rejected.
- Route tampering and direct API invocation: still cross authentication, SQL session
  validation, tenant resolution, and declared backend role checks.
- Stale-role replay: rejected; refresh authority is revoked on runtime role mutation.

## 8. Runtime Hardening Performed

No runtime code changes were required. The inspected runtime already satisfied the
POS user and role authority model established by PASS P1-A, P1-B, and 16.1B-D.1.

## 9. Exact Runtime Files Modified

None.

## 10. Regression Freezes Added

- `PosUserAuthorityCertificationTests`
- `PosRoleAuthorityCertificationTests`
- `PosPermissionAuthorityCertificationTests`
- `PosAdministrativeAuthorityCertificationTests`

## 11. Architecture Updates

- POS role claims remain candidate evidence only.
- Current SQL `Users.Role` is request-time authority.
- Role changes must revoke refresh authority.
- Frontend role visibility is not permission authority.
- Future user-management or permission models must define hierarchy, mutation
  authority, invalidation, and current-SQL enforcement before launch.

## 12. Remaining Debt

- No tenant POS user-management or invitation workflow exists.
- No separate POS permission/capability model, ABAC, MFA, SSO, or per-device session
  inventory exists.
- Bare `[Authorize]` endpoints grant all authenticated current POS roles by design;
  future least-privilege work should introduce an explicit permission matrix rather
  than relying on frontend visibility.
- Direct out-of-band database role edits do not pass through the EF revocation
  interceptor, although stale access claims still fail SQL role comparison.
- Dedicated operational dashboards, alerts, external retention, and PostgreSQL-backed
  role-mutation chaos evidence remain operational debt.

## Required Answers

1. Can users self-promote? **No.**
2. Can stale role claims survive? **No.**
3. Can stale JWT roles survive? **No.**
4. Can disabled users retain authority? **No.**
5. Can deleted users retain authority? **No.**
6. Can governance-restricted users retain authority? **No for the applicable POS vendor/tenant governance boundary.**
7. Can direct API invocation bypass permissions? **No declared backend role boundary can be bypassed; bare authenticated endpoints intentionally have no narrower role boundary.**
8. Can frontend role tampering create authority? **No.**
9. Can refresh preserve invalid authority? **No for inspected runtime role, user lifecycle, tenant lifecycle, and governance mutations.**
10. Were runtime code changes required? **No.**
11. Is POS User & Role Authority production-certifiable? **Yes, for the inspected runtime scope.**
