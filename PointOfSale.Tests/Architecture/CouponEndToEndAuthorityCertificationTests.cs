namespace PointOfSale.Tests.Architecture;

public sealed class CouponEndToEndAuthorityCertificationTests
{
    [Fact]
    public void CompleteCouponLifecycle_MustRemainBackendOwnedFromCreationThroughRecovery()
    {
        var couponsController = ReadRepoFile("PointOfSale.API/Controllers/CouponsController.cs");
        var storefrontCartController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartController.cs");
        var storefrontCouponService = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontCouponService.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var couponAuthority = ReadRepoFile("PointOfSale.Infrastructure/Discounts/CouponValidationService.cs");
        var paymentLifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CouponProjectionConsumer.cs");

        Assert.Contains("CreateCoupon(CreateCouponDto dto", couponsController, StringComparison.Ordinal);
        Assert.Contains("UpdateCoupon(Guid id, UpdateCouponDto dto", couponsController, StringComparison.Ordinal);
        Assert.Contains("ValidateAndCanonicalizeCouponEligibilityAsync", couponsController, StringComparison.Ordinal);
        Assert.Contains("ParseRequiredCouponIfMatch", couponsController, StringComparison.Ordinal);

        Assert.Contains("_couponService.ValidateCouponAsync(", storefrontCartController, StringComparison.Ordinal);
        Assert.Contains("await PersistCouponApplicationAsync(", storefrontCartController, StringComparison.Ordinal);
        Assert.Contains("cart = await RevalidateCouponForResponseAsync(", storefrontCartController, StringComparison.Ordinal);
        Assert.Contains("_couponValidationService.ValidateCouponByCodeAsync(", storefrontCouponService, StringComparison.Ordinal);

        Assert.Contains("ReconcileCheckoutDiscountAuthorityAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidateCouponByIdAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("await _couponValidationService.ReserveCouponAsync(", checkoutService, StringComparison.Ordinal);

        Assert.Contains("EnsureTransactionalMutation(\"Coupon reservation\")", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalMutation(\"Coupon confirmation\")", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalMutation(\"Coupon release\")", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("IsDuplicateUsageOrderException", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("CouponConsumedEvent", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("ConfirmCouponUsageAsync(", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("ReleaseCouponReservationAsync(", paymentLifecycle, StringComparison.Ordinal);

        Assert.Contains("LoadSqlCouponAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("ProjectCouponUsageFromSqlAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter(@event)", projection, StringComparison.Ordinal);
        Assert.Contains("BuildTombstoneFromSqlAuthorityAsync", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontAndAdminFrontends_MustRemainPresentationOnlyForCouponAuthority()
    {
        var couponInput = ReadRepoFile("PointOfSale.Storefront/src/components/cart/CouponInput.tsx");
        var cartStore = ReadRepoFile("PointOfSale.Storefront/src/store/cartStore.ts");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var adminCouponsApi = ReadRepoFile("PointOfSale.Web/src/services/adminCouponsApi.js");
        var adminCoupons = ReadRepoFile("PointOfSale.Web/src/pages/Coupons.jsx");
        var adminCouponAuthority = ReadRepoFile("PointOfSale.Web/src/auth/adminCouponReadAuthority.js");

        Assert.Contains("const success = await applyCoupon(normalizedCode);", couponInput, StringComparison.Ordinal);
        Assert.Contains("const success = await applyCoupon(null, normalizedRedeemToken);", couponInput, StringComparison.Ordinal);
        Assert.Contains("const cart = await applyCouponApi(code, smartOfferRedeemToken);", cartStore, StringComparison.Ordinal);
        Assert.Contains("detectCartStateMismatch(cart)", cartStore, StringComparison.Ordinal);
        Assert.Contains("couponCode: cart.appliedCoupon?.code", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("requireAdminCouponReadAuthority(response.data, currentUser)", adminCouponsApi, StringComparison.Ordinal);
        Assert.Contains("mutationOptionsFromCoupon(coupon)", adminCouponsApi, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", adminCouponsApi, StringComparison.Ordinal);
        Assert.Contains("await getAdminCoupon(couponId, user)", adminCoupons, StringComparison.Ordinal);
        Assert.Contains("Coupon lifecycle mutation requires accepted backend RowVersion evidence.", adminCouponsApi, StringComparison.Ordinal);
        Assert.Contains("Coupon read authority rejected", adminCouponAuthority, StringComparison.Ordinal);

        Assert.DoesNotContain("CouponReservation", couponInput, StringComparison.Ordinal);
        Assert.DoesNotContain("CouponUsage", couponInput, StringComparison.Ordinal);
        Assert.DoesNotContain("CouponReservation", cartStore, StringComparison.Ordinal);
        Assert.DoesNotContain("CouponUsage", cartStore, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", couponInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", couponInput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CouponProjectionAndSourceDocumentation_MustNotPromoteMongoToAuthority()
    {
        var couponsController = ReadRepoFile("PointOfSale.API/Controllers/CouponsController.cs");
        var storefrontCouponService = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontCouponService.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CouponProjectionConsumer.cs");
        var repository = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/CouponReadRepository.cs");
        var repositoryInterface = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/ICouponReadRepository.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("PostgreSQL is the source of truth for coupon identity, lifecycle, eligibility, and usage", couponsController, StringComparison.Ordinal);
        Assert.Contains("SQL-authoritative coupon validation service", storefrontCouponService, StringComparison.Ordinal);
        Assert.Contains("MongoDB is derivative visibility/read-model evidence only and cannot accept coupon authority", projection, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL remains authoritative for coupon validation, pricing, reservation, and usage", repository, StringComparison.Ordinal);
        Assert.Contains("StorefrontCouponService MUST re-enter SQL-backed coupon validation for accepted state", repositoryInterface, StringComparison.Ordinal);
        Assert.Contains("Accepted coupon validation/pricing/reservation/usage remains SQL-backed.", program, StringComparison.Ordinal);

        Assert.DoesNotContain("StorefrontCouponService reads from MongoDB", couponsController, StringComparison.Ordinal);
        Assert.DoesNotContain("StorefrontCouponService reads from MongoDB", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MongoDB is the READ-ONLY source for Storefront", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("PostgreSQL is write-only for coupons", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("PostgreSQL is write-only for coupons", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("StorefrontCouponService MUST use this repository, NEVER PostgreSQL", repositoryInterface, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordCompleteCouponEndToEndAuthorityTruth()
    {
        Assert.Contains(
            "PASS SF-CPN.FINAL Complete Coupon End-to-End Authority Certification",
            ReadRepoFile("docs/architecture/coupon-end-to-end-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-CPN.FINAL", ReadRepoFile(path), StringComparison.Ordinal);
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
}
