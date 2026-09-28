# MAIN CMS and Navigation Simplification Working Notes - 2026-09-27

## Baseline

- Repository: `C:\Users\fadya\source\repos\PointOfSale`
- Branch: `main`, tracking `origin/main`
- Environment checked: existing Windows Development services only. API `5010`, Admin `5173`, and Storefront `5174` returned HTTP `200`.
- Unrelated pre-existing product-image/security changes and artifacts were present before this work and must remain untouched.
- Authenticated browser and database mutation verification is blocked unless the existing `CMS_CERT_*` or `NAV_CERT_*` environment variables are supplied. No credentials or connection strings were read or printed.

## Evidence-Backed Findings

1. Pages are canonically served at `/storefront/content/pages` and `/storefront/content/pages/:pageId`; `/storefront/cms/pages*` are legacy aliases. Create, draft save, preview, publish, delete, immutable history, and append-only rollback already share one editor and PostgreSQL `CmsPages` authority.
2. A logical CMS page is identified by stable `CmsPage.PageGroupId`; row `Id` identifies a version. Navigation persists `TargetId = PageGroupId`, and the backend recomposes the current slug. A rename therefore retains the link. Deleted or unavailable targets fail closed. Draft-only pages may be saved in a Navigation draft but block Navigation publication until the page is published.
3. Navigation Header and Footer links are one draft/live scope in `StoreConfig.CustomizationJson.navigation`, protected by `StoreConfig.RowVersion`; they intentionally publish together. StoreConfig history owns Navigation publication history/rollback. The dedicated Footer Content feature is a separate immediate-live capability and must not be merged with Footer Navigation links.
4. `AdminCmsService.GetDefaultNavigation` fabricates four slug-only CMS Footer links even when no CMS pages exist. `NavigationAdmissionService` correctly preserves them as invalid repair candidates, producing the observed four warnings. With no pages, `GetNavigationTargetsAsync` correctly returns an empty `cmsPages` collection, so the selector has no possible repair target.
5. The frontend silently converts every target-load failure into empty arrays. The Add Link dialog therefore cannot distinguish a genuine empty page collection from an API/contract/network failure.
6. An unresolved repair candidate can be admitted for display with a null `targetId`, but `mapNavigationDraftToRequest` rejects it before the API call with `footer link N requires a selected cms target`. The draft remains in memory, but the message is generic, uses a zero-context ordinal, and offers no direct repair/remove action.
7. Navigation already has Header/Footer tabs, but the information architecture also exposes separate Header and Footer child links. Both resolve to the same editor; the separate links are entry points, not separate persistence workflows.
8. Pages and Navigation contain many hardcoded English strings. Some labels use inline Arabic conditionals while action bars, confirmations, validation summaries, warnings, status text, and error feedback remain English. Document direction is globally synchronized by `syncDocumentLanguage`, but the affected components do not consistently use locale resources.
9. The current CMS storage contract has one `Title`, one `Content`, and one SEO set per logical version. It has no locale discriminator or Arabic/English fields. Adding bilingual page inputs requires an architectural persistence/API/migration decision and cannot be safely inferred. This work will not invent a translation storage model.
10. No CMS unpublish operation exists. Existing statuses are Draft, Published, and UnpublishedChanges. This work will preserve that contract rather than fabricate unpublish behavior.
11. `reportAllChanges` and its `startTime` access do not exist in either application source tree, HTML, package manifests, or declared web-vitals code. The reported stack symbol is therefore not currently attributable to repository-owned code. An application-level global error suppressor would hide the originating error and is not a safe fix. Browser source-URL evidence is still required.

## Safe Changes Proposed

- Stop generating invalid default CMS Footer links. New/unconfigured Navigation will retain Home and Shop and present an honest empty Footer state; explicit default-page seeding remains separate.
- Preserve existing invalid links exactly, but add conspicuous Repair and Remove actions and focus the exact row when save validation fails.
- Represent target loading, failure, and genuine empty states separately. Provide Retry, Create page in a separate same-origin tab, and Refresh pages actions without discarding Navigation edits.
- Make the common Page and Navigation actions use complete English/Arabic locale resources and retain global RTL/LTR direction behavior.
- Keep Appearance and dedicated Footer Content separate, preserve combined Header/Footer publication, optimistic concurrency, draft/live isolation, target identity, tenant isolation, and server-side validation.
- Add focused frontend/backend regression tests for empty targets, target-load failure, invalid-link repair/removal, draft preservation after failure, default Navigation without fabricated CMS links, and existing combined publication behavior.

## Decision Boundary

Localized Arabic/English CMS page content cannot be implemented safely without choosing a new content storage and public API model. That decision is intentionally deferred. The final scope verdict cannot be a full PASS against the requested bilingual content requirement unless an established model is supplied.
