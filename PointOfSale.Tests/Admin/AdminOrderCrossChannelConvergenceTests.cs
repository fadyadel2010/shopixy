using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;

namespace PointOfSale.Tests.Admin;

public sealed class AdminOrderCrossChannelConvergenceTests
{
    [Fact]
    public async Task ListAndDetails_ConvergeStorefrontAffiliateAndCommerceHubWithoutLosingCanonicalIdentity()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);

        var storefrontOrder = CreateStorefrontOrder(tenantId, "ORD-DIRECT", 120m);
        var affiliateOrder = CreateStorefrontOrder(tenantId, "ORD-AFFILIATE", 80m);
        var affiliateId = Guid.NewGuid();
        var promotionLinkId = Guid.NewGuid();
        affiliateOrder.AffiliateAccountId = affiliateId;
        affiliateOrder.AffiliateReferralCode = "AFF-O3";
        affiliateOrder.PromotionLinkId = promotionLinkId;
        var attribution = AffiliateAttribution.CreateV2(
            tenantId,
            affiliateOrder.Id,
            affiliateId,
            affiliateOrder.AffiliateReferralCode,
            AttributionSource.ReferralLink,
            promotionLinkId);

        context.Orders.AddRange(storefrontOrder, affiliateOrder);
        context.AffiliateAttributions.Add(attribution);

        var marketplaceOrderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        SeedMarketplaceSubOrder(context, tenantId, marketplaceOrderId, subOrderId);

        using var orderLifecycle = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var shippingAssignment = CommerceHubBoundaryMutationScope.BeginShippingAssignment();
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.ListOrdersAsync(
            tenantId,
            new AdminOrderListQuery { Page = 1, PageSize = 20, SortBy = "orderNumber", SortDirection = "asc" },
            "o3-cross-channel-list");

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);

        var directRow = Assert.Single(result.Items, item => item.OrderId == storefrontOrder.Id);
        Assert.Equal(OrderSource.Ecommerce, directRow.Source);
        Assert.Null(directRow.AffiliateAttribution);
        Assert.False(directRow.IsMarketplaceOrder);

        var affiliateRow = Assert.Single(result.Items, item => item.OrderId == affiliateOrder.Id);
        Assert.Equal(OrderSource.Ecommerce, affiliateRow.Source);
        Assert.Equal(affiliateOrder.Id, affiliateRow.AffiliateAttribution?.OrderId);
        Assert.Equal(attribution.Id, affiliateRow.AffiliateAttribution?.AttributionId);
        Assert.False(affiliateRow.IsMarketplaceOrder);

        var marketplaceRow = Assert.Single(result.Items, item => item.OrderId == subOrderId);
        Assert.Equal(OrderSource.Marketplace, marketplaceRow.Source);
        Assert.Equal(marketplaceOrderId.ToString(), marketplaceRow.SourceOrderId);
        Assert.Equal(subOrderId, marketplaceRow.MarketplaceLineage?.OrderId);
        Assert.Equal(marketplaceOrderId, marketplaceRow.MarketplaceLineage?.MarketplaceOrderId);
        Assert.True(marketplaceRow.IsMarketplaceOrder);

        var affiliateDetails = await service.GetOrderDetailsAsync(
            tenantId,
            affiliateOrder.Id,
            "o3-affiliate-details");
        Assert.NotNull(affiliateDetails);
        Assert.Equal(affiliateOrder.Id, affiliateDetails!.AffiliateAttribution?.OrderId);
        Assert.Equal(80m, affiliateDetails.Totals.Total);
        Assert.Equal(80m, Assert.Single(affiliateDetails.Items).UnitPrice);

        var marketplaceDetails = await service.GetOrderDetailsAsync(
            tenantId,
            subOrderId,
            "o3-marketplace-details");
        Assert.NotNull(marketplaceDetails);
        Assert.Equal(subOrderId, marketplaceDetails!.OrderId);
        Assert.Equal(marketplaceOrderId.ToString(), marketplaceDetails.SourceOrderId);
        Assert.Equal(75m, marketplaceDetails.Totals.Total);
        Assert.Equal(75m, Assert.Single(marketplaceDetails.Items).UnitPrice);

        var marketplaceSearch = await service.ListOrdersAsync(
            tenantId,
            new AdminOrderListQuery { Page = 1, PageSize = 20, Search = "CH-SUB-O3" },
            "o3-marketplace-search");
        Assert.Equal(subOrderId, Assert.Single(marketplaceSearch.Items).OrderId);

        Assert.Null(await service.GetOrderDetailsAsync(
            foreignTenantId,
            storefrontOrder.Id,
            "o3-direct-foreign-tenant"));
        Assert.Null(await service.GetOrderDetailsAsync(
            foreignTenantId,
            affiliateOrder.Id,
            "o3-affiliate-foreign-tenant"));
        Assert.Null(await service.GetOrderDetailsAsync(
            foreignTenantId,
            subOrderId,
            "o3-marketplace-foreign-tenant"));
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

    private static Order CreateStorefrontOrder(Guid tenantId, string orderNumber, decimal unitPrice)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = orderNumber,
            GuestName = "O-3 Guest",
            GuestEmail = "o3@example.test",
            GuestPhone = "+201000000003",
            IsGuestCheckout = true,
            ShippingAddress = "O-3 Street",
            ShippingCity = "Cairo",
            ShippingCountry = "EG",
            Subtotal = unitPrice,
            Total = unitPrice,
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            Status = OrderStatus.Pending,
            Source = OrderSource.Ecommerce,
            CreatedAt = DateTime.UtcNow
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = Guid.NewGuid(),
            ProductName = $"Snapshot {orderNumber}",
            SKU = $"SKU-{orderNumber}",
            Quantity = 1,
            UnitPrice = unitPrice,
            Total = unitPrice,
            CreatedAt = DateTime.UtcNow
        });
        return order;
    }

    private static void SeedMarketplaceSubOrder(
        ApplicationDbContext context,
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId)
    {
        var customer = CommerceHubCustomer.Create(
            $"o3-{marketplaceOrderId:N}@example.test",
            "O-3 Marketplace Customer",
            "+201000000004",
            "test-hash");
        var marketplaceOrder = new CommerceHubMarketplaceOrder
        {
            Id = marketplaceOrderId,
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = "CH-PARENT-O3",
            FullName = "O-3 Marketplace Customer",
            PhoneNumber = "+201000000004",
            AddressLine = "CommerceHub Street",
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
            OrderNumber = "CH-SUB-O3",
            StoreId = tenantId,
            StoreName = "O-3 Tenant",
            Currency = "EGP",
            Subtotal = 75m,
            Total = 75m,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        subOrder.Items.Add(new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrderId,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "O-3 Marketplace Snapshot",
            Quantity = 1,
            UnitPrice = 75m,
            Total = 75m,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        });

        context.CommerceHubCustomers.Add(customer);
        context.CommerceHubMarketplaceOrders.Add(marketplaceOrder);
        context.CommerceHubSubOrders.Add(subOrder);
    }

    private sealed class StaticTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }
}
