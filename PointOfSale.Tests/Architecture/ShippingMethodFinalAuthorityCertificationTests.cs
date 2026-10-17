namespace PointOfSale.Tests.Architecture;

public sealed class ShippingMethodFinalAuthorityCertificationTests
{
    [Fact]
    public void FinalRuntimeAuthority_MustRemainBackendOwnedAcrossShippingMethodGovernance()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/ShippingMethodsController.cs");
        var storefront = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/ShippingMethodService.cs");
        var commerceHubShipping = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");
        var commerceHubCheckout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var standardCheckout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("TenantId => _tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("Id = Guid.NewGuid()", controller, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId && !s.IsDeleted)", controller, StringComparison.Ordinal);
        Assert.Contains("TryValidateCachedTenantScope(tenantId, documents", storefront, StringComparison.Ordinal);
        Assert.Contains("throw new StorefrontConfigOutOfSyncException", storefront, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", storefront, StringComparison.Ordinal);
        Assert.Contains("!method.IsDeleted && method.IsActive && method.TenantId == storeId", commerceHubShipping, StringComparison.Ordinal);
        Assert.Contains("_carrierIntegrationAuthority.GetIntegrationReadinessAsync(\n                    method.TenantId,", commerceHubShipping.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("OrderBy(candidate => candidate.EffectivePrice)", commerceHubShipping, StringComparison.Ordinal);
        Assert.Contains("bypassCache: true", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("requestedStoreSelection.ShippingOptionId != storeSelection.ShippingOptionId", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("LoadShippingMethodAuthorityAsync", standardCheckout, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", standardCheckout, StringComparison.Ordinal);
    }

    [Fact]
    public void SplitOrderShipmentCarrierProjectionCacheAndReplay_MustNotRestoreAuthority()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var boundary = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/CommerceHubBoundaryMutationScope.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var shipment = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var carrier = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs");
        var repository = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs");

        Assert.Contains("BeginShippingAssignment", checkout, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = shippingSelection.ShippingOptionId", checkout, StringComparison.Ordinal);
        Assert.Contains("ShippingSnapshotJson = BuildShippingSnapshotJson(shippingSelection)", checkout, StringComparison.Ordinal);
        Assert.Contains("BeginShippingAssignment", boundary, StringComparison.Ordinal);
        Assert.Contains("IsShippingAssignmentAuthorized", dbContext, StringComparison.Ordinal);
        Assert.Contains("nameof(CommerceHubSubOrder.ShippingMethodId)", dbContext, StringComparison.Ordinal);
        Assert.Contains("order.ShippingMethodId.Value != shippingMethodId", shipment, StringComparison.Ordinal);
        Assert.Contains("LoadOrderForShipmentMutationAsync(tenantId, orderId", shipment, StringComparison.Ordinal);
        Assert.Contains("order.ShippingMethodId.Value != shippingMethodId", shipment, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = order.ShippingMethodId.Value", carrier, StringComparison.Ordinal);
        Assert.Contains("ORDER_SHIPPING_METHOD_CARRIER_MISMATCH", carrier, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("TombstoneIfFreshAsync", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendCacheRecoveryAndBrowserState_MustRemainBackendBoundIntentOnly()
    {
        var storefrontCheckoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var storefrontShippingStep = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx");
        var recoverCheckout = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var commerceHubPricing = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/cart/useCommerceHubCartPricing.ts");
        var commerceHubCheckout = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");
        var commerceHubCheckoutTypes = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/types/checkout.ts");

        Assert.Contains("fetchShippingMethods", storefrontCheckoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingSelectionStatus === 'stale'", storefrontCheckoutStore, StringComparison.Ordinal);
        Assert.Contains("STALE_SHIPPING_SELECTION", storefrontCheckoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", storefrontCheckoutStore, StringComparison.Ordinal);
        Assert.Contains("t('checkout.shipping.retryMethods')", storefrontShippingStep, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: session.shippingMethod?.id ?? session.shippingMethodId ?? null", recoverCheckout, StringComparison.Ordinal);
        Assert.Contains("cartService.priceCart(request)", commerceHubPricing, StringComparison.Ordinal);
        Assert.Contains("const authoritativePricing = await cartService.priceCart", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("buildCommerceHubCheckoutRequest", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("shippingSelections: shippingSelections.map", commerceHubCheckoutTypes, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalCertificationRecord_MustFreezeShippingMethodAuthorityProductionCertification()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/shipping-method-final-authority-certification-findings.md");

        Assert.Contains("PASS M8-H Shipping Method Final Authority Certification", findings, StringComparison.Ordinal);
        Assert.Contains("Runtime Hardening Performed", findings, StringComparison.Ordinal);
        Assert.Contains("None. Final runtime reinspection found no new Shipping Method Authority gap", findings, StringComparison.Ordinal);
        Assert.Contains("Can frontend fabricate shipping-method authority? No.", findings, StringComparison.Ordinal);
        Assert.Contains("Is Shipping Method Authority production-certifiable? Yes.", findings, StringComparison.Ordinal);
        HistoricalShippingCertificationBoundary.AssertPriorClosureConditional();
    }
}
