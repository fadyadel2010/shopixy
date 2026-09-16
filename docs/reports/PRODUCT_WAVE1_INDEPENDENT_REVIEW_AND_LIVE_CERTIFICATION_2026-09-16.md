# PRODUCT WAVE 1 INDEPENDENT REVIEW AND LIVE CERTIFICATION (2026-09-16)

## 1. Executive Verdict

**FAIL.** The reviewed Wave 1 implementation is code-complete for the scoped slug, bilingual identity, and Product localization work. Focused regressions, production frontend builds, the localization audit, and the EF model-drift gate pass. The mandatory live product lifecycle was explicitly deferred before any test product was created, so storefront reality, responsive behavior, database reconciliation, and runtime tenant isolation remain `NOT CERTIFIED`. A strict independent certification cannot return `PASS` while those required gates are absent.

## 2. Review Mandate

This review independently inspected the prior implementation, reproduced its authority assumptions from source, corrected defects found during review, added regressions, and attempted live validation against the local AhramStore environment. No subagent or delegated implementation was used. No commit was created.

## 3. Scope

Wave 1 scope was limited to Product slug/storefront identity, bilingual name and description presentation, and localization of affected Product screens. SKU uniqueness, variant lifecycle redesign, pricing redesign, AI workflow changes, option-group behavior, product-type simplification, redirects, and historical data repair remain outside scope.

## 4. Initial Worktree Baseline

The inherited worktree contained changes to the Product authoring orchestrator, Product form, English and Arabic locale files, new slug/display helpers and tests, plus `PRODUCT_WAVE1_IDENTITY_SLUG_LOCALIZATION_CERTIFICATION_2026-09-16.md`. Nothing was staged. No migration, EF configuration, or model snapshot was changed.

## 5. Prior Evidence Assessment

The prior report was treated as evidence, not authority. Its core blank-slug diagnosis was correct, but its initial uniqueness implementation had a case-sensitive database query and a race window between slug resolution and persistence. Its `ProductDisplayIdentity` helper was not used by production. Its live browser, storefront, API/database, responsive, and tenant checks were explicitly incomplete.

## 6. Product Domain Contract

`Product.Name` is required and capped at 300 characters. `NameAr`, `Description`, and `DescriptionAr` are optional. `Product.Slug` is nullable in the persistence model, but the public slug route rejects a blank slug and product feeds omit slugless products. The existing `{ TenantId, Slug }` index is not unique.

## 7. Writer Inventory

Supported controller and bulk create/update paths route through `IProductAuthoringOrchestrator.AuthorAsync`. A direct writer remains in `DevelopmentCommerceHubSeedService`; it is development-only and assigns fixed nonblank slugs. The orchestrator is therefore the production authoring authority, but a database unique constraint still does not protect unsupported or future direct writers.

## 8. Original Slug Defect

The form initialized and submitted a blank slug while the server persisted the value verbatim. Such a product could exist by id but could not resolve through the storefront slug route. Invalid characters and case were not normalized server-side, and duplicate tenant slugs were accepted.

## 9. Slug Authority Decision

`ProductSlugAuthority` is the server-side authority. It selects an explicit valid slug first, then derives from canonical `Name`, then uses deterministic `product-{id8}` fallback text when no Latin slug text exists. The browser suggestion is convenience only; blank submission is sent as `null` and remains valid input to server derivation.

## 10. Slug Normalization

Normalization trims, lowercases, replaces non-ASCII-alphanumeric runs with `-`, trims separators, and caps output at 160 characters. Arabic-only, punctuation-only, null, and whitespace-only input falls through to deterministic generation rather than persisting unusable identity.

## 11. Collision Detection

Collision lookup is tenant-scoped, excludes the product being written, and case-folds stored values in SQL before comparing the base and numeric-suffix family. In-memory candidate matching uses `StringComparer.OrdinalIgnoreCase`, covering historical mixed-case rows.

## 12. Concurrency Remediation

PostgreSQL authoring now starts a transaction before slug resolution and takes a transaction-scoped advisory lock derived from the tenant ID. Resolution, idempotency claim, Product mutation, audit/outbox staging, save, and commit occur while that lock is held. Concurrent distinct-key creates with the same base slug therefore persist the base and `-2` rather than racing to the same value.

## 13. Idempotency Ordering

Slug resolution occurs before the idempotency fingerprint on create and update. Deterministic product IDs and self-exclusion make same-key retries converge on the same payload and slug. The existing transaction method accepts the already-open slug transaction instead of opening a nested transaction.

## 14. Update Stability

A blank update slug preserves the stored slug. Renaming does not regenerate the public URL. An explicit slug edit remains allowed and is normalized, while the form warns that existing storefront links will stop resolving. Redirect/history support was not introduced.

## 15. Tenant Isolation

The slug query, advisory lock key, storefront lookup, index shape, and cache keys are tenant-scoped. A PostgreSQL integration regression confirms that two tenants may independently use the same slug. Live cross-tenant probing was not performed.

## 16. Database Safety

No migration or direct SQL business write was made. A unique index was deliberately not added because historical duplicate/null state was not certified or repaired. The application lock prevents new orchestrator races on PostgreSQL, but historical rows and unsupported direct writers remain residual risks until a governed duplicate audit and migration are approved.

## 17. Bilingual Identity Contract

The presentation fallback is bidirectional: Arabic requests prefer the Arabic field then the primary field; English/default requests prefer the primary field then Arabic. Missing or whitespace-only values resolve to the other language or an empty string, never `null`. No machine translation or duplicate-content requirement was introduced.

## 18. Storefront DTO Propagation

`ProductReadDto` now includes `DescriptionAr`; `StorefrontProductService.MapToDto` maps it from `ProductDocument`; and the Storefront TypeScript contract exposes it. Projection writers already carried `DescriptionAr`, so no persistence or projection schema change was required.

## 19. Runtime Localization

The production Storefront uses `useLocalizedContent().localize`, backed by `getLocalizedField`. That function now provides the fallback in both language directions for names and descriptions and rejects whitespace-only fallback values. The unused duplicate C# `ProductDisplayIdentity` helper and its tests were removed during independent review.

## 20. Product Form UX

Arabic name and description are optional progressive disclosures, auto-open when stored values exist, and explain the storefront fallback. Create mode suggests a sanitized slug from the primary name until the user edits the slug. Edit mode never derives a new slug from a renamed product. Blank slug submission becomes `null`.

## 21. Localization Audit

The certification audit now includes ProductForm, Product media, Product pixel override, Product variants, Product list, and Product workspace files. Missing Product/common/variant/pixel keys and six Arabic values that were still English were repaired. `node scripts/i18n-certification-audit.mjs` exits successfully.

## 22. Backend Verification

Final focused Release command:

`dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --filter "FullyQualifiedName~ProductSlugAuthorityTests|FullyQualifiedName~ProductAuthoringIdempotencyIntegrationTests|FullyQualifiedName~StorefrontProductIdentityMappingTests" --logger "console;verbosity=minimal"`

Result: **31 passed, 0 failed, 0 skipped**. Coverage includes normalization, deterministic fallback, suffixing, idempotent create, concurrent distinct-key create, mixed-case history, blank-slug rename stability, cross-tenant reuse, and bilingual DTO propagation.

## 23. Frontend Verification

`ProductForm.test.tsx`: **26 passed**. Storefront `useTranslation.test.ts`: **5 passed**. The Product form suite covers optional Arabic disclosure and create slug suggestion/null submission. The Storefront suite covers Arabic selection, both fallback directions, descriptions, null values, and whitespace-only values.

## 24. Build Gates

`PointOfSale.Web` production build: `PASS`. `PointOfSale.Storefront` TypeScript plus Vite production build: `PASS`. Both emit pre-existing bundle-size/dynamic-import warnings but no build error. The focused .NET Release test invocation compiled Core, Infrastructure, API, and Tests successfully.

## 25. EF Model Gate

`dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API/PointOfSale.API.csproj --context ApplicationDbContext --no-build --configuration Release` returned: `No changes have been made to the model since the last migration.` No migration or snapshot change exists in this worktree.

## 26. Broad Regression Status

The repository-wide backend run was stopped after approximately eight and a half minutes because it was taking too long. Before cancellation it displayed unrelated pre-existing failures in architecture-document assertions, SQLite/PostgreSQL-specific setup, missing historical artifacts, and unrelated payment/shipping areas. It produced no final count and is not claimed as a passing gate. It was not rerun.

## 27. Live Session Baseline

The existing headed Edge profile reached `http://localhost:5173/products`. `GET /api/auth/session` returned HTTP 200 and the expected AhramStore tenant ID. A read-only Product API baseline reported one Product. The create page was opened, but the user deferred UI certification before submission. No test Product was created and no business data was mutated.

## 28. Live Acceptance Matrix

| Gate | Result |
|---|---|
| Authenticated AhramStore session | PASS |
| Read-only Product baseline | PASS |
| Manual create without AI | NOT CERTIFIED |
| Server-derived nonblank slug | NOT CERTIFIED |
| Edit rename preserves slug | NOT CERTIFIED |
| English/Arabic identity rendering | NOT CERTIFIED |
| Storefront slug route and visibility | NOT CERTIFIED |
| API/UI/database reconciliation | NOT CERTIFIED |
| 1440x1000 responsive/RTL review | NOT CERTIFIED |
| 390x844 responsive/RTL review | NOT CERTIFIED |
| Runtime cross-tenant isolation probe | NOT CERTIFIED |
| No unintended business mutation | PASS |

## 29. Final Change Inventory

Production changes are confined to `ProductAuthoringOrchestrator.cs`, `ProductSlugAuthority.cs`, ProductForm, Product locale dictionaries, Storefront DTO mapping/types, and Storefront localization fallback. Tests cover the new backend and frontend contracts. The localization audit scope was expanded. No dependency, financial, inventory valuation, migration, or model snapshot file changed. `git diff --check` reports no whitespace errors; only working-copy LF-to-CRLF notices are emitted.

## 30. Final Verdict

**FAIL.** Code-level Wave 1 remediation is accepted: focused tests pass, frontend builds pass, localization is complete for the audited screens, EF model drift is absent, and no business data was changed. Certification remains failed rather than conditionally passed because the required live create/edit/storefront/API/database/responsive/tenant matrix was deferred. Re-run only section 28 against one isolated retained AhramStore Product to qualify for an overall `PASS`.
