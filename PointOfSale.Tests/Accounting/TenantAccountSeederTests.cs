using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests.Accounting;

/// <summary>
/// Unit tests for Phase 2: Chart of Accounts Seeding & Treasury.
/// </summary>
public class TenantAccountSeederTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly TenantAccountSeeder _seeder;
    private readonly Guid _tenantId = Guid.NewGuid();
    
    public TenantAccountSeederTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _db = new ApplicationDbContext(options);
        
        var logger = Mock.Of<ILogger<TenantAccountSeeder>>();
        _seeder = new TenantAccountSeeder(_db, logger);
    }
    
    public void Dispose()
    {
        _db.Dispose();
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // ACCOUNT SEEDING TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task SeedAccountsAsync_CreatesAllSystemAccounts()
    {
        // Act
        await _seeder.SeedAccountsAsync(_tenantId);
        
        // Assert
        var accounts = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId)
            .ToListAsync();
        
        // Should have all template accounts
        Assert.Equal(SystemAccountTemplates.All.Count, accounts.Count);
        
        // All should be system accounts
        Assert.True(accounts.All(a => a.IsSystem));
        
        // Verify specific accounts exist
        Assert.Contains(accounts, a => a.AccountCode == "1010"); // Cashbox Main
        Assert.Contains(accounts, a => a.AccountCode == "1040"); // Cash Float Reserve
        Assert.Contains(accounts, a => a.AccountCode == "1110"); // Visa Clearing
        Assert.Contains(accounts, a => a.AccountCode == "2030"); // Treasury Suspense
        Assert.Contains(accounts, a => a.AccountCode == "4010"); // POS Sales
        Assert.Contains(accounts, a => a.AccountCode == "6010"); // Gateway Fees
        Assert.Contains(accounts, a => a.AccountCode == AccountCodes.MarketplaceVendorReceivable);
    }
    
    [Fact]
    public async Task SeedAccountsAsync_IsIdempotent()
    {
        // Act - Seed twice
        await _seeder.SeedAccountsAsync(_tenantId);
        var countAfterFirst = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId)
            .CountAsync();
        
        await _seeder.SeedAccountsAsync(_tenantId);
        var countAfterSecond = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId)
            .CountAsync();
        
        // Assert - Same count
        Assert.Equal(countAfterFirst, countAfterSecond);
    }
    
    [Fact]
    public async Task SeedAccountsAsync_SetsClearingFlagsCorrectly()
    {
        // Act
        await _seeder.SeedAccountsAsync(_tenantId);
        
        // Assert
        var visaClearing = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .FirstAsync(a => a.TenantId == _tenantId && a.AccountCode == "1110");
        
        Assert.True(visaClearing.IsClearing);
        Assert.Equal("Gateway", visaClearing.ClearingSourceType);
        Assert.Equal("visa", visaClearing.ClearingSourceCode);
        Assert.Equal(3, visaClearing.ExpectedSettlementDays);
    }
    
    [Fact]
    public async Task SeedAccountsAsync_SetsPreventNegativeCorrectly()
    {
        // Act
        await _seeder.SeedAccountsAsync(_tenantId);
        
        // Assert
        var cashbox = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .FirstAsync(a => a.TenantId == _tenantId && a.AccountCode == "1010");
        
        Assert.True(cashbox.PreventNegative);
        
        var revenue = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .FirstAsync(a => a.TenantId == _tenantId && a.AccountCode == "4010");
        
        Assert.False(revenue.PreventNegative);
    }
    
    [Fact]
    public async Task SeedAccountsAsync_SetsParentHierarchyCorrectly()
    {
        // Act
        await _seeder.SeedAccountsAsync(_tenantId);
        
        // Assert
        var cashbox = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .FirstAsync(a => a.TenantId == _tenantId && a.AccountCode == "1010");
        
        var cashAndBank = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .FirstAsync(a => a.TenantId == _tenantId && a.AccountCode == "1000");
        
        Assert.Equal(cashAndBank.Id, cashbox.ParentAccountId);
        Assert.Equal(2, cashbox.Level);
        Assert.Equal(1, cashAndBank.Level);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // PAYMENT METHOD SEEDING TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task SeedPaymentMethodsAsync_CreatesDefaultMethods()
    {
        // Arrange - Need accounts first
        await _seeder.SeedAccountsAsync(_tenantId);
        
        // Act
        await _seeder.SeedPaymentMethodsAsync(_tenantId);
        
        // Assert
        var methods = await _db.PaymentMethods
            .IgnoreQueryFilters()
            .Where(pm => pm.TenantId == _tenantId)
            .ToListAsync();
        
        Assert.True(methods.Count >= 4); // At least CASH, CARD, STRIPE, COD
        Assert.All(methods, pm => Assert.True(pm.IsSystem));
        
        // Verify specific methods
        Assert.Contains(methods, pm => pm.Code == PaymentMethodCodes.Cash);
        Assert.Contains(methods, pm => pm.Code == PaymentMethodCodes.Card);
    }
    
    [Fact]
    public async Task SeedPaymentMethodsAsync_CashHasTargetAccount()
    {
        // Arrange
        await _seeder.SeedAccountsAsync(_tenantId);
        
        // Act
        await _seeder.SeedPaymentMethodsAsync(_tenantId);
        
        // Assert
        var cashMethod = await _db.PaymentMethods
            .IgnoreQueryFilters()
            .FirstAsync(pm => pm.TenantId == _tenantId && pm.Code == PaymentMethodCodes.Cash);
        
        Assert.NotNull(cashMethod.TargetAccountId);
        Assert.Null(cashMethod.ClearingAccountId); // Cash has no clearing
        
        var targetAccount = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .FirstAsync(a => a.Id == cashMethod.TargetAccountId);
        
        Assert.Equal("1010", targetAccount.AccountCode); // Cashbox Main
    }
    
    [Fact]
    public async Task SeedPaymentMethodsAsync_CardHasClearingAccount()
    {
        // Arrange
        await _seeder.SeedAccountsAsync(_tenantId);
        
        // Act
        await _seeder.SeedPaymentMethodsAsync(_tenantId);
        
        // Assert
        var cardMethod = await _db.PaymentMethods
            .IgnoreQueryFilters()
            .FirstAsync(pm => pm.TenantId == _tenantId && pm.Code == PaymentMethodCodes.Card);
        
        Assert.Null(cardMethod.TargetAccountId); // Card uses clearing
        Assert.NotNull(cardMethod.ClearingAccountId);
        Assert.NotNull(cardMethod.FeesAccountId);
        
        var clearingAccount = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .FirstAsync(a => a.Id == cardMethod.ClearingAccountId);
        
        Assert.Equal("1110", clearingAccount.AccountCode); // Visa Clearing
    }
    
    [Fact]
    public async Task SeedPaymentMethodsAsync_IsIdempotent()
    {
        // Arrange
        await _seeder.SeedAccountsAsync(_tenantId);
        
        // Act
        await _seeder.SeedPaymentMethodsAsync(_tenantId);
        var countAfterFirst = await _db.PaymentMethods
            .IgnoreQueryFilters()
            .Where(pm => pm.TenantId == _tenantId)
            .CountAsync();
        
        await _seeder.SeedPaymentMethodsAsync(_tenantId);
        var countAfterSecond = await _db.PaymentMethods
            .IgnoreQueryFilters()
            .Where(pm => pm.TenantId == _tenantId)
            .CountAsync();
        
        // Assert
        Assert.Equal(countAfterFirst, countAfterSecond);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // TREASURY ACCOUNT SEEDING TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task SeedTreasuryAccountsAsync_CreatesDefaultTreasuryAccounts()
    {
        // Arrange
        await _seeder.SeedAccountsAsync(_tenantId);
        
        // Act
        await _seeder.SeedTreasuryAccountsAsync(_tenantId);
        
        // Assert
        var treasuryAccounts = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .Where(ta => ta.TenantId == _tenantId)
            .ToListAsync();
        
        Assert.True(treasuryAccounts.Count >= 4); // Cashbox, Bank, Visa, Stripe, COD, Wallet clearings
    }
    
    [Fact]
    public async Task SeedTreasuryAccountsAsync_SetsTypesCorrectly()
    {
        // Arrange
        await _seeder.SeedAccountsAsync(_tenantId);
        
        // Act
        await _seeder.SeedTreasuryAccountsAsync(_tenantId);
        
        // Assert
        var cashboxTreasury = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .FirstAsync(ta => ta.TenantId == _tenantId && ta.Type == TreasuryAccountType.Cashbox);
        
        Assert.Equal("Main Cashbox", cashboxTreasury.Name);
        Assert.True(cashboxTreasury.IsDefault);
        
        var clearingTreasury = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .FirstAsync(ta => ta.TenantId == _tenantId && ta.Name == "Visa Clearing");
        
        Assert.Equal(TreasuryAccountType.Clearing, clearingTreasury.Type);
        Assert.False(clearingTreasury.IsDefault);
    }
    
    [Fact]
    public async Task SeedTreasuryAccountsAsync_IsIdempotent()
    {
        // Arrange
        await _seeder.SeedAccountsAsync(_tenantId);
        
        // Act
        await _seeder.SeedTreasuryAccountsAsync(_tenantId);
        var countAfterFirst = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .Where(ta => ta.TenantId == _tenantId)
            .CountAsync();
        
        await _seeder.SeedTreasuryAccountsAsync(_tenantId);
        var countAfterSecond = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .Where(ta => ta.TenantId == _tenantId)
            .CountAsync();
        
        // Assert
        Assert.Equal(countAfterFirst, countAfterSecond);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // FULL SEED TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task SeedAsync_CreatesEverything()
    {
        // Act
        await _seeder.SeedAsync(_tenantId);
        
        // Assert
        var accounts = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId)
            .ToListAsync();
        
        var paymentMethods = await _db.PaymentMethods
            .IgnoreQueryFilters()
            .Where(pm => pm.TenantId == _tenantId)
            .ToListAsync();
        
        var treasuryAccounts = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .Where(ta => ta.TenantId == _tenantId)
            .ToListAsync();
        
        Assert.NotEmpty(accounts);
        Assert.NotEmpty(paymentMethods);
        Assert.NotEmpty(treasuryAccounts);
    }
    
    [Fact]
    public async Task SeedAsync_IsFullyIdempotent()
    {
        // Act
        await _seeder.SeedAsync(_tenantId);
        
        var firstCount = new
        {
            Accounts = await _db.LedgerAccounts.IgnoreQueryFilters().Where(a => a.TenantId == _tenantId).CountAsync(),
            PaymentMethods = await _db.PaymentMethods.IgnoreQueryFilters().Where(pm => pm.TenantId == _tenantId).CountAsync(),
            TreasuryAccounts = await _db.TreasuryAccounts.IgnoreQueryFilters().Where(ta => ta.TenantId == _tenantId).CountAsync()
        };
        
        // Seed again
        await _seeder.SeedAsync(_tenantId);
        
        var secondCount = new
        {
            Accounts = await _db.LedgerAccounts.IgnoreQueryFilters().Where(a => a.TenantId == _tenantId).CountAsync(),
            PaymentMethods = await _db.PaymentMethods.IgnoreQueryFilters().Where(pm => pm.TenantId == _tenantId).CountAsync(),
            TreasuryAccounts = await _db.TreasuryAccounts.IgnoreQueryFilters().Where(ta => ta.TenantId == _tenantId).CountAsync()
        };
        
        // Assert
        Assert.Equal(firstCount.Accounts, secondCount.Accounts);
        Assert.Equal(firstCount.PaymentMethods, secondCount.PaymentMethods);
        Assert.Equal(firstCount.TreasuryAccounts, secondCount.TreasuryAccounts);
    }
}
