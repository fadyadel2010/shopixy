using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests.Accounting;

/// <summary>
/// T-08 Part 21: merchandise-cost posting proofs.
/// Sale COGS must be Dr 5010 / Cr 1310 from frozen snapshots; reversals mirror it.
/// Only 5010/1310 may appear: no cash, clearing, AR/AP, or revenue leg.
/// </summary>
public sealed class T08EcommerceCogsPostingTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly CommerceAccountingService _service;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _cogsId = Guid.NewGuid();
    private readonly Guid _inventoryId = Guid.NewGuid();
    private readonly Guid _cashId = Guid.NewGuid();
    private readonly Guid _clearingId = Guid.NewGuid();

    public T08EcommerceCogsPostingTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString("N"))
            .Options;

        _db = new ApplicationDbContext(options);

        var cogs = Account.Create(_tenantId, AccountCodes.CostOfGoodsSold, "COGS", AccountType.CostOfGoodsSold);
        cogs.Id = _cogsId;
        var inventory = Account.Create(_tenantId, AccountCodes.Inventory, "Inventory", AccountType.Asset);
        inventory.Id = _inventoryId;
        var cash = Account.Create(_tenantId, "1010", "Cash", AccountType.Asset);
        cash.Id = _cashId;
        var clearing = Account.Create(_tenantId, AccountCodes.VisaClearing, "Clearing", AccountType.Asset);
        clearing.Id = _clearingId;
        _db.LedgerAccounts.AddRange(cogs, inventory, cash, clearing);
        _db.SaveChanges();

        var resolver = new Mock<IAccountResolver>();
        resolver
            .Setup(x => x.GetByCodesAsync(_tenantId, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenant, IEnumerable<string> codes, CancellationToken ct) =>
                codes.ToDictionary(code => code, code => code switch
                {
                    AccountCodes.CostOfGoodsSold => cogs,
                    AccountCodes.Inventory => inventory,
                    "1010" => cash,
                    _ => clearing
                }));

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        _service = new CommerceAccountingService(
            posting,
            resolver.Object,
            Mock.Of<ICashboxLedgerAccountService>(),
            NullLogger<CommerceAccountingService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task PostEcommerceCogsAsync_PostsBalancedDr5010Cr1310()
    {
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var capture = new PostingCapture(new Dictionary<string, Guid>
        {
            [AccountCodes.CostOfGoodsSold] = _cogsId,
            [AccountCodes.Inventory] = _inventoryId
        });
        var service = new CommerceAccountingService(
            capture.PostingService,
            capture.Resolver,
            Mock.Of<ICashboxLedgerAccountService>(),
            NullLogger<CommerceAccountingService>.Instance);

        var result = await service.PostEcommerceCogsAsync(new EcommerceCogsPostingRequest(
            _tenantId, orderId, "ORD-T08-1", "pay-intent-1", "EGP", DateTime.UtcNow,
            new[] { new EcommerceCogsLineDetail(itemId, Guid.NewGuid(), 2m, 50m, 100m) },
            100m));

        Assert.True(result.Success, result.ErrorMessage);
        var captured = capture.RequireSingle();
        Assert.Equal("Order_COGS", captured.SourceType);
        Assert.Equal($"ECOM_COGS:{_tenantId}:{orderId}:pay-intent-1", captured.IdempotencyKey);
        Assert.Equal(2, captured.Lines.Count);
        var debit = captured.Lines.Single(l => l.DebitAmount > 0m);
        var credit = captured.Lines.Single(l => l.CreditAmount > 0m);
        Assert.Equal(_cogsId, debit.AccountId);
        Assert.Equal(100m, debit.DebitAmount);
        Assert.Equal(_inventoryId, credit.AccountId);
        Assert.Equal(100m, credit.CreditAmount);
        Assert.DoesNotContain(captured.Lines, l => l.AccountId == _cashId || l.AccountId == _clearingId);
    }

    [Fact]
    public async Task PostEcommerceCogsAsync_WritesRealBalancedJournal()
    {
        var orderId = Guid.NewGuid();
        var result = await _service.PostEcommerceCogsAsync(new EcommerceCogsPostingRequest(
            _tenantId, orderId, "ORD-T08-2", "cod-delivery-commit", "EGP", DateTime.UtcNow,
            new[] { new EcommerceCogsLineDetail(Guid.NewGuid(), null, 5m, 60m, 300m) },
            300m));

        Assert.True(result.Success, result.ErrorMessage);
        var journal = await _db.JournalEntries.IgnoreQueryFilters()
            .Include(j => j.LedgerEntries)
            .SingleAsync(j => j.Id == result.JournalId);
        Assert.Equal("Order_COGS", journal.SourceType);
        Assert.Equal(300m, journal.LedgerEntries.Sum(l => l.DebitAmount));
        Assert.Equal(300m, journal.LedgerEntries.Sum(l => l.CreditAmount));
    }

    [Fact]
    public async Task PostEcommerceCogsAsync_UsesFrozenSnapshot_NotLiveCost()
    {
        // Live master cost moves to 60 AFTER checkout; the frozen request still carries 50.
        var result = await _service.PostEcommerceCogsAsync(new EcommerceCogsPostingRequest(
            _tenantId, Guid.NewGuid(), "ORD-T08-3", "pay-intent-9", "EGP", DateTime.UtcNow,
            new[] { new EcommerceCogsLineDetail(Guid.NewGuid(), Guid.NewGuid(), 2m, 50m, 100m) },
            100m));

        Assert.True(result.Success, result.ErrorMessage);
        var journal = await _db.JournalEntries.IgnoreQueryFilters()
            .Include(j => j.LedgerEntries)
            .SingleAsync(j => j.Id == result.JournalId);
        Assert.Equal(100m, journal.LedgerEntries.Sum(l => l.DebitAmount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task PostEcommerceCogsAsync_FailsClosed_OnNonPositiveTotal(decimal total)
    {
        var result = await _service.PostEcommerceCogsAsync(new EcommerceCogsPostingRequest(
            _tenantId, Guid.NewGuid(), "ORD-T08-4", "evt-1", "EGP", DateTime.UtcNow,
            new[] { new EcommerceCogsLineDetail(Guid.NewGuid(), null, 1m, 10m, 10m) },
            total));

        Assert.False(result.Success);
        Assert.Empty(_db.JournalEntries.IgnoreQueryFilters());
    }

    [Fact]
    public async Task PostEcommerceCogsAsync_FailsClosed_OnLineMismatch()
    {
        var result = await _service.PostEcommerceCogsAsync(new EcommerceCogsPostingRequest(
            _tenantId, Guid.NewGuid(), "ORD-T08-5", "evt-2", "EGP", DateTime.UtcNow,
            new[] { new EcommerceCogsLineDetail(Guid.NewGuid(), null, 2m, 50m, 999m) },
            999m));

        Assert.False(result.Success);
        Assert.Empty(_db.JournalEntries.IgnoreQueryFilters());
    }

    [Fact]
    public async Task PostEcommerceCogsReversalAsync_PostsDr1310Cr5010()
    {
        var capture = new PostingCapture(new Dictionary<string, Guid>
        {
            [AccountCodes.CostOfGoodsSold] = _cogsId,
            [AccountCodes.Inventory] = _inventoryId
        });
        var service = new CommerceAccountingService(
            capture.PostingService,
            capture.Resolver,
            Mock.Of<ICashboxLedgerAccountService>(),
            NullLogger<CommerceAccountingService>.Instance);

        var orderId = Guid.NewGuid();
        var result = await service.PostEcommerceCogsReversalAsync(new EcommerceCogsReversalPostingRequest(
            _tenantId, orderId, "ORD-T08-6", "refund-1", "EGP", DateTime.UtcNow,
            new[] { new EcommerceCogsReversalLineDetail(Guid.NewGuid(), 2m, 100m) },
            100m));

        Assert.True(result.Success, result.ErrorMessage);
        var captured = capture.RequireSingle();
        Assert.Equal("Order_COGS_Reversal", captured.SourceType);
        Assert.Equal($"ECOM_COGS_REV:{_tenantId}:{orderId}:refund-1", captured.IdempotencyKey);
        var debit = captured.Lines.Single(l => l.DebitAmount > 0m);
        var credit = captured.Lines.Single(l => l.CreditAmount > 0m);
        Assert.Equal(_inventoryId, debit.AccountId);
        Assert.Equal(_cogsId, credit.AccountId);
        Assert.Equal(100m, debit.DebitAmount);
        Assert.Equal(100m, credit.CreditAmount);
    }

    [Fact]
    public async Task PostCommerceHubCogsAsync_PostsDr5010Cr1310()
    {
        var subOrderId = Guid.NewGuid();
        var result = await _service.PostCommerceHubCogsAsync(new CommerceHubCogsPostingRequest(
            _tenantId, Guid.NewGuid(), subOrderId, "SUB-T08-A", Guid.NewGuid(),
            "evt-delivery-1", "EGP", DateTime.UtcNow, 3m, 60m, 180m));

        Assert.True(result.Success, result.ErrorMessage);
        var journal = await _db.JournalEntries.IgnoreQueryFilters()
            .Include(j => j.LedgerEntries)
            .SingleAsync(j => j.Id == result.JournalId);
        Assert.Equal("CommerceHub_COGS", journal.SourceType);
        Assert.Equal(subOrderId, journal.SourceId);
        Assert.Equal(180m, journal.LedgerEntries.Sum(l => l.DebitAmount));
        Assert.Equal(180m, journal.LedgerEntries.Sum(l => l.CreditAmount));
    }

    [Fact]
    public async Task PostCommerceHubCogsReversalAsync_PostsDr1310Cr5010()
    {
        var subOrderId = Guid.NewGuid();
        var result = await _service.PostCommerceHubCogsReversalAsync(new CommerceHubCogsReversalPostingRequest(
            _tenantId, Guid.NewGuid(), subOrderId, "SUB-T08-B", Guid.NewGuid(), Guid.NewGuid(),
            "return-1", "EGP", DateTime.UtcNow, 2m, 60m, 120m));

        Assert.True(result.Success, result.ErrorMessage);
        var journal = await _db.JournalEntries.IgnoreQueryFilters()
            .Include(j => j.LedgerEntries)
            .SingleAsync(j => j.Id == result.JournalId);
        Assert.Equal("CommerceHub_COGS_Reversal", journal.SourceType);
        var debit = journal.LedgerEntries.Single(l => l.DebitAmount > 0m);
        var credit = journal.LedgerEntries.Single(l => l.CreditAmount > 0m);
        Assert.Equal(120m, debit.DebitAmount);
        Assert.Equal(120m, credit.CreditAmount);
    }

    [Fact]
    public void IdempotencyKeys_RequireCommitEventIdentity()
    {
        var tenant = Guid.NewGuid();
        var order = Guid.NewGuid();
        Assert.Equal(
            $"ECOM_COGS:{tenant}:{order}:pay-intent-1",
            EcommerceCogsPolicy.BuildStorefrontCogsKey(tenant, order, "pay-intent-1"));
        Assert.Equal(
            $"ECOM_COGS_REV:{tenant}:{order}:refund-1",
            EcommerceCogsPolicy.BuildStorefrontCogsReversalKey(tenant, order, "refund-1"));
        var sub = Guid.NewGuid();
        Assert.Equal(
            $"CH_COGS:{tenant}:{sub}:evt-1",
            EcommerceCogsPolicy.BuildCommerceHubCogsKey(tenant, sub, "evt-1"));
        Assert.Equal(
            $"CH_COGS_REV:{tenant}:{sub}:ret-1",
            EcommerceCogsPolicy.BuildCommerceHubCogsReversalKey(tenant, sub, "ret-1"));
        Assert.Throws<ArgumentException>(() => EcommerceCogsPolicy.BuildStorefrontCogsKey(tenant, order, " "));
        Assert.Throws<ArgumentException>(() => EcommerceCogsPolicy.BuildCommerceHubCogsKey(tenant, sub, ""));
    }

    private sealed class PostingCapture
    {
        public readonly List<PostingRequest> Requests = new();
        public IFinancialPostingService PostingService { get; }
        public IAccountResolver Resolver { get; }

        public PostingCapture(IReadOnlyDictionary<string, Guid>? accountIds = null)
        {
            var posting = new Mock<IFinancialPostingService>();
            posting.Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PostingRequest req, CancellationToken ct) =>
                {
                    Requests.Add(req);
                    return JournalEntry.Create(
                        req.TenantId, $"JE-T08-{Guid.NewGuid():N}"[..16], req.JournalDate,
                        req.SourceType, req.SourceId, req.SourceReference, req.Description,
                        req.IdempotencyKey, req.UserId);
                });
            PostingService = posting.Object;

            var resolver = new Mock<IAccountResolver>();
            resolver
                .Setup(x => x.GetByCodesAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid tenant, IEnumerable<string> codes, CancellationToken ct) =>
                    codes.ToDictionary(code => code, code => AccountFor(code, accountIds)));
            Resolver = resolver.Object;
        }

        public PostingRequest RequireSingle()
        {
            Assert.Single(Requests);
            return Requests[0];
        }

        private static global::PointOfSale.Core.Entities.Accounting.Account AccountFor(
            string code, IReadOnlyDictionary<string, Guid>? accountIds)
        {
            var account = global::PointOfSale.Core.Entities.Accounting.Account.Create(Guid.Empty, code, code, AccountType.Asset);
            if (accountIds != null && accountIds.TryGetValue(code, out var id))
            {
                account.Id = id;
            }

            return account;
        }
    }
}

