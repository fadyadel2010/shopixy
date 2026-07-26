namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateAttributionCertificationTests
{
    [Fact]
    public void AttributionTruth_MustBeSqlOrderSnapshotAndCanonicalAttribution()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var affiliateService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var attribution = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateAttribution.cs");
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateAttributionConfiguration.cs");

        Assert.Contains("order.AffiliateAccountId = resolvedAffiliateAuthority.AffiliateAccountId", checkout, StringComparison.Ordinal);
        Assert.Contains("order.AffiliateReferralCode = resolvedAffiliateAuthority.ReferralCode", checkout, StringComparison.Ordinal);
        Assert.Contains("order.PromotionLinkId = resolvedAffiliateAuthority.PromotionLinkId", checkout, StringComparison.Ordinal);
        Assert.Contains("_dbContext.AffiliateAttributions.Add(attribution)", checkout, StringComparison.Ordinal);

        Assert.Contains("Affiliate attribution authority mismatch", affiliateService, StringComparison.Ordinal);
        Assert.Contains("event affiliate", affiliateService, StringComparison.Ordinal);
        Assert.Contains("order affiliate", affiliateService, StringComparison.Ordinal);
        Assert.Contains("order promotion link was not preserved", affiliateService, StringComparison.Ordinal);

        Assert.Contains("private set;", attribution, StringComparison.Ordinal);
        Assert.Contains("Exactly ONE attribution per (TenantId, OrderId)", attribution, StringComparison.Ordinal);
        Assert.Contains("HasIndex(e => new { e.TenantId, e.OrderId })", config, StringComparison.Ordinal);
        Assert.Contains("IsUnique()", config, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributionPersistence_MustUseFirstClickHttpOnlyCookieAndSqlRevalidation()
    {
        var middleware = CertificationSource.ReadRepoFile("PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("private const string AttributionCookieName = \"AFF_ATTR\"", middleware, StringComparison.Ordinal);
        Assert.Contains("Cookie:first-click-protected", middleware, StringComparison.Ordinal);
        Assert.Contains("attributionEvidenceTokens.TryValidate", middleware, StringComparison.Ordinal);
        Assert.Contains("AffiliateAttributionEvidenceTokenService.v1", CertificationSource.ReadRepoFile("PointOfSale.API/Security/AffiliateAttributionEvidenceTokenService.cs"), StringComparison.Ordinal);
        Assert.Contains("payload.TenantId != expectedTenantId", CertificationSource.ReadRepoFile("PointOfSale.API/Security/AffiliateAttributionEvidenceTokenService.cs"), StringComparison.Ordinal);
        Assert.Contains("payload.ExpiresAtUtc <=", CertificationSource.ReadRepoFile("PointOfSale.API/Security/AffiliateAttributionEvidenceTokenService.cs"), StringComparison.Ordinal);
        Assert.Contains("HttpOnly = true", middleware, StringComparison.Ordinal);
        Assert.Contains("Secure = true", middleware, StringComparison.Ordinal);
        Assert.Contains("SameSite = SameSiteMode.Lax", middleware, StringComparison.Ordinal);
        Assert.Contains("MaxAge = TimeSpan.FromDays(30)", middleware, StringComparison.Ordinal);

        Assert.Contains("LoadActiveAffiliateAccountAuthorityForFinalCheckoutAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadActiveAffiliatePromotionLinkAuthorityForFinalCheckoutAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void Checkout_MustRejectDtoAndCartAsAffiliateOwnershipAuthority()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var authoritySelection = Slice(
            checkout,
            "AffiliateAttributionAuthority? resolvedAffiliateAuthority = null;",
            "var affiliateCommissionPolicySnapshots = resolvedAffiliateAuthority != null");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("middleware-validated, tenant-bound", authoritySelection, StringComparison.Ordinal);
        Assert.DoesNotContain("request.AffiliateReferralCode", authoritySelection, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot.AffiliateSource == \"Funnel\"", authoritySelection, StringComparison.Ordinal);
        Assert.Contains("GuardAffiliateAttributionAuthority()", dbContext, StringComparison.Ordinal);
        Assert.Contains("cannot be reassigned after creation", dbContext, StringComparison.Ordinal);
        Assert.Contains("append-only and cannot be changed after conversion", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotionLinks_MustBeStoreScopedAttributionNotPurchasedProductGate()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        var requestResolution = Slice(
            checkout,
            "private async Task<AffiliateAttributionAuthority?> ResolveAffiliateAttributionAuthorityAsync",
            "private async Task<AffiliateAttributionAuthority> ResolveMiddlewareAffiliateAuthorityAsync");
        var middlewareResolution = Slice(
            checkout,
            "private async Task<AffiliateAttributionAuthority> ResolveMiddlewareAffiliateAuthorityAsync",
            "private async Task<AffiliateAttributionAuthority?> ResolveActiveAffiliateAccountAuthorityAsync");
        var finalRevalidation = Slice(
            checkout,
            "private async Task<AffiliateAttributionAuthority?> RevalidateResolvedAffiliateAuthorityInsideFinalTransactionAsync",
            "private async Task<AffiliateAccountAuthorityCandidate?> LoadActiveAffiliateAccountAuthorityForFinalCheckoutAsync");

        Assert.Contains("promotionLink.TenantId != tenantId", requestResolution, StringComparison.Ordinal);
        Assert.DoesNotContain("!checkoutProductIds.Contains(promotionLink.ProductId)", requestResolution, StringComparison.Ordinal);
        Assert.Contains("promotionLink.TenantId != tenantId", middlewareResolution, StringComparison.Ordinal);
        Assert.DoesNotContain("!checkoutProductIds.Contains(promotionLink.ProductId)", middlewareResolution, StringComparison.Ordinal);
        Assert.Contains("promotionLink.TenantId != tenantId", finalRevalidation, StringComparison.Ordinal);
        Assert.DoesNotContain("!checkoutProductIds.Contains(promotionLink.ProductId)", finalRevalidation, StringComparison.Ordinal);
    }

    [Fact]
    public void EconomicAbuse_MustBlockSameEmailOrPhoneSelfPurchaseAttribution()
    {
        var affiliateService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        Assert.Contains("EnsureAffiliateSelfPurchaseBlockedAsync", affiliateService, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_SELF_PURCHASE_BLOCKED", affiliateService, StringComparison.Ordinal);
        Assert.Contains("order.GuestEmail", affiliateService, StringComparison.Ordinal);
        Assert.Contains("order.GuestPhone", affiliateService, StringComparison.Ordinal);
        Assert.Contains("order.OnlineCustomerId", affiliateService, StringComparison.Ordinal);
        Assert.Contains("order.CustomerId", affiliateService, StringComparison.Ordinal);
        Assert.Contains("_context.AffiliateUsers", affiliateService, StringComparison.Ordinal);
        Assert.Contains("_context.Users", affiliateService, StringComparison.Ordinal);
        Assert.Contains("NormalizeEmailOrNull", affiliateService, StringComparison.Ordinal);
        Assert.Contains("NormalizePhoneOrNull", affiliateService, StringComparison.Ordinal);
        Assert.Contains("same phone identity", affiliateService, StringComparison.Ordinal);
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
