# PASS F-S1-B Service Marketplace Frontend Listing Card Authority

Service Marketplace Frontend Listing Card Authority is production-certifiable for the inspected runtime after card presentation validation, zero-price correction, media-key hardening, and derivative summary rendering.

## Service Identity Findings

- Current backend search responses own card service identity, title, description, and category presentation.
- A card fails closed when service identity, provider-profile identity, title, or provider display identity is absent.
- Hidden, deleted, inactive, or stale search candidates remain suppressed by current SQL search recomposition and frontend refresh validation.

## Provider Findings

- Provider display name and provider-profile identity come only from the backend-composed search DTO.
- The card derives only a visual initial from the current display name. It stores no provider authority and cannot preserve provider identity across actor transitions.

## Pricing Findings

- Starting price, pricing type, and currency remain backend listing authority.
- The card now preserves valid zero pricing instead of misreporting it as contact-only pricing.
- Negative, non-finite, incomplete, or malformed pricing summaries are suppressed rather than repaired or displayed.

## Rating Findings

- Average rating and review count remain current backend reputation summaries.
- The card displays them only when rating is finite and within 0-5 and review count is a non-negative integer.
- Invalid or replayed rating-shaped DTO values do not become visual authority.

## Trust Findings

- Trust badges remain backend reputation summaries.
- The frontend uses an allowlist only to decide whether a known backend badge can be presented. It cannot calculate or promote a badge.
- Trust is suppressed when the associated rating summary is invalid.

## Analytics Findings

- Completed-request presentation remains a derivative backend analytics summary.
- Total requests, completed requests, and completion rate must form a valid bounded set before completed-request statistics render.
- The card cannot calculate completion authority, mutate statistics, or override backend lifecycle truth.

## Media Findings

- Service media remains backend listing media authority.
- The card accepts only JSON-array storage keys under `marketplace/services/`, rejects traversal/query/fragment references, and builds a root-relative preview path.
- Invalid or failed media previews disappear and fall back to non-authoritative presentation.

## Cache Findings

- Listing cards remain inside tenant/user-scoped React Query keys.
- Cached cards are withheld during current-authority validation and after initial or background refresh failure.
- Actor or tenant transitions remove prior marketplace cache scopes before replacement-route presentation.

## Navigation Findings

- Card navigation carries only the encoded backend service ID and no DTO or history state.
- The detail route re-enters current backend service visibility authority; a card cannot make a hidden or removed service navigable as valid detail truth.

## Realtime Findings

- Listing cards accept no realtime payload as authority.
- Periodic/focus revalidation and mutation invalidation trigger backend recomposition only; no listing-card path uses `setQueryData` to publish event values.

## Tenant Isolation Findings

- Card cache identity includes authenticated tenant and user scope.
- Browser history, route state, local storage, media references, and card DTOs do not supply tenant authority.
- Public discovery remains intentionally global while every returned card is backend-revalidated against current SQL listing, tenant, and provider lineage.

## Failure-Handling Findings

- Search loading, refresh, 401, 403, 404, and 500-style failures cannot preserve card presentation as current truth.
- Invalid identity removes the card; malformed optional summaries are suppressed; media failures fall back without retaining a stale preview.

## Adversarial Findings

- DTO replay, cache replay, rating inflation, trust fabrication, analytics inflation, thumbnail injection, actor switching, tenant switching, history replay, projection lag, and realtime replay were reviewed.
- None can create listing-card authority on inspected paths. Cards remain bounded presentation over the current backend snapshot.

## Runtime Hardening Performed

- Added a listing-card presentation adapter that validates required identity and independently bounds pricing, rating, trust, analytics, category, and media display.
- Corrected zero-price rendering.
- Added backend-derived category and completed-request summaries.
- Hardened media preview paths and failure fallback.
- Kept navigation ID-only so detail visibility is revalidated.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/marketplace/listingCardAuthority.ts`
- `PointOfSale.Web/src/marketplace/components/ServiceCard.tsx`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`

## Regression Freezes Added

- `PointOfSale.Web/src/marketplace/listingCardAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendListingCardAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Answers

- Who owns listing-card authority? The frontend owns bounded derivative presentation; current backend listing, provider, reputation, and analytics authorities own every displayed business fact.
- Can cards become authority? No.
- Can provider identity drift? Not on inspected refresh and actor-transition paths.
- Can stale ratings survive refresh? No; cards are withheld during refresh and after refresh failure.
- Can trust be fabricated? No; the card presents only a recognized backend badge associated with a valid backend rating summary.
- Can analytics override backend truth? No.
- Can stale cards survive actor transitions? No; marketplace cache scopes are removed before replacement-route presentation.
- Can tenant leakage occur? No inspected card, cache, route, media, or history path crosses tenant/user scopes.
- Were runtime code changes required? Yes.
- Is Service Marketplace Frontend Listing Card Authority production-certifiable? Yes, for the inspected runtime after this hardening.

## Residual Operational Risk

- Media storage still depends on deployment serving validated `marketplace/services/` keys from the expected root-relative path. Broken delivery fails closed to the card fallback.
- Production should monitor search refresh failures, candidate-to-card suppression, malformed DTO telemetry, and media delivery errors.
