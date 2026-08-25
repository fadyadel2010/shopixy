namespace PointOfSale.Tests.Architecture;

/// <summary>
/// AFFILIATE-CHECKOUT-1 runtime certification guard.
/// Affiliate runtime must remain an attribution and commission-evidence overlay
/// on the certified storefront checkout runtime.
/// </summary>
public sealed class AffiliateCheckoutRuntimeCertificationTests
{
    [Fact]
    public void AffiliateRuntime_MustReuseCheckoutTransactionAndNotCreateParallelCheckoutRuntime()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var middleware = CertificationSource.ReadRepoFile("PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("RevalidateCheckoutAuthorityInsideTransactionAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("RevalidateResolvedAffiliateAuthorityInsideFinalTransactionAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("Affiliate checkout authority must be revalidated inside the final checkout transaction", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadAffiliateCommissionPolicySnapshotsAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("RefreshOrderItemAuthoritySnapshots", checkout, StringComparison.Ordinal);
        Assert.Contains("_dbContext.AffiliateAttributions.Add(attribution)", checkout, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync(cancellationToken)", checkout, StringComparison.Ordinal);
        Assert.Contains("UseMiddleware<PointOfSale.API.Middleware.AffiliateReferralMiddleware>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateCheckoutService", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateCheckoutController", program, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginTransactionAsync", middleware, StringComparison.Ordinal);
        Assert.DoesNotContain("_dbContext.Orders.Add", middleware, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateAdmission_MustFailClosedForInvalidStaleReplayAndWrongStoreEvidence()
    {
        var middleware = CertificationSource.ReadRepoFile("PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var evidence = CertificationSource.ReadRepoFile("PointOfSale.API/Security/AffiliateAttributionEvidenceTokenService.cs");
        var attributionConfig = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateAttributionConfiguration.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("payload.TenantId != expectedTenantId", evidence, StringComparison.Ordinal);
        Assert.Contains("payload.ExpiresAtUtc <=", evidence, StringComparison.Ordinal);
        Assert.Contains("Rejected tampered, foreign-tenant, or expired attribution cookie", middleware, StringComparison.Ordinal);
        Assert.Contains("l.TenantId == currentTenantId", middleware, StringComparison.Ordinal);
        Assert.Contains("l.IsActive", middleware, StringComparison.Ordinal);
        Assert.Contains("!l.IsDeleted", middleware, StringComparison.Ordinal);
        Assert.Contains("Affiliate account missing or inactive after PromotionLink resolution", middleware, StringComparison.Ordinal);
        Assert.Contains("promotionLink.TenantId != tenantId", checkout, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PROMOTION_LINK_STALE", checkout, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_AUTHORITY_STALE", checkout, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_POLICY_AUTHORITY_CONFLICT", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadAffiliateEligibleProductIdsAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("category.ParentId != null", checkout, StringComparison.Ordinal);
        Assert.Contains("parent.IsActive", checkout, StringComparison.Ordinal);
        Assert.Contains("HasIndex(e => new { e.TenantId, e.OrderId })", attributionConfig, StringComparison.Ordinal);
        Assert.Contains("IsUnique()", attributionConfig, StringComparison.Ordinal);
        Assert.Contains("append-only and cannot be changed after conversion", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CommissionSnapshot_MustBeImmutableCheckoutEvidenceAndCommissionEventsMustUseIt()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var affiliateService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var ledgerConfig = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateCommissionLedgerConfiguration.cs");

        Assert.Contains("orderItem.AffiliateCommissionEligible = shouldPersistAffiliateCommissionSnapshot", checkout, StringComparison.Ordinal);
        Assert.Contains("orderItem.AffiliateCommissionPolicyIdSnapshot", checkout, StringComparison.Ordinal);
        Assert.Contains("orderItem.AffiliateCommissionRateSnapshot", checkout, StringComparison.Ordinal);
        Assert.Contains("orderItem.AffiliateCommissionBaseAmountSnapshot", checkout, StringComparison.Ordinal);
        Assert.Contains("orderItem.AffiliateCommissionAmountSnapshot", checkout, StringComparison.Ordinal);
        Assert.Contains("orderItem.AffiliateCommissionSnapshotCapturedAtUtc", checkout, StringComparison.Ordinal);
        Assert.Contains("CK_OrderItems_AffiliateCommissionSnapshot_Valid", dbContext, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionLedger.CreateEarnedFromSnapshot", affiliateService, StringComparison.Ordinal);
        Assert.Contains("item.AffiliateCommissionRateSnapshot.Value", affiliateService, StringComparison.Ordinal);
        Assert.Contains("item.AffiliateCommissionAmountSnapshot.Value", affiliateService, StringComparison.Ordinal);
        Assert.Contains("HasPendingCommissionPaymentAuthority(order.PaymentStatus)", affiliateService, StringComparison.Ordinal);
        Assert.Contains("order.FinancialPostingStatus", affiliateService, StringComparison.Ordinal);
        Assert.Contains("builder.HasIndex(e => e.IdempotencyKey)", ledgerConfig, StringComparison.Ordinal);
        Assert.Contains("IsUnique()", ledgerConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateRuntime_MustNotOwnPaymentShippingOrderShipmentInventoryOrWarehouse()
    {
        var affiliateFiles = Directory.EnumerateFiles(FindRepoRoot(), "*.*", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Affiliate{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                           Path.GetFileName(path).Contains("Affiliate", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}PointOfSale.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var combinedAffiliateRuntime = string.Join('\n', affiliateFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("CreatePaymentIntentAsync(", combinedAffiliateRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentIntentCreatedEvent", combinedAffiliateRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateShipmentAsync(", combinedAffiliateRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierShipmentService", combinedAffiliateRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("IShippingCarrierAdapter", combinedAffiliateRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateAndReserveInventoryAsync", combinedAffiliateRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("BindOrderReservationsAsync", combinedAffiliateRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("WarehouseStocks.Add", combinedAffiliateRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("ReservedQuantity +=", combinedAffiliateRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("ReservedQuantity -=", combinedAffiliateRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateShipmentAsync(", combinedAffiliateRuntime, StringComparison.Ordinal);

        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        Assert.Contains("TryCreateTransactionalPaymentIntentAuthorityAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("ValidateShippingMethodAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("ValidateAndReserveInventoryAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("Order created; shipment orchestration deferred until authoritative confirmation/payment", checkout, StringComparison.Ordinal);
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
