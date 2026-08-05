# Wave 2 — Storefront Authoring Experience Alignment

## Executive Summary

Wave 2 creates one frontend authoring interaction model across Homepage, Product Template, CMS Pages, Navigation, Footer Content, Themes, Colors & Typography, Storefront Status, and StoreConfig History.

The implementation is principle-consistent rather than mechanically uniform. Shared layout, status, feedback, dirty-state protection, dialogs, conflict recovery, validation presentation, focus behavior, and keyboard behavior are canonical. Each certified lifecycle remains distinct:

- Homepage and Product Template save a draft and publish only their own Builder page scope.
- CMS Page preview reads a saved draft; CMS Page History and rollback remain Page-scoped.
- Header and Footer Navigation save and publish together.
- Footer Content and Storefront Status are immediate-live saves and do not use Publish language.
- Theme preview is signed and read-only; activation is an explicit immediate-live action.
- Colors & Typography has its own draft and publish lifecycle and does not claim Theme activation.
- StoreConfig rollback remains on StoreConfig History; Builder links to that surface and no longer implies a local rollback scope.

No Runtime, Theme runtime, Builder ownership, CMS ownership, Projection, Cache, API, DTO, persistence, authorization, concurrency rule, business rule, publication semantic, preview semantic, or rollback semantic was changed. All implementation changes are in `PointOfSale.Web` plus this certification document.

## Mandatory Reference Basis

Wave 2 was implemented against the complete contents of:

- `STOREFRONT_CANONICAL_UX_BLUEPRINT.md`
- `STOREFRONT_UX_PROTOTYPE_VALIDATION.md`
- `M1_INFORMATION_ARCHITECTURE_IMPLEMENTATION.md`
- `RUNTIME_OWNERSHIP_CERTIFICATION.md`
- `THEME_RUNTIME_CERTIFICATION.md`
- `BUILDER_RUNTIME_CERTIFICATION.md`
- `CACHE_RUNTIME_CERTIFICATION.md`
- `STOREFRONT_CMS_AUTHORING_CERTIFICATION.md`
- `FINAL_STOREFRONT_ARCHITECTURE_CERTIFICATION.md`

Certified implementations were treated as frozen boundaries. Wave 2 composes their existing APIs and accepted backend responses; it does not replace their authority.

## Canonical Authoring Contract

### Constitutional action order

Every applicable authoring toolbar renders actions in this order:

1. Back
2. Page or surface title
3. Status
4. Preview
5. History
6. Discard
7. Save Draft
8. Publish or the scope-exact live commit

An inapplicable action keeps no misleading substitute. For example, Footer has no Save Draft endpoint and therefore exposes `Save live Footer` in the final commit position; Storefront Status exposes `Save live Status`; Themes expose activation on the selected Theme card.

### Shared concepts

| Concept | Canonical behavior |
|---|---|
| Draft | Status badge and copy distinguish local candidate, accepted draft, published, and live state. |
| Dirty state | Field changes mark the surface dirty and enable Discard/Save where applicable. |
| Save | Uses the existing surface API and replaces the baseline only with the accepted full backend response. |
| Discard | Restores the accepted baseline, never an invented client default. |
| Preview | Names and disclosures match the certified preview source for that surface. |
| Publish/live commit | Uses scope-exact labels and an explicit confirmation describing inclusions and exclusions. |
| History | Names the exact history scope: `Page History` or `StoreConfig History`. |
| Rollback | Remains on its certified history owner; Builder no longer presents local rollback. |
| Conflict | One dialog, one wording model, retained candidate, `Load Latest`, `Retry My Draft`, and `Keep editing`. |
| Validation | Inline controls remain local; the shared summary uses `role="alert"` and blocks invalid commits. |
| Loading/busy | Shared announced loading state; busy actions expose `aria-busy`, spinners, and disabled controls. |
| Success/failure | Shared `role="status"`/`role="alert"` feedback with scope-exact success copy. |
| Unsaved exit | One guard covers browser unload, breadcrumb navigation, sidebar/in-app links, and explicit Back actions. |
| Recovery | Accepted latest-version adoption and retained-draft retry are explicit, never automatic candidate loss. |
| Permission | View-only/unauthorized state is intentional and fail-closed. |
| Empty | Pages, Themes, Footer sections, Product Template/Builder canvas, Navigation links, and History expose intentional empty states. |

### Dirty-state flow

`Field change → dirty status → beforeunload guard → breadcrumb/sidebar/link guard → canonical Unsaved changes dialog → Keep editing or Discard and leave`

The same guard also supplies:

- `Ctrl/Cmd+S` for the applicable save action.
- `Ctrl/Cmd+Shift+P` for the applicable preview action.
- Navigation suppression while Save/Publish/live commit is busy.

Builder focus/visibility refresh no longer reloads an active dirty or busy workspace, preventing candidate loss during ordinary window switching.

### Conflict and error presentation

HTTP and authoring failures are presented consistently:

| Evidence | Presentation |
|---|---|
| `409` / concurrency code | Conflict dialog; candidate retained; latest-version adoption and retry offered when authority evidence exists. |
| `422` / validation code | Validation summary plus field-level correction; commit remains disabled. |
| `404` / missing code | Resource unavailable recovery state. |
| `401` / `403` | Permission-required, fail-closed state. |
| `428` / precondition code | Latest-version-required recovery guidance. |
| Unexpected failure | Failure feedback retains the candidate and offers retry where safe. |

## Authoring Matrix

| Surface | State model | Preview | History | Discard / Save | Final commit | Conflict recovery |
|---|---|---|---|---|---|---|
| Homepage | Builder accepted draft | `Preview Saved Draft`; saves before preview per certified Builder behavior | `StoreConfig History` | Discard accepted page draft / `Save Draft` | `Publish Homepage` | Load latest / Retry retained sections |
| Product Template | Builder accepted template draft | `Preview Saved Draft`; saves before preview | `StoreConfig History` | Discard accepted template draft / `Save Draft` | `Publish Product Template` | Load latest / Retry retained sections |
| Pages list | Collection, not an editor | None; editor owns draft preview | None on list | New/seed/delete only | None; editor is sole Page publish initiator | N/A |
| CMS Page editor | Page accepted draft | `Preview Saved Draft`; disabled while dirty and never saves | `Page History` | Discard accepted Page draft / `Save Draft` | `Publish Page` | Load Latest / Retry My Draft; candidate retained |
| Navigation | One Header + Footer draft | Not applicable | `StoreConfig History` | Discard accepted Navigation / `Save Draft` | `Publish Header and Footer Navigation` | Load Latest / Retry My Draft |
| Footer Content | Immediate-live accepted configuration | Not applicable | `StoreConfig History` | Discard accepted live Footer | `Save live Footer` | Load Latest / Retry My Draft |
| Themes | Active Theme plus available catalog | `Preview Theme` using signed read-only preview | Not represented as StoreConfig authoring history | Not applicable | `Activate theme now` | Load Latest / Retry activation |
| Colors & Typography | Appearance accepted draft and live state | `Preview Candidate` in scoped local preview | `StoreConfig History` | Discard accepted Appearance draft / `Save Draft` | `Publish Colors & Typography` | Load Latest / Retry My Draft |
| Storefront Status | Immediate-live accepted status | `Open Storefront` remains clearly a live-store action, not draft preview | Not claimed | Discard accepted live Status | `Save live Status` | Load Latest / Retry My Draft |
| StoreConfig History | Backend publication snapshots | Not applicable | Exact StoreConfig scope; CMS Page and Theme catalog exclusions disclosed | N/A | `Rollback Now` with reason and explicit acknowledgement | Fresh RowVersion admission before rollback |

## Layout, Naming, and Feedback Alignment

The shared authoring layer provides:

- `AuthoringActionBar`
- `AuthoringStatusBadge`
- `AuthoringFeedback`
- `AuthoringValidationSummary`
- `AuthoringLoadingState`
- `AuthoringEmptyState`
- `AuthoringPermissionState`
- `AuthoringDialog`
- `AuthoringConfirmDialog`
- `AuthoringPromptDialog`
- `UnsavedChangesDialog`
- `AuthoringConflictDialog`
- `useAuthoringGuard`

Native `window.confirm`, `window.alert`, and `window.prompt` usage was removed from every aligned surface. Publish, activation, live-save, delete, snapshot replacement, translation overwrite, prompt input, exit, and conflict recovery now share the same accessible dialog behavior.

## Before / After Comparison

| Before | After |
|---|---|
| Save, Preview, Rollback, and Publish order varied per editor. | One constitutional action order across applicable editors. |
| Generic `Publish`, `Save`, `Save and Publish`, and `Publish Theme` labels obscured scope. | Scope-exact labels identify Homepage, Product Template, Page, combined Navigation, live Footer, live Status, Appearance publication, and Theme activation. |
| Builder exposed local rollback controls despite StoreConfig-owned history. | Builder links to `StoreConfig History`; rollback remains on its certified owner. |
| CMS list could preview and publish a draft outside the Page editor. | CMS list opens the Page editor; preview and publish remain editor-owned. |
| Dirty guards differed or were missing for sidebar, breadcrumb, or unload paths. | One hook protects unload, breadcrumb, sidebar/in-app links, Back, and editor page switching. |
| Native dialogs, alerts, and prompts varied in wording, focus, and recovery. | One modal system owns confirmation, prompts, unsaved exits, and conflicts. |
| Some conflicts automatically adopted server state or used `Keep my draft` without retry. | Candidate is retained; `Load Latest`, `Retry My Draft`, and `Keep editing` have one contract. |
| Footer and Status used publication-like wording for immediate-live mutations. | Both explicitly say `Save live …` and disclose their immediate-live boundary. |
| Loading, validation, success, and failure feedback used unrelated visual/ARIA patterns. | Shared announced states and summaries normalize presentation. |
| Empty Page, Theme, and History states could imply the wrong lifecycle. | Empty states name the exact missing resource and the allowed next action. |

## Files Changed

### Canonical authoring layer

- `PointOfSale.Web/src/modules/storefront/authoringExperience/AuthoringExperience.jsx`
- `PointOfSale.Web/src/modules/storefront/authoringExperience/useAuthoringGuard.js`
- `PointOfSale.Web/src/modules/storefront/authoringExperience/index.js`
- `PointOfSale.Web/src/modules/storefront/authoringExperience/authoringExperience.test.tsx`
- `PointOfSale.Web/src/modules/storefront/authoringExperience/wave2AuthoringAlignment.test.ts`

### Authoring surfaces

- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderTopBar.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/NavigationEditor.jsx`
- `PointOfSale.Web/src/shared/content/FooterConfigScreen.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/ThemeEditorPage.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/ThemeMarketplacePage.jsx`
- `PointOfSale.Web/src/pages/StorefrontSettings.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/VersionHistoryPage.jsx`
- `PointOfSale.Web/src/shared/content/authoringErrorFeedback.js`

### Regression coverage aligned to the constitutional contract

- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.test.tsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.test.tsx`
- `PointOfSale.Web/src/shared/content/FooterConfigScreen.test.tsx`
- `PointOfSale.Web/src/pages/StorefrontSettings.test.tsx`
- `PointOfSale.Web/src/modules/storefront/versionHistorySourceOfTruth.test.ts`

## Regression Coverage

### Automated results

- Storefront-focused suite: **18 test files passed; 122 tests passed**.
- Canonical authoring layer: **2 test files passed; 8 tests passed**.
- Production bundle: `npm run build` **passed** with 5,075 modules transformed.
- Scoped whitespace validation: `git diff --check` **passed** for the aligned tracked files.

Coverage includes:

- Constitutional toolbar ordering and exact labels.
- Dirty state, `beforeunload`, canonical exit dialog, and keyboard Save.
- Dialog focus, Escape dismissal, conflict wording, and recovery actions.
- CMS saved-draft preview isolation and editor-only publishing.
- Candidate retention and explicit latest-version adoption.
- Footer and Status accepted-response baseline replacement.
- Validation blocking before HTTP.
- Busy-state duplicate suppression.
- Builder runtime/ownership certifications and StoreConfig history source-of-truth assertions.
- Static Wave 2 certification across every aligned surface, including removal of native dialog drift.

## Accessibility

- Dialogs use `role="dialog"`, `aria-modal`, labelled titles, initial focus, Escape dismissal, focus containment, and focus restoration.
- Error and validation feedback use assertive live regions; success, loading, and informational feedback use polite live regions.
- Busy actions expose `aria-busy` and remain disabled until completion.
- Toolbars have an accessible `Authoring actions` label.
- Icon-only Navigation and modal controls now have accessible names where changed.
- Keyboard save and preview shortcuts are centralized.
- Visible focus rings are consistent on canonical actions.
- Empty, permission, and loading states are semantic sections/status regions rather than spinner-only output.

## Blueprint Compliance Evidence

| Blueprint requirement | Evidence |
|---|---|
| One application feel | Shared action bar, badges, feedback, dialogs, loading, empty, validation, and permission components. |
| Preserve real lifecycles | Matrix uses Page, Builder page, Navigation, immediate-live Footer/Status, Theme activation, Appearance publish, and StoreConfig rollback independently. |
| No global publish illusion | Every final action names its exact scope; CMS list has no publish action. |
| Preview source disclosure | Builder says saved-before-preview; CMS says saved draft and blocks dirty preview; Theme says signed read-only; Appearance says candidate preview. |
| Exact history ownership | CMS Page editor uses Page History; StoreConfig-owning surfaces link to StoreConfig History; exclusions are disclosed. |
| Candidate retention on conflict | Conflict state remains separate from the editor candidate until Load Latest is explicitly chosen. |
| Accepted response replaces baseline | Existing admission functions remain authoritative after saves, publishes, activation, live commits, and rollback. |
| No backend or runtime redesign | Changes are confined to frontend authoring composition, tests, and this document. |

## Risk

Residual risk is limited to frontend interaction integration:

- The guard uses Browser History events because the application uses `BrowserRouter`, not a data router. Browser unload, breadcrumbs, sidebar/in-app anchors, Back controls, and editor switching are covered; production cross-browser smoke testing remains advisable.
- Shared canonical action copy is currently English-first on several newly aligned controls while existing field content remains bilingual. This does not change semantics or accessibility, but a later translation-only pass may add resource keys.
- Automated ARIA/focus coverage is present, but assistive-technology testing with NVDA/JAWS and browser-specific keyboard smoke testing should remain part of release QA.
- Existing bundle-size warnings are unchanged by the authoring contract and are outside Wave 2 scope.

No data migration, cache invalidation, API deployment, DTO change, or backend rollback is required.

## Rollback

Wave 2 can be rolled back by reverting only the files listed in **Files Changed** and removing `PointOfSale.Web/src/modules/storefront/authoringExperience`. Because no API, persistence, DTO, cache, projection, runtime, or business behavior changed, rollback requires no data repair and no backend coordination.

Do not roll back certified Runtime, Theme runtime, Builder ownership, CMS ownership, Cache, Projection, API, or persistence code as part of a Wave 2 rollback.

## Certification

**PASS WITH OBSERVATIONS**

The implementation satisfies the Wave 2 constitutional authoring contract and preserves all certified ownership and behavior boundaries. The observations are limited to recommended cross-browser/assistive-technology release smoke testing and a future translation-resource pass; neither changes the implemented interaction model or business semantics.
