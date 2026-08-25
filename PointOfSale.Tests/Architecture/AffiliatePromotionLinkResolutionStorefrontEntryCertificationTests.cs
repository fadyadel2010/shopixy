namespace PointOfSale.Tests.Architecture;

public sealed class AffiliatePromotionLinkResolutionStorefrontEntryCertificationTests
{
    [Fact]
    public void StorefrontPromotionLinkAdmission_MustRemainBackendTenantAndLifecycleOwned()
    {
        var middleware = CertificationSource.ReadRepoFile("PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var frontendTracker = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/bootstrap/affiliateRefTracker.ts");
        var httpClient = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/services/api/httpClient.ts");

        Assert.Contains("l.ReferralCode == referralCode", middleware, StringComparison.Ordinal);
        Assert.Contains("l.TenantId == currentTenantId", middleware, StringComparison.Ordinal);
        Assert.Contains("IsProductCurrentlyAffiliateSelectableAsync(dbContext, product, context.RequestAborted)", middleware, StringComparison.Ordinal);
        Assert.Contains("policyWithActiveCategory", middleware, StringComparison.Ordinal);
        Assert.Contains("HasPositiveAffiliatePriceAsync", middleware, StringComparison.Ordinal);
        Assert.Contains("HasAvailableAffiliateInventoryAsync", middleware, StringComparison.Ordinal);
        Assert.Contains("AffiliateModeResolver.SetAffiliateModeV2", middleware, StringComparison.Ordinal);
        Assert.Contains("EnsureAttributionCookie", middleware, StringComparison.Ordinal);
        Assert.Contains("Protected promotion-link evidence has no current active SQL link", middleware, StringComparison.Ordinal);
        Assert.Contains("context.Response.Cookies.Delete(AttributionCookieName);", middleware, StringComparison.Ordinal);

        Assert.Contains("promotionLink.TenantId != tenantId", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadActiveAffiliatePromotionLinkAuthorityForFinalCheckoutAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PROMOTION_LINK_STALE", checkout, StringComparison.Ordinal);

        Assert.Contains("The header only HINTS to the middleware which ref code to validate", frontendTracker, StringComparison.Ordinal);
        Assert.Contains("config.headers['X-Affiliate-Ref'] = affiliateRef", httpClient, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordAfPa44()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/affiliate-promotion-link-resolution-storefront-entry-attribution-admission-certification-findings.md");

        foreach (var marker in new[]
        {
            "PASS AF-PA4.4",
            "Promotion Resolution Findings",
            "Storefront Entry Findings",
            "Attribution Findings",
            "Lifecycle Findings",
            "Multiple Referral Findings",
            "Cache Findings",
            "Failure Findings",
            "Adversarial Findings",
            "Storefront Admission Review",
            "Runtime Hardening Performed",
            "Regression Freezes Added",
            "Required Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS AF-PA4.4", CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA4.4", CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA4.4", CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA4.4", CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA4.4", CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerJourneyAttributionPersistence_MustClearStaleProtectedEvidenceAndKeepFrontendCandidateOnly()
    {
        var middleware = CertificationSource.ReadRepoFile("PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs");
        var evidence = CertificationSource.ReadRepoFile("PointOfSale.API/Security/AffiliateAttributionEvidenceTokenService.cs");
        var tracker = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/bootstrap/affiliateRefTracker.ts");
        var httpClient = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/services/api/httpClient.ts");
        var productList = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/ProductListPage.tsx");
        var filters = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/hooks/useDiscoveryFilters.ts");
        var findings = CertificationSource.ReadRepoFile("docs/architecture/affiliate-customer-journey-attribution-persistence-certification-findings.md");

        Assert.Contains("Cookie:first-click-protected", middleware, StringComparison.Ordinal);
        Assert.Contains("protectedEvidence?.PromotionLinkId.HasValue == true", middleware, StringComparison.Ordinal);
        Assert.Contains("Protected promotion-link evidence has no current active SQL link", middleware, StringComparison.Ordinal);
        Assert.Contains("Affiliate account missing or inactive after PromotionLink resolution", middleware, StringComparison.Ordinal);
        Assert.Contains("Protected promotion-link evidence no longer matches SQL authority", middleware, StringComparison.Ordinal);
        Assert.Contains("Protected affiliate evidence no longer matches SQL authority", middleware, StringComparison.Ordinal);
        Assert.Contains("context.Response.Cookies.Delete(AttributionCookieName);", middleware, StringComparison.Ordinal);

        Assert.Contains("HttpOnly = true", middleware, StringComparison.Ordinal);
        Assert.Contains("SameSite = SameSiteMode.Lax", middleware, StringComparison.Ordinal);
        Assert.Contains("MaxAge = TimeSpan.FromDays(30)", middleware, StringComparison.Ordinal);
        Assert.Contains("payload.TenantId != expectedTenantId", evidence, StringComparison.Ordinal);
        Assert.Contains("payload.ExpiresAtUtc <=", evidence, StringComparison.Ordinal);
        Assert.Contains("_protector.Unprotect(token)", evidence, StringComparison.Ordinal);

        Assert.Contains("The header only HINTS to the middleware which ref code to validate", tracker, StringComparison.Ordinal);
        Assert.Contains("sessionStorage.setItem(SESSION_KEY, refFromUrl)", tracker, StringComparison.Ordinal);
        Assert.Contains("config.headers['X-Affiliate-Ref'] = affiliateRef", httpClient, StringComparison.Ordinal);
        Assert.Contains("ProductCard", productList, StringComparison.Ordinal);
        Assert.Contains("useProductListDiscovery", productList, StringComparison.Ordinal);
        Assert.Contains("setSearchParams", filters, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS AF-PA5.1",
            "Journey Findings",
            "Persistence Findings",
            "Lifecycle Findings",
            "Cache Findings",
            "Failure Findings",
            "Adversarial Findings",
            "Journey Attribution Pipeline",
            "Runtime Hardening Performed",
            "Required Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS AF-PA5.1", CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.1", CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.1", CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.1", CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.1", CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void CartAttributionAdmission_MustRemainBackendLineOwnedAndMixedCartSafe()
    {
        var cartDto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Cart/CartDtos.cs");
        var cartService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cart/CartService.cs");
        var cartController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartController.cs");
        var funnelController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontFunnelController.cs");

        Assert.Contains("public bool IsAffiliateEligibleLine", cartDto, StringComparison.Ordinal);
        Assert.Contains("public Guid? AffiliateAccountId", cartDto, StringComparison.Ordinal);
        Assert.Contains("public string? AffiliateReferralCode", cartDto, StringComparison.Ordinal);
        Assert.Contains("public Guid? PromotionLinkId", cartDto, StringComparison.Ordinal);

        Assert.Contains("ResolveAffiliateCartLineAdmissionAsync", cartService, StringComparison.Ordinal);
        Assert.Contains("ApplyAffiliateCartLineAdmission(existingItem, lineAdmission)", cartService, StringComparison.Ordinal);
        Assert.Contains("ApplyAffiliateCartLineAdmission(item, refreshedLineAdmission)", cartService, StringComparison.Ordinal);
        Assert.Contains("ApplyAffiliateCartLineAdmission(\n                    item,\n                    AffiliateCartLineAdmission.NotEligible)", cartService.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("HasCurrentAffiliatePolicyAndCategoryAsync", cartService, StringComparison.Ordinal);
        Assert.Contains("HasPositiveAffiliateLinePriceAsync", cartService, StringComparison.Ordinal);
        Assert.Contains("HasAvailableAffiliateLineInventoryAsync", cartService, StringComparison.Ordinal);
        Assert.Contains("AffiliateCartLineAdmission.NotEligible", cartService, StringComparison.Ordinal);
        Assert.DoesNotContain("Blocked non-affiliate product from cart", cartService, StringComparison.Ordinal);

        Assert.Contains("AffiliateModeResolver.GetAffiliateAccountId(HttpContext)", cartController, StringComparison.Ordinal);
        Assert.Contains("AffiliateModeResolver.GetReferralCode(HttpContext)", cartController, StringComparison.Ordinal);
        Assert.Contains("AffiliateModeResolver.GetPromotionLinkId(HttpContext)", cartController, StringComparison.Ordinal);
        Assert.Contains("resolvedPromotionLinkId", funnelController, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordAfPa52()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/affiliate-cart-attribution-line-admission-mixed-cart-certification-findings.md");

        foreach (var marker in new[]
        {
            "PASS AF-PA5.2",
            "Cart Findings",
            "Line Admission Findings",
            "Mixed Cart Findings",
            "Lifecycle Findings",
            "Cache Findings",
            "Failure Findings",
            "Adversarial Findings",
            "Cart Admission Pipeline",
            "Runtime Hardening Performed",
            "Required Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS AF-PA5.2", CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.2", CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.2", CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.2", CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.2", CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutAttributionOrderLineAdmission_MustRemainBackendOwnedAndMixedOrderSafe()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("AffiliateOrderLineAdmissionSnapshot", checkout, StringComparison.Ordinal);
        Assert.Contains("PromotionLinkProductId", checkout, StringComparison.Ordinal);
        Assert.Contains("RevalidateResolvedAffiliateAuthorityInsideFinalTransactionAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadActiveAffiliateAccountAuthorityForFinalCheckoutAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadActiveAffiliatePromotionLinkAuthorityForFinalCheckoutAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("resolvedAffiliateAuthority.PromotionLinkProductId.Value", checkout, StringComparison.Ordinal);
        Assert.Contains("join product in _dbContext.Products.IgnoreQueryFilters()", checkout, StringComparison.Ordinal);
        Assert.Contains("join category in _dbContext.AffiliateCategories.IgnoreQueryFilters()", checkout, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus == PublishStatus.Published", checkout, StringComparison.Ordinal);
        Assert.Contains("product.IsOnlineVisible", checkout, StringComparison.Ordinal);
        Assert.Contains("category.IsActive", checkout, StringComparison.Ordinal);
        Assert.Contains("orderItem.AffiliateCommissionEligible = shouldPersistAffiliateCommissionSnapshot", checkout, StringComparison.Ordinal);
        Assert.Contains("orderItem.AffiliateCommissionPolicyIdSnapshot = shouldPersistAffiliateCommissionSnapshot ? affiliatePolicySnapshot!.PolicyId : null", checkout, StringComparison.Ordinal);
        Assert.Contains("orderItem.AffiliateCommissionAmountSnapshot = shouldPersistAffiliateCommissionSnapshot ? affiliateCommissionAmount : null", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("item.IsAffiliateEligibleLine", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot.AffiliateAccountId", checkout, StringComparison.Ordinal);

        Assert.Contains("CK_OrderItems_AffiliateCommissionSnapshot_Valid", dbContext, StringComparison.Ordinal);
        Assert.Contains("\\\"AffiliateCommissionEligible\\\" = true", dbContext, StringComparison.Ordinal);
        Assert.Contains("\\\"AffiliateCommissionEligible\\\" = false", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordAfPa53()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/affiliate-checkout-attribution-order-line-admission-commission-candidate-certification-findings.md");

        foreach (var marker in new[]
        {
            "PASS AF-PA5.3",
            "Checkout Findings",
            "Order Line Findings",
            "Commission Candidate Findings",
            "Mixed Order Findings",
            "Financial Findings",
            "Lifecycle Findings",
            "Cache Findings",
            "Failure Findings",
            "Adversarial Findings",
            "Checkout Admission Pipeline",
            "Runtime Hardening Performed",
            "Required Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS AF-PA5.3", CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.3", CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.3", CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.3", CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md"), StringComparison.Ordinal);
        Assert.Contains("PASS AF-PA5.3", CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md"), StringComparison.Ordinal);
    }
}
