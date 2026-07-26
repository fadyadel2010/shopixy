# PASS SF-SMO.LIFECYCLE Admin Smart Offer Lifecycle Authority

Scope reviewed: PointOfSale.Web Smart Offer Management lifecycle runtime only, plus backend APIs/DTOs directly consumed by Smart Offer lifecycle operations. Smart Offer Read Authority was treated as prior truth; Smart Offer Rule Engine, Financial calculations, Recovery internals, Coupons, Promotions, Affiliate, Marketplace, Checkout, Orders, Builder, CMS, and Analytics were not re-certified.

## Pages reviewed

- `PointOfSale.Web/src/pages/SmartOffersPage.jsx`
- `PointOfSale.Web/src/pages/SmartOfferEditor.jsx`
- `PointOfSale.Web/src/pages/SmartOfferPreview.jsx` as editor preview presentation only

## Backend endpoints reviewed

- `POST /api/admin/smart-offers`
- `PUT /api/admin/smart-offers/{id}`
- `DELETE /api/admin/smart-offers/{id}`
- `GET /api/admin/smart-offers`
- `GET /api/admin/smart-offers/{id}`
- `GET /api/admin/smart-offers/stats`

## Backend services reviewed

- `AdminSmartOffersController`
- `ApplicationDbContext.SmartOffers`
- `SmartOfferConfiguration`
- generated-coupon synchronization only where directly coupled to accepted Smart Offer lifecycle persistence

## Lifecycle findings

Smart Offer Lifecycle is backend-owned. Smart Offer identity, status, activation, deactivation, archive/delete, priority, validity evidence, business validation, generated-coupon lifecycle synchronization, RowVersion, If-Match validation, audit/logging evidence, persistence, and recovery originate from backend trusted tenant/session authority, Owner/Admin/Manager authorization, current tenant SQL `SmartOffers`, backend validation, SQL persistence, outbox event publication, and accepted `AdminSmartOfferDto` emission.

Frontend owns lifecycle intent, editing, selection, navigation, refresh, presentation, and rendering only.

## Concurrency findings

Existing Smart Offer update/delete paths lacked mandatory RowVersion/If-Match admission. Runtime now emits backend-owned RowVersion evidence, requires `If-Match` for existing-offer lifecycle mutations, locks the current tenant SQL row for mutation, rejects missing/malformed/stale RowVersion evidence, and maps EF concurrency failures to conflict responses. Stale administrators, duplicate saves, replay, retry, and forged RowVersion cannot overwrite newer accepted state.

## Recovery findings

Recovery converges to SQL truth. Refresh, reconnect, API restart, Redis flush, Mongo rebuild, and projection rebuild re-enter backend Smart Offer SQL authority and accepted DTO admission. Deleted/archived Smart Offers cannot survive as frontend authority; missing rows fail closed through current backend reads/mutations.

## Cache findings

React state, React Query if introduced later, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, and Mongo projections are not Smart Offer Lifecycle Authority. They may only hold editor candidates, invalidate, refetch, withhold, or render accepted backend DTOs.

## Cross-system findings

Generated coupon synchronization is a backend consequence of accepted Smart Offer lifecycle persistence and does not transfer Coupon Authority to Smart Offers. Promotions, Coupons, Checkout, Pricing, Orders, Affiliate, Marketplace, Builder, CMS, and Analytics cannot overwrite Smart Offer Lifecycle Authority, and Smart Offer Lifecycle cannot overwrite their authority.

## Lifecycle pipeline

Lifecycle begins as administrator intent in `SmartOffersPage.jsx`/`SmartOfferEditor.jsx`. Authority begins in `AdminSmartOffersController` after trusted tenant/session and role admission. Validation begins in backend request/entity validation and generated-coupon admission. Concurrency begins when existing mutations parse mandatory `If-Match` and compare it to current SQL RowVersion loaded with `FOR UPDATE`. SQL persistence begins at backend `SaveChangesAsync`. Presentation begins only after accepted backend `AdminSmartOfferDto` admission in `adminSmartOfferReadAuthority`.

## Boundary review

- Lifecycle Boundary: owner is backend SQL/controller; recovery source is SQL `SmartOffers`; failure behavior is reject/withhold.
- Concurrency Boundary: owner is backend RowVersion and If-Match validation; recovery source is current SQL row; failure behavior is 428/400/409.
- Recovery Boundary: owner is backend SQL truth; recovery source is current SQL reads; failure behavior is refetch or no presentation.
- Presentation Boundary: owner is frontend DTO admission over accepted backend DTOs; recovery source is current backend DTO; failure behavior is no render.

## Runtime hardening performed

- Added backend-owned `SmartOffer.RowVersion` and EF `xmin` concurrency mapping.
- Added RowVersion evidence to `AdminSmartOfferDto`.
- Added tenant-scoped locked lifecycle mutation loading with PostgreSQL `FOR UPDATE`.
- Required mandatory `If-Match` on Smart Offer update and archive/delete.
- Rejected stale/missing/malformed RowVersion before persistence.
- Bound EF original RowVersion and handled `DbUpdateConcurrencyException`.
- Scoped Smart Offer duplicate-name checks to current tenant.
- Added frontend Smart Offer lifecycle mutation wrappers that require accepted RowVersion and send `If-Match`.
- Routed create/update/toggle/archive page actions through admitted API wrappers.
- Required frontend Smart Offer DTO admission to include valid RowVersion evidence.

## Exact runtime files modified

- `PointOfSale.API/Controllers/AdminSmartOffersController.cs`
- `PointOfSale.API/DTOs/SmartOffers/SmartOfferDtos.cs`
- `PointOfSale.Core/Entities/SmartOffer.cs`
- `PointOfSale.Infrastructure/Data/Configurations/SmartOfferConfiguration.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Web/src/auth/adminSmartOfferReadAuthority.js`
- `PointOfSale.Web/src/services/adminSmartOffersApi.js`
- `PointOfSale.Web/src/pages/SmartOffersPage.jsx`

## Regression freezes added

- `PointOfSale.Tests/Architecture/AdminSmartOfferLifecycleAuthorityCertificationTests.cs`

## Architecture documentation updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
