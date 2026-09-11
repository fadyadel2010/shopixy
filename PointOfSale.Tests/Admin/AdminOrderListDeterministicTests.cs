using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;

namespace PointOfSale.Tests.Admin;

public sealed class AdminOrderListDeterministicTests
{
    [Fact]
    public async Task ListOrders_ClampsOutOfRangePageAndUsesOrderIdTieBreaker()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var createdAt = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var thirdId = Guid.Parse("00000000-0000-0000-0000-000000000003");

        context.Orders.AddRange(
            NewOrder(thirdId, tenantId, "ORD-TIE-3", createdAt),
            NewOrder(firstId, tenantId, "ORD-TIE-1", createdAt),
            NewOrder(secondId, tenantId, "ORD-TIE-2", createdAt));
        await context.SaveChangesAsync();

        var result = await CreateService(context).ListOrdersAsync(
            tenantId,
            new AdminOrderListQuery
            {
                Page = 99,
                PageSize = 2,
                SortBy = "createdAt",
                SortDirection = "desc"
            },
            "deterministic-page-clamp");

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(2, result.Page);
        Assert.False(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
        Assert.Equal(thirdId, Assert.Single(result.Items).OrderId);
    }

    [Fact]
    public async Task ListOrders_CombinedFiltersShareCountAndRowsWithoutOptionalRelations()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var targetId = Guid.NewGuid();
        var target = NewOrder(
            targetId,
            tenantId,
            "ORD-FILTER-TARGET",
            new DateTime(2026, 8, 26, 9, 30, 0, DateTimeKind.Utc));
        target.Status = OrderStatus.Processing;
        target.PaymentStatus = PaymentStatus.Pending;

        var wrongStatus = NewOrder(Guid.NewGuid(), tenantId, "ORD-FILTER-WRONG-STATUS", target.CreatedAt);
        wrongStatus.Status = OrderStatus.Confirmed;
        wrongStatus.PaymentStatus = PaymentStatus.Pending;

        var wrongPayment = NewOrder(Guid.NewGuid(), tenantId, "ORD-FILTER-WRONG-PAYMENT", target.CreatedAt);
        wrongPayment.Status = OrderStatus.Processing;
        wrongPayment.PaymentStatus = PaymentStatus.Failed;

        var wrongDate = NewOrder(
            Guid.NewGuid(),
            tenantId,
            "ORD-FILTER-WRONG-DATE",
            new DateTime(2026, 8, 25, 9, 30, 0, DateTimeKind.Utc));
        wrongDate.Status = OrderStatus.Processing;
        wrongDate.PaymentStatus = PaymentStatus.Pending;

        context.Orders.AddRange(target, wrongStatus, wrongPayment, wrongDate);
        await context.SaveChangesAsync();

        target.CreatedAt = new DateTime(2026, 8, 26, 9, 30, 0, DateTimeKind.Utc);
        wrongStatus.CreatedAt = target.CreatedAt;
        wrongPayment.CreatedAt = target.CreatedAt;
        wrongDate.CreatedAt = new DateTime(2026, 8, 25, 9, 30, 0, DateTimeKind.Utc);
        context.Entry(target).Property(order => order.CreatedAt).IsModified = true;
        context.Entry(wrongStatus).Property(order => order.CreatedAt).IsModified = true;
        context.Entry(wrongPayment).Property(order => order.CreatedAt).IsModified = true;
        context.Entry(wrongDate).Property(order => order.CreatedAt).IsModified = true;
        await context.SaveChangesAsync();

        var result = await CreateService(context).ListOrdersAsync(
            tenantId,
            new AdminOrderListQuery
            {
                Page = 1,
                PageSize = 20,
                Search = "  FILTER-TARGET  ",
                Status = OrderStatus.Processing,
                PaymentStatus = PaymentStatus.Pending,
                FromDate = new DateTime(2026, 8, 26),
                ToDate = new DateTime(2026, 8, 26)
            },
            "deterministic-combined-filters");

        var listed = Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(targetId, listed.OrderId);

        var details = await CreateService(context).GetOrderDetailsAsync(
            tenantId,
            targetId,
            "deterministic-missing-optional-evidence");
        Assert.NotNull(details);
        Assert.Empty(details!.Payments);
        Assert.Null(details.Shipment);
        Assert.Null(details.Customer.CustomerId);
    }

    [Fact]
    public async Task ListAndDetails_ExcludeCanonicalOrdersOutsideTheStorefrontRouteBoundary()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var storefrontId = Guid.NewGuid();
        var posId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 8, 26, 15, 0, 0, DateTimeKind.Utc);
        var storefront = NewOrder(storefrontId, tenantId, "ORD-STOREFRONT", createdAt);
        var pos = NewOrder(posId, tenantId, "ORD-POS", createdAt);
        pos.Source = OrderSource.POS;

        context.Orders.AddRange(storefront, pos);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.ListOrdersAsync(
            tenantId,
            new AdminOrderListQuery { Page = 1, PageSize = 20 },
            "deterministic-source-boundary");

        Assert.Equal(storefrontId, Assert.Single(result.Items).OrderId);
        Assert.NotNull(await service.GetOrderDetailsAsync(
            tenantId,
            storefrontId,
            "deterministic-storefront-detail"));
        Assert.Null(await service.GetOrderDetailsAsync(
            tenantId,
            posId,
            "deterministic-pos-detail"));
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options, new StaticTenantContext(tenantId));
    }

    private static AdminOrderService CreateService(ApplicationDbContext context) => new(
        context,
        Mock.Of<IEventPublisher>(),
        Mock.Of<IRefundEvidenceService>(),
        Mock.Of<ICouponValidationService>(),
        Mock.Of<IPaymentLifecycleService>(),
        Mock.Of<IProductVariantStockRepository>(),
        Mock.Of<IR2StorageService>(),
        Mock.Of<ILogger<AdminOrderService>>());

    private static Order NewOrder(Guid id, Guid tenantId, string orderNumber, DateTime createdAt) => new()
    {
        Id = id,
        TenantId = tenantId,
        OrderNumber = orderNumber,
        GuestName = "Order Guest",
        GuestEmail = "guest@example.test",
        GuestPhone = "+201000000000",
        IsGuestCheckout = true,
        ShippingAddress = "Test Street",
        ShippingCity = "Cairo",
        ShippingCountry = "EG",
        Subtotal = 100m,
        Total = 100m,
        Currency = "EGP",
        PaymentMethodCode = "COD",
        PaymentMethod = PaymentMethod.Cash,
        PaymentStatus = PaymentStatus.Pending,
        Status = OrderStatus.Pending,
        Source = OrderSource.Ecommerce,
        CreatedAt = createdAt
    };

    private sealed class StaticTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }
}
