using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.Governance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Services;
using Microsoft.Extensions.Options;
using PointOfSale.Tests.TestDoubles;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.Tests.CommerceHub;

public class CommerceHubFinanceServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubFinancialLedgerService _financialLedgerService;
    private readonly CommerceHubWalletService _walletService;
    private readonly CommerceHubFinancialAuditService _auditService;
    private readonly CommerceHubPayoutService _payoutService;
    private readonly CommerceHubRefundService _refundService;
    private readonly CommerceHubFinanceReconciliationJob _reconciliationJob;
    private readonly Mock<ICommerceHubRefundGatewayService> _refundGatewayMock;
    private readonly Mock<IDistributedLockService> _lockServiceMock;
    private readonly Mock<IEventPublisher> _publishEndpointMock;
    private readonly Mock<IAccountingOutboxService> _accountingOutboxMock;
    private readonly Guid _tenantId = Guid.NewGuid();

    public CommerceHubFinanceServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _financialLedgerService = new CommerceHubFinancialLedgerService(
            _dbContext,
            NullLogger<CommerceHubFinancialLedgerService>.Instance);
        _walletService = new CommerceHubWalletService(_dbContext);
        _auditService = new CommerceHubFinancialAuditService(_dbContext);
        var financialSafetyGuardService = new CommerceHubFinancialSafetyGuardService(_dbContext);
        var financialReadinessGate = new CommerceHubFinancialReadinessGate(
            _dbContext,
            EmergencyStopOptionsMonitor(),
            NullLogger<CommerceHubFinancialReadinessGate>.Instance);
        _payoutService = CreatePayoutService(new NoOpUserGovernanceService());

        _refundGatewayMock = new Mock<ICommerceHubRefundGatewayService>();
        _refundGatewayMock
            .Setup(service => service.RefundAsync(
                It.IsAny<CommerceHubPayment?>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommerceHubPayment? _, decimal __, string ___, string ____, string idempotencyKey, CancellationToken _____) =>
                new CommerceHubGatewayRefundResult(true, true, "MockProvider", $"refund-{idempotencyKey}", "confirmed", null));

        _lockServiceMock = new Mock<IDistributedLockService>();
        _lockServiceMock
            .Setup(service => service.TryAcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(DistributedLockResult.Acquired());
        _lockServiceMock
            .Setup(service => service.ReleaseLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(true);

        _publishEndpointMock = new Mock<IEventPublisher>();
        _publishEndpointMock
            .Setup(endpoint => endpoint.PublishAsync(
                It.IsAny<CommerceHubMarketplaceOrderStatusUpdatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _publishEndpointMock
            .Setup(endpoint => endpoint.PublishAsync(
                It.IsAny<CommerceHubSubOrderStatusUpdatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _accountingOutboxMock = new Mock<IAccountingOutboxService>();
        _accountingOutboxMock.SetReturnsDefault(Task.CompletedTask);

        _refundService = new CommerceHubRefundService(
            _dbContext,
            _refundGatewayMock.Object,
            _financialLedgerService,
            _auditService,
            _publishEndpointMock.Object,
            new RefundCompletionValidator(_dbContext),
            financialSafetyGuardService,
            financialReadinessGate,
            _lockServiceMock.Object,
            Mock.Of<ILogger<CommerceHubRefundService>>());
        var vendorDebtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        var reconciliationReadService = new CommerceHubFinanceReconciliationReadService(_dbContext, vendorDebtService);
        _reconciliationJob = new CommerceHubFinanceReconciliationJob(
            _dbContext,
            _accountingOutboxMock.Object,
            new LegacyCompatibleFinancialReconciliationService(reconciliationReadService, _auditService),
            _publishEndpointMock.Object,
            Mock.Of<ILogger<CommerceHubFinanceReconciliationJob>>());
    }

    [Fact]
    public async Task MarketplaceOrderProjectionFlags_ShouldRemainMutable_WithoutBreakingImmutabilityGuard()
    {
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-CH-PROJECTION-1",
            FullName = "Projection Test",
            PhoneNumber = "01000000000",
            AddressLine = "123 Test Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "PAYMOB",
            PaymentMethodName = "Paymob",
            Subtotal = 100m,
            ShippingTotal = 10m,
            TotalAmount = 110m,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            IsProjected = false
        };

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            _dbContext.CommerceHubMarketplaceOrders.Add(order);
            await _dbContext.SaveChangesAsync();
        }

        Exception? exception;
        using (CommerceHubBoundaryMutationScope.BeginProjection())
        {
            order.IsProjected = true;
            order.ProjectedAtUtc = DateTime.UtcNow;

            exception = await Record.ExceptionAsync(() => _dbContext.SaveChangesAsync());
        }

        Assert.Null(exception);
        Assert.True(order.IsProjected);
        Assert.NotNull(order.ProjectedAtUtc);
    }

    [Fact]
    public async Task CreatePayoutAsync_ShouldReserveAvailableEarnings_AndBeIdempotent()
    {
        var first = await SeedOnlineFinancialGraphAsync(orderSuffix: "A");
        var second = await SeedOnlineFinancialGraphAsync(orderSuffix: "B");

        var request = new CommerceHubCreatePayoutRequestDto
        {
            EarningIds = new List<Guid> { first.PrimaryEarning.Id, second.PrimaryEarning.Id },
            Method = CommerceHubPayoutMethod.Manual,
            IdempotencyKey = "payout-idempotent-1"
        };

        var payout = await _payoutService.CreatePayoutAsync(_tenantId, request, "trace-1");
        var repeated = await _payoutService.CreatePayoutAsync(_tenantId, request, "trace-2");

        Assert.Equal(payout.PayoutId, repeated.PayoutId);
        Assert.Equal(180m, payout.Amount);
        Assert.Equal("Pending", payout.Status);

        var earnings = await _dbContext.CommerceHubVendorEarnings
            .Where(earning => request.EarningIds.Contains(earning.Id))
            .ToListAsync();

        Assert.All(earnings, earning =>
        {
            Assert.Equal(CommerceHubVendorEarningStatus.InPayout, earning.Status);
            Assert.Equal(payout.PayoutId, earning.PayoutId);
        });

        Assert.Equal(1, await _dbContext.CommerceHubPayouts.CountAsync());
    }

    [Fact]
    public async Task CreatePayoutAsync_WithSelectedPayoutMethod_ShouldPersistVendorBoundSnapshot()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "METHOD");
        var payoutMethod = new CommerceHubVendorPayoutMethod
        {
            Id = Guid.NewGuid(),
            VendorId = _tenantId,
            MethodType = CommerceHubPayoutMethod.BankTransfer,
            DestinationValue = "EG-METHOD-001",
            IsDefault = true,
            IsVerified = true
        };
        _dbContext.CommerceHubVendorPayoutMethods.Add(payoutMethod);
        await _dbContext.SaveChangesAsync();

        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = [graph.PrimaryEarning.Id],
                PayoutMethodId = payoutMethod.Id,
                Method = payoutMethod.MethodType,
                IdempotencyKey = "payout-method-snapshot-1"
            },
            "trace-method-snapshot");

        var persisted = await _dbContext.CommerceHubPayouts.SingleAsync(item => item.Id == payout.PayoutId);
        Assert.Equal(payoutMethod.Id, payout.PayoutMethodId);
        Assert.Equal(payoutMethod.DestinationValue, payout.DestinationValueSnapshot);
        Assert.Equal(payoutMethod.Id, persisted.PayoutMethodId);
        Assert.Equal(payoutMethod.DestinationValue, persisted.DestinationValueSnapshot);
    }

    [Fact]
    public async Task CreatePayoutAsync_WithForeignPayoutMethod_ShouldRejectBeforeReservation()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "FOREIGN-METHOD");
        var payoutMethod = new CommerceHubVendorPayoutMethod
        {
            Id = Guid.NewGuid(),
            VendorId = Guid.NewGuid(),
            MethodType = CommerceHubPayoutMethod.BankTransfer,
            DestinationValue = "EG-FOREIGN-001",
            IsDefault = true,
            IsVerified = true
        };
        _dbContext.CommerceHubVendorPayoutMethods.Add(payoutMethod);
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _payoutService.CreatePayoutAsync(
                _tenantId,
                new CommerceHubCreatePayoutRequestDto
                {
                    EarningIds = [graph.PrimaryEarning.Id],
                    PayoutMethodId = payoutMethod.Id,
                    Method = payoutMethod.MethodType,
                    IdempotencyKey = "payout-foreign-method-1"
                },
                "trace-foreign-method"));

        var earning = await _dbContext.CommerceHubVendorEarnings.SingleAsync(item => item.Id == graph.PrimaryEarning.Id);
        Assert.Equal("PAYOUT_METHOD_NOT_FOUND", exception.Code);
        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal(CommerceHubVendorEarningStatus.Available, earning.Status);
        Assert.Null(earning.PayoutId);
        Assert.Empty(_dbContext.CommerceHubPayouts);
    }

    [Fact]
    public async Task CreatePayoutAsync_WhenSelectedEarningHasNoOrderLedger_ShouldRejectBeforeReservation()
    {
        var graph = await SeedOnlineFinancialGraphAsync(
            orderSuffix: "NOLEDGER",
            recordOrderLedger: false);
        await SeedHealthyFinancialReconciliationAsync(_tenantId);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _payoutService.CreatePayoutAsync(
                _tenantId,
                new CommerceHubCreatePayoutRequestDto
                {
                    EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                    Method = CommerceHubPayoutMethod.Manual,
                    IdempotencyKey = "payout-missing-ledger-1"
                },
                "trace-missing-ledger"));

        var earning = await _dbContext.CommerceHubVendorEarnings.SingleAsync(item => item.Id == graph.PrimaryEarning.Id);

        Assert.Equal("PAYOUT_LEDGER_CONFIRMATION_REQUIRED", exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(CommerceHubVendorEarningStatus.Available, earning.Status);
        Assert.Null(earning.PayoutId);
        Assert.Equal(0, await _dbContext.CommerceHubPayouts.CountAsync());
    }

    [Fact]
    public async Task ResolveLinkedUnresolvedCapturedPaymentsAsync_ShouldQueueSuspenseClearance_AndReleaseMarketplaceHold()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "SUSPENSE");
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            graph.Order.Status = OrderStatus.OnHold;
            await _dbContext.SaveChangesAsync();
        }

        _dbContext.CommerceHubUnresolvedPaymentWebhooks.Add(new CommerceHubUnresolvedPaymentWebhook
        {
            Id = Guid.NewGuid(),
            Provider = graph.Payment.Provider,
            ProviderEventId = "evt-ch-suspense-1",
            ProviderReference = graph.Payment.ProviderReference,
            ProviderIntentId = graph.Payment.ProviderIntentId,
            ProviderTransactionId = graph.Payment.ProviderTransactionId,
            TenantId = _tenantId,
            OrderId = graph.Order.Id,
            AffectedStoreIdsJson = $$"""["{{_tenantId:D}}"]""",
            SuspenseRoutedStoreIdsJson = $$"""["{{_tenantId:D}}"]""",
            Amount = graph.Payment.Amount,
            Currency = graph.Payment.Currency,
            EventType = "payment.succeeded",
            PayloadHash = new string('b', 64),
            RawPayload = "{}",
            Status = "RoutedToSuspense",
            RoutedToSuspenseAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await _dbContext.SaveChangesAsync();

        var resolved = await _reconciliationJob.ResolveLinkedUnresolvedCapturedPaymentsAsync(CancellationToken.None);

        Assert.Equal(1, resolved);

        var webhook = await _dbContext.CommerceHubUnresolvedPaymentWebhooks.SingleAsync();
        var order = await _dbContext.CommerceHubMarketplaceOrders.SingleAsync(item => item.Id == graph.Order.Id);

        Assert.Equal("ResolvedToPayment", webhook.Status);
        Assert.Null(webhook.LastError);
        Assert.Equal(OrderStatus.Delivered, order.Status);

        _accountingOutboxMock.Verify(outbox => outbox.EnqueueResolvedUnresolvedCommerceHubPaymentAsync(
            _tenantId,
            graph.Payment.Id,
            graph.Payment.Amount,
            "paymob",
            "evt-ch-suspense-1",
            It.IsAny<CancellationToken>()),
            Times.Once);
        _publishEndpointMock.Verify(endpoint => endpoint.PublishAsync(
            It.IsAny<CommerceHubMarketplaceOrderStatusUpdatedEvent>(),
            It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CreatePayoutAsync_WhenUnscopedUnresolvedCapturedPaymentExists_ShouldBlockFailClosed()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "UNSCOPED-PAYOUT");
        await AddUnscopedUnresolvedPaymentWebhookAsync(
            status: "PendingTenantResolution",
            providerEventId: $"unscoped-capture-{Guid.NewGuid():N}",
            amount: graph.Payment.Amount,
            eventType: "payment_intent.succeeded");

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _payoutService.CreatePayoutAsync(
                _tenantId,
                new CommerceHubCreatePayoutRequestDto
                {
                    EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                    Method = CommerceHubPayoutMethod.Manual,
                    IdempotencyKey = "payout-unscoped-webhook-1"
                },
                "trace-unscoped-payout"));

        Assert.Equal("UNRESOLVED_CAPTURED_PAYMENT_NOT_RECONCILED", exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(0, await _dbContext.CommerceHubPayouts.CountAsync());
    }

    [Fact]
    public async Task FinancialReadinessGate_WhenPaymentCompensationHasNoTenantScope_ShouldBlockGlobally()
    {
        var tenantId = Guid.NewGuid();
        var gate = new CommerceHubFinancialReadinessGate(
            _dbContext,
            EmergencyStopOptionsMonitor(),
            NullLogger<CommerceHubFinancialReadinessGate>.Instance);

        _dbContext.CommerceHubPaymentCompensationOutboxMessages.Add(new CommerceHubPaymentCompensationOutboxMessage
        {
            Id = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            TenantId = null,
            AffectedStoreIdsJson = "[]",
            StoreAllocationSnapshotJson = "[]",
            Provider = PaymentProvider.Paymob,
            ProviderIntentId = "pi-scope-missing",
            ProviderTransactionId = "tx-scope-missing",
            Amount = 100m,
            Currency = "EGP",
            Reason = "Captured payment requires refund compensation.",
            IdempotencyKey = $"scope-missing-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow.AddMinutes(-20)
        });
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            gate.EnsureCommerceIntakeAllowedAsync(
                new[] { tenantId },
                "test-scope-missing-compensation"));

        Assert.Equal("PAYMENT_COMPENSATION_SCOPE_UNDETERMINED", exception.Code);
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task FinancialReadinessGate_FirstVendorIntakeMayBootstrapButOutwardMoneyStillRequiresVendorScope()
    {
        var establishedVendorId = Guid.NewGuid();
        var firstOrderVendorId = Guid.NewGuid();
        _dbContext.CommerceHubFinancialReconciliationReports.Add(new CommerceHubFinancialReconciliationReport
        {
            Id = Guid.NewGuid(),
            TimestampUtc = DateTime.UtcNow,
            Status = "Healthy",
            DetailsJson = $$"""{"checkedVendorIds":["{{establishedVendorId:D}}"],"affectedVendorIds":[]}"""
        });
        await _dbContext.SaveChangesAsync();

        var gate = new CommerceHubFinancialReadinessGate(
            _dbContext,
            EmergencyStopOptionsMonitor(),
            NullLogger<CommerceHubFinancialReadinessGate>.Instance);

        await gate.EnsureCommerceIntakeAllowedAsync(
            new[] { firstOrderVendorId },
            "CommerceHubCheckout.Create");

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            gate.EnsureOutwardMoneyMovementAllowedAsync(
                firstOrderVendorId,
                "CommerceHubPayout.Create"));

        Assert.Equal("COMMERCEHUB_FINANCIAL_RECONCILIATION_VENDOR_SCOPE_REQUIRED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task FinancialReadinessGate_BeforePayoutVendorRefundMirror_UsesPostedPlatformAdjustmentCoverage()
    {
        var tenantId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var platformAdjustment = CreateBeforePayoutRefundTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Adjustment,
            refundId);
        var vendorMirror = CreateBeforePayoutRefundTransaction(
            CommerceHubFinancialEntityType.Vendor,
            tenantId,
            CommerceHubFinancialTransactionType.Refund,
            refundId);
        var canonicalJournal = JournalEntry.Create(
            tenantId,
            $"JE-MIRROR-{Guid.NewGuid():N}"[..18],
            DateTime.UtcNow,
            "CommerceHubFinancialTransaction",
            platformAdjustment.Id,
            refundId.ToString("D"),
            "Canonical before-payout payable reversal.",
            $"TEST-MIRROR:{platformAdjustment.Id:N}",
            null);

        _dbContext.CommerceHubFinancialTransactions.AddRange(platformAdjustment, vendorMirror);
        _dbContext.JournalEntries.Add(canonicalJournal);
        using (LedgerMutationScope.Authorize())
        {
            await SeedHealthyFinancialReconciliationAsync(tenantId);
            await _dbContext.SaveChangesAsync();
        }

        var gate = new CommerceHubFinancialReadinessGate(
            _dbContext,
            EmergencyStopOptionsMonitor(),
            NullLogger<CommerceHubFinancialReadinessGate>.Instance);

        await gate.EnsureOutwardMoneyMovementAllowedAsync(tenantId, "test-before-payout-mirror");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinancialReadinessGate_UncoveredVendorRefundMirror_BlocksWithoutPostedCanonicalAdjustment(
        bool includeUnpostedCanonicalAdjustment)
    {
        var tenantId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var vendorMirror = CreateBeforePayoutRefundTransaction(
            CommerceHubFinancialEntityType.Vendor,
            tenantId,
            CommerceHubFinancialTransactionType.Refund,
            refundId);
        _dbContext.CommerceHubFinancialTransactions.Add(vendorMirror);
        if (includeUnpostedCanonicalAdjustment)
        {
            _dbContext.CommerceHubFinancialTransactions.Add(CreateBeforePayoutRefundTransaction(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Adjustment,
                refundId));
        }

        await SeedHealthyFinancialReconciliationAsync(tenantId);
        var gate = new CommerceHubFinancialReadinessGate(
            _dbContext,
            EmergencyStopOptionsMonitor(),
            NullLogger<CommerceHubFinancialReadinessGate>.Instance);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            gate.EnsureOutwardMoneyMovementAllowedAsync(tenantId, "test-uncovered-mirror"));

        Assert.Equal("COMMERCEHUB_LEDGER_ACCOUNTING_COVERAGE_REQUIRED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task FinancialReadinessGate_VendorPayoutMirror_UsesPostedPlatformPayoutCoverage()
    {
        var tenantId = Guid.NewGuid();
        var payoutId = Guid.NewGuid();
        var platformPayout = CreatePayoutTransaction(CommerceHubFinancialEntityType.Platform, null, payoutId);
        var vendorMirror = CreatePayoutTransaction(CommerceHubFinancialEntityType.Vendor, tenantId, payoutId);
        var canonicalJournal = JournalEntry.Create(
            tenantId,
            $"JE-PAYOUT-{Guid.NewGuid():N}"[..18],
            DateTime.UtcNow,
            "CommerceHubFinancialTransaction",
            platformPayout.Id,
            payoutId.ToString("D"),
            "Canonical payout liability reduction.",
            $"TEST-PAYOUT:{platformPayout.Id:N}",
            null);

        _dbContext.CommerceHubFinancialTransactions.AddRange(platformPayout, vendorMirror);
        _dbContext.JournalEntries.Add(canonicalJournal);
        using (LedgerMutationScope.Authorize())
        {
            await SeedHealthyFinancialReconciliationAsync(tenantId);
            await _dbContext.SaveChangesAsync();
        }

        var gate = new CommerceHubFinancialReadinessGate(
            _dbContext,
            EmergencyStopOptionsMonitor(),
            NullLogger<CommerceHubFinancialReadinessGate>.Instance);

        await gate.EnsureOutwardMoneyMovementAllowedAsync(tenantId, "test-payout-mirror");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinancialReadinessGate_UncoveredVendorPayoutMirror_BlocksWithoutPostedCanonicalPayout(
        bool includeUnpostedCanonicalPayout)
    {
        var tenantId = Guid.NewGuid();
        var payoutId = Guid.NewGuid();
        var vendorMirror = CreatePayoutTransaction(CommerceHubFinancialEntityType.Vendor, tenantId, payoutId);
        _dbContext.CommerceHubFinancialTransactions.Add(vendorMirror);
        if (includeUnpostedCanonicalPayout)
        {
            _dbContext.CommerceHubFinancialTransactions.Add(
                CreatePayoutTransaction(CommerceHubFinancialEntityType.Platform, null, payoutId));
        }

        await SeedHealthyFinancialReconciliationAsync(tenantId);
        var gate = new CommerceHubFinancialReadinessGate(
            _dbContext,
            EmergencyStopOptionsMonitor(),
            NullLogger<CommerceHubFinancialReadinessGate>.Instance);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            gate.EnsureOutwardMoneyMovementAllowedAsync(tenantId, "test-uncovered-payout-mirror"));

        Assert.Equal("COMMERCEHUB_LEDGER_ACCOUNTING_COVERAGE_REQUIRED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task FinancialReadinessGate_WhenPaymentCompensationAffectedStoreIncludesTenant_ShouldBlockEvenIfBaseTenantDiffers()
    {
        var baseTenantId = Guid.NewGuid();
        var affectedTenantId = Guid.NewGuid();
        var gate = new CommerceHubFinancialReadinessGate(
            _dbContext,
            EmergencyStopOptionsMonitor(),
            NullLogger<CommerceHubFinancialReadinessGate>.Instance);

        _dbContext.CommerceHubPaymentCompensationOutboxMessages.Add(new CommerceHubPaymentCompensationOutboxMessage
        {
            Id = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            TenantId = baseTenantId,
            AffectedStoreIdsJson = $$"""["{{baseTenantId:D}}","{{affectedTenantId:D}}"]""",
            StoreAllocationSnapshotJson = "[]",
            Provider = PaymentProvider.Stripe,
            ProviderIntentId = "pi-affected-store",
            ProviderTransactionId = "tx-affected-store",
            Amount = 100m,
            Currency = "EGP",
            Reason = "Captured payment requires refund compensation.",
            IdempotencyKey = $"affected-store-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            IsFailed = true
        });
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            gate.EnsureCommerceIntakeAllowedAsync(
                new[] { affectedTenantId },
                "test-affected-store-compensation"));

        Assert.Equal("PAYMENT_COMPENSATION_NOT_READY", exception.Code);
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task FinancialReadinessGate_WhenPaymentCompensationHasDifferentBaseTenantAndNoAffectedStores_ShouldNotBlockTenant()
    {
        var baseTenantId = Guid.NewGuid();
        var unaffectedTenantId = Guid.NewGuid();
        await SeedHealthyFinancialReconciliationAsync(unaffectedTenantId);

        var gate = new CommerceHubFinancialReadinessGate(
            _dbContext,
            EmergencyStopOptionsMonitor(),
            NullLogger<CommerceHubFinancialReadinessGate>.Instance);

        _dbContext.CommerceHubPaymentCompensationOutboxMessages.Add(new CommerceHubPaymentCompensationOutboxMessage
        {
            Id = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            TenantId = baseTenantId,
            AffectedStoreIdsJson = "[]",
            StoreAllocationSnapshotJson = "[]",
            Provider = PaymentProvider.Stripe,
            ProviderIntentId = "pi-different-base-tenant",
            ProviderTransactionId = "tx-different-base-tenant",
            Amount = 100m,
            Currency = "EGP",
            Reason = "Captured payment requires refund compensation.",
            IdempotencyKey = $"different-base-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            IsFailed = true
        });
        await _dbContext.SaveChangesAsync();

        await gate.EnsureCommerceIntakeAllowedAsync(
            new[] { unaffectedTenantId },
            "test-different-base-compensation");
    }

    [Fact]
    public async Task FinancialReadinessGate_WhenUnresolvedWebhookAffectedStoreIncludesTenant_ShouldBlockEvenIfBaseTenantDiffers()
    {
        var baseTenantId = Guid.NewGuid();
        var affectedTenantId = Guid.NewGuid();
        await SeedHealthyFinancialReconciliationAsync(affectedTenantId);
        var gate = new CommerceHubFinancialReadinessGate(
            _dbContext,
            EmergencyStopOptionsMonitor(),
            NullLogger<CommerceHubFinancialReadinessGate>.Instance);

        _dbContext.CommerceHubUnresolvedPaymentWebhooks.Add(new CommerceHubUnresolvedPaymentWebhook
        {
            Id = Guid.NewGuid(),
            Provider = PaymentProvider.Stripe,
            ProviderEventId = $"affected-webhook-{Guid.NewGuid():N}",
            ProviderIntentId = "pi-unresolved-affected-store",
            ProviderTransactionId = "tx-unresolved-affected-store",
            TenantId = baseTenantId,
            OrderId = Guid.NewGuid(),
            AffectedStoreIdsJson = $$"""["{{baseTenantId:D}}","{{affectedTenantId:D}}"]""",
            SuspenseRoutedStoreIdsJson = "[]",
            Amount = 100m,
            Currency = "EGP",
            EventType = "payment_intent.succeeded",
            PayloadHash = new string('b', 64),
            RawPayload = "{}",
            Status = "PendingTenantResolution",
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-20)
        });
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            gate.EnsureCommerceIntakeAllowedAsync(
                new[] { affectedTenantId },
                "test-affected-store-unresolved-webhook"));

        Assert.Equal("UNRESOLVED_CAPTURED_PAYMENT_NOT_RECONCILED", exception.Code);
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task MarkFailedAsync_ShouldReleaseReservedEarnings()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "C");
        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                Method = CommerceHubPayoutMethod.Manual,
                IdempotencyKey = "payout-fail-1"
            },
            "trace-create");

        var failed = await _payoutService.MarkFailedAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                FailureReason = "Bank transfer rejected."
            },
            "trace-fail");

        var earning = await _dbContext.CommerceHubVendorEarnings.SingleAsync(item => item.Id == graph.PrimaryEarning.Id);
        var auditLog = await _dbContext.CommerceHubFinancialAuditLogs.SingleAsync(item => item.PayoutId == payout.PayoutId);

        Assert.Equal("Failed", failed.Status);
        Assert.Equal(CommerceHubVendorEarningStatus.Available, earning.Status);
        Assert.Null(earning.PayoutId);
        Assert.Equal("PayoutFailed", auditLog.Type);
    }

    [Fact]
    public async Task MarkCompletedAsync_ShouldMarkEarningsPaidOut_AndWriteAuditLog()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "D");
        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                Method = CommerceHubPayoutMethod.BankTransfer,
                IdempotencyKey = "payout-complete-1"
            },
            "trace-create");

        var processing = await _payoutService.MarkProcessingAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "processing-handoff-payout-complete-1",
                ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey
            },
            "trace-processing",
            processedBy: "admin-user");

        var completed = await _payoutService.MarkCompletedAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "BANK-REF-001",
                ExternalReferenceId = "PAYOUT-EXT-001",
                BankTransactionId = "BANK-TX-001",
                ExternalTransferHandoffToken = processing.ExternalTransferHandoffToken,
                ExternalTransferConfirmedExecuted = true
            },
            "trace-complete",
            processedBy: "admin-user");

        var earning = await _dbContext.CommerceHubVendorEarnings.SingleAsync(item => item.Id == graph.PrimaryEarning.Id);
        var auditLog = await _dbContext.CommerceHubFinancialAuditLogs.SingleAsync(item => item.PayoutId == payout.PayoutId);
        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(item => item.ReferenceId == payout.PayoutId)
            .ToListAsync();

        Assert.Equal("Completed", completed.Status);
        Assert.Equal("BANK-REF-001", completed.Reference);
        Assert.Equal("PAYOUT-EXT-001", completed.ExternalReferenceId);
        Assert.Equal("BANK-TX-001", completed.BankTransactionId);
        Assert.Equal("admin-user", completed.ProcessedBy);
        Assert.Equal(CommerceHubVendorEarningStatus.PaidOut, earning.Status);
        Assert.NotNull(earning.PaidOutAtUtc);
        Assert.Equal("Payout", auditLog.Type);
        Assert.Equal(90m, auditLog.Debit);
        Assert.Equal(3, ledgerEntries.Count);
        Assert.Contains(ledgerEntries, item => item.EntityType == CommerceHubFinancialEntityType.Platform && item.TransactionType == CommerceHubFinancialTransactionType.Settlement && item.Direction == CommerceHubFinancialTransactionDirection.Debit && item.Amount == 90m);
        Assert.Contains(ledgerEntries, item => item.EntityType == CommerceHubFinancialEntityType.Platform && item.TransactionType == CommerceHubFinancialTransactionType.Payout && item.Direction == CommerceHubFinancialTransactionDirection.Debit && item.Amount == 90m);
        Assert.Contains(ledgerEntries, item => item.EntityType == CommerceHubFinancialEntityType.Vendor && item.TransactionType == CommerceHubFinancialTransactionType.Payout && item.Direction == CommerceHubFinancialTransactionDirection.Debit && item.Amount == 90m);
    }

    [Fact]
    public async Task CreatePayoutAsync_WhenEarningCameFromDevelopmentSimulation_ShouldRejectExternalTransfer()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "DEV-PAYOUT");
        graph.Payment.ProviderTransactionId = $"DEV-SIM:ONLINE:{graph.Payment.Id:N}:test";
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            await _dbContext.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _payoutService.CreatePayoutAsync(
                _tenantId,
                new CommerceHubCreatePayoutRequestDto
                {
                    EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                    Method = CommerceHubPayoutMethod.BankTransfer,
                    IdempotencyKey = "payout-development-simulation-blocked"
                },
                "trace-development-payout-blocked"));

        Assert.Equal("PAYOUT_DEVELOPMENT_SIMULATION_NOT_TRANSFERABLE", exception.Code);
        Assert.Empty(await _dbContext.CommerceHubPayouts.ToListAsync());
        Assert.Equal(CommerceHubVendorEarningStatus.Available, graph.PrimaryEarning.Status);
    }

    [Fact]
    public async Task MarkCompletedAsync_ExternallyLockedPayout_ShouldRecognizeExecutedTransfer_WhenGovernanceNowBlocks()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "DG");
        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                Method = CommerceHubPayoutMethod.BankTransfer,
                IdempotencyKey = "payout-complete-governance-blocked"
            },
            "trace-create-governance-blocked");

        var processing = await _payoutService.MarkProcessingAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "processing-handoff-governance-blocked",
                ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey
            },
            "trace-processing-governance-blocked");

        var payoutServiceWithBlockedGovernance = CreatePayoutService(CreateBlockingVendorPayoutGovernance());

        var completed = await payoutServiceWithBlockedGovernance.MarkCompletedAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "BANK-REF-GOV-BLOCKED",
                ExternalReferenceId = "PAYOUT-EXT-GOV-BLOCKED",
                BankTransactionId = "BANK-TX-GOV-BLOCKED",
                ExternalTransferHandoffToken = processing.ExternalTransferHandoffToken,
                ExternalTransferConfirmedExecuted = true
            },
            "trace-complete-governance-blocked",
            processedBy: "ops-reconciliation");

        var earning = await _dbContext.CommerceHubVendorEarnings.SingleAsync(item => item.Id == graph.PrimaryEarning.Id);
        var ledgerEntryCount = await _dbContext.CommerceHubFinancialTransactions
            .CountAsync(item => item.ReferenceId == payout.PayoutId);

        Assert.Equal("Completed", completed.Status);
        Assert.Equal(CommerceHubVendorEarningStatus.PaidOut, earning.Status);
        Assert.Equal(3, ledgerEntryCount);
    }

    [Fact]
    public async Task MarkFailedAsync_ExternallyLockedPayout_ShouldRecognizeNonExecutionProof_WhenGovernanceNowBlocks()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "DF");
        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                Method = CommerceHubPayoutMethod.BankTransfer,
                IdempotencyKey = "payout-fail-governance-blocked"
            },
            "trace-create-fail-governance-blocked");

        var processing = await _payoutService.MarkProcessingAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "processing-handoff-fail-governance-blocked",
                ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey
            },
            "trace-processing-fail-governance-blocked");

        var payoutServiceWithBlockedGovernance = CreatePayoutService(CreateBlockingVendorPayoutGovernance());

        var failed = await payoutServiceWithBlockedGovernance.MarkFailedAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "BANK-NON-EXECUTION-GOV-BLOCKED",
                FailureReason = "Provider confirmed the transfer was not executed.",
                ExternalTransferHandoffToken = processing.ExternalTransferHandoffToken,
                ExternalTransferConfirmedNotExecuted = true
            },
            "trace-fail-governance-blocked",
            processedBy: "ops-reconciliation");

        var earning = await _dbContext.CommerceHubVendorEarnings.SingleAsync(item => item.Id == graph.PrimaryEarning.Id);

        Assert.Equal("Failed", failed.Status);
        Assert.Equal(CommerceHubVendorEarningStatus.Available, earning.Status);
        Assert.Null(earning.PayoutId);
    }

    [Fact]
    public async Task MarkCompletedAsync_NonManualPayoutWithoutExternalReference_ShouldRejectBeforeLedgerMutation()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "DX");
        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                Method = CommerceHubPayoutMethod.BankTransfer,
                IdempotencyKey = "payout-complete-missing-external-reference"
            },
            "trace-create-missing-reference");

        var processing = await _payoutService.MarkProcessingAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "processing-handoff-missing-external-reference",
                ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey
            },
            "trace-processing-missing-reference");

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _payoutService.MarkCompletedAsync(
                _tenantId,
                payout.PayoutId,
                new CommerceHubUpdatePayoutRequestDto
                {
                    Reference = "operator-note-only",
                    ExternalTransferHandoffToken = processing.ExternalTransferHandoffToken,
                    ExternalTransferConfirmedExecuted = true
                },
                "trace-complete-missing-reference"));

        var storedPayout = await _dbContext.CommerceHubPayouts.SingleAsync(item => item.Id == payout.PayoutId);
        var ledgerEntryCount = await _dbContext.CommerceHubFinancialTransactions
            .CountAsync(item => item.ReferenceId == payout.PayoutId);

        Assert.Equal("PAYOUT_EXTERNAL_CONFIRMATION_REQUIRED", exception.Code);
        Assert.Equal(CommerceHubPayoutStatus.Processing, storedPayout.Status);
        Assert.Equal(0, ledgerEntryCount);
    }

    [Fact]
    public async Task MarkCompletedAsync_ProcessingPayoutWithoutExecutionConfirmation_ShouldRejectBeforeLedgerMutation()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "DY");
        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                Method = CommerceHubPayoutMethod.BankTransfer,
                IdempotencyKey = "payout-complete-no-execution-confirmation"
            },
            "trace-create-no-execution-confirmation");

        var processing = await _payoutService.MarkProcessingAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "processing-handoff-no-execution-confirmation",
                ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey
            },
            "trace-processing-no-execution-confirmation");

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _payoutService.MarkCompletedAsync(
                _tenantId,
                payout.PayoutId,
                new CommerceHubUpdatePayoutRequestDto
                {
                    Reference = "BANK-REF-002",
                    ExternalReferenceId = "PAYOUT-EXT-002",
                    BankTransactionId = "BANK-TX-002",
                    ExternalTransferHandoffToken = processing.ExternalTransferHandoffToken
                    // ExternalTransferConfirmedExecuted is omitted (defaults to false)
                },
                "trace-complete-no-execution-confirmation"));

        var storedPayout = await _dbContext.CommerceHubPayouts.SingleAsync(item => item.Id == payout.PayoutId);
        var ledgerEntryCount = await _dbContext.CommerceHubFinancialTransactions
            .CountAsync(item => item.ReferenceId == payout.PayoutId);

        Assert.Equal("PAYOUT_EXTERNAL_TRANSFER_EXECUTION_CONFIRMATION_REQUIRED", exception.Code);
        Assert.Equal(CommerceHubPayoutStatus.Processing, storedPayout.Status);
        Assert.Equal(0, ledgerEntryCount);
    }

    [Fact]
    public async Task MarkCompletedAsync_PendingPayout_ShouldRejectBeforeExternalCompletionBoundary()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "DP");
        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                Method = CommerceHubPayoutMethod.BankTransfer,
                IdempotencyKey = "payout-complete-pending-rejected"
            },
            "trace-create-pending-rejected");

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _payoutService.MarkCompletedAsync(
                _tenantId,
                payout.PayoutId,
                new CommerceHubUpdatePayoutRequestDto
                {
                    Reference = "BANK-REF-PENDING",
                    ExternalReferenceId = "PAYOUT-EXT-PENDING",
                    BankTransactionId = "BANK-TX-PENDING",
                    ExternalTransferConfirmedExecuted = true
                },
                "trace-complete-pending-rejected"));

        var storedPayout = await _dbContext.CommerceHubPayouts.SingleAsync(item => item.Id == payout.PayoutId);
        var ledgerEntryCount = await _dbContext.CommerceHubFinancialTransactions
            .CountAsync(item => item.ReferenceId == payout.PayoutId);

        Assert.Equal("PAYOUT_MUST_BE_PROCESSING_BEFORE_COMPLETION", exception.Code);
        Assert.Equal(CommerceHubPayoutStatus.Pending, storedPayout.Status);
        Assert.Equal(0, ledgerEntryCount);
    }

    [Fact]
    public async Task CreateRefundAsync_PartialRefundBeforePayout_ShouldCreateNegativeAdjustment()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "E");

        var refund = await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 30m,
                Reason = "Customer returned one item.",
                IdempotencyKey = "refund-partial-1"
            },
            "trace-refund");

        var earnings = await _dbContext.CommerceHubVendorEarnings
            .Where(item => item.SubOrderId == graph.SubOrder.Id)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();

        var primary = earnings.Single(item => item.OriginalEarningId == null);
        var adjustment = earnings.Single(item => item.OriginalEarningId == primary.Id);
        var auditTypes = await _dbContext.CommerceHubFinancialAuditLogs
            .Where(item => item.RefundId == refund.RefundId)
            .Select(item => item.Type)
            .ToListAsync();
        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(item => item.ReferenceId == refund.RefundId)
            .ToListAsync();

        Assert.Equal("Completed", refund.Status);
        Assert.False(refund.IsFullRefund);
        Assert.Equal(CommerceHubVendorEarningStatus.Available, primary.Status);
        Assert.Equal(-27m, adjustment.Amount);
        Assert.Equal(CommerceHubVendorEarningStatus.Available, adjustment.Status);
        Assert.Equal(refund.RefundId, adjustment.RefundId);
        Assert.Contains("Refund", auditTypes);
        Assert.Contains("CommissionReversed", auditTypes);
        Assert.Contains("EarningAdjustment", auditTypes);
        Assert.Equal(4, ledgerEntries.Count);
        Assert.Contains(ledgerEntries, item => item.EntityType == CommerceHubFinancialEntityType.Platform && item.TransactionType == CommerceHubFinancialTransactionType.Refund && item.Direction == CommerceHubFinancialTransactionDirection.Debit && item.Amount == 30m);
        Assert.Contains(ledgerEntries, item => item.EntityType == CommerceHubFinancialEntityType.Platform && item.TransactionType == CommerceHubFinancialTransactionType.Adjustment && item.Direction == CommerceHubFinancialTransactionDirection.Debit && item.Amount == 27m);
        Assert.Contains(ledgerEntries, item => item.EntityType == CommerceHubFinancialEntityType.Vendor && item.TransactionType == CommerceHubFinancialTransactionType.Refund && item.Direction == CommerceHubFinancialTransactionDirection.Debit && item.Amount == 27m);
        Assert.Contains(ledgerEntries, item => item.EntityType == CommerceHubFinancialEntityType.Platform && item.TransactionType == CommerceHubFinancialTransactionType.Commission && item.Direction == CommerceHubFinancialTransactionDirection.Debit && item.Amount == 3m);
    }

    [Fact]
    public async Task CreateRefundAsync_WhenUnscopedUnresolvedProviderRefundExists_ShouldBlockFailClosed()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "UNSCOPED-REFUND");
        await AddUnscopedUnresolvedPaymentWebhookAsync(
            status: "ProviderRefundPendingReconciliation",
            providerEventId: $"unscoped-refund-{Guid.NewGuid():N}",
            amount: 10m,
            eventType: "charge.refunded");

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _refundService.CreateRefundAsync(
                _tenantId,
                new CommerceHubCreateRefundRequestDto
                {
                    SubOrderId = graph.SubOrder.Id,
                    Amount = 10m,
                    Reason = "Customer requested refund",
                    IdempotencyKey = "refund-unscoped-webhook-1"
                },
                "trace-unscoped-refund"));

        Assert.Equal("UNMATCHED_PROVIDER_REFUND", exception.Code);
        Assert.Equal(409, exception.StatusCode);
        _refundGatewayMock.Verify(service => service.RefundAsync(
            It.IsAny<CommerceHubPayment?>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRefundAsync_FullRefundBeforePayout_ShouldReversePrimaryEarning()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "F");

        var refund = await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 100m,
                Reason = "Order cancelled after payment.",
                IdempotencyKey = "refund-full-1"
            },
            "trace-refund-full");

        var earning = await _dbContext.CommerceHubVendorEarnings.SingleAsync(item => item.Id == graph.PrimaryEarning.Id);
        var auditLog = await _dbContext.CommerceHubFinancialAuditLogs
            .SingleAsync(item => item.RefundId == refund.RefundId && item.Type == "EarningReversed");

        Assert.Equal("Completed", refund.Status);
        Assert.True(refund.IsFullRefund);
        Assert.Equal(CommerceHubVendorEarningStatus.Reversed, earning.Status);
        Assert.Equal(refund.RefundId, earning.RefundId);
        Assert.NotNull(earning.ReversedAtUtc);
        Assert.Equal(90m, auditLog.Debit);
    }

    [Fact]
    public async Task CreateRefundAsync_AfterPaidOut_ShouldCreateNegativeAvailableAdjustment()
    {
        var graph = await SeedOnlineFinancialGraphAsync(
            orderSuffix: "G",
            earningStatus: CommerceHubVendorEarningStatus.PaidOut);

        var refund = await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 20m,
                Reason = "Late return after payout.",
                IdempotencyKey = "refund-after-payout-1"
            },
            "trace-after-payout");

        var earnings = await _dbContext.CommerceHubVendorEarnings
            .Where(item => item.SubOrderId == graph.SubOrder.Id)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
        var trackedRefund = await _dbContext.CommerceHubRefunds.SingleAsync(item => item.Id == refund.RefundId);
        var debtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        var debtSnapshot = await debtService.GetVendorDebtSnapshotAsync(_tenantId);

        var adjustment = earnings.Single(item => item.OriginalEarningId == graph.PrimaryEarning.Id);
        var primary = earnings.Single(item => item.Id == graph.PrimaryEarning.Id);

        Assert.Equal("Completed", refund.Status);
        Assert.True(trackedRefund.IsPostPayoutAdjustment);
        Assert.Equal(CommerceHubVendorEarningStatus.PaidOut, primary.Status);
        Assert.Equal(-18m, adjustment.Amount);
        Assert.Equal(CommerceHubVendorEarningStatus.Available, adjustment.Status);
        Assert.Equal(refund.RefundId, adjustment.RefundId);
        Assert.Equal(18m, debtSnapshot.AmountOwedByVendor);
    }

    [Fact]
    public async Task CreateRefundAsync_WhenGatewayAcceptsButDoesNotConfirm_ShouldRemainProcessing()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "I");
        _refundGatewayMock
            .Setup(service => service.RefundAsync(
                It.IsAny<CommerceHubPayment?>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubGatewayRefundResult(
                true,
                false,
                "MockProvider",
                "refund-pending-1",
                "pending_confirmation",
                null));

        var refund = await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 30m,
                Reason = "Awaiting gateway webhook.",
                IdempotencyKey = "refund-processing-1"
            },
            "trace-refund-processing");

        var trackedRefund = await _dbContext.CommerceHubRefunds.SingleAsync(item => item.Id == refund.RefundId);
        var earnings = await _dbContext.CommerceHubVendorEarnings
            .Where(item => item.SubOrderId == graph.SubOrder.Id)
            .ToListAsync();
        var refundAuditCount = await _dbContext.CommerceHubFinancialAuditLogs
            .CountAsync(item => item.RefundId == refund.RefundId);
        var payment = await _dbContext.CommerceHubPayments.SingleAsync(item => item.Id == graph.Payment.Id);

        Assert.Equal("Processing", refund.Status);
        Assert.Equal("pending_confirmation", refund.GatewayStatus);
        Assert.Null(refund.ProcessedAtUtc);
        Assert.NotNull(refund.ProcessingStartedAtUtc);
        Assert.Equal(CommerceHubRefundStatus.Processing, trackedRefund.Status);
        Assert.All(earnings, item => Assert.Null(item.RefundId));
        Assert.All(earnings, item => Assert.NotEqual(CommerceHubVendorEarningStatus.Reversed, item.Status));
        Assert.Equal(0, refundAuditCount);
        Assert.Equal(0m, payment.RefundedAmount);
    }

    [Fact]
    public async Task CreateRefundAsync_ReplayForStaleProcessingRefund_ShouldRecoverAndCompleteFinancialReversal()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "I-RECOVER");
        _refundGatewayMock
            .SetupSequence(service => service.RefundAsync(
                It.IsAny<CommerceHubPayment?>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubGatewayRefundResult(
                true,
                false,
                "MockProvider",
                null,
                "processing",
                null))
            .ReturnsAsync(new CommerceHubGatewayRefundResult(
                true,
                true,
                "MockProvider",
                "refund-recovered-1",
                "confirmed",
                null));

        var pendingRefund = await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 30m,
                Reason = "Replay should recover this refund.",
                IdempotencyKey = "refund-processing-recover-1"
            },
            "trace-refund-processing-recover-1");

        var trackedBeforeReplay = await _dbContext.CommerceHubRefunds.SingleAsync(item => item.Id == pendingRefund.RefundId);
        using (CommerceHubBoundaryMutationScope.BeginRefund())
        {
            trackedBeforeReplay.ProcessingStartedAtUtc = DateTime.UtcNow.AddMinutes(-5);
            await _dbContext.SaveChangesAsync();
        }

        var recoveredRefund = await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 30m,
                Reason = "Replay should recover this refund.",
                IdempotencyKey = "refund-processing-recover-1"
            },
            "trace-refund-processing-recover-1-replay");

        var trackedRefund = await _dbContext.CommerceHubRefunds.SingleAsync(item => item.Id == pendingRefund.RefundId);
        var payment = await _dbContext.CommerceHubPayments.SingleAsync(item => item.Id == graph.Payment.Id);
        var auditTypes = await _dbContext.CommerceHubFinancialAuditLogs
            .Where(item => item.RefundId == recoveredRefund.RefundId)
            .Select(item => item.Type)
            .ToListAsync();

        Assert.Equal("Completed", recoveredRefund.Status);
        Assert.Equal(CommerceHubRefundStatus.Completed, trackedRefund.Status);
        Assert.Equal("refund-recovered-1", trackedRefund.Reference);
        Assert.Equal("confirmed", trackedRefund.GatewayStatus);
        Assert.NotNull(trackedRefund.ProcessedAtUtc);
        Assert.Equal(30m, payment.RefundedAmount);
        Assert.Contains("Refund", auditTypes);
        Assert.Contains("CommissionReversed", auditTypes);
        _lockServiceMock.Verify(service => service.TryAcquireLockAsync(
            It.Is<string>(key => key.Contains(pendingRefund.RefundId.ToString("N"), StringComparison.OrdinalIgnoreCase)),
            It.IsAny<string>(),
            It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task ReconcileProcessingRefundsAsync_ShouldRecoverStaleProcessingRefund()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "I-WORKER");
        _refundGatewayMock
            .SetupSequence(service => service.RefundAsync(
                It.IsAny<CommerceHubPayment?>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubGatewayRefundResult(
                true,
                false,
                "MockProvider",
                null,
                "processing",
                null))
            .ReturnsAsync(new CommerceHubGatewayRefundResult(
                true,
                true,
                "MockProvider",
                "refund-recovered-worker",
                "confirmed",
                null));

        var pendingRefund = await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 30m,
                Reason = "Background recovery should complete this refund.",
                IdempotencyKey = "refund-processing-worker-1"
            },
            "trace-refund-processing-worker-1");

        var trackedBeforeRecovery = await _dbContext.CommerceHubRefunds.SingleAsync(item => item.Id == pendingRefund.RefundId);
        using (CommerceHubBoundaryMutationScope.BeginRefund())
        {
            trackedBeforeRecovery.ProcessingStartedAtUtc = DateTime.UtcNow.AddMinutes(-5);
            await _dbContext.SaveChangesAsync();
        }

        var recoveredCount = await _refundService.ReconcileProcessingRefundsAsync();

        var trackedRefund = await _dbContext.CommerceHubRefunds.SingleAsync(item => item.Id == pendingRefund.RefundId);
        var payment = await _dbContext.CommerceHubPayments.SingleAsync(item => item.Id == graph.Payment.Id);

        Assert.Equal(1, recoveredCount);
        Assert.Equal(CommerceHubRefundStatus.Completed, trackedRefund.Status);
        Assert.Equal("refund-recovered-worker", trackedRefund.Reference);
        Assert.Equal("confirmed", trackedRefund.GatewayStatus);
        Assert.Equal(30m, payment.RefundedAmount);
    }

    [Fact]
    public async Task ConfirmRefundAsync_ShouldCompleteFinancialReversalForProcessingRefund()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "J");
        _refundGatewayMock
            .Setup(service => service.RefundAsync(
                It.IsAny<CommerceHubPayment?>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubGatewayRefundResult(
                true,
                false,
                "MockProvider",
                "refund-pending-2",
                "pending_confirmation",
                null));

        var pendingRefund = await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 30m,
                Reason = "Webhook will confirm later.",
                IdempotencyKey = "refund-processing-2"
            },
            "trace-refund-processing-2");

        var completedRefund = await _refundService.ConfirmRefundAsync(
            _tenantId,
            pendingRefund.RefundId,
            new CommerceHubConfirmRefundRequestDto
            {
                Reference = "refund-pending-2",
                GatewayStatus = "confirmed"
            },
            "trace-refund-confirm-2");

        var earnings = await _dbContext.CommerceHubVendorEarnings
            .Where(item => item.SubOrderId == graph.SubOrder.Id)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
        var primary = earnings.Single(item => item.OriginalEarningId == null);
        var adjustment = earnings.Single(item => item.OriginalEarningId == primary.Id);
        var payment = await _dbContext.CommerceHubPayments.SingleAsync(item => item.Id == graph.Payment.Id);
        var auditTypes = await _dbContext.CommerceHubFinancialAuditLogs
            .Where(item => item.RefundId == completedRefund.RefundId)
            .Select(item => item.Type)
            .ToListAsync();

        Assert.Equal("Completed", completedRefund.Status);
        Assert.Equal("confirmed", completedRefund.GatewayStatus);
        Assert.Equal("refund-pending-2", completedRefund.Reference);
        Assert.NotNull(completedRefund.ProcessedAtUtc);
        Assert.Equal(30m, payment.RefundedAmount);
        Assert.Equal(CommerceHubVendorEarningStatus.Available, primary.Status);
        Assert.Equal(-27m, adjustment.Amount);
        Assert.Equal(CommerceHubVendorEarningStatus.Available, adjustment.Status);
        Assert.Contains("Refund", auditTypes);
        Assert.Contains("CommissionReversed", auditTypes);
        Assert.Contains("EarningAdjustment", auditTypes);
    }

    [Fact]
    public async Task ConfirmRefundAsync_WhenGatewayStatusIsNotTerminal_ShouldRejectWithoutFinancialMutation()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "J-PENDING");
        _refundGatewayMock
            .Setup(service => service.RefundAsync(
                It.IsAny<CommerceHubPayment?>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubGatewayRefundResult(
                true,
                false,
                "MockProvider",
                "refund-pending-status",
                "pending_confirmation",
                null));

        var pendingRefund = await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 30m,
                Reason = "Gateway has not settled refund yet.",
                IdempotencyKey = "refund-processing-pending-status"
            },
            "trace-refund-processing-pending-status");

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _refundService.ConfirmRefundAsync(
                _tenantId,
                pendingRefund.RefundId,
                new CommerceHubConfirmRefundRequestDto
                {
                    Reference = "refund-pending-status",
                    GatewayStatus = "pending_confirmation"
                },
                "trace-refund-confirm-pending-status"));

        var trackedRefund = await _dbContext.CommerceHubRefunds.SingleAsync(item => item.Id == pendingRefund.RefundId);
        var earnings = await _dbContext.CommerceHubVendorEarnings
            .Where(item => item.SubOrderId == graph.SubOrder.Id)
            .ToListAsync();
        var payment = await _dbContext.CommerceHubPayments.SingleAsync(item => item.Id == graph.Payment.Id);
        var refundAuditCount = await _dbContext.CommerceHubFinancialAuditLogs
            .CountAsync(item => item.RefundId == pendingRefund.RefundId);

        Assert.Equal("REFUND_GATEWAY_CONFIRMATION_REQUIRED", exception.Code);
        Assert.Equal(CommerceHubRefundStatus.Processing, trackedRefund.Status);
        Assert.Equal("pending_confirmation", trackedRefund.GatewayStatus);
        Assert.Null(trackedRefund.ProcessedAtUtc);
        Assert.Equal(0m, payment.RefundedAmount);
        Assert.Equal(0, refundAuditCount);
        Assert.All(earnings, item => Assert.Null(item.RefundId));
    }

    [Fact]
    public async Task ConfirmRefundAsync_WhenProviderReferenceMismatches_ShouldRejectWithoutFinancialMutation()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "J-MISMATCH");
        _refundGatewayMock
            .Setup(service => service.RefundAsync(
                It.IsAny<CommerceHubPayment?>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubGatewayRefundResult(
                true,
                false,
                "MockProvider",
                "refund-original-reference",
                "pending_confirmation",
                null));

        var pendingRefund = await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 30m,
                Reason = "Gateway webhook must match original refund.",
                IdempotencyKey = "refund-processing-reference-mismatch"
            },
            "trace-refund-processing-reference-mismatch");

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _refundService.ConfirmRefundAsync(
                _tenantId,
                pendingRefund.RefundId,
                new CommerceHubConfirmRefundRequestDto
                {
                    Reference = "refund-different-reference",
                    GatewayStatus = "confirmed"
                },
                "trace-refund-confirm-reference-mismatch"));

        var trackedRefund = await _dbContext.CommerceHubRefunds.SingleAsync(item => item.Id == pendingRefund.RefundId);
        var earnings = await _dbContext.CommerceHubVendorEarnings
            .Where(item => item.SubOrderId == graph.SubOrder.Id)
            .ToListAsync();
        var payment = await _dbContext.CommerceHubPayments.SingleAsync(item => item.Id == graph.Payment.Id);
        var refundAuditCount = await _dbContext.CommerceHubFinancialAuditLogs
            .CountAsync(item => item.RefundId == pendingRefund.RefundId);

        Assert.Equal("REFUND_PROVIDER_REFERENCE_MISMATCH", exception.Code);
        Assert.Equal(CommerceHubRefundStatus.Processing, trackedRefund.Status);
        Assert.Equal("refund-original-reference", trackedRefund.Reference);
        Assert.Null(trackedRefund.ProcessedAtUtc);
        Assert.Equal(0m, payment.RefundedAmount);
        Assert.Equal(0, refundAuditCount);
        Assert.All(earnings, item => Assert.Null(item.RefundId));
    }

    [Fact]
    public async Task CreatePayoutAsync_ShouldAutoNetOutstandingNegativeAdjustments()
    {
        var refundedAfterPayout = await SeedOnlineFinancialGraphAsync(
            orderSuffix: "K",
            earningStatus: CommerceHubVendorEarningStatus.PaidOut);

        await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = refundedAfterPayout.SubOrder.Id,
                Amount = 20m,
                Reason = "Refund after vendor payout.",
                IdempotencyKey = "refund-after-payout-net-1"
            },
            "trace-refund-net-1");

        var payoutGraph = await SeedOnlineFinancialGraphAsync(orderSuffix: "L");

        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = new List<Guid> { payoutGraph.PrimaryEarning.Id },
                Method = CommerceHubPayoutMethod.Manual,
                IdempotencyKey = "payout-netting-1"
            },
            "trace-payout-net-1");

        var payoutItems = payout.Items.OrderBy(item => item.Amount).ToList();

        Assert.Equal(72m, payout.Amount);
        Assert.Equal(2, payoutItems.Count);
        Assert.Equal(-18m, payoutItems[0].Amount);
        Assert.Equal(90m, payoutItems[1].Amount);
    }

    [Fact]
    public async Task WalletBalances_ShouldReflectOrderPayoutAndRefundAfterPayoutFlows()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "W");
        await _financialLedgerService.RecordOrderAsync(graph.Order, graph.SubOrder);

        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                Method = CommerceHubPayoutMethod.Manual,
                IdempotencyKey = "wallet-payout-1"
            },
            "trace-wallet-payout-create");

        var processing = await _payoutService.MarkProcessingAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "wallet-payout-processing-ref-1",
                ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey
            },
            "trace-wallet-payout-processing");

        await _payoutService.MarkCompletedAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "wallet-payout-ref-1",
                ExternalReferenceId = "wallet-payout-ext-1",
                ExternalTransferHandoffToken = processing.ExternalTransferHandoffToken,
                ExternalTransferConfirmedExecuted = true
            },
            "trace-wallet-payout-complete");

        await MarkCommerceHubLedgerPostedAsync(_tenantId);
        await SeedHealthyFinancialReconciliationAsync(_tenantId);

        await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 20m,
                Reason = "Refund after completed payout.",
                IdempotencyKey = "wallet-refund-after-payout-1"
            },
            "trace-wallet-refund");

        var vendorBalance = await _walletService.GetVendorBalanceAsync(_tenantId);
        var vendorStatement = await _walletService.GetVendorStatementAsync(_tenantId);
        var platformBalance = await _walletService.GetPlatformBalanceAsync();

        Assert.Equal(90m, vendorBalance.TotalCredits);
        Assert.Equal(108m, vendorBalance.TotalDebits);
        Assert.Equal(-18m, vendorBalance.Balance);
        Assert.Equal(3, vendorStatement.Transactions.Count);
        Assert.Equal(90m, vendorStatement.Transactions[0].RunningTotal);
        Assert.Equal(0m, vendorStatement.Transactions[1].RunningTotal);
        Assert.Equal(-18m, vendorStatement.Transactions[2].RunningTotal);

        Assert.Equal(100m, platformBalance.TotalCredits);
        Assert.Equal(110m, platformBalance.TotalDebits);
        Assert.Equal(-10m, platformBalance.Balance);
    }

    [Fact]
    public async Task ExecuteAsync_AfterCompletedPayout_ShouldKeepPayoutReconciliationBalanced()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "N");
        await _financialLedgerService.RecordOrderAsync(graph.Order, graph.SubOrder);

        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                Method = CommerceHubPayoutMethod.Manual,
                IdempotencyKey = "reconciliation-payout-1"
            },
            "trace-reconciliation-payout-create");

        var processing = await _payoutService.MarkProcessingAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "reconciliation-payout-processing-ref-1",
                ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey
            },
            "trace-reconciliation-payout-processing");

        await _payoutService.MarkCompletedAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "reconciliation-payout-ref-1",
                ExternalReferenceId = "reconciliation-payout-ext-1",
                ExternalTransferHandoffToken = processing.ExternalTransferHandoffToken,
                ExternalTransferConfirmedExecuted = true
            },
            "trace-reconciliation-payout-complete");

        var result = await _reconciliationJob.ExecuteAsync();

        Assert.Equal("OK", result.Status);
        Assert.Equal(0, result.OrphanTransactionsCount);
        Assert.Equal(0, result.MissingPairCount);
        Assert.Equal(0, result.PayableMismatchCount);
        Assert.Equal(0, result.PayoutFlowMismatchCount);
    }

    [Fact]
    public async Task ExecuteAsync_AfterPostPayoutRefund_ShouldKeepPayableAndReceivableSeparated()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "R");
        await _financialLedgerService.RecordOrderAsync(graph.Order, graph.SubOrder);

        var payout = await _payoutService.CreatePayoutAsync(
            _tenantId,
            new CommerceHubCreatePayoutRequestDto
            {
                EarningIds = new List<Guid> { graph.PrimaryEarning.Id },
                Method = CommerceHubPayoutMethod.Manual,
                IdempotencyKey = "reconciliation-post-payout-refund-1"
            },
            "trace-post-payout-refund-create");

        var processing = await _payoutService.MarkProcessingAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "reconciliation-post-payout-refund-processing-ref",
                ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey
            },
            "trace-post-payout-refund-processing");

        await _payoutService.MarkCompletedAsync(
            _tenantId,
            payout.PayoutId,
            new CommerceHubUpdatePayoutRequestDto
            {
                Reference = "reconciliation-post-payout-refund-payout-ref",
                ExternalReferenceId = "reconciliation-post-payout-refund-payout-ext",
                ExternalTransferHandoffToken = processing.ExternalTransferHandoffToken,
                ExternalTransferConfirmedExecuted = true
            },
            "trace-post-payout-refund-complete");

        await MarkCommerceHubLedgerPostedAsync(_tenantId);
        await SeedHealthyFinancialReconciliationAsync(_tenantId);

        await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 20m,
                Reason = "Returned after completed payout.",
                IdempotencyKey = "reconciliation-post-payout-refund-refund-1"
            },
            "trace-post-payout-refund-refund");

        var result = await _reconciliationJob.ExecuteAsync();

        Assert.Equal("OK", result.Status);
        Assert.Equal(0, result.PayableMismatchCount);
        Assert.Equal(0, result.ReceivableMismatchCount);
        Assert.Equal(0, result.RefundFlowMismatchCount);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWriteBalancedFinanceReconciliationAudit()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "M");
        await _financialLedgerService.RecordOrderAsync(graph.Order, graph.SubOrder);

        var result = await _reconciliationJob.ExecuteAsync();
        var auditLog = await _dbContext.CommerceHubFinancialAuditLogs
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstAsync(item => item.Type == "FinanceReconciliation");

        Assert.Equal("OK", result.Status);
        Assert.Equal(3, result.TotalTransactions);
        Assert.Equal(0, result.OrphanTransactionsCount);
        Assert.Equal(0, result.MissingPairCount);
        Assert.Equal(0, result.DuplicateEntryCount);
        Assert.Equal(0, result.CorrelationIntegrityMismatchCount);
        Assert.Equal(0, result.PayableMismatchCount);
        Assert.Equal(0, result.ReceivableMismatchCount);
        Assert.Equal(0, result.OrderFlowMismatchCount);
        Assert.Equal(0, result.RefundFlowMismatchCount);
        Assert.Equal(0, result.PayoutFlowMismatchCount);
        Assert.Equal(0, result.AdjustmentFlowMismatchCount);
        Assert.Contains("Status: OK", auditLog.Description);
    }

    [Fact]
    public async Task CreateRefundAsync_ShouldBeIdempotent()
    {
        var graph = await SeedOnlineFinancialGraphAsync(orderSuffix: "H");
        var request = new CommerceHubCreateRefundRequestDto
        {
            SubOrderId = graph.SubOrder.Id,
            Amount = 15m,
            Reason = "Customer goodwill refund.",
            IdempotencyKey = "refund-idempotent-1"
        };

        var first = await _refundService.CreateRefundAsync(_tenantId, request, "trace-first");
        var second = await _refundService.CreateRefundAsync(_tenantId, request, "trace-second");

        Assert.Equal(first.RefundId, second.RefundId);
        Assert.Equal(1, await _dbContext.CommerceHubRefunds.CountAsync());
        _refundGatewayMock.Verify(service => service.RefundAsync(
            It.IsAny<CommerceHubPayment?>(),
            request.Amount,
            It.IsAny<string>(),
            request.Reason,
            request.IdempotencyKey,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRefundAsync_ShippingOnlyRefund_ShouldNotReverseCommission()
    {
        var graph = await SeedOnlineFinancialGraphAsync(
            orderSuffix: "SHIP",
            total: 100m,
            commission: 10m,
            shipping: 10m);

        var refund = await _refundService.CreateRefundAsync(
            _tenantId,
            new CommerceHubCreateRefundRequestDto
            {
                SubOrderId = graph.SubOrder.Id,
                Amount = 10m,
                Reason = "Refund shipping only.",
                IdempotencyKey = "refund-shipping-only-1"
            },
            "trace-shipping");

        Assert.Equal(0m, refund.CommissionReversalAmount);
        Assert.Equal(10m, refund.VendorEarningReversalAmount);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private CommerceHubPayoutService CreatePayoutService(IUserGovernanceService userGovernanceService)
    {
        return new CommerceHubPayoutService(
            _dbContext,
            _financialLedgerService,
            new CommerceHubFinancialSafetyGuardService(_dbContext),
            new CommerceHubFinancialReadinessGate(
                _dbContext,
                EmergencyStopOptionsMonitor(),
                NullLogger<CommerceHubFinancialReadinessGate>.Instance),
            _auditService,
            userGovernanceService,
            NullLogger<CommerceHubPayoutService>.Instance);
    }

    private static IUserGovernanceService CreateBlockingVendorPayoutGovernance()
    {
        var governance = new Mock<IUserGovernanceService>();
        governance
            .Setup(service => service.EnsureVendorPayoutAllowedAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CommerceHubCheckoutException(
                "VENDOR_PAYOUT_BLOCKED",
                "Vendor payout authorization is currently blocked.",
                StatusCodes.Status409Conflict));

        return governance.Object;
    }

    private async Task<CommerceHubFinancialGraph> SeedOnlineFinancialGraphAsync(
        string orderSuffix,
        CommerceHubVendorEarningStatus earningStatus = CommerceHubVendorEarningStatus.Available,
        decimal total = 100m,
        decimal commission = 10m,
        decimal shipping = 0m,
        bool recordOrderLedger = true)
    {
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"CH-{orderSuffix}-{Guid.NewGuid():N}"[..15],
            FullName = "Finance Test Customer",
            PhoneNumber = "01000000000",
            AddressLine = "123 Finance Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "ONLINE",
            PaymentMethodName = "Online",
            Subtotal = total,
            ShippingTotal = shipping,
            TotalAmount = total + shipping,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled,
            PaidAt = DateTime.UtcNow.AddHours(-2)
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"{order.OrderNumber}-1",
            StoreId = _tenantId,
            StoreName = "Vendor Finance Store",
            Currency = "EGP",
            Subtotal = total,
            Shipping = shipping,
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = "ONLINE",
            PaymentMethodName = "Online",
            CommissionSnapshotJson = """{"rate":0.10}""",
            IsEarningCreated = true,
            EarningCreatedAtUtc = DateTime.UtcNow.AddHours(-2),
            PlatformCommissionAmount = commission,
            FinalPlatformCommissionAmount = commission,
            PromoDiscountAmount = 0m,
            VendorEarningAmount = total + shipping - commission,
            PlatformCommissionRate = total == 0 ? 0 : commission / total,
            Total = total + shipping,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled,
            PaidAt = DateTime.UtcNow.AddHours(-2)
        };

        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            Amount = total + shipping,
            Currency = "EGP",
            Status = CommerceHubPaymentRecordStatus.Paid,
            Provider = PaymentProvider.Paymob,
            ProviderReference = $"payment-{orderSuffix}",
            ProviderIntentId = "12345",
            ProviderTransactionId = "67890",
            IsProcessed = true,
            ProcessedAtUtc = DateTime.UtcNow.AddHours(-2),
            PaidAtUtc = DateTime.UtcNow.AddHours(-2)
        };

        var earning = new CommerceHubVendorEarning
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            PaymentId = payment.Id,
            Payment = payment,
            TenantId = _tenantId,
            Amount = total + shipping - commission,
            Status = earningStatus,
            AvailableAtUtc = earningStatus is CommerceHubVendorEarningStatus.Available or CommerceHubVendorEarningStatus.PaidOut
                ? DateTime.UtcNow.AddHours(-1)
                : null,
            PaidOutAtUtc = earningStatus == CommerceHubVendorEarningStatus.PaidOut
                ? DateTime.UtcNow.AddMinutes(-30)
                : null
        };

        order.SubOrders.Add(subOrder);
        order.Payments.Add(payment);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginFinancialWorkflow())
        {
            _dbContext.CommerceHubMarketplaceOrders.Add(order);
            _dbContext.CommerceHubVendorEarnings.Add(earning);
            await _dbContext.SaveChangesAsync();
        }

        if (recordOrderLedger)
        {
            await _financialLedgerService.RecordOrderAsync(order, subOrder);
            await MarkCommerceHubLedgerPostedAsync(_tenantId);
            await SeedHealthyFinancialReconciliationAsync(_tenantId);
        }

        return new CommerceHubFinancialGraph(order, subOrder, payment, earning);
    }

    private async Task MarkCommerceHubLedgerPostedAsync(Guid tenantId)
    {
        var unpostedVendorTransactions = await _dbContext.CommerceHubFinancialTransactions
            .Where(transaction =>
                ((transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                  transaction.EntityId == tenantId) ||
                 (transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                  (_dbContext.AccountingOutboxMessages.Any(message =>
                       message.TenantId == tenantId &&
                       message.BusinessEntityId == transaction.Id &&
                       message.EventType == "CommerceHubLedgerPosting") ||
                   _dbContext.CommerceHubFinancialTransactions.Any(vendorTransaction =>
                       vendorTransaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                       vendorTransaction.EntityId == tenantId &&
                       vendorTransaction.ReferenceType == transaction.ReferenceType &&
                       vendorTransaction.ReferenceId == transaction.ReferenceId &&
                       vendorTransaction.Currency == transaction.Currency)))) &&
                !_dbContext.JournalEntries.Any(journal =>
                    journal.TenantId == tenantId &&
                    journal.SourceType == "CommerceHubFinancialTransaction" &&
                    journal.SourceId == transaction.Id &&
                    journal.Status == JournalStatus.Posted))
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .ToListAsync();

        foreach (var transaction in unpostedVendorTransactions)
        {
            var journal = JournalEntry.Create(
                tenantId,
                $"JE-TEST-{Guid.NewGuid():N}"[..18],
                DateTime.UtcNow,
                "CommerceHubFinancialTransaction",
                transaction.Id,
                transaction.ReferenceId.ToString(),
                $"Test GL coverage for CommerceHub transaction {transaction.Id}",
                $"TEST-COMMERCEHUB-TX:{transaction.Id:N}",
                userId: null);

            _dbContext.JournalEntries.Add(journal);

            var outboxMessage = await _dbContext.AccountingOutboxMessages
                .FirstOrDefaultAsync(message =>
                    message.TenantId == tenantId &&
                    message.BusinessEntityId == transaction.Id &&
                    message.EventType == "CommerceHubLedgerPosting" &&
                    message.SourceType == "CommerceHubFinancialTransaction");

            // Fail fast if the CommerceHubLedgerPosting outbox row wasn't created
            // This ensures RecordOrderAsync is properly enqueuing the event
            Assert.NotNull(outboxMessage);
            outboxMessage.MarkProcessed(journal.Id);
        }

        using (LedgerMutationScope.Authorize())
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task SeedHealthyFinancialReconciliationAsync(Guid tenantId)
    {
        _dbContext.CommerceHubFinancialReconciliationReports.Add(new CommerceHubFinancialReconciliationReport
        {
            Id = Guid.NewGuid(),
            TimestampUtc = DateTime.UtcNow,
            Status = "Healthy",
            MismatchCount = 0,
            DetailsJson = $$"""{"checkedVendorIds":["{{tenantId:D}}"],"affectedVendorIds":[]}"""
        });

        await _dbContext.SaveChangesAsync();
    }

    private static CommerceHubFinancialTransaction CreateBeforePayoutRefundTransaction(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        Guid refundId) =>
        CommerceHubFinancialTransaction.Create(
            entityType,
            entityId,
            transactionType,
            CommerceHubFinancialTransactionDirection.Debit,
            90m,
            "EGP",
            CommerceHubFinancialReferenceType.Refund,
            refundId,
            "Before-payout refund coverage test transaction.",
            financialBucketOverride: CommerceHubFinancialBucket.Payable);

    private static CommerceHubFinancialTransaction CreatePayoutTransaction(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        Guid payoutId) =>
        CommerceHubFinancialTransaction.Create(
            entityType,
            entityId,
            CommerceHubFinancialTransactionType.Payout,
            CommerceHubFinancialTransactionDirection.Debit,
            90m,
            "EGP",
            CommerceHubFinancialReferenceType.Payout,
            payoutId,
            "Payout coverage test transaction.",
            financialBucketOverride: CommerceHubFinancialBucket.Payable);

    private async Task AddUnscopedUnresolvedPaymentWebhookAsync(
        string status,
        string providerEventId,
        decimal amount,
        string eventType)
    {
        _dbContext.CommerceHubUnresolvedPaymentWebhooks.Add(new CommerceHubUnresolvedPaymentWebhook
        {
            Id = Guid.NewGuid(),
            Provider = PaymentProvider.Stripe,
            ProviderEventId = providerEventId,
            Amount = amount,
            Currency = "EGP",
            EventType = eventType,
            PayloadHash = new string('a', 64),
            RawPayload = "{}",
            Status = status,
            AffectedStoreIdsJson = "[]",
            SuspenseRoutedStoreIdsJson = "[]",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        using (LedgerMutationScope.Authorize())
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private static IOptionsMonitor<CommerceHubEmergencyStopOptions> EmergencyStopOptionsMonitor() =>
        Mock.Of<IOptionsMonitor<CommerceHubEmergencyStopOptions>>(options =>
            options.CurrentValue == new CommerceHubEmergencyStopOptions());

    private sealed record CommerceHubFinancialGraph(
        CommerceHubMarketplaceOrder Order,
        CommerceHubSubOrder SubOrder,
        CommerceHubPayment Payment,
        CommerceHubVendorEarning PrimaryEarning);

    private sealed class LegacyCompatibleFinancialReconciliationService : IFinancialReconciliationService
    {
        private readonly ICommerceHubFinanceReconciliationReadService _readService;
        private readonly ICommerceHubFinancialAuditService _auditService;

        public LegacyCompatibleFinancialReconciliationService(
            ICommerceHubFinanceReconciliationReadService readService,
            ICommerceHubFinancialAuditService auditService)
        {
            _readService = readService;
            _auditService = auditService;
        }

        public async Task<FinancialReconciliationExecutionResult> RunAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = await _readService.GetSnapshotAsync(cancellationToken);
            _auditService.RecordFinanceReconciliation(
                snapshot.Status,
                $"Transactions={snapshot.TotalTransactions}",
                $"finance-reconciliation:{snapshot.ExecutedAtUtc:yyyyMMddHHmmss}");
            await _auditService.PersistAsync(cancellationToken);

            var report = new CommerceHubFinancialReconciliationReport
            {
                Id = Guid.NewGuid(),
                TimestampUtc = snapshot.ExecutedAtUtc,
                Status = snapshot.Status == "OK" ? "Healthy" : "Critical",
                MismatchCount = 0,
                DetailsJson = "{}"
            };

            return new FinancialReconciliationExecutionResult(snapshot, report, 0);
        }
    }
}
