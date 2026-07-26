# PASS SF-ADM1.1 Store Administration Entry & Navigation Authority Certification

PointOfSale.Web Store Administration entry is production-certifiable after direct-route and sidebar admission hardening. This pass reviewed only Store Administration runtime in PointOfSale.Web and the backend session/API boundaries directly consumed by that runtime.

## Administration Pages Reviewed

- `/storefront`, `/storefront/builder`, `/storefront/themes`, `/storefront/theme`, `/storefront/versions`
- `/storefront/cms/pages`, `/storefront/cms/pages/:pageId`, `/storefront/cms/navigation`, `/storefront/cms/footer`
- `/ecommerce/auth-providers`, `/ecommerce/orders`, `/ecommerce/orders/:orderId`
- `/ecommerce/payment-methods`, `/ecommerce/manual-payments`
- `/ecommerce/coupons`, `/ecommerce/promotions`, `/ecommerce/smart-offers`
- `/ecommerce/commerce-ai`, `/ecommerce/shipping-methods`, `/admin/shipping/setup`, `/admin/shipping/shipments`, `/admin/shipping/shipments/:id`, `/admin/shipping/health`
- `/ecommerce/blocked-phones`, `/ecommerce/online-customers`, `/ecommerce/whatsapp`, `/ecommerce/cart-recovery`, `/ecommerce/abandoned-checkouts`
- `/ecommerce/pixel-tracking`, `/analytics/funnels`, `/analytics/funnels/:funnelKey`, `/analytics/attribution`, `/analytics/attribution/models`, `/analytics/attribution/orders`, `/analytics/attribution/costs`, `/analytics/attribution/roi`, `/settings/analytics`

## Backend Endpoints And Services Reviewed

- `GET /api/auth/session`, `POST /api/auth/refresh`, `AuthController.BuildCommerceHubNavigationCapabilitiesAsync`, vendor lifecycle authority composition, `PosSessionAuthority`, tenant context resolution, and role/session validation.
- Store Admin consumed APIs: admin store config, CMS pages/navigation/footer, auth providers, admin orders, payment methods/manual payments, coupons, promotions, Commerce AI, shipping methods/admin shipping/carriers, blocked phones, online customers, WhatsApp admin, abandoned checkout/cart recovery, analytics, and pixel tracking.

## Findings

Entry authority begins only after backend `/api/auth/session` validates the POS session, tenant, role, lifecycle/governance state, and backend navigation capabilities. Frontend `AuthBootstrap` accepts only strict backend-shaped session evidence and stores no trusted Store Administration authority in LocalStorage or sessionStorage.

Navigation authority is backend-owned by `/api/auth/session` capabilities plus lifecycle blockers. Sidebar rendering is presentation-only and now maps Store Administration links through backend-issued capabilities instead of leaving coupons, promotions, Commerce AI, shipping, customer/phone, WhatsApp, and recovery links visible without Store Admin admission evidence.

Route authority is backend-owned. Runtime hardening wraps previously unguarded Store Administration direct URLs in `CommerceHubVendorRouteGuard`, so bookmarks, refreshes, history, and deep links require current backend capability and lifecycle evidence before rendering the admin page shell.

Capability authority is backend-owned. `commercehub.storefront.manage`, `commercehub.storefront.cms.manage`, `commercehub.orders.view`, `commercehub.payments.view`, and `commercehub.analytics.view` are emitted by backend session authority and rejected by the frontend if malformed, duplicated, unknown, stale, or lifecycle-incoherent.

Permission authority is backend-owned. Frontend does not infer Owner/Admin/Manager permission from role strings, routes, menu labels, React state, or cached DTOs. Backend endpoint authorization remains decisive for action/read admission.

Recovery is backend-authoritative. Initial load, refresh, focus, visibility return, online reconnect, BFCache `pageshow`, popstate, interval revalidation, 401, and 403 re-enter `/api/auth/session` or refresh/session authority. Runtime hardening expands the Store Admin endpoint family that triggers authority-loss revalidation.

Failure behavior is fail closed. 401/403/404/409/session expiry/tenant change/capability removal/subscription or lifecycle blocking cannot grant presentation. Network or timeout failures can show loading/error presentation only.

Concurrency converges to backend truth. Multiple tabs and browsers may temporarily show stale presentation until lifecycle revalidation or an admin API denial, then the POS vendor authority-loss event purges scoped presentation and re-enters backend session authority.

Adversarial attempts to edit sidebar data, React state, routes, LocalStorage, sessionStorage, cached DTOs, or bootstrap payloads cannot establish Store Administration because accepted session evidence requires valid GUID actor/tenant, allowed POS role, allowlisted unique capabilities, matching lifecycle tenant, valid lifecycle state, and open lifecycle blockers.

## Administration Pipeline

Login -> Backend Session -> Tenant Resolution -> Store Ownership/Lifecycle -> Capability Resolution -> Permission Resolution -> Navigation Admission -> Route Admission -> Presentation.

- Administration Authority begins at backend `/api/auth/session` and protected backend Store Admin APIs after current session/tenant/lifecycle authorization.
- Navigation Authority begins when backend builds current `NavigationCapabilities` and lifecycle blockers.
- Route Authority begins when `CommerceHubVendorRouteGuard` accepts current backend-shaped session/capability/lifecycle evidence.
- Presentation begins only after route admission and page-specific backend API reads return accepted evidence.

## Boundary Review

- Authentication Boundary: backend POS auth/session. Recovery source: `/api/auth/session` or refresh. Failure: logout or protected presentation withheld.
- Tenant Boundary: backend trusted tenant context and session tenant. Recovery source: current backend session/API. Failure: denied/withheld.
- Store Boundary: backend tenant/store lifecycle and Store Admin services. Recovery source: backend session/API reads. Failure: route/API denial.
- Capability Boundary: backend navigation capability composition. Recovery source: `/api/auth/session`. Failure: hidden sidebar and denied routes.
- Permission Boundary: backend role and endpoint authorization. Recovery source: backend session/API. Failure: 401/403.
- Navigation Boundary: frontend renders only admitted backend capabilities. Recovery source: session revalidation. Failure: hidden links.
- Route Boundary: `CommerceHubVendorRouteGuard` consumes backend authority. Recovery source: session revalidation. Failure: redirect to dashboard.
- Recovery Boundary: lifecycle events and API denials invalidate/refetch only. Failure: stale presentation cleared/withheld.
- Presentation Boundary: React/sidebar/pages format current evidence only. Recovery source: backend APIs. Failure: loading/error/empty states.

## Cross-System Isolation

Store Administration authority is isolated from PointOfSale.Storefront, CommerceHub customer/marketplace, Affiliate, and Super Admin runtimes. Those systems may share backend data domains, but Store Administration entry uses only POS backend session, tenant, lifecycle, capability, route, and endpoint authority.

## Runtime Hardening Performed

- Wrapped unguarded Store Administration direct routes with backend capability route guards.
- Added sidebar Store Admin path-to-capability admission for previously unguarded links.
- Expanded POS vendor authority-loss endpoint detection for Store Admin APIs so 401/403 causes backend session revalidation.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/App.jsx`
- `PointOfSale.Web/src/components/Sidebar.jsx`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`

## Regression Freezes Added

- `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts`
- `PointOfSale.Tests/Architecture/StoreAdministrationEntryNavigationAuthorityCertificationTests.cs`

## Certification Answers

- Did you review only PointOfSale.Web Store Administration runtime? Yes.
- Which frontend files were modified? `App.jsx`, `Sidebar.jsx`, `posVendorSessionAuthority.js`, and its focused test.
- Which backend files were modified? None.
- Who owns Store Administration entry? Backend POS session, tenant, lifecycle, capability, and endpoint authorization.
- Who owns Storefront capability? Backend `/api/auth/session` capability and lifecycle resolution.
- Who owns Navigation Authority? Backend session capabilities plus lifecycle blockers.
- Who owns Route Authority? Backend evidence consumed by `CommerceHubVendorRouteGuard`; frontend only enforces presentation withholding.
- Can frontend establish Store Administration? No.
- Can Sidebar become authority? No.
- Can React become authority? No.
- Can LocalStorage become authority? No.
- Can deep links bypass authorization? No.
- Can stale tabs retain Store Administration authority? No; they converge by revalidation/API denial and fail closed.
- Where does Store Administration begin? Backend `/api/auth/session` and protected Store Admin API authorization.
- Where does Navigation Authority begin? Backend `BuildCommerceHubNavigationCapabilitiesAsync` plus lifecycle blocker resolution.
- Where does Route Authority begin? At guarded frontend route admission after backend session evidence is accepted.
- Where does Presentation begin? After route admission and page backend reads return accepted evidence.
- Were runtime changes required? Yes.
- Is Store Administration Entry production-certifiable? Yes.
