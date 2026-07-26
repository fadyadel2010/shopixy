namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubRefreshTokenCertificationTests
{
    [Fact]
    public void CommerceHubCustomerAuth_MustNotIssueRefreshTokensUntilRotationAuthorityExists()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Customers/CustomerAuthDtos.cs");
        var cookies = ReadRepoFile("PointOfSale.API/Security/AuthCookieConfiguration.cs");

        Assert.DoesNotContain("[HttpPost(\"refresh", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh_token", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SetSessionCookies", controller, StringComparison.Ordinal);

        Assert.DoesNotContain("RefreshToken", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateRefresh", service, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshToken", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubCustomerRefresh", cookies, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubCustomerRefreshTokenCookieName", cookies, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCustomerModel_MustNotPersistRefreshTokenAuthority()
    {
        var customer = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCustomer.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var snapshot = ReadRepoFile("PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs");
        var findings = ReadRepoFile("docs/architecture/commercehub-session-lifecycle-revocation-certification-findings.md");

        Assert.DoesNotContain("RefreshToken", customer, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubCustomerRefreshToken", dbContext, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubCustomerRefreshToken", snapshot, StringComparison.Ordinal);
        Assert.Contains("CommerceHub customer refresh tokens are not currently used", findings, StringComparison.Ordinal);
        Assert.Contains("No old CommerceHub customer refresh token can be reused because none is issued or persisted", findings, StringComparison.Ordinal);
    }

    [Fact]
    public void NonCommerceHubRefreshTokenStores_MustStayOutsideCommerceHubCustomerAuthority()
    {
        var onlineCustomerToken = ReadRepoFile("PointOfSale.Core/Entities/OnlineCustomerRefreshToken.cs");
        var superAdmin = ReadRepoFile("PointOfSale.Core/SuperAdmin/Models/SuperAdminUser.cs");
        var posUser = ReadRepoFile("PointOfSale.Core/Entities/User.cs");
        var cookies = ReadRepoFile("PointOfSale.API/Security/AuthCookieConfiguration.cs");
        var findings = ReadRepoFile("docs/architecture/commercehub-session-lifecycle-revocation-certification-findings.md");

        Assert.Contains("OnlineCustomerRefreshToken", onlineCustomerToken, StringComparison.Ordinal);
        Assert.Contains("RefreshTokenHash", superAdmin, StringComparison.Ordinal);
        Assert.Contains("RefreshToken", posUser, StringComparison.Ordinal);
        Assert.Contains("StorefrontRefreshTokenCookieName", cookies, StringComparison.Ordinal);
        Assert.Contains("SuperAdminRefreshTokenCookieName", cookies, StringComparison.Ordinal);
        Assert.Contains("PosRefreshTokenCookieName", cookies, StringComparison.Ordinal);
        Assert.Contains("Legacy storefront online-customer refresh tokens", findings, StringComparison.Ordinal);
        Assert.Contains("not CommerceHub customer session authority", findings, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
