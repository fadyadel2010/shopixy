# POS-ADMIN-CERTIFICATION-01 PASS 1 Admin Authentication And Session Certification Findings

Date: 2026-05-26

## Scope

This pass certified the operational identity boundary used by POS tenant administrators together with the already-hardened super-admin platform boundary. The review covered POS login, registration-issued sessions, refresh rotation, logout, JWT request validation, stale role and disabled actor reuse, tenant authority, vendor-governance rejection, super-admin platform recomposition, operational audit, and support/impersonation exposure.

PostgreSQL remains identity authority. JWTs, cookies, presented refresh credentials, request headers, tenant selectors, and support UI state are candidate evidence only.

## Paths Audited

- POS authentication: `AuthController` login, register, refresh/refresh-token, logout, and session endpoints; `TenantRegistrationService` initial owner issuance; and `PlatformController` idempotent onboarding-session restoration.
- POS request authentication: default bearer configuration in `Program.cs`, `TenantResolutionMiddleware`, `AdminTenantAuthority`, and role-protected tenant-admin controllers.
- SQL identity state: `Users`, `Tenants`, vendor `UserGovernanceProfiles`, and the current persisted POS refresh-session binding.
- Platform authority: `SuperAdminAuthService`, `SuperAdminPlatformAuthorityMiddleware`, token-version rotation, and `SuperAdminOperationalAuditMiddleware`.
- Operational tenant scope: admin rebuild/reindex/repair endpoints certified by TENANT-CERTIFICATION-01 and SUPERADMIN-CON-01.
- Support/escalation: inspected production API surfaces and existing emergency governance documentation; no production impersonation endpoint was found.

## Authentication Lifecycle Findings

Confirmed weakness found and hardened:

- POS refresh credentials were stored and compared as raw token values, including tenant owner onboarding issuance and cached onboarding replay outside `AuthController`.
- POS logout cleared a refresh token but did not provide a SQL-bound way to reject the still-unexpired POS access token on ordinary authenticated routes.
- POS access-token authentication required a tenant claim but did not recompose current SQL user/session state on every POS bearer request. Role-protected admin middleware reduced privileged endpoint exposure, but ordinary authenticated POS surfaces could still accept stale session evidence.
- Cached tenant-registration replay could still mint a fresh POS session from previously cached success data without rechecking current SQL owner activity, tenant ownership, active tenant lineage, or current tenant governance state.
- Super-admin refresh rotation previously trusted a still-current refresh credential without re-running current governance login eligibility, which allowed a suspended or financially locked platform actor to mint a fresh platform session before later request-time authority rejection.

Targeted hardening:

- `AuthController` now stores only `RefreshTokenHasher.Hash(...)` in `Users.RefreshToken`.
- POS access tokens now include `pos_session`, a non-credential binding to the current hashed refresh session.
- `PosSessionAuthority` runs from default POS JWT validation and requires current SQL user existence, active/not-deleted actor state, unchanged tenant ownership, unchanged role, non-expired matching current session binding, active tenant lineage, and no current vendor governance ban.
- Logout clears the current SQL-bound session using the validated access-token binding, including header-based clients without a refresh cookie.
- If logout cannot persist SQL revocation, `AuthController.Logout(...)` now returns `503 SESSION_REVOCATION_FAILED` and does not pretend that cookie clearing alone completed containment.
- Tenant onboarding owner issuance and idempotent registration-session restoration now issue the same hash-only, SQL-bound POS session shape; cached replay rotation rejects a stale competing write.
- Cached onboarding replay now rechecks current SQL owner existence, `!IsDeleted`, active actor state, current tenant ownership, active tenant lineage, and current tenant governance before it is allowed to restore session cookies.
- `SuperAdminAuthService.RefreshAsync(...)` now re-runs current governance login eligibility before issuing replacement platform tokens; governance-restricted refresh attempts fail closed, clear the stored current refresh authority, and emit bounded audit/log diagnostics instead of silently rotating.
- Legacy raw POS refresh tokens and legacy access tokens without `pos_session` fail closed at rollout and require a new sign-in. This avoids treating an access-token-visible session binding as a refresh credential.

## Session Replay And Revocation Findings

The certified POS model is deliberately single-current-session per `User` row:

- Login, registration issuance, and refresh create a new random refresh credential, persist only its hash, and bind the new access token to that hash.
- A later login or refresh replaces the binding; older POS access tokens and refresh credentials no longer match SQL authority.
- Logout clears the binding; the access token used to log out and its refresh token fail closed immediately after SQL commit.
- If SQL revocation is temporarily unavailable during logout, the endpoint fails closed with `503` so clients and operators do not misclassify local cookie deletion as durable session invalidation.
- `Users.RefreshToken` is an EF concurrency token. Concurrent refresh, login, or logout updates cannot silently overwrite a session rotation after reading stale binding state; refresh concurrency is rejected as `REFRESH_REPLAY_REJECTED`.
- A replayed old refresh credential after successful rotation is rejected. This pass does not add a refresh-token family table that revokes the successfully rotated successor merely because a stale predecessor was attempted.
- Cached registration replay is evidence only. Replayed onboarding success may restore a session only while the same current SQL owner row, tenant ownership, active tenant lineage, and tenant governance still authorize login.
- Governance-restricted super-admin refresh attempts now fail closed before token issuance and revoke the currently stored platform refresh authority so suspended, banned, or financially locked platform actors cannot keep rotating from stale refresh state.
- Super-admin governance actions that move a platform actor away from `Active` now revoke the actor's current stored refresh authority and increment token version through the existing refresh-clear path.

Scenarios validated in focused tests:

- current SQL-bound POS session accepted;
- rotated or logged-out access session rejected;
- replay of the old refresh credential rejected after rotation;
- stale role claim rejected after SQL role change;
- inactive actor rejected;
- inactive tenant lineage rejected;
- current vendor governance ban rejected;
- hashed refresh persistence and access-token session binding issuance verified.
- relational two-writer session rotation verified to reject the stale writer through the SQL concurrency boundary rather than resurrect its binding.
- revoked-session rejection verified to emit bounded `pos_session` operational authority telemetry without credential-bearing labels.
- logout persistence failure verified to return `SESSION_REVOCATION_FAILED` while leaving current SQL session authority unchanged.

## Admin And Super-Admin Authority Guarantees

- POS JWT role and tenant claims are evidence only. Every successfully authenticated POS access token now passes current SQL session/actor validation; privileged role endpoints additionally retain `AdminTenantAuthority`.
- Tenant-admin operational controls continue to use trusted tenant context and SQL actor authority rather than raw `X-Tenant-Id` input.
- Super-admin tokens remain governed separately: current SQL `SuperAdmins`, current token version, and governance profile gate every `RequireSuperAdmin` request, followed by request-level audit for non-read platform operations.
- Super-admin refresh issuance is now held to the same current governance boundary as request-time platform authority. A still-valid refresh credential is not enough to mint a new platform session after governance restriction.
- Global/all-tenant recovery remains intentionally available only through explicit platform authority or documented runbook scope; POS session hardening does not widen tenant admin scope.

## Operational Identity Auditability

- POS SQL-authority rejection emits bounded `OperationalConvergenceTelemetry` classifications under `OperationalConvergenceAuthority.PosSession`.
- Diagnosable POS rejection classes include rotated/revoked session, stale role, inactive/deleted actor, inactive tenant, and governance rejection without metric labels containing user ids, token values, or raw tenant ids.
- Refresh concurrency replay and logout rotation races leave structured log breadcrumbs without logging raw credentials.
- Logout revocation persistence failure is explicit and retryable rather than silent; copied access tokens are not treated as revoked until SQL commit succeeds.
- Super-admin mutation attribution remains covered by `SuperAdminOperationalAuditMiddleware`; domain-specific financial and governance audit continues to be the stronger record where available.
- Cached onboarding session-restoration rejection and governance-blocked super-admin refresh rejection now leave bounded diagnostic evidence without logging raw access tokens, refresh credentials, or tenant-secret material.

## Support, Escalation And Impersonation Findings

- No production support impersonation endpoint was found in the inspected API surface.
- Support impersonation, tenant override, or break-glass operational identity is not certified as an available POS feature.
- `AdminAbandonedCartRecoveryController.LogRecoveryResult` is intentionally not treated as tenant-admin actor identity even though it lives under an `/api/admin/...` prefix. It is a machine callback boundary that accepts either an already-authenticated admin principal or a tenant-scoped recovery API key and rate limit. This exception is regression-frozen and must not quietly expand into broader anonymous admin behavior.
- Any future implementation must add current SQL original/effective actor authority, target tenant lineage, reason/reference and expiry/revocation semantics, request and domain audit, duplicate-action collapse, and fail-closed replay tests before tenant-scoped mutation is allowed.

## Regression Freeze Invariants

- Do not issue POS access tokens without a current SQL session binding claim or validate POS tokens using raw JWT claims alone.
- Do not persist new raw POS refresh credentials; `Users.RefreshToken` stores only the current hash.
- Do not remove concurrency protection from POS session rotation or allow refresh replay to mint multiple current sessions silently.
- Do not let logout merely clear client cookies; it must clear current SQL session authority.
- Do not allow stale actor role, deleted/disabled actor, inactive tenant, or governance-banned tenant sessions to remain authenticated for POS operations.
- Do not let cached onboarding replay mint a fresh POS session without revalidating current SQL actor activity, tenant ownership, tenant lineage, and tenant governance.
- Do not weaken `AdminTenantAuthority`, `SuperAdminPlatformAuthorityMiddleware`, trusted tenant resolution, or operational audit ordering as a consequence of session changes.
- Do not allow super-admin refresh issuance to bypass current governance login rules, and do not remove refresh revocation from governance actions that move a platform actor out of `Active`.
- Do not add new anonymous `/api/admin/*` actions without an explicit reviewed machine-auth or platform-auth boundary, focused tests, and documentation. The current abandoned-cart recovery callback is a named exception, not a pattern.
- Do not add support impersonation, tenant override, global repair, or emergency identity shortcuts without current SQL authority, explicit scope, audit, replay semantics, tests, and documentation in the same change.

## Remaining Identity-Risk Debt

- This is a focused POS admin/session hardening pass, not enterprise IAM, SSO, MFA, ABAC, device inventory, or a SIEM-grade identity audit store.
- POS intentionally has one current refresh-backed session per user. Per-device concurrent session management and operator-visible session inventory were not introduced.
- Refresh replay rejects stale credentials but does not maintain token-family history or automatically treat replay as credential-compromise revocation of an already-issued successor session.
- No ordinary POS password-change/reset or tenant-admin user-session management ceremony was found in the inspected production auth controller. A future password/reset/admin-disable management flow must clear or rotate the current binding and add stale-session tests.
- One reviewed machine callback currently shares an `/api/admin/...` route prefix while using tenant-scoped API-key authentication instead of tenant-admin actor identity. It is frozen as an explicit exception, but the cleaner long-term shape is to move non-admin machine callbacks onto a dedicated non-admin operational path or explicit service-auth scheme.
- Affiliate portal authentication is a separate non-POS-admin identity surface and still warrants its own refresh/session certification pass; this pass does not claim it shares the POS operational-authority boundary.
- No certified production support impersonation or break-glass workflow exists; this remains prohibited without a reviewed ceremony.
- Tenant lifecycle semantics outside the existing active/deleted lineage and governance boundaries remain owned by tenant-isolation certification; this pass does not redesign tenant status modeling.

## 2026-05-26 Re-Certification Addendum

- Re-checked the live POS bearer, tenant-admin, and super-admin authority paths against this document and the operational runbooks.
- Found and closed two current-state authority gaps outside the baseline bearer boundary: cached onboarding replay could restore a POS session without rechecking current SQL owner and tenant authority, and super-admin refresh could rotate a fresh platform session without rechecking current governance eligibility.
- Added focused regression coverage proving governance-restricted super-admin refresh revokes current platform refresh authority, super-admin governance restriction clears stored platform refresh state, cached onboarding replay fails closed for inactive owners, and cached onboarding replay fails closed for tenant-governance login denial.
- The current implementation now rejects raw JWT authority, stale SQL session bindings, expired current bindings, role drift, inactive/deleted users, inactive tenants, vendor governance bans, stale super-admin token versions, governance-restricted super-admin refresh reuse, and stale onboarding-session restoration after actor or tenant drift.
- Remaining debt is unchanged: no enterprise IAM/SSO/MFA/ABAC, POS per-device session inventory, refresh-family compromise table, POS password-reset/session-management ceremony, support impersonation, or affiliate-auth certification was added in this pass.
