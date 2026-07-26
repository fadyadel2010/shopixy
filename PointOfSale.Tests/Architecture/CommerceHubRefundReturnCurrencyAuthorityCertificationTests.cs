namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubRefundReturnCurrencyAuthorityCertificationTests
{
    [Fact]
    public void RefundAndReturnCurrency_MustBeReProvenAsEgpAtRuntimeBoundaries()
    {
        var authority = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundReturnCurrencyAuthority.cs");
        var customerRefunds = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerRefundService.cs");
        var execution = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs");
        var gatewayRefunds = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundGatewayService.cs");
        var recovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var returns = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubReturnMapper.cs");
        var admin = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminRefundsService.cs");

        Assert.Contains("RequireEgpCurrency", authority, StringComparison.Ordinal);
        Assert.Contains("RequireExpectedCurrency", authority, StringComparison.Ordinal);
        Assert.Contains("commercehub.refund.payment.currency", authority, StringComparison.Ordinal);
        Assert.Contains("RequireConsistentExecutionCurrency", customerRefunds, StringComparison.Ordinal);
        Assert.Contains("RequireConsistentExecutionCurrency", execution, StringComparison.Ordinal);
        Assert.Contains("RequireConsistentExecutionCurrency", recovery, StringComparison.Ordinal);
        Assert.Contains("RequireOrderCurrency", returns, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency", admin, StringComparison.Ordinal);
        Assert.Contains("gateway refunds require", gatewayRefunds, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefundExecutionPersistence_MustRejectNonEgpCurrency()
    {
        var executionConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubRefundExecutionConfiguration.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260623140000_CommerceHubRefundReturnCurrencyAuthorityHardening.cs");

        Assert.Contains("CK_CommerceHubRefundExecutions_Currency_EGP", executionConfiguration, StringComparison.Ordinal);
        Assert.Contains("CHECK (\"\"Currency\"\" = 'EGP') NOT VALID", migration, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
