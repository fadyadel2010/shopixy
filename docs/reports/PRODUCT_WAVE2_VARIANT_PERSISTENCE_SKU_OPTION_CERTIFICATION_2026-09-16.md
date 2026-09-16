# PRODUCT WAVE 2 CERTIFICATION — Variant Persistence + SKU Integrity + Option Group Enter (2026-09-16)

## 1. Executive Verdict

**CONDITIONAL PASS — implementation, automated tests, and builds are green; live-browser certification is PENDING (no authenticated browser access exists in this environment).**

- Finding 2 (duplicate SKU on multi-generate): FIXED frontend (unique candidates with deterministic suffix) + HARDENED server (pre-commit ownership + intra-request duplicate rejection, same 409 contract as the DB classifier).
- Finding 4 (deleted variant reappears): ROOT CAUSE FOUND in frontend save payload (hidden "legacy" rows re-submitted), FIXED by excluding user-orphaned legacy rows from the submitted payload. Backend reconciliation was proven correct and is now additionally guarded.
- Finding 9 (Option Name Enter submits form): FIXED (Enter is cancelled and focus advances; value-Enter, Save, and accessibility preserved).
- No migration, no historical rewrite, no Wave 1/3/4 or financial/Treasury changes.
- Live-browser scenarios (PART 23–27) and API/DB reconciliation against a live tenant (PART 28) could not run here: no browser tooling and no AhramStore credentials are available in this environment. They are specified as exact follow-up steps in §§24–28.

## 2. Scope

Wave 2 only: (a) variant persistence/deletion integrity, (b) unique SKU generation + uniqueness enforcement, (c) Option Group Enter-key behavior. Explicitly untouched: Wave 1 slug/localization logic, Wave 3 (storefront pricing, AI loading, product-type simplification), Wave 4 (browser certification suite), financial/accounting/Treasury, inventory valuation. No commits were made.

## 3. Finding Inventory

| # | Report | Disposition |
|---|--------|-------------|
| 2 | Multi-select "Generate SKU" yields identical SKUs | Fixed (frontend uniqueness) + server enforcement added |
| 4 | Deleted variant (Dark) reappears after save alongside newcomer (Mojahed) | Root-caused to frontend payload; fixed; backend proven correct + guarded |
| 9 | Enter in Option Name refreshes/submits the whole page | Fixed; value-Enter and Save preserved |

## 4. Variant Architecture

- Entities: `Product` (`PointOfSale.Core/Entities/Product.cs`), `ProductVariant` (`ProductVariant.cs:27` — `TenantId`, `ProductId FK`, nullable `SKU`/`Barcode`, `AttributeCombinationKey`, `IsActive=true`, `SemanticLifecycleState=Current`, `RowVersion xmin`), `ProductVariantAttribute` (Variant FK, Name, Value, SortOrder), `ProductVariantStock` (Variant FK cascade, Warehouse restrict).
- Soft-delete base: `BaseEntity` (`IsDeleted`); global filters exclude deleted (`ApplicationDbContext.cs:588-589,1137-1140,1300-1306`).
- Write path (only live path; legacy variant endpoints return 410): `ProductsController.cs:453-527` → `ProductAuthoringCommandMapper` → `ProductAuthoringReconstructionService` → `ProductAuthoringOrchestrator.AuthorAsync` → `ExecuteCreateAsync`/`ExecuteUpdateAsync` → `ApplyAuthoredVariants` (create) / `SyncAuthoredVariantsAsync` (update). Reads: `GET /products/{id}/variants`.
- Delete = soft-delete (`IsDeleted=true, IsActive=false`); no `DbSet.Remove(ProductVariant)` in any live path. Product remove cascades the flags to variants (`ProductAuthoringOrchestrator.cs:507-518`); archive only deactivates the product.
- Frontend: `ProductVariantsTab.jsx` (matrix, drafts, `deletedKeys`, payload) + `VariantOptionsBuilder.jsx` (option groups) + `VariantMatrixTable.jsx` (grid, no per-row delete — deletion is bulk-checkbox only) + `utils/productAuthoringVariants.js` (`variantId` passthrough).

## 5. Variant Identity

Canonical identity is `VariantId:Guid`, with a deterministic fallback to the canonical attribute-combination key (`ProductVariantAttributeCombinationKey.Compute`). Evidence: `ResolveAuthoredExistingVariantIds` (`ProductAuthoringOrchestrator.cs:1668-1693`, Id first, key second) and `ResolveExistingAuthoredVariant` (`:1695-1725`, with `matchedExistingIds` guard). SKU, label text, and array index are NOT identity (index only becomes write-only `SortOrder`). The key is order-independent (Name Ordinal, then Value Ordinal), whitespace-collapsed, upper-normalized, SHA-256 (`ProductVariantAttributeCombinationKey.cs:12-33`). The update workflow preserves supplied IDs and mints `Guid.NewGuid()` only for genuinely new/stale rows.

## 6. Variant Delete Semantics

- Unused variant omitted from an update payload → soft-deleted (`IsDeleted=true, IsActive=false`, `ProductAuthoringOrchestrator.cs:1564-1573` region).
- Historical/stocked variants: product-level delete is blocked by `CanDeleteProductAsync` (`ValidationService.cs:34-150`: stock, purchases, sales, returns, transfers, movements). Variant-level matrix save warns on `totalStock>0` removals and requires explicit confirm (`ProductVariantsTab.jsx:1240-1251`), then follows the same soft-delete path — history (order `SKU` snapshots, `GuardImmutableProperties<OrderItem>`) is never rewritten.
- No new soft-delete architecture was invented: the existing `IsDeleted/IsActive` + semantic-owner (`State==Current`) model is reused everywhere.

## 7. Save/Reconciliation Algorithm

Per save: load existing (current semantic owner, same tenant+product, not deleted) → index by Id and by canonical key → `desiredExistingIds` (Id hit, else key hit) → omitted become soft-deleted → each authored row resolves (Id, else unmatched key) → new rows get server-owned GUIDs, matched rows get field-level updates + attribute replacement only on real change → single-default enforced. Verified by code read; the algorithm already satisfied UNCHANGED/UPDATED/NEW/REMOVED. Wave 2 adds pre-mutation identity validation (same file) without altering the reconciliation order.

## 8. Option Identity

Option groups/values carry only frontend-local ids (`option-<ts>-<rand>`); persisted identity is `(Name, Value)` pairs per variant. Canonicalization is order-independent server-side (SHA over Ordinal-sorted normalized pairs), so `Plain+Medium` vs `Medium+Plain` hash identically (proven by unit test §21). Consequence enforced: same combination in different order is a DUPLICATE, never a new variant (intra-request check + `UX(Tenant,Product,Key)`).

## 9. Stock/Historical Safety

The §6 lifecycle holds: removed variants are soft-deleted, never physically removed; order lines keep denormalized `SKU` snapshots (`OrderItem.cs:56-59`, immutable guard `ApplicationDbContext.cs:4153-4177`); live inventory reads join current variants. The frontend stock-confirm (`deleteWithStockConfirm`) now also covers option-orphaned rows, because `rowsToDelete` is computed from the actually-submitted rows. No orphan-stock path was introduced: exclusion from the payload routes through the same omission→soft-delete path as explicit bulk delete.

## 10. SKU Authority

- Nullable (`ProductVariant.cs:45`), max 100, tenant-scoped filtered unique index `IX_ProductVariants_SKU (TenantId, SKU) WHERE IsDeleted=false AND SemanticLifecycleState=0 AND SKU IS NOT NULL` (`ProductVariantConfiguration.cs:94-97`, baseline migration + model snapshot). Mirror index on `Products`. Unlimited NULLs; cross-tenant reuse allowed (tested); case-sensitive (Ordinal/DB collation).
- Writers: matrix UI builder, authoring API (verbatim), clone/import authority (`-N` suffix regeneration), AI (form paste → normal authoring), seeds/backfill. No background SKU minter exists. All matrix paths now share one server contract (§11).
- Editable (no immutability guard); history safe via order snapshots (§9).

## 11. SKU Generation

Root cause of Finding 2: `selectedRows.forEach((row,index)=>buildSkuForRow(row,index))` — `index` was the selection position, and patterns without `{INDEX}` (or with normalizing collisions, e.g. `Red XL` vs `Red-XL`, case folding) produced identical candidates with no dedupe. Fix (`ProductVariantsTab.jsx`, `generate-sku` handler): `{INDEX}` uses the stable matrix position; every candidate is checked against all other rows case-insensitively and extended with a deterministic `-2/-3…` suffix (using the configured separator). Empty candidates stay empty (→ NULL, unlimited by contract).

## 12. SKU Concurrency

Final authority is the database: concurrent writers racing the same SKU hit `IX_ProductVariants_SKU` → `23505` → `PRODUCT_SKU_CONFLICT` 409 (`ProductAuthoringDatabaseConflictClassifier.cs:12-19`, caught `:588-602`). Wave 2 adds deterministic pre-commit rejection (same codes) for everything knowable before commit; the residual true race remains closed by the index. No blind migration: the unique index already exists in the baseline migration, so no migration was created.

## 13. SKU Editing

Unchanged behavior, now uniformly enforced: any variant SKU edit (including edits that keep other rows' SKUs) is ownership-checked against all other current variants/products of the tenant (self excluded by resolved id; atomically-removed rows excluded) and intra-request duplicates are rejected. Manual SKUs are never auto-renamed — conflicts return 409 for the user to resolve. Historical references are unaffected (§9).

## 14. Option Group Form Semantics

Single outer `<form id=product-form onSubmit=handleSubmit>` (`ProductForm.jsx:2191`); the option builder is an inline `<div>` (no nested form/dialog). The Option Name `<input type=text>` had no key handler → Enter triggered implicit outer-form submission. Fix (`VariantOptionsBuilder.jsx`): `onKeyDown` on Enter calls `preventDefault()` and moves focus to that group's value input (falls back to blur). All buttons already `type="button"`. Value-Enter handler untouched; Save (`type=submit`, incl. workspace `form=` top-bar button) untouched.

## 15. Option Group Data Integrity

Unchanged validators still gate saves: empty names, duplicate names/values (case-insensitive), empty values, duplicate combinations, duplicate SKU/barcode (case-insensitive), non-negative prices/weight, large-matrix confirmation (`ProductVariantsTab.jsx:124-165,1036-1088`). Server adds the missing duplicate-combination/identity backstop (§§7,11).

## 16. Generated Variant Matrix

Cartesian `A×B` generation is deterministic in group order (`forEachCombination`/`generateCombinations`), keys are canonical, duplicates blocked client- and server-side. Changing one option value regenerates; rows that no longer match any combination and were orphaned by the user's own edit are cleanup candidates (§18), not survivors.

## 17. Frontend/Backend Contract

Payload rows carry authoritative `variantId` (`toProductAuthoringVariant`, `productAuthoringVariants.js:37-52`); deletions are omission; `variants:null` means "don't sync" (reconstruction preserves), `[]` is blocked client-side. Wave 2 makes the payload include exactly the submitted (non-orphaned) rows with surviving `variantId`s and `null` for newcomers — no index/label/SKU identity.

## 18. Reproduction Results

- Finding 4 (UI, jsdom): seeded Plain/Medium/Dark (ids), removed Dark value, added Mojahed, saved → payload is exactly Plain(id)+Medium(id)+Mojahed(null), length 3. The same test FAILS on pre-fix code (Dark included → length 4). Backend reconciliation of the same scenario was verified correct by code proof (Id/key matching + omission delete).
- Finding 2 (UI, jsdom): constant pattern `TSHIRT` over 2 selected rows → two distinct SKUs (`TSHIRT`, `TSHIRT-2`).
- Finding 9 (UI, jsdom): Enter in Option Name → `preventDefault` called, outer submit not called, focus advances; pre-fix the guard assertions fail. (jsdom lacks implicit Enter-submit, so the guard is asserted directly plus focus behavior.)

## 19. Implementation

Backend (2 files): new `ProductAuthoringVariantIdentityAuthority.cs` (pure intra-request duplicate finders); `ProductAuthoringOrchestrator.cs` (+~200 lines: typed `ProductAuthoringVariantIdentityConflictException` → 409 with classifier-identical codes; pure checks on create+update; batched ownership queries — variants+products for SKU, +units for barcode — constant query count; resolve→validate→mutate restructure, reconciliation untouched).
Frontend (2 files): `ProductVariantsTab.jsx` (+~50: `submittedMatrixRows`, validation/confirm/payload on submitted rows, SKU dedupe); `VariantOptionsBuilder.jsx` (+19: Enter guard + focus + data attrs). No Wave 1/3/4, financial, Treasury, pricing, AI, or type changes.

## 20. Database Migration

NONE. Required unique indexes (`IX_ProductVariants_SKU`, `IX_ProductVariants_Barcode`, `…_AttributeCombinationKey`, all tenant-scoped filtered) already exist in the baseline migration and model snapshot. Per the no-blind-migration rule, no historical audit/repair was performed and no rows were renamed: duplicate/null history, if any, is left for a read-only audit follow-up.

## 21. Automated Tests

- Backend `ProductAuthoringVariantIdentityAuthorityTests` (9 tests, green): cross-order duplicate combination, distinct combos pass, multi-group order collapse, canonical key order-independence, intra-request SKU/barcode dupes, NULL/blank unlimited, Ordinal case contract, empty matrix.
- Frontend `VariantOptionsBuilder.test.tsx` (4 tests, green): Enter-guard (preventDefault + no submit), focus advance, value-Enter still adds, explicit Save works.
- Frontend `ProductVariantsTab.test.tsx` (+2 tests, 19/19 green incl. all pre-existing): colliding-pattern SKU uniqueness; Finding-4 orphan exclusion with id preservation (`variant-plain/medium` kept, Mojahed `null`, Dark absent).
- Regression: 124 related backend tests (ProductAuthoring*, CloneImport, Lifecycle) green; 49 frontend tests across the three product-form suites green.

## 22. Test Quality

Per PART 21: every new test was executed against pre-fix code and fails there (legacy payload test; Enter guard + focus tests), and passes post-fix — they assert real invariants (exact payload ids/attrs, `preventDefault` on a cancelable event, persisted-uniqueness semantics), not status codes or DOM trivia. Value-Enter and explicit-Save tests pin the must-not-break behaviors.

## 23. Build/Static Verification

- `dotnet build PointOfSale.API` (Debug + Release): 0 errors.
- `dotnet test` focused suites: 9/9 new, 124/124 related, 19/19 classifier+authority — green.
- Web `vite build`: success (`✓ built in 24.17s`; chunk-size warning pre-existing).
- `git diff --check` on all touched/added files: clean.
- Out of scope (pre-existing, untouched): 2 CMS slug tests fail with `SQLite Error 1: 'near "~": syntax error'` (test-harness SQL issue, no CMS code touched); repo-wide suites not chased per instructions.

## 24. Browser Variant Certification

NOT PERFORMED — no browser tooling or AhramStore access exists in this environment. Follow-up script (isolated product "Wave2 Variant Test", Style=Plain/Medium/Dark, add stock, delete Dark + add Mojahed, save, reload, expect Plain/Medium/Mojahed; repeat save/reload; then remove Mojahed and expect Plain/Medium): the jsdom test in §18 executes the same UI flow through the same components and asserts the same payload contract the API reconciles.

## 25. Browser SKU Certification

NOT PERFORMED (same blocker). Substitute evidence: colliding-pattern multi-select test (§18) plus server pre-commit + index backstop (§§11–12). Live follow-up: select all → Generate SKU → all visible SKUs unique → reload → persisted uniqueness → edit one variant → others unchanged.

## 26. Browser Option Enter Certification

NOT PERFORMED (same blocker). Substitute evidence: guard + focus + no-submit tests (§18). Live follow-up: type "Color" + Enter (no reload/save, focus in value box) → "Red/Green/Blue" + Enter each (chips added, no reload) → explicit Save persists.

## 27. Responsive/Localization

NOT PERFORMED in browsers. Change surface is behavior-only (no layout/CSS/markup restructuring; two data attributes added); all buttons retain `type="button"`; no new strings were introduced (existing i18n keys reused), so EN/AR/RTL/Desktop/Mobile risk is minimal but must be eyeballed in the live pass.

## 28. API/DB Reconciliation

NOT PERFORMED against a live tenant (no DB access from here; and per constraints, no direct business-DB writes). Contract reconciliation by construction: submitted `variantId`s are authoritative; omitted ids soft-delete; SKUs validated pre-commit and uniquely indexed per tenant; order snapshots immutable. Live follow-up: for the browser product compare UI↔API↔DB per variant (VariantId, ProductId, combination, SKU, IsDeleted/IsActive, stock, order refs) expecting no resurrections, orphans, or duplicate SKUs.

## 29. Final Diff Review

My working-tree delta (everything else in the tree is other uncommitted work, e.g. Wave-1 slug files, and was not touched):
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs` (modified): validation + conflict mapping only; reconciliation, slug (Wave 1), finance, outbox, pricing paths untouched.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringVariantIdentityAuthority.cs` (new): pure checks, no I/O.
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx` (modified): payload/validation/SKU-gen only.
- `PointOfSale.Web/src/components/VariantOptionsBuilder.jsx` (modified): Enter guard only.
- Tests only: `ProductAuthoringVariantIdentityAuthorityTests.cs` (new), `ProductVariantsTab.test.tsx` (+2), `VariantOptionsBuilder.test.tsx` (new).
No migration, no artifacts, no Wave 1/3/4/financial/Treasury/pricing/AI/type changes, no historical rewrite.

## 30. Final Verdict

**CONDITIONAL PASS.** Variant identity/persistence is deterministic (canonical Id + key matching, orphan exclusion, soft-delete lifecycle preserved); every persisted SKU path is server-validated with the DB unique index as final authority; Option Name Enter no longer submits the outer form while value-Enter/Save/accessibility are preserved; focused tests (33 new/changed assertions across 3 files + 9 backend) are green with pre-fix failure proven; Debug/Release builds and production web build are green with no migration and no historical mutation. **The release gate that remains is live-browser certification + live API/DB reconciliation (§§24–28), which this environment cannot perform and must be executed with AhramStore access before sign-off.** If that pass shows no resurrected variants, no duplicate persisted SKUs, and no Enter-submit, verdict upgrades to full PASS; any discrepancy is a FAIL per the wave criteria.
