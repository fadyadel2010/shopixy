using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class OrderOperationalLifecycleAuthorityHardeningTests
{
    [Fact]
    public void PaymentStatusInterceptor_BlocksDirectMutationEvenWhenEntityAcceptFlagIsCleared()
    {
        var options = CreateOptions(
            new PaymentStatusSaveChangesInterceptor(
                NullLogger<PaymentStatusSaveChangesInterceptor>.Instance));

        using var db = new ApplicationDbContext(options);
        var order = CreateOrder();
        db.Orders.Add(order);
        db.SaveChanges();

        order.PaymentStatus = PaymentStatus.Collected;
        order.AcceptPaymentStatusChange();

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("PaymentStatus can only be changed via IPaymentLifecycleService", ex.Message);
    }

    [Fact]
    public void FulfillmentStatusInterceptor_BlocksDirectMutationEvenWhenEntityAcceptFlagIsCleared()
    {
        var options = CreateOptions(
            new FulfillmentStatusSaveChangesInterceptor(
                NullLogger<FulfillmentStatusSaveChangesInterceptor>.Instance));

        using var db = new ApplicationDbContext(options);
        var order = CreateOrder();
        db.Orders.Add(order);
        db.SaveChanges();

        order.FulfillmentStatus = FulfillmentStatus.Confirmed;
        order.AcceptFulfillmentStatusChange();

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("FulfillmentStatus can only be changed via IOrderFulfillmentService", ex.Message);
    }

    [Fact]
    public void OrderItemPaymentAmounts_MustBeDatabaseConstrained()
    {
        var options = CreateOptions();
        using var db = new ApplicationDbContext(options);

        var orderItem = db.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(OrderItem));
        Assert.NotNull(orderItem);

        var constraint = orderItem!.GetCheckConstraints()
            .SingleOrDefault(item => item.Name == "CK_OrderItems_PaymentAmounts_Valid");

        Assert.NotNull(constraint);
        Assert.Equal(
            "\"PaidAmount\" >= 0 AND \"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"PaidAmount\"",
            constraint!.Sql);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OrderOperationalLifecycleAuthority_{Guid.NewGuid():N}");

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return builder.Options;
    }

    private static Order CreateOrder() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        OrderNumber = $"ORD-{Guid.NewGuid():N}",
        Currency = "EGP",
        PaymentMethodCode = "CARD",
        Total = 100m,
        Subtotal = 100m
    };
}
