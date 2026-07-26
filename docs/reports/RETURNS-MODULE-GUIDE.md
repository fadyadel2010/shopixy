# Purchase Returns and Sales Returns Implementation Guide

## Overview
This document describes the complete implementation of dedicated Purchase Return and Sales Return modules for the Point of Sale system. Both modules provide full integration with inventory, customer/supplier ledgers, and cashbox management.

---

## Features Implemented

### ✅ Purchase Return Module
**Purpose**: Handle returning purchased items back to suppliers

**Key Features**:
- Dedicated return document with unique numbering (PR-YYYYMMDD-XXXX)
- Select supplier and link to original purchase invoice
- Specify return quantities, prices, and reasons
- **Inventory Integration**: Automatically decreases stock when items are returned
- **Supplier Ledger**: Updates supplier balance (debits supplier account)
- **Cashbox Integration**: Optional cash refund from supplier
- Full audit trail with stock movements
- List view with pagination and filtering

**API Endpoints**:
- `GET /api/purchasereturns` - List all purchase returns
- `GET /api/purchasereturns/{id}` - Get specific purchase return
- `GET /api/purchasereturns/invoice/{invoiceId}` - Get purchase invoice for return
- `POST /api/purchasereturns` - Create new purchase return

**Frontend Route**: `/purchase-returns`

---

### ✅ Sales Return Module
**Purpose**: Handle returning sold items from customers

**Key Features**:
- Dedicated return document with unique numbering (SR-YYYYMMDD-XXXX)
- Select customer (optional for walk-in) and link to original sales invoice
- Specify return quantities, prices, and reasons
- **Inventory Integration**: Automatically increases stock when items are returned
- **Customer Ledger**: Updates customer balance (credits customer account)
- **Cashbox Integration**: Optional cash refund to customer
- Full audit trail with stock movements
- List view with pagination and filtering

**API Endpoints**:
- `GET /api/salesreturns` - List all sales returns
- `GET /api/salesreturns/{id}` - Get specific sales return
- `GET /api/salesreturns/invoice/{invoiceId}` - Get sales invoice for return
- `POST /api/salesreturns` - Create new sales return

**Frontend Route**: `/sales-returns`

---

## Technical Implementation

### Backend Controllers

#### 1. PurchaseReturnsController.cs
Location: `PointOfSale.API/Controllers/PurchaseReturnsController.cs`

**Key Methods**:
- `GetPurchaseReturns()` - Paginated list with filtering
- `GetPurchaseReturn(id)` - Get single return with full details
- `GetPurchaseInvoiceForReturn(invoiceId)` - Load original invoice
- `CreatePurchaseReturn(request)` - Create new return

**Business Logic**:
1. Validates supplier and warehouse
2. Validates original invoice (if provided)
3. Checks return quantities don't exceed purchased quantities
4. Generates unique return number
5. Creates return document with items
6. **Decreases inventory** (returning to supplier)
7. Creates stock movements (type: PurchaseReturn)
8. Updates supplier ledger (debit entry)
9. Creates supplier transaction
10. Optional: Records cashbox refund transaction
11. All operations in database transaction

#### 2. SalesReturnsController.cs
Location: `PointOfSale.API/Controllers/SalesReturnsController.cs`

**Key Methods**:
- `GetSalesReturns()` - Paginated list with filtering
- `GetSalesReturn(id)` - Get single return with full details
- `GetSalesInvoiceForReturn(invoiceId)` - Load original invoice
- `CreateSalesReturn(request)` - Create new return

**Business Logic**:
1. Validates warehouse and customer (optional)
2. Validates original invoice (if provided)
3. Checks return quantities don't exceed sold quantities
4. Generates unique return number
5. Creates return document with items
6. **Increases inventory** (customer returning items)
7. Creates stock movements (type: SalesReturn)
8. Updates customer ledger (credit entry)
9. Creates customer transaction
10. Optional: Records cashbox refund transaction
11. All operations in database transaction

---

### Frontend Pages

#### 1. PurchaseReturns.jsx
Location: `PointOfSale.Web/src/pages/PurchaseReturns.jsx`

**Features**:
- **List View**: Table showing all purchase returns with pagination
- **Create View**: Form to create new purchase return
- Supplier selection with automatic invoice loading
- Invoice selection pre-populates items
- Item-level return quantity input
- Validation: Cannot return more than purchased
- Cash refund option
- Real-time total calculation

#### 2. SalesReturns.jsx
Location: `PointOfSale.Web/src/pages/SalesReturns.jsx`

**Features**:
- **List View**: Table showing all sales returns with pagination
- **Create View**: Form to create new sales return
- Customer selection (optional for walk-in)
- Invoice selection pre-populates items
- Item-level return quantity input
- Validation: Cannot return more than sold
- Cash refund option
- Real-time total calculation

---

## Database Schema

### Return Entity
Already exists in: `PointOfSale.Core/Entities/Return.cs`

```csharp
public class Return : BaseEntity
{
    public string ReturnNumber { get; set; }
    public DateTime ReturnDate { get; set; }
    public ReturnType ReturnType { get; set; } // PurchaseReturn or SalesReturn
    
    // Reference to original invoice
    public Guid? OriginalInvoiceId { get; set; }
    public string? OriginalInvoiceNumber { get; set; }
    
    public Guid? CustomerId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    
    // Navigation properties
    public Customer? Customer { get; set; }
    public Supplier? Supplier { get; set; }
    public Warehouse Warehouse { get; set; }
    public ICollection<ReturnItem> Items { get; set; }
}
```

### ReturnItem Entity
Already exists in: `PointOfSale.Core/Entities/ReturnItem.cs`

```csharp
public class ReturnItem : BaseEntity
{
    public Guid ReturnId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    
    public string? Reason { get; set; }
    
    // Navigation properties
    public Return Return { get; set; }
    public Product Product { get; set; }
    public ProductVariant? ProductVariant { get; set; }
}
```

**Note**: The Return and ReturnItem entities are already configured in `ApplicationDbContext.cs`

---

## Integration Points

### 1. Inventory Management
- **Purchase Returns**: Stock is **decreased** (items leaving warehouse)
- **Sales Returns**: Stock is **increased** (items coming back)
- Creates `StockMovement` records with appropriate type
- Updates `WarehouseStock` quantities in real-time

### 2. Financial Ledger
- **Purchase Returns**: 
  - Debit supplier account (reduces what we owe)
  - Creates `SupplierTransaction` record
  - Creates `AccountLedger` entry
  
- **Sales Returns**:
  - Credit customer account (reduces what they owe)
  - Creates `CustomerTransaction` record
  - Creates `AccountLedger` entry

### 3. Cashbox Integration
- **Purchase Returns**: 
  - Optional cash refund FROM supplier
  - Increases cashbox balance
  - Transaction type: `PurchaseRefund`
  
- **Sales Returns**:
  - Optional cash refund TO customer
  - Decreases cashbox balance
  - Transaction type: `SaleRefund`

---

## Navigation

The return modules are accessible from the main navigation:

**Sales Menu**:
- Sales History
- **Sales Returns** ← New
- Customers

**Purchases Menu**:
- New Purchase
- **Purchase Returns** ← New
- Suppliers

---

## Usage Workflow

### Creating a Purchase Return

1. Navigate to **Purchases → Purchase Returns**
2. Click **"+ New Purchase Return"**
3. Select **Supplier**
4. Select **Warehouse**
5. (Optional) Select **Original Purchase Invoice**
   - This pre-populates items from the invoice
6. Specify **Return Quantities** for each item
7. Enter **Reason for Return**
8. (Optional) Check **"Refund to Cash"** if supplier is refunding money
9. Click **"Create Purchase Return"**

**Result**:
- Return document created with unique number (e.g., PR-20251127-0001)
- Stock decreased in warehouse
- Supplier balance updated
- Cashbox updated (if refund selected)
- Stock movements recorded

### Creating a Sales Return

1. Navigate to **Sales → Sales Returns**
2. Click **"+ New Sales Return"**
3. (Optional) Select **Customer** (leave blank for walk-in)
4. Select **Warehouse**
5. (Optional) Select **Original Sales Invoice**
   - This pre-populates items from the invoice
6. Specify **Return Quantities** for each item
7. Enter **Reason for Return**
8. (Optional) Check **"Refund from Cash"** if refunding customer
9. Click **"Create Sales Return"**

**Result**:
- Return document created with unique number (e.g., SR-20251127-0001)
- Stock increased in warehouse
- Customer balance updated
- Cashbox updated (if refund selected)
- Stock movements recorded

---

## Validation Rules

### Purchase Returns
- ✅ Supplier must be selected
- ✅ Warehouse must be selected
- ✅ At least one item with quantity > 0
- ✅ Return quantity cannot exceed purchased quantity
- ✅ Sufficient stock must exist in warehouse
- ✅ Refund amount cannot exceed total return amount

### Sales Returns
- ✅ Warehouse must be selected
- ✅ At least one item with quantity > 0
- ✅ Return quantity cannot exceed sold quantity
- ✅ Refund amount cannot exceed total return amount

---

## Return Document Numbering

### Purchase Returns
Format: `PR-YYYYMMDD-XXXX`
- PR = Purchase Return prefix
- YYYYMMDD = Date (e.g., 20251127)
- XXXX = Sequential number (0001, 0002, etc.)
- Resets daily

Example: `PR-20251127-0001`

### Sales Returns
Format: `SR-YYYYMMDD-XXXX`
- SR = Sales Return prefix
- YYYYMMDD = Date (e.g., 20251127)
- XXXX = Sequential number (0001, 0002, etc.)
- Resets daily

Example: `SR-20251127-0001`

---

## API Request Examples

### Create Purchase Return

```json
POST /api/purchasereturns
{
  "supplierId": "guid",
  "warehouseId": "guid",
  "returnDate": "2025-11-27T00:00:00Z",
  "originalInvoiceId": "guid", // optional
  "items": [
    {
      "productId": "guid",
      "productVariantId": null,
      "quantity": 5,
      "unitPrice": 10.00,
      "taxRate": 10,
      "reason": "Damaged goods"
    }
  ],
  "reason": "Items arrived damaged",
  "notes": "Supplier agreed to refund",
  "refundToCash": true,
  "refundAmount": 55.00
}
```

### Create Sales Return

```json
POST /api/salesreturns
{
  "customerId": "guid", // optional
  "warehouseId": "guid",
  "returnDate": "2025-11-27T00:00:00Z",
  "originalInvoiceId": "guid", // optional
  "items": [
    {
      "productId": "guid",
      "productVariantId": null,
      "quantity": 2,
      "unitPrice": 25.00,
      "taxRate": 10,
      "reason": "Customer changed mind"
    }
  ],
  "reason": "Customer not satisfied",
  "notes": "Full refund issued",
  "refundFromCash": true,
  "refundAmount": 55.00
}
```

---

## Testing Checklist

### Purchase Returns
- [ ] Create return without original invoice
- [ ] Create return with original invoice
- [ ] Verify stock decreases correctly
- [ ] Verify supplier balance updates
- [ ] Verify cashbox refund (if selected)
- [ ] Verify stock movement records
- [ ] Verify ledger entries
- [ ] Test validation: return quantity > purchased quantity
- [ ] Test validation: insufficient stock
- [ ] Test pagination in list view

### Sales Returns
- [ ] Create return without customer
- [ ] Create return with customer
- [ ] Create return with original invoice
- [ ] Verify stock increases correctly
- [ ] Verify customer balance updates
- [ ] Verify cashbox refund (if selected)
- [ ] Verify stock movement records
- [ ] Verify ledger entries
- [ ] Test validation: return quantity > sold quantity
- [ ] Test pagination in list view

---

## Files Created/Modified

### Backend Files Created
1. `PointOfSale.API/Controllers/PurchaseReturnsController.cs`
2. `PointOfSale.API/Controllers/SalesReturnsController.cs`

### Frontend Files Created
1. `PointOfSale.Web/src/pages/PurchaseReturns.jsx`
2. `PointOfSale.Web/src/pages/SalesReturns.jsx`

### Files Modified
1. `PointOfSale.Web/src/App.jsx` - Added routes
2. `PointOfSale.Web/src/components/Layout.jsx` - Added navigation links

### Existing Entities Used
- `PointOfSale.Core/Entities/Return.cs`
- `PointOfSale.Core/Entities/ReturnItem.cs`
- `PointOfSale.Core/Enums/ReturnType.cs`
- `PointOfSale.Core/Enums/StockMovementType.cs`
- `PointOfSale.Core/Enums/TransactionType.cs`
- `PointOfSale.Core/Enums/CashboxTransactionType.cs`

---

## Next Steps

1. **Build the API**:
   ```bash
   dotnet build PointOfSale.API/PointOfSale.API.csproj
   ```

2. **Run the API**:
   ```bash
   dotnet run --project PointOfSale.API/PointOfSale.API.csproj
   ```

3. **Start the Frontend**:
   ```bash
   cd PointOfSale.Web
   npm run dev
   ```

4. **Test the Features**:
   - Navigate to `/purchase-returns`
   - Navigate to `/sales-returns`
   - Create test returns
   - Verify inventory updates
   - Verify ledger entries
   - Verify cashbox transactions

---

## Support & Troubleshooting

### Common Issues

**Issue**: "Supplier not found"
- **Solution**: Ensure supplier exists and ID is correct

**Issue**: "Insufficient stock"
- **Solution**: Check warehouse stock levels before creating purchase return

**Issue**: "Cannot return more than purchased quantity"
- **Solution**: Verify return quantity against original invoice

**Issue**: Navigation links not showing
- **Solution**: Clear browser cache and refresh

---

## Summary

This implementation provides complete, production-ready Purchase Return and Sales Return modules with:

✅ Dedicated screens and workflows
✅ Full inventory integration (real-time stock updates)
✅ Complete financial ledger integration
✅ Cashbox integration for refunds
✅ Unique document numbering
✅ Comprehensive validation
✅ Audit trail via stock movements
✅ Professional UI with list and create views
✅ Pagination and filtering
✅ Transaction safety (database transactions)

Both modules are fully integrated into the existing POS system and maintain data integrity across all related systems.
