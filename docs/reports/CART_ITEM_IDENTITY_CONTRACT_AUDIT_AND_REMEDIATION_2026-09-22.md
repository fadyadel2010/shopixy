# Cart Item Identity Contract Audit and Remediation - 2026-09-22

## 1. Executive Summary

The storefront cart had two incompatible identity contracts. Variant products
persisted and mutated by `VariantId`, while simple products could be written with
`VariantId = null` even though every sellable product has a canonical SQL variant.
The storefront later preferred variant-scoped mutation routes, and checkout treated
null identifiers as legacy data.

The repository now implements one contract: every newly admitted cart line is a
sellable SKU identified by the server-authoritative `VariantId`. A simple-product
add may omit `VariantId`; the backend resolves, validates, persists, returns, and
subsequently mutates the canonical active variant. Caller-supplied simple-product
variant identifiers are assertions only and must match that canonical variant.

Focused backend tests pass 10/10, focused storefront tests pass 6/6, and the
storefront production build succeeds. Live HTTP/M4.5 evidence could not be
collected because no API listener or Docker runtime is available. Verdict:
**CONDITIONAL PASS**.

## 2. Objective

Establish and prove one coherent identity through:

`Add -> Redis persist -> GET -> update -> GET -> delete -> GET/revalidation`.

The repair must preserve tenant isolation, stock and price authority, existing
variant-product behavior, Redis cart identity, and load-test assets.

## 3. Scope

Reviewed and remediated:

- Storefront cart DTO and request contracts.
- `CartService` add, merge, update, delete, read revalidation, and Redis persistence.
- SQL-overlaid product/variant authority.
- Storefront API routes, state store, and cart-page mutation calls.
- Checkout's downstream interpretation of `VariantId`.
- Legacy Redis lines with a null simple-product `VariantId`.
- Focused backend and frontend regressions.

POS carts and CommerceHub carts are separate domains and were not changed.

## 4. Worktree Discipline

The shared worktree was already heavily dirty, including modified and untracked
files under `tests/load`. Those unrelated changes were not reset, rewritten, or
claimed by this audit. This work did not edit load workloads, thresholds, seed
identities, manifests, timing, or orchestration.

Because `git diff --name-only -- tests/load` was already non-empty, this report can
only certify that the cart-identity remediation itself did not intentionally touch
those files, not that the shared worktree's load directory equals `HEAD`.

## 5. Evidence Reviewed

Primary evidence:

- `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:423-428`
- `PointOfSale.Infrastructure/MongoDB/Documents/ProductVariantDocument.cs:60-69`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:121-139,281-368`
- `PointOfSale.Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs:25-85`
- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.API/DTOs/Cart/CartDtos.cs`
- `PointOfSale.API/Controllers/StorefrontCartController.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs:4907-5030`
- `PointOfSale.Storefront/src/store/cartStore.ts`
- `PointOfSale.Storefront/src/components/cart/CartItemRow.tsx:54-87`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts:232-295`

## 6. Existing Architecture

Storefront carts are Redis-first, TTL-based documents. `CartDto.Items` is serialized
to a tenant-and-owner-scoped Redis key; there is no relational storefront `CartItem`
authority. PostgreSQL remains authoritative for current product, variant, pricing,
and stock state. Mongo provides the candidate read projection, after which
`ProductSearchSqlAuthorityOverlay.ApplyAsync` recomposes current SQL truth.

This means the correct repair belongs in cart admission and Redis read migration,
not in a new relational cart table or database migration.

## 7. Canonical Contract Decision

Selected contract: **Model B - universal SKU identity**.

- `ProductId` identifies and groups the product.
- `VariantId` identifies the sellable SKU and cart line.
- Products with selectable variants require a caller-selected `VariantId`.
- Simple products allow omission on add, but the server resolves and returns their
  canonical default variant.
- Canonical persisted and returned cart lines always carry `VariantId`.
- Null remains representable only to read and upgrade pre-deployment Redis data.

## 8. Why Model B

Model B follows existing repository authority rather than introducing a new model:

- Product projection comments require at least one default variant for every product.
- `ProductVariantDocument.VariantId` is explicitly the cart and checkout identifier.
- Orders store `VariantId` as source of truth.
- Checkout calls null cart identifiers legacy and maps them to a default variant.
- Variant-scoped update/delete endpoints already exist.
- `CartItemRow` passes the response `variantId` back to the cart store.

Keeping null as the normal simple-product identity would preserve the defect and
continue to split add, persistence, frontend mutation, and checkout semantics.

## 9. Request and Response Contract

`AddCartItemRequest.ProductId` is required to locate the tenant/product authority.
For selectable-variant products, `VariantId` is mandatory. For simple products it
is optional; if supplied, it must equal the server-resolved canonical variant.

`CartItemDto.VariantId` remains nullable in C# and optional in TypeScript solely for
rolling migration compatibility. New successful add responses populate it for both
product shapes.

## 10. Pre-Fix Add Trace

Before remediation, variant resolution was entered only when
`product.HasVariants` was true. Inside that branch, the code again checked
`product.HasVariants` before attempting a default simple-product fallback. The
fallback was therefore unreachable. For a simple product, local `variant` remained
null, the cart merge fell back to `ProductId`, and the newly persisted line received
`VariantId = null`.

The immediate add could appear successful, but it emitted an identity that did not
match canonical variant-scoped update/delete behavior.

## 11. Root Cause

The root cause was control-flow placement, amplified by stale mixed-contract
comments: default-variant resolution for simple products was nested in the
selectable-variant branch. Tests covered variant products and cart persistence but
did not assert that a simple add with no submitted `VariantId` returned a canonical
identifier and completed the full mutation lifecycle.

## 12. Remediated Add Trace

`CartService.AddItemInternalAsync` now:

1. Loads the product by tenant and product id.
2. Rebuilds current product, variant, price, and stock truth from SQL.
3. Preserves mandatory selected-variant validation for `HasVariants = true`.
4. Evaluates simple-product storefront purchasability.
5. Resolves default-active, then active fallback, simple-product variant through
   `ResolveCanonicalSimpleProductVariant` (`CartService.cs:2213`).
6. Rejects any supplied mismatched identifier (`CartService.cs:477-483`).
7. Uses the canonical identifier for stock checks, merge lookup, persistence,
   response shaping, and events (`CartService.cs:532-669`).

## 13. Persistence Authority

`SaveCartToRedisAsync` serializes the resulting `CartDto`. New lines set
`CartItemDto.VariantId` from the resolved variant before the write. The line key is
therefore canonical before serialization; GET does not need to invent identity for
new carts.

No relational schema or migration is needed. Redis TTL and cart key ownership are
unchanged.

## 14. GET and Revalidation

`GetCartAsync` reads Redis, validates cart ownership/shape, then invokes
`RevalidateCartProductsAsync` (`CartService.cs:224-233`). Revalidation reloads each
product through the tenant-scoped SQL overlay.

For simple products it now:

- Removes lines whose current product is not purchasable.
- Removes lines when no active canonical variant exists.
- Upgrades a null legacy identifier to the canonical `VariantId` in place.
- Removes a non-null but noncanonical simple-product line.
- Refreshes stock and price and persists any repair.

The existing `CartId`, tenant, owner, and Redis key remain stable.

## 15. Update Contract

`UpdateItemByVariantAsync` locates the line by `VariantId` only. It reloads current
SQL authority and now handles both product shapes:

- Selectable variants use `EvaluateVariant`.
- Simple products require the route id to equal the canonical simple variant and
  use simple-product purchasability/stock authority.

Stock limits, maximum quantity, mutation locking, totals, events, Redis persistence,
and existing fail-closed behavior remain in force.

## 16. Delete Contract

`RemoveItemByVariantAsync` already used `VariantId` as the line identifier and did
not require product-shape-specific changes. Once add and migration preserve the
simple-product identifier, the same delete route works for both shapes.

Legacy product-scoped endpoints remain available for compatibility, but canonical
storefront responses drive the client through variant-scoped routes.

## 17. Frontend Path

The storefront path is coherent without a runtime logic rewrite:

1. `addCartItem` sends product id and optional selected variant.
2. The API response contains the canonical simple or selected variant id.
3. Zustand stores the response without synthesizing identity.
4. `CartItemRow` passes `item.variantId` for quantity and remove actions.
5. `cartStore` selects `updateCartItemByVariant` and
   `removeCartItemByVariant` when that identifier is present.

Contract comments in types, endpoints, API methods, and store actions now describe
simple and selectable-variant products consistently.

## 18. Checkout Continuity

Checkout already expects SKU identity. It snapshots `CartItemDto.VariantId`, loads
authoritative variants, reprices and reserves stock by variant, and writes order
items with `VariantId`. Its null-to-default mapping is explicitly a legacy bridge.

The repair moves normal simple-product carts onto checkout's primary path and leaves
the legacy bridge available during Redis TTL convergence.

## 19. Tenant and Association Security

The server never adopts an arbitrary simple-product `VariantId`. Canonical variants
come from the current `ProductDocument` only after the SQL overlay queries the
requested tenant and product and filters deleted/non-current variants. A submitted
identifier must equal that resolved value.

Focused tests prove random and other-tenant variant identifiers are rejected before
cart creation. This prevents attaching a valid foreign SKU to a current-tenant
product or persisting caller-controlled identity.

## 20. Legacy Migration Behavior

Two rolling-deployment paths are covered:

- GET upgrades an existing null simple-product line in place and persists it.
- Add against an existing null simple-product line assigns the canonical id before
  merge, increments that line, and does not create a duplicate.

Migration is lazy and bounded by cart access. It requires no bulk Redis scan and no
dual-write period. Carts naturally converge within their existing TTL.

## 21. Edge and Failure Cases

Regression coverage includes:

- Simple add without a client `VariantId`.
- Selectable-variant add/update/delete.
- Random and cross-tenant mismatches.
- Inactive, deleted, and unpriced/unpurchasable variants.
- Insufficient stock.
- Repeated add merging by canonical identity.
- Direct add to a legacy null-id line.
- GET-driven legacy migration with stable cart identity.

Failures occur before a new invalid cart is persisted.

## 22. Files Changed

- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.API/Services/Cart/ICartService.cs`
- `PointOfSale.API/DTOs/Cart/CartDtos.cs`
- `PointOfSale.API/Controllers/StorefrontCartController.cs`
- `PointOfSale.Storefront/src/types/cart.ts`
- `PointOfSale.Storefront/src/services/api/endpoints.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/store/cartStore.ts`
- `PointOfSale.Storefront/src/store/cartStore.persistence.test.ts`
- `PointOfSale.Tests/CartItemIdentityContractTests.cs`
- This report.

## 23. Backend Verification

Command:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --filter "FullyQualifiedName~CartItemIdentityContractTests" --no-restore --logger "console;verbosity=minimal"
```

Result: **10 passed, 0 failed, 0 skipped** on `net9.0`.

The test fixture uses the real `CartService`, EF InMemory SQL authority rows, mocked
Mongo candidate lookup, and an in-memory implementation of the Redis calls. It
asserts both returned DTOs and serialized persisted cart state.

## 24. Frontend and Build Verification

Commands and results:

```powershell
npx vitest run src/store/cartStore.persistence.test.ts --pool=threads --maxWorkers=1
# 1 file passed, 6 tests passed

npm run build
# TypeScript and Vite production build succeeded; 515 modules transformed
```

The initial default-fork Vitest run failed to start a worker under concurrent build
load. The sequential single-thread-pool rerun passed. Vite emitted only existing
dependency/chunk-size warnings.

The adjacent `StorefrontCartCoupon*` .NET suite was also attempted: 8 tests failed
during SQLite schema creation before assertions with
`SQLite Error 1: 'no such function: btrim'`. This is the existing
PostgreSQL-specific schema/test-environment incompatibility documented elsewhere;
it is not caused by the cart identity changes.

## 25. Runtime and Load Evidence

Live runtime evidence is unavailable in this session:

- `docker` is not installed/available in the shell.
- `http://localhost:5010/health/live` refused connection.
- `http://localhost:5010/health` refused connection.
- No M4.5 script/report was found to execute against a running environment.

No load script, threshold, seed, manifest, workload, or timing was changed to work
around this limitation. Required follow-up is a deployed/live simple-product HTTP
walkthrough that records POST response `variantId`, Redis persistence, GET, PUT by
that id, second GET, DELETE by that id, final GET, and tenant-negative behavior.

## 26. Final Certification

**CONDITIONAL PASS**

Code, persistence, frontend routing, checkout continuity, tenant association, and
focused regression evidence support Model B as the implemented canonical contract.
The original null simple-product identity defect and its direct-add legacy duplicate
risk are remediated.

Conditions before an unconditional production pass:

1. Run the live HTTP lifecycle against an environment with PostgreSQL, Redis,
   MongoDB, and the API healthy.
2. Capture the persisted Redis line or equivalent trusted runtime evidence showing
   the simple-product canonical `VariantId` survives write/read.
3. Execute the designated M4.5 gate without changing its frozen workload or
   thresholds, if/when that gate is made available.
