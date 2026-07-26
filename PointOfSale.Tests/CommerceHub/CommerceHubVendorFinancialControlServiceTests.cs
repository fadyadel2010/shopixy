using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubVendorFinancialControlServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubVendorFinancialControlService _service;

    public CommerceHubVendorFinancialControlServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        var walletService = new CommerceHubWalletService(_dbContext);
        var ledgerService = new CommerceHubFinancialLedgerService(
            _dbContext,
            NullLogger<CommerceHubFinancialLedgerService>.Instance);
        var debtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        var financialSafetyGuardService = new CommerceHubFinancialSafetyGuardService(_dbContext);

        _service = new CommerceHubVendorFinancialControlService(
            _dbContext,
            walletService,
            debtService,
            ledgerService,
            financialSafetyGuardService);
    }

    [Fact]
    public async Task GetVendorControlAsync_CommissionDebtAfterGrace_ShouldSuspendEvenWhenWalletIsPositive()
    {
        var vendorId = await SeedVendorAsync("Risk Vendor");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            2500m,
            CommerceHubFinancialReferenceType.Order,
            createdAtUtc: DateTime.UtcNow.AddDays(-5));
        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            1200m,
            CommerceHubFinancialReferenceType.Order,
            createdAtUtc: DateTime.UtcNow.AddDays(-5));

        await _dbContext.SaveChangesAsync();

        var control = await _service.GetVendorControlAsync(vendorId);

        Assert.Equal(vendorId, control.VendorId);
        Assert.Equal(1300m, control.CurrentBalance);
        Assert.Equal(1200m, control.AmountOwedByVendor);
        Assert.False(control.IsAtRisk);
        Assert.True(control.IsSuspendedFromNewOrders);
        Assert.Equal(1000m, control.HardLimitAmount);
        Assert.Equal(3, control.GracePeriodDays);
        Assert.False(control.IsGracePeriodActive);
        Assert.Equal(200.01m, control.MinimumPaymentToRestoreOrdering);
        Assert.Contains("settle at least 200.01 EGP", control.RequiredAction);
        Assert.Equal(0m, control.TotalSettlementPaymentsToPlatform);
        Assert.Empty(control.RecentSettlements);
    }

    [Fact]
    public async Task GetVendorControlAsync_ThresholdInsideGracePeriod_ShouldSuspendNewOrdersImmediately()
    {
        var vendorId = await SeedVendorAsync("Grace Vendor");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            1100m,
            CommerceHubFinancialReferenceType.Order,
            createdAtUtc: DateTime.UtcNow.AddDays(-1));

        await _dbContext.SaveChangesAsync();

        var control = await _service.GetVendorControlAsync(vendorId);

        Assert.Equal(1100m, control.AmountOwedByVendor);
        Assert.False(control.IsAtRisk);
        Assert.True(control.IsSuspendedFromNewOrders);
        Assert.True(control.IsGracePeriodActive);
        Assert.NotNull(control.GraceDeadlineUtc);
        Assert.Contains("New CommerceHub orders are suspended", control.RequiredAction);
    }

    [Fact]
    public async Task RecordSettlementPaymentAsync_ShouldCreateSettlementAndReduceCommissionDebt()
    {
        var vendorId = await SeedVendorAsync("Settlement Vendor");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            1200m,
            CommerceHubFinancialReferenceType.Order,
            createdAtUtc: DateTime.UtcNow.AddDays(-5));

        await _dbContext.SaveChangesAsync();

        var result = await _service.RecordSettlementPaymentAsync(
            vendorId,
            new CommerceHubRecordSettlementPaymentRequest
            {
                Amount = 250m,
                IdempotencyKey = "settlement-payment-001",
                ExternalReference = "bank-transfer-001",
                Notes = "Vendor paid part of the outstanding debt."
            },
            actorId: Guid.NewGuid(),
            actorEmail: "finance-admin@commercehub.local");

        var settlement = await _dbContext.CommerceHubVendorSettlements.SingleAsync();
        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceId == settlement.Id)
            .OrderBy(entry => entry.EntityType)
            .ToListAsync();

        // Verify persistence integrity
        Assert.Equal("settlement-payment-001", settlement.IdempotencyKey);
        Assert.Equal("bank-transfer-001", settlement.ExternalReference);

        Assert.Equal("SettlementPayment", result.Settlement.SettlementType);
        Assert.Equal(250m, result.Settlement.Amount);
        Assert.Equal("settlement-payment-001", result.Settlement.IdempotencyKey);
        Assert.Equal("bank-transfer-001", result.Settlement.ExternalReference);
        Assert.Equal(-950m, result.Control.CurrentBalance);
        Assert.True(result.Control.IsAtRisk);
        Assert.False(result.Control.IsSuspendedFromNewOrders);
        Assert.Equal(950m, result.Control.AmountOwedByVendor);
        Assert.Equal(250m, result.Control.TotalSettlementPaymentsToPlatform);
        Assert.Single(result.Control.RecentSettlements);

        Assert.Equal(2, ledgerEntries.Count);
        Assert.Contains(ledgerEntries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Credit &&
            entry.ReferenceType == CommerceHubFinancialReferenceType.Settlement &&
            entry.Amount == 250m);
        Assert.Contains(ledgerEntries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Credit &&
            entry.ReferenceType == CommerceHubFinancialReferenceType.Settlement &&
            entry.Amount == 250m);
    }

    [Fact]
    public async Task AdjustVendorBalanceAsync_Debit_ShouldIncreaseVendorDebt()
    {
        var vendorId = await SeedVendorAsync("Adjustment Vendor");

        var result = await _service.AdjustVendorBalanceAsync(
            vendorId,
            new CommerceHubAdjustVendorBalanceRequest
            {
                Amount = 75m,
                Direction = CommerceHubFinancialTransactionDirection.Debit,
                IdempotencyKey = "adjustment-001",
                ExternalReference = "admin-adjustment-001",
                Notes = "Manual debit adjustment after settlement dispute."
            },
            actorId: Guid.NewGuid(),
            actorEmail: "finance-admin@commercehub.local");

        var settlement = await _dbContext.CommerceHubVendorSettlements.SingleAsync();
        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceId == settlement.Id)
            .ToListAsync();

        Assert.Equal("AdminAdjustment", result.Settlement.SettlementType);
        Assert.Equal(-75m, result.Control.CurrentBalance);
        Assert.True(result.Control.IsAtRisk);
        Assert.False(result.Control.IsSuspendedFromNewOrders);
        Assert.Equal(75m, result.Control.AmountOwedByVendor);

        Assert.Equal(2, ledgerEntries.Count);
        Assert.Contains(ledgerEntries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Debit &&
            entry.ReferenceType == CommerceHubFinancialReferenceType.Adjustment &&
            entry.Amount == 75m);
        Assert.Contains(ledgerEntries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Debit &&
            entry.ReferenceType == CommerceHubFinancialReferenceType.Adjustment &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Receivable &&
            entry.Amount == 75m);
    }

    [Fact]
    public async Task ClearVendorDebtAsync_ShouldZeroOutCommissionDebt()
    {
        var vendorId = await SeedVendorAsync("Debt Clearance Vendor");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            180m,
            CommerceHubFinancialReferenceType.Order);

        await _dbContext.SaveChangesAsync();

        var result = await _service.ClearVendorDebtAsync(
            vendorId,
            new CommerceHubClearVendorDebtRequest
            {
                IdempotencyKey = "debt-clearance-001",
                ExternalReference = "debt-clearance-approval-001",
                Notes = "Debt written off after platform review."
            },
            actorId: Guid.NewGuid(),
            actorEmail: "finance-admin@commercehub.local");

        var settlement = await _dbContext.CommerceHubVendorSettlements.SingleAsync();
        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceId == settlement.Id)
            .ToListAsync();

        Assert.Equal("DebtClearance", result.Settlement.SettlementType);
        Assert.Equal(0m, result.Control.CurrentBalance);
        Assert.False(result.Control.IsAtRisk);
        Assert.False(result.Control.IsSuspendedFromNewOrders);
        Assert.Equal(0m, result.Control.AmountOwedByVendor);

        Assert.Equal(2, ledgerEntries.Count);
        Assert.Contains(ledgerEntries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Credit &&
            entry.ReferenceType == CommerceHubFinancialReferenceType.Adjustment &&
            entry.Amount == 180m);
        Assert.Contains(ledgerEntries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Credit &&
            entry.ReferenceType == CommerceHubFinancialReferenceType.Adjustment &&
            entry.FinancialBucket == CommerceHubFinancialBucket.Receivable &&
            entry.Amount == 180m);
    }

    [Fact]
    public async Task RecordSettlementPaymentAsync_ShouldReplayIdempotently_ForSameEvidence()
    {
        var vendorId = await SeedVendorAsync("Replay Settlement Vendor");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            300m,
            CommerceHubFinancialReferenceType.Order);

        await _dbContext.SaveChangesAsync();

        var request = new CommerceHubRecordSettlementPaymentRequest
        {
            Amount = 120m,
            IdempotencyKey = "settlement-payment-replay",
            ExternalReference = "bank-transfer-replay",
            Notes = "Vendor bank transfer."
        };

        var first = await _service.RecordSettlementPaymentAsync(vendorId, request, Guid.NewGuid(), "finance@example.com");
        var second = await _service.RecordSettlementPaymentAsync(vendorId, request, Guid.NewGuid(), "finance@example.com");

        Assert.Equal(first.Settlement.SettlementId, second.Settlement.SettlementId);
        Assert.Single(await _dbContext.CommerceHubVendorSettlements.ToListAsync());
        Assert.Equal(2, await _dbContext.CommerceHubFinancialTransactions
            .CountAsync(entry => entry.ReferenceId == first.Settlement.SettlementId));
    }

    [Fact]
    public async Task RecordSettlementPaymentAsync_ShouldRejectReusedExternalReference()
    {
        var vendorId = await SeedVendorAsync("Duplicate Evidence Vendor");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            500m,
            CommerceHubFinancialReferenceType.Order);

        await _dbContext.SaveChangesAsync();

        await _service.RecordSettlementPaymentAsync(
            vendorId,
            new CommerceHubRecordSettlementPaymentRequest
            {
                Amount = 100m,
                IdempotencyKey = "settlement-payment-a",
                ExternalReference = "bank-transfer-duplicate",
                Notes = "First payment."
            },
            Guid.NewGuid(),
            "finance@example.com");

        var ex = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            _service.RecordSettlementPaymentAsync(
                vendorId,
                new CommerceHubRecordSettlementPaymentRequest
                {
                    Amount = 100m,
                    IdempotencyKey = "settlement-payment-b",
                    ExternalReference = "bank-transfer-duplicate",
                    Notes = "Duplicate payment evidence."
                },
                Guid.NewGuid(),
                "finance@example.com"));

        Assert.Equal("EXTERNAL_REFERENCE_ALREADY_USED", ex.Code);
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

    private void SeedTransaction(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        CommerceHubFinancialReferenceType referenceType,
        DateTime? createdAtUtc = null)
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
                Guid.NewGuid(),
                $"{transactionType} {direction}",
                createdAtUtc: createdAtUtc));
    }
}
