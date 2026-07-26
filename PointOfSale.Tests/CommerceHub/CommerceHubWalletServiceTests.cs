using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubWalletServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubWalletService _walletService;

    public CommerceHubWalletServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _walletService = new CommerceHubWalletService(_dbContext);
    }

    [Fact]
    public async Task GetPlatformBalanceAsync_ShouldComputeBalanceFromLedgerOnly()
    {
        var vendorId = Guid.NewGuid();

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            120m,
            CommerceHubFinancialReferenceType.Order,
            createdAtUtc: new DateTime(2026, 3, 24, 10, 0, 0, DateTimeKind.Utc));

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            15m,
            CommerceHubFinancialReferenceType.Order,
            createdAtUtc: new DateTime(2026, 3, 24, 10, 5, 0, DateTimeKind.Utc));

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            30m,
            CommerceHubFinancialReferenceType.Refund,
            createdAtUtc: new DateTime(2026, 3, 24, 10, 10, 0, DateTimeKind.Utc));

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            5m,
            CommerceHubFinancialReferenceType.Refund,
            createdAtUtc: new DateTime(2026, 3, 24, 10, 15, 0, DateTimeKind.Utc));

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            90m,
            CommerceHubFinancialReferenceType.Order,
            createdAtUtc: new DateTime(2026, 3, 24, 10, 20, 0, DateTimeKind.Utc));

        await _dbContext.SaveChangesAsync();

        var balance = await _walletService.GetPlatformBalanceAsync();

        Assert.Equal("Platform", balance.EntityType);
        Assert.Null(balance.EntityId);
        Assert.Equal(120m, balance.TotalCredits);
        Assert.Equal(30m, balance.TotalDebits);
        Assert.Equal(90m, balance.Balance);
        Assert.Equal("EGP", balance.Currency);
        Assert.Equal(2, balance.TransactionsCount);
        Assert.Equal(new DateTime(2026, 3, 24, 10, 10, 0, DateTimeKind.Utc), balance.LastTransactionAtUtc);
    }

    [Fact]
    public async Task GetVendorBalanceAsync_ShouldFilterByVendorId()
    {
        var vendorA = Guid.NewGuid();
        var vendorB = Guid.NewGuid();

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorA,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            100m,
            CommerceHubFinancialReferenceType.Order);

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorA,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            25m,
            CommerceHubFinancialReferenceType.Refund);

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorA,
            CommerceHubFinancialTransactionType.Adjustment,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            CommerceHubFinancialReferenceType.Payout);

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorB,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            250m,
            CommerceHubFinancialReferenceType.Order);

        await _dbContext.SaveChangesAsync();

        var balance = await _walletService.GetVendorBalanceAsync(vendorA);

        Assert.Equal("Vendor", balance.EntityType);
        Assert.Equal(vendorA, balance.EntityId);
        Assert.Equal(110m, balance.TotalCredits);
        Assert.Equal(25m, balance.TotalDebits);
        Assert.Equal(85m, balance.Balance);
        Assert.Equal(3, balance.TransactionsCount);
    }

    [Fact]
    public async Task GetVendorStatementAsync_ShouldReturnChronologicalTransactionsWithRunningTotal()
    {
        var vendorId = Guid.NewGuid();

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            100m,
            CommerceHubFinancialReferenceType.Order,
            createdAtUtc: new DateTime(2026, 3, 24, 9, 0, 0, DateTimeKind.Utc),
            description: "Initial order");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Adjustment,
            CommerceHubFinancialTransactionDirection.Credit,
            5m,
            CommerceHubFinancialReferenceType.Payout,
            createdAtUtc: new DateTime(2026, 3, 24, 9, 5, 0, DateTimeKind.Utc),
            description: "Manual adjustment");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Payout,
            CommerceHubFinancialTransactionDirection.Debit,
            30m,
            CommerceHubFinancialReferenceType.Payout,
            createdAtUtc: new DateTime(2026, 3, 24, 9, 10, 0, DateTimeKind.Utc),
            description: "Vendor payout");

        await _dbContext.SaveChangesAsync();

        var statement = await _walletService.GetVendorStatementAsync(vendorId);

        Assert.Equal("Vendor", statement.EntityType);
        Assert.Equal(vendorId, statement.EntityId);
        Assert.Equal(105m, statement.TotalCredits);
        Assert.Equal(30m, statement.TotalDebits);
        Assert.Equal(75m, statement.Balance);
        Assert.Equal(3, statement.Transactions.Count);

        Assert.Collection(
            statement.Transactions,
            first =>
            {
                Assert.Equal("OrderPayment", first.TransactionType);
                Assert.Equal("Credit", first.Direction);
                Assert.Equal(100m, first.Amount);
                Assert.Equal(100m, first.RunningTotal);
                Assert.Equal("Initial order", first.Description);
            },
            second =>
            {
                Assert.Equal("Adjustment", second.TransactionType);
                Assert.Equal("Credit", second.Direction);
                Assert.Equal(5m, second.Amount);
                Assert.Equal(105m, second.RunningTotal);
                Assert.Equal("Manual adjustment", second.Description);
            },
            third =>
            {
                Assert.Equal("Payout", third.TransactionType);
                Assert.Equal("Debit", third.Direction);
                Assert.Equal(30m, third.Amount);
                Assert.Equal(75m, third.RunningTotal);
                Assert.Equal("Vendor payout", third.Description);
            });
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private void SeedTransaction(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        CommerceHubFinancialReferenceType referenceType,
        DateTime? createdAtUtc = null,
        string? description = null)
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
                description ?? $"{transactionType} {direction}",
                createdAtUtc: createdAtUtc));
    }
}
