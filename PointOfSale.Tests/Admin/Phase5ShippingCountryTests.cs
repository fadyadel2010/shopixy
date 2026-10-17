using Moq;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.Services.Checkout;
using PointOfSale.API.Services.Pricing;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests.Admin;

public sealed class Phase5ShippingCountryTests
{
    [Theory]
    [InlineData("[\"EG\"]", "eg")]
    [InlineData(null, "US")]
    [InlineData("[\"EG\"]", null)]
    public async Task SupportedCountryAndIncompletePreview_PreserveExistingPrice(string? scope, string? country)
    {
        var tax = new Mock<ITaxCalculationService>();
        tax.Setup(x => x.CalculateAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxComputationResult(0, 0, "synthetic", country));
        var service = new StorefrontPricingService(new ShippingCalculationService(), tax.Object);
        var result = await service.CalculateCheckoutAsync(Guid.NewGuid(), new CartSnapshot { Items = [], CartUpdatedAtUtc = DateTime.UtcNow },
            new ShippingMethodDocument { AvailableCountries = scope, BaseRate = 7.35m }, null, country);
        Assert.Equal(7.35m, result.Shipping);
    }

    [Theory]
    [InlineData("[\"EG\"]", "US")]
    [InlineData("[]", "EG")]
    [InlineData("not-json", "EG")]
    [InlineData("[\"ZZ\"]", "EG")]
    [InlineData("[null]", "EG")]
    public async Task AuthoritativePricing_RejectsUnsupportedDestinationAndMalformedScope(string scope, string country)
    {
        var tax = new Mock<ITaxCalculationService>(MockBehavior.Strict);
        var service = new StorefrontPricingService(new ShippingCalculationService(), tax.Object);
        var method = new ShippingMethodDocument { ShippingMethodId = Guid.NewGuid(), AvailableCountries = scope, BaseRate = 7.35m };
        await Assert.ThrowsAsync<CheckoutShippingMethodException>(() => service.CalculateCheckoutAsync(Guid.NewGuid(), new CartSnapshot { Items = [], CartUpdatedAtUtc = DateTime.UtcNow }, method, null, country));
        await Assert.ThrowsAsync<CheckoutShippingMethodException>(() => service.CalculateCartPreviewAsync(Guid.NewGuid(), new CartDto { Items = [] }, method, null, country));
        tax.VerifyNoOtherCalls();
    }
}
