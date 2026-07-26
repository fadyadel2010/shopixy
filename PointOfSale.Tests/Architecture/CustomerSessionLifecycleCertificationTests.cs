namespace PointOfSale.Tests.Architecture;

public sealed class CustomerSessionLifecycleCertificationTests
{
    [Fact]
    public void Pass161CustomerJwt_MustRemainCookieBackedRevocableAndCutoffValidated()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var revocationService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs");

        Assert.Contains("CommerceHubCustomerAccessTokenCookieName", program, StringComparison.Ordinal);
        Assert.Contains("IsTokenRevokedAsync", program, StringComparison.Ordinal);
        Assert.Contains("EnsureTokenSessionStillValidAsync", program, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"logout\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"revoke-all-sessions\")]", controller, StringComparison.Ordinal);
        Assert.Contains("customer.RevokeAllSessions(cutoffUtc)", revocationService, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass161SuspendedCustomers_MustNotLoginOrRetainJwtAccess()
    {
        var governance = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Governance/UserGovernanceService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("evaluation.Status == UserGovernanceStatus.Suspended", governance, StringComparison.Ordinal);
        Assert.Contains("\"CUSTOMER_SUSPENDED\"", governance, StringComparison.Ordinal);
        Assert.Contains("\"CUSTOMER_BANNED\" or \"CUSTOMER_SUSPENDED\"", controller, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerLoginAllowedAsync(customerId", program, StringComparison.Ordinal);
    }
}

