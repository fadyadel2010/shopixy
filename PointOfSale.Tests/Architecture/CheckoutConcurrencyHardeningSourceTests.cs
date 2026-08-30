using System.Collections.Generic;
using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class CheckoutConcurrencyHardeningSourceTests
{
    [Fact]
    public void CheckoutService_MustPropagateServerIdempotencyKey_ToGatewayIntentCreation()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        Assert.Contains("requestId = checkoutId;", source, StringComparison.Ordinal);
        Assert.Contains("BuildServerCheckoutIdempotencyKey(", source, StringComparison.Ordinal);
        Assert.Contains("string requestId,", source, StringComparison.Ordinal);
        var canonicalPaymentCallIndex = source.IndexOf(
            "var response = await _paymentGateway.CreateSessionAsync(",
            StringComparison.Ordinal);
        var propagatedRequestIdIndex = source.IndexOf("RequestId = requestId,", canonicalPaymentCallIndex, StringComparison.Ordinal);
        Assert.True(canonicalPaymentCallIndex > 0, "Checkout must use the canonical payment gateway adapter.");
        Assert.True(propagatedRequestIdIndex > canonicalPaymentCallIndex, "The server checkout idempotency key must reach payment session creation.");
    }

    [Fact]
    public void CheckoutService_MustRecoverDurableIdempotencyReplay_BeforeReadingRedisCart()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        var durableReplayIndex = source.IndexOf("activeStep = \"DurableIdempotencyReplay\"", StringComparison.Ordinal);
        var nullSnapshotClaimIndex = source.IndexOf("snapshotHash: null", StringComparison.Ordinal);
        var cartSnapshotIndex = source.IndexOf("var cartSnapshot = await GetCartSnapshotAsync(", StringComparison.Ordinal);

        Assert.True(durableReplayIndex > 0, "CheckoutService must have a pre-cart durable idempotency replay step.");
        Assert.True(nullSnapshotClaimIndex > durableReplayIndex, "Pre-cart replay must use lookup-only idempotency claim mode.");
        Assert.True(cartSnapshotIndex > nullSnapshotClaimIndex, "Durable idempotency replay must run before Redis cart snapshot reads.");
        Assert.Contains("StepName = \"DurableIdempotencyReplay\"", source, StringComparison.Ordinal);
        Assert.Contains("pre_cart_replay", source, StringComparison.Ordinal);
        Assert.Contains("pre_cart_existing_order", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderModel_MustEnforceSingleActiveOrderPerCart_AndRecoverOnConstraintRaces()
    {
        var dbContextSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Data",
            "ApplicationDbContext.cs");
        var modelSnapshotSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Migrations",
            "ApplicationDbContextModelSnapshot.cs");
        var checkoutServiceSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        Assert.Contains("IX_Orders_TenantId_CartId_ActiveStatuses", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("\\\"CartId\\\" IS NOT NULL", dbContextSource, StringComparison.Ordinal);
        Assert.Contains("IX_Orders_TenantId_CartId_ActiveStatuses", modelSnapshotSource, StringComparison.Ordinal);
        Assert.Contains("\\\"IsDeleted\\\" = false AND \\\"CartId\\\" IS NOT NULL", modelSnapshotSource, StringComparison.Ordinal);
        Assert.Contains("RecordCheckoutDuplicateOrderPrevented(\"db_unique_guard\")", checkoutServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutService_MustValidatePaymentMethodLimits_AgainstAuthoritativeFinalTotal()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        var earlyLimitIndex = source.IndexOf(
            "ValidatePaymentMethodOrderAmountLimits(paymentMethod, preReservationPricing.FinalTotal, correlationId);",
            StringComparison.Ordinal);
        var inventoryReservationIndex = source.IndexOf("inventoryReservation = await ValidateAndReserveInventoryAsync(", StringComparison.Ordinal);

        Assert.True(earlyLimitIndex > 0, "Checkout must validate payment method limits after authoritative pricing is calculated.");
        Assert.True(
            earlyLimitIndex < inventoryReservationIndex,
            "Invalid payment method amount limits must fail before stock reservation.");

        var orderCreationIndex = source.IndexOf("private async Task<OrderCreationResult> CreateOrderAsync", StringComparison.Ordinal);
        var orderPricingIndex = source.IndexOf(
            "var pricing = await _storefrontPricingService.CalculateCheckoutAsync",
            orderCreationIndex,
            StringComparison.Ordinal);
        var orderLimitIndex = source.IndexOf(
            "ValidatePaymentMethodOrderAmountLimits(paymentMethod, pricing.FinalTotal, correlationId);",
            orderCreationIndex,
            StringComparison.Ordinal);

        Assert.True(orderLimitIndex > orderPricingIndex, "Order creation must enforce limits on final persisted pricing.");

        var previewIndex = source.IndexOf("public async Task<CheckoutPricingPreviewResponseDto> PreviewPricingAsync", StringComparison.Ordinal);
        var previewPricingIndex = source.IndexOf(
            "var pricing = await _storefrontPricingService.CalculateCheckoutAsync",
            previewIndex,
            StringComparison.Ordinal);
        var previewLimitIndex = source.IndexOf(
            "ValidatePaymentMethodOrderAmountLimits(paymentMethod, pricing.FinalTotal, correlationId);",
            previewIndex,
            StringComparison.Ordinal);

        Assert.True(previewLimitIndex > previewPricingIndex, "Pricing preview must enforce limits on final preview total.");
        Assert.DoesNotContain("decimal orderSubtotal", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutService_MustRevalidateShippingPaymentAndPricingInsideFinalOrderTransaction()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        var orderCreationIndex = source.IndexOf("private async Task<OrderCreationResult> CreateOrderAsync", StringComparison.Ordinal);
        var beginTransactionIndex = source.IndexOf("BeginTransactionAsync(", orderCreationIndex, StringComparison.Ordinal);
        var transactionRevalidationIndex = source.IndexOf("RevalidateCheckoutAuthorityInsideTransactionAsync(", orderCreationIndex, StringComparison.Ordinal);
        var inventoryReservationIndex = source.IndexOf("inventoryReservation = await ValidateAndReserveInventoryAsync(", orderCreationIndex, StringComparison.Ordinal);

        Assert.True(beginTransactionIndex > orderCreationIndex, "Order creation must open a final transaction.");
        Assert.True(transactionRevalidationIndex > beginTransactionIndex, "Checkout must revalidate authority after the final transaction starts.");
        Assert.True(transactionRevalidationIndex < inventoryReservationIndex, "Authority revalidation must happen before inventory reservation.");

        var transactionHelperIndex = source.IndexOf("private async Task<TransactionScopedCheckoutAuthoritySnapshot> RevalidateCheckoutAuthorityInsideTransactionAsync", StringComparison.Ordinal);
        var lockedShippingIndex = source.IndexOf("lockAuthorityRow: true", transactionHelperIndex, StringComparison.Ordinal);
        var lockedPaymentIndex = source.IndexOf("lockAuthorityRow: true", lockedShippingIndex + 1, StringComparison.Ordinal);
        var catalogRefreshIndex = source.IndexOf("RecalculatePricesInternalAsync(", transactionHelperIndex, StringComparison.Ordinal);
        var catalogLockingIndex = source.IndexOf("lockAuthorityRows: true", catalogRefreshIndex, StringComparison.Ordinal);
        var discountRefreshIndex = source.IndexOf("ReconcileCheckoutDiscountAuthorityAsync(", transactionHelperIndex, StringComparison.Ordinal);
        var pricingIndex = source.IndexOf("var pricing = await _storefrontPricingService.CalculateCheckoutAsync(", transactionHelperIndex, StringComparison.Ordinal);
        var expectedPricingIndex = source.IndexOf("EnsureExpectedPricingMatches(", transactionHelperIndex, StringComparison.Ordinal);

        Assert.True(transactionHelperIndex > 0, "Checkout must declare a transaction-scoped authority revalidation helper.");
        Assert.True(lockedShippingIndex > transactionHelperIndex, "Transaction-scoped revalidation must lock shipping-method authority.");
        Assert.True(lockedPaymentIndex > lockedShippingIndex, "Transaction-scoped revalidation must lock payment-method authority.");
        Assert.True(catalogRefreshIndex > lockedPaymentIndex, "Transaction-scoped revalidation must reload catalog authority after payment/shipping rows are locked.");
        Assert.True(catalogLockingIndex > catalogRefreshIndex, "Final transaction catalog refresh must request row-level authority locking.");
        Assert.True(discountRefreshIndex > catalogLockingIndex, "Transaction-scoped revalidation must rerun coupon and promotion authority after catalog refresh.");
        Assert.True(pricingIndex > discountRefreshIndex, "Final pricing must be calculated only after catalog and discount authority are refreshed inside the transaction.");
        Assert.True(expectedPricingIndex > transactionHelperIndex, "Transaction-scoped revalidation must still reject stale client totals.");
        Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
        Assert.Contains("IsCheckoutTransactionSerializationFailure", source, StringComparison.Ordinal);
        Assert.Contains("CheckoutConcurrencyConflictException", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutService_MustRehydrateIdempotencyClaimInsideRetryTransaction_BeforeCommit()
    {
        var checkoutSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");
        var coordinatorSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutRequestIdempotencyCoordinator.cs");

        var orderCreationIndex = checkoutSource.IndexOf("private async Task<OrderCreationResult> CreateOrderAsync", StringComparison.Ordinal);
        var bindIndex = checkoutSource.IndexOf(
            "await _checkoutRequestIdempotencyCoordinator.BindOrderCreatedForCommitAsync(",
            orderCreationIndex,
            StringComparison.Ordinal);
        var saveChangesIndex = checkoutSource.IndexOf(
            "await _dbContext.SaveChangesAsync(cancellationToken);",
            bindIndex,
            StringComparison.Ordinal);
        var commitIndex = checkoutSource.IndexOf(
            "await transaction.CommitAsync(cancellationToken);",
            saveChangesIndex,
            StringComparison.Ordinal);

        Assert.True(bindIndex > orderCreationIndex, "CreateOrderAsync must bind the claimed idempotency row through the coordinator inside the final transaction.");
        Assert.True(saveChangesIndex > bindIndex, "The rebound idempotency claim must be flushed before the checkout transaction commits.");
        Assert.True(commitIndex > saveChangesIndex, "The checkout transaction must commit only after the durable idempotency bind is saved.");
        Assert.Contains("Task<OrderRequestIdempotency> BindOrderCreatedForCommitAsync(", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("LoadOwnedRecordForMutationAsync", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("Entries<OrderRequestIdempotency>()", coordinatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutService_MustRemoveRelationshipFixupPrincipals_BeforeSerializableRetry()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        var serializationFailureIndex = source.IndexOf(
            "if (IsCheckoutTransactionSerializationFailure(originalException))",
            StringComparison.Ordinal);
        var clearIndex = source.IndexOf("_dbContext.ChangeTracker.Clear();", serializationFailureIndex, StringComparison.Ordinal);
        var resetIndex = source.IndexOf("ResetOrderNavigationStateForRetry(order);", clearIndex, StringComparison.Ordinal);
        var continueIndex = source.IndexOf("continue;", resetIndex, StringComparison.Ordinal);
        var resetMethod = ExtractMethodBody(source, "ResetOrderNavigationStateForRetry");

        Assert.True(clearIndex > serializationFailureIndex, "Serializable rollback must clear EF tracking before retrying.");
        Assert.True(resetIndex > clearIndex, "Serializable retry must remove principal instances retained by relationship fixup after tracking is cleared.");
        Assert.True(continueIndex > resetIndex, "The retained order graph must be reset before the next transaction attempt.");
        Assert.Contains("order.Warehouse = null;", resetMethod, StringComparison.Ordinal);
        Assert.Contains("orderItem.Product = null!;", resetMethod, StringComparison.Ordinal);
        Assert.Contains("orderItem.Variant = null;", resetMethod, StringComparison.Ordinal);
        Assert.Contains("orderItem.Unit = null;", resetMethod, StringComparison.Ordinal);
        Assert.Contains("orderItem.Warehouse = null;", resetMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutService_MustRefreshItemLevelMoneySnapshots_AfterFinalAuthorityRevalidation()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        var orderCreationIndex = source.IndexOf("private async Task<OrderCreationResult> CreateOrderAsync", StringComparison.Ordinal);
        var headerRefreshIndex = source.IndexOf("RefreshOrderAuthoritySnapshot(", orderCreationIndex, StringComparison.Ordinal);
        var itemRefreshIndex = source.IndexOf("RefreshOrderItemAuthoritySnapshots(", orderCreationIndex, StringComparison.Ordinal);
        var inventoryReservationIndex = source.IndexOf("inventoryReservation = await ValidateAndReserveInventoryAsync(", orderCreationIndex, StringComparison.Ordinal);

        Assert.True(headerRefreshIndex > orderCreationIndex, "Order creation must refresh header authority inside the final transaction.");
        Assert.True(itemRefreshIndex > headerRefreshIndex, "Item-level money snapshots must be rebuilt after the final header authority refresh.");
        Assert.True(itemRefreshIndex < inventoryReservationIndex, "Item-level money snapshots must converge before inventory reservation and commit.");
        Assert.Contains("snapshot = transactionScopedAuthority.Snapshot;", source, StringComparison.Ordinal);
        Assert.Contains("CartSnapshot snapshot,", source, StringComparison.Ordinal);
        Assert.Contains("order.CouponId = snapshot.AppliedCouponId;", source, StringComparison.Ordinal);
        Assert.Contains("order.AppliedPromotionId = snapshot.AppliedPromotionId;", source, StringComparison.Ordinal);
        Assert.Contains("private void RefreshOrderItemAuthoritySnapshots(", source, StringComparison.Ordinal);
        Assert.Contains("orderItem.AffiliateCommissionBaseAmountSnapshot = shouldPersistAffiliateCommissionSnapshot ? lineTotal : null;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutService_MustReloadAffiliateCommissionPolicyAuthority_InsideFinalOrderTransaction()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        var orderCreationIndex = source.IndexOf("private async Task<OrderCreationResult> CreateOrderAsync", StringComparison.Ordinal);
        var transactionSnapshotIndex = source.IndexOf("snapshot = transactionScopedAuthority.Snapshot;", orderCreationIndex, StringComparison.Ordinal);
        var affiliatePolicyReloadIndex = source.IndexOf("LoadAffiliateCommissionPolicySnapshotsAsync(", transactionSnapshotIndex, StringComparison.Ordinal);
        var itemRefreshIndex = source.IndexOf("RefreshOrderItemAuthoritySnapshots(", transactionSnapshotIndex, StringComparison.Ordinal);

        Assert.True(transactionSnapshotIndex > orderCreationIndex, "Final order transaction must replace the pre-transaction cart snapshot with transaction-scoped authority.");
        Assert.True(affiliatePolicyReloadIndex > transactionSnapshotIndex, "Affiliate commission policy authority must be reloaded after transaction-scoped cart authority is known.");
        Assert.True(itemRefreshIndex > affiliatePolicyReloadIndex, "Affiliate item snapshots must be refreshed only after commission policy authority is reloaded inside the transaction.");
        Assert.Contains("lockAuthorityRows: true", source, StringComparison.Ordinal);
        Assert.Contains("LockAffiliateCommissionPolicyAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateCheckout_MustRevalidateAffiliateIdentityAuthority_InsideFinalOrderTransaction()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        var orderCreationIndex = source.IndexOf("private async Task<OrderCreationResult> CreateOrderAsync", StringComparison.Ordinal);
        var transactionSnapshotIndex = source.IndexOf("snapshot = transactionScopedAuthority.Snapshot;", orderCreationIndex, StringComparison.Ordinal);
        var affiliateRevalidationIndex = source.IndexOf("RevalidateResolvedAffiliateAuthorityInsideFinalTransactionAsync(", transactionSnapshotIndex, StringComparison.Ordinal);
        var affiliatePolicyReloadIndex = source.IndexOf("LoadAffiliateCommissionPolicySnapshotsAsync(", affiliateRevalidationIndex, StringComparison.Ordinal);
        var paymentIntentIndex = source.IndexOf("TryCreateTransactionalPaymentIntentAuthorityAsync(", affiliatePolicyReloadIndex, StringComparison.Ordinal);
        var attributionCreateIndex = source.IndexOf("AffiliateAttribution.Create", paymentIntentIndex, StringComparison.Ordinal);

        Assert.True(transactionSnapshotIndex > orderCreationIndex, "Final order transaction must first replace stale cart visibility with transaction-scoped SQL authority.");
        Assert.True(affiliateRevalidationIndex > transactionSnapshotIndex, "Affiliate identity/link authority must be revalidated after final SQL cart authority is known.");
        Assert.True(affiliatePolicyReloadIndex > affiliateRevalidationIndex, "Commission policy snapshots must depend on revalidated affiliate authority.");
        Assert.True(paymentIntentIndex > affiliateRevalidationIndex, "Payment-intent placeholder authority must not precede final affiliate authority revalidation.");
        Assert.True(attributionCreateIndex > affiliateRevalidationIndex, "Affiliate attribution must not be created from pre-transaction affiliate visibility.");
        Assert.Contains("AFFILIATE_AUTHORITY_STALE", source, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PROMOTION_LINK_STALE", source, StringComparison.Ordinal);
        Assert.Contains("LoadActiveAffiliateAccountAuthorityForFinalCheckoutAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadActiveAffiliatePromotionLinkAuthorityForFinalCheckoutAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadAffiliateUserAccountIdForFinalCheckoutAsync", source, StringComparison.Ordinal);
        Assert.Contains("Affiliate checkout authority must be revalidated inside the final checkout transaction", source, StringComparison.Ordinal);
        Assert.Contains("FROM \"\"AffiliateAccounts\"\"", source, StringComparison.Ordinal);
        Assert.Contains("FROM \"\"AffiliatePromotionLinks\"\"", source, StringComparison.Ordinal);
        Assert.Contains("FROM \"\"AffiliateUsers\"\"", source, StringComparison.Ordinal);
        Assert.Contains("WITH (UPDLOCK, ROWLOCK, HOLDLOCK)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckout_MustLockSqlAuthorityCompositionRows_InsideFinalTransaction()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubCheckoutService.cs");

        var transactionIndex = source.IndexOf("BeginTransactionAsync(", StringComparison.Ordinal);
        var serializableIndex = source.IndexOf("IsolationLevel.Serializable", transactionIndex, StringComparison.Ordinal);
        var listingLoadIndex = source.IndexOf("LoadCommerceHubListingsForCheckoutAuthorityAsync(", transactionIndex, StringComparison.Ordinal);
        var variantLoadIndex = source.IndexOf("LoadProductVariantsForCheckoutAuthorityAsync(", listingLoadIndex, StringComparison.Ordinal);
        var productLoadIndex = source.IndexOf("LoadProductsForCheckoutAuthorityAsync(", variantLoadIndex, StringComparison.Ordinal);
        var tenantLoadIndex = source.IndexOf("LoadTenantsForCheckoutAuthorityAsync(", productLoadIndex, StringComparison.Ordinal);
        var paymentMethodLoadIndex = source.IndexOf("LoadTenantPaymentMethodsForCheckoutAuthorityAsync(", tenantLoadIndex, StringComparison.Ordinal);
        var promoValidationIndex = source.IndexOf("ValidatePromoForCheckoutAsync(", paymentMethodLoadIndex, StringComparison.Ordinal);
        var shippingRecalcIndex = source.IndexOf("bypassCache: true", promoValidationIndex, StringComparison.Ordinal);
        var buildOrderIndex = source.IndexOf("BuildMarketplaceOrder(", shippingRecalcIndex, StringComparison.Ordinal);
        var reservationIndex = source.IndexOf("ReserveOrderItemAsync(", buildOrderIndex, StringComparison.Ordinal);

        Assert.True(transactionIndex > 0, "CommerceHub checkout must open a serializable final authority transaction.");
        Assert.True(serializableIndex > transactionIndex, "CommerceHub checkout final authority transaction must be serializable.");
        Assert.True(listingLoadIndex > transactionIndex, "CommerceHub checkout must reload listing authority after the final transaction starts.");
        Assert.True(variantLoadIndex > listingLoadIndex, "CommerceHub checkout must reload variant authority from SQL.");
        Assert.True(productLoadIndex > variantLoadIndex, "CommerceHub checkout must reload product authority from SQL.");
        Assert.True(tenantLoadIndex > productLoadIndex, "CommerceHub checkout must reload store/tenant authority from SQL.");
        Assert.True(paymentMethodLoadIndex > tenantLoadIndex, "CommerceHub checkout must reload payment-method authority from SQL.");
        Assert.True(promoValidationIndex > paymentMethodLoadIndex, "CommerceHub checkout must revalidate promo authority after SQL catalog/payment authority is loaded.");
        Assert.True(shippingRecalcIndex > promoValidationIndex, "CommerceHub checkout must recalculate shipping without cache before order creation.");
        Assert.True(buildOrderIndex > shippingRecalcIndex, "CommerceHub checkout must build the order only from final SQL-derived authority.");
        Assert.True(reservationIndex > buildOrderIndex, "CommerceHub checkout must reserve stock only after final order authority is composed.");

        Assert.Contains("private async Task<Dictionary<Guid, CommerceHubListing>> LoadCommerceHubListingsForCheckoutAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("private async Task<Dictionary<Guid, ProductVariant>> LoadProductVariantsForCheckoutAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("private async Task LockProductVariantAttributesForCheckoutAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("private async Task<Dictionary<Guid, Product>> LoadProductsForCheckoutAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("private async Task<Dictionary<Guid, Tenant>> LoadTenantsForCheckoutAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("private async Task<List<TenantPaymentMethod>> LoadTenantPaymentMethodsForCheckoutAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("\"ProductVariantAttributes\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckout_MustCollapseActiveCustomerCartOverlap_BeforeIdempotencySideEffects()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubCheckoutService.cs");

        var customerLockKeyIndex = source.IndexOf(
            "var customerCheckoutLockKey = BuildCustomerCheckoutLockKey(customerId);",
            StringComparison.Ordinal);
        var idempotencyLockKeyIndex = source.IndexOf(
            "var checkoutLockKey = BuildCheckoutLockKey(customerId, normalizedIdempotencyKey);",
            StringComparison.Ordinal);
        var firstAcquireIndex = source.IndexOf(
            "var customerCheckoutLockResult = await _lockService.TryAcquireLockAsync(",
            StringComparison.Ordinal);
        var idempotencyRecordIndex = source.IndexOf(
            "idempotencyRecord = new CommerceHubCheckoutIdempotency",
            StringComparison.Ordinal);
        var cartClaimIndex = source.IndexOf(
            "lockedCart = await LockCartForCheckoutAsync(",
            StringComparison.Ordinal);

        Assert.True(customerLockKeyIndex > 0, "CommerceHub checkout must derive a customer-cart active checkout lock.");
        Assert.True(idempotencyLockKeyIndex > customerLockKeyIndex, "Idempotency-key locking must remain subordinate to the customer-cart overlap lock.");
        Assert.True(firstAcquireIndex > idempotencyLockKeyIndex, "The customer-cart active checkout lock must be acquired before side effects.");
        Assert.True(firstAcquireIndex < idempotencyRecordIndex, "Active cart overlap must collapse before inserting a checkout idempotency claim.");
        Assert.True(idempotencyRecordIndex < cartClaimIndex, "Existing idempotency claim creation should still precede the SQL cart claim.");
        Assert.Contains("private static string BuildCustomerCheckoutLockKey(Guid customerId)", source, StringComparison.Ordinal);
        Assert.Contains("customerCheckoutLockAcquired", source, StringComparison.Ordinal);
        Assert.Contains("idempotencyCheckoutLockAcquired", source, StringComparison.Ordinal);
        Assert.Contains("CreateCheckoutInProgressException()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckout_MustSqlLockAndCleanUpCartClaimFailures()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubCheckoutService.cs");

        var lockCartMethodIndex = source.IndexOf(
            "private async Task<CommerceHubCart> LockCartForCheckoutAsync",
            StringComparison.Ordinal);
        var lockCartTransactionIndex = source.IndexOf(
            "IsolationLevel.Serializable",
            lockCartMethodIndex,
            StringComparison.Ordinal);
        var loadCartAuthorityIndex = source.IndexOf(
            "LoadCommerceHubCartForCheckoutAuthorityAsync(",
            lockCartTransactionIndex,
            StringComparison.Ordinal);
        var saveChangesIndex = source.IndexOf(
            "await _dbContext.SaveChangesAsync(cancellationToken);",
            loadCartAuthorityIndex,
            StringComparison.Ordinal);
        var commitIndex = source.IndexOf(
            "await transaction.CommitAsync(cancellationToken);",
            saveChangesIndex,
            StringComparison.Ordinal);
        var cleanupIndex = source.IndexOf(
            "await CleanupAfterCheckoutFailureAsync(",
            StringComparison.Ordinal);

        Assert.True(lockCartMethodIndex > 0, "CommerceHub checkout must have a dedicated SQL cart claim method.");
        Assert.True(lockCartTransactionIndex > lockCartMethodIndex, "The cart claim must run in a serializable transaction.");
        Assert.True(loadCartAuthorityIndex > lockCartTransactionIndex, "The cart row must be loaded through the authority-lock helper.");
        Assert.True(commitIndex > saveChangesIndex, "The locked cart state must commit before final checkout authority proceeds.");
        Assert.True(cleanupIndex > 0, "Failed cart claims must clean up the pending idempotency row and cart lock.");
        Assert.Contains("private async Task<CommerceHubCart?> LoadCommerceHubCartForCheckoutAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("FROM \"\"CommerceHubCarts\"\"", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentIntentCreation_MustRevalidateSqlReservationAuthority_BeforeProviderSideEffects()
    {
        var paymentSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Payments",
            "PaymentService.cs");
        var stripeSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Payments",
            "Stripe",
            "StripePaymentService.cs");
        var paymobSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Payments",
            "Paymob",
            "PaymobPaymentService.cs");
        var guardSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Payments",
            "CheckoutInventoryAuthorityGuard.cs");

        Assert.Contains("public static class CheckoutInventoryAuthorityGuard", guardSource, StringComparison.Ordinal);
        Assert.Contains("EnsureStorefrontOrderReservationsActiveAsync", guardSource, StringComparison.Ordinal);
        Assert.Contains("\"InventoryReservations\"", guardSource, StringComparison.Ordinal);
        Assert.Contains("\"ProductVariantStocks\"", guardSource, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", guardSource, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_INVENTORY_AUTHORITY_LOST", guardSource, StringComparison.Ordinal);

        var paymentGuardIndex = paymentSource.IndexOf(
            "await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(",
            StringComparison.Ordinal);
        var paymentExistingIntentIndex = paymentSource.IndexOf(
            "var existingActiveIntent = await FindExistingActiveIntentAsync(",
            StringComparison.Ordinal);
        Assert.True(paymentGuardIndex > 0, "Generic payment intent creation must revalidate SQL reservation authority.");
        Assert.True(paymentGuardIndex < paymentExistingIntentIndex, "Generic payment intent replay must not return or create payment authority before reservation revalidation.");

        var stripeGuardIndex = stripeSource.IndexOf(
            "await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(",
            StringComparison.Ordinal);
        var stripeConfigIndex = stripeSource.IndexOf(
            "var stripeConfig = await GetStripeConfigurationAsync(",
            StringComparison.Ordinal);
        Assert.True(stripeGuardIndex > 0, "Stripe payment intent creation must revalidate SQL reservation authority.");
        Assert.True(stripeGuardIndex < stripeConfigIndex, "Stripe provider configuration/session work must happen only after SQL reservation revalidation.");

        var paymobGuardIndex = paymobSource.IndexOf(
            "await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(",
            StringComparison.Ordinal);
        var paymobProviderSideEffectIndex = paymobSource.IndexOf(
            "var result = await _paymobRuntimeApplicationService.CreatePaymentIntentAsync(",
            StringComparison.Ordinal);
        Assert.True(paymobGuardIndex > 0, "Paymob payment intent creation must revalidate SQL reservation authority.");
        Assert.True(paymobGuardIndex < paymobProviderSideEffectIndex, "Paymob provider session work must happen only after SQL reservation revalidation.");
    }

    [Fact]
    public void CheckoutReplayAndRecovery_MustRevalidateSqlReservationAuthority_BeforePaymentIntentRepair()
    {
        var checkoutSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");
        var recoverySource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutPaymentIntentRecoveryService.cs");

        var replayMethodIndex = checkoutSource.IndexOf(
            "private async Task<PaymentIntent?> EnsureReplayPaymentIntentAsync",
            StringComparison.Ordinal);
        var replayGuardIndex = checkoutSource.IndexOf(
            "await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(",
            replayMethodIndex,
            StringComparison.Ordinal);
        var replaySwitchIndex = checkoutSource.IndexOf("switch (paymentMethodCode)", replayMethodIndex, StringComparison.Ordinal);
        Assert.True(replayGuardIndex > replayMethodIndex, "Checkout idempotency replay must prove active SQL reservation authority.");
        Assert.True(replayGuardIndex < replaySwitchIndex, "Checkout idempotency replay must prove reservations before provider-specific repair.");

        var existingIntentIndex = recoverySource.IndexOf("if (existingIntent != null)", StringComparison.Ordinal);
        var existingGuardIndex = recoverySource.IndexOf(
            "await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(",
            existingIntentIndex,
            StringComparison.Ordinal);
        var existingResumeIndex = recoverySource.IndexOf(
            "await CreateProviderAccuratePaymentIntentAsync(order, resumeIdempotencyKey, resumeCorrelationId, cancellationToken);",
            existingIntentIndex,
            StringComparison.Ordinal);
        Assert.True(existingGuardIndex > existingIntentIndex, "Recovery of an existing incomplete intent must prove reservation authority.");
        Assert.True(existingGuardIndex < existingResumeIndex, "Recovery must not resume provider payment authority before reservation revalidation.");

        var missingIntentIndex = recoverySource.IndexOf(
            "var idempotencyKey = await ResolveProviderIdempotencyKeyAsync(order, cancellationToken);",
            StringComparison.Ordinal);
        var missingGuardIndex = recoverySource.IndexOf(
            "await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(",
            missingIntentIndex,
            StringComparison.Ordinal);
        var missingCreateIndex = recoverySource.IndexOf(
            "await CreateProviderAccuratePaymentIntentAsync(order, idempotencyKey, correlationId, cancellationToken);",
            missingIntentIndex,
            StringComparison.Ordinal);
        Assert.True(missingGuardIndex > missingIntentIndex, "Missing payment-intent recovery must prove reservation authority.");
        Assert.True(missingGuardIndex < missingCreateIndex, "Missing payment-intent recovery must not create provider authority after stock loss.");
    }

    [Fact]
    public void CommerceHubPaymentInitiation_MustRecheckReservationsAfterPaymentLock()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubPaymentService.cs");

        var lockAcquiredIndex = source.IndexOf("if (!paymentLockResult.IsAcquired)", StringComparison.Ordinal);
        var tryIndex = source.IndexOf("try", lockAcquiredIndex, StringComparison.Ordinal);
        var unresolvedIndex = source.IndexOf("await EnsureNoUnresolvedPaymentAsync(marketplaceOrder, providerCode, cancellationToken);", tryIndex, StringComparison.Ordinal);
        var reservationRecheckIndex = source.IndexOf(
            "await _inventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(",
            unresolvedIndex,
            StringComparison.Ordinal);
        var paymentRowsIndex = source.IndexOf("var payments = await _dbContext.CommerceHubPayments", reservationRecheckIndex, StringComparison.Ordinal);

        Assert.True(reservationRecheckIndex > unresolvedIndex, "CommerceHub payment initiation must recheck reservation authority after the payment lock is acquired.");
        Assert.True(reservationRecheckIndex < paymentRowsIndex, "CommerceHub payment rows/provider session selection must happen only after the locked reservation recheck.");
    }

    [Fact]
    public void CheckoutService_MustUseSqlCatalogAuthorityAndRejectStaleCartPrices()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        var recalculationIndex = source.IndexOf("private async Task<(CartSnapshot Snapshot, bool PricesRefreshed, List<PriceChangeItem> PriceChanges, decimal OriginalSubtotal, decimal NewSubtotal, decimal OverallDiffPercent)> RecalculatePricesInternalAsync", StringComparison.Ordinal);
        var sqlAuthorityIndex = source.IndexOf("LoadAuthoritativeCheckoutVariantLookupAsync", recalculationIndex, StringComparison.Ordinal);
        var sqlPriceAssignmentIndex = source.IndexOf("item.CurrentPrice = authoritativeVariant.EffectivePrice;", recalculationIndex, StringComparison.Ordinal);
        var firstSubtotalIndex = source.IndexOf("originalSubtotal += item.CartPrice * item.Quantity;", recalculationIndex, StringComparison.Ordinal);

        Assert.True(sqlAuthorityIndex > recalculationIndex, "Checkout price recalculation must call SQL catalog authority.");
        Assert.True(sqlPriceAssignmentIndex > sqlAuthorityIndex, "Checkout line price must be assigned from SQL catalog authority.");
        Assert.True(firstSubtotalIndex > sqlPriceAssignmentIndex, "Checkout totals must be computed only after SQL authoritative price assignment.");
        Assert.DoesNotContain("item.CurrentPrice = variant.EffectivePrice;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("item.CurrentPrice = product.EffectivePrice;", source, StringComparison.Ordinal);

        var priceRefreshIndex = source.IndexOf("else if (pricesRefreshed)", StringComparison.Ordinal);
        var stalePriceThrowIndex = source.IndexOf("throw new CheckoutPriceChangedException(originalSubtotal, newSubtotal, overallDiffPercent);", priceRefreshIndex, StringComparison.Ordinal);

        Assert.True(stalePriceThrowIndex > priceRefreshIndex, "Checkout must fail closed when cart prices differ from authoritative catalog prices.");
    }

    [Fact]
    public void CheckoutProductAuthorityLock_PostgresEntityMaterialization_MustSelectXmin()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");

        var method = ExtractMethodBody(source, "LockCheckoutProductAuthorityRowsBatchAsync");

        Assert.Contains("_dbContext.Products", method, StringComparison.Ordinal);
        Assert.Contains("FromSqlRaw", method, StringComparison.Ordinal);
        Assert.Contains("SELECT xmin, *", method, StringComparison.Ordinal);
        Assert.Contains("FROM \"\"Products\"\"", method, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", method, StringComparison.Ordinal);
        Assert.Contains("\"\"TenantId\"\" = '{tenantId}'", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT *\r\n                    FROM \"\"Products\"\"", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutPromotionAndCouponCapacityLocks_PostgresEntityMaterialization_MustSelectXmin()
    {
        var checkoutSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutService.cs");
        var couponSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Discounts",
            "CouponValidationService.cs");
        var paymentLifecycleSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Payments",
            "PaymentLifecycleService.cs");

        var checkoutPromotionLock = ExtractMethodBody(checkoutSource, "LockPromotionForCapacityMutationAsync");
        var couponLock = ExtractMethodBody(couponSource, "LockCouponForCapacityMutationAsync");
        var paymentPromotionLock = ExtractMethodBody(paymentLifecycleSource, "LockPromotionForUsageMutationAsync");

        Assert.Contains("SELECT xmin, *", checkoutPromotionLock, StringComparison.Ordinal);
        Assert.Contains("FROM \"\"Promotions\"\"", checkoutPromotionLock, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", checkoutPromotionLock, StringComparison.Ordinal);

        Assert.Contains("SELECT xmin, * FROM \"\"Coupons\"\"", couponLock, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", couponLock, StringComparison.Ordinal);

        Assert.Contains("SELECT xmin, *", paymentPromotionLock, StringComparison.Ordinal);
        Assert.Contains("FROM \"\"Promotions\"\"", paymentPromotionLock, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", paymentPromotionLock, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderAndPaymentLifecycleLocks_PostgresXminBackedEntities_MustSelectXmin()
    {
        var orderLockingHelper = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Payments",
            "OrderLockingHelper.cs");
        var paymentRecovery = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Checkout",
            "CheckoutPaymentIntentRecoveryService.cs");
        var orderStatusSync = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "OrderStatusSyncService.cs");
        var unresolvedPaymentReconciliation = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Payments",
            "StorefrontUnresolvedPaymentReconciliationJob.cs");
        var fulfillment = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Fulfillment",
            "OrderFulfillmentService.cs");
        var reservationCleanup = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Jobs",
            "InventoryReservationCleanupJob.cs");
        var carrierShipment = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Shipping",
            "CarrierShipmentService.cs");
        var shipmentService = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Shipping",
            "ShipmentService.cs");
        var accountingOutbox = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Accounting",
            "AccountingOutboxProcessor.cs");
        var paymentLifecycle = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Payments",
            "PaymentLifecycleService.cs");

        Assert.Contains("SELECT xmin, *", ExtractMethodBody(orderLockingHelper, "LoadOrderWithLockAsync"), StringComparison.Ordinal);
        Assert.Contains("SELECT xmin, *", ExtractMethodBody(paymentRecovery, "LoadOrderForPaymentIntentRecoveryAsync"), StringComparison.Ordinal);
        Assert.Contains("SELECT xmin, *", ExtractMethodBody(orderStatusSync, "LoadOrderForStatusMutationAsync"), StringComparison.Ordinal);
        Assert.Contains("SELECT xmin, *", ExtractMethodBody(unresolvedPaymentReconciliation, "LoadPaymentIntentForRecoveryAsync"), StringComparison.Ordinal);
        Assert.Contains("FROM \"\"PaymentIntents\"\"", ExtractMethodBody(unresolvedPaymentReconciliation, "LoadPaymentIntentForRecoveryAsync"), StringComparison.Ordinal);
        Assert.Contains("SELECT xmin, *", ExtractMethodBody(unresolvedPaymentReconciliation, "LoadOrderForRecoveryAsync"), StringComparison.Ordinal);
        Assert.Contains("SELECT xmin, *", ExtractMethodBody(fulfillment, "LoadOrderForFulfillmentMutationAsync"), StringComparison.Ordinal);
        Assert.Contains("SELECT xmin, *", ExtractMethodBody(reservationCleanup, "LoadOrderForCleanupAuthorityAsync"), StringComparison.Ordinal);
        Assert.Contains("SELECT xmin, *", ExtractMethodBody(carrierShipment, "LoadOrderForCarrierShipmentMutationAsync"), StringComparison.Ordinal);
        Assert.Contains("SELECT xmin, *", ExtractMethodBody(shipmentService, "LoadOrderForShipmentMutationAsync"), StringComparison.Ordinal);
        Assert.Contains("SELECT xmin, *", ExtractMethodBody(accountingOutbox, "LoadOrderForAccountingPostAsync"), StringComparison.Ordinal);

        Assert.Contains("SELECT xmin, * FROM \"\"Orders\"\"", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("SELECT xmin, * FROM \"\"PaymentIntents\"\"", paymentLifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryAuthorityLocks_WithoutMappedXmin_MustKeepLockingSemantics()
    {
        var inventoryGuard = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Payments",
            "CheckoutInventoryAuthorityGuard.cs");
        var stockConfiguration = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Data",
            "Configurations",
            "ProductVariantStockConfiguration.cs");
        var reservationConfiguration = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Data",
            "Configurations",
            "InventoryReservationConfiguration.cs");

        Assert.DoesNotContain("HasColumnName(\"xmin\")", stockConfiguration, StringComparison.Ordinal);
        Assert.DoesNotContain("HasColumnName(\"xmin\")", reservationConfiguration, StringComparison.Ordinal);
        Assert.Contains("FROM \"\"InventoryReservations\"\"", inventoryGuard, StringComparison.Ordinal);
        Assert.Contains("FROM \"\"ProductVariantStocks\"\"", inventoryGuard, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", inventoryGuard, StringComparison.Ordinal);
    }

    [Fact]
    public void CartMutations_MustRunBehindDistributedLock_AndSurfaceConflictResponses()
    {
        var cartServiceSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Cart",
            "CartService.cs");
        var cartControllerSource = ReadRepoFile(
            "PointOfSale.API",
            "Controllers",
            "StorefrontCartController.cs");

        Assert.Contains("ExecuteWithCartMutationLockAsync(", cartServiceSource, StringComparison.Ordinal);
        Assert.Contains("ExecuteWithCartMergeLocksAsync(", cartServiceSource, StringComparison.Ordinal);
        Assert.Contains("throw new CartConcurrencyConflictException()", cartServiceSource, StringComparison.Ordinal);
        Assert.Contains("CART_CONCURRENCY_CONFLICT", cartControllerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontStores_MustAvoidClientCheckoutKeys_AndRetryCartConflicts()
    {
        var checkoutStoreSource = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "store",
            "checkoutStore.ts");
        var cartStoreSource = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "store",
            "cartStore.ts");

        Assert.DoesNotContain("'Idempotency-Key':", checkoutStoreSource, StringComparison.Ordinal);
        Assert.Contains("if (state.isSubmitting)", checkoutStoreSource, StringComparison.Ordinal);
        Assert.Contains("await useCartStore.getState().fetchCart();", checkoutStoreSource, StringComparison.Ordinal);
        Assert.Contains("retryCartActionAfterConflict", cartStoreSource, StringComparison.Ordinal);
        Assert.Contains("syncCartFromServer", cartStoreSource, StringComparison.Ordinal);
        Assert.Contains("CART_CONCURRENCY_CONFLICT", cartStoreSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutCon05_MarketplacePaymentAndFulfillment_MustRecomposeSqlInventoryAuthority()
    {
        var inventorySource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "InventoryConsistencyService.cs");
        var paymentSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubPaymentService.cs");
        var projectionSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "ReadModels",
            "Marketplace",
            "MarketplaceReadModelProjectionServices.cs");

        Assert.Contains("EnsureMarketplaceOrderReservationsActiveAsync", paymentSource, StringComparison.Ordinal);
        Assert.Contains("reservation.Status == InventoryReservationStatus.Active", inventorySource, StringComparison.Ordinal);
        Assert.Contains("EnsureReservedStockRowsCoverMarketplaceReservationsAsync", inventorySource, StringComparison.Ordinal);
        Assert.Contains("EnsureMarketplaceReservationItemsMatchSqlCatalogAuthorityAsync", inventorySource, StringComparison.Ordinal);
        Assert.Contains("EnsureMarketplaceOrderShippingAuthorityActiveAsync", inventorySource, StringComparison.Ordinal);
        Assert.Contains("EnsureDeliveryCommitSqlAuthorityAsync", inventorySource, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_DELIVERY_PAYMENT_AUTHORITY_LOST", inventorySource, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", projectionSource, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Stock", projectionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutCon07_MarketplaceReservationReplay_MustFenceOwnershipOverReservationAndRecovery()
    {
        var inventorySource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "InventoryConsistencyService.cs");
        var paymentSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubPaymentService.cs");
        var recoverySource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubPaymentRecoveryAuthorityService.cs");

        Assert.Contains("MARKETPLACE_ORDER_RESERVATION_OWNERSHIP_CONFLICT", inventorySource, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_ORDER_RESERVATION_OVER_RESERVED", inventorySource, StringComparison.Ordinal);
        Assert.Contains("activeReservedQuantity == item.RequiredQuantity", inventorySource, StringComparison.Ordinal);
        Assert.Contains("FilterCurrentConsumableReservationsAcrossTenantsAsync", inventorySource, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_RESERVATION_STOCK_AUTHORITY_LOST", inventorySource, StringComparison.Ordinal);
        Assert.Contains("FilterCurrentInventoryStockOwnersAsync", inventorySource, StringComparison.Ordinal);
        Assert.Contains("EnsureDeliveryCommitInventoryLineageAuthorityAsync", inventorySource, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_DELIVERY_INVENTORY_LINEAGE_LOST", inventorySource, StringComparison.Ordinal);
        Assert.Contains("EnsureReturnStockLineageAuthorityAsync", inventorySource, StringComparison.Ordinal);
        Assert.Contains("INVENTORY_RETURN_STOCK_AUTHORITY_LOST", inventorySource, StringComparison.Ordinal);
        Assert.Contains("ORDER BY \"\"WarehouseId\"\", \"\"Id\"\"", inventorySource, StringComparison.Ordinal);

        var postLockRecheckIndex = paymentSource.IndexOf(
            "await _inventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(",
            paymentSource.IndexOf("await EnsureNoUnresolvedPaymentAsync", StringComparison.Ordinal),
            StringComparison.Ordinal);
        var providerSideEffectRecheckIndex = paymentSource.IndexOf(
            "RecomposeMarketplaceReservationAuthorityBeforeProviderSessionAsync(",
            postLockRecheckIndex,
            StringComparison.Ordinal);
        var providerSwitchIndex = paymentSource.IndexOf(
            "var session = provider switch",
            providerSideEffectRecheckIndex,
            StringComparison.Ordinal);
        var recoveryMethodIndex = paymentSource.IndexOf(
            "private async Task<CommerceHubPaymentInitiateResponseDto?> TryRecoverIncompletePendingPaymentAsync",
            StringComparison.Ordinal);
        var recoveryRecheckIndex = paymentSource.IndexOf(
            "RecomposeMarketplaceReservationAuthorityBeforeProviderSessionAsync(",
            recoveryMethodIndex,
            StringComparison.Ordinal);
        var reusableReturnIndex = paymentSource.IndexOf(
            "CommerceHubPaymentSessionRecoveryAuthority.HasReusableSession(payment)",
            recoveryMethodIndex,
            StringComparison.Ordinal);

        Assert.True(providerSideEffectRecheckIndex > postLockRecheckIndex, "Provider session creation must recompose reservation authority after payment rows/readiness work.");
        Assert.True(providerSideEffectRecheckIndex < providerSwitchIndex, "Provider session side effects must happen only after the final SQL reservation recomposition.");
        Assert.True(recoveryRecheckIndex > recoveryMethodIndex, "Payment recovery must recompose reservation authority inside the recovery path.");
        Assert.True(recoveryRecheckIndex < reusableReturnIndex, "Payment recovery must not return stale reusable session authority after reservation loss.");

        Assert.Contains("HasActiveMarketplaceReservationAuthorityAsync", recoverySource, StringComparison.Ordinal);
        Assert.Contains("HasReservationAuthorityLoss", recoverySource, StringComparison.Ordinal);
        Assert.Contains("ReservationAuthorityLostMessage", recoverySource, StringComparison.Ordinal);
        Assert.Contains("InventoryReservations", recoverySource, StringComparison.Ordinal);
        Assert.Contains("ProductVariantStocks", recoverySource, StringComparison.Ordinal);
        Assert.Contains("reservation.Status == InventoryReservationStatus.Active", recoverySource, StringComparison.Ordinal);
        Assert.Contains("FilterCurrentConsumableReservationsAcrossTenantsAsync", recoverySource, StringComparison.Ordinal);
        Assert.Contains("WhereCurrentInventoryStockOwner", recoverySource, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservationCleanup_MustRecomposeLockedSqlAuthorityBeforeLocalRelease()
    {
        var cleanupSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Jobs",
            "InventoryReservationCleanupJob.cs");

        var localReleaseIndex = cleanupSource.IndexOf(
            "var inventoryReleased = await _stockRepository.ReleaseOrderReservationsAsync(",
            StringComparison.Ordinal);
        var lockedReloadIndex = cleanupSource.LastIndexOf(
            "ReloadCleanupAuthorityAsync(",
            localReleaseIndex,
            StringComparison.Ordinal);
        var beginTransactionIndex = cleanupSource.LastIndexOf(
            "BeginCleanupTransactionAsync(",
            localReleaseIndex,
            StringComparison.Ordinal);
        var rollbackSkipIndex = cleanupSource.IndexOf(
            "Skipped stale reservation cleanup after locked SQL authority changed",
            lockedReloadIndex,
            StringComparison.Ordinal);

        Assert.True(beginTransactionIndex > 0, "Reservation cleanup local release must open a cleanup transaction.");
        Assert.True(lockedReloadIndex > beginTransactionIndex, "Reservation cleanup must reload current authority after opening the cleanup transaction.");
        Assert.True(lockedReloadIndex < localReleaseIndex, "Reservation cleanup must recompose locked SQL truth before releasing inventory.");
        Assert.True(rollbackSkipIndex > lockedReloadIndex, "Reservation cleanup must collapse when locked SQL authority changed since candidate discovery.");

        Assert.Contains("IsolationLevel.Serializable", cleanupSource, StringComparison.Ordinal);
        Assert.Contains("lockRows: true", cleanupSource, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", cleanupSource, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", cleanupSource, StringComparison.Ordinal);
        Assert.Contains("LoadReservationAuthorityStateAsync", cleanupSource, StringComparison.Ordinal);
        Assert.Contains("LoadOrderForCleanupAuthorityAsync", cleanupSource, StringComparison.Ordinal);
        Assert.Contains("LoadCommerceHubOrderForCleanupAuthorityAsync", cleanupSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutCon08_OutOfOrderVisibilityPaths_MustRemainDerivativeAndFreshnessFenced()
    {
        var promotionProjectionSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Consumers",
            "PromotionProjectionConsumer.cs");
        var couponProjectionSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Consumers",
            "CouponProjectionConsumer.cs");
        var paymentMethodProjectionSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Consumers",
            "PaymentMethodProjectionConsumer.cs");
        var shippingProjectionSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Consumers",
            "ShippingMethodProjectionConsumer.cs");
        var storefrontConfigProjectionSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Consumers",
            "StorefrontConfigProjectionConsumer.cs");
        var tenantCurrencyProjectionSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Consumers",
            "TenantCurrencyProjectionConsumer.cs");
        var commerceHubProjectionSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Consumers",
            "CommerceHubProjectionConsumer.cs");
        var affiliatePolicyProjectionSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Consumers",
            "Affiliate",
            "AffiliateProductPolicyProjectionConsumer.cs");
        var marketplaceRebuildSource = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "ReadModels",
            "Marketplace",
            "MarketplaceReadModelProjectionServices.cs");

        Assert.Contains("WithPromotionEventFenceAsync", promotionProjectionSource, StringComparison.Ordinal);
        Assert.Contains("LoadLiveSqlPromotionAuthorityAsync", promotionProjectionSource, StringComparison.Ordinal);
        Assert.Contains("BuildDeletedPromotionDocument", promotionProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", promotionProjectionSource, StringComparison.Ordinal);

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", couponProjectionSource, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCouponAuthorityAsync", couponProjectionSource, StringComparison.Ordinal);
        Assert.Contains("MapSqlAuthorityToDocument(sqlCoupon, @event)", couponProjectionSource, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CouponUsages", couponProjectionSource, StringComparison.Ordinal);
        Assert.Contains("existingDocument ?? new CouponDocument", couponProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ProjectionTombstoneGuard.IsTombstoneNewerThanOrEqualToEvent", couponProjectionSource, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter(@event)", couponProjectionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscountValue = @event.DiscountValue", couponProjectionSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Set(x => x.CurrentUsage, @event.CurrentUsage)", couponProjectionSource, StringComparison.Ordinal);

        Assert.Contains("BuildFreshnessFilter(eventId, occurredAtUtc, version)", paymentMethodProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", paymentMethodProjectionSource, StringComparison.Ordinal);
        Assert.Contains("LoadSqlPaymentMethodAuthorityAsync", paymentMethodProjectionSource, StringComparison.Ordinal);
        Assert.Contains("MapSqlAuthorityToDocument", paymentMethodProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersion", paymentMethodProjectionSource, StringComparison.Ordinal);
        Assert.Contains("IsDeleted = true", paymentMethodProjectionSource, StringComparison.Ordinal);

        Assert.Contains(".IgnoreQueryFilters()", shippingProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", shippingProjectionSource, StringComparison.Ordinal);
        Assert.Contains("TombstoneIfFreshAsync", shippingProjectionSource, StringComparison.Ordinal);
        Assert.Contains("UpsertIfFreshAsync", shippingProjectionSource, StringComparison.Ordinal);

        Assert.Contains("BuildFreshStoreConfigEventFilter", storefrontConfigProjectionSource, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync", storefrontConfigProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", storefrontConfigProjectionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IConnectionMultiplexer", storefrontConfigProjectionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyDeleteAsync", storefrontConfigProjectionSource, StringComparison.Ordinal);

        Assert.Contains("BuildFreshCurrencyEventFilter", tenantCurrencyProjectionSource, StringComparison.Ordinal);
        Assert.Contains("LastCurrencyEventVersion", tenantCurrencyProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", tenantCurrencyProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentSqlTenantCurrencyAsync", tenantCurrencyProjectionSource, StringComparison.Ordinal);
        Assert.Contains(".Set(x => x.Currency, sqlCurrency)", tenantCurrencyProjectionSource, StringComparison.Ordinal);
        Assert.Contains("mongoDoc.LastCurrencyEventVersion = existingProjection.LastCurrencyEventVersion", storefrontConfigProjectionSource, StringComparison.Ordinal);

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", commerceHubProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator", commerceHubProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentSqlStoreNameAsync", commerceHubProjectionSource, StringComparison.Ordinal);
        Assert.Contains("StoreMetadataConsumerName", commerceHubProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ShippingMetadataConsumerName", commerceHubProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrency", commerceHubProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ListingLifecycleConsumerName", commerceHubProjectionSource, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCommerceHubListingAuthorityAsync", commerceHubProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentSqlVariantPriceAsync", commerceHubProjectionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("context.Message.StoreName,", commerceHubProjectionSource, StringComparison.Ordinal);

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", affiliatePolicyProjectionSource, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", affiliatePolicyProjectionSource, StringComparison.Ordinal);
        Assert.Contains("NormalizeCurrency(", affiliatePolicyProjectionSource, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", marketplaceRebuildSource, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Stock", marketplaceRebuildSource, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Pricing", marketplaceRebuildSource, StringComparison.Ordinal);
        Assert.Contains("!product.IsActive || !product.IsOnlineVisible", marketplaceRebuildSource, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Order", marketplaceRebuildSource, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Homepage", marketplaceRebuildSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutCon09_PaymentReplayAfterFinalization_MustRecomposeSqlAuthorityBeforeStalePaymentRecreation()
    {
        var commerceHubPaymentSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubPaymentService.cs");

        var legacyFallbackIndex = commerceHubPaymentSource.IndexOf(
            "private async Task<CommerceHubPayment?> CreateLegacyPaymobPaymentAsync",
            StringComparison.Ordinal);
        var recomposeCallIndex = commerceHubPaymentSource.IndexOf(
            "RecomposeLegacyPaymobPaymentCreationAuthorityAsync(",
            legacyFallbackIndex,
            StringComparison.Ordinal);
        var newPaymentIndex = commerceHubPaymentSource.IndexOf(
            "var payment = new CommerceHubPayment",
            legacyFallbackIndex,
            StringComparison.Ordinal);
        var authorityHelperIndex = commerceHubPaymentSource.IndexOf(
            "private async Task<CommerceHubMarketplaceOrder?> RecomposeLegacyPaymobPaymentCreationAuthorityAsync",
            StringComparison.Ordinal);
        var initiationGuardIndex = commerceHubPaymentSource.IndexOf(
            "await EnsureInitiationAllowedAsync(",
            authorityHelperIndex,
            StringComparison.Ordinal);
        var reservationGuardIndex = commerceHubPaymentSource.IndexOf(
            "await _inventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(",
            initiationGuardIndex,
            StringComparison.Ordinal);
        var staleRefusalIndex = commerceHubPaymentSource.IndexOf(
            "Paymob legacy webhook fallback refused to create stale payment authority",
            reservationGuardIndex,
            StringComparison.Ordinal);

        Assert.True(recomposeCallIndex > legacyFallbackIndex, "Legacy Paymob fallback must recompose SQL authority before creating a payment row.");
        Assert.True(recomposeCallIndex < newPaymentIndex, "Stale provider callback replay must not mint payment authority before SQL recomposition.");
        Assert.True(initiationGuardIndex > authorityHelperIndex, "Legacy fallback must reject already-paid or non-retryable SQL order truth.");
        Assert.True(reservationGuardIndex > initiationGuardIndex, "Legacy fallback must prove active SQL reservation authority before recreating payment authority.");
        Assert.True(staleRefusalIndex > reservationGuardIndex, "Stale callback rejection must be explicit and observable.");
        Assert.Contains("CommerceHubOrderStateHelper.HasCollectedPayment(marketplaceOrder.PaymentStatus)", commerceHubPaymentSource, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_NOT_RETRYABLE", commerceHubPaymentSource, StringComparison.Ordinal);
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

    private static string ExtractMethodBody(string source, string methodName)
    {
        var methodNameIndex = FindMethodDeclarationIndex(source, methodName);
        Assert.True(methodNameIndex >= 0, $"Could not find method {methodName}.");

        var bodyStart = source.IndexOf('{', methodNameIndex);
        Assert.True(bodyStart >= 0, $"Could not find method body for {methodName}.");

        var depth = 0;
        for (var i = bodyStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(bodyStart, i - bodyStart + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not parse method body for {methodName}.");
    }

    private static int FindMethodDeclarationIndex(string source, string methodName)
    {
        var searchIndex = 0;
        while (searchIndex < source.Length)
        {
            var methodNameIndex = source.IndexOf(methodName, searchIndex, StringComparison.Ordinal);
            if (methodNameIndex < 0)
            {
                return -1;
            }

            var lineStart = source.LastIndexOf('\n', methodNameIndex);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            var lineEnd = source.IndexOf('\n', methodNameIndex);
            lineEnd = lineEnd < 0 ? source.Length : lineEnd;
            var line = source.Substring(lineStart, lineEnd - lineStart).TrimStart();

            if ((line.StartsWith("private ", StringComparison.Ordinal) ||
                 line.StartsWith("public ", StringComparison.Ordinal) ||
                 line.StartsWith("internal ", StringComparison.Ordinal) ||
                 line.StartsWith("protected ", StringComparison.Ordinal)) &&
                line.Contains(methodName + "(", StringComparison.Ordinal))
            {
                return methodNameIndex;
            }

            searchIndex = methodNameIndex + methodName.Length;
        }

        return -1;
    }
}
