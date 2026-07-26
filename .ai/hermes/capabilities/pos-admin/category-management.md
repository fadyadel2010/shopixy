# POS Admin — Category Management

> Reverse-engineered from implementation. Implementation is the source of truth; where prose docs
> disagree, the code wins and the disagreement is reported under **Documentation Validation**.
> Scope: **POS Admin ERP Categories only** — the tenant-owned catalog taxonomy that organizes the
> local ERP product catalog and the POS Admin's own Storefront. This is NOT CommerceHub categories,
> NOT Affiliate categories, NOT Marketplace/Expense categories. Those are separate systems and are
> documented here only where a POS product *maps* to them.

Owning files (authoritative):
- `PointOfSale.Core/Entities/Category.cs` — the entity (minimal: Name, Description, ParentCategoryId).
- `PointOfSale.API/Controllers/CategoriesController.cs` — the ENTIRE admin write+read capability
  (no separate application service; all logic is inline in the controller against `ApplicationDbContext`).
- `PointOfSale.API/Controllers/StorefrontCategoriesController.cs` +
  `PointOfSale.API/Services/Storefront/StorefrontCategoryService.cs` — read-only storefront surface.
- `PointOfSale.API/Services/Catalog/CategoryLineageAuthority.cs` — SQL lineage/liveness authority.
- `PointOfSale.Infrastructure/Data/Configurations/ProductConfiguration.cs` — Product→Category FK.

# Purpose

POS Admin Categories exist to give a tenant a **hierarchical way to organize its own ERP product
catalog** and to drive **its own Storefront navigation**. They solve three business problems:

1. **Catalog organization** — every `Product` MUST belong to exactly one Category (`Product.CategoryId`
   is `IsRequired()` and must not be `Guid.Empty`). Categories are the primary grouping axis for the
   ERP catalog, reporting (Sales-by-Category), and coupon scoping.
2. **Storefront navigation** — the tenant's public storefront renders a category tree (mega menu /
   nav) and category landing pages resolved by slug.
3. **Coupon/discount scoping** — category-scoped coupons and promotions target `Product.CategoryId`;
   the `Guid.Empty` ban exists specifically to prevent empty-category coupon bypass.

What Categories **own**: the tenant's catalog taxonomy (Name, Description, parent/child structure)
and the identity that products are filed under. Nothing else.

What Categories **do NOT own**: products, variants, pricing, inventory/stock, orders, the storefront
read model, search indexes, SEO metadata storage, CommerceHub taxonomy, Affiliate taxonomy, or any
external category. They author a small structural truth in PostgreSQL and are read by downstream
consumers.

# Business Scope

Reverse-engineered against implementation. The entity is deliberately minimal:

```
Category : BaseEntity, ITenantOwned
  TenantId            Guid   (NOT NULL, DB-enforced, tenant ownership)
  Name                string
  Description         string?
  ParentCategoryId    Guid?  (self-FK; null/Guid.Empty = root)
  ParentCategory      nav
  SubCategories       nav (children)
  Products            nav
  (+ BaseEntity: Id, CreatedAt, UpdatedAt, IsDeleted)
```

BELONGS to POS Category (implementation-proven):
- **Category** — a named node owned by a tenant.
- **Category Tree** — 2-level hierarchy (root → subcategory). Enforced HARD (see Category Tree).
- **Parent / Children / Hierarchy** — via `ParentCategoryId` self-FK.
- **Slug** — DERIVED, not stored. Computed from `Name` at storefront read time
  (`GenerateSlug`: lowercase, spaces→`-`, `&`→`and`, strip non `[a-z0-9-]`). There is NO slug column.
- **Visibility** — there is NO explicit visibility/publish flag. A category is "visible" iff it is
  live in SQL lineage (not soft-deleted, and its whole parent chain is live). Visibility == lineage
  liveness, computed on read.
- **Status** — only `IsDeleted` (soft delete). No Active/Inactive/Archived/Draft states exist.
- **Description** — free-text `Description` column.
- **Storefront Navigation** — the category tree feeds the storefront nav/landing pages.
- **Category Deletion** — soft delete (`IsDeleted = true`), guarded.
- **Category Depth** — capped at 2 levels, enforced on create and update.
- **Category Lineage** — recursive liveness (`CategoryLineageAuthority`): a category is live only if
  itself and its entire ancestor chain are non-deleted and same-tenant; cycles/self-parent → dead.
- **Category Permissions** — role-gated: read = any authenticated tenant user; create/update =
  `Owner,Admin,Manager`; delete = `Owner,Admin`.
- **Default seeding** — first read for a tenant with no categories auto-seeds two roots: `General`
  and `Uncategorized` (`EnsureDefaultCategoriesAsync`).

Does NOT belong / NOT implemented (do not invent):
- **Display Order / SortOrder** — NO ordering column on POS Category. Lists are ordered by `Name`
  alphabetically. (Contrast: CommerceHubCategory and AffiliateCategory DO have SortOrder/DisplayOrder.)
- **SEO** (meta title/description/keywords), **Images**, **Icons** — NO columns on POS Category.
  (Contrast: CommerceHubCategory has `IconEmoji`.) The only SEO-adjacent artifact is the derived slug.
- **Category Merge** — NOT implemented. No merge endpoint/service.
- **Category Split** — NOT implemented.
- **Category Restore** — NOT implemented. Soft delete is one-way; there is no restore/undelete endpoint.
- **Explicit publish/hide** — NOT implemented (visibility is derived from lineage liveness).
- **Category-owned products/pricing/inventory/orders/search/SEO storage** — owned elsewhere.

# Authority Boundaries

| Concern | Authority | Notes |
|---|---|---|
| Category Tree / structure | **PostgreSQL `Categories`** (SQL) | Single source of truth. `ParentCategoryId` self-FK. |
| Category Metadata (Name/Description) | **PostgreSQL `Categories`** | Only Name + Description are stored. |
| Slug | **Derived (code)** | `GenerateSlug(Name)` at read time. Not stored, not authoritative anywhere. |
| Visibility | **Derived from SQL lineage** | `CategoryLineageAuthority.IsLiveInLineage` — no stored flag. |
| Storefront category read | **SQL-recomposed** (Mongo/Redis are NOT return authority) | `StorefrontCategoryService` explicitly recomposes from SQL on every read; Redis is best-effort cache, Mongo `CategoryDocument` exists but is NOT the return source. |
| Navigation tree | **SQL** (`BuildSqlCategoryTreeAsync`) | Built in-memory from live SQL rows. |
| Products filed under a category | **Product** (`Product.CategoryId`) owns the link | Category does not own products; FK is `OnDelete(Restrict)`. |
| Affiliate taxonomy | **AffiliateCategory** (separate global table) | POS product MAPS via `AffiliateProductPolicy.AffiliateCategoryId`; POS Category does NOT own it. |
| CommerceHub taxonomy | **CommerceHubCategory** (separate global table) | POS product MAPS via `CommerceHubListing.CommerceHubCategoryId`; POS Category does NOT own it. |
| SEO storage | **None** (only derived slug) | No SEO fields on POS Category. |
| Search index (`CategorySearchDocument`) | **Elasticsearch (projection/evidence)** | Derivative read model; SQL wins. |
| Analytics / Recommendations | **Downstream consumers** | Read `Product.CategoryId`; not owned by Category. |
| Presentation | **Frontend** (`pages/Categories.jsx`, storefront `CategoryGrid`) | UI only. |

Rule of thumb: **PostgreSQL `Categories` is the ONLY authority.** Everything else (Mongo
`CategoryDocument`, Redis cache, Elasticsearch `CategorySearchDocument`, storefront tree JSON) is a
derivative projection/evidence and is re-derived from SQL on read.

# Category Lifecycle

Operator intent → validation → SQL → (best-effort projection) → storefront read. There is NO outbox
event, NO MassTransit publish, and NO background job for POS Category mutations — the admin controller
writes SQL directly and returns. Projections (Mongo/ES) are updated by OTHER pipelines that read SQL.

```
Create ──> (Live in lineage) ──> Edit / Rename / Reparent ──> Soft Delete (guarded, terminal)
              │                                                        │
              └── read by storefront tree/slug (SQL-recomposed) ──────┘
```

Stage-by-stage (admin write path, `CategoriesController`):

1. **Create** (`POST /api/categories`, roles Owner/Admin/Manager):
   - If `ParentCategoryId` set → parent must exist, be same-tenant, not deleted, AND itself be a root
     (parent's `ParentCategoryId` must be null) → enforces max depth 2.
   - Duplicate guard: no other live category with the SAME `Name` at the SAME parent level.
   - Insert row with `CreatedAt = UtcNow`. `SaveChangesAsync`. Return `CategoryDetailDto`.
2. **Read/List** (`GET /api/categories`, `/tree`, `/{id}`): auto-seeds `General`+`Uncategorized`
   for empty tenants, then returns alphabetical list / 2-level tree with product counts.
3. **Edit** (`PUT /api/categories/{id}`, roles Owner/Admin/Manager):
   - Full overwrite of Name/Description/ParentCategoryId. `UpdatedAt = UtcNow`.
   - Reparent validation: cannot self-parent; parent must exist/same-tenant/live/be-a-root (depth 2);
     **cannot move a category that itself has subcategories** (prevents creating a 3-level chain).
4. **Delete** (`DELETE /api/categories/{id}`, roles Owner/Admin) — soft, guarded (see rules).
5. **Storefront read** — `StorefrontCategoryService` re-derives tree/slug from live SQL lineage on
   every request; Redis is a 30-min best-effort cache (fire-and-forget populate), never return authority.

There is NO Restore stage — deletion is terminal.

# Category Tree

REQUIRED section. The tree is **strictly 2 levels: root → subcategory. No Level 3.**

- **Parent** — `ParentCategoryId` self-FK (nullable). `null` or `Guid.Empty` = root.
- **Children** — `SubCategories` navigation; queried as `ParentCategoryId == this.Id`.
- **Depth cap = 2, enforced in TWO places** on create AND update:
  `if (parentCategory.ParentCategoryId.HasValue) → 400 "Maximum category depth is 2 levels"`. A parent
  must be a root; you cannot parent under a subcategory.
- **Move restriction** — on update, if the category being moved *has* subcategories, the move is
  rejected (`400 "Cannot move category that has subcategories"`). This is the second guard preventing
  a 3-level chain forming via reparenting.
- **Self-parent** — rejected on update (`400 "Category cannot be its own parent"`).
- **Cycles** — structurally impossible at write time given the depth-2 + move guards. Additionally,
  the read-side `CategoryLineageAuthority.IsLiveInLineage` carries a `visiting` HashSet and treats any
  cycle or self-parent (`parentId == categoryId`) as DEAD (not live) — defensive against corrupt data.
- **Root categories** — `ParentCategoryId` null/empty. `IsRootCategory(parentId)` in both admin tree
  and storefront service.
- **Leaf categories** — any category with no live children (roots may also be leaves).
- **Recursive validation (read-side liveness)** — `CategoryLineageAuthority`: a category is "live in
  lineage" only if it is non-deleted, same-tenant, AND its full ancestor chain is also live. A live
  child of a soft-deleted parent is NOT live (its subtree disappears from storefront).
- **Orphan handling (storefront)** — in `BuildCategoryTree` (Mongo path) a node whose `ParentId`
  points to a category NOT in the live filtered set is **hidden**, NOT promoted to root
  (`"Orphan category hidden from storefront tree"`). Parent lineage is authority.
- **Deleting a parent — effect on descendants:** the admin delete guard BLOCKS deleting a category
  that has live subcategories (`400 "Cannot delete category that has subcategories"`), so a parent
  cannot be deleted out from under live children through the admin API. (If a parent were deleted by
  other means, the read-side lineage authority would drop the whole subtree from storefront.)
- **Deleting a parent — effect on products:** delete is also BLOCKED if the category has ANY products
  (`400 "Cannot delete category that has products"`). Products are never orphaned by category delete.

# Reverse-Engineered Implementation Map

Frontend (POS Admin): `PointOfSale.Web/src/pages/Categories.jsx` (admin CRUD UI),
`SalesByCategoryReport.jsx` (reporting). Storefront: `modules/storefront/components/sections/
CategoryGrid.jsx` + `SubcategoryGrid.jsx` (nav/landing). Help: `admin/help/helpArticles/categories.md`.
(UI is out of scope — capability, not screens.)

Backend endpoints:
- Admin: `CategoriesController` (`api/categories`) — GET (list, `?includeChildren`), GET `tree`,
  GET `{id}`, POST, PUT `{id}`, DELETE `{id}`. `[Authorize]` at class; mutation methods add role gates.
- Storefront: `StorefrontCategoriesController` (`api/storefront/categories`) — `[AllowAnonymous]`,
  GET tree + GET `{slug}`. Tenant via `X-Tenant-Id` header (`StorefrontTenantContextResolver`).

Services / authority:
- `CategoryLineageAuthority` (static) — loads a per-tenant snapshot (`IgnoreQueryFilters`, all rows
  incl. deleted) and computes recursive liveness + memoization.
- `StorefrontCategoryService` (`IStorefrontCategoryService`) — SQL-authority tree/slug composition,
  Redis best-effort cache, Mongo `ICategoryReadRepository` present but NOT the return source.
- No dedicated admin `ICategoryService` — admin logic lives inline in the controller.

Persistence: PostgreSQL `Categories` table (`ApplicationDbContext.Categories`). Query filter:
`!IsDeleted && TenantId == CurrentTenantId`. `Product` FK: `HasOne(Category).WithMany(Products)
.HasForeignKey(CategoryId).OnDelete(Restrict)`; `CategoryId` `IsRequired()`; index `(TenantId,CategoryId)`.

Events / Outbox: **NONE for POS Category.** No `CategoryCreated/Updated/Deleted` integration event
exists (`grep` confirms only `AffiliateCategoryUpdatedEvent` — a different system). POS Category
mutations do not publish to MassTransit and have no outbox row.

Projections / read models (derivative, SQL-recomposed):
- Mongo `CategoryDocument` (`CategoryReadRepository`, `MongoDbContext`) — carries Name/NameAr/Slug/
  ParentId + promotion-banner fields; written/maintained by `PromotionProjectionConsumer` (promotion
  pipeline), NOT by the category controller. Storefront service does NOT return it as authority.
- Elasticsearch `CategorySearchDocument` (`UnifiedSearchIndexService.IndexCategoryAsync`) — search
  index; derivative/evidence.
- Redis — storefront tree + by-slug caches, 30-min TTL, fire-and-forget, graceful on failure.

Background jobs: none specific to POS Category.

# Product Relationship

```
POS Category ──(1:N, Product.CategoryId, FK Restrict)──> Product ──> ProductVariant (SKU/price/stock)
```

- **Ownership:** the Product owns its category link (`Product.CategoryId`, required, `!= Guid.Empty`).
  A Category owns NO product data. FK `OnDelete(Restrict)` + the controller's own product-existence
  guard together prevent deleting a category that has products.
- **Category → Product → Variant → Storefront/Search/SEO/Pricing/Inventory:** the Category contributes
  ONLY the grouping identity. Variant owns SKU/price/stock (see Product/Inventory/Pricing capabilities);
  storefront visibility/pricing/stock are computed by their own services. Category does not gate
  purchasability — a live category with products does not itself make a product visible/sellable;
  product-level `IsOnlineVisible`/`PublishStatus`/stock rules do.
- **Coupon scoping:** category-scoped coupons filter on `Product.CategoryId`; `Guid.Empty` is banned
  at the DB/config level precisely to stop empty-category coupon bypass.
- **Counts:** admin DTOs compute `ProductCount` per category via live `Products` count queries.

# Storefront Relationship

```
POS Category (SQL) ──recompose──> Storefront category tree + category-by-slug landing pages
```

- **Navigation/Browsing:** `GET /api/storefront/categories` returns the live 2-level tree
  (`BuildSqlCategoryTreeAsync`) for menus. Anonymous, tenant via `X-Tenant-Id`.
- **Landing pages:** `GET /api/storefront/categories/{slug}` resolves a category by DERIVED slug
  (`GenerateSlug(Name)`) against live lineage rows. If the tenant SQL lineage is inactive/missing,
  the tree is suppressed (empty) and slug lookup returns null → 404.
- **Collections/landing content:** promotion-banner fields on the Mongo `CategoryDocument`
  (HasActivePromotion, PromotionBannerText/EndDate/Style) are maintained by the promotion pipeline,
  not by Category itself.
- **Visibility:** derived — only categories live in lineage appear; soft-deleted or orphaned nodes are
  hidden; a subtree under a deleted parent disappears entirely.
- **Search:** storefront search over categories uses Elasticsearch `CategorySearchDocument` (derivative).
- **Caching:** Redis (30-min TTL) is best-effort; SQL recomposition is always the return authority, so
  a stale/missing cache never yields wrong or stale category structure.

# Affiliate Relationship

REQUIRED. **Affiliate has its OWN taxonomy — it does NOT use POS Categories.**

- `AffiliateCategory` (`PointOfSale.Core/Entities/Affiliate/AffiliateCategory.cs`, "AF-30 Global
  Marketplace Taxonomy Layer") is a **global, curated, non-per-tenant** taxonomy, explicitly
  "Isolated from POS store categories". Level 1 → Level 2, max depth 2, globally-unique slug,
  `(ParentId+Name)` unique, `IsActive` toggle (no soft delete). Managed by
  `AdminAffiliateCategoryController` (`api/admin/affiliate/categories`) — **mutations are Super Admin
  ONLY** (platform-level), reads are Owner/Admin.
- **Mapping, not ownership:** a POS product participates in affiliate discovery via
  `AffiliateProductPolicy.AffiliateCategoryId` — an FK from the affiliate policy to `AffiliateCategory`.
  This is a MAP (product → affiliate category), NOT POS Category owning affiliate categories.
- POS Category has **zero** references to AffiliateCategory. Changing a POS Category has NO effect on
  affiliate taxonomy, and vice versa. `AffiliateCategoryUpdatedEvent` belongs to that system only.

# CommerceHub Relationship

REQUIRED. **CommerceHub has its OWN taxonomy — POS Category does NOT own it.**

- `CommerceHubCategory` (`PointOfSale.Core/Entities/CommerceHubCategory.cs`) — "Global marketplace
  category shared across ALL tenants" (NOT tenant-scoped). Has `Name/NameAr/Slug/ParentId/Level/
  IconEmoji/IsActive/SortOrder` and a `Children` + `Attributes` collection
  (`CommerceHubCategoryAttribute`). Served by `CommerceHubCategoriesController`
  (`api/commercehub/categories`, `[AllowAnonymous][AllowAnonymousTenant]`, platform-level, no tenant
  context). Guarded by `CommerceHubCategoryLineageAuthority` (separate from POS lineage authority).
- **Mapping, not ownership:** a product listed on CommerceHub carries the mapping on the listing:
  `CommerceHubListing.CommerceHubCategoryId` (required, validated `!= Guid.Empty`) + `ProductId` +
  `VariantId`. So the chain is:

  ```
  POS Product ──> CommerceHubListing.CommerceHubCategoryId ──> CommerceHubCategory
                                                                  └─> (SubCategory via ParentId/Level)
                                                                  └─> CommerceHubCategoryAttribute(s)
  ```

- CommerceHub SubCategory = a `CommerceHubCategory` with `ParentId != null` / `Level > 1`.
  CommerceHub Attributes = `CommerceHubCategoryAttribute` rows attached to a CommerceHubCategory.
  ALL owned by CommerceHub, NOT by POS Category. POS Category has zero references to any CommerceHub
  category type. Changing a POS Category does NOT change CommerceHub taxonomy or listings.

# Business Rules (implementation-proven)

1. **Tenant isolation** — `TenantId` NOT NULL; every query scoped to `RequiredTenantId`; cross-tenant
   rows filtered/404. Global query filter `!IsDeleted && TenantId == CurrentTenantId`.
2. **Max depth = 2** — parent must be a root, enforced on create AND update.
3. **No moving a category that has subcategories** (second depth guard).
4. **No self-parent** on update.
5. **Duplicate prevention** — no two live categories with same `Name` under the same parent
   (case-sensitive equality as written).
6. **Delete guard: has products** → blocked (move/delete products first).
7. **Delete guard: has live subcategories** → blocked (delete children first).
8. **Soft delete only** — `IsDeleted = true`; FK `OnDelete(Restrict)` prevents DB cascade; no hard
   delete, no restore.
9. **Default seeding** — empty tenant auto-gets `General` + `Uncategorized` roots on first read.
10. **Product category required & non-empty** — `Product.CategoryId` `IsRequired`, `Guid.Empty` banned
    (coupon-bypass protection).
11. **Visibility/publishing** — NOT a stored flag; storefront visibility = live in SQL lineage
    (self + full ancestor chain non-deleted, same tenant, no cycle).
12. **Ordering** — alphabetical by `Name` only; no manual ordering.
13. **Role gates** — read (any authenticated tenant user); create/update (Owner/Admin/Manager);
    delete (Owner/Admin); storefront reads anonymous with `X-Tenant-Id`.

# Category State Machine

There is no status column beyond `IsDeleted`. The effective states are:

```
        create
          │
          ▼
   ┌─────────────┐   soft delete (guarded)   ┌───────────┐
   │    Live     │ ─────────────────────────>│  Deleted  │  (terminal — no restore)
   │ (in lineage)│                            └───────────┘
   └─────────────┘
     ▲   │
 edit│   │ parent soft-deleted / becomes orphan / cycle (data corruption)
     │   ▼
   ┌──────────────────────────┐
   │ Live in SQL but NOT live │  (hidden from storefront by CategoryLineageAuthority,
   │ in lineage (dead subtree)│   still returned by admin reads if row itself !IsDeleted)
   └──────────────────────────┘
```

- **Live**: `!IsDeleted`, same tenant, ancestor chain live → visible in admin + storefront.
- **Dead-in-lineage**: row itself `!IsDeleted` but an ancestor is deleted / cycle / orphan → hidden
  from storefront (admin list may still show the row; admin tree only walks live roots).
- **Deleted**: `IsDeleted = true` → excluded everywhere; terminal (no restore endpoint).

# Cross-System Effects

What CHANGES when a POS Category is created/edited/deleted:
- **Storefront tree / landing pages** — recomputed from SQL on next read (Redis cache 30-min TTL may
  briefly serve stale until expiry, but SQL recomposition is authority on refresh).
- **Admin lists / trees / product counts** — reflect immediately.
- **Elasticsearch `CategorySearchDocument`** — updated by the search-index pipeline (derivative).
- **Mongo `CategoryDocument`** — maintained by the promotion projection pipeline (derivative).

What does NOT change:
- **Products / Variants** — not modified (delete is blocked if products exist; rename does not touch
  products; `Product.CategoryId` values are untouched by category edits).
- **Pricing / Inventory / Warehouse / Stock** — unaffected; owned by variant/warehouse.
- **Orders / Checkout** — unaffected (order lines snapshot products, not categories).
- **Affiliate taxonomy** — unaffected (separate `AffiliateCategory` system).
- **CommerceHub taxonomy / listings** — unaffected (separate `CommerceHubCategory`;
  `CommerceHubListing.CommerceHubCategoryId` is independent of POS Category).
- **Analytics / Recommendations** — read `Product.CategoryId`; not driven by category mutation events
  (there are none).

# Validation Checklist

After **create**:
- Row in `Categories` with `TenantId`, `Name`, optional `Description`, `ParentCategoryId`; `IsDeleted=false`.
- If child: parent exists, same tenant, is a ROOT (parent.ParentCategoryId == null).
- No other live sibling with the same Name under the same parent.
- Appears in admin list/tree and (if live-in-lineage) storefront tree; slug = `GenerateSlug(Name)`.

After **edit/reparent**:
- Name/Description/ParentCategoryId overwritten; `UpdatedAt` set.
- New parent is a root; moved category has NO subcategories; not self-parented.
- Storefront slug changes if Name changed (slug is derived, not stored) — old slug URL now 404s.

After **delete**:
- `IsDeleted=true`; row gone from all live reads; NOT in storefront tree.
- Was blocked if any products referenced it OR any live subcategory existed.
- No restore possible.

Should NOT exist:
- Any POS Category with depth > 2.
- Any product with `CategoryId == Guid.Empty` or pointing to a deleted category (FK Restrict).
- Any POS Category integration event / outbox row (none are emitted).

# Common Failure Scenarios (cause + where to look)

1. **Category missing from storefront but present in admin** — likely dead-in-lineage: an ancestor is
   soft-deleted, or the parent link is broken/orphaned. Check `CategoryLineageAuthority` liveness and
   the parent chain in SQL. Also check tenant SQL lineage active (`TenantLineageAuthority`).
2. **Stale storefront tree** — Redis cache (30-min TTL). Confirm by comparing SQL recomposition vs
   cached JSON; wait for TTL or invalidate. SQL is authority.
3. **Cannot delete category** — has products (`400 has products`) or live subcategories
   (`400 has subcategories`). Move/delete dependents first.
4. **Cannot create/move (depth)** — trying to nest under a subcategory (`Maximum category depth is 2`)
   or moving a category that has children (`Cannot move category that has subcategories`).
5. **Duplicate name rejected** — a live sibling with the same Name under the same parent exists.
6. **Wrong storefront navigation** — orphan hidden (parent not in live set) or subtree dropped by a
   deleted ancestor. Check lineage.
7. **"Affiliate category mismatch"** — this is `AffiliateCategory` / `AffiliateProductPolicy.
   AffiliateCategoryId`, NOT POS Category. Do not investigate POS `Categories`.
8. **"CommerceHub category mismatch"** — this is `CommerceHubCategory` /
   `CommerceHubListing.CommerceHubCategoryId`, NOT POS Category.
9. **Search mismatch** — Elasticsearch `CategorySearchDocument` lag/reindex; derivative, reconcile
   against SQL.

Investigation order: SQL `Categories` (row + IsDeleted + parent chain) → `CategoryLineageAuthority`
liveness → tenant lineage active → storefront service SQL recomposition → Redis cache → ES/Mongo
projection → (only if the complaint is affiliate/CommerceHub) the corresponding SEPARATE taxonomy.

# Debugging Guide

- **Start at PostgreSQL `Categories`**: does the row exist for the tenant? `IsDeleted`? `ParentCategoryId`
  valid and pointing at a live root? This is authority.
- **Check `CategoryLineageAuthority`** when a category exists in SQL but is invisible on storefront —
  it computes recursive liveness (ancestor chain, cycles, orphans).
- **Check Product** when the complaint is delete-blocked, coupon scoping, product counts, or "product
  in wrong category" — the link lives on `Product.CategoryId`, not on Category.
- **Check Storefront service / Redis** when structure is right in SQL but wrong/stale on the storefront
  — recomposition vs 30-min cache.
- **Check Affiliate mapping** (`AffiliateProductPolicy.AffiliateCategoryId` + `AffiliateCategory`) ONLY
  for affiliate-taxonomy complaints — never POS `Categories`.
- **Check CommerceHub mapping** (`CommerceHubListing.CommerceHubCategoryId` + `CommerceHubCategory`)
  ONLY for marketplace-taxonomy complaints — never POS `Categories`.
- **Check Search** (`CategorySearchDocument` / `UnifiedSearchIndexService`) only for search-surface
  discrepancies; reconcile to SQL.

# Related Knowledge

- Core guides: `.ai-context/ARCHITECTURE_TRUTH.md`, `CROSS_SYSTEM_AUTHORITY_HARDENING.md` (SQL-authority,
  tenant isolation, projection recomposition).
- Related capabilities: Product Management (owns `Product.CategoryId`), Pricing Management (coupon
  category scoping), Storefront/Checkout (navigation & product visibility), Search.
- Related but SEPARATE domains: Affiliate (AffiliateCategory taxonomy), CommerceHub
  (CommerceHubCategory/SubCategory/Attributes), Expense Categories (`ExpenseCategory`, unrelated).

# Documentation Validation

Findings (implementation vs prose/expectations). Reported, not rewritten:

1. **Slug/Visibility/SEO/Images/Icons/DisplayOrder are NOT stored on POS Category.** Any doc or task
   framing that treats these as POS-Category-owned fields is wrong — slug is derived from Name at read
   time; visibility is derived from lineage; the others do not exist. (High — wrong ownership.)
2. **No Category Merge / Split / Restore.** These are not implemented; do not document them as
   capabilities. Soft delete is terminal. (High — missing/absent capability claim.)
3. **No POS Category integration events / outbox / MassTransit / background job.** Unlike most Shopixy
   write capabilities, POS Category mutation is a direct SQL write with no event. Mongo/ES documents are
   maintained by OTHER pipelines (promotion / search index) that read SQL. (Medium — hidden behavior /
   authority boundary.)
4. **Mongo `CategoryDocument` is NOT the storefront return authority** despite the service header prose
   ("MongoDB is the read-model source"). The live code (`BuildSqlCategoryTreeAsync`,
   `LoadSqlCategoryBySlugAsync`) recomposes from SQL and explicitly says "Redis/Mongo are not return
   authority here." The class docstring is stale. (Medium — doc-vs-code discrepancy.)
5. **Duplicate-name check is case-sensitive** (`c.Name == request.Name`). "Electronics" and
   "electronics" can coexist under the same parent, and they generate the SAME derived slug → slug
   collision at storefront (first match by name-order wins). (Medium — hidden behavior.)
6. **2-level depth is a hard product constraint**, not a soft convention (enforced on create, update,
   and move). Any assumption of deep hierarchies is incorrect. (High — wrong hierarchy.)
7. **Ownership vs mapping boundary is real and one-directional.** POS products MAP to AffiliateCategory
   and CommerceHubCategory via policy/listing FKs; POS Category owns neither and is never referenced by
   them. (Confirmed correct — highlighted to prevent conflation.)

# Summary of Discoveries

- **Category lifecycle discovered:** Create → Live(in-lineage) → Edit/Reparent → Soft-Delete(guarded,
  terminal). No Restore, no publish/hide state, no events, direct SQL writes.
- **Category tree discovered:** strict 2 levels (root → subcategory); depth enforced on create+update;
  move-with-children blocked; self-parent blocked; cycles/orphans dropped by read-side lineage authority.
- **Category authority discovered:** PostgreSQL `Categories` is the SOLE authority; slug/visibility are
  DERIVED; Mongo/Redis/Elasticsearch are derivative projections recomposed from SQL.
- **Storefront relationship discovered:** anonymous read-only tree + by-derived-slug landing; SQL
  recomposition is authority; Redis 30-min best-effort cache; orphan/dead-subtree hidden.
- **Product relationship discovered:** Product owns the link (`Product.CategoryId`, required, non-empty,
  FK Restrict); Category owns no product/pricing/inventory data; delete blocked if products exist.
- **Affiliate relationship discovered:** Affiliate has its OWN global taxonomy (`AffiliateCategory`,
  Super-Admin managed); product maps via `AffiliateProductPolicy.AffiliateCategoryId`; NOT owned by POS.
- **CommerceHub relationship discovered:** CommerceHub has its OWN global taxonomy
  (`CommerceHubCategory` + SubCategory via Level/ParentId + `CommerceHubCategoryAttribute`); product
  maps via `CommerceHubListing.CommerceHubCategoryId`; NOT owned by POS.
- **Business rules discovered:** tenant isolation, depth 2, no-move-with-children, no-self-parent,
  duplicate-name-per-level, delete guards (products/subcategories), soft-delete-only, default seeding,
  required non-empty product category, role gates.
- **Cross-system dependencies discovered:** feeds storefront nav, search index, coupon scoping, and
  reporting; NO effect on products/pricing/inventory/orders/affiliate/CommerceHub on mutation.
- **Documentation inconsistencies found:** stale "Mongo is read-model source" docstring; slug/SEO/
  images/icons/order not stored; no merge/split/restore; no category events; case-sensitive duplicate
  check → slug collision risk.
- **Hidden implementation behavior found:** derived-slug collision on case-variant names; default
  category auto-seeding on first read; read-side lineage liveness hides dead subtrees even when the row
  itself is not deleted; projections maintained by unrelated pipelines (not by the category controller).

# Attestation

- Created ONLY: `.ai/hermes/capabilities/pos-admin/category-management.md`.
- No other files were modified.
- No memory writes occurred.
- No code was generated, no features implemented, no refactors performed.
