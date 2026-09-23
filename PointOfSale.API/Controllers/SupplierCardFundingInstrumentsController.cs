using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/supplier-card-funding-instruments")]
[Authorize(Roles = "Owner,Admin,Manager")]
public sealed class SupplierCardFundingInstrumentsController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IFinancialPostingService _postingService;

    public SupplierCardFundingInstrumentsController(
        ApplicationDbContext db,
        ITenantContext tenantContext,
        IFinancialPostingService postingService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _postingService = postingService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SupplierCardInstrumentResponse>>> GetAll(
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var instruments = await _db.SupplierCardFundingInstruments
            .AsNoTracking()
            .Include(item => item.LedgerAccount)
            .Include(item => item.FundingSourceTreasuryAccount)
            .Where(item => !activeOnly || item.IsActive)
            .OrderBy(item => item.DisplayName)
            .ToListAsync(cancellationToken);

        var balances = await GetBalancesAsync(tenantId, instruments.Select(item => item.LedgerAccountId), cancellationToken);
        return Ok(instruments.Select(item => ToResponse(item, balances.GetValueOrDefault(item.LedgerAccountId))).ToList());
    }

    [HttpGet("funding-sources")]
    public async Task<ActionResult> GetFundingSources(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var sources = await _db.TreasuryAccounts
            .AsNoTracking()
            .Include(item => item.Account)
            .Where(item => item.IsActive && !item.IsDeleted &&
                (item.Type == TreasuryAccountType.Cashbox || item.Type == TreasuryAccountType.Bank) &&
                item.Account != null && item.Account.IsActive && !item.Account.IsDeleted)
            .OrderBy(item => item.Type)
            .ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                item.Name,
                Type = item.Type.ToString(),
                LedgerAccountId = item.AccountId,
                AccountCode = item.Account!.AccountCode
            })
            .ToListAsync(cancellationToken);
        return Ok(sources);
    }

    [HttpPost]
    public async Task<ActionResult<SupplierCardInstrumentResponse>> Create(
        CreateSupplierCardInstrumentRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var accountCode = request.LedgerAccountCode.Trim().ToUpperInvariant();
        if (accountCode is SupplierCardAuthority.LegacyPosVisaAccountCode or SupplierCardAuthority.CustomerVisaClearingAccountCode)
            return BadRequest(new { message = "1020 and 1110 cannot be used as supplier card funding accounts." });
        if (request.Last4 is { Length: > 0 } && (request.Last4.Length != 4 || !request.Last4.All(char.IsDigit)))
            return BadRequest(new { message = "Last4 must contain exactly four digits." });

        try
        {
            await SupplierCardAuthority.ValidateFundingSourceAsync(
                _db, tenantId, request.FundingSourceTreasuryAccountId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        if (await _db.LedgerAccounts.IgnoreQueryFilters().AnyAsync(
                account => account.TenantId == tenantId && account.AccountCode == accountCode, cancellationToken))
            return Conflict(new { message = "Ledger account code already exists for this tenant." });

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var account = Account.Create(
                tenantId,
                accountCode,
                request.LedgerAccountName.Trim(),
                AccountType.Asset,
                preventNegative: true);
            account.Id = Guid.NewGuid();
            account.Description = "Dedicated funded company card asset for supplier payments";
            account.DesignateAsSupplierCardFunding();
            _db.LedgerAccounts.Add(account);

            var instrument = SupplierCardFundingInstrument.Create(
                tenantId,
                request.DisplayName,
                account.Id,
                request.FundingSourceTreasuryAccountId,
                request.Issuer,
                request.Last4,
                request.IsActive);
            _db.SupplierCardFundingInstruments.Add(instrument);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var created = await _db.SupplierCardFundingInstruments
                .AsNoTracking()
                .Include(item => item.LedgerAccount)
                .Include(item => item.FundingSourceTreasuryAccount)
                .SingleAsync(item => item.Id == instrument.Id, cancellationToken);
            return CreatedAtAction(nameof(GetAll), ToResponse(created, 0m));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierCardInstrumentResponse>> Update(
        Guid id,
        UpdateSupplierCardInstrumentRequest request,
        CancellationToken cancellationToken)
    {
        var instrument = await _db.SupplierCardFundingInstruments
            .Include(item => item.LedgerAccount)
            .Include(item => item.FundingSourceTreasuryAccount)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (instrument == null)
            return NotFound();
        if (request.ExpectedAuthorityVersion != instrument.AuthorityVersion)
            return Conflict(new { message = "Instrument authority changed. Reload before editing." });
        if (request.Last4 is { Length: > 0 } && (request.Last4.Length != 4 || !request.Last4.All(char.IsDigit)))
            return BadRequest(new { message = "Last4 must contain exactly four digits." });

        instrument.DisplayName = request.DisplayName.Trim();
        instrument.Issuer = string.IsNullOrWhiteSpace(request.Issuer) ? null : request.Issuer.Trim();
        instrument.Last4 = string.IsNullOrWhiteSpace(request.Last4) ? null : request.Last4.Trim();
        instrument.IsActive = request.IsActive;
        instrument.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Instrument changed concurrently. Reload before editing." });
        }
        var balance = await SupplierCardAuthority.GetLedgerBalanceAsync(
            _db, instrument.TenantId, instrument.LedgerAccountId, cancellationToken);
        return Ok(ToResponse(instrument, balance));
    }

    [HttpPost("{id:guid}/fundings")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<SupplierCardFundingResponse>> Fund(
        Guid id,
        FundSupplierCardRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var key = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            return BadRequest(new { message = "Idempotency-Key is required and must not exceed 100 characters." });
        var amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m)
            return BadRequest(new { message = "Funding amount must be at least 0.01." });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            InstrumentId = id,
            request.FundingInstrumentVersion,
            request.FundingSourceTreasuryAccountId,
            Amount = amount,
            request.FundingDate,
            request.ReferenceNumber,
            request.Notes
        }))));

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = await ResolveFundingReplayAsync(tenantId, key, requestHash, cancellationToken);
            if (replay != null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return replay;
            }

            var instrument = await _db.SupplierCardFundingInstruments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == id && item.TenantId == tenantId, cancellationToken);
            if (instrument == null || instrument.IsDeleted || !instrument.IsActive ||
                instrument.AuthorityVersion != request.FundingInstrumentVersion)
                return BadRequest(new { message = "Active instrument authority was not found or changed." });
            if (instrument.FundingSourceTreasuryAccountId != request.FundingSourceTreasuryAccountId)
                return BadRequest(new { message = "Funding source does not match the instrument's configured authority." });

            var source = await SupplierCardAuthority.ValidateFundingSourceAsync(
                _db, tenantId, request.FundingSourceTreasuryAccountId, cancellationToken);
            await LockFundingSourceAsync(tenantId, source.AccountId, cancellationToken);
            var cardAccount = await _db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(account => account.Id == instrument.LedgerAccountId && account.TenantId == tenantId, cancellationToken);
            SupplierCardAuthority.ValidateLedgerAccount(cardAccount);

            var funding = new SupplierCardFunding
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FundingInstrumentId = instrument.Id,
                FundingLedgerAccountId = instrument.LedgerAccountId,
                FundingSourceTreasuryAccountId = source.Id,
                FundingSourceLedgerAccountId = source.AccountId,
                FundingInstrumentVersion = instrument.AuthorityVersion,
                FundingDate = NormalizeUtc(request.FundingDate),
                Amount = amount,
                IdempotencyKey = key,
                RequestHash = requestHash,
                ReferenceNumber = request.ReferenceNumber?.Trim(),
                Notes = request.Notes?.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            var outbox = CreateFundingOutbox(funding);
            _db.SupplierCardFundings.Add(funding);
            _db.AccountingOutboxMessages.Add(outbox);
            await _db.SaveChangesAsync(cancellationToken);

            var journal = await _postingService.PostJournalAsync(new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = funding.FundingDate,
                SourceType = "SupplierCardFunding",
                SourceId = funding.Id,
                SourceReference = funding.ReferenceNumber,
                Description = $"Fund supplier card {instrument.DisplayName}",
                IdempotencyKey = $"SUPPLIER_CARD_FUNDING:{tenantId}:{funding.Id}",
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(funding.FundingLedgerAccountId, amount, "Increase funded card asset"),
                    PostingLine.Credit(funding.FundingSourceLedgerAccountId, amount, "Card top-up funding source")
                }
            }, cancellationToken);

            var postedAt = DateTime.UtcNow;
            outbox.MarkProcessed(journal.Id);
            outbox.UpdatedAt = postedAt;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Ok(ToFundingResponse(funding) with { JournalId = journal.Id });
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new { message = "A concurrent financial mutation changed the funding authority. Retry safely." });
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            var replay = await ResolveFundingReplayAsync(tenantId, key, requestHash, cancellationToken);
            if (replay != null)
                return replay;
            throw;
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/statement")]
    public async Task<ActionResult<SupplierCardStatementResponse>> Statement(
        Guid id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var instrument = await _db.SupplierCardFundingInstruments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (instrument == null)
            return NotFound();

        var start = NormalizeUtc(from ?? DateTime.UtcNow.Date.AddMonths(-1));
        var endExclusive = NormalizeUtc((to ?? DateTime.UtcNow.Date).Date.AddDays(1));
        if (endExclusive <= start)
            return BadRequest(new { message = "Statement date range is invalid." });

        var opening = await _db.LedgerEntries.IgnoreQueryFilters()
            .Where(entry => entry.TenantId == tenantId && entry.AccountId == instrument.LedgerAccountId &&
                entry.JournalEntry.JournalDate < start.Date)
            .SumAsync(entry => (decimal?)(entry.DebitAmount - entry.CreditAmount), cancellationToken) ?? 0m;
        var ledgerRows = await _db.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(entry => entry.TenantId == tenantId && entry.AccountId == instrument.LedgerAccountId &&
                entry.JournalEntry.JournalDate >= start.Date && entry.JournalEntry.JournalDate < endExclusive.Date)
            .OrderBy(entry => entry.JournalEntry.JournalDate)
            .ThenBy(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.LineNumber)
            .Select(entry => new
            {
                entry.JournalEntryId,
                Date = entry.JournalEntry.JournalDate,
                entry.JournalEntry.SourceType,
                entry.JournalEntry.SourceId,
                entry.JournalEntry.SourceReference,
                entry.JournalEntry.Description,
                entry.DebitAmount,
                entry.CreditAmount
            })
            .ToListAsync(cancellationToken);

        var running = opening;
        var rows = ledgerRows.Select(row =>
        {
            running += row.DebitAmount - row.CreditAmount;
            return new SupplierCardStatementLine(
                row.JournalEntryId, row.Date, row.SourceType, row.SourceId,
                row.SourceReference, row.Description, row.DebitAmount,
                row.CreditAmount, row.DebitAmount - row.CreditAmount, running);
        }).ToList();

        return Ok(new SupplierCardStatementResponse(
            instrument.Id, instrument.DisplayName, start.Date, endExclusive.Date.AddDays(-1),
            opening, rows.Sum(row => row.Debit), rows.Sum(row => row.Credit), running, rows));
    }

    private async Task<Dictionary<Guid, decimal>> GetBalancesAsync(
        Guid tenantId,
        IEnumerable<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        var ids = accountIds.Distinct().ToList();
        return await _db.LedgerEntries.IgnoreQueryFilters()
            .Where(entry => entry.TenantId == tenantId && ids.Contains(entry.AccountId))
            .GroupBy(entry => entry.AccountId)
             .ToDictionaryAsync(group => group.Key, group => group.Sum(entry => entry.DebitAmount - entry.CreditAmount), cancellationToken);
    }

    private async Task LockFundingSourceAsync(
        Guid tenantId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        IQueryable<Account> query;
        var providerName = _db.Database.ProviderName ?? string.Empty;
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            query = _db.LedgerAccounts.FromSqlInterpolated($@"
                SELECT xmin, * FROM ""LedgerAccounts""
                WHERE ""TenantId"" = {tenantId} AND ""Id"" = {accountId}
                FOR UPDATE").IgnoreQueryFilters();
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            query = _db.LedgerAccounts.FromSqlInterpolated($@"
                SELECT * FROM [LedgerAccounts] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TenantId] = {tenantId} AND [Id] = {accountId}").IgnoreQueryFilters();
        }
        else
        {
            query = _db.LedgerAccounts.IgnoreQueryFilters()
                .Where(account => account.TenantId == tenantId && account.Id == accountId);
        }

        _ = await query.AsNoTracking().SingleAsync(cancellationToken);
    }

    private async Task<ActionResult<SupplierCardFundingResponse>?> ResolveFundingReplayAsync(
        Guid tenantId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SupplierCardFundings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.TenantId == tenantId && item.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing == null)
            return null;
        if (existing.RequestHash != requestHash)
            return Conflict(new { message = "Idempotency-Key is already bound to a different funding request." });

        var journalId = await _db.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(journal => journal.TenantId == tenantId &&
                journal.SourceType == "SupplierCardFunding" && journal.SourceId == existing.Id)
            .Select(journal => (Guid?)journal.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return Ok(ToFundingResponse(existing) with { JournalId = journalId });
    }

    private static SupplierCardInstrumentResponse ToResponse(SupplierCardFundingInstrument item, decimal balance) => new(
        item.Id, item.DisplayName, item.InstrumentType.ToString(), item.Issuer, item.Last4,
        item.IsActive, item.AuthorityVersion, item.LedgerAccountId, item.LedgerAccount.AccountCode,
        item.LedgerAccount.Name, item.FundingSourceTreasuryAccountId,
        item.FundingSourceTreasuryAccount.Name, item.FundingSourceTreasuryAccount.Type.ToString(), balance);

    private static SupplierCardFundingResponse ToFundingResponse(SupplierCardFunding item) => new(
        item.Id, item.FundingInstrumentId, item.FundingInstrumentVersion, item.FundingSourceTreasuryAccountId,
        item.Amount, item.FundingDate, item.ReferenceNumber, null);

    private static AccountingOutboxMessage CreateFundingOutbox(SupplierCardFunding funding)
    {
        var message = AccountingOutboxMessage.Create(
            funding.TenantId,
            "SupplierCardFunding",
            funding.Id,
            nameof(SupplierCardFunding),
            JsonSerializer.Serialize(new
            {
                funding.Id,
                funding.FundingInstrumentId,
                funding.FundingLedgerAccountId,
                funding.FundingSourceTreasuryAccountId,
                funding.FundingSourceLedgerAccountId,
                funding.FundingInstrumentVersion,
                funding.FundingDate,
                funding.Amount,
                funding.ReferenceNumber,
                funding.Notes
            }),
            $"OUTBOX:SUPPLIER_CARD_FUNDING:{funding.TenantId}:{funding.Id}",
            sourceType: "SupplierCardFunding");
        message.Id = Guid.NewGuid();
        message.CreatedAt = DateTime.UtcNow;
        return message;
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
        : value.ToUniversalTime();
}

public sealed record CreateSupplierCardInstrumentRequest(
    [Required, MaxLength(120)] string DisplayName,
    [Required, MaxLength(20)] string LedgerAccountCode,
    [Required, MaxLength(200)] string LedgerAccountName,
    Guid FundingSourceTreasuryAccountId,
    [MaxLength(100)] string? Issuer,
    [MaxLength(4)] string? Last4,
    bool IsActive = true);

public sealed record UpdateSupplierCardInstrumentRequest(
    [Required, MaxLength(120)] string DisplayName,
    [MaxLength(100)] string? Issuer,
    [MaxLength(4)] string? Last4,
    bool IsActive,
    int ExpectedAuthorityVersion);

public sealed record FundSupplierCardRequest(
    int FundingInstrumentVersion,
    Guid FundingSourceTreasuryAccountId,
    decimal Amount,
    DateTime FundingDate,
    [MaxLength(100)] string? ReferenceNumber,
    [MaxLength(1000)] string? Notes);

public sealed record SupplierCardInstrumentResponse(
    Guid Id, string DisplayName, string InstrumentType, string? Issuer, string? Last4,
    bool IsActive, int AuthorityVersion, Guid LedgerAccountId, string LedgerAccountCode,
    string LedgerAccountName, Guid FundingSourceTreasuryAccountId, string FundingSourceName,
    string FundingSourceType, decimal AvailableBalance);

public sealed record SupplierCardFundingResponse(
    Guid Id, Guid FundingInstrumentId, int FundingInstrumentVersion,
    Guid FundingSourceTreasuryAccountId, decimal Amount, DateTime FundingDate,
    string? ReferenceNumber, Guid? JournalId);

public sealed record SupplierCardStatementLine(
    Guid JournalId, DateTime Date, string TransactionType, Guid SourceDocumentId,
    string? SourceReference, string Description, decimal Debit, decimal Credit,
    decimal NetMovement, decimal RunningBalance);

public sealed record SupplierCardStatementResponse(
    Guid InstrumentId, string Instrument, DateTime From, DateTime To,
    decimal OpeningBalance, decimal TotalInflows, decimal TotalOutflows,
    decimal ClosingBalance, IReadOnlyList<SupplierCardStatementLine> Transactions);
