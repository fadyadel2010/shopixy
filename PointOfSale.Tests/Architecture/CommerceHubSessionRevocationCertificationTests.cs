namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSessionRevocationCertificationTests
{
    [Fact]
    public void CustomerAccessTokenRevocation_MustValidateSignedTokenBeforePersistingJti()
    {
        var revocationService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs");

        var revokeToken = Slice(revocationService, "public async Task RevokeTokenAsync", "public async Task<bool> IsTokenRevokedAsync");

        Assert.Contains("ValidateIssuerSigningKey = true", revokeToken, StringComparison.Ordinal);
        Assert.Contains("IssuerSigningKey = new SymmetricSecurityKey", revokeToken, StringComparison.Ordinal);
        Assert.Contains("ValidateIssuer = true", revokeToken, StringComparison.Ordinal);
        Assert.Contains("ValidIssuer = _customerJwtSettings.Issuer", revokeToken, StringComparison.Ordinal);
        Assert.Contains("ValidateAudience = true", revokeToken, StringComparison.Ordinal);
        Assert.Contains("ValidAudience = _customerJwtSettings.Audience", revokeToken, StringComparison.Ordinal);
        Assert.Contains("ValidateLifetime = false", revokeToken, StringComparison.Ordinal);
        Assert.Contains("jwtToken.ValidTo <= DateTime.UtcNow", revokeToken, StringComparison.Ordinal);
        Assert.Contains("_dbContext.RevokedCustomerAccessTokens", revokeToken, StringComparison.Ordinal);
        Assert.Contains("TryCacheRevocationAsync(jti, jwtToken.ValidTo)", revokeToken, StringComparison.Ordinal);
        Assert.Contains("TokenRevocationPersistenceException", revokeToken, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerProtectedEndpoints_MustRejectRevokedCutoffAndGovernanceRestrictedSessions()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var revocationService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs");

        var customerJwtScheme = Slice(program, ".AddJwtBearer(PointOfSale.API.Security.CustomerSecurityConstants.AuthenticationScheme", "// Use [Authorize(AuthenticationSchemes = \"AffiliateJwt\")]");
        var sessionCutoff = Slice(revocationService, "public async Task EnsureTokenSessionStillValidAsync", "public async Task<DateTime> RevokeAllTokensAsync");
        var revokedLookup = Slice(revocationService, "public async Task<bool> IsTokenRevokedAsync", "public async Task EnsureTokenSessionStillValidAsync");

        Assert.Contains("CommerceHubCustomerAccessTokenCookieName", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("JwtRegisteredClaimNames.Jti", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("context.Fail(\"Missing token jti claim.\")", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("IsTokenRevokedAsync(jtiClaim", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("EnsureTokenSessionStillValidAsync", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerLoginAllowedAsync(customerId", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("TokenRevocationCheckUnavailableException", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("context.Fail(\"Token revocation status could not be verified.\")", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("MapInboundClaims = false", customerJwtScheme, StringComparison.Ordinal);

        Assert.Contains("Redis lookup failed, falling back to database", revokedLookup, StringComparison.Ordinal);
        Assert.Contains("RevokedCustomerAccessTokens", revokedLookup, StringComparison.Ordinal);
        Assert.Contains("token.ExpiresAt > DateTime.UtcNow", revokedLookup, StringComparison.Ordinal);
        Assert.Contains("TokenRevocationCheckUnavailableException", revokedLookup, StringComparison.Ordinal);

        Assert.Contains("TokensValidAfterUtc", sessionCutoff, StringComparison.Ordinal);
        Assert.Contains("ResolveTokenIssuedAtUtc(principal)", sessionCutoff, StringComparison.Ordinal);
        Assert.Contains("tokenIssuedAtUtc.Value <= authority.TokensValidAfterUtc.Value", sessionCutoff, StringComparison.Ordinal);
        Assert.Contains("CustomerTokenSessionRevokedException", sessionCutoff, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityEvents_MustAdvanceDurableCutoffAndRecordReconstructionEvidence()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var entity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCustomerIdentityEvent.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("PasswordResetCompleted", service, StringComparison.Ordinal);
        Assert.Contains("PasswordChanged", service, StringComparison.Ordinal);
        Assert.Contains("EmailChanged", service, StringComparison.Ordinal);
        Assert.Contains("LoggedOut", controller, StringComparison.Ordinal);
        Assert.Contains("AllSessionsRevoked", controller, StringComparison.Ordinal);

        Assert.Contains("PasswordResetCompleted", entity, StringComparison.Ordinal);
        Assert.Contains("PasswordChanged", entity, StringComparison.Ordinal);
        Assert.Contains("EmailChanged", entity, StringComparison.Ordinal);
        Assert.Contains("LoggedOut", entity, StringComparison.Ordinal);
        Assert.Contains("AllSessionsRevoked", entity, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<CommerceHubCustomerIdentityEvent>()", dbContext, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
