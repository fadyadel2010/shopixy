# User Management Discovery And Implementation Blueprint

Date: 2026-09-17

Repository state inspected: `main` at `24e1f3a` (`fix products form issues`)

Product boundary: Muse POS Admin (`PointOfSale.Web`) and its default POS authentication scheme

Status: implementation blueprint, not implementation or certification

## 1. Executive Decision

Muse should add tenant-scoped POS user administration without introducing ASP.NET Identity, custom roles, per-user permission overrides, store memberships, warehouse memberships, or a second POS identity model.

The smallest safe design is:

- Keep `User` as the POS identity authority.
- Keep one non-null `User.TenantId` and one built-in `UserRole` per user.
- Treat the current `Tenant` as the store/company boundary for this wave.
- Keep the five built-in roles: `Owner`, `Admin`, `Manager`, `Cashier`, and `Warehouse`.
- Add a code-owned POS permission catalog and deterministic role-to-permission grants.
- Enforce permissions in backend authorization policies on every protected operation.
- Return server-derived effective permissions in the POS session DTO for navigation and UI presentation only.
- Add an Owner/Admin Users workspace for list, invite, profile edit, role change, disable/enable, password-reset initiation, and session revocation.
- Preserve exactly one tenant Owner. Ownership changes occur only through an explicit atomic transfer ceremony.
- Replace anonymous tenant staff registration with invitation acceptance.
- Reuse current SQL-backed POS session validation and immediate refresh revocation.
- Persist security-sensitive user mutation and audit evidence in the same SQL transaction.

This is not a proposal to merge POS users with SuperAdmin, Storefront customer, CommerceHub customer, or Affiliate identities. Those remain separate authorities.

## 2. Verdict And Release Gates

Current readiness for a tenant Users screen: **NOT READY**.

Primary blockers:

| Severity | Blocker | Repository evidence | Required disposition |
|---|---|---|---|
| Critical | Anonymous callers can create an active `Cashier` through tenant-resolved `POST /api/auth/register`. | `PointOfSale.API/Controllers/AuthController.cs:65-119`; behavior is frozen by `PointOfSale.Tests/Security/ProductionSecurityHardeningTests.cs:188-216` and `PointOfSale.Tests/Architecture/PosAdministrativeAuthorityCertificationTests.cs:12-24`. | Remove the action when invitation acceptance ships, or fail it closed before exposing Users management. Do not preserve anonymous staff enrollment by default. |
| Critical | Many financial and destructive APIs use only bare `[Authorize]`, allowing every valid POS role unless an action has a stronger override. | Bare controllers are enumerated in Section 7. Examples: `BanksController.cs:13-14`, `FinancialAccountsController.cs:19-20`, `ExpensesController.cs:22-23`, `PurchasesController.cs:34-35`, and `SalesController.cs:34-35`. | Establish permission policies and harden operation endpoints before claiming role-based access control. |
| High | Most authenticated frontend routes have no role or permission guard. | `PointOfSale.Web/src/App.jsx:259-326` and `452-465`; only selected route groups use `PosRoleRouteGuard` at `286-291`, `330-369`, `371-414`, and `427-450`. | Add permission route guards and align routes, navigation, controls, and APIs to the same catalog. |
| High | No tenant POS user-management API or UI exists. | The POS route tree has no `/users` route in `PointOfSale.Web/src/App.jsx:259-465`; the sidebar's Users group contains only audit logs in `Sidebar.jsx:330-338`. | Add the contracts in Sections 13 and 14. |
| High | User rows have no general optimistic-concurrency token and no database foreign key to `Tenants`. | `UserConfiguration.cs:9-47` only marks `RefreshToken` as a concurrency token. The baseline creates `Users` with only a PK in `20260723214642_Baseline_Recovery3.cs:3291-3319`. | Add PostgreSQL `xmin` concurrency mapping and a restrictive tenant FK after data preflight. |
| High | Username and email canonicalization is inconsistent and uniqueness is case-sensitive. | Global unique indexes are configured in `UserConfiguration.cs:42-46`; login compares exact `Username` in `AuthController.cs:288-294`; tenant registration lowercases email in `TenantRegistrationService.cs:105-106`. | Add global normalized identity columns and migrate only after collision preflight. |
| Medium | Audit UI and API authority disagree. Backend allows Owner/Admin, while the page and sidebar admit Admin/SuperAdmin and omit Owner. | `AuditLogsController.cs:15-18`; `AuditLog.jsx:198-207`; `Sidebar.jsx:330-338`. | Replace role checks with `audit.view` and remove POS assumptions about `SuperAdmin`. |

Release is permitted only after all Critical and High rows have tests and direct evidence. The Users page must not be shipped as a frontend-only feature.

## 3. Discovery Method And Limits

This report is based on static, read-only repository discovery. It inspected entities, EF configuration, the baseline migration, authentication and tenant middleware, POS session authority, role attributes, the React route tree, sidebar filtering, session state, audit infrastructure, and existing security certification tests.

No application code, schema, migration, configuration, test, or runtime data was changed. No database query, build, test execution, browser mutation, commit, or pull request was performed. Therefore:

- Production and local database contents are **UNKNOWN**.
- Existing case-insensitive identity collisions are **UNKNOWN**.
- Existing orphaned `Users.TenantId` values are **UNKNOWN**.
- The number of tenants with zero or multiple Owner rows is **UNKNOWN**.
- External clients calling `POST /api/auth/register` are **UNKNOWN**.
- Email delivery readiness in each environment is **UNKNOWN**.
- Future multi-store requirements are **UNKNOWN**.

Static source proves the architecture and gaps described here; it does not certify deployed behavior.

## 4. Current Identity Authorities

| Identity | Authority and scheme | Tenant meaning | In scope |
|---|---|---|---|
| POS Admin user | `User`; default bearer/POS cookies | Exactly one `TenantId` | Yes |
| SuperAdmin | `SuperAdminUser`; dedicated SuperAdmin scheme and policy | Platform-global | No |
| Storefront customer | Separate storefront/customer identity and schemes | Customer relationship to storefront | No |
| CommerceHub customer | Separate CommerceHub customer identity | Marketplace customer | No |
| Affiliate | `AffiliateUser`; dedicated Affiliate JWT | Global affiliate model | No |

The scheme isolation is visible in `PointOfSale.API/Program.cs:2508-2589` and the policy setup at `2999-3027`. SuperAdmin routes use separate capabilities such as `super-admin.users.govern` in `PointOfSale.Web/src/App.jsx:228-253`. That platform Users screen governs platform subjects; it is not a POS tenant staff-management implementation.

No production support impersonation or POS break-glass identity was found. The existing authentication certification also states this at `docs/architecture/admin-authentication-session-certification-findings.md:87-105`.

## 5. Current POS User And Scope Model

### 5.1 User authority

`User` contains identity, one enum role, active state, last login, a single refresh-session binding, email-verification state, and one required tenant ID. It does not contain memberships or permission assignments. Evidence: `PointOfSale.Core/Entities/User.cs:10-67`.

The role enum is fixed to:

| Role | Current source description |
|---|---|
| Owner | Tenant creator and highest privilege |
| Admin | Full admin access |
| Manager | Managerial access |
| Cashier | POS cashier access |
| Warehouse | Warehouse operations access |

Evidence: `PointOfSale.Core/Enums/UserRole.cs:7-32`.

### 5.2 Tenant is the current store boundary

`Tenant` explicitly represents a Store/Company/Business and is analogous to a Shopify Store or Odoo Company. Evidence: `PointOfSale.Core/Entities/Tenant.cs:5-20`.

`StoreConfig` declares one store configuration per tenant, and the EF model enforces a unique `TenantId`. Evidence: `PointOfSale.Core/Entities/StoreConfig.cs:6-24`; `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:1375-1378`.

CommerceHub also uses tenant IDs as vendor/store IDs. For example, marketplace suborders expose `StoreId`, while services compare listing and product `TenantId` to that store ID. Representative evidence: `PointOfSale.Core/Entities/CommerceHubMarketplaceOrder.cs:63-66`; `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs:1698-1703`.

Decision: Wave 1 scope is tenant-only. Do not introduce a `Store` table or a `UserStoreMembership` table merely to rename the existing tenant boundary.

### 5.3 Warehouses are not identity scopes

Warehouses are tenant-owned operational locations. The repository has no user-to-warehouse membership or warehouse claim. `ApplicationDbContext` filters warehouses by current tenant and defines a tenant-composite alternate key at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:594-598`.

Decision: the `Warehouse` role receives tenant-wide warehouse and shipping permissions in this blueprint. Per-warehouse assignment is deferred until a concrete isolation requirement exists. If it becomes necessary, add an explicit membership model; do not overload `User.Role`, `CreatedBy`, or request headers.

## 6. Current Authentication And Session Guarantees To Preserve

The existing POS session boundary is stronger than ordinary JWT role authorization and must be reused unchanged in principle:

- Browser tokens are read from HttpOnly cookies; bearer headers remain available for API clients. Evidence: `PointOfSale.API/Program.cs:2513-2541`.
- Every accepted POS access token is revalidated against current SQL user, tenant, role, active/deleted state, refresh binding, tenant lifecycle, and vendor governance. Evidence: `PointOfSale.API/Security/PosSessionAuthority.cs:46-124` and registration at `Program.cs:2543-2569`.
- The current model intentionally permits one current refresh-backed POS session per user. Evidence: `docs/architecture/admin-authentication-session-certification-findings.md:42-53`.
- Role downgrade, user deactivation, user deletion, tenant restriction, and governance restriction revoke persisted refresh authority. Evidence: `PointOfSale.Infrastructure/Data/PrivilegedRefreshTokenRevocationInterceptor.cs:55-131`.
- Role-protected tenant endpoints receive an additional current-SQL actor and tenant recomposition. Evidence: `PointOfSale.API/Middleware/AdminTenantAuthorityMiddleware.cs:24-72` and `PointOfSale.API/Security/AdminTenantAuthority.cs:52-141`.
- Tenant resolution strips untrusted `X-Tenant-Id`, resolves trusted context, rejects mismatches, and restores only the verified ID. Evidence: `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs:82-188`.
- Middleware order is Authentication, host resolution, tenant resolution, Authorization, and tenant-admin recomposition. Evidence: `PointOfSale.API/Program.cs:3561-3637`.
- A fallback policy requires authenticated users by default. Evidence: `PointOfSale.API/Program.cs:2999-3004`.

User management must not mint an alternative token, trust a client-supplied tenant, cache authority in local storage, or bypass `PosSessionAuthority`.

## 7. Current Authorization Inventory

### 7.1 Frontend route inventory

All ordinary POS pages are inside `ProtectedRoute`, which checks only authenticated state. Evidence: `PointOfSale.Web/src/components/ProtectedRoute.jsx:13-29` and route composition in `App.jsx:259-467`.

| Domain | Current route examples | Current frontend boundary |
|---|---|---|
| Dashboard and master data | `/`, `/products`, `/categories`, `/units`, `/customers`, `/suppliers`, `/warehouses` | Authenticated only |
| Sales and purchases | `/pos`, `/sales-history`, `/purchase-invoice`, returns | Authenticated only |
| Finance and reports | `/treasury/*`, `/financial-accounts`, `/banks`, `/expenses`, `/reports/*` | Mostly authenticated only; sidebar hiding is inconsistent |
| Audit | `/settings/audit-log` | Authenticated route; page performs an inconsistent Admin/SuperAdmin check |
| Settings | `/settings` | Authenticated only |
| Disabled maintenance link | `/maintenance/reset-database` | Authenticated route; API itself is SuperAdmin-only and always returns 403 |
| Storefront operations | `/ecommerce/*`, `/analytics/*` | Owner/Admin route group, except shipping Owner/Admin/Manager |
| Storefront design | `/storefront/*` | Owner/Admin route group |
| Affiliate administration | `/admin/affiliates/*` | Owner/Admin route group |
| CommerceHub | `/commercehub/*`, `/admin/commercehub` | Contextual vendor capability guards |
| Users | No route | Not implemented |

Evidence for the complete route tree: `PointOfSale.Web/src/App.jsx:259-465`.

`PosRoleRouteGuard` compares the session DTO's single role and redirects to `/`; it does not express operation permissions. Evidence: `PointOfSale.Web/src/components/PosRoleRouteGuard.jsx:4-12`.

`PermissionGuard` is also role-based despite its name. Evidence: `PointOfSale.Web/src/components/PermissionGuard.jsx:18-25` and `46-114`.

The sidebar filters selected nodes by `requiredRoles` or CommerceHub capability but exposes most master-data, finance, inventory, transaction, and report nodes without a role boundary. Evidence: `PointOfSale.Web/src/components/Sidebar.jsx:133-345` and filtering at `371-389`.

### 7.2 Backend inventory by domain

`[Authorize]` alone means any authenticated POS role can reach the action unless a method adds a stronger role or policy. Current SQL session validation prevents stale or cross-tenant identity; it does not convert bare authentication into least-privilege operation authorization.

| Domain | Representative controllers | Current boundary | Required target permissions |
|---|---|---|---|
| Dashboard | `DashboardController` | Bare `[Authorize]` | `dashboard.view` |
| Products/catalog | `ProductsController`, `ProductVariantsController`, `CategoriesController`, `UnitsController`, `ProductMediaController` | Mixed bare and Owner/Admin/Manager roles | `catalog.view`, `catalog.manage`, `catalog.delete`, `catalog.publish` |
| Inventory/warehouses | `WarehousesController`, `AdminInventoryController`, `StockTransfersController` | Mixed bare and Owner/Admin/Manager roles | `inventory.view`, `inventory.adjust`, `inventory.transfer`, `inventory.approve` |
| Customers | `CustomersController` | Mixed; create/payment permits Cashier, sensitive writes vary | `customers.view`, `customers.manage`, `customers.balance.manage` |
| Suppliers | `SuppliersController` | Mixed; management generally Owner/Admin/Manager | `suppliers.view`, `suppliers.manage`, `suppliers.balance.manage` |
| Sales/POS | `PosCartsController`, `SalesController`, `SalesReturnsController` | POS carts allow Owner/Admin/Manager/Cashier; many sales/returns actions inherit bare auth | `sales.view`, `sales.operate`, `sales.refund`, `sales.delete` |
| Purchasing | `PurchasesController`, `PurchaseReturnsController` | Purchases inherit bare auth; return creation is Owner/Admin/Manager | `purchases.view`, `purchases.manage`, `purchases.return` |
| Finance | `BanksController`, `FinancialAccountsController`, `ExpenseCategoriesController`, `ExpensesController`, `StatementOfAccountController`, `TreasuryController` | Significant bare auth; selected governance actions use roles | `finance.view`, `finance.operate`, `finance.admin`, `finance.initialize` |
| Reports | `ReportsController`, `FinancialReportsController`, `SalesRepReportsController` | Bare auth | `reports.operational.view`, `reports.financial.view` |
| Storefront orders | `AdminOrdersController`, `AdminAbandonedCheckoutsController`, `OnlineCustomersAdminController` | Owner/Admin/Manager | `storefront.orders.view`, `storefront.orders.manage` |
| Payments | `PaymentMethodsAdminController`, `AdminManualPaymentController`, `PaymentLifecycleController` | Mixed Owner/Admin/Manager and Owner/Admin | `storefront.payments.view`, `storefront.payments.manage`, `storefront.refunds.manage` |
| Promotions | `CouponsController`, `PromotionsController`, `AdminSmartOffersController` | Mostly Owner/Admin/Manager; some destructive actions Owner/Admin | `storefront.promotions.manage` |
| Analytics | Admin analytics controllers | Owner/Admin/Manager, including settings writes | `storefront.analytics.view`, `storefront.analytics.configure` |
| Storefront design | Store config, CMS, footer, domains, checkout fields, auth providers | Owner/Admin | `storefront.design.manage`, `storefront.domains.manage`, `storefront.auth.manage` |
| Shipping | Shipping methods, carrier config, shipping administration, shipments | Bare or Owner/Admin/Manager | `shipping.view`, `shipping.manage`, `shipping.fulfill` |
| Affiliate admin | Affiliate admin controllers | Owner/Admin | `affiliates.manage`, `affiliates.payout.manage` |
| CommerceHub vendor | CommerceHub admin/order/finance/payment controllers | Role plus contextual vendor lifecycle capabilities | Preserve existing CommerceHub capabilities; intersect with POS grants |
| Settings | `SettingsController` | Read is bare; writes Owner/Admin | `settings.view`, `settings.manage` |
| Audit | `AuditLogsController` | Owner/Admin | `audit.view`, `audit.export` |
| Upload | `UploadController` | Bare auth | Permission must be inherited from the owning operation; no generic upload entitlement |
| Services marketplace | `MarketplaceController` | Bare auth with service-specific authority inside controller | Keep marketplace actor boundary separate from POS staff permissions |
| Maintenance | `MaintenanceController` | SuperAdmin policy and disabled result | Remove POS route; no POS permission |

Representative route and role evidence was inspected in the controller files above. The complete bare-controller set currently includes `Auth`, `Banks`, `Cashbox`, `Categories`, `Coupons`, `Customers`, `Dashboard`, `ExpenseCategories`, `Expenses`, `FinancialAccounts`, `FinancialReports`, `Marketplace`, `Orders`, `Promotions`, `ProductVariants`, `Products`, `Sales`, `PurchaseReturns`, `Purchases`, `Settings`, `ShippingMethods`, `SalesRepReports`, `SalesRepresentatives`, `SalesReturns`, `StockTransfers`, `StatementOfAccount`, `Reports`, `Suppliers`, `Units`, `Warehouses`, `Treasury`, and `Upload` controllers.

### 7.3 Dangerous examples that prove the need for operation policies

| Operation | Current evidence | Consequence |
|---|---|---|
| Bank create/update/delete/opening balances | `BanksController.cs:13-14`, HTTP actions at `101-214` and `395` | Any authenticated POS role can attempt treasury master-data mutation. |
| Financial account create/update/delete/transfer | `FinancialAccountsController.cs:19-20`, actions at `95-205` | Any authenticated POS role can attempt chart/account mutation or transfer. |
| Expense create/update/delete | `ExpensesController.cs:22-23`, actions at `185`, `277`, `360`; only approval at `426-427` has governance roles | Authentication is stronger than anonymity but not least privilege. |
| Purchase create/update/delete | `PurchasesController.cs:34-35`, actions at `97`, `918`, `1370` | Cashier and Warehouse tokens are not excluded by endpoint metadata. |
| Sales create/update/return | `SalesController.cs:34-35`, actions at `102`, `1343`, `2202`; deletion alone has Owner/Admin at `1747-1748` | Role intent is inconsistent by operation. |
| Shipping method create/update/delete/sync | `ShippingMethodsController.cs:25-26`, actions at `102-344` | Every POS role can attempt shipping configuration mutation. |
| Financial reports | `FinancialReportsController.cs:16-18`, report actions at `64-247` | Every POS role can read sensitive financial statements. |
| Sales representative CRUD and reports | `SalesRepresentativesController.cs:13-14`; `SalesRepReportsController.cs:18-19` | Every POS role can mutate representatives and read performance/commission data. |

These are existing authorization debts. User management must not encode a polished role matrix in the UI while leaving these direct APIs permissive.

## 8. Target Authorization Architecture

### 8.1 Code-owned permissions, not database-owned permissions

Add a static `PosPermissions` catalog and a `PosRolePermissionCatalog` mapping. Do not add Role, Permission, RolePermission, UserPermission, or membership tables in Wave 1.

Rationale:

- The current user has exactly one enum role.
- There is no custom-role UX or persisted grant requirement.
- A code-owned map is reviewable, deterministic, version-controlled, testable, and immediately compatible with SQL role recomposition.
- Database-owned permission rows add seed drift, cache invalidation, migration complexity, and an administration surface without a demonstrated requirement.

### 8.2 Backend enforcement

Implement one `PosPermissionRequirement` and one `PosPermissionAuthorizationHandler` backed by the static role map. Register one scheme-bound policy for every permission key:

```csharp
options.AddPolicy(PosPermissions.UsersView, policy =>
{
    policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
    policy.RequireAuthenticatedUser();
    policy.AddRequirements(new PosPermissionRequirement(PosPermissions.UsersView));
});
```

The handler may trust the current role claim only because default POS bearer validation has already required the claim to match current SQL state on the same request through `PosSessionAuthority`. It must fail closed for unknown roles, missing permission keys, non-POS schemes, and malformed principals.

Use `[Authorize(Policy = PosPermissions.UsersView)]` on controllers/actions. Do not combine a permission policy with a broad class-level role list that can drift. Resource invariants such as actor/target rank and sole-owner protection remain service rules after policy admission.

### 8.3 Frontend projection

Extend `UserDto` with `EffectivePermissions`, generated by the same backend role catalog. Current DTO construction is centralized in `AuthController.UserDto` at `PointOfSale.API/Controllers/AuthController.cs:896-923`.

The frontend session validator at `PointOfSale.Web/src/auth/posVendorSessionAuthority.js:162-199` must validate and retain only known POS permission strings, as it already does for CommerceHub navigation capabilities. POS permissions and CommerceHub lifecycle capabilities remain separate arrays because they have different authorities and semantics.

Add:

- `hasPosPermission(user, permission)`
- `PosPermissionRouteGuard`
- `PermissionGate` for buttons and panels
- permission filtering in `Sidebar`

Frontend checks improve navigation and presentation. They never replace backend policy enforcement.

## 9. Permission Catalog And Default Role Matrix

Legend: `Y` is granted, `-` is denied. CommerceHub permissions remain additionally constrained by current vendor governance and lifecycle state.

| Permission | Owner | Admin | Manager | Cashier | Warehouse |
|---|---:|---:|---:|---:|---:|
| `dashboard.view` | Y | Y | Y | Y | Y |
| `catalog.view` | Y | Y | Y | Y | Y |
| `catalog.manage` | Y | Y | Y | - | - |
| `catalog.delete` | Y | Y | - | - | - |
| `catalog.publish` | Y | Y | Y | - | - |
| `inventory.view` | Y | Y | Y | - | Y |
| `inventory.adjust` | Y | Y | Y | - | Y |
| `inventory.transfer` | Y | Y | Y | - | Y |
| `inventory.approve` | Y | Y | Y | - | - |
| `customers.view` | Y | Y | Y | Y | - |
| `customers.manage` | Y | Y | Y | Y | - |
| `customers.balance.manage` | Y | Y | Y | - | - |
| `suppliers.view` | Y | Y | Y | - | - |
| `suppliers.manage` | Y | Y | Y | - | - |
| `suppliers.balance.manage` | Y | Y | Y | - | - |
| `sales.view` | Y | Y | Y | Y | - |
| `sales.operate` | Y | Y | Y | Y | - |
| `sales.refund` | Y | Y | Y | - | - |
| `sales.delete` | Y | Y | - | - | - |
| `purchases.view` | Y | Y | Y | - | Y |
| `purchases.manage` | Y | Y | Y | - | - |
| `purchases.return` | Y | Y | Y | - | - |
| `finance.view` | Y | Y | Y | - | - |
| `finance.operate` | Y | Y | Y | - | - |
| `finance.admin` | Y | Y | - | - | - |
| `finance.initialize` | Y | - | - | - | - |
| `reports.operational.view` | Y | Y | Y | - | - |
| `reports.financial.view` | Y | Y | - | - | - |
| `storefront.orders.view` | Y | Y | Y | - | - |
| `storefront.orders.manage` | Y | Y | Y | - | - |
| `storefront.payments.view` | Y | Y | Y | - | - |
| `storefront.payments.manage` | Y | Y | - | - | - |
| `storefront.refunds.manage` | Y | Y | - | - | - |
| `storefront.promotions.manage` | Y | Y | Y | - | - |
| `storefront.analytics.view` | Y | Y | Y | - | - |
| `storefront.analytics.configure` | Y | Y | - | - | - |
| `storefront.design.manage` | Y | Y | - | - | - |
| `storefront.domains.manage` | Y | Y | - | - | - |
| `storefront.auth.manage` | Y | Y | - | - | - |
| `shipping.view` | Y | Y | Y | - | Y |
| `shipping.manage` | Y | Y | Y | - | - |
| `shipping.fulfill` | Y | Y | Y | - | Y |
| `affiliates.manage` | Y | Y | - | - | - |
| `affiliates.payout.manage` | Y | Y | - | - | - |
| `settings.view` | Y | Y | - | - | - |
| `settings.manage` | Y | Y | - | - | - |
| `audit.view` | Y | Y | - | - | - |
| `audit.export` | Y | Y | - | - | - |
| `users.view` | Y | Y | - | - | - |
| `users.invite` | Y | Y | - | - | - |
| `users.profile.manage` | Y | Y | - | - | - |
| `users.role.manage` | Y | Y | - | - | - |
| `users.status.manage` | Y | Y | - | - | - |
| `users.password-reset.initiate` | Y | Y | - | - | - |
| `users.session.revoke` | Y | Y | - | - | - |
| `users.ownership.transfer` | Y | - | - | - | - |

Service-level restrictions narrow these grants:

| Actor | Allowed targets and role assignments |
|---|---|
| Owner | May manage Admin, Manager, Cashier, and Warehouse users. May not create a second Owner through ordinary invite or role update. |
| Admin | May manage Manager, Cashier, and Warehouse users. May view Owner/Admin rows but may not change their role, status, credentials, or sessions. |
| Any actor | May not disable, delete, or demote self through admin APIs. Own-profile and own-password flows are separate. |
| Owner transfer | Current Owner plus one active non-Owner target in the same tenant; atomic role swap and session revocation for both. |

The exact Manager access to finance and storefront configuration is a product decision that is currently inconsistent. This blueprint chooses operational finance access but denies financial statements and finance administration. If product owners choose differently, change the single catalog and its tests before rollout; do not add endpoint exceptions.

## 10. User Lifecycle And Invariants

### 10.1 Lifecycle states

Keep `IsActive` as the runtime login authority to minimize changes. Derive presentation status from explicit timestamps:

| Presentation status | Required state |
|---|---|
| Pending invitation | `IsActive = false`, `InvitationAcceptedAtUtc = null`, and an unconsumed/unrevoked invitation token exists |
| Invitation expired | Pending user with no valid invitation token |
| Active | `IsActive = true` and `IsDeleted = false` |
| Disabled | `IsActive = false`, `InvitationAcceptedAtUtc != null`, and `IsDeleted = false` |
| Removed | `IsDeleted = true`; excluded from normal lists unless explicitly requested |

Do not introduce a second status column that can disagree with `IsActive` unless the entire authentication authority is migrated in the same change.

### 10.2 Invitation

1. Owner/Admin submits profile and non-Owner role.
2. Service normalizes identity and checks global username/email uniqueness.
3. Service creates `User` with `IsActive = false`, an unknowable random password hash, and tenant from trusted context only.
4. Service creates a hash-only, single-use invitation token with bounded expiry.
5. User row, token row, and `POS_USER_INVITED` audit row commit atomically.
6. Email delivery occurs after commit. Delivery failure does not activate the user and is visible for retry.
7. Acceptance endpoint resolves token globally by hash and purpose, checks tenant/user/token state, validates password, sets the final password hash, marks email verified, records acceptance, consumes all outstanding invitation tokens, and activates the user atomically.
8. Acceptance does not automatically create a POS session. The user signs in through normal login so all existing authority checks apply.

Invitation acceptance implies control of the invited email. Set `EmailVerificationStatus = Verified` and `EmailVerifiedAtUtc` during acceptance rather than sending a second verification ceremony.

### 10.3 Disable and enable

- Disable requires a reason and optimistic-concurrency version.
- Disable sets `IsActive = false`, records actor/time/reason, and commits audit evidence.
- Existing `PrivilegedRefreshTokenRevocationInterceptor` clears the current refresh binding on active-to-inactive change.
- The next request using the old access token fails in `PosSessionAuthority` because current SQL active state is checked on every request.
- Enable does not issue a session. The user signs in normally.

### 10.4 Role change

- Ordinary role changes cannot assign or remove Owner.
- Admin cannot modify an Admin or Owner target.
- Role change, target session revocation, and before/after audit evidence commit together.
- Existing role-change revocation at `PrivilegedRefreshTokenRevocationInterceptor.cs:135-140` must remain.

### 10.5 Ownership transfer

Ownership transfer is a dedicated endpoint and transaction, not two role-edit calls.

Required transaction:

1. Lock current Owner and target user rows.
2. Revalidate actor is the current active Owner in the trusted tenant.
3. Revalidate target is active, accepted, non-deleted, and in the same tenant.
4. Demote old Owner to Admin.
5. Promote target to Owner.
6. Revoke both POS sessions.
7. Add one audit record containing both user IDs and role transitions.
8. Commit under the unique active-Owner database constraint.

Exactly one non-deleted Owner per tenant is the target invariant. The database enforces at most one; service transactions and certification enforce at least one.

### 10.6 Password reset and session revocation

Admin-initiated reset sends a one-time link. Administrators never choose, view, log, or return a user's password. The endpoint always returns `202` after authorized target validation and rate limiting.

Password completion must:

- Validate a hash-only, purpose-bound, single-use token.
- Apply one shared POS password policy.
- Replace `PasswordHash`.
- Revoke all outstanding reset tokens.
- Clear the current POS refresh binding.
- Commit audit evidence without token or password material.

Extend `PrivilegedRefreshTokenRevocationInterceptor` to revoke on `PasswordHash` change. Current source explicitly identifies missing POS password-reset/session-management ceremony as debt at `docs/architecture/admin-authentication-session-certification-findings.md:107-113`.

Manual session revocation clears the one current binding. The current model has no per-device session inventory, so the UI must say "Sign out current session" rather than "Manage devices" or "Sign out all devices".

### 10.7 Deletion

Wave 1 does not expose hard delete. Disable is the reversible administrative operation. A future soft-delete feature requires a relationship and retention audit across sales, audit, accounting, and `CreatedBy`/`UpdatedBy` references. That impact is **UNKNOWN** and must not be guessed during this implementation.

## 11. Data Model Blueprint

### 11.1 `User` additions

| Field | Type | Purpose |
|---|---|---|
| `NormalizedUsername` | required string, max 100 | Global canonical login key |
| `NormalizedEmail` | required string, max 200 | Global canonical email key |
| `RowVersion` | `uint`, PostgreSQL `xmin` | Optimistic concurrency for administrative mutations |
| `InvitationAcceptedAtUtc` | nullable timestamp | Distinguish pending/expired invitation from disabled accepted user |
| `InvitedAtUtc` | nullable timestamp | Lifecycle evidence |
| `InvitedByUserId` | nullable Guid | Actor attribution; use restrictive/no-action FK or scalar if historical actor deletion semantics require it |
| `DisabledAtUtc` | nullable timestamp | Disable evidence |
| `DisabledByUserId` | nullable Guid | Disable actor attribution |
| `DisableReason` | nullable string, bounded | Required reason for disabled users |

Use the repository's existing PostgreSQL `xmin` pattern, demonstrated by `StoreConfig.RowVersion` at `PointOfSale.Core/Entities/StoreConfig.cs:117-123` and mapping examples in `ApplicationDbContext.cs:1387-1392`.

### 11.2 POS security token entity

Add one purpose-bound `PosUserSecurityToken` entity rather than separate invitation and reset tables.

| Field | Rule |
|---|---|
| `Id` | Guid PK |
| `TenantId` | Required, immutable, `ITenantOwned` |
| `UserId` | Required FK to `Users`, delete restrict |
| `Purpose` | Enum: `Invitation`, `PasswordReset` |
| `TokenHash` | SHA-256 or stronger fixed hash of 48 random bytes; raw token never persisted |
| `ExpiresAtUtc` | Required bounded expiry |
| `ConsumedAtUtc` | Nullable; single-use marker |
| `RevokedAtUtc` | Nullable |
| `CreatedAtUtc` | Required |
| `CreatedByUserId` | Nullable for self-service/system issuance |

Required constraints and indexes:

- Unique `TokenHash`.
- Index `(TenantId, UserId, Purpose, ExpiresAtUtc)`.
- Check `ExpiresAtUtc > CreatedAtUtc`.
- Check that consumed and revoked timestamps are not before creation.
- Strict tenant query filter.
- `TenantSaveChangesInterceptor` enforcement through `ITenantOwned`.

The secure pattern already exists for email verification: hash-only storage, single use, and expiry are documented in `PointOfSale.Core/Entities/EmailVerificationToken.cs:3-63`; token generation uses 48 random bytes and stores a hash in `PointOfSale.API/Services/Auth/EmailVerificationService.cs:102-128`. Reuse the pattern, not the existing token rows, because invitation and reset require explicit purpose and tenant lifecycle semantics.

### 11.3 Relationships and indexes

Add:

- `FK_Users_Tenants_TenantId` with `DeleteBehavior.Restrict`.
- Unique index on `NormalizedUsername`.
- Unique index on `NormalizedEmail`.
- Existing non-unique `Users.TenantId` index remains.
- Partial unique index ensuring at most one non-deleted Owner per tenant.
- Optional filtered list index `(TenantId, IsDeleted, IsActive, Role)` if query plans justify it.

Global normalized uniqueness is intentional in this wave. Login currently finds a user globally before tenant context is known and keys only by username at `AuthController.cs:288-294`. Changing to tenant-composite identity would require a new login identifier/tenant selection contract and is out of scope.

## 12. Migration And Deployment Plan

### 12.1 Mandatory preflight

Run read-only production-equivalent SQL and stop on any row:

```sql
-- Case-insensitive username collisions
SELECT upper(btrim("Username")), count(*)
FROM "Users"
GROUP BY upper(btrim("Username"))
HAVING count(*) > 1;

-- Case-insensitive email collisions
SELECT upper(btrim("Email")), count(*)
FROM "Users"
GROUP BY upper(btrim("Email"))
HAVING count(*) > 1;

-- Orphan tenants
SELECT u."Id", u."TenantId"
FROM "Users" u
LEFT JOIN "Tenants" t ON t."Id" = u."TenantId"
WHERE t."Id" IS NULL;

-- Owner cardinality
SELECT "TenantId", count(*)
FROM "Users"
WHERE NOT "IsDeleted" AND "Role" = 0
GROUP BY "TenantId"
HAVING count(*) <> 1;
```

Confirm enum storage before using `Role = 0`; the baseline stores Role as integer at `20260723214642_Baseline_Recovery3.cs:3301-3303` and the current enum places Owner first at `UserRole.cs:7-17`.

### 12.2 Expand migration

1. Add normalized columns as nullable.
2. Backfill with the exact application normalizer.
3. Stop if duplicate normalized values exist; do not silently rename identities.
4. Make normalized columns required.
5. Replace old unique username/email indexes with global normalized unique indexes.
6. Add user lifecycle metadata columns.
7. Map `RowVersion` to `xmin`; do not add an ordinary writable version column on PostgreSQL.
8. Add the restrictive User-to-Tenant FK after orphan preflight.
9. Add the partial unique Owner index after owner-cardinality preflight.
10. Create `PosUserSecurityTokens` and its constraints/indexes.
11. Update the model snapshot and prove no unexpected pending model changes.

The baseline currently has globally unique case-sensitive indexes and no User FK at `20260723214642_Baseline_Recovery3.cs:13461-13476` and `3291-3319`.

### 12.3 Contract rollout

Deploy backend permission projection before frontend guards. During the compatibility interval, old sessions without `effectivePermissions` should fail session-shape validation and sign in again rather than infer permissions in the browser.

Do not dual-read both old and normalized login indefinitely. Backfill, deploy the normalizer, switch login and writes to normalized columns, and remove old indexes in the same controlled release.

### 12.4 Rollback

Rollback application behavior before dropping additive columns or token rows. Do not roll back accepted users or restore anonymous staff registration automatically. If a migration rollback is required, preserve user and audit data and remove only indexes/constraints proven incompatible. Ownership corrections require an explicit runbook, not ad hoc SQL.

## 13. API Contract

Base route: `/api/admin/users`

Controller boundary: default POS scheme plus permission policies

Tenant source: `ITenantContext.RequiredTenantId` only

Entity lookup behavior: tenant-filtered query; foreign-tenant IDs return `404`, not `403`

### 13.1 Read contracts

| Method | Route | Permission | Contract |
|---|---|---|---|
| GET | `/api/admin/users` | `users.view` | Paged list; search, role, status, invited date, last login, sort; max page size 100 |
| GET | `/api/admin/users/{userId}` | `users.view` | Detail without password hash, refresh binding, token hashes, or raw audit JSON |
| GET | `/api/admin/users/roles` | `users.view` | Built-in role names, localized display keys, effective permissions, and actor-assignable flag |
| GET | `/api/admin/users/{userId}/audit` | `audit.view` | User-management audit subset, tenant-scoped and paged |

Suggested list item:

```json
{
  "id": "guid",
  "username": "cashier.one",
  "email": "cashier@example.com",
  "firstName": "Cash",
  "lastName": "User",
  "phone": null,
  "role": "Cashier",
  "status": "Active",
  "emailVerified": true,
  "lastLoginAt": "2026-09-17T10:00:00Z",
  "invitedAtUtc": "2026-09-16T10:00:00Z",
  "version": "12345"
}
```

### 13.2 Mutation contracts

| Method | Route | Permission | Notes |
|---|---|---|---|
| POST | `/api/admin/users/invitations` | `users.invite` | Creates inactive user and invite; non-Owner roles only; returns `202` |
| POST | `/api/admin/users/{userId}/invitation/resend` | `users.invite` | Pending users only; rate-limited; revokes prior active invitation |
| DELETE | `/api/admin/users/{userId}/invitation` | `users.invite` | Revokes pending invitation; does not hard-delete user |
| PATCH | `/api/admin/users/{userId}/profile` | `users.profile.manage` | Names, phone, email; requires `If-Match` |
| PUT | `/api/admin/users/{userId}/role` | `users.role.manage` | Non-Owner roles only; requires `If-Match` |
| PUT | `/api/admin/users/{userId}/status` | `users.status.manage` | Enable/disable with reason; requires `If-Match` |
| POST | `/api/admin/users/{userId}/password-reset` | `users.password-reset.initiate` | Sends one-time link; never accepts a password |
| POST | `/api/admin/users/{userId}/session/revoke` | `users.session.revoke` | Clears current POS session binding |
| POST | `/api/admin/users/ownership-transfer` | `users.ownership.transfer` | Atomic Owner transfer; current Owner only |
| POST | `/api/auth/user-invitations/accept` | Anonymous token ceremony | `[AllowAnonymous]`, `[AllowAnonymousTenant]`, purpose-bound token, rate limit |
| POST | `/api/auth/password-reset/complete` | Anonymous token ceremony | Generic response, purpose-bound token, rate limit |

Use `If-Match: "<version>"` on mutable existing-user operations. Return the new ETag/version after success.

### 13.3 Error contract

Use the repository's `ApiErrorResponse` shape and bounded public messages.

| HTTP | Code | Meaning |
|---:|---|---|
| 400 | `VALIDATION_ERROR` | Invalid fields or password policy |
| 401 | Existing auth/session codes | Missing or expired POS session |
| 403 | `POS_PERMISSION_FORBIDDEN` | Actor lacks operation permission |
| 404 | `USER_NOT_FOUND` | User absent from trusted tenant scope |
| 409 | `IDENTITY_ALREADY_EXISTS` | Normalized username/email collision |
| 409 | `OWNER_INVARIANT_VIOLATION` | Ordinary operation would create/remove Owner incorrectly |
| 409 | `TARGET_ROLE_FORBIDDEN` | Actor may not manage target rank |
| 410 | `SECURITY_TOKEN_EXPIRED` | Validly shaped but expired token |
| 412 | `USER_VERSION_CONFLICT` | `If-Match` no longer current |
| 429 | `RATE_LIMITED` | Invite/reset resend limit |

Anonymous reset initiation, if later added, must return the same response for existing and non-existing identities to avoid enumeration.

### 13.4 Request rules

- Normalize username and email server-side; client normalization is advisory.
- Never accept `TenantId`, permission arrays, Owner role, `IsActive`, password hash, verification state, session binding, or audit actor fields from clients.
- Reject unknown JSON properties on security-sensitive creation/acceptance DTOs, following the strict registration converter pattern at `AuthController.cs:765-881`.
- Bound every string and page size.
- Apply CSRF protection to cookie-authenticated mutations through the existing middleware at `Program.cs:3563-3568`.
- Add specific rate-limit policies for invitation resend, password reset, and token acceptance.
- Never log raw tokens or credentials.

## 14. Backend Service Design

Add one `IPosUserManagementService` and keep controller actions thin. The service owns:

- Tenant-scoped queries.
- Actor/target rank checks.
- Identity normalization and uniqueness checks.
- Sole-owner invariants.
- Invitation and reset token lifecycle.
- Password hashing through the existing `IPasswordHasher<User>` registration at `Program.cs:376-380`.
- Session revocation.
- Optimistic-concurrency handling.
- Transactional audit-row creation.

Keep the implementation in one service unless token email rendering becomes independently reusable. Do not add generic repositories over `ApplicationDbContext`; the existing codebase uses the context directly for these authorities.

Use a database transaction for each security mutation. Add `AuditLog` to the same context before the single `SaveChangesAsync`/commit. Existing `AuditService` creates a separate scope and falls back to a queue at `PointOfSale.API/Services/Audit/AuditService.cs:163-179`; that is appropriate for general best-effort audit but does not make a user authority mutation and its audit evidence atomic.

The existing append-only guard for `AuditLog` at `ApplicationDbContext.cs:6847-6857` must remain.

## 15. Audit And Observability Contract

Required successful action types:

| Action | Required evidence |
|---|---|
| `POS_USER_INVITED` | actor, target, tenant, role, normalized identity fingerprints, expiry; no raw token |
| `POS_USER_INVITATION_RESENT` | actor, target, old-token revocation count, new expiry |
| `POS_USER_INVITATION_REVOKED` | actor, target, reason |
| `POS_USER_INVITATION_ACCEPTED` | target, tenant, timestamp, source; no password/token |
| `POS_USER_PROFILE_CHANGED` | actor, target, allow-listed before/after fields |
| `POS_USER_ROLE_CHANGED` | actor, target, before/after role |
| `POS_USER_DISABLED` | actor, target, reason |
| `POS_USER_ENABLED` | actor, target |
| `POS_USER_PASSWORD_RESET_ISSUED` | actor, target, expiry |
| `POS_USER_PASSWORD_RESET_COMPLETED` | target, session revoked flag |
| `POS_USER_SESSION_REVOKED` | actor, target, reason |
| `POS_USER_OWNERSHIP_TRANSFERRED` | old Owner, new Owner, role changes |

Audit rules:

- Set `TenantId` explicitly.
- Store actor in `UserId` and target in `EntityId`.
- Snapshot actor username.
- Use `EntityType = "User"`.
- Capture correlation ID in `ReferenceNumber` when available.
- Allow-list before/after fields; exclude password hashes, refresh bindings, token hashes, cookies, authorization headers, and full request bodies.
- Record denied authorization and invariant failures in structured logs/telemetry with bounded reason codes. Avoid filling append-only domain audit with ordinary failed validation noise.
- Preserve the interceptor's `PRIVILEGED_REFRESH_REVOKED` evidence at `PrivilegedRefreshTokenRevocationInterceptor.cs:209-247`.

Update the existing Audit screen to admit Owner/Admin through `audit.view`, localize the new actions, and filter by target user. Backend audit reads already apply explicit tenant predicates at `AuditLogsController.cs:53-58` and `134-146`.

## 16. Frontend Blueprint

### 16.1 Routes and navigation

Add:

```jsx
<Route element={<PosPermissionRouteGuard permission="users.view" />}>
    <Route path="users" element={<UsersManagementPage />} />
</Route>
```

Replace the current Users sidebar group with:

- `/users` for Users Management, gated by `users.view`.
- `/settings/audit-log` for Tracking Users, gated by `audit.view`.

Remove `SuperAdmin` from POS sidebar/page role checks. SuperAdmin has its own route tree and session authority.

Remove `/maintenance/reset-database` from the POS route tree. The endpoint is separately SuperAdmin-protected and permanently disabled at `MaintenanceController.cs:22-52`.

### 16.2 Users workspace

Desktop layout:

- Page title and concise security description.
- Summary cards: Active, Pending, Disabled, Owners/Admins.
- Search and filters: status, role, invited date, last login.
- Table columns: user, role, status, last login, invited by/date, actions.
- Stable server pagination and sort.
- Invite button visible only with `users.invite`.
- Row actions derived from both effective permission and target constraints returned by the API.

Mobile layout:

- Replace the wide table with stacked user cards.
- Keep status and role visible without expansion.
- Put actions in an accessible menu with destructive confirmation.
- Do not rely on hover.

### 16.3 Dialogs and ceremonies

| UI | Required behavior |
|---|---|
| Invite | Name, username, email, phone, assignable role; no Owner option; explains email acceptance |
| Edit profile | Does not expose role/status in the same form; uses version token |
| Change role | Shows before/after effective access summary and warns that current session is revoked |
| Disable | Requires reason and typed confirmation for Admin targets managed by Owner |
| Enable | Explains that no session is issued automatically |
| Reset password | Sends a link; never asks admin to set a password |
| Revoke session | States the single-current-session semantics |
| Transfer ownership | Owner-only; reauthentication requirement is recommended; explicit old/new roles and irreversible-session warning |

### 16.4 State and API handling

- Use the existing Axios instance so cookies, CSRF, safe messages, and session refresh behavior remain centralized. Evidence: `PointOfSale.Web/src/services/api.js:33-52` and `94-127`.
- Do not persist the user list or permissions to local storage.
- Cancel stale list requests on filter changes.
- Debounce search.
- On `412`, keep the dialog open, show a stale-data message, and refetch target detail.
- On `401`, use existing session-loss handling.
- On `403`, display access denied and refetch the session; do not optimistically infer a role change.
- Do not automatically retry non-idempotent mutations. Current retry logic is already limited to idempotent methods at `api.js:18-19` and `188-201`.

### 16.5 Localization and direction

Add symmetric English and Arabic keys under a dedicated `userManagement` namespace in:

- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`

Use `useRTL`, which derives language, `dir`, and locale at `PointOfSale.Web/src/hooks/useRTL.js:5-14`. Dialog placement, action menus, pagination arrows, field alignment, and mobile cards must work in LTR and RTL. No user-facing English fallback should remain in Arabic mode.

## 17. Existing Code Changes Required

### 17.1 Existing production files

| File | Required change |
|---|---|
| `PointOfSale.Core/Entities/User.cs` | Add normalized identity, concurrency, and lifecycle evidence fields. |
| `PointOfSale.Infrastructure/Data/Configurations/UserConfiguration.cs` | Configure normalization, `xmin`, tenant FK, indexes, and lengths. |
| `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` | Add token set/config/filter if not in a separate configuration; preserve strict user filter. |
| `PointOfSale.Infrastructure/Data/PrivilegedRefreshTokenRevocationInterceptor.cs` | Revoke on password change and preserve atomic security evidence. |
| `PointOfSale.API/Controllers/AuthController.cs` | Remove anonymous staff registration; normalize login; add effective permissions to session DTO. |
| `PointOfSale.API/Program.cs` | Register permission policies/handler, user service, token service, and rate limits. |
| `PointOfSale.Web/src/auth/posVendorSessionAuthority.js` | Validate known effective POS permissions independently from CommerceHub capabilities. |
| `PointOfSale.Web/src/App.jsx` | Add `/users` permission route; remove disabled maintenance route; incrementally replace role guards. |
| `PointOfSale.Web/src/components/Sidebar.jsx` | Permission-based navigation; correct Owner audit access. |
| `PointOfSale.Web/src/pages/AuditLog.jsx` | Replace Admin/SuperAdmin role check with `audit.view`. |
| `PointOfSale.Web/src/i18n/locales/en.json` | Complete English Users strings. |
| `PointOfSale.Web/src/i18n/locales/ar.json` | Complete Arabic Users strings. |

### 17.2 Proposed new production files

| File | Responsibility |
|---|---|
| `PointOfSale.Core/Security/PosPermissions.cs` | Permission constants and role grants |
| `PointOfSale.Core/Entities/PosUserSecurityToken.cs` | Invite/reset token authority |
| `PointOfSale.Core/Enums/PosUserSecurityTokenPurpose.cs` | Purpose enum |
| `PointOfSale.Infrastructure/Data/Configurations/PosUserSecurityTokenConfiguration.cs` | Constraints, indexes, FKs |
| `PointOfSale.API/Security/PosPermissionRequirement.cs` | Policy requirement and handler |
| `PointOfSale.API/Services/Users/IPosUserManagementService.cs` | Application contract |
| `PointOfSale.API/Services/Users/PosUserManagementService.cs` | Tenant user lifecycle and transactional audit |
| `PointOfSale.API/Controllers/AdminUsersController.cs` | Tenant admin user API |
| `PointOfSale.API/DTOs/Users/PosUserDtos.cs` | Bounded request/response contracts |
| `PointOfSale.Web/src/auth/posPermissions.js` | Known frontend permission names and helper |
| `PointOfSale.Web/src/components/PosPermissionRouteGuard.jsx` | Route presentation guard |
| `PointOfSale.Web/src/services/userManagementApi.js` | Users API client |
| `PointOfSale.Web/src/pages/UsersManagementPage.jsx` | Users workspace |

The implementer may split frontend components when the page becomes difficult to review, but should not build a new design system or generic state framework.

## 18. Test Blueprint

### 18.1 Backend unit and service tests

- Every permission key is unique and known.
- Every `UserRole` has an explicit grant set.
- Unknown roles and permissions deny.
- Owner has the intended superset except separate contextual CommerceHub rules.
- Admin cannot manage Owner/Admin targets.
- Invite rejects Owner role, client tenant, duplicate normalized username/email, and malformed fields.
- Invite creates inactive user, hash-only token, and audit atomically.
- Acceptance rejects missing, wrong-purpose, revoked, consumed, and expired tokens.
- Acceptance verifies email, sets password, activates once, and creates no session.
- Disable requires reason, revokes current session, and rejects self/Owner invariant violations.
- Role change revokes the target session.
- Password completion revokes the target session.
- Ownership transfer leaves exactly one Owner and revokes both sessions.
- Concurrent profile/role/status updates produce a version conflict, not last-write-wins.
- Audit data excludes password, refresh, and token material.

### 18.2 API integration matrix

For every Users endpoint, test:

| Scenario | Expected result |
|---|---|
| No authentication | `401` |
| Valid Cashier | `403` |
| Valid Warehouse | `403` |
| Valid Manager | `403` |
| Valid Admin on allowed target | Success |
| Valid Admin on Admin/Owner target mutation | `403` or stable target-forbidden `409` |
| Valid Owner | Success within invariant |
| Foreign-tenant user ID | `404` and no existence leak |
| Stale role token after role change | Authentication fails through current SQL authority |
| Disabled/deleted actor | Authentication fails |
| Stale `If-Match` | `412` |
| Duplicate normalized identity | `409` |
| Mutation audit persistence failure | Entire authority mutation rolls back |

Use a real PostgreSQL integration lane for normalized unique indexes, partial Owner index, foreign keys, transactions, and `xmin`. The current test project references EF InMemory and SQLite at `PointOfSale.Tests/PointOfSale.Tests.csproj:10-19`; neither can certify PostgreSQL-specific constraints by itself.

### 18.3 Existing tests to update

| Test | Required update |
|---|---|
| `AdminAuthenticationSessionCertificationTests.PosUserRegistration_MustNotCreateSessionAuthority` | Replace with proof that anonymous staff registration is absent/fail-closed and invitation acceptance creates no session. |
| `PosAdministrativeAuthorityCertificationTests.OrdinaryRegistration_MustNotAcceptRoleOrTenantAuthority` | Replace ordinary registration assumption with invitation DTO and trusted tenant proof. |
| `PosAdministrativeAuthorityCertificationTests.PosRuntime_MustNotExposeRoleAssignmentOrSelfPromotionEndpoint` | Replace blanket no-role-endpoint rule with policy, rank, and sole-owner certification. |
| `ProductionSecurityHardeningTests.Register_AssignsCashierRole_AndServerTenant` | Remove old public-registration expectation; test invite/accept lifecycle. |
| `PosRoleAuthorityCertificationTests` | Add password change, disable, ownership transfer, and explicit session-revoke cases. |
| `AdminRouteAuthorizationHardeningTests` | Extend beyond `/api/admin` prefixes and reject bare `[Authorize]` on classified sensitive POS operations. |

Preserve `PosUserAuthorityCertificationTests` invariants at `PointOfSale.Tests/Architecture/PosUserAuthorityCertificationTests.cs:15-50`.

### 18.4 Frontend tests

The web project has Vitest and Testing Library dependencies and scripts at `PointOfSale.Web/package.json:6-13` and `49-69`, but no existing `*.test.js`/`*.test.jsx` files were found during discovery.

Add focused tests for:

- Session permission validation rejects unknown/duplicate permission strings.
- Users route denies Manager/Cashier/Warehouse and admits Owner/Admin.
- Sidebar shows Users and Audit to Owner/Admin only.
- Audit page admits Owner and excludes SuperAdmin assumptions.
- Admin target action menu excludes Owner/Admin mutations.
- Owner transfer UI is Owner-only.
- Mutation controls stay disabled while submitting.
- `412`, `403`, and `409` messages retain form state correctly.
- English and Arabic key sets are symmetric.
- RTL action menu and dialog semantics are correct.

### 18.5 Full authorization regression

Create an endpoint-permission manifest test that enumerates controller actions and fails when a classified sensitive action has only bare authentication. This prevents future endpoints from bypassing the catalog.

The manifest must include non-`/api/admin` controllers because current sensitive routes such as `/api/Banks`, `/api/FinancialAccounts`, `/api/Purchases`, and `/api/Sales` are not under the admin prefix.

## 19. Browser And Accessibility Certification

Required headed browser matrix:

| Dimension | Values |
|---|---|
| Viewport | Desktop `1440x1000`; mobile `390x844` |
| Direction | English/LTR; Arabic/RTL |
| Roles | Owner, Admin, Manager, Cashier, Warehouse |
| User states | Pending, active, disabled, invitation expired |
| Network | Normal, slow response, mutation timeout, offline after submit |

Required evidence:

- Owner and Admin see Users navigation; other roles do not.
- Direct URL and direct API calls fail for unauthorized roles.
- Owner can manage Admin; Admin cannot manage Admin/Owner.
- Invite and acceptance work without exposing raw token in logs or response captures.
- Role/disable/session reset invalidates the target's already-open authenticated tab on its next request.
- Stale simultaneous edits produce a visible conflict and no silent overwrite.
- No horizontal page overflow at mobile width.
- Keyboard reaches filters, rows/cards, menus, dialogs, and confirmations in logical order.
- Dialog focus is trapped and restored.
- Status is not conveyed by color alone.
- Form errors are associated with fields and announced.
- Arabic labels, role descriptions, date formatting, pagination, and action placement are correct.

Do not claim automated Playwright coverage from the existing test project. The discovered `Playwright` references are WhatsApp transport code, not a browser E2E framework for `PointOfSale.Web`.

## 20. Rollout Waves

### Wave 0: containment and contract freeze

- Confirm access-log usage of `/api/auth/register`; mark usage **UNKNOWN** until measured.
- Freeze permission names and role matrix with product/security owners.
- Remove or fail closed anonymous staff registration.
- Add a regression preventing its reintroduction.
- Remove the disabled maintenance route from POS UI.

Exit gate: no unauthenticated path can create a POS staff user.

### Wave 1: authority foundation

- Add normalized identities, tenant FK, owner constraint, `xmin`, and security-token schema.
- Add permission catalog, handler, policies, and session DTO projection.
- Add PostgreSQL migration preflight and integration tests.
- Keep UI unchanged except session compatibility.

Exit gate: backend can deterministically answer permission checks, and schema invariants are certified.

### Wave 2: user lifecycle API

- Add Users controller/service/DTOs.
- Add invite, accept, profile, role, status, reset, revoke, and ownership transfer.
- Add atomic audit and session-revocation tests.
- Add rate limits and bounded error contracts.

Exit gate: complete API role/tenant/adversarial matrix passes.

### Wave 3: Users UI

- Add route, navigation, Users workspace, dialogs, permission gates, English/Arabic translations, and responsive behavior.
- Correct Audit UI authority mismatch.
- Complete frontend unit tests and headed browser matrix.

Exit gate: all five roles and all lifecycle states have browser evidence in LTR/RTL desktop/mobile.

### Wave 4: application-wide permission hardening

- Replace classified bare `[Authorize]` and ad hoc role strings across core POS, finance, reports, storefront operations, and shipping.
- Replace route/sidebar role checks with permission checks.
- Preserve CommerceHub contextual capabilities as an additional intersection, not a replacement.
- Add endpoint-manifest architecture enforcement.

Exit gate: no classified sensitive operation relies on authentication alone, and direct API tests match the frozen matrix.

### Wave 5: optional future capabilities

Not approved by this blueprint:

- Custom roles.
- Per-user permission overrides.
- Multi-tenant user memberships.
- Multiple stores per tenant.
- Per-warehouse user assignment.
- SSO, MFA, SCIM, or enterprise directory sync.
- Per-device session inventory.
- Support impersonation.

Each requires a separate evidence-based design.

## 21. Implementation Acceptance Criteria

Implementation is complete only when all statements are true:

- Anonymous users cannot create POS staff.
- Only trusted tenant context selects the managed tenant.
- Owner/Admin can list users; all other roles receive backend denial.
- Admin cannot mutate Owner/Admin targets.
- Ordinary operations cannot create, demote, disable, or remove Owner.
- Ownership transfer is atomic and leaves exactly one Owner.
- Username/email normalization and global uniqueness are enforced by PostgreSQL.
- Every User has a valid Tenant FK.
- Every user mutation uses optimistic concurrency.
- Invite/reset tokens are random, hash-only, purpose-bound, expiring, revocable, and single use.
- Password, role, status, ownership, and explicit session changes invalidate current target sessions as designed.
- Security mutation and audit evidence commit atomically.
- No secret or credential appears in audit, logs, response DTOs, browser storage, or screenshots.
- UI route, navigation, and controls consume server-derived effective permissions.
- Backend authorization remains authoritative for direct requests.
- Existing CommerceHub lifecycle authority still constrains CommerceHub navigation and operations.
- All classified sensitive endpoints use a role/permission/resource policy rather than bare authentication.
- PostgreSQL integration, .NET tests, frontend tests, builds, pending-model checks, diff checks, and browser matrix pass.

## 22. Muse Implementation Contract

Muse may implement the approved waves only after the permission matrix and anonymous-registration disposition are explicitly accepted.

Muse must:

- Stay within the files listed in Section 17 plus focused tests, migration, model snapshot, and directly required localization/component files.
- Preserve default POS JWT/session, tenant resolution, CSRF, cookie, SuperAdmin, Storefront, CommerceHub customer, and Affiliate authority boundaries.
- Use the current context, password hasher, email sender, API error conventions, and session revocation architecture.
- Use hash-only one-time tokens.
- Make audit atomic for user authority mutations.
- Add focused tests with each production change.
- Report every changed file, migration effect, command, failure, and unresolved limitation.

Muse must not:

- Certify its own implementation.
- Use SuperAdmin capabilities as POS permissions.
- Introduce ASP.NET Identity or custom-role tables.
- Add tenant/store/warehouse memberships.
- Trust client tenant IDs, role grants, permissions, lifecycle state, or audit actor fields.
- Preserve anonymous staff registration through an undocumented fallback.
- Weaken `PosSessionAuthority`, `AdminTenantAuthority`, tenant filtering, CSRF, or refresh revocation.
- Store or log raw invite/reset/refresh credentials.
- Perform direct production data correction or silently resolve preflight collisions.
- Add support impersonation or global user search.

Muse stops and reports rather than guessing if preflight finds collisions, owner-cardinality defects, orphan users, migration drift, undocumented callers of anonymous registration, or a requirement for multi-store/per-warehouse membership.

## 23. SOL Review And Certification Contract

SOL remains the independent reviewer and certifier.

SOL must:

- Inspect the complete diff, including generated migration and model snapshot.
- Re-derive permission and target-rank behavior from source rather than trusting the implementation report.
- Verify no unrelated identity authority was merged or weakened.
- Run focused and full .NET tests as appropriate.
- Run frontend tests, lint, and production build.
- Run API Release build and EF pending-model verification.
- Run `git diff --check` and inspect final `git status`.
- Validate migration preflight against production-equivalent read-only data before approval.
- Exercise all five roles through direct API calls.
- Complete the headed browser matrix in Section 19.
- Verify session invalidation from a second already-authenticated browser context.
- Inspect audit rows and logs for tenant, actor, target, before/after, correlation, and secret absence.
- Issue a final verdict with explicit PASS/FAIL gates and residual risks.

SOL must fail certification if frontend hiding is the only enforcement, if any classified mutation remains bare-authenticated, if audit can commit separately from authority mutation, if Owner cardinality can race, if token material is persisted raw, or if tenant scope can be supplied by the client.

## 24. Open Decisions And Unknowns

| Item | Current state | Required owner |
|---|---|---|
| External use of `/api/auth/register` | **UNKNOWN** | Operations/API owner; inspect access logs before removal deployment |
| Manager finance access | Repository is inconsistent; blueprint proposes operational access but not financial statements/admin | Product and finance security |
| Warehouse access to purchase receipts | No explicit workflow assignment found; blueprint grants purchase view only | Product/operations |
| Invitation expiry | **UNKNOWN**; recommend 24 hours | Security/product |
| Password reset expiry | **UNKNOWN**; recommend 30 minutes | Security |
| Password complexity | POS has no shared policy; other identities commonly require at least 8 characters | Security/product; define one POS policy before implementation |
| Owner transfer reauthentication | Not currently available as a POS ceremony; recommended before release | Security |
| Email deliverability | **UNKNOWN** by environment; sender abstraction exists | Operations |
| Existing identity collisions/orphans/owner defects | **UNKNOWN** until migration preflight | Database/operations |
| Multi-store/per-warehouse assignment | No current evidence | Future product design |

Unknowns do not authorize broader implementation. The safe default is deny, stop, and obtain a decision.

## 25. Final Architecture Statement

The current repository already has the essential security substrate for tenant POS user management: SQL-authoritative sessions, trusted tenant resolution, strict user query filtering, immediate stale-role/disabled-user rejection, refresh revocation, append-only audit storage, password hashing, email delivery, React route guards, localization, and responsive UI infrastructure.

What is missing is not a second identity platform. It is a coherent operation-permission layer, a safe staff invitation and recovery lifecycle, atomic ownership/user invariants, tenant Users APIs, and a matching UI. Implementing those elements with the existing `User -> Tenant -> UserRole` authority is the smallest design that closes the current gaps without inventing unsupported multi-store or enterprise IAM complexity.

---

# SECURITY REMEDIATION UPDATE — 2026-09-17 (SOL 5.6, security remediation phase)

This section records the Wave 0 + permission-foundation remediation. The historical
discovery above is preserved unchanged. Status vocabulary: DISCOVERED (found, not
fixed), REMEDIATED (code changed), VERIFIED (focused test + direct evidence),
UNKNOWN (not measured), DEFERRED (belongs to a later Users wave by design).

## R1. Critical #1 — Anonymous POS staff registration: REMEDIATED, VERIFIED

1. Finding: anonymous callers could create an active POS `Cashier` via
   `POST /api/auth/register`.
2. Original evidence: `PointOfSale.API/Controllers/AuthController.cs:65-119`
   (`Role = UserRole.Cashier`, `IsActive = true`, `[AllowAnonymous]`).
3. Root cause: the endpoint combined tenant-resolved context with anonymous
   admission and directly persisted an active staff identity. No invitation or
   approval existed.
4. Remediation:
   - `AuthController.Register` no longer accepts anonymous calls. It requires
     the `users.invite` permission policy and returns `410 Gone` with code
     `POS_STAFF_REGISTRATION_DISABLED` without creating any user, session, or
     cookie. No fallback preserves the old behavior.
   - Proven distinction (repository evidence, not assumption):
     tenant/business onboarding is a different authority —
     `POST /api/platform/register` → `PlatformController` →
     `TenantRegistrationService.RegisterTenantAsync`, which creates a new
     tenant plus its `Owner`. That flow is untouched, and the POS
     `/register` page calls only `/platform/register` (never
     `/api/auth/register`), so legitimate onboarding keeps working.
   - The future invitation-acceptance endpoint is NOT implemented here; staff
     onboarding stays disabled until the Users waves ship.
5. Files changed: `PointOfSale.API/Controllers/AuthController.cs`;
   `PointOfSale.Tests/Security/ProductionSecurityHardeningTests.cs`;
   `PointOfSale.Tests/Security/PosAuthenticationLifecycleTests.cs`;
   `PointOfSale.Tests/Architecture/AdminAuthenticationSessionCertificationTests.cs`;
   `PointOfSale.Tests/Architecture/PosAdministrativeAuthorityCertificationTests.cs`.
6. Tests added/updated: `AnonymousStaffRegistration_IsDisabledWithoutCreatingAUser`,
   `DisabledStaffRegistration_DoesNotDependOnTenantContext`,
   `Register_IsDisabledWithoutCreatingIdentityOrSessionAuthority`,
   `PosStaffRegistration_MustFailClosedUntilInvitationAcceptanceExists`,
   `OrdinaryStaffRegistration_MustBeDisabledAndPermissionProtected`; the anonymous
   `AuthController.Register` entry was removed from the approved-anonymous list.
7. Verification: focused .NET suites 80/80 pass (see final remediation report).
   Direct evidence: disabled action returns 410 and persists zero users with and
   without tenant context.
8. Remaining limitation: staff onboarding (invite/accept UI and API) does not
   exist yet — operational impact, not a security hole. Fail-closed by design.
9. New assumption: none. No schema, token, or lifecycle artifact was introduced.

## R2. Critical #2 — Bare authorization on sensitive POS APIs: REMEDIATED, VERIFIED

1. Finding: sensitive controllers relied on bare `[Authorize]`, so any valid POS
   role could attempt finance, purchasing, sales, report, and shipping operations.
2. Original evidence: Section 7 of this report (e.g. `Banks`, `FinancialAccounts`,
   `Expenses`, `Purchases`, `Sales`, `FinancialReports`, `ShippingMethods`).
3. Root cause: authentication (valid session) was the final authorization decision;
   no operation-permission layer existed.
4. Remediation (smallest architecture consistent with Section 8):
   - `PointOfSale.Core/Security/PosPermissions.cs`: 56-permission catalog plus
     `PosRolePermissionCatalog` with the exact Section 9 role matrix. No Identity,
     no Role/Permission/UserPermission tables, no memberships, no second identity.
   - `PosPermissionRequirement` + `PosPermissionAuthorizationHandler`: fail closed
     for unknown permissions, unknown roles, missing role claims, multiple role
     claims, malformed principals, missing POS user/tenant/session claims, forged
     permission claims (never read), and non-POS schemes. Authorization additionally
     requires a `pos_sql_authority=current` marker that `Program.cs` strips from
     incoming tokens and re-adds only after `PosSessionAuthority` revalidates the
     token against current SQL state — so a stolen but revoked/stale token cannot
     satisfy a permission even with a correct role string.
   - `PosEndpointPermissionCatalog`: deterministic controller/action → permission
     mapping with per-operation overrides (create/edit/delete/approve/refund/
     transfer/adjust/configure/export distinguished where the blueprint requires).
   - `PosPermissionApplicationModelConvention` (registered in `Program.cs`) attaches
     the mapped scheme-bound policy (`JwtBearerDefaults.AuthenticationScheme`) to
     every classified action; one authorization policy per permission key.
   - `AuditLogsController` moved from `Owner,Admin` roles to
     `audit.view` / `audit.export` policies.
   - `PosSessionAuthority`, `AdminTenantAuthority`, `TenantResolutionMiddleware`,
     CSRF, refresh revocation, SuperAdmin/CommerceHub/Storefront/Affiliate schemes
     are preserved and untouched in behavior.
   - Two pre-existing authority ambiguities found by the new manifest and fixed
     explicitly (not folded into POS permissions): `MarketplaceReadController.GetOrders`
     now requires `CustomerJwt`+Customer role (it reads customer claims), and
     `StorefrontOnlineCustomerController.CheckEmailExists` is marked
     `[AllowAnonymous]` as its documented public anti-enumeration ceremony.
5. Files changed: the 5 new backend files above plus `Program.cs`,
   `PosSessionAuthority.cs`, `AuditLogsController.cs`, `MarketplaceReadController.cs`,
   `StorefrontOnlineCustomerController.cs`.
6. Tests added: `PosPermissionAuthorizationTests` (12: matrix, unknown role/permission,
   forged permission/role, malformed principals, SuperAdmin separation),
   `PosEndpointPermissionManifestTests` (8: full-assembly manifest, representative
   `/api/Banks`, `/api/FinancialAccounts`, `/api/Purchases`, `/api/Sales`,
   financial-report mappings, runtime wiring proof).
7. Verification: 80/80 focused tests pass; manifest enumerates every controller
   action and fails on any unclassified authenticated POS operation. Limitation:
   no live HTTP 401/403 per-endpoint run exists in this repo (no web harness);
   enforcement is proven at handler + metadata + wiring level.
8. Remaining limitation: none for the permission layer itself. Resource-level
   rules (tenant filters, Treasury/accounting invariants, CommerceHub lifecycle)
   remain where they were, now behind permission admission.
9. New assumption: none. Section 9 matrix applied exactly; `users.ownership.transfer`
   reserved Owner-only in code with no management UI.

## R3. Frontend permission foundation (High): REMEDIATED, VERIFIED (presentation only)

Backend remains the sole security boundary. Added: server-derived
`EffectivePermissions` on the POS session DTO (from trusted SQL role + catalog,
never client-supplied); `posPermissions.js` parser/validator (`hasPosPermission`,
duplicate/unknown rejection, exact parity with the backend catalog proven by test);
`PosPermissionRouteGuard`; `PermissionGate` (replacing misleading role-based
`PermissionGuard` semantics); permission-based `Sidebar` filtering; permission
route guards across POS/App routes with CommerceHub capability guards kept as an
additional intersection; audit page corrected to `audit.view`/`audit.export` with
no `SuperAdmin` POS assumption; disabled `/maintenance/reset-database` POS route
removed from `App.jsx` and the `Settings` danger tab plus orphaned
`ResetDatabase.jsx` removed (SuperAdmin controller itself untouched and still
disabled-by-design); tenant-registration page rehydrates the authoritative
`/auth/session` DTO instead of inferring a session in the browser.
No Users Management screen, invite/role/status/password UI, or lifecycle API was built.

## R4. Tenant isolation, privilege escalation, scheme separation: VERIFIED

- Tenant isolation: permission layer adds no tenant logic and cannot bypass it;
  existing suites pass unchanged (`AdminTenantOperationalScopeTests` foreign-tenant
  fail-closed for Banks/ExpenseCategories/FinancialAccounts/ShippingMethods;
  `Categories/Dashboard/FinancialReports` tenant-scope tests).
- Privilege escalation matrix proven in handler tests: Cashier→`finance.admin`
  deny, Warehouse→`purchases.manage` deny, Manager→`users.view`/`reports.financial.view`
  deny, Admin→`users.ownership.transfer` deny, unknown/missing/duplicate roles deny,
  forged permission claims ignored, SuperAdmin and non-POS principals deny.
- SuperAdmin/POS separation: 77/77 preservation tests pass (SuperAdmin auth/platform,
  CommerceHub vendor/order/data authority, accounting governance, cookie/session
  authorities, tenant registration, checkout authorization).

## R5. Out-of-scope High findings: DEFERRED (by mission design, not closed)

`No tenant user-management API/UI`, `xmin` concurrency + User→Tenant FK, and
normalized-identity uniqueness belong to the later Users waves (§14 of the mission)
and were deliberately not implemented. No schema/migration change was made in this
phase (EF pending-model: PASS by construction — zero entity/DbContext/configuration
files touched).

## Final security status

| Finding | Severity | Remediated | Independently Tested | Status |
|---|---|---|---|---|
| Anonymous POS staff registration (`POST /api/auth/register`) | Critical | Yes | Yes (410 + zero-persistence tests) | VERIFIED |
| Bare `[Authorize]` on sensitive POS APIs | Critical | Yes | Yes (manifest + handler + wiring tests) | VERIFIED |
| Frontend routes authenticated-only / role-based guard | High | Yes (foundation; Users screen deferred) | Yes (parser/guard/sidebar/audit tests) | VERIFIED |
| Audit UI/API authority mismatch | Medium | Yes (`audit.view`/`audit.export`) | Yes | VERIFIED |
| No user-management API/UI | High | No (out of scope) | N/A | DEFERRED |
| `xmin` + User→Tenant FK | High | No (out of scope) | N/A | DEFERRED |
| Normalized identity uniqueness | High | No (out of scope) | N/A | DEFERRED |
| Invitation/password-reset/ownership ceremonies | — | No (out of scope) | N/A | DEFERRED |

Certification verdict for this phase: CONDITIONAL PASS (see
`docs/reports/USER_MANAGEMENT_SECURITY_REMEDIATION_2026-09-17.md` §21).
Conditions: 9 pre-existing unrelated frontend failures (proven on pristine tree),
no live-HTTP per-endpoint run (no harness in repo), no headed browser matrix.
