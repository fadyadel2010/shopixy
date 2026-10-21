# POS Admin smart navigation and Arabic localization

Date: 2026-10-21 · Workspace: `C:\Users\fadya\source\repos\PointOfSale`  
Baseline revision: `2d55764ab82fe11e69a2d9cbac685a0bfb3c4a16`  
Evidence: [admin-smart-navigation-20261021](../evidence/admin-smart-navigation-20261021/)

## A. Executive verdict — CONDITIONAL

**Implemented and verified in real headed Microsoft Edge, including the final production build.** Global bilingual page search, contextual results, Favorites, Recent pages, and the existing navigation's English/Arabic labels are integrated without replacing the Sidebar or changing authorization policies.

- **246 automated frontend tests passed**, 22 files, no skipped/failed tests in the final focused run. This includes **33 new navigation tests**.
- Admin production build and TypeScript check passed. Focused lint: **0 errors / 4 warnings**.
- **108 headed-browser assertions passed** across search, preferences, localization/responsive, controlled authority refresh, and closeout.
- Backend permission/boundary selection rebuilt from current source: **61 passed / 3 failed / 0 skipped**. All three failures expect legacy structures/attributes already absent at baseline; they were not weakened or deleted.
- **Not certified:** a real second authorized POS account/tenant switch. The provided Storefront customer account is a separate authority, not a substitute POS account. Live user/tenant changes have rendered-component coverage; browser isolation checks use a guest context, a foreign storage namespace, and an explicitly intercepted reduction of the existing user's presentation authority. No backend user/role/tenant was changed.

This is conditional rather than an unrestricted PASS because of that unexecuted real-account scenario and the preserved backend failures, not because implementation or headed-browser testing was omitted.

## B. Navigation inventory and baseline

### Sources of truth

- `PointOfSale.Web/src/App.jsx`: actual React Router route declarations and nested guards. `appRouteElements()` exposes these same elements; `AdminNavigationRoutes` derives the route objects used by **both** `useRoutes` and registry validation. No separately maintained search route table exists.
- `PointOfSale.Web/src/components/Sidebar.jsx`: existing menu paths, labels, hierarchy, icons, permissions and capability conditions, now exposed as `buildAdminMenu(t, user)` without moving/replacing the established menu.
- `PointOfSale.Web/src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.js`: existing Storefront Design/Operations tree, screen IDs, route compatibility and breadcrumb/title metadata.
- `PosPermissionRouteGuard`, `CommerceHubVendorRouteGuard`, `auth/posPermissions.js`, `auth/posVendorSessionAuthority.js`, and `marketplace/authority.ts`: existing authorization projections. Search intersects menu visibility with actual matched route guards, using these functions, never guessed roles or URL-derived permissions.
- `i18n/config.js`, `i18n/locales/{en,ar}.json`, `useRTL`, and `LanguageSwitcher`: existing localization and document-direction mechanism.

### Inventory totals

| Context | Unique searchable destinations |
|---|---:|
| POS Admin, including existing affiliate management | 59 |
| Storefront management: Design and Operations | 37 |
| CommerceHub merchant tools | 5 |
| Services Marketplace shell | 1 |
| **Total** | **102** |

The existing Sidebar has **107 destination placements / 27 collapsible groups** for the configured Owner. Five repeated destination placements remain in the Sidebar as before but are deduplicated in search. **102/102 destinations have English and Arabic labels.** All **134 menu/group placements** were inventoried from the expanded DOM in each locale. A code-source audit additionally finds **141 navigation/title/breadcrumb keys, zero missing English or Arabic resources**. All **25 new smart-navigation UI/module keys** exist in both locales.

Evidence: [translation-source-inventory.json](../evidence/admin-smart-navigation-20261021/translation-source-inventory.json), [full bilingual DOM inventory](../evidence/admin-smart-navigation-20261021/browser-localization.json), and `navigationRegistry.test.tsx`.

### Disambiguation and exclusions

- Actual `/products` remains **Item Card** in English. `Products`, `SKU`, Arabic alternatives and catalog keywords find it without renaming it. `/admin/affiliates/products` remains **Affiliate Products**, with a separate stable ID and breadcrumb.
- There is **no separate merchant Products menu route** to fabricate. CommerceHub's actual Publishing Settings route `/admin/commercehub` is discoverable through merchant-catalog/publishing aliases.
- Both `/treasury` and `/commercehub/finance` display **Finance Dashboard**. Results show **POS Admin → Treasury** versus **CommerceHub · Merchant → CommerceHub** in both languages.
- Similarly named Orders and Overview destinations retain their distinct module and full parent hierarchy.
- Excluded: Super Admin, public marketplace/CMS, public affiliate hub, and the separate customer Storefront application; redirects/compatibility aliases; wildcard routes; detail/editor routes requiring a product/order/customer/domain identity; non-destination structural groups; access-request placeholders; inaccessible capabilities/permissions. These remain available through their existing owning applications/workflows where authorized.
- No arbitrary route parameter or entity ID is synthesized. Existing menu selectors (`tab=main/sub`, `view=header/footer`) remain separate supported destinations. Unregistered selectors do not become saved destinations.

### Configured session and pre-change behavior

No registered Playwright session existed initially. The isolated headed Edge session was opened at `http://localhost:5173/`, then signed in with the supplied POS Admin account. Baseline: Owner, user `1bb114d3-a9a3-4a9d-ace3-3690361637a8`, tenant `c9c5c1ae-949e-498e-bc63-07242daeb49d`, English/LTR; 57 effective permissions and 11 vendor navigation capabilities at closeout. Credentials are not in scripts, reports, screenshots or stored preferences.

The Sidebar was long, Storefront Operations was initially discoverable/expanded, several labels depended on English fallbacks, and no existing page-search/Favorites/Recent implementation or supported per-user navigation-preference API was found. Existing settings API is tenant/business settings, not user navigation preferences. Existing actor-scoped Sidebar expansion storage was retained. Existing Radix Dialog usage was reused for the palette.

Initial focused regression baseline: **128 passed**, 9 files. See `baseline-tests.log` and `baseline-en.yaml`.

## C. Implementation

### Changed/new files

All application source changes are under `PointOfSale.Web/src/`:

| Files | Rationale |
|---|---|
| `App.jsx`, `App.d.ts` | Expose the existing JSX route tree, consume it through the registered route boundary, identify the POS layout, and mark existing breadcrumb-owning routes. Existing paths/elements/guards remain intact. |
| `components/Sidebar.jsx`, `Sidebar.d.ts` | Reuse the existing menu through a metadata factory, add explicit application identities, and render compact collapsible Favorites before the established hierarchy. |
| `components/Layout.jsx` | Scope navigation provider to the authenticated layout; add compact search and navigation breadcrumbs. |
| `components/LanguageSwitcher.jsx` | Small-screen spacing only; preserve its existing actual locale-switch mechanism. |
| `navigation/AdminNavigationRoutes.jsx` | Derive actual route objects and observe committed leaf screens below the existing route guards. Redirects are not recorded. |
| `navigation/navigationRegistry.js` | Derive accessible destinations from the existing menu and actual router; stable identities, both locales, breadcrumb/module metadata, aliases, exact route/query matching, ranking. |
| `navigation/navigationPreferences.js`, `SmartNavigationContext.js`, `SmartNavigationProvider.jsx` | Scoped ID-only preferences, validation, live authority filtering, storage failure recovery, cross-tab preference sync. |
| `navigation/CommandPalette.jsx` | Reused Radix modal, shortcut/visible entry, accessible keyboard search, module-aware results and favorite controls. |
| `navigation/SidebarFavorites.jsx`, `NavigationBreadcrumbs.jsx` | Compact contextual favorite links/removal; localized breadcrumb/title integration without duplicating page-owned breadcrumbs. |
| `navigation/*.d.ts`, `navigation/navigation.types.ts`, `auth/posPermissions.d.ts` | Type contracts for the existing JavaScript architecture and focused TypeScript tests. |
| `navigation/navigation.fixture.ts`, `navigationRegistry.test.tsx`, `navigationPreferences.test.ts`, `CommandPalette.test.tsx`, `NavigationBreadcrumbs.test.tsx` | 33 new behavioral/inventory regressions using the actual route tree and existing authority shape. |
| `i18n/locales/en.json`, `ar.json` | Missing navigation resources, search/preferences/module controls and accessible text; Arabic CommerceHub context, one navigation-label typo correction. |
| `modules/storefront/informationArchitecture/storefrontInformationArchitecture.js`, `.d.ts` | Explicit Storefront application metadata only; preserve the existing hierarchy and compatibility definitions. |
| `modules/storefront/informationArchitecture/StorefrontBreadcrumbs.jsx` | Localize the formerly hardcoded Storefront Administration document-title suffix. |

### Search and keyboard

- Visible compact header search, plus **Ctrl+K**. Does not intercept text inputs, textareas, selects, editable fields, composition, repeat, modifier variants, prevented shortcuts, or another open dialog.
- Existing Radix Dialog supplies focus trapping, Escape/dismissal and scroll locking. Focus is placed in the combobox and restored to the prior connected element. Arrow Up/Down select and scroll; Enter navigates the selected actual destination.
- Uses real `navigate(entry.to)` objects validated against `matchRoutes`. Actual route admission/redirect remains owned by the existing guards; dialog closure does not manufacture a successful recent visit.
- Predictable tiers: exact label/alias; strong label/alias prefix; word-prefix phrase; all-query-word match in explicitly registered labels/module/breadcrumb/aliases. Single accidental characters do not match. English and Arabic are indexed regardless of UI language; Arabic diacritics, tatweel and common alef forms are normalized.
- Query length is bounded at 200 characters. The current 102-entry result inventory scrolls inside a viewport-limited dialog. No remote search request or search-query persistence is introduced.
- Stable ID: application + existing Storefront screen ID, menu key, or translation identity, never the displayed label or an unvalidated URL. No duplicate result for one destination contract.

### Favorites and Recent persistence

Browser-local, versioned key: `pointofsale.admin.navigation.v1:<encoded tenantId>:<encoded userId>`. Value contains only `version`, `favorites` IDs and `recent` IDs. No email, tokens, queries, entity/customer/order details or search content.

- Favorites: insertion order, no duplicates, at most 20; star actions in results; compact collapsible Sidebar section with localized title, module and parent context; remove without navigating; localized empty state.
- Recent: latest 10 distinct accepted registered screen visits; revisits move first; clear is persisted and does not immediately re-add the current route. A later reload is a new successful visit and records that page again.
- Route-commit observation runs below existing nested guards rather than on clicks or in Layout. An unregistered route, denied guard, redirect, dynamic detail URL or unsupported selector does not enter history. API-data success is **not** inferred from a route visit; data-loading/business-workflow certification is outside this navigation task.
- Revalidate stored IDs against the **currently accessible** registry; prune restricted/deleted IDs; recover from corrupt/outdated/partially invalid storage and denied/quota-limited storage. Preferences remain functional in memory if storage cannot be used.
- Actor/tenant changes remount the scoped preference state; logout removes the authenticated layout; a different actor cannot inherit it. Same-user preferences remain isolated on disk for that actor's next login. Cross-tab changes synchronize only the current namespace.
- Browser-local preferences are intentionally per origin/device; development `5173` and production-preview `5175` do not share localStorage.

### Localization and RTL

Eight missing bilingual keys were added: `nav.paymentMethods`; `storefront.design`, `storefront.abandonedCheckouts`, `storefront.onlineCustomers`, `storefront.shippingSetup`; `analytics.conversionFunnels`, `analytics.campaignCosts`, `analytics.marketingRoi`.

The complete existing menu/group tree and owned Storefront title/breadcrumb metadata are checked for actual resources in **both** locales, not i18next fallback success. Search indexes both sets of resources. Locale switching relabels results/Favorites/Recents without changing IDs. Existing Storefront breadcrumbs are reused on operations pages that previously had none; pages/frames that already render them retain ownership. POS breadcrumbs/document titles use the same accessible menu hierarchy. Mixed brand names such as CommerceHub/StoreConfig are intentionally preserved within Arabic strings. Direction comes from existing i18n/`useRTL`; `<bdi>` isolates mixed metadata.

No global CSS, package dependency, backend API/schema or authorization-policy change was introduced. Header spacing at small widths was tightened after a real 390px overflow reproduction; all critical controls remain visible down to 320px.

## D. Headed-browser verification

### Actual running build

Established isolated session: `opencode-ui-test`, Microsoft Edge **headed**. Primary dev app remains `http://localhost:5173/`; final production assets served by Vite preview at `http://localhost:5175/`, using the existing local API proxy and session cookies. This did not replace/stop the user's development server or API.

Final build: **`/assets/index-C6lqqx8V.js`**. Served production index exactly matches `PointOfSale.Web/dist/index.html`; hashed build identity in [running-build.json](../evidence/admin-smart-navigation-20261021/running-build.json). Tests asserted production `/assets/` scripts and no Vite source client. Search/preferences were rerun after the last breadcrumb fix/build; localization, authority and closeout also ran on that final build.

| Scenario | Final outcome | Evidence |
|---|---|---|
| Search, real route identity, English/Arabic names/aliases, SKU, duplicate names, Ctrl+K, arrows, Enter, Escape, focus restoration, ordinary field and actual Product Feed dialog safety | **26 passed** | [browser-search.json](../evidence/admin-smart-navigation-20261021/browser-search.json) |
| Favorites across POS/Storefront/CommerceHub; reload/add/remove/deduplication; invalid IDs; foreign namespace; 12 actual Sidebar destinations; Recent cap/revisit/clear/reload; query non-retention; real Super Admin denial; fresh guest denial/isolation | **19 passed** | [browser-preferences.json](../evidence/admin-smart-navigation-20261021/browser-preferences.json) |
| Full expanded navigation inventory in both locales; metadata/empty states; cross-language search; same-name context; favorites/recents; breadcrumbs/title; desktop 1440px, tablet 820px and 390/320px; RTL and mobile Sidebar open/close; identical destination IDs | **46 passed** | [browser-localization.json](../evidence/admin-smart-navigation-20261021/browser-localization.json) |
| Open search cleared on live authority refresh; reduced result/favorite set; direct-route guard rejection and no false Recent; restored real session; corrupt preference recovery | **8 passed, explicitly controlled session-response interception** | [browser-authority.json](../evidence/admin-smart-navigation-20261021/browser-authority.json) |
| Dialog accessible label/description, real Tab/Shift+Tab containment, long-list keyboard scrolling, scroll-lock release, original Sidebar inert/collapse behavior, AhramStore application boundary, final primary session preservation | **9 passed** | [browser-closeout.json](../evidence/admin-smart-navigation-20261021/browser-closeout.json) |

**Total: 108 assertions, 5 final scenario runs passed.** [Summary](../evidence/admin-smart-navigation-20261021/browser-summary.json). These are DOM/route/storage/network assertions, not screenshot-only claims.

Real selected routes include `/products`, `/treasury`, `/commercehub/finance`, `/ecommerce/payment-methods`, `/marketplace`, plus the 12 ordinary menu paths listed in the preferences evidence. Search network evidence records real API status codes, without response bodies/credentials. Marketplace's existing `POST /api/marketplace/search` is a **read**, not a business mutation. No business write was submitted by the navigation scenarios.

AhramStore was opened read-only at **`http://ahramstore.localhost:5174/`**, confirmed separate from POS and without the Admin palette. Nissan was not accessed. No customer login or checkout was needed for this boundary smoke; it is not a Storefront workflow audit.

### Screenshots and DOM

All paths below are under `.ai/evidence/admin-smart-navigation-20261021/`:

- `search-products-en.png`, `search-finance-context-en.png`: distinct actual result identities.
- `favorites-en.png`, `recent-en.png`: separate contextual preference sections.
- `sidebar-en-desktop.png`, `sidebar-ar-desktop.png`: established hierarchy/localized header.
- `palette-en-desktop.png`, `palette-ar-desktop.png`: bilingual module/breadcrumb context.
- `palette-en-390.png`, `palette-ar-390.png`: narrow layout, including mixed Arabic/CommerceHub metadata.
- `sidebar-en-390.png`, `sidebar-ar-390.png`: existing mobile Sidebar preserved.
- `final-primary-dashboard.png`, `final-primary-dashboard.yaml`: returned to primary development dashboard, same Owner/tenant, English/LTR.
- `browser-localization.json`: **all 134 Sidebar placements and all 102 result IDs/text/accessibility labels in each locale**, not just screenshot samples.

### Failed attempts, blocked and unexecuted scenarios

- Earlier script attempts are retained, not silently reported as passes. Failures included overly broad selectors matching both a favorite link and its original menu link, a hidden HelpDrawer also matching `aside`, immediate assertions before Radix focus/Sidebar transitions finished, and using viewport width instead of the RTL content width with a scrollbar. Selectors/waits were corrected without weakening product assertions.
- An initial operations-breadcrumb check correctly found no breadcrumb; source was repaired by reusing the existing component, followed by a new build and final passing tests.
- Two early browser-daemon runs closed due to an unsupported script-runtime `URL` use in an event handler. The isolated session was reopened following the workspace instructions. No unrelated Edge process was terminated.
- **Blocked/unexecuted:** a real authenticated second POS user/tenant switch and a real backend permission revocation. No second POS account/switch procedure was supplied; no role/tenant changes or new test accounts were invented. Controlled presentation-authority refresh, guest/namespace checks and rendered-component actor/tenant tests are evidence for distinct aspects, **not** a claim to have executed those real-account scenarios.
- **Unexecuted:** full screen-reader/WCAG audit, other browser engines, every destination's business workflow/API-data correctness and external services. Navigation-label inventory is complete for the actual Sidebar, not a claim to translate unrelated screen content.

## E. Automated quality results

| Gate | Result | Evidence |
|---|---|---|
| Pre-change focused frontend baseline | **128 passed**, 9 files | `baseline-tests.log` |
| Final navigation, Sidebar, route guards, auth bootstrap/vendor authority, i18n/RTL, all frontend architecture and Storefront IA tests | **246 passed**, 22 files, 0 failures/skips | `frontend-tests-complete.log` |
| New focused tests within that total | **33 passed**: registry 12, preference 11, palette 7, breadcrumb 3 | `src/navigation/*.test.*` |
| Admin TypeScript, `npx tsc --noEmit` | **PASS** | `typecheck-complete.log` (empty successful output) |
| Admin production build | **PASS**, 5,294 modules | `build-closeout.log` |
| Focused lint on changed source/declarations/tests | **PASS with warnings: 0 errors / 4 warnings** | `lint-complete.log` |
| Changed Admin tracked-file whitespace validation | **PASS**, `git diff --check -- PointOfSale.Web` | command result; Git only warns about existing Windows line-ending normalization |
| Rebuilt backend permission/session and application-boundary selection | **61 passed / 3 failed / 0 skipped**, 64 total | `backend-navigation-rebuilt.trx`, `backend-tests-rebuilt.log` |

The final frontend command uses `--maxWorkers=1 --testTimeout=20000`. Earlier parallel/default-budget runs are retained: one full route-graph import exceeded a 120-second shell limit, one TypeScript run exceeded that limit, and a combined run had **3 test timeouts / 240 passes** while other builds were active. The final serial runs pass all assertions without modifying unrelated tests or global timeouts. An early task-authored test used an unsupported Testing Library `exact` option; that test call was corrected before the final passing typecheck.

Lint warnings: three React Fast Refresh warnings from exporting the shared route/menu factories and shortcut predicate alongside components; one pre-existing Sidebar effect dependency warning. Build retains the existing large-main-chunk warning (approximately 5.66 MB uncompressed). Browserslist data and ESLint legacy-config deprecation warnings were not addressed by unrelated dependency/config churn.

### Three preserved backend failures

1. `StoreAdministrationEntryNavigationAuthorityCertificationTests.StoreAdministrationRoutes_MustUseOwningBackendAuthorization`: expects a CommerceHub analytics capability wrapper in `App.jsx`. Existing Storefront analytics routes use canonical POS permission guards; the expected older wrapper is already absent at HEAD.
2. `StoreAdministrationEntryNavigationAuthorityCertificationTests.StoreAdministrationSidebar_MustBePresentationOnlyFromBackendCapabilities`: expects the old `STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH` structure, already absent at HEAD before this task.
3. `PosPermissionAuthorityCertificationTests.RepresentativeAdministrativeEndpoints_MustDeclareBackendPermissionPolicies`: `.Single(...)` expects the class-level Owner/Admin `[Authorize]` attribute on `AdminCommerceHubController`, which the current controller does not have. The attribute is also absent at HEAD. No backend controller or policy was changed by this task; this report does not infer unrestricted access from a source-shape failure.

Baseline source comparison: [historical-failure-baseline.json](../evidence/admin-smart-navigation-20261021/historical-failure-baseline.json). Initial `--no-build` and subsequent rebuilt selections produced the same **61/3** result. The rebuilt result, not stale assembly success, is the reported final backend gate.

Existing Storefront/Admin boundary selections include frontend architecture/IA tests and `StorefrontProductBoundaryCertificationTests`, `CommerceHubDomainBoundaryArchitectureCertificationTests`, Services Marketplace actor navigation and POS session/permission tests. Customer Storefront code does not consume the new Admin registry. The entire repository's frontend/backend suites and configured-database/external-service integration suites were **not** run; no broad all-suite certification is claimed.

## F. Preservation and remaining limitations

- Worked only in the primary Windows repository; **no tester machine access**.
- Existing Sidebar hierarchy, icons, collapse/expand, active-route state and Storefront access placeholders remain; source/behavior tests and headed assertions verify them. Route paths, route guards, backend/API policies and module boundaries were not relaxed.
- No database reset/restore, migration/schema change, new backend API, or merchant/customer/order/payment test record creation. Navigation tests use reads and browser-local task-owned preferences. No actual payment/shipping/publication action was performed.
- Initial tracked manual-payment changes, the untracked manual-payment test and prior evidence directory remain. SHA-256 comparison confirms all six pre-existing changed/untracked source files are unchanged by this task: `unrelated-preservation-comparison.json`. Their contents were not overwritten to obtain passing tests.
- **No staging, commit, reset, clean, stash, discard/checkout or broad restore**. Existing historical reports/screenshots were not modified. Task evidence stays in its requested directory.
- Original primary account/tenant and English locale were reverified at closeout. The isolated automation session remains open on the primary dashboard; the API was neither stopped nor restarted. Production preview is task-owned on port 5175; the original development servers remain intact.
- Favorites/Recents are deliberately local to a browser/origin, not server-synchronized. Entity-specific detail/editor routes are deliberately not retained. A committed registered screen is a page visit, not proof that every subsequent data request or business operation succeeded.
- Remaining verification limitations and backend failures are explicitly listed above. Stop condition: this focused navigation/localization implementation, its tests, headed proof and report; no unrelated feature or data changes.
