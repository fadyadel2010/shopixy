namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCustomerIdentitySessionAuthorityCertificationTests
{
    [Fact]
    public void CustomerAccessValidation_MustRemainCurrentSqlIdentityAuthority()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var revocationService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs");
        var authService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var customerOrders = ReadRepoFile("PointOfSale.API/Controllers/CustomerOrdersController.cs");

        var customerJwtScheme = Slice(program,
            ".AddJwtBearer(PointOfSale.API.Security.CustomerSecurityConstants.AuthenticationScheme",
            "// Use [Authorize(AuthenticationSchemes = \"AffiliateJwt\")]");
        var sessionValidation = Slice(revocationService,
            "public async Task EnsureTokenSessionStillValidAsync",
            "public async Task<DateTime> RevokeAllTokensAsync");

        Assert.Contains("IsTokenRevokedAsync", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("EnsureTokenSessionStillValidAsync", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerLoginAllowedAsync(customerId", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("customer.Id == customerId && !customer.IsDeleted", sessionValidation, StringComparison.Ordinal);
        Assert.Contains("customer.IsEmailVerified", sessionValidation, StringComparison.Ordinal);
        Assert.Contains("Customer email verification is required", sessionValidation, StringComparison.Ordinal);
        Assert.Contains("TokensValidAfterUtc", sessionValidation, StringComparison.Ordinal);
        Assert.Contains("ResolveTokenIssuedAtUtc(principal)", sessionValidation, StringComparison.Ordinal);
        Assert.Contains("if (!customer.IsEmailVerified)", authService, StringComparison.Ordinal);
        Assert.Contains("EMAIL_NOT_VERIFIED", authService, StringComparison.Ordinal);
        Assert.Contains("[Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]", customerOrders, StringComparison.Ordinal);
        var orderService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerOrderService.cs");
        Assert.Contains("!current.IsDeleted", orderService, StringComparison.Ordinal);
        Assert.Contains("current.CustomerId == customerId", orderService, StringComparison.Ordinal);
        Assert.Contains("current.Id == orderId", orderService, StringComparison.Ordinal);
        Assert.Contains("current.Currency == FinancialCurrencyGuard.PlatformBaseCurrencyCode", orderService, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCustomerRefreshTokens_MustRemainAbsentUntilCertifiedRotationExists()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Customers/CustomerAuthDtos.cs");
        var customer = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCustomer.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.DoesNotContain("[HttpPost(\"refresh", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshToken", service, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshToken", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshToken", customer, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubCustomerRefreshToken", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderAccessRecoveryTokens_MustBeSqlBoundAndServerExpiring()
    {
        var accessService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderAccessService.cs");
        var ordersController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubOrdersController.cs");
        var customerOrders = ReadRepoFile("PointOfSale.API/Controllers/CustomerOrdersController.cs");

        Assert.Contains("TokenVersion = \"v2\"", accessService, StringComparison.Ordinal);
        Assert.Contains("RecoveryTokenTtlMinutes", accessService, StringComparison.Ordinal);
        Assert.Contains("expiresAtUnixSeconds", accessService, StringComparison.Ordinal);
        Assert.Contains("DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAtUnixSeconds", accessService, StringComparison.Ordinal);
        Assert.Contains("BuildPayload(marketplaceOrderId, orderNumber, expiresAtUnixSeconds)", accessService, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", accessService, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateAccessToken(marketplaceOrderId, orderNumber);", accessService, StringComparison.Ordinal);

        Assert.Contains("LoadOrderAuthoritySnapshotAsync", ordersController, StringComparison.Ordinal);
        Assert.Contains("authenticatedCustomerId.Value == authority.CustomerId", ordersController, StringComparison.Ordinal);
        Assert.Contains("ValidateAccessToken(marketplaceOrderId, authority.OrderNumber, orderAccessToken)", ordersController, StringComparison.Ordinal);
        Assert.Contains("OrderAccessToken = null", ReadRepoFile("PointOfSale.API/Controllers/CommerceHubCheckoutController.cs"), StringComparison.Ordinal);
        Assert.Contains("[Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]", customerOrders, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerIdentitySessionImplementationEvidence_MustBeFrozen()
    {
        Assert.Contains("PASS M8 CommerceHub Customer Identity & Session Authority", ReadRepoFile("docs/architecture/commercehub-customer-identity-session-authority-certification-findings.md"), StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<CommerceHubCustomerIdentityEvent>()", ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs"), StringComparison.Ordinal);
        Assert.Contains("IX_CHCustomerIdentityEvents_Customer_Occurred", ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubCustomerIdentityEventConfiguration.cs"), StringComparison.Ordinal);
        Assert.Contains("CommerceHubCustomerIdentityEvent", ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs"), StringComparison.Ordinal);
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
