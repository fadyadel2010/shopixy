namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubOrderLifecycleCertificationTests
{
    [Fact]
    public void OrderLifecycleCertificationDocumentation_MustContainRequiredPassSixteenFiveASections()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-order-fulfillment-lifecycle-certification-findings.md");

        foreach (var marker in new[]
        {
            "Scope: PASS 16.5A",
            "## Order Lifecycle Inventory",
            "## Ownership Findings",
            "## POS Admin Authority Findings",
            "## Super Admin Authority Findings",
            "## Status Transition Findings",
            "## Cancellation Findings",
            "## Delivery Findings",
            "## Return Findings",
            "## Refund Trigger Findings",
            "## Dashboard And Projection Findings",
            "## Frontend Findings",
            "## Replay And Recovery Findings",
            "## Auditability Findings",
            "## Chaos Findings",
            "## Monitoring Findings",
            "## Runtime Hardening Performed",
            "## Regression Freeze Added",
            "## Architecture Updates",
            "## Remaining Debt",
            "## Required Answers"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MarketplaceOrderLifecycle_MustRemainSqlOwnedAndProjectionVersioned()
    {
        var marketplaceOrder = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubMarketplaceOrder.cs");
        var subOrder = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubSubOrder.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var scope = ReadRepoFile("PointOfSale.Infrastructure/Data/CommerceHubBoundaryMutationScope.cs");

        Assert.Contains("public OrderStatus Status { get; set; } = OrderStatus.Pending", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains("public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains("public Guid CustomerId { get; set; }", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains("GetAffectedStoreIds()", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains("IProjectionVersioned", marketplaceOrder, StringComparison.Ordinal);

        Assert.Contains("public Guid StoreId { get; set; }", subOrder, StringComparison.Ordinal);
        Assert.Contains("public OrderStatus Status { get; set; } = OrderStatus.Pending", subOrder, StringComparison.Ordinal);
        Assert.Contains("public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending", subOrder, StringComparison.Ordinal);
        Assert.Contains("IProjectionVersioned", subOrder, StringComparison.Ordinal);

        Assert.Contains("IsOrderLifecycleAuthorized", scope, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubMarketplaceOrder>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubSubOrder>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubSubOrderItem>", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHub marketplace order lifecycle fields can only be changed", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHub sub-order lifecycle fields can only be changed", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderLifecycleCertificationDocumentation_MustBeRegisteredInArchitectureContext()
    {
        Assert.Contains("PASS 16.5A Order & Fulfillment Lifecycle Certification", ReadRepoFile(".ai-context/PROJECT_CONTEXT.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.5A Order & Fulfillment Lifecycle Certification Truth", ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.5A", ReadRepoFile(".ai-context/KNOWN_RISKS.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.5A Order & Fulfillment Lifecycle Certification", ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.5A Order & Fulfillment Lifecycle Certification Freeze", ReadRepoFile(".ai-context/CODEX_RULES.md"), StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
