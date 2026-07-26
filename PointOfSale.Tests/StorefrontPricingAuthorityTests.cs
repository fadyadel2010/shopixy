using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.Services.Pricing;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests;

public class StorefrontPricingAuthorityTests
{
    [Fact]
    public async Task CheckoutPricing_RecomputesFromCurrentSnapshot_WhenCartPreviewWasStale()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(
            tenantId,
            countryCode: "US",
            ("Tax.Rate", "10"));

        var pricingService = CreatePricingService(dbContext);
        var shippingMethod = CreateShippingMethod(tenantId, baseRate: 5m);

        var cartPreview = await pricingService.CalculateCartPreviewAsync(
            tenantId,
            new CartDto
            {
                TenantId = tenantId,
                Currency = "USD",
                Items = new List<CartItemDto>
                {
                    new()
                    {
                        ProductId = Guid.NewGuid(),
                        Quantity = 1,
                        UnitPrice = 100m
                    }
                }
            },
            shippingMethod,
            paymentMethod: null,
            shippingCountry: "US");

        var checkoutPricing = await pricingService.CalculateCheckoutAsync(
            tenantId,
            new CartSnapshot
            {
                TenantId = tenantId,
                Currency = "USD",
                CartUpdatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Items = new List<CartItemSnapshot>
                {
                    new()
                    {
                        ProductId = Guid.NewGuid(),
                        Quantity = 1,
                        CartPrice = 100m,
                        CurrentPrice = 125m
                    }
                }
            },
            shippingMethod,
            paymentMethod: null,
            shippingCountry: "US");

        Assert.Equal(100m, cartPreview.Subtotal);
        Assert.Equal(125m, checkoutPricing.Subtotal);
        Assert.Equal(142.50m, checkoutPricing.Total);
        Assert.NotEqual(cartPreview.Total, checkoutPricing.Total);
    }

    [Fact]
    public async Task TaxCalculation_UsesConfiguredTenantRate()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(
            tenantId,
            countryCode: "US",
            ("Tax.Rate", "15.5"));

        var taxService = CreateTaxService(dbContext);

        var result = await taxService.CalculateAsync(tenantId, 200m, "US");

        Assert.Equal(0.155m, result.TaxRate);
        Assert.Equal(31.00m, result.TaxAmount);
        Assert.Equal("tenant-setting", result.Source);
        Assert.Equal("US", result.CountryCode);
    }

    [Fact]
    public async Task TaxCalculation_IgnoresOtherTenantScopedRate()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        dbContext.Tenants.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Tenant A",
                Status = TenantStatus.Active,
                Country = "US",
                Currency = "USD"
            },
            new Tenant
            {
                Id = otherTenantId,
                Name = "Tenant B",
                Status = TenantStatus.Active,
                Country = "US",
                Currency = "USD"
            });
        dbContext.SystemSettings.Add(new SystemSetting
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenantId,
            Key = "Tax.Rate",
            Value = "22"
        });
        await dbContext.SaveChangesAsync();

        var taxService = CreateTaxService(dbContext);

        var result = await taxService.CalculateAsync(tenantId, 100m, "US");

        Assert.Equal(0.09m, result.TaxRate);
        Assert.Equal(9.00m, result.TaxAmount);
        Assert.Equal("country", result.Source);
    }

    [Fact]
    public async Task TaxCalculation_NormalizesBoundaryRate_OnePercentCase()
    {
        // Critical boundary test: rate == 1m should be treated as 1% (0.01), not 100%
        // This prevents a 100× overcharge bug
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(
            tenantId,
            countryCode: "US",
            ("Tax.Rate", "1"));

        var taxService = CreateTaxService(dbContext);

        var result = await taxService.CalculateAsync(tenantId, 100m, "US");

        // rate == 1m should normalize to 0.01 (1%), not remain as 1.0 (100%)
        Assert.Equal(0.01m, result.TaxRate);
        Assert.Equal(1.00m, result.TaxAmount); // 100 * 0.01 = 1.00, not 100 * 1.0 = 100.00
        Assert.Equal("tenant-setting", result.Source);
    }

    [Fact]
    public void StorefrontPricingOptions_NormalizesPercentageInputs()
    {
        // This test verifies the normalization behavior that occurs in Program.cs PostConfigure
        // When percentage-style values (> 1) are provided, they should be divided by 100
        var options = new StorefrontPricingOptions
        {
            DefaultTaxRate = 8m,
            CountryTaxRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["US"] = 9m,
                ["EG"] = 14m,
                ["FR"] = 0.20m // Already fractional, should not be changed
            }
        };

        // Call the shared normalization method used in Program.cs PostConfigure
        options.NormalizeTaxRates();

        // Verify normalization
        Assert.Equal(0.08m, options.DefaultTaxRate);
        Assert.Equal(0.09m, options.CountryTaxRates["US"]);
        Assert.Equal(0.14m, options.CountryTaxRates["EG"]);
        Assert.Equal(0.20m, options.CountryTaxRates["FR"]); // Should remain unchanged

        // Verify validation passes after normalization
        Assert.True(options.ValidateCountryTaxRates(out var errorMessage));
        Assert.Null(errorMessage);
    }

    [Fact]
    public void ShippingCalculationService_ReturnsSameAmountAcrossCalls()
    {
        var service = new ShippingCalculationService();

        var first = service.Calculate(
            baseRate: 10m,
            perKgRate: 2m,
            freeShippingThreshold: 100m,
            subtotal: 50m,
            totalWeight: 3m);

        var second = service.Calculate(
            baseRate: 10m,
            perKgRate: 2m,
            freeShippingThreshold: 100m,
            subtotal: 50m,
            totalWeight: 3m);

        Assert.Equal(16m, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void TryCalculate_RejectsThresholdOnlyZeroRateBeforeThreshold_Regression()
    {
        var service = new ShippingCalculationService();

        // Arrange: parameters that trigger the guard condition
        // - Non-zero freeShippingThreshold
        // - Subtotal below threshold
        // - Zero baseRate and perKgRate
        var baseRate = 0m;
        var perKgRate = 0m;
        var freeShippingThreshold = 100m;
        var subtotal = 50m;
        var totalWeight = 3m;

        // Act: Call TryCalculate with the guard enabled
        var tryCalculateResult = service.TryCalculate(
            baseRate: baseRate,
            perKgRate: perKgRate,
            freeShippingThreshold: freeShippingThreshold,
            subtotal: subtotal,
            out var tryCalculateShippingCost,
            totalWeight: totalWeight,
            rejectThresholdOnlyZeroRateBeforeThreshold: true);

        // Call Calculate with the same inputs for comparison
        var calculateResult = service.Calculate(
            baseRate: baseRate,
            perKgRate: perKgRate,
            freeShippingThreshold: freeShippingThreshold,
            subtotal: subtotal,
            totalWeight: totalWeight);

        // Assert: TryCalculate should reject (return false) when guard is triggered
        Assert.False(tryCalculateResult, "TryCalculate should return false when guard condition is met");
        Assert.Equal(0m, tryCalculateShippingCost);

        // Verify Calculate returns the same value that would be computed without the guard
        Assert.Equal(0m, calculateResult);

        // Test the inverse: TryCalculate without guard should succeed and match Calculate
        var tryCalculateWithoutGuard = service.TryCalculate(
            baseRate: baseRate,
            perKgRate: perKgRate,
            freeShippingThreshold: freeShippingThreshold,
            subtotal: subtotal,
            out var shippingCostWithoutGuard,
            totalWeight: totalWeight,
            rejectThresholdOnlyZeroRateBeforeThreshold: false);

        Assert.True(tryCalculateWithoutGuard, "TryCalculate should return true when guard is disabled");
        Assert.Equal(calculateResult, shippingCostWithoutGuard);
    }

    [Fact]
    public async Task CartPreview_ComposesDiscountTaxShippingAndPaymentFeeCorrectly()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(
            tenantId,
            countryCode: "US",
            ("Tax.Rate", "10"));

        var pricingService = CreatePricingService(dbContext);
        var shippingMethod = CreateShippingMethod(tenantId, baseRate: 12m);
        var paymentMethod = CreatePaymentMethod(tenantId, fee: 3m, feeType: "fixed");

        var pricing = await pricingService.CalculateCartPreviewAsync(
            tenantId,
            new CartDto
            {
                TenantId = tenantId,
                Currency = "USD",
                DiscountTotal = 7m,
                CartPromotionDiscountTotal = 5m,
                Items = new List<CartItemDto>
                {
                    new()
                    {
                        ProductId = Guid.NewGuid(),
                        Quantity = 1,
                        UnitPrice = 100m,
                        HasActivePromotion = true,
                        DiscountedUnitPrice = 90m
                    }
                }
            },
            shippingMethod,
            paymentMethod,
            shippingCountry: "US");

        Assert.Equal(100m, pricing.Subtotal);
        Assert.Equal(15m, pricing.PromotionDiscount);
        Assert.Equal(5m, pricing.CartPromotionDiscount);
        Assert.Equal(7m, pricing.CouponDiscount);
        Assert.Equal(22m, pricing.TotalDiscount);
        Assert.Equal(12m, pricing.Shipping);
        Assert.Equal(10m, pricing.Tax);
        Assert.Equal(3m, pricing.PaymentFee);
        Assert.Equal(103m, pricing.Total);
    }

    [Fact]
    public async Task CheckoutPricing_PreservesCartLevelPromotionInDeterministicPromotionBucket()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(
            tenantId,
            countryCode: "US",
            ("Tax.Rate", "10"));

        var pricingService = CreatePricingService(dbContext);

        var pricing = await pricingService.CalculateCheckoutAsync(
            tenantId,
            new CartSnapshot
            {
                TenantId = tenantId,
                Currency = "USD",
                CartUpdatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CouponDiscountTotal = 4m,
                PromotionDiscountTotal = 6m,
                CartPromotionDiscountTotal = 5m,
                Items = new List<CartItemSnapshot>
                {
                    new()
                    {
                        ProductId = Guid.NewGuid(),
                        Quantity = 1,
                        CartPrice = 100m,
                        CurrentPrice = 100m
                    }
                }
            },
            shippingMethod: null,
            paymentMethod: null,
            shippingCountry: "US");

        Assert.Equal(4m, pricing.CouponDiscount);
        Assert.Equal(11m, pricing.PromotionDiscount);
        Assert.Equal(5m, pricing.CartPromotionDiscount);
        Assert.Equal(15m, pricing.TotalDiscount);
        Assert.Equal(95m, pricing.Total);
    }

    private static StorefrontPricingService CreatePricingService(ApplicationDbContext dbContext)
    {
        return new StorefrontPricingService(
            new ShippingCalculationService(),
            CreateTaxService(dbContext));
    }

    private static TaxCalculationService CreateTaxService(ApplicationDbContext dbContext)
    {
        return new TaxCalculationService(
            dbContext,
            Options.Create(new StorefrontPricingOptions
            {
                DefaultTaxRate = 8m,
                CountryTaxRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                {
                    ["US"] = 9m,
                    ["EG"] = 14m
                }
            }),
            NullLogger<TaxCalculationService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext(
        Guid tenantId,
        string? countryCode,
        params (string Key, string Value)[] settings)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var dbContext = new ApplicationDbContext(options);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Tenant",
            Status = TenantStatus.Active,
            Country = countryCode,
            Currency = "USD"
        });

        foreach (var setting in settings)
        {
            dbContext.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Key = setting.Key,
                Value = setting.Value
            });
        }

        dbContext.SaveChanges();
        return dbContext;
    }

    private static ShippingMethodDocument CreateShippingMethod(Guid tenantId, decimal baseRate)
    {
        return new ShippingMethodDocument
        {
            TenantId = tenantId,
            ShippingMethodId = Guid.NewGuid(),
            Name = "Standard",
            BaseRate = baseRate,
            PerKgRate = 0m,
            Currency = "USD",
            IsActive = true
        };
    }

    private static PaymentMethodDocument CreatePaymentMethod(Guid tenantId, decimal fee, string feeType)
    {
        return new PaymentMethodDocument
        {
            TenantId = tenantId,
            PaymentMethodId = Guid.NewGuid(),
            Code = "COD",
            DisplayName = "Cash on Delivery",
            Fee = fee,
            FeeType = feeType,
            IsEnabled = true,
            SupportsOffline = true
        };
    }
}
