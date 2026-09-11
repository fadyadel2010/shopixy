namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSessionHijackingCertificationTests
{
    [Fact]
    public void CustomerAuthCookie_MustRemainHttpOnlyStrictScopedAndClearedOnSecurityEvents()
    {
        var cookieConfig = ReadRepoFile("PointOfSale.API/Security/AuthCookieConfiguration.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var orderAccessCookie = ReadRepoFile("PointOfSale.API/Security/CommerceHubOrderAccessCookie.cs");

        var httpOnlyOptions = Slice(cookieConfig, "public static CookieOptions CreateHttpOnlyOptions", "public static CookieOptions CreateExpiredOptions");
        AssertContains(httpOnlyOptions,
            "HttpOnly = true",
            "Secure = true",
            "SameSite = SameSiteMode.Strict",
            "MaxAge = lifetime",
            "Path = path",
            "IsEssential = true");

        AssertContains(cookieConfig,
            "public static CookieOptions CreateExpiredOptions",
            "HttpOnly = true",
            "Secure = true",
            "SameSite = SameSiteMode.Strict",
            "Expires = DateTimeOffset.UnixEpoch",
            "Path = path",
            "IsEssential = true");

        var setCookie = Slice(controller, "private void SetCustomerAccessCookie", "private void ClearCommerceHubRecoveryCookie()");
        AssertContains(setCookie,
            "CommerceHubCustomerAccessTokenCookieName",
            "CreateHttpOnlyOptions",
            "CustomerApiCookiePath",
            "CommerceHubApiCookiePath",
            "Response.Cookies.Delete",
            "CreateExpiredOptions(path)",
            "AuthCookieConfiguration.EnsureCsrfCookie(HttpContext)");

        AssertSecurityEventClearsCustomerCookie(controller, "[HttpPost(\"logout\")]", "[HttpPost(\"revoke-all-sessions\")]");
        AssertSecurityEventClearsCustomerCookie(controller, "[HttpPost(\"revoke-all-sessions\")]", "[HttpPost(\"forgot-password\")]");
        AssertSecurityEventClearsCustomerCookie(controller, "[HttpPost(\"reset-password\")]", "[HttpPost(\"verify-email\")]");
        AssertSecurityEventClearsCustomerCookie(controller, "[HttpPost(\"change-password\")]", "[HttpPost(\"change-email\")]");
        AssertSecurityEventClearsCustomerCookie(controller, "[HttpPost(\"change-email\")]", "private Guid? ResolveCustomerId()");

        AssertContains(orderAccessCookie,
            "public const string CookieName = \"commercehub_order_access\"",
            "HttpOnly = true",
            "Secure = !allowInsecureDevelopmentCookie",
            "SameSite = SameSiteMode.Strict",
            "Path = CookiePath",
            "ClearOrderAccessToken");
    }

    [Fact]
    public void CustomerStolenJwt_MustDieAfterLogoutRevokeAllPasswordResetPasswordChangeAndEmailChange()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var revocation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        var tokenGeneration = Slice(service, "private string GenerateToken", "private static CustomerProfileDto MapProfile");
        AssertContains(tokenGeneration,
            "JwtRegisteredClaimNames.Jti",
            "Guid.NewGuid().ToString()",
            "CustomerSecurityConstants.SessionIssuedAtTicksClaim",
            "issuedAtUtc.Ticks.ToString()");

        var revokeAll = Slice(revocation, "public async Task<DateTime> RevokeAllTokensAsync", "public async Task CleanupExpiredTokensAsync");
        AssertContains(revokeAll,
            "customer.RevokeAllSessions(cutoffUtc)",
            "await _dbContext.SaveChangesAsync(cancellationToken)",
            "TokenRevocationPersistenceException");

        var sessionCutoff = Slice(revocation, "public async Task EnsureTokenSessionStillValidAsync", "public async Task<DateTime> RevokeAllTokensAsync");
        AssertContains(sessionCutoff,
            "TokensValidAfterUtc",
            "ResolveTokenIssuedAtUtc(principal)",
            "tokenIssuedAtUtc.Value <= authority.TokensValidAfterUtc.Value",
            "CustomerTokenSessionRevokedException");

        var jwtScheme = Slice(program, ".AddJwtBearer(PointOfSale.API.Security.CustomerSecurityConstants.AuthenticationScheme", "// Use [Authorize(AuthenticationSchemes = \"AffiliateJwt\")]");
        AssertContains(jwtScheme,
            "CommerceHubCustomerAccessTokenCookieName",
            "JwtBearerRoutingHelper.TryReadIssuer(token, out var issuer)",
            "!string.Equals(issuer, customerJwtSettings.Issuer, StringComparison.Ordinal)",
            "JwtRegisteredClaimNames.Jti",
            "IsTokenRevokedAsync(jtiClaim",
            "EnsureTokenSessionStillValidAsync",
            "EnsureCustomerLoginAllowedAsync(customerId",
            "TokenRevocationCheckUnavailableException",
            "context.Fail(\"Token revocation status could not be verified.\")",
            "ClockSkew = TimeSpan.Zero");

        var logout = Slice(controller, "[HttpPost(\"logout\")]", "[HttpPost(\"revoke-all-sessions\")]");
        AssertContains(logout,
            "RevokeTokenAsync(accessToken, \"logout\"",
            "RevokeAllTokensAsync(",
            "logout_blacklist_persistence_fallback",
            "ClearCustomerAccessCookies()");

        AssertContains(Slice(service, "public async Task<CommerceHubCustomerAuthResult> ResetPasswordAsync", "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync"),
            "customer.RevokeAllSessions(now)",
            "PasswordResetCompleted");
        AssertContains(Slice(service, "public async Task<CommerceHubCustomerAuthResult> ChangePasswordAsync", "public async Task<CommerceHubCustomerAuthResult> ChangeEmailAsync"),
            "customer.RevokeAllSessions(DateTime.UtcNow)",
            "PasswordChanged");
        AssertContains(Slice(service, "public async Task<CommerceHubCustomerAuthResult> ChangeEmailAsync", "private async Task InvalidateEmailVerificationTokensAsync"),
            "customer.RevokeAllSessions(now)",
            "customer.MarkEmailUnverified()",
            "EmailChanged");
    }

    [Fact]
    public void CommerceHubFrontend_MustNotStoreCustomerBearerTokens()
    {
        var httpClient = ReadRepoFile("PointOfSale.CommerceHub/src/api/http-client.ts");
        var authStore = ReadRepoFile("PointOfSale.CommerceHub/src/store/customer-auth-store.ts");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");

        Assert.Contains("withCredentials: true", httpClient, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", httpClient, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", authStore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", authStore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", authStore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clearActivePaymentRecoveryContext()", authStore, StringComparison.Ordinal);

        var browserResponse = Slice(controller, "private static CustomerAuthResponseDto CreateBrowserAuthResponse", "private string GetTraceId()");
        Assert.Contains("AccessToken = string.Empty", browserResponse, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCustomerSessions_MustRemainAccessTokenOnlyUntilRefreshIsCertified()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var authService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var cookieConfig = ReadRepoFile("PointOfSale.API/Security/AuthCookieConfiguration.cs");

        Assert.DoesNotContain("refresh-token", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshToken", authService, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubCustomerRefresh", cookieConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("commercehub_customer_refresh", cookieConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivilegedStolenAuthority_MustBeBoundToCurrentSqlSessionAndRotationState()
    {
        var auth = ReadRepoFile("PointOfSale.API/Controllers/AuthController.cs");
        var posAuthority = ReadRepoFile("PointOfSale.API/Security/PosSessionAuthority.cs");
        var superAdminAuthority = ReadRepoFile("PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminPlatformAuthority.cs");
        var superAdminAuth = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminAuthService.cs");
        var tenantLineage = ReadRepoFile("PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs");
        var governance = ReadRepoFile("PointOfSale.API/Services/Governance/UserGovernanceService.cs");

        AssertContains(Slice(auth, "private (string accessToken, string refreshToken) GenerateTokens", "private async Task RecordPosRefreshSecurityEventAsync"),
            "RefreshTokenGenerator.Generate()",
            "RefreshTokenHasher.Hash(refreshToken)",
            "user.RefreshToken = sessionBinding",
            "PosSecurityConstants.SessionBindingClaim");

        AssertContains(posAuthority,
            "MatchesCurrentSession(sessionBinding, actor.RefreshToken)",
            "stale_actor_role_claim",
            "revoked_or_rotated_pos_session",
            "tenant_inactive_or_deleted",
            "tenant_governance_banned");

        AssertContains(tenantLineage,
            "tenant.IsActive",
            "tenant.Status == TenantStatus.Active",
            "tenant.DeletedAtUtc == null");

        AssertContains(Slice(governance, "public async Task EnsureTenantLoginAllowedAsync", "public async Task EnsureVendorListingAllowedAsync"),
            "_dbContext.Tenants",
            "tenantAuthority.Status != TenantStatus.Active",
            "TENANT_AUTHORITY_REDUCED");

        AssertContains(Slice(auth, "public async Task<ActionResult<AuthResponse>> RefreshToken", "private (string accessToken, string refreshToken) GenerateTokens"),
            "RefreshTokenHasher.Hash(presentedRefreshToken)",
            "REFRESH_TOKEN_REPLAY_DETECTED",
            "REFRESH_REPLAY_REJECTED",
            "EnsureTenantLoginAllowedAsync(user.TenantId",
            "DbUpdateConcurrencyException");

        AssertContains(superAdminAuthority,
            "authority.TokenVersion != tokenVersion",
            "STALE_SUPER_ADMIN_TOKEN",
            "SUPER_ADMIN_GOVERNANCE_REJECTED");

        AssertContains(superAdminAuth,
            "superAdmin.SetRefreshToken(HashRefreshToken(refreshToken)",
            "superAdmin.ClearRefreshToken()",
            "TOKEN_REUSE_DETECTED",
            "TOKEN_REFRESH_FAILED",
            "SuperAdminSecurityConstants.TokenVersionClaim");
    }

    private static void AssertSecurityEventClearsCustomerCookie(string controller, string startMarker, string endMarker)
    {
        var action = Slice(controller, startMarker, endMarker);
        AssertContains(action,
            "ClearCustomerAccessCookies()");
        Assert.Contains("ClearCommerceHubRecoveryCookie()", action, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

    private static void AssertContains(string source, params string[] markers)
    {
        foreach (var marker in markers)
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
