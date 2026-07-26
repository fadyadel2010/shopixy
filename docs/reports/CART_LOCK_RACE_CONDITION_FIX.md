# Cart Lock Race Condition Fix

## Issue Summary

The cart mutation locking mechanism had a critical race condition that allowed concurrent requests to mutate the same cart simultaneously, defeating the purpose of the distributed lock.

## Root Cause

The `ExecuteWithCartMutationLockAsync` method was dynamically choosing between two different lock types:

1. **Owner Lock**: `lock:cart:owner:{tenantId}:{ownerType}:{ownerId}` - Used when no cart exists
2. **Cart-ID Lock**: `lock:checkout:cart:{tenantId}:{cartId}` - Used when a cart exists

### The Race Condition Scenario

1. **Request A** calls `ExecuteWithCartMutationLockAsync` for a new cart
   - No cart exists in Redis
   - Acquires owner lock: `lock:cart:owner:tenant:session:abc123`
   - Starts `ApplyDraftAsync` operation

2. **Request A** creates a cart inside `AddItemInternalAsync`
   - Calls `CreateEmptyCart()` which assigns a new `CartId` (e.g., `xyz789`)
   - Saves cart to Redis with the new CartId

3. **Request B** (concurrent) calls `ExecuteWithCartMutationLockAsync` for the same owner
   - Cart NOW exists in Redis (created by Request A)
   - Tries to acquire cart-id lock: `lock:checkout:cart:tenant:xyz789`
   - **This is a DIFFERENT lock key!**
   - Successfully acquires the lock (no conflict with owner lock)

4. **Both requests now hold different locks but are mutating the same cart** ❌
   - Request A holds: `lock:cart:owner:tenant:session:abc123`
   - Request B holds: `lock:checkout:cart:tenant:xyz789`
   - Lost-update race condition occurs

## The Fix

**Always use the owner lock for cart mutations.** Cart-ID locks should only be used by `CheckoutExecutionLockManager` during checkout operations, where both checkout and cart locks are acquired together.

### Changes Made

1. **`ExecuteWithCartMutationLockAsync`**: Removed dynamic lock resolution. Now always uses `BuildOwnerMutationLockKey(tenantId, owner)` regardless of whether a cart exists.

2. **`ResolveCartMutationLockKeyAsync`**: Simplified to always return the owner lock key, ensuring consistency with `ExecuteWithCartMutationLockAsync`.

### Benefits

- **Stable Lock Identity**: The lock key never changes for a given owner, even when a cart is created mid-operation
- **No Race Window**: Concurrent requests for the same owner will always contend for the same lock
- **Separation of Concerns**: Cart mutations use owner locks; checkout operations use cart-id locks (via `CheckoutExecutionLockManager`)
- **Simpler Logic**: No need to pre-read the cart to determine which lock to use

## Code Changes

### Before (Vulnerable)

```csharp
private async Task<T> ExecuteWithCartMutationLockAsync<T>(...)
{
    var cacheKey = BuildCacheKey(tenantId, owner);
    var currentCart = await GetCartFromRedisAsync(tenantId, owner, cacheKey, correlationId);
    var lockKey = currentCart == null
        ? BuildOwnerMutationLockKey(tenantId, owner)
        : CheckoutExecutionLockManager.BuildCartLockKey(tenantId, currentCart.CartId);
    // Lock key can change between requests!
}
```

### After (Fixed)

```csharp
private async Task<T> ExecuteWithCartMutationLockAsync<T>(...)
{
    // CRITICAL FIX: Always use owner lock for cart mutations
    var lockKey = BuildOwnerMutationLockKey(tenantId, owner);
    // Lock key is stable for all cart mutations
}
```

## Testing Recommendations

1. **Concurrent Cart Creation**: Multiple requests creating carts for the same owner simultaneously
2. **ApplyDraft Concurrency**: Multiple `ApplyDraftAsync` calls for the same session
3. **Add Item During Draft**: Adding items while a draft is being applied
4. **Cart Merge Scenarios**: Merging session and customer carts under concurrent load

## Related Components

- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutExecutionLockManager.cs`
- Redis distributed locking infrastructure

## Impact

- **Security**: Prevents lost updates and data corruption in cart operations
- **Reliability**: Ensures cart mutations are properly serialized
- **Performance**: Minimal impact; removes one Redis read per cart mutation
