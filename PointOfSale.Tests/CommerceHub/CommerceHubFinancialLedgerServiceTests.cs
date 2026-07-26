using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubFinancialLedgerServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubFinancialLedgerService _service;

    public CommerceHubFinancialLedgerServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _service = new CommerceHubFinancialLedgerService(
            _dbContext,
            NullLogger<CommerceHubFinancialLedgerService>.Instance);
    }

    [Fact]
    public async Task RecordOrderAsync_ForOnlineOrder_ShouldCreatePlatformVendorAndCommissionCredits()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-ORD-1");
        var subOrder = CreateSubOrder(order, vendorId: Guid.NewGuid(), "CH-LEDGER-ORD-1-A");

        await _service.RecordOrderAsync(order, subOrder);

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .OrderBy(entry => entry.TransactionType)
            .ToListAsync();

        Assert.Equal(3, entries.Count);

        var platformOrder = Assert.Single(entries.Where(entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.OrderPayment));
        Assert.Equal(CommerceHubFinancialTransactionDirection.Credit, platformOrder.Direction);
        Assert.Equal(110m, platformOrder.Amount);
        Assert.Equal(CommerceHubFinancialBucket.Cash, platformOrder.FinancialBucket);
        Assert.Equal(CommerceHubFinancialReferenceType.Order, platformOrder.ReferenceType);
        Assert.Equal(subOrder.Id, platformOrder.ReferenceId);

        var vendorOrder = Assert.Single(entries.Where(entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.TransactionType == CommerceHubFinancialTransactionType.OrderPayment));
        Assert.Equal(subOrder.StoreId, vendorOrder.EntityId);
        Assert.Equal(CommerceHubFinancialTransactionDirection.Credit, vendorOrder.Direction);
        Assert.Equal(100m, vendorOrder.Amount);
        Assert.Equal(CommerceHubFinancialBucket.Payable, vendorOrder.FinancialBucket);

        var commission = Assert.Single(entries.Where(entry =>
            entry.TransactionType == CommerceHubFinancialTransactionType.Commission));
        Assert.Equal(CommerceHubFinancialEntityType.Platform, commission.EntityType);
        Assert.Equal(CommerceHubFinancialTransactionDirection.Credit, commission.Direction);
        Assert.Equal(10m, commission.Amount);
        Assert.Equal(CommerceHubFinancialBucket.Receivable, commission.FinancialBucket);
    }

    [Fact]
    public async Task RecordOrderAsync_WithPromoDiscount_ShouldCreatePromotionExpenseEntry()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-ORD-PROMO-1");
        order.PromoCode = "SAVE20";
        order.PromoDiscountTotal = 2m;

        var subOrder = CreateSubOrder(
            order,
            vendorId: Guid.NewGuid(),
            orderNumber: "CH-LEDGER-ORD-PROMO-1-A",
            promoDiscountAmount: 2m,
            finalPlatformCommissionAmount: 8m);

        await _service.RecordOrderAsync(order, subOrder);

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .OrderBy(entry => entry.TransactionType)
            .ThenBy(entry => entry.Direction)
            .ToListAsync();

        Assert.Equal(4, entries.Count);

        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Debit &&
            entry.Amount == 2m &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Receivable);
    }

    [Fact]
    public async Task RecordOrderAsync_ForCodOrder_ShouldCreateVendorCollectionAndCommissionDebtEntries()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-COD-1", CommerceHubPaymentMethod.CashOnDelivery);
        var subOrder = CreateSubOrder(order, vendorId: Guid.NewGuid(), "CH-LEDGER-COD-1-A", CommerceHubPaymentMethod.CashOnDelivery);

        await _service.RecordOrderAsync(order, subOrder);

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .OrderBy(entry => entry.TransactionType)
            .ThenBy(entry => entry.Direction)
            .ToListAsync();

        Assert.Equal(3, entries.Count);

        var vendorOrder = Assert.Single(entries.Where(entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.TransactionType == CommerceHubFinancialTransactionType.OrderPayment));
        Assert.Equal(CommerceHubFinancialTransactionDirection.Credit, vendorOrder.Direction);
        Assert.Equal(110m, vendorOrder.Amount);
        Assert.Equal(CommerceHubFinancialBucket.Cash, vendorOrder.FinancialBucket);

        var vendorCommission = Assert.Single(entries.Where(entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Commission));
        Assert.Equal(CommerceHubFinancialTransactionDirection.Debit, vendorCommission.Direction);
        Assert.Equal(10m, vendorCommission.Amount);
        Assert.Equal(CommerceHubFinancialBucket.Receivable, vendorCommission.FinancialBucket);

        var platformCommission = Assert.Single(entries.Where(entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Commission));
        Assert.Equal(CommerceHubFinancialTransactionDirection.Credit, platformCommission.Direction);
        Assert.Equal(10m, platformCommission.Amount);
        Assert.Equal(CommerceHubFinancialBucket.Receivable, platformCommission.FinancialBucket);

        Assert.DoesNotContain(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.OrderPayment);
        Assert.All(entries, entry => Assert.Equal($"Order:{subOrder.Id:N}", entry.CorrelationId));
    }

    [Fact]
    public async Task RecordRefundAsync_ForOnlineOrderBeforePayout_ShouldCreateCashPayableVendorAndCommissionDebits()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-REF-1");
        var subOrder = CreateSubOrder(order, vendorId: Guid.NewGuid(), "CH-LEDGER-REF-1-A");
        var refund = new CommerceHubRefund
        {
            Id = Guid.NewGuid(),
            TenantId = subOrder.StoreId,
            SubOrderId = subOrder.Id,
            Amount = 40m,
            VendorEarningReversalAmount = 36m,
            CommissionReversalAmount = 4m,
            Currency = "EGP",
            Status = CommerceHubRefundStatus.Completed,
            IsPostPayoutAdjustment = false,
            Reason = "Customer refund"
        };

        await _service.RecordRefundAsync(order, subOrder, refund);

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .OrderBy(entry => entry.TransactionType)
            .ToListAsync();

        Assert.Equal(4, entries.Count);
        Assert.All(entries, entry => Assert.Equal(CommerceHubFinancialTransactionDirection.Debit, entry.Direction));

        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Refund &&
            entry.Amount == 40m &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Cash);

        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Adjustment &&
            entry.Amount == 36m &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Payable);

        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.EntityId == subOrder.StoreId &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Refund &&
            entry.Amount == 36m &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Payable);

        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Commission &&
            entry.Amount == 4m &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Receivable);

        Assert.All(entries, entry => Assert.Equal(refund.Id.ToString("D"), entry.CorrelationId));
    }

    [Fact]
    public async Task RecordRefundAsync_WhenPromoDiscountReversalExists_ShouldCreatePromotionCredit()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-REF-PROMO-1");
        order.PromoCode = "SAVE20";
        order.PromoDiscountTotal = 1m;

        var subOrder = CreateSubOrder(
            order,
            vendorId: Guid.NewGuid(),
            orderNumber: "CH-LEDGER-REF-PROMO-1-A",
            promoDiscountAmount: 2m,
            finalPlatformCommissionAmount: 8m);
        var refund = new CommerceHubRefund
        {
            Id = Guid.NewGuid(),
            TenantId = subOrder.StoreId,
            SubOrderId = subOrder.Id,
            Amount = 40m,
            VendorEarningReversalAmount = 36m,
            CommissionReversalAmount = 3m,
            PromoDiscountReversalAmount = 1m,
            Currency = "EGP",
            Status = CommerceHubRefundStatus.Completed,
            IsPostPayoutAdjustment = false,
            Reason = "Customer refund"
        };

        await _service.RecordRefundAsync(order, subOrder, refund);

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .OrderBy(entry => entry.TransactionType)
            .ThenBy(entry => entry.Direction)
            .ToListAsync();

        Assert.Equal(5, entries.Count);
        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Credit &&
            entry.Amount == 1m &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Receivable);
    }

    [Fact]
    public async Task RecordRefundAsync_ForOnlineOrderAfterPayout_ShouldCreatePlatformCashVendorDebtAndCommissionDebits()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-REF-POST-1");
        var subOrder = CreateSubOrder(order, vendorId: Guid.NewGuid(), "CH-LEDGER-REF-POST-1-A");
        var refund = new CommerceHubRefund
        {
            Id = Guid.NewGuid(),
            TenantId = subOrder.StoreId,
            SubOrderId = subOrder.Id,
            Amount = 40m,
            VendorEarningReversalAmount = 36m,
            CommissionReversalAmount = 4m,
            Currency = "EGP",
            Status = CommerceHubRefundStatus.Completed,
            IsPostPayoutAdjustment = true,
            Reason = "Customer refund after payout"
        };

        await _service.RecordRefundAsync(order, subOrder, refund);

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .OrderBy(entry => entry.TransactionType)
            .ToListAsync();

        Assert.Equal(3, entries.Count);
        Assert.All(entries, entry => Assert.Equal(CommerceHubFinancialTransactionDirection.Debit, entry.Direction));

        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Refund &&
            entry.Amount == 40m &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Cash);

        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.EntityId == subOrder.StoreId &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Refund &&
            entry.Amount == 36m &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Receivable);

        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Commission &&
            entry.Amount == 4m &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Receivable);

        Assert.DoesNotContain(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Adjustment);
        Assert.All(entries, entry => Assert.Equal(refund.Id.ToString("D"), entry.CorrelationId));
    }

    [Fact]
    public async Task RecordRefundAsync_ForCodOrder_ShouldReduceVendorDebtAndReversePlatformCommission()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-COD-REF-1", CommerceHubPaymentMethod.CashOnDelivery);
        var subOrder = CreateSubOrder(order, vendorId: Guid.NewGuid(), "CH-LEDGER-COD-REF-1-A", CommerceHubPaymentMethod.CashOnDelivery);
        var refund = new CommerceHubRefund
        {
            Id = Guid.NewGuid(),
            TenantId = subOrder.StoreId,
            SubOrderId = subOrder.Id,
            Amount = 40m,
            VendorEarningReversalAmount = 36m,
            CommissionReversalAmount = 4m,
            Currency = "EGP",
            Status = CommerceHubRefundStatus.Completed,
            Reason = "Customer refund"
        };

        await _service.RecordRefundAsync(order, subOrder, refund);

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .OrderBy(entry => entry.TransactionType)
            .ThenBy(entry => entry.Direction)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Commission &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Credit &&
            entry.Amount == 4m &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Receivable);
        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Commission &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Debit &&
            entry.Amount == 4m &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Receivable);
        Assert.DoesNotContain(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Refund);
        Assert.DoesNotContain(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Refund);
        Assert.All(entries, entry => Assert.Equal(refund.Id.ToString("D"), entry.CorrelationId));
    }

    [Fact]
    public async Task RecordCommissionAsync_ShouldCreatePlatformCommissionEntry()
    {
        var referenceId = Guid.NewGuid();

        await _service.RecordCommissionAsync(
            12.5m,
            "EGP",
            CommerceHubFinancialTransactionDirection.Credit,
            CommerceHubFinancialReferenceType.Order,
            referenceId,
            "Commission applied for CommerceHub test order.",
            accountingTenantId: Guid.NewGuid());

        var entry = await _dbContext.CommerceHubFinancialTransactions.SingleAsync();

        Assert.Equal(CommerceHubFinancialEntityType.Platform, entry.EntityType);
        Assert.Null(entry.EntityId);
        Assert.Equal(CommerceHubFinancialTransactionType.Commission, entry.TransactionType);
        Assert.Equal(CommerceHubFinancialTransactionDirection.Credit, entry.Direction);
        Assert.Equal(12.5m, entry.Amount);
        Assert.Equal(CommerceHubFinancialBucket.Receivable, entry.FinancialBucket);
        Assert.Equal(referenceId, entry.ReferenceId);
        Assert.False(string.IsNullOrWhiteSpace(entry.IdempotencyKey));
        Assert.Equal($"Order:{referenceId:N}", entry.CorrelationId);
    }

    [Fact]
    public async Task RecordTransactionAsync_ShouldBeLedgerIdempotent_ForEquivalentEntries()
    {
        var vendorId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();

        await _service.RecordTransactionAsync(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Adjustment,
            CommerceHubFinancialTransactionDirection.Debit,
            15m,
            "EGP",
            CommerceHubFinancialReferenceType.Adjustment,
            referenceId,
            "Manual debit adjustment.");

        await _service.RecordTransactionAsync(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Adjustment,
            CommerceHubFinancialTransactionDirection.Debit,
            15m,
            "EGP",
            CommerceHubFinancialReferenceType.Adjustment,
            referenceId,
            "Manual debit adjustment.");

        var entries = await _dbContext.CommerceHubFinancialTransactions.ToListAsync();

        var entry = Assert.Single(entries);
        Assert.Equal($"Adjustment:{referenceId:N}", entry.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(entry.IdempotencyKey));
    }

    [Fact]
    public async Task RecordTransactionAsync_WhenCurrencyIsMissing_ShouldRejectBeforeLedgerWrite()
    {
        var exception = await Assert.ThrowsAsync<FinancialCurrencyMissingException>(() =>
            _service.RecordTransactionAsync(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialTransactionDirection.Credit,
                15m,
                " ",
                CommerceHubFinancialReferenceType.Adjustment,
                Guid.NewGuid(),
                "Manual adjustment without currency."));

        Assert.Equal("commercehub.financial_transaction.currency", exception.Component);
        Assert.Empty(await _dbContext.CommerceHubFinancialTransactions.ToListAsync());
    }

    [Fact]
    public async Task RecordTransactionAsync_WhenCurrencyIsInvalid_ShouldRejectBeforeLedgerWrite()
    {
        await Assert.ThrowsAsync<FinancialCurrencyInvalidException>(() =>
            _service.RecordTransactionAsync(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialTransactionDirection.Credit,
                15m,
                "NOT-A-CURRENCY",
                CommerceHubFinancialReferenceType.Adjustment,
                Guid.NewGuid(),
                "Manual adjustment with invalid currency."));

        Assert.Empty(await _dbContext.CommerceHubFinancialTransactions.ToListAsync());
    }

    [Fact]
    public async Task FinancialSafetyGuard_WhenCurrencyIsMissing_ShouldRejectStateCapture()
    {
        var guard = new CommerceHubFinancialSafetyGuardService(_dbContext);

        var exception = await Assert.ThrowsAsync<FinancialCurrencyMissingException>(() =>
            guard.CaptureStateForUpdateAsync(Guid.NewGuid(), " "));

        Assert.Equal("commercehub.financial_guard.currency", exception.Component);
    }

    [Fact]
    public async Task RecordTransactionAsync_ReusedIdempotencyKeyWithDifferentFinancialFingerprint_ShouldThrow()
    {
        var referenceId = Guid.NewGuid();
        const string idempotencyKey = "ledger-collision-key";

        await _service.RecordTransactionAsync(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Adjustment,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            "EGP",
            CommerceHubFinancialReferenceType.Adjustment,
            referenceId,
            "Original adjustment.",
            idempotencyKey: idempotencyKey,
            accountingTenantId: Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordTransactionAsync(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialTransactionDirection.Credit,
                11m,
                "EGP",
                CommerceHubFinancialReferenceType.Adjustment,
                referenceId,
                "Divergent adjustment.",
                idempotencyKey: idempotencyKey,
                accountingTenantId: Guid.NewGuid()));

        Assert.Contains("idempotency key collision", exception.Message, StringComparison.OrdinalIgnoreCase);

        var entry = await _dbContext.CommerceHubFinancialTransactions.SingleAsync();
        Assert.Equal(10m, entry.Amount);
    }

    [Fact]
    public async Task RecordTransactionAsync_DefaultIdempotencyKeyWithDifferentAmount_ShouldThrow()
    {
        var referenceId = Guid.NewGuid();

        await _service.RecordTransactionAsync(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Adjustment,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            "EGP",
            CommerceHubFinancialReferenceType.Adjustment,
            referenceId,
            "Original adjustment.",
            accountingTenantId: Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordTransactionAsync(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialTransactionDirection.Credit,
                11m,
                "EGP",
                CommerceHubFinancialReferenceType.Adjustment,
                referenceId,
                "Divergent adjustment.",
                accountingTenantId: Guid.NewGuid()));

        Assert.Contains("idempotency key collision", exception.Message, StringComparison.OrdinalIgnoreCase);

        var entry = await _dbContext.CommerceHubFinancialTransactions.SingleAsync();
        Assert.Equal(10m, entry.Amount);
    }

    [Fact]
    public async Task RecordTransactionAsync_WhenExistingReferenceHasDifferentDirection_ShouldAppendCorrectEntry()
    {
        var referenceId = Guid.NewGuid();

        await _service.RecordTransactionAsync(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            4m,
            "EGP",
            CommerceHubFinancialReferenceType.Refund,
            referenceId,
            "Incorrect legacy commission direction.",
            accountingTenantId: Guid.NewGuid());

        await _service.RecordTransactionAsync(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            4m,
            "EGP",
            CommerceHubFinancialReferenceType.Refund,
            referenceId,
            "Correct refund commission reversal.",
            accountingTenantId: Guid.NewGuid());

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceType == CommerceHubFinancialReferenceType.Refund && entry.ReferenceId == referenceId)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.Direction == CommerceHubFinancialTransactionDirection.Credit);
        Assert.Contains(entries, entry => entry.Direction == CommerceHubFinancialTransactionDirection.Debit);
    }

    [Fact]
    public async Task RecordPayoutAsync_ShouldCreateBalancedPlatformCashPayableAndVendorPayableReductionEntries()
    {
        var vendorId = Guid.NewGuid();
        var payout = new CommerceHubPayout
        {
            Id = Guid.NewGuid(),
            TenantId = vendorId,
            Amount = 90m,
            Currency = "EGP",
            Status = CommerceHubPayoutStatus.Completed
        };

        await _service.RecordPayoutAsync(payout);

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .OrderBy(entry => entry.EntityType)
            .ThenBy(entry => entry.TransactionType)
            .ToListAsync();

        Assert.Equal(3, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal(90m, entry.Amount);
            Assert.Equal(CommerceHubFinancialReferenceType.Payout, entry.ReferenceType);
            Assert.Equal(payout.Id, entry.ReferenceId);
            Assert.Equal($"Payout:{payout.Id:N}", entry.CorrelationId);
        });

        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.EntityId == null &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Debit &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Cash);

        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.EntityId == null &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Payout &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Debit &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Payable);

        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.EntityId == vendorId &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Payout &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Debit &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Payable);
    }

    [Fact]
    public async Task RecordPayoutAsync_WhenPayoutIsNotCompleted_ShouldRejectBeforeLedgerWrite()
    {
        var payout = new CommerceHubPayout
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Amount = 90m,
            Currency = "EGP",
            Status = CommerceHubPayoutStatus.Processing
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordPayoutAsync(payout));

        Assert.Contains("Payout must be completed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _dbContext.CommerceHubFinancialTransactions.ToListAsync());
    }

    [Fact]
    public async Task RecordOrderAsync_WhenSubOrderBelongsToDifferentMarketplaceOrder_ShouldRejectBeforeLedgerWrite()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-ORD-MISMATCH-1");
        var subOrder = CreateSubOrder(order, Guid.NewGuid(), "CH-LEDGER-ORD-MISMATCH-1-A");
        subOrder.MarketplaceOrderId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordOrderAsync(order, subOrder));

        Assert.Contains("Sub-order must belong", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _dbContext.CommerceHubFinancialTransactions.ToListAsync());
    }

    [Fact]
    public async Task RecordRefundAsync_WhenRefundIsNotCompleted_ShouldRejectBeforeLedgerWrite()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-REF-PENDING-1");
        var subOrder = CreateSubOrder(order, Guid.NewGuid(), "CH-LEDGER-REF-PENDING-1-A");
        var refund = new CommerceHubRefund
        {
            Id = Guid.NewGuid(),
            TenantId = subOrder.StoreId,
            SubOrderId = subOrder.Id,
            Amount = 40m,
            VendorEarningReversalAmount = 36m,
            CommissionReversalAmount = 4m,
            Currency = "EGP",
            Status = CommerceHubRefundStatus.Processing,
            Reason = "Pending refund"
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordRefundAsync(order, subOrder, refund));

        Assert.Contains("Refund must be completed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _dbContext.CommerceHubFinancialTransactions.ToListAsync());
    }

    [Fact]
    public async Task RecordRefundAsync_WhenRefundTenantDiffersFromSubOrderVendor_ShouldRejectBeforeLedgerWrite()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-REF-TENANT-MISMATCH-1");
        var subOrder = CreateSubOrder(order, Guid.NewGuid(), "CH-LEDGER-REF-TENANT-MISMATCH-1-A");
        var refund = new CommerceHubRefund
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            Amount = 40m,
            VendorEarningReversalAmount = 36m,
            CommissionReversalAmount = 4m,
            Currency = "EGP",
            Status = CommerceHubRefundStatus.Completed,
            Reason = "Mismatched refund"
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordRefundAsync(order, subOrder, refund));

        Assert.Contains("Refund tenant must match", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _dbContext.CommerceHubFinancialTransactions.ToListAsync());
    }

    [Fact]
    public async Task RecordOrderAsync_WhenReferenceHasPartialLedger_ShouldAppendMissingEntries()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-PARTIAL-ORD-1");
        var subOrder = CreateSubOrder(order, vendorId: Guid.NewGuid(), "CH-LEDGER-PARTIAL-ORD-1-A");

        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Commission,
                CommerceHubFinancialTransactionDirection.Credit,
                subOrder.PlatformCommissionAmount,
                subOrder.Currency,
                CommerceHubFinancialReferenceType.Order,
                subOrder.Id,
                "Previously committed commission row."));
        await _dbContext.SaveChangesAsync();

        await _service.RecordOrderAsync(order, subOrder);

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceType == CommerceHubFinancialReferenceType.Order && entry.ReferenceId == subOrder.Id)
            .ToListAsync();

        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.OrderPayment);
        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.TransactionType == CommerceHubFinancialTransactionType.OrderPayment);
        Assert.Single(entries.Where(entry => entry.TransactionType == CommerceHubFinancialTransactionType.Commission));
    }

    [Fact]
    public async Task RecordRefundAsync_WhenReferenceHasPartialLedger_ShouldAppendMissingEntries()
    {
        var order = CreateMarketplaceOrder("CH-LEDGER-PARTIAL-REF-1");
        var subOrder = CreateSubOrder(order, vendorId: Guid.NewGuid(), "CH-LEDGER-PARTIAL-REF-1-A");
        var refund = new CommerceHubRefund
        {
            Id = Guid.NewGuid(),
            TenantId = subOrder.StoreId,
            SubOrderId = subOrder.Id,
            Amount = 40m,
            VendorEarningReversalAmount = 36m,
            CommissionReversalAmount = 4m,
            Currency = "EGP",
            Status = CommerceHubRefundStatus.Completed,
            IsPostPayoutAdjustment = false,
            Reason = "Partial ledger recovery"
        };

        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Refund,
                CommerceHubFinancialTransactionDirection.Debit,
                refund.Amount,
                refund.Currency,
                CommerceHubFinancialReferenceType.Refund,
                refund.Id,
                "Previously committed refund cash row.",
                idempotencyKey: $"commercehub:refund:{refund.Id:N}:platform-cash",
                correlationId: refund.Id.ToString("D"),
                reason: refund.Reason,
                financialBucketOverride: CommerceHubFinancialBucket.Cash));
        await _dbContext.SaveChangesAsync();

        await _service.RecordRefundAsync(order, subOrder, refund);

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceType == CommerceHubFinancialReferenceType.Refund && entry.ReferenceId == refund.Id)
            .ToListAsync();

        Assert.Equal(4, entries.Count);
        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Adjustment &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Payable);
        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Refund &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Payable);
        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Commission);
        Assert.Single(entries.Where(entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Refund));
    }

    [Fact]
    public async Task RecordPayoutAsync_WhenReferenceHasPartialLedger_ShouldAppendMissingEntries()
    {
        var vendorId = Guid.NewGuid();
        var payout = new CommerceHubPayout
        {
            Id = Guid.NewGuid(),
            TenantId = vendorId,
            Amount = 90m,
            Currency = "EGP",
            Status = CommerceHubPayoutStatus.Completed
        };

        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Settlement,
                CommerceHubFinancialTransactionDirection.Debit,
                payout.Amount,
                payout.Currency,
                CommerceHubFinancialReferenceType.Payout,
                payout.Id,
                "Previously committed payout cash row."));
        await _dbContext.SaveChangesAsync();

        await _service.RecordPayoutAsync(payout);

        var entries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceType == CommerceHubFinancialReferenceType.Payout && entry.ReferenceId == payout.Id)
            .ToListAsync();

        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Payout &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Payable);
        Assert.Contains(entries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Payout &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Payable);
        Assert.Single(entries.Where(entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Settlement));
    }

    [Fact]
    public async Task ApplicationDbContext_ShouldRejectCommerceHubFinancialTransactionUpdates()
    {
        await _service.RecordTransactionAsync(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Adjustment,
            CommerceHubFinancialTransactionDirection.Credit,
            15m,
            "EGP",
            CommerceHubFinancialReferenceType.Payout,
            Guid.NewGuid(),
            "Manual ledger adjustment.",
            accountingTenantId: Guid.NewGuid());

        var entry = await _dbContext.CommerceHubFinancialTransactions.SingleAsync();
        _dbContext.Entry(entry).Property(nameof(CommerceHubFinancialTransaction.Description)).CurrentValue = "Tampered description";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _dbContext.SaveChangesAsync());

        Assert.Contains("append-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static CommerceHubMarketplaceOrder CreateMarketplaceOrder(
        string orderNumber,
        CommerceHubPaymentMethod paymentMethod = CommerceHubPaymentMethod.Online)
    {
        return new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            FullName = "Ledger Customer",
            PhoneNumber = "01000000000",
            AddressLine = "1 Ledger Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = paymentMethod == CommerceHubPaymentMethod.CashOnDelivery ? "COD" : "ONLINE",
            PaymentMethodName = paymentMethod == CommerceHubPaymentMethod.CashOnDelivery ? "Cash on Delivery" : "Online",
            Subtotal = 100m,
            ShippingTotal = 10m,
            TotalAmount = 110m
        };
    }

    private static CommerceHubSubOrder CreateSubOrder(
        CommerceHubMarketplaceOrder order,
        Guid vendorId,
        string orderNumber,
        CommerceHubPaymentMethod paymentMethod = CommerceHubPaymentMethod.Online,
        decimal promoDiscountAmount = 0m,
        decimal? finalPlatformCommissionAmount = null)
    {
        var grossCommissionAmount = 10m;
        return new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = orderNumber,
            StoreId = vendorId,
            StoreName = "Ledger Vendor",
            Currency = "EGP",
            Subtotal = 100m,
            Shipping = 10m,
            PaymentMethod = paymentMethod,
            PaymentMethodCode = paymentMethod == CommerceHubPaymentMethod.CashOnDelivery ? "COD" : "ONLINE",
            PaymentMethodName = paymentMethod == CommerceHubPaymentMethod.CashOnDelivery ? "Cash on Delivery" : "Online",
            PlatformCommissionAmount = grossCommissionAmount,
            FinalPlatformCommissionAmount = finalPlatformCommissionAmount ?? grossCommissionAmount,
            PromoDiscountAmount = promoDiscountAmount,
            VendorEarningAmount = 100m,
            PlatformCommissionRate = 0.1m,
            Total = 110m
        };
    }
}
