# PASS M1-C CommerceHub Vendor Capability Authority Certification

CommerceHub Vendor Capability Authority is production-certifiable for inspected POS/Admin vendor runtime paths after explicit backend-issued capability expansion and capability-gated route/menu/dashboard hardening.

## Capability Findings
- Current vendor capability presentation derives from backend POS session `NavigationCapabilities` composed on registration, login, current session, and refresh.
- The frontend strictly allowlists CommerceHub vendor capabilities and rejects unknown, duplicate, malformed, missing, or fabricated capability payloads.
- Vendor capability truth remains backend-owned; frontend state may validate, suppress, withhold, and refetch only.

## Navigation Findings
- CommerceHub sidebar, duplicate e-commerce order/payment links, storefront links, CMS links, and analytics links consume current backend-issued capabilities.
- Direct routes for catalog publishing, orders, finance, payments, analytics, storefront builder/theme/version, CMS, and auth-provider configuration are guarded by the same accepted capability boundary.
- Navigation is candidate presentation only and cannot become action authority.

## Catalog Findings
- CommerceHub publish, update, unpublish, and bulk listing actions remain gated by `deriveCommerceHubVendorEligibilityPresentation`.
- Mutation responses are not cached as catalog authority; the runtime converges through current backend refetch.

## Order Findings
- Order list and detail routes require `commercehub.orders.view`.
- Order presentation uses actor-scoped query keys and backend tenant-admin order APIs; stale cached rows cannot cross tenant/user scope.

## Finance Findings
- Finance and earnings routes require `commercehub.finance.view`.
- Payout-method changes require current backend-derived finance eligibility before mutation.

## Analytics Findings
- Analytics funnels, attribution, ROI, campaign-cost, pixel-tracking, and analytics-provider routes require backend-issued `commercehub.analytics.view`.
- Storefront dashboard analytics shortcuts are hidden unless the current backend session carries analytics capability.

## Storefront Findings
- Storefront dashboard, builder, themes, theme editor, version history, and auth-provider routes require `commercehub.storefront.manage`.
- CMS pages, navigation, and footer routes require `commercehub.storefront.cms.manage`.
- Storefront dashboard CMS shortcuts are hidden unless the current backend session carries CMS capability.

## Bootstrap Findings
- Login, registration, session bootstrap, and refresh all re-enter backend capability composition.
- Focus, visibility, online, popstate, BFCache, and interval revalidation replace frontend capability presentation instead of preserving it.

## Cache Findings
- POS actor/tenant/role/session transitions clear prior POS query/runtime authority.
- Vendor-scoped finance, earnings, payments, order, catalog, storefront, and CMS caches remain derivative and cannot restore removed capabilities.

## Failure Findings
- 401/403 CommerceHub vendor authority loss purges vendor-scoped presentation and revalidates current POS session authority.
- Malformed capability DTOs fail closed at session authority derivation.

## Adversarial Findings
- Local storage, session storage, React state, contexts, query caches, feature flags, route/history state, mutation responses, cross-tab events, refresh behavior, and malformed DTOs cannot fabricate, preserve, replay, merge, repair, extend, or replace vendor capabilities.

## Runtime Hardening Performed
- Added backend-issued `commercehub.analytics.view`, `commercehub.storefront.manage`, `commercehub.storefront.cms.manage`, and `commercehub.storefront.preview`.
- Extended the frontend capability allowlist and eligibility presentation.
- Gated storefront, CMS, analytics, auth-provider, payment-method, and manual-payment routes through `CommerceHubVendorRouteGuard`.
- Filtered sidebar entries and storefront dashboard shortcuts through backend-derived capabilities.

## Exact Runtime Files Modified
- `PointOfSale.API/Controllers/AuthController.cs`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/components/Sidebar.jsx`
- `PointOfSale.Web/src/App.jsx`
- `PointOfSale.Web/src/pages/StorefrontDashboard.jsx`

## Regression Freezes Added
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts`
- `PointOfSale.Tests/Architecture/CommerceHubFrontendAuthorityCertificationTests.cs`

## Architecture Updates
- Vendor capability truth is backend-owned by vendor, capability, governance, participation, debt, suspension, blocking, and tenant authorities.
- Frontend Vendor Capability Authority is derivative presentation and enforcement only.

## Explicit Answers
- Vendor capability authority is owned by backend vendor, capability, governance, participation, debt, suspension, blocking, and tenant authorities.
- Frontend cannot fabricate capabilities.
- Navigation cannot expose unauthorized CommerceHub vendor capabilities after this pass.
- Catalog actions cannot bypass capability authority on inspected paths.
- Finance actions cannot bypass capability authority on inspected paths.
- Storefront actions cannot bypass capability authority on inspected paths.
- Stale capabilities cannot survive refresh as current authority.
- Cache cannot restore capabilities.
- Tenant leakage is contained by current backend actor scope and transition purge.
- Runtime changes were required.
- Vendor Capability Authority is production-certifiable for inspected runtime paths.
