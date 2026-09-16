# SHOPIXY — PRODUCT WAVE 2A CERTIFICATION
## Variant + Simple Product Deletion Safety / Historical Dependency Validation
**Date:** 2026-09-16 · **Scope:** WAVE 2A only (extends Wave 2) · **Commit:** none (per instruction, working tree only)

---

## 1. Executive Verdict

**CONDITIONAL PASS** — code, automated tests, and static verification are green; live-browser and live-DB legs are pending by environment boundary (same precedent as the Wave 2 conditional pass).

What was proven:

- A brand-new unsafe deletion surface — **variant-matrix save omission** (`SyncAuthoredVariantsAsync` soft-deleted any omitted variant with zero history checks) — is now blocked server-side with structured business reasons.
- **Product Remove** (`ExecuteArchiveOrRemoveAsync`) can no longer bypass historical protection when invoked directly: it pre-validates and re-validates inside the authoring transaction.
- The existing `CanDeleteProductAsync` authority (6 product-level checks) is preserved byte-for-byte in behavior and extended with 4 new dependency families: **Storefront orders, CommerceHub orders, open reservations, published CommerceHub listings**, plus **reserved/damaged stock**.
- A new **variant-level authority** (`CanDeleteVariantAsync` + `GetVariantDeleteBlockersAsync`) applies the identical 10-dependency rulebook scoped to a single SKU, so **simple products** (Product + one default Variant) and every multi-variant SKU share one protection source (`ProductVariantDeletionProtectionAuthority`).
- Frontend explains WHY: matrix preflight per omitted variant, structured `reasons[]` in the delete modal, allowlisted authoring error codes, En/Ar strings.
- 22 new backend tests + 2 new frontend tests, all passing; Wave 2 and bulk-delete regressions pass (2 pre-existing environment failures documented, unrelated).

Pending (environment boundary, no access): live AhramStore browser certification (§24) and live API/DB reconciliation against a staging tenant (§25 relegated to static + isolated-store evidence). **No historical business data was touched** — all verification used isolated InMemory/SQLite stores; the running API process was never called and no Postgres writes occurred.

---

## 2. Scope

WAVE 2A only. Extends Wave 2 (variant persistence, variant-deletion persistence, SKU uniqueness, Option Group Enter). No Wave 1/3/4 changes, no Treasury/accounting changes, no historical rewrites, no migrations, no direct SQL business writes, no commit. Files changed (production): `ProductVariantDeletionProtectionAuthority.cs` (new), `IValidationService.cs`, `ValidationService.cs`, `ProductAuthoringOrchestrator.cs`, `ProductsController.cs`, `ProductVariantsTab.jsx`, `ProductForm.jsx`, `ValidationDeleteModal.jsx`, `en.json`, `ar.json`. Tests: `PointOfSale.Tests/ProductDeletionSafety/` (new), `ProductVariantsTab.test.tsx`.

---

## 3. Delete Path Inventory

| # | Path | File:lines | Pre-fix protection |
|---|------|-------------|--------------------|
| 1 | Product delete button → `ValidationDeleteModal` → `DELETE /products/{id}` | `Products.jsx:492-498,1593-1603`, `ProductsController.cs:549-601` | `CanDeleteProductAsync` (6 checks) ✓ |
| 2 | Product `can-delete` pre-check | `ValidationDeleteModal.jsx:84-86`, `ProductsController.cs:789-806` | same 6 checks, flat message ✓ |
| 3 | Bulk delete `POST /products/bulk/delete` | `bulkProductApi.js`, `BulkProductLifecycleService.cs:605-614` | per-item `CanDeleteProductAsync` → `DEPENDENCY_BLOCKED` ✓ |
| 4 | Deactivate `PUT /products/{id}/deactivate` → Archive | `ProductsController.cs:607-649` | none (correct: Archive is the safe lifecycle) ✓ |
| 5 | **Variant matrix save omission** → `SyncAuthoredVariantsAsync` | `ProductVariantsTab.jsx:1275-1289`, `ProductAuthoringOrchestrator.cs:1617-1626` | **NONE — soft-deleted unconditionally. Root unsafe path. FIXED.** |
| 6 | Variant delete-selected (frontend only, feeds omission) | `ProductVariantsTab.jsx:1243-1259` | stock "delete anyway" confirm — **replaced by server preflight. FIXED.** |
| 7 | Legacy `DELETE products/{id}/variants/{vid}` | `ProductVariantsController.cs:1056-1170` | deprecated 410; residual body checked default/only/stock-confirm only, never history. Left deprecated; canonical path is now guarded. |
| 8 | Direct orchestrator `Remove` (imports, admin tools, jobs) | `ProductAuthoringOrchestrator.cs:487-581` | **NONE — FIXED (pre-check + in-transaction re-check).** |
| 9 | Option/value removal orphaning variants | flows through path 5 | **FIXED (same guard).** |
| 10 | Unit delete `DELETE {id}/units/{unitId}` | `ProductsController.cs:1928-1986` | purchase/sale/return usage check ✓ (unchanged) |

---

## 4. Existing Delete Authority

- `CanDeleteProductAsync` (`ValidationService.cs:34-150`, via `IValidationService.cs:11-13`) was the SOLE deletion validator. It checked, in order: `PRODUCT_HAS_STOCK` (Quantity != 0 only), `PRODUCT_USED_IN_PURCHASES`, `PRODUCT_USED_IN_SALES`, `PRODUCT_USED_IN_RETURNS`, `PRODUCT_USED_IN_TRANSFERS`, `PRODUCT_HAS_MOVEMENTS` — all `ProductId`-level, tenant-scoped through parents. Proven by `BulkDeleteValidationAuthorityTests` (6-code theory, still passing).
- No `CanDeleteVariantAsync` / `CanRemoveVariant` / `Has*History` / `IsDeletable` existed anywhere (repo-wide grep: zero backend hits). Variant deletion had no authority at all.
- Delete-vs-retire semantics (existing, reused unchanged): `IsDeleted` (tombstone, global-filtered) vs `IsActive=false` + `PublishStatus=Archived` (retire). No `IsArchived` on Product/Variant. `DELETE` = Remove (validated), Deactivate = Archive (unvalidated by design).

---

## 5. Protected Dependencies

Every dependency below has source evidence (entity + scoping rule) and is enforced by `ProductVariantDeletionProtectionAuthority.GetBlockersAsync`, in stable order:

1. **Current stock incl. reserved/damaged** — `ProductVariantStock` (inventory source of truth; `ReservedQuantity` backs open reservations; `DamagedQuantity` is physical on hand). Codes `PRODUCT_HAS_STOCK` / `VARIANT_HAS_STOCK`.
2. **Purchase history** — `PurchaseInvoiceItem` (ProductId + ProductVariantId, scoped via `PurchaseInvoice.TenantId`).
3. **Sales history + COGS** — `SalesInvoiceItem` with immutable `CostPriceSnapshot`/`CostAmountSnapshot`.
4. **Returns + COGS reversal** — `ReturnItem` with immutable `CostPriceSnapshot`/`CostAmountSnapshot`.
5. **Transfers** — `StockTransferItem` (scoped via `StockTransfer.TenantId`).
6. **Movements** — `StockMovement` audit log (`TenantId` direct, ProductVariantId nullable).
7. **Storefront orders** — `OrderItem`: immutable SKU snapshot; money lives on items; scoped via `Order.TenantId`. Codes `*_HAS_STOREFRONT_ORDERS`.
8. **CommerceHub orders** — `CommerceHubSubOrderItem` with immutable `UnitCostSnapshot`/`CostAmountSnapshot`; tenant scoping is `SubOrder.StoreId` (StoreId IS the store tenant — `CommerceHubCheckoutService.cs:759`, `InventoryConsistencyService.cs:1687`). Codes `*_HAS_COMMERCEHUB_ORDERS`.
9. **Open reservations** — `InventoryReservation` with `(Reserved − Committed − Released) > 0`; terminal/fully-released rows do not block. Codes `*_HAS_RESERVATIONS`.
10. **Published marketplace listing** — `CommerceHubListing.IsPublished` (unpublished rows are dormant history and do not block). Codes `*_HAS_COMMERCEHUB_LISTING`.

Deliberately non-blocking (documented): POS cart items (transient; checkout revalidates sellability), wishlists, affiliate links, promotions — non-financial references owned by other workflows. No dependency was added without entity evidence.

---

## 6. Simple Product Model

Simple = `Product(HasVariants=false)` + **exactly one `IsDefault` Variant** + default `ProductUnit` as price authority + `ProductVariantStock` as inventory authority (`Product.cs:98-113`, `ProductVariantService.cs:204-305 EnsureDefaultVariantAlignedAsync`, called on every create/update). There is no `ProductType` enum and no separate simple-product table.

Bypass analysis (pre-fix): product deletion validated `ProductId`-level history, so a simple product WITH `ProductId`-stamped history was blocked — but variant-level-only references and the four new families (orders, reservations, listings, reserved stock) were invisible, and the matrix/omission and direct-Remove paths had no checks at all. Post-fix: the simple product's default variant is an ordinary variant row evaluated by the same 10-dependency authority at both product and variant level (`CanDeleteProductAsync_SimpleProductWithPurchaseHistory_IsBlocked`, `..._SimpleProductWithStorefrontOrder_IsBlocked`). No bypass remains; no lifecycle redesign was needed (STOP condition not triggered).

---

## 7. Variant Model

Multi-variant = `Product(HasVariants=true)` + N variants, each an independent sellable SKU with own price/stock/history. Verified precision: history on variant B does NOT block deletion of unused variant A (`CanDeleteVariantAsync_SiblingVariantStock_DoesNotBlock`). Per-variant states: unused → deletable; stock/purchases/sales/returns/transfers/movements/storefront/CommerceHub/reservations/listing → blocked with the specific `VARIANT_*` code (one test per family, §23 test list). UI behavior differs correctly by layer: matrix preflight aborts the save naming each SKU + reason; product modal lists structured reasons + offers Deactivate; authoring-save rejection surfaces per-reason errors through the existing `errors[]` contract.

---

## 8. Delete vs Retire

Existing domain terminology reused, no new states invented:

- **DELETE** — entity has zero protected blockers; `Remove` sets `IsDeleted` (+ `IsActive=false`, `Archived`) on product and all live variants with tombstone outbox events.
- **RETIRE / DISABLE** — entity has protected history: stays addressable (`IsDeleted=false`) with `IsActive=false`, `PublishStatus=Archived`, `IsOnlineVisible=false` via Deactivate/Archive (never validated, by design — retiring IS the safe lifecycle).
- **BLOCK** — prerequisite cleanup required first (e.g., release reservations, unpublish listing); the UI names the prerequisite in business terms.

---

## 9. UI Validation

- Matrix save: preflight `GET /products/{id}/variants/{vid}/can-delete` per omitted persisted variant (fail-closed: network failure or non-`true` verdict blocks). Abort message = headline (`variants.deleteBlockedByHistory`, En/Ar) + per-SKU server reasons (`label: reason…`), e.g. "STYLE-DARK: Cannot delete this variant: it has purchase history…". Old "delete them anyway" stock-bypass confirm removed.
- Product modal: `can-delete` now returns additive `reasons[]` (`{code,messageEn,messageAr}`); modal renders them as a bullet list under the headline; Deactivate alternative still offered. No DB/table internals exposed (server messages use business terms; modal keeps its unsafe-pattern sanitizer).
- Authoring-save rejection: per-reason `VARIANT_*` codes added to `SAFE_AUTHORING_ERROR_CODES` so `ProductForm` surfaces the real 400 `errors[]` messages instead of a generic conflict.
- i18n: 3 keys added to `en.json` + `ar.json` (`deleteBlockedByHistory`, `deleteProtectionCheckFailed`, `deleteProtectionUnknown`); JSON validity verified; production build passes.

---

## 10. Clean Product Case

`Wave2A Test Product` + `Dark`, no stock/purchases/sales/orders/returns/movements/COGS/reservations: `CanDeleteVariantAsync` → valid; save omission of a history-free variant proceeds (existing orphan test + new "allows save" test green). Clean products/variants remain deletable through the existing lifecycle — no over-blocking.

---

## 11. Stock Case

Isolated variant + stock added via entity graph (sellable qty; reserved-only; damaged implied by authority predicate): deletion blocked (`VARIANT_HAS_STOCK` / `PRODUCT_HAS_STOCK`), history rows intact, no orphan stock (nothing is deleted — the operation is refused before any mutation). Sibling-variant precision verified.

---

## 12. Purchase History Case

Controlled purchase line with `ProductVariantId` set: variant blocked (`VARIANT_USED_IN_PURCHASES`), product blocked (`PRODUCT_USED_IN_PURCHASES`). Purchase header + line verified intact after refusal. Simple-product equivalence verified (§6).

---

## 13. Sales History Case

Controlled sale line with `CostPriceSnapshot=7.5`: blocked (`VARIANT_USED_IN_SALES`); snapshot row re-read after refusal and asserted unchanged (7.5). POS sale history is the same `SalesInvoiceItem` authority (POS and admin sales share the table).

---

## 14. Storefront Order Case

Storefront `Order` + `OrderItem` (immutable SKU snapshot, `VariantId` set): variant AND product blocked (`*_HAS_STOREFRONT_ORDERS`); order item count re-verified = 1 after refusal. No dangling reference possible — nothing is removed.

---

## 15. CommerceHub Order Case

Live CommerceHub execution unavailable in this environment (boundary per spec §13). Service-level evidence instead: `CommerceHubSubOrderItem` (with `UnitCostSnapshot`/`CostAmountSnapshot`, `SubOrder.StoreId = tenant`) seeded through the legitimate order-lifecycle boundary scope (`CommerceHubBoundaryMutationScope.BeginOrderLifecycle`) → variant blocked (`VARIANT_HAS_COMMERCEHUB_ORDERS`); other-store `StoreId` → allowed (tenant isolation). Live end-to-end CommerceHub ordering remains pending with browser certification.

---

## 16. Return/COGS Case

Purchase-return line with `CostPriceSnapshot`/`CostAmountSnapshot` = 8: blocked (`VARIANT_USED_IN_RETURNS`). Sales COGS covered by §13; CommerceHub COGS snapshots covered by §15; return COGS-reversal snapshots covered here. No accounting references eliminated — refusal precedes mutation.

---

## 17. Option-Orphan Case

Wave 2 scenario (Plain/Medium/Dark → remove Dark, add Mojahed → payload has exactly Plain/Medium/Mojahed with preserved IDs) still passes unchanged for history-free variants. Controlled variant: Dark WITH purchase history + same option removal → save aborted BEFORE any mutation with `VARIANT_USED_IN_PURCHASES`; Mojahed never created in that attempt; Dark row untouched. Chosen lifecycle = **block option removal** (existing safe semantic: keep + deactivate explicitly), not silent preserve-and-hide — the user sees exactly why and what to do.

---

## 18. Simple Product Delete

Matrix: no history → allowed; stock → `PRODUCT_HAS_STOCK`; purchase → `PRODUCT_USED_IN_PURCHASES`; storefront order → `PRODUCT_HAS_STOREFRONT_ORDERS`; each mirrored at variant level with `VARIANT_*` codes. Product deletion cannot bypass variant history because both levels evaluate the same authority over the same underlying variant row.

---

## 19. Server Authority

Backend is authoritative at three layers: (1) controller/bulk pre-check (`CanDeleteProductAsync`, incl. new families); (2) orchestrator admission guard — `Remove` pre-validates, update validates every omitted variant; (3) **in-transaction re-validation** inside the authoring transaction before any `IsDeleted` mutation, mapped to `ValidationFailed` with per-blocker errors; `DeleteProduct` maps a raced refusal back to the standard localized contract via re-validation. New read-only endpoint `GET /products/{id}/variants/{variantId}/can-delete` (no route conflicts; 410-deprecated mutation controller untouched). Established error contracts reused (`ValidationResult`/`errorCode`/`messageEn`/`messageAr`/`reasons[]`; authoring `errors[]` with `Code/Field/Message`). Residual bound: under READ COMMITTED, a history commit landing between the in-transaction re-check query and `SaveChanges` is still theoretically possible (no migration/FK change permitted to close it with predicate locks); window narrowed to milliseconds inside one transaction and any observed race fails closed. Explicitly bounded per spec.

---

## 20. Tenant Isolation

All 10 checks scope through the authoritative parent tenant (`TenantId` direct or invoice/order/store-tenant parent). Tests: foreign-tenant variant → `VARIANT_NOT_FOUND` (never leaks cross-tenant state as allowed/blocked); foreign-tenant purchase history → does not block; other-store CommerceHub history (`StoreId ≠ tenant`) → does not block. Same-`VariantId` cross-tenant access is rejected at the existence check; `ProductId` cross-tenant product checks scope parents by tenant.

---

## 21. Concurrency

Validation + mutation are NOT fully serializable (documented bound, §19). Mitigations in place: idempotency-key admission (replays converge, no duplicate deletes — existing bulk tests green), `If-Match` expected-version staleness rejection, in-transaction re-validation, fail-closed preflight. No test can deterministically reproduce the residual ms-race without serializable isolation (out of scope: no migration permitted); documented instead of hand-waved.

---

## 22. Implementation

Reused, in order: `ValidationService` (extended, legacy codes/messages/order preserved) → shared `ProductVariantDeletionProtectionAuthority` (single rulebook for service + orchestrator; skips only entity types absent from trimmed test models, with a production-model completeness test pinning all 10 mapped) → orchestrator typed refusal (`ProductAuthoringProtectedHistoryException` → `ValidationFailed`, mirroring the Wave 2 identity-conflict pattern) → existing `Remove`/`Archive` lifecycle → existing error contracts → existing Deactivate alternative. No second deletion framework, no unrelated lifecycle changes, no Treasury/accounting/history writes.

---

## 23. Automated Tests

Backend (`ProductVariantDeletionSafetyTests`, 22 tests, all passing): clean-variant allowed; stock / reserved-only stock / sibling precision; purchase / sales+COGS-intact / return / transfer / movement; storefront order+survival; CommerceHub same-store block; active reservation block + released allowed; published listing block; multi-reason ordering + bilingual messages; foreign-variant NOT_FOUND; foreign-history ignored; other-store ignored; simple-product purchase/order equivalence; product reserved-stock; production-model completeness. Frontend (`ProductVariantsTab.test.tsx`, +2): protected omission aborts save naming SKU+reason with zero save calls; deletable omission saves 3-row payload. Regression: 52/52 (Wave 2 identity + slug + storefront identity + deletion safety), 64 bulk-lifecycle + 9 delete-validation green; Wave 2 save/reload, SKU, Enter suites green (25/25 variants tab, 26/26 ProductForm). Pre-existing failures (evidence-backed, untouched code): `BulkDeleteValidationAuthorityTests…IgnoresForeignTenantTransactionReferences` (SQLite EnsureCreated chokes on HEAD's Postgres `~` check constraint in `SupplierCardFundingInstrumentConfiguration.cs`, commit f939e6b); one timing-flaky 101-variant test (passes on rerun, 9.8s vs 10s budget). Every new test fails against pre-fix behavior (new codes/endpoint/guard absent) except pure allow-cases which guard against over-blocking.

---

## 24. Browser Certification

**NOT PERFORMED — environment boundary.** No authenticated AhramStore browser session is available in this environment, and the instruction forbids mutating existing business data. Desktop 1440x1000 / mobile 390x844 / LTR / RTL / Arabic localization therefore remain **PENDING**; RTL safety is structural (`dir={i18n.dir()}`, `ps-6`/`text-start` utilities, locale-keyed messages) and the production build compiles, but live proof is deferred to a credentialed session using isolated test records only.

---

## 25. API/DB Reconciliation

Live reconciliation pending with §24. Static + isolated-store evidence instead: every refusal test re-reads the protected rows (COGS snapshots, order items, purchase lines) and asserts them byte-identical; UI↔API↔DB shape verified by contract (endpoint `reasons[]` ↔ modal list; authoring `errors[]` ↔ save banner; `VariantId/ProductId/SKU/IsDeleted/IsActive` untouched on refusal). No orphan stock/reference possible — refused operations mutate nothing. No Postgres writes were made by this workstream (isolated InMemory/SQLite only; live API never invoked).

---

## 26. Final Diff Review

Reviewed all production hunks. Confirm: no Wave 1 regression (slug/identity tests 52/52); no Wave 3/4 changes; no Treasury/accounting changes; no historical rewrites (refuse-before-mutate everywhere); no direct SQL writes (EF LINQ only); no unsafe destructive delete (all delete paths validated; Archive untouched); no unrelated refactors (hunks limited to listed files); `git diff --check` clean; no migration (query-only change; EF model check = full-model mapping test green). Note: the working tree contains pre-existing unrelated dirty changes (Wave 1/2, Storefront) — this workstream neither depends on nor disturbs them. **No commit made**, per instruction.

---

## 27. Final Verdict

**CONDITIONAL PASS** (code + automated + static legs green; live legs pending).

- Protected variants cannot be destructively deleted — including via save omission and direct Remove. ✓
- Simple-product lifecycle has equivalent protection. ✓
- Stock / purchases / sales / storefront / CommerceHub / returns / COGS references remain intact (refusal precedes mutation; snapshots re-asserted). ✓
- Server enforces at three layers; frontend explains why in En/Ar with structured reasons. ✓
- Option-removal cannot silently destroy protected variants (blocks with reasons). ✓
- Tenant isolation passes; concurrency explicitly bounded. ✓
- Wave 2 regressions pass; builds (API Debug to temp dir — Debug bin locked by the running API process; API Release via test build, 0 errors; Web production build ✓) and `git diff --check` pass. ✓
- No historical data rewritten; no live data touched. ✓

**FAIL conditions avoided:** none triggered. **To reach FULL PASS:** run §24 browser matrix + §25 live reconciliation in a credentialed AhramStore session with isolated records, and disposition the two pre-existing failures (SQLite `~` DDL; flaky timing test) outside this workstream.

### STOP-condition disposition
None triggered: lifecycle derivable from domain evidence; no migration needed; no broad redesign required; no financial rewrite contemplated.
