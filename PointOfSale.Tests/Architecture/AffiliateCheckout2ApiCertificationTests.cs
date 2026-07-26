using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// AFFILIATE-CHECKOUT-2 API certification guard.
/// Affiliate APIs must remain referral, context, attribution, commission-evidence,
/// analytics, and audit surfaces. Checkout execution remains POS Admin Checkout.
/// </summary>
public sealed class AffiliateCheckout2ApiCertificationTests
{
    private static readonly string[] AffiliateApiFiles =
    [
        "PointOfSale.API/Controllers/AffiliateAuthController.cs",
        "PointOfSale.API/Controllers/AffiliatePortalController.cs",
        "PointOfSale.API/Controllers/AffiliateAnalyticsController.cs",
        "PointOfSale.API/Controllers/AffiliateDiscoveryController.cs",
        "PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs",
        "PointOfSale.API/Controllers/AffiliatePayoutEvidenceController.cs",
        "PointOfSale.API/Controllers/AdminAffiliateController.cs",
        "PointOfSale.API/Controllers/AdminAffiliateReadinessController.cs",
        "PointOfSale.API/Controllers/AdminProductAffiliateController.cs",
        "PointOfSale.API/Controllers/AdminAffiliateCategoryController.cs",
        "PointOfSale.API/Controllers/PublicAffiliateController.cs",
        "PointOfSale.API/Controllers/PublicAffiliateProductsController.cs",
        "PointOfSale.API/Controllers/AdminAnalyticsAttributionController.cs",
        "PointOfSale.API/Controllers/StorefrontFunnelController.cs",
        "PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs",
        "PointOfSale.API/Helpers/AffiliateModeResolver.cs"
    ];

    [Fact]
    public void AffiliateApiSurface_MustNotExposeDuplicateCheckoutExecutionEndpoints()
    {
        var combined = ReadAffiliateApiSurface();

        Assert.DoesNotContain("AffiliateCheckoutController", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateCheckoutService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ICheckoutService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitOrder", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreateOrder", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CheckoutPricing", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CheckoutSession", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("RecoveryToken", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatePaymentIntentAsync", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateShipmentAsync", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateAndReserveInventoryAsync", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("BindOrderReservationsAsync", combined, StringComparison.Ordinal);

        Assert.DoesNotMatch(new Regex(@"\[Http(Post|Put|Patch)\([^\)]*checkout", RegexOptions.IgnoreCase), combined);
        Assert.DoesNotMatch(new Regex(@"\[Http(Post|Put|Patch)\([^\)]*payment", RegexOptions.IgnoreCase), combined);
        Assert.DoesNotMatch(new Regex(@"\[Http(Post|Put|Patch)\([^\)]*shipment", RegexOptions.IgnoreCase), combined);
    }

    [Fact]
    public void AffiliateApiControllers_MustDelegateMutationAuthorityToAffiliateServicesOrCartContextOnly()
    {
        var adminAffiliate = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminAffiliateController.cs");
        var productAffiliate = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminProductAffiliateController.cs");
        var portal = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePortalController.cs");
        var funnel = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontFunnelController.cs");

        Assert.Contains("IAffiliateService", adminAffiliate, StringComparison.Ordinal);
        Assert.Contains("IAffiliatePayoutService", adminAffiliate, StringComparison.Ordinal);
        Assert.Contains("IAffiliateSettlementOrchestrator", adminAffiliate, StringComparison.Ordinal);
        Assert.Contains("_affiliateService.ApproveCommissionForOrderAsync", adminAffiliate, StringComparison.Ordinal);
        Assert.Contains("_orchestrator.ExecuteAffiliateBatchPayoutAsync", adminAffiliate, StringComparison.Ordinal);
        Assert.Contains("_orchestrator.ExecuteAffiliatePayoutAsync", adminAffiliate, StringComparison.Ordinal);
        Assert.Contains("Legacy commission reconciliation is disabled", adminAffiliate, StringComparison.Ordinal);

        Assert.Contains("_affiliateService.EnableAffiliateWithCategoryAsync", productAffiliate, StringComparison.Ordinal);
        Assert.Contains("_affiliateService.DisableAffiliateForProductAsync", productAffiliate, StringComparison.Ordinal);

        Assert.Contains("_payoutService.RequestPayoutAsync", portal, StringComparison.Ordinal);
        Assert.Contains("ResolveAffiliateAccountId", portal, StringComparison.Ordinal);

        Assert.Contains("Checkout remains the commerce authority", funnel, StringComparison.Ordinal);
        Assert.Contains("_cartService.AddItemAsync", funnel, StringComparison.Ordinal);
        Assert.Contains("var checkoutUrl = $\"/checkout?cartId={cart.CartId}\"", funnel, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitOrder", funnel, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateApiRequests_MustNotAcceptCheckoutPaymentShippingOrOrderTruth()
    {
        var combined = ReadAffiliateApiSurface();
        var requestBlocks = Regex.Matches(
                combined,
                @"public\s+(?:sealed\s+)?(?:class|record)\s+\w*(?:Request|Dto|Body)\b[\s\S]*?^\}",
                RegexOptions.Multiline)
            .Select(match => match.Value)
            .Where(block => block.Contains("Affiliate", StringComparison.OrdinalIgnoreCase) ||
                            block.Contains("Promotion", StringComparison.OrdinalIgnoreCase) ||
                            block.Contains("Payout", StringComparison.OrdinalIgnoreCase) ||
                            block.Contains("Funnel", StringComparison.OrdinalIgnoreCase) ||
                            block.Contains("Category", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var requestSurface = string.Join('\n', requestBlocks);

        Assert.DoesNotContain("CartTotal", requestSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Subtotal", requestSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TaxTotal", requestSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ShippingMethodId", requestSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ShippingCost", requestSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PaymentMethodId", requestSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PaymentIntent", requestSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OrderTotal", requestSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Inventory", requestSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WarehouseId", requestSurface, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AffiliateApiSecurity_MustPreserveAffiliateIdentityTenantAndReplayBoundaries()
    {
        var auth = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateAuthController.cs");
        var links = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs");
        var portal = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePortalController.cs");
        var admin = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminAffiliateController.cs");
        var evidence = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePayoutEvidenceController.cs");
        var middleware = CertificationSource.ReadRepoFile("PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs");
        var token = CertificationSource.ReadRepoFile("PointOfSale.API/Security/AffiliateAttributionEvidenceTokenService.cs");

        Assert.Contains("[EnableRateLimiting(\"AffiliateAuthRateLimit\")]", auth, StringComparison.Ordinal);
        Assert.Contains("Email verification is required before login", auth, StringComparison.Ordinal);
        Assert.Contains("HashToken(refreshToken)", auth, StringComparison.Ordinal);
        Assert.Contains("Refresh token stored on AffiliateUser entity", auth, StringComparison.Ordinal);

        Assert.Contains("[Authorize(Policy = \"AffiliateOnly\")]", links, StringComparison.Ordinal);
        Assert.Contains("l.AffiliateUserId == affiliateUserId", links, StringComparison.Ordinal);
        Assert.Contains("l.TenantId == tenantId", links, StringComparison.Ordinal);

        Assert.Contains("[Authorize(Policy = \"AffiliateOnly\")]", portal, StringComparison.Ordinal);
        Assert.Contains("User.FindFirst(\"affiliate_account_id\")", portal, StringComparison.Ordinal);
        Assert.Contains("_payoutService.RequestPayoutAsync", portal, StringComparison.Ordinal);

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", admin, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", admin, StringComparison.Ordinal);
        Assert.Contains("StoreTenantId == tenantId", admin, StringComparison.Ordinal);

        Assert.Contains("_tenantContext.RequiredTenantId", evidence, StringComparison.Ordinal);
        Assert.Contains("RegisterVerifiedEvidenceAsync", evidence, StringComparison.Ordinal);

        Assert.Contains("Rejected tampered, foreign-tenant, or expired attribution cookie", middleware, StringComparison.Ordinal);
        Assert.Contains("payload.TenantId != expectedTenantId", token, StringComparison.Ordinal);
        Assert.Contains("payload.ExpiresAtUtc <=", token, StringComparison.Ordinal);
    }

    [Fact]
    public void PosAdminCheckoutApis_MustRemainTheOnlyCheckoutExecutionSurface()
    {
        var checkoutController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutSessionController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var checkoutService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("ICheckoutService", checkoutController, StringComparison.Ordinal);
        Assert.Contains("_checkoutService.ProcessCheckoutAsync", checkoutController, StringComparison.Ordinal);
        Assert.Contains("CheckoutSessionController", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("RevalidateResolvedAffiliateAuthorityInsideFinalTransactionAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadAffiliateCommissionPolicySnapshotsAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("UseMiddleware<PointOfSale.API.Middleware.AffiliateReferralMiddleware>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateCheckoutController", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateCheckoutService", program, StringComparison.Ordinal);
    }

    private static string ReadAffiliateApiSurface() =>
        string.Join('\n', AffiliateApiFiles.Select(CertificationSource.ReadRepoFile));
}
