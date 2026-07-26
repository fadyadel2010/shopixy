namespace PointOfSale.Tests.Architecture;

public sealed class ReliabilityAdoptionStrategyArchitectureTests
{
    [Fact]
    public void ReliabilityAdoptionStrategy_MustDefineRequiredEnterpriseSections()
    {
        var strategy = File.ReadAllText(StrategyPath());

        foreach (var requiredSection in new[]
        {
            "## Adoption Matrix",
            "## Migration Strategy",
            "## Reliability Enforcement Checklist",
            "## Future Guardrails",
            "## Non-Goals",
            "## Technical Debt Tracking",
            "## Final Review"
        })
        {
            Assert.Contains(requiredSection, strategy, StringComparison.Ordinal);
        }

        Assert.Contains("Tier 1", strategy, StringComparison.Ordinal);
        Assert.Contains("Tier 2", strategy, StringComparison.Ordinal);
        Assert.Contains("Tier 3", strategy, StringComparison.Ordinal);
        Assert.Contains("controlled reliability convergence", strategy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not perfect normalization", strategy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReliabilityAdoptionStrategy_MustClassifyMajorCriticalConsumers()
    {
        var strategy = File.ReadAllText(StrategyPath());

        foreach (var consumerOrFlow in new[]
        {
            "ProductVariantProjectionConsumer",
            "ProductMediaChangedEventConsumer",
            "ProductStockChangedConsumer",
            "ProductStockPriceSearchIndexConsumer",
            "CommerceHubProjectionConsumer",
            "AffiliateProductIntelligenceProjectionConsumer",
            "AffiliateProductPolicyProjectionConsumer",
            "PromotionProjectionConsumer",
            "DiscountReadModelRepairService",
            "CouponProjectionConsumer",
            "PaymentMethodProjectionConsumer",
            "ShippingMethodProjectionConsumer",
            "TenantCurrencyProjectionConsumer",
            "StorefrontConfigProjectionConsumer",
            "OrderProjectionConsumer",
            "CommerceHubOrderProjectionConsumer",
            "StockSyncOutboxProcessor",
            "MarketplaceProjectionOutboxProcessor",
            "AccountingOutboxProcessor",
            "CommerceHubPaymentCompensationOutboxWorker",
            "CommerceHubPaymentService",
            "AffiliateSettlementOrchestrator",
            "ShipmentAuthorityRecoveryHostedService",
            "AnalyticsEventConsumer"
        })
        {
            Assert.Contains(consumerOrFlow, strategy, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReliabilityAdoptionStrategy_MustPreserveNonGoalsAndManualExceptions()
    {
        var strategy = File.ReadAllText(StrategyPath());

        foreach (var marker in new[]
        {
            "Do not migrate every consumer in one sprint.",
            "Do not build a generic projection framework or inheritance tree.",
            "Payment lifecycle, provider webhooks, unresolved payment reconciliation, refunds, payouts",
            "Accounting outbox, financial readiness, payment/refund/payout recovery",
            "ProjectionEventFence",
            "ProjectionAuthorityValidator",
            "ProjectionTombstoneGuard",
            "SqlTruthRebuildGuard"
        })
        {
            Assert.Contains(marker, strategy, StringComparison.Ordinal);
        }
    }

    private static string StrategyPath()
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.Combine(solutionRoot, "docs", "architecture", "reliability-adoption-strategy.md");
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
