using System.IO;
using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

public sealed class CheckoutPromotionVisibilityTests
{
    [Fact]
    public void CheckoutService_MustFailClosed_WhenPromotionCalculationFails()
    {
        var serviceSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");
        var contractSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "ICheckoutService.cs");
        var discountAuthorityMethod = ExtractMethodBody(serviceSource, "ReconcileCheckoutDiscountAuthorityAsync");

        Assert.Contains("PROMOTION_CALCULATION_FAILED", contractSource);
        Assert.Contains("ApplyPromotionResultOrThrow", serviceSource);
        Assert.Contains("RecordPromotionFailClosed", serviceSource);
        Assert.Contains("throw CreatePromotionCalculationFailureException(mode, correlationId, exception);", serviceSource);
        Assert.Contains("ErrorMessage = \"Promotion calculation failed\"", serviceSource);
        Assert.NotNull(discountAuthorityMethod);
        Assert.Contains("return ApplyPromotionResultOrThrow(", discountAuthorityMethod!, StringComparison.Ordinal);
        Assert.Contains("promotionResult: null", discountAuthorityMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("proceeding with degraded checkout", discountAuthorityMethod!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ClearAllPromotionFields(snapshot);", discountAuthorityMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("return true; // Promotion calculation failed (soft-failure)", discountAuthorityMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyPromotionFallbackOrResultAsync", serviceSource);
        Assert.DoesNotContain("ShouldFailCheckoutOnPromotionErrorAsync", serviceSource);
        Assert.DoesNotContain("RecordPromotionFailOpen", serviceSource);
        Assert.DoesNotContain("continuing with fail-open", serviceSource);
    }

    [Fact]
    public void StorefrontCheckout_MustSubmitAuthoritativeExpectedPricing()
    {
        var typesSource = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "types",
            "checkout.ts");
        var storeSource = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "store",
            "checkoutStore.ts");

        // Verify CheckoutExpectedPricing interface exists
        Assert.Matches(@"export\s+interface\s+CheckoutExpectedPricing", typesSource);
        Assert.Matches(@"expectedPricing\s*:\s*CheckoutExpectedPricing", typesSource);

        // CRITICAL: Verify the submit path fails closed on invalid pricing before posting expectedPricing.
        var submitHandlerIndex = storeSource.IndexOf("submitCheckout: async", StringComparison.Ordinal);
        Assert.True(submitHandlerIndex >= 0, "submitCheckout handler not found in checkoutStore.ts");

        var pricingGateIndex = storeSource.IndexOf(
            "latestState.pricingLoading || !latestState.pricing || latestState.pricingError",
            submitHandlerIndex,
            StringComparison.Ordinal);
        var expectedPricingIndex = storeSource.IndexOf(
            "expectedPricing: buildExpectedPricing(latestState.pricing)",
            submitHandlerIndex,
            StringComparison.Ordinal);
        var postIndex = storeSource.IndexOf(
            "httpClient.post<CheckoutResponse>",
            submitHandlerIndex,
            StringComparison.Ordinal);

        Assert.True(pricingGateIndex > submitHandlerIndex, "submitCheckout must gate on pricing freshness.");
        Assert.True(expectedPricingIndex > pricingGateIndex, "submitCheckout must build expectedPricing after the pricing gate.");
        Assert.True(postIndex > expectedPricingIndex, "submitCheckout must submit authoritative expectedPricing to the API.");

        // Verify error constants for promotion calculation and checkout total mismatch
        Assert.Contains("PROMOTION_CALCULATION_FAILED", storeSource);
        Assert.Contains("CHECKOUT_TOTAL_MISMATCH", storeSource);
    }

    [Fact]
    public void CheckoutPromotionUsageMutation_MustRevalidateSqlAuthority_BeforeUsageWrite()
    {
        var paymentLifecycleSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Payments",
            "PaymentLifecycleService.cs");
        var confirmUsageMethod = ExtractMethodBody(paymentLifecycleSource, "ConfirmOfferPromotionUsageAsync");
        var usageAuthorityMethod = ExtractMethodBody(paymentLifecycleSource, "EnsurePromotionUsageAuthorityStillValidAsync");

        Assert.NotNull(confirmUsageMethod);
        Assert.NotNull(usageAuthorityMethod);

        var authorityIndex = confirmUsageMethod!.IndexOf("await EnsurePromotionUsageAuthorityStillValidAsync(", StringComparison.Ordinal);
        var usageWriteIndex = confirmUsageMethod.IndexOf("_db.PromotionUsages.Add(usage);", StringComparison.Ordinal);
        var currentUsageMutationIndex = confirmUsageMethod.IndexOf("promotion.CurrentUsage = confirmedUsageCount;", StringComparison.Ordinal);
        var publishAppliedIndex = confirmUsageMethod.IndexOf("await _eventPublisher.PublishAsync(new PromotionAppliedEvent", StringComparison.Ordinal);

        Assert.True(authorityIndex > 0, "Offer usage confirmation must revalidate current SQL promotion authority.");
        Assert.True(usageWriteIndex > authorityIndex, "Promotion usage must be written only after authority revalidation.");
        Assert.True(currentUsageMutationIndex > usageWriteIndex, "Offer usage count must mutate the locked SQL promotion row after the durable usage claim.");
        Assert.True(publishAppliedIndex > currentUsageMutationIndex, "PromotionAppliedEvent must be staged after the tracked SQL promotion mutation so its version advances.");
        Assert.DoesNotContain("ExecuteUpdateAsync", confirmUsageMethod, StringComparison.Ordinal);
        Assert.Contains("advance ProjectionMutationVersion", confirmUsageMethod, StringComparison.Ordinal);
        Assert.Contains("promotion.Validate();", usageAuthorityMethod!, StringComparison.Ordinal);
        Assert.Contains("!promotion.IsActive || promotion.StartDate > nowUtc || promotion.EndDate < nowUtc", usageAuthorityMethod, StringComparison.Ordinal);
        Assert.Contains("promotion.MaxUsage.HasValue && confirmedUsageCount >= promotion.MaxUsage.Value", usageAuthorityMethod, StringComparison.Ordinal);
        Assert.Contains("CalculateCurrentPromotionDiscountForOrderAsync(", usageAuthorityMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPromoConsumption_MustReloadSqlAuthority_BeforeUsageWrite()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "PromoService.cs");
        var consumeMethod = ExtractMethodBody(source, "ConsumePromoAsync");

        Assert.NotNull(consumeMethod);

        var reloadIndex = consumeMethod!.IndexOf("LoadPromoForConsumptionAuthorityAsync(", StringComparison.Ordinal);
        var validationIndex = consumeMethod.IndexOf("TryGetPromoValidationFailure(", StringComparison.Ordinal);
        var usageWriteIndex = consumeMethod.IndexOf("_dbContext.PromoCodeUsages.Add(new PromoCodeUsage", StringComparison.Ordinal);

        Assert.True(reloadIndex > 0, "CommerceHub promo consumption must reload current SQL promo authority.");
        Assert.True(validationIndex > reloadIndex, "CommerceHub promo consumption must validate the reloaded promo.");
        Assert.True(usageWriteIndex > validationIndex, "CommerceHub promo usage must be written only after current SQL validation.");
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("authoritativePromo", consumeMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void CouponProjectionDelete_MustWriteTombstone_WhenPriorProjectionIsMissing()
    {
        var source = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Consumers",
            "CouponProjectionConsumer.cs");
        var deleteMethod = ExtractMethodBody(source, "Consume");

        Assert.NotNull(deleteMethod);
        Assert.Contains("ProjectionTombstoneGuard.IsTombstoneNewerThanOrEqualToEvent", source, StringComparison.Ordinal);
        Assert.Contains("existingDocument ?? new CouponDocument", source, StringComparison.Ordinal);
        Assert.Contains("Always write a tombstone", source, StringComparison.Ordinal);
        Assert.Contains("tombstone.IsActive = false;", source, StringComparison.Ordinal);
        Assert.Contains("tombstone.DeletedAtUtc = sqlCoupon?.UpdatedAt ?? @event.OccurredAtUtc;", source, StringComparison.Ordinal);
        Assert.Contains("BuildTombstoneFromSqlAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCouponAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("MapSqlAuthorityToDocument(sqlCoupon, @event)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Document not found for soft delete", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var pathParts = new List<string>
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."
        };

        pathParts.AddRange(segments);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(pathParts.ToArray())));
    }

    private static string? ExtractMethodBody(string source, string methodName)
    {
        var methodPattern = Regex.Escape(methodName) + @"\s*\(";
        foreach (Match match in Regex.Matches(source, methodPattern))
        {
            var openParenIndex = source.IndexOf('(', match.Index);
            if (openParenIndex == -1)
            {
                continue;
            }

            var closeParenIndex = FindMatching(source, openParenIndex, '(', ')');
            if (closeParenIndex == -1)
            {
                continue;
            }

            var openBraceIndex = FindMethodOpeningBrace(source, closeParenIndex + 1);
            if (openBraceIndex == -1)
            {
                continue;
            }

            var closeBraceIndex = FindMatching(source, openBraceIndex, '{', '}');
            if (closeBraceIndex != -1)
            {
                return source.Substring(match.Index, closeBraceIndex - match.Index + 1);
            }
        }

        return null;
    }

    private static int FindMatching(string source, int openIndex, char open, char close)
    {
        var depth = 1;
        for (var i = openIndex + 1; i < source.Length; i++)
        {
            if (source[i] == open)
            {
                depth++;
            }
            else if (source[i] == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int FindMethodOpeningBrace(string source, int startIndex)
    {
        for (var i = startIndex; i < source.Length; i++)
        {
            if (char.IsWhiteSpace(source[i]))
            {
                continue;
            }

            return source[i] == '{' ? i : -1;
        }

        return -1;
    }
}
