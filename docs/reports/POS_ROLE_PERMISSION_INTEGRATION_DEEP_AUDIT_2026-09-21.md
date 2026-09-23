# POS Role and Permission Integration Deep Audit

Date: 2026-09-21  
Scope: POS authentication, SQL session authority, role-derived permissions, protected API actions, frontend routes, navigation, and action controls  
Result: **Scoped conditional pass**

## 1. Executive Decision

The audited POS authorization path is now internally consistent and fail-closed. The reported Cashier warehouse-selector failure was not a stale session or tenant-resolution defect; it was an integration defect in which the POS screen called the warehouse-administration read endpoint, which correctly requires `inventory.view` and correctly excludes Cashier.

The remediation adds a minimal sales-scoped warehouse-option endpoint, removes conflicting legacy role restrictions, aligns action-specific permissions, and projects those permissions into frontend controls. Authorization-specific tests pass. Production-wide certification remains conditional because six unrelated security tests fail and no live browser/runtime role walkthrough was performed.

## 2. Audit Scope

Reviewed surfaces:

- The 56-permission POS catalog and all five POS roles.
- JWT validation and current SQL session revalidation.
- Endpoint-to-permission classification and policy installation.
- Legacy `[Authorize(Roles = ...)]` intersections.
- Current-session permission projection to the web client.
- React route guards, sidebar visibility, and sensitive action controls.
- The POS warehouse selector incident and tenant isolation.
- Role/status/session revocation behavior.
- Relevant .NET and frontend regression evidence.

Excluded from a positive runtime certification:

- Live browser execution for all five roles.
- Production infrastructure and deployed identity-provider behavior.
- Unrelated storefront checkout, theme authoring, sanitizer, and SQLite concurrency defects.

## 3. Repository State

The worktree contained extensive pre-existing modified, deleted, and untracked files. This audit did not reset or revert unrelated work. Findings and test results therefore describe the current shared worktree, not a clean baseline commit.

## 4. Evidence Reviewed

Primary implementation evidence:

- `PointOfSale.Core/Security/PosPermissions.cs`
- `PointOfSale.API/Security/PosEndpointPermissionCatalog.cs`
- `PointOfSale.API/Security/PosPermissionApplicationModelConvention.cs`
- `PointOfSale.API/Security/PosPermissionAuthorizationHandler.cs`
- `PointOfSale.API/Security/PosSessionAuthority.cs`
- `PointOfSale.API/Controllers/AuthController.cs`
- `PointOfSale.API/Services/Users/PosUserManagementService.cs`
- `PointOfSale.Web/src/auth/posPermissions.js`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/components/AuthBootstrap.jsx`
- `PointOfSale.Web/src/App.jsx`
- `PointOfSale.Web/src/components/Sidebar.jsx`

Available user-management reports were also reviewed, including the discovery blueprint, implementation report, and security-remediation report dated 2026-09-17.

## 5. Missing Evidence Artifact

The requested file `docs/reports/USER_MANAGEMENT_INDEPENDENT_REVIEW_AND_CERTIFICATION_2026-09-17.md` is absent. No matching renamed independent-review report was found. This absence is an evidence gap and prevents claiming that this audit relied on that independent certification.

## 6. Threat Model

The audit tested or inspected these authority failures:

- Forged role or permission claims.
- Stale roles after a role change.
- Revoked or rotated sessions.
- Disabled/deleted users and inactive tenants.
- Cross-tenant warehouse disclosure.
- A frontend route or control admitting an operation denied by the API.
- A legacy role attribute silently narrowing a canonical permission grant.
- Unknown permissions or unclassified authenticated actions.

## 7. Canonical Authority Chain

The authoritative request path is:

1. The bearer token identifies the candidate user, tenant, role, and session binding.
2. `PosSessionAuthority` reloads current SQL user state and validates active status, tenant identity, current role, current refresh-token binding, tenant lineage, and governance status.
3. Successful SQL validation adds the trusted `pos_sql_authority=current` marker.
4. The endpoint policy requires one known POS permission.
5. `PosPermissionAuthorizationHandler` requires exactly one known role, the SQL marker, a valid user/tenant identity, and a session binding.
6. `PosRolePermissionCatalog.IsGranted` decides the permission from the current canonical role matrix.

The handler succeeds only at `PointOfSale.API/Security/PosPermissionAuthorizationHandler.cs:39-41`. Client-supplied permission claims are not consulted.

## 8. Permission Catalog

`PosPermissions.All` contains 56 unique permissions across dashboard, catalog, inventory, customer, supplier, sales, purchase, finance, reports, storefront, shipping, affiliate, settings, audit, and user-management domains.

No new permission was introduced for the incident fix. The repair reused `sales.operate` for the minimal POS warehouse-option dependency and retained `inventory.view` for warehouse administration.

Backend and frontend permission names are checked for exact set equality by `PosSessionPermissionProjectionTests.FrontendKnownPermissions_MatchBackendCatalogExactly`.

## 9. Canonical Role Matrix

| Role | Grant count | Canonical scope |
|---|---:|---|
| Owner | 56 | Every POS permission |
| Admin | 54 | Every permission except `finance.initialize` and `users.ownership.transfer` |
| Manager | 31 | Operational catalog, inventory, customer, supplier, sales, purchase, finance operation, operational reports, storefront operations, promotions, analytics view, and shipping |
| Cashier | 6 | Dashboard, catalog read, customer read/manage, sales read/operate |
| Warehouse | 8 | Dashboard, catalog read, inventory read/adjust/transfer, purchase read, shipping read/fulfill |

This matrix is role-derived in `PointOfSale.Core/Security/PosPermissions.cs:90-123`; it is not persisted as browser authority.

## 10. Owner Assessment

Owner receives all 56 permissions, including initialization and ownership transfer. No audited endpoint or frontend guard was found to narrow an Owner grant after the legacy-role cleanup.

## 11. Admin Assessment

Admin receives 54 permissions. The two explicit denials are security-significant:

- `finance.initialize` remains Owner-only.
- `users.ownership.transfer` remains Owner-only.

Frontend routes for initial capitalization and ownership transfer use those explicit permissions, so an Admin role label alone cannot expose either operation.

## 12. Manager Assessment

Manager receives 31 operational permissions. Important boundaries include:

- Allowed: catalog manage/publish, all inventory permissions, customer/supplier balance operations, sales refund, purchases and returns, finance view/operate, operational reports, storefront order management, payment view, promotions, analytics view, and shipping.
- Denied: catalog delete, sales delete, finance admin/initialize, financial reports, payment management/refunds, storefront configuration/design/domains/auth, affiliate administration, settings, audit, and users.

Vendor earnings was explicitly moved to `reports.financial.view`, preventing the generic storefront-order read rule from exposing financial earnings to Manager.

## 13. Cashier Assessment

Cashier receives only:

- `dashboard.view`
- `catalog.view`
- `customers.view`
- `customers.manage`
- `sales.view`
- `sales.operate`

Cashier remains denied `inventory.view`. The fix intentionally does not grant warehouse administration. POS obtains only active warehouse identifiers and names through the sales-scoped option endpoint.

## 14. Warehouse Assessment

Warehouse receives:

- `dashboard.view`
- `catalog.view`
- `inventory.view`
- `inventory.adjust`
- `inventory.transfer`
- `purchases.view`
- `shipping.view`
- `shipping.fulfill`

Warehouse is denied `inventory.approve`. Warehouse lifecycle changes and transfer approval/rejection therefore remain unavailable, while stock reads, adjustments, transfers, purchase reads, and fulfillment remain available.

## 15. Protected Endpoint Inventory

`PosEndpointPermissionCatalog` is the machine-enforced inventory for POS-protected controllers. It classifies these domains:

| Domain | Representative controllers | Permission families |
|---|---|---|
| Dashboard | `Dashboard` | `dashboard.*` |
| Catalog | `Categories`, `Products`, `ProductVariants`, `Units`, media, feeds, sync, reindex | `catalog.*`, action-specific `inventory.adjust` |
| Inventory | `AdminInventory`, `Warehouses`, `StockTransfers` | `inventory.*` |
| Parties | `Customers`, `Suppliers`, supplier cards | customer/supplier view, manage, balance |
| Sales | `PosCarts`, `Sales`, returns, representatives | `sales.*`, operational reports |
| Purchases | `Purchases`, `PurchaseReturns` | `purchases.*` |
| Finance | banks, accounts, expenses, cashbox, treasury, statements | `finance.*`, financial reports |
| Reports | operational and financial report controllers | `reports.*` |
| Storefront orders | orders, abandoned checkout, online customers, fulfillment | storefront orders and shipping |
| Payments | payment methods, manual payment, payment lifecycle | storefront payments/refunds |
| Storefront admin | promotions, analytics, design, domains, auth | storefront administration permissions |
| Shipping | methods, carriers, administration, shipments | `shipping.*` |
| Affiliate/CommerceHub | affiliate admin, CommerceHub catalog, finance, orders, payouts | affiliate, catalog publish, reports, orders, payments |
| Administration | settings, audit, WhatsApp, users with explicit policies | settings, audit, orders, user management |

The manifest test scans authenticated controller actions and fails when an action has neither a known POS permission nor an approved separate authority.

## 16. Legacy Role Intersection Finding

ASP.NET Core combines multiple authorization attributes. A permission policy plus `[Authorize(Roles = ...)]` therefore creates an intersection, not an alternative. This was a second, drifting authority matrix.

The new regression test initially identified 51 mismatches where a canonical permission granted a role but a legacy role attribute denied it. After remediation, the test passes with zero mismatches. Redundant bare `[Authorize]` attributes were also removed from classified `/api/admin` controllers so the installed permission policy is the sole POS endpoint authority.

## 17. Incident Reconstruction

The reported failure chain was:

1. Cashier successfully authenticated and obtained a SQL-revalidated POS session.
2. `PointOfSale.Web/src/pages/POS.jsx` requested `GET /api/warehouses` while loading the sale workspace.
3. `WarehousesController.GetWarehouses` resolved to `inventory.view`.
4. Cashier did not have `inventory.view` by design.
5. `PosPermissionAuthorizationHandler` did not call `context.Succeed`.
6. The API returned 403.

The 403 was correct for the endpoint. The frontend dependency was incorrect.

## 18. Root Cause

The POS sale workflow depended on an administration-shaped endpoint. It needed a small list of selectable fulfillment warehouses, but called an endpoint whose contract exposed the warehouse-management domain and was correctly protected by inventory read authority.

This is an endpoint-purpose and UI/API integration defect, not evidence of an incorrect Cashier role, failed tenant SQL authority, or stale permission projection.

## 19. Remediation Design

The selected design preserves least privilege:

- Keep `GET /api/warehouses` on `inventory.view`.
- Keep Cashier denied inventory administration.
- Add `GET /api/pos-carts/warehouse-options` on `sales.operate`.
- Return only the fields needed by POS.
- Filter by current tenant and active status in SQL.
- Change only the POS selector to use the new endpoint.

No compatibility fallback to the broader endpoint was added.

## 20. Backend Incident Fix

`PosCartsController.GetWarehouseOptions` at `PointOfSale.API/Controllers/PosCartsController.cs:33`:

- Uses `ITenantContext.RequiredTenantId`.
- Uses `AsNoTracking`.
- Filters by tenant.
- Filters `IsActive`.
- Orders options deterministically.
- Projects a minimal `PosWarehouseOptionResponse` containing `Id`, `Name`, and `IsDefault`.

The endpoint resolves to `sales.operate` at `PointOfSale.API/Security/PosEndpointPermissionCatalog.cs:83-84`.

## 21. Tenant Isolation

Warehouse options are filtered in the database by the current required tenant identifier. The runtime test seeds active/inactive current-tenant warehouses and another tenant's warehouse, then verifies that only active current-tenant rows are returned.

Result: `WarehouseOptions_ReturnOnlyActiveWarehousesFromCurrentTenant` passed.

## 22. Inventory Permission Refinement

Warehouse and transfer actions now resolve as follows:

| Action | Permission |
|---|---|
| List/read warehouses | `inventory.view` |
| Create/update warehouse | `inventory.approve` |
| Adjust stock | `inventory.adjust` |
| Transfer stock | `inventory.transfer` |
| List/create/complete transfer | read or `inventory.transfer` by HTTP/action semantics |
| Approve/reject transfer | `inventory.approve` |

Frontend warehouse lifecycle controls and transfer approval/rejection controls use the same server-derived permissions.

## 23. Additional Backend Alignment

The audit also corrected proven action classifications and removed conflicting role restrictions across:

- Catalog read, manage, delete, publish, stock adjustment, and synchronization actions.
- Customer and supplier balance/payment/opening-balance operations.
- Purchase deletion validation and mutation operations.
- Cashbox and treasury operations.
- Shipping configuration, shipment, and fulfillment operations.
- CommerceHub catalog, order, earnings, payment, payout, and refund surfaces.
- Admin inventory, orders, accounting, search/sync, feed-token, and user-management surfaces.

The guiding rule was to change the smallest action-level policy rather than broaden a role.

## 24. Frontend Route Authority

Protected routes use `PosPermissionRouteGuard`, which calls `hasPosPermission` against `user.effectivePermissions`. Relevant route boundaries include:

- Products, Categories, Units, and barcode printing: `catalog.view`.
- Product authoring workspace: `catalog.manage`.
- Warehouses and inventory: `inventory.view`.
- Stock transfers: `inventory.transfer`.
- POS: `sales.operate`.
- Sales history: `sales.view`.
- CommerceHub earnings: `reports.financial.view`.

The route layer is usability enforcement only; API policy remains authoritative.

## 25. Frontend Action Authority

Read-capable catalog users previously saw controls for writes they could not perform. The corrected controls now require:

| UI action | Required permission |
|---|---|
| Add/edit products, categories, or units | `catalog.manage` |
| Delete products, categories, or units | `catalog.delete` |
| Product feeds, category sync, publish/unpublish | `catalog.publish` |
| Product stock adjustment form | `inventory.adjust` |
| Create/edit/activate warehouse | `inventory.approve` |
| Approve/reject stock transfer | `inventory.approve` |

Cashier and Warehouse retain catalog reads without seeing denied mutation controls.

## 26. Navigation Parity

Sidebar entries are filtered with the same effective permission list. Two discoverability gaps were corrected:

- Sales History is now visible through `sales.view`, including for Cashier.
- Treasury Transfers is now visible through `finance.operate`, including for Manager.

Warehouse, stock transfer, CommerceHub earnings, users, audit, and settings entries remain aligned with their route permissions.

## 27. Session and Revocation Authority

Current session authority is SQL-backed on every access-token validation:

- Disabled or deleted actor: denied.
- Token tenant different from current actor tenant: denied.
- Token role different from current SQL role: denied as `stale_actor_role_claim`.
- Missing, expired, revoked, or rotated session binding: denied as `revoked_or_rotated_pos_session`.
- Inactive/deleted tenant or restricted governance state: denied.

Role changes clear the target refresh-token binding in the same transaction at `PosUserManagementService.cs:678-690`. Status changes, password reset, explicit session revocation, and ownership transfer also revoke current bindings.

## 28. 401, 403, and Client Convergence

Expected behavior:

- Missing or stale authentication/session authority produces 401 and cannot reach permission success.
- A current authenticated POS actor lacking the required permission receives 403.
- The client refreshes once after 401, then reloads `/auth/session` and replaces the effective permission projection.
- Only idempotent reads are automatically replayed after refresh; mutations are never replayed automatically.
- Failed refresh clears local authority and redirects protected POS routes to login.
- Session authority is revalidated at bootstrap, every 60 seconds, focus, online, navigation history changes, page restore, and visibility return.

This provides prompt frontend convergence while preserving backend enforcement on every request.

## 29. Verification Evidence

| Command/suite | Result |
|---|---|
| Focused role-conflict regression | 1/1 passed after remediation; initially exposed 51 conflicts, then 8, then 0 |
| POS permission, manifest, projection, and authentication lifecycle suite | 52/52 passed |
| Admin hardening plus endpoint manifest | 20/20 passed |
| Warehouse-option tenant runtime test | 1/1 passed |
| Frontend focused permission/session tests | 18/18 passed |
| Frontend architecture tests | 54/54 passed |
| Frontend production build | Passed; existing large-chunk warning remains |
| Broad `.NET` security namespace | 350/356 passed |
| `git diff --check` | No whitespace errors; line-ending warnings only |

The six broad-suite failures were independently reproduced and are outside the POS permission changes:

- `PosSessionConcurrencyTests.ConcurrentSessionRotations_RejectStaleWriterInsteadOfResurrectingSession`: SQLite foreign-key setup failure.
- `StorefrontProductionBlockerFixTests.ThemeEditorPage_MustHydrateMaterializedThemes_And_SaveDraftBeforePublish`: expected draft-persist sequence absent.
- `StorefrontProductionBlockerFixTests.CheckoutStore_MustNotClearCartBeforePaymentConfirmation`: prohibited pre-confirmation cart clear found.
- Three `SafeRichTextSanitizerTests`: disallowed-tag/text escaping and `tel:+` encoding expectations differ from output.

No live browser matrix was executed, so desktop/mobile interaction and deployed 401/403 presentation are not certified here.

## 30. Final Certification

**Authorization integration result: CONDITIONAL PASS.**

Certified within the reviewed source and automated evidence:

- The five-role matrix is canonical and server-derived.
- The 56-permission backend/frontend catalogs match.
- Audited authenticated POS actions resolve to known policies or documented separate authority.
- Legacy role attributes no longer narrow canonical permission grants.
- Cashier POS warehouse selection works without granting inventory administration.
- Warehouse options are active-only and tenant-isolated.
- Route, navigation, and audited action controls project server-derived permissions.
- Role and session changes fail closed through current SQL authority.

Not certified:

- A production-wide green security suite, because six unrelated tests remain failing.
- Live all-role browser behavior, because no browser/runtime walkthrough was performed.
- The missing independent user-management review artifact.

Production sign-off should require resolution or accepted disposition of the six unrelated failures and a browser walkthrough for Owner, Admin, Manager, Cashier, and Warehouse covering direct URLs, sidebar visibility, permitted operations, denied operations, role change, status disablement, and session revocation.
