using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PointOfSale.Tests.Architecture;

public sealed class CouponFinancialAuthorityCertificationTests
{
    [Fact]
    public void FrontendCouponFinancialPresentation_MustSubmitCandidatesOnly_AndRenderBackendPricing()
    {
        var couponInput = ReadRepoFile("PointOfSale.Storefront/src/components/cart/CouponInput.tsx");
        var cartStore = ReadRepoFile("PointOfSale.Storefront/src/store/cartStore.ts");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var orderSummary = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx");
        var reviewStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");

        Assert.Contains("const success = await applyCoupon(normalizedCode);", couponInput, StringComparison.Ordinal);
        Assert.Contains("const success = await applyCoupon(null, normalizedRedeemToken);", couponInput, StringComparison.Ordinal);
        Assert.Contains("const cart = await applyCouponApi(code, smartOfferRedeemToken);", cartStore, StringComparison.Ordinal);
        Assert.Contains("cart,", cartStore, StringComparison.Ordinal);
        Assert.Contains("hydrationSource: 'api'", cartStore, StringComparison.Ordinal);

        Assert.Contains("PRICING: '/api/storefront/checkout/pricing'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const response = await httpClient.post<CheckoutPricingBreakdown>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("pricing: response.data", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("if (latestState.pricingLoading || !latestState.pricing || latestState.pricingError)", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("Checkout displays ONLY server-quoted pricing", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.couponDiscount, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("Checkout displays ONLY backend pricing quotes", reviewStep, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.couponDiscount, currency)", reviewStep, StringComparison.Ordinal);

        Assert.DoesNotContain("DiscountValue", couponInput, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxDiscount", couponInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MinimumOrder", couponInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", couponInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", couponInput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackendCouponFinancialAuthority_MustValidateCalculateAndCanonicalizeBeforeAcceptedPricing()
    {
        var cartController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartController.cs");
        var couponValidation = ReadRepoFile("PointOfSale.Infrastructure/Discounts/CouponValidationService.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var pricingService = ReadRepoFile("PointOfSale.API/Services/Pricing/StorefrontPricingService.cs");

        Assert.Contains("_couponService.ValidateCouponAsync(", cartController, StringComparison.Ordinal);
        Assert.Contains("cart.ApplyCouponDiscount(couponInfo);", cartController, StringComparison.Ordinal);
        Assert.Contains("await PersistCouponApplicationAsync(", cartController, StringComparison.Ordinal);
        Assert.Contains("cart = await RevalidateCouponForResponseAsync(", cartController, StringComparison.Ordinal);

        Assert.Contains("DiscountValidation.GetCouponValidationError", couponValidation, StringComparison.Ordinal);
        Assert.Contains("coupon.MinOrderAmount.HasValue", couponValidation, StringComparison.Ordinal);
        Assert.Contains("CalculateDiscount(coupon, snapshot.OrderSubtotal)", couponValidation, StringComparison.Ordinal);
        Assert.Contains("coupon.DiscountType == DiscountType.Percentage", couponValidation, StringComparison.Ordinal);
        Assert.Contains("coupon.MaxDiscountAmount.HasValue", couponValidation, StringComparison.Ordinal);
        Assert.Contains("RoundCurrency(Math.Min(discount, orderSubtotal))", couponValidation, StringComparison.Ordinal);

        Assert.Contains("RecalculatePricesReadOnlyAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ReconcileCheckoutDiscountAuthorityAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidateCouponByIdAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("snapshot.DiscountTotal = couponValidation.CalculatedDiscountAmount ?? 0m;", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_storefrontPricingService.CalculateCheckoutAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CalculateCheckoutAsync", pricingService, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalCheckoutCouponFinancialAuthority_MustRevalidateInsideSerializableTransactionAndPersistSqlSnapshot()
    {
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var createOrder = ExtractMethodSource(checkoutService, "CreateOrderAsync");
        var transactionRevalidation = ExtractMethodSource(checkoutService, "RevalidateCheckoutAuthorityInsideTransactionAsync");
        var refreshOrder = ExtractMethodSource(checkoutService, "RefreshOrderAuthoritySnapshot");
        var expectedPricing = ExtractMethodSource(checkoutService, "EnsureExpectedPricingMatches");

        var transactionIndex = createOrder.IndexOf("BeginTransactionAsync(", StringComparison.Ordinal);
        var revalidateIndex = createOrder.IndexOf("RevalidateCheckoutAuthorityInsideTransactionAsync(", StringComparison.Ordinal);
        var refreshIndex = createOrder.IndexOf("RefreshOrderAuthoritySnapshot(", StringComparison.Ordinal);
        var saveOrderIndex = createOrder.IndexOf("_dbContext.Orders.Add(order);", StringComparison.Ordinal);
        var reserveCouponIndex = createOrder.IndexOf("await _couponValidationService.ReserveCouponAsync(", StringComparison.Ordinal);
        var commitIndex = createOrder.IndexOf("await transaction.CommitAsync(cancellationToken);", StringComparison.Ordinal);

        Assert.True(transactionIndex >= 0, "Checkout must begin a SQL transaction.");
        Assert.True(revalidateIndex > transactionIndex, "Final coupon/pricing authority must be revalidated inside the SQL transaction.");
        Assert.True(refreshIndex > revalidateIndex, "Order snapshot must refresh from transaction-scoped authority.");
        Assert.True(saveOrderIndex > refreshIndex, "Order persistence must follow backend financial canonicalization.");
        Assert.True(reserveCouponIndex > saveOrderIndex, "Coupon reservation must bind to persisted order identity.");
        Assert.True(commitIndex > reserveCouponIndex, "Coupon reservation must commit atomically with the order.");
        Assert.Contains("IsolationLevel.Serializable", createOrder, StringComparison.Ordinal);

        Assert.Contains("lockAuthorityRow: true", transactionRevalidation, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRows: true", transactionRevalidation, StringComparison.Ordinal);
        Assert.Contains("mode: \"checkout\"", transactionRevalidation, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", transactionRevalidation, StringComparison.Ordinal);

        Assert.Contains("order.DiscountAmount = couponDiscountAmount;", refreshOrder, StringComparison.Ordinal);
        Assert.Contains("order.TotalDiscountAmount = totalDiscountAmount;", refreshOrder, StringComparison.Ordinal);
        Assert.Contains("order.TaxAmount = taxAmount;", refreshOrder, StringComparison.Ordinal);
        Assert.Contains("order.Total = total;", refreshOrder, StringComparison.Ordinal);
        Assert.Contains("order.Currency = validatedOrderCurrency;", refreshOrder, StringComparison.Ordinal);
        Assert.Contains("order.PricingSnapshotJson = JsonSerializer.Serialize(new", refreshOrder, StringComparison.Ordinal);

        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"coupon_discount\"", expectedPricing, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"tax\"", expectedPricing, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"total\"", expectedPricing, StringComparison.Ordinal);
    }

    [Fact]
    public void CouponFinancialRecoveryAndCache_MustNotPromoteRedisMongoOrBrowserStateToAuthority()
    {
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var checkoutSessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var couponProjection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CouponProjectionConsumer.cs");

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.DoesNotContain("pricing", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("couponDiscount", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tax", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", localStoragePartialize, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("BuildReplayResponseFromExistingOrderAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.DiscountAmount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.TaxAmount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.Total", checkoutService, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_CART_REVALIDATION_FAILED", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("ProjectCouponUsageFromSqlAuthorityAsync", couponProjection, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CouponUsages", couponProjection, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordCouponFinancialAuthorityTruth()
    {
        Assert.Contains(
            "PASS SF-CPN5 Coupon Financial Authority Certification",
            ReadRepoFile("docs/architecture/coupon-financial-authority-certification-findings.md"),
            StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("PASS SF-CPN5", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }

    private static string ExtractMethodSource(string source, string methodName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var root = syntaxTree.GetRoot();
        var methodDeclaration = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.Text == methodName);

        Assert.True(methodDeclaration != null, $"Expected source to contain method {methodName}.");
        return methodDeclaration!.ToFullString();
    }
}
