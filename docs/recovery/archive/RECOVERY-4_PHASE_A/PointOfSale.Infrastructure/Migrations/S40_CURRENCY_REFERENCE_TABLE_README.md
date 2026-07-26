# S40: Currency Reference Table Migration

## Overview
This migration replaces the weak regex-based check constraint on `Tenants.Currency` with a proper ISO 4217 currency reference table and foreign key constraint.

## Problem
The previous implementation used a check constraint:
```sql
"Currency" ~ '^[A-Z]{3}$'
```

This only validated the format (3 uppercase letters) but allowed invalid codes like "AAA", "ZZZ", etc.

## Solution
1. **Created `Currencies` reference table** with valid ISO 4217 currency codes
2. **Populated with 80+ currencies** covering major world currencies including:
   - Major currencies (USD, EUR, GBP, JPY, etc.)
   - Middle East & North Africa (EGP, SAR, AED, etc.)
   - Asia Pacific (CNY, INR, SGD, etc.)
   - Europe (SEK, NOK, PLN, etc.)
   - Americas (MXN, BRL, ARS, etc.)
   - Africa (ZAR, NGN, KES, etc.)

3. **Added foreign key constraint** from `Tenants.Currency` to `Currencies.Code`
4. **Updated model configuration** to use FK instead of check constraint

## Database Changes

### New Table: Currencies
```sql
CREATE TABLE "Currencies" (
    "Code" VARCHAR(3) PRIMARY KEY,
    "Name" VARCHAR(100) NOT NULL,
    "Symbol" VARCHAR(10) NOT NULL,
    "DecimalPlaces" INTEGER NOT NULL DEFAULT 2
);
```

### Modified Table: Tenants
- Dropped: `CK_Tenants_Currency_IsoCode` check constraint
- Added: `FK_Tenants_Currencies_Currency` foreign key constraint
- Added: `IX_Tenants_Currency` index for FK lookups

## Code Changes

### New Entity
- `PointOfSale.Core/Entities/Currency.cs`

### Updated Files
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
  - Added `Currencies` DbSet
  - Added Currency entity configuration
  - Updated Tenant configuration with FK relationship

- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
  - Added Currency entity definition
  - Removed check constraint from Tenant
  - Added FK relationship

## Migration Safety

### Data Protection
- Migration automatically updates any invalid currency codes to "USD" before adding FK
- Verification checks ensure all tenants have valid currencies after migration

### Rollback Considerations
- If rollback is needed, the check constraint can be re-added
- Currency reference table can be dropped
- No data loss occurs during rollback

## Benefits

1. **Data Integrity**: Only valid ISO 4217 codes can be stored
2. **Referential Integrity**: Database enforces valid currency codes
3. **Extensibility**: Easy to add new currencies by inserting into reference table
4. **Performance**: Indexed FK lookups are efficient
5. **Documentation**: Currency names and symbols are now stored in database
6. **Consistency**: Single source of truth for valid currencies

## Usage

### Adding New Currencies
```sql
INSERT INTO "Currencies" ("Code", "Name", "Symbol", "DecimalPlaces") 
VALUES ('XXX', 'New Currency', 'X', 2);
```

### Querying Valid Currencies
```csharp
var currencies = await _context.Currencies.ToListAsync();
```

### Tenant Currency Validation
Currency validation now happens automatically at the database level via FK constraint.

## Testing
- Verify all existing tenants have valid currencies
- Test that invalid currency codes are rejected
- Verify FK constraint prevents deletion of currencies in use
- Test currency symbol lookups

## Related Changes
- S40: Added validation attributes to `SettingsDtos.CurrencyCode`
- S40: Added XML documentation for currency properties
