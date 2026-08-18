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
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class AdminOrderListPostgresTests
{
    [Fact]
    public async Task ListOrders_Postgres_ReturnsTenantStorefrontOrderWithoutSetOperationFailure()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext))
        {
            return;
        }

        await using (var scope = databaseScope!)
        {
            await using var context = scope.CreateDbContext(tenantContext!);
            var orderId = Guid.NewGuid();
            var marketplaceOrderId = Guid.NewGuid();
            var subOrderId = Guid.NewGuid();
            context.Tenants.Add(NewTenant(tenantId));
            context.Orders.Add(NewStorefrontOrder(orderId, tenantId));
            context.PaymentIntents.Add(NewManualWalletPaymentIntent(orderId, tenantId));
            SeedMarketplaceSubOrder(context, tenantId, marketplaceOrderId, subOrderId);
            using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
            using var shippingAssignmentBoundary = CommerceHubBoundaryMutationScope.BeginShippingAssignment();
            await context.SaveChangesAsync();

            var service = CreateService(context);

            var result = await service.ListOrdersAsync(
                tenantId,
                new AdminOrderListQuery
                {
                    Page = 1,
                    PageSize = 20,
                    SortBy = "createdAt",
                    SortDirection = "desc"
                },
                "admin-order-list-postgres");

            Assert.Equal(2, result.TotalCount);
            var storefrontOrder = Assert.Single(result.Items, item => item.OrderId == orderId);
            Assert.Equal(tenantId, storefrontOrder.TenantId);
            Assert.Equal($"ORD-TEST-{orderId:N}", storefrontOrder.OrderNumber);
            Assert.Equal(PaymentStatus.AwaitingVerification, storefrontOrder.PaymentStatus);
            Assert.False(storefrontOrder.IsMarketplaceOrder);

            var marketplaceOrder = Assert.Single(result.Items, item => item.OrderId == subOrderId);
            Assert.Equal(tenantId, marketplaceOrder.TenantId);
            Assert.True(marketplaceOrder.IsMarketplaceOrder);
            Assert.Equal(marketplaceOrderId.ToString(), marketplaceOrder.SourceOrderId);
        }
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

    private static Tenant NewTenant(Guid tenantId) => new()
    {
        Id = tenantId,
        Name = "Admin Order List Tenant",
        Slug = $"admin-order-list-{tenantId:N}",
        Currency = "EGP",
        IsActive = true,
        Status = TenantStatus.Active,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static Order NewStorefrontOrder(Guid orderId, Guid tenantId) => new()
    {
        Id = orderId,
        TenantId = tenantId,
        OrderNumber = $"ORD-TEST-{orderId:N}",
        GuestName = "Storefront Guest",
        GuestEmail = "guest@example.test",
        GuestPhone = "+201000000000",
        IsGuestCheckout = true,
        ShippingAddress = "Test Street",
        ShippingCity = "Cairo",
        ShippingCountry = "EG",
        Subtotal = 100m,
        Total = 100m,
        Currency = "EGP",
        PaymentMethodCode = "MANUALWALLET",
        PaymentMethod = PaymentMethod.BankTransfer,
        PaymentStatus = PaymentStatus.AwaitingVerification,
        Source = OrderSource.Ecommerce,
        CreatedAt = DateTime.UtcNow
    };

    private static PaymentIntent NewManualWalletPaymentIntent(Guid orderId, Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        OrderId = orderId,
        Amount = 100m,
        Currency = "EGP",
        PaymentMethod = PaymentMethod.BankTransfer,
        Provider = PaymentProvider.ManualWallet,
        PaymentReference = $"PAY-{orderId:N}",
        IdempotencyKey = $"manual-wallet-{orderId:N}",
        Status = PaymentIntentStatus.AwaitingVerification,
        ProofImageUrl = $"tenants/{tenantId:D}/manual-wallet-proofs/checkout-sessions/{Guid.NewGuid():D}/proof.png",
        ExpiresAtUtc = DateTime.UtcNow.AddDays(2),
        CreatedAt = DateTime.UtcNow
    };

    private static void SeedMarketplaceSubOrder(
        ApplicationDbContext context,
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId)
    {
        var customer = CommerceHubCustomer.Create(
            $"marketplace-{marketplaceOrderId:N}@example.test",
            "Marketplace Customer",
            "+201000000001",
            "test-hash");
        var marketplaceOrder = new CommerceHubMarketplaceOrder
        {
            Id = marketplaceOrderId,
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = $"CH-{marketplaceOrderId:N}",
            FullName = "Marketplace Customer",
            PhoneNumber = "+201000000001",
            AddressLine = "Marketplace Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            Subtotal = 75m,
            TotalAmount = 75m,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        var subOrder = new CommerceHubSubOrder
        {
            Id = subOrderId,
            MarketplaceOrderId = marketplaceOrderId,
            MarketplaceOrder = marketplaceOrder,
            OrderNumber = $"CH-SUB-{subOrderId:N}",
            StoreId = tenantId,
            StoreName = "Admin Order List Tenant",
            Currency = "EGP",
            Subtotal = 75m,
            Total = 75m,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        };

        context.CommerceHubCustomers.Add(customer);
        context.CommerceHubMarketplaceOrders.Add(marketplaceOrder);
        context.CommerceHubSubOrders.Add(subOrder);
    }
}
