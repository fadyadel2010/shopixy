# Product Management Capability (POS Admin)

Reverse-engineered from implementation. Documentation under `docs/architecture/*` is
NOT authoritative here; every statement below was verified against the C# backend
(`PointOfSale.API` / `PointOfSale.Infrastructure` / `PointOfSale.Core`) and the
`PointOfSale.Web` React admin UI. Discrepancies with docs are listed in
"Documentation Validation" at the end.

---

# Purpose

Product Management is the POS-Admin capability that owns the **catalog master record**
for a tenant. It defines what a sellable item IS — its identity (name, SKU, barcode),
its classification (category), its commercial terms (prices, tax, units, shipping),
its presentation for e-commerce (SEO, media, publication status, templates), and its
eligibility for downstream channels (storefront, CommerceHub, affiliate, search).

It exists to solve one core problem: there must be a **single authoritative source of
truth for product identity and publication policy**, while every other system
(inventory, pricing engines, storefront read models, search index, affiliate,
analytics) consumes projections derived from that truth. Product Management writes the
truth; it does not own stock quantities, order lines, payments, or ledger postings.

The product aggregate is the *nucleus* other capabilities attach to. It is intentionally
narrow: it authors and publishes the product; it does not fulfill it.

---

# Business Scope

## Belongs to Product Management

- **Product creation** — `POST /api/products` (ProductsController.CreateProduct).
- **Product editing** — `PUT /api/products/{id}` (UpdateProduct).
- **Product publishing / visibility** — carried by `PublishStatus` (Draft=0,
  Published=1, Archived=2) and `IsOnlineVisible`, `IsSearchable`, `IsFeatured`. There is
  **NO dedicated publish endpoint**; publishing is a field mutation inside UpdateProduct.
- **Product archiving** — implicit via `PublishStatus = Archived`, or explicit soft
  disable via `PUT /api/products/{id}/deactivate` (sets `IsActive=false` only).
- **Product status** — `IsActive` (POS usability), `PublishStatus` (e-commerce lifecycle),
  `IsOnlineVisible` (storefront visibility gate), `IsDeleted` (soft-delete tombstone).
- **Product deletion (soft delete)** — `DELETE /api/products/{id}`; sets IsDeleted,
  IsActive=false, IsOnlineVisible=false, PublishStatus=Archived. Hard delete is
  impossible (see Deletion rules).
- **Media** — ProductMedia table via `ProductMediaController`
  (`/api/admin/products/{productId}/media`). Product-level media only; variant media is a
  separate field on ProductVariant.
- **Categories** — a product references exactly one live Category (FK `CategoryId`,
  required, non-empty). Validated against live SQL lineage.
- **Brands / Manufacturer / Tags / Features / FAQs** — stored on the Product row
  (Tags/Features/Faqs as JSONB). Tags also carry a legacy comma-string `Tags` column.
- **Attributes** — product-level structured Features/Faqs are product-owned. Variant-level
  attribute name/value pairs live on ProductVariant (not Product).
- **Pricing relationship (authored, not authoritative for channels)** — `SalePrice`
  (POS), `OnlinePrice` (storefront source-of-truth), `PurchasePrice`, tax, units. NOTE:
  for simple products the *real* price lives on the default ProductVariant; the Product
  row mirrors it (see Authority Boundaries).
- **Inventory relationship (reference only)** — a product may be `TrackStock`; actual
  quantities live on ProductVariantStocks / WarehouseStocks, owned by Inventory.
- **SEO relationship** — Slug, MetaTitle, MetaDescription, MetaKeywords.
- **Shipping relationship** — Weight/Length/Width/Height + units (authored; used by
  shipping quotes in checkout).
- **Storefront relationship** — projected to MongoDB ProductDocument + Elasticsearch;
  Product Management authors the fields, Storefront reads the projection.
- **CommerceHub relationship** — CommerceHub reads the product projection / eligibility;
  Product Management does not list products in CommerceHub (that is a separate capability).
- **Search relationship** — products are indexed into Elasticsearch by the projection
  pipeline; Product Management authors the fields, the search consumer indexes them.
- **Analytics relationship** — product is a dimension in analytics (sales, movement
  reports) but analytics does not write product truth.
- **Multi-unit pricing** — ProductUnits (base + alternate units with conversion factors)
  via `ProductsController` unit endpoints. Conversion-factor edits are guarded once a unit
  has transaction history (ValidationService.CanModifyProductUnitConversionAsync).
- **Affiliate enablement** — `AdminProductAffiliateController` manages a
  product-level AffiliateProductPolicy (commission, category, template). This controller
  is Product-Management-adjacent but delegates all writes to IAffiliateService; it is
  documented here only as a related surface.
- **AI-generated content** — `AiGenerated` flag + `AiMetadataJson`; accepted from the AI
  product generator modal and stored verbatim.

## Does NOT belong to Product Management

- **Stock quantity truth** — Inventory / WarehouseStocks. Product Management only sets
  TrackStock / AllowBackorder flags and the `HasVariants` container shape.
- **Order / checkout / payment / ledger** — separate capabilities. Product is referenced
  by order lines but never mutated by them.
- **Price *calculation*** at checkout (promotions, tax computation) — promotions are
  overlaid at projection time (PromotionVisibilityProjectionSupport), not stored on the
  product.
- **Variant business rules + SKU-level stock** — owned by ProductVariantsController /
  variant services. Product Management delegates variant alignment to
  IProductVariantService.EnsureDefaultVariantAlignedAsync.
- **Marketplace (CommerceHub) listing lifecycle** — separate controller
  (AdminCommerceHubController). Product Management only feeds eligibility.
- **Search query/ranking** — owned by the search service; Product Management only supplies
  indexed fields.
- **Accounting** — never touched by product mutations.

---

# Authority Boundaries

The platform enforces a strict **SQL-authority** model. PostgreSQL is the source of
truth for product identity, visibility, and presentation. Everything else is a derived
read model.

## Authority (PostgreSQL — writable truth)
- `Products` table (PointOfSale.Infrastructure/Data/Configurations/ProductConfiguration.cs).
- `ProductMedia`, `ProductVariants`, `ProductVariantStocks`, `ProductUnits`,
  `AffiliateProductPolicies` (related).
- Written only inside ProductsController / ProductVariantsController /
  ProductMediaController / AdminProductAffiliateController (latter delegates to
  IAffiliateService).
- Enforced constraints: `TenantId` NOT NULL (ITenantOwned); `CategoryId` required and must
  reference a **live** category with valid SQL parent lineage (CategoryLineageAuthority);
  `ProjectionMutationVersion` monotonic (IProjectionVersioned) — advanced by DbContext on
  every change and used to reject stale projections.

## Evidence (events — outbox pattern)
- Product mutations publish IntegrationEvents inside the SAME SQL transaction
  (MassTransit outbox): `ProductCreatedEvent`, `ProductUpdatedEvent`,
  `ProductPublishStatusChangedEvent`, `ProductArchivedEvent`, `ProductDeletedEvent`,
  plus `ProductMediaChangedEvent`, variant events, and a Marketplace outbox envelope
  (`IMarketplaceEventOutboxService.EnqueueAsync` for `ProductUpdatedEvent`).
- Events are NOT authoritative; they are delivery vehicles. If an event and SQL disagree,
  SQL wins (SQL authority overlay, below).

## Presentation / Projection (derived, never authoritative)
- **MongoDB `products` collection (ProductDocument)** — storefront read model. Written by
  `ProductCreatedEventConsumer` / `ProductUpdatedEventConsumer` /
  `ProductMediaChangedEventConsumer`. Governed by
  `ProductSqlAuthorityProjectionWriter` + `ProductSearchSqlAuthorityOverlay`, which
  **recompose the document from live SQL** before persisting it to Mongo. If SQL says the
  product is inactive or not published, the Mongo writer issues a ReviewedVisibilityTombstone
  instead of an upsert.
- **Elasticsearch** — search index, written from the re-composed SQL-authoritative
  ProductDocument by `ProductSearchDocumentMapper` / `IProductSearchIndexService`.
- **Redis** — optional cache in front of the storefront read path
  (StorefrontProductService). Cache misses/errors are non-fatal; SQL re-composition is the
  safety net.

## Who owns what (explicit)
- **Product truth (identity, publication, media, category, authored price fields):**
  Product Management (PostgreSQL).
- **Inventory truth (quantities, reservations):** Inventory capability (WarehouseStocks).
- **Pricing truth at sale time (promo/tax computation):** Promotion/Tax engines, overlaid
  at projection/checkout, not stored on product.
- **Publication gate decision (visible online?):** SQL `IsActive && IsOnlineVisible &&
  PublishStatus==Published` — computed by the SQL authority overlay, not by the event.
- **Marketplace eligibility:** CommerceHub reads product eligibility; listing lifecycle
  owned by CommerceHub.
- **Search indexing:** Search capability consumes the projection.
- **Analytics:** reads product as dimension.
- **Media storage truth:** ProductMedia SQL rows (URLs only; the file bytes live in external
  object storage, not in this system).

KEY HIDDEN BEHAVIOR: The storefront Mongo/Elastic documents are re-derived from SQL on
every event. A product can be "created" in Mongo but then immediately **tombstoned** by the
SQL overlay if `IsActive`/`IsOnlineVisible`/`PublishStatus` do not prove visibility. The
Mongo document's `IsOnlineVisible` is computed as
`sqlProduct.IsOnlineVisible && sqlProduct.PublishStatus == PublishStatus.Published`
(ProductSearchSqlAuthorityOverlay line ~171) — NOT copied blindly from the event. This is
the single most important authority rule to remember when debugging "product exists in SQL
but is missing in storefront."

---

# Operational Lifecycle

Reverse-engineered end-to-end for Create, Update, Publish/Unpublish, Archive/Deactivate,
Delete, and Media change.

## CREATE
1. Operator fills ProductForm (React). Client validates: category required; for a new
   simple product, a price > 0 is required (`ProductForm.handleSubmit`). Variant products
   send `sku/barcode/price/onlinePrice/costPrice = null` at product level.
2. `POST /api/products` (CreateProduct).
3. Backend guards:
   - `CategoryId != Guid.Empty` (security: empty category would bypass category-scoped
     coupons) → 400.
   - Category must be a **live** SQL category with valid parent lineage
     (CategoryLineageAuthority.RequireLiveCategoryAsync) → 400 otherwise.
   - For simple products: barcode must not be owned by another live product/variant
     (ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync);
     SKU resolved to a unique value (ResolveUniqueSkuAsync).
   - Normalizes Features/Faqs/Tags (structured lists), serializes JSONB.
4. Builds `Product` entity; if `HasVariants`, commerce fields forced to null/0 at product
   level (container semantics).
5. Opens SQL transaction. Calls
   `IProductVariantService.EnsureDefaultVariantAlignedAsync` → creates the default variant
   (simple products get exactly one default variant that actually stores SKU/price/stock).
   `ApplySimpleProductIdentityFromDefaultVariant` copies default-variant identity back onto
   the product row for queries.
6. `SaveChangesAsync()` commits SQL rows.
7. Publishes `ProductCreatedEvent` (wrapped in try/catch for EventVersionOverflow → if
   conversion overflows, the whole transaction rolls back and returns 409 Conflict
   "EventVersionOverflow", manual intervention required).
8. Publishes `ProductVariantCreatedEvent` for the default variant.
9. Second `SaveChangesAsync()` + `transaction.CommitAsync()` — events are in the outbox
   atomically with the SQL commit.
10. Audit log (LogCreate).
11. Returns 201 Created with the product DTO.
12. **OUTBOX → PROJECTION:** MassTransit delivers `ProductCreatedEvent` →
    ProductCreatedEventConsumer → `ProductSqlAuthorityProjectionWriter.ApplyAsync` →
    ProductSearchSqlAuthorityOverlay re-composes the document from live SQL → upsert or
    tombstone into MongoDB `products` → Elasticsearch index.
13. Operator sees the new product in POS Admin list immediately (direct SQL read).

## UPDATE (includes publish / unpublish / archive)
1. `PUT /api/products/{id}` (UpdateProduct).
2. Same category + barcode + SKU guards as Create (current default variant excluded from
   conflict check).
3. Maps all fields onto the existing `Product` (price fields nulled if HasVariants).
4. Opens transaction. `EnsureDefaultVariantAlignedAsync` keeps default variant in sync;
   `ApplySimpleProductIdentityFromDefaultVariant` re-syncs product-level identity.
5. Bumps `ProjectionMutationVersion + 1`; converts to event version via
   ProductEventVersionHelper. On overflow → 409 Conflict, no partial write.
6. Enqueues a Marketplace outbox `ProductUpdatedEvent` envelope
   (IMarketplaceEventOutboxService) — used for CommerceHub/marketplace sync.
7. `SaveChangesAsync()`.
8. Publishes `ProductUpdatedEvent` (Mongo sync). If `PublishStatus` or `IsOnlineVisible`
   changed vs before-update snapshot, also publishes `ProductPublishStatusChangedEvent`.
   If the product transitioned into archived state (was not archived, now
   `!IsActive || PublishStatus==Archived`), publishes `ProductArchivedEvent`.
   If default variant was created/changed, publishes variant events.
9. Second `SaveChangesAsync()` + commit.
10. Audit log (LogUpdate, captures before/after).
11. Projection pipeline re-composes Mongo + Elastic from SQL. The overlay recomputes
    `IsOnlineVisible` and will tombstone if visibility no longer proven.

NOTE: There is **no separate publish endpoint**. "Publishing" = set
`PublishStatus=Published` and `IsOnlineVisible=true` in UpdateProduct. "Unpublishing" =
the inverse. A product can be Published but still invisible if IsOnlineVisible=false, and
vice-versa — both must hold for storefront visibility (overlay rule).

## DEACTIVATE (soft disable, reversible)
- `PUT /api/products/{id}/deactivate`: sets `IsActive=false`, publishes
  `ProductArchivedEvent`. Does NOT change PublishStatus or IsOnlineVisible, does NOT
  soft-delete. This is the lightweight "hide from POS/storefront" action. Re-activation is
  performed by a subsequent UpdateProduct setting `IsActive=true`.

## DELETE (soft delete, guarded, effectively archival)
- `DELETE /api/products/{id}`: runs `ValidationService.CanDeleteProductAsync`. Blocked
  (400) if the product has any stock in any warehouse, or is referenced by purchase
  invoice items, sales invoice items, return items, stock transfer items, or has any
  stock movement. If allowed: sets `IsDeleted=true`, `IsActive=false`,
  `IsOnlineVisible=false`, `PublishStatus=Archived`; marks all live variants
  `IsDeleted=true, IsActive=false`; publishes `ProductDeletedEvent`,
  `ProductArchivedEvent`, and per-variant `ProductVariantDeletedEvent` /
  `VariantDeletedEvent`. There is **no restore/undo** endpoint — deletion is permanent
  soft-state; recovery requires direct data fix or replay.

## MEDIA CHANGE
- `ProductMediaController` Add/Remove/Reorder → `ProductMediaService` writes
  `ProductMedia` rows (URLs only), validates media type/provider/extension, then publishes
  `ProductMediaChangedEvent`. Consumer re-composes media from SQL authority (it re-loads
  the SQL media list and rejects the event if the SQL `ProjectionMutationVersion` does not
  match `requireExactVersion`), updates Mongo, then re-runs the SQL authority overlay and
  re-indexes to Elasticsearch.

## Events → projection summary
- SQL commit and outbox are atomic (same transaction) → no "SQL updated but projection
  missed" under normal operation.
- Projection is SQL-recomposed (not event-payload-trusted) → projection can never show a
  product more visible than SQL allows.
- Stale/out-of-order events are dropped via monotonic `EventVersion` /
  `ProjectionMutationVersion` checks (e.g. ProductMediaChangedEventConsumer uses
  BuildFreshMediaEventFilter enforcing version-then-timestamp ordering).

---

# Reverse Engineered Implementation Map

## Frontend (PointOfSale.Web)
- `src/components/ProductForm.jsx` — the master authoring form (modal + workspace modes).
  Owns form state, validation (category required, price>0 for new simple products), slug
  sanitization, tag/faq normalization, AI-content apply, default-variant sync. Submits to
  `POST/PUT /api/products`, then `PUT /api/products/{id}/variants/{defaultVariantId}` for
  simple-product pricing. Invalidates POS runtime cache after save.
- `src/pages/Products.jsx` — product list page.
- `src/components/ProductVariantsTab.jsx` — variant grid (variant-mode products).
- `src/components/ProductMediaManager.jsx` — media add/reorder/remove (talks to
  `/api/admin/products/{id}/media`).
- `src/components/ProductAffiliateSettingsCard.jsx` + `services/adminProductAffiliateApi.js`
  — affiliate enable/disable/template/category.
- `src/components/ProductPixelOverrideTab.jsx` — storefront pixel overrides (related).
- `src/storefront/products/ProductTemplateRegistry.ts` — template options.
- API client: `src/services/api` (axios). Tenant isolation is header-based
  (`X-Tenant-Id`) on the storefront side; POS admin uses the authenticated principal.

## Backend endpoints (PointOfSale.API/Controllers)
- `ProductsController.cs` — CRUD + search + units + stock-adjust + barcode-generate +
  can-delete + deactivate + sync-to-mongo.
  - GET /api/products (paged, filter by search/categoryId/isActive; ILIKE on
    name/barcode/sku)
  - GET /api/products/lookup (id,name,sku only)
  - GET /api/products/{id}
  - GET /api/products/barcode/{barcode} (variant-aware)
  - POST /api/products
  - PUT /api/products/{id}
  - DELETE /api/products/{id} (Owner,Admin only)
  - PUT /api/products/{id}/deactivate
  - GET /api/products/{id}/can-delete
  - GET /api/products/search (POS quick search, variant-aware, warehouse stock)
  - GET /api/products/quick-access (flattened sellable variant items)
  - POST /api/products/generate-barcode (EAN13/CODE128, collision-checked)
  - GET /api/products/{id}/stock
  - POST /api/products/{id}/adjust-stock (simple products only)
  - unit endpoints: GET/POST/PUT/DELETE /api/products/{id}/units...
  - POST /api/products/migrate-to-multi-unit, POST /api/products/sync-stock-to-mongo
- `ProductVariantsController.cs` — variant CRUD, idempotency-keyed create, semantic
  ownership (current/merged/split), price/distributed reliability.
- `ProductMediaController.cs` — product media CRUD (Owner/Admin/Manager).
- `AdminProductAffiliateController.cs` — affiliate policy (Owner/Admin) delegating to
  IAffiliateService.
- `StorefrontProductsController.cs` (`/api/storefront/products`, AllowAnonymous,
  X-Tenant-Id) — READ-ONLY storefront reads via StorefrontProductService (Mongo candidate
  + SQL authority re-composition + Redis cache).
- `PublicAffiliateProductsController.cs` — public affiliate product reads (boolean
  IsAffiliateEnabled only; never commission rate).

## Application / Domain services
- `ProductMediaService` — media validation + mutation + event publish.
- `IProductVariantService` / `EnsureDefaultVariantAlignedAsync` — keeps the default variant
  in sync with the simple-product container; source of the "container vs SKU" semantics.
- `ValidationService.CanDeleteProductAsync` / `CanModifyProductUnitConversionAsync` —
  guards financial/inventory integrity.
- `CategoryLineageAuthority` — validates category is live in SQL parent lineage.
- `ProductVariantCloneImportAuthority` — barcode/SKU semantic ownership conflict checks.
- `ProductEventVersionHelper` — converts monotonic `ProjectionMutationVersion` (long) to
  event `EventVersion` (int); overflow → throws ArgumentOutOfRangeException (caught to roll
  back the transaction).
- `IProductMediaService`, `IBarcodeService`, `IValidationService`, `IAuditService`.

## Repositories / persistence
- `ApplicationDbContext` (EF Core, PostgreSQL primary). `Products` configured in
  ProductConfiguration: PK Id; Name required (max 300); Barcode/SKU max 100; decimal
  precisions; FeaturesJson/FaqJson/TagsJson as jsonb; CategoryId required (FK to Category,
  DeleteBehavior.Restrict); indexes on TenantId, (TenantId,CategoryId), Barcode, SKU, Name.
- Related tables: ProductMedia, ProductVariants (+Attributes, Stocks),
  ProductUnits (+Unit), AffiliateProductPolicies, WarehouseStocks.

## Events / Outbox
- Core events: ProductCreatedEvent, ProductUpdatedEvent, ProductPublishStatusChangedEvent,
  ProductArchivedEvent, ProductDeletedEvent, ProductPriceChangedEvent,
  ProductStockChangedEvent, LowStockAlertEvent (non-authoritative), ProductMediaChangedEvent.
- Variant events: ProductVariantCreatedEvent, ProductVariantUpdatedEvent,
  ProductVariantDeletedEvent, VariantDeletedEvent.
- Outbox: MassTransit outbox (events committed in same SQL tx). Marketplace outbox envelope
  via IMarketplaceEventOutboxService (used for CommerceHub sync of ProductUpdatedEvent).

## Projection pipeline
- `ProductCreatedEventConsumer` / `ProductUpdatedEventConsumer` →
  `ProductSqlAuthorityProjectionWriter` → `ProductSearchSqlAuthorityOverlay.ApplyAsync`
  re-composes a `ProductDocument` from live SQL (variants, units, media, promotions,
  category name) → `IStorefrontProductProjectionMutationBoundary` upserts/tombstones in
  MongoDB. Then Elasticsearch index (`ProductSearchDocumentMapper` /
  `IProductSearchIndexService`).
- `ProductMediaChangedEventConsumer` — media-only re-projection with SQL version fence.
- `ProductVisibilityLifecycleProjectionConsumer` — visibility lifecycle projections.
- `AffiliateProductPolicyProjectionConsumer` — affiliate policy projection.

## Caching / Search / Storefront
- Redis (optional, non-fatal) in StorefrontProductService read path.
- Elasticsearch (`ProductDiscoveryService`, `ProductSearchService`,
  `AffiliateProductDiscoveryService`) indexed from the SQL-authoritative Mongo doc.
- Storefront reads Mongo as candidate, re-composes SQL authority, then caches.

---

# Business Rules (proven by implementation)

1. **Tenant isolation is mandatory.** Every product carries `TenantId` (NOT NULL). All
   controllers use `ITenantContext.RequiredTenantId`. Cross-tenant reads return 404 (never
   403) per affiliate/storefront design. X-Tenant-Id is stripped server-side.
2. **CategoryId must not be empty (Guid.Empty).** Enforced by ProductsController on create
   and update, then validated against live SQL category lineage; the EF model requires a
   CategoryId/FK. This is a security rule to prevent category-scoped coupon bypass.
3. **Category must be a live SQL category with valid parent lineage.**
   CategoryLineageAuthority.RequireLiveCategoryAsync walks the parent chain; a deleted
   ancestor, a Guid.Empty parent, or a cycle → category rejected → 400.
4. **Barcode uniqueness (semantic).** A barcode cannot be owned by another *live* product
   or variant (ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync).
   Check excludes the product's own current default variant on update.
5. **SKU resolution.** For simple products, SKU is resolved to a unique value
   (ResolveUniqueSkuAsync). Variant products force product-level SKU null.
6. **Variant container semantics (PHASE 11.5).** If `HasVariants==true`: product-level
   Barcode/SKU/Price/OnlinePrice/PurchasePrice forced to null/0; TrackStock=false; all
   commerce is per-variant. If `HasVariants==false`: exactly one default variant owns the
   real SKU/price/stock; product row mirrors it.
7. **Default variant is auto-created** on product create and kept aligned on update
   (EnsureDefaultVariantAlignedAsync). Product cannot exist without at least the default
   variant for simple products.
8. **PublishStatus enum:** Draft=0, Published=1, Archived=2. New products default to
   Draft (entity default) but the UI defaults the form to Published (publishStatus:1) and
   IsOnlineVisible:true.
9. **Storefront visibility gate (computed, not stored blindly):** MongoDB/Elastic
   `IsOnlineVisible = sqlProduct.IsOnlineVisible && sqlProduct.PublishStatus == Published`.
   A product Published but with IsOnlineVisible=false is NOT shown online. A product
   Archived or IsActive=false is tombstoned from the projection.
10. **Soft-delete only.** Hard delete is impossible. Deletion requires zero stock, zero
    invoice/return/transfer references, and zero stock movements
    (ValidationService.CanDeleteProductAsync). Otherwise 400 with bilingual message.
11. **Delete side-effects:** sets IsDeleted/IsActive=false, IsOnlineVisible=false,
    PublishStatus=Archived on product and all live variants; publishes delete + archive +
    variant-delete events. There is no restore endpoint.
12. **Deactivate vs Archive:** deactivate sets IsActive=false only (reversible via update);
    it does not change PublishStatus/IsOnlineVisible and does not soft-delete.
13. **Media validation:** MediaType must be `image` or `video`. Images: only .jpg/.jpeg/.png/
    .webp URLs, provider must be empty. Videos: provider must be youtube/vimeo/mp4 with
    provider-specific URL rules; mp4 must end in .mp4. URL must be absolute or
    root-relative. Storefront only indexes media of type image/video.
14. **Media reorder is total.** Reorder request must contain every media id exactly once
    (else InvalidOperationException). Positions are 1-based resequenced.
15. **Removing media detaches variants** that referenced it (variant.MediaId/ImageUrl
    cleared) and publishes ProductVariantUpdatedEvent for each affected variant.
16. **Event version monotonicity.** ProjectionMutationVersion advances monotonically;
    overflow in conversion → transaction rollback + 409 Conflict "EventVersionOverflow".
17. **Multi-unit conversion guard.** Once a ProductUnit has been used in purchase/sales
    invoices or stock movements, its conversion factor cannot be modified.
18. **Affiliate enablement rules:** commission rate must be 0.0001–0.50; max cap positive
    when set; requires affiliate readiness gate; policy is product-level (not per-variant);
    disable is soft (IsEnabled=false, values retained for restore); public APIs expose only
    IsAffiliateEnabled boolean, never the rate. Effective public affiliate visibility
    additionally requires product IsActive && IsOnlineVisible && Published, resolved
    storefront pricing, `TotalAvailableStock > 0`, and `InStock`; backorder or non-stock
    tracking alone does not make a zero-stock public affiliate product visible.
19. **Audit.** Create/Update/Delete are audit-logged via IAuditService.
20. **Projection is SQL-authoritative.** Mongo/Elastic documents are re-derived from SQL on
    every event; stale or visibility-failing projections are tombstoned, never persisted as
    visible.

---

# Cross-System Effects

## Inventory
- Product Management sets `TrackStock` / `AllowBackorder` flags and the `HasVariants`
  container shape. Actual stock lives in ProductVariantStocks/WarehouseStocks (Inventory
  owns). `adjust-stock` endpoint only works for simple products (variant products must
  adjust the specific variant). Product deletion is blocked if any stock remains.

## Pricing
- Product Management authors `SalePrice` (POS), `OnlinePrice` (storefront source of truth),
  `PurchasePrice`, tax. For variant products, price lives on the variant; product row is a
  mirror. Promotions are NOT stored on product — overlaid at projection (Mongo) and
  checkout. Changing price publishes ProductUpdatedEvent → Mongo re-composition reflects new
  price (subject to version checks).

## Orders / Checkout
- Orders reference products/variants by id; orders never write back to product truth.
  Checkout reads the SQL-authoritative projection (Mongo+overlay) for price/stock; uses
  TrackStock/AllowBackorder to decide stock validation. HasVariants products require a
  variantId in cart requests (enforced at storefront/checkout).

## Storefront
- Storefront reads Mongo `products` as a candidate, then re-composes SQL authority, then
  Redis-caches. Visibility = IsActive && IsOnlineVisible && Published. Media of type
  image/video projected. ProductTemplate / SEO fields projected. If SQL says invisible →
  tombstone; storefront never shows it.

## CommerceHub (Marketplace)
- CommerceHub is a SEPARATE capability and NOT governed by Product Management. Product
  Management feeds it via the Marketplace outbox envelope (ProductUpdatedEvent enqueued to
  IMarketplaceEventOutboxService). CommerceHub reads product eligibility/listing lifecycle
  independently. Product Management does NOT list or unlist in CommerceHub.

## Affiliate
- Product-level affiliate policy (commission, category, template) is managed via
  AdminProductAffiliateController but written by IAffiliateService (Affiliate domain).
  Effective eligibility depends on product publication + price + stock (see rule 18). Public
  affiliate reads expose only IsAffiliateEnabled.

## Search (Elasticsearch)
- Search index built from the SQL-authoritative Mongo document. Product create/update/media
  changes re-index. Visibility rules apply (tombstoned products drop out of search).
  Search is a projection, not authority.

## Analytics
- Product is a reporting dimension (sales, movement reports: ProductMovementReport.jsx,
  TopProductStatsTable). Analytics reads product/order data; never mutates product truth.

## Notifications
- `LowStockAlertEvent` is `INonAuthoritativeIntegrationEvent` (advisory only) — it does not
  change product state. No product-created/updated notifications are emitted by Product
  Management itself (notification wiring, if any, would be downstream of events).

## Identity
- Product Management never touches auth/identity except tenant scoping
  (ITenantContext.RequiredTenantId) and role checks (Owner/Admin/Manager for writes;
  Owner/Admin for delete/affiliate).

## Accounting
- No accounting/ledger effect from product mutations. Product is referenced by ledger
  postings (e.g. COGS) but Product Management does not post.

---

# Validation Checklist (executable by Hermes)

## After creating a product
- SQL: a `Products` row exists with the tenant id, a valid non-empty CategoryId, and
  (simple product) a default ProductVariant carrying SKU/price/stock.
- SQL: `ProjectionMutationVersion` advanced; `CreatedAt` set.
- Outbox: `ProductCreatedEvent` + `ProductVariantCreatedEvent` enqueued in same tx.
- Mongo: a `products` document exists for (TenantId, ProductId), OR a tombstone if
  IsOnlineVisible=false / PublishStatus!=Published at creation (UI defaults both true, so
  normally visible).
- Storefront: visible if IsActive && IsOnlineVisible && Published; else absent/tombstoned.
- Search: indexed iff visible.
- POS Admin list: immediately visible (direct SQL).
- NOT yet visible online if IsOnlineVisible=false or PublishStatus=Draft/Archived.

## After editing a product
- SQL row reflects new fields; default variant re-synced for simple products.
- `ProductUpdatedEvent` + (if visibility changed) `ProductPublishStatusChangedEvent`
  published; Marketplace outbox enqueued.
- Mongo/Elastic re-composed from SQL; visibility recomputed (tombstone if no longer proven).
- Audit log entry for update (before/after).
- If price changed: storefront reflects new OnlinePrice (source of truth) after projection.

## After archiving / deactivating
- Deactivate: `IsActive=false`; POS/storefront hide it; PublishStatus unchanged; NOT
  soft-deleted; reversible via update.
- Archive (PublishStatus=Archived via update): storefront + search tombstone the product;
  SQL row remains (IsDeleted false unless deleted).
- ProductArchivedEvent published in both cases.

## After deleting
- SQL: product + live variants `IsDeleted=true`, IsActive=false, IsOnlineVisible=false,
  PublishStatus=Archived.
- Mongo: tombstoned (removed from storefront). Search: dropped.
- ProductDeletedEvent + ProductArchivedEvent + variant delete events published.
- Blocked entirely if stock/invoice/return/transfer/movement references exist.
- NO restore endpoint — recovery requires data fix or replay.

## After media change
- SQL ProductMedia rows updated (URLs only); positions resequenced.
- ProductMediaChangedEvent published with version fence; Mongo media updated; Elastic
  re-indexed; variants referencing removed media have MediaId/ImageUrl cleared.

---

# Common Failure Scenarios

1. **Product exists in SQL but missing in Storefront.**
   - Cause: SQL overlay computed IsOnlineVisible=false (IsOnlineVisible false OR
     PublishStatus != Published) → tombstone. Or tenant mismatch / product soft-deleted /
     category not live.
   - Investigate: check `Products.IsActive`, `IsOnlineVisible`, `PublishStatus`,
     `IsDeleted`, and that CategoryId resolves to a live category. Check Mongo doc's
     LastEventVersion vs SQL ProjectionMutationVersion.
2. **Product visible in Storefront but missing in CommerceHub.**
   - Cause: CommerceHub is a separate capability; product may be eligible for storefront but
     not listed/marketplace-eligible. Product Management only feeds CommerceHub via the
     marketplace outbox; listing lifecycle is CommerceHub-owned.
   - Investigate: CommerceHub listing state, not Product Management.
3. **Wrong price on storefront.**
   - Cause: storefront uses `OnlinePrice` (variant's, for variant products; product's for
     simple). SalePrice is only the POS fallback. A stale Mongo doc or a projection that
     lost the update (version skip) can show old price.
   - Investigate: compare variant.ProductId OnlinePrice/SalePrice in SQL vs Mongo doc Price/
     OnlinePrice; check LastEventVersion monotonicity; check outbox delivery.
4. **Missing media.**
   - Cause: media type not image/video (not indexed); or ProductMediaChangedEvent rejected
     by SQL version fence (version mismatch); or media added after projection but event
     lost.
   - Investigate: ProductMedia rows vs Mongo Media array + LastMediaEventVersion; verify
     media type in {image,video}.
5. **Projection stale / Search stale.**
   - Cause: consumer error/retry; event dropped as stale by monotonic version; or search
     index not updated after tombstone.
   - Investigate: event version vs projection version; MassTransit outbox; Elasticsearch
     document vs Mongo doc.
6. **Category mismatch.**
   - Cause: CategoryId points to a deleted/cyclical ancestor → SQL overlay returns null →
     product tombstoned/not projected.
   - Investigate: CategoryLineageAuthority live check; category IsDeleted up the chain.
7. **Attribute mismatch.**
   - Cause: variant attributes live on ProductVariant, not Product. Reading attributes from
     the product row yields nothing; they must come from the variant projection.
   - Investigate: ProductVariants.Attributes (semantic current owner only).
8. **Publication mismatch (Published but not visible).**
   - Cause: IsOnlineVisible=false while PublishStatus=Published; overlay ANDs both.
   - Investigate: both flags, not just PublishStatus.
9. **Cache inconsistency (Redis).**
   - Cause: Redis holds a stale SQL-accepted DTO. Non-fatal — storefront re-composes SQL on
     read and only caches SQL-accepted results; cache miss/error degrades to live
     re-composition.
   - Investigate: Redis key (CacheKeyBuilder.ProductBySlugKey), and confirm SQL authority
     path used.

---

# Debugging Guide

## Where to start
- Identify the symptom surface: POS Admin (direct SQL) vs Storefront/Search (projection).
- If POS Admin is wrong → backend/SQL. If only Storefront/Search is wrong → projection
  pipeline. If both wrong → SQL truth.

## When to check backend
- Create/Update/Delete returns non-2xx. Check ProductsController guards: empty CategoryId,
  dead category lineage, barcode/SKU conflict, EventVersionOverflow (409).
- Check ValidationService for delete/unit-conversion blocks (bilingual error codes
  PRODUCT_HAS_STOCK, PRODUCT_USED_IN_* , PRODUCT_HAS_MOVEMENTS).

## When to check frontend
- Form validation errors (category required, price>0 for new simple product, slug
  sanitization). Note: client sends productTemplate=null when IsOnlineVisible=false — a
  product published with visibility off will have no template projected.
- Variant mode toggle moves price/stock to default variant (confirm dialog).

## When to check SQL (PostgreSQL — source of truth)
- Always the authority. Verify Products row: TenantId, CategoryId (live), IsActive,
  IsOnlineVisible, PublishStatus, IsDeleted, ProjectionMutationVersion, HasVariants; and
  the default ProductVariant (SKU/price/stock) for simple products; ProductMedia rows.

## When to check projections (Mongo/Elastic)
- Storefront/Search mismatch. Verify ProductSqlAuthorityProjectionWriter /
  ProductSearchSqlAuthorityOverlay re-composition; compare Mongo `products` doc
  (IsOnlineVisible, LastEventVersion, Media, Variants) to SQL; check the upsert-vs-tombstone
  disposition.

## When to check cache (Redis)
- After confirming SQL + Mongo agree but storefront still stale. Redis is optional/non-fatal;
  invalidate or confirm storefront re-composition path.

## When to check CommerceHub
- Only when the complaint is specifically about marketplace listing. Product Management feeds
  CommerceHub via the marketplace outbox but does not control listing lifecycle.

## Event/outbox health
- MassTransit outbox delivers events atomically with SQL. If an event never arrived, check
  the outbox table and consumer error logs. Monotonic version guards mean a skipped/out-of-
  order event is dropped silently (by design) — replay from SQL may be required.

---

# Related Knowledge

## Core Guides / System Guides (docs/architecture — verify against impl)
- catalog-presentation-authority-certification-findings.md
- mongo-projection-convergence-certification-findings.md
- storefront-recovery-rebuild-certification-findings.md
- product-detail-media-public-projection-certification-findings.md
- cross-system-convergence-certification-findings.md
- affiliate-product-* certification findings (eligibility/lifecycle)
- commercehub-catalog-authority-certification-findings.md

## Capabilities
- Inventory / Warehouse (stock truth)
- Pricing / Promotions / Tax (overlay at projection/checkout)
- Orders / Checkout / Payments / Ledger (consumers, not owners)
- Storefront (read projection consumer)
- CommerceHub / Marketplace (separate listing lifecycle)
- Affiliate (product-level policy via IAffiliateService)
- Search (Elastic projection)
- Analytics (reporting dimension)
- Media (ProductMedia; external object storage for bytes)

## Related Domains
- ProductVariant (sellable SKU, prices, attributes, stocks)
- Category (live lineage authority)
- Unit / ProductUnit (multi-unit pricing)
- AffiliateProductPolicy (commission/template/category)
- ProjectionMutationVersion / EventVersion (monotonic authority)

---

# Documentation Validation (implementation vs docs)

NOTE: The repository has NO `.ai-context` directory. Authoritative docs live under
`docs/architecture/*` as "certification findings" (often payment/affiliate/commercehub
focused). The following are discrepancies/inconsistencies found by comparing implementation
to those docs and to the stale README assumption that docs are authoritative.

## Missing documentation
- No capability doc for the SQL-authority overlay re-composition model
  (ProductSearchSqlAuthorityOverlay) — the most important runtime behavior for
  storefront/search correctness is undocumented as a product-level concept.
- No documentation of the container-vs-variant ("PHASE 11.5") authoring model where simple
  products actually store commerce data on a default variant. This is central and only
  visible in code comments.
- No documentation that "publishing" has no dedicated endpoint (it is a field on Update).
- No documentation of the EventVersionOverflow rollback path (409 Conflict).
- No documentation that product deletion has hard guards (stock/invoice/return/transfer/
  movement) and no restore.

## Outdated / inconsistent documentation
- Docs describe MongoDB as a storefront read model but understate that Mongo is
  **re-derived from SQL on every event** and will tombstone products the event would have
  made visible. Treating Mongo as "source of truth" anywhere is incorrect.
- Storefront visibility is commonly described as driven by PublishStatus alone;
  implementation requires `IsActive && IsOnlineVisible && Published` (overlay ANDs
  IsOnlineVisible with Published).
- Affiliate docs that imply product-level commission is stored on the product row are
  incorrect: it lives in AffiliateProductPolicies via IAffiliateService, and public APIs
  expose only IsAffiliateEnabled.

## Architecture inconsistencies
- Some certification docs imply CommerceHub is governed by Product Management; implementation
  keeps CommerceHub as a separate capability that only consumes the marketplace outbox
  envelope. Product Management does not list/unlist in CommerceHub.
- Media docs that treat Product.Images (comma-separated string) as the gallery are
  partially stale: the projection now prefers the ProductMedia table (image/video types)
  via ProductMediaChangedEvent; Images string is still carried in events but the storefront
  gallery is driven by ProductMedia.

## Missing business rules (in docs)
- Barcode/SKU semantic-ownership conflict checks (live-only, excludes own default variant).
- Category lineage liveness (deleted ancestor / cycle → reject).
- Empty-CategoryId security rule (coupon bypass prevention).
- Multi-unit conversion-factor lock after transaction use.
- Media type/provider/extension whitelist.
- Affiliate effective-eligibility gating (published + price + stock).

## Hidden implementation behavior (not obvious from surface)
- `IsOnlineVisible` in the Mongo/Elastic doc is COMPUTED
  (`IsOnlineVisible && PublishStatus==Published`), not copied from the event payload.
- The Mongo projection writer can collapse a "created" event into a tombstone if SQL does
  not prove visibility at projection time.
- Client sends `productTemplate=null` whenever `IsOnlineVisible=false`, so publishing with
  visibility off loses the template in the projection.
- ProductMediaChangedEvent consumer enforces an exact SQL version match
  (requireExactVersion=true) and re-reads SQL media; a version drift silently rejects the
  event.
- Event version overflow returns 409 and rolls back the entire product write (fail-closed).
- Deactivate is reversible (IsActive only); delete is not (no restore endpoint).

## Incorrect assumptions to avoid
- "Product row holds the price" — for variant products the product price is 0/null; the
  variant holds it.
- "PublishStatus=Published means visible online" — only if IsOnlineVisible is also true.
- "MongoDB/Elasticsearch is the product source of truth" — it is a SQL-recomposed
  projection; SQL wins on every disagreement.
- "Deleting a product removes it" — it is a soft delete; recovery needs data fix/replay.

---

# End-of-task attestation

- Created ONLY: `.ai/hermes/capabilities/pos-admin/product-management.md`
- No other files modified.
- No code generated, no features implemented, no refactors applied.
- No memory writes performed.
