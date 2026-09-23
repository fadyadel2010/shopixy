# User Management Security Remediation Report

Date: 2026-09-17
Role: SOL 5.6, Lead Security Engineer and Principal Backend/Frontend Architect
Scope: SECURITY REMEDIATION ONLY. No Users Management screen, lifecycle, tokens, or schema.
Authoritative input: `docs/reports/USER_MANAGEMENT_DISCOVERY_AND_IMPLEMENTATION_BLUEPRINT_2026-09-17.md`
(planning; this report is remediation + verification, not certification of a Users feature).

## 1. Executive Summary

Both Critical findings from discovery are remediated and independently tested:
(1) anonymous POS staff registration is removed — `POST /api/auth/register` now
requires `users.invite` and returns `410 Gone` (`POS_STAFF_REGISTRATION_DISABLED`)
without creating users, sessions, or cookies; legitimate tenant/business onboarding
(`POST /api/platform/register` → Owner creation) is proven distinct and preserved.
(2) Sensitive POS APIs no longer rely on bare authentication — a code-owned
56-permission catalog with the discovery role matrix, a fail-closed authorization
handler bound to SQL-revalidated POS sessions, per-operation endpoint mapping, and
scheme-bound policies are enforced via an MVC convention, guarded by a deterministic
endpoint-permission manifest test. Frontend receives server-derived
`EffectivePermissions` for navigation only; backend policies remain authoritative.
Tenant isolation, SuperAdmin/POS separation, CommerceHub, Treasury, and accounting
authorities are preserved (77/77 preservation tests). Verdict: CONDITIONAL PASS.

## 2. Scope

In scope: disable anonymous staff registration; POS permission catalog, role matrix,
requirement/handler, policies, endpoint mapping, session projection; sensitive
endpoint hardening; frontend permission parsing, route guards, gates, sidebar
filtering, audit correction, maintenance-route removal; manifest + regression tests.
Out of scope (untouched): Users UI, invite/accept/reset/ownership APIs and UI, token
tables, user schema columns (`xmin`, FK, normalized identity), migrations.

## 3. Original Findings

| # | Severity | Finding | Evidence |
|---|---|---|---|
| 1 | Critical | Anonymous callers create active `Cashier` via tenant-resolved `POST /api/auth/register` | `AuthController.cs:65-119` |
| 2 | Critical | Financial/destructive APIs use bare `[Authorize]` (Banks, FinancialAccounts, Expenses, Purchases, Sales, FinancialReports, Shipping, …) | Section 7 inventory |
| 3 | High | Most POS frontend routes authenticated-only; `PermissionGuard` role-based despite name | `App.jsx:259-326`, `PermissionGuard.jsx:18-25` |
| 4 | High | No tenant user-management API/UI | No `/users` route |
| 5 | High | No `xmin` concurrency token; no User→Tenant FK | `UserConfiguration.cs:9-47` |
| 6 | High | Case-sensitive, inconsistent identity canonicalization | `UserConfiguration.cs:42-46` |
| 7 | Medium | Audit UI (Admin/SuperAdmin) vs API (Owner/Admin) mismatch | `AuditLogsController:15-18`, `AuditLog.jsx:198-207` |

Findings 4–6 are DEFERRED to the Users waves by mission design (§14).

## 4. Root Causes

1. Registration mixed anonymous admission with direct persistence of an active staff
   identity; no invitation ceremony existed.
2. Authentication (valid session) was the final authorization decision; no
   operation-permission layer existed between `PosSessionAuthority` and controllers.
3. Frontend had role strings but no permission model; session DTO carried no
   effective permissions.
4. Audit page reused platform (`SuperAdmin`) intuitions inside the POS tree.

## 5. Remediation Architecture

`User → UserRole → PosRolePermissionCatalog (code-owned) → PosPermissionRequirement /
PosPermissionAuthorizationHandler → scheme-bound policy (POS bearer) →
controller/action enforcement (explicit policy or MVC convention)`.
Permissions are additionally gated by a `pos_sql_authority=current` marker that
`Program.cs` strips from inbound tokens and re-adds only after `PosSessionAuthority`
revalidates user, tenant, role, active/deleted state, session binding, tenant
lifecycle, and vendor governance against current SQL. Frontend: `UserDto.
EffectivePermissions` (server-derived) → validated session shape → `hasPosPermission`
→ `PosPermissionRouteGuard` / `PermissionGate` / sidebar filtering. No Identity, no
permission tables, no memberships, no second identity model, no schema change.

## 6. Anonymous Registration Remediation

`AuthController.Register` (`AuthController.cs:65-75`): `[Authorize(Policy =
users.invite)]`, synchronous, returns `410 Gone` + `POS_STAFF_REGISTRATION_DISABLED`,
touches no database, mints no tokens/cookies. Distinction proof: `Register.jsx`
calls only `/platform/register`; `TenantRegistrationService` creates tenant + Owner
(`UserRole.Owner`) atomically — untouched. Invitation acceptance is not implemented;
staff onboarding stays fail-closed until the Users waves.

## 7. Permission Foundation

- `PointOfSale.Core/Security/PosPermissions.cs`: 56 constants; `PosRolePermissionCatalog`
  implements the discovery §9 matrix exactly (Owner = all; Admin = all except
  `finance.initialize` + `users.ownership.transfer`; Manager operational subset;
  Cashier POS subset; Warehouse inventory/shipping subset). Unknown role → empty grant
  set; unknown permission → never granted.
- `PosPermissionRequirement` / `PosPermissionAuthorizationHandler`: succeed only with
  exactly one known role claim, valid POS user/tenant GUID claims, non-empty session
  binding, the SQL-authority marker, and a catalog grant. Everything else fails closed
  (forged `permission(s)` claims are never read).
- `Program.cs`: one policy per permission key, bound to `JwtBearerDefaults`
  (default POS scheme); handler registered; convention registered; SQL-authority
  marker issuance added to POS `OnTokenValidated` after successful validation.

## 8. Sensitive Endpoint Hardening

`PosEndpointPermissionCatalog` classifies ~70 POS controllers with read/write/delete
defaults plus per-action overrides (publishing, transfers, refunds, approvals,
initialization, export). `PosPermissionApplicationModelConvention` stamps the mapped
`AuthorizeFilter(policy)` on every classified action that lacks anonymous or
separate-authority metadata; explicit `[Authorize(Policy=…)]` (AuditLogs, Register)
coexists. Business/resource invariants (tenant filters, Treasury/accounting rules,
CommerceHub lifecycle, product protections) are preserved underneath as additional
layers. Incidental fixes with explicit authority (not POS permissions):
`MarketplaceReadController.GetOrders` → `CustomerJwt`+Customer (it reads customer
claims); `StorefrontOnlineCustomerController.CheckEmailExists` → documented
`[AllowAnonymous]` anti-enumeration ceremony.

## 9. Frontend Permission Foundation

`posPermissions.js` (56 known permissions, `deriveEffectivePosPermissions` rejects
unknown/duplicates/non-arrays, `hasPosPermission`); session validator requires
`effectivePermissions`; `PosPermissionRouteGuard`; `PermissionGate` + permission-based
`PermissionGuard` (role API removed); sidebar permission filtering (CommerceHub
capability intersection kept); `App.jsx` permission route groups incl. Treasury
(`reports.financial.view` vs `finance.operate` vs `finance.initialize`), audit
(`audit.view`), settings (`settings.view`); `/maintenance/reset-database` route,
Settings danger tab, and orphaned `ResetDatabase.jsx` removed (SuperAdmin controller
untouched, still disabled-by-design); `AuditLog.jsx` gated by `audit.view` with
export additionally gated by `audit.export`, SuperAdmin assumption removed;
`Register.jsx` rehydrates authoritative `/auth/session` instead of inferring a session.
No Users screen or lifecycle UI built.

## 10. Tenant Isolation Verification

The permission layer contains no tenant logic and cannot bypass isolation: managed
tenant still comes only from trusted context; no `TenantId`/`StoreId`/`Role`/
permission/actor input is trusted. Evidence: `AdminTenantOperationalScopeTests`
(foreign-tenant fail-closed: Banks, ExpenseCategories, FinancialAccounts,
ShippingMethods) + `Categories/Dashboard/FinancialReports` tenant-scope suites pass
unchanged within the 80/80 focused run; no tenant/isolation file was modified.

## 11. Privilege Escalation Verification

Handler tests prove: Cashier→`finance.admin` DENY, Warehouse→`purchases.manage` DENY,
Manager→`users.view`/`reports.financial.view` DENY, Admin→`users.ownership.transfer`
DENY, unknown role DENY, missing/duplicate role claims DENY, forged permission claims
ignored (DENY), forged Owner role without SQL marker DENY, malformed/unauthenticated
DENY, SuperAdmin-shaped and session-less principals DENY, authorized combinations
ALLOW (Manager→`finance.operate`, Warehouse→`shipping.fulfill`,
Cashier→`sales.operate`).

## 12. SuperAdmin/POS Separation

Preserved: dedicated SuperAdmin scheme/policy/claims validator, `TenantsController`
and `MaintenanceController` SuperAdmin-only, separate SuperAdmin route tree and
capabilities. Handler denies SuperAdmin-shaped principals for POS permissions;
`SuperAdminAuthorizationTests` + `SuperAdminPlatformAuthorityTests` pass.

## 13. CommerceHub Preservation

CommerceHub customer/storefront/affiliate schemes, vendor lifecycle authority,
navigation capabilities, and contextual guards are untouched; POS permission guards
intersect (never replace) capability guards on CommerceHub routes. Evidence:
`CommerceHubVendorOrderActionAuthorityTests`, `CommerceHubDataAuthorityBoundaryTests`,
`CommerceHubAdminSessionSecurityCertificationTests`, `DynamicCheckoutAuthorizationTests`
pass; frontend `posVendorSessionAuthority` capability logic unchanged (11/11 tests).

## 14. Audit Authorization Correction

Backend: `AuditLogsController` class → `audit.view`, `ExportAuditLogs` →
`audit.export` (previously `Owner,Admin` roles). Frontend: `AuditLog.jsx` admits
`audit.view` (Owner AND Admin per matrix — Owner was previously excluded) and no
longer references `SuperAdmin`. Export button additionally requires `audit.export`.

## 15. Endpoint Manifest

`PosEndpointPermissionManifestTests.SensitivePosActions_MustResolveToKnownPermissionPolicies`
enumerates every controller action in the API assembly: anonymous actions must be
approved (with `AuthController.Register` explicitly forbidden from anonymity);
permission-policy or approved separate-authority (SuperAdmin namespace, non-POS
scheme/policy) actions pass; all remaining authenticated POS actions must resolve
through `PosEndpointPermissionCatalog` to a known permission; every catalog entry
must cover at least one real action. Representative mappings (`Banks`, `Purchases`,
`Sales`, `FinancialAccounts`, `FinancialReports`) are pinned; runtime wiring
(convention + scheme-bound policies + handler) is asserted from `Program.cs` source.

## 16. Tests

New (24 .NET + 3 frontend): `PosPermissionAuthorizationTests` (12),
`PosEndpointPermissionManifestTests` (8), `PosSessionPermissionProjectionTests` (4:
server-derived projection, no client-supplied permissions, exact backend↔frontend
catalog parity, no browser-storage persistence),
`PosPermissionRouteGuard.test.tsx` (2), +1 session-permission case in
`posVendorSessionAuthority.test.ts`. Updated: registration hardening tests (4 files),
treasury/IA frontend assertions (3 files), session fixtures (4 files).
Focused .NET: 80/80. Preservation .NET: 77/77. Frontend focused: 94/94 + 15/15.
Full frontend: 1368/1377 — 9 failures in 6 files proven pre-existing on the pristine
tree via `git stash` (payment/footer/cart/checkout/UX/warehouse suites, untouched by
this work). No Users lifecycle tests (nothing to test yet).

## 17. Build/Static Verification

API Debug: 0 errors. API Release: 0 errors (189 warnings, all pre-existing in
untouched files; zero in new/edited files). Frontend `vite build`: PASS.
`git diff --check`: PASS. Secret scan (diff + new files, key/token patterns): clean.
ESLint: NOT APPLICABLE (no eslint config exists in repo — pre-existing).
EF pending-model: PASS by construction (zero entity/DbContext/configuration files
touched; no migration needed or created). Localization: no i18n keys added/removed;
`en`/`ar` symmetry untouched (unused `databaseReset` keys retained intentionally).

## 18. Changed Files

Required security remediation (backend): `AuthController.cs`, `Program.cs`,
`PosSessionAuthority.cs`, `AuditLogsController.cs`, `MarketplaceReadController.cs`,
`StorefrontOnlineCustomerController.cs`, NEW `Core/Security/PosPermissions.cs`,
NEW `Security/PosPermissionRequirement.cs`,
NEW `Security/PosPermissionAuthorizationHandler.cs`,
NEW `Security/PosEndpointPermissionCatalog.cs`,
NEW `Security/PosPermissionApplicationModelConvention.cs`.
Required security remediation (frontend): `App.jsx`, `Sidebar.jsx`,
`PermissionGuard.jsx`, `AuditLog.jsx`, `Register.jsx`, `Settings.jsx`,
deleted `ResetDatabase.jsx`, NEW `auth/posPermissions.js`,
NEW `components/PosPermissionRouteGuard.jsx`.
Required tests: `ProductionSecurityHardeningTests.cs`,
`PosAuthenticationLifecycleTests.cs`,
`AdminAuthenticationSessionCertificationTests.cs`,
`PosAdministrativeAuthorityCertificationTests.cs`,
NEW `PosPermissionAuthorizationTests.cs`, NEW `PosEndpointPermissionManifestTests.cs`,
NEW `PosSessionPermissionProjectionTests.cs`, `posVendorSessionAuthority.js` +
`.test.ts`, `AuthBootstrap.test.tsx`, `CommerceHubVendorRouteGuard.test.tsx`,
NEW `PosPermissionRouteGuard.test.tsx`, `treasuryInitialCapitalizationAuthority.test.ts`,
`storefrontInformationArchitecture.test.tsx`,
`wave1StorefrontInformationArchitecture.test.tsx`.
Required architecture support: none beyond the above (no schema, no infra).
Docs: discovery report §R1–R5 + status table; this report. No unrelated refactoring.

## 19. Remaining Findings

DEFERRED by design: Users API/UI, invitation/acceptance, password reset, role/status
management, ownership-transfer ceremony (permission reserved, ceremony absent),
`xmin`/FK/normalized-identity schema waves. Operational note: staff onboarding is
disabled until invitation acceptance ships; tenant onboarding works normally.
Limitations: no live-HTTP per-endpoint 401/403 run (no harness in repo — proven at
handler/metadata/wiring level); no headed browser matrix run.

## 20. Updated Users Blueprint Impact

Wave 0 exit gate ("no unauthenticated path can create POS staff") is met. The
permission foundation Wave 1 asked for exists (catalog, handler, policies, session
projection, manifest). Subsequent waves can proceed without re-architecting:
`users.*` permissions (incl. Owner-only `users.ownership.transfer`) are reserved and
tested; `AuthController.Register` is the documented placeholder the invitation
acceptance endpoint will replace. No correction to the discovery matrix was needed.

## 21. Security Status

| Finding | Severity | Remediated | Independently Tested | Status |
|---|---|---|---|---|
| Anonymous POS staff registration | Critical | Yes | Yes | VERIFIED |
| Bare authorization on sensitive POS APIs | Critical | Yes | Yes | VERIFIED |
| Frontend routes authenticated-only / role-based guard | High | Yes (foundation) | Yes | VERIFIED |
| Audit UI/API authority mismatch | Medium | Yes | Yes | VERIFIED |
| User-management API/UI, schema, ceremonies | High/— | No (out of scope) | N/A | DEFERRED |

STATUS: CONDITIONAL PASS. Critical findings: 2/2 closed. In-scope High: 1/1
(foundation) + 2 deferred by mission design. Focused tests: 80/80 .NET, 77/77
preservation, 1368/1377 frontend (9 pre-existing, proven). Build: PASS (Debug +
Release + frontend). EF pending model: PASS. Frontend: PASS modulo pre-existing
failures. Remaining blockers: none for this phase; staff onboarding awaits Users
waves. Conditions: pre-existing unrelated failures documented; live-HTTP and browser
matrix verification outstanding (no harness in repo).

## 22. Evidence Index

- Disabled registration: `AuthController.cs:65-75`; tests
  `ProductionSecurityHardeningTests` (anonymous/disabled cases),
  `PosAuthenticationLifecycleTests.Register_IsDisabled…`.
- Catalog/matrix: `Core/Security/PosPermissions.cs`; test
  `Catalog_HasUniqueKnownPermissionsAndExplicitGrants`.
- Handler fail-closed: `Security/PosPermissionAuthorizationHandler.cs`; 12 handler tests.
- Wiring: `Program.cs` (convention + 56 policies + SQL marker);
  `Security/PosPermissionApplicationModelConvention.cs`;
  `Security/PosEndpointPermissionCatalog.cs`; manifest tests (8).
- Session projection: `AuthController` UserDto `EffectivePermissions`;
  `PosSessionPermissionProjectionTests` (4); frontend `posPermissions.js` + tests.
- Frontend guards: `App.jsx`, `Sidebar.jsx`, `PermissionGuard.jsx`,
  `PosPermissionRouteGuard.jsx`, `AuditLog.jsx`, `Register.jsx`, `Settings.jsx`.
- Preservation: 77/77 (SuperAdmin, CommerceHub, accounting, session, onboarding).
- Tenant isolation: `AdminTenantOperationalScopeTests` + tenant-scope suites (in 80/80).
- Builds/scans: API Debug + Release 0 errors; `vite build` PASS; `git diff --check`
  PASS; secret scan clean; full outputs in tool logs.
- Pre-existing failures: pristine-tree `git stash` run — same 6 files / 9 tests fail
  without this work (`paymentFlow`, `footerAuthoringContract`,
  `storefrontCartStateResolver`, `storefrontPaymentFlow`, `storefrontUxStateStability`,
  `warehouseFrontendCertification`).
