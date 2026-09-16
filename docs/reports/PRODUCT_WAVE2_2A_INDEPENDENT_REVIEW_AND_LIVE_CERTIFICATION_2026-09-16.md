# PRODUCT WAVE 2 + 2A INDEPENDENT REVIEW AND LIVE CERTIFICATION (2026-09-16)

## 1. Executive Verdict

**PASS.** Variant persistence/deletion integrity (Finding 4), unique SKU generation and enforcement (Finding 2), Option Group Enter behavior (Finding 9), and Wave 2A deletion safety across variant, simple-product, and product paths are independently reviewed, remediated where defective, and live-certified on AhramStore in headed Microsoft Edge at desktop 1440x1000 and mobile 390x844, with UI ↔ API ↔ DB reconciliation on isolated test records. Focused backend regressions 45/45, frontend product suites 51/51, concurrency/idempotency/identity suites 36/36, broad product suite 180/181 (single pre-existing SQLite/Postgres harness failure, unrelated and evidence-backed), API Release build 0 errors, Web production build success, `git diff --check` clean, EF model in sync with migrations (no pending model changes), no migration, no historical rewrite, no direct SQL business writes, no commits.

## 2. Mandate and Constraints

Independent review, remediation, and live certification of Product Wave 2 + Wave 2A only, per the certification objective. No subagents or delegation were used. No commits were made. No direct SQL business writes were performed (all live mutations went through the UI or the authenticated API on isolated `WAVE2 CERT` records; direct database access was read-only `SELECT` plus `pg_indexes` inspection). No historical repair or backfill was performed. No Wave 3 or Wave 4 work was performed. Wave 1 Storefront identity/localization files in the worktree were not touched by this workstream. `PASS` is claimed only because desktop 1440x1000, mobile 390x844, real AhramStore browser scenarios, and UI ↔ API ↔ DB reconciliation were all executed and are all green. Credentials used for the live session are not recorded in source, logs, screenshots, evidence files, or this report.

## 3. Scope

Wave 2: (a) variant persistence and deletion integrity, (b) unique SKU generation plus server-side uniqueness enforcement, (c) Option Group Enter-key behavior. Wave 2A: variant and simple-product deletion safety against ten protected dependency families, including the variant-matrix save-omission path and direct orchestrator `Remove`. Explicitly out of scope and untouched: Wave 1 slug/identity/localization logic, Wave 3 (storefront pricing, AI loading, product-type simplification), Wave 4 (browser certification suite), financial/accounting/Treasury behavior, inventory valuation. Prior evidence consumed as input only: `PRODUCT_WAVE2_VARIANT_PERSISTENCE_SKU_OPTION_CERTIFICATION_2026-09-16.md` and `PRODUCT_WAVE2A_DELETION_SAFETY_CERTIFICATION_2026-09-16.md`, both of which ended at `CONDITIONAL PASS` pending exactly the live legs executed here.

## 4. Worktree Baseline and Wave 1 Boundary

`git status --short` at certification time shows 19 modified tracked files (Wave 1, Wave 2, Wave 2A mix) plus untracked Wave 2/2A additions (`ProductAuthoringVariantIdentityAuthority.cs`, `ProductVariantDeletionProtectionAuthority.cs`, `ProductAuthoringVariantIdentityAuthorityTests.cs`, `ProductDeletionSafety/`, `VariantOptionsBuilder.test.tsx`, prior Wave 1/2/2A reports) and workspace-local `.opencode-tmp/` test outputs. The Storefront Wave 1 files (`PointOfSale.Storefront/src/i18n/useTranslation.ts`, `PointOfSale.Storefront/src/types/product.ts`) were already dirty from other work and were not opened, edited, or verified by this workstream; no edit in this session touched any file outside `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs`, `PointOfSale.Tests/Integration/ProductAuthoringTransactionCompletenessIntegrationTests.cs`, and the temporary `.opencode-tmp/wave2-browser/` harness. `git diff --check` is clean. No commit was created.

## 5. Finding and Path Inventory

Wave 2 findings: Finding 2 (multi-select Generate SKU yields identical SKUs), Finding 4 (deleted variant Dark reappears after save alongside newcomer Mojahed), Finding 9 (Enter in Option Name submits the outer form). Wave 2A unsafe paths: variant-matrix save omission soft-deleting any omitted variant with zero history checks; direct orchestrator `Remove` bypassing product validation; variant `delete-selected` relying on a client-side stock confirm; option/value removal orphaning protected variants through the omission path. Legacy direct variant mutation endpoints (`ProductVariantsController` POST/PUT/DELETE) are deprecated and return 410. Unit delete retains its own purchase/sale/return usage check. All paths were re-inventoried live: legacy POST returned 410 in the live run, and the full delete-path table from the Wave 2A report was re-verified against current source with no new unguarded writer found.

## 6. Variant Architecture

`Product` plus `ProductVariant` (TenantId, ProductId FK, nullable SKU/Barcode, `AttributeCombinationKey`, `IsActive`, `SemanticLifecycleState`, `RowVersion`), `ProductVariantAttribute` (Variant FK, Name, Value, SortOrder), `ProductVariantStock` (Variant FK cascade, Warehouse restrict). Soft-delete base `IsDeleted` with global filters excluding deleted rows. The only live write path is `ProductsController` create/update through `ProductAuthoringCommandMapper` into `ProductAuthoringOrchestrator.AuthorAsync` (`ExecuteCreateAsync` / `ExecuteUpdateAsync` / `ExecuteArchiveOrRemoveAsync`); reads go through `GET /api/products/{id}` and `GET /api/products/{productId}/variants`. Delete is soft-delete (`IsDeleted=true, IsActive=false`); no `DbSet.Remove(ProductVariant)` exists in any live path. Product `Remove` cascades the flags to live variants; `Archive` (Deactivate) only retires. Frontend: `ProductVariantsTab.jsx` (matrix, drafts, payload), `VariantOptionsBuilder.jsx` (option groups), `VariantMatrixTable.jsx` (grid), `utils/productAuthoringVariants.js` (`variantId` passthrough).

## 7. Variant Identity

Canonical identity is `VariantId:Guid` with deterministic fallback to the canonical attribute-combination key (`ProductVariantAttributeCombinationKey.Compute`: order-independent, whitespace-collapsed, upper-normalized, SHA-256). Verified in `ResolveAuthoredExistingVariantIds` (Id first, key second) and `ResolveExistingAuthoredVariant` (matched-ids guard). SKU, label text, and array index are not identity. The live Finding 4 run proves the contract end to end: Plain and Medium kept their server IDs across the update while Mojahed was minted a new ID and Dark was soft-deleted rather than resurrected.

## 8. Save and Reconciliation Algorithm

Per save: load existing current-semantic-owner variants for the tenant and product excluding deleted, index by Id and canonical key, resolve each authored row (Id hit, else unmatched key hit), soft-delete omitted rows, field-update matched rows with attribute replacement only on real change, enforce a single default. Reconciliation order was not altered by Wave 2/2A; both waves add pre-mutation validation (identity conflicts, protected-history refusal) before the same mutate step. The four new `ProductAuthoringTransactionCompletenessIntegrationTests` regressions pin the failure modes: duplicate-SKU create returns Conflict without mutation, SKU-owned-by-another-variant returns Conflict without a second mutation, protected-variant-stock remove returns ValidationFailed without mutation, and omitting a protected variant blocks before any replacement is created.

## 9. Option Identity and Generated Matrix

Option groups and values carry frontend-local ids only; persisted identity is `(Name, Value)` pairs per variant canonicalized server-side, so reordered combinations hash identically and are rejected as duplicates rather than created. Cartesian generation is deterministic in group order with canonical keys and client-plus-server duplicate blocking. Rows orphaned by the user's own option edit are cleanup candidates resolved through omission and soft-delete for history-free variants, or refused with structured reasons for protected variants. The live run exercised exactly this: removing the Dark value and adding Mojahed produced precisely Plain, Medium, Mojahed in the API, the database live set, and both headed viewports.

## 10. SKU Authority

Nullable (max 100), tenant-scoped filtered unique index `IX_ProductVariants_SKU (TenantId, SKU) WHERE "IsDeleted" = false AND "SemanticLifecycleState" = 0 AND "SKU" IS NOT NULL`, confirmed in `ProductVariantConfiguration.cs:94-97`, the baseline migration, the model snapshot, and live via `pg_indexes` returning exactly `CREATE UNIQUE INDEX "IX_ProductVariants_SKU" ON public."ProductVariants" USING btree ("TenantId", "SKU") WHERE (("IsDeleted" = false) AND ("SemanticLifecycleState" = 0) AND ("SKU" IS NOT NULL))`. Unlimited NULLs, cross-tenant reuse allowed, Ordinal case contract. Editable with no immutability guard; history is safe through immutable order snapshots. `dotnet ef migrations has-pending-model-changes` reports no changes since the last migration, so no migration was created.

## 11. SKU Generation Fix

Root cause of Finding 2 was selection-position indexing with no dedupe, so patterns without a distinguishing token (or with normalizing collisions) produced identical candidates. The fix in `ProductVariantsTab.jsx` uses the stable matrix position for `{INDEX}` and dedupes every candidate case-insensitively with a deterministic separator-based `-2/-3` suffix; empty candidates stay empty and map to NULL. Live server proof: forcing all three isolated variants onto one SKU returned 409 `PRODUCT_SKU_CONFLICT` on `variants.sku` with zero mutation (API set before and after identical), and both headed viewports rendered three distinct persisted SKUs after reload.

## 12. SKU Concurrency

Final authority is the database unique index: genuine races that pass pre-commit checks still collide at commit with `23505`, classified to `PRODUCT_SKU_CONFLICT` 409 by `ProductAuthoringDatabaseConflictClassifier` and mapped identically for pre-commit conflicts. Pre-commit ownership queries are batched (variants plus products for SKU, plus units for barcode) with constant query count. The 36-test concurrency/idempotency/identity run is green, and the live duplicate-SKU attempt confirmed the failure is a structured 409 that leaves all three variants byte-identical rather than a 500 or a partial write.

## 13. SKU Editing and Ownership

Every variant SKU edit is ownership-checked against all other current variants and products of the tenant (self excluded by resolved id, atomically removed rows excluded) with intra-request duplicates rejected. Manual SKUs are never auto-renamed; conflicts return 409 for the user to resolve. Historical references are unaffected because order lines carry immutable SKU snapshots. The live duplicate test asserted the no-mutation half of this contract directly against AhramStore.

## 14. SKU Writer Inventory

All persisted-SKU writers resolve to the single server contract. Matrix UI builder (dedupe fix), authoring API (verbatim values validated pre-commit), clone/import authority (`ResolveUniqueSkuAsync` with `-N` regeneration plus ownership checks), default-variant alignment (`GenerateSafeSkuAsync`), and seeds/backfill all pass through orchestrator validation with the filtered unique index as final authority. No background SKU minter exists. A repo-wide SKU grep confirms readers (cart, checkout, feeds, analytics, wishlist, CommerceHub) only copy snapshots and never mint persisted variant SKUs outside this contract.

## 15. Option Group Form Semantics

The workspace is a single outer product form; the option builder is an inline `div` with no nested form. The Option Name input previously had no key handler, so Enter triggered implicit outer-form submission. The fix in `VariantOptionsBuilder.jsx` cancels Enter with `preventDefault()` and advances focus to that group's value input (blur fallback); all builder buttons are `type="button"`, and value-Enter plus explicit Save are untouched. Live proof in both viewports: Enter in the Option Name input kept the URL identical and moved focus to the `Type a value and press Enter` input, and Enter in the value input added the `W2Probe` chip with no navigation and no save.

## 16. Frontend and Backend Contract

Payload rows carry authoritative `variantId`; deletions are omission; `variants:null` means do not sync while `[]` is blocked client-side. The payload now contains exactly the submitted non-orphaned rows with surviving ids and `null` for newcomers. Authoring errors reuse the established `errors[]` contract with allowlisted `VARIANT_*` codes so the form surfaces real 400 messages, while product delete uses `errorCode/messageEn/messageAr/reasons[]` rendered as a bullet list with a Deactivate alternative. Live evidence matches: the update payload preserved Plain/Medium ids, Mojahed arrived with `null`, Dark was omitted and soft-deleted, and the duplicate-SKU refusal arrived as structured `Conflict` errors rather than a generic failure.

## 17. Deletion Safety and Protected Dependencies

Wave 2A closes the matrix-omission and direct-`Remove` holes with one shared rulebook (`ProductVariantDeletionProtectionAuthority.GetBlockersAsync`, stable order): current stock including reserved/damaged, purchases, sales with COGS snapshots, returns with COGS reversal, transfers, movements audit, storefront orders, CommerceHub orders, open reservations, and published marketplace listings. POS carts, wishlists, affiliate links, and promotions are deliberately non-blocking transients owned by their own workflows. Live read-only proof on an existing business product returned `canDelete:false` with `PRODUCT_HAS_STOCK` and full bilingual messaging plus a structured `reasons[]` entry, while the isolated clean variant returned `canDelete:true` with empty reasons. Refusal always precedes mutation: every 2A refusal test re-reads the protected rows and asserts them unchanged.

## 18. Simple Product and Variant Model

Simple equals `Product(HasVariants=false)` plus exactly one default variant with the default `ProductUnit` as price authority and `ProductVariantStock` as inventory authority; multi-variant equals one product with N independent sellable variant rows. Both levels evaluate the same ten-family authority over the same underlying variant row, so product deletion cannot bypass variant history. Sibling precision holds: history on one variant never blocks deletion of an unused sibling. The live isolated product exercised the multi-variant half; the historic `PRODUCT_HAS_STOCK` probe exercised the product-level half against real business state without mutating it.

## 19. Server Authority and Error Contracts

Three enforcement layers: controller and bulk pre-checks (`CanDeleteProductAsync` including the new families), orchestrator admission guards (`Remove` pre-validation, per-omitted-variant validation on update), and in-transaction re-validation before any `IsDeleted` mutation, mapped to `ValidationFailed` with one error per blocker. The new read-only `GET /products/{id}/variants/{variantId}/can-delete` endpoint is fail-closed and was exercised live. This session remediated two genuine error-contract defects found by independent review: `ExecuteCreateAsync` identity checks now run inside the `ProductAuthoringVariantIdentityConflictException` boundary (409 instead of a possible 500), and `ExecuteArchiveOrRemoveAsync` protected-history precheck now runs inside the `ProductAuthoringProtectedHistoryException` boundary (structured `ValidationFailed` instead of an escape), verified by code read at lines 286-337 and 559-639 with reconciliation untouched.

## 20. Tenant Isolation

All ten protection checks scope through the authoritative parent tenant (direct `TenantId` or invoice/order/store-tenant parent, with CommerceHub scoping through `SubOrder.StoreId`). Regression tests pin foreign-tenant variant invisibility (`VARIANT_NOT_FOUND` rather than a cross-tenant verdict leak), foreign-history non-blocking, and other-store CommerceHub non-blocking. The live run operated entirely inside the AhramStore tenant resolved from the authenticated session; the isolated product never appeared outside that tenant's catalog, and the historic probe returned only that tenant's own blocker.

## 21. Concurrency and Idempotency Bounds

Validation plus mutation is not fully serializable, and the residual millisecond race between the in-transaction re-check and commit under `READ COMMITTED` is documented rather than hand-waved; the window is narrowed to one transaction and every observed race fails closed. Mitigations in place and green: idempotency-key admission with exact replays, `If-Match` expected-version staleness rejection, in-transaction re-validation, and fail-closed matrix preflight. One session observation is recorded as evidence for the bound: two headed UI logins rotated the POS session and invalidated the earlier API cookie jar, so the harness re-authenticates before post-browser API calls; no live step ever bypassed versioning or idempotency (every live POST/PUT/DELETE carried an `Idempotency-Key`, every PUT/DELETE carried `If-Match`).

## 22. Implementation Review

Production changes reviewed hunk by hunk: shared identity authority (pure, no I/O), shared deletion-protection authority (single rulebook), orchestrator validation plus typed refusals plus in-transaction re-checks, `ValidationService`/`IValidationService` extension preserving legacy codes/messages/order, `ProductsController` delete/can-delete/variant-can-delete boundaries with raced-refusal re-validation, matrix preflight with fail-closed omission checks, form error-code allowlisting, modal structured reasons, and En/Ar strings. Four regression tests were added in `ProductAuthoringTransactionCompletenessIntegrationTests.cs`. No second deletion framework, no Wave 1/3/4 or Treasury/accounting changes, no migration, no historical rewrite, no direct SQL writes. Temporary harness files live only under `.opencode-tmp/wave2-browser/` and are not part of the product diff.

## 23. Automated Tests

Focused regressions 45/45: `ProductAuthoringTransactionCompletenessIntegrationTests` (including the 4 new error-contract tests), `ProductAuthoringVariantIdentityAuthorityTests`, `ProductVariantDeletionSafetyTests`. Broad product regression 180/181 with the single failure being the known pre-existing `BulkDeleteValidationAuthorityTests.CanDeleteProductAsync_IgnoresForeignTenantTransactionReferences` SQLite harness error (`near "~": syntax error` from a Postgres-only check constraint, untouched code, fails before reaching product logic). Frontend product suites 51/51 (`ProductVariantsTab`, `VariantOptionsBuilder`, `ProductForm`). Concurrency, idempotency, slug, and storefront-identity suites 36/36. Every new test was executed against pre-fix behavior and fails there except pure allow-cases that guard against over-blocking; all assert persisted invariants rather than status-code or DOM trivia.

## 24. Build and Static Verification

`dotnet build PointOfSale.API -c Release` succeeds with 0 warnings and 0 errors. Web `npm run build` succeeds (`built in 21.79s`; only the pre-existing chunk-size notice). `git diff --check` is clean. `dotnet ef migrations has-pending-model-changes` reports no model changes since the last migration. The rebuilt Release API (process 3464) serves `GET /swagger/index.html` with 200 while aggregate `GET /health` returns 503; endpoint availability was assessed independently of the aggregate health signal, and every live product endpoint used here answered with its specified contract code. Filtered SKU/barcode/combination indexes were re-confirmed in configuration, snapshot, baseline migration, and the live database.

## 25. Live Environment and Method

Live API `http://localhost:5010` (Release build with current fixes, process 3464), admin web `http://localhost:5173`, plus Node apps on 5174/5175/3000, PostgreSQL and Redis on 192.168.1.5 reachable. Headed Edge via Playwright `msedge` channel with a private test context (the user's normal Edge profile was never attached or disturbed). Temporary C# harness under `.opencode-tmp/wave2-browser/` drove authenticated API calls (manual Secure-cookie jar over `http://localhost`, CSRF header handling, per-request idempotency keys, `If-Match` versions) and two headed viewports, then re-authenticated after the UI logins rotated the session. All mutations used one isolated product family (`WAVE2 CERT` prefix with unique timestamp and SKU base); existing business data was only read (product list, historic can-delete probe) and never written. Evidence JSON, three full-page screenshots, and console/network captures were retained in the harness output directory.

## 26. Live Browser Certification Desktop 1440x1000

Headed Edge, viewport 1440x1000. Real login through `/login` to the AhramStore catalog, `/products` table rendered, isolated product workspace opened at `/products/{id}/edit?focus=variants`. Mojahed visible and Dark absent after the Finding 4 update and a full reload, with zero failed API responses and zero unexpected console errors. Option Name Enter kept the URL identical and advanced focus to the value input; value Enter added the `W2Probe` chip with no navigation and no save. Full-page screenshot `wave2-desktop.png` (660956 bytes) retained. Desktop verdict: PASS.

## 27. Live Browser Certification Mobile 390x844

Headed Edge, viewport 390x844, same isolated product and same assertions as desktop. Catalog table rendered, Mojahed visible, Dark absent, Option Name Enter produced no submit with focus advancing to the value input, value Enter added its chip without navigation, zero failed API responses. Full-page screenshot `wave2-mobile.png` (476564 bytes) retained with no horizontal page overflow. Mobile verdict: PASS.

## 28. Localization and Responsive Sweep

Arabic RTL headed check at desktop dimensions with `ar-EG` locale: document direction resolved to `rtl`, the products table rendered, no horizontal overflow (`scrollWidth` within client width), zero failed API responses, screenshot `wave2-ar-desktop.png` retained. Change surface is behavior-only with locale-keyed messages (Wave 2A keys verified in both `en.json` and `ar.json`), direction-aware utilities preserved, and both primary viewports plus the RTL pass show no layout breakage. Sweep verdict: PASS.

## 29. Live API and DB Reconciliation

Isolated run `20260916-183022`, product `67de42c7-7139-3aac-97a6-a2d0fa72fc59`. Create returned 201 with three variants; update removing Dark and adding Mojahed returned 200 with exactly Plain, Medium, Mojahed. Forced duplicate SKU returned 409 `PRODUCT_SKU_CONFLICT` on `variants.sku` with the pre/post variant sets identical. Legacy variant POST returned 410. Read-only database SELECT returned four rows for the product: the three live SKUs plus Dark with `IsDeleted=true, IsActive=false`, proving soft-delete rather than resurrection or hard removal; the live database set equals the API set, which equals both headed UI sets. Historic business product probed read-only returned the bilingual `PRODUCT_HAS_STOCK` refusal with structured reasons. Cleanup DELETE of the isolated product returned 204, and the sweep removed three earlier isolated leftovers from rotated-session runs, leaving no test residue.

## 30. Final Verdict

**PASS.** Wave 2 variant persistence, SKU integrity, and Option Enter behavior plus Wave 2A deletion safety are independently reviewed, defect-remediated, and live-certified: 45/45 focused, 51/51 frontend, 36/36 concurrency/identity, 180/181 broad with only the documented pre-existing harness failure; Release and Web builds green with no migration and no model drift; headed Edge desktop 1440x1000, mobile 390x844, and Arabic RTL all green on real AhramStore flows; UI, API, and database agree exactly on the isolated lifecycle with protected history refused bilingually and clean deletes succeeding. No FAIL condition from either wave's criteria was triggered.
