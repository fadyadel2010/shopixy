using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Accounting;

public sealed class TreasuryInitialCapitalizationService : ITreasuryInitialCapitalizationService
{
    private static readonly string[] ForbiddenDestinationCodes = ["1020", "1110", "1040", "2030", "3010"];
    private readonly ApplicationDbContext _db;
    private readonly IFinancialPostingService _postingService;

    public TreasuryInitialCapitalizationService(
        ApplicationDbContext db,
        IFinancialPostingService postingService)
    {
        _db = db;
        _postingService = postingService;
    }

    public async Task<IReadOnlyList<TreasuryInitialCapitalizationDestination>> GetEligibleDestinationsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId &&
                item.IsActive &&
                !item.IsDeleted &&
                (item.Type == TreasuryAccountType.Cashbox || item.Type == TreasuryAccountType.Bank) &&
                item.Account != null &&
                item.Account.TenantId == tenantId &&
                item.Account.IsActive &&
                !item.Account.IsDeleted &&
                item.Account.AccountType == AccountType.Asset &&
                !item.Account.IsClearing &&
                !ForbiddenDestinationCodes.Contains(item.Account.AccountCode))
            .OrderBy(item => item.Type)
            .ThenBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .Select(item => new TreasuryInitialCapitalizationDestination(
                item.Id,
                item.Name,
                item.NameAr,
                item.Type,
                item.AccountId,
                item.Account!.AccountCode,
                item.Account.Name,
                item.Account.CurrentBalance))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TreasuryInitialCapitalization>> GetAllAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _db.TreasuryInitialCapitalizations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(document => document.Allocations)
            .Where(document => document.TenantId == tenantId)
            .OrderByDescending(document => document.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<TreasuryInitialCapitalization?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _db.TreasuryInitialCapitalizations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(document => document.Allocations)
            .SingleOrDefaultAsync(
                document => document.TenantId == tenantId && document.Id == id,
                cancellationToken);
    }

    public async Task<TreasuryInitialCapitalization> CreateAndPostAsync(
        Guid tenantId,
        Guid ownerUserId,
        CreateTreasuryInitialCapitalizationCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCreateInput(tenantId, ownerUserId, command);
        var cutoverDate = NormalizeDate(command.CutoverDate);
        var normalizedAllocations = NormalizeAllocations(command.Allocations);
        var evidenceReference = command.EvidenceReference.Trim();
        var notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();
        var requestHash = HashCreateRequest(
            cutoverDate,
            normalizedAllocations,
            evidenceReference,
            notes,
            command.DeclarationVersion,
            command.DeclarationAcknowledged,
            command.PredecessorCapitalizationId);

        await EnsureOwnerAsync(tenantId, ownerUserId, cancellationToken);
        var replay = await ResolveCreateReplayAsync(tenantId, command.OperationId, requestHash, cancellationToken);
        if (replay != null)
            return replay;

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await LockTenantAsync(tenantId, cancellationToken);
            replay = await ResolveCreateReplayAsync(tenantId, command.OperationId, requestHash, cancellationToken);
            if (replay != null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return replay;
            }

            await ValidateEligibilityAsync(tenantId, cutoverDate, command.PredecessorCapitalizationId, cancellationToken);
            var destinations = await ResolveDestinationsAsync(tenantId, normalizedAllocations, cancellationToken);
            var openingEquity = await ResolveOpeningEquityAsync(tenantId, cancellationToken);
            var total = normalizedAllocations.Sum(item => item.Amount);

            var document = TreasuryInitialCapitalization.Create(
                tenantId,
                command.OperationId,
                cutoverDate,
                total,
                requestHash,
                evidenceReference,
                notes,
                ownerUserId,
                openingEquity.Id,
                openingEquity.AccountCode,
                openingEquity.Name,
                command.PredecessorCapitalizationId);

            foreach (var allocation in normalizedAllocations)
            {
                var destination = destinations[allocation.TreasuryAccountId];
                document.AddAllocation(TreasuryInitialCapitalizationAllocation.Create(
                    tenantId,
                    document.Id,
                    destination.Id,
                    destination.AccountId,
                    destination.Type,
                    destination.Name,
                    destination.Account!.AccountCode,
                    destination.Account.Name,
                    allocation.Amount));
            }

            var outbox = CreateOutbox(document, command.OperationId);
            _db.TreasuryInitialCapitalizations.Add(document);
            _db.AccountingOutboxMessages.Add(outbox);
            await _db.SaveChangesAsync(cancellationToken);

            var lines = document.Allocations
                .OrderBy(item => item.LedgerAccountId)
                .Select(item => PostingLine.Debit(
                    item.LedgerAccountId,
                    item.Amount,
                    $"Initial capitalization: {item.TreasuryAccountNameSnapshot}"))
                .Append(PostingLine.Credit(
                    document.OpeningEquityLedgerAccountId,
                    document.TotalAmount,
                    "Opening Balance Equity offset"))
                .ToList();

            var journal = await _postingService.PostJournalAsync(new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = document.CutoverDate,
                SourceType = TreasuryInitialCapitalization.AccountingSourceType,
                SourceId = document.Id,
                SourceReference = document.DocumentNumber,
                Description = $"Initial Treasury capitalization {document.DocumentNumber}",
                IdempotencyKey = BuildPostingKey(tenantId, command.OperationId),
                UserId = ownerUserId,
                Lines = lines
            }, cancellationToken);

            var postedAt = DateTime.UtcNow;
            document.MarkPosted(journal.Id, postedAt);
            outbox.MarkProcessed(journal.Id);
            outbox.UpdatedAt = postedAt;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (await GetByIdAsync(tenantId, document.Id, cancellationToken))!;
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            replay = await ResolveCreateReplayAsync(tenantId, command.OperationId, requestHash, cancellationToken);
            if (replay != null)
                return replay;

            throw Error(
                "CONCURRENCY_CONFLICT",
                "Another initial capitalization request completed first.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TreasuryInitialCapitalization> ReverseAsync(
        Guid tenantId,
        Guid ownerUserId,
        Guid capitalizationId,
        ReverseTreasuryInitialCapitalizationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || ownerUserId == Guid.Empty || capitalizationId == Guid.Empty || command.OperationId == Guid.Empty)
            throw Validation("INVALID_ALLOCATION", "Tenant, Owner, document, and operation identities are required.");
        if (!command.Confirmed)
            throw Validation("INVALID_ALLOCATION", "Explicit reversal confirmation is required.");
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 500)
            throw Validation("INVALID_ALLOCATION", "A reversal reason of at most 500 characters is required.");

        var reason = command.Reason.Trim();
        var requestHash = Hash(new { capitalizationId, command.OperationId, reason, command.Confirmed });
        await EnsureOwnerAsync(tenantId, ownerUserId, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await LockTenantAsync(tenantId, cancellationToken);
            var document = await _db.TreasuryInitialCapitalizations
                .IgnoreQueryFilters()
                .Include(item => item.Allocations)
                .SingleOrDefaultAsync(
                    item => item.TenantId == tenantId && item.Id == capitalizationId,
                    cancellationToken);
            if (document == null)
                throw new TreasuryInitialCapitalizationException(
                    "INITIAL_CAPITALIZATION_NOT_FOUND",
                    "Initial capitalization was not found.",
                    TreasuryInitialCapitalizationErrorKind.NotFound);

            if (document.Status == TreasuryInitialCapitalizationStatus.Reversed)
            {
                if (document.ReversalOperationId == command.OperationId && document.ReversalRequestHash == requestHash)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return document;
                }

                if (document.ReversalOperationId == command.OperationId)
                    throw Error("IDEMPOTENCY_CONFLICT", "The reversal operation ID is bound to a different request.");

                throw Error("INITIAL_CAPITALIZATION_ALREADY_EXISTS", "The initial capitalization is already reversed.");
            }

            if (document.Status != TreasuryInitialCapitalizationStatus.Posted || !document.PostedJournalId.HasValue)
                throw Error("INITIAL_CAPITALIZATION_NOT_ELIGIBLE", "Only a Posted capitalization can be reversed.");

            await ValidateFrozenJournalAsync(document, cancellationToken);

            var outbox = CreateReversalOutbox(document, command.OperationId, requestHash, reason);
            _db.AccountingOutboxMessages.Add(outbox);
            await _db.SaveChangesAsync(cancellationToken);

            var reversal = await _postingService.ReverseJournalAsync(
                tenantId,
                document.PostedJournalId.Value,
                reason,
                ownerUserId,
                cancellationToken);

            var reversedAt = DateTime.UtcNow;
            document.MarkReversed(reversal.Id, command.OperationId, requestHash, reason, reversedAt);
            outbox.MarkProcessed(reversal.Id);
            outbox.UpdatedAt = reversedAt;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return document;
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw Error("CONCURRENCY_CONFLICT", "The capitalization changed concurrently. Reload before retrying.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task EnsureOwnerAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var isOwner = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(user =>
                user.Id == userId &&
                user.TenantId == tenantId &&
                user.Role == UserRole.Owner &&
                user.IsActive &&
                !user.IsDeleted,
                cancellationToken);
        if (!isOwner)
        {
            throw new TreasuryInitialCapitalizationException(
                "AUTHORIZATION_REQUIRED",
                "An active tenant Owner is required for this financial attestation.",
                TreasuryInitialCapitalizationErrorKind.Forbidden);
        }
    }

    private async Task ValidateEligibilityAsync(
        Guid tenantId,
        DateTime cutoverDate,
        Guid? predecessorId,
        CancellationToken cancellationToken)
    {
        if (await _db.TreasuryInitialCapitalizations.IgnoreQueryFilters().AnyAsync(
                item => item.TenantId == tenantId &&
                    !item.IsDeleted &&
                    item.Status != TreasuryInitialCapitalizationStatus.Reversed,
                cancellationToken))
        {
            throw Error("INITIAL_CAPITALIZATION_ALREADY_EXISTS", "An active initial capitalization already exists.");
        }

        var priorDocuments = await _db.TreasuryInitialCapitalizations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .OrderBy(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        if (priorDocuments.Count > 0)
        {
            if (!predecessorId.HasValue)
                throw Error("INITIAL_CAPITALIZATION_NOT_ELIGIBLE", "A replacement must reference the reversed predecessor.");

            var predecessor = priorDocuments.SingleOrDefault(item => item.Id == predecessorId.Value);
            if (predecessor == null || predecessor.Status != TreasuryInitialCapitalizationStatus.Reversed ||
                predecessor.CutoverDate != cutoverDate || predecessor != priorDocuments[^1])
            {
                throw Error("INITIAL_CAPITALIZATION_NOT_ELIGIBLE", "The replacement predecessor or cutover date is invalid.");
            }
        }
        else if (predecessorId.HasValue)
        {
            throw Error("INITIAL_CAPITALIZATION_NOT_ELIGIBLE", "A predecessor cannot be used for the first capitalization.");
        }

        var treasuryAccountIds = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId &&
                (item.Type == TreasuryAccountType.Cashbox || item.Type == TreasuryAccountType.Bank))
            .Select(item => item.AccountId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var allowedJournalIds = priorDocuments
            .Where(item => item.Status == TreasuryInitialCapitalizationStatus.Reversed)
            .SelectMany(item => new[] { item.PostedJournalId, item.ReversalJournalId })
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToHashSet();
        var hasUnrelatedTreasuryMovement = await _db.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(entry =>
                entry.TenantId == tenantId &&
                treasuryAccountIds.Contains(entry.AccountId) &&
                (entry.JournalEntry.Status == JournalStatus.Posted || entry.JournalEntry.Status == JournalStatus.Reversed) &&
                !allowedJournalIds.Contains(entry.JournalEntryId),
                cancellationToken);
        if (hasUnrelatedTreasuryMovement)
            throw Error("INITIAL_CAPITALIZATION_NOT_ELIGIBLE", "Existing Treasury movement prevents initial capitalization.");
    }

    private async Task<Dictionary<Guid, TreasuryAccount>> ResolveDestinationsAsync(
        Guid tenantId,
        IReadOnlyList<TreasuryInitialCapitalizationAllocationCommand> allocations,
        CancellationToken cancellationToken)
    {
        var ids = allocations.Select(item => item.TreasuryAccountId).ToList();
        var destinations = await _db.TreasuryAccounts
            .IgnoreQueryFilters()
            .Include(item => item.Account)
            .Where(item => item.TenantId == tenantId && ids.Contains(item.Id))
            .OrderBy(item => item.AccountId)
            .ToListAsync(cancellationToken);
        if (destinations.Count != ids.Count)
            throw Validation("INVALID_TREASURY_DESTINATION", "Every destination must be a same-tenant Treasury account.");

        foreach (var destination in destinations)
        {
            if (!destination.IsActive || destination.IsDeleted ||
                destination.Type is not (TreasuryAccountType.Cashbox or TreasuryAccountType.Bank) ||
                destination.Account == null || destination.Account.TenantId != tenantId ||
                !destination.Account.IsActive || destination.Account.IsDeleted ||
                destination.Account.AccountType != AccountType.Asset || destination.Account.IsClearing ||
                ForbiddenDestinationCodes.Contains(destination.Account.AccountCode))
            {
                throw Validation("INVALID_TREASURY_DESTINATION", "Only active same-tenant Cashbox or Bank Treasury assets are allowed.");
            }
        }

        return destinations.ToDictionary(item => item.Id);
    }

    private async Task<Account> ResolveOpeningEquityAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var account = await _db.LedgerAccounts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item =>
                item.TenantId == tenantId && item.AccountCode == "3010",
                cancellationToken);
        if (account == null || !account.IsActive || account.IsDeleted ||
            account.AccountType != AccountType.Equity || account.IsClearing)
        {
            throw Error("INVALID_OPENING_EQUITY", "Active same-tenant Opening Balance Equity 3010 is required.");
        }

        return account;
    }

    private async Task<TreasuryInitialCapitalization?> ResolveCreateReplayAsync(
        Guid tenantId,
        Guid operationId,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await GetByOperationIdAsync(tenantId, operationId, cancellationToken);
        if (existing == null)
            return null;
        if (existing.RequestHash != requestHash)
            throw Error("IDEMPOTENCY_CONFLICT", "The operation ID is bound to a different capitalization request.");
        if (existing.Status == TreasuryInitialCapitalizationStatus.Draft)
            throw Error("CONCURRENCY_CONFLICT", "The capitalization request is still being processed.");
        return existing;
    }

    private async Task<TreasuryInitialCapitalization?> GetByOperationIdAsync(
        Guid tenantId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        return await _db.TreasuryInitialCapitalizations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(document => document.Allocations)
            .SingleOrDefaultAsync(
                document => document.TenantId == tenantId && document.OperationId == operationId,
                cancellationToken);
    }

    private async Task ValidateFrozenJournalAsync(
        TreasuryInitialCapitalization document,
        CancellationToken cancellationToken)
    {
        var lines = await _db.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == document.TenantId && item.JournalEntryId == document.PostedJournalId)
            .ToListAsync(cancellationToken);
        var destinationLines = lines.Where(item => item.AccountId != document.OpeningEquityLedgerAccountId).ToList();
        var offsetLines = lines.Where(item => item.AccountId == document.OpeningEquityLedgerAccountId).ToList();
        if (offsetLines.Count != 1 || offsetLines[0].DebitAmount != 0m || offsetLines[0].CreditAmount != document.TotalAmount ||
            destinationLines.Count != document.Allocations.Count)
        {
            throw Error("INITIAL_CAPITALIZATION_NOT_ELIGIBLE", "The original capitalization journal does not match frozen authority.");
        }

        foreach (var allocation in document.Allocations)
        {
            var line = destinationLines.SingleOrDefault(item => item.AccountId == allocation.LedgerAccountId);
            if (line == null || line.DebitAmount != allocation.Amount || line.CreditAmount != 0m)
                throw Error("INITIAL_CAPITALIZATION_NOT_ELIGIBLE", "The original capitalization journal does not match frozen allocations.");
        }
    }

    private static void ValidateCreateInput(
        Guid tenantId,
        Guid ownerUserId,
        CreateTreasuryInitialCapitalizationCommand command)
    {
        if (tenantId == Guid.Empty || ownerUserId == Guid.Empty || command.OperationId == Guid.Empty)
            throw Validation("INVALID_ALLOCATION", "Tenant, Owner, and operation identities are required.");
        if (!command.DeclarationAcknowledged ||
            !string.Equals(command.DeclarationVersion, TreasuryInitialCapitalization.DeclarationIdentifier, StringComparison.Ordinal))
        {
            throw Validation("INVALID_ALLOCATION", "The current accounting-cutover declaration must be acknowledged.");
        }
        if (command.CutoverDate.Kind == DateTimeKind.Local)
            throw Validation("INVALID_CUTOVER_DATE", "Cutover date must not use a local time zone.");
        if (NormalizeDate(command.CutoverDate) > DateTime.UtcNow.Date)
            throw Validation("INVALID_CUTOVER_DATE", "Cutover date cannot be in the future.");
        if (command.Allocations == null || command.Allocations.Count == 0)
            throw Validation("INVALID_ALLOCATION", "At least one Treasury allocation is required.");
        if (string.IsNullOrWhiteSpace(command.EvidenceReference) || command.EvidenceReference.Trim().Length > 200)
            throw Validation("INVALID_ALLOCATION", "An evidence reference of at most 200 characters is required.");
        if (command.Notes?.Trim().Length > 1000)
            throw Validation("INVALID_ALLOCATION", "Notes must not exceed 1000 characters.");
    }

    private static IReadOnlyList<TreasuryInitialCapitalizationAllocationCommand> NormalizeAllocations(
        IReadOnlyList<TreasuryInitialCapitalizationAllocationCommand> allocations)
    {
        if (allocations.Any(item => item.TreasuryAccountId == Guid.Empty))
            throw Validation("INVALID_TREASURY_DESTINATION", "A Treasury destination is required for every allocation.");
        if (allocations.GroupBy(item => item.TreasuryAccountId).Any(group => group.Count() > 1))
            throw Validation("INVALID_ALLOCATION", "A Treasury destination may be allocated only once.");

        var normalized = allocations
            .Select(item => item with { Amount = Math.Round(item.Amount, 2, MidpointRounding.AwayFromZero) })
            .OrderBy(item => item.TreasuryAccountId)
            .ToList();
        if (normalized.Any(item => item.Amount <= 0m))
            throw Validation("INVALID_ALLOCATION", "Every allocation amount must be at least 0.01.");
        if (normalized.Sum(item => item.Amount) <= 0m)
            throw Validation("INVALID_ALLOCATION", "Capitalization total must be positive.");
        return normalized;
    }

    private async Task LockTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return;

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = _db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? "SELECT 1 FROM \"Tenants\" WHERE \"Id\" = @tenantId FOR UPDATE"
            : "SELECT 1 FROM [Tenants] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = @tenantId";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenantId";
        parameter.Value = tenantId;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result == null)
            throw Error("INITIAL_CAPITALIZATION_NOT_ELIGIBLE", "The tenant is not available for capitalization.");
    }

    private static AccountingOutboxMessage CreateOutbox(
        TreasuryInitialCapitalization document,
        Guid operationId)
    {
        var message = AccountingOutboxMessage.Create(
            document.TenantId,
            TreasuryInitialCapitalization.AccountingSourceType,
            document.Id,
            nameof(TreasuryInitialCapitalization),
            JsonSerializer.Serialize(new
            {
                document.Id,
                document.DocumentNumber,
                document.CutoverDate,
                document.TotalAmount,
                document.DeclarationVersion,
                document.EvidenceReference,
                document.CreatedByUserId,
                document.OpeningEquityLedgerAccountId,
                Allocations = document.Allocations.Select(item => new
                {
                    item.TreasuryAccountId,
                    item.LedgerAccountId,
                    item.TreasuryAccountTypeSnapshot,
                    item.Amount
                })
            }),
            $"OUTBOX:{BuildPostingKey(document.TenantId, operationId)}",
            sourceType: TreasuryInitialCapitalization.AccountingSourceType);
        message.Id = Guid.NewGuid();
        message.CreatedAt = DateTime.UtcNow;
        return message;
    }

    private static AccountingOutboxMessage CreateReversalOutbox(
        TreasuryInitialCapitalization document,
        Guid operationId,
        string requestHash,
        string reason)
    {
        var eventType = "TreasuryInitialCapitalizationReversal";
        var message = AccountingOutboxMessage.Create(
            document.TenantId,
            eventType,
            document.Id,
            nameof(TreasuryInitialCapitalization),
            JsonSerializer.Serialize(new { document.Id, operationId, requestHash, reason, document.PostedJournalId }),
            $"OUTBOX:TREASURY_INITIAL_CAPITALIZATION_REVERSAL:{document.TenantId}:{operationId}",
            sourceType: "Reversal");
        message.Id = Guid.NewGuid();
        message.CreatedAt = DateTime.UtcNow;
        return message;
    }

    private static string HashCreateRequest(
        DateTime cutoverDate,
        IReadOnlyList<TreasuryInitialCapitalizationAllocationCommand> allocations,
        string evidenceReference,
        string? notes,
        string declarationVersion,
        bool declarationAcknowledged,
        Guid? predecessorCapitalizationId) => Hash(new
        {
            cutoverDate,
            allocations,
            evidenceReference,
            notes,
            declarationVersion,
            declarationAcknowledged,
            predecessorCapitalizationId
        });

    private static string Hash(object value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private static string BuildPostingKey(Guid tenantId, Guid operationId) =>
        $"TREASURY_INITIAL_CAPITALIZATION:{tenantId}:{operationId}";

    private static DateTime NormalizeDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException!)
        {
            if (current is DbUpdateConcurrencyException ||
                current is PostgresException { SqlState: "23505" or "40001" })
            {
                return true;
            }
        }

        return false;
    }

    private static TreasuryInitialCapitalizationException Validation(string code, string message) =>
        new(code, message, TreasuryInitialCapitalizationErrorKind.Validation);

    private static TreasuryInitialCapitalizationException Error(string code, string message) =>
        new(code, message, TreasuryInitialCapitalizationErrorKind.Conflict);
}
