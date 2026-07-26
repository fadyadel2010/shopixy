# Phase 11.1 — Product Variants Domain Model

## ✅ Implementation Complete

This document summarizes the Phase 11.1 implementation of Shopify-grade Product Variants.

---

## 🎯 Mission Accomplished

- ✅ Product Variants introduced in a **non-breaking, backward-compatible way**
- ✅ Existing Products table, POS screens, Cart, Checkout, Storefront, and inventory **continue working exactly as before**
- ✅ Every product now backed by at least one **Default Variant** (`IsDefault = true`)
- ✅ System ready for full Shopify-grade variants later (Phase 11.2+)

---

## 📦 Architecture

```
Product (catalog entity)
   └── ProductVariant (sellable SKU)
           ├─ Price (POS price)
           ├─ OnlinePrice (Storefront price)
           ├─ CostPrice (Purchase price)
           ├─ ProductVariantStock (per warehouse)
           ├─ Barcode
           ├─ Attributes (Color, Size, etc.)
           └─ ImageUrl
```

---

## 📁 Files Created/Modified

### New Entity Files
| File | Description |
|------|-------------|
| `PointOfSale.Core/Entities/ProductVariant.cs` | Updated with TenantId, pricing fields, navigation properties |
| `PointOfSale.Core/Entities/ProductVariantStock.cs` | Variant-level inventory per warehouse |
| `PointOfSale.Core/Entities/ProductVariantAttribute.cs` | Variant attributes (Color, Size, etc.) |

### New EF Core Configurations
| File | Description |
|------|-------------|
| `PointOfSale.Infrastructure/Data/Configurations/ProductVariantConfiguration.cs` | Fluent mapping with indexes |
| `PointOfSale.Infrastructure/Data/Configurations/ProductVariantStockConfiguration.cs` | Unique constraint on TenantId+VariantId+WarehouseId |
| `PointOfSale.Infrastructure/Data/Configurations/ProductVariantAttributeConfiguration.cs` | Variant attribute indexes |

### Domain Events
| File | Description |
|------|-------------|
| `PointOfSale.Core/Events/ProductVariantEvents.cs` | Created, Updated, StockChanged, Deleted events (not consumed yet) |

### Services
| File | Description |
|------|-------------|
| `PointOfSale.API/Services/ProductVariantService.cs` | Auto-creates default variants for new products |

### Updated Files
| File | Change |
|------|--------|
| `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` | Added DbSets, query filters with TenantId |
| `PointOfSale.API/Program.cs` | Registered IProductVariantService |
| `PointOfSale.API/Controllers/ProductsController.cs` | Calls CreateDefaultVariantAsync on product creation |

### Migration Files
| File | Description |
|------|-------------|
| `migrations/Phase11_1_ProductVariants.sql` | **Run this in pgAdmin4** |
| `PointOfSale.Infrastructure/Data/Migrations/20260112204103_AddProductVariantsPhase11_1.cs` | EF Core migration (auto-generated) |

---

## 🗄️ Database Schema

### ProductVariants Table
```sql
CREATE TABLE "ProductVariants" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "ProductId" uuid NOT NULL REFERENCES "Products"("Id"),
    "SKU" varchar(100),
    "Barcode" varchar(100),
    "Price" numeric(18,2) NOT NULL,        -- POS price
    "OnlinePrice" numeric(18,2),            -- Storefront price
    "CostPrice" numeric(18,2) NOT NULL,     -- Purchase price
    "IsDefault" boolean NOT NULL,           -- One per product MUST be true
    "IsActive" boolean NOT NULL DEFAULT true,
    "ImageUrl" varchar(500),
    "SortOrder" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamptz NOT NULL,
    "UpdatedAt" timestamptz,
    "IsDeleted" boolean NOT NULL DEFAULT false
);

-- Indexes
CREATE INDEX "IX_ProductVariants_TenantId_ProductId" ON "ProductVariants"("TenantId", "ProductId");
CREATE INDEX "IX_ProductVariants_SKU" ON "ProductVariants"("SKU") WHERE "IsDeleted" = false;
CREATE INDEX "IX_ProductVariants_Barcode" ON "ProductVariants"("Barcode") WHERE "IsDeleted" = false;
CREATE INDEX "IX_ProductVariants_ProductId_IsDefault" ON "ProductVariants"("ProductId", "IsDefault") WHERE "IsDefault" = true;
```

### ProductVariantStocks Table
```sql
CREATE TABLE "ProductVariantStocks" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "VariantId" uuid NOT NULL REFERENCES "ProductVariants"("Id"),
    "WarehouseId" uuid NOT NULL REFERENCES "Warehouses"("Id"),
    "Quantity" numeric(18,2) NOT NULL,
    "ReservedQuantity" numeric(18,2) NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false
);

-- Unique constraint: one stock record per variant per warehouse
CREATE UNIQUE INDEX "IX_ProductVariantStocks_TenantId_VariantId_WarehouseId" 
    ON "ProductVariantStocks"("TenantId", "VariantId", "WarehouseId");
```

### ProductVariantAttributes Table
```sql
CREATE TABLE "ProductVariantAttributes" (
    "Id" uuid PRIMARY KEY,
    "VariantId" uuid NOT NULL REFERENCES "ProductVariants"("Id"),
    "Name" varchar(100) NOT NULL,    -- e.g., "Color", "Size"
    "Value" varchar(255) NOT NULL,   -- e.g., "Red", "XL"
    "SortOrder" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamptz NOT NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false
);
```

---

## 🔧 Default Variant Auto-Creation

### On Product Creation (Domain Layer)
When a new product is created via `ProductsController.CreateProduct()`:

```csharp
// After product is added to context
await _productVariantService.CreateDefaultVariantAsync(product);
await _context.SaveChangesAsync(); // Transactional - both saved together
```

The default variant is created with:
- `SKU` = Product.SKU or Product.Barcode
- `Price` = Product.SalePrice
- `OnlinePrice` = Product.OnlinePrice
- `CostPrice` = Product.PurchasePrice
- `IsDefault` = true
- `IsActive` = Product.IsActive

---

## 🚀 How to Apply Migration

### Option 1: pgAdmin4 (Recommended)
1. Open pgAdmin4
2. Connect to your database
3. Open Query Tool
4. Copy contents of `migrations/Phase11_1_ProductVariants.sql`
5. Execute

### Option 2: Command Line
```bash
psql -U postgres -d your_database -f migrations/Phase11_1_ProductVariants.sql
```

---

## ✅ Validation Queries

After migration, run these to verify:

```sql
-- Should be equal:
SELECT COUNT(*) AS "ProductCount" FROM "Products";
SELECT COUNT(*) AS "DefaultVariantCount" FROM "ProductVariants" WHERE "IsDefault" = true;

-- Should return ZERO rows (no products without variants):
SELECT p."Id", p."Name" 
FROM "Products" p 
WHERE NOT EXISTS (SELECT 1 FROM "ProductVariants" pv WHERE pv."ProductId" = p."Id");

-- Should return ZERO rows (no products with multiple defaults):
SELECT "ProductId", COUNT(*) 
FROM "ProductVariants" 
WHERE "IsDefault" = true
GROUP BY "ProductId" 
HAVING COUNT(*) > 1;
```

---

## 📋 Definition of Done

| Requirement | Status |
|-------------|--------|
| Every Product has one default Variant | ✅ |
| Stock exists per Variant | ✅ |
| No Storefront or Checkout code touched | ✅ |
| No breaking changes | ✅ |
| Product creation automatically creates a Variant | ✅ |
| Domain events created (not projected yet) | ✅ |

---

## 🔜 Next Phase: 11.2 — POS Admin Variant UI

Phase 11.2 will add:
- Variant management UI in POS Admin
- Create/Edit/Delete variants for a product
- Variant-specific pricing and stock management
- Barcode generation per variant
