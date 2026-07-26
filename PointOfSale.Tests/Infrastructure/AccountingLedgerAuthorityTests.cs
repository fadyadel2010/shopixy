using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Tests.Infrastructure;

public sealed class AccountingLedgerAuthorityTests
{
    [Fact]
    public async Task LoadCurrentSettlementAsync_UsesCurrentSettlementOrderLineageInsteadOfRecordedSnapshot()
    {
        var tenantId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Orders.Add(CreateOrder(tenantId, orderId));
        dbContext.PaymentSettlementBatches.Add(new PaymentSettlementBatch
        {
            Id = batchId,
            TenantId = tenantId,
            SettlementType = "GATEWAY",
            SettlementId = "set-live",
            Provider = "stripe",
            GrossAmount = 100m,
            Fees = 3m,
            Currency = "EGP",
            OrderCount = 2,
            SettledAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        dbContext.PaymentSettlementOrders.Add(new PaymentSettlementOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SettlementBatchId = batchId,
            OrderId = orderId,
            OrderTotal = 100m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var settlement = await AccountingLedgerAuthority.LoadCurrentSettlementAsync(
            dbContext,
            tenantId,
            batchId,
            "GATEWAY",
            "set-live");

        Assert.NotNull(settlement);
        Assert.Equal(2, settlement!.RecordedOrderCount);
        Assert.Equal(1, settlement.CurrentOrderCount);
    }

    [Fact]
    public async Task EvaluateSettlementPosting_RetriesWhenRecordedSettlementLineageDriftsFromCurrentMappings()
    {
        var tenantId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Orders.Add(CreateOrder(tenantId, orderId));
        dbContext.PaymentSettlementBatches.Add(new PaymentSettlementBatch
        {
            Id = batchId,
            TenantId = tenantId,
            SettlementType = "COD",
            SettlementId = "set-drift",
            GrossAmount = 50m,
            Fees = 0m,
            Currency = "EGP",
            OrderCount = 2,
            SettledAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        dbContext.PaymentSettlementOrders.Add(new PaymentSettlementOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SettlementBatchId = batchId,
            OrderId = orderId,
            OrderTotal = 50m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var settlement = await AccountingLedgerAuthority.LoadCurrentSettlementAsync(
            dbContext,
            tenantId,
            batchId,
            "COD",
            "set-drift");

        var decision = AccountingLedgerAuthority.EvaluateSettlementPosting(settlement);

        Assert.False(decision.ShouldPost);
        Assert.True(decision.ShouldRetry);
        Assert.Contains("order lineage drifted", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadCurrentSettlementAsync_TreatsSoftDeletedSettlementBatchAsMissingAuthority()
    {
        var tenantId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Orders.Add(CreateOrder(tenantId, orderId));
        dbContext.PaymentSettlementBatches.Add(new PaymentSettlementBatch
        {
            Id = batchId,
            TenantId = tenantId,
            SettlementType = "GATEWAY",
            SettlementId = "set-deleted",
            Provider = "stripe",
            GrossAmount = 100m,
            Fees = 3m,
            Currency = "EGP",
            OrderCount = 1,
            SettledAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = true
        });
        dbContext.PaymentSettlementOrders.Add(new PaymentSettlementOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SettlementBatchId = batchId,
            OrderId = orderId,
            OrderTotal = 100m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var settlement = await AccountingLedgerAuthority.LoadCurrentSettlementAsync(
            dbContext,
            tenantId,
            batchId,
            "GATEWAY",
            "set-deleted");

        Assert.Null(settlement);
    }

    [Fact]
    public async Task LoadCurrentRefundAsync_RecomposesCurrentPostSettlementRefundLineage()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Orders.Add(CreateOrder(tenantId, orderId));
        dbContext.PaymentSettlementBatches.Add(new PaymentSettlementBatch
        {
            Id = batchId,
            TenantId = tenantId,
            SettlementType = "GATEWAY",
            SettlementId = "set-refund-live",
            Provider = "stripe",
            GrossAmount = 100m,
            Fees = 3m,
            Currency = "EGP",
            OrderCount = 2,
            SettledAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        dbContext.PaymentSettlementOrders.Add(new PaymentSettlementOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SettlementBatchId = batchId,
            OrderId = orderId,
            OrderTotal = 100m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow
        });
        dbContext.OrderRefunds.Add(new OrderRefund
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            RefundId = "refund-live",
            Amount = 25m,
            Currency = "EGP",
            IsFullRefund = false,
            PaymentMethod = "stripe",
            IsPostSettlementRefund = true,
            SettlementBatchId = batchId,
            SettlementType = "GATEWAY",
            SettlementId = "set-refund-live",
            SettledAtUtc = DateTime.UtcNow,
            FinancialPostingStatus = FinancialPostingStatus.Pending,
            RefundedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var refund = await AccountingLedgerAuthority.LoadCurrentRefundAsync(
            dbContext,
            tenantId,
            orderId,
            "refund-live");

        Assert.NotNull(refund);
        Assert.False(refund!.SettlementAuthorityMissing);
        Assert.Equal(2, refund.RecordedSettlementOrderCount);
        Assert.Equal(1, refund.CurrentSettlementOrderCount);
        Assert.True(refund.SettlementIncludesOrder);
    }

    [Fact]
    public void EvaluateRefundPosting_RetriesWhenPostSettlementRefundLineageDrifts()
    {
        var decision = AccountingLedgerAuthority.EvaluateRefundPosting(
            new RefundAccountingAuthoritySnapshot(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "refund-drift",
                Amount: 25m,
                Currency: "EGP",
                IsFullRefund: false,
                PaymentMethod: "stripe",
                IsPostSettlementRefund: true,
                SettlementBatchId: Guid.NewGuid(),
                SettlementType: "GATEWAY",
                SettlementId: "set-drift",
                PayoutBatchId: "payout-drift",
                SettledAtUtc: DateTime.UtcNow,
                FinancialPostingStatus: FinancialPostingStatus.Pending,
                FinancialPostingJournalId: null,
                CurrentOrderPaymentStatus: PaymentStatus.Settled,
                SettlementAuthorityMissing: false,
                RecordedSettlementOrderCount: 2,
                CurrentSettlementOrderCount: 1,
                SettlementIncludesOrder: true));

        Assert.False(decision.ShouldPost);
        Assert.True(decision.ShouldRetry);
        Assert.Contains("settlement lineage drifted", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateSettlementPosting_AcceptsCurrentSettlementOrderLineage()
    {
        var decision = AccountingLedgerAuthority.EvaluateSettlementPosting(
            new SettlementAccountingAuthoritySnapshot(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "GATEWAY",
                "set-current",
                "stripe",
                GrossAmount: 100m,
                Fees: 5m,
                Currency: "EGP",
                SettledAtUtc: DateTime.UtcNow,
                RecordedOrderCount: 2,
                CurrentOrderCount: 2));

        Assert.True(decision.ShouldPost);
        Assert.False(decision.ShouldRetry);
        Assert.Null(decision.Reason);
    }

    private static Order CreateOrder(Guid tenantId, Guid orderId)
    {
        return new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = $"SET-{orderId:N}",
            Status = OrderStatus.Completed,
            FulfillmentStatus = FulfillmentStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled,
            Currency = "EGP",
            ShippingAddress = "Settlement authority street",
            Total = 100m,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"AccountingLedgerAuthority_{Guid.NewGuid():N}")
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new ApplicationDbContext(options, tenantContext);
    }
}
