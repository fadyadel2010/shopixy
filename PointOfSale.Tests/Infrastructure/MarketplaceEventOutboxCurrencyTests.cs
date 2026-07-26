using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Eventing;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Infrastructure;

public sealed class MarketplaceEventOutboxCurrencyTests
{
    [Fact]
    public async Task EnqueueAsync_WhenCurrencyIsMissing_RejectsEventAndDoesNotStageOutboxMessage()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId, "EGP");
        var service = new MarketplaceEventOutboxService(
            dbContext,
            NullLogger<MarketplaceEventOutboxService>.Instance,
            new CommerceReliabilityTelemetry());

        var exception = await Assert.ThrowsAsync<FinancialCurrencyMissingException>(() =>
            service.EnqueueAsync(
                new OrderCreatedEvent
                {
                    CorrelationId = "corr-missing",
                    EntityId = orderId,
                    TenantId = tenantId,
                    OrderNumber = "ORD-MISSING",
                    TotalAmount = 100m,
                    SubtotalAmount = 90m,
                    Currency = string.Empty,
                    Items =
                    [
                        new MarketplaceOrderItem
                        {
                            ProductId = Guid.NewGuid(),
                            Sku = "SKU-1",
                            Quantity = 1,
                            UnitPrice = 100m
                        }
                    ]
                },
                CancellationToken.None));

        Assert.Equal("marketplace.order_created.currency", exception.Component);
        Assert.Equal(0, await dbContext.Set<MarketplaceEventOutboxMessage>().CountAsync());
        
        // Verify no entity was even staged in the ChangeTracker
        var trackedMessages = dbContext.ChangeTracker.Entries<MarketplaceEventOutboxMessage>().ToList();
        Assert.Empty(trackedMessages);
    }

    [Fact]
    public async Task EnqueueAsync_WhenCurrencyMatchesTenant_StagesOutboxMessage()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId, "EGP");
        var service = new MarketplaceEventOutboxService(
            dbContext,
            NullLogger<MarketplaceEventOutboxService>.Instance,
            new CommerceReliabilityTelemetry());

        await service.EnqueueAsync(
            new OrderCreatedEvent
            {
                CorrelationId = "corr-valid",
                EntityId = orderId,
                TenantId = tenantId,
                OrderNumber = "ORD-VALID",
                TotalAmount = 100m,
                SubtotalAmount = 90m,
                Currency = "EGP",
                Items =
                [
                    new MarketplaceOrderItem
                    {
                        ProductId = Guid.NewGuid(),
                        Sku = "SKU-1",
                        Quantity = 1,
                        UnitPrice = 100m
                    }
                ]
            },
            CancellationToken.None);

        await dbContext.SaveChangesAsync();

        Assert.Equal(1, await dbContext.Set<MarketplaceEventOutboxMessage>().CountAsync());
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId, string currency)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = $"tenant-{tenantId:N}",
            Slug = $"tenant-{tenantId:N}",
            Currency = currency,
            IsActive = true,
            Status = TenantStatus.Active
        });
        context.SaveChanges();
        return context;
    }
}
