# Testing the POS System

## Prerequisites

Before testing, ensure:
1. ✅ API is running on https://localhost:7263
2. ✅ Frontend is running on http://localhost:5173
3. ✅ You're logged in as admin
4. ✅ At least one warehouse exists in the database
5. ✅ Some products exist in the database

## Test Checklist

### 1. Barcode Scanning
- [ ] Focus is automatically on barcode input when page loads
- [ ] Typing a barcode and pressing Enter searches for the product
- [ ] Product is added to cart automatically
- [ ] Scanning the same barcode again increases quantity
- [ ] Error message shows if product not found
- [ ] Press F1 to refocus barcode input

### 2. Product Search
- [ ] Type at least 2 characters to trigger search
- [ ] Autocomplete dropdown appears with matching products
- [ ] Products show name, category, price, and stock
- [ ] Use ↑↓ arrow keys to navigate results
- [ ] Press Enter to select highlighted product
- [ ] Product is added to cart
- [ ] Press F2 to focus search input

### 3. Shopping Cart
- [ ] Items appear in cart after adding
- [ ] Quantity can be increased/decreased with +/- buttons
- [ ] Quantity can be typed directly
- [ ] Cannot add more than available stock
- [ ] Low stock warning appears when quantity > 80% of stock
- [ ] Item discount can be entered per item
- [ ] Remove button (×) removes item from cart
- [ ] Cart shows total item count

### 4. Discounts
- [ ] Can switch between percentage and fixed amount discount
- [ ] Invoice discount is applied to subtotal
- [ ] Both item and invoice discounts are shown separately
- [ ] Totals recalculate automatically

### 5. Payment Processing
- [ ] "Process Payment" button is disabled when cart is empty
- [ ] Clicking payment button opens payment modal
- [ ] Can select Cash, Card, Mixed, or Credit payment
- [ ] **Cash Payment:**
  - [ ] Enter cash amount
  - [ ] Change is calculated and displayed
  - [ ] Cannot proceed if cash < total
- [ ] **Card Payment:**
  - [ ] Full amount charged to card
- [ ] **Mixed Payment:**
  - [ ] Can split between cash and card
  - [ ] Shows remaining amount to pay
  - [ ] Cannot proceed if total payment < invoice total
- [ ] **Credit Payment:**
  - [ ] Warning shows that it will be added to customer account
  - [ ] Requires customer to be selected (shows error if not)

### 6. Sale Completion
- [ ] After successful payment, success message shows
- [ ] Receipt modal appears automatically
- [ ] Cart is cleared
- [ ] Invoice number is displayed

### 7. Receipt
- [ ] Receipt shows all invoice details
- [ ] Items list is complete with quantities and prices
- [ ] Totals are correct (subtotal, discount, tax, total)
- [ ] Payment method and amount paid are shown
- [ ] Change is shown (if cash payment)
- [ ] Print button opens browser print dialog
- [ ] Receipt is formatted for thermal printer (80mm width)
- [ ] Close button closes the receipt

### 8. Keyboard Shortcuts
- [ ] F1 - Focuses barcode scanner
- [ ] F2 - Focuses product search
- [ ] F9 - Opens payment modal (when cart has items)
- [ ] Esc - Clears cart with confirmation (when cart has items)

### 9. Stock Validation
- [ ] Cannot add more items than available in stock
- [ ] Error message shows when trying to exceed stock
- [ ] Stock is updated in database after sale
- [ ] Stock movement is recorded

### 10. Error Handling
- [ ] Error messages appear for invalid operations
- [ ] Success messages appear for successful operations
- [ ] Messages auto-dismiss after a few seconds
- [ ] API errors are displayed to user

## Creating Test Data

If you don't have products yet, create some test products:

```sql
-- Insert a test warehouse
INSERT INTO "Warehouses" ("Id", "Name", "Code", "Address", "Phone", "IsDefault", "IsActive", "CreatedAt", "IsDeleted")
VALUES (gen_random_uuid(), 'Main Warehouse', 'WH001', '123 Main St', '555-0100', true, true, NOW(), false);

-- Insert a test category
INSERT INTO "Categories" ("Id", "Name", "Description", "ParentCategoryId", "CreatedAt", "IsDeleted")
VALUES (gen_random_uuid(), 'Electronics', 'Electronic items', NULL, NOW(), false);

-- Insert test products
INSERT INTO "Products" ("Id", "Name", "Barcode", "SKU", "CategoryId", "PurchasePrice", "SalePrice", "TaxRate", "PriceIncludesTax", "Unit", "MinimumStockLevel", "TrackStock", "IsActive", "CreatedAt", "IsDeleted")
SELECT 
    gen_random_uuid(),
    'Test Product ' || i,
    '12345678' || LPAD(i::text, 2, '0'),
    'SKU-' || i,
    (SELECT "Id" FROM "Categories" LIMIT 1),
    10.00,
    15.00,
    10.00,
    false,
    'Piece',
    5,
    true,
    true,
    NOW(),
    false
FROM generate_series(1, 10) i;

-- Add stock to warehouse
INSERT INTO "WarehouseStocks" ("Id", "WarehouseId", "ProductId", "ProductVariantId", "Quantity", "ReservedQuantity", "CreatedAt", "IsDeleted")
SELECT 
    gen_random_uuid(),
    (SELECT "Id" FROM "Warehouses" LIMIT 1),
    p."Id",
    NULL,
    100,
    0,
    NOW(),
    false
FROM "Products" p;
```

## Expected Results

After completing a sale:
1. ✅ Sale is saved in `SalesInvoices` table
2. ✅ Sale items are saved in `SalesInvoiceItems` table
3. ✅ Stock is reduced in `WarehouseStocks` table
4. ✅ Stock movement is recorded in `StockMovements` table
5. ✅ Customer balance is updated (if credit sale)
6. ✅ Customer transaction is recorded (if credit sale)
7. ✅ Receipt can be printed

## Known Limitations

- Customer selection UI is not yet implemented (shows placeholder button)
- Popular/recent products grid is not yet populated
- PDF export uses browser print (dedicated PDF generation can be added)
- Thermal printer ESC/POS commands not yet implemented (receipt is formatted for 80mm width)

## Next Steps

After testing, you may want to:
1. Add customer selection functionality
2. Implement popular products grid
3. Add product images to cart display
4. Implement actual thermal printer integration
5. Add sales history and reports
6. Implement returns processing
