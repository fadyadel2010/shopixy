# Multi-Unit Support Implementation Plan

## Overview
This document outlines the complete implementation of multi-unit support for products, enabling each product to have multiple units (e.g., Piece, Carton, Box) with conversion factors and unit-specific pricing.

## Implementation Phases

### Phase 1: Database Schema & Entities ✓
1. Create `Unit` entity (master data for units like Piece, Carton, Kg, etc.)
2. Create `ProductUnit` entity (links products to units with conversion & pricing)
3. Update transaction entities to store unit information
4. Create migration scripts

### Phase 2: Backend Services & API ✓
1. Create Unit service & controller
2. Create ProductUnit service & controller
3. Update Purchase/Sales/Returns services to handle units
4. Add validation logic

### Phase 3: Frontend - Product Management ✓
1. Update Product card/form to manage units
2. Add unit selection UI
3. Add conversion factor & pricing inputs

### Phase 4: Frontend - Transactions ✓
1. Update Purchase screen with unit selection
2. Update Sales screen with unit selection
3. Update Purchase Return screen
4. Update Sales Return screen

### Phase 5: Testing & Migration ✓
1. Create unit tests
2. Test transaction flows
3. Create data migration for existing products
4. Documentation

## Database Schema

### Units Table
```sql
CREATE TABLE Units (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    Symbol NVARCHAR(20),
    IsBaseUnit BIT NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL,
    UpdatedAt DATETIME2,
    IsDeleted BIT NOT NULL DEFAULT 0
)
```

### ProductUnits Table
```sql
CREATE TABLE ProductUnits (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId UNIQUEIDENTIFIER NOT NULL,
    UnitId UNIQUEIDENTIFIER NOT NULL,
    ConversionFactor DECIMAL(18,6) NOT NULL,
    PurchasePrice DECIMAL(18,2),
    SellingPrice DECIMAL(18,2),
    IsDefault BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL,
    UpdatedAt DATETIME2,
    IsDeleted BIT NOT NULL DEFAULT 0,
    FOREIGN KEY (ProductId) REFERENCES Products(Id),
    FOREIGN KEY (UnitId) REFERENCES Units(Id)
)
```

### Transaction Entity Updates
Add to PurchaseInvoiceItem, SalesInvoiceItem, ReturnItem, StockMovement:
- `UnitId` (GUID, nullable for backward compatibility)
- `QuantityInUnit` (decimal - quantity as entered in selected unit)
- `ConversionFactor` (decimal - snapshot of conversion at transaction time)
- `BaseQuantity` (decimal - calculated quantity in base units)

## Key Business Rules

1. **Base Unit Requirement**: Each product must have exactly one base unit (ConversionFactor = 1)
2. **Stock Storage**: All stock quantities stored in base units internally
3. **Transaction Recording**: Store both unit quantity and base quantity for audit trail
4. **Price Fallback**: Unit price → Product unit price → Derived from base unit price
5. **Historical Integrity**: Store conversion factor on transaction to preserve history

## API Endpoints

### Units Management
- `GET /api/units` - List all units
- `POST /api/units` - Create new unit
- `PUT /api/units/{id}` - Update unit
- `DELETE /api/units/{id}` - Delete unit

### Product Units
- `GET /api/products/{id}/units` - Get product's configured units
- `POST /api/products/{id}/units` - Add unit to product
- `PUT /api/products/{id}/units/{unitId}` - Update product unit
- `DELETE /api/products/{id}/units/{unitId}` - Remove unit from product

### Transaction Updates
- Purchase/Sales/Returns endpoints accept `unitId` and `quantityInUnit`
- Return calculated `baseQuantity` in response

## Migration Strategy

### For Existing Products
1. Create default "Piece" unit if not exists
2. For each existing product:
   - Create ProductUnit record with:
     - UnitId = Piece unit
     - ConversionFactor = 1
     - PurchasePrice = Product.PurchasePrice
     - SellingPrice = Product.SalePrice
     - IsDefault = true

### For Existing Transactions
- Historical transactions remain unchanged
- New transactions use unit-aware fields
- Reports handle both old and new format

## UI/UX Considerations

### Product Card
- Section for "Units & Pricing"
- Table showing: Unit Name | Conversion | Purchase Price | Selling Price | Default
- Add/Edit/Delete unit buttons
- Visual indicator for base unit

### Transaction Screens
- Unit dropdown next to quantity input
- Show conversion tooltip: "1 Carton = 5 Pieces"
- Display both unit quantity and base quantity
- Auto-fill price based on selected unit

### Stock Display
- Show available stock in selected unit
- Option to view in different units
- Always show base unit for reference

## Testing Checklist

### Unit Tests
- [ ] Conversion factor calculations
- [ ] Base quantity computation
- [ ] Price fallback logic
- [ ] Stock availability checks
- [ ] Validation rules

### Integration Tests
- [ ] Create product with multiple units
- [ ] Purchase in different units
- [ ] Sell in different units
- [ ] Returns in different units
- [ ] Stock transfers with units
- [ ] Inventory valuation accuracy

### Manual Test Scenarios
1. Product with Piece (base) and Carton (1:5)
2. Purchase 2 Cartons @ $50/carton → Stock +10 pieces
3. Sell 3 Pieces @ $12/piece → Stock -3 pieces
4. Sell 1 Carton @ $55/carton → Stock -5 pieces
5. Verify final stock = 2 pieces
6. Verify inventory valuation

## Implementation Timeline

- **Phase 1**: 2-3 hours (Database & Entities)
- **Phase 2**: 3-4 hours (Backend Services & API)
- **Phase 3**: 2-3 hours (Product Management UI)
- **Phase 4**: 4-5 hours (Transaction Screens)
- **Phase 5**: 2-3 hours (Testing & Migration)

**Total Estimated Time**: 13-18 hours

## Rollback Plan

1. Keep migration scripts reversible
2. Maintain backward compatibility in APIs
3. Feature flag for enabling multi-unit support
4. Database backup before migration
5. Ability to revert to single-unit mode

## Success Criteria

- [ ] Products can have multiple units configured
- [ ] Purchases work with any configured unit
- [ ] Sales work with any configured unit
- [ ] Returns work with any configured unit
- [ ] Stock movements correctly convert to base units
- [ ] Inventory reports show accurate quantities
- [ ] Historical data remains intact
- [ ] All existing features continue to work
- [ ] Performance impact < 5%
- [ ] User documentation complete
