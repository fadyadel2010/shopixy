namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PASS AF-PA5.4 - Commission Candidate lifecycle, Pending Commission creation, and Financial Authority freeze.
/// </summary>
public sealed class AffiliateCommissionCandidatePendingAuthorityCertificationTests
{
    [Fact]
    public void PendingCommission_MustBeginOnlyAtBackendFinancialLifecycleBoundary()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var recordMethod = Slice(service, "public async Task RecordCommissionForOrderAsync", "private async Task<bool> TryRepairMissingAttributionAsync");

        Assert.Contains("HasPendingCommissionPaymentAuthority(order.PaymentStatus)", recordMethod, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Collected", service, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Settled", service, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.PartiallyRefunded", service, StringComparison.Ordinal);
        Assert.Contains("order.FinancialPostingStatus", recordMethod, StringComparison.Ordinal);
        Assert.Contains("FinancialPostingStatus.Posted", recordMethod, StringComparison.Ordinal);
        Assert.Contains("FinancialPostingStatus.NotRequired", recordMethod, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionLedger.CreateEarnedFromSnapshot", recordMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingCommission_MustNotBeCreatedForTerminalOrUnpaidOrderStates()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var recordMethod = Slice(service, "public async Task RecordCommissionForOrderAsync", "private async Task<bool> TryRepairMissingAttributionAsync");

        Assert.Contains("order.Status is OrderStatus.Cancelled or OrderStatus.Refunded", recordMethod, StringComparison.Ordinal);
        Assert.Contains("terminal_order_status", recordMethod, StringComparison.Ordinal);
        Assert.Contains("payment_not_collected", recordMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus.Pending", Slice(service, "private static bool HasPendingCommissionPaymentAuthority", "private async Task<bool> TryRepairMissingAttributionAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus.CashOnDelivery", Slice(service, "private static bool HasPendingCommissionPaymentAuthority", "private async Task<bool> TryRepairMissingAttributionAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus.Rejected", Slice(service, "private static bool HasPendingCommissionPaymentAuthority", "private async Task<bool> TryRepairMissingAttributionAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus.Expired", Slice(service, "private static bool HasPendingCommissionPaymentAuthority", "private async Task<bool> TryRepairMissingAttributionAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus.Refunded", Slice(service, "private static bool HasPendingCommissionPaymentAuthority", "private async Task<bool> TryRepairMissingAttributionAsync"), StringComparison.Ordinal);
    }

    [Fact]
    public void PendingCommission_MustUseImmutableCandidateSnapshotAndNotCurrentEligibility()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var recordMethod = Slice(service, "public async Task RecordCommissionForOrderAsync", "private async Task<bool> TryRepairMissingAttributionAsync");

        Assert.Contains("item.AffiliateCommissionEligible", recordMethod, StringComparison.Ordinal);
        Assert.Contains("item.AffiliateCommissionRateSnapshot.Value", recordMethod, StringComparison.Ordinal);
        Assert.Contains("item.AffiliateCommissionBaseAmountSnapshot", recordMethod, StringComparison.Ordinal);
        Assert.Contains("item.AffiliateCommissionAmountSnapshot.Value", recordMethod, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionLedger.CreateEarnedFromSnapshot", recordMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateProductPolicies", recordMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateCategories", recordMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PromotionLink", recordMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateSnapshots_MustSurvivePostCheckoutAffiliateSuspension()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var recordMethod = Slice(service, "public async Task RecordCommissionForOrderAsync", "private async Task<bool> TryRepairMissingAttributionAsync");

        Assert.Contains("Later suspension must not erase an admitted commission candidate", recordMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("affiliate.Status != AffiliateStatus.Active", recordMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("inactive_affiliate", recordMethod, StringComparison.Ordinal);
        Assert.Contains("affiliate_missing_or_deleted", recordMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void Frontend_MustNotOwnCommissionLifecycleOrPendingCreation()
    {
        var webFiles = Directory.EnumerateFiles(FindRepoRoot(), "*.*", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}PointOfSale.Web{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                           path.Contains($"{Path.DirectorySeparatorChar}PointOfSale.Storefront{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                           path.Contains($"{Path.DirectorySeparatorChar}PointOfSale.CommerceHub{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var combinedFrontend = string.Join('\n', webFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("CreateEarnedFromSnapshot", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateCommissionLedger", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordCommissionForOrderAsync", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("CommissionEntryType.Earned", combinedFrontend, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
