using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Tests.Infrastructure;

public sealed class OrderPaymentLineageAuthorityTests
{
    [Fact]
    public async Task LoadCurrentVisibilityAsync_ReturnsCurrentSqlPaymentTruth()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Orders.Add(CreateOrder(tenantId, orderId, PaymentStatus.Refunded));
        await dbContext.SaveChangesAsync();

        var snapshot = await OrderPaymentLineageAuthority.LoadCurrentVisibilityAsync(
            dbContext,
            tenantId,
            orderId);

        Assert.NotNull(snapshot);
        Assert.Equal(orderId, snapshot!.OrderId);
        Assert.Equal(PaymentStatus.Refunded, snapshot.PaymentStatus);
    }

    [Fact]
    public async Task LoadCurrentVisibilityAsync_FailsClosed_ForDeletedOrCrossTenantPaymentLineage()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Orders.Add(CreateOrder(tenantId, orderId, PaymentStatus.Settled));
        await dbContext.SaveChangesAsync();

        var crossTenantSnapshot = await OrderPaymentLineageAuthority.LoadCurrentVisibilityAsync(
            dbContext,
            otherTenantId,
            orderId);

        Assert.Null(crossTenantSnapshot);

        var order = await dbContext.Orders.SingleAsync(candidate => candidate.Id == orderId);
        order.IsDeleted = true;
        await dbContext.SaveChangesAsync();

        var deletedSnapshot = await OrderPaymentLineageAuthority.LoadCurrentVisibilityAsync(
            dbContext,
            tenantId,
            orderId);

        Assert.Null(deletedSnapshot);
    }

    private static Order CreateOrder(Guid tenantId, Guid orderId, PaymentStatus paymentStatus)
    {
        return new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = $"PAY-{orderId:N}",
            Status = OrderStatus.Confirmed,
            PaymentStatus = paymentStatus,
            Currency = "EGP",
            ShippingAddress = "Payment authority street",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OrderPaymentLineageAuthority_{Guid.NewGuid():N}")
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new ApplicationDbContext(options, tenantContext);
    }
}
