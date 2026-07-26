# Sales Idempotency Claim Token Fix

## Date
2026-04-08

## Problem
The idempotency helper `TryClaimSalesIdempotencyAsync` in `SalesController.cs` was deleting/stealing any existing in-progress claim, which allowed two concurrent `UpdateSale` requests to both proceed. This created a race condition where:

1. Request A claims the idempotency key
2. Request B sees an in-progress claim and immediately deletes it
3. Request B claims the same key
4. Both requests proceed simultaneously, violating idempotency guarantees

## Solution
Implemented a claim token system with staleness checking to prevent concurrent claim theft:

### 1. Entity Changes (`SalesRequestIdempotency.cs`)
Added two new fields:
- `ClaimToken` (string): Unique token identifying the claim holder
- `ClaimTimestampUtc` (DateTime?): Timestamp when the claim was created

### 2. Claim Logic Changes (`TryClaimSalesIdempotencyAsync`)
- Generates a unique claim token (GUID) for each claim attempt
- Only deletes existing claims if they are older than a configurable staleness cutoff (5 minutes)
- Returns detailed logging with claim token for debugging
- Prevents concurrent requests from stealing active claims

### 3. Staleness Check (`DeleteStaleSalesIdempotencyClaimAsync`)
Replaced `DeletePendingSalesIdempotencyClaimAsync` with a new method that:
- Only deletes claims older than the staleness cutoff
- Uses `COALESCE(ClaimTimestampUtc, CreatedAt)` for backward compatibility
- Prevents deletion of active (non-stale) claims

### 4. Token Validation (`CompleteSalesIdempotencyAsync`)
Added explicit validation before completing:
- Verifies the claim token matches before persisting the response
- Throws an exception if the token doesn't match (claim was stolen)
- Rolls back the transaction if validation fails

### 5. Database Changes
Created migration `20260408120000_AddSalesIdempotencyClaimToken`:
- Adds `ClaimToken` column (varchar(32), nullable)
- Adds `ClaimTimestampUtc` column (timestamp with time zone, nullable)
- Includes both EF Core migration and SQL script

### 6. DbContext Configuration
Updated `ApplicationDbContext.cs` to configure the `ClaimToken` max length (32 characters)

## Key Improvements

1. **Prevents Claim Theft**: Active claims cannot be stolen by concurrent requests
2. **Staleness Detection**: Automatically reclaims abandoned claims after 5 minutes
3. **Token Validation**: Ensures only the claim holder can complete the operation
4. **Backward Compatible**: Uses COALESCE to handle existing records without claim tokens
5. **Better Logging**: Includes claim token in all log messages for debugging

## Configuration
The staleness cutoff is currently hardcoded to 5 minutes in `TryClaimSalesIdempotencyAsync`:
```csharp
const int staleClaimMinutes = 5; // Configurable staleness cutoff
```

This can be moved to configuration if needed.

## Testing Recommendations

1. Test concurrent UpdateSale requests with the same payment attempt ID
2. Verify that only one request proceeds while others receive 409 Conflict
3. Test stale claim reclamation after 5+ minutes
4. Verify token validation prevents claim theft
5. Test backward compatibility with existing records (null ClaimToken)

## Files Modified

1. `PointOfSale.Core/Entities/SalesRequestIdempotency.cs` - Added ClaimToken and ClaimTimestampUtc
2. `PointOfSale.API/Controllers/SalesController.cs` - Updated claim logic with token generation and validation
3. `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` - Added ClaimToken configuration
4. `PointOfSale.Infrastructure/Migrations/20260408120000_AddSalesIdempotencyClaimToken.cs` - EF Core migration
5. `PointOfSale.Infrastructure/Migrations/20260408120000_AddSalesIdempotencyClaimToken.sql` - SQL migration script

## Migration Instructions

Run the migration to add the new columns:
```bash
dotnet ef database update --project PointOfSale.Infrastructure --startup-project PointOfSale.API
```

Or apply the SQL script directly:
```bash
psql -d your_database -f PointOfSale.Infrastructure/Migrations/20260408120000_AddSalesIdempotencyClaimToken.sql
```
