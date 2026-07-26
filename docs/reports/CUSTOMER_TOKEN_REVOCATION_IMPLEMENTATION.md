# Customer Access Token Revocation Implementation

## Overview

Implemented server-side token revocation for CommerceHub customer access tokens to prevent replay attacks after logout. Previously, logout only cleared the browser cookie, leaving the access token valid until expiration.

## Security Issue Fixed

**Problem**: Customer logout only deleted the client-side cookie. The access token remained valid and could be replayed from another client until its natural expiration time.

**Solution**: Server-side token blacklist that tracks revoked tokens by their JWT ID (jti claim) and validates tokens during authentication.

## Implementation Components

### 1. Database Entity
**File**: `PointOfSale.Core/Entities/RevokedCustomerAccessToken.cs`

Tracks revoked tokens with:
- `Jti`: JWT ID from the token (unique identifier)
- `CustomerId`: Associated customer
- `RevokedAt`: When the token was revoked
- `ExpiresAt`: Natural expiration time (for cleanup)
- `Reason`: Optional revocation reason (e.g., "logout", "security_incident")

### 2. Database Migration
**Files**: 
- `add_revoked_customer_access_tokens.sql`
- `add-revoked-customer-access-tokens.ps1`

Creates the `RevokedCustomerAccessTokens` table with:
- Unique index on `Jti` for fast lookup during validation
- Index on `CustomerId` for customer-specific queries
- Index on `ExpiresAt` for cleanup operations
- Foreign key to `CommerceHubCustomers` with cascade delete

**Run migration**:
```powershell
.\add-revoked-customer-access-tokens.ps1
```

### 3. Token Revocation Service
**File**: `PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs`

**Interface**: `ICustomerTokenRevocationService`

**Methods**:
- `RevokeTokenAsync(string accessToken, string reason)`: Adds token to blacklist
- `IsTokenRevokedAsync(string jti)`: Checks if token is revoked
- `CleanupExpiredTokensAsync()`: Removes expired tokens from blacklist

**Features**:
- Parses JWT to extract `jti` and `CustomerId` claims
- Prevents duplicate revocations
- Graceful error handling (doesn't block logout on failure)
- Comprehensive logging

### 4. JWT Authentication Integration
**File**: `PointOfSale.API/Program.cs`

Added `OnTokenValidated` event handler to customer JWT authentication:
```csharp
OnTokenValidated = async context =>
{
    var jtiClaim = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
    if (!string.IsNullOrWhiteSpace(jtiClaim))
    {
        var revocationService = context.HttpContext.RequestServices
            .GetRequiredService<ICustomerTokenRevocationService>();
        
        if (await revocationService.IsTokenRevokedAsync(jtiClaim))
        {
            context.Fail("Token has been revoked");
            return;
        }
    }
}
```

### 5. Logout Controller Update
**File**: `PointOfSale.API/Controllers/CustomersAuthController.cs`

Updated `Logout` method to:
1. Read access token from cookie
2. Revoke token server-side via `ICustomerTokenRevocationService`
3. Delete cookie (existing behavior)

```csharp
[HttpPost("logout")]
[AllowAnonymous]
public async Task<IActionResult> Logout(CancellationToken cancellationToken)
{
    // SECURITY: Revoke the access token server-side to prevent replay attacks
    if (Request.Cookies.TryGetValue(
        AuthCookieConfiguration.CommerceHubCustomerAccessTokenCookieName,
        out var accessToken) &&
        !string.IsNullOrWhiteSpace(accessToken))
    {
        await _tokenRevocationService.RevokeTokenAsync(accessToken, "logout", cancellationToken);
    }

    Response.Cookies.Delete(
        AuthCookieConfiguration.CommerceHubCustomerAccessTokenCookieName,
        AuthCookieConfiguration.CreateExpiredOptions("/api/customers"));

    return Ok(new { message = "Logged out successfully." });
}
```

### 6. Background Cleanup Service
**File**: `PointOfSale.API/Services/CommerceHub/RevokedTokenCleanupService.cs`

Runs every hour to remove expired tokens from the blacklist:
- Waits 5 minutes after startup before first cleanup
- Removes tokens where `ExpiresAt < DateTime.UtcNow`
- Logs cleanup operations for observability

### 7. Service Registration
**File**: `PointOfSale.API/Program.cs`

Registered services:
```csharp
builder.Services.AddScoped<ICustomerTokenRevocationService, CustomerTokenRevocationService>();
builder.Services.AddHostedService<RevokedTokenCleanupService>();
```

## Security Flow

### Login
1. Customer logs in
2. JWT generated with unique `jti` claim (Guid.NewGuid())
3. Token stored in HTTP-only cookie
4. Token valid until `ExpiresAt`

### Authenticated Request
1. Token extracted from cookie
2. JWT signature and expiration validated
3. **NEW**: `jti` checked against revocation blacklist
4. If revoked: authentication fails with "Token has been revoked"
5. If not revoked: request proceeds

### Logout
1. Token read from cookie
2. **NEW**: Token's `jti` added to revocation blacklist
3. Cookie deleted
4. Token now invalid for all future requests

### Cleanup
1. Background service runs hourly
2. Removes tokens where `ExpiresAt < DateTime.UtcNow`
3. Keeps blacklist size minimal

## Performance Considerations

### Database Indexes
- **Unique index on `Jti`**: O(log n) lookup during every authenticated request
- **Index on `ExpiresAt`**: Efficient cleanup queries
- **Index on `CustomerId`**: Fast customer-specific queries

### Blacklist Growth
- Tokens removed after natural expiration
- Typical token lifetime: 60-120 minutes
- Cleanup runs hourly
- Expected blacklist size: ~1-2 hours of logout events

### Query Performance
```sql
-- Fast lookup during authentication (uses unique index)
SELECT 1 FROM RevokedCustomerAccessTokens WHERE Jti = @jti

-- Efficient cleanup (uses ExpiresAt index)
DELETE FROM RevokedCustomerAccessTokens WHERE ExpiresAt < GETUTCDATE()
```

## Testing

### Manual Testing
1. **Login**: POST `/api/customers/auth/login`
2. **Verify authenticated**: GET `/api/customers/auth/me` (should succeed)
3. **Logout**: POST `/api/customers/auth/logout`
4. **Verify revoked**: GET `/api/customers/auth/me` (should fail with 401)
5. **Check database**: Query `RevokedCustomerAccessTokens` table

### Database Verification
```sql
-- Check revoked tokens
SELECT * FROM RevokedCustomerAccessTokens
ORDER BY RevokedAt DESC;

-- Verify token is revoked
SELECT * FROM RevokedCustomerAccessTokens
WHERE Jti = '<jti-from-token>';

-- Check cleanup (should be empty after ExpiresAt)
SELECT * FROM RevokedCustomerAccessTokens
WHERE ExpiresAt < GETUTCDATE();
```

### Logs to Monitor
```
// Successful revocation
Customer access token revoked | CustomerId: {guid} | Jti: {guid} | Reason: logout

// Token validation failure
CustomerJwt Authentication Failed: Token has been revoked | Path: /api/customers/auth/me

// Cleanup
Cleaned up {count} expired revoked customer access tokens
```

## Deployment Checklist

1. ✅ Run database migration: `.\add-revoked-customer-access-tokens.ps1`
2. ✅ Deploy updated API code
3. ✅ Restart API to load new services
4. ✅ Test logout flow
5. ✅ Verify revoked tokens are rejected
6. ✅ Monitor cleanup service logs
7. ✅ Check database growth over time

## Monitoring

### Key Metrics
- Revoked tokens per hour
- Blacklist size
- Cleanup operation duration
- Authentication failures due to revocation

### Health Checks
- Verify cleanup service is running
- Monitor blacklist growth rate
- Alert on excessive revocation failures

## Future Enhancements

### Optional Improvements
1. **Redis Cache**: Cache revoked JTIs in Redis for faster lookups
2. **Bulk Revocation**: Revoke all tokens for a customer (security incident)
3. **Revocation Audit**: Track who/what triggered revocation
4. **Metrics**: Expose revocation metrics via Prometheus/OpenTelemetry
5. **Admin API**: Allow admins to revoke customer tokens

### Example: Revoke All Customer Tokens
```csharp
public async Task RevokeAllCustomerTokensAsync(Guid customerId, string reason)
{
    // Would require tracking all active tokens per customer
    // Or implementing a "revoked after" timestamp per customer
}
```

## Related Security Fixes

This implementation complements:
- **Refresh Token Revocation**: OnlineCustomer refresh tokens already have revocation
- **POS User Logout**: POS users revoke refresh tokens on logout
- **Super Admin Logout**: Super admin tokens use version-based invalidation

## References

- JWT Best Practices: https://datatracker.ietf.org/doc/html/rfc8725
- Token Revocation: https://datatracker.ietf.org/doc/html/rfc7009
- OWASP Token Handling: https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html

## Summary

Customer access tokens are now properly revoked on logout, preventing replay attacks. The implementation uses a database-backed blacklist with efficient indexing, automatic cleanup, and comprehensive logging. The solution is production-ready and follows security best practices.
