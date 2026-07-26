# Coupon NormalizedCode Implementation

## Summary
Added a persisted `NormalizedCode` column to the `Coupons` table to enable efficient case-insensitive coupon code lookups with database-side uniqueness guarantees. This eliminates the need for in-memory string manipulation (`Code.ToUpper()`) in queries and ensures proper indexing for performance.

## Changes Made

### 1. Entity Model Updates
**File:** `PointOfSale.Core/Entities/Coupon.cs`

- Added `NormalizedCode` property (string, NOT NULL)
- Added `SetCode()` helper method to maintain consistency between `Code` and `NormalizedCode`
- Added XML documentation explaining the purpose and usage

```csharp
/// <summary>
/// Normalized (uppercase) version of Code for case-insensitive lookups.
/// CRITICAL: Must be kept in sync with Code. Set via SetCode() method.
/// Database has unique index on (TenantId, NormalizedCode).
/// </summary>
public string NormalizedCode { get; set; } = string.Empty;

/// <summary>
/// Set the coupon code and automatically update NormalizedCode.
/// Use this method instead of directly setting Code to ensure consistency.
/// </summary>
public void SetCode(string code)
{
    Code = code.Trim().ToUpperInvariant();
    NormalizedCode = Code;
}
```

### 2. Database Migration
**Files:** 
- `add_coupon_normalized_code.sql` - SQL migration script
- `add-coupon-normalized-code.ps1` - PowerShell execution script

**Migration Steps:**
1. Add `NormalizedCode` column (nullable initially)
2. Backfill existing rows: `UPDATE "Coupons" SET "NormalizedCode" = UPPER("Code")`
3. Make `NormalizedCode` NOT NULL
4. Create unique index: `IX_Coupons_TenantId_NormalizedCode` (filtered by `IsDeleted = false`)
5. Create lookup index: `IX_Coupons_NormalizedCode` (filtered by `IsDeleted = false`)

### 3. Controller Updates

#### CouponsController.cs
**Lines Updated:**
- **Line 163** (ValidateCoupon): Changed `c.Code == normalizedCode` to `c.NormalizedCode == normalizedCode`
- **Line 304** (CreateCoupon): Changed `c.Code.ToUpper() == normalizedCode` to `c.NormalizedCode == normalizedCode`
- **Line 316** (CreateCoupon): Added `NormalizedCode = normalizedCode` when creating new coupon
- **Line 379** (UpdateCoupon): Changed `c.Code.ToUpper() == normalizedCode` to `c.NormalizedCode == normalizedCode`
- **Line 395** (UpdateCoupon): Added `coupon.NormalizedCode = normalizedCode` when updating code

#### OrdersController.cs
**Line Updated:**
- **Line 1447**: Changed `entity.Code.ToUpper() == normalizedCode` to `entity.NormalizedCode == normalizedCode`

#### AdminSmartOffersController.cs
**Lines Updated:**
- **Line 374**: Changed `x.Code.ToUpper() == couponCode` to `x.NormalizedCode == normalizedCode`
- **Line 399**: Added `coupon.NormalizedCode = couponCode.ToUpperInvariant()` when creating/updating coupon

### 4. Test Updates
Updated test files to include `NormalizedCode` when creating test coupon entities:

- `PointOfSale.Tests/CouponsControllerCqrsConsistencyTests.cs` (Line 82)
- `PointOfSale.Tests/DiscountValidationHardeningTests.cs` (Line 148)
- `PointOfSale.Tests/CouponUsageServiceCqrsConsistencyTests.cs` (Line 402)

## Benefits

### Performance
- **Indexed lookups**: Database can use the index on `NormalizedCode` directly
- **No function calls in WHERE clause**: Eliminates `Code.ToUpper()` which prevents index usage
- **Faster queries**: Index scans instead of table scans for coupon code lookups

### Data Integrity
- **Unique constraint**: Database enforces uniqueness at the (TenantId, NormalizedCode) level
- **Race condition prevention**: Concurrent coupon creation with same code (different case) is prevented
- **Consistency**: Code normalization happens once at write time, not repeatedly at read time

### Maintainability
- **Clear intent**: `NormalizedCode` column makes the normalization strategy explicit
- **Easier debugging**: Can see normalized values directly in database queries
- **Future-proof**: Easy to add additional normalization rules if needed

## Query Pattern Changes

### Before
```csharp
var coupon = await _context.Coupons
    .FirstOrDefaultAsync(c => 
        c.TenantId == tenantId && 
        c.Code.ToUpper() == normalizedCode);
```

### After
```csharp
var coupon = await _context.Coupons
    .FirstOrDefaultAsync(c => 
        c.TenantId == tenantId && 
        c.NormalizedCode == normalizedCode);
```

## Verification Steps

After running the migration:

1. **Verify column exists:**
   ```sql
   SELECT column_name, data_type, is_nullable 
   FROM information_schema.columns 
   WHERE table_name = 'Coupons' AND column_name = 'NormalizedCode';
   ```

2. **Verify backfill:**
   ```sql
   SELECT COUNT(*) as total, 
          COUNT("NormalizedCode") as with_normalized,
          COUNT(*) - COUNT("NormalizedCode") as missing
   FROM "Coupons";
   ```

3. **Verify unique index:**
   ```sql
   SELECT indexname, indexdef 
   FROM pg_indexes 
   WHERE tablename = 'Coupons' 
   AND indexname LIKE '%NormalizedCode%';
   ```

4. **Check for duplicates (should return 0 rows):**
   ```sql
   SELECT "TenantId", "NormalizedCode", COUNT(*) as count
   FROM "Coupons"
   WHERE "IsDeleted" = false
   GROUP BY "TenantId", "NormalizedCode"
   HAVING COUNT(*) > 1;
   ```

## Rollback Plan

If needed, the migration can be rolled back:

```sql
-- Remove indexes
DROP INDEX IF EXISTS "IX_Coupons_TenantId_NormalizedCode";
DROP INDEX IF EXISTS "IX_Coupons_NormalizedCode";

-- Remove column
ALTER TABLE "Coupons" DROP COLUMN IF EXISTS "NormalizedCode";
```

**Note:** Code changes would also need to be reverted.

## Related Files Not Modified

The following files were reviewed but did not require changes:

- **CouponProjectionConsumer.cs**: Already normalizes code with `ToUpperInvariant()` when projecting to MongoDB
- **CouponDocument.cs**: MongoDB document already stores normalized code
- **CouponEvents.cs**: Events don't need NormalizedCode since normalization happens at entity level
- **PromoService.cs**: Uses PromoCodes table, not Coupons table
- **PaymentMethodsAdminController.cs**: Uses TenantPaymentMethods table, not Coupons table

## Testing Recommendations

1. **Unit Tests**: Verify `SetCode()` method properly sets both Code and NormalizedCode
2. **Integration Tests**: Test coupon creation/update with various case combinations
3. **Performance Tests**: Compare query performance before/after migration
4. **Uniqueness Tests**: Verify database rejects duplicate codes (case-insensitive)

## Migration Execution

Run the migration using the PowerShell script:

```powershell
./add-coupon-normalized-code.ps1 -ConnectionString "your-connection-string"
```

Or set the environment variable and run:

```powershell
$env:ConnectionStrings__DefaultConnection = "your-connection-string"
./add-coupon-normalized-code.ps1
```

## Completion Checklist

- [x] Added NormalizedCode property to Coupon entity
- [x] Created SQL migration script
- [x] Created PowerShell execution script
- [x] Updated all coupon code queries to use NormalizedCode
- [x] Updated coupon creation to set NormalizedCode
- [x] Updated coupon updates to maintain NormalizedCode
- [x] Updated test files to include NormalizedCode
- [x] Documented changes and verification steps
- [ ] Run migration on development database
- [ ] Verify all queries work correctly
- [ ] Run test suite
- [ ] Deploy to staging
- [ ] Deploy to production

## Notes

- The `Code` field is still stored as-is (preserving original case for display purposes)
- The `NormalizedCode` field is always uppercase for consistent lookups
- MongoDB projection already normalizes codes, so no changes needed there
- All queries now use indexed `NormalizedCode` column for better performance
