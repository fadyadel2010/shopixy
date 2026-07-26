using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services;

/// <summary>
/// Shared replay-safe idempotency coordination for financial write operations.
/// Persists request hashes and response snapshots so retries never create duplicate money movement.
/// </summary>
public class FinancialMutationIdempotencyService : IFinancialMutationIdempotencyService
{
    private const int StaleClaimMinutes = 5;

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<FinancialMutationIdempotencyService> _logger;

    public FinancialMutationIdempotencyService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<FinancialMutationIdempotencyService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<(SalesRequestIdempotency? Record, IActionResult? ShortCircuitResult)> TryClaimAsync(
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var claimId = Guid.NewGuid();
            var claimToken = Guid.NewGuid().ToString("N");
            var nowUtc = DateTime.UtcNow;
            var rowsInserted = await TryInsertClaimAsync(
                claimId,
                tenantId,
                key,
                operation,
                requestHash,
                claimToken,
                nowUtc,
                cancellationToken);

            if (rowsInserted > 0)
            {
                var claimedRecord = await _context.SalesRequestIdempotencyRecords
                    .FirstAsync(record =>
                        record.TenantId == tenantId &&
                        record.Key == key &&
                        record.Operation == operation,
                        cancellationToken);

                _logger.LogInformation(
                    "Financial mutation idempotency claimed | TenantId: {TenantId} | Operation: {Operation} | Key: {Key}",
                    tenantId,
                    operation,
                    key);

                return (claimedRecord, null);
            }

            var existingRecord = await _context.SalesRequestIdempotencyRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(record =>
                    record.TenantId == tenantId &&
                    record.Key == key &&
                    record.Operation == operation,
                    cancellationToken);

            if (existingRecord?.IsDeleted == true)
            {
                await DeleteRecordByIdAsync(existingRecord.Id, cancellationToken);
                continue;
            }

            if (existingRecord == null)
            {
                continue;
            }

            if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return (null, new BadRequestObjectResult(new
                {
                    message = "Idempotency key reuse with different payload"
                }));
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                _logger.LogInformation(
                    "Financial mutation idempotency replay | TenantId: {TenantId} | Operation: {Operation} | Key: {Key}",
                    tenantId,
                    operation,
                    key);

                return (null, BuildStoredResponse(existingRecord));
            }

            var claimAge = nowUtc - (existingRecord.ClaimTimestampUtc ?? existingRecord.CreatedAt);
            if (claimAge.TotalMinutes < StaleClaimMinutes)
            {
                return (null, new ConflictObjectResult(new
                {
                    message = "A financial mutation with the same idempotency key is already being processed."
                }));
            }

            var rowsDeleted = await DeleteStaleClaimAsync(
                tenantId,
                key,
                operation,
                nowUtc.AddMinutes(-StaleClaimMinutes),
                cancellationToken);

            if (rowsDeleted > 0)
            {
                continue;
            }

            return (null, new ConflictObjectResult(new
            {
                message = "A financial mutation with the same idempotency key is already being processed."
            }));
        }

        return (null, new ConflictObjectResult(new
        {
            message = "A financial mutation with the same idempotency key is already being processed."
        }));
    }

    public async Task<IActionResult> CompleteAsync(
        IDbContextTransaction transaction,
        SalesRequestIdempotency record,
        IActionResult result,
        Func<IActionResult, (int StatusCode, string? ResponseJson)> snapshotFactory,
        CancellationToken cancellationToken = default)
    {
        var currentRecord = await _context.SalesRequestIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == record.Id, cancellationToken);

        if (currentRecord == null || currentRecord.ClaimToken != record.ClaimToken)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Financial mutation idempotency claim token validation failed.");
        }

        var snapshot = snapshotFactory(result);
        record.ResponseStatusCode = snapshot.StatusCode;
        record.ResponseJson = snapshot.ResponseJson;
        record.CompletedAtUtc = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    private bool IsPostgresProvider() =>
        _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<int> TryInsertClaimAsync(
        Guid claimId,
        Guid tenantId,
        string key,
        string operation,
        string requestHash,
        string claimToken,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""SalesRequestIdempotency""
                (""Id"", ""TenantId"", ""Key"", ""Operation"", ""RequestHash"", ""ClaimToken"", ""ClaimTimestampUtc"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                ({claimId}, {tenantId}, {key}, {operation}, {requestHash}, {claimToken}, {nowUtc}, {nowUtc}, {nowUtc}, {false})
                ON CONFLICT (""TenantId"", ""Key"", ""Operation"") DO NOTHING", cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [SalesRequestIdempotency]
                    ([Id], [TenantId], [Key], [Operation], [RequestHash], [ClaimToken], [ClaimTimestampUtc], [CreatedAt], [UpdatedAt], [IsDeleted])
                SELECT
                    {claimId}, {tenantId}, {key}, {operation}, {requestHash}, {claimToken}, {nowUtc}, {nowUtc}, {nowUtc}, {false}
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [SalesRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Key] = {key}
                      AND [Operation] = {operation})", cancellationToken);
        }

        var existingTrackedClaim = _context.ChangeTracker
            .Entries<SalesRequestIdempotency>()
            .Any(entry =>
                entry.State != EntityState.Detached &&
                entry.State != EntityState.Deleted &&
                entry.Entity.TenantId == tenantId &&
                entry.Entity.Key == key &&
                entry.Entity.Operation == operation);

        if (existingTrackedClaim)
        {
            return 0;
        }

        var existingPersistedClaim = await _context.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(record =>
                record.TenantId == tenantId &&
                record.Key == key &&
                record.Operation == operation,
                cancellationToken);

        if (existingPersistedClaim)
        {
            return 0;
        }

        var claim = new SalesRequestIdempotency
        {
            Id = claimId,
            TenantId = tenantId,
            Key = key,
            Operation = operation,
            RequestHash = requestHash,
            ClaimToken = claimToken,
            ClaimTimestampUtc = nowUtc,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            IsDeleted = false,
        };

        _context.SalesRequestIdempotencyRecords.Add(claim);

        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(claim).State = EntityState.Detached;
            return 0;
        }
    }

    private async Task<int> DeleteRecordByIdAsync(Guid recordId, CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM ""SalesRequestIdempotency""
                WHERE ""Id"" = {recordId}", cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM [SalesRequestIdempotency]
                WHERE [Id] = {recordId}", cancellationToken);
        }

        return await _context.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record => record.Id == recordId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<int> DeleteStaleClaimAsync(
        Guid tenantId,
        string key,
        string operation,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM ""SalesRequestIdempotency""
                WHERE ""TenantId"" = {tenantId}
                  AND ""Key"" = {key}
                  AND ""Operation"" = {operation}
                  AND ""ResponseStatusCode"" IS NULL
                  AND ""CompletedAtUtc"" IS NULL
                  AND COALESCE(""ClaimTimestampUtc"", ""CreatedAt"") < {cutoffUtc}", cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM [SalesRequestIdempotency]
                WHERE [TenantId] = {tenantId}
                  AND [Key] = {key}
                  AND [Operation] = {operation}
                  AND [ResponseStatusCode] IS NULL
                  AND [CompletedAtUtc] IS NULL
                  AND COALESCE([ClaimTimestampUtc], [CreatedAt]) < {cutoffUtc}", cancellationToken);
        }

        return await _context.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == tenantId &&
                record.Key == key &&
                record.Operation == operation &&
                record.ResponseStatusCode == null &&
                record.CompletedAtUtc == null &&
                (record.ClaimTimestampUtc ?? record.CreatedAt) < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static IActionResult BuildStoredResponse(SalesRequestIdempotency record)
    {
        var statusCode = record.ResponseStatusCode ?? StatusCodes.Status200OK;
        if (string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return new StatusCodeResult(statusCode);
        }

        using var document = JsonDocument.Parse(record.ResponseJson);
        var payload = document.RootElement.Clone();
        return new ObjectResult(payload)
        {
            StatusCode = statusCode,
        };
    }
}
