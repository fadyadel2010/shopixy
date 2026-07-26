using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubFinancialAuditService
{
    void RecordPaymentCollected(
        Guid? tenantId,
        Guid marketplaceOrderId,
        Guid? subOrderId,
        Guid? paymentId,
        decimal amount,
        string currency,
        string? correlationId);

    void RecordCommissionDeducted(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        decimal commissionAmount,
        string currency,
        string? correlationId);

    void RecordVendorEarningCreated(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid earningId,
        decimal earningAmount,
        string currency,
        string? correlationId);

    void RecordVendorEarningAvailable(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid earningId,
        decimal earningAmount,
        string currency,
        string? correlationId);

    void RecordVendorEarningReversed(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid earningId,
        Guid refundId,
        decimal earningAmount,
        string currency,
        string? correlationId);

    void RecordVendorAdjustmentCreated(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid earningId,
        Guid refundId,
        decimal adjustmentAmount,
        string currency,
        string? correlationId);

    void RecordCommissionReversed(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid refundId,
        decimal commissionAmount,
        string currency,
        string? correlationId);

    void RecordCodCollected(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        decimal amount,
        string currency,
        string? correlationId);

    void RecordPayoutCompleted(
        Guid tenantId,
        Guid payoutId,
        decimal amount,
        string currency,
        string? correlationId);

    void RecordPayoutFailed(
        Guid tenantId,
        Guid payoutId,
        string currency,
        string description,
        string? correlationId);

    void RecordRefundProcessed(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid refundId,
        Guid? paymentId,
        decimal amount,
        string currency,
        string? correlationId);

    void RecordRefundReversal(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid earningId,
        Guid refundId,
        decimal amount,
        string currency,
        string? correlationId);

    void RecordRefundFailed(
        Guid tenantId,
        Guid subOrderId,
        Guid refundId,
        Guid? paymentId,
        string currency,
        string description,
        string? correlationId);

    void RecordFinanceReconciliation(
        string status,
        string description,
        string? correlationId);

    Task PersistAsync(CancellationToken cancellationToken = default);
}

public class CommerceHubFinancialAuditService : ICommerceHubFinancialAuditService
{
    private readonly ApplicationDbContext _dbContext;

    public CommerceHubFinancialAuditService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void RecordPaymentCollected(
        Guid? tenantId,
        Guid marketplaceOrderId,
        Guid? subOrderId,
        Guid? paymentId,
        decimal amount,
        string currency,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            MarketplaceOrderId = marketplaceOrderId,
            SubOrderId = subOrderId,
            PaymentId = paymentId,
            Type = "Payment",
            Debit = amount,
            Credit = 0,
            Currency = currency,
            Description = $"Payment collected | Amount: {amount:N2} {currency}",
            CorrelationId = correlationId
        });
    }

    public void RecordCommissionDeducted(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        decimal commissionAmount,
        string currency,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            MarketplaceOrderId = marketplaceOrderId,
            SubOrderId = subOrderId,
            Type = "Commission",
            Debit = 0,
            Credit = commissionAmount,
            Currency = currency,
            Description = $"Platform commission snapshot recorded | Amount: {commissionAmount:N2} {currency}",
            CorrelationId = correlationId
        });
    }

    public void RecordVendorEarningCreated(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid earningId,
        decimal earningAmount,
        string currency,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            MarketplaceOrderId = marketplaceOrderId,
            SubOrderId = subOrderId,
            EarningId = earningId,
            Type = "EarningCreated",
            Debit = 0,
            Credit = earningAmount,
            Currency = currency,
            Description = $"Vendor earning created | Amount: {earningAmount:N2} {currency}",
            CorrelationId = correlationId
        });
    }

    public void RecordVendorEarningAvailable(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid earningId,
        decimal earningAmount,
        string currency,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            MarketplaceOrderId = marketplaceOrderId,
            SubOrderId = subOrderId,
            EarningId = earningId,
            Type = "EarningAvailable",
            Debit = 0,
            Credit = earningAmount,
            Currency = currency,
            Description = $"Vendor earning available | Amount: {earningAmount:N2} {currency}",
            CorrelationId = correlationId
        });
    }

    public void RecordVendorEarningReversed(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid earningId,
        Guid refundId,
        decimal earningAmount,
        string currency,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            MarketplaceOrderId = marketplaceOrderId,
            SubOrderId = subOrderId,
            EarningId = earningId,
            RefundId = refundId,
            Type = "EarningReversed",
            Debit = earningAmount,
            Credit = 0,
            Currency = currency,
            Description = $"Vendor earning reversed by refund | Amount: {earningAmount:N2} {currency}",
            CorrelationId = correlationId
        });
    }

    public void RecordVendorAdjustmentCreated(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid earningId,
        Guid refundId,
        decimal adjustmentAmount,
        string currency,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            MarketplaceOrderId = marketplaceOrderId,
            SubOrderId = subOrderId,
            EarningId = earningId,
            RefundId = refundId,
            Type = "EarningAdjustment",
            Debit = Math.Abs(adjustmentAmount),
            Credit = 0,
            Currency = currency,
            Description = $"Negative earning adjustment created | Amount: {adjustmentAmount:N2} {currency}",
            CorrelationId = correlationId
        });
    }

    public void RecordCommissionReversed(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid refundId,
        decimal commissionAmount,
        string currency,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            MarketplaceOrderId = marketplaceOrderId,
            SubOrderId = subOrderId,
            RefundId = refundId,
            Type = "CommissionReversed",
            Debit = commissionAmount,
            Credit = 0,
            Currency = currency,
            Description = $"Platform commission reversed | Amount: {commissionAmount:N2} {currency}",
            CorrelationId = correlationId
        });
    }

    public void RecordCodCollected(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        decimal amount,
        string currency,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            MarketplaceOrderId = marketplaceOrderId,
            SubOrderId = subOrderId,
            Type = "CodCollection",
            Debit = amount,
            Credit = 0,
            Currency = currency,
            Description = $"Cash on delivery collected | Amount: {amount:N2} {currency}",
            CorrelationId = correlationId
        });
    }

    public void RecordPayoutCompleted(
        Guid tenantId,
        Guid payoutId,
        decimal amount,
        string currency,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            PayoutId = payoutId,
            Type = "Payout",
            Debit = amount,
            Credit = 0,
            Currency = currency,
            Description = $"Vendor payout completed | Amount: {amount:N2} {currency}",
            CorrelationId = correlationId
        });
    }

    public void RecordPayoutFailed(
        Guid tenantId,
        Guid payoutId,
        string currency,
        string description,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            PayoutId = payoutId,
            Type = "PayoutFailed",
            Debit = 0,
            Credit = 0,
            Currency = currency,
            Description = description,
            CorrelationId = correlationId
        });
    }

    public void RecordRefundProcessed(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid refundId,
        Guid? paymentId,
        decimal amount,
        string currency,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            MarketplaceOrderId = marketplaceOrderId,
            SubOrderId = subOrderId,
            RefundId = refundId,
            PaymentId = paymentId,
            Type = "Refund",
            Debit = amount,
            Credit = 0,
            Currency = currency,
            Description = $"Customer refund completed | Amount: {amount:N2} {currency}",
            CorrelationId = correlationId
        });
    }

    public void RecordRefundReversal(
        Guid tenantId,
        Guid marketplaceOrderId,
        Guid subOrderId,
        Guid earningId,
        Guid refundId,
        decimal amount,
        string currency,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            MarketplaceOrderId = marketplaceOrderId,
            SubOrderId = subOrderId,
            EarningId = earningId,
            RefundId = refundId,
            Type = "RefundReversal",
            Debit = amount,
            Credit = 0,
            Currency = currency,
            Description = $"Refund completed after payout and created vendor repayment exposure | Amount: {amount:N2} {currency}",
            CorrelationId = correlationId
        });
    }

    public void RecordRefundFailed(
        Guid tenantId,
        Guid subOrderId,
        Guid refundId,
        Guid? paymentId,
        string currency,
        string description,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            TenantId = tenantId,
            SubOrderId = subOrderId,
            RefundId = refundId,
            PaymentId = paymentId,
            Type = "RefundFailed",
            Debit = 0,
            Credit = 0,
            Currency = currency,
            Description = description,
            CorrelationId = correlationId
        });
    }

    public void RecordFinanceReconciliation(
        string status,
        string description,
        string? correlationId)
    {
        Append(new CommerceHubFinancialAuditLog
        {
            Type = "FinanceReconciliation",
            Debit = 0,
            Credit = 0,
            Currency = "EGP",
            Description = $"Status: {status} | {description}",
            CorrelationId = correlationId
        });
    }

    public Task PersistAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    private void Append(CommerceHubFinancialAuditLog entry)
    {
        entry.Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
        entry.CreatedAtUtc = entry.CreatedAtUtc == default ? DateTime.UtcNow : entry.CreatedAtUtc;
        _dbContext.CommerceHubFinancialAuditLogs.Add(entry);
    }
}
