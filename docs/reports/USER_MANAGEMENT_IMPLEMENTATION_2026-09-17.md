# User Management Implementation Report

Date: 2026-09-17
Role: MUSE Spark 1.3, implementation engineer
Authoritative inputs:
- `docs/reports/USER_MANAGEMENT_DISCOVERY_AND_IMPLEMENTATION_BLUEPRINT_2026-09-17.md`
- `docs/reports/USER_MANAGEMENT_SECURITY_REMEDIATION_2026-09-17.md`

## 1. Executive Summary

Waves 1–3 were found substantially implemented in the working tree on top of the
completed SOL security remediation. A deep production-readiness review (backend,
frontend, and migration verification) identified a set of release-blocking
findings. One migration-level finding matches the mission's mandatory STOP
condition (unexpected migration drift), so the implementation is reported as
BLOCKED pending an explicit disposition; code-level hardening beyond the stop
condition was paused so SOL reviews a coherent state.

Work completed in this session:
- Both authoritative documents were read in full before any modification.
- Independent backend and frontend production-readiness reviews were executed.
- Two stale architecture certification tests were corrected to the permission
  policy authority (previously failing: role-attribute assertions and a
  role-based frontend guard assertion).
- All bounded quality gates that could run without a database target were run.

## 2. Blueprint Compliance

| Blueprint area | Status |
|---|---|
| Wave 1 schema (normalized identity, xmin, Tenant FK, token table) | Implemented; one migration/model divergence found (see §5) |
| 56-permission catalog, role matrix, scheme-bound policies | Implemented and test-pinned |
| Wave 2 lifecycle API (all 13 admin routes + 2 anonymous ceremonies) | Implemented; fixes identified (see §8) |
| Wave 3 Users UI, EN/AR, RTL, responsive | Implemented; fixes identified (see §14) |
| Wave 4/5 | Not implemented (correct) |

## 3. Waves Implemented

- Wave 1 — User authority/schema foundation: present.
- Wave 2 — User lifecycle API: present.
- Wave 3 — POS Admin Users UI: present.

## 4. Files Changed (this session)

- `PointOfSale.Tests/Architecture/PosPermissionAuthorityCertificationTests.cs` —
  replaced two pre-permission-era assertions that failed against the current
  architecture:
  - `RepresentativeAdministrativeEndpoints_MustDeclareBackendRoles` expected a
    class-level `Roles` attribute on `AuditLogsController`, which R2 moved to
    `audit.view`. Updated to pin the permission policy for AuditLogs and the
    Owner/Admin role gates still intentionally declared by the commerce/affiliate
    controllers (permission layering is separately certified by
    `PosEndpointPermissionManifestTests`).
  - `FrontendPermissionGuards_MustRemainPresentationOnly` asserted the removed
    `requiredRoles.includes(userRole)` role API; updated to assert
    `hasPosPermission` presence and role-API absence.
- This report.

All other user-management changes were already in the working tree before this
session (listed in Section 18 of the blueprint and reflected in `git status`).

## 5. Database Migration

Migration: `20260917135014_PosUserManagementAuthority`.

**STOP CONDITION TRIGGERED — migration/model drift confirmed (mission §38):**

- The migration deliberately retains the legacy `IX_Users_TenantId` index and
  adds `IX_Users_TenantId_SoleOwner` + `IX_Users_TenantList`
  (`20260917135014_PosUserManagementAuthority.cs:18-19, 179-189`).
- The EF model and snapshot contain only the SoleOwner and TenantList indexes;
  no separate `IX_Users_TenantId` index exists in the model
  (`UserConfiguration.cs:80-95`; snapshot `ApplicationDbContextModelSnapshot.cs:21892-21906`).
- `dotnet ef migrations has-pending-model-changes` reports PASS because the
  model and snapshot agree; the drift is between the deployed migration result
  and the model metadata. A future migration scaffolded from this model would
  silently drop `IX_Users_TenantId` (breaking the blueprint §11.3 requirement
  that it remain), and the deployed index set cannot be re-derived from the
  model.
- Related drift: the migration adds `NormalizedUsername`/`NormalizedEmail` as
  required columns with `defaultValue: ""` rather than the blueprint §12.2
  nullable → backfill → required sequence.

Per the mission (§38: do not rewrite history, do not modify old migrations, do
not guess), this drift is reported, not silently corrected. Resolution options
require an explicit decision: (a) add a model-declared non-unique TenantId
index name in the model and snapshot, or (b) revise the new migration (not
history) to align with the blueprint's nullable/backfill/required sequence.
Neither was performed unilaterally.

Also confirmed (see §8): the migration's Owner-cardinality guard cannot detect
tenants with zero Owners (`HAVING COUNT(*) <> 1` forms no group for empty
sets), while the preflight script's companion query exists but is labeled
informational and is not enforced by the migration.

## 6. User Schema

Verified present: `NormalizedUsername`/`NormalizedEmail` (required, max 100/200),
`RowVersion` mapped to PostgreSQL `xmin` (`ApplicationDbContext.cs:5942` area),
`InvitationAcceptedAtUtc`, `InvitedAtUtc`, `InvitedByUserId`, `DisabledAtUtc`,
`DisabledByUserId`, `DisableReason`, restrictive `FK_Users_Tenants_TenantId`,
unique normalized identity indexes, partial unique sole-Owner index
(`IsDeleted = FALSE AND Role = 0`), composite TenantList index, and
`PosUserSecurityToken` with purposes `Invitation`/`PasswordReset`, unique
`TokenHash`, timestamp check constraints, restrictive User FK, and token
indexes.

## 7. Token Security

Verified: 48 random bytes, SHA-256 hash-only persistence
(`PosUserManagementService.HashToken`), raw tokens never stored or logged, no
password/token material in audit payloads, single-use + revocation + expiry +
purpose binding, acceptance issues no POS session, reset completion clears the
refresh binding and no replacement session is issued.

Open issue (review finding, unresolved): ceremony token validation happens
before the transaction begins; token consumption is not serialized against
concurrent revocation/issuance for the same user. Recommended fix: perform
validation + consumption inside one transaction that locks the same user row
used by issuance/resend/revocation, with conditional/atomic consumption.

## 8. User Lifecycle

Verified: invite creates inactive, unverified, non-Owner users with an
unknowable random password; acceptance marks verified, activates, consumes
outstanding tokens, creates no session; disable requires reason and revokes
session; enable issues no session; role change enforces Owner>Admin target
ranks and revokes the target session; ownership transfer is one transaction
with row locks, demote-then-promote under the partial unique index, both
sessions revoked, and a single audit record.

Open issues (review findings, unresolved):

1. **Legacy-user eligibility (critical):** lifecycle status and enable/reset/
   transfer eligibility require `InvitationAcceptedAtUtc`, which is null for
   all pre-migration users and for Owners created by
   `TenantRegistrationService.RegisterTenantAsync`. Existing active users
   present as Disabled, existing disabled users cannot be enabled, and reset/
   transfer target them fails. The blueprint's status table derives Active from
   `IsActive && !IsDeleted`, so eligibility should use invitation provenance
   (e.g., `InvitedAtUtc != null`) rather than requiring an acceptance timestamp
   for non-invitation accounts. Requires an approved treatment for legacy rows;
   no historical acceptance dates were invented.
2. **Email change does not revoke outstanding invitation/reset tokens**, so a
   previously delivered link remains credential authority for an address that
   changed; acceptance would verify the new email without proof of control.
3. **Five mutations lack If-Match** (resend invitation, revoke invitation,
   password-reset issuance, session revoke, ownership transfer). The blueprint
   §13.2 wording covers "mutable existing-user operations"; the controller
   currently enforces versions only on profile/role/status.
4. **Migration Owner-cardinality guard misses zero-Owner tenants** (§5).
5. Ownership-transfer audit payload records incorrect before/after values
   (`oldOwnerId = target.Id`; before-role captured after mutation), and the
   transfer's audit row is indexed only under the new Owner's `EntityId`, so
   the old Owner's user-audit read omits it.
6. Transfer row locks are not tenant-scoped (foreign IDs are locked before
   tenant rejection) and lock order is not deterministic.
7. Clearing refresh fields before save suppresses the interceptor's
   `PRIVILEGED_REFRESH_REVOKED` evidence for those mutations (it sees an
   already-empty binding), weakening the blueprint §15 evidence requirement.

## 9. API Contracts

All blueprint §13 routes exist with matching methods and permissions:
`GET/POST /api/admin/users`, `GET /{userId}`, `GET /roles`,
`GET /{userId}/audit` (audit.view), `POST /invitations`,
`POST /{userId}/invitation/resend`, `DELETE /{userId}/invitation`,
`PATCH /{userId}/profile`, `PUT /{userId}/role`, `PUT /{userId}/status`,
`POST /{userId}/password-reset`, `POST /{userId}/session/revoke`,
`POST /ownership-transfer`, `POST /api/auth/user-invitations/accept`,
`POST /api/auth/password-reset/complete`. Rate limits are attached to
invitation, reset, and token-acceptance operations; responses use
`ApiErrorResponse` codes.

## 10. Authorization

Every `AdminUsersController` action carries an explicit permission policy; the
56-permission catalog matches blueprint §9 exactly (verified by tests); the
service enforces Owner→Admin/Manager/Cashier/Warehouse and
Admin→Manager/Cashier/Warehouse target ranks, self-mutation denial, and the
no-second-Owner invariant; `users.ownership.transfer` is Owner-only. One open
finding: policy-level denials currently surface the default 403 handling rather
than a `POS_PERMISSION_FORBIDDEN` error envelope; the service-level denials do
produce the required code.

## 11. Tenant Isolation

Managed tenant always comes from `ITenantContext.RequiredTenantId`; no request
DTO accepts TenantId/role/permission/actor fields; tenant-filtered lookups
return `USER_NOT_FOUND` for foreign-tenant IDs (no existence leak); cross-
tenant transfer targets are rejected. Global lookups exist only for
normalized-identity uniqueness and hash-bound token discovery, and anonymous
consumption validates token/user tenant equality.

## 12. Session Revocation

Reuses `PrivilegedRefreshTokenRevocationInterceptor` and `PosSessionAuthority`
unchanged; the interceptor now also revokes on `PasswordHash` change
(blueprint §10.6). One open evidence issue is listed in §8.7.

## 13. Audit

All 12 required `POS_USER_*` action types are emitted, transactionally with the
mutation, with no password/hash/token material in payloads. Open items:
transfer audit payload correctness and both-participant indexing (§8.5),
interceptor evidence suppression (§8.7), and user-audit reads currently return
all `User`-entity audit rows rather than the user-management subset only.

## 14. Frontend

Verified: `/users` route under `PosPermissionRouteGuard` (`users.view`),
permission-filtered sidebar, summary cards, filters, server pagination (max
100), sorting, invite dialog (no Owner option, invitation explanation, no
password field), profile/role/status separation with If-Match, disable reason +
typed confirmation, safe reset UI, "Sign out current session" labeling,
Owner-only ownership-transfer ceremony with typed confirmation, anonymous
accept/reset pages, EN/AR key symmetry (automated), no localStorage persistence
of user data, reuse of the shared Axios client with CSRF/cookies.

Open issues (review findings, unresolved):

1. Search is not effectively debounced (each keystroke fires a request with the
   previous debounced value) and canceled requests can clear loading state.
2. 412 recovery adopts the new version without showing the operator the
   current server values or preserving review of the draft.
3. Mutation 403s do not trigger session-authority refresh.
4. Arabic leakage: raw permission names in the role dialog, raw audit action
   codes/descriptions on the page, hardcoded "Close" accessible labels, and
   `DISABLE`/`TRANSFER` confirmation strings left in English in `ar.json`.
5. The Axios 401-refresh branch replays non-idempotent mutations (invite, reset
   issuance, ownership transfer), violating the no-auto-retry rule.
6. Dialogs/menus lack focus trap/restore and menu keyboard navigation; Radix
   primitives are already installed and should be used.
7. TypeScript errors in `UsersManagementPage.test.tsx` (implicit any /
   possibly-undefined), plus pre-existing tsc issues; ESLint 9 config absent
   (pre-existing).

## 15. Localization

`userManagement` namespaces are symmetric between `en.json` and `ar.json`
(automated test passes), but the four Arabic-leakage items in §14.4 remain.

## 16. Tests

Backend focused (Users + permissions + session projection): **69/69 passed**
(this session, `--filter "PosUserManagement|PosPermission|PosEndpointPermission|PosSessionPermission"`).
Frontend focused (5 Users test files): **24/24 passed**.
Backend test gaps identified: token concurrency, real cross-tenant persisted
targets, expired/revoked/wrong-purpose reset completion, legacy-user
eligibility, live-HTTP role matrix, race tests for 412, both-participant
transfer audit assertions. Frontend gaps: stale-request/debounce behavior,
403-refresh, submission-lock across dialogs, RTL dialog rendering, viewport
overflow.

## 17. PostgreSQL Verification

Not performed. No approved production-equivalent database target exists in this
session; the mission forbids guessing a database. `UserManagementPreflight.sql`
exists (5 read-only checks; check 5 is informational and not enforced by the
migration — see §5/§8.4). Npgsql metadata tests pass; live `xmin`, FK,
partial-index, transaction, and lock behavior remain uncertified.

## 18. Build Verification

| Gate | Result |
|---|---|
| .NET solution Debug build | PASS (0 errors, 0 warnings) |
| API Release build | PASS (0 errors; 189 pre-existing warnings in untouched files) |
| Focused backend tests | PASS 69/69 |
| Focused frontend tests (5 Users files) | PASS 24/24 |
| Frontend production build (`vite build`) | PASS (~1m22s; pre-existing chunk-size warning) |
| `dotnet ef migrations has-pending-model-changes` | PASS (model ≡ snapshot) |
| `git diff --check` | PASS |
| Full backend test suite | **INCOMPLETE** — aborted by the user after ~13 minutes; not clean-certifiable this session. Observed during the run (unverified relation to this work): SQLite `btrim` failures in Affiliate/Coupon SQLite fixtures and several pre-existing documentation-freeze architecture tests. |
| Full frontend suite | NOT RUN (time-bounded session; 9 pre-existing failures documented by SOL's remediation report remain the baseline) |
| ESLint | NOT APPLICABLE (no ESLint config in repo, pre-existing) |
| Secret scan | Not re-run this session; SOL's remediation reported clean |

## 19. Migration Verification

`has-pending-model-changes` passes, but §5 documents migration-result vs model
drift that this check cannot see. Deployment of
`PosUserManagementAuthority` remains blocked pending the §5 disposition and
production preflight on an approved target.

## 20. Known Failures

1. Migration/model drift (`IX_Users_TenantId` retained by migration only;
   required-with-default normalized columns) — STOP condition, §5.
2. Migration Owner-cardinality guard misses zero-Owner tenants — §8.4.
3. Legacy-user lifecycle eligibility defect — §8.1 (release blocker).
4. Token lifecycle not serialized — §7.
5. Email change leaves outstanding tokens valid — §8.2.
6. Missing If-Match on five mutations — §8.3.
7. Ownership-transfer audit payload/indexing — §8.5.
8. Non-scoped, unordered transfer locks — §8.6.
9. Suppressed interceptor revocation evidence — §8.7.
10. Policy-denial responses lack `POS_PERMISSION_FORBIDDEN` envelope — §10.
11. Frontend items in §14 (debounce/cancellation, 412 UX, 403 refresh,
    mutation replay, Arabic leakage, focus management, test type errors).

## 21. Deviations From Blueprint

1. **Migration column strategy**: blueprint §12.2 requires
   nullable → backfill → required; the migration ships required-with-default
   columns and a backfill guarded by `WHERE "NormalizedUsername" = ''`.
   Deviation also manifests as the §5 drift. Disposition: requires the explicit
   §5 decision; not silently reinterpreted.
2. **Normalization equivalence**: blueprint §12.2 requires backfill "with the
   exact application normalizer"; the migration uses SQL `UPPER(BTRIM())`,
   which differs from .NET `Trim().ToUpperInvariant()` for non-ASCII/Unicode
   whitespace edge cases. Disposition: flagged for a decision (produce backfill
   via the normalizer or prove equivalence); login/uniqueness currently rely on
   the application normalizer while the database keys are backfilled by SQL.
3. **If-Match scope**: five mutating endpoints do not take If-Match (§8.3) —
   implementation deviation from §13.2's mutable-operations wording; listed as
   a defect, not a design choice.
4. **Zero-Owner migration guard**: preflight script includes a zero-Owner
   query (informational), but the migration guard cannot fail on zero-Owner
   tenants; blueprint §12.1 requires exactly-one. Disposition: fix required.
5. **Session revocation labeling and ceremony semantics** follow the blueprint;
   no deviation found. All other blueprint deviations known at this time are
   the open issues in §8/§14 rather than intentional reinterpretations.

## 22. Deferred Items

- Wave 4 endpoint-wide permission hardening (mission scope boundary).
- Wave 5 capabilities (custom roles, memberships, SSO/MFA/SCIM, device
  sessions, impersonation) — not implemented.
- Live-HTTP per-endpoint role matrix and headed browser matrix (no harness;
  same limitation SOL recorded).
- Real-PostgreSQL integration certification (no approved database target).
- Reauthentication ceremony for ownership transfer (blueprint recommends;
  architecture does not currently provide a POS reauth ceremony).

## 23. SOL Review Checklist

1. Adjudicate the §5 migration drift (index model representation + column
   nullability sequence) before any deployment; confirm the migration should
   be revised rather than history rewritten.
2. Approve the legacy-user eligibility treatment (§8.1) and the email-change
   token revocation (§8.2).
3. Approve If-Match preconditions for the five mutations and both transfer
   participants (§8.3), plus 412 `USER_VERSION_CONFLICT` mapping in
   `GlobalExceptionMiddleware` for admin concurrency failures.
4. Approve bounded expiry options and token-consumption serialization (§7).
5. Decide whether policy-denied requests must return the
   `POS_PERMISSION_FORBIDDEN` envelope (currently default 403 body).
6. Approve the frontend fixes in §14 and the test-gap priorities in §16.
7. Run the read-only preflight (`UserManagementPreflight.sql`) against a
   production-equivalent target; treat zero-Owner output as a blocker, not
   informational.
8. Re-run the full quality gates after fixes; the full .NET suite was not
   completed in this session and must not be assumed clean.

## 24. Final Status

**BLOCKED — mandatory stop condition triggered: confirmed migration drift
(migration retains `IX_Users_TenantId` and uses required-with-default
normalized columns while the EF model/snapshot define neither; migration
Owner-cardinality guard misses zero-Owner tenants). Production fixes beyond
the already-verified stale-test correction were paused per mission §38/§39
(no history rewrite, no guessing, no silent reinterpretation). Resolution
requires: (1) an explicit migration disposition, and (2) an approved
production-equivalent database target for the read-only preflight. Secondary
release blockers are itemized in §20.**

IMPLEMENTED — AWAITING SOL INDEPENDENT REVIEW is **not** claimed, because the
stop condition and the §20 defect list stand between the current tree and the
mission's definition of done.
