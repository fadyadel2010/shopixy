# PRODUCT WAVE 1 — IDENTITY / SLUG / LOCALIZATION CERTIFICATION (2026-09-16)

## 1. Executive Verdict

**CONDITIONAL PASS (code + automated verification) / INCOMPLETE (live browser + live tenant verification).**

Wave 1 code remediation is implemented, unit- and integration-tested, and both
production builds pass. The proven contract defect — the UI promised
("URL-friendly identifier (auto-generated from name if empty)") what the server
never delivered (slug persisted verbatim, blank allowed) — is fixed at the
server authority. Bilingual UX now treats Arabic as optional progressive
disclosure with explicit fallback, and the affected Product screens have zero
missing i18n keys.

Not performed in this environment: real Edge browser certification against
AhramStore, live storefront reality check, and live tenant-isolation probing
(no authenticated browser/tenant access available here). Those items are
explicitly listed as residual work, not claimed.

## 2. Scope

Wave 1 only:

- Product slug / storefront visibility integrity (server derivation,
  normalization, tenant-scoped collision safety, slug stability on update).
- Product name / description bilingual UX (Option B selected: primary name +
  "Add Arabic translation" disclosure, proven against the domain contract).
- Core Arabic localization for Create / Edit / List / Detail / Variants /
  options / pricing / inventory / publication / SEO screens.

Untouched (Wave 2/3/4): SKU uniqueness, variant deletion/persistence,
option-group Enter behavior, simple-product storefront pricing, AI
loading/freeze, product-type simplification, full browser certification.

## 3. Findings

- **Finding 1 (confirmed, contract defect):** manual product creation could
  persist `Slug = null` or `''`. The storefront slug route
  (`StorefrontProductsController` → `StorefrontProductService.GetProductBySlugAsync`)
  rejects blank slugs (`400 INVALID_SLUG`); such products are reachable only
  by id. POS Admin list gates "View on storefront" on
  `isOnlineVisible && slug` (`Products.jsx:1475`), so the product silently
  has no storefront link. Backend never generated a slug; only the AI modal
  filled one (and only when the field was blank).
- **Finding 3 (confirmed, fixed):** 31 `t('…')` keys used by Product screens
  did not exist in either locale (masked by `|| 'English'` fallbacks), and 6
  Arabic values were untranslated English. Products namespace is now 328/328
  with zero gaps; all 177 keys used by the three Product screens resolve.
- **Finding 5 (resolved per domain contract, not by deleting fields):**
  `Name` is canonical/required; `NameAr`/`Description`/`DescriptionAr` were
  already optional at every layer (entity nullable, validator only requires
  `Name` + `CategoryId` for non-drafts). The burden was presentational, so
  the fix is presentational (collapsible translations + fallback hints), with
  API contracts unchanged.

## 4. Slug Authority

Proven from source (authoritative):

1. Slug is **optional at rest** (`Product.cs:122` `string? Slug`; no
   `IsRequired`/length config in `ProductConfiguration`; Mongo sparse index).
2. Slug is **required for slug-route storefront lookup** (blank → `400
   INVALID_SLUG`; `MetaProductFeedService` excludes slug-less products).
3. Slug is **NOT unique per tenant** (`IX_Products_TenantId_Slug` non-unique;
   no classifier entry; `FindBySlugAsync` is `FirstOrDefaultAsync`).
4. Slug is **stored case-sensitively but read case-folded** (writes verbatim;
   `GetProductBySlugAsync`/`SlugResolverController` lowercase before query).
5. Slug **is editable** today (full overwrite; stale slug dies immediately).
6. When omitted, **nothing generated one** (backend or frontend).
7. Duplicates are **silently allowed** (last-write-wins SQL; nondeterministic
   Mongo/Redis hits).
8. Invalid characters are **not rejected** server-side.
9. Arabic-only input: frontend sanitizer stripped it to `''`; API passthrough
   stored it verbatim.
10. Empty name + empty slug → `null`/`''` persisted (invisible on slug route).
11. Name change **never** touched slug (independent assignment).
12. No redirect/history semantics exist; URL preservation was accidental.

Authority decision: **the server (ProductAuthoringOrchestrator) is now the
slug authority**, implementing what the UI already promised. New helpers:
`PointOfSale.Core/Helpers/ProductSlugAuthority.cs` (normalize / generate /
ensure-unique, deterministic, no randomness). Slugs are NOT derived from
localized UI state at read time; derivation happens once at write from the
canonical `Name`.

## 5. Manual Creation

Root cause chain: `ProductForm` initialized `slug: ''` → payload spread sent
`''` → `CreateProductRequest.Slug` → mapper → `command.Seo.Slug` →
`ApplyProduct` stored verbatim (`Orchestrator:1120`). No validator, no
classifier, no generator intervened.

Fix: orchestrator resolves the slug on create (normalize requested → derive
from `Name` → deterministic `product-{8 hex}` fallback), tenant-unique with
`-2/-3` suffixes, excluding the deterministic product id so same-key retries
converge. Update path preserves a stored slug when the request is blank and
backfills when the stored slug is missing. Frontend sends `null` for blank
slugs and suggests (create-mode only) a preview from the name.

## 6. Storefront Visibility

Manually created products were missing from the storefront slug route for an
identity reason (blank slug), NOT a publication/tenant/projection reason.
Verified untouched and correct: `IsActive`/`PublishStatus`/`IsOnlineVisible`
gating, tenant scoping (`TenantId` on SQL/Mongo/Redis/ES), SQL-authority
overlay, and cache invalidation were left as-is. No visibility rule was
bypassed; the fix only guarantees the identity value the route requires.

## 7. Slug Implementation

- `ProductSlugAuthority.Normalize`: trim → lowercase → `[^a-z0-9]+` → `-`,
  collapse, trim `-`, cap 160, null when unusable. Never throws.
- `GenerateBase(requestedSlug, name, productId)`: requested → name →
  `product-{first 8 hex of N-formatted id}` (deterministic, URL-safe).
- `EnsureUnique(base, isTaken)`: base first, then `-2`, `-3`, …, length-aware
  truncation. Case-insensitive comparison (historical mixed-case rows).
- `ResolveTenantUniqueSlugAsync` (orchestrator): tenant-scoped candidate load
  (`Slug == base || StartsWith(base + "-")`), `AsNoTracking`, excludes the
  product being written. Runs **before** the idempotency fingerprint in both
  create and update so replays converge (this ordering was proven necessary
  by `ProductAuthoringIdempotencyIntegrationTests` — see §16).
- Frontend: `sanitizeSlugValue` on slug input (full normalization, was
  lowercase+spaces only); create-mode name→slug suggestion until manually
  edited; AI-applied slug counts as manual; edit mode never auto-suggests.

## 8. Slug Edit Semantics

Selected: **C — stable with explicit-edit support, no regeneration.**
Renaming never rewrites the slug. Editing the slug field is still permitted
(power users/SEO), now with an amber warning
(`products.slugEditWarning`: changing it breaks existing links). No
redirect/history table was introduced (that would be a Wave 4–scale identity
redesign; per STOP conditions it is documented, not built). Existing
search/index behavior preserved: projections carry the current slug through
unchanged paths.

## 9. Database Safety

**No migration generated.** Rationale: a tenant-unique constraint cannot be
applied safely without first auditing historical duplicate/null rows, and the
task forbids silent historical rewriting. The invariant is instead enforced
at the write authority (all product writes flow through the orchestrator —
verified sole writer, §22), which prevents *new* collisions. Recommended
follow-up (not Wave 1): read-only duplicate audit, then a tenant-aware unique
index migration with explicit backfill semantics. EF model is unchanged
(Release build + model snapshot untouched).

## 10. Bilingual Identity

Domain contract (entity + configuration + validator + readers):

- `Name`: canonical, required (non-draft), max 300. The only name the
  storefront can rely on.
- `NameAr`: optional translation. No auto-translation anywhere (none added).
- `Description` / `DescriptionAr`: both optional.
- DTOs always carry both languages; presentation picks per locale.

New explicit fallback authority `Core/Helpers/ProductDisplayIdentity.cs`:
Arabic request → `NameAr → Name → ""`; English/default → `Name → NameAr → ""`
(descriptions same shape, empty when both absent). Never returns null.

## 11. Description UX

Same model as names: primary description always visible; Arabic description
behind "Add Arabic description" disclosure with the same fallback hint. No
duplication forced, no silent machine translation, no contract change.

## 12. Localization Audit

Method: parsed `en.json`/`ar.json` + extracted all `t('…')` from
`ProductForm.jsx`, `Products.jsx`, `ProductWorkspacePage.jsx`.

- Before: 31 used keys missing in **both** locales (raw-key leakage masked by
  `||` fallbacks), 6 untranslated `ar` values, `pixel.*` namespace absent.
- After: `products` 328/328, `common` 110/110, `variants` 46/46,
  `pixel` 1/1, zero missing in either locale; 177/177 used keys resolve.
- Remaining same-value entries are locale-neutral (`{{name}}:`, `0.00`).
- Surgical text edits only (mixed CRLF/LF preserved; no whole-file rewrite).

## 13. Canonical Product Terminology

Reused existing repo-approved Arabic (verified in `ar.json`): المنتج، اسم
المنتج، الوصف، الوصف بالعربية، الوحدة الأساسية/الافتراضية، سعر الشراء/البيع،
المتجر الإلكتروني، منشور/مسودة/نشط، المخزون، الهوية. New keys follow the
same glossary (`products.slugEditWarning` uses الرابط المختصر for slug;
`storefrontLayout` → تخطيط المتجر الإلكتروني; `pixel.productOverride` →
تجاوز البكسل). Full key list is in the `en.json`/`ar.json` diff.

## 14. Product Form UX

- Required indicators unchanged and correct: only Name + Category are `*`
  (the only technically required fields) — slug/units/prices show no `*`
  because the server now guarantees identity and publish-time validation
  handles the rest.
- Slug field: full live sanitization, auto-generated-if-empty hint (now true),
  amber edit warning on existing products.
- Arabic name/description: collapsed "+ Add Arabic translation/description"
  buttons when empty; auto-reveal when a value exists (loaded product or
  typed); explicit "storefront shows primary language when empty" hints.
- Create-mode slug suggestion never dirties edit-mode forms (regression-tested).

## 15. Implementation

Production files changed (4 modified, 4 added):

- `PointOfSale.Core/Helpers/ProductSlugAuthority.cs` (new).
- `PointOfSale.Core/Helpers/ProductDisplayIdentity.cs` (new).
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs`
  (slug resolution pre-fingerprint on create/update + uniqueness query).
- `PointOfSale.Web/src/components/ProductForm.jsx` (suggest/sanitize/collapse).
- `PointOfSale.Web/src/i18n/locales/{en,ar}.json` (38 + 38 keys, 6 repairs).
- `PointOfSale.Tests/Unit/Helpers/Product{SlugAuthority,DisplayIdentity}Tests.cs`.

No SKU/variant/option-group/pricing/AI-loading/product-type changes. No
financial, accounting, treasury, or inventory-valuation changes. No new
dependencies.

## 16. Tests

- New: `ProductSlugAuthorityTests` (23) + `ProductDisplayIdentityTests` (15)
  — **38/38 pass**. Cover normalization, Arabic-only/empty/invalid names,
  determinism, `-2/-3` collisions, case-insensitivity, max-length, both
  fallback directions, trim behavior.
- Existing backend: idempotency (4 previously broken by mid-work ordering —
  caught, fixed by pre-fingerprint resolution, now pass), transaction
  completeness, and `ProductAuthoringInfrastructurePreparationTests` (2
  initially tripped by the token "Storefront" in comments — reworded, now
  pass): **27/27 pass** in the targeted run.
- Full product-related backend sweep: 153 pass; 23 failures all proven
  pre-existing via `git stash` comparison on the pristine tree (22
  distributed-reliability tests exercising deprecated legacy variant
  endpoints — Wave 2 territory; 1 `StorefrontCatalog…` architecture test).
- Frontend: `ProductForm` + `Products` suites **155/155 pass** (one
  whitespace test initially caught the edit-mode suggestion side effect;
  fixed by create-mode-only suggestion); `architecture-tests` **52/52 pass**.
- Localization completeness is asserted by audit script (177/177 keys
  resolve), not yet a committed test — recommended Wave 1 follow-up.

## 17. Browser Certification

**Not performed** — no authenticated Edge session or AhramStore tenant access
exists in this environment, and the task forbids unsafe mutations. The
minimum protocol (§17 steps 1–16) is ready to execute on demand: create a
simple product manually without AI, verify the suggested slug, save, confirm
edit/storefront-link behavior, switch ar/en, check RTL at 1440×1000 and
390×844, and retain (not delete) the isolated test product.

## 18. Storefront Reality Check

**Not performed live** (see §17). By construction, every product written
through the authoring API now carries a non-blank, tenant-unique,
lowercase slug, which is exactly the value the slug route, feeds, and
`Products.jsx` storefront links require. Live URL/title/search verification
awaits §17.

## 19. Responsive Certification

**Not performed live** (see §17). Code-level notes: added elements reuse
existing responsive primitives (`md:col-span-2`, `text-xs`, text buttons);
no fixed widths, no new layout containers; RTL preserved (`dir="rtl"`,
`text-start`). No page-overflow risk introduced beyond pre-existing layout.

## 20. API/Database Reconciliation

**Not performed against a live tenant DB** (read-only check requires tenant
access; no SQL business writes were made, per constraints). Reconciliation
procedure for the §17 test product: compare UI ↔ `GET /products/{id}` ↔
`Products` row (`Name/NameAr/Description/DescriptionAr/Slug/Status/
Published/StorefrontEnabled`) ↔ storefront slug route. Expected: slug
non-blank and identical across all four surfaces.

## 21. Tenant Isolation

Code-level: slug resolution, lookup, indexes, and cache keys are all
`(TenantId, Slug)`-scoped; the uniqueness query filters `TenantId` and only
excludes the caller's own product id; no cross-tenant read was added.
Runtime probing (tenant A product must not resolve in tenant B) awaits §17
and is recorded as residual.

## 22. Final Diff Review

Inspected full diffs of all 4 modified files: orchestrator (slug-only),
`ProductForm.jsx` (slug/bilingual UX only), locales (additive keys + 6 value
repairs). No Wave 2/3/4 files touched; no financial/inventory logic touched;
no dependency changes; `git diff --check` clean; EF model/snapshot unchanged
(no migration). New files are additive helpers/tests. **No commit made** (per
instructions); changes are uncommitted in the working tree.

## 23. Residual Risks

1. Race-window duplicate slugs under concurrent different-key creates (no DB
   unique constraint yet) — mitigated at authority, needs the §9 migration
   after a duplicate audit.
2. Historical null/duplicate slugs remain as-is (by design; no silent repair).
3. Concurrent same-name creates may briefly share a base slug; last writer is
   still deterministic per product id on retry.
4. `product-{id8}` fallback slugs for Arabic-only names are stable but not
   human-meaningful; acceptable per "no silent auto-translation" rule.
5. Live browser/storefront/responsive/tenant verification (§17–§21) outstanding.
6. Localization completeness audit is a script, not yet a CI test.
7. Pre-existing failures untouched: 22 distributed-reliability + 1
   `StorefrontCatalog…` architecture test (fail identically on pristine tree).

## 24. Final Verdict

**CONDITIONAL PASS:** manual creation now yields correct identity (server
guarantees a usable slug); slug authority is explicit, deterministic,
tenant-scoped, and idempotency-safe; bilingual UX is understandable with
explicit fallback; Arabic localization for affected screens is complete
(0 missing keys); no data loss, no tenant logic change, no financial impact;
focused tests (38 new + 27 integration/architecture + 155 frontend + 52
frontend-architecture) and both production builds pass.

**FAIL-clause status:** no silent unusable-identity path remains in the
authoring flow; no collision corruption (application-level uniqueness, DB
constraint deferred per §9); no empty-identity fallback; tenant isolation
code unchanged; existing edit flows regression-tested. Live §17–§21
certification is the remaining gate before an unqualified PASS.
