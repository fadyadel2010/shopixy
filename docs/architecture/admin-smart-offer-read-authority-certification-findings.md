# PASS SF-SMO.READ Admin Smart Offer Read Authority

Scope reviewed: PointOfSale.Web Smart Offer Management read runtime only, plus backend APIs/DTOs directly consumed by that read surface. Smart Offer Lifecycle, Rule Engine, Financial calculations, Recovery internals, Coupons, Promotions, Affiliate, Marketplace, Checkout, Orders, Builder, CMS, and Analytics were not re-certified.

## Pages reviewed

- `PointOfSale.Web/src/pages/SmartOffersPage.jsx`
- `PointOfSale.Web/src/pages/SmartOfferPreview.jsx` as read-only preview rendering evidence only

## Backend endpoints reviewed

- `GET /api/admin/smart-offers`
- `GET /api/admin/smart-offers/{id}`
- `GET /api/admin/smart-offers/stats`

## Backend services reviewed

- `AdminSmartOffersController` SQL read paths
- `ApplicationDbContext.SmartOffers`
- `ApplicationDbContext.Tenants`
- generated-coupon read evidence only for usage and eligibility summary fields

## Read findings

Smart Offer Read is backend-owned. Smart Offer identity, tenant scope, name, status, priority, trigger/offer type, page/offer scope, generated-coupon discount metadata, currency, usage counts, usage limits, eligibility summary, created date, updated date, and persistence originate from current tenant-scoped SQL reads and accepted backend DTO emission.

The frontend owns search/filter/sort/page intent, selection, refresh, navigation, presentation, and rendering only.

## DTO findings

`AdminSmartOfferDto` now carries tenant, currency, usage, eligibility, and validity evidence. `adminSmartOfferReadAuthority` rejects malformed, foreign-tenant, duplicate, route-mismatched, unsupported enum, incomplete discount, missing currency, missing eligibility, and invalid date/number evidence before presentation.

## Cache findings

Admin Smart Offer reads use no-store backend response semantics and frontend no-store request headers. React state, React Query if introduced later, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, and Mongo projections are not Smart Offer Authority and may only invalidate, refetch, withhold, or render accepted DTOs.

## Recovery findings

Refresh, reconnect, restart, Redis flush, Mongo rebuild, and projection rebuild converge by re-entering backend SQL read authority. Stale Smart Offer DTOs cannot survive as authority because list/detail/stats presentation is replaced only after current backend DTO admission.

## Cross-system findings

Smart Offer Read does not consume Promotion, Coupon, Checkout, Pricing, Orders, Affiliate, Marketplace, Builder, CMS, or Analytics authority. Generated-coupon fields are read evidence for Smart Offer presentation only and do not overwrite Coupon Authority. Smart Offer Read cannot overwrite another domain.

## Read pipeline

SQL `SmartOffers` begins Smart Offer persistence and identity. Authority begins when `AdminSmartOffersController` resolves trusted tenant context and reads current SQL rows. DTO admission begins in `adminSmartOfferReadAuthority`. SQL persistence begins only in backend write transactions outside this read pass. Presentation begins in `SmartOffersPage.jsx` only after admitted backend DTOs are stored in React state.

## Boundary review

- Identity Boundary: backend SQL `SmartOffers.Id` and `TenantId`; recovery source is SQL; failure behavior is no presentation.
- Status Boundary: backend SQL `SmartOffers.Status`; recovery source is SQL; failure behavior is rejected DTO.
- Eligibility Boundary: backend SQL Smart Offer plus generated-coupon read evidence; recovery source is SQL; failure behavior is rejected DTO or unavailable summary.
- Financial Boundary: backend DTO discount metadata and tenant currency evidence only; frontend never calculates accepted discount authority; failure behavior is rejected DTO.
- Recovery Boundary: backend SQL reread; Redis/Mongo/browser caches are derivative; failure behavior is refetch or withhold.
- Presentation Boundary: `adminSmartOfferReadAuthority`; recovery source is current backend DTO; failure behavior is no render.

## Runtime hardening performed

- Added tenant-scoped `AsNoTracking` backend SQL list/detail/stats reads.
- Added no-store response semantics to admin Smart Offer reads.
- Added backend-executed search, status filter, sort, and pagination intent.
- Added read-only detail endpoint and frontend detail reread.
- Added tenant, currency, usage, eligibility, and validity DTO evidence.
- Added frontend no-store Smart Offer read API wrapper.
- Added frontend DTO/list/stats admission before presentation.
- Moved list search/filter/sort from client-owned result shaping to backend-owned read execution.

## Exact runtime files modified

- `PointOfSale.API/Controllers/AdminSmartOffersController.cs`
- `PointOfSale.API/DTOs/SmartOffers/SmartOfferDtos.cs`
- `PointOfSale.Web/src/pages/SmartOffersPage.jsx`
- `PointOfSale.Web/src/auth/adminSmartOfferReadAuthority.js`
- `PointOfSale.Web/src/services/adminSmartOffersApi.js`

## Regression freezes added

- `PointOfSale.Tests/Architecture/AdminSmartOfferReadAuthorityCertificationTests.cs`

## Architecture documentation updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
