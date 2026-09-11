using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Governance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubVendorOrderActionAuthorityTests
{
    [Fact]
    public async Task PendingCodOrder_ReturnsOnlyBackendOwnedProcessAndCancelActions()
    {
        var storeId = Guid.NewGuid();
        await using var context = CreateContext();
        var subOrder = SeedSubOrder(context, storeId, PaymentStatus.CashOnDelivery);
        using var orderLifecycle = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycle = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        await context.SaveChangesAsync();

        var authority = await CreateService(context).GetVendorOrderActionAuthorityAsync(storeId, subOrder.Id);

        Assert.Equal(storeId, authority.TenantId);
        Assert.Equal(subOrder.Id, authority.SubOrderId);
        Assert.True(authority.PaymentReady);
        Assert.False(authority.InventoryReady);
        Assert.Equal(subOrder.ProjectionMutationVersion, authority.LifecycleVersion);
        Assert.Equal(new[] { "process", "cancel" }, authority.AvailableActions.Select(action => action.Code));
        Assert.DoesNotContain(authority.AvailableActions, action => action.Code is "confirm" or "complete" or "return");
    }

    [Fact]
    public async Task ForeignVendorCannotReadActionAuthority()
    {
        await using var context = CreateContext();
        var subOrder = SeedSubOrder(context, Guid.NewGuid(), PaymentStatus.Collected);
        using var orderLifecycle = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycle = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            CreateService(context).GetVendorOrderActionAuthorityAsync(Guid.NewGuid(), subOrder.Id));

        Assert.Equal("SUBORDER_FORBIDDEN", exception.Code);
        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public async Task UnresolvedOnlinePaymentSuppressesFulfillmentButRetainsCancellation()
    {
        var storeId = Guid.NewGuid();
        await using var context = CreateContext();
        var subOrder = SeedSubOrder(context, storeId, PaymentStatus.Pending);
        subOrder.PaymentMethod = CommerceHubPaymentMethod.Online;
        subOrder.PaymentMethodCode = "PAYMOB";
        using var orderLifecycle = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycle = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        await context.SaveChangesAsync();

        var authority = await CreateService(context).GetVendorOrderActionAuthorityAsync(storeId, subOrder.Id);

        Assert.False(authority.PaymentReady);
        Assert.Equal("cancel", Assert.Single(authority.AvailableActions).Code);
        Assert.NotNull(authority.PaymentReadinessMessage);
    }

    [Fact]
    public async Task DeliveredOrder_DoesNotExposeAnIdempotentDeliverAction()
    {
        var storeId = Guid.NewGuid();
        await using var context = CreateContext();
        var subOrder = SeedSubOrder(context, storeId, PaymentStatus.Collected);
        subOrder.Status = OrderStatus.Delivered;
        using var orderLifecycle = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycle = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        await context.SaveChangesAsync();

        var authority = await CreateService(context).GetVendorOrderActionAuthorityAsync(storeId, subOrder.Id);

        Assert.Empty(authority.AvailableActions);
        Assert.Contains("No vendor fulfillment transition", authority.LifecycleMessage);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static CommerceHubOrderManagementService CreateService(ApplicationDbContext context) => new(
        context,
        Mock.Of<IInventoryConsistencyService>(),
        Mock.Of<ICommerceHubFinancialWorkflowService>(),
        Mock.Of<IUserGovernanceService>(),
        Mock.Of<IEventPublisher>(),
        Mock.Of<ILogger<CommerceHubOrderManagementService>>());

    private static CommerceHubSubOrder SeedSubOrder(
        ApplicationDbContext context,
        Guid storeId,
        PaymentStatus paymentStatus)
    {
        var customer = CommerceHubCustomer.Create(
            $"vendor-orders-{Guid.NewGuid():N}@example.test",
            "Marketplace Customer",
            "+201000000001",
            "hash");
        var parent = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = "ORD-CH-AUTH-PARENT",
            FullName = "Marketplace Customer",
            PhoneNumber = "+201000000001",
            AddressLine = "Test Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            PaymentStatus = paymentStatus,
            TotalAmount = 100m,
            Subtotal = 100m
        };
        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = parent.Id,
            MarketplaceOrder = parent,
            OrderNumber = "ORD-CH-AUTH-A",
            StoreId = storeId,
            StoreName = "Vendor A",
            Currency = "EGP",
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            PaymentStatus = paymentStatus,
            Status = OrderStatus.Pending,
            Subtotal = 100m,
            Total = 100m
        };
        subOrder.Items.Add(new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrder = subOrder,
            SubOrderId = subOrder.Id,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Product A",
            Quantity = 1,
            UnitPrice = 100m,
            Total = 100m
        });
        context.CommerceHubCustomers.Add(customer);
        context.CommerceHubMarketplaceOrders.Add(parent);
        context.CommerceHubSubOrders.Add(subOrder);
        return subOrder;
    }
}
