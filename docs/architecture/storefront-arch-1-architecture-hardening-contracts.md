# SHOPIXY ARCH-1 Storefront Architecture Hardening Contracts

Status: Permanent architecture contract

Scope: Storefront runtime, storefront product projection, Mongo/Elasticsearch read models, DTO contracts, cart/checkout runtime admission, and storefront frontend consumers.

Non-goals: No new UX, no new commerce features, no business expansion.

Final certification: ARCHITECTURE REQUIRES HARDENING

Reason: Runtime consumers are harmonized around the current pricing and purchasability authorities, but ARCH-1 discovered remaining governance work that must become automated policy: database-level ProductUnit value constraints, broader static analysis for forbidden business logic outside authority classes, and projection-schema admission checks that reject invalid historical documents before runtime mapping.

## 1. Invariant Catalog

| Invariant | Owner | Enforcement Layer | Failure Behavior | Recovery |
|---|---|---|---|---|
| SQL is the commerce truth for products, product units, variants, publication, stock, and tenant lineage. | PostgreSQL domain model and application services | EF model, domain/application services, SQL authority overlays | Runtime must fail closed or suppress stale derivative data. | Recompose projection from SQL; tombstone derivative reads when SQL no longer proves visibility. |
| Storefront projection is derivative only. | Projection pipeline | Projection consumers, `ProductSearchSqlAuthorityOverlay`, mutation boundary | Projection data cannot create business truth; stale data is discarded or tombstoned. | Rebuild projection from SQL and invalidate Redis/Elasticsearch. |
| Exactly one active DefaultUnit per product. | ProductUnit SQL authority | Unique filtered SQL index and ProductUnits mutation service/controller | Mutation rejected or previous default is demoted. | Repair duplicate defaults in SQL, publish ProductUnitsChanged, rebuild product projection. |
| Exactly one active BaseUnit per product. | ProductUnit SQL authority | Unique filtered SQL index and ProductUnits mutation service/controller | Mutation rejected. | Repair SQL ProductUnits, then rebuild projection. |
| ProductUnit binding is unique per product/unit. | ProductUnit SQL authority | Unique filtered SQL index | Mutation rejected. | Remove duplicate inactive/historical rows; replay product-unit event. |
| BaseUnit ConversionFactor is exactly 1. | ProductUnit mutation authority | Application validation today; must become SQL check constraint | Mutation rejected at application layer; historical invalid rows may still exist. | Data repair plus migration adding DB constraint. |
| ProductUnit ConversionFactor is positive. | ProductUnit mutation authority | Application validation today; must become SQL check constraint | Mutation rejected at application layer; historical invalid rows may still exist. | Data repair plus migration adding DB constraint. |
| Storefront simple-product price comes only from DefaultUnit.SalePrice. | `ProductStorefrontVisibilityAuthority` | Runtime authority helper, DTO mappers, architecture tests | Missing or non-positive DefaultUnit price makes product unavailable/not purchasable. | Fix SQL DefaultUnit.SellingPrice and rebuild projections. |
| `StorefrontEffectivePrice` is computed only. | `ProductDocument` plus `ProductStorefrontVisibilityAuthority` | `[BsonIgnore]` computed property | It must never be persisted or accepted from a client/projection write. | Remove persisted field, rebuild Mongo/Elasticsearch from SQL. |
| Variant storefront price is computed from variant effective price only. | Variant SQL authority and projection document | `ProductVariant.EffectiveOnlinePrice`, `ProductVariantDocument.EffectivePrice`, purchasability authority | Non-positive variant price makes variant unavailable. | Fix SQL variant pricing, replay variant event, rebuild projection. |
| Storefront product purchasability has one authority. | `ProductStorefrontVisibilityAuthority` | Runtime service/controller/cart/wishlist/discovery calls | Consumers must not duplicate `isActive && visible && stock && price` rules. | Replace local logic with authority call and add regression coverage. |
| Product-level variant products require variant selection before purchase. | `ProductStorefrontVisibilityAuthority` | Runtime availability DTOs and cart admission | Product card/detail may show availability, but direct product purchase fails closed until variant selected. | Select canonical active priced variant for presentation only; cart uses selected VariantId. |
| Frontend never owns price, stock, availability, or variant matching truth. | Backend API DTO contract | Storefront DTOs, frontend type contracts, architecture tests | Frontend can format or disable controls, but backend rejects invalid cart/checkout intents. | Move logic back to backend authority and update DTO. |
| Checkout reloads current SQL authority. | Checkout runtime | Checkout service SQL reload/recalculation | Stale cart totals or projection totals are rejected/recalculated. | Revalidate checkout from SQL; user retries with current totals. |
| Cart mutation reloads current product authority before accepting item changes. | Cart runtime | Cart service mutation lock and SQL authority overlay | Invalid product/variant/price/stock fails closed. | Refresh cart from backend; remove or adjust invalid lines. |
| Mongo documents must be tenant-owned. | Projection pipeline | TenantId in projection documents and Mongo filters | Missing/mismatched tenant identity rejects event/projection or hides document. | Rebuild for tenant from SQL authority. |
| Elasticsearch and Redis are read acceleration only. | Runtime read model boundary | SQL authority overlay before customer-facing reads; cache invalidation | Stale hits are recomposed, deleted, or bypassed. | Reindex from SQL/Mongo after SQL overlay; invalidate cache. |
| Publication state is backend-owned. | Product SQL authority | Product `PublishStatus`, `IsActive`, `IsOnlineVisible`, lifecycle authority | Unpublished/inactive/offline products fail closed in public runtime. | Publish/reactivate in admin, then rebuild projection. |
| Variant lifecycle is backend-owned. | `ProductVariantLifecycleAuthority` | SQL lifecycle checks and projection consumers | Merged/split/superseded/inactive variants cannot become public purchase candidates. | Repair lifecycle lineage, replay variant event, rebuild projection. |
| Stock state is backend-owned. | Inventory SQL authority | ProductVariantStock, stock mutation support, stock projection events | Non-positive stock blocks purchase unless explicit backorder policy allows it. | Replay stock events from SQL; rebuild projection; reconcile stock ledger. |
| Projection ordering must reject stale events. | Projection consumers | event fences, mutation versions, tombstones, last-event metadata | Duplicate/stale events are ignored; stale delete/create cannot resurrect public data. | Replay event stream or perform SQL-authoritative rebuild. |
| Runtime DTOs are presentation contracts, not authority inputs. | Backend API | DTO mappers and controller request admission | Client-sent price/stock/availability is ignored or rejected. | Reissue DTO from backend authority. |

## 2. Domain Guard Report

Domain protections that must remain permanent:

| State | Required Guard | Current Enforcement | Required Hardening |
|---|---|---|---|
| ProductUnit identity | Unique live ProductId/UnitId | SQL filtered unique index | Keep architecture test. |
| DefaultUnit cardinality | One active default per product | SQL filtered unique index plus mutation demotion | Add invariant test for no default deletion without replacement on published storefront products. |
| BaseUnit cardinality | One active base per product | SQL filtered unique index plus mutation validation | Add SQL check for base conversion factor. |
| ConversionFactor | Positive; base unit equals 1 | Application validation | Add SQL check constraints. |
| SellingPrice | DefaultUnit selling price must be positive for public simple products | Runtime fail-closed in purchasability authority | Add publish-time validation and projection admission audit. |
| Variant active state | Only active/current variants are public candidates | `ProductVariantLifecycleAuthority` and projection consumers | Add mutation tests for merged/split/superseded variants. |
| Publication state | Public runtime requires active, online-visible, published SQL truth | SQL overlay and lifecycle checks | Add projection rebuild test that removes stale public documents. |
| Stock state | SQL stock authority owns quantity and backorder policy | stock services/events and runtime authority | Add mutation tests for stale stock event ordering. |
| Tenant ownership | TenantId must match across SQL, projection, cache, DTO | tenant filters and projection event admission | Keep cross-tenant regression tests. |

## 3. Boundary Protection Report

Boundary contract:

```
SQL -> Projection -> Mongo/Elasticsearch/Redis -> DTO -> Frontend
```

Rules:

| Boundary | Allowed | Forbidden |
|---|---|---|
| SQL to projection | Projection recomposes current SQL truth and writes derivative read shape. | Trusting event payload as full business truth when current SQL disagrees. |
| Projection to Mongo | Upsert/tombstone reviewed derivative documents with tenant and event metadata. | Direct business decisions from Mongo writes; writing computed effective price as stored truth. |
| Mongo/Elasticsearch to DTO | Re-enter SQL overlay where required and build pricing/availability presentation on backend. | Returning stale projection prices, availability, variants, or publication state as final authority. |
| DTO to frontend | Frontend formats values and submits intent IDs/quantities/options. | Client-calculated price, stock, availability, variant identity, discount, or publication truth. |
| Frontend to cart/checkout | Submit product/variant/quantity intent only. | Submit totals, current prices, stock decisions, or selected business state as trusted values. |

## 4. Projection Hardening Report

Projection fields that must exist:

- Tenant identity: `TenantId`.
- Source identity: `ProductId`, `VariantId` for variant rows.
- Source freshness: `LastEventId`, `LastEventOccurredAtUtc`, `LastEventVersion`, stock-specific event metadata, tombstones for deleted variants.
- Read-model evolution: `ReadModelName`, `ReadModelSchemaVersion`, `ProjectionVersion`, `ProjectionConvergedAtUtc`.
- Runtime-required shape: `DefaultUnit`, `Units`, `Variants`, `IsActive`, `IsOnlineVisible`, `IsSearchable`, `TrackStock`, `AllowBackorder`, `StockQuantity`, product media/category/template fields.

Fields that must be recomputed:

- Simple-product storefront effective price from `DefaultUnit.SalePrice`.
- Visible price from default/first active priced variant or DefaultUnit.
- Variant effective price from variant `OnlinePrice` fallback to `Price`.
- Availability and purchasability DTOs from `ProductStorefrontVisibilityAuthority`.
- Pricing presentation DTOs from backend pricing presentation mapper.
- Stock labels and maximum quantity from backend availability presentation mapper.

Fields that must never become authority:

- Mongo `Price`, `OnlinePrice`, `EffectivePrice`, `StorefrontVisiblePrice`, projection stock fields, promotion overlay fields, Elasticsearch product prices, Redis cart snapshots, DTO `currentPrice`, frontend `price` props.

Projection failures requiring rebuild:

- Missing or mismatched TenantId.
- Missing source ProductId or VariantId.
- Read-model schema/projection version behind a strict runtime requirement.
- Missing DefaultUnit for a public simple product.
- DefaultUnit price <= 0 for a public simple product.
- No active priced variant for a public variant product.
- Stale event ordering conflict or tombstone conflict.
- Product active/online-visible/publication mismatch between SQL and projection.
- Projection contains a persisted `StorefrontEffectivePrice`.

## 5. Governance Report

Future developers can accidentally reintroduce business logic in:

- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Storefront/*`
- `PointOfSale.API/Controllers/Storefront*`
- `PointOfSale.Infrastructure/Elasticsearch/Services/*Discovery*`
- `PointOfSale.Infrastructure/Consumers/*Product*`
- `PointOfSale.Storefront/src/components/products/*`
- `PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx`
- `PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx`
- Storefront hooks that adapt product discovery/list/homepage results.

Guardrails:

- Every customer-facing price/availability/purchasability path must call the named backend authority or consume a DTO produced by it.
- Every projection/reindex/repair path must recompose current SQL authority before writing public product read models.
- Every frontend product component must consume `pricing` and `availability` DTOs and never calculate discount, stock, availability, or variant matching.
- Every cart/checkout mutation must reload current backend authority and reject stale client/projection state.
- Every new product projection writer must go through the reviewed mutation boundary or product SQL authority projection writer.

## 6. Static Analysis Recommendations

Permanent automated rules:

| Rule | Detection |
|---|---|
| No duplicated storefront price logic | Flag `OnlinePrice ?? Price`, `promotionDiscountValue / 100`, `calculateDiscountedPrice`, or `Math.max(0, basePrice` outside approved pricing mappers/authorities. |
| No duplicated availability logic | Flag local combinations of `isActive`, `isOnlineVisible`, `inStock`, `hasVariants`, `totalStock`, `canAddToCart` outside approved availability mappers/authorities. |
| No frontend business truth | Flag frontend writes or calculations involving price, discount, stock, publication, or variant resolution outside formatting and API intent submission. |
| No persisted `StorefrontEffectivePrice` | Flag any setter, DTO input, Mongo field, EF migration column, or BSON persisted property named `StorefrontEffectivePrice`. |
| No projection authority writes | Flag direct full product read-model replacement outside approved mutation boundary and SQL projection writer. |
| No stale projection runtime | Flag Storefront runtime reads returning Mongo/ES product candidates without SQL overlay where the surface is customer-facing purchase/discovery. |
| No cart/checkout client totals | Flag controller/service paths accepting client price, stock, subtotal, discount, or total as authority. |
| No ProductUnit invalid values | Flag missing SQL check constraints for positive conversion factor and base conversion factor = 1. |

## 7. Architecture Documentation Contracts

Pricing Authority Contract:

- Simple Storefront product price is `DefaultUnit.SalePrice`.
- Variant Storefront price is variant effective price.
- `StorefrontEffectivePrice` is a computed accessor only.
- Pricing presentation is built on the backend.
- Frontend formats backend pricing; it does not calculate it.

Projection Contract:

- Projection is derivative.
- Projection must carry tenant/source/freshness metadata.
- Projection may be rebuilt from SQL at any time.
- Projection failures fail closed by suppressing/tombstoning public visibility.
- Projection must not store computed effective price as authority.

Runtime Contract:

- Product runtime reads re-enter current SQL authority when returning customer-facing product candidates.
- Cart and checkout reload authoritative SQL/product/variant/stock/price state before accepting mutations or final totals.
- Runtime consumers delegate purchasability to `ProductStorefrontVisibilityAuthority`.
- Runtime DTOs carry backend-composed pricing and availability.

Boundary Contract:

- SQL owns truth.
- Projection transforms truth.
- Mongo/Elasticsearch/Redis accelerate reads.
- DTOs carry backend decisions.
- Frontend presents decisions and submits intent only.

Invariant Catalog Contract:

- This document is the permanent Storefront invariant catalog.
- New Storefront work must update this contract and architecture certification tests in the same change when it changes authority boundaries.
- New features cannot bypass these invariants.

## 8. Regression Governance

Permanent regression suites:

- Architecture tests: source-scanning tests for boundary rules and forbidden patterns.
- Invariant tests: ProductUnit default/base uniqueness, conversion factor validity, positive public DefaultUnit price.
- Projection tests: SQL recomposition, tombstone ordering, stale event rejection, read-model schema convergence.
- Authority tests: `ProductStorefrontVisibilityAuthority`, pricing presentation mapper, availability presentation mapper.
- Consumer consistency tests: product detail, listing, discovery, homepage, wishlist, cart, checkout all agree on pricing/availability DTOs.
- Mutation tests: intentionally mutate price/availability logic and confirm architecture tests fail.
- Historical-data admission tests: invalid Mongo documents fail closed and trigger rebuild/repair signals.

## 9. Architecture Certification

Quality gates:

| Gate | Result |
|---|---|
| Single Commerce Authority | PASS for runtime paths inspected; SQL remains source of truth. |
| Single Purchasability Authority | PASS for current runtime consumers using `ProductStorefrontVisibilityAuthority`. |
| Single Projection Contract | PASS conceptually; REQUIRES more automated projection admission checks. |
| Single Runtime Contract | PASS for current DTO/frontend harmonization. |
| No Authority Leakage | PASS for inspected runtime paths; REQUIRES static analysis expansion. |
| No Business Duplication | PASS for inspected pricing/availability consumers; REQUIRES mutation tests. |
| Invariants Protected | PARTIAL: cardinality protected; value constraints require DB hardening. |
| Boundaries Hardened | PARTIAL: runtime overlay exists; historical invalid projection admission requires hardening. |

Final certification:

ARCHITECTURE REQUIRES HARDENING
