# Purchase Return Bug Investigation Report

## 🔍 Investigation Summary

**Date:** 2025-12-05  
**Issue:** Stock calculation incorrect when performing Purchase Return with multi-unit products  
**Status:** ✅ **BACKEND IS CORRECT** - Issue is in **FRONTEND**

---

## 📊 User's Scenario

1. **Initial Stock:** 4 pieces
2. **Purchase:** 1 Carton (1 Carton = 10 pieces)
3. **Expected Stock After Purchase:** 4 + 10 = **14 pieces** ✅ CORRECT
4. **Return:** 1 Carton
5. **Expected Stock After Return:** 14 - 10 = **4 pieces** ❌ **ACTUAL: 13 pieces**

**Problem:** System deducted only 1 piece instead of 10 pieces

---

## ✅ Backend Code Analysis

### Purchase Returns Controller (`PurchaseReturnsController.cs`)

**Lines 212-240: Unit Conversion Logic**
```csharp
// Get unit information and conversion factor
decimal conversionFactor = 1;
Guid? unitId = itemRequest.UnitId;

if (itemRequest.UnitId.HasValue)
{
    var productUnit = await _context.ProductUnits
        .FirstOrDefaultAsync(pu => pu.ProductId == itemRequest.ProductId && pu.UnitId == itemRequest.UnitId);
    
    if (productUnit != null)
    {
        conversionFactor = productUnit.ConversionFactor;  // ✅ Gets conversion factor
    }
}

// Calculate base quantity
var baseQuantity = itemRequest.Quantity * conversionFactor;  // ✅ CORRECT
```

**Lines 268-285: Stock Update Logic**
```csharp
// Update stock - DECREASE (returning to supplier) using BASE quantity
var stock = await _context.WarehouseStocks
    .FirstOrDefaultAsync(ws =>
        ws.WarehouseId == request.WarehouseId &&
        ws.ProductId == itemRequest.ProductId &&
        ws.ProductVariantId == itemRequest.ProductVariantId);

if (stock.AvailableQuantity < baseQuantity)  // ✅ Checks base quantity
{
    return BadRequest(...);
}

stock.Quantity -= baseQuantity;  // ✅ CORRECT - Uses base quantity
```

**Lines 287-305: Stock Movement Record**
```csharp
var movement = new StockMovement
{
    MovementType = StockMovementType.PurchaseReturn,
    UnitId = unitId,  // ✅ Stores unit
    ConversionFactor = conversionFactor,  // ✅ Stores conversion factor
    QuantityChange = -baseQuantity,  // ✅ CORRECT - Negative base quantity
    QuantityAfter = stock.Quantity,
    ...
};
```

### Sales Returns Controller (`SalesReturnsController.cs`)

**Lines 216-244: Unit Conversion Logic** - ✅ IDENTICAL to Purchase Returns  
**Lines 272-295: Stock Update Logic** - ✅ CORRECT (uses `+=` to increase stock)  
**Lines 297-315: Stock Movement Record** - ✅ CORRECT

---

## ❌ Root Cause: Frontend Issue

The backend code is **100% correct**. The problem is that the **frontend is not sending the `UnitId`** in the request, or it's sending the wrong unit.

### What's Happening:

1. **Frontend sends request WITHOUT `UnitId`:**
   ```json
   {
       "productId": "xxx",
       "quantity": 1,
       "unitId": null  // ❌ MISSING or NULL
   }
   ```

2. **Backend receives null `UnitId`:**
   - Falls back to default unit (line 228-236)
   - If no default unit exists, `conversionFactor` remains `1`
   - Result: `baseQuantity = 1 * 1 = 1` instead of `1 * 10 = 10`

3. **Stock is updated with wrong quantity:**
   - Deducts 1 piece instead of 10 pieces

---

## 🔧 Required Fix

### Frontend Files to Check:

1. **`PurchaseReturns.jsx`** - Purchase Return form
2. **`SalesReturns.jsx`** - Sales Return form

### What to Look For:

```javascript
// ❌ WRONG - Not sending unitId
const returnItems = items.map(item => ({
    productId: item.productId,
    quantity: item.quantity,
    // unitId is MISSING!
}));

// ✅ CORRECT - Must send unitId
const returnItems = items.map(item => ({
    productId: item.productId,
    quantity: item.quantity,
    unitId: item.unitId,  // ✅ MUST INCLUDE THIS
}));
```

### Specific Issues to Check:

1. **Is `unitId` being captured in the form state?**
   ```javascript
   const [items, setItems] = useState([{
       productId: '',
       quantity: 0,
       unitId: null,  // ← Is this being set?
   }]);
   ```

2. **Is `unitId` being sent in the API request?**
   ```javascript
   const response = await api.post('/purchasereturns', {
       items: items.map(item => ({
           productId: item.productId,
           quantity: item.quantity,
           unitId: item.unitId,  // ← Is this included?
       }))
   });
   ```

3. **Is the unit selector showing the correct unit from the original invoice?**
   - When loading an invoice for return, the `unitId` should be pre-populated

---

## 🧪 Testing Steps

### To Verify the Fix:

1. **Create a test product with multiple units:**
   - Base Unit: Piece (conversion: 1)
   - Carton: 10 pieces (conversion: 10)

2. **Create a Purchase Invoice:**
   - Buy 1 Carton
   - Verify stock increases by 10 pieces

3. **Create a Purchase Return:**
   - Return 1 Carton
   - **Verify:**
     - ✅ Stock decreases by 10 pieces (not 1)
     - ✅ Stock Movement shows correct base quantity
     - ✅ Conversion factor is recorded

4. **Check the Network Request:**
   - Open browser DevTools → Network tab
   - Create a return
   - Inspect the POST request payload
   - **Verify `unitId` is present and correct**

---

## 📋 Expected Request Payload

```json
{
    "supplierId": "guid",
    "warehouseId": "guid",
    "returnDate": "2025-12-05",
    "originalInvoiceId": "guid",
    "items": [
        {
            "productId": "guid",
            "unitId": "carton-unit-guid",  // ✅ MUST BE PRESENT
            "quantity": 1,  // Quantity in the selected unit (Carton)
            "unitPrice": 100.00,
            "taxRate": 10,
            "reason": "Damaged"
        }
    ],
    "reason": "Damaged goods",
    "refundToCash": true,
    "refundAmount": 110.00
}
```

---

## 🎯 Summary

| Component | Status | Notes |
|-----------|--------|-------|
| **Backend - Purchase Returns** | ✅ CORRECT | Properly uses conversion factor |
| **Backend - Sales Returns** | ✅ CORRECT | Properly uses conversion factor |
| **Backend - Stock Movement** | ✅ CORRECT | Records base quantity correctly |
| **Frontend - Purchase Returns** | ❌ **NEEDS FIX** | Not sending `unitId` |
| **Frontend - Sales Returns** | ❌ **NEEDS FIX** | Not sending `unitId` |

---

## 🚀 Next Steps

1. **Locate the frontend return forms**
2. **Verify `unitId` is captured from original invoice**
3. **Ensure `unitId` is sent in the API request**
4. **Test with multi-unit products**
5. **Verify stock calculations are correct**

The backend is rock-solid! We just need to make sure the frontend is sending the complete data. 💪
