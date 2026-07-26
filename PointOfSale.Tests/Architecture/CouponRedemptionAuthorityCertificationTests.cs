using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PointOfSale.Tests.Architecture;

public sealed class CouponRedemptionAuthorityCertificationTests
{
    [Fact]
    public void StorefrontCouponEntry_MustSubmitCandidatesOnly_AndPresentAcceptedCartDto()
    {
        var input = ReadRepoFile("PointOfSale.Storefront/src/components/cart/CouponInput.tsx");
        var store = ReadRepoFile("PointOfSale.Storefront/src/store/cartStore.ts");
        var api = ReadRepoFile("PointOfSale.Storefront/src/services/api/storefrontApi.ts");

        Assert.Contains("const success = await applyCoupon(normalizedCode);", input, StringComparison.Ordinal);
        Assert.Contains("const success = await applyCoupon(null, normalizedRedeemToken);", input, StringComparison.Ordinal);
        Assert.Contains("const cart = await applyCouponApi(code, smartOfferRedeemToken);", store, StringComparison.Ordinal);
        Assert.Contains("set({", store, StringComparison.Ordinal);
        Assert.Contains("cart,", store, StringComparison.Ordinal);
        Assert.Contains("hydrationSource: 'api'", store, StringComparison.Ordinal);
        Assert.Contains("httpClient.post<CartDto>(ENDPOINTS.CART_APPLY_COUPON, request)", api, StringComparison.Ordinal);
        Assert.Contains("httpClient.post<CartDto>(ENDPOINTS.CART_REMOVE_COUPON, {})", api, StringComparison.Ordinal);
        Assert.DoesNotContain("CouponUsage", input, StringComparison.Ordinal);
        Assert.DoesNotContain("maxUsage", input, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", input, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CartCouponEndpoints_MustValidateThroughBackendAuthority_AndPersistOnlyServerAcceptedCouponState()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontCouponService.cs");

        Assert.Contains("[HttpPost(\"apply-coupon\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[EnableRateLimiting(\"CouponRateLimit\")]", controller, StringComparison.Ordinal);
        Assert.Contains("CouponErrorCodes.AuthenticationRequired", controller, StringComparison.Ordinal);
        Assert.Contains("_couponService.ValidateCouponAsync(", controller, StringComparison.Ordinal);
        Assert.Contains("CouponId = couponResult.CouponId!.Value", controller, StringComparison.Ordinal);
        Assert.Contains("cart.ApplyCouponDiscount(couponInfo);", controller, StringComparison.Ordinal);
        Assert.Contains("await PersistCouponApplicationAsync(", controller, StringComparison.Ordinal);
        Assert.Contains("cart = await RevalidateCouponForResponseAsync(", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"remove-coupon\")]", controller, StringComparison.Ordinal);
        Assert.Contains("cart.ClearCoupon();", controller, StringComparison.Ordinal);
        Assert.Contains("await PersistCouponRemovalAsync(", controller, StringComparison.Ordinal);

        Assert.Contains("_couponValidationService.ValidateCouponByCodeAsync(", service, StringComparison.Ordinal);
        Assert.Contains("GetCustomerConfirmedUsageCountAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("_dbContext.CouponUsages.Add", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("_dbContext.CouponReservations.Add", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutCouponRedemption_MustRevalidateReserveAndPersistInsideSqlTransaction()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var createOrder = ExtractMethodSource(checkout, "CreateOrderAsync");

        var transactionIndex = createOrder.IndexOf("BeginTransactionAsync(", StringComparison.Ordinal);
        var revalidateIndex = createOrder.IndexOf("RevalidateCheckoutAuthorityInsideTransactionAsync(", StringComparison.Ordinal);
        var saveOrderIndex = createOrder.IndexOf("_dbContext.Orders.Add(order);", StringComparison.Ordinal);
        var reserveIndex = createOrder.IndexOf("await _couponValidationService.ReserveCouponAsync(", StringComparison.Ordinal);
        var commitIndex = createOrder.IndexOf("await transaction.CommitAsync(cancellationToken);", StringComparison.Ordinal);

        Assert.True(transactionIndex >= 0, "Checkout must begin a SQL transaction before coupon reservation.");
        Assert.True(revalidateIndex > transactionIndex, "Checkout must revalidate coupon/checkout authority inside the final transaction.");
        Assert.True(saveOrderIndex > revalidateIndex, "Order SQL persistence must follow final authority revalidation.");
        Assert.True(reserveIndex > saveOrderIndex, "Coupon reservation must bind to the persisted backend order identity.");
        Assert.True(commitIndex > reserveIndex, "Coupon reservation must commit atomically with order persistence.");
        Assert.Contains("IsolationLevel.Serializable", createOrder, StringComparison.Ordinal);
        Assert.Contains("order.CouponId.Value", createOrder, StringComparison.Ordinal);
        Assert.Contains("order.CouponCode", createOrder, StringComparison.Ordinal);
    }

    [Fact]
    public void CouponUsageConfirmation_MustBeSqlOwnedIdempotentAndProjectionDerivative()
    {
        var couponAuthority = ReadRepoFile("PointOfSale.Infrastructure/Discounts/CouponValidationService.cs");
        var paymentLifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var usageEntity = ReadRepoFile("PointOfSale.Core/Entities/CouponUsage.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CouponProjectionConsumer.cs");

        Assert.Contains("ConfirmCouponUsageAsync(", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalMutation(\"Coupon confirmation\")", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("await LockCouponForCapacityMutationAsync", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("ValidateCouponByIdAsync(", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CouponUsages.Add(couponUsage);", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("IsDuplicateUsageOrderException", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("SetCouponCurrentUsageAsync", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("CouponConsumedEvent", couponAuthority, StringComparison.Ordinal);
        Assert.Contains("OrderId is UNIQUE", usageEntity, StringComparison.Ordinal);
        Assert.Contains("ProjectCouponUsageFromSqlAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CouponUsages", projection, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
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
