# Storefront Cart Controller Authentication Fix

**Date:** 2026-04-08  
**Status:** ✅ COMPLETED  
**Security Level:** HIGH PRIORITY

## Problem Identified

The `StorefrontCartController` had a critical security vulnerability:

1. **[AllowAnonymous] attribute** - Allowed unauthenticated access to all cart endpoints
2. **Header-based customer ID** - Trusted the `X-Customer-Id` header without verification
3. **Impersonation risk** - Callers could impersonate any customer by setting the header

This violated the principle of least privilege and created a significant security risk where any user could access or modify another customer's cart by simply changing the `X-Customer-Id` header value.

## Solution Implemented

### 1. Removed [AllowAnonymous] Attribute

**Before:**
```csharp
[ApiController]
[Route("api/storefront/cart")]
[AllowAnonymous]
public class StorefrontCartController : ControllerBase
```

**After:**
```csharp
[ApiController]
[Route("api/storefront/cart")]
[Authorize(AuthenticationSchemes = "StorefrontJwt")]
public class StorefrontCartController : ControllerBase
```

### 2. Updated ValidateHeaders Method

The method now:

1. **Extracts customer ID from authenticated claims** (CustomerId claim)
2. **Rejects requests without valid authentication**
3. **Optionally verifies X-Customer-Id header** if present (must match claim)
4. **Verifies tenant ID** from claim matches header
5. **Always uses authenticated customer ID** for cart operations

**Key Changes:**

```csharp
// Step 2: Extract customer ID from authenticated claims
var customerIdClaim = User.FindFirst("CustomerId")?.Value;

if (string.IsNullOrWhiteSpace(customerIdClaim) || !Guid.TryParse(customerIdClaim, out var customerId))
{
    return (null, null, Unauthorized(new ApiErrorResponse(
        "INVALID_TOKEN",
        "Authentication token is missing or invalid customer ID claim",
        traceId)));
}

// Step 3: Optional verification - if X-Customer-Id header is present, it must match
if (Request.Headers.TryGetValue(CustomerIdHeader, out var customerHeaderValue) &&
    !string.IsNullOrWhiteSpace(customerHeaderValue))
{
    if (!Guid.TryParse(customerHeaderValue, out var headerCustomerId) || headerCustomerId != customerId)
    {
        return (null, null, BadRequest(new ApiErrorResponse(
            "CUSTOMER_ID_MISMATCH",
            "X-Customer-Id header does not match authenticated customer",
            traceId)));
    }
}
```

### 3. Removed Session Cart Support

Since the controller now requires authentication, the session-based cart logic (using `X-Session-Id` header) has been removed. All carts are now customer-specific and require authentication.

**Removed:**
- `X-Session-Id` header constant
- Session cart logic in ValidateHeaders
- Guest cart support

## Security Improvements

1. **Authentication Required** - All cart operations now require a valid StorefrontJwt token
2. **Claim-Based Identity** - Customer ID is derived from authenticated claims, not headers
3. **Header Verification** - X-Customer-Id header (if present) is validated against the claim
4. **Tenant Verification** - Tenant ID from claim is verified against the header
5. **No Impersonation** - Users cannot access other customers' carts by manipulating headers

## Alignment with Other Controllers

This fix aligns StorefrontCartController with other authenticated storefront controllers:

- `StorefrontAccountController` - Uses `[Authorize(AuthenticationSchemes = "StorefrontJwt")]`
- `StorefrontWishlistController` - Uses `[Authorize(AuthenticationSchemes = "StorefrontJwt")]`
- `StorefrontOrdersReadController` - Uses `[Authorize(AuthenticationSchemes = "StorefrontJwt")]`
- `StorefrontOnlineCustomerController` - Uses `[Authorize(AuthenticationSchemes = "StorefrontJwt")]`

All these controllers extract customer ID from the `CustomerId` claim using a similar pattern.

## Breaking Changes

⚠️ **Important:** This is a breaking change for any clients that:

1. Call cart endpoints without authentication
2. Rely on session-based (guest) carts
3. Use X-Customer-Id header for customer identification

**Migration Required:**
- Clients must obtain a StorefrontJwt token before accessing cart endpoints
- Guest cart functionality needs to be reimplemented if required (separate endpoint or different approach)

## Testing Recommendations

1. **Authenticated Access** - Verify cart operations work with valid StorefrontJwt token
2. **Unauthenticated Rejection** - Verify 401 Unauthorized for requests without token
3. **Header Mismatch** - Verify 400 Bad Request when X-Customer-Id doesn't match claim
4. **Tenant Mismatch** - Verify 400 Bad Request when X-Tenant-Id doesn't match claim
5. **Cross-Customer Access** - Verify users cannot access other customers' carts

## Files Modified

- `PointOfSale.API/Controllers/StorefrontCartController.cs`
  - Removed `[AllowAnonymous]` attribute (line 45)
  - Added `[Authorize(AuthenticationSchemes = "StorefrontJwt")]` attribute
  - Removed `SessionIdHeader` constant
  - Updated `ValidateHeaders` method to use claim-based authentication
  - Removed session cart logic

## Verification

✅ No compilation errors  
✅ Follows established authentication patterns  
✅ Aligns with other storefront controllers  
✅ Prevents customer impersonation  
✅ Validates both customer ID and tenant ID from claims
