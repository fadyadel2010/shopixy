# Security Fix: Logout Refresh Token Revocation

## Issue
**Severity:** 🔴 Critical  
**Component:** POS Authentication (`AuthController.cs`)

The logout endpoint only cleared client-side cookies but did not revoke the persisted refresh token in the database. This meant that a stolen or retained refresh token cookie could still be used to mint new sessions through `POST /api/auth/refresh` after the user logged out.

## Root Cause
The `Logout` method in `PointOfSale.API/Controllers/AuthController.cs` (lines 195-201) only called:
```csharp
AuthCookieConfiguration.ClearSessionCookies(HttpContext, AuthCookieConfiguration.Pos);
```

This cleared the cookies on the client side but left the `User.RefreshToken` and `User.RefreshTokenExpiryTime` fields intact in the database, allowing the refresh token to remain valid.

## Fix Applied
Updated the `Logout` method to:

1. **Extract the refresh token** from the cookie before clearing it
2. **Find the user** with that refresh token in the database
3. **Nullify the refresh token fields** (`RefreshToken` and `RefreshTokenExpiryTime`)
4. **Save changes** to persist the revocation
5. **Log the revocation** for audit purposes
6. **Handle errors gracefully** - logout succeeds even if token revocation fails

### Code Changes
**File:** `PointOfSale.API/Controllers/AuthController.cs`

```csharp
[HttpPost("logout")]
[Authorize]
public async Task<IActionResult> Logout(CancellationToken cancellationToken)
{
    // SECURITY: Revoke the persisted refresh token to prevent session reuse
    if (Request.Cookies.TryGetValue(AuthCookieConfiguration.PosRefreshTokenCookieName, out var refreshToken) &&
        !string.IsNullOrWhiteSpace(refreshToken))
    {
        try
        {
            // Find and revoke the refresh token in the database
            var user = await _context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.RefreshToken == refreshToken && !u.IsDeleted, cancellationToken);

            if (user != null)
            {
                user.RefreshToken = null;
                user.RefreshTokenExpiryTime = null;
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Refresh token revoked on logout | UserId: {UserId} | TenantId: {TenantId}",
                    user.Id,
                    user.TenantId);
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail logout if token revocation fails
            _logger.LogError(
                ex,
                "Failed to revoke refresh token on logout | Token: {TokenPrefix}",
                refreshToken[..Math.Min(10, refreshToken.Length)]);
        }
    }

    AuthCookieConfiguration.ClearSessionCookies(HttpContext, AuthCookieConfiguration.Pos);

    return Ok(new { message = "Logged out successfully" });
}
```

## Security Impact

### Before Fix
- ❌ Logout cleared cookies only
- ❌ Refresh token remained valid in database
- ❌ Stolen refresh token could mint new sessions after logout
- ❌ No server-side session invalidation

### After Fix
- ✅ Logout clears cookies AND revokes database token
- ✅ Refresh token is nullified in database
- ✅ Stolen refresh token cannot mint new sessions after logout
- ✅ Complete server-side session invalidation
- ✅ Audit logging for token revocation

## Verification Status

### Other Auth Controllers
Checked other authentication controllers for similar issues:

1. **✅ AffiliateAuthController** - Already implements proper token revocation
2. **✅ StorefrontAuthController** - Already implements proper token revocation using `IStorefrontRefreshTokenService`
3. **✅ SuperAdminAuthController** - Delegates to `_authService.LogoutAsync()` which handles revocation

Only the POS `AuthController` had this vulnerability.

## Testing Recommendations

1. **Test logout revocation:**
   - Login to POS
   - Capture the refresh token cookie
   - Logout
   - Attempt to use the captured refresh token with `POST /api/auth/refresh`
   - Expected: Should return 401 Unauthorized

2. **Test error handling:**
   - Simulate database failure during logout
   - Expected: Logout should still succeed (cookies cleared) but error logged

3. **Test audit logging:**
   - Logout successfully
   - Verify log entry contains: "Refresh token revoked on logout | UserId: {id} | TenantId: {id}"

## Related Security Considerations

- The fix uses `IgnoreQueryFilters()` to find the user, which is correct since we need to revoke tokens even if tenant context is not established
- Error handling ensures logout always succeeds from the user's perspective (cookies cleared) even if database revocation fails
- Logging includes only a prefix of the token (first 10 chars) to avoid logging sensitive data in error cases
