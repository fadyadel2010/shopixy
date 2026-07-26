# CERT-2 POS Admin + Storefront Architecture Certification

Certification date: 2026-07-20

Certification scope: POS Admin frontend architecture, Storefront frontend architecture, configuration flow, state management, projection/API consumption, storefront runtime, checkout presentation, CMS/media presentation, authorization boundaries, tenant isolation, and frontend security boundaries.

Source authority: implementation only. Documentation and comments were treated as secondary evidence unless matched by executable frontend behavior.

Out of scope: backend implementation, database implementation, payment internal runtime, settlement, refund execution, payout execution, accounting, and infrastructure.

Final Certification: PASS

## Executive Summary

The audited frontend source implements a clear split between POS/Admin ownership in `PointOfSale.Web` and customer storefront runtime ownership in `PointOfSale.Storefront`.

POS/Admin owns storefront administration, catalog administration, CMS/navigation/footer administration, payment/shipping configuration surfaces, Commerce AI controls, and storefront builder/theme/version workflows. These surfaces are protected by the POS authenticated route tree and, for CommerceHub/storefront functionality, capability or role guards in `PointOfSale.Web/src/App.jsx:233-434`.

Storefront owns the customer-facing runtime, including tenant/session bootstrap, store configuration hydration, homepage, catalog/search/category/product pages, cart, checkout, customer auth/account/order history, CMS page rendering, navigation/footer, affiliate landing/recovery routes, and Commerce AI widget display. Runtime endpoints are centralized under storefront/public API paths in `PointOfSale.Storefront/src/services/api/endpoints.ts:7-68`, and no direct admin endpoint consumption was identified inside `PointOfSale.Storefront/src`.

The frontend architecture passes this certification because source-backed frontend boundaries keep business authority in backend APIs while the frontend performs routing, presentation, local UX state, request construction, and defensive client-side admission checks.

## POS Admin Runtime

Implemented owner: `PointOfSale.Web`.

Implemented behavior:

- POS Admin route protection starts with `ProtectedRoute` wrapping the authenticated app layout (`PointOfSale.Web/src/App.jsx:233-434`).
- Storefront builder, theme marketplace/editor, and version routes require `commercehub.storefront.manage` (`PointOfSale.Web/src/App.jsx:330-337`).
- Storefront CMS pages, navigation, and footer routes require `commercehub.storefront.cms.manage` (`PointOfSale.Web/src/App.jsx:338-344`).
- Payment method and manual payment administration require `commercehub.payments.view` (`PointOfSale.Web/src/App.jsx:299-302`).
- E-commerce orders, online customers, WhatsApp, cart recovery, and abandoned checkout pages require `commercehub.orders.view` (`PointOfSale.Web/src/App.jsx:311-325`, `PointOfSale.Web/src/App.jsx:380-390`).
- Shipping setup/workspace routes are role-guarded for `Owner`, `Admin`, and `Manager`, while shipment/health routes use CommerceHub storefront capability guards (`PointOfSale.Web/src/App.jsx:369-378`).
- Store configuration administration uses `/admin/store-config`, runtime preview uses `/storefront/store-config`, draft page layout save and explicit publish are separate, and config updates include `If-Match` when `rowVersion` is present (`PointOfSale.Web/src/modules/storefront/services/storeConfigService.js:26-150`).
- CMS administration uses `/admin/pages`, `/admin/cms/navigation`, and `/admin/footer`; publish, rollback, navigation update, and navigation publish include concurrency headers (`PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:40-148`).
- Storefront settings inside the generic settings page are gated by `commercehub.storefront.manage` both in tab rendering and component behavior (`PointOfSale.Web/src/pages/Settings.jsx:694-729`, `PointOfSale.Web/src/pages/StorefrontSettings.jsx:30-67`, `PointOfSale.Web/src/pages/StorefrontSettings.jsx:185-191`).
- Storefront settings save preserves the accepted candidate configuration rather than replacing unrelated fields with hardcoded defaults (`PointOfSale.Web/src/pages/StorefrontSettings.jsx:109-132`).
- Payment method administration fetches `/admin/payment-methods`, renders an empty/error state on fetch failure, and does not render hardcoded fallback payment methods (`PointOfSale.Web/src/pages/PaymentMethods.jsx:100-112`, `PointOfSale.Web/src/pages/PaymentMethods.jsx:348-365`).

Assessment: PASS.

## Storefront Runtime

Implemented owner: `PointOfSale.Storefront`.

Implemented behavior:

- Storefront bootstrap is deterministic: `resolveTenant()` -> `initializeSession()` -> `fetchStoreConfig()` -> `applyTheme()` -> state hydration (`PointOfSale.Storefront/src/bootstrap/bootstrap.ts:1-23`, `PointOfSale.Storefront/src/bootstrap/bootstrap.ts:117-210`).
- Tenant resolution accepts dev-only query/env fallback and subdomain resolution; production query/env tenant overrides are not admitted (`PointOfSale.Storefront/src/bootstrap/tenantResolver.ts:104-200`).
- Request context supplies `X-Tenant-Id`, `X-Session-Id`, and optional `X-Customer-Id` after tenant bootstrap (`PointOfSale.Storefront/src/bootstrap/requestContext.ts:67-95`).
- The storefront HTTP client rejects non-bootstrap requests before request context is ready and then attaches tenant/session/customer, affiliate, language, and CSRF headers (`PointOfSale.Storefront/src/services/api/httpClient.ts:156-205`).
- Storefront API wrappers consume storefront/public endpoints for config, products, discovery, categories, cart, coupons, smart offers, CMS pages, navigation, footer, account orders, affiliate funnel, and affiliate landing data (`PointOfSale.Storefront/src/services/api/storefrontApi.ts:35-620`).
- Customer routes include home, products, product details, categories, cart, checkout, checkout pending, order confirmation/tracking, CMS pages, search, auth routes, account, wishlist, recovery links, affiliate landing, and localized `/ar` and `/en` versions (`PointOfSale.Storefront/src/app/router.tsx:92-284`).
- Store configuration is held in a Zustand store after bootstrap and exposes selectors for theme, layout, behavior, storefront status, maintenance message, currency, and Commerce AI options (`PointOfSale.Storefront/src/store/storeConfigStore.ts:30-91`).
- Commerce AI chat widget renders only when the published storefront config enables it (`PointOfSale.Storefront/src/components/chat/ChatWidget.tsx:66-72`, `PointOfSale.Storefront/src/components/chat/ChatWidget.tsx:288-294`).

Assessment: PASS.

## Projection And API Consumption

Implemented conclusions:

- Storefront runtime consumes storefront/public API endpoints, not admin endpoints, for customer-facing runtime data (`PointOfSale.Storefront/src/services/api/endpoints.ts:7-68`).
- A source search found no `/admin` endpoint consumption under `PointOfSale.Storefront/src`.
- POS/Admin source owns admin endpoint consumption for mutable configuration and administration workflows (`PointOfSale.Web/src/modules/storefront/services/storeConfigService.js:26-150`, `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:40-148`, `PointOfSale.Web/src/pages/PaymentMethods.jsx:100-245`).
- Projection mechanics, cache invalidation timing, and persistence model are backend-owned and cannot be certified from frontend source alone.

Assessment: PASS, with backend projection mechanics explicitly out of frontend certification scope.

## State Management

Implemented behavior:

- POS/Admin uses global auth/session state, React Query at app scope, local page state, and dedicated Zustand builder state for storefront draft editing (`PointOfSale.Web/src/App.jsx:171-441`, `PointOfSale.Web/src/modules/storefront/store/builderStore.js:21-260`).
- Builder state tracks dirty/saving/publishing/preview state, selected sections, and undo/redo stacks while keeping edits draft-only until save/publish APIs are called (`PointOfSale.Web/src/modules/storefront/store/builderStore.js:21-248`).
- Storefront uses Zustand stores for store config, checkout, cart, auth, navigation/footer/search/wishlist-style runtime state per audited source.
- Checkout pricing preview cancels in-flight requests, sequences responses, and only admits the latest active pricing response (`PointOfSale.Storefront/src/store/checkoutStore.ts:1376-1466`).
- Checkout autosave/session initialization captures cart identity, aborts stale requests, and ignores stale session responses (`PointOfSale.Storefront/src/store/checkoutStore.ts:264-346`).
- Storefront auth starts as unknown, relies on API confirmation, uses HttpOnly cookie assumptions, and clears checkout/session artifacts on auth transitions (`PointOfSale.Storefront/src/auth/store/authStore.ts:7-12`, `PointOfSale.Storefront/src/auth/store/authStore.ts:84-125`).

Assessment: PASS.

## Configuration Flow

Implemented flow:

POS Admin UI -> admin save/publish APIs -> backend authority/projection -> storefront/public APIs -> customer storefront rendering.

Evidence:

- Store config administration reads `/admin/store-config`, runtime preview reads `/storefront/store-config`, config updates include `If-Match`, page layout save is draft-only, and publish is explicit (`PointOfSale.Web/src/modules/storefront/services/storeConfigService.js:30-75`).
- Theme preset/override/publish and preview/version/rollback calls are admin-service-owned (`PointOfSale.Web/src/modules/storefront/services/storeConfigService.js:85-150`).
- CMS page CRUD, version history, publish/rollback, preview links, navigation update/publish, and footer configuration are admin-service-owned (`PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:40-148`).
- Storefront runtime loads store config through `/api/storefront/store-config` and keeps it in runtime state after bootstrap (`PointOfSale.Storefront/src/services/api/storefrontApi.ts:35-47`, `PointOfSale.Storefront/src/store/storeConfigStore.ts:30-91`).
- Storefront settings only mutate the accepted backend-shaped candidate object and then replaces local state with the accepted backend response (`PointOfSale.Web/src/pages/StorefrontSettings.jsx:109-132`).

Assessment: PASS.

## Checkout Experience

Implemented behavior:

- Storefront checkout loads shipping methods and payment methods from storefront APIs with no-store headers, admits backend responses, and marks stale selections when previously selected values disappear (`PointOfSale.Storefront/src/store/checkoutStore.ts:1181-1344`).
- Manual wallet config, wallet types, max upload size, allowed MIME types/extensions, merchant wallet number, and upload token flow are backend-backed via storefront payment endpoints (`PointOfSale.Storefront/src/store/checkoutStore.ts:1476-1510`, `PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx:56-75`, `PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx:112-132`).
- Checkout requires address, current shipping method, non-stale shipping/payment selections, non-degraded auth state, manual wallet validation when applicable, and current backend pricing preview before submission (`PointOfSale.Storefront/src/store/checkoutStore.ts:1550-1670`).
- Checkout submits cart ID, shipping address, shipping method, payment method, coupon, notes, expected backend pricing, and manual wallet fields to `/api/storefront/checkout` (`PointOfSale.Storefront/src/store/checkoutStore.ts:1710-1741`).
- Payment-session presentation is admitted only after backend checkout response evidence matches the selected provider, cart, order identity, total/currency/status, and provider-specific handoff artifacts (`PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts:34-149`).
- Checkout clears sensitive in-memory/manual wallet/session fields after accepted order handoff (`PointOfSale.Storefront/src/store/checkoutStore.ts:1743-1791`).

Assessment: PASS.

## CMS And Media Presentation

Implemented behavior:

- Storefront CMS pages are fetched through storefront API wrappers and preview admission uses preview token parameters only when requested (`PointOfSale.Storefront/src/services/api/storefrontApi.ts:397-421`).
- Admin CMS preview links are admitted only if backend returns a non-empty slug, non-empty preview token, and future expiry (`PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:23-38`, `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:88-96`).
- CMS rich text sanitization uses DOMPurify allow-lists, strips event handlers, restricts anchor protocols/targets, and enforces `noopener noreferrer` for `_blank` links (`PointOfSale.Storefront/src/utils/htmlSanitizer.ts:3-40`, `PointOfSale.Storefront/src/utils/htmlSanitizer.ts:92-135`).
- Storefront navigation and footer are fetched through storefront endpoints (`PointOfSale.Storefront/src/services/api/storefrontApi.ts:427-438`).
- Product media management and storefront media/gallery presentation were observed as frontend presentation concerns; authoritative storage and media processing remain backend-owned/out of frontend scope.

Assessment: PASS.

## Security Boundaries

Implemented behavior:

- POS/Admin API client uses `/api`, credentials, CSRF header attachment, language header attachment, 401 refresh, safe error messaging, and idempotent retry behavior (`PointOfSale.Web/src/services/api.js:33-41`, `PointOfSale.Web/src/services/api.js:94-130`, `PointOfSale.Web/src/services/api.js:220-335`).
- CommerceHub/vendor authority loss is dispatched on 401/403 responses for CommerceHub vendor authority endpoints (`PointOfSale.Web/src/services/api.js:303-308`).
- Storefront API client uses credentials, bootstrap-readiness enforcement, tenant/session/customer headers, language and affiliate context, CSRF header attachment, and 401 refresh behavior (`PointOfSale.Storefront/src/services/api/httpClient.ts:145-205`, `PointOfSale.Storefront/src/services/api/httpClient.ts:232-331`).
- Storefront auth does not rely on JavaScript-readable tokens and clears sensitive checkout artifacts on auth transitions (`PointOfSale.Storefront/src/auth/store/authStore.ts:7-12`, `PointOfSale.Storefront/src/auth/store/authStore.ts:84-125`).
- Customer-protected route behavior and admin route guards were observed in source; backend authorization remains required and out of frontend certification scope.
- CMS HTML rendering is sanitized before insertion (`PointOfSale.Storefront/src/utils/htmlSanitizer.ts:3-40`, `PointOfSale.Storefront/src/utils/htmlSanitizer.ts:92-135`).

Assessment: PASS.

## Runtime Boundary

Frontend owns by implementation:

- Presentation and rendering for POS/Admin and Storefront.
- Client-side routing and guarded-route UX.
- Local state for UI drafts, forms, loading/error/retry behavior, cart/checkout presentation, search, auth status display, navigation/footer display, and CMS rendering.
- Client-side form validation, stale selection detection, request sequencing, abort handling, and provider handoff evidence checks.
- API request construction with tenant/session/customer/language/CSRF context.

Frontend does not own by implementation:

- Authoritative pricing or totals.
- Authoritative inventory or stock admission.
- Payment execution authority or reusable payment-session authority.
- Storefront projection mechanics, database writes, cache invalidation timing, settlement, refunds execution, payout execution, or accounting.

Assessment: PASS.

## Critical Invariants

Implementation-backed invariants:

- Dedicated storefront runtime does not consume admin endpoints under `PointOfSale.Storefront/src`.
- Storefront normal API calls are rejected until tenant context is ready, except bootstrap store-config (`PointOfSale.Storefront/src/services/api/httpClient.ts:159-171`).
- Storefront request headers include tenant/session and optional customer identity from bootstrap/auth context (`PointOfSale.Storefront/src/services/api/httpClient.ts:174-185`).
- POS Admin storefront-management routes and embedded settings UI require CommerceHub capabilities or POS role guards (`PointOfSale.Web/src/App.jsx:296-378`, `PointOfSale.Web/src/pages/Settings.jsx:694-729`).
- Storefront settings preserves backend-shaped config and uses accepted mutation responses as local authority (`PointOfSale.Web/src/pages/StorefrontSettings.jsx:109-132`).
- Checkout requires backend pricing preview and submits expected pricing instead of locally finalizing totals (`PointOfSale.Storefront/src/store/checkoutStore.ts:1651-1718`).
- Payment provider artifacts are rejected unless backend checkout response proves the selected provider/cart/order handoff (`PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts:34-149`).
- Manual wallet wallet types are backend-configured, not hardcoded as frontend payment authority (`PointOfSale.Storefront/src/store/checkoutStore.ts:1476-1510`, `PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx:56-75`).
- CMS rich text is sanitized through a DOMPurify allow-list before insertion (`PointOfSale.Storefront/src/utils/htmlSanitizer.ts:3-40`, `PointOfSale.Storefront/src/utils/htmlSanitizer.ts:92-135`).

Assessment: PASS.

## Residual Risks And Limits

1. Backend authorization, projection freshness, database constraints, and payment-provider behavior cannot be certified from frontend source and must be covered by backend/integration certifications.
2. POS/Admin payment provider configuration forms contain known-provider field schemas in frontend source (`PointOfSale.Web/src/pages/PaymentMethods.jsx:258-287`). This is acceptable for an admin form UI, but provider schema drift should be covered by tests or backend-driven metadata if providers become dynamic.
3. Manual wallet upload file type/size has safe frontend defaults if backend config omits values (`PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx:69-75`). Backend enforcement remains required.
4. Some direct imports from `PointOfSale.Storefront/src` remain in POS/Admin tests, not runtime code. This is a test-coupling risk, not a customer/admin runtime boundary violation.

## Certification Checklist

| Area | Result | Source-backed basis |
| --- | --- | --- |
| POS Admin Runtime | PASS | Authenticated route tree plus role/capability guards protect storefront, CMS, payment, shipping, order, and analytics admin surfaces. |
| Storefront Runtime | PASS | Customer runtime bootstraps tenant/session/config and consumes storefront/public endpoints only. |
| Projection/API Consumption | PASS | Storefront source has no admin endpoint consumption; backend projection mechanics are out of frontend scope. |
| State Management | PASS | Zustand/local state is UI-scoped; sequencing/abort logic avoids stale client response authority. |
| Configuration Flow | PASS | Admin save/publish APIs feed storefront public API consumption; settings preserve backend-shaped config. |
| Checkout Experience | PASS | Backend methods/pricing/payment evidence are required before order handoff presentation. |
| CMS/Media Presentation | PASS | CMS rendering is sanitized; media presentation remains frontend-only display. |
| Security Boundaries | PASS | Tenant/session/customer/CSRF/language headers, guarded routes, auth refresh, and token non-exposure patterns are implemented. |
| Runtime Boundary | PASS | Frontend does not own pricing, inventory, payment execution, settlement, refunds, payouts, accounting, or projection mechanics. |
| Critical Invariants | PASS | No current source-backed frontend architecture invariant violation found in audited scope. |
| Overall Certification | PASS | Current frontend source satisfies the scoped architecture certification. |

## Final Certification

PASS
