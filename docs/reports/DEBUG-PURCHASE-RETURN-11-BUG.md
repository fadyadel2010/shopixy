# Debug Query for Purchase Return Issue

## Problem
- Stock was 14 pieces
- Purchased 1 Carton (10 pieces) → Stock became 24 pieces ✅ CORRECT
- Returned 1 Carton → Stock became 13 pieces ❌ WRONG (should be 14)
- It removed 11 pieces instead of 10

## Diagnostic Queries

Run these queries in your database to investigate:

### 1. Check Stock Movements for the Product
```sql
SELECT 
    "Id",
    "MovementType",
    "Quantity" as DisplayQuantity,
    "ConversionFactor",
    "QuantityChange" as BaseQuantityChange,
    "QuantityAfter",
    "ReferenceNumber",
    "Notes",
    "CreatedAt"
FROM "StockMovements"
WHERE "ProductId" = 'YOUR-PRODUCT-GUID-HERE'
ORDER BY "CreatedAt" DESC
LIMIT 10;
```

**What to look for:**
- The Purchase should show: `QuantityChange = +10`, `ConversionFactor = 10`
- The Return should show: `QuantityChange = -10`, `ConversionFactor = 10`
- If Return shows `-11`, there's a bug in the calculation

### 2. Check the Return Items Table
```sql
SELECT 
    ri."Id",
    ri."Quantity" as DisplayQuantity,
    ri."ConversionFactor",
    ri."BaseQuantity",
    r."ReturnNumber",
    r."ReturnType",
    r."CreatedAt"
FROM "ReturnItems" ri
JOIN "Returns" r ON ri."ReturnId" = r."Id"
WHERE ri."ProductId" = 'YOUR-PRODUCT-GUID-HERE'
ORDER BY r."CreatedAt" DESC
LIMIT 5;
```

**What to look for:**
- `Quantity` should be `1` (1 Carton)
- `ConversionFactor` should be `10`
- `BaseQuantity` should be `10` (1 × 10)

### 3. Check Purchase Invoice Items
```sql
SELECT 
    pii."Id",
    pii."Quantity" as DisplayQuantity,
    pii."ConversionFactor",
    pii."BaseQuantity",
    pi."InvoiceNumber",
    pi."CreatedAt"
FROM "PurchaseInvoiceItems" pii
JOIN "PurchaseInvoices" pi ON pii."PurchaseInvoiceId" = pi."Id"
WHERE pii."ProductId" = 'YOUR-PRODUCT-GUID-HERE'
ORDER BY pi."CreatedAt" DESC
LIMIT 5;
```

**What to look for:**
- The purchase should show `Quantity = 1`, `ConversionFactor = 10`, `BaseQuantity = 10`

### 4. Check Current Stock
```sql
SELECT 
    ws."Quantity" as CurrentStock,
    ws."ReservedQuantity",
    ws."AvailableQuantity",
    w."Name" as WarehouseName,
    p."Name" as ProductName
FROM "WarehouseStocks" ws
JOIN "Warehouses" w ON ws."WarehouseId" = w."Id"
JOIN "Products" p ON ws."ProductId" = p."Id"
WHERE ws."ProductId" = 'YOUR-PRODUCT-GUID-HERE';
```

---

## Possible Causes

### Theory 1: Double Deduction Bug
The system might be deducting stock twice:
- Once with `quantity` (1)
- Once with `baseQuantity` (10)
- Total: 11 pieces removed

**Check:** Look for duplicate stock movements in the database

### Theory 2: Incorrect Conversion Factor
The conversion factor might be 11 instead of 10.

**Check:** Query the ProductUnits table:
```sql
SELECT 
    pu."ConversionFactor",
    u."Name" as UnitName,
    u."Symbol",
    pu."IsBaseUnit",
    pu."IsDefault"
FROM "ProductUnits" pu
JOIN "Units" u ON pu."UnitId" = u."Id"
WHERE pu."ProductId" = 'YOUR-PRODUCT-GUID-HERE';
```

### Theory 3: Frontend Sending Wrong Quantity
The frontend might be sending `quantity: 11` instead of `quantity: 1`.

**Check:** Open browser DevTools → Network tab → Look at the POST request to `/api/purchasereturns`

---

## How to Get Product GUID

Run this query to find your product's GUID:
```sql
SELECT "Id", "Name", "SKU"
FROM "Products"
WHERE "Name" LIKE '%YOUR-PRODUCT-NAME%';
```

---

## Expected Results

If everything is correct, you should see:

**Purchase Invoice Item:**
- Quantity: 1
- ConversionFactor: 10
- BaseQuantity: 10

**Purchase Return Item:**
- Quantity: 1
- ConversionFactor: 10
- BaseQuantity: 10

**Stock Movement (Purchase):**
- QuantityChange: +10
- ConversionFactor: 10

**Stock Movement (Return):**
- QuantityChange: -10
- ConversionFactor: 10

**Final Stock:**
- Should be 14 pieces (24 - 10)

---

## Next Steps

1. Run the diagnostic queries above
2. Share the results with me
3. I'll identify exactly where the bug is
4. We'll fix it together!

The issue is likely in one of these places:
- ❌ Conversion factor is wrong (11 instead of 10)
- ❌ Stock is being deducted twice
- ❌ Frontend is sending wrong quantity
- ❌ Validation is modifying the quantity

Let's find out which one! 🔍
