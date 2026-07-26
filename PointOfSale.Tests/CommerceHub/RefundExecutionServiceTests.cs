using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.TestDoubles;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.CommerceHub;

public sealed class RefundExecutionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IEventPublisher> _publishEndpoint = new();
    private readonly ICommerceHubFinancialLedgerService _financialLedgerService;
    private readonly ICommerceHubFinancialAuditService _financialAuditService;
    private readonly ICommerceHubVendorDebtService _vendorDebtService;
    private readonly ICommerceHubWalletService _walletService;
    private readonly TestLogger<RefundExecutionService> _logger = new();
    private readonly RefundExecutionService _service;

    public RefundExecutionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _financialLedgerService = new CommerceHubFinancialLedgerService(
            _dbContext,
            Mock.Of<ILogger<CommerceHubFinancialLedgerService>>());
        _financialAuditService = new CommerceHubFinancialAuditService(_dbContext);
        _vendorDebtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        _walletService = new CommerceHubWalletService(_dbContext);
        var financialSafetyGuardService = new CommerceHubFinancialSafetyGuardService(_dbContext);
        var financialReadinessGate = new CommerceHubFinancialReadinessGate(
            _dbContext,
            Mock.Of<IOptionsMonitor<CommerceHubEmergencyStopOptions>>(options =>
                options.CurrentValue == new CommerceHubEmergencyStopOptions()),
            Mock.Of<ILogger<CommerceHubFinancialReadinessGate>>());
        _service = new RefundExecutionService(
            _dbContext,
            _financialLedgerService,
            _financialAuditService,
            _vendorDebtService,
            _walletService,
            financialSafetyGuardService,
            financialReadinessGate,
            new NoOpUserGovernanceService(),
            new RefundCompletionValidator(_dbContext),
            _publishEndpoint.Object,
            _logger);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPersistManualExecution_AndPreventDuplicateRows()
    {
        var graph = await SeedRefundRequestAsync(
            CommerceHubRefundRequestStatus.Approved,
            RefundPayoutMethod.VodafoneCash,
            "ENC:test-account",
            "010****678");
        await _financialLedgerService.RecordOrderAsync(graph.Order, graph.SubOrder);
        await SeedPostedCommerceHubJournalsAsync(graph.SubOrder.StoreId);
        await SeedHealthyFinancialReconciliationAsync(graph.SubOrder.StoreId);
        var actorId = Guid.NewGuid();

        var request = new SuperAdminExecuteRefundRequest
        {
            Method = RefundPayoutMethod.VodafoneCash,
            TransactionReference = "TXN123456",
            Notes = "Sent via Vodafone Cash",
            ConfirmMoneySent = true
        };

        await _service.ExecuteAsync(
            graph.RefundRequest.Id,
            request,
            actorId,
            "owner@commercehub.local",
            "corr-execute");

        await _service.ExecuteAsync(
            graph.RefundRequest.Id,
            request,
            actorId,
            "owner@commercehub.local",
            "corr-execute-duplicate");

        var refundRequest = await _dbContext.CommerceHubRefundRequests.SingleAsync();
        var execution = await _dbContext.CommerceHubRefundExecutions.SingleAsync();
        var trackedRefund = await _dbContext.CommerceHubRefunds.SingleAsync();
        var auditActions = await _dbContext.AuditLogs
            .Select(item => item.ActionType)
            .ToListAsync();

        Assert.Equal(CommerceHubRefundRequestStatus.Paid, refundRequest.Status);
        Assert.Equal(1, refundRequest.ExecutionAttempts);
        Assert.NotNull(refundRequest.CompletedAtUtc);

        Assert.Equal(graph.RefundRequest.Id, execution.RefundId);
        Assert.Equal(RefundPayoutMethod.VodafoneCash, execution.Method);
        Assert.Equal("TXN123456", execution.TransactionReference);
        Assert.Equal("Sent via Vodafone Cash", execution.Notes);
        Assert.True(execution.IsSuccessful);
        Assert.Null(execution.FailureReason);
        Assert.Equal(actorId, execution.ExecutedBy);

        Assert.Equal(CommerceHubRefundStatus.Completed, trackedRefund.Status);
        Assert.Equal("TXN123456", trackedRefund.Reference);
        Assert.Equal("manual-confirmed", trackedRefund.GatewayStatus);

        Assert.Equal(1, await _dbContext.CommerceHubRefundExecutions.CountAsync());
        Assert.Equal(1, await _dbContext.CommerceHubRefunds.CountAsync());
        Assert.Contains("REFUND_PAYOUT_INITIATED", auditActions);
        Assert.Contains("REFUND_PAYOUT_CONFIRMED", auditActions);
        Assert.DoesNotContain("REFUND_PAYOUT_ALREADY_CONFIRMED", auditActions);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWriteRefundLedgerEntries()
    {
        var graph = await SeedRefundRequestAsync(
            CommerceHubRefundRequestStatus.Approved,
            RefundPayoutMethod.VodafoneCash,
            "ENC:test-account",
            "010****678");
        await _financialLedgerService.RecordOrderAsync(graph.Order, graph.SubOrder);
        await SeedPostedCommerceHubJournalsAsync(graph.SubOrder.StoreId);
        await SeedHealthyFinancialReconciliationAsync(graph.SubOrder.StoreId);

        await _service.ExecuteAsync(
            graph.RefundRequest.Id,
            new SuperAdminExecuteRefundRequest
            {
                Method = RefundPayoutMethod.VodafoneCash,
                TransactionReference = "TXN-LEDGER-001",
                Notes = "Ledger integration test",
                ConfirmMoneySent = true
            },
            Guid.NewGuid(),
            "owner@commercehub.local",
            "corr-ledger");

        var trackedRefund = await _dbContext.CommerceHubRefunds.SingleAsync();
        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(item => item.ReferenceId == trackedRefund.Id)
            .OrderBy(item => item.TransactionType)
            .ToListAsync();
        var operationAudit = await _dbContext.CommerceHubFinancialOperationAudits
            .SingleAsync(item => item.ReferenceId == trackedRefund.Id);

        Assert.Equal(2, ledgerEntries.Count);
        Assert.Contains(ledgerEntries, item =>
            item.EntityType == CommerceHubFinancialEntityType.Vendor &&
            item.EntityId == graph.SubOrder.StoreId &&
            item.TransactionType == CommerceHubFinancialTransactionType.Commission &&
            item.Direction == CommerceHubFinancialTransactionDirection.Credit &&
            item.Amount == 10m &&
            item.FinancialBucket == CommerceHubFinancialBucket.Receivable);
        Assert.Contains(ledgerEntries, item =>
            item.EntityType == CommerceHubFinancialEntityType.Platform &&
            item.TransactionType == CommerceHubFinancialTransactionType.Commission &&
            item.Direction == CommerceHubFinancialTransactionDirection.Debit &&
            item.Amount == 10m &&
            item.FinancialBucket == CommerceHubFinancialBucket.Receivable);
        Assert.Equal(0m, operationAudit.BeforePlatformCash);
        Assert.Equal(20m, operationAudit.BeforeVendorDebt);
        Assert.Equal(10m, operationAudit.AfterVendorDebt);
        Assert.All(ledgerEntries, item => Assert.Equal(trackedRefund.Id.ToString("D"), item.CorrelationId));
    }

    [Fact]
    public async Task RecoverReconciliationRequiredRefundsAsync_ShouldCompleteLocalRecognition_FromPersistedExecutionEvidence()
    {
        var graph = await SeedRefundRequestAsync(
            CommerceHubRefundRequestStatus.Processing,
            RefundPayoutMethod.VodafoneCash,
            "ENC:test-account",
            "010****678");
        await _financialLedgerService.RecordOrderAsync(graph.Order, graph.SubOrder);
        await SeedPostedCommerceHubJournalsAsync(graph.SubOrder.StoreId);
        await SeedHealthyFinancialReconciliationAsync(graph.SubOrder.StoreId);

        var trackedRefund = new CommerceHubRefund
        {
            Id = Guid.NewGuid(),
            TenantId = graph.SubOrder.StoreId,
            SubOrderId = graph.SubOrder.Id,
            RefundRequestId = graph.RefundRequest.Id,
            OrderItemId = graph.Item.Id,
            Quantity = graph.RefundRequest.Quantity,
            Amount = graph.RefundRequest.TotalAmount,
            CommissionReversalAmount = 10m,
            PromoDiscountReversalAmount = 0m,
            VendorEarningReversalAmount = 0m,
            Currency = graph.Order.Currency,
            Status = CommerceHubRefundStatus.ReconciliationRequired,
            Reason = graph.RefundRequest.Reason,
            Reference = "TXN-RECOVER-001",
            GatewayStatus = "manual_transfer_reconciliation_required",
            FailureReason = "External manual refund transfer was confirmed by the operator, but POS could not complete local financial recognition. Reconciliation required.",
            IdempotencyKey = $"refund-exec-{graph.RefundRequest.Id:N}",
            IsFullRefund = false,
            IsPostPayoutAdjustment = false,
            ProcessingStartedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            CreatedBy = Guid.NewGuid()
        };

        var execution = new CommerceHubRefundExecution
        {
            Id = Guid.NewGuid(),
            RefundId = graph.RefundRequest.Id,
            Method = RefundPayoutMethod.VodafoneCash,
            AccountSnapshot = "ENC:test-account",
            TransactionReference = "TXN-RECOVER-001",
            Amount = graph.RefundRequest.TotalAmount,
            Currency = graph.Order.Currency,
            ExecutedBy = Guid.NewGuid(),
            ExecutedAt = DateTime.UtcNow.AddMinutes(-10),
            Notes = "Recover persisted evidence",
            IsSuccessful = false,
            FailureReason = trackedRefund.FailureReason,
            CreatedBy = Guid.NewGuid()
        };

        _dbContext.CommerceHubRefunds.Add(trackedRefund);
        _dbContext.CommerceHubRefundExecutions.Add(execution);
        await SaveCommerceHubFixtureAsync();

        var recovered = await _service.RecoverReconciliationRequiredRefundsAsync();

        Assert.True(
            recovered == 1,
            string.Join(Environment.NewLine, _logger.Entries.Select(entry => $"{entry.LogLevel}: {entry.Message}")));

        var refreshedRequest = await _dbContext.CommerceHubRefundRequests.SingleAsync(item => item.Id == graph.RefundRequest.Id);
        var refreshedRefund = await _dbContext.CommerceHubRefunds.SingleAsync(item => item.Id == trackedRefund.Id);
        var refreshedExecution = await _dbContext.CommerceHubRefundExecutions.SingleAsync(item => item.Id == execution.Id);

        Assert.Equal(CommerceHubRefundRequestStatus.Paid, refreshedRequest.Status);
        Assert.Equal(CommerceHubRefundStatus.Completed, refreshedRefund.Status);
        Assert.Equal("manual-confirmed", refreshedRefund.GatewayStatus);
        Assert.True(refreshedExecution.IsSuccessful);
        Assert.Null(refreshedExecution.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectMethodOverrideWithoutConfirmation()
    {
        var graph = await SeedRefundRequestAsync(
            CommerceHubRefundRequestStatus.Approved,
            RefundPayoutMethod.VodafoneCash,
            "ENC:test-account",
            "010****678");
        await _financialLedgerService.RecordOrderAsync(graph.Order, graph.SubOrder);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.ExecuteAsync(
                graph.RefundRequest.Id,
                new SuperAdminExecuteRefundRequest
                {
                    Method = RefundPayoutMethod.BankTransfer,
                    TransactionReference = "TXN-BANK-1",
                    Notes = "Attempted override without confirmation",
                    ConfirmMoneySent = true,
                    ConfirmMethodOverride = false
                },
                Guid.NewGuid(),
                "owner@commercehub.local",
                "corr-method-mismatch"));

        Assert.Equal("REFUND_PAYOUT_METHOD_OVERRIDE_CONFIRMATION_REQUIRED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal(CommerceHubRefundRequestStatus.Approved, (await _dbContext.CommerceHubRefundRequests.SingleAsync()).Status);
        Assert.Equal(0, await _dbContext.CommerceHubRefundExecutions.CountAsync());
        Assert.Equal(0, await _dbContext.CommerceHubRefunds.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectWhenPayoutSnapshotIsMissing()
    {
        var graph = await SeedRefundRequestAsync(
            CommerceHubRefundRequestStatus.Approved,
            null,
            null,
            null);
        await _financialLedgerService.RecordOrderAsync(graph.Order, graph.SubOrder);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.ExecuteAsync(
                graph.RefundRequest.Id,
                new SuperAdminExecuteRefundRequest
                {
                    Method = RefundPayoutMethod.VodafoneCash,
                    TransactionReference = "TXN-NO-SNAPSHOT",
                    ConfirmMoneySent = true
                },
                Guid.NewGuid(),
                "owner@commercehub.local",
                "corr-no-snapshot"));

        Assert.Equal("REFUND_PAYOUT_SNAPSHOT_REQUIRED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal(CommerceHubRefundRequestStatus.Approved, (await _dbContext.CommerceHubRefundRequests.SingleAsync()).Status);
        Assert.Equal(0, await _dbContext.CommerceHubRefundExecutions.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectWhenReturnTriggerHasNotBeenReached()
    {
        var graph = await SeedRefundRequestAsync(
            CommerceHubRefundRequestStatus.Approved,
            RefundPayoutMethod.VodafoneCash,
            "ENC:test-account",
            "010****678");
        await _financialLedgerService.RecordOrderAsync(graph.Order, graph.SubOrder);

        var returnRequest = new CommerceHubReturnRequest
        {
            Id = Guid.NewGuid(),
            RefundId = graph.RefundRequest.Id,
            Refund = graph.RefundRequest,
            OrderItemId = graph.Item.Id,
            OrderItem = graph.Item,
            CustomerId = graph.Customer.Id,
            Customer = graph.Customer,
            VendorId = graph.SubOrder.StoreId,
            Status = CommerceHubReturnStatus.PickupScheduled
        };

        graph.RefundRequest.ReturnRequestId = returnRequest.Id;
        graph.RefundRequest.RefundExecutionTrigger = RefundExecutionTrigger.OnVendorConfirmation;
        _dbContext.CommerceHubReturnRequests.Add(returnRequest);
        await SaveCommerceHubFixtureAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.ExecuteAsync(
                graph.RefundRequest.Id,
                new SuperAdminExecuteRefundRequest
                {
                    Method = RefundPayoutMethod.VodafoneCash,
                    TransactionReference = "TXN-BLOCKED-BY-RETURN",
                    ConfirmMoneySent = true
                },
                Guid.NewGuid(),
                "owner@commercehub.local",
                "corr-return-trigger"));

        Assert.Equal("REFUND_RETURN_TRIGGER_NOT_REACHED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal(0, await _dbContext.CommerceHubRefundExecutions.CountAsync());
        Assert.Equal(0, await _dbContext.CommerceHubRefunds.CountAsync());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<RefundGraph> SeedRefundRequestAsync(
        CommerceHubRefundRequestStatus status,
        RefundPayoutMethod? payoutMethod,
        string? payoutAccountSnapshot,
        string? payoutMaskedSnapshot)
    {
        var customer = CommerceHubCustomer.Create("refund-exec@example.com", "Refund Exec Test", "01011111111", "hash");

        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..12],
            FullName = "Refund Exec Test",
            PhoneNumber = "01011111111",
            AddressLine = "123 Refund Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            Subtotal = 400m,
            ShippingTotal = 20m,
            TotalAmount = 420m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Collected
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"SUB-{Guid.NewGuid():N}"[..12],
            StoreId = Guid.NewGuid(),
            StoreName = "Vendor Refund Execution",
            Currency = "EGP",
            Subtotal = 400m,
            Shipping = 20m,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            PlatformCommissionAmount = 20m,
            VendorEarningAmount = 400m,
            PlatformCommissionRate = 0.05m,
            Total = 420m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Collected
        };

        var item = new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Refund Execution Product",
            VariantName = "Black / Medium",
            Quantity = 2,
            UnitPrice = 200m,
            PlatformCommissionAmount = 20m,
            Total = 400m,
            Status = CommerceHubOrderItemStatus.Delivered,
            DeliveredAt = DateTime.UtcNow.AddDays(-1)
        };

        var refundRequest = new CommerceHubRefundRequest
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            OrderItemId = item.Id,
            OrderItem = item,
            CustomerId = customer.Id,
            Customer = customer,
            VendorId = subOrder.StoreId,
            Quantity = 1,
            UnitPriceSnapshot = 200m,
            TotalAmount = 200m,
            Reason = "Damaged package",
            ReasonCode = RefundReasonCode.DamagedItem,
            Status = status,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            IdempotencyKey = $"refund-request-{Guid.NewGuid():N}",
            PayoutMethodSnapshot = payoutMethod,
            PayoutAccountSnapshot = payoutAccountSnapshot,
            PayoutAccountMaskedSnapshot = payoutMaskedSnapshot,
            PayoutAccountHolderNameSnapshot = payoutMaskedSnapshot == null ? null : "Refund Exec Test"
        };

        order.SubOrders.Add(subOrder);
        subOrder.Items.Add(item);

        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubRefundRequests.Add(refundRequest);
        await SaveCommerceHubFixtureAsync();

        return new RefundGraph(customer, order, subOrder, item, refundRequest);
    }

    private async Task SaveCommerceHubFixtureAsync()
    {
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginRefund())
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task SeedPostedCommerceHubJournalsAsync(Guid vendorId)
    {
        var uncoveredTransactions = await _dbContext.CommerceHubFinancialTransactions
            .Where(transaction =>
                ((transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                  transaction.EntityId == vendorId) ||
                 (transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                  (_dbContext.AccountingOutboxMessages.Any(message =>
                       message.TenantId == vendorId &&
                       message.BusinessEntityId == transaction.Id &&
                       message.EventType == "CommerceHubLedgerPosting") ||
                   _dbContext.CommerceHubFinancialTransactions.Any(vendorTransaction =>
                       vendorTransaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                       vendorTransaction.EntityId == vendorId &&
                       vendorTransaction.ReferenceType == transaction.ReferenceType &&
                       vendorTransaction.ReferenceId == transaction.ReferenceId &&
                       vendorTransaction.Currency == transaction.Currency)))) &&
                !_dbContext.JournalEntries.Any(journal =>
                    journal.TenantId == vendorId &&
                    journal.SourceType == "CommerceHubFinancialTransaction" &&
                    journal.SourceId == transaction.Id &&
                    journal.Status == JournalStatus.Posted))
            .ToListAsync();

        foreach (var transaction in uncoveredTransactions)
        {
            var journal = JournalEntry.Create(
                vendorId,
                $"JE-CH-{Guid.NewGuid():N}"[..18],
                DateTime.UtcNow,
                "CommerceHubFinancialTransaction",
                transaction.Id,
                transaction.ReferenceId.ToString("D"),
                $"GL coverage for CommerceHub transaction {transaction.Id:D}",
                $"COMMERCEHUB_LEDGER_POSTING:{vendorId:D}:{transaction.Id:D}",
                null);
            
            _dbContext.JournalEntries.Add(journal);

            // Also mark the corresponding CommerceHubLedgerPosting outbox message as processed
            // to mirror production readiness gating
            var outboxMessage = await _dbContext.AccountingOutboxMessages
                .FirstOrDefaultAsync(message =>
                    message.TenantId == vendorId &&
                    message.EntityTypeName == "CommerceHubFinancialTransaction" &&
                    message.BusinessEntityId == transaction.Id &&
                    message.EventType == "CommerceHubLedgerPosting" &&
                    message.SourceType == "CommerceHubFinancialTransaction");

            if (outboxMessage == null)
            {
                throw new InvalidOperationException(
                    $"Missing CommerceHubLedgerPosting outbox message for transaction {transaction.Id:D}. " +
                    "RecordOrderAsync may have failed to create the expected outbox entry.");
            }

            outboxMessage.MarkProcessed(journal.Id);
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedHealthyFinancialReconciliationAsync(Guid vendorId)
    {
        _dbContext.CommerceHubFinancialReconciliationReports.Add(new CommerceHubFinancialReconciliationReport
        {
            Id = Guid.NewGuid(),
            TimestampUtc = DateTime.UtcNow,
            Status = "Healthy",
            MismatchCount = 0,
            DetailsJson = $$"""{"checkedVendorIds":["{{vendorId:D}}"],"affectedVendorIds":[]}"""
        });

        await _dbContext.SaveChangesAsync();
    }

    private sealed record RefundGraph(
        CommerceHubCustomer Customer,
        CommerceHubMarketplaceOrder Order,
        CommerceHubSubOrder SubOrder,
        CommerceHubSubOrderItem Item,
        CommerceHubRefundRequest RefundRequest);

    private sealed record LogEntry(
        LogLevel LogLevel,
        string Message);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (exception != null)
            {
                message = $"{message}{Environment.NewLine}{exception}";
            }

            Entries.Add(new LogEntry(logLevel, message));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
