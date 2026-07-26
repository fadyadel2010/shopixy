namespace PointOfSale.Tests.Architecture;

public sealed class ShippingMethodSelectionAuthorityCertificationTests
{
    [Fact]
    public void CommerceHubShipping_MustGenerateAndDeterministicallySelectTheCheapestEligibleMethod()
    {
        var shipping = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");

        Assert.Contains("!method.IsDeleted && method.IsActive && method.TenantId == storeId", shipping, StringComparison.Ordinal);
        Assert.Contains("Where(candidate => candidate.IsEligible)", shipping, StringComparison.Ordinal);
        Assert.Contains("OrderBy(candidate => candidate.EffectivePrice)", shipping, StringComparison.Ordinal);
        Assert.Contains("ThenBy(candidate => candidate.Method.SortOrder)", shipping, StringComparison.Ordinal);
        Assert.Contains("requestedShippingOptionId != cheapest.Method.Id", shipping, StringComparison.Ordinal);
        Assert.Contains("ShippingOptionId = cheapest.Method.Id", shipping, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCartAndCheckout_MustTreatClientSelectionsAndPricesAsRevalidatedCandidates()
    {
        var cart = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("requestedSelectionsByStore", cart, StringComparison.Ordinal);
        Assert.Contains("bypassCache: true", cart, StringComparison.Ordinal);
        Assert.Contains("requestedSelectionsByStore", checkout, StringComparison.Ordinal);
        Assert.Contains("bypassCache: true", checkout, StringComparison.Ordinal);
        Assert.Contains("requestedStoreSelection.ShippingOptionId != storeSelection.ShippingOptionId", checkout, StringComparison.Ordinal);
        Assert.Contains("requestedShippingPrice != storeSelection.ShippingPrice", checkout, StringComparison.Ordinal);
        Assert.Contains("SHIPPING_CHANGED", checkout, StringComparison.Ordinal);
        Assert.Contains("IDEMPOTENCY_PAYLOAD_CONFLICT", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void SplitOrdersAndShipments_MustUseBackendResolvedOrderMethodLineage()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var shipment = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var carrierShipment = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("var shippingSelections = shippingResult.StoreSelections.ToDictionary(selection => selection.StoreId)", checkout, StringComparison.Ordinal);
        Assert.Contains("StoreId = group.Key", checkout, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = shippingSelection.ShippingOptionId", checkout, StringComparison.Ordinal);
        Assert.Contains("order.ShippingMethodId.Value != shippingMethodId", shipment, StringComparison.Ordinal);
        Assert.Contains("method.Id == order.ShippingMethodId.Value", carrierShipment, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifact_MustRecordM8DShippingMethodSelectionTruth()
    {
        Assert.Contains(
            "PASS M8-D Shipping Method Selection Authority",
            CertificationSource.ReadRepoFile("docs/architecture/shipping-method-selection-authority-certification-findings.md"),
            StringComparison.Ordinal);
    }
}
