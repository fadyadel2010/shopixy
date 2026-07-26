# AF-17A: Affiliate Inventory & Variant Truth Alignment with Storefront

**Date:** February 24, 2026  
**Status:** 🔴 CRITICAL - Production Blocking  
**Priority:** P0 - Data Integrity Violation

---

## 🚨 EXECUTIVE SUMMARY

### Critical Defects Identified

1. **Zero Stock Products Visible** - Products with no inventory appear as `InStock = true`
2. **Missing Quantity Data** - No `TotalAvailableStock` field exposed
3. **Variant Price = 0.00** - Variant products show zero price instead of variant range
4. **Logic Divergence** - Affiliate Discover uses different inventory rules than Storefront

### Impact
- Affiliates promote out-of-stock products
- Commission calculations incorrect for variant products
- Customer experience degraded (broken links to unavailable products)
- Potential revenue loss from incorrect pricing

### Root Cause
Affiliate projection consumer indexes products independently without using Storefront's authoritative inventory and pricing logic.

---

## 📊 STOREFRONT TRUTH (Source of Authority)

### Location
**File:** `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`  
**Methods:** `MapToDto()`, `MapToListItemDto()`

### Authoritative Logic

```csharp
// PHASE 11.5 — VARIANT-CENTRIC PRICING
var activeVariants = (document.Variants ?? new List<ProductVariantDocument>())
    .Where(v => v.IsActive)
    .ToList();
var defaultVariant = activeVariants.FirstOrDefault(v => v.IsDefault) 
    ?? activeVariants.FirstOrDefault();

decimal effectivePrice;
bool inStock;
int stockQuantity;

if (activeVariants.Count > 0 && defaultVariant != null)
{
    // VARIANT SOURCE OF TRUTH
    effectivePrice = defaultVariant.EffectivePrice;
    inStock = activeVariants.Any(v => v.TotalStock > 0);
    stockQuantity = (int)activeVariants.Sum(v => v.TotalStock);
}
else
{
    // LEGACY FALLBACK (products without variants)
    effectivePrice = document.EffectivePrice;
    inStock = document.InStock;
    stockQuantity = document.StockQuantity;
}

// Product available only if active, visible, AND price > 0
var isAvailable = document.IsActive && document.IsOnlineVisible && effectivePrice > 0;
```

### Key Principles

1. **Variant Products:** Use default variant's `EffectivePrice`
2. **Stock Aggregation:** Sum `TotalStock` across all active variants
3. **InStock Logic:** ANY variant with stock > 0 → product is in stock
4. **Availability:** Must be active, visible, AND have price > 0
5. **Simple Products:** Use product-level fields directly

