using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PointOfSale.Tests.Architecture;

public sealed class VerifiedSafeFlowsArchitectureTests
{
    [Fact]
    public void StorefrontCheckoutPriceAuthority_MustUseSqlVariantsAndRejectExpectedPricingMismatches()
    {
        var checkout = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var recalculatePrices = ExtractMethodBody(checkout, "RecalculatePricesInternalAsync");
        var loadAuthority = ExtractMethodBody(checkout, "LoadAuthoritativeCheckoutVariantLookupAsync");
        var createOrder = ExtractMethodBody(checkout, "CreateOrderAsync");
        var ensureExpectedPricing = ExtractMethodBody(checkout, "EnsureExpectedPricingMatches");

        Assert.NotNull(recalculatePrices);
        Assert.NotNull(loadAuthority);
        Assert.NotNull(createOrder);
        Assert.NotNull(ensureExpectedPricing);

        Assert.Contains("Prices and stock policy come from SQL product/variant authority", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("Validate prices from MongoDB ProductVariants", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductVariants (prices, stock via IProductVariantReadRepository)", checkout, StringComparison.Ordinal);

        Assert.Contains("_dbContext.ProductVariants", loadAuthority!, StringComparison.Ordinal);
        Assert.Contains("variant.OnlinePrice", loadAuthority!, StringComparison.Ordinal);
        Assert.Contains("variant.Price", loadAuthority!, StringComparison.Ordinal);
        Assert.Contains("variant.Product.IsOnlineVisible", loadAuthority!, StringComparison.Ordinal);

        Assert.Contains("LoadAuthoritativeCheckoutVariantLookupAsync", recalculatePrices!, StringComparison.Ordinal);
        Assert.Contains("item.CurrentPrice = authoritativeVariant.EffectivePrice", recalculatePrices!, StringComparison.Ordinal);
        Assert.Contains("Variant price validated from SQL authority", recalculatePrices!, StringComparison.Ordinal);

        Assert.Contains("_storefrontPricingService.CalculateCheckoutAsync", createOrder!, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches", createOrder!, StringComparison.Ordinal);
        Assert.Contains("UnitPrice = item.CurrentPrice", createOrder!, StringComparison.Ordinal);
        Assert.Contains("PricingSnapshotJson", createOrder!, StringComparison.Ordinal);

        Assert.Contains("expected_pricing_missing", ensureExpectedPricing!, StringComparison.Ordinal);
        Assert.Contains("CheckoutTotalMismatchException", ensureExpectedPricing!, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded", ensureExpectedPricing!, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontOrderCreation_MustKeepCheckoutAuthoritativeWithoutCreatingShipmentSideEffects()
    {
        var checkout = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));
        var consumer = File.ReadAllText(ApiPath("Consumers", "OrderShipmentOrchestrationConsumer.cs"));
        var createOrder = ExtractMethodBody(checkout, "CreateOrderAsync");
        var reserveOffer = ExtractMethodBody(checkout, "ReserveAppliedOfferPromotionAsync");
        Assert.NotNull(createOrder);
        Assert.NotNull(reserveOffer);

        AssertContainsInOrder(
            createOrder!,
            "BeginTransactionAsync",
            "_dbContext.Orders.Add(order)",
            "BindOrderReservationsAsync",
            "EmitOrderCreatedEventAsync");

        Assert.Contains("_dbContext.AffiliateAttributions.Add(attribution)", createOrder!, StringComparison.Ordinal);
        Assert.Contains("_couponValidationService.ReserveCouponAsync", createOrder!, StringComparison.Ordinal);
        Assert.Contains("ReserveAppliedOfferPromotionAsync", createOrder!, StringComparison.Ordinal);
        Assert.Contains("EmitOrderCreatedEventAsync", createOrder!, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateShipmentInTransactionAsync", createOrder!, StringComparison.Ordinal);
        Assert.DoesNotContain("EmitShipmentCreatedEventAsync", createOrder!, StringComparison.Ordinal);
        Assert.DoesNotContain("EmitShipmentExternalRegistrationRequestedEventAsync", createOrder!, StringComparison.Ordinal);
        Assert.Contains("PersistInventoryReservedOutboxAsync", createOrder!, StringComparison.Ordinal);
        Assert.Contains("BindOrderCreatedForCommitAsync", createOrder!, StringComparison.Ordinal);

        AssertContainsInOrder(
            createOrder!,
            "PersistInventoryReservedOutboxAsync",
            "BindOrderCreatedForCommitAsync",
            "SaveChangesAsync",
            "CommitAsync");

        Assert.Contains("Database.CurrentTransaction", reserveOffer!, StringComparison.Ordinal);
        Assert.Contains("Offer promotion reservation must execute inside the checkout transaction", reserveOffer!, StringComparison.Ordinal);
        Assert.Contains("IConsumer<OrderConfirmedEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("IConsumer<OrderPaidEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("IConsumer<OrderCancelledEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("x.AddConsumer<OrderShipmentOrchestrationConsumer>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckoutCartToOrderFlow_MustLockValidateAndConsumeServerCart()
    {
        var dto = File.ReadAllText(ApiPath("DTOs", "CommerceHub", "CommerceHubCheckoutDtos.cs"));
        var checkout = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));
        var checkoutAsync = ExtractMethodBody(checkout, "CheckoutAsync");
        var lockCart = ExtractMethodBody(checkout, "LockCartForCheckoutAsync");
        var verifyCart = ExtractMethodBody(checkout, "EnsureLockedCartMatchesCheckoutRequest");
        var cleanup = ExtractMethodBody(checkout, "CleanupAfterCheckoutFailureAsync");

        Assert.NotNull(checkoutAsync);
        Assert.NotNull(lockCart);
        Assert.NotNull(verifyCart);
        Assert.NotNull(cleanup);

        Assert.Contains("public int? CartVersion", dto, StringComparison.Ordinal);
        Assert.Contains("CART_VERSION_REQUIRED", dto, StringComparison.Ordinal);
        Assert.Contains("Checkout requires the latest server cart version", dto, StringComparison.Ordinal);

        AssertContainsInOrder(
            checkoutAsync!,
            "ResolveAuthenticatedCartOwnerContextAsync",
            "lockedCart = await LockCartForCheckoutAsync",
            "EnsureLockedCartMatchesCheckoutRequest",
            "BeginTransactionAsync");

        Assert.Contains("cart.Version != expectedVersion", lockCart!, StringComparison.Ordinal);
        Assert.Contains("cart.IsLocked = true", lockCart!, StringComparison.Ordinal);
        Assert.Contains("cart.Version += 1", lockCart!, StringComparison.Ordinal);

        Assert.Contains("DeserializeStoredCheckoutCartItems", verifyCart!, StringComparison.Ordinal);
        Assert.Contains("storedLines.SequenceEqual(requestedFingerprints)", verifyCart!, StringComparison.Ordinal);
        Assert.Contains("CART_CHANGED", verifyCart!, StringComparison.Ordinal);

        Assert.Contains("_dbContext.CommerceHubCarts.Remove(lockedCart)", checkoutAsync!, StringComparison.Ordinal);
        Assert.Contains("cart.IsLocked = false", cleanup!, StringComparison.Ordinal);
        Assert.Contains("RemoveCheckoutIdempotencyClaimIfPendingAsync", cleanup!, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentStatusAuthority_MustBeGuardedBySaveChangesInterceptorAndCentralLifecycle()
    {
        var interceptor = File.ReadAllText(InfrastructurePath("Data", "PaymentStatusSaveChangesInterceptor.cs"));
        var lifecycle = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));

        Assert.Contains("PaymentStatusSaveChangesInterceptor : SaveChangesInterceptor", interceptor, StringComparison.Ordinal);
        Assert.Contains("private static readonly AsyncLocal<bool> _authorizedContext", interceptor, StringComparison.Ordinal);
        Assert.Contains("PaymentStatusChanged", interceptor, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus can only be changed via IPaymentLifecycleService", interceptor, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException", interceptor, StringComparison.Ordinal);

        Assert.Contains("AddScoped<PaymentStatusSaveChangesInterceptor>", program, StringComparison.Ordinal);
        Assert.Contains("AddInterceptors", program, StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<PaymentStatusSaveChangesInterceptor>()", program, StringComparison.Ordinal);

        Assert.Contains("PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext()", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext()", lifecycle, StringComparison.Ordinal);
        Assert.Contains("order.AcceptPaymentStatusChange()", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ValidateSuccessfulAmount", lifecycle, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireMatch", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_accountingOutbox", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_marketplaceEventOutbox", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryReservationFlow_MustUseSqlAuthorityTransactionsAndDatabaseInvariants()
    {
        var reservationConfig = File.ReadAllText(InfrastructurePath("Data", "Configurations", "InventoryReservationConfiguration.cs"));
        var stockConfig = File.ReadAllText(InfrastructurePath("Data", "Configurations", "ProductVariantStockConfiguration.cs"));
        var stockRepository = File.ReadAllText(InfrastructurePath("Data", "Repositories", "ProductVariantStockRepository.cs"));
        var inventoryService = File.ReadAllText(ApiPath("Services", "CommerceHub", "InventoryConsistencyService.cs"));

        Assert.Contains("CK_InventoryReservations_ReservedQuantity_NonNegative", reservationConfig, StringComparison.Ordinal);
        Assert.Contains("CK_InventoryReservations_CommittedQuantity_NonNegative", reservationConfig, StringComparison.Ordinal);
        Assert.Contains("CK_InventoryReservations_ReleasedQuantity_NonNegative", reservationConfig, StringComparison.Ordinal);
        Assert.Contains("CK_InventoryReservations_ReservedFlow_Valid", reservationConfig, StringComparison.Ordinal);
        Assert.Contains("\\\"CommittedQuantity\\\" + \\\"ReleasedQuantity\\\" <= \\\"ReservedQuantity\\\"", reservationConfig, StringComparison.Ordinal);
        Assert.Contains("CK_InventoryReservations_Returns_DoNotExceedCommitted", reservationConfig, StringComparison.Ordinal);

        Assert.Contains("CK_ProductVariantStocks_Quantity_NonNegative", stockConfig, StringComparison.Ordinal);
        Assert.Contains("CK_ProductVariantStocks_ReservedQuantity_NonNegative", stockConfig, StringComparison.Ordinal);
        Assert.Contains("CK_ProductVariantStocks_ReservedQuantity_Within_Quantity", stockConfig, StringComparison.Ordinal);
        Assert.Contains("\\\"Quantity\\\" >= \\\"ReservedQuantity\\\"", stockConfig, StringComparison.Ordinal);

        Assert.Contains("BeginTransactionAsync", stockRepository, StringComparison.Ordinal);
        Assert.Contains("Quantity - s.ReservedQuantity", stockRepository, StringComparison.Ordinal);
        Assert.Contains("stock.ReservedQuantity += reservable", stockRepository, StringComparison.Ordinal);
        Assert.Contains("stock.ReservedQuantity -= releasable", stockRepository, StringComparison.Ordinal);
        Assert.Contains("reservation.CommittedQuantity += commitQuantity", stockRepository, StringComparison.Ordinal);
        Assert.Contains("LastMutationEventId", stockRepository, StringComparison.Ordinal);
        Assert.Contains("StockUpdatedAtUtc", stockRepository, StringComparison.Ordinal);

        Assert.Contains("BeginTransactionAsync", inventoryService, StringComparison.Ordinal);
        Assert.Contains("stock.ReservedQuantity += toReserve", inventoryService, StringComparison.Ordinal);
        Assert.Contains("stock.ReservedQuantity -= reservableCommit", inventoryService, StringComparison.Ordinal);
        Assert.Contains("reservation.CommittedQuantity += reservableCommit", inventoryService, StringComparison.Ordinal);
        Assert.Contains("LastMutationEventId", inventoryService, StringComparison.Ordinal);
        Assert.Contains("StockUpdatedAtUtc", inventoryService, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionFreshnessAndMarketplaceReadModels_MustRejectStaleOrDuplicateState()
    {
        var productVariantConsumer = File.ReadAllText(InfrastructurePath("Consumers", "ProductVariantProjectionConsumer.cs"));
        var commerceHubConsumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));
        var productRepository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "Marketplace", "MarketplaceProductReadRepository.cs"));
        var orderRepository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "Marketplace", "MarketplaceOrderReadRepository.cs"));
        var projectionService = File.ReadAllText(InfrastructurePath("ReadModels", "Marketplace", "MarketplaceReadModelProjectionServices.cs"));
        var modelSnapshot = File.ReadAllText(InfrastructurePath("Migrations", "ApplicationDbContextModelSnapshot.cs"));

        foreach (var source in new[] { productVariantConsumer, commerceHubConsumer })
        {
            Assert.Contains("LastEventId", source, StringComparison.Ordinal);
            Assert.Contains("EventId", source, StringComparison.Ordinal);
            Assert.Contains("StockUpdatedAtUtc", source, StringComparison.Ordinal);
            Assert.Contains("stale", source, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var source in new[] { productRepository, orderRepository })
        {
            Assert.Contains("incomingEntityMutationVersion", source, StringComparison.Ordinal);
            Assert.Contains("LastEntityMutationVersion", source, StringComparison.Ordinal);
            Assert.Contains("Lt(x => x.LastEntityMutationVersion, incomingEntityMutationVersion)", source, StringComparison.Ordinal);
            Assert.Contains("Eq(x => x.LastEntityMutationVersion, incomingEntityMutationVersion)", source, StringComparison.Ordinal);
            Assert.Contains("ProjectionWriteResult.SkippedStale", source, StringComparison.Ordinal);
        }

        Assert.Contains("ResolveProductProjectionMutationVersion", projectionService, StringComparison.Ordinal);
        Assert.Contains("product.ProjectionMutationVersion", projectionService, StringComparison.Ordinal);
        Assert.Contains("variant.ProjectionMutationVersion", projectionService, StringComparison.Ordinal);
        Assert.Contains("stock.ProjectionMutationVersion", projectionService, StringComparison.Ordinal);
        Assert.Contains("order.ProjectionMutationVersion", projectionService, StringComparison.Ordinal);
        Assert.Contains("LastEntityMutationVersion", projectionService, StringComparison.Ordinal);

        foreach (var entityType in new[]
                 {
                     "PointOfSale.Core.Entities.Product",
                     "PointOfSale.Core.Entities.ProductVariant",
                     "PointOfSale.Core.Entities.ProductVariantStock",
                     "PointOfSale.Core.Entities.Order"
                 })
        {
            var entityModel = ExtractModelEntityBlock(modelSnapshot, entityType);
            Assert.NotNull(entityModel);
            Assert.Contains("b.Property<long>(\"ProjectionMutationVersion\")", entityModel!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StorefrontOrderProjectionRecovery_MustRebuildAffiliateAuthorityAndCommissionState()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "OrderProjectionConsumer.cs"));
        var builder = File.ReadAllText(InfrastructurePath("MongoDB", "Projection", "StorefrontOrderProjectionBuilder.cs"));
        var loader = File.ReadAllText(InfrastructurePath("MongoDB", "Projection", "StorefrontOrderProjectionAuthoritySnapshotLoader.cs"));
        var reconciliation = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontOrderProjectionReconciliationService.cs"));
        var definition = File.ReadAllText(InfrastructurePath("Consumers", "OrderProjectionConsumerDefinition.cs"));

        Assert.Contains("IConsumer<AffiliateCommissionReversedEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("StorefrontOrderProjectionAuthoritySnapshotLoader.LoadAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("\"AFFILIATE_ATTRIBUTED\"", consumer, StringComparison.Ordinal);
        Assert.Contains("\"AFFILIATE_COMMISSION_EARNED\"", consumer, StringComparison.Ordinal);
        Assert.Contains("\"AFFILIATE_COMMISSION_REVERSED\"", consumer, StringComparison.Ordinal);

        Assert.Contains("AffiliateCommissionAmount = authoritySnapshot?.AffiliateCommissionAmount", builder, StringComparison.Ordinal);
        Assert.Contains("document.AffiliateCommissionAmount", builder, StringComparison.Ordinal);
        Assert.Contains("document.IsAffiliateOrder", builder, StringComparison.Ordinal);
        Assert.Contains("document.AffiliateReferralCode", builder, StringComparison.Ordinal);
        Assert.Contains("StorefrontOrderProjectionAuthoritySnapshot", builder, StringComparison.Ordinal);

        Assert.Contains("CommissionEntryType.Earned", loader, StringComparison.Ordinal);
        Assert.Contains("CommissionEntryType.Reversed", loader, StringComparison.Ordinal);
        Assert.Contains("Math.Max(0m, earnedCommission - reversedCommission)", loader, StringComparison.Ordinal);

        Assert.Contains("StorefrontOrderProjectionAuthoritySnapshotLoader.LoadAsync", reconciliation, StringComparison.Ordinal);
        Assert.Contains("order-projection-v7", definition, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentSuccessFailureFlow_MustBeTransactionalVersionAwareAndQuarantineProviderMismatches()
    {
        var lifecycle = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var checkout = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));

        var handleSuccess = ExtractMethodBody(lifecycle, "HandleSuccessAsync");
        var handleFailure = ExtractMethodBody(lifecycle, "HandleFailureAsync");
        var checkoutFlow = ExtractMethodBody(checkout, "ProcessCheckoutAsync");
        var createOrderFlow = ExtractMethodBody(checkout, "CreateOrderAsync");
        var createPaymentSession = ExtractMethodBody(checkout, "CreateCheckoutPaymentSessionAsync");
        Assert.NotNull(handleSuccess);
        Assert.NotNull(handleFailure);
        Assert.NotNull(checkoutFlow);
        Assert.NotNull(createOrderFlow);
        Assert.NotNull(createPaymentSession);

        AssertContainsInOrder(
            handleSuccess!,
            "BeginLifecycleTransactionAsync",
            "TryQuarantineProviderAuthorityMismatchAsync",
            "ValidateSuccessfulAmount",
            "PublishSuccessDomainEventAsync",
            "SaveChangesAsync",
            "CommitAsync");

        AssertContainsInOrder(
            handleFailure!,
            "BeginLifecycleTransactionAsync",
            "Ignoring stale failure after success",
            "PublishFailureDomainEventAsync",
            "SaveChangesAsync",
            "CommitAsync");

        var beginLifecycleTransaction = ExtractMethodBody(lifecycle, "BeginLifecycleTransactionAsync");
        Assert.NotNull(beginLifecycleTransaction);
        Assert.Contains("BeginTransactionAsync", beginLifecycleTransaction!, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel", beginLifecycleTransaction!, StringComparison.Ordinal);

        Assert.Contains("PaymentLifecycleOutcome.AmountMismatch", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.AmountMismatch", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentStatus.AmountMismatch", lifecycle, StringComparison.Ordinal);
        Assert.Contains("RecordPaymentAmountMismatch", lifecycle, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireMatch", lifecycle, StringComparison.Ordinal);

        AssertContainsInOrder(
            checkoutFlow!,
            "CreateOrderAsync",
            "orderCommitted = true",
            "POST-COMMIT OPERATIONS",
            "CreateCheckoutPaymentSessionAsync");
        Assert.Contains("_paymentGateway.CreateSessionAsync", createPaymentSession!, StringComparison.Ordinal);
        AssertContainsInOrder(
            createOrderFlow!,
            "ValidateAndReserveInventoryAsync",
            "_dbContext.Orders.Add",
            "BindOrderReservationsAsync",
            "CommitAsync");
        Assert.Contains("ReleaseInventoryReservationAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("_variantStockRepository.BindOrderReservationsAsync", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void PosSaleAccountingOutbox_MustLeaseBeforePostingAndOrderByAggregateVersion()
    {
        var message = File.ReadAllText(CorePath("Entities", "Accounting", "AccountingOutboxMessage.cs"));
        var configuration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "AccountingOutboxMessageConfiguration.cs"));
        var processor = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxProcessor.cs"));
        var modelSnapshot = File.ReadAllText(InfrastructurePath("Migrations", "ApplicationDbContextModelSnapshot.cs"));

        Assert.Contains("ClaimedUntilUtc", message, StringComparison.Ordinal);
        Assert.Contains("TryClaim", message, StringComparison.Ordinal);
        Assert.Contains("ClaimedUntilUtc = now.AddSeconds", message, StringComparison.Ordinal);
        Assert.Contains(".IsConcurrencyToken()", configuration, StringComparison.Ordinal);
        Assert.Contains("x.ClaimedUntilUtc", configuration, StringComparison.Ordinal);

        Assert.Contains("HasUnprocessedEarlierAggregateVersionAsync", processor, StringComparison.Ordinal);
        Assert.Contains("ThenBy(m => m.AggregateVersion ?? int.MaxValue)", processor, StringComparison.Ordinal);
        Assert.Contains("DbUpdateConcurrencyException", processor, StringComparison.Ordinal);
        AssertContainsInOrder(
            processor,
            "freshMessage.TryClaim",
            "SaveChangesAsync",
            "ProcessMessageAsync");

        Assert.Contains("b.Property<DateTime?>(\"ClaimedUntilUtc\")", modelSnapshot, StringComparison.Ordinal);
        Assert.Contains("b.Property<int?>(\"AggregateVersion\")", modelSnapshot, StringComparison.Ordinal);
        Assert.Contains("IX_AccountingOutbox_ProcessingStatus", modelSnapshot, StringComparison.Ordinal);
        Assert.Contains("IX_AccountingOutbox_AggregateVersionOrdering", modelSnapshot, StringComparison.Ordinal);
        Assert.Contains("CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion", modelSnapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void RefundAndPayoutExecution_MustUseDurableLocalConfirmationBoundaries()
    {
        var refundExecution = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));
        var refundGateway = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundGatewayService.cs"));
        var payoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var superAdminPayouts = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutsService.cs"));

        Assert.DoesNotContain("ICommerceHubRefundGatewayService", refundExecution, StringComparison.Ordinal);
        Assert.DoesNotContain("_refundGatewayService", refundExecution, StringComparison.Ordinal);
        Assert.DoesNotContain("RefundStripeAsync", refundExecution, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentProvider.Stripe", refundExecution, StringComparison.Ordinal);
        Assert.Contains("ManualRefundExecutionCommand", refundExecution, StringComparison.Ordinal);
        Assert.Contains("CommerceHubRefundExecution", refundExecution, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", refundExecution, StringComparison.Ordinal);
        Assert.Contains("ConfirmMoneySent", refundExecution, StringComparison.Ordinal);
        Assert.Contains("CommerceHubRefundStatus.Processing", refundExecution, StringComparison.Ordinal);
        Assert.Contains("CommerceHubRefundStatus.Completed", refundExecution, StringComparison.Ordinal);

        Assert.Contains("EnsureNoActiveLocalTransactionForExternalRefund", refundGateway, StringComparison.Ordinal);
        Assert.Contains("Database.CurrentTransaction", refundGateway, StringComparison.Ordinal);

        Assert.DoesNotContain("HttpClient", superAdminPayouts, StringComparison.Ordinal);
        Assert.DoesNotContain("Stripe", superAdminPayouts, StringComparison.Ordinal);
        Assert.DoesNotContain("TransferAsync(", superAdminPayouts, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginTransactionAsync", superAdminPayouts, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync", superAdminPayouts, StringComparison.Ordinal);
        Assert.Contains("GetVendorQueueSnapshotsAsync", superAdminPayouts, StringComparison.Ordinal);

        var markCompleted = ExtractMethodBody(payoutService, "MarkCompletedAsync");
        Assert.NotNull(markCompleted);
        AssertContainsInOrder(
            markCompleted!,
            "BeginTransactionAsync(IsolationLevel.Serializable",
            "!IsExternallyLockedPayoutStatus(payout.Status)",
            "EnsureTerminalPayoutEvidence",
            "EnsureExternalPayoutEvidenceIsUniqueAsync",
            "payout.Status = CommerceHubPayoutStatus.Completed",
            "CompletionEvidenceFingerprint",
            "VerifyStateTransitionAsync",
            "CommitAsync");
        Assert.Contains("PAYOUT_MUST_BE_PROCESSING_BEFORE_COMPLETION", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_COMPLETION_EVIDENCE_TAMPERED", payoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPayoutReadiness_MustConstrainOperationalPayableByLedgerPayableAndRiskHolds()
    {
        var payoutRead = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubVendorPayoutReadService.cs"));
        var payoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var superAdminPayouts = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutsService.cs"));

        Assert.Contains("operationalOutstandingAfterPending", payoutRead, StringComparison.Ordinal);
        Assert.Contains("ledgerOutstandingAfterPending", payoutRead, StringComparison.Ordinal);
        Assert.Contains("Math.Min(operationalOutstandingAfterPending, ledgerOutstandingAfterPending)", payoutRead, StringComparison.Ordinal);
        Assert.Contains("eligibleOutstanding", payoutRead, StringComparison.Ordinal);
        Assert.Contains("holdbackAmount", payoutRead, StringComparison.Ordinal);
        Assert.Contains("DeliveryHold", payoutRead, StringComparison.Ordinal);
        Assert.Contains("RefundWindowOpen", payoutRead, StringComparison.Ordinal);
        Assert.Contains("SettlementDelayActive", payoutRead, StringComparison.Ordinal);
        Assert.Contains("ActiveRefundRequest", payoutRead, StringComparison.Ordinal);
        Assert.Contains("LedgerPayableDrift", payoutRead, StringComparison.Ordinal);

        Assert.Contains("EnsureEarningsHaveLedgerConfirmationAsync", payoutService, StringComparison.Ordinal);
        Assert.Contains("CaptureAndValidatePayoutFinancialReadinessAsync", payoutService, StringComparison.Ordinal);
        Assert.Contains("payout.Amount > state.VendorPayable", payoutService, StringComparison.Ordinal);
        Assert.Contains("state.PlatformCash < payout.Amount", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_AMOUNT_EXCEEDS_PAYABLE", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_PLATFORM_CASH_INSUFFICIENT", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_LEDGER_CONFIRMATION_REQUIRED", payoutService, StringComparison.Ordinal);

        Assert.Contains("snapshot.TotalPayable <= 0m", superAdminPayouts, StringComparison.Ordinal);
        Assert.Contains("snapshot.EligibleAmount <= 0m", superAdminPayouts, StringComparison.Ordinal);
        Assert.Contains("IsPayoutAuthorityBlockingRisk", superAdminPayouts, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_AUTHORITY_CONFLICT", superAdminPayouts, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubLifecycleBoundarySafeFlows_MustHaveRuntimePositiveAndNegativeProof()
    {
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var boundaryTests = File.ReadAllText(TestsPath("CommerceHub", "CommerceHubDataAuthorityBoundaryTests.cs"));

        Assert.Contains("GuardCommerceHubFinancialLifecycleRecordImmutability", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubPayment>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubRefund>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubPayout>", dbContext, StringComparison.Ordinal);

        foreach (var testName in new[]
                 {
                     "PaymentFinancialIdentity_CannotBeChangedEvenInsidePaymentBoundary",
                     "PaymentLifecycleFields_CanBeChangedInsidePaymentBoundary",
                     "PaymentRefundFields_CanBeChangedInsideRefundBoundary",
                     "RefundFinancialIdentity_CannotBeChangedEvenInsideRefundBoundary",
                     "RefundLifecycleFields_CanBeChangedInsideRefundBoundary",
                     "PayoutFinancialIdentity_CannotBeChangedEvenInsidePayoutBoundary",
                     "PayoutLifecycleFields_CanBeChangedInsidePayoutBoundary",
                     "PaymentItemAllocation_CannotBeChangedAfterCreation"
                 })
        {
            Assert.Contains(testName, boundaryTests, StringComparison.Ordinal);
        }
    }

    private static void AssertContainsInOrder(string source, params string[] markers)
    {
        var previousIndex = -1;

        foreach (var marker in markers)
        {
            var index = source.IndexOf(marker, previousIndex + 1, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Expected marker '{marker}' after index {previousIndex}.");
            previousIndex = index;
        }
    }

    private static string? ExtractMethodBody(string source, string methodName)
    {
        // Parse the source code using Roslyn
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var root = syntaxTree.GetRoot();

        // Find the method declaration by name (check both block bodies and expression bodies)
        var methodDeclaration = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName && (m.Body != null || m.ExpressionBody != null));

        if (methodDeclaration != null)
        {
            // Return the full method declaration including signature and body (block or expression)
            return methodDeclaration.ToFullString();
        }

        // Also check for local functions (nested methods)
        var localFunction = root.DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()
            .FirstOrDefault(lf => lf.Identifier.Text == methodName && (lf.Body != null || lf.ExpressionBody != null));

        if (localFunction != null)
        {
            return localFunction.ToFullString();
        }

        return null;
    }

    private static string? ExtractModelEntityBlock(string source, string entityTypeName)
    {
        var marker = $"modelBuilder.Entity(\"{entityTypeName}\", b =>";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var end = source.IndexOf("modelBuilder.Entity(\"", start + marker.Length, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    private static string ApiPath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(new[] { solutionRoot, "PointOfSale.API" }.Concat(parts).ToArray()));
    }

    private static string InfrastructurePath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(new[] { solutionRoot, "PointOfSale.Infrastructure" }.Concat(parts).ToArray()));
    }

    private static string CorePath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(new[] { solutionRoot, "PointOfSale.Core" }.Concat(parts).ToArray()));
    }

    private static string TestsPath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(new[] { solutionRoot, "PointOfSale.Tests" }.Concat(parts).ToArray()));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
