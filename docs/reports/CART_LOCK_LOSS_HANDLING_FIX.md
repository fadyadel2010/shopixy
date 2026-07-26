# Cart Lock Loss Handling Fix

## Issue
In `CartService.cs`, when `RefreshLockAsync` returned `!IsAcquired` (lock lost during operation), the code only logged an error but continued executing the protected operation. This created a critical concurrency safety issue where cart mutations could proceed without proper locking.

## Changes Made

### 1. Enhanced CartConcurrencyConflictException
**File**: `PointOfSale.API/Services/Cart/CartService.cs`

Added a new constructor to include detailed diagnostic information:
```csharp
public CartConcurrencyConflictException(string message)
    : base(message)
{
}
```

### 2. Updated RenewLocksPeriodicAsync Method
**File**: `PointOfSale.API/Services/Cart/CartService.cs` (lines ~2350-2420)

**Changes**:
- Added `CancellationTokenSource operationCts` parameter to enable operation cancellation
- When lock loss is detected (`!refreshResult.IsAcquired`):
  - Cancels the operation via `operationCts.Cancel()`
  - Throws `CartConcurrencyConflictException` with detailed context (operation name, lock key, correlation ID)

**Code**:
```csharp
else if (!refreshResult.IsAcquired)
{
    _logger.LogError(
        "Lock renewal failed - lock lost during operation | Operation: {Operation} | LockKey: {LockKey} | CorrelationId: {CorrelationId}",
        operationName,
        lockKey,
        correlationId);
    
    // CRITICAL: Lock lost - abort the protected operation immediately
    operationCts.Cancel();
    throw new CartConcurrencyConflictException(
        $"Lock lost during operation '{operationName}' | LockKey: {lockKey} | CorrelationId: {correlationId}");
}
```

### 3. Updated ExecuteWithRedisLocksAsync Method
**File**: `PointOfSale.API/Services/Cart/CartService.cs` (lines ~2242-2340)

**Changes**:
- Added `operationCts` CancellationTokenSource for operation-level cancellation
- Passed `operationCts` to `RenewLocksPeriodicAsync`
- Added lock loss check after operation completes but before returning result
- Updated finally block to properly propagate `CartConcurrencyConflictException`

**Key additions**:
```csharp
using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

// After operation completes:
if (renewalTask.IsFaulted)
{
    try
    {
        await renewalTask; // This will throw the exception
    }
    catch (CartConcurrencyConflictException)
    {
        throw; // Re-throw to prevent returning stale result
    }
}

// In finally block:
catch (CartConcurrencyConflictException)
{
    // Lock was lost during operation - this exception should propagate
    throw;
}
```

## Behavior

### Before Fix
1. Lock acquired successfully
2. Operation starts
3. Background renewal task detects lock loss
4. **Error logged but operation continues**
5. Operation completes and returns result
6. **Stale/unsafe result returned to caller**

### After Fix
1. Lock acquired successfully
2. Operation starts
3. Background renewal task detects lock loss
4. **Renewal task cancels operation and throws exception**
5. Operation may complete, but result is checked
6. **If lock was lost, exception thrown before returning result**
7. **Caller receives 409 Conflict with detailed error message**

## HTTP Response
When lock is lost, the API returns:
- **Status**: 409 Conflict
- **Error Code**: `CART_CONCURRENCY_CONFLICT`
- **Message**: Includes operation name, lock key, and correlation ID for debugging

Example:
```json
{
  "errorCode": "CART_CONCURRENCY_CONFLICT",
  "message": "Lock lost during operation 'cart_add_item' | LockKey: lock:cart:owner:abc123:customer:xyz789 | CorrelationId: req-12345",
  "traceId": "req-12345"
}
```

## Impact
- **Concurrency Safety**: Prevents cart mutations from completing when lock is lost
- **Data Integrity**: Ensures cart updates are only applied when properly locked
- **Observability**: Detailed error messages include operation context for debugging
- **Client Behavior**: Existing error handling in controllers already returns proper 409 responses

## Testing Recommendations
1. Simulate lock expiration during long-running cart operations
2. Verify 409 response is returned when lock is lost
3. Confirm cart state remains consistent after lock loss
4. Test with multiple concurrent cart mutations
5. Verify correlation IDs are properly logged for debugging
