# Warehouse Opening Stock Balance Implementation Plan

## Overview
Implementing the "Opening Stock Balance – Warehouses" module to allow users to enter initial inventory quantities for each product before the system is used for the first time.

## Implementation Steps

### 1. Database Changes
- **StockMovement Entity**: Already has `IsOpeningBalance` field (will add if missing)
- **Add enum value**: `OpeningBalance` to `StockMovementType` enum
- **Migration**: Create migration to add the opening balance field if needed

### 2. Backend API (WarehousesController.cs)
Create two new endpoints:

#### GET `/api/warehouses/opening-stock`
- Query parameter: `warehouseId` (required)
- Returns: List of products with their opening stock status
- Response includes:
  - Product ID, Code, Name
  - Base Unit
  - Available Units (for conversion)
  - Opening Quantity (if exists)
  - Cost Price (if exists)
  - Total Value
  - `HasOpeningStock` flag

#### POST `/api/warehouses/opening-stock`
- Saves opening stock for multiple products in one warehouse
- **Validation**:
  - Warehouse must exist
  - Product must exist
  - Each product can only have ONE opening balance per warehouse
  - Quantity must be > 0
  - Unit conversion to base unit
- **Creates**:
  1. `StockMovement` record with `MovementType = OpeningBalance` and `IsOpeningBalance = true`
  2. `WarehouseStock` record (or updates existing)
- **Transaction-based** for data integrity

### 3. Frontend Implementation
Create: `WarehouseOpeningStock.jsx`

**Features**:
- Warehouse selector (dropdown - required)
- Product search/autocomplete
- Unit selector (shows available units for selected product)
- Opening quantity input (numeric)
- Cost price input (optional)
- Add button to insert into table
- Table showing:
  - Product Code
  - Product Name
  - Unit
  - Opening Quantity
  - Cost Price
  - Total Value (auto-calculated)
  - Delete button (only for unsaved items)
- Save button (saves all entries)
- Loading states
- Error handling with toast notifications

### 4. Navigation & Routing
- **App.jsx**: Add route `/opening-balance/warehouse-stock`
- **Sidebar.jsx**: Add menu item under "Opening Balance" section

### 5. Translations
Add keys to `config.js` for both English and Arabic:
- Page title
- Form labels
- Table headers
- Messages
- Validation errors

### 6. Business Logic

#### Unit Conversion
- User can select any unit for the product
- System converts to base unit before saving
- Example: If user enters "2 Cartons" and 1 Carton = 12 Pieces, save as 24 Pieces

#### Opening Stock Rules
- **One-time only**: Each product can have only ONE opening balance per warehouse
- **Permanent**: Cannot be edited or deleted after saving
- **Included in all reports**:
  - Warehouse Stock Balance
  - Inventory Valuation Report
  - Item Movement Details Report
  - Product Card → Stock Summary

#### Stock Movement
- `MovementType` = `OpeningBalance`
- `IsOpeningBalance` = `true`
- `QuantityChange` = positive quantity (in base unit)
- `QuantityAfter` = opening quantity
- `ReferenceNumber` = `OB-WH-{warehouseId}-{productId}`

### 7. Integration Points
1. ✅ **WarehouseStock** - Creates or updates stock record
2. ✅ **StockMovement** - Creates audit trail
3. ✅ **Inventory Reports** - Included in all stock calculations
4. ✅ **Product Card** - Shows in stock summary
5. ⚠️ **General Ledger** - Optional (inventory asset account)

### 8. Validation Rules

#### Frontend Validation
- Warehouse must be selected
- Product must be selected
- Unit must be selected
- Quantity must be > 0
- Cost Price is optional but recommended
- Cannot add duplicate products to the table

#### Backend Validation
- Warehouse must exist and be active
- Product must exist and be active
- Product must have `TrackStock = true`
- Only one opening balance per product per warehouse
- Quantity must be positive
- Unit conversion must be valid
- Transaction rollback on any error

## Files to Create/Modify

### Create:
1. `PointOfSale.Web/src/pages/WarehouseOpeningStock.jsx`
2. `PointOfSale.Infrastructure/Migrations/[timestamp]_AddOpeningBalanceToStockMovement.cs` (if needed)

### Modify:
1. `PointOfSale.Core/Entities/StockMovement.cs` (add `IsOpeningBalance` if missing)
2. `PointOfSale.Core/Enums/StockMovementType.cs` (add `OpeningBalance` if missing)
3. `PointOfSale.API/Controllers/WarehousesController.cs` (add endpoints)
4. `PointOfSale.Web/src/App.jsx` (add route)
5. `PointOfSale.Web/src/components/Sidebar.jsx` (add menu item)
6. `PointOfSale.Web/src/i18n/config.js` (add translations)

## Testing Checklist

### Backend Testing
- [ ] GET endpoint returns products with opening stock status
- [ ] POST endpoint creates stock movements
- [ ] Duplicate opening stock is rejected
- [ ] Unit conversion works correctly
- [ ] WarehouseStock is created/updated
- [ ] Transaction rollback works on error

### Frontend Testing
- [ ] Page loads correctly
- [ ] Warehouse dropdown works
- [ ] Product search/autocomplete works
- [ ] Unit selector shows correct units
- [ ] Quantity and price inputs work
- [ ] Add button adds to table
- [ ] Table displays correctly
- [ ] Delete button removes from table
- [ ] Save button creates opening stock
- [ ] Success/error toasts display
- [ ] Translations work in both languages

### Integration Testing
- [ ] Opening stock appears in Warehouse Stock Balance
- [ ] Opening stock appears in Inventory Valuation Report
- [ ] Opening stock appears in Item Movement Details
- [ ] Opening stock appears in Product Card
- [ ] Stock calculations are correct after opening stock

## Implementation Order

1. ✅ Check and update entities (StockMovement, StockMovementType)
2. ✅ Create database migration if needed
3. ✅ Implement backend API endpoints
4. ✅ Create frontend page
5. ✅ Add routing and navigation
6. ✅ Add translations
7. ✅ Test functionality
8. ✅ Verify integration with reports

## Notes

### Important Considerations
- Opening stock is **permanent** and **cannot be edited**
- Use base unit for all storage
- Cost price is optional but important for inventory valuation
- Each product can have different opening stock in different warehouses
- Opening stock movements are marked with `IsOpeningBalance = true` for filtering

### Security
- Only **Admin** and **Manager** roles can save opening stock
- Opening stock is locked after creation
- Complete audit trail in `StockMovement`

### Data Integrity
- Uses database transactions for atomicity
- Validates warehouse and product existence
- Prevents duplicate opening stock entries
- Ensures unit conversion accuracy

---

*Implementation Plan Created: 2025-12-02*
*Status: Ready to Implement*
