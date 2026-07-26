using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Customers;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.TestDoubles;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.Tests.CommerceHub;

public class CustomerRefundServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;

    public CustomerRefundServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateRefundRequestAsync_ShouldCreateRequestedRefund_AndAuditLog()
    {
        var graph = await SeedOrderGraphAsync(
            subOrderStatus: OrderStatus.Delivered,
            itemStatus: CommerceHubOrderItemStatus.Delivered,
            deliveredAt: DateTime.UtcNow.AddDays(-1),
            quantity: 2,
            unitPrice: 150m);

        var service = CreateService();

        var refund = await service.CreateRefundRequestAsync(
            graph.Customer.Id,
                new CustomerCreateRefundRequestDto
                {
                    OrderItemId = graph.Item.Id,
                    Quantity = 1,
                    ReasonCode = RefundReasonCode.DamagedItem,
                    ReasonText = "Damaged item"
                },
                "trace-create",
                "refund-create-1");

        var persistedRefund = await _dbContext.CommerceHubRefundRequests.SingleAsync();
        var auditLogs = await _dbContext.AuditLogs
            .OrderBy(log => log.ActionType)
            .ToListAsync();
        var auditLog = Assert.Single(auditLogs.Where(log => log.ActionType == "REFUND_REQUEST_CREATED"));

        Assert.Equal(persistedRefund.Id, refund.RefundId);
        Assert.Equal(CommerceHubRefundRequestStatus.Requested, persistedRefund.Status);
        Assert.Equal(graph.Order.Id, persistedRefund.OrderId);
        Assert.Equal(graph.Item.Id, persistedRefund.OrderItemId);
        Assert.Equal(graph.SubOrder.StoreId, persistedRefund.VendorId);
        Assert.Equal(150m, persistedRefund.UnitPriceSnapshot);
        Assert.Equal(150m, persistedRefund.TotalAmount);
        Assert.Equal("refund-create-1", persistedRefund.IdempotencyKey);
        Assert.Equal(2, auditLogs.Count);
        Assert.Contains(auditLogs, log => log.ActionType == "PAYOUT_DETAILS_USED_FOR_REFUND");
        Assert.Equal("REFUND_REQUEST_CREATED", auditLog.ActionType);
        Assert.Equal("CommerceHubRefundRequest", auditLog.EntityType);
        Assert.Contains("\"RefundWindowValid\":true", auditLog.AfterDataJson);
        Assert.Contains("\"RemainingQuantity\":1", auditLog.AfterDataJson);
    }

    [Fact]
    public async Task CreateRefundRequestAsync_ShouldReject_WhenItemIsNotDelivered()
    {
        var graph = await SeedOrderGraphAsync(
            subOrderStatus: OrderStatus.Delivered,
            itemStatus: CommerceHubOrderItemStatus.Pending,
            deliveredAt: null);

        var service = CreateService();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.CreateRefundRequestAsync(
                graph.Customer.Id,
                new CustomerCreateRefundRequestDto
                {
                    OrderItemId = graph.Item.Id,
                    Quantity = 1,
                    ReasonCode = RefundReasonCode.Other,
                    ReasonText = "Package issue"
                },
                "trace-not-delivered",
                "refund-not-delivered-1"));

        Assert.Equal("REFUND_NOT_ALLOWED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal("Refund not available: Item not delivered", exception.Message);
    }

    [Fact]
    public async Task CreateRefundRequestAsync_ShouldReject_WhenRefundWindowExpired()
    {
        var graph = await SeedOrderGraphAsync(
            subOrderStatus: OrderStatus.Delivered,
            itemStatus: CommerceHubOrderItemStatus.Delivered,
            deliveredAt: DateTime.UtcNow.AddDays(-10));

        var service = CreateService(new RefundSettings
        {
            RefundWindowDays = 7,
            MaxReasonLength = 1000
        });

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.CreateRefundRequestAsync(
                graph.Customer.Id,
                new CustomerCreateRefundRequestDto
                {
                    OrderItemId = graph.Item.Id,
                    Quantity = 1,
                    ReasonCode = RefundReasonCode.DamagedItem,
                    ReasonText = "Arrived damaged"
                },
                "trace-window-expired",
                "refund-window-expired-1"));

        Assert.Equal("REFUND_NOT_ALLOWED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal("Refund window expired", exception.Message);
    }

    [Fact]
    public async Task CreateRefundRequestAsync_ShouldReject_WhenRequestedQuantityExceedsRemaining()
    {
        var graph = await SeedOrderGraphAsync(
            subOrderStatus: OrderStatus.Delivered,
            itemStatus: CommerceHubOrderItemStatus.Delivered,
            deliveredAt: DateTime.UtcNow.AddDays(-1),
            quantity: 2);

        _dbContext.CommerceHubRefundRequests.Add(new CommerceHubRefundRequest
        {
            Id = Guid.NewGuid(),
            OrderId = graph.Order.Id,
            OrderItemId = graph.Item.Id,
            CustomerId = graph.Customer.Id,
            VendorId = graph.SubOrder.StoreId,
            Quantity = 1,
            UnitPriceSnapshot = graph.Item.UnitPrice,
            TotalAmount = graph.Item.UnitPrice,
            Reason = "First request",
            Status = CommerceHubRefundRequestStatus.Requested,
            IdempotencyKey = "existing-refund-1"
        });
        await _dbContext.SaveChangesAsync();

        var service = CreateService();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.CreateRefundRequestAsync(
                graph.Customer.Id,
                new CustomerCreateRefundRequestDto
                {
                    OrderItemId = graph.Item.Id,
                    Quantity = 2,
                    ReasonCode = RefundReasonCode.Other,
                    ReasonText = "Second request"
                },
                "trace-overage",
                "refund-overage-1"));

        Assert.Equal("REFUND_QUANTITY_EXCEEDED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task CreateRefundRequestAsync_ShouldReturnExistingRefund_ForIdempotentReplay()
    {
        var graph = await SeedOrderGraphAsync(
            subOrderStatus: OrderStatus.Delivered,
            itemStatus: CommerceHubOrderItemStatus.Delivered,
            deliveredAt: DateTime.UtcNow.AddDays(-1));

        var service = CreateService();
        var request = new CustomerCreateRefundRequestDto
        {
            OrderItemId = graph.Item.Id,
            Quantity = 1,
            ReasonCode = RefundReasonCode.NotAsDescribed,
            ReasonText = "Wrong size"
        };

        var first = await service.CreateRefundRequestAsync(graph.Customer.Id, request, "trace-first", "refund-idempotent-1");
        var second = await service.CreateRefundRequestAsync(graph.Customer.Id, request, "trace-second", "refund-idempotent-1");

        Assert.Equal(first.RefundId, second.RefundId);
        Assert.Equal(1, await _dbContext.CommerceHubRefundRequests.CountAsync());
    }

    [Fact]
    public async Task CreateRefundRequestAsync_ShouldReject_DuplicateRequestWithDifferentIdempotencyKey()
    {
        var graph = await SeedOrderGraphAsync(
            subOrderStatus: OrderStatus.Delivered,
            itemStatus: CommerceHubOrderItemStatus.Delivered,
            deliveredAt: DateTime.UtcNow.AddDays(-1));

        var service = CreateService();
        var request = new CustomerCreateRefundRequestDto
        {
            OrderItemId = graph.Item.Id,
            Quantity = 1,
            ReasonCode = RefundReasonCode.NotAsDescribed,
            ReasonText = "Wrong size"
        };

        await service.CreateRefundRequestAsync(graph.Customer.Id, request, "trace-first", "refund-duplicate-1");

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.CreateRefundRequestAsync(graph.Customer.Id, request, "trace-second", "refund-duplicate-2"));

        Assert.Equal("DUPLICATE_REFUND_REQUEST", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal(1, await _dbContext.CommerceHubRefundRequests.CountAsync());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private CustomerRefundService CreateService(RefundSettings? settings = null)
    {
        return new CustomerRefundService(
            _dbContext,
            Options.Create(settings ?? new RefundSettings()),
            CreateRefundEvidenceServiceStub(),
            CreateCustomerPayoutDetailsServiceStub(),
            CreateRefundPayoutSecurityServiceStub(),
            new NoOpUserGovernanceService(),
            new CommerceHubFinancialSafetyGuardService(_dbContext),
            Mock.Of<ILogger<CustomerRefundService>>());
    }

    private static IRefundEvidenceService CreateRefundEvidenceServiceStub()
    {
        var mock = new Mock<IRefundEvidenceService>();
        mock.Setup(service => service.ValidateOwnedMediaAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CommerceHubRefundEvidenceMedia>());
        mock.Setup(service => service.GetEvidenceLookupAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<RefundEvidenceMediaDto>>());
        mock.Setup(service => service.TryParseMediaId(It.IsAny<string?>()))
            .Returns((string? _) => null);
        return mock.Object;
    }

    private static ICustomerPayoutDetailsService CreateCustomerPayoutDetailsServiceStub()
    {
        var mock = new Mock<ICustomerPayoutDetailsService>();
        mock.Setup(service => service.ResolveRefundPayoutAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CustomerRefundPayoutInputDto?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedRefundPayoutDetails
            {
                Method = RefundPayoutMethod.VodafoneCash,
                AccountIdentifier = "01001234567",
                EncryptedAccountIdentifier = "01001234567",
                MaskedAccountIdentifier = "010****567",
                AccountHolderName = "Refund Customer",
                IsVerified = false
            });
        return mock.Object;
    }

    private static IRefundPayoutSecurityService CreateRefundPayoutSecurityServiceStub()
    {
        var mock = new Mock<IRefundPayoutSecurityService>();
        mock.Setup(service => service.Encrypt(It.IsAny<string>()))
            .Returns((string value) => value);
        mock.Setup(service => service.Decrypt(It.IsAny<string?>()))
            .Returns((string? value) => value ?? string.Empty);
        mock.Setup(service => service.IsEncrypted(It.IsAny<string?>()))
            .Returns(false);
        return mock.Object;
    }

    private async Task<CustomerRefundGraph> SeedOrderGraphAsync(
        OrderStatus subOrderStatus,
        CommerceHubOrderItemStatus itemStatus,
        DateTime? deliveredAt,
        int quantity = 2,
        decimal unitPrice = 120m)
    {
        var customer = CommerceHubCustomer.Create("customer@example.com", "Refund Customer", "01000000000", "hash");
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..12],
            FullName = "Refund Customer",
            PhoneNumber = "01000000000",
            AddressLine = "123 Refund Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "CARD",
            PaymentMethodName = "Card",
            Subtotal = unitPrice * quantity,
            ShippingTotal = 20m,
            TotalAmount = (unitPrice * quantity) + 20m,
            Status = subOrderStatus,
            PaymentStatus = PaymentStatus.Settled
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"SUB-{Guid.NewGuid():N}"[..12],
            StoreId = Guid.NewGuid(),
            StoreName = "Vendor A",
            Currency = "EGP",
            Subtotal = unitPrice * quantity,
            Shipping = 20m,
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = "CARD",
            PaymentMethodName = "Card",
            Total = (unitPrice * quantity) + 20m,
            Status = subOrderStatus,
            PaymentStatus = PaymentStatus.Settled
        };

        var item = new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Refundable Product",
            VariantName = "Blue / Large",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Total = quantity * unitPrice,
            Status = itemStatus,
            DeliveredAt = deliveredAt
        };

        order.SubOrders.Add(subOrder);
        subOrder.Items.Add(item);

        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        await SaveCommerceHubFixtureAsync();

        return new CustomerRefundGraph(customer, order, subOrder, item);
    }

    private async Task SaveCommerceHubFixtureAsync()
    {
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private sealed record CustomerRefundGraph(
        CommerceHubCustomer Customer,
        CommerceHubMarketplaceOrder Order,
        CommerceHubSubOrder SubOrder,
        CommerceHubSubOrderItem Item);
}
