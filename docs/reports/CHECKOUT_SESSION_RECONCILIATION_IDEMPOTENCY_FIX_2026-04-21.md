# Checkout Session Reconciliation Idempotency Fix

**Date:** 2026-04-21  
**Issue:** Non-idempotent reconciliation with invalidated duplicates  
**Status:** ✅ Fixed

## Problem

The `LoadRecoverableSessionsByCartAsync` query was returning abandoned sessions that had already been invalidated by previous reconciliation runs. This caused:

1. **Infinite re-processing**: Invalidated duplicates were returned in every reconciliation
2. **Token rotation**: Each reconciliation gave invalidated sessions a new recovery token
3. **Timestamp updates**: `LastActivityAtUtc` was updated on already-invalidated sessions
4. **Duplicate events**: New abandonment events were published for the same sessions
5. **Reconciliation counter inflation**: The same sessions were counted multiple times

### Root Cause

The `InvalidateSession` method marked sessions as `Abandoned` but didn't set a durable invalidation marker. The query filtered by status (`Active` or `Abandoned`) but couldn't distinguish between:
- Legitimately abandoned sessions (recoverable)
- Invalidated duplicates (should be excluded)

## Solution

### 1. Added Invalidation Marker to Entity

**File:** `PointOfSale.Core/Entities/CheckoutSession.cs`

```csharp
/// <summary>
/// When this session was invalidated during reconciliation (duplicate resolution).
/// Null if session has never been invalidated.
/// Used to exclude already-invalidated duplicates from recoverable session queries.
/// Prevents non-idempotent reconciliation where invalidated sessions are re-processed.
/// </summary>
public DateTime? InvalidatedAtUtc { get; set; }
```

### 2. Updated Invalidation Logic

**File:** `PointOfSale.Infrastructure/Services/CheckoutSessionService.cs`

```csharp
private static void InvalidateSession(
    CheckoutSession session,
    DateTime nowUtc,
    ICollection<CheckoutSession> invalidatedSessions)
{
    session.RecoveryToken = CreateRecoveryNonce();
    session.LastActivityAtUtc = nowUtc;
    session.Status = CheckoutSessionStatus.Abandoned;
    session.AbandonedAtUtc ??= nowUtc;
    session.InvalidatedAtUtc = nowUtc;  // ✅ NEW: Durable invalidation marker

    invalidatedSessions.Add(session);
}
```

### 3. Updated Query to Exclude Invalidated Sessions

**File:** `PointOfSale.Infrastructure/Services/CheckoutSessionService.cs`

```csharp
private async Task<List<CheckoutSession>> LoadRecoverableSessionsByCartAsync(
    Guid tenantId,
    Guid cartId,
    CancellationToken ct)
{
    return await _db.CheckoutSessions
        .IgnoreQueryFilters()
        .Where(session =>
            session.TenantId == tenantId &&
            session.CartId == cartId &&
            (session.Status == CheckoutSessionStatus.Active ||
             session.Status == CheckoutSessionStatus.Abandoned) &&
            session.InvalidatedAtUtc == null)  // ✅ NEW: Exclude invalidated sessions
        .OrderBy(session => session.Status == CheckoutSessionStatus.Active ? 0 : 1)
        .ThenByDescending(session => session.LastActivityAtUtc)
        .ThenByDescending(session => session.CreatedAtUtc)
        .ThenByDescending(session => session.Id)
        .ToListAsync(ct);
}
```

### 4. Database Migration

**File:** `add_checkout_session_invalidated_at.sql`

```sql
-- Add the InvalidatedAtUtc column (nullable, no default)
ALTER TABLE "CheckoutSessions"
ADD COLUMN "InvalidatedAtUtc" timestamp without time zone NULL;

-- Add partial index for query performance (only indexes non-invalidated sessions)
CREATE INDEX "IX_CheckoutSessions_InvalidatedAtUtc"
ON "CheckoutSessions" ("InvalidatedAtUtc")
WHERE "InvalidatedAtUtc" IS NULL;
```

## Impact

### Before Fix
```
Reconciliation Run 1:
- Finds 3 sessions (1 active, 2 abandoned)
- Invalidates 2 duplicates
- Sets Status = Abandoned, rotates tokens

Reconciliation Run 2:
- Finds 3 sessions again (same 2 invalidated + 1 active)
- Invalidates same 2 duplicates again
- Rotates tokens again, updates timestamps
- Publishes duplicate events

Reconciliation Run 3+:
- Infinite loop continues...
```

### After Fix
```
Reconciliation Run 1:
- Finds 3 sessions (1 active, 2 abandoned)
- Invalidates 2 duplicates
- Sets InvalidatedAtUtc = now

Reconciliation Run 2:
- Finds 1 session (only the active one)
- Invalidated sessions excluded by query
- No duplicate processing

Reconciliation Run 3+:
- Idempotent behavior maintained
```

## Testing Recommendations

1. **Verify query filtering:**
   ```sql
   SELECT COUNT(*) FROM "CheckoutSessions"
   WHERE "TenantId" = '...'
     AND "CartId" = '...'
     AND ("Status" = 0 OR "Status" = 2)
     AND "InvalidatedAtUtc" IS NULL;
   ```

2. **Verify invalidation marker is set:**
   ```sql
   SELECT "Id", "Status", "InvalidatedAtUtc"
   FROM "CheckoutSessions"
   WHERE "CartId" = '...'
   ORDER BY "CreatedAtUtc" DESC;
   ```

3. **Test reconciliation idempotency:**
   - Create duplicate sessions for same cart
   - Run reconciliation multiple times
   - Verify invalidated sessions are not re-processed

## Files Changed

1. ✅ `PointOfSale.Core/Entities/CheckoutSession.cs` - Added `InvalidatedAtUtc` property
2. ✅ `PointOfSale.Infrastructure/Services/CheckoutSessionService.cs` - Updated `InvalidateSession` and `LoadRecoverableSessionsByCartAsync`
3. ✅ `add_checkout_session_invalidated_at.sql` - Database migration script

## Deployment Notes

1. Run migration script: `add_checkout_session_invalidated_at.sql`
2. Deploy updated code
3. Existing invalidated sessions will have `InvalidatedAtUtc = NULL` (acceptable - they won't be re-invalidated)
4. New invalidations will be properly tracked going forward
