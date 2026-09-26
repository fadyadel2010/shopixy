using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Pricing;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Tenancy;

namespace PointOfSale.Tests.Tenancy;

public sealed class CartAndTaxTenantMemoTests
{
    [Fact]
    public async Task CartCurrency_ReusesWarmedTenantWithoutQuery()
    {
        using var fixture = new TenantMemoTestFixture();
        Guid tenantId;
        using (var seed = fixture.CreateContext())
        {
            tenantId = TenantMemoTestFixture.SeedTenant(seed, "cart-currency").Id;
        }

        using var request = fixture.CreateContext();
        _ = await TenantAuthorityMemo.GetTenantByIdAsync(request, tenantId);
        fixture.Counter.Reset();

        Assert.Equal("EGP", await CreateCartService(request).GetStorefrontCurrencyAsync(tenantId, default));
        Assert.Equal(0, fixture.Counter.Count);
    }

    [Fact]
    public async Task TaxCountry_ReusesWarmedTenantWithoutAdditionalTenantQuery()
    {
        using var fixture = new TenantMemoTestFixture();
        Guid tenantId;
        using (var seed = fixture.CreateContext())
        {
            tenantId = TenantMemoTestFixture.SeedTenant(seed, "tax-country").Id;
        }

        using var request = fixture.CreateContext();
        _ = await TenantAuthorityMemo.GetTenantByIdAsync(request, tenantId);
        var service = new TaxCalculationService(
            request,
            Options.Create(new StorefrontPricingOptions
            {
                DefaultTaxRate = 0.1m,
                CountryTaxRates = new Dictionary<string, decimal> { ["EG"] = 0.14m }
            }),
            NullLogger<TaxCalculationService>.Instance);
        fixture.Counter.Reset();

        var result = await service.CalculateAsync(tenantId, 100m, countryCode: null);

        Assert.Equal("EG", result.CountryCode);
        Assert.Equal("country", result.Source);
        Assert.Equal(14m, result.TaxAmount);
        Assert.Equal(1, fixture.Counter.Count);
    }

    private static CartService CreateCartService(
        PointOfSale.Infrastructure.Data.ApplicationDbContext dbContext) =>
        new(
            dbContext,
            null!,
            Options.Create(new RedisSettings()),
            null!,
            null!,
            Mock.Of<IMongoDatabase>(),
            null!,
            null!,
            null!,
            null!,
            NullLogger<CartService>.Instance);
}
