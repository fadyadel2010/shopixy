# Storefront Product List/Detail Consistency Deep Audit - 2026-09-22

## 1. Executive Summary

The Shopixy storefront had a reproducible list-to-detail identity mismatch for
historical products whose canonical slug is blank. Product cards correctly fell
back to `/products/{ProductId}`, but `ProductDetailsPage` treated every route value
as a slug and called the slug-only API. The resulting request returned 404 even
though the product remained active, visible, tenant-owned, and purchasable.

The repair routes GUID identifiers to the existing tenant-scoped by-ID detail API
and keeps non-GUID identifiers on the canonical slug API. No database, projection,
cache, cart, checkout, variant, unit, or visibility rule was weakened or replaced.

Focused verification passed: 10/10 frontend tests, 4/4 backend tests, storefront
production build, live Shopixy discovery, live detail reads for all three affected
records, slug and ID controls for the Gram product, and cross-tenant negative
checks. Overall verdict: **PASS**, with the evidentiary limitation that no automated
browser was available; route selection is proven by focused client tests and the
live endpoints are proven through the real Shopixy host/proxy.

## 2. Objective

Determine why a storefront product can be listed but open as "Product not found,"
identify the exact failing contract, remediate it without weakening authority, add
regression coverage, verify MAIN behavior, and preserve a durable audit record.

## 3. Constraints Honored

- No subagents were used.
- No Kubernetes, k6, M4.5, load workload, threshold, VU, seed, or load-test file was
  used or modified.
- The heavily dirty shared worktree was preserved; unrelated changes were not
  reverted or claimed.
- Tenant isolation, SQL visibility authority, canonical `VariantId`, cart, and
  checkout contracts were preserved.
- No live product data was mutated and no slug backfill was run.

## 4. Environment

- Date: 2026-09-22.
- API: `http://localhost:5010`.
- Storefront/Vite proxy: `http://localhost:5174`.
- Verified Shopixy host: `shopixy.localhost:5174`.
- Shopixy tenant: `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`.
- Stores inspected: PostgreSQL, MongoDB, Elasticsearch, and Redis.

Raw API calls with a caller-supplied `X-Tenant-Id` are now rejected with
`TENANT_REQUIRED`. Live verification therefore used the verified Shopixy host and
the actual Vite proxy, matching the storefront request path.

## 5. Incident Statement

The externally visible symptom was valid product-card content followed by a detail
404. That symptom can arise only when the list and detail paths disagree on product
identity or visibility. The audit tested slug, ProductId, tenant, unit, variant,
projection, cache, and SQL visibility hypotheses independently.

## 6. Product Identity Contract

The card link contract in
`PointOfSale.Storefront/src/components/products/productCardLinks.ts:3-4` is:

```text
slug present -> /products/{slug}
slug absent  -> /products/{ProductId}
```

This is intentional and already covered by
`productCardRendererResolver.test.ts:35-39`. The public route therefore carries a
product detail identifier, not unconditionally a slug.

## 7. Live Discovery Inventory

`GET /api/storefront/products/discover?page=1&pageSize=100&lang=en` through the
Shopixy host returned:

- HTTP 200.
- `TotalCount = 19` and 19 returned items.
- Three items with a blank slug.
- One match for the audited Gram ProductId.

All discover items with nonblank slugs resolved to their matching ProductId during
the initial sweep. The only reproducible failures were the three blank-slug items.

## 8. Affected Records

| ProductId | Name | SQL/Mongo slug | Type | Unit | Live by-ID after fix |
|---|---|---|---|---|---|
| `b55ddc92-14b2-0160-78e2-a9ebfe867f1b` | `10.10product` | blank | simple | Piece | 200 |
| `7dba5eca-90b5-916a-f6ac-e1caf141b7af` | `Vendor A test` | blank | simple | Piece | 200 |
| `a621601a-bf86-89ab-23c9-5a281c62c575` | `G2-20260904-A` | blank | simple | Piece | 200 |

Each record is active, online-visible, searchable, nondeleted, and has one active
default canonical variant. These records are historical data, created before the
current deterministic slug-authoring authority was consistently applied.

## 9. Gram Product Control

The identified Gram product is:

- ProductId `cb81e893-e41f-fc67-dd39-d7c9f4afaa33`.
- Slug `realme-gt-neo-3`.
- Default unit `Gram` / `g`, unit ID
  `4e19026c-cb9d-4892-923e-8f1ab3ae4ce5`.
- `HasVariants = false`.
- Canonical default variant
  `7a620725-bc66-4ba1-ac3b-e33867edc0b5`.

Live slug detail and live by-ID detail both returned HTTP 200 and the same
ProductId. Gram is therefore not a causal discriminator for the reproduced defect.

## 10. Variant Presentation Control

The card text "Multiple options" is sourced from server-composed variant
presentation (`HasVariants` / `displayPriceText`). The audited Gram product reports
`HasVariants = false`. Actual variant controls such as Samsung and Dell products
report `HasVariants = true`. No evidence tied variant presentation to the 404.

## 11. Pre-Fix Request Trace

For a blank-slug item:

1. Discovery returned a visible SQL-accepted product with `slug = ""`.
2. `getProductCardHref` generated `/products/{ProductId}`.
3. Router matched `/products/:slug` and supplied the ProductId string as `slug`.
4. `ProductDetailsPage` called `fetchProductBySlug(ProductId)`.
5. The client requested `GET /api/storefront/products/{ProductId}`.
6. The controller interpreted the GUID text as a slug.
7. Mongo `FindBySlugAsync(tenantId, ProductId, true)` found no document.
8. Detail returned 404.

The old endpoint behavior remains reproducible directly: sending
`b55ddc92-...` to the slug endpoint returns HTTP 404.

## 12. Root Cause

The root cause was a frontend contract mismatch between card URL generation and
detail API selection. The card layer supported both slug and ProductId identifiers;
the detail page supported only slug identifiers. The backend already exposed the
correct tenant-scoped by-ID route, so no backend data-access capability was missing.

## 13. Why Slug Is Not Globally Guaranteed

`ApplicationDbContext` permits nullable historical slugs. The live tenant contains
three active records with empty strings in both SQL and Mongo projections. A card
must therefore either suppress otherwise valid records or retain the existing ID
fallback. Suppressing records would change visibility semantics; the ID fallback is
the safer compatibility path.

## 14. Current Slug Authoring Authority

Current product writes run through `ProductAuthoringOrchestrator` and
`ProductSlugAuthority`:

- Create resolves a deterministic tenant-unique slug before persistence.
- Update preserves a stored slug when no replacement is requested.
- A historical missing slug is deterministically derived during update.
- Tenant-local collision suffixing and PostgreSQL advisory locking prevent races.

Production controller and bulk lifecycle writes use this orchestrator. The direct
product add found under `DevelopmentCommerceHubSeedService` is development seeding,
not a public authoring path.

## 15. PostgreSQL Evidence

PostgreSQL confirms that all three affected products belong to Shopixy and satisfy
current public visibility gates. It also confirms one active default variant for
each. The Gram record has its valid slug, active Gram unit, and canonical variant.

Because SQL itself contains the blank historical slugs, this is not a stale Mongo
slug divergence. SQL authority correctly accepts the products as visible.

## 16. MongoDB Evidence

Mongo contains matching tenant/ProductId documents for all affected records, with
the same blank slug and matching `HasVariants = false`. Mongo also contains the
healthy Gram document with `Slug = realme-gt-neo-3`, its units, and variant.

The by-ID repository method scopes by both tenant and ProductId. Mongo was a valid
candidate source; only the pre-fix query field was wrong.

## 17. Elasticsearch Evidence

Elasticsearch supplied discovery candidates, after which
`ProductDiscoveryService` recomposed them through SQL authority. It did not invent
the ProductIds or bypass visibility. The three affected cards were valid discovery
results; their detail failure occurred after navigation.

## 18. Redis Evidence

Detail cache keys are separately tenant-scoped by slug and ProductId in
`CacheKeyBuilder`. The by-ID route uses the existing ProductId namespace and the
same storefront invalidation generation as slug detail.

At final inspection, the only matching Redis key was the Shopixy storefront-product
generation key; no stale product payload explained the 404. Cache was not the root
cause and no cache key or invalidation contract was changed.

## 19. SQL Detail Authority

Both slug and by-ID detail services use Mongo only as a candidate and invoke
`ApplyCurrentSqlProductDetailAuthorityAsync`. Cached DTOs are also recomposed from
current SQL authority before return. Switching endpoint selection therefore does
not make Mongo or Redis authoritative.

Relevant implementation:

- `StorefrontProductService.cs:136-205`
- `ProductReadRepository.cs:26-33`
- `ProductSearchSqlAuthorityOverlay`
- `ProductStorefrontVisibilityAuthority`

## 20. Tenant Isolation

`StorefrontProductsController.GetProductById` resolves the trusted storefront
tenant and calls `GetProductByIdAsync(tenantId, id, affiliateMode, ...)`.
Repository, cache key, and SQL overlay all retain that tenant.

Live negative checks for Shopixy product
`b55ddc92-14b2-0160-78e2-a9ebfe867f1b` returned HTTP 404 under both
`ahramstore.localhost:5174` and `greatone.localhost:5174`. Raw unverified
`X-Tenant-Id` requests returned `TENANT_REQUIRED` rather than trusting the header.

## 21. Remediation Decision

Selected: preserve canonical slug URLs when available and use the existing by-ID
API only for the ProductId fallback already emitted by cards.

Rejected for this incident:

- Live mass slug backfill: unnecessary mutation and redirect/collision risk.
- Treat every route value as a slug: preserves the defect.
- Make SQL-only detail without Mongo candidate: broad architecture change.
- Suppress blank-slug products: changes valid storefront visibility.
- Slug-then-ID fallback after 404: adds avoidable request latency and ambiguity.

## 22. Code Changes

- Added `ENDPOINTS.PRODUCT_BY_ID` in `endpoints.ts:22-23`.
- Added GUID-aware `fetchProductByIdentifier` in
  `storefrontApi.ts:165-178`.
- Updated `ProductDetailsPage.tsx:6,14,26-50` to load by the route identifier.
- Added focused frontend endpoint-selection tests.
- Added backend controller tenant-bound by-ID success/miss tests.

No API implementation change was required because the secure by-ID route and
service already existed.

## 23. Preserved Contracts

The remediation does not change:

- Product card URL preference for canonical slugs.
- Trusted host/domain tenant resolution.
- SQL visibility, category, pricing, stock, media, promotion, or affiliate rules.
- `HasVariants`, default variant, or canonical `VariantId` composition.
- Cart add/update/remove behavior.
- Checkout product/variant identity.
- Redis TTL, generation, invalidation, or key ownership.
- Search/discovery ranking and filtering.

## 24. Frontend Regression Tests

Command:

```text
npm test -- --run src/services/api/storefrontProductDetailApi.test.ts src/components/products/productCardRendererResolver.test.ts
```

Result: **10 passed, 0 failed** across two files.

Coverage proves canonical slug cards use
`/api/storefront/products/{slug}`, blank-slug ProductId cards use
`/api/storefront/products/by-id/{ProductId}`, and card href generation retains both
forms.

## 25. Backend Regression Tests

Command:

```text
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~PointOfSale.Tests.Storefront.StorefrontSearchReliabilityTests
```

Result: **4 passed, 0 failed**. The two added tests prove the resolved tenant is
passed to by-ID lookup for a blank-slug product and a tenant-scoped miss remains
404. Existing search reliability tests also passed. The build emitted existing
repository warnings but no errors.

## 26. Production Build

`npm run build` completed successfully:

- TypeScript compilation passed.
- Vite transformed 515 modules and emitted production assets.
- Existing chunk-size/dynamic-import and browsers-list freshness warnings remain;
  none are caused by this fix.

`git diff --check` reported no whitespace errors in the changed application and
test files.

## 27. Live Post-Fix Verification

Through `Host: shopixy.localhost:5174`:

| Request | Result |
|---|---|
| SPA `/products/b55ddc92-...` | 200, root document served |
| by-ID `b55ddc92-...` | 200, correct ProductId/default variant |
| by-ID `7dba5eca-...` | 200, correct ProductId/default variant |
| by-ID `a621601a-...` | 200, correct ProductId/default variant |
| slug `realme-gt-neo-3` | 200, Gram product |
| by-ID `cb81e893-...` | 200, same Gram ProductId |
| old slug call with `b55ddc92-...` | 404, reproduces old mismatch |
| Shopixy ID under AhramStore host | 404 |
| Shopixy ID under greatone host | 404 |

## 28. Scope and Evidentiary Limits

The exact historical screenshot/request trace was not retained, so the audit does
not claim that the currently healthy Gram record itself produced that screenshot.
It proves that Gram is healthy now and independently reproduces the same visible
failure class on three live Shopixy cards.

No browser automation runtime was available. The SPA route, focused endpoint
selection, production bundle, and real proxied APIs were verified separately. No
live create/update mutation was performed because the defect did not require data
repair.

## 29. Rollout and Monitoring

Deploy the storefront bundle; no API restart, migration, projection replay, cache
flush, or product rewrite is required. After deployment, monitor detail 404s split
by slug and by-ID route. A future maintenance task may backfill historical blank
slugs through `ProductSlugAuthority`, with redirect and collision planning, but it
is not required for list/detail correctness.

## 30. Certification

Certified invariants:

- Every currently listed Shopixy product has a working detail identity path.
- Nonblank slugs remain canonical and SEO-friendly.
- Historical blank-slug products use their stable ProductId fallback.
- By-ID detail remains tenant-scoped and SQL-authority recomposed.
- Gram, Piece, simple, and selectable-variant semantics are not conflated.
- Canonical variant, cart, and checkout identities are unchanged.
- No load-test or infrastructure workload was touched.

Final verdict: **PASS** for the implemented list/detail consistency contract and
live API behavior. Browser-render certification is supported by unit/build/live
endpoint evidence rather than an automated browser recording.
