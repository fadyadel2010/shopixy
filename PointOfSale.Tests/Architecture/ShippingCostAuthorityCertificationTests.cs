namespace PointOfSale.Tests.Architecture;

public sealed class ShippingCostAuthorityCertificationTests
{
    [Fact]
    public void ShippingCostCalculation_MustUseBackendConfigurationAndAuthoritativeCartInputs()
    {
        var calculation = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Pricing/ShippingCalculationService.cs");
        var shipping = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");

        Assert.Contains("baseRate + (perKgRate * Math.Max(0m, totalWeight))", calculation, StringComparison.Ordinal);
        Assert.Contains("freeShippingThreshold.HasValue && subtotal >= freeShippingThreshold.Value", calculation, StringComparison.Ordinal);
        Assert.Contains("TotalWeightKg = group.Sum(line => line.TotalWeight)", CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs"), StringComparison.Ordinal);
        Assert.Contains("TotalWeightKg = group.Sum(line => line.TotalWeight)", CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs"), StringComparison.Ordinal);
        Assert.Contains("_shippingCalculationService.TryCalculate", shipping, StringComparison.Ordinal);
        Assert.Contains("OrderBy(candidate => candidate.EffectivePrice)", shipping, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckout_MustRejectForgedCostCandidatesAndPersistOnlyServerResolvedValues()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("bypassCache: true", checkout, StringComparison.Ordinal);
        Assert.Contains("requestedShippingTotal != shippingResult.TotalShipping", checkout, StringComparison.Ordinal);
        Assert.Contains("requestedShippingPrice != storeSelection.ShippingPrice", checkout, StringComparison.Ordinal);
        Assert.Contains("\"SHIPPING_CHANGED\"", checkout, StringComparison.Ordinal);
        Assert.Contains("Shipping = shippingSelection.ShippingPrice", checkout, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = shippingSelection.ShippingOptionId", checkout, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrder.ShippingTotal = RoundCurrency(marketplaceOrder.SubOrders.Sum(subOrder => subOrder.Shipping))", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotsAndProjections_MustCopySqlCreatedOrderEvidenceRatherThanCreateCost()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubOrderProjectionConsumer.cs");
        var repair = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairSupport.cs");

        Assert.Contains("ShippingSnapshotJson = BuildShippingSnapshotJson(shippingSelection)", checkout, StringComparison.Ordinal);
        Assert.Contains("ShippingTotal = order.ShippingTotal", projection, StringComparison.Ordinal);
        Assert.Contains("Shipping = subOrder.Shipping", projection, StringComparison.Ordinal);
        Assert.Contains("ShippingTotal = order.ShippingTotal", repair, StringComparison.Ordinal);
        Assert.Contains("Shipping = subOrder.Shipping", repair, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserCartPersistence_MustNotSyncOrRestoreShippingCostAuthority()
    {
        var cartDto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/CommerceHub/CommerceHubCartDtos.cs");
        var pricingRequest = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/types/cart.ts");
        var checkoutPage = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");
        var cartUpsertRequest = cartDto[..cartDto.IndexOf("public class CommerceHubCartVariantAttributeDto", StringComparison.Ordinal)];

        Assert.DoesNotContain("ShippingTotal", cartUpsertRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("ShippingPrice", cartUpsertRequest, StringComparison.Ordinal);
        Assert.Contains("shippingSelections: [...selections]", pricingRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("shippingFee", pricingRequest[pricingRequest.IndexOf("buildCommerceHubCartPricingRequest", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("await cartService.priceCart", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("authoritativePricing.shippingTotal", checkoutPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifact_MustRecordM8EShippingCostTruth()
    {
        Assert.Contains(
            "PASS M8-E Shipping Cost Authority",
            CertificationSource.ReadRepoFile("docs/architecture/shipping-cost-authority-certification-findings.md"),
            StringComparison.Ordinal);
    }
}
