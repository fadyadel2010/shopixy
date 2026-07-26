using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Models.Refunds;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Payments;

public sealed class PaymentLifecycleRefundAuthorityTests
{
    [Fact]
    public async Task RecordRefundAsync_WhenSettledOrderSettlementLineageDrifts_ShouldFailClosed()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var accountingOutbox = new Mock<IAccountingOutboxService>();
        var commerceAccounting = new Mock<ICommerceAccountingService>();
        var service = CreateService(context, accountingOutbox, commerceAccounting);
        var order = await SeedPaidOrderAsync(
            context,
            tenantId,
            orderTotal: 100m,
            paidAmount: 100m,
            paymentStatus: PaymentStatus.Settled);
        var settlementBatch = new PaymentSettlementBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SettlementType = "GATEWAY",
            SettlementId = "settlement-drift-001",
            Provider = "STRIPE",
            GrossAmount = 100m,
            Fees = 2m,
            Currency = "EGP",
            OrderCount = 2,
            SettledAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var settlementOrder = new PaymentSettlementOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SettlementBatchId = settlementBatch.Id,
            OrderId = order.Id,
            OrderTotal = 100m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow
        };

        context.PaymentSettlementBatches.Add(settlementBatch);
        context.PaymentSettlementOrders.Add(settlementOrder);
        await context.SaveChangesAsync();

        var result = await service.RecordRefundAsync(
            order.Id,
            25m,
            "refund-settled-drift-001",
            "Settled refund with drifted lineage");

        Assert.False(result.Success);
        Assert.Contains("order lineage drifted", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.OrderRefunds.Where(item => item.OrderId == order.Id).ToListAsync());

        accountingOutbox.Verify(
            dependency => dependency.EnqueueSettledEcommerceRefundAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<EcommerceRefundAccountingContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        commerceAccounting.Verify(
            dependency => dependency.PostEcommerceRefundAsync(
                It.IsAny<Order>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<EcommerceRefundAccountingContext?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecordRefundAsync_WhenPostSettlementRefundHistoryExistsWithoutCurrentSettlementLineage_ShouldFailClosed()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var accountingOutbox = new Mock<IAccountingOutboxService>();
        var commerceAccounting = new Mock<ICommerceAccountingService>();
        var service = CreateService(context, accountingOutbox, commerceAccounting);
        var order = await SeedPaidOrderAsync(
            context,
            tenantId,
            orderTotal: 100m,
            paidAmount: 100m,
            paymentStatus: PaymentStatus.PartiallyRefunded);
        var settledAtUtc = DateTime.UtcNow.AddMinutes(-5);

        context.OrderRefunds.Add(new OrderRefund
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            RefundId = "refund-post-settlement-history-001",
            Amount = 25m,
            Currency = "EGP",
            IsFullRefund = false,
            Reason = "Prior post-settlement refund",
            PaymentMethod = "CARD",
            IsPostSettlementRefund = true,
            SettlementBatchId = Guid.NewGuid(),
            SettlementType = "GATEWAY",
            SettlementId = "settlement-history-001",
            SettledAtUtc = settledAtUtc,
            FinancialPostingStatus = FinancialPostingStatus.Posted,
            FinancialPostingJournalId = Guid.NewGuid(),
            FinancialPostedAtUtc = settledAtUtc,
            FinancialPostingPendingAtUtc = settledAtUtc,
            RefundedAtUtc = settledAtUtc,
            CreatedAt = settledAtUtc
        });
        await context.SaveChangesAsync();

        var result = await service.RecordRefundAsync(
            order.Id,
            10m,
            "refund-post-settlement-history-002",
            "Second refund after settlement lineage drift");

        Assert.False(result.Success);
        Assert.Contains("settlement snapshot is missing", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await context.OrderRefunds.Where(item => item.OrderId == order.Id).ToListAsync());

        accountingOutbox.Verify(
            dependency => dependency.EnqueueSettledEcommerceRefundAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<EcommerceRefundAccountingContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        accountingOutbox.Verify(
            dependency => dependency.EnqueueEcommerceRefundAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        commerceAccounting.Verify(
            dependency => dependency.PostEcommerceRefundAsync(
                It.IsAny<Order>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<EcommerceRefundAccountingContext?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"PaymentLifecycleRefundAuthority_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static PaymentLifecycleService CreateService(
        ApplicationDbContext context,
        Mock<IAccountingOutboxService> accountingOutbox,
        Mock<ICommerceAccountingService> commerceAccounting)
    {
        accountingOutbox
            .Setup(service => service.EnqueueSettledEcommerceRefundAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<EcommerceRefundAccountingContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        accountingOutbox
            .Setup(service => service.EnqueueEcommerceRefundAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        commerceAccounting
            .Setup(service => service.PostEcommerceRefundAsync(
                It.IsAny<Order>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<EcommerceRefundAccountingContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "REFUND-JOURNAL-TEST"));

        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher
            .Setup(service => service.PublishAsync(
                It.IsAny<OrderRefundedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        eventPublisher
            .Setup(service => service.PublishAsync(
                It.IsAny<OrderPaymentStatusChangedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var marketplaceOutbox = new Mock<IMarketplaceEventOutboxService>();
        marketplaceOutbox
            .Setup(service => service.EnqueueAsync(
                It.IsAny<RefundProcessedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var correlationIdAccessor = new Mock<ICorrelationIdAccessor>();
        correlationIdAccessor
            .Setup(accessor => accessor.GetCorrelationId())
            .Returns("refund-authority-test");

        return new PaymentLifecycleService(
            context,
            accountingOutbox.Object,
            commerceAccounting.Object,
            eventPublisher.Object,
            marketplaceOutbox.Object,
            correlationIdAccessor.Object,
            Mock.Of<IProductVariantStockRepository>(),
            Mock.Of<IInventoryReservationMetricsService>(),
            Mock.Of<ICouponValidationService>(),
            NullLogger<PaymentLifecycleService>.Instance,
            Options.Create(new PaymentLifecycleSettings { RequireRowLevelLocking = false }));
    }

    private static async Task<Order> SeedPaidOrderAsync(
        ApplicationDbContext context,
        Guid tenantId,
        decimal orderTotal,
        decimal paidAmount,
        PaymentStatus paymentStatus)
    {
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Refund Tests",
            CreatedAt = DateTime.UtcNow
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = category.Id,
            Name = "Refundable Item",
            SKU = $"REF-{Guid.NewGuid():N}",
            SalePrice = orderTotal,
            PurchasePrice = 1m,
            CreatedAt = DateTime.UtcNow
        };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..16],
            PaymentMethodCode = "CARD",
            PaymentStatus = paymentStatus,
            FulfillmentStatus = FulfillmentStatus.Delivered,
            FulfillmentMode = FulfillmentMode.Manual,
            Total = orderTotal,
            Currency = "EGP",
            Status = OrderStatus.Confirmed,
            IsPaid = true,
            PaidAt = DateTime.UtcNow,
            FinancialPostingStatus = FinancialPostingStatus.Posted,
            FinancialPostedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Items =
            {
                new OrderItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    ProductName = product.Name,
                    SKU = product.SKU!,
                    Quantity = 1,
                    UnitPrice = orderTotal,
                    Total = orderTotal,
                    PaidAmount = paidAmount,
                    CreatedAt = DateTime.UtcNow
                }
            }
        };
        var paymentIntent = new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            Amount = paidAmount,
            Currency = "EGP",
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Card,
            Provider = PaymentProvider.Stripe,
            Status = PaymentIntentStatus.Paid,
            PaymentReference = $"PAY-{Guid.NewGuid():N}"[..20],
            ProviderIntentId = $"pi_{Guid.NewGuid():N}",
            ProviderPaymentId = $"ch_{Guid.NewGuid():N}",
            CapturedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var collectionJournal = JournalEntry.Create(
            tenantId,
            $"JE-{Guid.NewGuid():N}"[..20],
            DateTime.UtcNow,
            "OnlinePayment",
            order.Id,
            order.OrderNumber,
            "Posted online collection for refund authority test",
            BuildOnlinePaymentIdempotencyKey(tenantId, order.Id, paymentIntent.Id),
            userId: null);

        context.Categories.Add(category);
        context.Products.Add(product);
        context.Orders.Add(order);
        context.PaymentIntents.Add(paymentIntent);
        context.JournalEntries.Add(collectionJournal);
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }
        return order;
    }

    private static string BuildOnlinePaymentIdempotencyKey(Guid tenantId, Guid orderId, Guid paymentIntentId) =>
        $"ONLINE_PAYMENT:{tenantId}:{orderId}:{paymentIntentId}";
}
