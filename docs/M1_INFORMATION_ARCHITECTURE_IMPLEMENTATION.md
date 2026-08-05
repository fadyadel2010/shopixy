# M1 Information Architecture Implementation

## Certification

**PASS**

Wave 1 implements the constitutional Storefront Administration information architecture without changing Storefront Runtime, Projection, Theme Runtime, Builder Runtime, cache behavior, backend behavior, API contracts, DTOs, persistence, authorization policy, publication, preview, rollback, versioning, concurrency, or business rules.

The certified recovery documents and their runtime/ownership boundaries were treated as frozen. No recovery document was edited, and no file under `PointOfSale.API`, `PointOfSale.Core`, `PointOfSale.Infrastructure`, or `PointOfSale.Storefront` was changed for M1.

## Blueprint compliance

The tenant Storefront administration hierarchy is now defined once and rendered in this exact order:

| Order | Canonical node | Icon | Children |
|---:|---|---|---|
| 1 | Overview | `LayoutDashboard` | — |
| 2 | Storefront Status | `Activity` | — |
| 3 | Homepage | `Home` | — |
| 4 | Catalog | `ShoppingBag` | Product Template (`PanelsTopLeft`) |
| 5 | Content | `FileText` | Pages (`Files`) |
| 6 | Navigation | `Navigation` | Header (`PanelTop`), Footer Links (`PanelBottom`) |
| 7 | Footer Content | `PanelBottomDashed` | — |
| 8 | Appearance | `Palette` | Themes (`LayoutTemplate`), Colors & Typography (`Paintbrush`) |
| 9 | StoreConfig Version History | `History` | — |

The root uses the constitutional `Store` icon. `Category Template` has no node, route, dashboard card, or discoverability path. Overview, Storefront Status, and Product Template are directly discoverable from both the sidebar and Overview screen.

Legacy tenant labels such as Storefront Settings, Homepage Builder, Home Page, Theme Editor, Theme & Colors, CMS Pages, Footer & Social, and generic Version History are not exposed by the migrated Storefront Administration surfaces. Compatibility translation keys remain stable but now resolve to canonical terminology.

## Files changed

### Canonical IA and shell

- `PointOfSale.Web/src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.js`
- `PointOfSale.Web/src/modules/storefront/informationArchitecture/StorefrontBreadcrumbs.jsx`
- `PointOfSale.Web/src/modules/storefront/informationArchitecture/StorefrontAdminFrame.jsx`
- `PointOfSale.Web/src/modules/storefront/informationArchitecture/index.js`
- `PointOfSale.Web/src/App.jsx`
- `PointOfSale.Web/src/components/Sidebar.jsx`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`

### Canonical navigation, names, and titles

- `PointOfSale.Web/src/pages/StorefrontDashboard.jsx`
- `PointOfSale.Web/src/pages/StorefrontSettings.jsx`
- `PointOfSale.Web/src/pages/Settings.jsx`
- `PointOfSale.Web/src/components/StorefrontStatusCard.jsx`
- `PointOfSale.Web/src/components/AffiliateReadinessWarning.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderTopBar.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/ThemeMarketplacePage.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/ThemeEditorPage.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/VersionHistoryPage.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/NavigationEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/StorefrontFooterSettings.jsx`
- `PointOfSale.Web/src/shared/content/FooterConfigScreen.jsx`

The Builder, CMS, Navigation, Theme, Status, and Footer files above received only IA-facing labels, links, breadcrumbs, page titles, route-mode selection, and dirty-navigation guard registration. Their services, stores, mutation payloads, accepted-response handling, ownership, persistence, publication, preview, rollback, and concurrency behavior were not changed.

### Tests and evidence

- `PointOfSale.Web/src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.test.tsx`
- `PointOfSale.Web/src/pages/StorefrontSettings.test.tsx`
- `M1_INFORMATION_ARCHITECTURE_IMPLEMENTATION.md`

Pre-existing unrelated workspace changes were preserved and were not reverted or incorporated into the M1 scope.

## Route compatibility

Canonical and legacy routes are registered side by side under the same existing Owner/Admin guard. Aliases render the same screen instead of forcing redirects, so an old bookmark retains its original URL, query string, history entry, and back/forward behavior.

| Screen | Canonical route | Preserved legacy route or alias |
|---|---|---|
| Overview | `/storefront` | `/storefront/overview` is also accepted |
| Storefront Status | `/storefront/status` | `/settings?tab=storefront` |
| Homepage | `/storefront/homepage` | `/storefront/builder?page=home`; bare `/storefront/builder` retains its existing Home fallback |
| Product Template | `/storefront/catalog/product-template?templateId={id}` | `/storefront/builder?page=product-template&templateId={id}` |
| Pages | `/storefront/content/pages` | `/storefront/cms/pages` |
| Page | `/storefront/content/pages/{pageId}` | `/storefront/cms/pages/{pageId}` |
| Header | `/storefront/navigation?view=header` | `/storefront/cms/navigation` and `?view=header` |
| Footer Links | `/storefront/navigation?view=footer` | `/storefront/cms/navigation?view=footer` |
| Footer Content | `/storefront/footer-content` | `/storefront/cms/footer` |
| Themes | `/storefront/appearance/themes` | `/storefront/themes` |
| Colors & Typography | `/storefront/appearance/colors-typography` | `/storefront/theme` |
| StoreConfig Version History | `/storefront/storeconfig-version-history` | `/storefront/versions` |

No public Storefront URL changed. No existing route was removed. No API URL changed.

## Sidebar compatibility

- The constitutional tree is a single data definition, preventing duplicate nodes and label/order drift.
- Existing role filtering is retained and now applies recursively to nested groups.
- The Storefront group remains Owner/Admin-only.
- Active state is resolved by screen identity, so canonical and legacy URLs select the same sidebar node.
- Parent groups are organizational buttons and do not navigate to an unowned catch-all screen.
- Header and Footer Links share the same `/storefront/navigation` component and differ only by `view`, preventing remount/discard when switching the shared workspace.
- Expanded state is persisted in `localStorage` under a versioned key and restored on remount/reload.

## Deep-link compatibility

- `templateId` is read unchanged on canonical and legacy Product Template URLs and is retained when choosing, creating, or deleting templates.
- `pageId` remains a path identity on canonical and legacy Page routes.
- `view=header|footer` remains the Navigation view identity.
- Route matching does not strip unrelated query parameters.
- All route destinations load their owning screen independently; the Overview screen is not required as an initialization step.
- The history test moves from a legacy Product Template URL to its canonical URL, then back and forward, while retaining `templateId=summer` and resolving the same screen identity throughout.

## Breadcrumb validation

The shared breadcrumb resolver produces the constitutional route-backed hierarchy:

- `Storefront / Overview`
- `Storefront / Storefront Status`
- `Storefront / Homepage`
- `Storefront / Catalog / Product Template / {template name}`
- `Storefront / Content / Pages`
- `Storefront / Content / Pages / {page title}`
- `Storefront / Navigation / Header`
- `Storefront / Navigation / Footer Links`
- `Storefront / Footer Content`
- `Storefront / Appearance / Themes`
- `Storefront / Appearance / Colors & Typography`
- `Storefront / StoreConfig Version History`

Every segment before the current screen is a safe canonical link. Dynamic Product Template and Page identity is adopted by the breadcrumb and document title. Breadcrumb exits from dirty Homepage/Product Template, Page, Navigation, Footer Content, Colors & Typography, and Storefront Status workspaces invoke those screens' existing discard confirmation. Embedded Page History remains the existing frozen in-screen history workflow; M1 does not create a new history route or change its behavior.

## Page-title validation

The visible headings and browser titles now use Overview, Storefront Status, Homepage, Product Template, Pages, Header, Footer Links, Footer Content, Themes, Colors & Typography, and StoreConfig Version History. Tenant-facing Storefront IA no longer exposes Builder or CMS as screen names.

## Accessibility validation

- Expandable groups use native buttons with `aria-expanded` and `aria-controls`.
- Current leaf routes use `aria-current="page"`.
- Sidebar and breadcrumb navigation have explicit accessible labels.
- Decorative icons are hidden from the accessibility tree while labels remain textual.
- Breadcrumb links retain visible keyboard focus rings.
- DOM order matches constitutional menu order, preserving focus order.
- Keyboard Enter expansion and persisted expansion state are covered by the focused test.
- Mobile close control has an accessible label.

## Regression coverage

### Automated results

- Focused IA suite: **30/30 passed**.
  - exact labels, grouping, ordering, and all 15 semantic icons;
  - explicit Category Template absence;
  - 24 canonical/legacy route-resolution cases;
  - static route registration and Owner/Admin guard coverage;
  - canonical breadcrumbs, dynamic identity, document titles, and guarded navigation;
  - query identity across back/forward history;
  - keyboard navigation, active semantics, and reload persistence.
- Storefront, status, authorization, and footer regression run: **17 files, 116 tests passed**.
- Production build: **PASS**, 5,072 modules transformed.
- Scoped `git diff --check`: **PASS**; only repository line-ending notices were emitted.

Vitest emits existing React Router v7 future-flag notices and existing `act(...)` warnings in older guard/runtime-loop tests. These are non-failing harness notices and did not mask assertion failures.

### Regression search

A scoped production search across the tenant Storefront sidebar, Overview, Status, Storefront pages, CMS presentation pages, and the new IA module found no user-facing Storefront Settings, Homepage Builder, Category Template, or Back to Settings labels. The only Category Template matches in that scope are negative assertions proving its absence.

Repository-wide similarly named surfaces outside tenant Storefront Administration were intentionally not renamed: product-authoring publication fields use “Storefront Settings,” and the separate super-admin homepage-authoring authority has its own “Homepage Builder” surface. They are not sidebar nodes, routes, breadcrumbs, or titles in this constitutional tenant IA and changing them would expand M1 beyond its authorized boundary.

## Backward-compatibility evidence

- Existing URLs remain registered, rather than being removed after adding canonical routes.
- Existing query-driven Builder mode selection remains supported.
- Existing navigation `view` state remains URL-driven, so reload/back/forward restore the selected Header or Footer Links view.
- Existing dirty-state and `beforeunload` protection remains intact; the new breadcrumb links are now enrolled in the same protection.
- Existing Owner/Admin authorization wraps both route families. The existing `PosRoleRouteGuard` suite passes, including unauthorized route behavior.
- Direct-load and reload safety is supported by independent route registration, resolver coverage, production compilation, and remount/persistence tests.
- No identifier migration, data rewrite, persistence migration, DTO change, or compatibility cleanup was introduced.

## Risk analysis

| Risk | Mitigation | Residual risk |
|---|---|---|
| Old bookmark stops resolving | Every legacy route remains registered and is covered by the compatibility matrix | Low |
| Query identity is lost | Alias rendering and history tests preserve `templateId`/`view` | Low |
| Nested authorization leaks a child | Recursive role/capability filtering plus the existing route guard | Low |
| Dirty authoring state is discarded through a new breadcrumb | Breadcrumbs delegate to each workspace's current dirty flag and confirmation | Low |
| Header/Footer tab switch remounts shared state | Both canonical tabs use one pathname and one mounted editor | Low |
| Sidebar state is lost on reload | Versioned `localStorage` persistence and remount test | Low |
| Legacy aliases drift from canonical naming | One resolver drives active node, breadcrumb, and document title for both route families | Low |

The principal maintenance cost is that both canonical and legacy route families must remain tested until a separately authorized compatibility-removal phase.

## Rollback strategy

Rollback is presentation-only and does not require data recovery:

1. Restore the previous sidebar configuration and labels.
2. Remove the canonical route aliases and `StorefrontAdminFrame` while retaining all legacy routes.
3. Restore the previous internal navigation targets and headings.
4. Remove the M1 IA module and focused tests.

Because M1 changes no backend contract, DTO, persistence shape, version, cache entry, projection, published state, or runtime ownership, rollback requires no database migration, cache invalidation, content republish, or StoreConfig repair.

## Final scope assertion

- Runtime ownership changes: **none**
- API/DTO/backend/persistence changes: **none**
- Business/workflow/publication changes: **none**
- Theme/Builder/CMS/Navigation/Footer runtime changes: **none**
- Cache/projection changes: **none**
- Constitutional menu items missing: **none**
- Category Template exposed: **no**
- Legacy routes removed: **none**
- Certification: **PASS**
