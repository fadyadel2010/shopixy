# PASS F-S1-C Service Marketplace Frontend Service Detail Presentation Authority

Service Marketplace Frontend Service Detail Presentation Authority is production-certifiable for the inspected runtime after detail DTO validation, refresh withholding, media hardening, review validation, provider composition binding, route-state reset, and campaign-evidence currency hardening.

## Service Identity Findings

- Current backend detail responses own service identity, title, description, category, and active visibility.
- Required service/provider identity and active status fail closed in `deriveServiceDetailPresentation`; detail DTOs cannot publish an inactive or identity-incomplete service.
- Description and category are formatted only. No route state, card DTO, local state, or browser history snapshot supplies detail identity.

## Provider Findings

- Provider identity derives from the visibility-qualified backend service/profile join.
- The backend detail DTO now reuses `service.Profile` from the certified listing read instead of issuing a broader second profile lookup.
- Profile bio, location, skills, image URL, and portfolio links are optional presentation. Invalid JSON or unsafe URL schemes are suppressed.
- Actor/profile refresh controls buyer actions only; it cannot replace the provider identity in the service DTO.

## Pricing Findings

- Starting price, pricing type, and currency remain backend listing authority.
- Valid zero pricing is preserved. Negative, non-finite, incomplete, or malformed price/currency/type combinations are suppressed rather than repaired.
- Package JSON is not currently rendered on the service-detail page and therefore cannot override listing pricing authority.

## Media Findings

- Service media is accepted only from backend-owned `marketplace/services/` storage keys.
- Traversal, query, fragment, absolute URL, malformed JSON, and non-array media values are suppressed.
- Media load failure hides the failed image and creates no fallback business truth.

## Rating Findings

- Detail trust/rating summaries derive from backend SQL-composed reputation authority.
- Individual reviews are accepted only with valid identity, buyer name, date, and integer rating from 1 through 5.
- Cached reviews are withheld during refresh and after refresh failure.

## Trust Findings

- Trust average, count, badge, and distribution must be internally bounded before display.
- The frontend recognizes backend badge values but cannot calculate, promote, or fabricate a badge.
- Invalid distributions, counts, averages, or badges suppress the trust summary.

## Analytics Findings

- The detail page has no completed-request, popularity, or activity analytics surface.
- POS-verified campaign order count is optional derivative evidence and is bounded before display.
- Campaign revenue is no longer rendered with service currency because the review DTO does not carry authoritative revenue currency. Analytics cannot invent or relabel financial truth.

## Visibility Findings

- Backend `GetServiceByIdAsync` requires active, non-deleted service, active tenant, and same-tenant active non-deleted provider profile authority.
- The frontend requires `isActive === true`, withholds cached detail during revalidation, and fails closed on detail authority failure.
- A 401, 403, or 404 from the review visibility path also removes the entire detail presentation.

## Cache Findings

- Detail and review query keys include authenticated tenant/user scope and service ID.
- Cached detail is not displayed while current authority is fetching or after refresh failure.
- Cached reviews are not displayed while refreshing or after failure.
- Actor/tenant transitions remove foreign marketplace scopes before replacement-route presentation.

## Navigation Findings

- Cards navigate with service ID only. Deep links, refresh, bookmarks, back, and forward navigation re-enter backend detail visibility authority.
- Request form text and open state reset when service ID or actor scope changes, preventing route-local intent replay onto another service.

## Realtime Findings

- Service details accept no realtime payload as service, provider, pricing, rating, trust, media, or analytics truth.
- Marketplace events and the active-route authority boundary may invalidate/refetch only.
- Periodic, focus, reconnect, and visibility revalidation converge detail presentation through backend authority.

## Tenant Isolation Findings

- Detail, review, and profile caches are scoped by authenticated tenant and user.
- Tenant values are not accepted from route state, query parameters, DTOs, local storage, or media references.
- Actor/tenant transitions purge the prior scope and remount the marketplace boundary.

## Failure-Handling Findings

- Initial and background detail loading withholds prior detail content.
- Detail refresh failures remove the service presentation.
- Review refresh failures remove cached reviews; authority-shaped review 401/403/404 failures remove the whole detail page.
- Invalid optional profile, pricing, trust, evidence, and media fields are suppressed without becoming fallback authority.

## Adversarial Findings

- DTO replay, cache replay, provider replay, pricing replay, rating replay, trust replay, analytics replay, media replay, visibility races, actor switching, tenant switching, history replay, deep-link replay, and realtime replay were reviewed.
- Required identity and visibility fail closed; optional malformed summaries are suppressed; no optimistic detail write exists.
- Campaign evidence cannot fabricate financial currency, and service-detail presentation cannot create listing, provider, rating, trust, analytics, request, or lifecycle authority.

## Runtime Hardening Performed

- Added a pure detail/review presentation adapter with bounded identity, pricing, media, provider, rating, trust, and evidence validation.
- Withheld cached detail and reviews during current-authority refresh and after refresh failure.
- Reset request form state on service and actor transitions.
- Reused the visibility-qualified provider profile in backend detail DTO composition.
- Removed campaign revenue rendering until authoritative revenue currency is present.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/api/marketplaceApi.ts`
- `PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx`
- `PointOfSale.Web/src/marketplace/serviceDetailAuthority.ts`
- `PointOfSale.Web/src/marketplace/types/index.ts`

## Regression Freezes Added

- `PointOfSale.Web/src/marketplace/serviceDetailAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendServiceDetailAuthorityCertificationTests.cs`

## Architecture Updates

- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers

- Who owns service-detail authority? Current backend SQL listing, tenant, provider-profile, review/reputation, and analytics authorities own business truth; the frontend owns bounded presentation only.
- Can detail pages become authority? No.
- Can provider identity drift? Not through inspected frontend cache, route, or composition paths after binding the DTO to the visibility-qualified provider profile.
- Can stale pricing survive refresh? No; detail is withheld during refresh and malformed or failed pricing is suppressed.
- Can trust be fabricated? No; trust must be a bounded backend summary and the frontend cannot calculate or promote it.
- Can analytics override backend truth? No; no detail analytics authority exists, and optional evidence is derivative only.
- Can hidden services remain visible? No on inspected validation paths.
- Can deep links bypass visibility authority? No.
- Can stale detail pages survive actor transitions? No.
- Can tenant leakage occur? No inspected detail cache, route, media, review, or profile path crosses tenant/user scopes.
- Were runtime code changes required? Yes.
- Is Service Marketplace Frontend Service Detail Presentation Authority production-certifiable? Yes, for the inspected runtime after this hardening.

## Residual Operational Risk

- Service packages remain opaque JSON and are not rendered. A future package UI needs a typed backend contract, bounded package adapter, authoritative package currency, and renewed certification.
- Campaign revenue remains intentionally hidden until the DTO supplies authoritative currency.
- Production browser automation should continue visibility-change, actor-switch, tenant-switch, offline/reconnect, 401/403/404, browser-history, and media-delivery exercises.
