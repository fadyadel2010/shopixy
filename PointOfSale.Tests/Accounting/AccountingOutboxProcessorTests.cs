using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Models.Refunds;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PosPaymentMethod = PointOfSale.Core.Enums.PaymentMethod;

namespace PointOfSale.Tests.Accounting;

public sealed class AccountingOutboxProcessorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _db;
    private readonly Mock<ICommerceAccountingService> _commerceAccounting = new();
    private readonly Mock<IFinancialPostingService> _postingService = new();
    private readonly Mock<IMarketplaceEventOutboxService> _marketplaceOutbox = new();
    private readonly AccountingOutboxProcessor _processor;
    private readonly Guid _tenantId = Guid.NewGuid();

    public AccountingOutboxProcessorTests()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped(_ => _commerceAccounting.Object);
        services.AddScoped(_ => _postingService.Object);
        services.AddScoped(_ => _marketplaceOutbox.Object);

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<ApplicationDbContext>();
        _marketplaceOutbox
            .Setup(x => x.EnqueueAsync(
                It.IsAny<PointOfSale.Core.Events.Marketplace.RefundProcessedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _processor = new AccountingOutboxProcessor(
            _serviceProvider,
            NullLogger<AccountingOutboxProcessor>.Instance);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_SaleReversal_ReversesPostedSaleJournals_AndReappliesUpdatedSale()
    {
        var invoice = SeedInvoice(accountingMutationVersion: 1);
        var revenueJournal = CreateJournal(
            "JE-REV-1",
            "SalesInvoice",
            invoice.Id,
            invoice.InvoiceNumber,
            $"SALE:{_tenantId}:{invoice.Id}");
        var cogsJournal = CreateJournal(
            "JE-COGS-1",
            "SalesInvoice_COGS",
            invoice.Id,
            invoice.InvoiceNumber,
            $"COGS:{_tenantId}:{invoice.Id}");

        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "SaleReversal",
            invoice.Id,
            typeof(SalesInvoice).FullName ?? nameof(SalesInvoice),
            JsonSerializer.Serialize(new
            {
                InvoiceId = invoice.Id,
                OperationKey = "update-req-1",
                MutationVersion = invoice.AccountingMutationVersion
            }),
            $"SALE:{_tenantId}:{invoice.Id}:update-req-1");
        message.Id = Guid.NewGuid();

        using (LedgerMutationScope.Authorize())
        {
            _db.JournalEntries.AddRange(revenueJournal, cogsJournal);
            _db.AccountingOutboxMessages.Add(message);
            await _db.SaveChangesAsync();
        }

        _postingService
            .Setup(x => x.ExistsByIdempotencyKeyAsync(_tenantId, message.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _postingService
            .Setup(x => x.ReverseJournalAsync(
                _tenantId,
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid journalId, string reason, Guid? userId, CancellationToken ct) =>
                CreateJournal(
                    $"JE-R-{journalId.ToString("N")[..6]}",
                    "Reversal",
                    journalId,
                    null,
                    $"REVERSE:{tenantId}:{journalId}"));

        _commerceAccounting
            .Setup(x => x.PostPosSaleAsync(
                It.Is<SalesInvoice>(sale => sale.Id == invoice.Id),
                It.IsAny<CancellationToken>(),
                It.Is<string?>(key => key == "update-req-1")))
            .ReturnsAsync(PostingResult.Succeeded(Guid.NewGuid(), "JE-UPDATED-1"));

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        _postingService.Verify(x => x.ReverseJournalAsync(
            _tenantId,
            revenueJournal.Id,
            It.IsAny<string>(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        _postingService.Verify(x => x.ReverseJournalAsync(
            _tenantId,
            cogsJournal.Id,
            It.IsAny<string>(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        _commerceAccounting.Verify(x => x.PostPosSaleAsync(
            It.Is<SalesInvoice>(sale => sale.Id == invoice.Id),
            It.IsAny<CancellationToken>(),
            It.Is<string?>(key => key == "update-req-1")), Times.Once);

        await _db.Entry(message).ReloadAsync();
        var storedMessage = await _db.AccountingOutboxMessages
            .AsNoTracking()
            .SingleAsync();
        Assert.NotNull(storedMessage.ProcessedAtUtc);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_VersionedAggregate_DefersNewerMessageUntilEarlierVersionCompletes()
    {
        var invoice = SeedInvoice(accountingMutationVersion: 2);
        var earlierMessage = AccountingOutboxMessage.Create(
            _tenantId,
            "PosSale",
            invoice.Id,
            typeof(SalesInvoice).FullName ?? nameof(SalesInvoice),
            JsonSerializer.Serialize(new
            {
                InvoiceId = invoice.Id,
                MutationVersion = 1
            }),
            $"SALE:{_tenantId}:{invoice.Id}:v1",
            aggregateVersion: 1);
        earlierMessage.Id = Guid.NewGuid();
        earlierMessage.ClaimedUntilUtc = DateTime.UtcNow.AddMinutes(10);

        var newerMessage = AccountingOutboxMessage.Create(
            _tenantId,
            "PosSale",
            invoice.Id,
            typeof(SalesInvoice).FullName ?? nameof(SalesInvoice),
            JsonSerializer.Serialize(new
            {
                InvoiceId = invoice.Id,
                MutationVersion = 2
            }),
            $"SALE:{_tenantId}:{invoice.Id}:v2",
            aggregateVersion: 2);
        newerMessage.Id = Guid.NewGuid();

        _db.AccountingOutboxMessages.AddRange(earlierMessage, newerMessage);
        await _db.SaveChangesAsync();

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);
        _commerceAccounting.Verify(x => x.PostPosSaleAsync(
            It.IsAny<SalesInvoice>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>()), Times.Never);

        var storedNewerMessage = await _db.AccountingOutboxMessages
            .AsNoTracking()
            .SingleAsync(message => message.Id == newerMessage.Id);
        Assert.Null(storedNewerMessage.ProcessedAtUtc);
        Assert.Null(storedNewerMessage.ClaimedUntilUtc);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_VersionedAggregate_DefersNewerMessageUntilEarlierCriticalFailureIsRecovered()
    {
        var invoice = SeedInvoice(accountingMutationVersion: 2);
        var earlierMessage = AccountingOutboxMessage.Create(
            _tenantId,
            "PosSale",
            invoice.Id,
            typeof(SalesInvoice).FullName ?? nameof(SalesInvoice),
            JsonSerializer.Serialize(new
            {
                InvoiceId = invoice.Id,
                MutationVersion = 1
            }),
            $"SALE:{_tenantId}:{invoice.Id}:v1",
            aggregateVersion: 1);
        earlierMessage.Id = Guid.NewGuid();
        earlierMessage.IsFailed = true;

        var newerMessage = AccountingOutboxMessage.Create(
            _tenantId,
            "PosSale",
            invoice.Id,
            typeof(SalesInvoice).FullName ?? nameof(SalesInvoice),
            JsonSerializer.Serialize(new
            {
                InvoiceId = invoice.Id,
                MutationVersion = 2
            }),
            $"SALE:{_tenantId}:{invoice.Id}:v2",
            aggregateVersion: 2);
        newerMessage.Id = Guid.NewGuid();

        _db.AccountingOutboxMessages.AddRange(earlierMessage, newerMessage);
        await _db.SaveChangesAsync();

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);
        _commerceAccounting.Verify(x => x.PostPosSaleAsync(
            It.IsAny<SalesInvoice>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>()), Times.Never);

        var storedNewerMessage = await _db.AccountingOutboxMessages
            .AsNoTracking()
            .SingleAsync(message => message.Id == newerMessage.Id);
        Assert.Null(storedNewerMessage.ProcessedAtUtc);
        Assert.Null(storedNewerMessage.ClaimedUntilUtc);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_PosSale_DoesNotShortCircuitWhenPrimaryJournalAlreadyExists()
    {
        var invoice = SeedInvoice();
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "PosSale",
            invoice.Id,
            typeof(SalesInvoice).FullName ?? nameof(SalesInvoice),
            JsonSerializer.Serialize(new { InvoiceId = invoice.Id }),
            $"SALE:{_tenantId}:{invoice.Id}");
        message.Id = Guid.NewGuid();

        var existingRevenueJournal = CreateJournal(
            "JE-REV-EXISTING",
            "SalesInvoice",
            invoice.Id,
            invoice.InvoiceNumber,
            message.IdempotencyKey);

        using (LedgerMutationScope.Authorize())
        {
            _db.JournalEntries.Add(existingRevenueJournal);
            _db.AccountingOutboxMessages.Add(message);
            await _db.SaveChangesAsync();
        }

        _commerceAccounting
            .Setup(x => x.PostPosSaleAsync(
                It.Is<SalesInvoice>(sale => sale.Id == invoice.Id),
                It.IsAny<CancellationToken>(),
                null))
            .ReturnsAsync(PostingResult.Succeeded(existingRevenueJournal.Id, existingRevenueJournal.JournalNumber));

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        _postingService.Verify(x => x.ExistsByIdempotencyKeyAsync(
            _tenantId,
            message.IdempotencyKey,
            It.IsAny<CancellationToken>()), Times.Never);
        _commerceAccounting.Verify(x => x.PostPosSaleAsync(
            It.Is<SalesInvoice>(sale => sale.Id == invoice.Id),
            It.IsAny<CancellationToken>(),
            null), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_PosSale_SkipsStaleMutationVersion()
    {
        var invoice = SeedInvoice(accountingMutationVersion: 1);
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "PosSale",
            invoice.Id,
            typeof(SalesInvoice).FullName ?? nameof(SalesInvoice),
            JsonSerializer.Serialize(new
            {
                InvoiceId = invoice.Id,
                MutationVersion = 0
            }),
            $"SALE:{_tenantId}:{invoice.Id}");
        message.Id = Guid.NewGuid();

        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        _commerceAccounting.Verify(x => x.PostPosSaleAsync(
            It.IsAny<SalesInvoice>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>()), Times.Never);

        var storedMessage = await _db.AccountingOutboxMessages
            .AsNoTracking()
            .SingleAsync();
        Assert.NotNull(storedMessage.ProcessedAtUtc);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_SaleReversal_SkipsStaleMutationVersion()
    {
        var invoice = SeedInvoice(accountingMutationVersion: 2);
        var journal = CreateJournal(
            "JE-REV-STALE",
            "SalesInvoice",
            invoice.Id,
            invoice.InvoiceNumber,
            $"SALE:{_tenantId}:{invoice.Id}");
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "SaleReversal",
            invoice.Id,
            typeof(SalesInvoice).FullName ?? nameof(SalesInvoice),
            JsonSerializer.Serialize(new
            {
                InvoiceId = invoice.Id,
                OperationKey = "update-req-stale",
                MutationVersion = 1
            }),
            $"SALE:{_tenantId}:{invoice.Id}:update-req-stale");
        message.Id = Guid.NewGuid();

        using (LedgerMutationScope.Authorize())
        {
            _db.JournalEntries.Add(journal);
            _db.AccountingOutboxMessages.Add(message);
            await _db.SaveChangesAsync();
        }

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        _postingService.Verify(x => x.ReverseJournalAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            null,
            It.IsAny<CancellationToken>()), Times.Never);
        _commerceAccounting.Verify(x => x.PostPosSaleAsync(
            It.IsAny<SalesInvoice>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>()), Times.Never);

        var storedMessage = await _db.AccountingOutboxMessages
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == message.Id);
        Assert.NotNull(storedMessage.ProcessedAtUtc);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_PosRefund_DerivesCashRefundFromLegacyInvoiceWhenPayloadIsMissingAllocation()
    {
        var invoice = SeedInvoice(paymentMethod: PosPaymentMethod.Cash, paidAmount: 100m, cashAmount: 100m, cardAmount: 0m);
        var returnObj = SeedReturn(invoice, totalAmount: 40m);
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "PosRefund",
            returnObj.Id,
            typeof(Return).FullName ?? nameof(Return),
            JsonSerializer.Serialize(new { ReturnId = returnObj.Id }),
            $"REFUND:{_tenantId}:{returnObj.Id}");
        message.Id = Guid.NewGuid();

        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        _commerceAccounting
            .Setup(x => x.PostPosRefundAsync(
                It.Is<Return>(r => r.Id == returnObj.Id),
                It.Is<RefundAllocation>(allocation =>
                    allocation.Method == RefundMethod.Cash &&
                    allocation.CashAmount == 40m &&
                    allocation.CardAmount == 0m),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingResult.Succeeded(Guid.NewGuid(), "JE-REFUND-1"));

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        _commerceAccounting.Verify(x => x.PostPosRefundAsync(
            It.Is<Return>(r => r.Id == returnObj.Id),
            It.Is<RefundAllocation>(allocation =>
                allocation.Method == RefundMethod.Cash &&
                allocation.CashAmount == 40m &&
                allocation.CardAmount == 0m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_PosRefund_ProratesLegacyMixedRefundUsingOriginalInvoiceSplit()
    {
        var invoice = SeedInvoice(paymentMethod: PosPaymentMethod.Mixed, paidAmount: 100m, cashAmount: 60m, cardAmount: 40m);
        var returnObj = SeedReturn(invoice, totalAmount: 50m);
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "PosRefund",
            returnObj.Id,
            typeof(Return).FullName ?? nameof(Return),
            JsonSerializer.Serialize(new { ReturnId = returnObj.Id }),
            $"REFUND:{_tenantId}:{returnObj.Id}");
        message.Id = Guid.NewGuid();

        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        _commerceAccounting
            .Setup(x => x.PostPosRefundAsync(
                It.Is<Return>(r => r.Id == returnObj.Id),
                It.Is<RefundAllocation>(allocation =>
                    allocation.Method == RefundMethod.Mixed &&
                    allocation.CashAmount == 30m &&
                    allocation.CardAmount == 20m),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingResult.Succeeded(Guid.NewGuid(), "JE-REFUND-2"));

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        _commerceAccounting.Verify(x => x.PostPosRefundAsync(
            It.Is<Return>(r => r.Id == returnObj.Id),
            It.Is<RefundAllocation>(allocation =>
                allocation.Method == RefundMethod.Mixed &&
                allocation.CashAmount == 30m &&
                allocation.CardAmount == 20m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_PosRefund_FailsLegacyPayloadWhenOriginalInvoiceCannotBeLoadedSafely()
    {
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Refund fixture warehouse",
            IsActive = true
        };
        var returnObj = new Return
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ReturnNumber = "SR-20260410-1000",
            ReturnDate = DateTime.UtcNow,
            ReturnType = ReturnType.SalesReturn,
            WarehouseId = warehouse.Id,
            TotalAmount = 25m
        };

        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "PosRefund",
            returnObj.Id,
            typeof(Return).FullName ?? nameof(Return),
            JsonSerializer.Serialize(new { ReturnId = returnObj.Id }),
            $"REFUND:{_tenantId}:{returnObj.Id}");
        message.Id = Guid.NewGuid();

        _db.Warehouses.Add(warehouse);
        _db.Returns.Add(returnObj);
        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);
        _commerceAccounting.Verify(x => x.PostPosRefundAsync(
            It.IsAny<Return>(),
            It.IsAny<RefundAllocation>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await _db.Entry(message).ReloadAsync();
        var storedMessage = await _db.AccountingOutboxMessages.SingleAsync();
        Assert.Null(storedMessage.ProcessedAtUtc);
        Assert.Equal(1, storedMessage.AttemptCount);
        Assert.Contains("original invoice could not be loaded", storedMessage.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_SupplierOpeningBalance_ProcessesDistinctEntriesForSameSupplier()
    {
        var supplierId = Guid.NewGuid();
        var firstOpeningBalance = SeedSupplierOpeningBalance(supplierId, 125m);
        var secondOpeningBalance = SeedSupplierOpeningBalance(supplierId, 75m);
        var firstMessage = AccountingOutboxMessage.Create(
            _tenantId,
            "SupplierOpeningBalance",
            firstOpeningBalance.Id,
            typeof(AccountLedger).FullName ?? nameof(AccountLedger),
            JsonSerializer.Serialize(new { OpeningBalanceId = firstOpeningBalance.Id }),
            $"SUPPLIER_OPENING_BALANCE:{_tenantId}:{firstOpeningBalance.Id}");
        var secondMessage = AccountingOutboxMessage.Create(
            _tenantId,
            "SupplierOpeningBalance",
            secondOpeningBalance.Id,
            typeof(AccountLedger).FullName ?? nameof(AccountLedger),
            JsonSerializer.Serialize(new { OpeningBalanceId = secondOpeningBalance.Id }),
            $"SUPPLIER_OPENING_BALANCE:{_tenantId}:{secondOpeningBalance.Id}");

        firstMessage.Id = Guid.NewGuid();
        secondMessage.Id = Guid.NewGuid();

        _db.AccountingOutboxMessages.AddRange(firstMessage, secondMessage);
        await _db.SaveChangesAsync();

        var processedOpeningBalanceIds = new List<Guid>();
        _postingService
            .Setup(x => x.ExistsByIdempotencyKeyAsync(_tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _commerceAccounting
            .Setup(x => x.PostSupplierOpeningBalanceAsync(It.IsAny<AccountLedger>(), It.IsAny<CancellationToken>()))
            .Callback<AccountLedger, CancellationToken>((openingBalance, _) => processedOpeningBalanceIds.Add(openingBalance.Id))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), $"JE-OB-{Guid.NewGuid():N}"[..12]));

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(2, processedCount);
        Assert.Contains(firstOpeningBalance.Id, processedOpeningBalanceIds);
        Assert.Contains(secondOpeningBalance.Id, processedOpeningBalanceIds);

        await _db.Entry(firstMessage).ReloadAsync();
        await _db.Entry(secondMessage).ReloadAsync();
        Assert.NotNull(firstMessage.ProcessedAtUtc);
        Assert.NotNull(secondMessage.ProcessedAtUtc);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_SupplierOpeningBalance_LegacySupplierPayloadRemainsCompatible()
    {
        var supplierId = Guid.NewGuid();
        var openingBalance = SeedSupplierOpeningBalance(supplierId, 90m);
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "SupplierOpeningBalance",
            supplierId,
            typeof(AccountLedger).FullName ?? nameof(AccountLedger),
            JsonSerializer.Serialize(new { SupplierId = supplierId }),
            $"SUPPLIER_OPENING_BALANCE:{_tenantId}:{supplierId}");
        message.Id = Guid.NewGuid();

        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        AccountLedger? processedOpeningBalance = null;
        _postingService
            .Setup(x => x.ExistsByIdempotencyKeyAsync(_tenantId, message.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _commerceAccounting
            .Setup(x => x.PostSupplierOpeningBalanceAsync(It.IsAny<AccountLedger>(), It.IsAny<CancellationToken>()))
            .Callback<AccountLedger, CancellationToken>((entry, _) => processedOpeningBalance = entry)
            .ReturnsAsync(PostingResult.Succeeded(Guid.NewGuid(), "JE-OB-LEGACY"));

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        Assert.NotNull(processedOpeningBalance);
        Assert.Equal(openingBalance.Id, processedOpeningBalance!.Id);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_WhenCriticalFinancialRetriesAreExhausted_RemainsRetryableAndDoesNotDeadLetter()
    {
        var invoice = SeedInvoice();
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "PosSale",
            invoice.Id,
            typeof(SalesInvoice).FullName ?? nameof(SalesInvoice),
            JsonSerializer.Serialize(new { InvoiceId = invoice.Id }),
            $"SALE:{_tenantId}:{invoice.Id}");
        message.Id = Guid.NewGuid();

        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var metrics = new Mock<IAccountingMetrics>();
        var processor = new AccountingOutboxProcessor(
            _serviceProvider,
            NullLogger<AccountingOutboxProcessor>.Instance,
            Options.Create(new AccountingOutboxOptions
            {
                MaxRetryAttempts = 1
            }),
            metrics.Object);

        _commerceAccounting
            .Setup(x => x.PostPosSaleAsync(
                It.Is<SalesInvoice>(sale => sale.Id == invoice.Id),
                It.IsAny<CancellationToken>(),
                null))
            .ThrowsAsync(new InvalidOperationException("Downstream ledger unavailable"));

        var processedCount = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);

        await _db.Entry(message).ReloadAsync();
        Assert.False(message.IsFailed);
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.NextRetryAtUtc);
        Assert.Contains("Downstream ledger unavailable", message.LastError, StringComparison.OrdinalIgnoreCase);

        metrics.Verify(
            x => x.IncrementOutboxDeadLettered(_tenantId, "PosSale"),
            Times.Never);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_OnlinePayment_CollapsesStaleTerminalPaymentReplay()
    {
        var order = SeedOrder(PaymentStatus.Expired);
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "OnlinePayment",
            order.Id,
            typeof(Order).FullName ?? nameof(Order),
            JsonSerializer.Serialize(new
            {
                OrderId = order.Id,
                PaymentIntentId = "pi_stale",
                Gateway = "stripe",
                MutationVersion = 1
            }),
            $"ONLINE_PAYMENT:{_tenantId}:{order.Id}:pi_stale",
            aggregateVersion: 1);
        message.Id = Guid.NewGuid();

        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        _commerceAccounting.Verify(x => x.PostOnlinePaymentCapturedAsync(
            It.IsAny<Order>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        var storedMessage = await _db.AccountingOutboxMessages
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == message.Id);
        Assert.NotNull(storedMessage.ProcessedAtUtc);
        Assert.Equal(Guid.Empty, storedMessage.JournalId);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_EcommerceRefund_RecomposesAmountFromCurrentRefundAuthority()
    {
        var order = SeedOrder(PaymentStatus.PartiallyRefunded);
        var refund = new OrderRefund
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderId = order.Id,
            RefundId = "rf_current",
            Amount = 25m,
            Currency = order.Currency,
            IsFullRefund = false,
            Reason = "Partial refund",
            PaymentMethod = "stripe",
            FinancialPostingStatus = FinancialPostingStatus.Pending,
            RefundedAtUtc = DateTime.UtcNow
        };

        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "EcommerceRefund",
            order.Id,
            typeof(Order).FullName ?? nameof(Order),
            JsonSerializer.Serialize(new
            {
                OrderId = order.Id,
                RefundAmount = 75m,
                RefundId = refund.RefundId,
                MutationVersion = 2
            }),
            $"ECOM_REFUND:{_tenantId}:{order.Id}:{refund.RefundId}",
            aggregateVersion: 2);
        message.Id = Guid.NewGuid();

        _db.OrderRefunds.Add(refund);
        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var postedJournalId = Guid.NewGuid();
        _commerceAccounting
            .Setup(x => x.PostEcommerceRefundAsync(
                It.Is<Order>(candidate => candidate.Id == order.Id),
                25m,
                refund.RefundId,
                It.IsAny<EcommerceRefundAccountingContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingResult.Succeeded(postedJournalId, "JE-REFUND-CURRENT"));

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        _commerceAccounting.Verify(x => x.PostEcommerceRefundAsync(
            It.Is<Order>(candidate => candidate.Id == order.Id),
            25m,
            refund.RefundId,
            It.IsAny<EcommerceRefundAccountingContext?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _commerceAccounting.Verify(x => x.PostEcommerceRefundAsync(
            It.IsAny<Order>(),
            75m,
            It.IsAny<string>(),
            It.IsAny<EcommerceRefundAccountingContext?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_GatewaySettlement_RecomposesAmountsFromCurrentSettlementBatch()
    {
        var order = SeedOrder(PaymentStatus.Settled);
        var settlementBatch = new PaymentSettlementBatch
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            SettlementType = "GATEWAY",
            SettlementId = "set_current",
            Provider = "stripe",
            GrossAmount = 100m,
            Fees = 3m,
            Currency = "EGP",
            OrderCount = 1,
            SettledAtUtc = DateTime.UtcNow
        };
        var settlementOrder = new PaymentSettlementOrder
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            SettlementBatchId = settlementBatch.Id,
            OrderId = order.Id,
            OrderTotal = order.Total,
            Currency = order.Currency,
            CreatedAt = DateTime.UtcNow
        };

        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "GatewaySettlement",
            settlementBatch.Id,
            typeof(PaymentSettlementBatch).FullName ?? nameof(PaymentSettlementBatch),
            JsonSerializer.Serialize(new
            {
                SettlementBatchId = settlementBatch.Id,
                Gateway = "stripe",
                GrossAmount = 500m,
                Fees = 40m,
                SettlementId = settlementBatch.SettlementId
            }),
            $"GATEWAY_SETTLEMENT:{_tenantId}:{settlementBatch.SettlementId}");
        message.Id = Guid.NewGuid();

        _db.PaymentSettlementBatches.Add(settlementBatch);
        _db.PaymentSettlementOrders.Add(settlementOrder);
        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        _commerceAccounting
            .Setup(x => x.PostGatewaySettlementAsync(
                _tenantId,
                "stripe",
                100m,
                3m,
                settlementBatch.SettlementId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingResult.Succeeded(Guid.NewGuid(), "JE-SETTLEMENT-CURRENT"));

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        _commerceAccounting.Verify(x => x.PostGatewaySettlementAsync(
            _tenantId,
            "stripe",
            100m,
            3m,
            settlementBatch.SettlementId,
            It.IsAny<CancellationToken>()), Times.Once);
        _commerceAccounting.Verify(x => x.PostGatewaySettlementAsync(
            _tenantId,
            "stripe",
            500m,
            40m,
            settlementBatch.SettlementId,
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_GatewaySettlement_RetriesWhenCurrentSettlementOrderLineageDrifts()
    {
        var order = SeedOrder(PaymentStatus.Settled);
        var settlementBatch = new PaymentSettlementBatch
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            SettlementType = "GATEWAY",
            SettlementId = "set_drift",
            Provider = "stripe",
            GrossAmount = 100m,
            Fees = 3m,
            Currency = "EGP",
            OrderCount = 2,
            SettledAtUtc = DateTime.UtcNow
        };
        var settlementOrder = new PaymentSettlementOrder
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            SettlementBatchId = settlementBatch.Id,
            OrderId = order.Id,
            OrderTotal = order.Total,
            Currency = order.Currency,
            CreatedAt = DateTime.UtcNow
        };

        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "GatewaySettlement",
            settlementBatch.Id,
            typeof(PaymentSettlementBatch).FullName ?? nameof(PaymentSettlementBatch),
            JsonSerializer.Serialize(new
            {
                SettlementBatchId = settlementBatch.Id,
                Gateway = "stripe",
                GrossAmount = settlementBatch.GrossAmount,
                Fees = settlementBatch.Fees,
                SettlementId = settlementBatch.SettlementId
            }),
            $"GATEWAY_SETTLEMENT:{_tenantId}:{settlementBatch.SettlementId}");
        message.Id = Guid.NewGuid();

        _db.PaymentSettlementBatches.Add(settlementBatch);
        _db.PaymentSettlementOrders.Add(settlementOrder);
        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);
        _commerceAccounting.Verify(x => x.PostGatewaySettlementAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await _db.Entry(message).ReloadAsync();
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(1, message.AttemptCount);
        Assert.Contains("order lineage drifted", message.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_EcommerceRefund_RetriesWhenPostSettlementRefundLosesCurrentSettlementOrderLineage()
    {
        var order = SeedOrder(PaymentStatus.Settled);
        var settlementBatch = new PaymentSettlementBatch
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            SettlementType = "GATEWAY",
            SettlementId = "set_refund_retry",
            Provider = "stripe",
            GrossAmount = 100m,
            Fees = 3m,
            Currency = "EGP",
            OrderCount = 1,
            SettledAtUtc = DateTime.UtcNow
        };
        var refund = new OrderRefund
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderId = order.Id,
            RefundId = "refund-post-settlement-retry",
            Amount = 25m,
            Currency = order.Currency,
            IsFullRefund = false,
            Reason = "Settlement lineage drift",
            PaymentMethod = "stripe",
            IsPostSettlementRefund = true,
            SettlementBatchId = settlementBatch.Id,
            SettlementType = settlementBatch.SettlementType,
            SettlementId = settlementBatch.SettlementId,
            FinancialPostingStatus = FinancialPostingStatus.Pending,
            RefundedAtUtc = DateTime.UtcNow
        };

        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "EcommerceRefund",
            order.Id,
            typeof(Order).FullName ?? nameof(Order),
            JsonSerializer.Serialize(new
            {
                OrderId = order.Id,
                RefundAmount = refund.Amount,
                RefundId = refund.RefundId,
                MutationVersion = 2,
                IsPostSettlementRefund = true,
                SettlementBatchId = settlementBatch.Id,
                SettlementType = settlementBatch.SettlementType,
                SettlementId = settlementBatch.SettlementId
            }),
            $"ECOM_REFUND:{_tenantId}:{order.Id}:{refund.RefundId}",
            aggregateVersion: 2);
        message.Id = Guid.NewGuid();

        _db.PaymentSettlementBatches.Add(settlementBatch);
        _db.OrderRefunds.Add(refund);
        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);
        _commerceAccounting.Verify(x => x.PostEcommerceRefundAsync(
            It.IsAny<Order>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<EcommerceRefundAccountingContext?>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await _db.Entry(message).ReloadAsync();
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(1, message.AttemptCount);
        Assert.Contains("no longer part of the current settlement-order lineage", message.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CleanupAsync_DeletesExpiredMessagesWithinConfiguredBatchCap()
    {
        var nowUtc = DateTime.UtcNow;
        var oldProcessedA = CreateOutboxMessage("PosSale", Guid.NewGuid());
        oldProcessedA.ProcessedAtUtc = nowUtc.AddDays(-20);
        oldProcessedA.CreatedAt = nowUtc.AddDays(-21);

        var oldProcessedB = CreateOutboxMessage("PosSale", Guid.NewGuid());
        oldProcessedB.ProcessedAtUtc = nowUtc.AddDays(-19);
        oldProcessedB.CreatedAt = nowUtc.AddDays(-20);

        var oldFailedA = CreateOutboxMessage("PosSale", Guid.NewGuid());
        oldFailedA.IsFailed = true;
        oldFailedA.AttemptCount = 5;
        oldFailedA.LastError = "terminal";
        oldFailedA.CreatedAt = nowUtc.AddDays(-40);
        oldFailedA.UpdatedAt = nowUtc.AddDays(-35);

        var oldFailedB = CreateOutboxMessage("NonFinancialProjection", Guid.NewGuid());
        oldFailedB.IsFailed = true;
        oldFailedB.AttemptCount = 5;
        oldFailedB.LastError = "terminal";
        oldFailedB.CreatedAt = nowUtc.AddDays(-39);
        oldFailedB.UpdatedAt = nowUtc.AddDays(-34);

        var recentProcessed = CreateOutboxMessage("PosSale", Guid.NewGuid());
        recentProcessed.ProcessedAtUtc = nowUtc.AddDays(-1);
        recentProcessed.CreatedAt = nowUtc.AddDays(-2);

        var recentFailed = CreateOutboxMessage("NonFinancialProjection", Guid.NewGuid());
        recentFailed.IsFailed = true;
        recentFailed.AttemptCount = 3;
        recentFailed.LastError = "recent";
        recentFailed.CreatedAt = nowUtc.AddDays(-2);
        recentFailed.UpdatedAt = nowUtc.AddDays(-1);

        var pending = CreateOutboxMessage("PosRefund", Guid.NewGuid());
        pending.CreatedAt = nowUtc.AddHours(-1);

        _db.AccountingOutboxMessages.AddRange(
            oldProcessedA,
            oldProcessedB,
            oldFailedA,
            oldFailedB,
            recentProcessed,
            recentFailed,
            pending);
        await _db.SaveChangesAsync();

        var processor = new AccountingOutboxProcessor(
            _serviceProvider,
            NullLogger<AccountingOutboxProcessor>.Instance,
            Options.Create(new AccountingOutboxOptions
            {
                ProcessedRetentionDays = 14,
                FailedRetentionDays = 30,
                CleanupBatchSize = 1,
                CleanupMaxBatchesPerRun = 1
            }));

        var cleanupResult = await processor.CleanupAsync();

        Assert.Equal(1, cleanupResult.ProcessedDeleted);
        Assert.Equal(1, cleanupResult.FailedDeleted);

        var remainingMessages = await _db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .ToListAsync();

        Assert.Equal(5, remainingMessages.Count);
        Assert.Contains(remainingMessages, message => message.Id == oldFailedA.Id);
        Assert.DoesNotContain(remainingMessages, message => message.Id == oldFailedB.Id);
        Assert.Contains(remainingMessages, message => message.Id == recentProcessed.Id);
        Assert.Contains(remainingMessages, message => message.Id == recentFailed.Id);
        Assert.Contains(remainingMessages, message => message.Id == pending.Id);
        Assert.True(remainingMessages.Count(message => message.ProcessedAtUtc != null && message.ProcessedAtUtc < nowUtc.AddDays(-14)) == 1);
        Assert.True(remainingMessages.Count(message => message.IsFailed && (message.UpdatedAt ?? message.CreatedAt) < nowUtc.AddDays(-30)) == 1);
    }

    [Fact]
    public async Task CleanupAsync_RetainsExpiredFailedCriticalFinancialTruthMessages()
    {
        var nowUtc = DateTime.UtcNow;
        var criticalMessages = AccountingOutboxProcessor.CriticalFinancialTruthEventTypes
            .Select(eventType =>
            {
                var message = CreateOutboxMessage(eventType, Guid.NewGuid());
                message.IsFailed = true;
                message.AttemptCount = 5;
                message.LastError = "terminal";
                message.CreatedAt = nowUtc.AddDays(-40);
                message.UpdatedAt = nowUtc.AddDays(-35);
                return message;
            })
            .ToList();

        var nonCriticalFailed = CreateOutboxMessage("NonFinancialProjection", Guid.NewGuid());
        nonCriticalFailed.IsFailed = true;
        nonCriticalFailed.AttemptCount = 5;
        nonCriticalFailed.LastError = "terminal";
        nonCriticalFailed.CreatedAt = nowUtc.AddDays(-40);
        nonCriticalFailed.UpdatedAt = nowUtc.AddDays(-35);

        _db.AccountingOutboxMessages.AddRange(criticalMessages);
        _db.AccountingOutboxMessages.Add(nonCriticalFailed);
        await _db.SaveChangesAsync();

        var processor = new AccountingOutboxProcessor(
            _serviceProvider,
            NullLogger<AccountingOutboxProcessor>.Instance,
            Options.Create(new AccountingOutboxOptions
            {
                FailedRetentionDays = 30,
                CleanupBatchSize = 500,
                CleanupMaxBatchesPerRun = 1
            }));

        var cleanupResult = await processor.CleanupAsync();

        Assert.Equal(1, cleanupResult.FailedDeleted);

        var remainingIds = await _db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Select(message => message.Id)
            .ToListAsync();

        Assert.DoesNotContain(nonCriticalFailed.Id, remainingIds);
        foreach (var criticalMessage in criticalMessages)
        {
            Assert.Contains(criticalMessage.Id, remainingIds);
        }
    }

    [Fact]
    public void CriticalFinancialTruthEventTypes_CoversEveryMoneyAffectingDispatchEvent()
    {
        var expectedCriticalEventTypes = new[]
        {
            "PosSale",
            "SaleReversal",
            "PosPurchase",
            "PurchaseReversal",
            "SupplierPayment",
            "CustomerPayment",
            "SupplierOpeningBalance",
            "PartyLedgerAdjustment",
            "PosRefund",
            "PurchaseReturn",
            "PosExpense",
            "ExpenseReversal",
            "OnlinePayment",
            "CodDelivered",
            "WalletApproved",
            "EcommerceRefund",
            "UnresolvedOnlinePayment",
            "ResolvedUnresolvedOnlinePayment",
            "GatewaySettlement",
            "CodSettlement",
            "CommerceHubLedgerPosting"
        };

        foreach (var eventType in expectedCriticalEventTypes)
        {
            Assert.Contains(eventType, AccountingOutboxProcessor.CriticalFinancialTruthEventTypes);
        }
    }

    [Fact]
    public async Task RequeueMessageAsync_NonTerminalDelayedMessage_ClearsRetryState()
    {
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "CommerceHubLedgerPosting",
            Guid.NewGuid(),
            "CommerceHubFinancialTransaction",
            "{}",
            $"COMMERCEHUB_TX:{Guid.NewGuid():N}");
        message.Id = Guid.NewGuid();
        message.RecordNonTerminalFinancialFailure("Old mapper rejected the posting.");
        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var requeued = await _processor.RequeueMessageAsync(message.Id);

        Assert.True(requeued);
        _db.ChangeTracker.Clear();
        var stored = await _db.AccountingOutboxMessages.SingleAsync(entry => entry.Id == message.Id);
        Assert.False(stored.IsFailed);
        Assert.Equal(0, stored.AttemptCount);
        Assert.Null(stored.ClaimedUntilUtc);
        Assert.Null(stored.LastError);
        Assert.Null(stored.NextRetryAtUtc);
        Assert.NotNull(stored.UpdatedAt);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_BeforePayoutVendorRefundMirror_MarksProcessedWithoutPostingDuplicateJournal()
    {
        var refundId = Guid.NewGuid();
        var platformAdjustment = CreateCommerceHubTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Adjustment,
            CommerceHubFinancialBucket.Payable,
            refundId);
        var vendorMirror = CreateCommerceHubTransaction(
            CommerceHubFinancialEntityType.Vendor,
            _tenantId,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialBucket.Payable,
            refundId);
        var message = CreateCommerceHubLedgerPostingMessage(vendorMirror);
        message.RecordNonTerminalFinancialFailure("Legacy duplicate payable posting failed.");
        message.ResetForRetry();

        _db.CommerceHubFinancialTransactions.AddRange(platformAdjustment, vendorMirror);
        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        _db.ChangeTracker.Clear();
        var stored = await _db.AccountingOutboxMessages.SingleAsync(entry => entry.Id == message.Id);
        Assert.NotNull(stored.ProcessedAtUtc);
        Assert.Equal(Guid.Empty, stored.JournalId);
        Assert.False(stored.IsFailed);
        Assert.Null(stored.LastError);
        Assert.Null(stored.NextRetryAtUtc);
        Assert.Empty(await _db.CommerceAccountingAudits.ToListAsync());
        _commerceAccounting.Verify(accounting => accounting.PostCommerceHubFinancialTransactionAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_OrphanVendorRefundPayable_RemainsRetryable()
    {
        var vendorMirror = CreateCommerceHubTransaction(
            CommerceHubFinancialEntityType.Vendor,
            _tenantId,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialBucket.Payable,
            Guid.NewGuid());
        var message = CreateCommerceHubLedgerPostingMessage(vendorMirror);

        _db.CommerceHubFinancialTransactions.Add(vendorMirror);
        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);
        _db.ChangeTracker.Clear();
        var stored = await _db.AccountingOutboxMessages.SingleAsync(entry => entry.Id == message.Id);
        Assert.Null(stored.ProcessedAtUtc);
        Assert.False(stored.IsFailed);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Contains("canonical platform payable adjustment", stored.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(stored.NextRetryAtUtc);
        _commerceAccounting.Verify(accounting => accounting.PostCommerceHubFinancialTransactionAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_VendorPayoutPayableMirror_MarksProcessedWithoutPostingDuplicateJournal()
    {
        var payoutId = Guid.NewGuid();
        var platformPayout = CreateCommerceHubTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Payout,
            CommerceHubFinancialBucket.Payable,
            CommerceHubFinancialReferenceType.Payout,
            payoutId);
        var vendorMirror = CreateCommerceHubTransaction(
            CommerceHubFinancialEntityType.Vendor,
            _tenantId,
            CommerceHubFinancialTransactionType.Payout,
            CommerceHubFinancialBucket.Payable,
            CommerceHubFinancialReferenceType.Payout,
            payoutId);
        var message = CreateCommerceHubLedgerPostingMessage(vendorMirror);

        _db.CommerceHubFinancialTransactions.AddRange(platformPayout, vendorMirror);
        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processedCount);
        _db.ChangeTracker.Clear();
        var stored = await _db.AccountingOutboxMessages.SingleAsync(entry => entry.Id == message.Id);
        Assert.NotNull(stored.ProcessedAtUtc);
        Assert.Equal(Guid.Empty, stored.JournalId);
        Assert.Empty(await _db.CommerceAccountingAudits.ToListAsync());
        _commerceAccounting.Verify(accounting => accounting.PostCommerceHubFinancialTransactionAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_OrphanVendorPayoutPayable_RemainsRetryable()
    {
        var vendorMirror = CreateCommerceHubTransaction(
            CommerceHubFinancialEntityType.Vendor,
            _tenantId,
            CommerceHubFinancialTransactionType.Payout,
            CommerceHubFinancialBucket.Payable,
            CommerceHubFinancialReferenceType.Payout,
            Guid.NewGuid());
        var message = CreateCommerceHubLedgerPostingMessage(vendorMirror);

        _db.CommerceHubFinancialTransactions.Add(vendorMirror);
        _db.AccountingOutboxMessages.Add(message);
        await _db.SaveChangesAsync();

        var processedCount = await _processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processedCount);
        _db.ChangeTracker.Clear();
        var stored = await _db.AccountingOutboxMessages.SingleAsync(entry => entry.Id == message.Id);
        Assert.Null(stored.ProcessedAtUtc);
        Assert.False(stored.IsFailed);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Contains("canonical platform payout liability", stored.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(stored.NextRetryAtUtc);
        _commerceAccounting.Verify(accounting => accounting.PostCommerceHubFinancialTransactionAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private CommerceHubFinancialTransaction CreateCommerceHubTransaction(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialBucket bucket,
        Guid referenceId) =>
        CreateCommerceHubTransaction(
            entityType,
            entityId,
            transactionType,
            bucket,
            CommerceHubFinancialReferenceType.Refund,
            referenceId);

    private CommerceHubFinancialTransaction CreateCommerceHubTransaction(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialBucket bucket,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId) =>
        CommerceHubFinancialTransaction.Create(
            entityType,
            entityId,
            transactionType,
            CommerceHubFinancialTransactionDirection.Debit,
            90m,
            "EGP",
            referenceType,
            referenceId,
            "CommerceHub operational mirror test transaction.",
            financialBucketOverride: bucket);

    private AccountingOutboxMessage CreateCommerceHubLedgerPostingMessage(CommerceHubFinancialTransaction transaction)
    {
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "CommerceHubLedgerPosting",
            transaction.Id,
            nameof(CommerceHubFinancialTransaction),
            JsonSerializer.Serialize(new
            {
                TransactionId = transaction.Id,
                TransactionType = transaction.TransactionType.ToString(),
                Direction = transaction.Direction.ToString(),
                EntityType = transaction.EntityType.ToString(),
                FinancialBucket = transaction.FinancialBucket.ToString(),
                transaction.Amount,
                transaction.Currency,
                transaction.Description,
                OccurredAtUtc = transaction.CreatedAtUtc
            }),
            $"COMMERCEHUB_TX:{_tenantId:N}:{transaction.Id:N}",
            sourceType: nameof(CommerceHubFinancialTransaction));
        message.Id = Guid.NewGuid();
        return message;
    }

    private SalesInvoice SeedInvoice(
        PosPaymentMethod paymentMethod = PosPaymentMethod.Cash,
        decimal paidAmount = 100m,
        decimal? cashAmount = null,
        decimal? cardAmount = null,
        int accountingMutationVersion = 0)
    {
        var warehouseId = Guid.NewGuid();
        _db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = _tenantId,
            Name = $"Warehouse {warehouseId:N}"[..20],
            Code = $"WH-{warehouseId:N}"[..12],
            IsActive = true
        });

        var invoice = new SalesInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = "INV-20260408-1000",
            InvoiceDate = DateTime.UtcNow,
            WarehouseId = warehouseId,
            PaymentMethod = paymentMethod,
            TotalAmount = 100m,
            PaidAmount = paidAmount,
            CashAmount = cashAmount,
            CardAmount = cardAmount,
            AccountingMutationVersion = accountingMutationVersion
        };

        _db.SalesInvoices.Add(invoice);
        return invoice;
    }

    private Return SeedReturn(SalesInvoice invoice, decimal totalAmount)
    {
        var returnObj = new Return
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ReturnNumber = "SR-20260410-1001",
            ReturnDate = DateTime.UtcNow,
            ReturnType = ReturnType.SalesReturn,
            OriginalInvoiceId = invoice.Id,
            OriginalInvoiceNumber = invoice.InvoiceNumber,
            WarehouseId = invoice.WarehouseId,
            TotalAmount = totalAmount
        };

        _db.Returns.Add(returnObj);
        return returnObj;
    }

    private Order SeedOrder(PaymentStatus paymentStatus)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..12],
            Status = paymentStatus is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded
                ? OrderStatus.Refunded
                : OrderStatus.Pending,
            FulfillmentStatus = FulfillmentStatus.Delivered,
            PaymentStatus = paymentStatus,
            Total = 100m,
            Currency = "EGP",
            PaymentMethodCode = "STRIPE",
            PaymentGateway = "stripe",
            PaidAt = DateTime.UtcNow
        };

        _db.Orders.Add(order);
        return order;
    }

    private AccountLedger SeedSupplierOpeningBalance(Guid supplierId, decimal creditAmount)
    {
        if (!_db.Suppliers.Local.Any(supplier => supplier.Id == supplierId))
        {
            _db.Suppliers.Add(new Supplier
            {
                Id = supplierId,
                TenantId = _tenantId,
                Name = $"Supplier {supplierId:N}"[..20]
            });
        }

        var openingBalance = new AccountLedger
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            PartyType = PartyType.Supplier,
            PartyId = supplierId,
            TransactionDate = DateTime.UtcNow,
            TransactionType = TransactionType.OpeningBalance,
            DocumentNumber = $"OB-SUP-{Guid.NewGuid():N}"[..15],
            DebitAmount = 0m,
            CreditAmount = creditAmount,
            RunningBalance = -creditAmount,
            CreatedAt = DateTime.UtcNow
        };

        _db.AccountLedgers.Add(openingBalance);
        return openingBalance;
    }

    private JournalEntry CreateJournal(
        string journalNumber,
        string sourceType,
        Guid sourceId,
        string? sourceReference,
        string idempotencyKey)
    {
        var journal = JournalEntry.Create(
            _tenantId,
            journalNumber,
            DateTime.UtcNow,
            sourceType,
            sourceId,
            sourceReference,
            journalNumber,
            idempotencyKey,
            null);
        journal.Id = Guid.NewGuid();
        return journal;
    }

    private AccountingOutboxMessage CreateOutboxMessage(string eventType, Guid businessEntityId)
    {
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            eventType,
            businessEntityId,
            typeof(object).FullName ?? nameof(Object),
            "{}",
            $"{eventType}:{_tenantId}:{businessEntityId}");
        message.Id = Guid.NewGuid();
        return message;
    }
}
