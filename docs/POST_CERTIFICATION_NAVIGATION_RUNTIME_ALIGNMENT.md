# Post-Certification Navigation Runtime Alignment

**Repository:** PointOfSale  
**Date:** 2026-08-03  
**Scope:** Source-backed runtime investigation and narrow recovery for Storefront Navigation rendering  
**Certification:** **PASS**

## Executive Summary

The public Storefront navigation runtime has one source owner: published `StoreConfig.CustomizationJson.navigation.live`, exposed by `StorefrontNavigationController` and consumed through the Storefront `navigationStore`.

Header Navigation was already correctly runtime-owned. The header appeared to show only utility actions because the public navigation API reads only `live`; the observed admin snapshot had authored links in `draft` while `live.header` and `live.footer` were empty. Source proves that draft save does not publish. Only `AdminCmsService.PublishNavigationAsync` promotes draft to live.

Footer Navigation had a real runtime wiring bug. The public API exposed `navigation.live.footer`, but `FooterRuntime` and installed Theme footers consumed only the separate Footer Editor sections from `/api/storefront/footer`. The recovery keeps Footer Navigation separate from Footer Editor sections and adds a distinct `model.navigation.links` path to the footer presentation model.

## Verified Root Cause

1. Draft contains navigation because `NavigationEditor.handleSave` calls `cmsService.updateNavigation`, and `AdminCmsService.UpdateNavigationAsync` writes the admitted request to `navigation["draft"]` (`PointOfSale.Web/src/modules/storefront/cms/pages/NavigationEditor.jsx:209`, `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:126-134`, `PointOfSale.API/Services/Admin/AdminCmsService.cs:338-391`).
2. Live is empty after save because `UpdateNavigationAsync` preserves or initializes `navigation["live"]` as empty and does not copy draft to live (`PointOfSale.API/Services/Admin/AdminCmsService.cs:382-389`).
3. Draft to live promotion belongs to `AdminCmsService.PublishNavigationAsync`; it admits the draft in publish mode and assigns the admitted draft to `navigation["live"]` (`PointOfSale.API/Services/Admin/AdminCmsService.cs:413-477`).
4. Public Storefront navigation consumes `live`, not `draft`; `StorefrontNavigationController.ExtractLiveNavigation` requires `navigation.live` and returns empty navigation if it is absent (`PointOfSale.API/Controllers/StorefrontNavigationController.cs:67-116`).
5. Header rendering was not hidden by theme layout. `StorefrontLayout` resolves a header component and renders `<HeaderRuntime component={HeaderComponent} />` when present (`PointOfSale.Storefront/src/storefront/layout/StorefrontLayout.tsx:31-67`). `HeaderRuntime` maps `selectHeaderLinks` into a presentation model and `NavigationSlot` renders `model.navigation.links` (`PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx:50-68`, `:112-163`).
6. Footer Navigation was separate from Footer Editor sections. Footer Editor sections are read from SQL footer tables by `/api/storefront/footer` through `StorefrontFooterReadService` (`PointOfSale.API/Services/Storefront/StorefrontFooterReadService.cs:34-87`, `:105-127`), while Navigation footer links are read from `StoreConfig.navigation.live.footer` by `/api/storefront/navigation` (`PointOfSale.API/Controllers/StorefrontNavigationController.cs:94-155`).
7. Before this recovery, `FooterRuntime` loaded only `useFooterStore` and passed only `sections`/`socialLinks` to themes. The public navigation footer menu had no React runtime consumer. The recovery adds `selectFooterLinks` to `FooterRuntime`, maps those links with `resolveNavLinkUrl`, and passes them as `model.navigation.links` (`PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx:179-203`).

## Complete Runtime Call Graph

```text
Navigation Editor
  -> cmsService.updateNavigation
  -> PUT /admin/cms/navigation
  -> AdminCmsPagesController.UpdateNavigation
  -> AdminCmsService.UpdateNavigationAsync
  -> NavigationAdmissionService.AdmitAsync(Draft)
  -> NavigationStorageAdapter.ToStorage
  -> StoreConfig.CustomizationJson.navigation.draft

Navigation Publish
  -> NavigationEditor.handlePublish
  -> cmsService.publishNavigation(savedNavigation.rowVersion)
  -> POST /admin/cms/navigation/publish
  -> AdminCmsPagesController.PublishNavigation
  -> AdminCmsService.PublishNavigationAsync
  -> NavigationStorageAdapter.FromStorage(draft)
  -> NavigationAdmissionService.AdmitAsync(Publish)
  -> StoreConfig.CustomizationJson.navigation.live = admitted draft
  -> NavigationPublishedEvent

Public Header/Footer Navigation
  -> GET /api/storefront/navigation
  -> StorefrontNavigationController.GetNavigation
  -> ExtractLiveNavigation(StoreConfig.CustomizationJson.navigation.live)
  -> RecomposeNavigationFromSqlAuthorityAsync
  -> NavigationDto.header/footer
  -> Storefront fetchNavigation
  -> navigationStore.loadNavigation
  -> HeaderRuntime / FooterRuntime presentation models
  -> Theme header NavigationSlot / Theme footer model.navigation.links

Footer Editor Sections
  -> GET /api/storefront/footer
  -> StorefrontFooterController.GetFooter
  -> StorefrontFooterReadService.GetFooterAsync
  -> StorefrontFooterSections / StorefrontSocialLinks SQL
  -> footerStore.loadFooter
  -> FooterRuntime model.sections/model.socialLinks
  -> Theme footer section/social rendering
```

## Source-Backed Ownership Graph

| Surface | Runtime owner | Source proof | Non-owner boundary |
|---|---|---|---|
| Header Navigation persistence | `StoreConfig.CustomizationJson.navigation.live.header` | `AdminCmsService.PublishNavigationAsync` writes `navigation["live"]` at `PointOfSale.API/Services/Admin/AdminCmsService.cs:469` | Draft is authoring state only |
| Header Navigation public reader | `StorefrontNavigationController` | `ExtractLiveNavigation` requires `navigation.live` at `PointOfSale.API/Controllers/StorefrontNavigationController.cs:94-116` | Mongo projection, theme defaults, hardcoded links, and Homepage Builder are not read |
| Header Navigation renderer | `HeaderRuntime.NavigationSlot` | `model.navigation.links.map` at `PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx:68` | Themes place the slot only |
| Footer Navigation persistence | `StoreConfig.CustomizationJson.navigation.live.footer` | Same publish owner writes the complete admitted draft to live at `AdminCmsService.cs:469` | Footer Editor SQL sections are separate |
| Footer Navigation public reader | `StorefrontNavigationController` | `FilterNavigationMenu(navigation.Footer, ...)` at `PointOfSale.API/Controllers/StorefrontNavigationController.cs:185` | `/api/storefront/footer` does not own Navigation footer links |
| Footer Navigation renderer | `FooterRuntime` plus Theme footers | `selectFooterLinks` and `footerNavigationLinks.map` at `LayoutRuntime.tsx:179-203`; theme footers render `navigation.links.map` | No theme loads navigation or substitutes section fallback for navigation |
| Footer Editor Sections renderer | `FooterRuntime` from `footerStore` | `loadFooter`/`sections.map` at `LayoutRuntime.tsx:173-232` | Not the Navigation footer menu |

## Search Proof

Header navigation renderer:

```text
PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx:50  const NavigationSlot
PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx:68  model.navigation.links.map
PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx:112 export const HeaderRuntime
PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx:118 selectHeaderLinks
```

Footer navigation renderer:

```text
PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx:179 const footerNavigationLinks = useNavigationStore(selectFooterLinks)
PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx:195 links: footerNavigationLinks.map
PointOfSale.Storefront/src/components/layout/Footer.tsx:44 navigation.links.map
PointOfSale.Storefront/src/storefront/themes/*/layout/footer.tsx navigation.links.map in every active installed footer
```

Navigation publish owner:

```text
PointOfSale.API/Services/Admin/AdminCmsService.cs:413 PublishNavigationAsync
PointOfSale.API/Services/Admin/AdminCmsService.cs:455 NavigationAdmissionMode.Publish
PointOfSale.API/Services/Admin/AdminCmsService.cs:469 navigation["live"] = admitted draft
```

Navigation live reader:

```text
PointOfSale.API/Controllers/StorefrontNavigationController.cs:67 ExtractLiveNavigation(...)
PointOfSale.API/Controllers/StorefrontNavigationController.cs:104 navigation.TryGetProperty("live", out var live)
PointOfSale.API/Controllers/StorefrontNavigationController.cs:184-185 FilterNavigationMenu(header/footer)
```

Theme consumer:

```text
PointOfSale.Storefront/src/storefront/layout/StorefrontLayout.tsx:65 HeaderRuntime
PointOfSale.Storefront/src/storefront/layout/StorefrontLayout.tsx:67 FooterRuntime
PointOfSale.Storefront/src/storefront/themes/themeRuntimeArchitecture.test.ts:53 installed themes cannot import navigation/footer stores
PointOfSale.Storefront/src/storefront/themes/themeRuntimeArchitecture.test.ts:113 active footers must consume navigation.links
```

## Files Changed

Production:

- `PointOfSale.Storefront/src/storefront/presentation/models.ts`
- `PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx`
- `PointOfSale.Storefront/src/components/layout/Footer.tsx`
- `PointOfSale.Storefront/src/storefront/themes/alizon/layout/footer.tsx`
- `PointOfSale.Storefront/src/storefront/themes/cosmetics/layout/footer.tsx`
- `PointOfSale.Storefront/src/storefront/themes/fashion/layout/footer.tsx`
- `PointOfSale.Storefront/src/storefront/themes/hype/layout/footer.tsx`
- `PointOfSale.Storefront/src/storefront/themes/minimal/layout/footer.tsx`
- `PointOfSale.Storefront/src/storefront/themes/pearl/layout/footer.tsx`
- `PointOfSale.Storefront/src/storefront/themes/phoenix/layout/footer.tsx`
- `PointOfSale.Storefront/src/storefront/themes/seven/layout/footer.tsx`
- `PointOfSale.Storefront/src/storefront/themes/sharp/layout/footer.tsx`

Tests and certification:

- `PointOfSale.Tests/Admin/NavigationAdmissionServiceTests.cs`
- `PointOfSale.Tests/Cms/StorefrontNavigationRuntimeTests.cs`
- `PointOfSale.Storefront/src/storefront/runtime/navigationRuntimeAlignment.test.tsx`
- `PointOfSale.Storefront/src/storefront/themes/themeRuntimeArchitecture.test.ts`
- `POST_CERTIFICATION_NAVIGATION_RUNTIME_ALIGNMENT.md`

## Regression Tests

Added or extended focused coverage proving:

- Draft to live publish copies admitted header and footer links.
- Public Storefront navigation returns published live header links.
- Public Storefront navigation returns published live footer links.
- Header custom links and category links resolve through the runtime navigation path.
- CMS footer links resolve through the runtime navigation path.
- Footer navigation remains distinct from Footer Editor sections.
- Theme footers consume runtime `model.navigation.links`.
- No Theme fallback replaces runtime navigation with footer sections.

## Validation

| Gate | Result |
|---|---|
| Focused backend Navigation/runtime tests: `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~NavigationAdmissionServiceTests\|FullyQualifiedName~StorefrontNavigationRuntimeTests" --no-restore` | **PASS: 20/20** |
| Focused Web Navigation authoring test: `npm test -- --run src/modules/storefront/cms/services/navigationAuthoringContract.test.ts --reporter=dot` | **PASS: 10/10** |
| Focused Storefront Theme/runtime tests: `npm test -- --run src/storefront/runtime/navigationRuntimeAlignment.test.tsx src/storefront/themes/themeRuntimeArchitecture.test.ts --reporter=dot` | **PASS: 8/8** |
| Focused Storefront Runtime tests: `npm test -- --run src/storefront/runtime/navigationRuntimeAlignment.test.tsx src/storefront/runtime/runtimeOwnershipRecovery.test.ts --reporter=dot` | **PASS: 6/6** |
| Full Storefront tests: `npm test -- --run --reporter=dot` | **PASS: 26 files, 236 tests** |
| Storefront build: `npm run build` | **PASS: 513 modules transformed** |
| Web build: `npm run build` | **PASS: 5068 modules transformed** |
| .NET build: `dotnet build PointOfSale.sln --no-restore --verbosity:minimal` | **PASS: 0 warnings, 0 errors** |
| Search proof | **PASS** |
| `git diff --check` | **PASS; repository LF-to-CRLF warnings only** |

Existing non-blocking frontend warnings remain: Browserslist data age, Vite mixed static/dynamic import notices, and large chunk warnings.

## Certification

**PASS**

The runtime owner of Header Navigation and Footer Navigation is now source-backed and aligned:

- Save writes draft only.
- Publish promotes admitted draft to live.
- Public Storefront navigation reads live only.
- Header Navigation is rendered by `HeaderRuntime.NavigationSlot`.
- Footer Navigation is rendered from the same runtime navigation store through `FooterRuntime` and Theme footer presentation components.
- Footer Navigation remains separate from Footer Editor sections.
- Themes remain presentation-only and do not load navigation/footer data.
