using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Moq;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests.Accounting;

/// <summary>
/// Unit tests for TreasuryTransferService.
/// </summary>
public class TreasuryTransferServiceTests
{
    private readonly Mock<IFinancialPostingService> _postingServiceMock;
    private readonly TreasuryTransferService _service;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _cashboxId;
    private readonly Guid _bankId;
    private readonly Guid _clearingId;
    
    public TreasuryTransferServiceTests()
    {
        _postingServiceMock = new Mock<IFinancialPostingService>();
        var logger = Mock.Of<ILogger<TreasuryTransferService>>();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"TreasuryTransferService_{Guid.NewGuid():N}")
            .Options;
        var db = new ApplicationDbContext(options);
        var cashbox = Account.Create(_tenantId, "1010", "Cashbox", AccountType.Asset);
        var bank = Account.Create(_tenantId, "1020", "Bank", AccountType.Asset);
        var clearing = Account.Create(_tenantId, "1110", "Clearing", AccountType.Asset);
        _cashboxId = cashbox.Id;
        _bankId = bank.Id;
        _clearingId = clearing.Id;
        db.LedgerAccounts.AddRange(cashbox, bank, clearing);
        db.TreasuryAccounts.AddRange(
            TreasuryAccount.CreateCashbox(_tenantId, cashbox.Id, "Cashbox"),
            TreasuryAccount.CreateBank(_tenantId, bank.Id, "Bank"),
            TreasuryAccount.CreateClearing(_tenantId, clearing.Id, "Clearing"));
        db.SaveChanges();

        _service = new TreasuryTransferService(_postingServiceMock.Object, db, logger);
        
        // Setup default mock behavior
        _postingServiceMock
            .Setup(s => s.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PostingRequest req, CancellationToken _) => new JournalEntry
            {
                Id = Guid.NewGuid(),
                TenantId = req.TenantId,
                JournalNumber = 1,
                IdempotencyKey = req.IdempotencyKey
            });
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // TRANSFER TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task TransferAsync_CreatesBalancedJournal()
    {
        // Arrange
        PostingRequest? capturedRequest = null;
        _postingServiceMock
            .Setup(s => s.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new JournalEntry { Id = Guid.NewGuid(), JournalNumber = 1 });
        
        // Act
        await _service.TransferAsync(_tenantId, _cashboxId, _bankId, 100m, "Test transfer");
        
        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal(_tenantId, capturedRequest!.TenantId);
        Assert.Equal(2, capturedRequest.Lines.Count);
        
        // Verify debit and credit
        var debitLine = capturedRequest.Lines.First(l => l.DebitAmount > 0);
        var creditLine = capturedRequest.Lines.First(l => l.CreditAmount > 0);
        
        Assert.Equal(_bankId, debitLine.AccountId);
        Assert.Equal(100m, debitLine.DebitAmount);
        
        Assert.Equal(_cashboxId, creditLine.AccountId);
        Assert.Equal(100m, creditLine.CreditAmount);
    }
    
    [Fact]
    public async Task TransferAsync_GeneratesIdempotencyKey()
    {
        // Arrange
        var transferId = Guid.NewGuid();
        PostingRequest? capturedRequest = null;
        _postingServiceMock
            .Setup(s => s.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new JournalEntry { Id = Guid.NewGuid(), JournalNumber = 1 });
        
        // Act
        await _service.TransferAsync(_tenantId, _cashboxId, _bankId, 100m, "Test", transferId);
        
        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Contains($"TRANSFER:{_tenantId}:{transferId}", capturedRequest!.IdempotencyKey);
    }
    
    [Fact]
    public async Task TransferAsync_RejectsZeroAmount()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.TransferAsync(_tenantId, _cashboxId, _bankId, 0m, "Test"));
    }
    
    [Fact]
    public async Task TransferAsync_RejectsNegativeAmount()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.TransferAsync(_tenantId, _cashboxId, _bankId, -100m, "Test"));
    }
    
    [Fact]
    public async Task TransferAsync_RejectsSameAccount()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.TransferAsync(_tenantId, _cashboxId, _cashboxId, 100m, "Test"));
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // SETTLEMENT TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task SettleClearingAsync_CreatesBalancedJournal()
    {
        // Arrange
        PostingRequest? capturedRequest = null;
        _postingServiceMock
            .Setup(s => s.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new JournalEntry { Id = Guid.NewGuid(), JournalNumber = 1 });
        
        // Act
        await _service.SettleClearingAsync(_tenantId, _clearingId, _bankId, 500m, "Visa settlement");
        
        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal("ClearingSettlement", capturedRequest!.SourceType);
        
        var debitLine = capturedRequest.Lines.First(l => l.DebitAmount > 0);
        var creditLine = capturedRequest.Lines.First(l => l.CreditAmount > 0);
        
        Assert.Equal(_bankId, debitLine.AccountId);
        Assert.Equal(500m, debitLine.DebitAmount);
        
        Assert.Equal(_clearingId, creditLine.AccountId);
        Assert.Equal(500m, creditLine.CreditAmount);
    }
    
    [Fact]
    public async Task SettleClearingAsync_GeneratesSettlementIdempotencyKey()
    {
        // Arrange
        var settlementId = Guid.NewGuid();
        PostingRequest? capturedRequest = null;
        _postingServiceMock
            .Setup(s => s.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new JournalEntry { Id = Guid.NewGuid(), JournalNumber = 1 });
        
        // Act
        await _service.SettleClearingAsync(_tenantId, _clearingId, _bankId, 500m, "Test", settlementId);
        
        // Assert
        Assert.Contains($"SETTLEMENT:{_tenantId}:{settlementId}", capturedRequest!.IdempotencyKey);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // DEPOSIT TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task DepositToBankAsync_CreatesBalancedJournal()
    {
        // Arrange
        PostingRequest? capturedRequest = null;
        _postingServiceMock
            .Setup(s => s.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new JournalEntry { Id = Guid.NewGuid(), JournalNumber = 1 });
        
        // Act
        await _service.DepositToBankAsync(_tenantId, _cashboxId, _bankId, 1000m, "Daily deposit");
        
        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal("BankDeposit", capturedRequest!.SourceType);
        
        var debitLine = capturedRequest.Lines.First(l => l.DebitAmount > 0);
        var creditLine = capturedRequest.Lines.First(l => l.CreditAmount > 0);
        
        Assert.Equal(_bankId, debitLine.AccountId);
        Assert.Equal(1000m, debitLine.DebitAmount);
        
        Assert.Equal(_cashboxId, creditLine.AccountId);
        Assert.Equal(1000m, creditLine.CreditAmount);
    }
    
    [Fact]
    public async Task DepositToBankAsync_GeneratesDepositIdempotencyKey()
    {
        // Arrange
        var depositId = Guid.NewGuid();
        PostingRequest? capturedRequest = null;
        _postingServiceMock
            .Setup(s => s.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new JournalEntry { Id = Guid.NewGuid(), JournalNumber = 1 });
        
        // Act
        await _service.DepositToBankAsync(_tenantId, _cashboxId, _bankId, 1000m, "Test", depositId);
        
        // Assert
        Assert.Contains($"DEPOSIT:{_tenantId}:{depositId}", capturedRequest!.IdempotencyKey);
    }
    
    [Fact]
    public async Task DepositToBankAsync_RejectsInvalidAmount()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.DepositToBankAsync(_tenantId, _cashboxId, _bankId, 0m, "Test"));
    }
}
