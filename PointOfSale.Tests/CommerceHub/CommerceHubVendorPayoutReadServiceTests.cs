using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;
using System.Reflection;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubVendorPayoutReadServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubVendorPayoutReadService _service;

    public CommerceHubVendorPayoutReadServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _service = new CommerceHubVendorPayoutReadService(
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
    }

    [Fact]
    public async Task GetVendorSnapshotAsync_ShouldCalculateEligibleHoldbackAndRefundAdjustedPayable()
    {
        var vendorId = await SeedVendorAsync("Snapshot Vendor");
        await SeedDefaultPayoutMethodAsync(vendorId);

        await SeedOnlineSubOrderAsync(
            vendorId,
            orderNumber: "CH-SNAP-A",
            vendorEarningAmount: 90m,
            commissionAmount: 10m,
            deliveredAt: DateTime.UtcNow.AddDays(-10),
            itemStatus: CommerceHubOrderItemStatus.Delivered,
            refundedVendorAmount: 20m);

        await SeedOnlineSubOrderAsync(
            vendorId,
            orderNumber: "CH-SNAP-B",
            vendorEarningAmount: 45m,
            commissionAmount: 5m,
            deliveredAt: DateTime.UtcNow.AddDays(-2),
            itemStatus: CommerceHubOrderItemStatus.Delivered);

        await SeedOnlineSubOrderAsync(
            vendorId,
            orderNumber: "CH-SNAP-C",
            vendorEarningAmount: 30m,
            commissionAmount: 10m,
            deliveredAt: null,
            itemStatus: CommerceHubOrderItemStatus.Shipped);

        var snapshot = await _service.GetVendorSnapshotAsync(vendorId);

        Assert.Equal(165m, snapshot.TotalEarnings);
        Assert.Equal(145m, snapshot.TotalPayable);
        Assert.Equal(70m, snapshot.EligibleAmount);
        Assert.Equal(75m, snapshot.HoldbackAmount);
        Assert.Equal("BankTransfer", snapshot.DefaultPayoutMethodType);
        Assert.Contains("RefundWindowOpen", snapshot.RiskFlags);
        Assert.Contains("SettlementDelayActive", snapshot.RiskFlags);
        Assert.Contains("LargePayableDelay", snapshot.RiskFlags);
        Assert.Contains("DeliveryHold", snapshot.RiskFlags);
        Assert.DoesNotContain("NoDefaultPayoutMethod", snapshot.RiskFlags);
        Assert.DoesNotContain("LedgerPayableDrift", snapshot.RiskFlags);
    }

    [Fact]
    public async Task GetVendorSnapshotAsync_ShouldNotReduceOutstandingPayableForPostPayoutRefunds()
    {
        var vendorId = await SeedVendorAsync("Post Payout Refund Vendor");
        await SeedDefaultPayoutMethodAsync(vendorId);

        await SeedOnlineSubOrderAsync(
            vendorId,
            orderNumber: "CH-SNAP-D",
            vendorEarningAmount: 90m,
            commissionAmount: 10m,
            deliveredAt: DateTime.UtcNow.AddDays(-10),
            itemStatus: CommerceHubOrderItemStatus.Delivered,
            refundedVendorAmount: 18m,
            isPostPayoutAdjustment: true);

        await SeedOnlineSubOrderAsync(
            vendorId,
            orderNumber: "CH-SNAP-E",
            vendorEarningAmount: 50m,
            commissionAmount: 5m,
            deliveredAt: DateTime.UtcNow.AddDays(-10),
            itemStatus: CommerceHubOrderItemStatus.Delivered);

        await SeedCompletedPayoutAsync(vendorId, 90m);

        var snapshot = await _service.GetVendorSnapshotAsync(vendorId);

        Assert.Equal(140m, snapshot.TotalEarnings);
        Assert.Equal(50m, snapshot.TotalPayable);
        Assert.Equal(50m, snapshot.EligibleAmount);
        Assert.Equal(0m, snapshot.HoldbackAmount);
    }

    [Fact]
    public async Task GetVendorSnapshotAsync_WhenVendorIsGovernanceSuspended_ShouldExposeGovernanceRiskFlag()
    {
        var vendorId = await SeedVendorAsync("Governance Suspended Vendor");
        await SeedDefaultPayoutMethodAsync(vendorId);
        await SeedOnlineSubOrderAsync(
            vendorId,
            orderNumber: "CH-SNAP-GOV",
            vendorEarningAmount: 90m,
            commissionAmount: 10m,
            deliveredAt: DateTime.UtcNow.AddDays(-10),
            itemStatus: CommerceHubOrderItemStatus.Delivered);

        _dbContext.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = vendorId,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Suspended,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var snapshot = await _service.GetVendorSnapshotAsync(vendorId);

        Assert.Contains("GovernanceSuspended", snapshot.RiskFlags);
    }

    [Fact]
    public async Task GetVendorSnapshotAsync_WhenPriorPayoutNeedsReconciliation_ShouldKeepPositiveEligibilityButFlagQueueBlocker()
    {
        var vendorId = await SeedVendorAsync("Reconciliation Blocked Vendor");
        await SeedDefaultPayoutMethodAsync(vendorId);
        await SeedOnlineSubOrderAsync(
            vendorId,
            orderNumber: "CH-SNAP-RECON-BLOCK",
            vendorEarningAmount: 90m,
            commissionAmount: 10m,
            deliveredAt: DateTime.UtcNow.AddDays(-10),
            itemStatus: CommerceHubOrderItemStatus.Delivered);
        await SeedReconciliationRequiredPayoutAsync(vendorId, 20m);

        var snapshot = await _service.GetVendorSnapshotAsync(vendorId);

        Assert.Equal(70m, snapshot.TotalPayable);
        Assert.Equal(70m, snapshot.EligibleAmount);
        Assert.Equal(20m, snapshot.PendingPayoutsTotal);
        Assert.Contains(PayoutRiskFlags.ExternalTransferReconciliationRequired, snapshot.RiskFlags);
    }

    [Fact]
    public async Task GetVendorSnapshotAsync_WhenOperationalPayableHasNoLedgerBacking_ShouldCapPayableAndFlagDrift()
    {
        var vendorId = await SeedVendorAsync("Ledger Authority Vendor");
        await SeedDefaultPayoutMethodAsync(vendorId);

        await SeedOnlineSubOrderAsync(
            vendorId,
            orderNumber: "CH-SNAP-LEDGER-DRIFT",
            vendorEarningAmount: 90m,
            commissionAmount: 10m,
            deliveredAt: DateTime.UtcNow.AddDays(-10),
            itemStatus: CommerceHubOrderItemStatus.Delivered,
            recordLedger: false);

        var snapshot = await _service.GetVendorSnapshotAsync(vendorId);

        Assert.Equal(90m, snapshot.TotalEarnings);
        Assert.Equal(0m, snapshot.TotalPayable);
        Assert.Equal(0m, snapshot.EligibleAmount);
        Assert.Contains("LedgerPayableDrift", snapshot.RiskFlags);
    }

    [Fact]
    public async Task GetVendorSnapshotAsync_WhenLedgerHasMultipleCurrencies_ShouldBlockPayoutEligibility()
    {
        var vendorId = await SeedVendorAsync("Mixed Ledger Currency Vendor");
        await SeedDefaultPayoutMethodAsync(vendorId);

        await SeedOnlineSubOrderAsync(
            vendorId,
            orderNumber: "CH-SNAP-CURRENCY-LEDGER",
            vendorEarningAmount: 90m,
            commissionAmount: 10m,
            deliveredAt: DateTime.UtcNow.AddDays(-10),
            itemStatus: CommerceHubOrderItemStatus.Delivered);

        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Vendor,
                vendorId,
                CommerceHubFinancialTransactionType.OrderPayment,
                CommerceHubFinancialTransactionDirection.Credit,
                25m,
                "USD",
                CommerceHubFinancialReferenceType.Order,
                Guid.NewGuid(),
                "Mixed currency payable must block payout eligibility"));
        await _dbContext.SaveChangesAsync();

        var snapshot = await _service.GetVendorSnapshotAsync(vendorId);

        Assert.Equal("EGP", snapshot.Currency);
        Assert.Equal(0m, snapshot.TotalPayable);
        Assert.Equal(0m, snapshot.EligibleAmount);
        Assert.Contains(PayoutRiskFlags.LedgerCurrencySplit, snapshot.RiskFlags);
        Assert.Contains(PayoutRiskFlags.PayoutBlockedByAuthorityConflict, snapshot.RiskFlags);
    }

    [Fact]
    public async Task GetVendorSnapshotAsync_WhenOperationalCurrencyDiffersFromLedger_ShouldBlockEligibility()
    {
        var vendorId = await SeedVendorAsync("Operational Currency Conflict Vendor");
        await SeedDefaultPayoutMethodAsync(vendorId);

        await SeedOnlineSubOrderAsync(
            vendorId,
            orderNumber: "CH-SNAP-CURRENCY-EGP",
            vendorEarningAmount: 50m,
            commissionAmount: 5m,
            deliveredAt: DateTime.UtcNow.AddDays(-10),
            itemStatus: CommerceHubOrderItemStatus.Delivered);

        await SeedOnlineSubOrderAsync(
            vendorId,
            orderNumber: "CH-SNAP-CURRENCY-USD",
            vendorEarningAmount: 30m,
            commissionAmount: 3m,
            deliveredAt: DateTime.UtcNow.AddDays(-10),
            itemStatus: CommerceHubOrderItemStatus.Delivered,
            recordLedger: false,
            currency: "USD");

        var snapshot = await _service.GetVendorSnapshotAsync(vendorId);

        Assert.Equal("EGP", snapshot.Currency);
        Assert.Equal(50m, snapshot.TotalPayable);
        Assert.Equal(0m, snapshot.EligibleAmount);
        Assert.Equal(50m, snapshot.HoldbackAmount);
        Assert.Contains(PayoutRiskFlags.OperationalCurrencyMismatch, snapshot.RiskFlags);
        Assert.Contains(PayoutRiskFlags.PayoutBlockedByAuthorityConflict, snapshot.RiskFlags);
    }

    [Fact]
    public async Task GetVendorQueueSnapshotsAsync_WhenVendorHasBadTenantAndLedgerCurrencies_ShouldQuarantineOnlyThatVendor()
    {
        var healthyVendorId = await SeedVendorAsync("Healthy Queue Vendor");
        await SeedDefaultPayoutMethodAsync(healthyVendorId);
        await SeedOnlineSubOrderAsync(
            healthyVendorId,
            orderNumber: "CH-SNAP-QUEUE-HEALTHY",
            vendorEarningAmount: 80m,
            commissionAmount: 8m,
            deliveredAt: DateTime.UtcNow.AddDays(-10),
            itemStatus: CommerceHubOrderItemStatus.Delivered);

        var quarantinedVendorId = await SeedVendorAsync("Quarantined Queue Vendor", currency: "BAD");
        await SeedDefaultPayoutMethodAsync(quarantinedVendorId);
        await SeedOnlineSubOrderAsync(
            quarantinedVendorId,
            orderNumber: "CH-SNAP-QUEUE-BAD",
            vendorEarningAmount: 40m,
            commissionAmount: 4m,
            deliveredAt: DateTime.UtcNow.AddDays(-10),
            itemStatus: CommerceHubOrderItemStatus.Delivered);
        await SeedMalformedVendorLedgerTransactionAsync(quarantinedVendorId, "BAD");

        var queue = await _service.GetVendorQueueSnapshotsAsync();
        var healthySnapshot = Assert.Single(queue);
        var quarantinedSnapshot = await _service.GetVendorSnapshotAsync(quarantinedVendorId);

        Assert.Equal(healthyVendorId, healthySnapshot.VendorId);
        Assert.Equal("EGP", healthySnapshot.Currency);
        Assert.Equal(80m, healthySnapshot.TotalPayable);

        Assert.Equal(string.Empty, quarantinedSnapshot.Currency);
        Assert.Equal(0m, quarantinedSnapshot.TotalPayable);
        Assert.Equal(0m, quarantinedSnapshot.EligibleAmount);
        Assert.Contains(PayoutRiskFlags.LedgerCurrencySplit, quarantinedSnapshot.RiskFlags);
        Assert.Contains(PayoutRiskFlags.PayoutBlockedByAuthorityConflict, quarantinedSnapshot.RiskFlags);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<Guid> SeedVendorAsync(string name, string currency = "EGP")
    {
        var vendorId = Guid.NewGuid();
        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = name,
            Currency = currency,
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
            DestinationValue = "EG-DEFAULT-001",
            IsDefault = true,
            IsVerified = true,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedOnlineSubOrderAsync(
        Guid vendorId,
        string orderNumber,
        decimal vendorEarningAmount,
        decimal commissionAmount,
        DateTime? deliveredAt,
        CommerceHubOrderItemStatus itemStatus,
        decimal refundedVendorAmount = 0m,
        bool isPostPayoutAdjustment = false,
        bool recordLedger = true,
        string currency = "EGP")
    {
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();
        using var refundBoundary = refundedVendorAmount > 0m
            ? CommerceHubBoundaryMutationScope.BeginRefund()
            : null;

        var totalAmount = vendorEarningAmount + commissionAmount;
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            CustomerId = Guid.NewGuid(),
            FullName = "Snapshot Customer",
            PhoneNumber = "01000000000",
            AddressLine = "123 Snapshot Street",
            City = "Cairo",
            Currency = currency,
            PaymentMethodCode = "ONLINE",
            PaymentMethodName = "Online",
            Subtotal = totalAmount,
            ShippingTotal = 0m,
            TotalAmount = totalAmount,
            Status = itemStatus == CommerceHubOrderItemStatus.Delivered ? OrderStatus.Delivered : OrderStatus.Shipped,
            PaymentStatus = PaymentStatus.Settled,
            PaidAt = DateTime.UtcNow.AddDays(-12),
            CreatedAt = DateTime.UtcNow.AddDays(-12)
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"{orderNumber}-1",
            StoreId = vendorId,
            StoreName = "Snapshot Vendor",
            Currency = currency,
            Subtotal = totalAmount,
            Shipping = 0m,
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = "ONLINE",
            PaymentMethodName = "Online",
            PlatformCommissionAmount = commissionAmount,
            VendorEarningAmount = vendorEarningAmount,
            PlatformCommissionRate = totalAmount == 0m ? 0m : commissionAmount / totalAmount,
            Total = totalAmount,
            Status = order.Status,
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
            ProductName = "Snapshot Product",
            Quantity = 1,
            UnitPrice = totalAmount,
            PlatformCommissionRate = subOrder.PlatformCommissionRate,
            PlatformCommissionAmount = commissionAmount,
            Total = totalAmount,
            Status = itemStatus,
            DeliveredAt = deliveredAt,
            CreatedAt = order.CreatedAt
        };

        order.SubOrders.Add(subOrder);
        subOrder.Items.Add(item);

        _dbContext.CommerceHubMarketplaceOrders.Add(order);

        if (refundedVendorAmount > 0m)
        {
            var refundId = Guid.NewGuid();
            _dbContext.CommerceHubRefunds.Add(new CommerceHubRefund
            {
                Id = refundId,
                TenantId = vendorId,
                SubOrderId = subOrder.Id,
                Amount = refundedVendorAmount,
                VendorEarningReversalAmount = refundedVendorAmount,
                CommissionReversalAmount = 0m,
                Currency = currency,
                Status = CommerceHubRefundStatus.Completed,
                Reason = "Snapshot refund",
                IsPostPayoutAdjustment = isPostPayoutAdjustment,
                CreatedAt = DateTime.UtcNow.AddDays(-9)
            });

            if (recordLedger && !isPostPayoutAdjustment)
            {
                _dbContext.CommerceHubFinancialTransactions.Add(
                    CommerceHubFinancialTransaction.Create(
                        CommerceHubFinancialEntityType.Vendor,
                        vendorId,
                        CommerceHubFinancialTransactionType.Refund,
                        CommerceHubFinancialTransactionDirection.Debit,
                        refundedVendorAmount,
                        currency,
                        CommerceHubFinancialReferenceType.Refund,
                        refundId,
                        "Snapshot refund payable reversal"));
            }
        }

        if (recordLedger)
        {
            _dbContext.CommerceHubFinancialTransactions.Add(
                CommerceHubFinancialTransaction.Create(
                    CommerceHubFinancialEntityType.Vendor,
                    vendorId,
                    CommerceHubFinancialTransactionType.OrderPayment,
                    CommerceHubFinancialTransactionDirection.Credit,
                    vendorEarningAmount,
                    currency,
                    CommerceHubFinancialReferenceType.Order,
                    subOrder.Id,
                    "Snapshot order payable"));
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedCompletedPayoutAsync(Guid vendorId, decimal amount)
    {
        using var payoutBoundary = CommerceHubBoundaryMutationScope.BeginPayout();

        var payout = new CommerceHubPayout
        {
            Id = Guid.NewGuid(),
            TenantId = vendorId,
            Amount = amount,
            Currency = "EGP",
            Status = CommerceHubPayoutStatus.Completed,
            Method = CommerceHubPayoutMethod.BankTransfer,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow.AddDays(-8),
            ProcessedAtUtc = DateTime.UtcNow.AddDays(-8)
        };
        _dbContext.CommerceHubPayouts.Add(payout);
        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Vendor,
                vendorId,
                CommerceHubFinancialTransactionType.Payout,
                CommerceHubFinancialTransactionDirection.Debit,
                amount,
                "EGP",
                CommerceHubFinancialReferenceType.Payout,
                payout.Id,
                "Snapshot payout payable release"));

        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedReconciliationRequiredPayoutAsync(Guid vendorId, decimal amount)
    {
        using var payoutBoundary = CommerceHubBoundaryMutationScope.BeginPayout();

        _dbContext.CommerceHubPayouts.Add(new CommerceHubPayout
        {
            Id = Guid.NewGuid(),
            TenantId = vendorId,
            Amount = amount,
            Currency = "EGP",
            Status = CommerceHubPayoutStatus.ReconciliationRequired,
            Method = CommerceHubPayoutMethod.BankTransfer,
            FailureReason = "External transfer outcome is unknown after the processing handoff exceeded the safe window.",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow.AddHours(-4),
            ProcessedAtUtc = DateTime.UtcNow.AddHours(-4)
        });

        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedMalformedVendorLedgerTransactionAsync(Guid vendorId, string currency)
    {
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();

        var transaction = (CommerceHubFinancialTransaction)Activator.CreateInstance(
            typeof(CommerceHubFinancialTransaction),
            nonPublic: true)!;
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.Id), Guid.NewGuid());
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.EntityType), CommerceHubFinancialEntityType.Vendor);
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.EntityId), vendorId);
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.TransactionType), CommerceHubFinancialTransactionType.OrderPayment);
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.Direction), CommerceHubFinancialTransactionDirection.Credit);
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.FinancialBucket), CommerceHubFinancialBucket.Payable);
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.Amount), 5m);
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.Currency), currency);
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.ReferenceType), CommerceHubFinancialReferenceType.Order);
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.ReferenceId), Guid.NewGuid());
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.Description), "Malformed snapshot ledger row");
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.IdempotencyKey), Guid.NewGuid().ToString("N"));
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.CorrelationId), Guid.NewGuid().ToString("N"));
        SetNonPublicProperty(transaction, nameof(CommerceHubFinancialTransaction.CreatedAtUtc), DateTime.UtcNow);

        _dbContext.CommerceHubFinancialTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync();
    }

    private static void SetNonPublicProperty<T>(T instance, string propertyName, object? value)
    {
        var property = typeof(T).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }
}
