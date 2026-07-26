# Product Composition Runtime — FINAL CANONICAL ARCHITECTURE (Implementation-Backed)

> Authority rule: Implementation is the ONLY source of truth. Every claim below is cited to
> `file:line` in the backend. Prior markdown, UI labels, controller/DTO names, and comments are
> treated as secondary and were re-verified against code. Where the prompt's assumptions conflict
> with code, the code wins (see `## Documentation Validation`).
>
> Scope: the "Product Workspace" = the product authoring + catalog runtime inside the Merchant
> Platform (POS Admin write surface + Storefront read surface). It is the PostgreSQL-authoritative
> catalog owner that every other runtime (Storefront search, CommerceHub marketplace, Affiliate,
> Facebook/Tracking pixel, AI) reads from through SQL-recomposed projections.

---

## 1. Executive Summary

The Product Workspace is **NOT a single "Product Runtime" and NOT a "Product Composition Runtime"**
in the CQRS-projection sense. It is the **Catalog / Product-Information-Management (PIM) Authoring
Runtime**: a **PostgreSQL-authoritative write model** that owns the product aggregate and its
sellable variant aggregate, and emits catalog integration events through MassTransit's transactional outbox.
All other subsystems (Storefront read API, Elasticsearch search, CommerceHub marketplace listing,
Affiliate discovery, Facebook/tracking pixel, AI generation) are **downstream projections or
separate runtimes that re-derive their view from the SQL catalog on every public read** — they do
NOT own product truth.

Key architectural thesis (verified throughout):

- **Product = catalog/parent aggregate** (identity, SEO/slug, category, media, publication,
  shipping, marketing). `PointOfSale.Core/Entities/Product.cs:9`
- **ProductVariant = first-class sellable SKU aggregate** (price, stock, SKU, barcode, own events,
  own semantic lifecycle). `PointOfSale.Core/Entities/ProductVariant.cs:26`
- **ProductVariantStock = inventory authority** (per-warehouse quantity/reserved/damaged).
  `PointOfSale.Core/Entities/ProductVariantStock.cs:17` — legacy `WarehouseStock` is compatibility-only.
- **Mongo + Elasticsearch are projections, never sources.** Every public read path **rebuilds the
  document from PostgreSQL** and **tombstones/deletes** the projection when SQL no longer proves
  `IsActive && IsOnlineVisible && PublishStatus==Published`.
  `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs:88-112`
  and `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:170-172`

The "composition" the prompt refers to is the **two-tier aggregate composition**
(Product ⊃ Variant ⊃ VariantStock ⊕ Media ⊕ Attributes ⊕ Units) plus the **event-driven fan-out**
to sibling runtimes — not a runtime that orchestrates those runtimes centrally.

---

## 2. Core Discovery — What is a Product?

**A Product is an Aggregate Root (catalog parent) owning product-level identity and metadata,
composed of one-or-more Variant aggregate roots that own sellability.** It is NOT a projection,
NOT a marketplace entity, NOT a pure inventory entity. It is the SQL authority for catalog truth.

Evidence:
- `Product : BaseEntity, ITenantOwned, IProjectionVersioned` — it carries a `ProjectionMutationVersion`
  monotonic stamp used to reject stale downstream projections. `Product.cs:9,210-225`
- A Product MUST have exactly one default variant; for simple products the default variant mirrors
  product commerce fields both ways. `ProductsController.cs:388-390`
  (`EnsureDefaultVariantAlignedAsync` + `ApplySimpleProductIdentityFromDefaultVariant`).
- When `HasVariants=true`, the Product is "just a container": Product-level SKU/Barcode/price are
  forced to `null`/`0` and variants own everything.
  `ProductsController.cs:329,335-337,344,345` and `ProductVariantService.cs:251-279`.

Classification verdict: **Product = Aggregate Root (catalog/parent); Variant = separate Aggregate
Root (sellable SKU).** No single one of the prompt's listed labels ("Product Runtime", "Catalog
Composition Runtime", "PIM Runtime", "Product Authoring Runtime") fully captures it — the accurate
name is **Product Authoring + Catalog Runtime** (a SQL-authoritative PIM write model). See
`## Runtime Classification`.

---

## 3. Runtime Purpose

Why this runtime exists: to let a merchant (tenant) author and own the canonical definition of a
sellable product — its identity, descriptive content, SEO, media, variants, pricing, stock link,
publication, and marketing flags — in ONE authoritative SQL write model, and to **publish a single
immutable event stream** that every downstream consumer (storefront, search, marketplace, affiliate,
pixel, AI) projects from. It exists to **preserve runtime ownership, authority boundaries, and
consistency** by making PostgreSQL the only writer of catalog truth and forcing every projection to
re-prove SQL authority before it is presented.

Evidence:
- Outbox pattern: catalog events are saved in the same SQL transaction as the domain change through
  MassTransit's transactional outbox. The custom Hangfire `OutboxMessage` backbone exists for other
  authoritative events, not product/variant catalog events. `ProductsController.cs:404-447`;
  `ApplicationDbContext.cs:1230-1231`; `Program.cs:2044`.
- Fail-closed visibility: projection writers refuse to present a product unless SQL proves it is
  active + online-visible + published. `ProductSqlAuthorityProjectionWriter.cs:94-112`.

---

## 4. Runtime Owns (Authoritative SQL)

The Product Workspace owns the following, written to PostgreSQL under `(TenantId, …)` scoping:

| Concept | Ownership | Evidence |
|---|---|---|
| Product identity (ProductId, Name, NameAr) | Product aggregate | `Product.cs:15,17,22` |
| SKU (simple-product level) | Product.SKU mirrors default variant; variant owns real SKU; SQL uniqueness is tenant-scoped | `ProductVariantService.cs:351`; `ProductIdentityTenantScopeHardening.cs` |
| Barcode (simple-product level) | Product.Barcode mirrors default variant; variant owns real barcode; SQL uniqueness is tenant-scoped | `ProductVariantService.cs:364`; `ProductIdentityTenantScopeHardening.cs` |
| Variants | `ProductVariant` aggregate (1+ per product) | `ProductVariant.cs:26`; `Product.cs:291` |
| Variant attributes | `ProductVariantAttribute` (denormalized name/value) | `ProductVariantAttribute.cs:18` |
| Attribute definitions | **NONE** — no taxonomy; attributes are denormalized name/value pairs | `ProductVariantAttribute.cs:18-42` |
| Categories | `Category` hierarchy (parent/child), product references CategoryId | `Category.cs:8-28`; `Product.cs:50` |
| Units | `Unit` + `ProductUnit` (conversion factor, per-unit price/barcode) | `Unit.cs:8`; `ProductUnit.cs:6` |
| Brands | **NONE as entity** — free-text `Product.Brand` + `Product.Manufacturer` strings | `Product.cs:234,239` |
| Pricing (Base/Retail/Wholesale/Tier) | Columnar on Product + Variant + per-`ProductUnit.SellingPrice`; `OnlinePrice` = storefront source of truth | `Product.cs:55-67`; `ProductVariant.cs:61-72`; `ProductUnit.cs:25` |
| Cost | `Product.PurchasePrice`, `ProductVariant.CostPrice`, `ProductUnit.PurchasePrice` | `Product.cs:55`; `ProductVariant.cs:72` |
| Inventory link | Via `ProductVariantStock` (variant→warehouse); NOT a separate "inventory runtime" | `ProductVariantStock.cs:17-33` |
| Media | `ProductMedia` collection (validated URLs; image/video) + variant `MediaId` pointer | `ProductMedia.cs:5-17`; `ProductVariant.cs:121` |
| Product images / thumbnail / gallery | `ProductMedia` ordered by Position; legacy `Product.Images` CSV fallback | `ProductMediaService.cs:60-117`; `ProductDocument.cs:251-258` |
| SEO (slug, meta title/desc/keywords) | `Product.Slug/MetaTitle/MetaDescription/MetaKeywords` (SQL columns) | `Product.cs:121-136` |
| Metadata / tags / specs | `Product.Tags/TagsJson`, `FeaturesJson`, `FaqJson`, `RelatedProductIds` | `Product.cs:244-264` |
| Slug | Stored column `Product.Slug` (NOT uniquely indexed; not computed) | `Product.cs:121`; `ProductSearchSqlAuthorityOverlay.cs:162` |
| Visibility / publication | `PublishStatus` enum (Draft/Published/Archived), `IsOnlineVisible`, `IsSearchable`, `IsFeatured` | `Product.cs:179-194`; `PublishStatus.cs:6-16` |
| Affiliate eligibility | **NOT on Product** — separate `AffiliateProductPolicy` SQL entity (FK→Product) | `AffiliateProductPolicy.cs:14-21` |
| CommerceHub publication | **NOT on Product** — separate `CommerceHubListing` SQL entity (variant-level) | `CommerceHubListing.cs:9-13` |
| Facebook/Pixel override | **NOT on Product** — separate `ProductPixelOverride` SQL entity (FK→Product) | `ProductPixelOverride.cs:16-28` |
| AI generated content | `Product.AiGenerated` bool + `Product.AiMetadataJson` (on Product); generated text lands on Product via normal Update | `Product.cs:254-259`; `ProductsController.cs:372-373` |
| Descriptions / specifications | `Product.Description/DescriptionAr`, `FeaturesJson`, `FaqJson` | `Product.cs:26-43` |
| Status / lifecycle | `IsActive`, `PublishStatus`, `IsDeleted` (soft delete) | `Product.cs:94,179`; `ProductsController.cs:837-845` |

---

## 5. Runtime Does NOT Own (Delegated to Other Runtimes)

| Concept | Owner runtime | Evidence |
|---|---|---|
| Inventory quantity/reservations mesh | Inventory Runtime (operational) — ProductWorkspace only owns the *link* via `ProductVariantStock` rows and emits stock events | `ProductVariantStock.cs:13-16` (legacy = compat only) |
| Orders / checkouts / payments | Order/Payment lifecycle runtimes | `CheckoutService` (separate); not in Product controllers |
| Customers | Customer runtime | — |
| CommerceHub marketplace listing/projection | CommerceHub Runtime — re-derives `CommerceHubProductDocument` from catalog events + SQL overlay | `CommerceHubProjectionConsumer.cs:411,506,1104` |
| Affiliate projection / commission | Affiliate Runtime — `AffiliateProductPolicy` + `PublicAffiliateProductDocument` projection | `AffiliateProductPolicy.cs:14`; `AffiliateProductPolicyProjectionConsumer.cs` |
| Media storage (S3/bucket) | **NO first-party storage** — media are validated external URLs (image ext / YouTube/Vimeo/mp4). No S3 uploader in product path. | `ProductMediaService.cs:304-352` (URL-only validation) |
| AI generation service | AI Runtime (`AdminProductGeneratorService`) — generates a DTO returned to the human; does NOT write Product rows directly. Human accepts → normal Product Update. | `AdminProductGeneratorService.cs:80-133,348-362` (returns DTO, no `_context.Products` write) |
| Facebook/Tracking pixel emission | Tracking & Attribution Runtime (GA4 MP + Meta CAPI server-side) | `ProductPixelOverride` only overrides *conversion value*, not product fields |
| Analytics | Analytics runtime (reads `LedgerEntries`, not catalog projections) | — |
| Search index | Elasticsearch single-authority index, fed by SQL-recomposed overlay | `ProductSearchSqlAuthorityOverlay.cs:68-83` |
| Notifications / recovery | Separate runtimes | — |

---

## 6. Product Identity (Authoritative Model)

- **ProductId**: `Guid` PK, PostgreSQL source of truth; Mongo `ProductDocument.ProductId` is a
  reference, not authority. `Product.cs:9`; `ProductDocument.cs:103-104`.
- **TenantId**: mandatory (`ITenantContext.RequiredTenantId`), never from client X-Tenant-Id.
  `Product.cs:15`; `ProductsController.cs:325`.
- **SKU**: product-level SKU is a mirror of the default variant's SKU (simple products only).
  Unique-ified via `ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync` and backed by
  tenant-scoped SQL uniqueness for live product/variant SKU owners.
  `ProductsController.cs:301-310`; `ProductVariantService.cs:351`;
  `ProductIdentityTenantScopeHardening.cs`.
- **Barcode**: product-level barcode mirrors default variant; uniqueness enforced tenant-locally
  cross-entity via SQL indexes/triggers and
  `ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync`.
  `ProductsController.cs:284-299`; `ProductVariantService.cs:364`;
  `ProductIdentityTenantScopeHardening.cs`.
- **Slug**: stored column, denormalized for SEO; category slug is computed at read time
  (`StorefrontCategoryService.GenerateSlug`), product slug is NOT uniquely indexed.
  `Product.cs:121`; `SeoService.cs` reads `product.Slug`.
- **Variant identifiers**: `VariantId` (PK) + per-variant SKU/Barcode + `AttributeCombinationKey`
  (SHA-256 of normalized attribute set, unique per Tenant+Product).
  `ProductVariant.cs:56`; `ProductVariantAttributeCombinationKey.cs` (referenced by skill map).
- **Marketplace identifiers**: CommerceHub listing id + variant id (`CommerceHubListing`);
  optional `Product.AmazonASIN` for Amazon SP-API sync (not a shopixy-authoritative id).
  `Product.cs:280`.
- **External identifiers**: `AmazonASIN`, `LastSyncedAt` for external sync bookkeeping.
  `Product.cs:280-285`.

---

## 7. Product Lifecycle (Implementation)

Actual implemented flow (each step = SQL write + outbox event, projections re-derive):

1. **Create Product** → `ProductsController.CreateProduct` (`:262-474`). In a SQL transaction:
   insert Product, ensure default variant, publish `ProductCreatedEvent` + `ProductVariantCreatedEvent`.
2. **Generate AI Draft** → `AdminProductGeneratorService.GenerateProductAsync` returns a DTO to the
   UI; human pastes into the form; subsequent `CreateProduct`/`UpdateProduct` writes `AiGenerated=true`
   + `AiMetadataJson`. AI does NOT write Product rows. `AdminProductGeneratorService.cs:80-133`;
   `ProductsController.cs:372-373`.
3. **Manual Editing** → `UpdateProduct` (`:482-...`) re-publishes `ProductUpdatedEvent` with
   `ChangedFields`; enforces variant rules (`:569-597`).
4. **Variant Creation** → `ProductVariantsController` / `EnsureDefaultVariantAlignedAsync` creates
   variant rows; `ProductVariantCreatedEvent`. `ProductVariantService.cs:203-249`.
5. **Media Upload** → `ProductMediaController`/`ProductMediaService.AddMediaAsync` inserts validated
   URL rows, publishes `ProductMediaChangedEvent`. `ProductMediaService.cs:71-119`.
6. **Category Assignment** → `CategoryId` required, must reference a *live* category lineage
   (`CategoryLineageAuthority.RequireLiveCategoryAsync`); `Guid.Empty` rejected. `ProductsController.cs:270-282`.
7. **Pricing** → Product/Variant/Unit columns; `OnlinePrice` is storefront source of truth.
   `Product.cs:67`; `ProductDocument.cs:135-139`.
8. **SEO** → slug/meta columns written on Product; `SeoService` reads them. `Product.cs:121-136`.
9. **Affiliate Enable** → create `AffiliateProductPolicy` (requires category); emits
   `AffiliateProductPolicyChangedEvent`; NOT a Product column. `AffiliateProductPolicy.cs:53-72`.
10. **CommerceHub Publish** → create `CommerceHubListing` (variant-level, `IsPublished=true`);
    emits listing events; `CommerceHubProjectionConsumer` re-composes `CommerceHubProductDocument`
    from SQL. `CommerceHubListing.cs:46-80`; `CommerceHubProjectionConsumer.cs:411`.
11. **Search Projection** → `ProductSearchIndexConsumer`/`ProductStockPriceSearchIndexConsumer`
    call `ProductSearchSqlAuthorityOverlay.ApplyAsync`; ES doc deleted if SQL hidden.
    `ProductSearchSqlAuthorityOverlay.cs:170-172,285-289`.
12. **Facebook/Pixel Override** → `ProductPixelOverride` (conversion value only) — separate entity.
    `ProductPixelOverride.cs:8-15`.
13. **Inventory Synchronization** → stock mutations emit `ProductVariantStockChangedEvent`
    (`MutationSource` POS vs CommerceHub); `ProductVariantStockChangedEvent.cs:30-41,166-213`.
14. **Product Updates** → `UpdateProduct` re-publishes; default-variant mirror kept aligned.
15. **Soft Delete** → `DeleteProduct` sets `IsDeleted=true`, `IsActive=false`, `IsOnlineVisible=false`,
    `PublishStatus=Archived`, cascades to variants, publishes `ProductDeletedEvent` +
    `ProductArchivedEvent` + per-variant delete events. `ProductsController.cs:820-893`.
16. **Recovery** → projection consumers reject stale events; outbox critical-poison messages
    flagged `RequiresOperatorReconciliation` for human repair (not auto-deleted).
    `OutboxPublisherJob.cs:146-181,219-236`.

---

## 8. Variant Runtime (Implementation)

- **Model**: `ProductVariant` is a first-class aggregate with its own PK (`VariantId`), SKU, barcode,
  price, cost, stock collection, attributes, media pointer. `ProductVariant.cs:26-199`.
- **Parent/Child**: `ProductId` FK; Product = parent catalog, Variant = child sellable. Every product
  has ≥1 default variant. `ProductVariant.cs:197`; `Product.cs:291`.
- **SKU strategy**: variant SKU unique per tenant; for simple products mirrors product SKU.
  `ProductVariantService.cs:351,423-429`; `ProductVariantConfiguration.cs`;
  `ProductIdentityTenantScopeHardening.cs`.
- **Barcode strategy**: variant barcode unique per tenant; cross-checked vs product units inside
  the same tenant.
  `ProductVariantService.cs:364`; `ProductVariantCloneImportAuthority`;
  `ProductIdentityTenantScopeHardening.cs`.
- **Variant pricing**: `Price` (POS), `OnlinePrice` (storefront, source of truth), `CostPrice`;
  `EffectiveOnlinePrice` falls back to Price. `ProductVariant.cs:61-72,191-193`.
- **Variant inventory**: `ProductVariantStock` per warehouse (Quantity, ReservedQuantity,
  DamagedQuantity; Available = Qty − Reserved). `ProductVariantStock.cs:38-58`.
- **Variant images**: `MediaId` pointer to `ProductMedia` preferred over `ImageUrl`.
  `ProductVariant.cs:121-127`.
- **Variant visibility / publication**: `IsActive`; marketplace publication is per-variant via
  `CommerceHubListing.VariantId`. `ProductVariant.cs:83`; `CommerceHubListing.cs:13`.
- **Variant identity / semantic lifecycle**: `SemanticLifecycleState` {Current, Merged, SplitParent,
  Detached}. Only `Current` variants own purchasability/SKU/barcode/stock. Uniqueness indexes scoped
  `IsDeleted=false AND SemanticLifecycleState=0`. `ProductVariantSemanticLifecycleState.cs:8-14`;
  `ProductSearchSqlAuthorityOverlay.cs:232`.
- **AttributeCombinationKey**: SHA-256 of normalized attribute set, unique per Tenant+Product.
  `ProductVariant.cs:56`.

---

## 9. Product State Machines (Implementation)

Implemented state machines:

1. **Publication**: `PublishStatus` {Draft=0, Published=1, Archived=2}. `PublishStatus.cs:6-16`.
   Soft delete forces `Archived`. `ProductsController.cs:843`.
2. **Online visibility**: `IsOnlineVisible` boolean (independent of `PublishStatus`); the *effective*
   storefront-visible flag is `IsActive && IsOnlineVisible && PublishStatus==Published`.
   `ProductSearchSqlAuthorityOverlay.cs:170-172`.
3. **Variant semantic lifecycle**: {Current, Merged, SplitParent, Detached}. `ProductVariantSemanticLifecycleState.cs:8-14`.
4. **Variant active**: `ProductVariant.IsActive`. `ProductVariant.cs:83`.
5. **CommerceHub listing**: `CommerceHubListing.IsPublished` (Publish/Unpublish; `RestorePublicationOnActivate`
   flag for SuperAdmin restore gating). `CommerceHubListing.cs:14,82-174`.
6. **Affiliate policy**: `AffiliateProductPolicy.IsEnabled` (requires category; rate [0.0001,0.50]).
   `AffiliateProductPolicy.cs:32,107-119`.
7. **Affiliate projection sync**: `PublicAffiliateProductDocument` via `AffiliateProductPolicyProjectionConsumer`.
8. **Search sync**: `CommerceHubProductDocument.SearchSyncStatus` {Pending,Synced,Failed,TerminalFailure},
   `MaxSearchRetries=5`. `CommerceHubProductDocument.cs:9-16`.
9. **Pixel override**: `ProductPixelOverride.IsEnabled`. `ProductPixelOverride.cs:33`.
10. **Soft-delete**: `IsDeleted` on Product/Variant/Stock/Media (global query filter). `ProductsController.cs:837`.

No single "Deleted/Recovering/Published" global product enum exists beyond `PublishStatus` +
`IsDeleted`; "Recovering" is operational (outbox operator reconciliation), not a product state.

---

## 10. Pricing Runtime Integration

- **Base/Retail (POS)**: `Product.SalePrice`, `ProductVariant.Price`. `Product.cs:61`; `ProductVariant.cs:61`.
- **Online/Storefront**: `Product.OnlinePrice`, `ProductVariant.OnlinePrice` — **source of truth for
  storefront**; falls back to Sale/Price. `Product.cs:67`; `ProductVariant.cs:67-72`;
  `ProductDocument.cs:135-139`.
- **Wholesale/Tier**: **NO tier/wholesale entity.** Pricing is columnar; multi-unit pricing lives on
  `ProductUnit.SellingPrice`. `ProductUnit.cs:25`.
- **Marketplace price**: `CommerceHubListing.CommerceHubPrice` (override, SQL) → resolved by
  `CommerceHubPriceAuthority.ResolveAsync` only when `CommerceHubPriceOverrideRuntimePolicy.
  IsEnabledForTenant` AND override>0, else falls back to variant effective online price
  (`PriceSource.ProductVariantEffectiveOnlinePrice`). Surfaced as `CommerceHubProductDocument.
  CommerceHubPriceOverride` + `OverridePresent` + `PriceSource`. Set only by a merchant/admin actor
  (override requires `actorUserId`). `CommerceHubListing.cs:17-21`; `CommerceHubProductDocument.cs:39-43`;
  `CommerceHubPriceAuthority.cs:94-124`; `CommerceHubCommandHandlers.cs:368,382,426-438`.
- **Facebook/Pixel price**: **No product marketing price field.** `ProductPixelOverride` overrides the
  *pixel conversion value* (Percentage or Fixed) per platform (Meta/Google/TikTok flag), not the
  product price. Real-vs-override is `actualOrderTotal` (real) vs the resolver's `resolvedValue`
  (override) in `PixelValueResolver`; the named strings "RealPrice"/"MarketingPrice" do NOT exist as
  code identifiers. `ProductPixelOverride.cs:36-62`; `PixelValueResolver.cs:95-178`.
- **Catalog Sync / Conversion Metadata**: there is **no Facebook catalog-sync / product-feed export**
  in code — Meta CAPI only sends server-side conversion events. Conversion metadata lives in the
  Tracking & Attribution runtime. Product Workspace stores only the override row.
  `CommerceHubProductDocument.cs:51`.
- **Discounts**: not a product-owned concept here; promotion overlay applied at projection time
  (`PromotionVisibilityProjectionSupport`). `ProductSearchSqlAuthorityOverlay.cs:328-341`.

---

## 11. Inventory Runtime Integration

- **Inventory ownership**: `ProductVariantStock` = SQL authority for stock (per variant/warehouse).
  `ProductVariantStock.cs:13-16` (legacy `WarehouseStock` = compat only, NOT authority).
- **Stock / reservations**: `Quantity`, `ReservedQuantity`, `DamagedQuantity`; `AvailableQuantity =
  Quantity − ReservedQuantity`. `ProductVariantStock.cs:38-58`.
- **Variant stock**: stock is per-variant; product-level `StockQuantity` is a SUM of active variants.
  `ProductSearchSqlAuthorityOverlay.cs:292-299`.
- **Warehouse links**: `ProductVariantStock.WarehouseId` → `Warehouse`. `ProductVariantStock.cs:33`.
- **Synchronization**: `ProductVariantStockChangedEvent` carries `MutationSource` (POS/CommerceHub) so
  downstream consumers reconcile paths without treating them as separate truths; `StockUpdatedAtUtc`
  monotonic timestamp rejects stale projections. `ProductVariantStockChangedEvent.cs:30-41,198-208`.

---

## 12. Media Runtime Integration

- **Amazon S3 / storage**: **No first-party S3 upload in the product media path.** `ProductMedia.Url`
  is a validated external URL (absolute or root-relative); images restricted to .jpg/.jpeg/.png/.webp;
  videos to YouTube/Vimeo/mp4. No bucket/key generation, no thumbnail pipeline. `ProductMediaService.cs:304-352`.
- **Media identity / storage keys / URLs**: `ProductMedia` row {Id, Url, Provider, Position}. URL is
  supplied by caller (e.g., a CDN/S3 they manage out-of-band). `ProductMedia.cs:7-16`.
- **Image processing / compression / thumbnail**: **Not implemented** in this runtime.
- **Gallery**: ordered `ProductMedia` list; `Media` field on `ProductDocument` is the forward model,
  legacy `Images` CSV is fallback. `ProductDocument.cs:251-258`.
- **Media ownership**: Product owns `ProductMedia`; variant references via `MediaId`. `ProductMedia.cs:9`.

---

## 13. AI Runtime Integration

- **AI Draft**: `AdminProductGeneratorService` calls an external LLM (CommerceAI endpoint) and returns
  a structured DTO (`AdminAiGeneratedProductResponse`) — it does **NOT** write Product rows.
  `AdminProductGeneratorService.cs:80-133,348-362`.
- **AI Description / Specs / SEO / Tags**: produced as DTO fields (description, features, faq, tags,
  metaTitle/Desc/Keywords, slug). Human copies into the Product form; normal `Create/Update` persists
  them and sets `AiGenerated=true`, `AiMetadataJson`. `ProductsController.cs:372-373`;
  `Product.cs:254-259`. (The "Commerce AI" columns on `StoreConfig` are governance feature-flags, not an
  AI content table; `StorefrontCommerceAiController` is usage telemetry, not content.)
- **AI Attributes / SEO / Tags / Images**: attributes handled as variant name/value on save. **AI image
  generation is NOT implemented** — the only image code is local file upload (`ImageService`, "// TODO:
  Azure Blob Storage"); media remain URL-only. No TTI/text-to-image call exists in the AI runtime.
- **Human Review / Acceptance / Overwrite**: the human is the gate — AI output is a suggestion; saving
  overwrites the product fields normally (`ProductUpdatedEvent` carries the AI text). No separate
  "AI draft" row to accept/reject; `AiGenerated` is a flag only. `ProductsController.cs:673-696`.
- **Overwrite rules**: manual edits fully replace AI-generated fields (no merge logic specific to AI);
  `AiMetadataJson` (BSON in Mongo) preserves provenance. `ProductDocument.cs:234-240`.

---

## 14. Affiliate Runtime Integration

- **Affiliate Eligible**: **Not a Product column.** Eligibility = existence of an enabled
  `AffiliateProductPolicy` (FK→Product, one per Tenant+Product). `AffiliateProductPolicy.cs:9-21`.
- **Visibility rules**: affiliate projection (`PublicAffiliateProductDocument`) derives pricing from
  the storefront `ProductDocument` via `ProductStorefrontVisibilityAuthority.TryResolveAffiliatePublicPricing`.
  `ProductStorefrontVisibilityAuthority.cs:29-86`.
- **Affiliate metadata / commission**: `AffiliateProductPolicy.CommissionRate` [0.0001,0.50],
  `MaxCommissionPerItem`, `TemplateType`, `AffiliateCategoryId` (required when enabled).
  `AffiliateProductPolicy.cs:27-49`.
- **Projection**: `AffiliateProductPolicyProjectionConsumer` builds the affiliate read model from the
  policy + catalog projection. `AffiliateProductPolicyProjectionConsumer.cs`.
- **Synchronization**: catalog events fan out; affiliate discovery reads the recomposed `ProductDocument`.
  `ProductStorefrontVisibilityAuthority.cs:161-165`.

---

## 15. CommerceHub Runtime Integration

- **Marketplace publication**: `CommerceHubListing` (variant-level, `IsPublished`); created/published
  via `CommerceHubCommandHandlers`. `CommerceHubListing.cs:9-80`.
- **Projection**: `CommerceHubProjectionConsumer` subscribes to the catalog event surface and
  re-derives `CommerceHubProductDocument` (one row per variant) using
  `CommerceHubSearchSqlAuthorityOverlay.ApplyAsync` — i.e., **SQL authority**, not message-only.
  `CommerceHubProjectionConsumer.cs:411,506,1104`.
- **Read models / visibility**: `CommerceHubProductDocument.IsPublished`, `InStock`, `AvailableStock`,
  `SearchSyncStatus`. `CommerceHubProductDocument.cs:56-159`.
- **Synchronization / indexing**: per-concern version stamps; `SearchSyncStatus` + `SearchRetryCount`
  (max 5) drive ES indexing with retry. `CommerceHubProductDocument.cs:14,159-161`.
- **Recovery**: SuperAdmin restore is SQL-authority gated (listing cannot be restored unless
  tenant/product/variant/category SQL all live). `CommerceHubListing.cs:141-174`.

---

## 16. Facebook Runtime Integration (Tracking & Attribution)

- **Facebook Pixel**: there is **NO Facebook product/marketing-price runtime.** `ProductPixelOverride`
  is a per-product override of the *pixel conversion value* (used by the Tracking & Attribution
  runtime's Meta CAPI emission). `ProductPixelOverride.cs:8-15`.
- **Marketing Price / Real Price**: **no such product fields.** `ProductPixelOverride.Strategy`
  {Percentage, Fixed} + `Value` computes the reported conversion value; precedence: product override →
  global override → actual price. `ProductPixelOverride.cs:36-43,11-15`.
- **Override rules**: `ApplyToMeta`/`ApplyToGoogle`/`ApplyToTikTok` flags (TikTok is a phantom in
  dispatch — no TikTok dispatcher exists). `ProductPixelOverride.cs:52-62`.
- **Catalog Sync / Conversion Metadata**: handled by the Tracking & Attribution runtime (server-side
  GA4 MP + Meta CAPI). Product Workspace only stores the override entity; it does not sync catalogs.
- **Conclusion**: "Facebook Marketing Projection" as a product-owned concept does **not exist**; it is
  a separate analytics runtime reading an override row. See `## Documentation Validation`.

---

## 17. Search Runtime Integration

- **Elasticsearch**: **single authority for product search**; 503 on failure (no Mongo fallback for
  search). `ProductSearchIndexService` + `ProductSearchSqlAuthorityOverlay`.
- **Projection / index documents**: `ProductSearchSqlAuthorityOverlay.ApplyAsync` rebuilds the
  `ProductDocument` from SQL (variants, stock, media, units, category, winning promotion) and returns
  `null` (→ delete ES doc) when: tenant inactive, category lineage broken, or no priced active variant.
  `ProductSearchSqlAuthorityOverlay.cs:92-143,285-289`.
- **SEO / search metadata / ranking**: `IsSearchable`, `IsFeatured`, promotion overlay; ranking via
  projection fields. `Product.cs:189-194`; `ProductSearchSqlAuthorityOverlay.cs:328-341`.

---

## 18. SEO Runtime

- **Slug**: stored column `Product.Slug` (denormalized; not uniquely indexed). `Product.cs:121`.
- **Meta Title/Description/Keywords**: `Product.MetaTitle/MetaDescription/MetaKeywords`. `Product.cs:126-136`.
- **SeoService** reads cache-first (Redis) → MongoDB `ProductDocument`, then **re-validates against SQL
  authority** via `ProductSearchSqlAuthorityOverlay.ApplyAsync` and returns null unless
  `{IsActive:true, IsOnlineVisible:true}` — so SEO fails-closed to SQL truth like every other read path.
  `SeoService.cs:140-149,1183-1211`. (It does NOT read raw PostgreSQL product rows on the hot path.)
  `SeoService.cs:204-250`. No dedicated `<meta og:…>` string emission or `Product`/`Offer` JSON-LD block
  in the service — it returns structured `OgImage`/`OgType`/canonical fields; HTML serialization is the
  frontend's concern. Breadcrumb `BreadcrumbList` JSON-LD is built (`SeoService.cs:1008-1024`).
- **Category slug**: computed from name at read time (`StorefrontCategoryService.GenerateSlug`),
  not stored. (Referenced in skill map; `SeoService.cs:250` builds category canonical from `GenerateSlug`.)

---

## 19. CQRS

- **Commands**: HTTP writes in `ProductsController`/`ProductVariantsController`/`ProductMediaController`
  mutate SQL aggregates inside a transaction. `ProductsController.cs:262-474,820-893`.
- **Queries**: admin reads SQL directly (`AsNoTracking`); storefront reads Mongo/ES then re-applies
  SQL overlay. `StorefrontProductService.cs:93,168,762-772`.
- **Events**: `ProductCreatedEvent`, `ProductUpdatedEvent`, `ProductDeletedEvent`,
  `ProductPublishStatusChangedEvent`, `ProductArchivedEvent`, `ProductVariantCreatedEvent/Updated/Deleted`,
  `ProductVariantStockChangedEvent`, `ProductMediaChangedEvent`, `VariantPriceUpdatedEvent`.
  `ProductEvents.cs`, `ProductVariantEvents.cs`.
- **Outbox (two backbones — important nuance)**: Catalog events (`ProductCreatedEvent`,
  `ProductUpdatedEvent`, `ProductVariantCreatedEvent/Updated/Deleted`, etc.) are published through
  `MassTransitEventPublisher` (registered as `IEventPublisher`, `Program.cs:2044`), which uses
  **MassTransit's transactional outbox** (`modelBuilder.AddOutboxMessageEntity()`,
  `ApplicationDbContext.cs:1230-1231`). The event row is written **atomic with the SQL business
  transaction** (`ProductsController.cs:395-398`), so a committed product always has its event
  published. A *separate* custom `OutboxMessage` + `OutboxPublisherJob` (Hangfire, every 5s) generic
  backbone exists (`OutboxMessage.cs:19-98`; `OutboxPublisherJob.cs:24-144`) but it currently carries
  **other** authoritative events (e.g. `TenantCurrencyChangedEvent`), NOT the product/variant catalog
  events — verified: no `ProductCreatedEvent/Variant` is ever constructed into the custom `OutboxMessage`
  table. Both backbones share the same discipline: monotonic `EventVersion`, critical-event ordering by
  (TenantId, AggregateId, EventVersion), and fail-closed reconciliation (critical poison →
  `RequiresOperatorReconciliation`, never silently dropped). `OutboxPublisherJob.cs:146-181,318-336`;
  `IntegrationEventAuthority.cs:9-37` (critical fragments include "Product"/"Variant"/"Stock").
- **Inbox**: not used by catalog (events are consumed by projection consumers, idempotent via EventId/version).
- **Replay / repair**: consumers reject stale versions; `ProjectionEventFence`/fence helpers; outbox
  critical-poison → `RequiresOperatorReconciliation`. `OutboxPublisherJob.cs:146-181`.
- **Projection**: Mongo `ProductDocument` + `ProductVariantDocument` + `CommerceHubProductDocument` +
  affiliate doc, all recomposed from SQL. `ProductSqlAuthorityProjectionWriter.cs:88-128`.

---

## 20. Persistence Runtime

- **SQL (PostgreSQL) ownership**: Product, ProductVariant, ProductVariantStock, ProductMedia,
  ProductUnit, Category, Unit, AffiliateProductPolicy, CommerceHubListing, ProductPixelOverride —
  all authoritative. `PointOfSale.Core/Entities/*`.
- **Mongo ownership**: read models only (`ProductDocument`, `CommerceHubProductDocument`,
  `PublicAffiliateProductDocument`, storefront pages/footer/nav). Recomposed from SQL; tombstoned on
  SQL non-visibility. `ProductSqlAuthorityProjectionWriter.cs:94-112`.
- **Redis ownership**: cache invalidation only (e.g., `ProductCacheInvalidationConsumer`); not truth.
- **Amazon S3 ownership**: **none in product path** (URL-only media).
- **Elasticsearch ownership**: search index single authority; recomposed from SQL.
  `ProductSearchSqlAuthorityOverlay.cs:68-83`.
- **Read vs Write models**: Write = SQL aggregates; Read = Mongo/ES projections (repair-on-read).

---

## 21. Runtime Authority Matrix

| Concept | Owner (mutator) | Runtime truth | Projection writer | Notes |
|---|---|---|---|---|
| Product | ProductsController (SQL) | PostgreSQL `Products` | `ProductSqlAuthorityProjectionWriter` | `ProductsController.cs:262` |
| Variant | ProductVariantsController/Service (SQL) | `ProductVariants` | `ProductVariantProjectionConsumer` | `ProductVariantService.cs:203` |
| Price | Product/Variant/Unit (SQL) | `Products`/`ProductVariants`/`ProductUnits` | overlay | `OnlinePrice` storefront truth |
| Media | ProductMediaService (SQL) | `ProductMedia` | `ProductMediaChangedEventConsumer` | URL-only |
| Inventory link | Inventory runtime (SQL) | `ProductVariantStock` | stock consumers | link only |
| SEO | ProductsController (SQL) | `Products` | overlay/SeoService | `Product.cs:121` |
| Affiliate | Affiliate runtime (SQL) | `AffiliateProductPolicy` | `AffiliateProductPolicyProjectionConsumer` | NOT on Product |
| CommerceHub projection | CommerceHub runtime (SQL) | `CommerceHubListing` | `CommerceHubProjectionConsumer` | SQL overlay |
| Facebook projection | Tracking/Attribution runtime | `ProductPixelOverride` | (read by pixel emit) | conversion value only |
| AI Draft | AI runtime (returns DTO) | n/a (human writes Product) | n/a | `AdminProductGeneratorService.cs:80` |
| Barcode/SKU | Product/Variant (SQL) | `Products`/`ProductVariants` | overlay | tenant-scoped SQL uniqueness; ProductUnit barcode conflicts enforced by tenant-aware trigger |

---

## 22. Cross Runtime Authority Graph

| Peer | Reads from | Writes | Events consumed | Runtime authority | Projection | Ownership |
|---|---|---|---|---|---|---|
| Inventory | SQL `ProductVariantStock` | stock mutations | `ProductVariantStockChangedEvent` | Inventory runtime | stock consumers | link only |
| Orders | SQL catalog (read price/stock at checkout) | orders | catalog events | Order runtime | — | reads catalog |
| Media | SQL `ProductMedia` | media rows | `ProductMediaChangedEvent` | Product (URL) | media consumer | URL only |
| Affiliate | recomposed `ProductDocument` | `AffiliateProductPolicy` | catalog + policy events | Affiliate runtime | affiliate doc | policy only |
| CommerceHub | SQL overlay | `CommerceHubListing` | full catalog surface | CommerceHub runtime | CH doc | listing only |
| Payments | catalog (read-only) | payments | — | Payment runtime | — | reads catalog |
| Analytics | `LedgerEntries`/SQL | analytics rows | order events | Analytics runtime | — | separate |
| Builder/Storefront | Mongo/ES (recomposed) | — | catalog events | Storefront read | Mongo/ES | read-only |
| Notifications | — | notifications | catalog events | Notification runtime | — | separate |
| AI | LLM API | DTO → human | — | AI runtime | — | suggestion only |
| Facebook/Tracking | `ProductPixelOverride` | pixel override | — | Attribution runtime | — | override only |
| Search | SQL overlay | ES index | catalog/stock events | Search runtime | ES | index only |
| Recovery | outbox/SQL | outbox rows | — | Outbox job | — | operator reconciliation |

---

## 23. Business Invariants (Implementation-Backed)

- A Product **must** have `TenantId` (NOT NULL, DB-enforced). `Product.cs:15`.
- A Product **must** have a `CategoryId != Guid.Empty` (security: prevents category-scoped coupon
  bypass). `ProductsController.cs:270-282`; `ProductConfiguration.cs:53-55`.
- A Product **must** have exactly one default variant. `ProductVariantService.cs:210-219`.
- Product and Variant SKU/Barcode authority **must** be tenant-scoped; cross-tenant reuse is allowed,
  same-tenant live ownership is rejected by SQL indexes/triggers and helper checks.
  `ProductIdentityTenantScopeHardening.cs`; `ProductVariantCloneImportAuthority`.
- A Variant SKU/Barcode **must** be unique per tenant (cross-entity checked).
  `ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync`;
  `ProductsController.cs:284-299`.
- A Variant `AttributeCombinationKey` **must** be unique per Tenant+Product. `ProductVariant.cs:56`.
- Only `Current` semantic variants may own SKU/barcode/stock/public visibility.
  `ProductVariantSemanticLifecycleState.cs:8-14`.
- A Product is storefront-visible **only if** `IsActive && IsOnlineVisible && PublishStatus==Published`.
  `ProductSearchSqlAuthorityOverlay.cs:170-172`.
- `OnlinePrice` is the storefront pricing source of truth. `ProductDocument.cs:135-139`.
- Stock `AvailableQuantity = Quantity − ReservedQuantity`. `ProductVariantStock.cs:53`.
- Affiliate policy requires a category and rate in [0.0001,0.50]. `AffiliateProductPolicy.cs:95-124`.
- CommerceHub price override requires an actor (no anonymous override). `CommerceHubListing.cs:59`.
- Media URLs must be image (jpg/png/webp) or video (YouTube/Vimeo/mp4). `ProductMediaService.cs:304-352`.
- Outbox critical events must publish in version order; exhausted critical poison → operator
  reconciliation (never silently dropped). `OutboxPublisherJob.cs:146-181,318-336`.

---

## 24. Runtime Timeline (One Full Lifecycle, with Authority/Sync/Consistency)

1. **Create Product** — Owner: ProductsController; Authority: SQL `Products`+default `ProductVariants`;
   Sync: `ProductCreatedEvent`+`ProductVariantCreatedEvent` via the **MassTransit transactional outbox**
   (atomic with the SQL save, `ProductsController.cs:395-398`); Consistency: transaction commit gates
   events; Retry: MassTransit outbox (at-least-once, idempotent consumers); Failure: rollback whole tx
   (`ProductsController.cs:379-468`); Events: created+variant-created.
2. **AI Draft Generated** — Owner: AI runtime (DTO only); Authority: none on Product until human saves;
   Sync: n/a; Consistency: suggestion; Failure: 503/429/504 to UI (`AdminProductGeneratorService.cs:87-173`).
3. **Manual Editing** — Owner: ProductsController; Authority: SQL; Sync: `ProductUpdatedEvent`; Retry: outbox.
4. **Variants Added** — Owner: ProductVariantService; Authority: SQL `ProductVariants`; Sync: variant events.
5. **Images Uploaded** — Owner: ProductMediaService; Authority: SQL `ProductMedia` (URL); Sync: `ProductMediaChangedEvent`.
6. **Pricing Configured** — Owner: Product/Variant; Authority: SQL; Sync: `VariantPriceUpdatedEvent`/`ProductUpdatedEvent`.
7. **Affiliate Enabled** — Owner: Affiliate runtime; Authority: SQL `AffiliateProductPolicy`; Sync: policy event.
8. **CommerceHub Published** — Owner: CommerceHub runtime; Authority: SQL `CommerceHubListing`; Sync: listing event.
9. **Facebook/Pixel Override** — Owner: Attribution runtime; Authority: SQL `ProductPixelOverride` (value only).
10. **Inventory Synchronized** — Owner: Inventory runtime; Authority: SQL `ProductVariantStock`; Sync: `ProductVariantStockChangedEvent` (MutationSource).
11. **Search Indexed** — Owner: Search runtime; Authority: ES recomposed from SQL; Consistency: SQL overlay deletes doc if hidden (`ProductSearchSqlAuthorityOverlay.cs:170-172,285-289`).
12. **Customer Purchases** — Owner: Order/Payment runtimes; read catalog SQL for price/stock; reserve via inventory.
13. **Inventory Updated** — Owner: Inventory runtime; emits stock event; projection re-syncs.
14. **Product Archived / Soft Deleted** — Owner: ProductsController; Authority: SQL (`IsDeleted`, Archived);
    Sync: delete+archive+variant-delete events; Consistency: tx; Recovery: operator reconciliation on poison.

---

## 25. Runtime Classification (Verdict)

The Product Workspace is **fundamentally a Product Authoring + Catalog (PIM) Runtime** — a
**SQL-authoritative write model with event-driven fan-out** — NOT a "Composition Runtime" in the
orchestrator sense and NOT a projection/PIM-read runtime. Evidence:

- It owns the canonical SQL aggregates (Product, Variant, Stock link, Media, SEO, Publication).
  `Product.cs`, `ProductVariant.cs`, `ProductMedia.cs`.
- It is the *source* of every downstream projection; none originate truth. `ProductSqlAuthorityProjectionWriter.cs:88-112`.
- Variants are first-class aggregates, so it is also a **Variant Runtime** composed under the catalog.
- "Hybrid" correctly describes it only in the sense that it spans authoring (write) + catalog (read
  model emission) + lifecycle (publish/archive/delete) — but authority is singular (PostgreSQL).

Accurate label: **Product Authoring & Catalog Runtime (PIM write model) within the Merchant Platform.**

---

## 26. Final Architecture Answer

"How does Shopixy let one Product Workspace orchestrate authoring, variants, pricing, media, AI,
affiliate, CommerceHub, Facebook, inventory, search, and every subsystem while preserving ownership,
authority, consistency, and separation of concerns?"

Answer (implementation-backed): **It does not centrally orchestrate those runtimes.** Instead it uses
a **single PostgreSQL-authoritative catalog write model + MassTransit's transactional outbox**, and every
other subsystem is a **separate runtime that projects from the catalog and re-proves SQL authority on
every read**:

1. **One authority**: Product + Variant + Stock-link + Media + SEO + Publication are written only to
   PostgreSQL, tenant-scoped, under a transaction that also writes the MassTransit outbox row.
   `ProductsController.cs:379-447`; `ApplicationDbContext.cs:1230-1231`; `Program.cs:2044`.
2. **Event fan-out, not RPC**: catalog changes emit integration events; CommerceHub, Affiliate,
   Storefront, Search, Media consumers each subscribe. `ProductEvents.cs`, `ProductVariantEvents.cs`.
3. **Projections never originate truth**: Mongo/ES documents are *recomposed from SQL* on each public
   read; if SQL no longer proves `IsActive && IsOnlineVisible && PublishStatus==Published`, the
   projection is tombstoned/deleted (fail-closed). `ProductSqlAuthorityProjectionWriter.cs:94-112`;
   `ProductSearchSqlAuthorityOverlay.cs:170-172,285-289`.
4. **Separate runtimes own their own tables**: Affiliate (`AffiliateProductPolicy`), CommerceHub
   (`CommerceHubListing`), Facebook/pixel (`ProductPixelOverride`), AI (DTO suggestion) — each is a
   distinct SQL entity/aggregate, not a Product column. `AffiliateProductPolicy.cs:14`;
   `CommerceHubListing.cs:9`; `ProductPixelOverride.cs:16`; `AdminProductGeneratorService.cs:80`.
5. **Consistency via version fencing**: `ProjectionMutationVersion` + per-concern stamps
   (`LastStockEventVersion`, `LastEventVersion`) + outbox (TenantId, AggregateId, EventVersion) ordering
   reject stale events; critical poison → operator reconciliation. `Product.cs:210-225`;
   `OutboxPublisherJob.cs:146-181,318-336`.
6. **Inventory link only**: the Product Workspace owns the `ProductVariantStock` rows (the link) but
   the operational inventory runtime owns reservation/fulfillment execution. `ProductVariantStock.cs:13-16`.

This keeps runtime ownership crisp (each subsystem owns its own table), authority unambiguous
(PostgreSQL catalog is the only writer of product truth), consistency guaranteed (outbox + SQL
recomposition + version fencing), and concerns separated (authoring vs projection vs marketplace vs
affiliate vs pixel vs AI are independent runtimes sharing one event stream).

---

## 27. Documentation Validation

Assumptions from the prompt, checked against implementation:

| Prompt assumption | Verdict | Evidence / correction |
|---|---|---|
| "Product Composition Runtime" as a central orchestrator | **Incorrect** | Product Workspace is a SQL-authoritative PIM write model + outbox fan-out; it does not orchestrate sibling runtimes. `ProductsController.cs:379-447`. |
| Product is a projection / marketplace / inventory entity | **Incorrect** | Product is an Aggregate Root (catalog parent) owning SQL truth. `Product.cs:9`. |
| Brand is an entity / Attribute is a taxonomy | **Incorrect** | Brand = free-text `Product.Brand`; attributes = denormalized name/value on `ProductVariantAttribute`. `Product.cs:234`; `ProductVariantAttribute.cs:18`. |
| Pricing tiers / wholesale entity exist | **Incorrect** | No tier/wholesale entity; pricing is columnar + per-`ProductUnit.SellingPrice`. `ProductUnit.cs:25`. |
| Catalog events use one generic outbox `OutboxMessage`+Hangfire job | **Partially incorrect** | Catalog events ride **MassTransit's transactional outbox** (atomic with SQL save); the separate custom `OutboxMessage`/`OutboxPublisherJob` Hangfire backbone carries *other* authoritative events (e.g. `TenantCurrencyChangedEvent`), not product/variant events. `Program.cs:2044`; `ApplicationDbContext.cs:1230-1231`; subagent verified no catalog event writes the custom table. |
| Facebook "Marketing Price" / "Real Price" product fields exist | **Incorrect** | No such product fields; real-vs-override is `actualOrderTotal` vs `resolvedValue` in `PixelValueResolver`. `PixelValueResolver.cs:95-178`. |
| Facebook catalog sync / product feed | **Incorrect (absent)** | No Facebook catalog-sync/feed-export code; Meta CAPI sends conversion events only. `ProductPixelOverride.cs:16`. |
| Facebook runtime / catalog sync owned by Product | **Incorrect** | Facebook = Tracking & Attribution runtime; Product stores only an override row. `ProductPixelOverride.cs:16`. |
| AI generates and writes product content directly | **Incorrect** | AI returns a DTO to the human; human saves via normal Update; `AiGenerated` is a flag. `AdminProductGeneratorService.cs:80-133`. |
| Media uses Amazon S3 / thumbnail generation / CDN | **Incorrect** | Media are validated external URLs only; no S3 uploader/thumbnail pipeline. `ProductMediaService.cs:304-352`. |
| Affiliate eligibility is a Product column | **Incorrect** | Separate `AffiliateProductPolicy` SQL entity (FK→Product). `AffiliateProductPolicy.cs:14-21`. |
| CommerceHub publication is a Product column | **Incorrect** | Separate `CommerceHubListing` (variant-level) SQL entity. `CommerceHubListing.cs:9-13`. |
| "Recovering" is a product state | **Incorrect** | Recovery is outbox operator reconciliation, not a product enum. `OutboxPublisherJob.cs:146-181`. |
| SKU/Barcode are purely product-level | **Partially incorrect** | For variants, SKU/Barcode live on `ProductVariant` and are mirrored to Product only for simple products. `ProductVariantService.cs:351,364`. |
| Product slug is computed/unique | **Incorrect** | Slug is a stored, non-uniquely-indexed column. `Product.cs:121`. |
| SKU/Barcode are globally unique across all tenants | **Incorrect** | Product/variant identity is tenant-scoped. Cross-tenant SKU/barcode reuse is allowed; same-tenant live collisions are blocked by SQL indexes/triggers and helper checks. `ProductIdentityTenantScopeHardening.cs`; `ProductVariantCloneImportAuthority.cs`. |

Every claim above is cited to `file:line`. Where implementation contradicts the prompt, implementation
wins; the incorrect assumptions have been replaced with the verified architecture.
