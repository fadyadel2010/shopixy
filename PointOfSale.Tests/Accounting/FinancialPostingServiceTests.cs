using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests.Accounting;

/// <summary>
/// Unit tests for the FinancialPostingService.
/// Tests the core ledger engine functionality.
/// </summary>
public class FinancialPostingServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly FinancialPostingService _service;
    private readonly Guid _tenantId = Guid.NewGuid();
    
    // Test accounts
    private Account _cashAccount = null!;
    private Account _revenueAccount = null!;
    private Account _expenseAccount = null!;
    private Account _bankAccount = null!;
    
    public FinancialPostingServiceTests()
    {
        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _db = new ApplicationDbContext(options);
        
        var logger = new Mock<ILogger<FinancialPostingService>>();
        _service = new FinancialPostingService(_db, logger.Object);
        
        // Setup test accounts
        SetupTestAccounts();
    }
    
    private void SetupTestAccounts()
    {
        _cashAccount = Account.Create(
            _tenantId,
            "1010",
            "Cash",
            AccountType.Asset,
            preventNegative: true);
        _cashAccount.SetOpeningBalance(10000m);
        
        _revenueAccount = Account.Create(
            _tenantId,
            "4010",
            "Sales Revenue",
            AccountType.Revenue);
        
        _expenseAccount = Account.Create(
            _tenantId,
            "5010",
            "Office Supplies",
            AccountType.Expense);
        
        _bankAccount = Account.Create(
            _tenantId,
            "1020",
            "Bank Account",
            AccountType.Asset,
            preventNegative: true);
        _bankAccount.SetOpeningBalance(50000m);
        
        _db.LedgerAccounts.AddRange(_cashAccount, _revenueAccount, _expenseAccount, _bankAccount);
        _db.SaveChanges();
    }
    
    public void Dispose()
    {
        _db.Dispose();
    }
    
    #region Balanced Journal Tests
    
    [Fact]
    public async Task PostJournalAsync_BalancedJournal_PostsSuccessfully()
    {
        // Arrange
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Test cash sale",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 100m, "Cash received"),
                PostingLine.Credit(_revenueAccount.Id, 100m, "Sale revenue")
            }
        };
        
        // Act
        var journal = await _service.PostJournalAsync(request);
        
        // Assert
        Assert.NotNull(journal);
        Assert.Equal(JournalStatus.Posted, journal.Status);
        Assert.Equal(2, journal.LedgerEntries.Count);
        Assert.True(journal.IsBalanced);
        Assert.Equal(request.IdempotencyKey, journal.IdempotencyKey);
        
        // Verify balances updated
        var cash = await _db.LedgerAccounts.FindAsync(_cashAccount.Id);
        Assert.Equal(10100m, cash!.CurrentBalance); // 10000 + 100
        
        var revenue = await _db.LedgerAccounts.FindAsync(_revenueAccount.Id);
        Assert.Equal(100m, revenue!.CurrentBalance); // 0 + 100 (credit increases revenue)
    }
    
    [Fact]
    public async Task PostJournalAsync_MultiLineJournal_PostsSuccessfully()
    {
        // Arrange: Compound entry with multiple debits
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Split deposit",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 300m, "Cash portion"),
                PostingLine.Debit(_bankAccount.Id, 700m, "Bank portion"),
                PostingLine.Credit(_revenueAccount.Id, 1000m, "Total sale")
            }
        };
        
        // Act
        var journal = await _service.PostJournalAsync(request);
        
        // Assert
        Assert.Equal(3, journal.LedgerEntries.Count);
        Assert.Equal(1000m, journal.TotalDebits);
        Assert.Equal(1000m, journal.TotalCredits);
        Assert.True(journal.IsBalanced);
    }
    
    #endregion
    
    #region Unbalanced Journal Tests
    
    [Fact]
    public async Task PostJournalAsync_UnbalancedJournal_ThrowsUnbalancedJournalException()
    {
        // Arrange
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Unbalanced entry",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 100m),
                PostingLine.Credit(_revenueAccount.Id, 99m) // Mismatch!
            }
        };
        
        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnbalancedJournalException>(
            () => _service.PostJournalAsync(request));
        
        Assert.Equal(100m, ex.TotalDebits);
        Assert.Equal(99m, ex.TotalCredits);
        Assert.Equal(1m, ex.Difference);
    }
    
    [Fact]
    public async Task PostJournalAsync_SingleLine_ThrowsInvalidJournalException()
    {
        // Arrange
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Single line",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 100m)
            }
        };
        
        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidJournalException>(
            () => _service.PostJournalAsync(request));
        
        Assert.Contains("at least 2 lines", ex.Message);
    }
    
    [Fact]
    public async Task PostJournalAsync_BothDebitAndCredit_ThrowsInvalidJournalException()
    {
        // Arrange
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Invalid line",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                new PostingLine { AccountId = _cashAccount.Id, DebitAmount = 100m, CreditAmount = 50m },
                PostingLine.Credit(_revenueAccount.Id, 50m)
            }
        };
        
        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidJournalException>(
            () => _service.PostJournalAsync(request));
        
        Assert.Contains("Cannot have both", ex.Message);
    }
    
    #endregion
    
    #region Idempotency Tests
    
    [Fact]
    public async Task PostJournalAsync_DuplicateIdempotencyKey_ReturnsSameJournal()
    {
        // Arrange
        var idempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}";
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "First post",
            IdempotencyKey = idempotencyKey,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 100m),
                PostingLine.Credit(_revenueAccount.Id, 100m)
            }
        };
        
        // Act
        var journal1 = await _service.PostJournalAsync(request);
        var journal2 = await _service.PostJournalAsync(request);
        
        // Assert
        Assert.Equal(journal1.Id, journal2.Id);
        Assert.Equal(journal1.JournalNumber, journal2.JournalNumber);
        
        // Verify balance was only affected once
        var cash = await _db.LedgerAccounts.FindAsync(_cashAccount.Id);
        Assert.Equal(10100m, cash!.CurrentBalance); // Only +100, not +200
    }
    
    [Fact]
    public async Task GetJournalByIdempotencyKeyAsync_ExistingKey_ReturnsJournal()
    {
        // Arrange
        var idempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}";
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Find me",
            IdempotencyKey = idempotencyKey,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 100m),
                PostingLine.Credit(_revenueAccount.Id, 100m)
            }
        };
        
        var original = await _service.PostJournalAsync(request);
        
        // Act
        var found = await _service.GetJournalByIdempotencyKeyAsync(_tenantId, idempotencyKey);
        
        // Assert
        Assert.NotNull(found);
        Assert.Equal(original.Id, found.Id);
    }
    
    #endregion
    
    #region PreventNegative Tests
    
    [Fact]
    public async Task PostJournalAsync_CausesNegativeBalance_ThrowsInsufficientFundsException()
    {
        // Arrange: Try to spend more than available
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Overdraw attempt",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_expenseAccount.Id, 15000m), // More than cash balance
                PostingLine.Credit(_cashAccount.Id, 15000m)   // Cash has PreventNegative
            }
        };
        
        // Act & Assert
        var ex = await Assert.ThrowsAsync<InsufficientFundsException>(
            () => _service.PostJournalAsync(request));
        
        Assert.Equal("1010", ex.AccountCode);
        Assert.Equal(10000m, ex.CurrentBalance);
        Assert.Equal(15000m, ex.RequestedAmount);
    }
    
    [Fact]
    public async Task PostJournalAsync_ExactBalance_Succeeds()
    {
        // Arrange: Spend exactly available balance
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Exact spend",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_expenseAccount.Id, 10000m),
                PostingLine.Credit(_cashAccount.Id, 10000m)
            }
        };
        
        // Act
        var journal = await _service.PostJournalAsync(request);
        
        // Assert
        Assert.NotNull(journal);
        
        var cash = await _db.LedgerAccounts.FindAsync(_cashAccount.Id);
        Assert.Equal(0m, cash!.CurrentBalance);
    }
    
    #endregion
    
    #region Reversal Tests
    
    [Fact]
    public async Task ReverseJournalAsync_ValidJournal_CreatesReversalAndMarksOriginal()
    {
        // Arrange: Post original journal
        var originalRequest = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Original entry",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 500m),
                PostingLine.Credit(_revenueAccount.Id, 500m)
            }
        };
        
        var original = await _service.PostJournalAsync(originalRequest);
        var cashAfterOriginal = (await _db.LedgerAccounts.FindAsync(_cashAccount.Id))!.CurrentBalance;
        
        // Act
        var reversal = await _service.ReverseJournalAsync(
            _tenantId,
            original.Id,
            "Customer refund");
        
        // Assert
        Assert.NotNull(reversal);
        Assert.Equal(JournalStatus.Posted, reversal.Status);
        Assert.Equal(original.Id, reversal.ReversesJournalId);
        Assert.Contains("Reversal", reversal.Description);
        
        // Verify original is marked as reversed
        var originalReloaded = await _db.JournalEntries.FindAsync(original.Id);
        Assert.Equal(JournalStatus.Reversed, originalReloaded!.Status);
        Assert.Equal(reversal.Id, originalReloaded.ReversedByJournalId);
        
        // Verify balances are back to original
        var cashAfterReversal = (await _db.LedgerAccounts.FindAsync(_cashAccount.Id))!.CurrentBalance;
        Assert.Equal(cashAfterOriginal - 500m, cashAfterReversal); // Should be 10000 again
    }
    
    [Fact]
    public async Task ReverseJournalAsync_AlreadyReversed_ThrowsJournalAlreadyReversedException()
    {
        // Arrange
        var originalRequest = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Original",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 100m),
                PostingLine.Credit(_revenueAccount.Id, 100m)
            }
        };
        
        var original = await _service.PostJournalAsync(originalRequest);
        await _service.ReverseJournalAsync(_tenantId, original.Id, "First reversal");
        
        // Act & Assert
        await Assert.ThrowsAsync<JournalAlreadyReversedException>(
            () => _service.ReverseJournalAsync(_tenantId, original.Id, "Second reversal"));
    }
    
    [Fact]
    public async Task ReverseJournalAsync_NonExistentJournal_ThrowsJournalNotFoundException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<JournalNotFoundException>(
            () => _service.ReverseJournalAsync(_tenantId, Guid.NewGuid(), "Reason"));
    }
    
    #endregion
    
    #region Account Validation Tests
    
    [Fact]
    public async Task PostJournalAsync_NonExistentAccount_ThrowsAccountNotFoundException()
    {
        // Arrange
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Bad account",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(Guid.NewGuid(), 100m), // Non-existent
                PostingLine.Credit(_revenueAccount.Id, 100m)
            }
        };
        
        // Act & Assert
        await Assert.ThrowsAsync<AccountNotFoundException>(
            () => _service.PostJournalAsync(request));
    }
    
    [Fact]
    public async Task PostJournalAsync_InactiveAccount_ThrowsInactiveAccountException()
    {
        // Arrange: Deactivate an account
        _cashAccount.IsActive = false;
        await _db.SaveChangesAsync();
        
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Inactive account",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 100m),
                PostingLine.Credit(_revenueAccount.Id, 100m)
            }
        };
        
        // Act & Assert
        await Assert.ThrowsAsync<InactiveAccountException>(
            () => _service.PostJournalAsync(request));
    }
    
    #endregion
    
    #region Balance After Tests
    
    [Fact]
    public async Task PostJournalAsync_RecordsCorrectBalanceAfter()
    {
        // Arrange
        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Balance after test",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 250m),
                PostingLine.Credit(_revenueAccount.Id, 250m)
            }
        };
        
        // Act
        var journal = await _service.PostJournalAsync(request);
        
        // Assert
        var cashEntry = journal.LedgerEntries.First(e => e.AccountId == _cashAccount.Id);
        Assert.Equal(10250m, cashEntry.BalanceAfter); // 10000 + 250
        
        var revenueEntry = journal.LedgerEntries.First(e => e.AccountId == _revenueAccount.Id);
        Assert.Equal(250m, revenueEntry.BalanceAfter);
    }
    
    #endregion

    #region Purchase Return Treasury Authority Tests

    [Fact]
    public async Task PostJournalAsync_PurchaseReturnWithoutOriginalCashPayment_RejectsTreasuryInflow()
    {
        var invoice = new PurchaseInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = "PUR-NON-CASH",
            InvoiceDate = DateTime.UtcNow.Date,
            Status = InvoiceStatus.Completed,
            TotalAmount = 100m,
            PaidAmount = 100m,
            PaymentMethod = PaymentMethod.Credit
        };
        var purchaseReturn = new Return
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ReturnNumber = "PR-FORGED-CASH",
            ReturnDate = DateTime.UtcNow.Date,
            ReturnType = ReturnType.PurchaseReturn,
            OriginalPurchaseInvoiceId = invoice.Id,
            TotalAmount = 100m,
            RefundMethod = RefundMethod.Cash,
            CashRefundAmount = 100m
        };
        _db.PurchaseInvoices.Add(invoice);
        _db.Returns.Add(purchaseReturn);
        _db.TreasuryAccounts.Add(TreasuryAccount.CreateCashbox(
            _tenantId,
            _cashAccount.Id,
            "Cash"));
        await _db.SaveChangesAsync();

        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = purchaseReturn.ReturnDate,
            SourceType = "PurchaseReturn",
            SourceId = purchaseReturn.Id,
            SourceReference = purchaseReturn.ReturnNumber,
            Description = "Forged purchase return cash inflow",
            IdempotencyKey = $"PURCHASE_RETURN:{_tenantId}:{purchaseReturn.Id}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 100m),
                PostingLine.Credit(_revenueAccount.Id, 100m)
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidJournalException>(
            () => _service.PostJournalAsync(request));

        Assert.Contains("not authorized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Journal Number Tests
    
    [Fact]
    public async Task PostJournalAsync_GeneratesSequentialJournalNumbers()
    {
        // Arrange
        var request1 = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "First",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 100m),
                PostingLine.Credit(_revenueAccount.Id, 100m)
            }
        };
        
        var request2 = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Test",
            SourceId = Guid.NewGuid(),
            Description = "Second",
            IdempotencyKey = $"TEST:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 100m),
                PostingLine.Credit(_revenueAccount.Id, 100m)
            }
        };
        
        // Act
        var journal1 = await _service.PostJournalAsync(request1);
        var journal2 = await _service.PostJournalAsync(request2);
        
        // Assert
        Assert.StartsWith("JE-", journal1.JournalNumber);
        Assert.StartsWith("JE-", journal2.JournalNumber);
        
        // Extract numbers and verify sequential
        var num1 = int.Parse(journal1.JournalNumber.Split('-').Last());
        var num2 = int.Parse(journal2.JournalNumber.Split('-').Last());
        Assert.Equal(num1 + 1, num2);
    }
    
    #endregion
}
