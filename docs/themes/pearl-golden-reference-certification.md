# Pearl Golden Reference Theme Certification

Date: 2026-07-27

Certification Authority: Independent Certification Authority for the Shopixy Theme Platform

Candidate Theme: Pearl

Certification Target: Golden Reference Theme

Final Decision: PASS WITH OBSERVATIONS

## Executive Summary

Pearl is suitable to serve as the canonical implementation pattern for a Shopixy storefront theme, subject to the non-blocking observations recorded in this certification. The source evidence shows that Pearl participates in the platform theme architecture through the same runtime registry, backend catalog, marketplace registry, Vite glob discovery, layout resolver, section resolver, ThemeConfig, CSS variable flow, StoreConfig runtime state, CMS section data normalization, and storefront rendering path used by existing themes.

No blocking production defect was found in Pearl during this certification. The Storefront production build completed successfully, the full Storefront Vitest suite passed, the targeted Pearl Storefront tests passed, and the Web marketplace Pearl test passed.

Certification is not unconditional because repository verification exposed non-Pearl infrastructure gaps and accessibility/test coverage observations: `npm run lint` is currently unusable in `PointOfSale.Storefront` because ESLint 9 cannot find a flat config, one backend architecture artifact test fails on repository documentation state, and Pearl accessibility coverage is source-contract based rather than browser/axe based. These do not block Pearl runtime certification, but they prevent a clean `CERTIFIED GOLDEN REFERENCE THEME` decision.

## Scope

This certification independently audited the following areas from repository evidence:

1. Theme Architecture
2. Theme SDK Compatibility
3. Theme Registration
4. Theme Discovery
5. Theme Resolver
6. Theme Section Contracts
7. Theme Runtime
8. CMS Compatibility
9. Product Experience
10. Editorial Experience
11. Accessibility
12. Performance
13. Maintainability
14. Test Coverage
15. Production Readiness
16. Future Theme Authoring Readiness

The audit reviewed Pearl as implemented in `PointOfSale.Storefront/src/storefront/themes/pearl`, platform theme infrastructure in `PointOfSale.Storefront/src/storefront/themes`, platform section/runtime code in `PointOfSale.Storefront/src/components/sections`, backend theme authority code in `PointOfSale.Core` and `PointOfSale.API`, and admin marketplace registration in `PointOfSale.Web`.

## Audit Methodology

Evidence sources were limited to repository source code, tests, build output, and command results generated during this certification. Prior implementation reports were not used as authority.

Source inspection covered:

- Pearl theme files under `PointOfSale.Storefront/src/storefront/themes/pearl`.
- Storefront runtime registries and resolvers under `PointOfSale.Storefront/src/storefront/themes`.
- Storefront `ThemeConfig`, CSS variable, StoreConfig, SectionRenderer, and homepage section normalization contracts.
- Admin/Web marketplace registration under `PointOfSale.Web/src/modules/storefront/themes`.
- Backend theme authority and validation under `PointOfSale.Core/Constants/StorefrontThemes.cs` and related API services.
- Locale, Tailwind, font-loading, image pipeline, product sourcing, and test files.

Commands executed:

- `npm test -- --run src/storefront/themes/pearlTheme.test.ts src/storefront/themes/pearl/pearlLayout.test.tsx src/storefront/themes/pearl/pearlSections.test.tsx` in `PointOfSale.Storefront`: passed, 3 files, 76 tests.
- `npm test -- --run` in `PointOfSale.Storefront`: passed, 4 files, 79 tests.
- `npm run build` in `PointOfSale.Storefront`: passed, production bundle generated.
- `npm run lint` in `PointOfSale.Storefront`: failed because ESLint 9 could not find `eslint.config.(js|mjs|cjs)`.
- `npm test -- --run src/modules/storefront/themes/themeMarketplaceRegistry.test.ts` in `PointOfSale.Web`: passed, 1 file, 3 tests.
- `dotnet test --filter FullyQualifiedName~StorefrontThemeAuthorityCertificationTests` at repository root: failed 1 of 5 tests; 4 passed and `CertificationArtifacts_MustRecordP2GcTruth` failed because repository documentation artifacts did not contain `PASS P2-GC`.

## Findings

### 1. Theme Architecture

Pearl has a clear theme-local architecture. The theme owns a ThemeConfig, layout components, section implementations, a theme-local product card, and layout tokens:

- `PointOfSale.Storefront/src/storefront/themes/pearl/themeConfig.ts:19-48` defines `pearlThemeConfig` as a `ThemeConfig` with colors, fonts, UI, and layout defaults.
- `PointOfSale.Storefront/src/storefront/themes/pearl/layout/pearlTokens.ts:19-46` centralizes Pearl container, spacing, typography, nav, and accent tokens.
- `PointOfSale.Storefront/src/storefront/themes/pearl/layout/header.tsx`, `footer.tsx`, and `AnnouncementMarquee.tsx` implement theme-local layout.
- `PointOfSale.Storefront/src/storefront/themes/pearl/sections` implements `hero-banner`, `category-grid`, `product-grid`, `banner`, `newsletter-signup`, and `text-block`.
- `PointOfSale.Storefront/src/storefront/themes/pearl/components/PearlProductCard.tsx` keeps product-card presentation local to Pearl.

Architecture finding: Pearl follows the expected theme directory pattern and keeps visual implementation theme-local. Required global integration is limited to registries, locale keys, Tailwind animation/font dependencies, and backend/admin theme catalogs.

### 2. Theme SDK Compatibility

Pearl conforms to the platform `ThemeConfig` shape:

- `PointOfSale.Storefront/src/types/storeConfig.ts:64-93` defines `ThemeConfig`, `ColorsConfig`, `FontConfig`, `UiConfig`, and `LayoutConfig`.
- `pearlThemeConfig` supplies all required fields and only valid layout variants: `productCardVariant: 'compact'` and `headerVariant: 'centered'` in `themeConfig.ts:44-47`.
- `PointOfSale.Storefront/src/theme/cssVariables.ts:33-71` maps `ThemeConfig` into CSS variables.
- `PointOfSale.Storefront/src/theme/applyTheme.ts:14-25` applies those variables to `document.documentElement` and body.

SDK finding: Pearl is compatible with the existing ThemeConfig and CSS variable flow. It does not require new SDK schema to render.

### 3. Theme Registration

Pearl is registered in all required theme authorities found during audit:

- Storefront runtime registry: `PointOfSale.Storefront/src/storefront/themes/ThemeRegistry.ts:17-43` includes `id: 'pearl'`, `name: 'Pearl'`, `version: '1.0.0'`, and standard capabilities.
- Runtime registry validation: `ThemeRegistry.ts:59-82` enforces one default, unique IDs, non-empty manifest fields, and non-empty capabilities.
- Backend catalog: `PointOfSale.Core/Constants/StorefrontThemes.cs:13-63` includes Pearl in `StorefrontThemes.All`.
- Backend catalog validation: `StorefrontThemes.cs:67-104` rejects duplicate IDs, missing default, invalid manifests, and normalizes unsupported IDs to default.
- Web marketplace registry: `PointOfSale.Web/src/modules/storefront/themes/themeMarketplaceRegistry.js:9-47` includes Pearl with description and accent.

Registration finding: Pearl is registered consistently across runtime, backend, and marketplace layers.

### 4. Theme Discovery

Pearl is discoverable by the platform glob contracts:

- Sections are discovered through `import.meta.glob<SectionModule>('./*/sections/*.tsx')` in `ThemeSectionResolver.ts:13`.
- Layout slots are discovered through `import.meta.glob<LayoutComponentModule>('./*/layout/*.tsx')` in `ThemeLayoutResolver.ts:9`.
- Pearl tests mirror the section glob and assert discoverability of `hero-banner`, `product-grid`, and `category-grid` in `pearlTheme.test.ts:8-51`.

Discovery finding: Pearl section and layout files match the naming and directory conventions required by the runtime resolver.

### 5. Theme Resolver

Theme selection is runtime-config driven:

- `PointOfSale.Storefront/src/storefront/themes/ThemeResolver.ts:29-36` resolves development override, StoreConfig `storefrontTheme`, then default theme.
- Unknown theme IDs fall back to the registered default through `getDefaultTheme()`.
- `pearlTheme.test.ts:38-56` verifies Pearl resolves through `ThemeResolver` and unknown IDs fall back unchanged.
- Backend normalization uses `StorefrontThemes.NormalizeOrDefault` in `StorefrontThemes.cs:100-104`.

Resolver finding: Pearl does not introduce a custom resolver path. It is selected by the standard resolver and inherits fallback behavior.

### 6. Theme Section Contracts

The platform section renderer normalizes both homepage sections and Storefront CMS sections:

- `PointOfSale.Storefront/src/components/sections/homepageSections.ts:41-57` maps `data` or `settings` into normalized `HomepageSection.data` and sorts by `order`.
- `PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx:118-134` renders resolved components with `<Component {...section.data} sectionId={section.id} {...sectionProps} />`.

Pearl sections preserve the flat CMS section props used by platform sections:

- `hero-banner.tsx:43-55` accepts title, subtitle, CTA fields, background image, overlay, text color, alignment, and height.
- `category-grid.tsx:100` accepts title, subtitle, source, maxItems, categoryIds, sectionId, and columns.
- `product-grid.tsx:43-53` accepts title, subtitle, layout, source, maxItems, productIds, sectionId, columns, and rows.
- `banner.tsx:23-30` accepts title, subtitle, CTA fields, imageUrl, and backgroundImage.
- `newsletter-signup.tsx:22-28` accepts heading, subheading, buttonText, backgroundColor, and textColor.
- `text-block.tsx:20-29` accepts heading, content, alignment, backgroundColor, textColor, and maxWidth.

Section contract finding: Pearl sections are compatible with the platform section renderer and CMS settings flow.

### 7. Theme Runtime

Runtime composition uses platform providers and resolvers:

- `PointOfSale.Storefront/src/storefront/themes/ThemeContext.tsx:13-35` reads `config.storefrontTheme`, resolves the active theme, and exposes `themeId`.
- `PointOfSale.Storefront/src/storefront/layout/StorefrontLayout.tsx:20-63` resolves theme header/footer asynchronously, falls back to default components, and renders resolved layout slots.
- `SectionRenderer.tsx:35-79` resolves theme section components by `themeId`, starts with default fallback, and uses `SectionErrorBoundary` at render time.

Runtime finding: Pearl is isolated behind standard ThemeProvider, layout resolver, and section resolver paths.

### 8. CMS Compatibility

CMS compatibility is supported by normalized section settings, StoreConfig theme state, and backend validation:

- StoreConfig includes `storefrontTheme?: string` in `PointOfSale.Storefront/src/types/storeConfig.ts:6-12`.
- Backend projection extracts and normalizes `storefrontTheme` from customization JSON in `PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs:588-614`.
- Admin request validation rejects unsupported storefront themes in `PointOfSale.API/DTOs/Admin/AdminStoreConfigDtos.cs:83-85`.
- Admin theme activation rejects unsupported theme IDs in `PointOfSale.API/Services/Admin/AdminThemeActivationService.cs:40-52`.
- Preview token creation/validation rejects unsupported theme IDs in `PointOfSale.API/Services/Storefront/StorefrontThemePreviewTokenService.cs:52-74` and `:94-121`.

CMS finding: Pearl is valid across backend StoreConfig projection, admin validation, theme activation, and preview-token flows.

### 9. Product Experience

Pearl product grid and product card reuse platform product sourcing and product business logic:

- `product-grid.tsx:54-69` uses `normalizeHomepageCollectionLimit`, `normalizeHomepageGuidList`, `resolveHomepageCollectionSource`, and `useHomepageSectionProducts`.
- `PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts:17-19` caches products and in-flight requests.
- `useHomepageSectionProducts.ts:80-130` reuses platform product APIs and selection logic for curated/random/default product shelves.
- `PearlProductCard.tsx:68-148` renders product cards using platform localization, currency, wishlist, badges, affiliate routing, image resolution, and optimized image components.
- `PearlProductCard.tsx:128-145` preserves the variant-aware pricing precedence: `displayPriceText`, then compare-at sale pricing, then current price.
- The same pricing precedence exists in `PointOfSale.Storefront/src/components/products/ProductCardDefault.tsx:87-105`.
- Product images use `resolveProductImage` from `PointOfSale.Storefront/src/utils/resolveProductImage.ts:71-107` and `OptimizedImage` from `PointOfSale.Storefront/src/components/media/OptimizedImage.tsx:60-71`.

Product finding: Pearl preserves platform product sourcing, pricing, localization, wishlist, badge, image, and affiliate navigation behavior while applying theme-local presentation.

### 10. Editorial Experience

Pearl implements a coherent editorial luxury presentation:

- Hero uses dark block background, optional image overlay, Newsreader display heading, gold eyebrow, and red CTA in `hero-banner.tsx:62-120`.
- Category grid uses image-first editorial tiles, deterministic art fallback, responsive columns, centered headings, and hover treatment in `category-grid.tsx:131-188`.
- Promotional banner uses a dark editorial block, optional image, red CTA, and lazy image loading in `banner.tsx:34-82`.
- Newsletter section uses theme defaults, email input, and square red submit button in `newsletter-signup.tsx:41-80`.
- Text block uses platform rich-text sanitizer and editorial typography in `text-block.tsx:29-60`.

Editorial finding: Pearl has enough visual and structural coverage to act as a complete storefront reference theme, not merely a color preset.

### 11. Accessibility

Positive accessibility evidence:

- Header exposes `role="banner"` and labeled navigation in `header.tsx:74-121`.
- Mobile menu button exposes `aria-label` and `aria-expanded` in `header.tsx:104-110`.
- SectionRenderer supports admin/editor selection keyboard activation with Enter and Space in `SectionRenderer.tsx:107-115`.
- Product and category cards include `aria-label`, image `alt`, and focus-visible styles in `PearlProductCard.tsx:84-96` and `category-grid.tsx:74-78`.
- Marquee uses `motion-reduce:animate-none` in `AnnouncementMarquee.tsx:95-98`.
- Tests assert structural accessibility contracts in `pearlLayout.test.tsx:156-167` and `pearlSections.test.tsx:264-269`.

Accessibility observation:

- `AnnouncementMarquee.tsx:87-98` sets `aria-label={t('announcement.label')}` on the region and marks the repeated text track `aria-hidden="true"`. The actual announcement text is therefore not exposed to assistive technologies; only the generic label is exposed. This should be corrected before Pearl is treated as the accessibility gold standard.
- The available tests are source-contract tests, not DOM/axe/browser accessibility tests. The tests explicitly document the node environment limitation in `pearlLayout.test.tsx:4-13` and `pearlSections.test.tsx:4-8`.

Accessibility finding: Pearl has good structural accessibility evidence, but accessibility certification is not complete enough for an unconditional golden declaration.

### 12. Performance

Positive performance evidence:

- Theme layouts and sections are discovered through lazy `import.meta.glob` calls in `ThemeLayoutResolver.ts:9` and `ThemeSectionResolver.ts:13`.
- Production build output shows Pearl emitted as separate chunks, including `pearlTokens`, `AnnouncementMarquee`, `newsletter-signup`, `banner`, `category-grid`, `product-grid`, `hero-banner`, `header`, and `footer` chunks.
- Product images use `OptimizedImage`, lazy loading by default, async decoding, fallback handling, and size hints in `OptimizedImage.tsx:26-71`.
- Product section loading uses cache and in-flight request deduplication in `useHomepageSectionProducts.ts:17-19` and `:80-130`.
- Hero background image uses eager loading intentionally in `hero-banner.tsx:73-80`.

Performance observations:

- The Storefront build reports non-Pearl global warnings: stale Browserslist data, dynamic/static import chunking warnings, and an application chunk larger than 500 kB.
- External Google Fonts are loaded in `PointOfSale.Storefront/index.html:10-17`, including Jost and Newsreader. This supports Pearl typography but depends on network font delivery.

Performance finding: Pearl itself is small and lazily chunked; broader storefront bundle warnings remain outside Pearl but should be tracked for production performance hygiene.

### 13. Maintainability

Positive maintainability evidence:

- Pearl tokens are centralized in `pearlTokens.ts`.
- Pearl component files include explicit reuse decisions and identify platform-owned business logic.
- Theme-local components import platform services rather than duplicating product, cart, wishlist, nav, footer, currency, localization, or sanitizer logic.
- Source-contract tests cover registration, discovery, tokens, layout structure, section contracts, product card behavior, and editorial section contracts.

Maintainability observations:

- The product card remains theme-local because the global product-card dispatcher does not support a Pearl variant. This is documented in `PearlProductCard.tsx:9-12` and future promotion is documented in `PearlProductCard.tsx:40-42`.
- Category tiles use deterministic art because `CategoryTreeDto` has no image field, documented in `category-grid.tsx:17-19` and `:46-50`.
- Some tests assert source substrings rather than rendered DOM behavior. This is practical in the current node test environment but is more brittle than component tests.

Maintainability finding: Pearl is maintainable as a reference theme, with known platform extension gaps isolated and documented.

### 14. Test Coverage

Automated evidence:

- Storefront targeted Pearl tests passed: 3 test files, 76 tests.
- Storefront full Vitest suite passed: 4 test files, 79 tests.
- Web marketplace registry test passed: 1 test file, 3 tests.
- Storefront production build passed.
- Backend theme authority test command passed 4 tests and failed 1 documentation-artifact assertion.

Coverage strengths:

- Runtime registry, resolver, and discovery are tested in `pearlTheme.test.ts`.
- Layout tokens, marquee behavior, header/footer source contracts, responsiveness, and structural accessibility are tested in `pearlLayout.test.tsx`.
- Section contracts, product grid, product card behavior, images, pricing, and editorial sections are tested in `pearlSections.test.tsx`.
- Marketplace registration is tested in `themeMarketplaceRegistry.test.ts`.

Coverage limitations:

- No browser-rendered accessibility test was found.
- No Playwright/Cypress visual or runtime screenshot evidence was found.
- No DOM rendering test for Pearl sections was found because current tests avoid DOM imports due analytics/document coupling.
- `npm run lint` cannot currently run in Storefront.

Test coverage finding: Pearl has strong source-contract and build coverage for this repository's current testing style, but lacks browser-level accessibility and visual regression evidence.

### 15. Production Readiness

Production readiness evidence:

- Storefront build passed with TypeScript and Vite.
- Pearl runtime, layout, section, and marketplace tests passed.
- Backend catalog includes Pearl and validates supported theme IDs.
- Admin activation and preview token services reject unsupported theme IDs.
- StoreConfig projection normalizes Pearl-compatible `storefrontTheme` values.

Production readiness observations:

- Storefront lint is not operational under ESLint 9 due missing flat config.
- Backend architecture certification command is not clean because one documentation artifact assertion fails.
- Storefront build emits global bundle/chunk warnings unrelated to Pearl.
- Marketplace `previewImage` for Pearl is `null` in `themeMarketplaceRegistry.js:43`.

Production readiness finding: Pearl is runtime/build ready, but repository quality gates are not fully clean.

### 16. Future Theme Authoring Readiness

Pearl demonstrates the recommended practices expected of future themes:

- Registration: Runtime, backend, and marketplace registration are present.
- ThemeConfig: Pearl exports a complete `ThemeConfig`.
- Tokens: Pearl has theme-local layout and typography tokens.
- Layout: Header, footer, and announcement marquee are theme-local and resolver-compatible.
- Sections: Six section types follow platform section naming and props conventions.
- Product Cards: Product card presentation is theme-local while business logic is platform-reused.
- Images: Product images use platform image resolution and optimized rendering.
- Typography: Jost and Newsreader are applied through tokens and loaded in `index.html`.
- Accessibility: Landmarks, labels, focus states, reduced motion, and alt text are present, with the marquee text exposure observation noted.
- Runtime Isolation: Pearl is isolated under `themes/pearl` and discovered dynamically.
- Testing Strategy: Registration, discovery, layout, section, and product contracts are tested.
- Theme-local Components: Pearl layout, sections, and product card live inside the theme directory.
- Platform Reuse: Pearl imports platform navigation, footer, product, image, localization, currency, wishlist, badge, sanitizer, and affiliate primitives.
- Business Logic Reuse: Product sourcing, pricing, image resolution, badges, wishlist, CMS settings, footer/nav loading, and rich-text sanitization remain platform-owned.

Future authoring answer: Yes, if a new engineer joins tomorrow, Pearl can serve as the canonical template for creating a brand-new Shopixy theme. It shows the required registration surfaces, file layout, ThemeConfig, resolver compatibility, CMS section contracts, product-card reuse strategy, theme-local presentation boundaries, and tests. There are no blockers to using Pearl as the authoring template, but the observations in this document should be addressed before it is treated as a perfect golden standard.

## Scorecard

| Area | Score | Justification |
| --- | ---: | --- |
| Architecture | 92 | Strong theme-local structure, standard registry/resolver integration, and clear separation of Pearl presentation from platform business logic. Deductions for product-card dispatcher gap and absence of theme-local page examples. |
| Maintainability | 88 | Central tokens, explicit reuse documentation, and source-contract tests support maintenance. Deductions for source-substring test brittleness and verbose component comments that may drift. |
| Reusability | 90 | Pearl reuses platform nav, footer, product sourcing, pricing, image, affiliate, i18n, wishlist, badge, and sanitizer systems. Deductions for local product-card adaptation and category art fallback caused by platform gaps. |
| Accessibility | 82 | Landmarks, labels, focus states, alt text, and reduced-motion handling exist. Deductions for marquee text not exposed to assistive tech and lack of browser/axe test evidence. |
| Performance | 88 | Pearl chunks are lazy and small, product images use optimized loading, and product data uses cache/deduplication. Deductions for global bundle warnings and external font dependency. |
| Theme Isolation | 91 | Pearl implementation is isolated under `themes/pearl` and loaded through standard resolvers. Required global changes are limited to registries/locales/fonts/Tailwind animation. |
| Documentation | 84 | Source comments and the design report provide strong implementation rationale. Deductions for missing dedicated theme-authoring guide before this certification and null marketplace preview image. |
| Future Extensibility | 89 | Pearl is a strong template for future themes and exercises key extension surfaces. Deductions for unformalized product-card variant registration and category image contract gaps. |
| Developer Experience | 86 | Tests are easy to run and theme files follow predictable conventions. Deductions for broken Storefront lint command and non-DOM source-contract testing constraints. |
| Overall Production Readiness | 88 | Build and tests pass for Pearl and Storefront runtime. Deductions for lint failure, one backend documentation artifact test failure, accessibility observation, and global build warnings. |

## Risk Assessment

Blocking risks: None found for Pearl runtime, build, registration, discovery, resolver, CMS compatibility, or product experience.

Non-blocking risks:

- Storefront lint gate is unavailable because ESLint 9 requires a flat config not present in the package.
- Backend architecture command is not clean because `CertificationArtifacts_MustRecordP2GcTruth` fails on documentation artifact content.
- Announcement marquee exposes only a generic region label to assistive technologies while hiding the actual repeated announcement text.
- Pearl accessibility evidence is structural/source based, not browser/axe based.
- Marketplace preview image is null, reducing admin discoverability polish.
- Pearl category tiles use generated art instead of category imagery because the current category DTO does not expose image data.
- Product card is theme-local instead of registered as a platform product-card variant because the dispatcher does not yet expose a theme-scoped variant hook.

Residual risk: Runtime behavior was verified through build and tests, but not through a live browser session, screenshot review, or end-to-end storefront navigation.

## Future Recommendations

1. Add a Storefront ESLint flat config or align ESLint dependency/configuration so `npm run lint` is a valid quality gate.
2. Fix the backend documentation-artifact test failure or update the asserted artifacts so the architecture test command is clean.
3. Expose the actual announcement text to assistive technologies, either through `aria-label` containing the resolved text or visually hidden text inside the region.
4. Add browser-level accessibility coverage for Pearl using axe, Playwright, or equivalent tooling.
5. Add visual regression snapshots for Pearl desktop and mobile homepage states.
6. Add a real Pearl marketplace preview image.
7. Define a formal theme authoring guide derived from Pearl's structure, including required files, registry updates, testing checklist, and CMS section prop conventions.
8. Consider a platform-supported theme product-card variant extension point so future themes do not need local product-card adaptations.
9. Extend category DTOs or CMS section settings with image metadata so category themes can use the platform image pipeline consistently.

## Final Certification

Decision: PASS WITH OBSERVATIONS

Blocking observations: None.

Non-blocking observations:

- Storefront lint command fails due missing ESLint flat config.
- Backend architecture certification command has one documentation-artifact failure unrelated to Pearl runtime.
- Announcement marquee does not expose actual announcement text to assistive technologies.
- Pearl accessibility testing is source-contract based, not browser/axe based.
- Marketplace preview image is null.

Future enhancements:

- Add browser accessibility and visual regression tests.
- Add a formal theme authoring guide.
- Promote Pearl product card into a supported platform theme-card variant if the SDK adds that extension point.
- Add category image support to the platform contract.
- Reduce global Storefront build chunk warnings.

## Certification Statement

Based on direct source inspection, automated test execution, production build output, and repository evidence collected on 2026-07-27, Pearl satisfies the engineering requirements to be used as the Shopixy Theme Platform's canonical reference implementation for future theme authors, subject to the observations listed above.

Pearl is not certified by trust in prior implementation reports. This certification is based only on repository evidence and command results produced during the independent audit.

Final certification: PASS WITH OBSERVATIONS.
