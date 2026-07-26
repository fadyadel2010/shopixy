using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.RegularExpressions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;
using Xunit;

namespace PointOfSale.Tests.Integration;

public sealed class EcommerceRefundLifecycleTests
{
    [Fact]
    public async Task RecordRefundAsync_WhenAmountExceedsRemainingRefundable_ShouldRejectWithoutFinancialEffects()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var context = await CreateContextAsync(connection, tenantId);
        var service = CreateService(context);
        var order = await SeedPaidOrderAsync(context, tenantId, orderTotal: 100m, paidAmount: 100m);

        var result = await service.RecordRefundAsync(
            order.Id,
            150m,
            "refund-too-large",
            "Over refund attempt");

        Assert.False(result.Success);
        Assert.Contains("exceeds remaining refundable amount", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var persistedOrder = await context.Orders
            .Include(item => item.Items)
            .Include(item => item.Refunds)
            .SingleAsync(item => item.Id == order.Id);

        Assert.Equal(PaymentStatus.Collected, persistedOrder.PaymentStatus);
        Assert.Empty(persistedOrder.Refunds);
        Assert.Equal(0m, persistedOrder.Items.Single().RefundedAmount);
    }

    [Fact]
    public async Task RecordRefundAsync_WhenRefundIdIsReplayed_ShouldBeIdempotentAndNotRefundTwice()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var context = await CreateContextAsync(connection, tenantId);
        var accountingOutbox = new Mock<IAccountingOutboxService>();
        accountingOutbox
            .Setup(service => service.EnqueueEcommerceRefundAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var commerceAccounting = new Mock<ICommerceAccountingService>();
        commerceAccounting
            .Setup(service => service.PostEcommerceRefundAsync(
                It.IsAny<Order>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<EcommerceRefundAccountingContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "REFUND-JOURNAL-TEST"));
        var service = CreateService(context, accountingOutbox, commerceAccounting);
        var order = await SeedPaidOrderAsync(context, tenantId, orderTotal: 100m, paidAmount: 100m);

        var first = await service.RecordRefundAsync(order.Id, 40m, "refund-001", "Customer refund");
        var replay = await service.RecordRefundAsync(order.Id, 40m, "refund-001", "Customer refund retry");

        Assert.True(first.Success, first.ErrorMessage);
        Assert.Equal(PaymentStatus.PartiallyRefunded, first.NewStatus);
        Assert.True(replay.Success);
        Assert.True(replay.WasIdempotent);
        Assert.False(replay.StateChanged);

        var persistedOrder = await context.Orders
            .Include(item => item.Items)
            .Include(item => item.Refunds)
                .ThenInclude(refund => refund.Lines)
            .SingleAsync(item => item.Id == order.Id);

        Assert.Single(persistedOrder.Refunds);
        var refund = persistedOrder.Refunds.Single();
        Assert.Equal(40m, refund.Amount);
        Assert.Equal(40m, persistedOrder.Items.Single().RefundedAmount);

        // Verify OrderRefundLine records were created
        Assert.NotEmpty(refund.Lines);
        Assert.Single(refund.Lines);
        var refundLine = refund.Lines.Single();
        Assert.Equal(refund.Id, refundLine.OrderRefundId);
        Assert.Equal(persistedOrder.Items.Single().Id, refundLine.OrderItemId);
        Assert.Equal(40m, refundLine.Amount);

        accountingOutbox.Verify(
            service => service.EnqueueEcommerceRefundAsync(
                tenantId,
                order.Id,
                40m,
                "refund-001",
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify commerce accounting side effect
        commerceAccounting.Verify(
            service => service.PostEcommerceRefundAsync(
                It.Is<Order>(o => o.Id == order.Id),
                40m,
                "refund-001",
                It.IsAny<EcommerceRefundAccountingContext?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordRefundAsync_WhenRefundReplayIsCollapsed_ShouldFanOutCurrentPaymentVisibilityOnce()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var context = await CreateContextAsync(connection, tenantId);
        var eventPublisher = new Mock<IEventPublisher>();
        var service = CreateService(context, eventPublisher: eventPublisher);
        var order = await SeedPaidOrderAsync(context, tenantId, orderTotal: 100m, paidAmount: 100m);

        var first = await service.RecordRefundAsync(order.Id, 40m, "refund-visibility-001", "Customer refund");
        var replay = await service.RecordRefundAsync(order.Id, 40m, "refund-visibility-001", "Customer refund replay");

        Assert.True(first.Success, first.ErrorMessage);
        Assert.True(replay.WasIdempotent);
        eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.Is<OrderPaymentStatusChangedEvent>(paymentChanged =>
                    paymentChanged.OrderId == order.Id &&
                    paymentChanged.OldStatus == PaymentStatus.Collected &&
                    paymentChanged.NewStatus == PaymentStatus.PartiallyRefunded &&
                    paymentChanged.PaymentReference == "refund-visibility-001" &&
                    paymentChanged.Description == "Payment partially refunded" &&
                    paymentChanged.IdempotencyKey.Contains(":RefundAuthority:refund-visibility-001", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordGatewaySettlementAsync_WhenSettlementReplayIsCollapsed_ShouldFanOutSettledPaymentVisibilityOnce()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var context = await CreateContextAsync(connection, tenantId);
        var eventPublisher = new Mock<IEventPublisher>();
        var service = CreateService(context, eventPublisher: eventPublisher);
        var order = await SeedPaidOrderAsync(context, tenantId, orderTotal: 100m, paidAmount: 100m);
        var request = new GatewaySettlementRequest
        {
            TenantId = tenantId,
            SettlementId = "settlement-visibility-001",
            Gateway = "CARD",
            OrderIds = [order.Id],
            GrossAmount = 100m,
            Fees = 3m,
            Currency = "EGP",
            BankReference = "bank-visibility-001"
        };

        var first = await service.RecordGatewaySettlementAsync(request);
        var replay = await service.RecordGatewaySettlementAsync(request);

        Assert.True(first.Success, first.ErrorMessage);
        Assert.Equal(PaymentStatus.Settled, first.NewStatus);
        Assert.True(replay.WasIdempotent);
        Assert.False(replay.StateChanged);
        Assert.Equal(PaymentStatus.Settled, (await context.Orders.SingleAsync(candidate => candidate.Id == order.Id)).PaymentStatus);
        eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.Is<OrderPaymentStatusChangedEvent>(paymentChanged =>
                    paymentChanged.OrderId == order.Id &&
                    paymentChanged.OldStatus == PaymentStatus.Collected &&
                    paymentChanged.NewStatus == PaymentStatus.Settled &&
                    paymentChanged.AmountPaid == 100m &&
                    paymentChanged.PaymentReference == "settlement-visibility-001" &&
                    paymentChanged.Description == "Payment settlement accepted" &&
                    paymentChanged.IdempotencyKey.Contains(":SettlementAuthority:settlement-visibility-001", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordRefundAsync_WhenCumulativeRefundReachesTotal_ShouldMarkOrderRefunded()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var context = await CreateContextAsync(connection, tenantId);
        var service = CreateService(context);
        var order = await SeedPaidOrderAsync(context, tenantId, orderTotal: 100m, paidAmount: 100m);

        var first = await service.RecordRefundAsync(order.Id, 40m, "refund-001", "Partial refund");
        var second = await service.RecordRefundAsync(order.Id, 60m, "refund-002", "Final refund");

        Assert.True(first.Success, first.ErrorMessage);
        Assert.True(second.Success, second.ErrorMessage);
        Assert.Equal(PaymentStatus.Refunded, second.NewStatus);

        var persistedOrder = await context.Orders
            .Include(item => item.Items)
            .Include(item => item.Refunds)
                .ThenInclude(refund => refund.Lines)
            .SingleAsync(item => item.Id == order.Id);

        Assert.Equal(PaymentStatus.Refunded, persistedOrder.PaymentStatus);
        Assert.Equal(2, persistedOrder.Refunds.Count);
        Assert.Equal(100m, persistedOrder.Refunds.Sum(refund => refund.Amount));
        Assert.Equal(100m, persistedOrder.Items.Single().RefundedAmount);

        // Verify OrderRefundLine records were created for both refunds
        var firstRefund = persistedOrder.Refunds.First();
        var secondRefund = persistedOrder.Refunds.Last();
        
        Assert.NotEmpty(firstRefund.Lines);
        Assert.Single(firstRefund.Lines);
        var firstRefundLine = firstRefund.Lines.Single();
        Assert.Equal(firstRefund.Id, firstRefundLine.OrderRefundId);
        Assert.Equal(persistedOrder.Items.Single().Id, firstRefundLine.OrderItemId);
        Assert.Equal(40m, firstRefundLine.Amount);

        Assert.NotEmpty(secondRefund.Lines);
        Assert.Single(secondRefund.Lines);
        var secondRefundLine = secondRefund.Lines.Single();
        Assert.Equal(secondRefund.Id, secondRefundLine.OrderRefundId);
        Assert.Equal(persistedOrder.Items.Single().Id, secondRefundLine.OrderItemId);
        Assert.Equal(60m, secondRefundLine.Amount);

        // Verify total refund lines match total refund amount
        var totalRefundLineAmount = persistedOrder.Refunds
            .SelectMany(refund => refund.Lines)
            .Sum(line => line.Amount);
        Assert.Equal(100m, totalRefundLineAmount);
    }

    [Fact]
    public async Task RecordRefundAsync_WhenSettledOrderHasNoSettlementSnapshot_ShouldRejectWithoutFinancialEffects()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var context = await CreateContextAsync(connection, tenantId);
        var accountingOutbox = new Mock<IAccountingOutboxService>();
        var service = CreateService(context, accountingOutbox);
        var order = await SeedPaidOrderAsync(
            context,
            tenantId,
            orderTotal: 100m,
            paidAmount: 100m,
            paymentStatus: PaymentStatus.Settled);

        var result = await service.RecordRefundAsync(
            order.Id,
            25m,
            "refund-settled-missing-snapshot",
            "Settled refund without settlement record");

        Assert.False(result.Success);
        Assert.Contains("settlement snapshot", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var persistedOrder = await context.Orders
            .Include(item => item.Items)
            .Include(item => item.Refunds)
            .SingleAsync(item => item.Id == order.Id);

        Assert.Equal(PaymentStatus.Settled, persistedOrder.PaymentStatus);
        Assert.Empty(persistedOrder.Refunds);
        Assert.Equal(0m, persistedOrder.Items.Single().RefundedAmount);
        accountingOutbox.Verify(
            service => service.EnqueueEcommerceRefundAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        accountingOutbox.Verify(
            service => service.EnqueueSettledEcommerceRefundAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<EcommerceRefundAccountingContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecordRefundAsync_WhenSettledOrderHasSettlementSnapshot_ShouldStampSnapshotAndUseSettledOutbox()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var context = await CreateContextAsync(connection, tenantId);
        var accountingOutbox = new Mock<IAccountingOutboxService>();
        var commerceAccounting = new Mock<ICommerceAccountingService>();
        commerceAccounting
            .Setup(service => service.PostEcommerceRefundAsync(
                It.IsAny<Order>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<EcommerceRefundAccountingContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "REFUND-JOURNAL-TEST"));
        var service = CreateService(context, accountingOutbox, commerceAccounting);
        var order = await SeedPaidOrderAsync(
            context,
            tenantId,
            orderTotal: 100m,
            paidAmount: 100m,
            paymentStatus: PaymentStatus.Settled);
        var settledAtUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var settlementBatch = new PaymentSettlementBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SettlementType = "GATEWAY",
            SettlementId = "settlement-001",
            Provider = "STRIPE",
            PayoutBatchId = "payout-001",
            GrossAmount = 100m,
            Fees = 2m,
            Currency = "EGP",
            BankReference = "bank-ref-001",
            OrderCount = 1,
            SettledAtUtc = settledAtUtc,
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
            "refund-settled-001",
            "Settled refund");

        Assert.True(result.Success, result.ErrorMessage);

        var refund = await context.OrderRefunds.SingleAsync(item => item.OrderId == order.Id);
        Assert.True(refund.IsPostSettlementRefund);
        Assert.Equal(settlementBatch.Id, refund.SettlementBatchId);
        Assert.Equal("GATEWAY", refund.SettlementType);
        Assert.Equal("settlement-001", refund.SettlementId);
        Assert.Equal("payout-001", refund.PayoutBatchId);
        Assert.Equal(settledAtUtc, refund.SettledAtUtc);

        accountingOutbox.Verify(
            service => service.EnqueueSettledEcommerceRefundAsync(
                tenantId,
                order.Id,
                25m,
                "refund-settled-001",
                It.Is<EcommerceRefundAccountingContext>(context =>
                    context.IsPostSettlementRefund &&
                    context.SettlementBatchId == settlementBatch.Id &&
                    context.SettlementType == "GATEWAY" &&
                    context.SettlementId == "settlement-001" &&
                    context.PayoutBatchId == "payout-001" &&
                    context.SettledAtUtc == settledAtUtc),
                It.IsAny<CancellationToken>()),
            Times.Once);
        accountingOutbox.Verify(
            service => service.EnqueueEcommerceRefundAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify commerce accounting side effect
        commerceAccounting.Verify(
            service => service.PostEcommerceRefundAsync(
                It.Is<Order>(o => o.Id == order.Id),
                25m,
                "refund-settled-001",
                It.Is<EcommerceRefundAccountingContext?>(ctx =>
                    ctx != null &&
                    ctx.IsPostSettlementRefund &&
                    ctx.SettlementBatchId == settlementBatch.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);
        return connection;
    }

    private static async Task<ApplicationDbContext> CreateContextAsync(SqliteConnection connection, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .EnableSensitiveDataLogging()
            .Options;
        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static PaymentLifecycleService CreateService(
        ApplicationDbContext context,
        Mock<IAccountingOutboxService>? accountingOutbox = null,
        Mock<ICommerceAccountingService>? commerceAccounting = null,
        Mock<IEventPublisher>? eventPublisher = null)
    {
        accountingOutbox ??= new Mock<IAccountingOutboxService>();
        accountingOutbox
            .Setup(service => service.EnqueueEcommerceRefundAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
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
            .Setup(service => service.EnqueueGatewaySettlementAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        eventPublisher ??= new Mock<IEventPublisher>();
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
        eventPublisher
            .Setup(service => service.PublishAsync(
                It.IsAny<GatewaySettlementCompletedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var createdCommerceAccountingMock = commerceAccounting == null;
        commerceAccounting ??= new Mock<ICommerceAccountingService>();
        
        // Only configure default behavior if we created the mock ourselves
        if (createdCommerceAccountingMock)
        {
            commerceAccounting
                .Setup(service => service.PostEcommerceRefundAsync(
                    It.IsAny<Order>(),
                    It.IsAny<decimal>(),
                    It.IsAny<string>(),
                    It.IsAny<EcommerceRefundAccountingContext?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "REFUND-JOURNAL-TEST"));
        }

        var marketplaceOutbox = new Mock<IMarketplaceEventOutboxService>();
        marketplaceOutbox
            .Setup(service => service.EnqueueAsync(
                It.IsAny<RefundProcessedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var correlationIdAccessor = new Mock<ICorrelationIdAccessor>();
        correlationIdAccessor
            .Setup(accessor => accessor.GetCorrelationId())
            .Returns("refund-lifecycle-test");

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
        PaymentStatus paymentStatus = PaymentStatus.Collected)
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
            "Posted online collection for refund lifecycle test",
            BuildOnlinePaymentIdempotencyKey(tenantId, order.Id, paymentIntent.Id),
            userId: null);

        context.Categories.Add(category);
        context.Products.Add(product);
        context.Orders.Add(order);
        context.PaymentIntents.Add(paymentIntent);
        context.JournalEntries.Add(collectionJournal);
        await context.SaveChangesAsync();
        return order;
    }

    private static string BuildOnlinePaymentIdempotencyKey(Guid tenantId, Guid orderId, Guid paymentIntentId) =>
        $"ONLINE_PAYMENT:{tenantId}:{orderId}:{paymentIntentId}";

    private static void RegisterSqliteFunctions(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?, string?, string?, string?>(
            "REGEXP_REPLACE",
            (input, pattern, replacement, options) =>
            {
                if (input == null)
                {
                    return null;
                }

                var regexOptions = RegexOptions.None;
                if (!string.IsNullOrWhiteSpace(options) &&
                    options.Contains('i', StringComparison.OrdinalIgnoreCase))
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                return Regex.Replace(
                    input,
                    pattern ?? string.Empty,
                    replacement ?? string.Empty,
                    regexOptions);
            });
    }
}
