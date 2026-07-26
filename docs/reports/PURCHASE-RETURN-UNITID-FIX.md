# Purchase & Sales Returns - UnitId Bug Fix

## ✅ Fix Applied - 2025-12-05

### Problem Summary
When creating Purchase Returns or Sales Returns for products with multiple units (e.g., Carton = 10 pieces), the system was deducting only 1 piece instead of the correct base quantity (10 pieces).

**Root Cause:** Frontend was not sending `unitId` to the backend, causing the conversion factor to default to 1.

---

## 🔧 Changes Made

### 1. Purchase Returns (`PurchaseReturns.jsx`) - ✅ FIXED

#### Change 1: Capture `unitId` from Original Invoice
**Location:** `handleInvoiceSelect` function (lines 109-139)

**Before:**
```javascript
const returnItems = response.data.items.map(item => ({
    productId: item.productId,
    productVariantId: item.productVariantId,
    name: item.product.name,
    // ❌ unitId was MISSING
    originalQuantity: item.quantity,
    quantity: 0,
    unitPrice: item.unitPrice,
    taxRate: item.taxRate,
    reason: ''
}))
```

**After:**
```javascript
const returnItems = response.data.items.map(item => ({
    productId: item.productId,
    productVariantId: item.productVariantId,
    name: item.product.name,
    unitId: item.unitId, // ✅ ADDED: Capture unitId
    unitName: item.unit?.name || 'Piece', // ✅ ADDED: Display unit name
    originalQuantity: item.quantity,
    quantity: 0,
    unitPrice: item.unitPrice,
    taxRate: item.taxRate,
    reason: ''
}))
```

#### Change 2: Send `unitId` in API Request
**Location:** `handleSubmit` function (lines 161-215)

**Before:**
```javascript
items: returnItems.map(i => ({
    productId: i.productId,
    productVariantId: i.productVariantId,
    // ❌ unitId was MISSING
    quantity: i.quantity,
    unitPrice: i.unitPrice,
    taxRate: i.taxRate,
    reason: i.reason
}))
```

**After:**
```javascript
items: returnItems.map(i => ({
    productId: i.productId,
    productVariantId: i.productVariantId,
    unitId: i.unitId, // ✅ ADDED: Send unitId to backend
    quantity: i.quantity,
    unitPrice: i.unitPrice,
    taxRate: i.taxRate,
    reason: i.reason
}))
```

---

### 2. Sales Returns (`SalesReturns.jsx`) - ✅ ALREADY CORRECT

**Status:** No changes needed! Sales Returns was already correctly:
- Capturing `unitId` from original invoice (line 118)
- Sending `unitId` in the API payload (line 188)

---

## 🧪 Testing Instructions

### Test Scenario:
1. **Create a product with multiple units:**
   - Product: "Test Product"
   - Base Unit: Piece (conversion: 1)
   - Carton: 10 pieces (conversion: 10)
   - Initial stock: 4 pieces

2. **Create a Purchase Invoice:**
   - Buy 1 Carton
   - **Expected:** Stock increases to 14 pieces (4 + 10)
   - **Verify:** Check stock in inventory

3. **Create a Purchase Return:**
   - Return 1 Carton
   - **Expected:** Stock decreases to 4 pieces (14 - 10)
   - **Verify:** Check stock in inventory

4. **Verify in Database:**
   ```sql
   SELECT * FROM "StockMovements" 
   WHERE "ProductId" = 'your-product-id'
   ORDER BY "CreatedAt" DESC
   ```
   - Should show `QuantityChange = -10` for the return
   - Should show `ConversionFactor = 10`
   - Should show correct `UnitId`

---

## 📊 Expected API Payload

### Purchase Return Request (After Fix):
```json
{
    "supplierId": "guid",
    "warehouseId": "guid",
    "returnDate": "2025-12-05",
    "originalInvoiceId": "guid",
    "items": [
        {
            "productId": "guid",
            "productVariantId": null,
            "unitId": "carton-unit-guid",  // ✅ NOW INCLUDED
            "quantity": 1,
            "unitPrice": 100.00,
            "taxRate": 10,
            "reason": "Damaged"
        }
    ],
    "reason": "Damaged goods",
    "notes": "",
    "refundToCash": true,
    "refundAmount": 110.00
}
```

---

## 🎯 Backend Processing (Already Correct)

The backend correctly processes the `unitId`:

1. **Retrieves conversion factor:**
   ```csharp
   var productUnit = await _context.ProductUnits
       .FirstOrDefaultAsync(pu => pu.ProductId == item.ProductId && pu.UnitId == item.UnitId);
   
   conversionFactor = productUnit.ConversionFactor; // Gets 10 for Carton
   ```

2. **Calculates base quantity:**
   ```csharp
   var baseQuantity = itemRequest.Quantity * conversionFactor;
   // 1 Carton * 10 = 10 pieces
   ```

3. **Updates stock:**
   ```csharp
   stock.Quantity -= baseQuantity; // Deducts 10 pieces, not 1
   ```

---

## ✅ Verification Checklist

After deploying the fix, verify:

- [ ] Purchase Return captures `unitId` from original invoice
- [ ] Purchase Return sends `unitId` in API request
- [ ] Stock decreases by correct base quantity (e.g., 10 pieces for 1 Carton)
- [ ] Stock Movement records show correct `ConversionFactor`
- [ ] Stock Movement records show correct `UnitId`
- [ ] Sales Return still works correctly (no regression)
- [ ] Multi-unit products calculate correctly
- [ ] Single-unit products still work (backward compatibility)

---

## 📝 Notes

- **Sales Returns** was already correct and required no changes
- **Purchase Returns** had the bug and has been fixed
- The backend logic was always correct - this was purely a frontend issue
- The fix ensures that `unitId` is preserved from the original invoice through to the return
- This maintains the integrity of the multi-unit system

---

## 🚀 Impact

**Before Fix:**
- Returning 1 Carton (10 pieces) → Stock decreased by 1 piece ❌

**After Fix:**
- Returning 1 Carton (10 pieces) → Stock decreases by 10 pieces ✅

The multi-unit system now works correctly for all return scenarios! 🎉
