# PRODUCT WAVE 4 INDEPENDENT REVIEW AND DELETE UX CERTIFICATION (2026-09-17)

Date (UTC): 2026-09-17
Scope: Independent review and certification of Product Wave 4, remediation of protected-variant deletion feedback without weakening deletion safety, and live AhramStore/API/DB certification.
Authority: source code, automated tests, builds, static gates, and live headed-Edge evidence. Prior reports were treated as evidence, not authority.
Commit: none; changes remain uncommitted in the working tree.
This report contains exactly 30 numbered sections.

## 1. Executive Verdict

PASS. Product Wave 4 was independently reviewed, remediated where the deletion UX was defective, regression-tested, built, and live-certified on AhramStore. Required behavior holds end to end: protected delete blocks, explains the authoritative reason, and performs no mutation; clean delete succeeds. Live headed-Edge proof covers English desktop, Arabic RTL desktop, mobile English and Arabic, actual can-delete and PUT traffic, SQL fingerprints before and after blocked saves, SKU collision precision, naming and SKU generation, responsive layout, API and DB reconciliation, and application-boundary cleanup. Automated gates are green and no commit was made.

## 2. Mandate and Constraints

Independent review, remediation, and live certification only. Constraints observed: no subagents/delegation, no commit, no SQL deletion, no historical/financial/Treasury/accounting changes. No migration, no backfill, no historical rewrite, no direct SQL business write, no order/payment/stock mutation outside isolated test products, and no Treasury/GL/cash-flow/settlement/inventory-valuation change. SQL access was read-only SELECT plus pg fingerprint SELECTs. Live mutations used authenticated application and API boundaries against uniquely named isolated products only. Credentials were used only in process memory and are absent from source, evidence, screenshots, console output retained in files, and this report.

## 3. Scope

In scope is Wave 4 only: variant-editor UX and identity clarity, plus the reported protected-variant deletion-feedback defect. Specifically: ProductVariantsTab matrix preflight presentation, VariantMediaPicker localization and accessibility, ProductForm precise 409 identity-conflict surfacing, symmetric English/Arabic keys, progressive disclosure behavior, deterministic variant-name derivation, and contextual generated SKUs. Out of scope and not redesigned: Wave 1 slug authority, Wave 2 persistence and SKU authority, Wave 2A deletion-protection rulebook, Wave 3 pricing/AI/product-type authority, financial/accounting/Treasury behavior, checkout economics, and historical data repair.

## 4. Worktree Baseline and Preservation

At certification time the worktree contains 28 tracked modifications plus extensive prior-wave/unrelated untracked artifacts; they were preserved and classified rather than cleaned. The API was running at http://localhost:5010 and Swagger returned HTTP 200. The Web app was reachable at http://localhost:5173 with HTTP 200. The existing API process was PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe; whether it contained the latest source changes was unverified, so the Debug process with PID 17068 was stopped and replaced by a Release build launched from verified output with PID 6632 and Swagger 200 before live certification. WAVE2_EMAIL and WAVE2_PASSWORD environment variables were missing from the inherited process environment; a legitimate AhramStore Owner login was later supplied by the user and injected only into the harness process memory via character-code construction, never written to source, evidence, or this report.

## 5. Prior Evidence Assessment

Two Muse artifacts were consumed as evidence, not authority: docs/reports/PRODUCT_WAVE4_DISCOVERY_2026-09-16.md and docs/reports/PRODUCT_WAVE4_VARIANT_EDITOR_UX_IDENTITY_CERTIFICATION_2026-09-16.md. Their structural findings were re-derived from source: frontend-only feasibility, no persisted variant Name column, derived display names, frontend-suggests/server-validates SKU contract, progressive disclosure grouping, and approximately 110 missing symmetric keys. Their PASS claim was not accepted until independent code review, strengthened regressions, builds, static gates, and fresh live AhramStore/API/DB evidence were completed.

## 6. Deletion Flow and Server Authority

The deletion flow was traced in source and then proved live. Matrix omission calls GET /products/{productId}/variants/{variantId}/can-delete. The API returns structured canDelete, errorCode, messageEn, messageAr, and reasons with code/messageEn/messageAr entries. ProductVariantDeletionProtectionAuthority remains fail-closed. The matrix preflight aborts before PUT when blocked, so a blocked omission never reaches the authoring PUT. A clean variant returns canDelete true with empty reasons and proceeds to PUT. This authority was not weakened: controller, ValidationService, orchestrator admission, and in-transaction revalidation behavior are unchanged.

## 7. Protected-Variant Presentation Defect

Independent review found one presentation defect in code and tests, not a missing authority. The authoritative reason existed in the can-delete response, but it was rendered in a non-live, non-focusable banner far above the bottom Save controls. The prior test only checked DOM text, so it could not prove that a user deleting a protected variant near the bottom Save bar would see, understand, or be moved to the reason. That gap is why protected deletion feedback required remediation even though blocking itself worked.

## 8. Frontend Remediation

PointOfSale.Web/src/components/ProductVariantsTab.jsx was remediated minimally. An errorRef was added with focus-on-error behavior. A nearby role="alert" and tabIndex={-1} matrix-save banner with id variant-matrix-error was added next to the Save Matrix controls. A localized heading variants.matrixSaveFailed was added. The protected-variant label now derives the display name from attributes and includes SKU, for example Style: Dark (STYLE-DARK), instead of a bare row identifier. No deletion-safety logic was relaxed; the change only makes the existing block visible, nearby, announced, and focused.

## 9. Localization Remediation

Symmetric keys were added: EN variants.matrixSaveFailed is Variant matrix was not saved. AR variants.matrixSaveFailed is لم يتم حفظ مصفوفة المتغيرات. VariantMediaPicker media-load failure was localized instead of leaking raw server English. Existing Wave 4 symmetric variant keys were retained. Technical tokens, placeholders, persisted SKU values, brand names, and identifiers remain English by design. Arabic protected-delete output was verified live to contain the server Arabic reasons with no English leakage.

## 10. Accessibility Remediation

The matrix-save failure surface is now a live region with role="alert", focusable via tabIndex -1, and programmatically focused on error. VariantMediaPicker exposes role="dialog", aria-modal, a labelled title, an accessible close label, and role="alert" for load failure. The Advanced disclosure retains a real button with aria-expanded and aria-controls behavior. Live checks assert document.activeElement equals the matrix alert after both protected-delete blocks and after the precise SKU-collision error.

## 11. Backend Protection Integrity

Deletion safety was not weakened. ProductVariantDeletionProtectionAuthority, ValidationService CanDeleteProductAsync and CanDeleteVariantAsync plus GetProductDeleteBlockersAsync and GetVariantDeleteBlockersAsync, ProductsController can-delete boundaries, and ProductAuthoringOrchestrator admission plus in-transaction revalidation remain fail-closed. Blocked saves return ValidationFailed with one structured error per blocker. Tracked state is cleared so no partial delete can flush later. Live protected traffic returned canDelete false with errorCode VARIANT_HAS_STOCK and five stable reasons; no PUT was emitted in either language.

## 12. Transaction and No-Partial-Mutation Guarantee

The transaction guarantee was re-verified by tests and live fingerprints. Duplicate-SKU create returns Conflict without mutation. SKU owned by another variant returns Conflict without a second mutation. Protected remove returns ValidationFailed without mutation. Omitting a protected variant blocks before creating its replacement. Type flips are rejected without mutation. Live SQL fingerprints built from product IsDeleted/IsActive/UpdatedAt/xmin plus all variant id/sku/isDeleted/isActive/isDefault/updatedAt rows were byte-identical before and after both English and Arabic protected blocks.

## 13. SKU and Identity Conflict Precision

ProductForm.jsx precise 409 handling was strengthened for PRODUCT_SKU_CONFLICT, PRODUCT_BARCODE_CONFLICT, and PRODUCT_ATTRIBUTE_CONFLICT using the actual top-level errors envelope. The allowlisted server message is surfaced verbatim, while the generic concurrency message is preserved for non-allowlisted failures. Live tenant-wide collision reusing an isolated live SKU returned HTTP 409 with code PRODUCT_SKU_CONFLICT on field variants.sku and message SKU is already assigned to another product variant. The UI showed that precise message under the Variant matrix was not saved heading, did not show the generic changed-by-another-user text, and focused the alert.

## 14. Implementation Review

Wave 4 production candidates were reviewed hunk by hunk: PointOfSale.Web/src/components/ProductVariantsTab.jsx, PointOfSale.Web/src/components/VariantMediaPicker.jsx, PointOfSale.Web/src/components/ProductForm.jsx, PointOfSale.Web/src/i18n/locales/en.json, and PointOfSale.Web/src/i18n/locales/ar.json. Test changes were reviewed in ProductVariantsTab.test.tsx, ProductVariantsTabWave4I18n.test.tsx, and ProductForm.test.tsx. Backend files in the dirty tree were inspected to confirm no Wave 4 weakening: ProductsController.cs, ProductVariantDeletionProtectionAuthority.cs, ValidationService.cs, and ProductAuthoringOrchestrator.cs. Prior-wave hunks were left untouched.

## 15. Automated Frontend Verification

Focused suites pass after the final media-picker patch: npm test with ProductVariantsTab.test.tsx, ProductVariantsTabWave4.test.tsx, ProductVariantsTabWave4I18n.test.tsx, and ProductForm.test.tsx gives 4 files and 74 tests passed. The broader product set with ProductFormWave3.test.tsx, VariantOptionsBuilder.test.tsx, and AiProductGeneratorModal.test.tsx gives 7 files and 91 tests passed. New regressions assert exact protected reason text, exact variant name and SKU, no save call, focused alert, Arabic reason with no English leakage and focus, and three identity-conflict codes against the real errors envelope.

## 16. Automated Backend Verification

Backend deletion-safety and transaction verification passes: dotnet test with ProductDeletionSafety and ProductAuthoringTransactionCompletenessIntegrationTests gives 38 passed and 0 failed. The combined authority gate with ProductAuthoringVariantIdentityAuthorityTests, ProductVariantLifecycleAuthorityTests, ProductAuthoringIdempotencyIntegrationTests, ProductAuthoringTransactionCompletenessIntegrationTests, and ProductWave3 gives 105 passed and 0 failed. Full solution build with dotnet build PointOfSale.sln succeeds with 0 warnings and 0 errors.

## 17. Build and Static Gates

PointOfSale.Web production build passes with vite v5.4.21, 5217 modules transformed, built in 19.69s; the only notice is the pre-existing oversized-bundle chunk warning. PointOfSale.API Release build passes with 0 warnings and 0 errors. git diff --check is clean apart from working-copy LF-to-CRLF notices. No commit was created. Bundle-size and dynamic-import notices are pre-existing and non-failing.

## 18. EF Model and Migration Gate

dotnet ef migrations has-pending-model-changes with PointOfSale.Infrastructure and PointOfSale.API startup reports No changes have been made to the model since the last migration. No migration was generated or applied. Wave 4 required no schema change because variant names remain derived, SKUs remain validated verbatim, and deletion protection remains a read authority plus orchestrator refusal.

## 19. i18n Audit Gate

The package has no npm alias named i18n:audit, so the repository audit script was invoked directly: node scripts/i18n-certification-audit.mjs completed with no output and exit 0. This matches the prior Wave 3 gate behavior. No raw-key or missing-key failure was reported for the remediated surface.

## 20. Live Environment and Method

The final run used the rebuilt Release API on http://localhost:5010, admin Vite on http://localhost:5173, headed Microsoft Edge via Playwright msedge channel, and an AhramStore Owner session verified to tenant c9c5c1ae-949e-498e-bc63-07242daeb49d with reference category 48496721-af25-495a-bf56-888687acb13b and unit 3041db2b-3c76-4e30-a4d2-b690ffcb1462. Stamp 20260916-225335 created isolated product A WAVE4 INDEPENDENT CERT with id 52e56ca2-bf4e-546f-4cf1-82787eba98e9 and isolated product B WAVE4 COLLISION CERT with id 94a8ec0e-e66e-3b12-e77b-1f62dbbb9216. The protected target was existing product iPhone 17 Pro Max with variant blue / 512 and SKU IPHONE-17-PRO-MAX-BLUE-512. Evidence is under .opencode-tmp/wave4-independent/bin/Release/net9.0/evidence with JSON and five screenshots. SQL reconciliation used read-only SELECTs. Cleanup used authenticated API DELETE with idempotency and version headers.

## 21. Protected Delete Certification English Desktop

At 1440x1000 English, selecting the protected blue / 512 row, confirming Delete 1 Selected, and clicking Save Matrix produced GET can-delete 200 with canDelete false, errorCode VARIANT_HAS_STOCK, and five reasons: VARIANT_HAS_STOCK, VARIANT_USED_IN_PURCHASES, VARIANT_USED_IN_RETURNS, VARIANT_HAS_MOVEMENTS, and VARIANT_HAS_COMMERCEHUB_LISTING. PUT count was 0. The nearby alert showed heading Variant matrix was not saved, exact variant blue / 512 with IPHONE-17-PRO-MAX-BLUE-512, and the full English authoritative reasons. document.activeElement was the alert. The SQL fingerprint was unchanged. Screenshot wave4-protected-delete-en.png was retained.

## 22. Protected Delete Certification Arabic RTL

At 1440x1000 with ar-EG, document direction was rtl. The same protected blue / 512 row was selected and confirmed with localized delete controls, then saved with localized Save Matrix. No PUT was emitted. The alert showed heading لم يتم حفظ مصفوفة المتغيرات and the full Arabic authoritative reasons, including stock, purchase, return, movement, and CommerceHub listing sentences. No English authoritative reason leaked into the Arabic alert. document.activeElement was the alert. The SQL fingerprint remained identical to the pre-block value. Screenshot wave4-protected-delete-ar.png was retained.

## 23. Clean-Delete Control Certification

The isolated clean path proves the fix does not over-block. Removing isolated Size: 512 while keeping Size: 256 returned canDelete true with empty messageEn/messageAr and empty reasons, then PUT 200. Reload removed Size: 512 from the UI while retaining Size: 256. Final API showed one live variant with the generated SKU. Read-only SQL showed live 1 and deleted 1 with the live SKU agreeing. This satisfies clean delete succeeds.

## 24. SKU Collision Certification

Isolated product B attempted to reuse isolated product A live SKU. The PUT returned 409 with PRODUCT_SKU_CONFLICT on variants.sku and message SKU is already assigned to another product variant. The UI alert contained Variant matrix was not saved plus that precise message, contained no generic changed-by-another-user text, and received focus. This proves precise identity-conflict UX rather than a generic concurrency failure.

## 25. Variant Naming and SKU Generation Certification

Desktop English showed Size: 256 and Size: 512 with no exact bare 256 cell. Generating SKU for 2 selected rows produced contextual distinct values WAVE4-INDEPENDENT-CERT-20260916-225335-256 and WAVE4-INDEPENDENT-CERT-20260916-225335-512. Save returned PUT 200 and reload retained both SKUs byte-stable. Advanced Variant Tools defaulted to aria-expanded false, expanded to all five sections Variant Templates, Choose Attributes, SKU Builder, Combination Rules, and Workspace Statistics, then recollapsed to false.

## 26. Responsive Certification Desktop and Mobile EN AR

Desktop 1440x1000 English showed no document overflow and no unexpected HTTP failures. Mobile 390x844 English and Arabic both showed correct document direction ltr and rtl respectively, no document overflow, an internal div.overflow-x-auto matrix scroll container, and a reachable Advanced Variant Tools control. Screenshots wave4-independent-desktop-en.png, wave4-mobile-en.png, and wave4-mobile-ar.png were retained.

## 27. API and DB Reconciliation and Tenant Isolation

UI, API, and DB agree. After clean deletion, API reported one live variant with the generated SKU and DB reported the same live SKU with one live and one soft-deleted row. The protected product fingerprint was unchanged by both blocked saves. All live work stayed in tenant c9c5c1ae-949e-498e-bc63-07242daeb49d resolved from the authenticated session. No order, financial, Treasury, accounting, or historical mutation was performed; only isolated test rows were written and then removed.

## 28. Cleanup and Historical Safety

Both isolated products were removed through authenticated API DELETE with cleanup-b 204 and cleanup-a 204. No isolated product remains visible from this run. The protected iPhone variant was never mutated; its five blockers remain intact. No SQL DELETE or UPDATE was issued; database access was SELECT-only. No historical SKU rename, no financial write, no Treasury write, and no migration were performed.

## 29. Change Classification and Evidence Inventory

The 28 tracked modifications break down as Wave 4 production 5 files: ProductVariantsTab.jsx, VariantMediaPicker.jsx, ProductForm.jsx, en.json, ar.json; Wave 4 tracked test updates 2 files: ProductVariantsTab.test.tsx and ProductForm.test.tsx; prior-wave and shared-surface remainder 21 files including ProductsController, StorefrontDtos, IValidationService, ProductAuthoringInfrastructurePolicies, ProductAuthoringOrchestrator, StorefrontProductService, ValidationService, CatalogPricingAuthority, ProductPricingCompatibilityMirror, ProductCreatedEventConsumer, ProductStorefrontVisibilityAuthority, Storefront useTranslation and product types, AffiliateStorefrontPricingSnapshotTests, ProductVariantLifecycleAuthorityTests, ProductAuthoringIdempotencyIntegrationTests, ProductAuthoringTransactionCompletenessIntegrationTests, i18n-certification-audit.mjs, AiProductGeneratorModal, ValidationDeleteModal, and VariantOptionsBuilder. Meaningful untracked additions preserved include ProductAuthoringVariantIdentityAuthority.cs, ProductVariantDeletionProtectionAuthority.cs, ProductSlugAuthority.cs, ProductAuthoringVariantIdentityAuthorityTests.cs, ProductDeletionSafety tests, ProductWave3 tests, Storefront identity tests, ProductSlugAuthorityTests.cs, AiProductGeneratorModal.test.tsx, ProductFormWave3.test.tsx, ProductVariantsTabWave4.test.tsx, ProductVariantsTabWave4I18n.test.tsx, VariantOptionsBuilder.test.tsx, prior Wave 1 through Wave 4 reports, and temporary harnesses wave2-browser, wave3-browser, and wave4-independent. Build-output and secure-upload artifacts under .opencode-tmp were left untouched. Evidence inventory: wave4-independent-evidence.json with failureCount 0 plus five PNG screenshots listed in section 20.

## 30. Final Verdict and Acceptance Matrix

PASS. Protected delete blocks, explains the authoritative reason, and performs no mutation; clean delete succeeds. Acceptance matrix: independent review completed; remediation without weakening deletion safety completed; frontend focused 74 passed; broader frontend 91 passed; backend focused 38 passed; combined backend 105 passed; Web production build passed; API Release build 0 warnings and 0 errors; solution build passed; git diff check clean apart from LF notices; EF model gate passed; i18n audit passed; live AhramStore Owner session and tenant verified; protected English block with exact reason, no PUT, focused alert, unchanged DB fingerprint passed; protected Arabic RTL block with exact Arabic reason, no PUT, no English leakage, focused alert, unchanged DB fingerprint passed; clean-delete control with canDelete true, PUT 200, UI/API/DB agreement passed; SKU collision 409 with precise focused message passed; naming and contextual SKU generation with reload stability passed; Advanced disclosure default collapsed/expand/collapse passed; desktop and mobile EN/AR responsive with no document overflow passed; API and DB reconciliation with tenant isolation passed; isolated cleanup 204 with no historical/financial/Treasury change passed; 28 tracked changes plus untracked artifacts preserved and classified passed; no commit passed.
