namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontCustomerAuthorityCertificationTests
{
    [Fact]
    public void StorefrontRegistration_MustCreateIdentityOnlyWithoutSessionAuthority()
    {
        var authService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontAuthService.cs");
        var authController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontAuthController.cs");

        Assert.Contains("OnlineCustomers.Add(customer)", authService, StringComparison.Ordinal);
        Assert.Contains("OnlineCustomerRegisteredEvent", authService, StringComparison.Ordinal);
        Assert.Contains("AccessToken = string.Empty", authService, StringComparison.Ordinal);
        Assert.Contains("RefreshToken = string.Empty", authService, StringComparison.Ordinal);
        Assert.DoesNotContain("registerAccessTokenJti", authController, StringComparison.Ordinal);
        Assert.DoesNotContain("\"REGISTER failed - Access token missing JTI", authController, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontSessionIssuance_MustRequireCurrentSqlCustomerAndBlockedPhoneAuthority()
    {
        var refreshService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontRefreshTokenService.cs");
        var authService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontAuthService.cs");
        var authController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontAuthController.cs");

        Assert.Contains("IgnoreQueryFilters()", refreshService, StringComparison.Ordinal);
        Assert.Contains("c.TenantId == tenantId && c.Id == customerId", refreshService, StringComparison.Ordinal);
        Assert.Contains("customer == null || !customer.IsActive", refreshService, StringComparison.Ordinal);
        Assert.Contains("_phoneRiskProfileService.IsBlockedAsync(tenantId, customer.Phone", refreshService, StringComparison.Ordinal);
        Assert.Contains("await IsBlockedPhoneAsync(tenantId, request.Phone", authService, StringComparison.Ordinal);
        Assert.Contains("await IsBlockedPhoneAsync(tenantId, customer.Phone", authService, StringComparison.Ordinal);
        Assert.Contains("LOGIN failed - refresh token authority was not persisted", authController, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", authController, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontJwtAndAccountApis_MustRevalidateCurrentSqlAndCallerOwnership()
    {
        var middleware = CertificationSource.ReadRepoFile("PointOfSale.API/Middleware/StorefrontJwtBlacklistMiddleware.cs");
        var accountController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontAccountController.cs");

        Assert.Contains("blacklist.IsBlacklistedAsync(tenantId, jti", middleware, StringComparison.Ordinal);
        Assert.Contains("customer.Id == customerId && customer.TenantId == tenantId", middleware, StringComparison.Ordinal);
        Assert.Contains("customer.TokensValidAfterUtc", middleware, StringComparison.Ordinal);
        Assert.Contains("TOKEN_ISSUED_BEFORE_CUTOFF", middleware, StringComparison.Ordinal);
        Assert.Contains("c.Id == customerId &&", accountController, StringComparison.Ordinal);
        Assert.Contains("c.TenantId == tenantId &&", accountController, StringComparison.Ordinal);
        Assert.Contains("_phoneRiskProfileService.IsBlockedAsync(tenantId.Value, request.Phone", accountController, StringComparison.Ordinal);
        Assert.Contains("order.TenantId == tenantId.Value", accountController, StringComparison.Ordinal);
        Assert.Contains("authority.OnlineCustomerId != customerId.Value", accountController, StringComparison.Ordinal);
        Assert.Contains("BelongsToAuthenticatedStorefrontCustomer(order, customerId.Value)", accountController, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCustomerAuthority_DocumentationMustBeFrozen()
    {
        Assert.Contains(
            "PASS P2-E Storefront Customer Authority",
            CertificationSource.ReadRepoFile("docs/architecture/storefront-customer-authority-certification-findings.md"),
            StringComparison.Ordinal);
        Assert.Contains("PASS P2-E Storefront Customer Authority", CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md"), StringComparison.Ordinal);
        Assert.Contains("PASS P2-E Storefront Customer Authority Truth", CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md"), StringComparison.Ordinal);
        Assert.Contains("PASS P2-E Storefront Customer Runtime Certifiable", CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md"), StringComparison.Ordinal);
        Assert.Contains("PASS P2-E Storefront Customer Authority Hardening", CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md"), StringComparison.Ordinal);
        Assert.Contains("PASS P2-E Storefront Customer Authority Freeze", CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md"), StringComparison.Ordinal);
    }
}
