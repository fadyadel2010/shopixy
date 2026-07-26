using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;
using Xunit;

namespace PointOfSale.Tests.Integration;

/// <summary>
/// PHASE: COD Payment Lifecycle Integration Tests
/// 
/// These tests verify the HARD INVARIANTS for COD payment collection:
/// 1. COD can ONLY be collected after FulfillmentStatus == Delivered
/// 2. Non-offline-collection orders cannot be collected via MarkOfflineCollectionDeliveredAsync
/// 3. PaymentStatus transitions are atomic with ledger journal creation
/// 4. Idempotency: Duplicate collection attempts are safe
/// 
/// ACCEPTANCE CRITERIA:
/// ✅ COD-1: COD order + not delivered → attempt collect → blocked
/// ✅ COD-2: COD order + delivered → payment auto-collected
/// ✅ COD-3: Non-COD order → attempt collect → blocked
/// ✅ COD-4: Already collected → attempt collect → idempotent success
/// ✅ COD-5: Ledger journal created only after successful collection
/// </summary>
public class CodPaymentLifecycleTests : IDisposable
{
    private readonly TestDatabaseScope _databaseScope;
    private readonly ApplicationDbContext _dbContext;
    private readonly PaymentLifecycleService _lifecycleService;
    private readonly Mock<IAccountingOutboxService> _mockOutbox;
    private readonly Mock<ICommerceAccountingService> _mockCommerceAccounting;
    private readonly Mock<IEventPublisher> _mockEventPublisher;
    private readonly Mock<IMarketplaceEventOutboxService> _mockMarketplaceEventOutbox;
    private readonly Mock<ICorrelationIdAccessor> _mockCorrelationIdAccessor;
    private readonly Mock<IInventoryReservationMetricsService> _mockInventoryReservationMetrics;
    private readonly Guid _tenantId = Guid.NewGuid();

    public CodPaymentLifecycleTests()
    {
        _databaseScope = TestDatabaseScope.Open();
        _dbContext = _databaseScope.CreateDbContext(_tenantId);

        // Setup mocks
        _mockOutbox = new Mock<IAccountingOutboxService>();
        _mockCommerceAccounting = new Mock<ICommerceAccountingService>();
        _mockEventPublisher = new Mock<IEventPublisher>();
        _mockMarketplaceEventOutbox = new Mock<IMarketplaceEventOutboxService>();
        _mockCorrelationIdAccessor = new Mock<ICorrelationIdAccessor>();
        _mockInventoryReservationMetrics = new Mock<IInventoryReservationMetricsService>();
        var mockLogger = new Mock<ILogger<PaymentLifecycleService>>();

        // Setup outbox to return success
        _mockOutbox
            .Setup(x => x.EnqueueCodDeliveredAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OutboxEnqueueResult.Created(Guid.NewGuid(), "cod-test"));
        _mockCommerceAccounting
            .Setup(service => service.PostCodDeliveredAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "COD-JOURNAL-TEST"));
        _mockCorrelationIdAccessor
            .Setup(accessor => accessor.GetCorrelationId())
            .Returns("cod-test-correlation");

        _dbContext.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "COD Payment Lifecycle Tenant",
            Slug = $"cod-payment-lifecycle-{_tenantId:N}",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        _lifecycleService = new PaymentLifecycleService(
            _dbContext,
            _mockOutbox.Object,
            _mockCommerceAccounting.Object,
            _mockEventPublisher.Object,
            _mockMarketplaceEventOutbox.Object,
            _mockCorrelationIdAccessor.Object,
            Mock.Of<IProductVariantStockRepository>(),
            _mockInventoryReservationMetrics.Object,
            Mock.Of<ICouponValidationService>(),
            mockLogger.Object,
            Options.Create(new PaymentLifecycleSettings()));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COD-1: COD order + not delivered → attempt collect → blocked
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkCodDelivered_WhenNotDelivered_ShouldBlock()
    {
        // Arrange: COD order in OutForDelivery status
        var order = CreateCodOrder(FulfillmentStatus.OutForDelivery, PaymentStatus.Pending);
        await _dbContext.Orders.AddAsync(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(order.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not delivered", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        
        // Verify order unchanged
        var orderAfter = await _dbContext.Orders.FindAsync(order.Id);
        Assert.Equal(PaymentStatus.Pending, orderAfter!.PaymentStatus);
        Assert.Equal(FulfillmentStatus.OutForDelivery, orderAfter.FulfillmentStatus);
        
        // Verify no ledger journal created
        _mockOutbox.Verify(
            x => x.EnqueueCodDeliveredAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(FulfillmentStatus.Created)]
    [InlineData(FulfillmentStatus.Confirmed)]
    [InlineData(FulfillmentStatus.Packed)]
    [InlineData(FulfillmentStatus.OutForDelivery)]
    [InlineData(FulfillmentStatus.FailedDelivery)]
    public async Task MarkCodDelivered_WhenNotInDeliveredStatus_ShouldBlock(FulfillmentStatus status)
    {
        // Arrange
        var order = CreateCodOrder(status, PaymentStatus.Pending);
        await _dbContext.Orders.AddAsync(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(order.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not delivered", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COD-2: COD order + delivered → payment auto-collected
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkCodDelivered_WhenDelivered_ShouldCollect()
    {
        // Arrange: COD order in Delivered status
        var order = CreateCodOrder(FulfillmentStatus.Delivered, PaymentStatus.Pending);
        await _dbContext.Orders.AddAsync(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(order.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(PaymentStatus.Collected, result.NewStatus);
        
        // Verify order updated
        var orderAfter = await _dbContext.Orders.FindAsync(order.Id);
        Assert.Equal(PaymentStatus.Collected, orderAfter!.PaymentStatus);
        Assert.True(orderAfter.IsPaid);
        Assert.NotNull(orderAfter.PaidAt);
        
        // Verify commerce accounting side effect
        _mockCommerceAccounting.Verify(
            service => service.PostCodDeliveredAsync(
                It.Is<Order>(o => o.Id == order.Id && o.TenantId == _tenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        
        // Verify ledger journal enqueued
        _mockOutbox.Verify(
            x => x.EnqueueCodDeliveredAsync(_tenantId, order.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        
        // Verify domain event published
        _mockEventPublisher.Verify(
            x => x.PublishAsync(It.IsAny<PointOfSale.Core.Events.OfflinePaymentCollectedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COD-3: Non-COD order → attempt collect → blocked
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("STRIPE")]
    [InlineData("PAYMOB")]
    [InlineData("WALLET")]
    [InlineData("MANUALWALLET")]
    public async Task MarkCodDelivered_WhenNotCodOrder_ShouldBlock(string paymentMethod)
    {
        // Arrange: Non-COD order
        var order = CreateOrder(paymentMethod, FulfillmentStatus.Delivered, PaymentStatus.Pending);
        await _dbContext.Orders.AddAsync(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(order.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not a COD order", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        
        // Verify order unchanged
        var orderAfter = await _dbContext.Orders.FindAsync(order.Id);
        Assert.Equal(PaymentStatus.Pending, orderAfter!.PaymentStatus);
        
        // Verify no ledger journal created
        _mockOutbox.Verify(
            x => x.EnqueueCodDeliveredAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COD-4: Already collected → attempt collect → idempotent success
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkCodDelivered_WhenAlreadyCollected_ShouldBeIdempotent()
    {
        // Arrange: COD order already collected
        var order = CreateCodOrder(FulfillmentStatus.Delivered, PaymentStatus.Collected);
        order.IsPaid = true;
        order.PaidAt = DateTime.UtcNow.AddHours(-1);
        await _dbContext.Orders.AddAsync(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(order.Id);

        // Assert
        Assert.True(result.Success); // Idempotent success
        Assert.Equal(PaymentStatus.Collected, result.NewStatus);
        
        // Verify no duplicate ledger journal created
        _mockOutbox.Verify(
            x => x.EnqueueCodDeliveredAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COD-5: Ledger journal created only after successful collection
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkCodDelivered_WhenSuccessful_ShouldCreateLedgerJournal()
    {
        // Arrange
        var order = CreateCodOrder(FulfillmentStatus.Delivered, PaymentStatus.Pending);
        await _dbContext.Orders.AddAsync(order);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(order.Id);

        // Assert
        Assert.True(result.Success);
        
        // Verify ledger journal enqueued with correct parameters
        _mockOutbox.Verify(
            x => x.EnqueueCodDeliveredAsync(
                _tenantId,
                order.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
        
        // Verify outbox status returned
        Assert.NotNull(result.OutboxStatus);
        Assert.Equal("CREATED", result.OutboxStatus);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════════════════

    private Order CreateCodOrder(FulfillmentStatus fulfillmentStatus, PaymentStatus paymentStatus)
    {
        return CreateOrder("COD", fulfillmentStatus, paymentStatus);
    }

    private Order CreateOrder(string paymentMethod, FulfillmentStatus fulfillmentStatus, PaymentStatus paymentStatus)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid().ToString()[..8]}",
            PaymentMethodCode = paymentMethod,
            PaymentStatus = paymentStatus,
            FulfillmentStatus = fulfillmentStatus,
            FulfillmentMode = FulfillmentMode.Manual,
            Total = 100.00m,
            Currency = "EGP",
            Status = OrderStatus.Confirmed,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        _databaseScope.Dispose();
    }
}
