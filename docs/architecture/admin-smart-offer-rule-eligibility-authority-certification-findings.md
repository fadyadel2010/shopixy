# PASS SF-SMO.RULE Admin Smart Offer Rule & Eligibility Authority

Scope reviewed: PointOfSale.Web Smart Offer Management rule and eligibility runtime only, plus backend APIs/DTOs directly consumed by Smart Offer rule admission. Smart Offer Read Authority and Smart Offer Lifecycle Authority were treated as prior architectural truth. Smart Offer financial calculations, recovery internals, Coupons, Promotions, Affiliate, Marketplace, Checkout, Orders, Builder, CMS, and Analytics were not re-certified.

## Pages reviewed

- `PointOfSale.Web/src/pages/SmartOffersPage.jsx`
- `PointOfSale.Web/src/pages/SmartOfferEditor.jsx`
- `PointOfSale.Web/src/pages/SmartOfferPreview.jsx` as editor preview presentation only

## Backend endpoints reviewed

- `POST /api/admin/smart-offers`
- `PUT /api/admin/smart-offers/{id}`
- `GET /api/admin/smart-offers`
- `GET /api/admin/smart-offers/{id}`
- `GET /api/admin/smart-offers/stats`

## Backend services reviewed

- `AdminSmartOffersController`
- `ApplicationDbContext.SmartOffers`
- `SmartOfferConfiguration`
- `SmartOffer.Validate`
- generated-coupon synchronization only where directly coupled to Smart Offer rule admission

## Rule findings

Smart Offer Rule Authority is backend-owned. The inspected accepted Smart Offer rule model is limited to backend-minted identity, tenant scope, status, priority, trigger type, page/display scope, delay seconds, scroll percent, once-per-session display rule, max displays per user, popup content, and generated-coupon candidate settings. Product, category, customer, customer group, store, channel, exclusion, arbitrary rule JSON, minimum quantity, maximum quantity, and explicit target-scope eligibility fields are not accepted Smart Offer persistence fields in `UpsertSmartOfferRequest`, `SmartOffer`, or `AdminSmartOfferDto`.

The frontend owns rule editing, selection, navigation, preview, and mutation DTO candidate submission only. Accepted rules exist only after backend request validation, entity validation, RowVersion/`If-Match` admission for existing updates, SQL persistence, generated-coupon synchronization where present, outbox eventing, and accepted `AdminSmartOfferDto` emission.

## Eligibility findings

Smart Offer Eligibility is backend-owned. The inspected runtime does not persist Smart Offer product, category, customer, customer group, store, channel, or exclusion eligibility. Generated coupons created from Smart Offers are canonicalized by the backend to all-order coupon eligibility except for backend-validated minimum order and max discount constraints; product/category/exclusion coupon eligibility is explicitly cleared during generated-coupon sync.

Injected product IDs, category IDs, customer IDs, store IDs, channel IDs, exclusions, unsupported scopes, and arbitrary rule JSON cannot become accepted Smart Offer eligibility because there is no accepted DTO/entity persistence path for those fields.

## Validation findings

Backend validation begins in `AdminSmartOffersController.ValidateRequest`, `SmartOffer.Validate`, generated-coupon conflict validation, discount validation, tenant-scoped duplicate checks, and mandatory RowVersion/`If-Match` checks for existing-offer updates. Empty or malformed accepted Smart Offer identity is backend-generated; foreign-tenant mutation targets fail current tenant SQL lookup; stale updates fail RowVersion comparison.

The frontend cannot validate accepted rules. It can normalize form candidate values and withhold malformed backend DTO presentation through `adminSmartOfferReadAuthority`, but accepted rule authority begins only at backend admission.

## Recovery findings

Recovery converges to backend SQL truth. Refresh, reconnect, API restart, Redis flush, Mongo rebuild, and projection rebuild re-enter backend Smart Offer SQL reads/mutations and accepted DTO admission. Stale Smart Offer rule DTOs cannot survive recovery because presentation is replaced only by current backend DTOs and existing mutations require current RowVersion evidence.

## Cache findings

React state stores editor candidates and admitted presentation only. React Query if introduced later, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, and Mongo projections are not Smart Offer Rule or Eligibility Authority. They may invalidate, refetch, withhold, or render accepted backend DTOs only.

## Cross-system findings

Smart Offer Rule admission does not consume Promotion, Coupon, Checkout, Pricing, Orders, Affiliate, Marketplace, Builder, CMS, or Analytics authority. Generated-coupon synchronization is a backend consequence of accepted Smart Offer persistence and cannot transfer Coupon Authority to Smart Offers or let Smart Offers overwrite Coupon, Promotion, Checkout, Pricing, Order, Affiliate, Marketplace, Builder, CMS, or Analytics authority.

## Rule pipeline

Rule Candidate begins in `SmartOfferEditor.jsx` form state. Mutation DTO begins when `SmartOffersPage.jsx` submits the candidate through `adminSmartOffersApi`. Validation begins in `AdminSmartOffersController.ValidateRequest` and `SmartOffer.Validate`. Canonicalization begins in backend normalization of names, text, coupon codes, nullable numeric values, generated-coupon defaults, and eligibility clearing. Eligibility validation begins in backend generated-coupon admission and discount/minimum-order checks. SQL persistence begins at backend `SaveChangesAsync` for `SmartOffers` and generated coupons. Accepted DTO emission begins in `MapToAdminDto`. Presentation begins only after `adminSmartOfferReadAuthority` admits the accepted backend DTO.

## Boundary review

- Rule Boundary: owner is backend controller/entity validation; recovery source is SQL `SmartOffers`; failure behavior is 400/409/428 or no presentation.
- Eligibility Boundary: owner is backend generated-coupon canonicalization and SQL persistence; recovery source is SQL Smart Offer/generated coupon truth; failure behavior is reject or all-order generated-coupon eligibility.
- Validation Boundary: owner is backend validation and RowVersion admission; recovery source is current tenant SQL row; failure behavior is 400/404/409/428.
- Recovery Boundary: owner is backend SQL truth; recovery source is current backend reads; failure behavior is refetch or withhold.
- Presentation Boundary: owner is frontend DTO admission over accepted backend DTOs; recovery source is current backend DTO; failure behavior is no render.

## Runtime hardening performed

No runtime behavior change was required. Existing Smart Offer read and lifecycle hardening already provides tenant-scoped SQL reads, accepted DTO admission, mandatory RowVersion/`If-Match` for existing mutations, backend request/entity validation, backend generated-coupon canonicalization, SQL persistence, and refetch convergence.

## Exact runtime files modified

None.

## Regression freezes added

- `PointOfSale.Tests/Architecture/AdminSmartOfferRuleEligibilityAuthorityCertificationTests.cs`

## Architecture documentation updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

