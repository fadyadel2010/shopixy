using System.IO;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PointOfSale.Tests.Architecture;

public sealed class CheckoutCouponCqrsConsistencyTests
{
    [Fact]
    public void CouponUsageService_MustFailClosed_ForLegacyConsumption_AndDelegateValidation()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Coupons",
            "CouponUsageService.cs");

        Assert.Contains("_couponValidationService.ValidateCouponByIdAsync(", source);
        Assert.Contains("COUPON_EARLY_CONSUMPTION_REMOVED", source);
        Assert.Contains("throw new CouponAuthorityException", source);
        Assert.DoesNotContain("_dbContext.CouponUsages.Add", source);
        Assert.DoesNotContain("CouponConsumedEvent", source);
        Assert.DoesNotMatch(
            new Regex(@"return\s+Task\.FromResult\s*\(\s*new\s+CouponUsage", RegexOptions.None),
            source);
    }

    [Fact]
    public void CheckoutService_MustCommitCouponPromotionReservationsAndProjectionEvents_BeforeTransactionCommit()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");
        var createOrderSource = ExtractCreateOrderMethodSource(source);

        var couponIndex = createOrderSource.IndexOf("await _couponValidationService.ReserveCouponAsync(", StringComparison.Ordinal);
        var promotionIndex = createOrderSource.IndexOf("await ReserveAppliedOfferPromotionAsync(", StringComparison.Ordinal);
        var orderProjectionIndex = createOrderSource.IndexOf("await EmitOrderCreatedEventAsync(", StringComparison.Ordinal);
        var rollbackIndex = createOrderSource.IndexOf("await transaction.RollbackAsync(", StringComparison.Ordinal);
        var commitIndex = createOrderSource.IndexOf("await transaction.CommitAsync(cancellationToken);", StringComparison.Ordinal);

        Assert.True(couponIndex > 0, "Expected transactional coupon reservation in CreateOrderAsync.");
        Assert.True(promotionIndex > couponIndex, "Expected offer reservation after coupon reservation in CreateOrderAsync.");
        Assert.True(orderProjectionIndex > promotionIndex, "Expected order projection enqueue after promotional state is persisted.");
        Assert.DoesNotContain("await EmitShipmentCreatedEventAsync(", createOrderSource);
        Assert.DoesNotContain("await EmitShipmentExternalRegistrationRequestedEventAsync(", createOrderSource);
        Assert.True(commitIndex > orderProjectionIndex, "Expected transaction commit after coupon/promotion/order work.");
        Assert.True(rollbackIndex > 0, "Expected explicit rollback path for the checkout transaction.");
    }

    [Fact]
    public void ShipmentOrchestration_MustRunAfterCheckoutCommit_AndNeverCallCarrierInsideCheckoutTransaction()
    {
        var checkoutSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");
        var consumerSource = ReadRepoFile(
            "PointOfSale.API",
            "Consumers",
            "OrderShipmentOrchestrationConsumer.cs");
        var methodBody = ExtractCreateOrderMethodSource(checkoutSource);

        var commitIndex = methodBody.IndexOf("await transaction.CommitAsync(cancellationToken);", StringComparison.Ordinal);
        Assert.True(commitIndex > 0, "Expected transaction commit inside CreateOrderAsync method body.");

        // Extract the transactional region from method start to commit
        var transactionalRegion = methodBody.Substring(0, commitIndex);

        var shipmentCreatedIndex = transactionalRegion.IndexOf("await EmitShipmentCreatedEventAsync(", StringComparison.Ordinal);
        var externalRegistrationRequestIndex = transactionalRegion.IndexOf("await EmitShipmentExternalRegistrationRequestedEventAsync(", StringComparison.Ordinal);
        var directCarrierExecutionIndex = transactionalRegion.IndexOf("ExecuteCarrierForExistingShipmentAsync(", StringComparison.Ordinal);

        Assert.Equal(-1, shipmentCreatedIndex);
        Assert.Equal(-1, externalRegistrationRequestIndex);
        Assert.Equal(-1, directCarrierExecutionIndex);
        Assert.Contains("IConsumer<OrderConfirmedEvent>", consumerSource);
        Assert.Contains("IConsumer<OrderPaidEvent>", consumerSource);
    }

    [Fact]
    public void CheckoutService_MustBindIdempotencyRecordToOrderBeforeTransactionCommit()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");
        var createOrderSource = ExtractCreateOrderMethodSource(source);
        
        var orderBindingIndex = createOrderSource.IndexOf(
            "await _checkoutRequestIdempotencyCoordinator.BindOrderCreatedForCommitAsync(",
            StringComparison.Ordinal);
        var saveChangesIndex = createOrderSource.IndexOf(
            "await _dbContext.SaveChangesAsync(cancellationToken);",
            orderBindingIndex,
            StringComparison.Ordinal);
        Assert.True(saveChangesIndex >= 0, "Expected to find 'await _dbContext.SaveChangesAsync(cancellationToken);' in createOrderSource after the transactional idempotency bind.");
        
        var commitIndex = createOrderSource.IndexOf("await transaction.CommitAsync(cancellationToken);", saveChangesIndex, StringComparison.Ordinal);
        Assert.True(commitIndex >= 0, "Expected to find 'await transaction.CommitAsync(cancellationToken);' in createOrderSource after saveChangesIndex.");

        Assert.True(orderBindingIndex > 0, "Expected CreateOrderAsync to bind the idempotency record to the committed order.");
        Assert.True(saveChangesIndex > orderBindingIndex, "Expected SaveChangesAsync to persist the idempotency record after binding it to the order.");
        Assert.True(commitIndex > saveChangesIndex, "Expected idempotency completion to be persisted (SaveChangesAsync) before the checkout transaction commits.");
        var transactionalRegion = createOrderSource[..commitIndex];
        Assert.DoesNotContain("await _checkoutRequestIdempotencyCoordinator.MarkOrderCreatedAsync(", transactionalRegion, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutService_MustNotKeepLegacyCouponUsageChangedFlow()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        Assert.DoesNotContain("EmitCouponUsageChangedEventAsync(", source);
        Assert.DoesNotContain("CouponUsageChangedEvent emitted", source);
        Assert.DoesNotContain("await ConsumeAppliedOfferPromotionAsync(", source);
        Assert.Contains("await ReserveAppliedOfferPromotionAsync(", source);
    }

    [Fact]
    public void CheckoutService_MustRevalidateCouponsThroughSqlAuthorityBeforeCreateOrder()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");
        var processCheckoutSource = ExtractMethodSource(source, "ProcessCheckoutAsync");
        var discountAuthoritySource = ExtractMethodSource(source, "ReconcileCheckoutDiscountAuthorityAsync");
        var revalidateIndex = processCheckoutSource.IndexOf(
            "context.PromotionCalculationFailed = await ReconcileCheckoutDiscountAuthorityAsync(",
            StringComparison.Ordinal);
        var createOrderIndex = processCheckoutSource.IndexOf(
            "var orderCreationResult = await CreateOrderAsync(",
            StringComparison.Ordinal);

        Assert.Contains("ValidateCouponByIdAsync(", discountAuthoritySource);
        Assert.True(revalidateIndex >= 0, "Expected checkout flow to revalidate discounts through SQL authority.");
        Assert.True(createOrderIndex >= 0, "Expected checkout flow to call CreateOrderAsync.");
        Assert.True(
            revalidateIndex < createOrderIndex,
            "Expected SQL-authoritative coupon revalidation to run before CreateOrderAsync in the checkout flow.");
    }

    [Fact]
    public void CouponAdapters_MustDelegateCustomerRuleEnforcement_ToSqlAuthority()
    {
        var storefrontSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Storefront",
            "StorefrontCouponService.cs");
        var checkoutValidationSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Coupons",
            "CouponUsageService.cs");
        var authoritySource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Discounts",
            "CouponValidationService.cs");

        Assert.Contains("_couponValidationService.ValidateCouponByCodeAsync(", storefrontSource);
        Assert.Contains("_couponValidationService.ValidateCouponByIdAsync(", checkoutValidationSource);
        Assert.DoesNotContain("CouponCustomerValidationRules.", storefrontSource);
        Assert.DoesNotContain("CouponCustomerValidationRules.", checkoutValidationSource);

        Assert.Contains("RequireAuthenticatedCustomer(customerId)", authoritySource);
        Assert.Contains("GetCustomerConfirmedUsageCountAsync", authoritySource);
        Assert.Contains("GetActiveCustomerReservationCountAsync", authoritySource);
        Assert.Contains("CustomerHasCompletedOrdersAsync", authoritySource);
        Assert.Contains("ValidateScope(", authoritySource);
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

    private static string ExtractCreateOrderMethodSource(string source)
    {
        return ExtractMethodSource(source, "CreateOrderAsync");
    }

    private static string ExtractMethodSource(string source, string methodName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var root = syntaxTree.GetRoot();

        var methodDeclaration = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName);

        Assert.True(methodDeclaration != null, $"Expected CheckoutService to declare {methodName}.");

        return methodDeclaration.ToFullString();
    }
}
