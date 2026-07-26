using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class PromoServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly PromoService _service;

    public PromoServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _service = new PromoService(_dbContext);
    }

    [Fact]
    public async Task ValidatePromoAsync_ShouldReject_WhenCustomerUsageLimitIsReached()
    {
        var customerId = Guid.NewGuid();
        var promo = CreatePromoCode("LIMIT1", maxUsagePerCustomer: 1);
        _dbContext.PromoCodes.Add(promo);
        _dbContext.PromoCodeUsages.Add(new PromoCodeUsage
        {
            Id = Guid.NewGuid(),
            PromoCodeId = promo.Id,
            MarketplaceOrderId = Guid.NewGuid(),
            CustomerId = customerId,
            PromoCodeSnapshot = promo.Code,
            DiscountAmount = 2m,
            EligibleBaseCommissionAmount = 10m,
            AppliedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.ValidatePromoAsync(
                promo.Code,
                new[]
                {
                    new PromoCommissionLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m)
                },
                customerId));

        Assert.Equal("PROMO_CUSTOMER_USAGE_LIMIT_REACHED", exception.Code);
    }

    [Fact]
    public async Task ConsumePromoAsync_ShouldBeIdempotent_PerMarketplaceOrder()
    {
        var customerId = Guid.NewGuid();
        var promo = CreatePromoCode("IDEMPOTENT");
        var customer = CreateCustomer(customerId);
        var order = CreateMarketplaceOrder(customer.Id);

        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.PromoCodes.Add(promo);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        await SaveCommerceHubFixtureAsync();

        var first = await _service.ConsumePromoAsync(
            promo,
            order.Id,
            customerId,
            discountAmount: 2.5m,
            eligibleBaseCommissionAmount: 10m,
            correlationId: "promo-test-1");
        await _dbContext.SaveChangesAsync();

        var second = await _service.ConsumePromoAsync(
            promo,
            order.Id,
            customerId,
            discountAmount: 2.5m,
            eligibleBaseCommissionAmount: 10m,
            correlationId: "promo-test-1");

        var usages = await _dbContext.PromoCodeUsages
            .Where(usage => usage.PromoCodeId == promo.Id)
            .ToListAsync();
        var usedCount = await _dbContext.PromoCodes
            .Where(item => item.Id == promo.Id)
            .Select(item => item.UsedCount)
            .SingleAsync();

        Assert.True(first.IsApplied);
        Assert.False(first.WasAlreadyConsumed);
        Assert.True(second.IsApplied);
        Assert.True(second.WasAlreadyConsumed);
        Assert.Single(usages);
        Assert.Equal(1, usedCount);
    }

    [Fact]
    public async Task ConsumePromoAsync_ShouldSkip_WhenPromoExpiredBeforeFinalClaim()
    {
        var promo = CreatePromoCode("EXPIRED", expiryDate: DateTime.UtcNow.AddMinutes(-1));
        var customer = CreateCustomer(Guid.NewGuid());
        var order = CreateMarketplaceOrder(customer.Id);

        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.PromoCodes.Add(promo);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        await SaveCommerceHubFixtureAsync();

        var result = await _service.ConsumePromoAsync(
            promo,
            order.Id,
            customerId: Guid.NewGuid(),
            discountAmount: 3m,
            eligibleBaseCommissionAmount: 12m,
            correlationId: "promo-test-expired");

        Assert.False(result.IsApplied);
        Assert.Equal("PROMO_EXPIRED", result.SkipCode);
        Assert.Empty(await _dbContext.PromoCodeUsages.ToListAsync());
    }

    [Fact]
    public async Task ConsumePromoAsync_ShouldReloadSqlAuthority_AndRejectStaleActivePayload()
    {
        var promo = CreatePromoCode("STALEACTIVE");
        var customer = CreateCustomer(Guid.NewGuid());
        var order = CreateMarketplaceOrder(customer.Id);

        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.PromoCodes.Add(promo);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        await SaveCommerceHubFixtureAsync();

        _dbContext.Entry(promo).State = EntityState.Detached;
        var stalePayload = CreatePromoCode("STALEACTIVE");
        stalePayload.Id = promo.Id;

        var authoritativePromo = await _dbContext.PromoCodes.SingleAsync(item => item.Id == promo.Id);
        authoritativePromo.IsActive = false;
        authoritativePromo.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var result = await _service.ConsumePromoAsync(
            stalePayload,
            order.Id,
            customer.Id,
            discountAmount: 3m,
            eligibleBaseCommissionAmount: 12m,
            correlationId: "promo-test-stale-payload");

        Assert.False(result.IsApplied);
        Assert.Equal("PROMO_INACTIVE", result.SkipCode);
        Assert.Empty(await _dbContext.PromoCodeUsages.ToListAsync());
    }

    [Fact]
    public async Task ConsumePromoAsync_ShouldSkip_WhenAnotherPromoWasAlreadyConsumedForOrder()
    {
        var customer = CreateCustomer(Guid.NewGuid());
        var firstPromo = CreatePromoCode("FIRST");
        var secondPromo = CreatePromoCode("SECOND");
        var order = CreateMarketplaceOrder(customer.Id);

        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.PromoCodes.AddRange(firstPromo, secondPromo);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        await SaveCommerceHubFixtureAsync();

        var first = await _service.ConsumePromoAsync(
            firstPromo,
            order.Id,
            customer.Id,
            discountAmount: 2m,
            eligibleBaseCommissionAmount: 10m,
            correlationId: "promo-test-first");
        await _dbContext.SaveChangesAsync();

        var second = await _service.ConsumePromoAsync(
            secondPromo,
            order.Id,
            customer.Id,
            discountAmount: 1m,
            eligibleBaseCommissionAmount: 10m,
            correlationId: "promo-test-second");

        var usages = await _dbContext.PromoCodeUsages
            .OrderBy(usage => usage.AppliedAtUtc)
            .ToListAsync();

        Assert.True(first.IsApplied);
        Assert.False(second.IsApplied);
        Assert.Equal("PROMO_ALREADY_CONSUMED_FOR_ORDER", second.SkipCode);
        Assert.Single(usages);
        Assert.Equal(firstPromo.Id, usages[0].PromoCodeId);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static PromoCode CreatePromoCode(
        string code,
        int? maxUsagePerCustomer = null,
        DateTime? expiryDate = null)
    {
        return new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = code,
            DiscountType = PromoDiscountType.Percentage,
            DiscountValue = 20m,
            MaxUsagePerCustomer = maxUsagePerCustomer,
            ExpiryDate = expiryDate,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static CommerceHubCustomer CreateCustomer(Guid id)
    {
        var customer = CommerceHubCustomer.Create(
            email: $"{id:N}@example.com",
            name: "Promo Test Customer",
            phoneNumber: "01000000000",
            passwordHash: "HASH");
        return customer;
    }

    private static CommerceHubMarketplaceOrder CreateMarketplaceOrder(Guid customerId)
    {
        return new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            OrderNumber = $"CH-TEST-{Guid.NewGuid():N}".ToUpperInvariant(),
            FullName = "Promo Test Customer",
            PhoneNumber = "01000000000",
            AddressLine = "1 Promo Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery",
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };
    }

    private async Task SaveCommerceHubFixtureAsync()
    {
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        {
            await _dbContext.SaveChangesAsync();
        }
    }
}
