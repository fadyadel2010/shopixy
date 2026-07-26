namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceOrderLineageAdminPresentationAuthorityCertificationTests
{
    [Fact]
    public void AdminMarketplaceOrderLineage_MustBeBackendOwnedAndDtoAdmitted()
    {
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");
        var frontendAuthority = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js");
        var ordersPage = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");

        Assert.Contains("public AdminMarketplaceLineageDto? MarketplaceLineage", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid MarketplaceOrderId", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid VendorId", dto, StringComparison.Ordinal);
        Assert.Contains("public string MarketplaceSourceType", dto, StringComparison.Ordinal);
        Assert.Contains("public string? PaymentSnapshotJson", dto, StringComparison.Ordinal);
        Assert.Contains("public string? ShippingSnapshotJson", dto, StringComparison.Ordinal);
        Assert.Contains("public string? CommissionSnapshotJson", dto, StringComparison.Ordinal);

        Assert.Contains("MarketplaceLineage = new AdminMarketplaceLineageDto", service, StringComparison.Ordinal);
        Assert.Contains("MarketplaceOrderId = subOrder.MarketplaceOrderId", service, StringComparison.Ordinal);
        Assert.Contains("MarketplaceOrderNumber = subOrder.MarketplaceOrder.OrderNumber", service, StringComparison.Ordinal);
        Assert.Contains("VendorId = subOrder.StoreId", service, StringComparison.Ordinal);
        Assert.Contains("SourceOrderId = marketplaceOrder.Id.ToString()", service, StringComparison.Ordinal);
        Assert.Contains("MarketplaceLineage = null", service, StringComparison.Ordinal);

        Assert.Contains("requireBackendMarketplaceLineageAuthority", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("vendorId.toLowerCase() !== orderTenantId.toLowerCase()", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("sourceType !== 'Marketplace'", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("normalizeOptionalJson", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("Marketplace Lineage", ordersPage, StringComparison.Ordinal);
        Assert.Contains("getMarketplaceLineagePresentation(order)", ordersPage, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceOrderLineageDocumentation_MustBeFrozen()
    {
        var findings = ReadRepoFile("docs/architecture/marketplace-order-lineage-admin-presentation-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS MP-ORD1 Marketplace Order Lineage Admin Presentation Authority", findings, StringComparison.Ordinal);
        Assert.Contains("Marketplace Order Lineage is production-certifiable", findings, StringComparison.Ordinal);
        Assert.Contains("PASS MP-ORD1 Marketplace Order Lineage Admin Presentation Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS MP-ORD1 Marketplace Order Lineage Admin Presentation Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS MP-ORD1 Marketplace Order Lineage Admin Presentation Authority", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS MP-ORD1 Marketplace Order Lineage Admin Presentation Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS MP-ORD1 Marketplace Order Lineage Admin Presentation Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
