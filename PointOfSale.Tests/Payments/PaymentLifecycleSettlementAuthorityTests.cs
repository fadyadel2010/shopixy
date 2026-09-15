using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Payments;

public sealed class PaymentLifecycleSettlementAuthorityTests
{
    [Fact]
    public async Task RecordGatewaySettlementAsync_WhenFeesExceedGross_ShouldFailClosedBeforeMutation()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var commerceAccounting = new Mock<ICommerceAccountingService>();
        var eventPublisher = new Mock<IEventPublisher>();
        var service = CreateService(context, commerceAccounting, eventPublisher);
        var order = await SeedCollectedGatewayOrderAsync(context, tenantId, total: 100m, gateway: "stripe");

        var result = await service.RecordGatewaySettlementAsync(new GatewaySettlementRequest
        {
            TenantId = tenantId,
            SettlementId = "gateway-invalid-fees-001",
            Gateway = "stripe",
            GrossAmount = 100m,
            Fees = 101m,
            Currency = "EGP",
            OrderIds = new List<Guid> { order.Id }
        });

        Assert.False(result.Success);
        Assert.Contains("fees must be between zero and the gross amount", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.PaymentSettlementBatches.ToListAsync());
        Assert.Empty(await context.PaymentSettlementOrders.ToListAsync());
        Assert.Equal(PaymentStatus.Collected, (await context.Orders.SingleAsync(item => item.Id == order.Id)).PaymentStatus);

        commerceAccounting.Verify(
            dependency => dependency.PostGatewaySettlementAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        eventPublisher.Verify(
            dependency => dependency.PublishAsync(
                It.IsAny<OrderPaymentStatusChangedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecordOfflineSettlementAsync_WhenGrossAmountIsNotPositive_ShouldFailClosedBeforeMutation()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var commerceAccounting = new Mock<ICommerceAccountingService>();
        var eventPublisher = new Mock<IEventPublisher>();
        var service = CreateService(context, commerceAccounting, eventPublisher);
        var order = await SeedCollectedCodOrderAsync(context, tenantId, total: 80m);

        var result = await service.RecordOfflineSettlementAsync(new OfflineSettlementRequest
        {
            TenantId = tenantId,
            SettlementId = "cod-invalid-gross-001",
            GrossAmount = 0m,
            Fees = 0m,
            Currency = "EGP",
            ShippingCompany = "carrier",
            OrderIds = new List<Guid> { order.Id }
        });

        Assert.False(result.Success);
        Assert.Contains("gross amount must be positive", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.PaymentSettlementBatches.ToListAsync());
        Assert.Empty(await context.PaymentSettlementOrders.ToListAsync());
        Assert.Equal(PaymentStatus.Collected, (await context.Orders.SingleAsync(item => item.Id == order.Id)).PaymentStatus);

        commerceAccounting.Verify(
            dependency => dependency.PostCodSettlementAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        eventPublisher.Verify(
            dependency => dependency.PublishAsync(
                It.IsAny<OrderPaymentStatusChangedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecordGatewaySettlementAsync_WhenProviderHasNoClearingAuthority_ShouldFailBeforeMutation()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var commerceAccounting = new Mock<ICommerceAccountingService>();
        var eventPublisher = new Mock<IEventPublisher>();
        var service = CreateService(context, commerceAccounting, eventPublisher);
        var order = await SeedCollectedGatewayOrderAsync(context, tenantId, total: 100m, gateway: "wallet");

        var result = await service.RecordGatewaySettlementAsync(new GatewaySettlementRequest
        {
            TenantId = tenantId,
            SettlementId = "unsupported-wallet-settlement",
            Gateway = "wallet",
            GrossAmount = 100m,
            Fees = 1m,
            Currency = "EGP",
            OrderIds = new List<Guid> { order.Id }
        });

        Assert.False(result.Success);
        Assert.Contains("not supported", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.PaymentSettlementBatches.ToListAsync());
        Assert.Empty(await context.PaymentSettlementOrders.ToListAsync());
        Assert.Equal(PaymentStatus.Collected, (await context.Orders.SingleAsync(item => item.Id == order.Id)).PaymentStatus);
        commerceAccounting.Verify(
            dependency => dependency.PostGatewaySettlementAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecordGatewaySettlementAsync_ReplayIsIdempotentAndChangedEvidenceIsRejected()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var commerceAccounting = new Mock<ICommerceAccountingService>();
        var eventPublisher = new Mock<IEventPublisher>();
        var accountingOutbox = new Mock<IAccountingOutboxService>();
        accountingOutbox
            .Setup(service => service.EnqueueGatewaySettlementAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = CreateService(context, commerceAccounting, eventPublisher, accountingOutbox.Object);
        var order = await SeedCollectedGatewayOrderAsync(context, tenantId, total: 100m, gateway: "stripe");
        var request = new GatewaySettlementRequest
        {
            TenantId = tenantId,
            SettlementId = "stripe-settlement-replay-001",
            Gateway = "stripe",
            GrossAmount = 100m,
            Fees = 3m,
            Currency = "EGP",
            OrderIds = new List<Guid> { order.Id },
            BankReference = "bank-reference-001"
        };

        var first = await service.RecordGatewaySettlementAsync(request);
        var replay = await service.RecordGatewaySettlementAsync(request);
        var changedEvidence = await service.RecordGatewaySettlementAsync(new GatewaySettlementRequest
        {
            TenantId = request.TenantId,
            SettlementId = request.SettlementId,
            Gateway = request.Gateway,
            GrossAmount = request.GrossAmount,
            Fees = 4m,
            Currency = request.Currency,
            OrderIds = request.OrderIds,
            BankReference = request.BankReference
        });

        Assert.True(first.Success, first.ErrorMessage);
        Assert.True(replay.Success, replay.ErrorMessage);
        Assert.True(replay.WasIdempotent);
        Assert.False(replay.StateChanged);
        Assert.False(changedEvidence.Success);
        Assert.Contains("different contents", changedEvidence.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await context.PaymentSettlementBatches.ToListAsync());
        Assert.Single(await context.PaymentSettlementOrders.ToListAsync());
        accountingOutbox.Verify(
            dependency => dependency.EnqueueGatewaySettlementAsync(
                tenantId,
                It.IsAny<Guid>(),
                "stripe",
                100m,
                3m,
                request.SettlementId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"PaymentLifecycleSettlementAuthority_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static PaymentLifecycleService CreateService(
        ApplicationDbContext context,
        Mock<ICommerceAccountingService> commerceAccounting,
        Mock<IEventPublisher> eventPublisher,
        IAccountingOutboxService? accountingOutbox = null)
    {
        commerceAccounting
            .Setup(service => service.PostGatewaySettlementAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "GATEWAY-SETTLEMENT-JOURNAL-TEST"));
        commerceAccounting
            .Setup(service => service.PostCodSettlementAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "COD-SETTLEMENT-JOURNAL-TEST"));

        eventPublisher
            .Setup(service => service.PublishAsync(
                It.IsAny<OrderPaymentStatusChangedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var correlationIdAccessor = new Mock<ICorrelationIdAccessor>();
        correlationIdAccessor
            .Setup(accessor => accessor.GetCorrelationId())
            .Returns("settlement-authority-test");

        accountingOutbox ??= new AccountingOutboxService(
            context,
            correlationIdAccessor.Object,
            NullLogger<AccountingOutboxService>.Instance);

        return new PaymentLifecycleService(
            context,
            accountingOutbox,
            commerceAccounting.Object,
            eventPublisher.Object,
            Mock.Of<IMarketplaceEventOutboxService>(),
            correlationIdAccessor.Object,
            Mock.Of<IProductVariantStockRepository>(),
            Mock.Of<IInventoryReservationMetricsService>(),
            Mock.Of<ICouponValidationService>(),
            NullLogger<PaymentLifecycleService>.Instance,
            Options.Create(new PaymentLifecycleSettings { RequireRowLevelLocking = false }));
    }

    private static async Task<Order> SeedCollectedGatewayOrderAsync(
        ApplicationDbContext context,
        Guid tenantId,
        decimal total,
        string gateway)
    {
        var order = CreateCollectedOrder(tenantId, total, paymentMethodCode: "CARD", paymentGateway: gateway);
        var paymentIntent = new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            Amount = total,
            Currency = order.Currency,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Card,
            Provider = PaymentProvider.Stripe,
            Status = PaymentIntentStatus.Paid,
            PaymentReference = $"PAY-{Guid.NewGuid():N}"[..20],
            ProviderIntentId = $"pi_{Guid.NewGuid():N}",
            ProviderPaymentId = $"ch_{Guid.NewGuid():N}",
            CapturedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        context.Orders.Add(order);
        context.PaymentIntents.Add(paymentIntent);
        context.JournalEntries.Add(CreatePostedJournal(
            tenantId,
            order.Id,
            order.OrderNumber,
            $"ONLINE_PAYMENT:{tenantId}:{order.Id}:{paymentIntent.Id}",
            "Posted online collection for settlement authority test"));
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }
        return order;
    }

    private static async Task<Order> SeedCollectedCodOrderAsync(
        ApplicationDbContext context,
        Guid tenantId,
        decimal total)
    {
        var order = CreateCollectedOrder(tenantId, total, paymentMethodCode: "COD", paymentGateway: null);

        context.Orders.Add(order);
        context.JournalEntries.Add(CreatePostedJournal(
            tenantId,
            order.Id,
            order.OrderNumber,
            $"COD_DELIVERED:{tenantId}:{order.Id}",
            "Posted COD collection for settlement authority test"));
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }
        return order;
    }

    private static Order CreateCollectedOrder(
        Guid tenantId,
        decimal total,
        string paymentMethodCode,
        string? paymentGateway)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..16],
            PaymentMethodCode = paymentMethodCode,
            PaymentGateway = paymentGateway,
            PaymentStatus = PaymentStatus.Collected,
            FulfillmentStatus = FulfillmentStatus.Delivered,
            FulfillmentMode = FulfillmentMode.Manual,
            Total = total,
            Currency = "EGP",
            Status = OrderStatus.Completed,
            IsPaid = true,
            PaidAt = DateTime.UtcNow,
            FinancialPostingStatus = FinancialPostingStatus.Posted,
            FinancialPostingJournalId = Guid.NewGuid(),
            FinancialPostedAtUtc = DateTime.UtcNow,
            ShippingAddress = "Settlement authority street",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static JournalEntry CreatePostedJournal(
        Guid tenantId,
        Guid orderId,
        string orderNumber,
        string idempotencyKey,
        string description)
    {
        return JournalEntry.Create(
            tenantId,
            $"JE-{Guid.NewGuid():N}"[..20],
            DateTime.UtcNow,
            "SettlementAuthorityTest",
            orderId,
            orderNumber,
            description,
            idempotencyKey,
            userId: null);
    }
}
