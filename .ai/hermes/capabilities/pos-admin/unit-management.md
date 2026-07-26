# Unit Management (ERP Units of Measure) — POS Admin

> Reverse-engineered from implementation. Implementation is the source of truth; where
> documentation disagrees, implementation wins. This document covers ONLY ERP Units of
> Measure used by POS Admin. It does NOT cover Package Sizes, Product Variants,
> CommerceHub Attributes, Affiliate Product Options, Shipping Units, or Carrier Packaging.

## Owning implementation (authoritative file map)

- Entity: `PointOfSale.Core/Entities/Unit.cs` (the tenant-owned unit of measure).
- Link entity: `PointOfSale.Core/Entities/ProductUnit.cs` (product↔unit + conversion + per-unit pricing/barcode).
- Unit master CRUD: `PointOfSale.API/Controllers/UnitsController.cs` (route `api/units`) — the ENTIRE unit-master capability inline. There is NO `IUnitService`.
- Product-unit configuration: `PointOfSale.API/Controllers/ProductsController.cs` endpoints `POST/PUT/DELETE {id}/units`, `GET {id}/units`, `GET units-batch`. There is NO `IProductUnitService`.
- Conversion resolution at transaction time: inline in `SalesController` (`PrepareSaleItemsAsync`), `PurchasesController`, `SalesReturnsController`, `PurchaseReturnsController`.
- Conversion-lock validation: `PointOfSale.API/Services/ValidationService.CanModifyProductUnitConversionAsync` (interface `IValidationService`).
- DTOs: `PointOfSale.API/DTOs/UnitDTOs.cs` (`UnitDto`, `CreateUpdateUnitDto`, `ProductUnitDto`, `CreateUpdateProductUnitDto`, `UnitSelectionDto`).
- Persistence config: `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` (`Units`, `ProductUnits` DbSets, query filters, ProductUnit unique indexes, invoice-item check constraints).
- Projection read: `ProductCreatedEventConsumer` (Mongo `DefaultUnit`/`Units`), `Elasticsearch/Services/ProductSearchSqlAuthorityOverlay` (search `DefaultUnit`), `Services/Storefront/StorefrontProductService` (storefront `DefaultUnit`).
- Frontend: `PointOfSale.Web/src/pages/Units.jsx` (unit master), `components/ProductUnitsModal.jsx`, `services/productUnitsApi.js`.

# Purpose

Why Units exist (implementation-backed):

A `Unit` is a tenant-scoped **unit of measure** (Piece, Carton, Box, Kg, Gram, Liter,
Milliliter, Dozen — the seed set). It solves one concrete ERP problem: a product may be
**bought, sold, moved, and returned in different-sized packages**, but inventory must be
tracked in ONE canonical quantity. Units + per-product conversion factors let a POS operator
transact in "2 Cartons" while the system stores stock in **base units** (e.g. 10 pieces).

What Units own:
- The definition of a measure name/symbol/description and its active/deleted status (the `Unit`
  master, per tenant).
- Through `ProductUnit`, the **conversion factor** (base units per this unit) and OPTIONAL
  per-unit `PurchasePrice`, `SellingPrice`, and `Barcode` for a given product.
- The identification of which product-unit is the **base unit** (`IsBaseUnit`, factor = 1) and
  which is the **default unit** (`IsDefault`) for transactions/display.

What Units do NOT own:
- Actual stock quantities (owned by `ProductVariantStocks`, keyed by Variant + Warehouse).
- Money/accounting (owned by Sales/Purchase/Return posting + `LedgerService` + GL).
- Reservations, movements, or lifecycle state of any transaction.
- Product identity/pricing authority beyond the optional per-unit price hints.
- Any cross-system taxonomy (CommerceHub attributes, affiliate options, shipping/carrier
  packaging) — those are separate systems and are explicitly out of scope.

# Business Scope

Reverse-engineered field-by-field. Two distinct tables.

## `Unit` (the master) — `Unit.cs`
- **TenantId** — NOT NULL, DB-enforced; global query filter `!IsDeleted && TenantId == CurrentTenantId`.
- **Name** — required string. Case-insensitive uniqueness enforced in controller (see Business Rules).
- **Symbol** — nullable short symbol (e.g. `pc`, `ctn`, `kg`).
- **Description** — nullable.
- **IsActive** — bool, default `true`. Activation flag (see below).
- **IsDeleted** (from `BaseEntity`) — soft-delete flag.
- Navigation: `ProductUnits` (products that reference this unit).

The `Unit` master itself has **NO Code field**, **NO decimal-precision field**, **NO
fractional-unit flag**, **NO base/derived flag**, and **NO conversion factor**. Those concepts
either do not exist or live on `ProductUnit`. Do NOT document a "Unit Code" or "Unit decimal
precision" as owned by the Unit master — grep proves they are absent.

## `ProductUnit` (product↔unit link) — `ProductUnit.cs`
- **ProductId**, **UnitId** — the pairing (unique per product; see indexes).
- **ConversionFactor** (`decimal`) — "number of base units per this unit". Stored SQL column type
  `numeric` (unbounded precision/scale — NO fixed decimal precision is configured). Example doc in
  code: base = Piece, this = Carton of 5 → factor = 5.
- **PurchasePrice** (`decimal?`), **SellingPrice** (`decimal?`) — optional per-unit price hints.
- **Barcode** (`string?`) — optional per-unit barcode (a carton can scan differently than a piece).
- **IsDefault** (`bool`) — the default unit for display/transactions; at most one per product.
- **IsBaseUnit** (`bool`) — the base unit; exactly one per product; factor must be 1.
- **IsDeleted** — soft-delete.

### Concepts asked about, mapped to reality
- **Base Unit** = `ProductUnit.IsBaseUnit == true` (factor pinned to 1). It is a per-PRODUCT concept,
  NOT a per-Unit-master concept. There is no global base unit.
- **Derived Unit** = any `ProductUnit` with `IsBaseUnit == false` and factor ≠ 1.
- **Conversion Factor** = `ProductUnit.ConversionFactor` (base units per this unit).
- **Decimal Precision** = NOT a modeled concept. Column is `numeric` (unbounded). Currency values in
  transactions round 2 dp AwayFromZero, but the unit/factor itself has no precision policy.
- **Fractional Units** = supported only implicitly: `ConversionFactor` and transaction `Quantity` are
  `decimal`, so fractional factors and fractional quantities are storable. There is NO "allow
  fractions" flag and NO integer enforcement. Do NOT document a fractional-unit toggle.
- **Status / Activation** = `Unit.IsActive`. Inactive units are hidden from the default list and are
  rejected when attaching to a product; an existing product-unit whose master went inactive is rejected
  on product-unit update.
- **Deletion** = soft delete (`IsDeleted = true`) on both `Unit` and `ProductUnit`, guarded.
- **Default Unit** = `ProductUnit.IsDefault`; the transaction-time fallback when no unit is specified.
- **Display** = the `UnitSelectionDto.ConversionText` (e.g. "1 Carton = 5 Pieces") is a presentation
  helper; the projections carry `DefaultUnit` for storefront/search display.
- **Validation** = conversion factor > 0; base factor == 1; name uniqueness; barcode ownership; a
  transaction-usage lock (see Business Rules).

# Authority Boundaries

| Concern | Authority (writes truth) | Evidence / Projection / Presentation |
|---|---|---|
| Unit Definition (name/symbol/status) | `UnitsController` → `Units` table (PostgreSQL) | none |
| Conversion Rules (factor) | `ProductsController` `{id}/units` → `ProductUnits` table | Mongo/ES/Storefront `DefaultUnit.ConversionFactor` are PROJECTIONS |
| Base Unit designation | `ProductUnits.IsBaseUnit` (unique partial index) | — |
| Default Unit designation | `ProductUnits.IsDefault` (unique partial index) | — |
| Product Quantity semantics | Transaction controllers convert entered Qty → BaseQuantity | invoice item `ConversionFactor`/`BaseQuantity` snapshots |
| Inventory Quantity | `ProductVariantStocks` (Variant + Warehouse) — NOT Unit | Unit only supplies the factor used to compute the delta |
| Purchase Quantity | `PurchaseInvoiceItem` (`Quantity`, `ConversionFactor`, `BaseQuantity`) | — |
| Sales Quantity | `SalesInvoiceItem` (`Quantity`, `ConversionFactor`, `BaseQuantity`) | — |
| Return Quantity | `ReturnItem` (`Quantity`, `ConversionFactor`, `BaseQuantity`) | — |
| Warehouse Quantity | `ProductVariantStocks` per warehouse (base units) | — |
| Reservations | Inventory/checkout reservation system (base units) | Unit not involved |
| Stock Movements | `StockMovements` (append-only, base units, optional `UnitId` tag) | — |
| Accounting | Sales/Purchase posting + `LedgerService` + GL | Unit not involved |
| Presentation | `UnitSelectionDto.ConversionText`, projection `DefaultUnit` | — |

Key authority facts:
- **Unit is master data, not transactional authority.** It never holds stock, money, reservations,
  or movement state.
- **The conversion factor is captured onto each transaction line as an immutable snapshot**
  (`ConversionFactor` + `BaseQuantity` on `SalesInvoiceItem`/`PurchaseInvoiceItem`/`ReturnItem`). Once
  a line is written, changing the `ProductUnit.ConversionFactor` later does NOT retroactively alter
  historical lines — and, more strongly, the factor becomes **locked** once used (see Business Rules).
- **Base units are the canonical stock quantity.** Every stock mutation is expressed as
  `BaseQuantity = Quantity × ConversionFactor`.

# Unit Lifecycle

Two separate lifecycles: the **Unit master** and the **ProductUnit link**.

## Unit master lifecycle (`UnitsController`, route `api/units`)
1. **Create** (`POST api/units`, roles Owner/Admin/Manager) — case-insensitive name uniqueness
   check across the tenant; sets `TenantId`, `Name`, `Symbol`, `Description`, `IsActive`. No factor,
   no base flag (those belong to product-units).
2. **Edit** (`PUT api/units/{id}`, Owner/Admin/Manager) — tenant-scoped load; re-checks name
   uniqueness excluding self; updates name/symbol/description/`IsActive`.
3. **Activation** — there is NO dedicated activate/deactivate endpoint. Activation is just the
   `IsActive` boolean on create/update. `GET api/units?includeInactive=false` (default) hides
   inactive units.
4. **Usage** — a unit becomes usable by products via `POST products/{id}/units` (which requires the
   master to be active and tenant-owned).
5. **Delete** (`DELETE api/units/{id}`, roles Owner/Admin) — **soft delete** (`IsDeleted = true`),
   guarded: blocked (400) if ANY `ProductUnit` references it for a same-tenant product.
6. **Restore** — there is **NO restore/undelete endpoint** for the Unit master. Soft-delete is
   terminal from the API surface.
7. **Seeding** — `POST api/units/seed-defaults` (Owner/Admin/Manager) inserts the 8 default measures
   (Piece, Carton, Box, Kilogram, Gram, Liter, Milliliter, Dozen) that don't already exist by name.

## ProductUnit lifecycle (`ProductsController`, route `api/products/{id}/units`)
1. **Create/Attach** (`POST {id}/units`) — validates: master is active + tenant-owned; barcode not
   owned by another semantic entity; pair not already configured; `ConversionFactor > 0`; if
   `IsBaseUnit`, no existing base unit AND factor must equal 1; if `IsDefault`, unsets other defaults
   in the same transaction. Publishes a product-units-changed projection sync.
2. **Edit** (`PUT {id}/units/{unitId}`) — rejects if the master is now inactive; barcode ownership
   re-checked; `ConversionFactor > 0`; if the row is a base unit, factor must stay 1; if switching to
   default, unsets other defaults. NOTE: this endpoint does NOT itself call
   `CanModifyProductUnitConversionAsync` (see Documentation Validation / hidden behavior).
3. **Delete** (`DELETE {id}/units/{unitId}`) — **soft delete**, guarded: cannot delete the base unit;
   cannot delete if the unit has been used in `PurchaseInvoiceItems`, `SalesInvoiceItems`, or
   `ReturnItems` for that product.
4. **Restore** — NONE.
5. **Migration** (`POST products/migrate-to-multi-unit`, Owner/Admin) — backfills a base
   "Piece" `ProductUnit` (factor 1, default+base) for products lacking any unit configuration.

## States (implementation-proven)
Unit master: `Active (IsActive=true, IsDeleted=false)` ⇄ `Inactive (IsActive=false)` →
`Deleted (IsDeleted=true)` (terminal, guarded, no restore).
ProductUnit: `Configured` → `Deleted (IsDeleted=true)` (terminal, guarded). `IsBaseUnit` and
`IsDefault` are role flags within the Configured state, not separate lifecycle states.

There is NO "Created→Approved→Published" workflow, NO status enum with intermediate states, and NO
event-driven state machine on the Unit itself.

# Unit Conversion

This is the core capability. Implementation:

- **Base Unit**: the single `ProductUnit` per product with `IsBaseUnit == true`. Its
  `ConversionFactor` is enforced to be exactly `1` at create and update. Enforced further by a
  unique partial index `UX_ProductUnits_ProductId_Base_Active` (one base per product).
- **Derived Unit**: any other `ProductUnit`, factor ≠ 1 (e.g. Carton = 5).
- **Conversion Factor semantics**: `ConversionFactor` = **base units per one of this unit**.
- **Multiplication (entry → storage)**: at transaction time the entered `Quantity` (in the chosen
  unit) is converted to canonical base units: `BaseQuantity = Quantity × ConversionFactor`. This
  `BaseQuantity` is what mutates `ProductVariantStocks.Quantity` and is written to `StockMovements`.
  Verified in `SalesController` (`variantStock.Quantity += entry.Item.BaseQuantity` on return-to-stock,
  decrements on sale) and `PurchasesController` (`variantStock.Quantity -= BaseQuantity` on reversal,
  increments on receipt).
- **Division (storage → price/unit)**: in `PurchasesController` cost re-weighting, per-base-unit value
  is computed as `UnitPrice / ConversionFactor` (e.g. `(UnitPrice / ConversionFactor) * BaseQuantity`).
  So the entered `UnitPrice` is a **price per chosen unit**, and dividing by the factor yields the
  per-base-unit cost.
- **Precision / Rounding**: the factor column is `numeric` (unbounded); no rounding is applied to the
  factor itself. Monetary results round to 2 dp AwayFromZero via the currency helper. There is NO
  configurable decimal-precision-per-unit.
- **Validation**: `ConversionFactor > 0` at create AND update (400 "Conversion factor must be greater
  than 0"); base factor forced to 1 (400 otherwise). At transaction prep, a factor `<= 0` on a
  configured product-unit is rejected ("Unit conversion is invalid"). A DB check constraint
  `CK_PurchaseInvoiceItems_ConversionFactor_Positive` / `..._BaseQuantity_Positive` (and the same for
  `ReturnItems`) enforce `> 0` at the storage layer for those lines.
- **Reverse Conversion**: NOT implemented as a distinct operation. There is no
  base-units→display-units back-conversion helper in the transaction path; display uses the stored
  `Quantity`/`ConversionFactor` snapshot and the `ConversionText` string. The system stores base
  quantities and shows the original entered quantity from the immutable line snapshot.
- **Storage Unit vs Display Unit**: storage is ALWAYS base units (`ProductVariantStocks`,
  `StockMovements`, `BaseQuantity`). Display/entry is the chosen `ProductUnit` (or the default). The
  invoice line permanently records BOTH the entered `Quantity` and the `ConversionFactor` +
  `BaseQuantity` so history is reconstructable regardless of later factor changes.

Conversion resolution at transaction time (Sales/Purchase):
1. If a `UnitId` is supplied, load the `ProductUnit` for (product, unit); 400 if not configured;
   400 if factor ≤ 0; use its factor.
2. If no `UnitId`, load the product-unit where `IsDefault`; in Sales a missing default is a 400
   ("Default unit is not configured"); in Purchases a missing default silently falls back to factor 1.
3. Compute `BaseQuantity = Quantity × factor`; persist `ConversionFactor` + `BaseQuantity` on the line.

# Product Relationship

Ownership chain (implementation):

- **Unit → Product**: a `Unit` (master) is linked to a `Product` ONLY through `ProductUnit`. A product
  has 0..N product-units; when configured, exactly one is base (factor 1) and at most one is default.
- **Product → Variant**: stock/price actually live on the `ProductVariant` (SKU), not the product.
  Units are attached at the **product** level (`ProductUnit.ProductId`), NOT per variant. All variants
  of a product share the same unit configuration and conversion factors.
- **Variant → Inventory → Warehouse**: `ProductVariantStocks` (key TenantId, VariantId, WarehouseId)
  holds the canonical stock in **base units**. Units contribute only the factor used to compute the
  base-unit delta; they never key or own a stock row.
- **Purchase / Sales / Returns**: each transaction line references `ProductId` + optional `UnitId`,
  snapshots `ConversionFactor`+`BaseQuantity`, and mutates variant/warehouse stock in base units.

Who owns what:
- Unit configuration + conversion factor: **Product-level** (`ProductUnit`).
- SKU identity, price, stock: **Variant + Warehouse**.
- Quantity conversion to base units: **transaction controllers** at write time.
- Historical unit/factor of a line: **the invoice/return item snapshot** (immutable).

# Purchase Relationship

Purchase Unit → Inventory Storage → Conversion → Stock Movement (implementation):

- The purchase line carries a chosen `UnitId` (or falls back to the default product-unit, else factor 1).
- `BaseQuantity = Quantity × ConversionFactor`; `variantStock.Quantity += BaseQuantity` (receipt) on
  the resolved variant + warehouse, in **base units**.
- A `StockMovement` (`QuantityChange = ±BaseQuantity`, optional `UnitId` tag) is appended.
- Cost: per-base-unit cost = `UnitPrice / ConversionFactor`; product-level weighted-average
  `PurchasePrice` is recomputed across all variants/warehouses (see Purchase Management capability).
- The purchase does NOT change the `ProductUnit` definition; it only reads the factor and snapshots it.

# Sales Relationship

Sales Unit → Inventory → Stock → Reservation → Movement (implementation):

- Sale line resolves `UnitId` (explicit) or the product's `IsDefault` product-unit (400 if none).
- `authoritativeUnitPrice` is resolved server-side from the variant price × factor (or the
  product-unit `SellingPrice`); the client `UnitPrice` is overwritten — units feed the price basis but
  do NOT own price authority.
- `BaseQuantity = Quantity × ConversionFactor` decrements `ProductVariantStocks.Quantity` (base units);
  a `StockMovement` is appended.
- Reservations (checkout path) operate purely in base units; the POS sale path posts stock directly.
  Units are not part of the reservation key.

# Warehouse Relationship

Warehouse → Inventory → Unit → Stock → Movement (implementation):

- Stock is stored **per warehouse** in `ProductVariantStocks` (key includes `WarehouseId`), always in
  base units. Units are NOT warehouse-scoped and do NOT vary by warehouse.
- Every warehouse-affecting transaction (purchase, sale, return, transfer) converts the entered
  quantity to base units via the product-unit factor before touching the warehouse stock row.
- `StockMovements` records the base-unit change per warehouse with an optional `UnitId` tag for audit.
- Warehouse quantity math never uses the display unit; the unit is a presentation/entry convenience
  resolved to base units at the boundary.

# Implementation Discovery

- **Frontend**: `pages/Units.jsx` (unit master CRUD), `components/ProductUnitsModal.jsx`
  (per-product unit config), `services/productUnitsApi.js`. Help article `admin/help/helpArticles/units.md`.
- **Controllers / Endpoints**:
  - `UnitsController` (`api/units`): `GET` (list, `includeInactive`), `GET {id}`, `POST`, `PUT {id}`,
    `DELETE {id}`, `POST seed-defaults`.
  - `ProductsController`: `GET {id}/units`, `GET products/units-batch`, `POST {id}/units`,
    `PUT {id}/units/{unitId}`, `DELETE {id}/units/{unitId}`, `POST migrate-to-multi-unit`.
- **Services**: NO dedicated unit service. `ValidationService.CanModifyProductUnitConversionAsync`
  is the only unit-specific service method (and it is defined but not wired into the update path — see
  Documentation Validation).
- **Repositories**: none; controllers use `ApplicationDbContext` directly.
- **Entities**: `Unit`, `ProductUnit`; unit references on `OrderItem.UnitId/Unit`,
  `SalesInvoiceItem`, `PurchaseInvoiceItem`, `ReturnItem`, `StockMovement.UnitId/Unit` (all nullable,
  "backward compatibility").
- **Database (PostgreSQL, authoritative)**:
  - `Units` — tenant-scoped, global query filter `!IsDeleted && TenantId == CurrentTenantId`. No
    unique index on Name is configured in EF (uniqueness is controller-enforced only).
  - `ProductUnits` — query filter also joins Product+Unit tenant. Unique partial indexes:
    `UX_ProductUnits_ProductId_UnitId_Active`, `UX_ProductUnits_ProductId_Base_Active` (one base),
    `UX_ProductUnits_ProductId_Default_Active` (one default), `UX_ProductUnits_Barcode_Active`.
    `ConversionFactor` column type `numeric` (unbounded).
  - Invoice/return item check constraints: `CK_*_ConversionFactor_Positive`, `CK_*_BaseQuantity_Positive`.
- **Mongo (read model)**: product document carries `DefaultUnit` + `Units` (populated by
  `ProductCreatedEventConsumer`). Derivative, SQL-recomposed. NOT authority.
- **Redis**: no unit-specific cache; participates only via product cache.
- **Elasticsearch**: `ProductSearchSqlAuthorityOverlay` writes `DefaultUnit` onto the search
  document from live SQL. Derivative.
- **MassTransit / Outbox**: unit CHANGES are propagated via the PRODUCT projection path.
  `ProductsController.PublishProductUnitsChangedAsync` enqueues a marketplace outbox
  `ProductUpdatedEvent` and publishes a `ProductUpdatedEvent` (fields `DefaultUnit`, `Units`),
  version-guarded by `ProductEventVersionHelper`. There is NO `UnitCreated/Updated/Deleted` integration
  event for the Unit master — master edits do not emit their own event.
- **Analytics / Background jobs**: none specific to Units.

# Business Rules (implementation-proven)

- **Duplicate prevention (master)**: case-insensitive Name uniqueness across the tenant on create and
  update (controller check; NOT a DB unique index).
- **Duplicate prevention (product-unit)**: a (ProductId, UnitId) pair cannot be configured twice
  (controller check + `UX_ProductUnits_ProductId_UnitId_Active`).
- **Delete restrictions (master)**: cannot delete a Unit referenced by any `ProductUnit` of a
  same-tenant product.
- **Delete restrictions (product-unit)**: cannot delete the base unit; cannot delete a product-unit
  used in any purchase/sales/return line for that product.
- **Base unit rules**: exactly one base unit per product; base `ConversionFactor` must equal 1
  (enforced on create AND update); base cannot be deleted; enforced by unique partial index.
- **Default unit rules**: at most one default per product; setting a new default unsets others in the
  same transaction; enforced by unique partial index.
- **Conversion validation**: `ConversionFactor > 0` at create and update; ≤ 0 rejected at
  transaction prep; DB check constraints enforce `> 0` on stored transaction lines.
- **Zero-factor / negative-factor**: both blocked by `> 0` validation and DB check constraints.
- **Circular conversion prevention**: N/A by design — conversion is a single flat factor to base
  units, not a graph of unit→unit conversions, so cycles are structurally impossible. Do NOT document
  a circular-conversion guard; none exists because none is needed.
- **Precision rules**: none configured (numeric/unbounded); monetary rounding 2 dp AwayFromZero.
- **Fraction rules**: decimals allowed; no integer/fraction enforcement.
- **Barcode ownership**: a product-unit barcode cannot collide with another live product, variant, or
  product-unit (`ProductVariantCloneImportAuthority.IsProductUnitBarcodeOwnedByAnotherSemanticEntityAsync`).
- **Inactive-master guard**: an inactive Unit master cannot be attached to a product, and updating a
  product-unit whose master is inactive is rejected.
- **Conversion-factor lock (defined)**: `CanModifyProductUnitConversionAsync` returns
  `UNIT_USED_IN_TRANSACTIONS` when the (product, unit) appears in purchase/sales invoice items or stock
  movements — intended to lock the factor once used. (See Documentation Validation: it is not invoked
  by the product-unit update endpoint.)

# State Machine

Unit master:
```
Active (IsActive=true, IsDeleted=false)
  ⇄ Inactive (IsActive=false)        [via PUT, reversible]
  → Deleted (IsDeleted=true)         [via DELETE, guarded, TERMINAL — no restore]
```
ProductUnit:
```
Configured  (roles within: IsBaseUnit? / IsDefault?)
  → Deleted (IsDeleted=true)         [via DELETE, guarded, TERMINAL — no restore]
```
No intermediate/approval/publish states exist. Enum-vs-reachability check: there is no unit status
enum at all — status is just the `IsActive` + `IsDeleted` booleans.

# Cross-System Effects

What CHANGES when a unit configuration changes:
- **Products**: product-unit add/edit/delete bumps `Product.UpdatedAt` and triggers a
  product-units projection sync (`ProductUpdatedEvent` + marketplace outbox), version-guarded.
- **Inventory / Warehouse**: unchanged directly — stock stays in base units. A new/edited factor only
  affects FUTURE conversions; existing stock rows are untouched.
- **Purchases / Sales / Returns**: future lines use the current factor; existing lines keep their
  snapshotted `ConversionFactor`/`BaseQuantity`.
- **Storefront / Search (Mongo/ES)**: `DefaultUnit`/`Units` on the product projection are refreshed
  from SQL on the product update event.

What does NOT change:
- **Historical transaction lines** (immutable snapshots) — never recomputed by a later factor change.
- **Accounting / Ledger / GL** — Units never post journals; no accounting effect.
- **Reservations / StockMovements history** — append-only, base-unit; not rewritten.
- **CommerceHub attributes, Affiliate options, Pricing rules, Checkout totals** — no Unit dependency
  beyond the display `DefaultUnit` carried on the product projection.
- **Orders / OrderItem** — `OrderItem.UnitId` is a nullable snapshot; changing a product-unit does
  not alter existing orders.

# Validation Checklist (operational)

After **creating a Unit master**: a `Units` row exists with the tenant, `IsActive=true`,
`IsDeleted=false`; the name is unique (case-insensitive) for the tenant.
After **deactivating a Unit**: it disappears from the default list; it cannot be newly attached to a
product; existing product-units referencing it fail on update.
After **deleting a Unit master**: `IsDeleted=true`; blocked if any product-unit references it.
After **attaching a product-unit**: exactly one base unit (factor 1) exists; at most one default;
(ProductId,UnitId) unique; factor > 0; barcode not colliding; product projection refreshed.
After **editing a factor**: base stays factor 1; historical lines unchanged; (intended) blocked if
already used in transactions — verify via `CanModifyProductUnitConversionAsync` if wired.
After **a purchase/sale/return line**: line has `ConversionFactor` + `BaseQuantity` (both > 0);
`ProductVariantStocks.Quantity` moved by exactly `BaseQuantity`; a `StockMovement` recorded.
After **deleting a product-unit**: not the base unit; not used in any transaction line.

# Common Failure Scenarios (cause + investigation order)

- **"A unit with the name X already exists"** — case-insensitive master name collision. Check `Units`
  for the tenant.
- **"Cannot delete this unit because it is being used by one or more products"** — master delete
  blocked by a referencing `ProductUnit`. Investigate `ProductUnits` → detach first.
- **"Unit is not configured for product" / "not bound to product"** — transaction referenced a
  `UnitId` with no `ProductUnit` row. Check product-unit config → transaction path.
- **"Default unit is not configured for product"** (Sales) — no `IsDefault` product-unit. Check
  product-unit config. (Purchases silently fall back to factor 1 — a divergence, investigate the
  Purchase path if quantities look wrong.)
- **"Base unit must have a conversion factor of 1" / "Product already has a base unit"** — base-unit
  rule violation. Check `IsBaseUnit` rows + factor.
- **"Conversion factor must be greater than 0" / "Unit conversion is invalid"** — zero/negative
  factor. Check `ProductUnit.ConversionFactor`.
- **Inventory mismatch** — stock off by a factor multiple → suspect a wrong `ConversionFactor` at line
  entry OR a factor changed after some lines were written. Compare line snapshots vs current factor,
  then base-unit stock math.
- **Purchase/Sales mismatch** — entered quantity vs base-unit delta: verify
  `BaseQuantity = Quantity × ConversionFactor` on the line vs the `StockMovement`.
- **Warehouse mismatch** — stock in wrong warehouse: this is a warehouse/variant resolution issue, not
  a unit issue (units are not warehouse-scoped); check variant+warehouse resolution.
- **Duplicate/fraction issues** — fractions are allowed by design; a "fraction mismatch" is almost
  always a wrong factor, not a fraction toggle.

# Debugging Guide

If Hermes receives a Unit bug, investigate in this order:
1. **Start at the Unit entity / ProductUnit config** when the symptom is create/edit/delete rejection,
   duplicate, base/default, or factor validation. Confirm `Units` and `ProductUnits` rows + indexes.
2. **Check Product** when unit changes seem not to reflect on storefront/search — verify the
   `ProductUpdatedEvent` projection sync fired (`PublishProductUnitsChangedAsync`) and the Mongo/ES
   `DefaultUnit`/`Units` recomposed from SQL.
3. **Check Conversion** (`ProductUnit.ConversionFactor` vs line `ConversionFactor`/`BaseQuantity`)
   whenever quantities/stock are off by a multiple — compare the live factor to the line snapshot.
4. **Check Inventory** (`ProductVariantStocks`, base units) when stock totals are wrong AFTER
   confirming the factor and `BaseQuantity` are correct.
5. **Check Purchase** when receipt quantities/cost look wrong — verify base-unit increment and
   `UnitPrice / ConversionFactor` cost math; note the Purchase default-unit fallback-to-1 behavior.
6. **Check Sales** when sold quantity/price basis looks wrong — verify server-side price resolution
   and base-unit decrement.
7. **Check Warehouse** only for stock-location issues (variant+warehouse resolution), NOT unit math —
   units are not warehouse-scoped.
8. **Check the transaction-usage lock** (`CanModifyProductUnitConversionAsync`) when a factor was
   edited despite prior usage — confirm whether the guard is actually enforced on the path taken.

# Related Knowledge

- Core Guides: `.ai-context/ARCHITECTURE_TRUTH.md`, `PROJECT_CONTEXT.md`, `CODEX_RULES.md`.
- System Guides: SQL-authority + projection overlay pattern; MassTransit outbox atomicity;
  monotonic projection version guard.
- Related Capabilities: Product Management (owns `ProductUnit` endpoints + projection),
  Inventory Management (base-unit stock authority), Purchase / Sales / Purchase-Return /
  Sales-Return Management (consume the factor + snapshot lines), Warehouse Management (per-warehouse
  base-unit stock).
- Related Domains: Pricing (per-unit `SellingPrice`/`PurchasePrice` hints), Storefront/Search
  projections (`DefaultUnit` display).

# Documentation Validation

Comparing implementation to the concepts requested / existing docs — reporting ONLY findings:

- **Missing documentation**: The Unit master has NO `Code`, NO decimal-precision, NO fractional-unit
  flag, NO status enum — any doc implying these should be corrected. Only `Name/Symbol/Description/
  IsActive/IsDeleted` exist.
- **Wrong authority assumption**: Units are frequently assumed to own quantity/conversion authority
  end-to-end. Reality: the factor lives on `ProductUnit` (product-level), stock authority is
  `ProductVariantStocks` (variant+warehouse), and lines carry immutable factor snapshots.
- **Wrong conversion assumptions**: there is NO unit→unit conversion graph and therefore NO
  circular-conversion guard; conversion is a single flat factor to base units. There is NO
  reverse-conversion operation.
- **Hidden implementation behavior**:
  1. `ProductUnit.UpdateProductUnit` does NOT call `ValidationService.CanModifyProductUnitConversionAsync`
     — the transaction-usage LOCK on the conversion factor is DEFINED but NOT WIRED into the update
     endpoint. A factor can be changed after the unit was used in transactions (only base-factor=1 and
     `> 0` are checked). This is a real integrity gap: historical lines keep their snapshot, but future
     conversions silently use the new factor. **Report this.**
  2. Purchase transaction prep SILENTLY falls back to `ConversionFactor = 1` when no default
     product-unit exists, whereas Sales returns a 400. Divergent behavior for the same missing-default
     condition.
  3. `Unit` master edits/deletes do NOT emit a dedicated integration event; projections update ONLY
     through the PRODUCT update path (`PublishProductUnitsChangedAsync`). A renamed unit not reflected
     downstream until a product-unit change re-syncs is expected, not a bug.
  4. Unit master Name uniqueness is CONTROLLER-enforced only — there is NO DB unique index on
     `Units(TenantId, Name)`. A concurrent create could race a duplicate. `ProductUnit` base/default/
     pair/barcode uniqueness ARE backed by DB unique partial indexes.
  5. `CreateUnit`'s duplicate check does NOT scope by tenant in the query (`u.Name.ToLower() ==
     dto.Name.ToLower()` without an explicit `TenantId` filter) — it relies on the global query filter
     for tenant scoping. Correct in effect, but the intent is implicit.
- **Missing business rules**: the base-unit-immutability, the "cannot delete used unit", and the
  inactive-master attach guard are often omitted from prose docs.
- **Missing validation**: DB check constraints (`CK_*_ConversionFactor_Positive`,
  `CK_*_BaseQuantity_Positive`) and the four ProductUnit unique partial indexes.
- **Missing cross-system effects**: the product-projection sync (Mongo/ES/Storefront `DefaultUnit`)
  triggered by product-unit changes.

---

## Summary of discoveries

- **Unit lifecycle discovered**: Two lifecycles. Unit master: Active ⇄ Inactive → soft-Deleted
  (guarded, no restore), created via CRUD or `seed-defaults`. ProductUnit: Configured → soft-Deleted
  (guarded, no restore); base/default are role flags, not states. No approval/publish workflow, no
  status enum.
- **Unit authority discovered**: Unit master owns name/symbol/status only. `ProductUnit` (product-level)
  owns the conversion factor + optional per-unit price/barcode + base/default designation. Stock
  authority is `ProductVariantStocks` (variant+warehouse, base units); money is Sales/Purchase posting.
  Transaction lines snapshot the factor immutably.
- **Conversion model discovered**: single flat `ConversionFactor` = base units per chosen unit;
  `BaseQuantity = Quantity × ConversionFactor` for storage; `UnitPrice / ConversionFactor` for
  per-base-unit cost. Base factor pinned to 1. No unit→unit graph, no cycles, no reverse-conversion op,
  no configurable precision, fractions allowed implicitly.
- **Product relationship discovered**: units attach at PRODUCT level (shared by all variants); stock/
  price live on variant+warehouse; lines snapshot factor+base quantity.
- **Purchase relationship discovered**: line factor → base-unit stock increment + StockMovement;
  weighted-average cost via `UnitPrice/ConversionFactor`; silent fallback to factor 1 when no default.
- **Sales relationship discovered**: line factor → base-unit stock decrement; server-authoritative
  price basis (variant price × factor or product-unit SellingPrice); 400 when no default unit.
- **Warehouse relationship discovered**: stock stored per warehouse in base units; units are NOT
  warehouse-scoped; entered qty converted to base units before touching warehouse stock.
- **Business rules discovered**: name uniqueness (controller), pair/base/default/barcode uniqueness
  (DB indexes), base factor=1, factor>0 (+DB checks), delete guards (master: referenced; product-unit:
  base or used-in-transactions), inactive-master attach guard, barcode ownership, defined-but-unwired
  conversion lock.
- **Cross-system dependencies discovered**: product-projection sync to Mongo/ES/Storefront
  `DefaultUnit`/`Units` via `ProductUpdatedEvent` + marketplace outbox (version-guarded); no accounting,
  reservation, CommerceHub, or affiliate dependency on units.
- **Documentation inconsistencies found**: no Unit Code/precision/fraction-flag/status-enum despite
  common assumptions; Units don't own quantity/stock authority; no circular-conversion guard needed;
  master name uniqueness lacks a DB index; Sales vs Purchase divergent missing-default handling.
- **Hidden implementation behavior found**: (1) conversion-factor transaction-usage LOCK
  (`CanModifyProductUnitConversionAsync`) is DEFINED but NOT invoked by the update endpoint — integrity
  gap; (2) Purchase silent factor=1 fallback vs Sales 400; (3) Unit master emits no dedicated event
  (piggybacks product projection); (4) master Name uniqueness controller-only (no DB unique index);
  (5) create duplicate check relies on the global tenant query filter rather than an explicit filter.

## Attestation
- Created ONLY this file: `.ai/hermes/capabilities/pos-admin/unit-management.md`.
- No other files were modified.
- No memory writes occurred.
