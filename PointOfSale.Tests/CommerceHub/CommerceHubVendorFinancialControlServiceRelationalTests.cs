using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.RegularExpressions;
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

/// <summary>
/// Relational database tests for CommerceHubVendorFinancialControlService that exercise
/// database-enforced unique constraints and filtered indexes.
/// Uses SQLite with relational behavior to verify idempotency and external reference uniqueness.
/// </summary>
public sealed class CommerceHubVendorFinancialControlServiceRelationalTests : IDisposable
{
    private readonly SqliteConnection _persistentConnection;
    private readonly string _databaseName;
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubVendorFinancialControlService _service;

    public CommerceHubVendorFinancialControlServiceRelationalTests()
    {
        // Create a unique database name for this test instance
        _databaseName = $"TestDb_{Guid.NewGuid():N}";
        
        // Create a persistent connection to keep the shared in-memory database alive
        _persistentConnection = CreateOpenConnection();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_persistentConnection)
            .Options;

        _dbContext = new ApplicationDbContext(options);
        
        // Apply migrations to create schema with unique constraints
        _dbContext.Database.EnsureCreated();
        ApplyCommerceHubVendorSettlementsConstraints();
        SeedCommerceHubFinancialGuardLock();

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

    private void ApplyCommerceHubVendorSettlementsConstraints()
    {
        // Apply the unique constraints from migration S64
        _dbContext.Database.ExecuteSqlRaw(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ""UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey""
            ON ""CommerceHubVendorSettlements"" (""VendorId"", ""IdempotencyKey"");
        ");

        _dbContext.Database.ExecuteSqlRaw(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ""UX_CommerceHubVendorSettlements_ExternalReference""
            ON ""CommerceHubVendorSettlements"" (""ExternalReference"")
            WHERE ""ExternalReference"" IS NOT NULL
              AND ""ExternalReference"" <> ''
              AND ""ExternalReference"" NOT LIKE 'legacy:%';
        ");
    }

    private void SeedCommerceHubFinancialGuardLock()
    {
        _dbContext.CommerceHubFinancialGuardLocks.Add(new CommerceHubFinancialGuardLock
        {
            Id = Guid.Parse("7b5b9720-2e31-4d55-808c-6b0dd4d833a1"),
            Scope = "PlatformCash",
            CreatedAtUtc = DateTime.UtcNow
        });
        _dbContext.SaveChanges();
    }

    private CommerceHubVendorFinancialControlService CreateServiceInstance()
    {
        // Create a new connection to the shared in-memory database for true concurrent access
        var connection = CreateOpenConnection();
        
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new ApplicationDbContext(options);

        var walletService = new CommerceHubWalletService(dbContext);
        var ledgerService = new CommerceHubFinancialLedgerService(
            dbContext,
            NullLogger<CommerceHubFinancialLedgerService>.Instance);
        var debtService = new CommerceHubVendorDebtService(
            dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        var financialSafetyGuardService = new CommerceHubFinancialSafetyGuardService(dbContext);

        return new CommerceHubVendorFinancialControlService(
            dbContext,
            walletService,
            debtService,
            ledgerService,
            financialSafetyGuardService);
    }

    private SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection($"DataSource={_databaseName};Mode=Memory;Cache=Shared");
        connection.Open();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim());
        connection.CreateFunction<string?, string?, string?>(
            "btrim",
            (value, characters) => value?.Trim(characters?.ToCharArray() ?? []));
        connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        connection.CreateFunction<string?, string?, string?, string?, string?>(
            "REGEXP_REPLACE",
            (value, pattern, replacement, flags) =>
            {
                if (value is null || pattern is null)
                {
                    return value;
                }

                var regexOptions = flags?.Contains('i') == true
                    ? RegexOptions.IgnoreCase
                    : RegexOptions.None;
                return Regex.Replace(value, pattern, replacement ?? string.Empty, regexOptions);
            });
        return connection;
    }

    [Fact]
    public async Task RecordSettlementPaymentAsync_ShouldReplayIdempotently_DatabaseLevel()
    {
        // Arrange
        var vendorId = await SeedVendorAsync("Replay Settlement Vendor DB");

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
            IdempotencyKey = "settlement-payment-replay-db",
            ExternalReference = "bank-transfer-replay-db",
            Notes = "Vendor bank transfer."
        };

        // Act
        var first = await _service.RecordSettlementPaymentAsync(
            vendorId, 
            request, 
            Guid.NewGuid(), 
            "finance@example.com");
        
        var second = await _service.RecordSettlementPaymentAsync(
            vendorId, 
            request, 
            Guid.NewGuid(), 
            "finance@example.com");

        // Assert - Verify idempotent replay returns same settlement
        Assert.Equal(first.Settlement.SettlementId, second.Settlement.SettlementId);
        Assert.Single(await _dbContext.CommerceHubVendorSettlements.ToListAsync());
        
        // Verify ledger entries were created only once
        Assert.Equal(2, await _dbContext.CommerceHubFinancialTransactions
            .CountAsync(entry => entry.ReferenceId == first.Settlement.SettlementId));

        // Verify persistence integrity
        var settlement = await _dbContext.CommerceHubVendorSettlements.SingleAsync();
        Assert.Equal("settlement-payment-replay-db", settlement.IdempotencyKey);
        Assert.Equal("bank-transfer-replay-db", settlement.ExternalReference);
    }

    [Fact]
    public async Task RecordSettlementPaymentAsync_ShouldRecoverFromIdempotencyRaceCondition_DatabaseLevel()
    {
        // Arrange - Seed vendor and transaction in shared context
        var vendorId = await SeedVendorAsync("Race Condition Vendor DB");

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
            IdempotencyKey = "settlement-payment-race-db",
            ExternalReference = "bank-transfer-race-db",
            Notes = "Vendor bank transfer."
        };

        // Create two separate service instances with independent DbContexts to simulate race
        var service1 = CreateServiceInstance();
        var service2 = CreateServiceInstance();

        // Act - Execute both calls concurrently to trigger DbUpdateException recovery
        var tasks = new[]
        {
            service1.RecordSettlementPaymentAsync(vendorId, request, Guid.NewGuid(), "finance1@example.com"),
            service2.RecordSettlementPaymentAsync(vendorId, request, Guid.NewGuid(), "finance2@example.com")
        };

        var results = await Task.WhenAll(tasks);

        // Assert - Both calls should return the same settlement ID (recovery path worked)
        Assert.Equal(results[0].Settlement.SettlementId, results[1].Settlement.SettlementId);
        
        // Verify only one settlement was persisted
        var settlements = await _dbContext.CommerceHubVendorSettlements.ToListAsync();
        Assert.Single(settlements);
        Assert.Equal("settlement-payment-race-db", settlements[0].IdempotencyKey);
        Assert.Equal("bank-transfer-race-db", settlements[0].ExternalReference);
        
        // Verify ledger entries were created only once (2 mirrored transactions)
        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceId == results[0].Settlement.SettlementId)
            .ToListAsync();
        Assert.Equal(2, ledgerEntries.Count);
    }

    [Fact]
    public async Task RecordSettlementPaymentAsync_ShouldRejectReusedExternalReference_DatabaseLevel()
    {
        // Arrange
        var vendorId = await SeedVendorAsync("Duplicate Evidence Vendor DB");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            500m,
            CommerceHubFinancialReferenceType.Order);

        await _dbContext.SaveChangesAsync();

        // Act - First payment succeeds
        await _service.RecordSettlementPaymentAsync(
            vendorId,
            new CommerceHubRecordSettlementPaymentRequest
            {
                Amount = 100m,
                IdempotencyKey = "settlement-payment-a-db",
                ExternalReference = "bank-transfer-duplicate-db",
                Notes = "First payment."
            },
            Guid.NewGuid(),
            "finance@example.com");

        // Act & Assert - Second payment with same external reference should fail
        var ex = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            _service.RecordSettlementPaymentAsync(
                vendorId,
                new CommerceHubRecordSettlementPaymentRequest
                {
                    Amount = 100m,
                    IdempotencyKey = "settlement-payment-b-db",
                    ExternalReference = "bank-transfer-duplicate-db",
                    Notes = "Duplicate payment evidence."
                },
                Guid.NewGuid(),
                "finance@example.com"));

        Assert.Equal("EXTERNAL_REFERENCE_ALREADY_USED", ex.Code);

        // Verify only one settlement was created
        Assert.Single(await _dbContext.CommerceHubVendorSettlements.ToListAsync());
    }

    [Fact]
    public async Task RecordSettlementPaymentAsync_ShouldRecoverFromExternalReferenceRaceCondition_DatabaseLevel()
    {
        // Arrange - Seed vendor and transaction in shared context
        var vendorId = await SeedVendorAsync("External Reference Race Vendor DB");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            500m,
            CommerceHubFinancialReferenceType.Order);

        await _dbContext.SaveChangesAsync();

        // Create two separate service instances with independent DbContexts to simulate race
        var service1 = CreateServiceInstance();
        var service2 = CreateServiceInstance();

        var request1 = new CommerceHubRecordSettlementPaymentRequest
        {
            Amount = 100m,
            IdempotencyKey = "settlement-payment-ext-race-1-db",
            ExternalReference = "bank-transfer-ext-race-db",
            Notes = "First payment attempt."
        };

        var request2 = new CommerceHubRecordSettlementPaymentRequest
        {
            Amount = 100m,
            IdempotencyKey = "settlement-payment-ext-race-2-db",
            ExternalReference = "bank-transfer-ext-race-db", // Same external reference
            Notes = "Second payment attempt."
        };

        // Act - Execute both calls concurrently with same external reference but different idempotency keys
        var task1 = service1.RecordSettlementPaymentAsync(vendorId, request1, Guid.NewGuid(), "finance1@example.com");
        var task2 = service2.RecordSettlementPaymentAsync(vendorId, request2, Guid.NewGuid(), "finance2@example.com");

        // One should succeed, one should throw
        var exception = await Record.ExceptionAsync(async () => await Task.WhenAll(task1, task2));

        // Assert - One call should have thrown EXTERNAL_REFERENCE_ALREADY_USED
        Assert.NotNull(exception);
        var innerException = exception.InnerException as CommerceHubVendorFinancialControlException 
                             ?? exception as CommerceHubVendorFinancialControlException;
        Assert.NotNull(innerException);
        Assert.Equal("EXTERNAL_REFERENCE_ALREADY_USED", innerException.Code);

        // Verify only one settlement was persisted
        var settlements = await _dbContext.CommerceHubVendorSettlements.ToListAsync();
        Assert.Single(settlements);
        Assert.Equal("bank-transfer-ext-race-db", settlements[0].ExternalReference);
        
        // Verify ledger entries were created only once (2 mirrored transactions)
        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceId == settlements[0].Id)
            .ToListAsync();
        Assert.Equal(2, ledgerEntries.Count);
    }

    [Fact]
    public async Task RecordSettlementPaymentAsync_ShouldEnforceUniqueIdempotencyKeyPerVendor_DatabaseLevel()

    {
        // Arrange
        var vendor1Id = await SeedVendorAsync("Vendor 1 DB");
        var vendor2Id = await SeedVendorAsync("Vendor 2 DB");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendor1Id,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            300m,
            CommerceHubFinancialReferenceType.Order);

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendor2Id,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            300m,
            CommerceHubFinancialReferenceType.Order);

        await _dbContext.SaveChangesAsync();

        var sharedIdempotencyKey = "shared-idempotency-key-db";

        // Act - Same idempotency key should work for different vendors
        var settlement1 = await _service.RecordSettlementPaymentAsync(
            vendor1Id,
            new CommerceHubRecordSettlementPaymentRequest
            {
                Amount = 50m,
                IdempotencyKey = sharedIdempotencyKey,
                ExternalReference = "bank-transfer-vendor1-db",
                Notes = "Vendor 1 payment."
            },
            Guid.NewGuid(),
            "finance@example.com");

        var settlement2 = await _service.RecordSettlementPaymentAsync(
            vendor2Id,
            new CommerceHubRecordSettlementPaymentRequest
            {
                Amount = 50m,
                IdempotencyKey = sharedIdempotencyKey,
                ExternalReference = "bank-transfer-vendor2-db",
                Notes = "Vendor 2 payment."
            },
            Guid.NewGuid(),
            "finance@example.com");

        // Assert - Both settlements should succeed with different IDs
        Assert.NotEqual(settlement1.Settlement.SettlementId, settlement2.Settlement.SettlementId);
        Assert.Equal(2, await _dbContext.CommerceHubVendorSettlements.CountAsync());

        // Verify both have the same idempotency key but different vendors
        var settlements = await _dbContext.CommerceHubVendorSettlements.ToListAsync();
        Assert.All(settlements, s => Assert.Equal(sharedIdempotencyKey, s.IdempotencyKey));
        Assert.Contains(settlements, s => s.VendorId == vendor1Id);
        Assert.Contains(settlements, s => s.VendorId == vendor2Id);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _persistentConnection.Dispose();
    }

    private async Task<Guid> SeedVendorAsync(string name)
    {
        var vendorId = Guid.NewGuid();
        if (!await _dbContext.Currencies.AnyAsync(currency => currency.Code == "EGP"))
        {
            _dbContext.Currencies.Add(new Currency
            {
                Code = "EGP",
                Name = "Egyptian Pound",
                Symbol = "EGP",
                DecimalPlaces = 2
            });
        }

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
        Guid entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        CommerceHubFinancialReferenceType referenceType,
        DateTime? createdAtUtc = null)
    {
        var transaction = CommerceHubFinancialTransaction.Create(
            entityType,
            entityId,
            transactionType,
            direction,
            amount,
            "EGP",
            referenceType,
            Guid.NewGuid(),
            $"Test {transactionType} transaction",
            createdAtUtc: createdAtUtc ?? DateTime.UtcNow,
            createdBy: Guid.NewGuid(),
            financialBucketOverride: CommerceHubFinancialBucket.Receivable);

        _dbContext.CommerceHubFinancialTransactions.Add(transaction);
    }
}
