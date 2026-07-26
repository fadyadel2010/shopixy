using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub.FinancialLedger;

public interface ICommerceHubFinancialLedgerService
{
    Task RecordTransactionAsync(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        string currency,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        string description,
        string? idempotencyKey = null,
        string? correlationId = null,
        Guid? createdBy = null,
        string? reason = null,
        CommerceHubFinancialBucket? financialBucketOverride = null,
        Guid? accountingTenantId = null,
        CancellationToken cancellationToken = default);

    Task RecordOrderAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        CancellationToken cancellationToken = default);

    Task RecordRefundAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        CommerceHubRefund refund,
        CancellationToken cancellationToken = default);

    Task RecordCommissionAsync(
        decimal amount,
        string currency,
        CommerceHubFinancialTransactionDirection direction,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        string description,
        string? idempotencyKey = null,
        string? correlationId = null,
        Guid? accountingTenantId = null,
        CancellationToken cancellationToken = default);

    Task RecordPayoutAsync(
        CommerceHubPayout payout,
        CancellationToken cancellationToken = default);

    Task RecordMirroredTransactionsAsync(
        Guid vendorId,
        decimal amount,
        string currency,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        CommerceHubFinancialTransactionType vendorTransactionType,
        CommerceHubFinancialTransactionDirection vendorDirection,
        CommerceHubFinancialBucket vendorBucket,
        string vendorDescription,
        CommerceHubFinancialTransactionType platformTransactionType,
        CommerceHubFinancialTransactionDirection platformDirection,
        CommerceHubFinancialBucket platformBucket,
        string platformDescription,
        string? correlationId = null,
        string? vendorIdempotencyKey = null,
        string? platformIdempotencyKey = null,
        Guid? createdBy = null,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task RecordAdjustmentEntriesAsync(
        Guid vendorId,
        decimal amount,
        string currency,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        CommerceHubFinancialTransactionDirection vendorDirection,
        CommerceHubFinancialTransactionDirection platformDirection,
        string vendorDescription,
        string platformDescription,
        CommerceHubFinancialTransactionType transactionType = CommerceHubFinancialTransactionType.Adjustment,
        string? correlationId = null,
        Guid? createdBy = null,
        string? reason = null,
        CancellationToken cancellationToken = default);
}

public class CommerceHubFinancialLedgerService : ICommerceHubFinancialLedgerService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CommerceHubFinancialLedgerService> _logger;

    public CommerceHubFinancialLedgerService(
        ApplicationDbContext dbContext,
        ILogger<CommerceHubFinancialLedgerService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task RecordTransactionAsync(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        string currency,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        string description,
        string? idempotencyKey = null,
        string? correlationId = null,
        Guid? createdBy = null,
        string? reason = null,
        CommerceHubFinancialBucket? financialBucketOverride = null,
        Guid? accountingTenantId = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAtomicAsync(
            async shouldPersist =>
            {
                await AppendTransactionIfMissingAsync(
                    entityType,
                    entityId,
                    transactionType,
                    direction,
                    amount,
                    currency,
                    referenceType,
                    referenceId,
                    description,
                    idempotencyKey,
                    correlationId,
                    createdBy,
                    reason,
                    financialBucketOverride,
                    accountingTenantId,
                    cancellationToken);

                if (shouldPersist)
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task RecordOrderAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marketplaceOrder);
        ArgumentNullException.ThrowIfNull(subOrder);
        ValidateOrderFinancialIntegrity(marketplaceOrder, subOrder);

        var correlationId = BuildCorrelationId(CommerceHubFinancialReferenceType.Order, subOrder.Id);
        var isCod = subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery;

        await ExecuteAtomicAsync(
            async shouldPersist =>
            {
                await AppendTransactionIfMissingAsync(
                    CommerceHubFinancialEntityType.Vendor,
                    subOrder.StoreId,
                    CommerceHubFinancialTransactionType.OrderPayment,
                    CommerceHubFinancialTransactionDirection.Credit,
                    isCod ? subOrder.Total : subOrder.VendorEarningAmount,
                    subOrder.Currency,
                    CommerceHubFinancialReferenceType.Order,
                    subOrder.Id,
                    isCod
                        ? BuildVendorCodOrderDescription(subOrder.OrderNumber)
                        : BuildVendorOrderDescription(subOrder.OrderNumber),
                    financialBucketOverride: isCod
                        ? CommerceHubFinancialBucket.Cash
                        : null,
                    correlationId: correlationId,
                    cancellationToken: cancellationToken);

                if (!isCod)
                {
                    await AppendTransactionIfMissingAsync(
                        CommerceHubFinancialEntityType.Platform,
                        null,
                        CommerceHubFinancialTransactionType.OrderPayment,
                        CommerceHubFinancialTransactionDirection.Credit,
                        subOrder.Total,
                        subOrder.Currency,
                        CommerceHubFinancialReferenceType.Order,
                        subOrder.Id,
                        BuildPlatformOrderDescription(subOrder.OrderNumber),
                        correlationId: correlationId,
                        accountingTenantId: subOrder.StoreId,
                        cancellationToken: cancellationToken);
                }

                if (isCod && subOrder.PlatformCommissionAmount > 0m)
                {
                    await AppendTransactionIfMissingAsync(
                        CommerceHubFinancialEntityType.Vendor,
                        subOrder.StoreId,
                        CommerceHubFinancialTransactionType.Commission,
                        CommerceHubFinancialTransactionDirection.Debit,
                        subOrder.PlatformCommissionAmount,
                        subOrder.Currency,
                        CommerceHubFinancialReferenceType.Order,
                        subOrder.Id,
                        BuildCommissionDescription(subOrder.OrderNumber, subOrder.PlatformCommissionRate),
                        correlationId: correlationId,
                        accountingTenantId: subOrder.StoreId,
                        cancellationToken: cancellationToken);
                }

                if (subOrder.PlatformCommissionAmount > 0m)
                {
                    await AppendTransactionIfMissingAsync(
                        CommerceHubFinancialEntityType.Platform,
                        null,
                        CommerceHubFinancialTransactionType.Commission,
                        CommerceHubFinancialTransactionDirection.Credit,
                        subOrder.PlatformCommissionAmount,
                        subOrder.Currency,
                        CommerceHubFinancialReferenceType.Order,
                        subOrder.Id,
                        BuildCommissionDescription(subOrder.OrderNumber, subOrder.PlatformCommissionRate),
                        correlationId: correlationId,
                        accountingTenantId: subOrder.StoreId,
                        cancellationToken: cancellationToken);
                }

                if (subOrder.PromoDiscountAmount > 0m)
                {
                    await AppendTransactionIfMissingAsync(
                        CommerceHubFinancialEntityType.Platform,
                        null,
                        CommerceHubFinancialTransactionType.Promotion,
                        CommerceHubFinancialTransactionDirection.Debit,
                        subOrder.PromoDiscountAmount,
                        subOrder.Currency,
                        CommerceHubFinancialReferenceType.Order,
                        subOrder.Id,
                        BuildPromotionExpenseDescription(subOrder.OrderNumber, marketplaceOrder.PromoCode),
                        correlationId: correlationId,
                        accountingTenantId: subOrder.StoreId,
                        cancellationToken: cancellationToken);
                }

                if (shouldPersist)
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task RecordRefundAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        CommerceHubRefund refund,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marketplaceOrder);
        ArgumentNullException.ThrowIfNull(subOrder);
        ArgumentNullException.ThrowIfNull(refund);
        ValidateRefundFinancialIntegrity(subOrder, refund);

        var correlationId = BuildRefundCorrelationId(refund.Id);
        var grossCommissionReversalAmount = CommissionCalculator.RoundAmount(
            Math.Max(0m, refund.Amount - refund.VendorEarningReversalAmount));
        var decision = RefundFinancialDecisionEngine.Create(
            subOrder.PaymentMethod,
            refund.IsPostPayoutAdjustment,
            refund.Amount,
            refund.VendorEarningReversalAmount,
            refund.CommissionReversalAmount,
            grossCommissionReversalAmount,
            refund.Currency);

        await ExecuteAtomicAsync(
            async shouldPersist =>
            {
                if (decision.PlatformCashDelta < 0m)
                {
                    await AppendTransactionIfMissingAsync(
                        CommerceHubFinancialEntityType.Platform,
                        null,
                        CommerceHubFinancialTransactionType.Refund,
                        CommerceHubFinancialTransactionDirection.Debit,
                        Math.Abs(decision.PlatformCashDelta),
                        refund.Currency,
                        CommerceHubFinancialReferenceType.Refund,
                        refund.Id,
                        BuildPlatformRefundCashDescription(subOrder.OrderNumber),
                        idempotencyKey: BuildRefundLedgerIdempotencyKey(refund.Id, "platform-cash"),
                        correlationId: correlationId,
                        createdBy: refund.CreatedBy,
                        reason: refund.Reason,
                        financialBucketOverride: CommerceHubFinancialBucket.Cash,
                        accountingTenantId: subOrder.StoreId,
                        cancellationToken: cancellationToken);
                }

                if (decision.Scenario == RefundFinancialScenario.OnlineBeforePayout &&
                    refund.VendorEarningReversalAmount > 0m)
                {
                    await AppendTransactionIfMissingAsync(
                        CommerceHubFinancialEntityType.Platform,
                        null,
                        CommerceHubFinancialTransactionType.Adjustment,
                        CommerceHubFinancialTransactionDirection.Debit,
                        refund.VendorEarningReversalAmount,
                        refund.Currency,
                        CommerceHubFinancialReferenceType.Refund,
                        refund.Id,
                        BuildPlatformPayableRefundDescription(subOrder.OrderNumber),
                        idempotencyKey: BuildRefundLedgerIdempotencyKey(refund.Id, "platform-payable"),
                        correlationId: correlationId,
                        createdBy: refund.CreatedBy,
                        reason: refund.Reason,
                        financialBucketOverride: CommerceHubFinancialBucket.Payable,
                        accountingTenantId: subOrder.StoreId,
                        cancellationToken: cancellationToken);
                }

                if (decision.Scenario is RefundFinancialScenario.OnlineBeforePayout or RefundFinancialScenario.OnlineAfterPayout &&
                    refund.VendorEarningReversalAmount > 0m)
                {
                    await AppendTransactionIfMissingAsync(
                        CommerceHubFinancialEntityType.Vendor,
                        subOrder.StoreId,
                        CommerceHubFinancialTransactionType.Refund,
                        CommerceHubFinancialTransactionDirection.Debit,
                        refund.VendorEarningReversalAmount,
                        refund.Currency,
                        CommerceHubFinancialReferenceType.Refund,
                        refund.Id,
                        decision.Scenario == RefundFinancialScenario.OnlineAfterPayout
                            ? BuildVendorDebtRefundDescription(subOrder.OrderNumber)
                            : BuildVendorRefundDescription(subOrder.OrderNumber),
                        idempotencyKey: BuildRefundLedgerIdempotencyKey(refund.Id, "vendor-refund"),
                        correlationId: correlationId,
                        createdBy: refund.CreatedBy,
                        reason: refund.Reason,
                        financialBucketOverride: decision.Scenario == RefundFinancialScenario.OnlineAfterPayout
                            ? CommerceHubFinancialBucket.Receivable
                            : CommerceHubFinancialBucket.Payable,
                        cancellationToken: cancellationToken);
                }

                if (decision.Scenario == RefundFinancialScenario.CashOnDelivery &&
                    grossCommissionReversalAmount > 0m)
                {
                    await AppendTransactionIfMissingAsync(
                        CommerceHubFinancialEntityType.Vendor,
                        subOrder.StoreId,
                        CommerceHubFinancialTransactionType.Commission,
                        CommerceHubFinancialTransactionDirection.Credit,
                        grossCommissionReversalAmount,
                        refund.Currency,
                        CommerceHubFinancialReferenceType.Refund,
                        refund.Id,
                        BuildVendorCommissionReversalDescription(subOrder.OrderNumber),
                        idempotencyKey: BuildRefundLedgerIdempotencyKey(refund.Id, "vendor-debt-reduction"),
                        correlationId: correlationId,
                        createdBy: refund.CreatedBy,
                        reason: refund.Reason,
                        cancellationToken: cancellationToken);
                }

                if (refund.CommissionReversalAmount > 0m)
                {
                    await AppendTransactionIfMissingAsync(
                        CommerceHubFinancialEntityType.Platform,
                        null,
                        CommerceHubFinancialTransactionType.Commission,
                        CommerceHubFinancialTransactionDirection.Debit,
                        refund.CommissionReversalAmount,
                        refund.Currency,
                        CommerceHubFinancialReferenceType.Refund,
                        refund.Id,
                        BuildCommissionReversalDescription(subOrder.OrderNumber),
                        idempotencyKey: BuildRefundLedgerIdempotencyKey(refund.Id, "platform-commission"),
                        correlationId: correlationId,
                        createdBy: refund.CreatedBy,
                        reason: refund.Reason,
                        accountingTenantId: subOrder.StoreId,
                        cancellationToken: cancellationToken);
                }

                if (refund.PromoDiscountReversalAmount > 0m)
                {
                    await AppendTransactionIfMissingAsync(
                        CommerceHubFinancialEntityType.Platform,
                        null,
                        CommerceHubFinancialTransactionType.Promotion,
                        CommerceHubFinancialTransactionDirection.Credit,
                        refund.PromoDiscountReversalAmount,
                        refund.Currency,
                        CommerceHubFinancialReferenceType.Refund,
                        refund.Id,
                        BuildPromotionExpenseReversalDescription(subOrder.OrderNumber),
                        idempotencyKey: BuildRefundLedgerIdempotencyKey(refund.Id, "platform-promotion"),
                        correlationId: correlationId,
                        createdBy: refund.CreatedBy,
                        reason: refund.Reason,
                        accountingTenantId: subOrder.StoreId,
                        cancellationToken: cancellationToken);
                }

                if (shouldPersist)
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task RecordCommissionAsync(
        decimal amount,
        string currency,
        CommerceHubFinancialTransactionDirection direction,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        string description,
        string? idempotencyKey = null,
        string? correlationId = null,
        Guid? accountingTenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return;
        }

        await RecordTransactionAsync(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            direction,
            amount,
            currency,
            referenceType,
            referenceId,
            description,
            idempotencyKey,
            correlationId,
            accountingTenantId: accountingTenantId,
            cancellationToken: cancellationToken);
    }

    public async Task RecordPayoutAsync(
        CommerceHubPayout payout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payout);
        ValidatePayoutFinancialIntegrity(payout);

        var correlationId = BuildCorrelationId(CommerceHubFinancialReferenceType.Payout, payout.Id);

        await ExecuteAtomicAsync(
            async shouldPersist =>
            {
                await AppendTransactionIfMissingAsync(
                    CommerceHubFinancialEntityType.Platform,
                    null,
                    CommerceHubFinancialTransactionType.Settlement,
                    CommerceHubFinancialTransactionDirection.Debit,
                    payout.Amount,
                    payout.Currency,
                    CommerceHubFinancialReferenceType.Payout,
                    payout.Id,
                    BuildPlatformPayoutCashDescription(),
                    correlationId: correlationId,
                    accountingTenantId: payout.TenantId,
                    cancellationToken: cancellationToken);

                await AppendTransactionIfMissingAsync(
                    CommerceHubFinancialEntityType.Platform,
                    null,
                    CommerceHubFinancialTransactionType.Payout,
                    CommerceHubFinancialTransactionDirection.Debit,
                    payout.Amount,
                    payout.Currency,
                    CommerceHubFinancialReferenceType.Payout,
                    payout.Id,
                    BuildPlatformPayoutLiabilityDescription(),
                    correlationId: correlationId,
                    accountingTenantId: payout.TenantId,
                    cancellationToken: cancellationToken);

                await AppendTransactionIfMissingAsync(
                    CommerceHubFinancialEntityType.Vendor,
                    payout.TenantId,
                    CommerceHubFinancialTransactionType.Payout,
                    CommerceHubFinancialTransactionDirection.Debit,
                    payout.Amount,
                    payout.Currency,
                    CommerceHubFinancialReferenceType.Payout,
                    payout.Id,
                    BuildVendorPayoutLiabilityDescription(),
                    correlationId: correlationId,
                    financialBucketOverride: CommerceHubFinancialBucket.Payable,
                    cancellationToken: cancellationToken);

                if (shouldPersist)
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task RecordMirroredTransactionsAsync(
        Guid vendorId,
        decimal amount,
        string currency,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        CommerceHubFinancialTransactionType vendorTransactionType,
        CommerceHubFinancialTransactionDirection vendorDirection,
        CommerceHubFinancialBucket vendorBucket,
        string vendorDescription,
        CommerceHubFinancialTransactionType platformTransactionType,
        CommerceHubFinancialTransactionDirection platformDirection,
        CommerceHubFinancialBucket platformBucket,
        string platformDescription,
        string? correlationId = null,
        string? vendorIdempotencyKey = null,
        string? platformIdempotencyKey = null,
        Guid? createdBy = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }

        await ExecuteAtomicAsync(
            async shouldPersist =>
            {
                await AppendTransactionIfMissingAsync(
                    CommerceHubFinancialEntityType.Vendor,
                    vendorId,
                    vendorTransactionType,
                    vendorDirection,
                    amount,
                    currency,
                    referenceType,
                    referenceId,
                    vendorDescription,
                    idempotencyKey: vendorIdempotencyKey,
                    correlationId: correlationId ?? BuildCorrelationId(referenceType, referenceId),
                    createdBy: createdBy,
                    reason: reason,
                    financialBucketOverride: vendorBucket,
                    cancellationToken: cancellationToken);

                await AppendTransactionIfMissingAsync(
                    CommerceHubFinancialEntityType.Platform,
                    null,
                    platformTransactionType,
                    platformDirection,
                    amount,
                    currency,
                    referenceType,
                    referenceId,
                    platformDescription,
                    idempotencyKey: platformIdempotencyKey,
                    correlationId: correlationId ?? BuildCorrelationId(referenceType, referenceId),
                    createdBy: createdBy,
                    reason: reason,
                    financialBucketOverride: platformBucket,
                    accountingTenantId: vendorId,
                    cancellationToken: cancellationToken);

                if (shouldPersist)
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task RecordAdjustmentEntriesAsync(
        Guid vendorId,
        decimal amount,
        string currency,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        CommerceHubFinancialTransactionDirection vendorDirection,
        CommerceHubFinancialTransactionDirection platformDirection,
        string vendorDescription,
        string platformDescription,
        CommerceHubFinancialTransactionType transactionType = CommerceHubFinancialTransactionType.Adjustment,
        string? correlationId = null,
        Guid? createdBy = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }

        await ExecuteAtomicAsync(
            async shouldPersist =>
            {
                await AppendTransactionIfMissingAsync(
                    CommerceHubFinancialEntityType.Vendor,
                    vendorId,
                    transactionType,
                    vendorDirection,
                    amount,
                    currency,
                    referenceType,
                    referenceId,
                    vendorDescription,
                    reason: reason,
                    correlationId: correlationId ?? BuildCorrelationId(referenceType, referenceId),
                    createdBy: createdBy,
                    accountingTenantId: vendorId,
                    cancellationToken: cancellationToken);

                await AppendTransactionIfMissingAsync(
                    CommerceHubFinancialEntityType.Platform,
                    null,
                    transactionType,
                    platformDirection,
                    amount,
                    currency,
                    referenceType,
                    referenceId,
                    platformDescription,
                    reason: reason,
                    correlationId: correlationId ?? BuildCorrelationId(referenceType, referenceId),
                    createdBy: createdBy,
                    accountingTenantId: vendorId,
                    cancellationToken: cancellationToken);

                if (shouldPersist)
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            },
            cancellationToken);
    }

    private async Task AppendTransactionIfMissingAsync(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        string currency,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        string description,
        string? idempotencyKey = null,
        string? correlationId = null,
        Guid? createdBy = null,
        string? reason = null,
        CommerceHubFinancialBucket? financialBucketOverride = null,
        Guid? accountingTenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return;
        }

        var transaction = CommerceHubFinancialTransaction.Create(
            entityType,
            entityId,
            transactionType,
            direction,
            amount,
            currency,
            referenceType,
            referenceId,
            description,
            idempotencyKey,
            correlationId,
            createdBy: createdBy,
            reason: reason,
            financialBucketOverride: financialBucketOverride);

        if (await TransactionExistsAsync(transaction, cancellationToken))
        {
            _logger.LogDebug(
                "CommerceHub ledger skipped duplicate transaction | EntityType: {EntityType} | EntityId: {EntityId} | TransactionType: {TransactionType} | ReferenceId: {ReferenceId} | IdempotencyKey: {IdempotencyKey}",
                transaction.EntityType,
                transaction.EntityId,
                transaction.TransactionType,
                transaction.ReferenceId,
                transaction.IdempotencyKey);
            return;
        }

        _dbContext.CommerceHubFinancialTransactions.Add(transaction);

        // Enqueue a GL posting outbox message in the SAME transaction scope so the
        // AccountingOutboxProcessor can post a JournalEntry with
        // SourceType = "CommerceHubFinancialTransaction" and satisfy the
        // EnsureCommerceHubLedgerAccountingCoverageAsync check in the ReadinessGate.
        var glPostingPayload = JsonSerializer.Serialize(new
        {
            TransactionId   = transaction.Id,
            TransactionType = transaction.TransactionType.ToString(),
            Direction       = transaction.Direction.ToString(),
            EntityType      = transaction.EntityType.ToString(),
            FinancialBucket = transaction.FinancialBucket.ToString(),
            transaction.Amount,
            transaction.Currency,
            transaction.Description,
            OccurredAtUtc   = transaction.CreatedAtUtc
        });

        var resolvedAccountingTenantId = ResolveAccountingTenantId(transaction, accountingTenantId);

        var outboxMessage = AccountingOutboxMessage.Create(
            tenantId:          resolvedAccountingTenantId,
            eventType:         "CommerceHubLedgerPosting",
            businessEntityId:  transaction.Id,
            entityTypeName:    nameof(CommerceHubFinancialTransaction),
            payloadJson:       glPostingPayload,
            idempotencyKey:    $"COMMERCEHUB_TX:{resolvedAccountingTenantId:N}:{transaction.Id:N}",
            correlationId:     transaction.CorrelationId,
            sourceType:        nameof(CommerceHubFinancialTransaction));

        _dbContext.AccountingOutboxMessages.Add(outboxMessage);
    }

    private static Guid ResolveAccountingTenantId(
        CommerceHubFinancialTransaction transaction,
        Guid? accountingTenantId)
    {
        var tenantId = accountingTenantId ?? transaction.EntityId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                "CommerceHub financial transactions must be posted to a tenant-owned chart of accounts. " +
                $"TransactionId={transaction.Id}; EntityType={transaction.EntityType}; TransactionType={transaction.TransactionType}.");
        }

        return tenantId.Value;
    }

    private async Task<bool> TransactionExistsAsync(
        CommerceHubFinancialTransaction transaction,
        CancellationToken cancellationToken)
    {
        // S40: Normalize idempotency key check once for consistency
        bool hasIdempotencyKey = !string.IsNullOrWhiteSpace(transaction.IdempotencyKey);
        string? normalizedIdempotencyKey = hasIdempotencyKey ? transaction.IdempotencyKey : null;

        if (hasIdempotencyKey)
        {
            var trackedByIdempotency = _dbContext.ChangeTracker
                .Entries<CommerceHubFinancialTransaction>()
                .Where(entry => entry.State != EntityState.Deleted)
                .Select(entry => entry.Entity)
                .FirstOrDefault(entry => entry.IdempotencyKey == normalizedIdempotencyKey);

            if (trackedByIdempotency != null)
            {
                EnsureIdempotencyKeyMatchesFinancialFingerprint(trackedByIdempotency, transaction);
                return true;
            }

            var persistedByIdempotency = await _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(entry => entry.IdempotencyKey == normalizedIdempotencyKey, cancellationToken);

            if (persistedByIdempotency != null)
            {
                EnsureIdempotencyKeyMatchesFinancialFingerprint(persistedByIdempotency, transaction);
                return true;
            }
        }

        // Same financial fingerprint is a safe replay even when the caller omitted
        // or regenerated an idempotency key. A reused key with a divergent
        // fingerprint is rejected above so money cannot be silently dropped.
        var trackedExists = _dbContext.ChangeTracker
            .Entries<CommerceHubFinancialTransaction>()
            .Any(entry =>
                entry.State != EntityState.Deleted &&
                HasSameFinancialFingerprint(entry.Entity, transaction));

        if (trackedExists)
        {
            return true;
        }

        return await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .AnyAsync(
                entry => entry.EntityType == transaction.EntityType &&
                         entry.ReferenceType == transaction.ReferenceType &&
                         entry.ReferenceId == transaction.ReferenceId &&
                         entry.TransactionType == transaction.TransactionType &&
                         entry.EntityId == transaction.EntityId &&
                         entry.Direction == transaction.Direction &&
                         entry.FinancialBucket == transaction.FinancialBucket &&
                         entry.Amount == transaction.Amount &&
                         entry.Currency == transaction.Currency,
                cancellationToken);
    }

    private static void EnsureIdempotencyKeyMatchesFinancialFingerprint(
        CommerceHubFinancialTransaction existing,
        CommerceHubFinancialTransaction incoming)
    {
        if (HasSameFinancialFingerprint(existing, incoming))
        {
            return;
        }

        throw new InvalidOperationException(
            "CommerceHub ledger idempotency key collision detected with a different financial fingerprint. " +
            $"IdempotencyKey='{incoming.IdempotencyKey}', ExistingReference='{existing.ReferenceType}:{existing.ReferenceId}', " +
            $"IncomingReference='{incoming.ReferenceType}:{incoming.ReferenceId}'.");
    }

    private static bool HasSameFinancialFingerprint(
        CommerceHubFinancialTransaction existing,
        CommerceHubFinancialTransaction incoming)
    {
        return existing.EntityType == incoming.EntityType &&
               existing.ReferenceType == incoming.ReferenceType &&
               existing.ReferenceId == incoming.ReferenceId &&
               existing.TransactionType == incoming.TransactionType &&
               existing.EntityId == incoming.EntityId &&
               existing.Direction == incoming.Direction &&
               existing.FinancialBucket == incoming.FinancialBucket &&
               existing.Amount == incoming.Amount &&
               existing.Currency == incoming.Currency;
    }

    private async Task ExecuteAtomicAsync(
        Func<bool, Task> recorder,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction != null)
        {
            await recorder(false);
            return;
        }

        if (!_dbContext.Database.IsRelational())
        {
            await recorder(true);
            return;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            await recorder(true);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsLedgerDuplicateException(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            await EnsureDuplicateLedgerWriteIsSafeAsync(cancellationToken);
            _logger.LogDebug(
                exception,
                "CommerceHub ledger ignored duplicate transaction race detected by database uniqueness constraints.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsLedgerDuplicateException(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               ((postgresException.ConstraintName?.StartsWith("UX_CommerceHubFinancialTransactions_", StringComparison.Ordinal) ?? false) ||
                (postgresException.ConstraintName?.StartsWith("UX_CHFinancialTx_", StringComparison.Ordinal) ?? false));
    }

    private async Task EnsureDuplicateLedgerWriteIsSafeAsync(CancellationToken cancellationToken)
    {
        var pendingEntries = _dbContext.ChangeTracker
            .Entries<CommerceHubFinancialTransaction>()
            .Where(entry => entry.State == EntityState.Added)
            .ToList();

        foreach (var pendingEntry in pendingEntries)
        {
            var transaction = pendingEntry.Entity;
            var persistedByIdempotency = await _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(entry => entry.IdempotencyKey == transaction.IdempotencyKey, cancellationToken);

            if (persistedByIdempotency != null)
            {
                EnsureIdempotencyKeyMatchesFinancialFingerprint(persistedByIdempotency, transaction);
                pendingEntry.State = EntityState.Detached;
                
                // CRITICAL: Also detach the associated AccountingOutboxMessage that was added
                // alongside this transaction to prevent accidental persistence of stale outbox messages
                DetachAssociatedOutboxMessage(transaction);
                
                continue;
            }

            var hasEquivalentFingerprint = await _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .AnyAsync(
                    entry => entry.EntityType == transaction.EntityType &&
                             entry.ReferenceType == transaction.ReferenceType &&
                             entry.ReferenceId == transaction.ReferenceId &&
                             entry.TransactionType == transaction.TransactionType &&
                             entry.EntityId == transaction.EntityId &&
                             entry.Direction == transaction.Direction &&
                             entry.FinancialBucket == transaction.FinancialBucket &&
                             entry.Amount == transaction.Amount &&
                             entry.Currency == transaction.Currency,
                    cancellationToken);

            if (!hasEquivalentFingerprint)
            {
                throw new InvalidOperationException(
                    "CommerceHub ledger duplicate write could not be proven to be an exact financial replay.");
            }

            pendingEntry.State = EntityState.Detached;
            
            // CRITICAL: Also detach the associated AccountingOutboxMessage that was added
            // alongside this transaction to prevent accidental persistence of stale outbox messages
            DetachAssociatedOutboxMessage(transaction);
        }
    }

    /// <summary>
    /// Detaches the AccountingOutboxMessage associated with a CommerceHubFinancialTransaction
    /// to prevent accidental persistence during duplicate-write recovery.
    /// </summary>
    private void DetachAssociatedOutboxMessage(CommerceHubFinancialTransaction transaction)
    {
        // Find the outbox message that was added alongside this transaction
        // The outbox message has businessEntityId = transaction.Id
        var outboxEntry = _dbContext.ChangeTracker
            .Entries<AccountingOutboxMessage>()
            .FirstOrDefault(entry => 
                entry.State == EntityState.Added &&
                entry.Entity.BusinessEntityId == transaction.Id &&
                entry.Entity.EntityTypeName == nameof(CommerceHubFinancialTransaction));

        if (outboxEntry != null)
        {
            outboxEntry.State = EntityState.Detached;
        }
    }

    private static string BuildCorrelationId(CommerceHubFinancialReferenceType referenceType, Guid referenceId) =>
        $"{referenceType}:{referenceId:N}";

    private static string BuildRefundCorrelationId(Guid refundId) =>
        refundId.ToString("D");

    private static string BuildRefundLedgerIdempotencyKey(Guid refundId, string scope) =>
        $"commercehub:refund:{refundId:N}:{scope}";

    private static string BuildPlatformOrderDescription(string orderNumber) =>
        $"Order payment received for Order #{orderNumber}";

    private static string BuildVendorOrderDescription(string orderNumber) =>
        $"Vendor proceeds credited for Order #{orderNumber}";

    private static string BuildVendorCodOrderDescription(string orderNumber) =>
        $"Vendor cash collected from customer for COD Order #{orderNumber}";

    private static string BuildCommissionDescription(string orderNumber, decimal commissionRate)
    {
        if (commissionRate > 0m)
        {
            return $"Commission deducted ({commissionRate * 100m:0.##}%) for Order #{orderNumber}";
        }

        return $"Commission deducted for Order #{orderNumber}";
    }

    private static string BuildPlatformRefundCashDescription(string orderNumber) =>
        $"Customer refund cash paid for Order #{orderNumber}";

    private static string BuildPlatformPayableRefundDescription(string orderNumber) =>
        $"Vendor payable reduced before payout for refunded Order #{orderNumber}";

    private static string BuildVendorRefundDescription(string orderNumber) =>
        $"Vendor balance adjusted for refund on Order #{orderNumber}";

    private static string BuildVendorDebtRefundDescription(string orderNumber) =>
        $"Vendor debt created after payout for refunded Order #{orderNumber}";

    private static string BuildCommissionReversalDescription(string orderNumber) =>
        $"Commission reversed for refunded Order #{orderNumber}";

    private static string BuildVendorCommissionReversalDescription(string orderNumber) =>
        $"Vendor commission debt reversed for refunded Order #{orderNumber}";

    private static string BuildPromotionExpenseDescription(string orderNumber, string? promoCode)
    {
        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            return $"Promo {promoCode} absorbed by platform for Order #{orderNumber}";
        }

        return $"Promotion expense absorbed by platform for Order #{orderNumber}";
    }

    private static string BuildPromotionExpenseReversalDescription(string orderNumber) =>
        $"Promotion expense reversed for refunded Order #{orderNumber}";

    private static string BuildVendorPayoutSettlementDescription() =>
        "Vendor settlement received from platform payout";

    private static string BuildVendorPayoutLiabilityDescription() =>
        "Vendor payable reduced by payout execution";

    private static string BuildPlatformPayoutLiabilityDescription() =>
        "Platform vendor payable reduced by payout execution";

    private static string BuildPlatformPayoutCashDescription() =>
        "Platform cash disbursed for vendor payout";

    private static void ValidateOrderFinancialIntegrity(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder)
    {
        if (marketplaceOrder.Id == Guid.Empty)
        {
            throw new InvalidOperationException("Marketplace order id is required before recording ledger entries.");
        }

        if (subOrder.Id == Guid.Empty)
        {
            throw new InvalidOperationException("Sub-order id is required before recording ledger entries.");
        }

        if (subOrder.StoreId == Guid.Empty)
        {
            throw new InvalidOperationException("Sub-order vendor id is required before recording ledger entries.");
        }

        if (subOrder.MarketplaceOrderId != Guid.Empty && subOrder.MarketplaceOrderId != marketplaceOrder.Id)
        {
            throw new InvalidOperationException("Sub-order must belong to the marketplace order before recording ledger entries.");
        }

        if (subOrder.Total <= 0m)
        {
            throw new InvalidOperationException("Sub-order total must be positive before recording ledger entries.");
        }

        if (!string.Equals(marketplaceOrder.Currency, subOrder.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Marketplace order currency must match sub-order currency before recording ledger entries.");
        }

        FinancialCurrencyGuard.RequireEgpCurrency(
            subOrder.Currency,
            "commercehub.ledger.order.currency",
            $"marketplaceOrderId:{marketplaceOrder.Id:D};subOrderId:{subOrder.Id:D}");

        if (subOrder.PlatformCommissionAmount < 0m ||
            subOrder.PromoDiscountAmount < 0m ||
            subOrder.VendorEarningAmount < 0m ||
            subOrder.FinalPlatformCommissionAmount < 0m)
        {
            throw new InvalidOperationException("Sub-order financial amounts cannot be negative before recording ledger entries.");
        }

        var grossCommission = CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.PlatformCommissionAmount));
        var promoDiscount = CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.PromoDiscountAmount));
        var finalCommission = ResolveStoredFinalCommission(subOrder);
        var expectedFinalCommission = CommissionCalculator.RoundAmount(Math.Max(0m, grossCommission - promoDiscount));
        if (finalCommission != expectedFinalCommission)
        {
            throw new InvalidOperationException("Sub-order final commission must equal gross commission minus promo discount before recording ledger entries.");
        }

        if (promoDiscount > grossCommission)
        {
            throw new InvalidOperationException("Sub-order promo discount cannot exceed the gross platform commission.");
        }

        var expectedVendorEarning = CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.Total - grossCommission));
        if (CommissionCalculator.RoundAmount(subOrder.VendorEarningAmount) != expectedVendorEarning)
        {
            throw new InvalidOperationException("Sub-order vendor earnings must be calculated from the gross commission before recording ledger entries.");
        }
    }

    private static decimal ResolveStoredFinalCommission(CommerceHubSubOrder subOrder)
    {
        var storedFinal = CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.FinalPlatformCommissionAmount));
        return storedFinal > 0m || subOrder.PlatformCommissionAmount == 0m
            ? storedFinal
            : CommissionCalculator.RoundAmount(Math.Max(0m, subOrder.PlatformCommissionAmount - subOrder.PromoDiscountAmount));
    }

    private static void ValidateRefundFinancialIntegrity(
        CommerceHubSubOrder subOrder,
        CommerceHubRefund refund)
    {
        if (refund.Status != CommerceHubRefundStatus.Completed)
        {
            throw new InvalidOperationException("Refund must be completed before recording ledger entries.");
        }

        if (refund.SubOrderId != subOrder.Id)
        {
            throw new InvalidOperationException("Refund must belong to the sub-order before recording ledger entries.");
        }

        if (refund.TenantId != subOrder.StoreId)
        {
            throw new InvalidOperationException("Refund tenant must match the sub-order vendor before recording ledger entries.");
        }

        if (refund.Amount <= 0m)
        {
            throw new InvalidOperationException("Refund amount must be positive before recording ledger entries.");
        }

        if (refund.Amount > subOrder.Total)
        {
            throw new InvalidOperationException("Refund amount cannot exceed the sub-order total before recording ledger entries.");
        }

        if (refund.VendorEarningReversalAmount < 0m ||
            refund.CommissionReversalAmount < 0m ||
            refund.PromoDiscountReversalAmount < 0m)
        {
            throw new InvalidOperationException("Refund reversal amounts cannot be negative before recording ledger entries.");
        }

        if (refund.VendorEarningReversalAmount > refund.Amount)
        {
            throw new InvalidOperationException("Refund vendor earning reversal cannot exceed the refund amount.");
        }

        if (!string.Equals(subOrder.Currency, refund.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refund currency must match sub-order currency before recording ledger entries.");
        }

        FinancialCurrencyGuard.RequireEgpCurrency(
            refund.Currency,
            "commercehub.ledger.refund.currency",
            $"subOrderId:{subOrder.Id:D};refundId:{refund.Id:D}");

        var grossCommissionReversalAmount = CommissionCalculator.RoundAmount(
            Math.Max(0m, refund.Amount - refund.VendorEarningReversalAmount));
        var expectedGrossReversal = CommissionCalculator.RoundAmount(
            refund.CommissionReversalAmount + refund.PromoDiscountReversalAmount);

        if (grossCommissionReversalAmount != expectedGrossReversal)
        {
            throw new InvalidOperationException("Refund commission reversal plus promo reversal must equal the gross commission reversal.");
        }

        if (refund.PromoDiscountReversalAmount < 0m || refund.PromoDiscountReversalAmount > grossCommissionReversalAmount)
        {
            throw new InvalidOperationException("Refund promo reversal must be non-negative and cannot exceed the gross commission reversal.");
        }
    }

    private static void ValidatePayoutFinancialIntegrity(CommerceHubPayout payout)
    {
        if (payout.Id == Guid.Empty)
        {
            throw new InvalidOperationException("Payout id is required before recording ledger entries.");
        }

        if (payout.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException("Payout vendor id is required before recording ledger entries.");
        }

        if (payout.Status != CommerceHubPayoutStatus.Completed)
        {
            throw new InvalidOperationException("Payout must be completed before recording ledger entries.");
        }

        if (payout.Amount <= 0m)
        {
            throw new InvalidOperationException("Payout amount must be positive before recording ledger entries.");
        }

        FinancialCurrencyGuard.RequireEgpCurrency(
            payout.Currency,
            "commercehub.ledger.payout.currency",
            $"payoutId:{payout.Id:D};vendorId:{payout.TenantId:D}");
    }
}
