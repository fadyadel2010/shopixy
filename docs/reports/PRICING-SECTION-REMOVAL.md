# Pricing Section Removal - Update Summary

## Overview
Removed the redundant Pricing section from the Product Form since cost and selling prices are now managed per unit in the Product Units section. Tax Rate was moved to Basic Information as it applies to the product as a whole.

## Changes Made

### 1. **Removed from State**
- `purchasePrice` field removed from `formData`
- `salePrice` field removed from `formData`

### 2. **Updated Product Loading**
- Removed `purchasePrice` and `salePrice` from `fetchProduct()` function
- These fields are no longer loaded when editing existing products

### 3. **Updated Product Saving**
- Set `purchasePrice: 0` and `salePrice: 0` as defaults when saving products
- Added comment explaining these are managed per unit
- Prices are now defined individually for each unit in the Product Units section

### 4. **UI Changes**
**Removed:**
- Entire "Pricing" section card
- Cost Price input field
- Selling Price input field

**Moved to Basic Information:**
- Tax Rate (%) field
- "Price includes tax" checkbox

### 5. **Form Layout**
**Basic Information Section Now Includes:**
1. Product Name *
2. SKU
3. Barcode (with Generate button)
4. Category *
5. Description
6. **Tax Rate (%)** ← Moved here
7. **Price includes tax** ← Moved here

## Rationale

### Why Remove Pricing Section?
1. **Redundancy**: Users now specify purchase and selling prices for each unit
2. **Clarity**: Having two places for prices was confusing
3. **Flexibility**: Different units can have different prices (e.g., Box vs Piece)
4. **Better UX**: Streamlined form with less duplication

### Why Keep Tax Rate?
- Tax rate typically applies to the **entire product**, not per unit
- It's a product-level attribute used in calculations
- Simplifies tax management across all units

## User Workflow

### Before:
1. Enter product details
2. Enter **Cost Price** and **Selling Price** ← Redundant!
3. Add units with their own prices ← Duplicate!
4. Save

### After:
1. Enter product details
2. Set Tax Rate (applies to all units)
3. Add units with **their specific prices**
4. Save

## Example Use Case

**Product: Coca Cola**

**Units:**
- **Piece** (Base Unit)
  - Purchase Price: $0.50
  - Selling Price: $1.00
  
- **6-Pack**
  - Conversion: 6 pieces
  - Purchase Price: $2.50
  - Selling Price: $5.00
  
- **Case** (24 cans)
  - Conversion: 24 pieces
  - Purchase Price: $9.00
  - Selling Price: $18.00

**Tax Rate:** 10% (applies to all units)

## Technical Notes

- Product table still has `PurchasePrice` and `SalePrice` columns for backward compatibility
- These are set to `0` for new products
- Existing products retain their old values but they're not displayed/editable
- Actual prices are retrieved from the ProductUnits table

## Benefits

✅ **Eliminates confusion** - One source of truth for prices  
✅ **More flexible** - Different prices for different units  
✅ **Cleaner UI** - Less fields to fill  
✅ **Better data model** - Prices where they belong (per unit)  
✅ **Consistent** - All pricing in one section  

## Files Modified

1. `PointOfSale.Web/src/components/ProductForm.jsx`
   - Removed `purchasePrice` and `salePrice` from state
   - Removed Pricing section from UI
   - Moved Tax Rate to Basic Information
   - Updated save logic to set default prices to 0

## Migration Notes

- **No database changes required**
- **No API changes required**
- **Backward compatible** - existing products unaffected
- Old price fields in database remain but are unused for new products
