# Cart Owner Validation Optimization

## Issue
In `CartService.ApplyDraftAsync`, the method was performing redundant owner/tenant validation:
1. Called `EnsureOwnerTenantMatchAsync` once at the start (line 872)
2. Called `AddItemAsync` multiple times in a loop (lines 886, 926)
3. Each `AddItemAsync` call triggered another `EnsureOwnerTenantMatchAsync` (line 194)
4. For customer owners, each validation performed an EF Core database lookup to `OnlineCustomers` table

This resulted in N+1 database queries where N is the number of items in the draft.

## Solution
Added an optional `skipOwnerValidation` parameter to `AddItemAsync`:
- Default value: `false` (maintains backward compatibility)
- When `true`: skips the `EnsureOwnerTenantMatchAsync` call
- Allows callers who have already validated the owner to skip redundant validation

## Changes Made

### 1. ICartService.cs
- Added `bool skipOwnerValidation = false` parameter to `AddItemAsync` signature
- Added XML documentation for the new parameter

### 2. CartService.cs
- Updated `AddItemAsync` method signature to include `bool skipOwnerValidation = false`
- Wrapped `EnsureOwnerTenantMatchAsync` call in conditional: `if (!skipOwnerValidation)`
- Updated `ApplyDraftAsync` to pass `skipOwnerValidation: true` to both `AddItemAsync` calls (lines 901, 928)
- Added inline comments explaining why validation is skipped

## Performance Impact
For a draft with N items:
- **Before**: 1 + N database queries (1 in ApplyDraftAsync + N in AddItemAsync calls)
- **After**: 1 database query (only in ApplyDraftAsync)
- **Improvement**: Eliminates N redundant database queries

Example: For a 10-item draft, this reduces database queries from 11 to 1 (90% reduction).

## Security
- Security checks remain intact
- `ApplyDraftAsync` still validates owner/tenant at the start
- Other callers (controllers) continue to validate on each call
- The optimization only applies when the caller explicitly passes `skipOwnerValidation: true`

## Backward Compatibility
- Default parameter value ensures existing callers work without changes
- All existing tests pass
- No breaking changes to the public API

## Testing
- Existing CartService tests pass
- Build completes successfully with no errors
- No changes required to test code

## Files Modified
1. `PointOfSale.API/Services/Cart/ICartService.cs`
2. `PointOfSale.API/Services/Cart/CartService.cs`
