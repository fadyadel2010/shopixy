namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateAttributionRecoveryCertificationTests
{
    [Fact]
    public void MissingAttributionRecovery_MustRebuildOnlyFromDurableOrderSnapshot()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var hosted = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateAttributionReconciliationHostedService.cs");

        Assert.Contains("ReconcileMissingAttributionsAsync", hosted, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<AffiliateService>()", hosted, StringComparison.Ordinal);

        Assert.Contains("ReconcileMissingAttributionsAsync", service, StringComparison.Ordinal);
        Assert.Contains("order.AffiliateAccountId.HasValue", service, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(order.AffiliateReferralCode)", service, StringComparison.Ordinal);
        Assert.Contains("AttributeOrderToAffiliateFromOrderSnapshotAsync", service, StringComparison.Ordinal);
        Assert.Contains("order.PromotionLinkId", service, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryAndReplay_MustRejectAttributionMigration()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var consumer = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateOrderAttributionConsumer.cs");

        Assert.Contains("persisted attribution affiliate", service, StringComparison.Ordinal);
        Assert.Contains("does not match order snapshot affiliate", service, StringComparison.Ordinal);
        Assert.Contains("persisted attribution referral code", service, StringComparison.Ordinal);
        Assert.Contains("does not match order snapshot referral code", service, StringComparison.Ordinal);
        Assert.Contains("persisted attribution promotion link", service, StringComparison.Ordinal);
        Assert.Contains("does not match order snapshot promotion link", service, StringComparison.Ordinal);

        Assert.Contains("expectedAffiliateAccountId", consumer, StringComparison.Ordinal);
        Assert.Contains("evt.PromotionLinkId", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("already attributed", Slice(consumer, "catch (Exception ex)", "throw; // Let MassTransit retry"), StringComparison.Ordinal);
    }

    [Fact]
    public void CommissionReplay_MustConvergeWithoutDuplicateEarnApproveReverse()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        Assert.Contains("AFFILIATE_COMM:EARNED", CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateCommissionLedger.cs"), StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_COMM:APPROVED", CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateCommissionLedger.cs"), StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_COMM:REVERSED", CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateCommissionLedger.cs"), StringComparison.Ordinal);

        Assert.Contains("ReconcileRefundedCommissionEntriesAsync(order, currentEarnedEntries, ct)", service, StringComparison.Ordinal);
        Assert.Contains("ReconcileDeliveredCommissionApprovalsAsync(order, currentEarnedEntries, ct)", service, StringComparison.Ordinal);
        Assert.Contains("ShouldRetryMissingEarnedCommissionForRefundAsync", service, StringComparison.Ordinal);
        Assert.Contains("Unique constraint violation on SaveChanges", service, StringComparison.Ordinal);
        Assert.Contains("Detaching {Count} staged outbox/event entities", service, StringComparison.Ordinal);
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
