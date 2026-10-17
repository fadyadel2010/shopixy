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
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Treasury;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
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
    private readonly ITestOutputHelper _output;

    public CodPaymentLifecycleTests(ITestOutputHelper output)
    {
        _output = output;
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

        _mockEventPublisher.SetReturnsDefault(Task.CompletedTask);
        _mockMarketplaceEventOutbox.SetReturnsDefault(Task.CompletedTask);

        // Setup outbox to return success
        _mockOutbox
            .Setup(x => x.EnqueueCodDeliveredAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid tenantId, Guid orderId, CancellationToken cancellationToken) =>
            {
                var idempotencyKey = $"COD_DELIVERED:{tenantId}:{orderId}";
                var existing = await _dbContext.AccountingOutboxMessages
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(message =>
                        message.TenantId == tenantId &&
                        message.IdempotencyKey == idempotencyKey,
                        cancellationToken);

                if (existing != null)
                {
                    return OutboxEnqueueResult.Exists(existing.Id, "PENDING", idempotencyKey);
                }

                var trackedOrder = _dbContext.ChangeTracker
                    .Entries<Order>()
                    .Single(entry => entry.Entity.Id == orderId);
                var aggregateVersion = checked((int)(trackedOrder.Entity.ProjectionMutationVersion + 1));
                var message = PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage.Create(
                    tenantId,
                    "CodDelivered",
                    orderId,
                    "CodDelivered",
                    $"{{\"OrderId\":\"{orderId:D}\",\"MutationVersion\":{aggregateVersion}}}",
                    idempotencyKey,
                    "cod-test-correlation",
                    aggregateVersion);
                _dbContext.AccountingOutboxMessages.Add(message);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return OutboxEnqueueResult.Created(message.Id, idempotencyKey);
            });
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
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(_tenantId, order.Id);

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
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(_tenantId, order.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not delivered", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(FulfillmentStatus.Created)]
    [InlineData(FulfillmentStatus.OutForDelivery)]
    [InlineData(FulfillmentStatus.FailedDelivery)]
    [InlineData(FulfillmentStatus.Returned)]
    public async Task PaymentCapability_DoesNotAdvertiseCodCollectionBeforeOrAfterCanonicalDelivery(
        FulfillmentStatus fulfillmentStatus)
    {
        var order = CreateCodOrder(fulfillmentStatus, PaymentStatus.Pending);
        await _dbContext.Orders.AddAsync(order);
        await _dbContext.SaveChangesAsync();

        var actions = await _lifecycleService.GetAllowedActionsAsync(order.Id);

        Assert.False(actions.CanMarkOfflineCollectionDelivered);
    }

    [Fact]
    public async Task PaymentCapability_AdvertisesCodCollectionOnlyForDeliveredPendingCod()
    {
        var order = CreateCodOrder(FulfillmentStatus.Delivered, PaymentStatus.Pending);
        await _dbContext.Orders.AddAsync(order);
        await _dbContext.SaveChangesAsync();

        var actions = await _lifecycleService.GetAllowedActionsAsync(order.Id);

        Assert.True(actions.CanMarkOfflineCollectionDelivered);
        Assert.False(actions.CanRefund);
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
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(_tenantId, order.Id);

        // Assert
        Assert.True(result.Success, result.ErrorMessage);
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
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(_tenantId, order.Id);

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
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(_tenantId, order.Id);

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
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(_tenantId, order.Id);

        // Assert
        Assert.True(result.Success, result.ErrorMessage);
        
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

    [Fact]
    public async Task MarkCodDelivered_WhenTenantAuthorityDoesNotMatch_ShouldNotMutate()
    {
        var order = CreateCodOrder(FulfillmentStatus.Delivered, PaymentStatus.Pending);
        await _dbContext.Orders.AddAsync(order);
        await _dbContext.SaveChangesAsync();

        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(
            Guid.NewGuid(),
            order.Id,
            deliveryReference: "FORGED-CROSS-TENANT");

        Assert.False(result.Success);
        Assert.Equal("Order not found", result.ErrorMessage);

        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Orders
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == order.Id);
        Assert.Equal(PaymentStatus.Pending, persisted.PaymentStatus);
        _mockOutbox.Verify(
            outbox => outbox.EnqueueCodDeliveredAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Relational COD Ledger and Treasury Authority Certification
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ClearingTransferAndInactiveTreasuryMappings_AreRejectedWithoutPosting()
    {
        await new TenantAccountSeeder(_dbContext, NullLogger<TenantAccountSeeder>.Instance).SeedAsync(_tenantId);
        var resolver = new AccountResolver(_dbContext, NullLogger<AccountResolver>.Instance);
        var posting = new FinancialPostingService(_dbContext, NullLogger<FinancialPostingService>.Instance);
        var operations = new TreasuryOperationService(resolver, posting, _dbContext, NullLogger<TreasuryOperationService>.Instance);
        foreach (var clearing in new[] { "1110", "1120", "1130", "1140" })
        {
            await Assert.ThrowsAsync<TreasuryValidationException>(() => operations.TransferAsync(_tenantId, clearing, "1010", 1m, "Invalid source"));
            await Assert.ThrowsAsync<TreasuryValidationException>(() => operations.TransferAsync(_tenantId, "1010", clearing, 1m, "Invalid destination"));
        }
        var cash = await resolver.GetByCodeAsync(_tenantId, "1010");
        var mapping = await _dbContext.TreasuryAccounts.SingleAsync(item => item.AccountId == cash.Id);
        mapping.IsActive = false;
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<TreasuryValidationException>(() => operations.TransferAsync(_tenantId, "1010", "1030", 1m, "Inactive mapping"));
        mapping.IsActive = true;
        cash.IsActive = false;
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<TreasuryValidationException>(() => operations.TransferAsync(_tenantId, "1010", "1030", 1m, "Inactive account"));
        Assert.Empty(await _dbContext.JournalEntries.ToListAsync());
        Assert.All(await _dbContext.LedgerAccounts.ToListAsync(), account => Assert.Equal(0m, account.CurrentBalance));
    }

    [Fact]
    public async Task CodCollection_WithRealPosting_ConservesJournalTruthAndCannotBecomeCashboxTransfer()
    {
        await new TenantAccountSeeder(_dbContext, NullLogger<TenantAccountSeeder>.Instance).SeedAsync(_tenantId);
        var resolver = new AccountResolver(_dbContext, NullLogger<AccountResolver>.Instance);
        var posting = new FinancialPostingService(_dbContext, NullLogger<FinancialPostingService>.Instance);
        var commerce = new CommerceAccountingService(posting, resolver, Mock.Of<ICashboxLedgerAccountService>(), NullLogger<CommerceAccountingService>.Instance);
        _mockCommerceAccounting
            .Setup(service => service.PostCodDeliveredAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns((Order current, CancellationToken ct) => commerce.PostCodDeliveredAsync(current, ct));

        var order = CreateCodOrder(FulfillmentStatus.OutForDelivery, PaymentStatus.Pending);
        await _dbContext.Orders.AddAsync(order);
        await _dbContext.SaveChangesAsync();
        Assert.Empty(await _dbContext.JournalEntries.ToListAsync());
        Assert.False((await _lifecycleService.MarkOfflineCollectionDeliveredAsync(_tenantId, order.Id)).Success);

        // Disposable physical-delivery fixture; no customer or carrier call.
        order.FulfillmentStatus = FulfillmentStatus.Delivered;
        order.DeliveredAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(_tenantId, order.Id);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True((await _lifecycleService.MarkOfflineCollectionDeliveredAsync(_tenantId, order.Id)).Success);

        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Orders.SingleAsync(item => item.Id == order.Id);
        Assert.Equal(PaymentStatus.Collected, persisted.PaymentStatus);
        Assert.Equal(FinancialPostingStatus.Posted, persisted.FinancialPostingStatus);
        var journal = await _dbContext.JournalEntries.Include(item => item.LedgerEntries).SingleAsync();
        Assert.Equal("Order_COD", journal.SourceType);
        Assert.Equal(order.Id, journal.SourceId);
        Assert.Equal(order.OrderNumber, journal.SourceReference);
        Assert.Equal(_tenantId, journal.TenantId);
        Assert.Equal($"COD_DELIVERED:{_tenantId}:{order.Id}", journal.IdempotencyKey);
        Assert.Equal(persisted.FinancialPostingJournalId, journal.Id);
        Assert.Equal(100m, journal.LedgerEntries.Sum(line => line.DebitAmount));
        Assert.Equal(100m, journal.LedgerEntries.Sum(line => line.CreditAmount));
        var clearing = await resolver.GetByCodeAsync(_tenantId, AccountCodes.CodClearing);
        var cashbox = await resolver.GetByCodeAsync(_tenantId, AccountCodes.CashboxMain);
        var bank = await resolver.GetByCodeAsync(_tenantId, AccountCodes.BankAccount);
        Assert.Equal(100m, clearing.CurrentBalance);
        Assert.Equal(0m, cashbox.CurrentBalance);
        Assert.Equal(0m, bank.CurrentBalance);
        Assert.Equal(100m, Assert.Single(journal.LedgerEntries, line => line.AccountId == clearing.Id).DebitAmount);
        var sales = await resolver.GetByCodeAsync(_tenantId, AccountCodes.EcommerceSales);
        Assert.Equal(100m, Assert.Single(journal.LedgerEntries, line => line.AccountId == sales.Id).CreditAmount);

        var operations = new TreasuryOperationService(resolver, posting, _dbContext, NullLogger<TreasuryOperationService>.Instance);
        foreach (var destination in new[] { AccountCodes.CashboxMain, AccountCodes.BankAccount })
            await Assert.ThrowsAsync<TreasuryValidationException>(() => operations.TransferAsync(_tenantId, AccountCodes.CodClearing, destination, 1m, "Forbidden clearing transfer"));
        var transfer = new TreasuryTransferService(posting, _dbContext, NullLogger<TreasuryTransferService>.Instance);
        await Assert.ThrowsAsync<TreasuryValidationException>(() => transfer.SettleClearingAsync(_tenantId, clearing.Id, cashbox.Id, 1m, "Forbidden cashbox settlement"));
        await Assert.ThrowsAsync<TreasuryValidationException>(() => transfer.TransferAsync(Guid.NewGuid(), clearing.Id, bank.Id, 1m, "Foreign tenant"));
        foreach (var amount in new[] { 0m, -1m })
            await Assert.ThrowsAsync<TreasuryValidationException>(() => transfer.SettleClearingAsync(_tenantId, clearing.Id, bank.Id, amount, "Invalid amount"));

        await using var reload = _databaseScope.CreateDbContext(_tenantId);
        var ledger = await new FinancialReportService(reload, NullLogger<FinancialReportService>.Instance)
            .GetGeneralLedgerAsync(_tenantId, clearing.Id, DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date.AddDays(1));
        var entry = Assert.Single(ledger.Entries);
        Assert.Equal(journal.Id, entry.JournalId);
        Assert.Equal(order.OrderNumber, entry.SourceReference);
        Assert.Equal(100m, entry.DebitAmount);
        Assert.Equal(0m, entry.CreditAmount);
        Assert.Equal(100m, ledger.ClosingBalance);
        Assert.Single(await reload.JournalEntries.ToListAsync());
        Assert.Empty(await reload.PaymentSettlementBatches.ToListAsync());
        _mockEventPublisher.Verify(service => service.PublishAsync(It.IsAny<PointOfSale.Core.Events.OfflinePaymentCollectedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
            tenantId = _tenantId, orderId = order.Id, order.OrderNumber, order.Total,
            journalId = journal.Id, journal.JournalNumber, journal.JournalDate, journal.SourceType,
            journal.SourceReference, journal.IdempotencyKey, journal.CreatedByUserId,
            clearingId = clearing.Id, beforeClearing = 0m, afterClearing = 100m,
            cashboxId = cashbox.Id, beforeCashbox = 0m, afterCashbox = 0m,
            bankId = bank.Id, beforeBank = 0m, afterBank = 0m,
            ledger = ledger.Entries, settlementExecuted = false,
            database = reload.Database.GetDbConnection().Database
        }));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Collected)]
    public async Task OperatorCollection_RejectsCarrierOwnershipEvenOnReplay(PaymentStatus status)
    {
        var order = CreateCodOrder(FulfillmentStatus.Delivered, status);
        order.FulfillmentMode = FulfillmentMode.Carrier;
        order.FulfillmentCarrierCode = "bosta";
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(_tenantId, order.Id);
        Assert.False(result.Success);
        Assert.Equal("COD_MANUAL_FULFILLMENT_REQUIRED", result.FailureCode);
        Assert.False((await _lifecycleService.GetAllowedActionsAsync(order.Id)).CanMarkOfflineCollectionDelivered);
        _mockCommerceAccounting.Verify(service => service.PostCodDeliveredAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("not-collected")]
    [InlineData("wrong-provider")]
    [InlineData("wrong-amount")]
    [InlineData("wrong-currency")]
    [InlineData("foreign-shipment")]
    [InlineData("foreign-tenant")]
    [InlineData("not-delivered")]
    public async Task CarrierCollection_RequiresCurrentTenantOwnedShipmentCollectionTruth(string defect)
    {
        var order = CreateCodOrder(defect == "not-delivered" ? FulfillmentStatus.OutForDelivery : FulfillmentStatus.Delivered, PaymentStatus.Pending);
        order.FulfillmentMode = FulfillmentMode.Carrier;
        order.FulfillmentCarrierCode = "bosta";
        _dbContext.Orders.Add(order);
        var shipment = new Shipment
        {
            TenantId = _tenantId, OrderId = order.Id, CarrierCode = "bosta", ShipmentNumber = $"SHP-{Guid.NewGuid():N}",
            IsCashOnDelivery = true, CodAmount = 100m, Currency = "EGP", CodCollected = true,
            CodCollectedAtUtc = DateTime.UtcNow, Status = ShipmentStatus.Delivered
        };
        if (defect == "not-collected") shipment.CodCollected = false;
        if (defect == "wrong-provider") shipment.CarrierCode = "aramex";
        if (defect == "wrong-amount") shipment.CodAmount = 99m;
        if (defect == "wrong-currency") shipment.Currency = "USD";
        if (defect == "foreign-shipment") shipment.TenantId = Guid.NewGuid();
        if (defect != "missing") _dbContext.Shipments.Add(shipment);
        await _dbContext.SaveChangesAsync();

        var result = await _lifecycleService.RecordCarrierCodCollectionAsync(
            defect == "foreign-tenant" ? Guid.NewGuid() : _tenantId, order.Id, shipment.Id);
        Assert.False(result.Success, defect);
        Assert.Equal(PaymentStatus.Pending, (await _dbContext.Orders.SingleAsync(item => item.Id == order.Id)).PaymentStatus);
        Assert.Empty(await _dbContext.JournalEntries.ToListAsync());
        Assert.Empty(await _dbContext.AccountingOutboxMessages.ToListAsync());
        _mockEventPublisher.Verify(service => service.PublishAsync(It.IsAny<PointOfSale.Core.Events.OfflinePaymentCollectedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CarrierCollection_MutableShipmentFlagsAndCallerMoneyCannotSupplyEvidence()
    {
        await new TenantAccountSeeder(_dbContext, NullLogger<TenantAccountSeeder>.Instance).SeedAsync(_tenantId);
        var resolver = new AccountResolver(_dbContext, NullLogger<AccountResolver>.Instance);
        var commerce = new CommerceAccountingService(new FinancialPostingService(_dbContext, NullLogger<FinancialPostingService>.Instance),
            resolver, Mock.Of<ICashboxLedgerAccountService>(), NullLogger<CommerceAccountingService>.Instance, _dbContext);
        _mockCommerceAccounting.Setup(service => service.PostCodDeliveredAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns((Order current, CancellationToken ct) => commerce.PostCodDeliveredAsync(current, ct));
        var order = CreateCodOrder(FulfillmentStatus.Delivered, PaymentStatus.Pending);
        order.FulfillmentMode = FulfillmentMode.Carrier;
        order.FulfillmentCarrierCode = "bosta";
        _dbContext.Orders.Add(order);
        var shipment = new Shipment
        {
            TenantId = _tenantId, OrderId = order.Id, CarrierCode = "bosta", ShipmentNumber = $"SHP-{Guid.NewGuid():N}",
            IsCashOnDelivery = true, CodAmount = 100m, Currency = "EGP", CodCollected = true,
            CodCollectedAtUtc = DateTime.UtcNow, Status = ShipmentStatus.Delivered
        };
        _dbContext.Shipments.Add(shipment);
        await _dbContext.SaveChangesAsync();
        var result = await _lifecycleService.HandlePaymentResultAsync(new PointOfSale.Core.Interfaces.Payments.HandlePaymentResultRequest
        {
            TenantId = _tenantId, OrderId = order.Id, CarrierCollectionShipmentId = shipment.Id,
            Source = PointOfSale.Core.Interfaces.Payments.PaymentLifecycleSource.OfflineCollection,
            Outcome = PointOfSale.Core.Interfaces.Payments.PaymentLifecycleOutcome.Succeeded,
            Amount = 999m, ProviderAmount = 999m, Currency = "USD", ProviderCurrency = "USD", IgnoreQueryFilters = true
        });
        Assert.False(result.Success);
        Assert.Equal("COD_CARRIER_EVIDENCE_REQUIRED", result.FailureCode);
        var replay = await _lifecycleService.RecordCarrierCodCollectionAsync(_tenantId, order.Id, shipment.Id);
        Assert.False(replay.Success);
        Assert.Empty(await _dbContext.JournalEntries.ToListAsync());
        Assert.Empty(await _dbContext.AccountingOutboxMessages.ToListAsync());
        Assert.Equal(0m, (await resolver.GetByCodeAsync(_tenantId, "1130")).CurrentBalance);
        Assert.Equal(0m, (await resolver.GetByCodeAsync(_tenantId, "1010")).CurrentBalance);
        Assert.Equal(0m, (await resolver.GetByCodeAsync(_tenantId, "1030")).CurrentBalance);
        Assert.Empty(await _dbContext.PaymentSettlementBatches.ToListAsync());
        _mockEventPublisher.Verify(service => service.PublishAsync(It.IsAny<PointOfSale.Core.Events.OfflinePaymentCollectedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

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
