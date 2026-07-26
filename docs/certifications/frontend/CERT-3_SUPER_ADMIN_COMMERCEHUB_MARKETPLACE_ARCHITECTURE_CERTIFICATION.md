# CERT-3

# Super Admin + CommerceHub Marketplace Architecture Certification

Certification date: 2026-07-20

Certification basis: implemented source code only. Existing documentation was not used as authority.

Audited frontend source areas:

- `PointOfSale.Web/src/App.jsx`
- `PointOfSale.Web/src/super-admin/**`
- `PointOfSale.Web/src/auth/**`
- `PointOfSale.Web/src/components/AuthBootstrap.jsx`
- `PointOfSale.Web/src/components/CommerceHubVendorRouteGuard.jsx`
- `PointOfSale.Web/src/pages/CommerceHubSettings.jsx`
- `PointOfSale.Web/src/pages/CommerceHubFinance.jsx`
- `PointOfSale.Web/src/pages/CommerceHubPayments.jsx`
- `PointOfSale.Web/src/services/api.js`
- `PointOfSale.Web/src/marketplace-cms/**`
- `PointOfSale.CommerceHub/src/**`

## Executive Summary

Super Admin UI ownership is implemented in `PointOfSale.Web/src/super-admin/**` and mounted under `/super-admin` in `PointOfSale.Web/src/App.jsx`. Its authority starts at authenticated Super Admin route presentation and API consumption through `superAdminApiClient`. Its authority ends at presentation, client-side admission checks, route/capability gating, and invocation of backend Super Admin APIs.

CommerceHub Marketplace customer runtime ownership is implemented in the independent `PointOfSale.CommerceHub` frontend app. Its authority starts at marketplace presentation, navigation, local customer/cart state, client validation, and consumption of CommerceHub, marketplace public, and customer APIs. Its authority ends before vendor authority, pricing authority, inventory authority, payment execution, settlement, refunds, payouts, and accounting.

Marketplace customer runtime is architecturally separated from POS Admin by package and router: `PointOfSale.CommerceHub/package.json` defines a standalone Vite app, and `PointOfSale.CommerceHub/src/router/index.tsx` defines customer marketplace routes. POS Admin and Super Admin remain in `PointOfSale.Web`, including vendor administration and legacy public marketplace CMS/product routes in `PointOfSale.Web/src/App.jsx`.

No source evidence showed the CommerceHub customer runtime calling `/api/super-admin` or using Super Admin APIs directly.

Final certification: PASS WITH OBSERVATIONS.

## Implemented Runtime Ownership

Super Admin owns implemented platform governance and configuration surfaces:

- `/super-admin` is mounted behind `SuperAdminProtectedRoute` and `SuperAdminCapabilityRoute` in `PointOfSale.Web/src/App.jsx`.
- `SuperAdminProtectedRoute` requires `user.role === 'SuperAdmin'` and `user.authType === 'super_admin'`, revalidates session on interval, focus, online, popstate, pageshow, and visibility changes, and redirects invalid sessions to `/super-admin/login`.
- `SuperAdminCapabilityRoute` admits child routes only when the server-derived Super Admin identity contains the required navigation capability.
- `SuperAdminLayout` renders navigation according to `hasSuperAdminNavigationCapability` and includes Finance, Payments, Payment Providers, Orders, Reviews, Users, Marketplace Governance, Marketplace Operations, Refunds, Payouts, Commission, Promo Codes, Homepage, Storefront Defaults, Marketplace CMS, and Marketplace Footer.
- `superAdminApiClient` uses base URL `/api/super-admin`, `withCredentials`, CSRF headers, request IDs, token refresh, timeout handling, and fail-closed redirect on 401/403.

CommerceHub Marketplace owns implemented customer marketplace surfaces:

- `PointOfSale.CommerceHub/src/router/index.tsx` implements routes for home, products, product detail, cart, checkout, payment recovery, email verification, customer auth, customer account, customer settings, orders, refunds, and CMS slug pages.
- `PointOfSale.CommerceHub/src/layouts/AppLayout.tsx` provides a CommerceHub header, footer, language-driven `dir`/`lang`, and query invalidation on language changes.
- `PointOfSale.CommerceHub/src/api/endpoints.ts` defines marketplace/customer runtime APIs under `/api/commercehub`, `/api/marketplace`, `/api/homepage`, and `/api/customers`.
- `PointOfSale.CommerceHub/src/api/http-client.ts` sends CommerceHub request headers, language headers, CSRF tokens, credentials, and purges customer session authority on 401/403 from customer-authority endpoints.

Neither frontend owns backend financial authority, inventory authority, payment execution, settlement, refund execution, payout execution, or accounting.

## Super Admin Runtime

Implemented behavior:

- Platform and marketplace governance is implemented by `MarketplaceGovernancePage` and `marketplaceGovernanceService`, which load dashboard/listing evidence from `/api/super-admin/marketplace-governance/*` and perform suppression, restoration, and tenant suspension/restoration through Super Admin mutation endpoints.
- Marketplace operational dashboards and repair controls are implemented by `MarketplaceOperationsPage` and `marketplaceOperationsService`, using `/api/super-admin/marketplace-operations/*` for dashboard, global reindex, tenant reindex, catch-up reindex, listing verify, and tenant verify actions.
- Commission configuration is implemented by `CommissionManagementPage` and `commissionService`, using `/api/super-admin/commission` endpoints for rule CRUD and preview. Category/vendor option reads use raw backend endpoints for option display only.
- Marketplace homepage configuration is implemented by `HomepageBuilderPage`, `HomepageManagerPage`, and `homepageService`, using `/api/super-admin/homepage/sections` endpoints for draft, publish, reorder, template generation, and schemas.
- Marketplace CMS and policy-style pages are implemented by `MarketplaceCmsPagesPage`, `MarketplaceCmsPageEditor`, and `marketplaceCmsService`, using `/api/super-admin/marketplace/pages`, publish, preview, delete, footer, and translate endpoints.
- Marketplace footer configuration is implemented by `MarketplaceFooterSettingsPage` through `marketplaceCmsService`, using `/api/super-admin/marketplace/footer`.
- Storefront footer defaults are implemented by `StorefrontFooterDefaultsPage` and `storefrontFooterService`, using `/api/super-admin/storefront/footer`.
- Promo/campaign controls are implemented by `PromoCodesPage` and `promoService`, using `/api/super-admin/promo-codes` endpoints and presenting promos as commission-only controls.
- Payment provider configuration is implemented by `PaymentProvidersPage` and `paymentProvidersService`, using provider dashboard/configuration/secret metadata/rotation endpoints. Secret values are entered as password replacement fields and displayed secret evidence is metadata/fingerprint, not raw secret values.
- Review moderation is routed at `/super-admin/reviews` behind `super-admin.reviews.view`.
- User/vendor lifecycle and restrictions are implemented by `UsersPage` and `usersService`, which expose list/detail/risk and actions including `suspend`, `ban`, `activate`, `financial-lock`, and `financial-unlock` through `/api/super-admin/users/*`.
- Analytics and operational dashboards are implemented across Finance, Marketplace Governance, Marketplace Operations, Payment Providers, Orders, Payments, Refunds, Payouts, and Promo pages through scoped Super Admin query keys.

Authority boundary conclusion:

- Super Admin frontend is a privileged control surface and API consumer.
- Super Admin frontend does not become the source of record for governance, commission, payment provider, CMS publication, or operational repair state.

## Marketplace Runtime

Implemented behavior:

- Homepage uses `homepageSectionsQueryOptions`, category tree queries, product detail queries, and product list queries to resolve published sections. No homepage authority is locally persisted in the CommerceHub runtime.
- Marketplace catalog and product discovery use `/api/commercehub/products`, `/api/marketplace/products/:id`, `/api/commercehub/search`, `/api/commercehub/search/autocomplete`, `/api/commercehub/search/popular`, and search event APIs.
- Categories use `/api/commercehub/categories/tree` and are built into a frontend tree for display/filtering.
- Product detail resolves variants through `/api/marketplace/products/:id/variant-resolution`, presents seller/store identity from product evidence, and blocks add-to-cart when currency, availability, variant, stock, or cart-lock checks fail.
- Cart uses Zustand persisted local state, server cart sync, server cart pricing, live product revalidation, conflict recovery, locked-cart recovery, and promo invalidation handling.
- Checkout requires authenticated customer state, validates customer contact/address fields locally, saves the cart, reprices with `/api/commercehub/cart/price`, verifies selected payment method availability, submits to `/api/commercehub/checkout` with an idempotency key, then initiates online payment through `/api/commercehub/payments/initiate` only when backend order response requires payment initiation.
- Payment presentation supports COD, Paymob redirect handoff, and Stripe embedded `client_secret` handoff. The frontend presents handoff evidence and does not execute provider settlement or payment authority itself.
- Order presentation exists in checkout success/pending UI, payment recovery, customer order list, and customer order detail routes.
- CMS pages and footer consume `/api/marketplace/pages/:slug` and `/api/marketplace/footer`.
- Reviews consume `/api/marketplace/reviews` and `/api/marketplace/products/:productId/reviews` style endpoints.
- Customer account, settings, orders, refunds, and payout-details presentation consume `/api/customers/*` endpoints and are guarded by customer session/capability authority.

Not implemented in the audited CommerceHub customer router:

- No vendor directory route was found in `PointOfSale.CommerceHub/src/router/index.tsx`.
- No wishlist route was found in `PointOfSale.CommerceHub/src/router/index.tsx`.
- No recommendations route/service was found in `PointOfSale.CommerceHub/src/router/index.tsx` or `PointOfSale.CommerceHub/src/services/**`.
- No Commerce AI chat route/widget was found in `PointOfSale.CommerceHub/src/router/index.tsx` or `PointOfSale.CommerceHub/src/components/layout/**`.

## Vendor Isolation

Implemented behavior:

- POS/Admin vendor routes are guarded by `CommerceHubVendorRouteGuard`, which uses `canAccessCommerceHubVendorNavigationCapability`.
- `derivePosVendorSessionUserAuthority` requires valid POS actor role, tenant ID, user ID, server-derived vendor lifecycle authority, server-derived navigation capabilities, and lifecycle compatibility with operating capabilities.
- Vendor lifecycle presentation requires `vendorLifecycleAuthority.vendorId` to equal the session `tenantId` and rejects restricted lifecycle evidence that does not block operating surfaces.
- Vendor catalog management in `CommerceHubSettings` derives current vendor identity from `currentUser.tenantId`, filters products/listings/KPIs/analytics/notifications through `deriveCurrentVendorScopedArray` and `deriveCurrentVendorScopedDto`, and returns an empty result if any array item is foreign or malformed.
- Vendor finance, earnings, and payments query keys include `['commercehub-vendor-session', tenantId, userId, ...]`, preventing shared query-cache keys across vendor actors.
- `AuthBootstrap` clears query cache and POS runtime cache when actor scope changes and removes CommerceHub vendor queries when vendor authority is lost.

Conclusion:

- Frontend maintains implemented vendor isolation for POS/Admin vendor administration presentation.
- No implemented UI path was found that allows one vendor to render another vendor's accepted administration DTO after frontend admission checks.
- Frontend isolation remains presentation/admission defense only; backend remains authoritative for tenant access.

## Marketplace API Consumption

Implemented behavior:

- CommerceHub runtime endpoints are declared under `/api/commercehub`, `/api/marketplace`, `/api/homepage`, and `/api/customers`.
- CommerceHub runtime source search found no `/api/super-admin` consumption.
- Super Admin source uses `/api/super-admin` through `superAdminApiClient`.
- POS/Admin vendor administration uses `/api/admin/commercehub`, `/api/commercehub`, `/api/admin/*`, and vendor-scoped endpoints through the POS `api` client.

Conclusion:

- Marketplace customer runtime does not duplicate Super Admin authority.
- Super Admin APIs are consumed only by the Super Admin frontend code path in the audited source.

## State Management

Implemented behavior:

- Super Admin and marketplace query caches are separated in `PointOfSale.Web/src/queryClient.js` and applied through `RuntimeQueryBoundaries`.
- CommerceHub customer runtime uses its own QueryClient with `staleTime: 30000`, `retry: 1`, and `refetchOnWindowFocus: false`.
- Customer session state is managed by Zustand in `customer-auth-store`, with request IDs and authority revision checks to prevent stale hydration from overwriting newer authority.
- Customer session bootstrap revalidates on interval, focus, online, popstate, pageshow, visibility, storage logout events, and customer-authority loss events, then removes customer query keys when authority changes or disappears.
- Cart state is Zustand-persisted under `commercehub-cart`, scoped as `guest` or `customer:<id>`, and migrates guest cart into authenticated server-backed cart only during bootstrap.
- Cart sync handles conflict and locked-cart errors by refreshing from backend cart state.
- Checkout uses a submission-in-flight ref and idempotency key rotation tied to a checkout fingerprint.
- Marketplace product/search/category/homepage reads use React Query keys with request parameters included in the key.
- POS/Admin vendor pages use tenant/user scoped query keys and clear caches on actor changes.

Conclusion:

- State ownership is split by runtime and actor scope.
- Race protection is implemented for customer hydration, Super Admin refresh, POS session bootstrap, cart conflicts, cart locks, and checkout idempotency.
- Optimistic updates are limited; authoritative server refresh/invalidation is used after mutations.

## Security Boundaries

Implemented behavior:

- Super Admin protected routes require Super Admin role, `authType`, session bootstrap, revalidation, and navigation capabilities.
- Super Admin session payload admission requires valid GUID, email, `role === 'SuperAdmin'`, Bearer token type, future expirations, token version, and known non-duplicated capabilities.
- POS/Admin protected routes require authenticated POS session through `ProtectedRoute`.
- POS/Admin CommerceHub vendor routes require vendor navigation capability plus lifecycle block checks.
- CommerceHub customer account/orders/refunds/settings routes require customer session and customer navigation capabilities.
- Customer redirect targets are normalized to same-origin relative paths.
- POS API client appends CSRF headers, retries only idempotent methods, refreshes tokens, emits vendor authority loss on 401/403 for CommerceHub vendor endpoints, and redacts unsafe backend details from user-facing error messages.
- CommerceHub runtime API client appends CSRF headers and dispatches customer authority loss on 401/403 from customer-authority endpoints.
- Payment provider secret UI displays configured markers, password replacement fields, and secret metadata/fingerprints rather than raw active secret values.

Conclusion:

- Implemented frontend authorization boundaries are capability- and role-gated for Super Admin, vendor, and customer routes.
- Frontend does not claim final authorization authority; it fails closed and revalidates against backend session/profile endpoints.

## Configuration Flow

Implemented one-way flow:

- Super Admin creates/updates/publishes marketplace homepage sections through `/api/super-admin/homepage/sections*`.
- CommerceHub runtime reads published homepage sections through `/api/homepage/sections`.
- Super Admin creates/publishes marketplace CMS pages and footer through `/api/super-admin/marketplace/pages*` and `/api/super-admin/marketplace/footer`.
- CommerceHub runtime reads CMS pages and footer through `/api/marketplace/pages/:slug` and `/api/marketplace/footer`.
- Super Admin configures commission, promos, payment providers, governance, and operations through `/api/super-admin/*`.
- CommerceHub runtime consumes published/catalog/pricing/payment eligibility/order evidence through public/customer marketplace APIs and does not write Super Admin configuration.

Conclusion:

- Implemented configuration flows from Super Admin configuration/publication to published runtime reads to customer experience.
- No reverse write from CommerceHub customer runtime into Super Admin configuration was found.

## Marketplace Checkout

Implemented behavior:

- Cart groups items by `storeId` and `storeName` and presents vendor/store grouping in cart and checkout summary.
- Cart pricing is requested from `/api/commercehub/cart/price`; the frontend blocks checkout while pricing is refreshing, errored, missing, or when no common payment method is available.
- Payment method availability is server-provided in `CommerceHubCartPricingResponse.paymentMethods` and the UI disables unavailable methods with backend reasons.
- Checkout validates customer fields locally but creates the order only through `/api/commercehub/checkout`.
- Checkout request carries cart version, customer fields, selected payment method, server-provided shipping total, store shipping selections, promo code, stores, listing IDs, product IDs, variant IDs, quantities, and expected unit prices.
- Checkout response admission requires lifecycle authority, financial authority, valid order IDs, order number, currency, payment method, statuses, totals, sub-orders, items, and payment session evidence.
- Online payment initiation requires payment initiation response to match the current order ID, provider, expected amount, expected currency, and handoff type.
- Order confirmation presents backend-created order and split sub-orders; payment-pending state preserves recovery context and supports retrying initiation for the same authoritative order.

Conclusion:

- Marketplace checkout presentation is multi-vendor and server-authoritative.
- Frontend owns client validation, presentation, idempotency key management, and payment handoff display only.

## UX Consistency

Implemented behavior:

- Loading states exist for Super Admin secure session checks, POS session restoration, CommerceHub auth loading, homepage skeletons, product grid skeletons, product detail skeletons, cart/checkout refresh messages, and Super Admin page skeletons/spinners.
- Error states include `EmptyState`, `InlineErrorState`, toast messages, inline Super Admin action errors, and retry handlers for product, order, refunds, reviews, and search surfaces.
- Retry behavior is implemented through React Query retry settings, explicit refetch actions, POS idempotent API retries, and checkout payment retry/recovery.
- Empty states are implemented for homepage with no sections, empty cart, no products/listings, missing product, no CMS pages, no payment methods, and unavailable dashboards.
- Form validation exists for checkout customer fields, Super Admin commission/promo forms, CMS fields, payment provider configuration, and vendor catalog publish attributes.
- Offline assumptions are limited to revalidation on `online`; no offline-capable marketplace order submission was found.

## Runtime Boundary

Frontend owns implemented:

- Presentation.
- Navigation.
- Client validation.
- Local state.
- Rendering.
- Query caching and invalidation.
- Admission checks for backend evidence before display.

Frontend does not own implemented:

- Vendor authority.
- Commission authority.
- Marketplace policy authority.
- Pricing authority.
- Inventory authority.
- Payment execution.
- Settlement.
- Refund execution.
- Payout execution.
- Accounting.

Implementation-backed boundaries:

- Financial evidence is admitted by `requireMarketplaceOrderFinancialAuthority`, `requireCommerceHubCartPricingAuthority`, `requireCommerceHubCartStateCurrencyAuthority`, and `requirePaymentAmountAuthority`.
- Order creation evidence is admitted by `requireCheckoutOrderCreationAuthority` and payment handoff evidence by `requirePaymentInitiationAuthority`.
- Product/catalog currency evidence is admitted by `requireCommerceHubCatalogCurrency`.
- Vendor session and lifecycle authority is admitted by `derivePosVendorSessionUserAuthority` and `deriveCommerceHubVendorLifecyclePresentation`.
- Super Admin session authority is admitted by `deriveSuperAdminSessionAuthority`.

## Critical Invariants

Implementation-backed invariants:

- CommerceHub runtime does not consume `/api/super-admin`.
- CommerceHub checkout cannot submit unless cart pricing evidence is present and current.
- CommerceHub checkout reprices immediately before order creation.
- CommerceHub checkout creates orders through `/api/commercehub/checkout` only.
- Online payment initiation is a post-order handoff through `/api/commercehub/payments/initiate` and must match the created order, expected amount, expected currency, and provider.
- Marketplace order financial evidence is rejected when totals, sub-order totals, item totals, or currency evidence contradict.
- Product/catalog evidence is rejected when CommerceHub currency is not EGP.
- Product detail blocks add-to-cart when product currency lacks CommerceHub authority, product is unavailable, selected variant is missing, variant is out of stock, variant resolution is pending/failed, or cart is locked.
- Cart conflict and cart lock responses refresh from backend cart state rather than preserving stale local state.
- Vendor administration DTOs are rejected when tenant/vendor identity does not match the current POS vendor session.
- Vendor lifecycle evidence must match tenant ID and must block operating capabilities when lifecycle status is not Approved or Active.
- Super Admin route rendering requires Super Admin role, auth type, and server-derived capability evidence.
- Payment provider secrets are represented in Super Admin as replacement inputs and metadata/fingerprints, not raw displayed secrets.
- Marketplace published CMS/footer/homepage content is read from public marketplace/runtime APIs, not locally persisted as frontend authority.

## Architecture Boundaries

Super Admin implemented ownership:

- Marketplace governance.
- Vendor/user governance and restrictions.
- Marketplace configuration and publication for homepage, CMS pages, footer, promos, commission rules, payment providers, and operational dashboards.
- Operational administration for marketplace governance, operations, orders, reviews, payments, refunds, payouts, and finance dashboards.

CommerceHub Marketplace implemented ownership:

- Customer marketplace experience.
- Product discovery and browsing.
- Search and categories presentation.
- Product detail and variant presentation.
- Cart presentation and local intent state.
- Checkout presentation and payment handoff presentation.
- Customer account/orders/refunds presentation.
- Marketplace CMS page and footer presentation.

Neither implemented frontend owns:

- Financial authority.
- Inventory authority.
- Payment execution.
- Settlement.
- Refund execution.
- Payout execution.
- Accounting.

## Architecture Observations

- CommerceHub customer runtime is a separate app, but `PointOfSale.Web` still contains marketplace-adjacent UI: Super Admin, POS/Admin vendor CommerceHub administration, service marketplace routes, and legacy public marketplace CMS/product routes.
- Vendor directory, wishlist, recommendations, and Commerce AI chat were not found as implemented routes/services in `PointOfSale.CommerceHub`.
- CommerceHub cart summary computes local display totals from local price snapshots and shipping snapshots, but checkout is blocked until backend cart pricing is available and checkout reprices immediately before order creation.
- Marketplace analytics in `PointOfSale.CommerceHub/src/services/analytics-service.ts` is debug console logging only; search click/impression/filter APIs exist in `commerceHubService`.

## Architecture Risks

No architectural violations were identified.

## Certification Checklist

| Area | Result |
| --- | --- |
| Super Admin Runtime | PASS |
| Marketplace Runtime | PASS |
| Vendor Isolation | PASS |
| Marketplace API Consumption | PASS |
| State Management | PASS |
| Security Boundaries | PASS |
| Configuration Flow | PASS |
| Marketplace Checkout | PASS |
| UX Consistency | PASS |
| Runtime Boundary | PASS |
| Critical Invariants | PASS |
| Architecture Boundaries | PASS |
| Overall Certification | PASS WITH OBSERVATIONS |

## Final Certification

PASS WITH OBSERVATIONS
