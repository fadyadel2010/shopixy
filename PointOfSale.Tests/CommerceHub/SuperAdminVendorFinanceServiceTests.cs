using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class SuperAdminVendorFinanceServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly SuperAdminVendorFinanceService _service;
    private readonly Guid _actorId = Guid.NewGuid();

    public SuperAdminVendorFinanceServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        var walletService = new CommerceHubWalletService(_dbContext);
        var debtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        var dashboardService = new CommerceHubFinancialDashboardService(_dbContext, walletService, debtService);
        var payoutReadService = new CommerceHubVendorPayoutReadService(
            _dbContext,
            Options.Create(new RefundSettings
            {
                RefundWindowDays = 7
            }),
            Options.Create(new CommerceHubPayoutSettings
            {
                PayoutDelayDays = 3,
                LargePayableDelayDays = 1,
                LargePayableAlertAmount = 50m
            }),
            NullLogger<CommerceHubVendorPayoutReadService>.Instance);
        var ledgerService = new CommerceHubFinancialLedgerService(
            _dbContext,
            NullLogger<CommerceHubFinancialLedgerService>.Instance);

        _service = new SuperAdminVendorFinanceService(
            _dbContext,
            dashboardService,
            debtService,
            payoutReadService,
            ledgerService,
            NullLogger<SuperAdminVendorFinanceService>.Instance);
    }

    [Fact]
    public async Task GetVendorsAsync_ShouldComputeLedgerAggregatesAndRiskFlags()
    {
        var atRiskVendorId = await SeedVendorAsync("Alpha Vendor");
        var suspendedVendorId = await SeedVendorAsync("Bravo Vendor");

        var orderReference = Guid.NewGuid();
        var refundReference = Guid.NewGuid();

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            atRiskVendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            100m,
            CommerceHubFinancialReferenceType.Order,
            orderReference,
            new DateTime(2026, 3, 25, 8, 0, 0, DateTimeKind.Utc));
        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            atRiskVendorId,
            CommerceHubFinancialTransactionType.Payout,
            CommerceHubFinancialTransactionDirection.Debit,
            30m,
            CommerceHubFinancialReferenceType.Payout,
            Guid.NewGuid(),
            new DateTime(2026, 3, 25, 8, 30, 0, DateTimeKind.Utc));
        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            atRiskVendorId,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            10m,
            CommerceHubFinancialReferenceType.Refund,
            refundReference,
            new DateTime(2026, 3, 25, 9, 0, 0, DateTimeKind.Utc));
        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            atRiskVendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            18m,
            CommerceHubFinancialReferenceType.Order,
            orderReference,
            new DateTime(2026, 3, 25, 9, 1, 0, DateTimeKind.Utc));
        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            20m,
            CommerceHubFinancialReferenceType.Order,
            orderReference,
            new DateTime(2026, 3, 25, 8, 5, 0, DateTimeKind.Utc));
        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            2m,
            CommerceHubFinancialReferenceType.Refund,
            refundReference,
            new DateTime(2026, 3, 25, 9, 5, 0, DateTimeKind.Utc));

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            suspendedVendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            1200m,
            CommerceHubFinancialReferenceType.Order,
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(-5));

        await _dbContext.SaveChangesAsync();

        var result = await _service.GetVendorsAsync(new SuperAdminVendorFinanceListQuery
        {
            Page = 1,
            PageSize = 20,
            SortBy = SuperAdminVendorFinanceSortField.VendorName,
            SortDirection = SuperAdminSortDirection.Asc
        });

        Assert.Equal(2, result.TotalCount);

        var atRiskVendor = Assert.Single(result.Items.Where(item => item.VendorId == atRiskVendorId));
        Assert.Equal(42m, atRiskVendor.CurrentBalance);
        Assert.Equal(16m, atRiskVendor.AmountOwedByVendor);
        Assert.Equal(100m, atRiskVendor.TotalEarnings);
        Assert.Equal(18m, atRiskVendor.TotalCommissions);
        Assert.Equal(10m, atRiskVendor.TotalRefunds);
        Assert.Equal(30m, atRiskVendor.TotalPayouts);
        Assert.Equal(0.1m, atRiskVendor.RefundRatio);
        Assert.Equal("AT_RISK", atRiskVendor.Status);
        Assert.True(atRiskVendor.IsAtRisk);
        Assert.False(atRiskVendor.IsSuspended);

        var suspendedVendor = Assert.Single(result.Items.Where(item => item.VendorId == suspendedVendorId));
        Assert.Equal(-1200m, suspendedVendor.CurrentBalance);
        Assert.Equal(1200m, suspendedVendor.AmountOwedByVendor);
        Assert.Equal("SUSPENDED", suspendedVendor.Status);
        Assert.True(suspendedVendor.IsSuspended);
        Assert.False(suspendedVendor.IsAtRisk);
    }

    [Fact]
    public async Task GetVendorsAsync_ShouldIncludeVendorWithoutTransactions()
    {
        var vendorId = await SeedVendorAsync("Idle Vendor");

        var result = await _service.GetVendorsAsync(new SuperAdminVendorFinanceListQuery
        {
            Page = 1,
            PageSize = 20
        });

        var vendor = Assert.Single(result.Items.Where(item => item.VendorId == vendorId));
        Assert.Equal(0m, vendor.CurrentBalance);
        Assert.Equal(0m, vendor.TotalEarnings);
        Assert.Equal(0m, vendor.TotalCommissions);
        Assert.Equal(0m, vendor.TotalRefunds);
        Assert.Equal(0m, vendor.TotalPayouts);
        Assert.Equal("ACTIVE", vendor.Status);
        Assert.True(vendor.IsInactive);
    }

    [Fact]
    public async Task RecordSettlementAsync_ShouldBeIdempotentAndAvoidDuplicateLedgerRows()
    {
        var vendorId = await SeedVendorAsync("Settlement Vendor");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            300m,
            CommerceHubFinancialReferenceType.Order);

        await _dbContext.SaveChangesAsync();

        var request = new SuperAdminVendorSettlementRequestDto
        {
            Amount = 120m,
            Flow = SuperAdminVendorSettlementFlow.VendorPaysPlatform,
            Currency = "EGP",
            IdempotencyKey = "settlement-001",
            Reason = "Vendor transferred part of the debt."
        };

        var first = await _service.RecordSettlementAsync(vendorId, request, _actorId);
        var second = await _service.RecordSettlementAsync(vendorId, request, _actorId);

        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceId == first.Action.ReferenceId)
            .ToListAsync();

        Assert.Equal(first.Action.ReferenceId, second.Action.ReferenceId);
        Assert.Equal(first.Action.CorrelationId, second.Action.CorrelationId);
        Assert.Equal("settlement-001", first.Action.IdempotencyKey);
        Assert.Equal(_actorId, first.Action.CreatedBy);
        Assert.Equal(2, ledgerEntries.Count);
        Assert.Equal(-180m, first.Vendor.CurrentBalance);
        Assert.Equal(180m, first.Vendor.AmountOwedByVendor);
        Assert.Equal(-180m, second.Vendor.CurrentBalance);
        Assert.All(ledgerEntries, entry => Assert.Equal(_actorId, entry.CreatedBy));
    }

    [Fact]
    public async Task RecordSettlementAsync_ReusedIdempotencyKeyWithDifferentPayload_ShouldConflict()
    {
        var vendorId = await SeedVendorAsync("Conflict Vendor");

        var initial = new SuperAdminVendorSettlementRequestDto
        {
            Amount = 90m,
            Flow = SuperAdminVendorSettlementFlow.VendorPaysPlatform,
            Currency = "EGP",
            IdempotencyKey = "settlement-conflict",
            Reason = "Initial settlement"
        };

        await _service.RecordSettlementAsync(vendorId, initial, _actorId);

        var exception = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            _service.RecordSettlementAsync(
                vendorId,
                new SuperAdminVendorSettlementRequestDto
                {
                    Amount = 150m,
                    Flow = SuperAdminVendorSettlementFlow.VendorPaysPlatform,
                    Currency = "EGP",
                    IdempotencyKey = "settlement-conflict",
                    Reason = "Changed amount"
                },
                _actorId));

        Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD", exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(2, await _dbContext.CommerceHubFinancialTransactions.CountAsync());
    }

    [Fact]
    public async Task RecordSettlementAsync_PlatformPaysVendor_ShouldRequirePayoutExecutionWorkflow()
    {
        var vendorId = await SeedVendorAsync("Payout Bypass Vendor");

        var exception = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            _service.RecordSettlementAsync(
                vendorId,
                new SuperAdminVendorSettlementRequestDto
                {
                    Amount = 90m,
                    Flow = SuperAdminVendorSettlementFlow.PlatformPaysVendor,
                    Currency = "EGP",
                    IdempotencyKey = "platform-pays-vendor-bypass",
                    Reason = "Attempt direct outward settlement"
                },
                _actorId));

        Assert.Equal("PAYOUT_EXECUTION_WORKFLOW_REQUIRED", exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.Empty(await _dbContext.CommerceHubFinancialTransactions.ToListAsync());
    }

    [Fact]
    public async Task RecordAdjustmentAsync_ShouldCreateBalancedVendorAndPlatformEntries()
    {
        var vendorId = await SeedVendorAsync("Adjustment Vendor");

        var result = await _service.RecordAdjustmentAsync(
            vendorId,
            new SuperAdminVendorAdjustmentRequestDto
            {
                Amount = 75m,
                Direction = CommerceHubFinancialTransactionDirection.Debit,
                Currency = "EGP",
                IdempotencyKey = "adjustment-001",
                Reason = "Manual chargeback"
            },
            _actorId);

        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceId == result.Action.ReferenceId)
            .ToListAsync();

        Assert.Equal(-75m, result.Vendor.CurrentBalance);
        Assert.True(result.Vendor.IsAtRisk);
        Assert.False(result.Vendor.IsSuspended);
        Assert.Equal("Manual chargeback", result.Action.Reason);
        Assert.Equal("adjustment-001", result.Action.IdempotencyKey);
        Assert.Equal(_actorId, result.Action.CreatedBy);
        Assert.Equal(2, ledgerEntries.Count);
        Assert.Contains(ledgerEntries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Adjustment &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Debit &&
            entry.Amount == 75m);
        Assert.Contains(ledgerEntries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Adjustment &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Credit &&
            entry.Amount == 75m);
        Assert.All(ledgerEntries, entry => Assert.Equal(_actorId, entry.CreatedBy));
    }

    [Fact]
    public async Task GetVendorAuditAsync_ShouldReturnAdminLedgerTimeline()
    {
        var vendorId = await SeedVendorAsync("Timeline Vendor");

        await _service.RecordAdjustmentAsync(
            vendorId,
            new SuperAdminVendorAdjustmentRequestDto
            {
                Amount = 40m,
                Direction = CommerceHubFinancialTransactionDirection.Credit,
                Currency = "EGP",
                IdempotencyKey = "audit-adjustment",
                Reason = "Manual goodwill credit"
            },
            _actorId);

        await _service.RecordSettlementAsync(
            vendorId,
            new SuperAdminVendorSettlementRequestDto
            {
                Amount = 25m,
                Flow = SuperAdminVendorSettlementFlow.VendorPaysPlatform,
                Currency = "EGP",
                IdempotencyKey = "audit-settlement",
                Reason = "Vendor bank transfer"
            },
            _actorId);

        var result = await _service.GetVendorAuditAsync(
            vendorId,
            new SuperAdminVendorFinanceAuditQuery
            {
                Page = 1,
                PageSize = 10
            });

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, item => item.ActionType == "Adjustment" && item.Reason == "Manual goodwill credit");
        Assert.Contains(result.Items, item => item.ActionType == "Settlement" && item.Reason == "Vendor bank transfer");
        Assert.All(result.Items, item => Assert.Equal(_actorId, item.ActorId));
    }

    [Fact]
    public async Task GetAlertsAsync_ShouldReturnDebtAndRefundRatioSignals()
    {
        var suspendedVendorId = await SeedVendorAsync("Debt Vendor");
        var refundVendorId = await SeedVendorAsync("Refund Vendor");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            suspendedVendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            1200m,
            CommerceHubFinancialReferenceType.Order,
            createdAtUtc: DateTime.UtcNow.AddDays(-5));

        var orderReference = Guid.NewGuid();
        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            refundVendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            100m,
            CommerceHubFinancialReferenceType.Order,
            orderReference);
        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            refundVendorId,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            35m,
            CommerceHubFinancialReferenceType.Refund,
            Guid.NewGuid());

        await _dbContext.SaveChangesAsync();

        var result = await _service.GetAlertsAsync(
            new SuperAdminVendorFinanceAlertsQuery
            {
                Page = 1,
                PageSize = 10,
                HighRefundRatioThreshold = 0.25m
            });

        Assert.Contains(result.Items, item =>
            item.VendorId == suspendedVendorId &&
            item.AlertType == "SUSPENDED_DEBT" &&
            item.Severity == "CRITICAL");
        Assert.Contains(result.Items, item =>
            item.VendorId == refundVendorId &&
            item.AlertType == "HIGH_REFUND_RATIO" &&
            item.Severity == "WARNING");
    }

    [Fact]
    public async Task GetAlertsAsync_ShouldReturnLargePayableDelaySignal()
    {
        var vendorId = await SeedVendorAsync("Delayed Payable Vendor");
        await SeedDefaultPayoutMethodAsync(vendorId);
        await SeedDelayedOnlineSubOrderAsync(vendorId, "CH-DELAY-1", 90m, 10m, DateTime.UtcNow.AddDays(-10));

        var result = await _service.GetAlertsAsync(
            new SuperAdminVendorFinanceAlertsQuery
            {
                Page = 1,
                PageSize = 10
            });

        Assert.Contains(result.Items, item =>
            item.VendorId == vendorId &&
            item.AlertType == "LARGE_PAYABLE_DELAY" &&
            item.Severity == "CRITICAL");
    }

    [Fact]
    public async Task RecordSettlementAsync_ShouldFailWhenCorrelationIntegrityIsBroken()
    {
        var vendorId = await SeedVendorAsync("Integrity Vendor");
        var request = new SuperAdminVendorSettlementRequestDto
        {
            Amount = 90m,
            Flow = SuperAdminVendorSettlementFlow.VendorPaysPlatform,
            Currency = "EGP",
            IdempotencyKey = "settlement-integrity",
            Reason = "Initial settlement"
        };

        var initial = await _service.RecordSettlementAsync(vendorId, request, _actorId);

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Settlement,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            CommerceHubFinancialReferenceType.Settlement,
            Guid.NewGuid(),
            correlationId: initial.Action.CorrelationId,
            idempotencyKey: "rogue-settlement-platform",
            createdBy: _actorId,
            reason: "Rogue duplicate");

        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            _service.RecordSettlementAsync(vendorId, request, _actorId));

        Assert.Equal("FINANCIAL_INTEGRITY_VIOLATION", exception.Code);
    }

    [Fact]
    public void CommerceHubFinancialTransaction_Create_ShouldRejectUnknownTransactionType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Vendor,
                Guid.NewGuid(),
                (CommerceHubFinancialTransactionType)999,
                CommerceHubFinancialTransactionDirection.Credit,
                10m,
                "EGP",
                CommerceHubFinancialReferenceType.Adjustment,
                Guid.NewGuid(),
                "Invalid transaction type"));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<Guid> SeedVendorAsync(string name)
    {
        var vendorId = Guid.NewGuid();
        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = name,
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        await _dbContext.SaveChangesAsync();
        return vendorId;
    }

    private async Task SeedDefaultPayoutMethodAsync(Guid vendorId)
    {
        _dbContext.CommerceHubVendorPayoutMethods.Add(new CommerceHubVendorPayoutMethod
        {
            Id = Guid.NewGuid(),
            VendorId = vendorId,
            MethodType = CommerceHubPayoutMethod.BankTransfer,
            DestinationValue = "EG-DELAY-DEFAULT",
            IsDefault = true,
            IsVerified = true,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedDelayedOnlineSubOrderAsync(
        Guid vendorId,
        string orderNumber,
        decimal vendorEarningAmount,
        decimal commissionAmount,
        DateTime deliveredAtUtc)
    {
        var totalAmount = vendorEarningAmount + commissionAmount;
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            CustomerId = Guid.NewGuid(),
            FullName = "Delayed Customer",
            PhoneNumber = "01000000000",
            AddressLine = "123 Delay Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "ONLINE",
            PaymentMethodName = "Online",
            Subtotal = totalAmount,
            ShippingTotal = 0m,
            TotalAmount = totalAmount,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled,
            PaidAt = deliveredAtUtc.AddDays(-1),
            CreatedAt = deliveredAtUtc.AddDays(-3)
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"{orderNumber}-1",
            StoreId = vendorId,
            StoreName = "Delayed Vendor",
            Currency = "EGP",
            Subtotal = totalAmount,
            Shipping = 0m,
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = "ONLINE",
            PaymentMethodName = "Online",
            PlatformCommissionAmount = commissionAmount,
            VendorEarningAmount = vendorEarningAmount,
            PlatformCommissionRate = totalAmount == 0m ? 0m : commissionAmount / totalAmount,
            Total = totalAmount,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled,
            PaidAt = order.PaidAt,
            CreatedAt = order.CreatedAt
        };

        var item = new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Delayed Product",
            Quantity = 1,
            UnitPrice = totalAmount,
            PlatformCommissionRate = subOrder.PlatformCommissionRate,
            PlatformCommissionAmount = commissionAmount,
            Total = totalAmount,
            Status = CommerceHubOrderItemStatus.Delivered,
            DeliveredAt = deliveredAtUtc,
            CreatedAt = order.CreatedAt
        };

        order.SubOrders.Add(subOrder);
        subOrder.Items.Add(item);

        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        {
            await _dbContext.SaveChangesAsync();
        }

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            vendorEarningAmount,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            deliveredAtUtc.AddDays(-1),
            $"delayed-order:{subOrder.Id:N}",
            $"delayed-order:{subOrder.Id:N}");

        await _dbContext.SaveChangesAsync();
    }

    private void SeedTransaction(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        CommerceHubFinancialReferenceType referenceType,
        Guid? referenceId = null,
        DateTime? createdAtUtc = null,
        string? correlationId = null,
        string? idempotencyKey = null,
        Guid? createdBy = null,
        string? reason = null)
    {
        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                entityType,
                entityId,
                transactionType,
                direction,
                amount,
                "EGP",
                referenceType,
                referenceId ?? Guid.NewGuid(),
                $"{transactionType} {direction}",
                idempotencyKey: idempotencyKey,
                correlationId: correlationId,
                createdAtUtc: createdAtUtc,
                createdBy: createdBy,
                reason: reason));
    }
}
