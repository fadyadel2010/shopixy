using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
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
/// AF-14 + AF-14C: Integration tests for the Affiliate Accounting Bridge.
///
/// Verifies that commission earned/paid events produce correct journal entries
/// with proper double-entry accounting, idempotency, and balance effects.
///
/// Tests are structured in 5 groups:
///   1. Earned Commission — Debit Expense (6110), Credit Payable (2105)
///   2. Paid Commission — Debit Payable (2105), Credit Cash (1010)
///   3. Idempotency — Replay same event → only one journal entry created
///   4. Cross-Tenant Isolation
///   5. AF-14C Hardening — Out-of-order guard, negative payable prevention
/// </summary>
public class AffiliateAccountingBridgeTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly FinancialPostingService _postingService;
    private readonly Guid _tenantId = Guid.NewGuid();

    // Standard accounts
    private Account _cashAccount = null!;
    private Account _affiliateExpenseAccount = null!;
    private Account _affiliatePayableAccount = null!;

    public AffiliateAccountingBridgeTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AF14_Test_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new ApplicationDbContext(options);

        var logger = new Mock<ILogger<FinancialPostingService>>();
        _postingService = new FinancialPostingService(_db, logger.Object);

        SetupTestAccounts();
    }

    private void SetupTestAccounts()
    {
        // Cash in Hand - starting balance 10,000
        _cashAccount = Account.Create(
            _tenantId,
            AccountCodes.CashboxMain, // "1010"
            "Cash in Hand",
            AccountType.Asset,
            preventNegative: true);
        _cashAccount.SetOpeningBalance(10000m);

        // Affiliate Marketing Expense (6110)
        _affiliateExpenseAccount = Account.Create(
            _tenantId,
            AccountCodes.AffiliateMarketingExpense, // "6110"
            "Affiliate Marketing Expense",
            AccountType.Expense);

        // Affiliate Payable (2105) — AF-14C: PreventNegative = true
        _affiliatePayableAccount = Account.Create(
            _tenantId,
            AccountCodes.AffiliatePayable, // "2105"
            "Affiliate Payable",
            AccountType.Liability,
            preventNegative: true); // AF-14C: Safety belt

        _db.LedgerAccounts.AddRange(
            _cashAccount,
            _affiliateExpenseAccount,
            _affiliatePayableAccount);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. EARNED COMMISSION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EarnedCommission_CreatesJournalEntry_WithCorrectDebitsAndCredits()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var commissionAmount = 20m;
        var idempotencyKey = $"AFFILIATE_COMM_EARNED:{_tenantId}:{orderId}";

        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionEarned",
            SourceId = orderId,
            SourceReference = $"AFFILIATE_COMM_EARNED:{orderId}",
            Description = $"Affiliate commission earned for order ORD-001",
            IdempotencyKey = idempotencyKey,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliateExpenseAccount.Id, commissionAmount,
                    "Affiliate commission expense — Order ORD-001"),
                PostingLine.Credit(_affiliatePayableAccount.Id, commissionAmount,
                    "Affiliate payable — Order ORD-001")
            }
        };

        // Act
        var journal = await _postingService.PostJournalAsync(request);

        // Assert
        Assert.NotNull(journal);
        Assert.Equal(JournalStatus.Posted, journal.Status);
        Assert.Equal(2, journal.LedgerEntries.Count);
        Assert.True(journal.IsBalanced);
        Assert.Equal(commissionAmount, journal.TotalDebits);
        Assert.Equal(commissionAmount, journal.TotalCredits);

        // Verify debit entry (Expense 6110)
        var debitEntry = journal.LedgerEntries.First(e => e.DebitAmount > 0);
        Assert.Equal(_affiliateExpenseAccount.Id, debitEntry.AccountId);
        Assert.Equal(commissionAmount, debitEntry.DebitAmount);
        Assert.Equal(0m, debitEntry.CreditAmount);

        // Verify credit entry (Payable 2105)
        var creditEntry = journal.LedgerEntries.First(e => e.CreditAmount > 0);
        Assert.Equal(_affiliatePayableAccount.Id, creditEntry.AccountId);
        Assert.Equal(commissionAmount, creditEntry.CreditAmount);
        Assert.Equal(0m, creditEntry.DebitAmount);

        // Verify expense account balance increased (debit-normal, debit increases)
        var expenseAccount = await _db.LedgerAccounts.FindAsync(_affiliateExpenseAccount.Id);
        Assert.Equal(commissionAmount, expenseAccount!.CurrentBalance);

        // Verify payable account balance increased (credit-normal, credit increases)
        var payableAccount = await _db.LedgerAccounts.FindAsync(_affiliatePayableAccount.Id);
        Assert.Equal(commissionAmount, payableAccount!.CurrentBalance);

        // Verify Cash is UNCHANGED (earned does not touch treasury)
        var cashAccount = await _db.LedgerAccounts.FindAsync(_cashAccount.Id);
        Assert.Equal(10000m, cashAccount!.CurrentBalance);
    }

    [Fact]
    public async Task EarnedCommission_DoesNotAffectTreasuryBalance()
    {
        // Arrange — record initial cash balance
        var initialCash = (await _db.LedgerAccounts.FindAsync(_cashAccount.Id))!.CurrentBalance;

        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionEarned",
            SourceId = Guid.NewGuid(),
            SourceReference = "AFFILIATE_COMM_EARNED:test",
            Description = "Commission earned test",
            IdempotencyKey = $"AFFILIATE_COMM_EARNED:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliateExpenseAccount.Id, 50m),
                PostingLine.Credit(_affiliatePayableAccount.Id, 50m)
            }
        };

        // Act
        await _postingService.PostJournalAsync(request);

        // Assert — Cash must be unchanged
        var cashAfter = (await _db.LedgerAccounts.FindAsync(_cashAccount.Id))!.CurrentBalance;
        Assert.Equal(initialCash, cashAfter);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. PAID COMMISSION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PaidCommission_CreatesJournalEntry_ClearsPayableAndReducesCash()
    {
        // Arrange — first create the payable by earning commission
        var orderId = Guid.NewGuid();
        var commissionAmount = 20m;

        var earnRequest = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionEarned",
            SourceId = orderId,
            SourceReference = $"AFFILIATE_COMM_EARNED:{orderId}",
            Description = "Commission earned",
            IdempotencyKey = $"AFFILIATE_COMM_EARNED:{_tenantId}:{orderId}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliateExpenseAccount.Id, commissionAmount),
                PostingLine.Credit(_affiliatePayableAccount.Id, commissionAmount)
            }
        };
        await _postingService.PostJournalAsync(earnRequest);

        // Now pay the commission
        var paidRequest = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionPaid",
            SourceId = orderId,
            SourceReference = $"AFFILIATE_COMM_PAID:{orderId}",
            Description = "Commission paid",
            IdempotencyKey = $"AFFILIATE_COMM_PAID:{_tenantId}:{orderId}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliatePayableAccount.Id, commissionAmount,
                    "Affiliate payable cleared"),
                PostingLine.Credit(_cashAccount.Id, commissionAmount,
                    "Cash paid to affiliate")
            }
        };

        // Act
        var journal = await _postingService.PostJournalAsync(paidRequest);

        // Assert
        Assert.NotNull(journal);
        Assert.Equal(JournalStatus.Posted, journal.Status);
        Assert.True(journal.IsBalanced);

        // Verify Affiliate Payable is back to 0 (earned +20, paid -20)
        var payableAccount = await _db.LedgerAccounts.FindAsync(_affiliatePayableAccount.Id);
        Assert.Equal(0m, payableAccount!.CurrentBalance);

        // Verify Cash is reduced by commission amount
        var cashAccount = await _db.LedgerAccounts.FindAsync(_cashAccount.Id);
        Assert.Equal(10000m - commissionAmount, cashAccount!.CurrentBalance);
    }

    [Fact]
    public async Task PaidCommission_FullFlow_TreasuryShowsCorrectBalance()
    {
        // Scenario: Customer pays 510, affiliate earns 20, affiliate gets paid
        // Final cash should be 510 - 20 = 490
        
        // Step 1: Simulate receiving 510 (cash sale — debit cash, credit revenue)
        var revenueAccount = Account.Create(
            _tenantId, "4010", "Sales Revenue", AccountType.Revenue);
        _db.LedgerAccounts.Add(revenueAccount);
        await _db.SaveChangesAsync();

        var saleRequest = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Sale revenue 510",
            IdempotencyKey = $"SALE:{_tenantId}:{Guid.NewGuid()}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_cashAccount.Id, 510m),
                PostingLine.Credit(revenueAccount.Id, 510m)
            }
        };
        await _postingService.PostJournalAsync(saleRequest);

        // Verify cash = 10000 + 510 = 10510
        var cashAfterSale = (await _db.LedgerAccounts.FindAsync(_cashAccount.Id))!.CurrentBalance;
        Assert.Equal(10510m, cashAfterSale);

        // Step 2: Commission earned 20
        var orderId = Guid.NewGuid();
        var earnRequest = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionEarned",
            SourceId = orderId,
            Description = "Commission earned 20",
            IdempotencyKey = $"AFFILIATE_COMM_EARNED:{_tenantId}:{orderId}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliateExpenseAccount.Id, 20m),
                PostingLine.Credit(_affiliatePayableAccount.Id, 20m)
            }
        };
        await _postingService.PostJournalAsync(earnRequest);

        // Cash should still be 10510 (earned doesn't touch cash)
        var cashAfterEarn = (await _db.LedgerAccounts.FindAsync(_cashAccount.Id))!.CurrentBalance;
        Assert.Equal(10510m, cashAfterEarn);

        // Step 3: Commission paid 20
        var paidRequest = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionPaid",
            SourceId = orderId,
            Description = "Commission paid 20",
            IdempotencyKey = $"AFFILIATE_COMM_PAID:{_tenantId}:{orderId}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliatePayableAccount.Id, 20m),
                PostingLine.Credit(_cashAccount.Id, 20m)
            }
        };
        await _postingService.PostJournalAsync(paidRequest);

        // Final cash should be 10510 - 20 = 10490
        var finalCash = (await _db.LedgerAccounts.FindAsync(_cashAccount.Id))!.CurrentBalance;
        Assert.Equal(10490m, finalCash);

        // Verify GL is balanced: payable should be 0
        var finalPayable = (await _db.LedgerAccounts.FindAsync(_affiliatePayableAccount.Id))!.CurrentBalance;
        Assert.Equal(0m, finalPayable);

        // Verify expense is recognized
        var finalExpense = (await _db.LedgerAccounts.FindAsync(_affiliateExpenseAccount.Id))!.CurrentBalance;
        Assert.Equal(20m, finalExpense);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. IDEMPOTENCY TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EarnedCommission_ReplayedTwice_OnlyOneJournalCreated()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var idempotencyKey = $"AFFILIATE_COMM_EARNED:{_tenantId}:{orderId}";

        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionEarned",
            SourceId = orderId,
            Description = "Commission earned",
            IdempotencyKey = idempotencyKey,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliateExpenseAccount.Id, 20m),
                PostingLine.Credit(_affiliatePayableAccount.Id, 20m)
            }
        };

        // Act — post twice with same idempotency key
        var journal1 = await _postingService.PostJournalAsync(request);
        var journal2 = await _postingService.PostJournalAsync(request);

        // Assert — same journal returned both times
        Assert.Equal(journal1.Id, journal2.Id);
        Assert.Equal(journal1.JournalNumber, journal2.JournalNumber);

        // Verify only ONE journal entry exists with this key
        var count = await _db.JournalEntries
            .IgnoreQueryFilters()
            .CountAsync(j => j.TenantId == _tenantId && j.IdempotencyKey == idempotencyKey);
        Assert.Equal(1, count);

        // Verify balance was applied only once
        var expenseBalance = (await _db.LedgerAccounts.FindAsync(_affiliateExpenseAccount.Id))!.CurrentBalance;
        Assert.Equal(20m, expenseBalance); // NOT 40

        var payableBalance = (await _db.LedgerAccounts.FindAsync(_affiliatePayableAccount.Id))!.CurrentBalance;
        Assert.Equal(20m, payableBalance); // NOT 40
    }

    [Fact]
    public async Task PaidCommission_ReplayedTwice_OnlyOneJournalCreated()
    {
        // Arrange — first earn
        var orderId = Guid.NewGuid();
        await _postingService.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionEarned",
            SourceId = orderId,
            Description = "Commission earned",
            IdempotencyKey = $"AFFILIATE_COMM_EARNED:{_tenantId}:{orderId}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliateExpenseAccount.Id, 20m),
                PostingLine.Credit(_affiliatePayableAccount.Id, 20m)
            }
        });

        // Now try to pay twice
        var payIdempotencyKey = $"AFFILIATE_COMM_PAID:{_tenantId}:{orderId}";
        var payRequest = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionPaid",
            SourceId = orderId,
            Description = "Commission paid",
            IdempotencyKey = payIdempotencyKey,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliatePayableAccount.Id, 20m),
                PostingLine.Credit(_cashAccount.Id, 20m)
            }
        };

        // Act
        var journal1 = await _postingService.PostJournalAsync(payRequest);
        var journal2 = await _postingService.PostJournalAsync(payRequest);

        // Assert
        Assert.Equal(journal1.Id, journal2.Id);

        // Only one paid journal
        var count = await _db.JournalEntries
            .IgnoreQueryFilters()
            .CountAsync(j => j.TenantId == _tenantId && j.IdempotencyKey == payIdempotencyKey);
        Assert.Equal(1, count);

        // Cash only reduced once
        var cashBalance = (await _db.LedgerAccounts.FindAsync(_cashAccount.Id))!.CurrentBalance;
        Assert.Equal(10000m - 20m, cashBalance); // NOT 10000 - 40
    }

    /// <summary>
    /// AF-14C: Duplicate event replay should return the existing journal without side effects.
    /// Verifies the explicit idempotency path (no time-based heuristic).
    /// </summary>
    [Fact]
    public async Task DuplicateEvent_Replay_ShouldReturnExistingJournal()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var key = $"AFFILIATE_COMM_EARNED:{_tenantId}:{orderId}";

        var request = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionEarned",
            SourceId = orderId,
            Description = "Commission earned",
            IdempotencyKey = key,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliateExpenseAccount.Id, 15m),
                PostingLine.Credit(_affiliatePayableAccount.Id, 15m)
            }
        };

        // Act — post first
        var original = await _postingService.PostJournalAsync(request);

        // Verify key exists
        var exists = await _postingService.ExistsByIdempotencyKeyAsync(_tenantId, key);
        Assert.True(exists);

        // Post again — should return same journal
        var replay = await _postingService.PostJournalAsync(request);

        // Assert — exact same entity
        Assert.Equal(original.Id, replay.Id);
        Assert.Equal(original.JournalNumber, replay.JournalNumber);

        // Balance applied only once
        var expense = (await _db.LedgerAccounts.FindAsync(_affiliateExpenseAccount.Id))!.CurrentBalance;
        Assert.Equal(15m, expense);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. CROSS-TENANT ISOLATION TEST
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EarnedCommission_DifferentTenants_IndependentJournals()
    {
        // Arrange — create accounts for a second tenant
        var tenant2 = Guid.NewGuid();
        var expense2 = Account.Create(tenant2, "6110", "Affiliate Expense T2", AccountType.Expense);
        var payable2 = Account.Create(tenant2, "2105", "Affiliate Payable T2", AccountType.Liability);
        _db.LedgerAccounts.AddRange(expense2, payable2);
        await _db.SaveChangesAsync();

        var orderId = Guid.NewGuid();

        // Tenant 1 earns commission
        var req1 = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionEarned",
            SourceId = orderId,
            Description = "T1 commission",
            IdempotencyKey = $"AFFILIATE_COMM_EARNED:{_tenantId}:{orderId}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliateExpenseAccount.Id, 20m),
                PostingLine.Credit(_affiliatePayableAccount.Id, 20m)
            }
        };

        // Tenant 2 earns commission for the same orderId (different tenant)
        var req2 = new PostingRequest
        {
            TenantId = tenant2,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionEarned",
            SourceId = orderId,
            Description = "T2 commission",
            IdempotencyKey = $"AFFILIATE_COMM_EARNED:{tenant2}:{orderId}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(expense2.Id, 30m),
                PostingLine.Credit(payable2.Id, 30m)
            }
        };

        // Act
        var journal1 = await _postingService.PostJournalAsync(req1);
        var journal2 = await _postingService.PostJournalAsync(req2);

        // Assert — separate journals
        Assert.NotEqual(journal1.Id, journal2.Id);
        Assert.Equal(_tenantId, journal1.TenantId);
        Assert.Equal(tenant2, journal2.TenantId);

        // Each tenant's accounts have independent balances
        var t1Expense = (await _db.LedgerAccounts.FindAsync(_affiliateExpenseAccount.Id))!.CurrentBalance;
        Assert.Equal(20m, t1Expense);

        var t2Expense = (await _db.LedgerAccounts.FindAsync(expense2.Id))!.CurrentBalance;
        Assert.Equal(30m, t2Expense);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. AF-14C HARDENING TESTS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AF-14C: Paid without Earned should fail due to PreventNegative on account 2105.
    /// This is the DB-level safety belt that prevents negative payable.
    /// The consumer also has an application-level guard (out-of-order check),
    /// but this test verifies the FinancialPostingService rejects it too.
    /// </summary>
    [Fact]
    public async Task PaidWithoutEarned_ShouldFail_NegativePayablePrevented()
    {
        // Arrange — no earned commission (payable balance = 0)
        var orderId = Guid.NewGuid();

        var paidRequest = new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionPaid",
            SourceId = orderId,
            Description = "Commission paid (no earned)",
            IdempotencyKey = $"AFFILIATE_COMM_PAID:{_tenantId}:{orderId}",
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliatePayableAccount.Id, 20m, "Payable cleared"),
                PostingLine.Credit(_cashAccount.Id, 20m, "Cash paid")
            }
        };

        // Act & Assert — should throw because payable would go negative
        // Account 2105 has PreventNegative = true
        await Assert.ThrowsAsync<PointOfSale.Core.Exceptions.Accounting.InsufficientFundsException>(
            () => _postingService.PostJournalAsync(paidRequest));

        // Verify no journal was created
        var journals = await _db.JournalEntries
            .IgnoreQueryFilters()
            .CountAsync(j => j.TenantId == _tenantId &&
                j.IdempotencyKey == $"AFFILIATE_COMM_PAID:{_tenantId}:{orderId}");
        Assert.Equal(0, journals);

        // Verify cash was NOT reduced
        var cash = (await _db.LedgerAccounts.FindAsync(_cashAccount.Id))!.CurrentBalance;
        Assert.Equal(10000m, cash);

        // Verify payable did NOT go negative
        var payable = (await _db.LedgerAccounts.FindAsync(_affiliatePayableAccount.Id))!.CurrentBalance;
        Assert.Equal(0m, payable);
    }

    /// <summary>
    /// AF-14C: The ExistsByIdempotencyKeyAsync method is the foundation of the
    /// out-of-order guard. This test verifies it works correctly.
    /// </summary>
    [Fact]
    public async Task ExistsByIdempotencyKey_ReturnsFalse_WhenNoJournalExists()
    {
        // Arrange
        var key = $"AFFILIATE_COMM_EARNED:{_tenantId}:{Guid.NewGuid()}";

        // Act
        var exists = await _postingService.ExistsByIdempotencyKeyAsync(_tenantId, key);

        // Assert
        Assert.False(exists);
    }

    /// <summary>
    /// AF-14C: Verifies that the out-of-order guard logic works:
    /// after posting an earned journal, ExistsByIdempotencyKeyAsync returns true.
    /// </summary>
    [Fact]
    public async Task ExistsByIdempotencyKey_ReturnsTrue_AfterEarnedJournalPosted()
    {
        // Arrange — post an earned journal
        var orderId = Guid.NewGuid();
        var earnedKey = $"AFFILIATE_COMM_EARNED:{_tenantId}:{orderId}";

        await _postingService.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionEarned",
            SourceId = orderId,
            Description = "Commission earned",
            IdempotencyKey = earnedKey,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliateExpenseAccount.Id, 20m),
                PostingLine.Credit(_affiliatePayableAccount.Id, 20m)
            }
        });

        // Act
        var exists = await _postingService.ExistsByIdempotencyKeyAsync(_tenantId, earnedKey);

        // Assert
        Assert.True(exists);
    }

    /// <summary>
    /// AF-14C: Full earned→paid flow where the out-of-order guard is satisfied.
    /// This proves the normal path still works after hardening.
    /// </summary>
    [Fact]
    public async Task PaidAfterEarned_OutOfOrderGuardSatisfied_PostsSuccessfully()
    {
        // Arrange — earn first
        var orderId = Guid.NewGuid();
        var earnedKey = $"AFFILIATE_COMM_EARNED:{_tenantId}:{orderId}";

        await _postingService.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionEarned",
            SourceId = orderId,
            Description = "Commission earned",
            IdempotencyKey = earnedKey,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliateExpenseAccount.Id, 25m),
                PostingLine.Credit(_affiliatePayableAccount.Id, 25m)
            }
        });

        // Verify earned exists (this is what the consumer would check)
        Assert.True(await _postingService.ExistsByIdempotencyKeyAsync(_tenantId, earnedKey));

        // Act — pay
        var paidKey = $"AFFILIATE_COMM_PAID:{_tenantId}:{orderId}";
        var journal = await _postingService.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = DateTime.UtcNow,
            SourceType = "AffiliateCommissionPaid",
            SourceId = orderId,
            Description = "Commission paid",
            IdempotencyKey = paidKey,
            Lines = new List<PostingLine>
            {
                PostingLine.Debit(_affiliatePayableAccount.Id, 25m),
                PostingLine.Credit(_cashAccount.Id, 25m)
            }
        });

        // Assert
        Assert.NotNull(journal);
        Assert.Equal(JournalStatus.Posted, journal.Status);

        // Payable = 0, Cash = 10000 - 25 = 9975
        var payable = (await _db.LedgerAccounts.FindAsync(_affiliatePayableAccount.Id))!.CurrentBalance;
        Assert.Equal(0m, payable);

        var cash = (await _db.LedgerAccounts.FindAsync(_cashAccount.Id))!.CurrentBalance;
        Assert.Equal(9975m, cash);
    }
}
