namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontCheckoutSubmissionAuthorityCertificationTests
{
    [Fact]
    public void StorefrontCheckoutSubmissionFrontend_MustSubmitIntentOnly()
    {
        var checkoutPage = ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var checkoutTypes = ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var reviewStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");
        var activeOrderContext = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/activeCheckoutOrderContext.ts");
        var paymentSessionAuthority = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");

        Assert.Contains("const handlePlaceOrder = useCallback(async () =>", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("const success = await submitCheckout();", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("<ReviewStep onPlaceOrder={handlePlaceOrder}", checkoutPage, StringComparison.Ordinal);

        Assert.Contains("export interface CheckoutRequest", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("cartId: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("shippingAddress: AddressDto;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: PaymentMethod;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: CheckoutExpectedPricing;", checkoutTypes, StringComparison.Ordinal);
        Assert.DoesNotContain("orderId:", Slice(checkoutTypes, "export interface CheckoutRequest", "export interface CheckoutPricingPreviewRequest"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orderNumber:", Slice(checkoutTypes, "export interface CheckoutRequest", "export interface CheckoutPricingPreviewRequest"), StringComparison.OrdinalIgnoreCase);

        Assert.Contains("submitCheckout: async () =>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("if (state.isSubmitting)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("await useCartStore.getState().fetchCart();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("await get().refreshPricingPreview();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const finalRequest: CheckoutRequest = {", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("cartId: cart.cartId", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingAddress,", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: latestState.shippingMethodId", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: latestState.paymentMethod", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("httpClient.post<CheckoutResponse>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_ENDPOINTS.CHECKOUT", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("requireStorefrontPaymentSessionAuthority(response.data, latestState.paymentMethod, cart.cartId)", checkoutStore, StringComparison.Ordinal);

        Assert.DoesNotContain("orderId:", Slice(checkoutStore, "const finalRequest: CheckoutRequest = {", "};", "const finalRequest"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orderNumber:", Slice(checkoutStore, "const finalRequest: CheckoutRequest = {", "};", "const finalRequest"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("crypto.randomUUID()", Slice(checkoutStore, "submitCheckout: async () =>", "resetError: () =>"), StringComparison.Ordinal);

        Assert.Contains("disabled={isSubmitting || !isPhoneVerified || pricingLoading || !pricing || !isReviewValid}", reviewStep, StringComparison.Ordinal);
        Assert.Contains("await onPlaceOrder();", reviewStep, StringComparison.Ordinal);

        Assert.Contains("return null;", activeOrderContext, StringComparison.Ordinal);
        Assert.Contains("Deliberately no-op", activeOrderContext, StringComparison.Ordinal);
        Assert.Contains("mutation data cannot establish persistent payment", activeOrderContext, StringComparison.Ordinal);

        Assert.Contains("Checkout responses are provider-handoff evidence, never durable frontend", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(response.orderId)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(response.orderNumber)", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("throw new Error('Backend checkout response did not prove valid payment-session presentation evidence.')", paymentSessionAuthority, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendCheckoutSubmission_MustOwnCreationIdentitySnapshotAndTransaction()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var order = ReadRepoFile("PointOfSale.Core/Entities/Order.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("[Route(\"api/storefront/checkout\")]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("[HttpPost]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("_checkoutService.ProcessCheckoutAsync(", checkoutController, StringComparison.Ordinal);
        Assert.Contains("ApplyGuestOrderAccessCookies(response);", checkoutController, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key: optional compatibility header only", checkoutController, StringComparison.Ordinal);

        Assert.Contains("public class CheckoutResponseDto", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public Guid OrderId { get; set; }", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public string OrderNumber { get; set; }", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public OrderTotalsDto Totals { get; set; } = new();", checkoutDtos, StringComparison.Ordinal);

        Assert.Contains("BuildServerCheckoutIdempotencyKey(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Operation = \"STOREFRONT_CHECKOUT_IDEMPOTENCY_V2\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CheckoutCartIdempotencySnapshotBuilder.BuildHash", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_checkoutRequestIdempotencyCoordinator.TryClaimAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("FindActiveOrderForCartAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", checkoutService, StringComparison.Ordinal);
        Assert.Contains("RevalidateCheckoutAuthorityInsideTransactionAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidateAndReserveInventoryAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.OrderNumber = await GenerateOrderNumberAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Orders.Add(order);", checkoutService, StringComparison.Ordinal);
        Assert.Contains("BindOrderCreatedForCommitAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync(cancellationToken);", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_checkoutRequestIdempotencyCoordinator.CompleteAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("IsActiveCartOrderUniqueViolation", checkoutService, StringComparison.Ordinal);

        Assert.Contains("public string OrderNumber { get; set; }", order, StringComparison.Ordinal);
        Assert.Contains("public Guid? CartId { get; set; }", order, StringComparison.Ordinal);
        Assert.Contains("public string? PricingSnapshotJson { get; set; }", order, StringComparison.Ordinal);
        Assert.Contains("public string PaymentMethodCode { get; set; }", order, StringComparison.Ordinal);
        Assert.Contains("public Guid? ShippingMethodId { get; set; }", order, StringComparison.Ordinal);

        Assert.Contains("IX_Orders_TenantId_CartId_ActiveStatuses", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendFinalRevalidation_MustFailClosedBeforeOrderCommit()
    {
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var idempotencyCoordinator = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutRequestIdempotencyCoordinator.cs");

        var finalRevalidation = Slice(
            checkoutService,
            "private async Task<TransactionScopedCheckoutAuthoritySnapshot> RevalidateCheckoutAuthorityInsideTransactionAsync",
            "private void RefreshOrderAuthoritySnapshot");

        Assert.Contains("ValidateShippingMethodAsync(", finalRevalidation, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRow: true", finalRevalidation, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentMethodAsync(", finalRevalidation, StringComparison.Ordinal);
        Assert.Contains("RecalculatePricesInternalAsync(", finalRevalidation, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRows: true", finalRevalidation, StringComparison.Ordinal);
        Assert.Contains("ReconcileCheckoutDiscountAuthorityAsync(", finalRevalidation, StringComparison.Ordinal);
        Assert.Contains("_storefrontPricingService.CalculateCheckoutAsync", finalRevalidation, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentMethodOrderAmountLimits(paymentMethod, pricing.FinalTotal", finalRevalidation, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", finalRevalidation, StringComparison.Ordinal);

        Assert.Contains("catch (CheckoutTotalMismatchException ex)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutOutOfStockException ex)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutConcurrencyConflictException ex)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutShippingMethodException ex)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutPaymentMethodException ex)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutStorefrontUnavailableException ex)", checkoutController, StringComparison.Ordinal);

        Assert.Contains("TryRecoverCompletedByRequestHashAsync", idempotencyCoordinator, StringComparison.Ordinal);
        Assert.Contains("CheckoutIdempotencyClaimStatus.InProgress", idempotencyCoordinator, StringComparison.Ordinal);
        Assert.Contains("CheckoutIdempotencyClaimStatus.ExistingOrder", idempotencyCoordinator, StringComparison.Ordinal);
        Assert.Contains("CheckoutIdempotencyClaimStatus.PayloadConflict", idempotencyCoordinator, StringComparison.Ordinal);
        Assert.Contains("Skipping checkout idempotency cleanup for durable completed record", idempotencyCoordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalWarehouseResolutionConflicts_MustNotBeWrappedAsOrderCreation500()
    {
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");

        var orderCreationCatch = Slice(
            checkoutService,
            "catch (Exception ex)",
            "// Only run pre-commit compensation if order was NOT committed",
            "activeStep = \"OrderCreation\";");

        Assert.Contains("ex is WarehouseResolutionException", orderCreationCatch, StringComparison.Ordinal);
        Assert.Contains("throw;", orderCreationCatch, StringComparison.Ordinal);
        Assert.Contains("catch (WarehouseResolutionException ex)", checkoutController, StringComparison.Ordinal);
        Assert.True(
            checkoutController.IndexOf("catch (WarehouseResolutionException ex)", StringComparison.Ordinal) <
            checkoutController.IndexOf("catch (CheckoutOrderCreationException ex)", StringComparison.Ordinal),
            "Warehouse resolution conflicts must be classified before ORDER_CREATION_FAILED.");
    }

    [Fact]
    public void OnlineProviderPostCommitRecovery_MustRequireRealProviderHandoff()
    {
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var paymobRuntime = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs");

        var recovery = Slice(
            checkoutService,
            "async Task<CheckoutResponseDto?> TryRecoverPaymentIntentPostCommitFailureAsync",
            "async Task<bool> TryCloseUnrecoverablePaymentIntentGapAsync");

        Assert.Contains("IsPostCommitPaymentRecoveryResponseSafe(recoveryResponse)", recovery, StringComparison.Ordinal);
        Assert.Contains("Checkout post-commit payment intent recovery did not produce a safe provider handoff", recovery, StringComparison.Ordinal);
        Assert.Contains("HasPaymobCheckoutHandoff(recoveryResponse)", recovery, StringComparison.Ordinal);
        Assert.Contains("return null;", recovery, StringComparison.Ordinal);

        Assert.Contains("EnsureReplayResponseContainsOnlineProviderHandoff(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("TryRegenerateReplayOnlineProviderHandoffAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Checkout replay regenerating online provider handoff after recoverable initialization failure", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Checkout replay regenerated online provider handoff", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Checkout replay blocked because online provider handoff is missing", checkoutService, StringComparison.Ordinal);
        Assert.Contains("\"PAYMENT_HANDOFF_MISSING\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("response.ExpiresAtUtc.Value <= DateTime.UtcNow", checkoutService, StringComparison.Ordinal);
        Assert.Contains("response.ExpiresAtUtc.Value <= DateTime.UtcNow", checkoutService, StringComparison.Ordinal);
        Assert.Contains("\"existing_order\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("\"stored_replay\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentStatus.Failed or PaymentIntentStatus.Cancelled or PaymentIntentStatus.Expired", checkoutService, StringComparison.Ordinal);

        Assert.Contains("catch (PaymentException ex)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("throw;", Slice(checkoutService, "catch (PaymentException ex)", "catch (UnauthorizedAccessException ex)"), StringComparison.Ordinal);

        Assert.Contains("\"PAYMOB\" => HasPaymobCheckoutHandoff(response)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("QueryHelpers.ParseQuery(uri.Query)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("\"/unifiedcheckout\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("\"clientSecret\"", checkoutService, StringComparison.Ordinal);

        Assert.Contains("Paymob payment initialization context resolved", paymobRuntime, StringComparison.Ordinal);
        Assert.Contains("IntegrationId: {IntegrationId}", paymobRuntime, StringComparison.Ordinal);
        Assert.Contains("ClientSecretPresent: {ClientSecretPresent}", paymobRuntime, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPa56Truth()
    {
        Assert.Contains(
            "PASS SF-PA5.6 Checkout Submission Authority Certification",
            ReadRepoFile("docs/architecture/storefront-checkout-submission-authority-certification-findings.md"),
            StringComparison.Ordinal);

    }

    private static string Slice(string source, string startMarker, string endMarker, string? afterMarker = null)
    {
        var startSearchIndex = 0;
        if (afterMarker != null)
        {
            startSearchIndex = source.IndexOf(afterMarker, StringComparison.Ordinal);
            Assert.True(startSearchIndex >= 0, $"Missing after marker: {afterMarker}");
        }

        var start = source.IndexOf(startMarker, startSearchIndex, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
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
            if (current.GetFiles("*.sln").Any() ||
                current.GetDirectories("PointOfSale.API")
                    .Any(directory => File.Exists(Path.Combine(directory.FullName, "PointOfSale.API.csproj"))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
