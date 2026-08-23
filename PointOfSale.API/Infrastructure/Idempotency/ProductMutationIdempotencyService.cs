using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Infrastructure.Idempotency;

public sealed class ProductMutationIdempotencyService : IProductMutationIdempotencyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ProductMutationIdempotencyService(
        ApplicationDbContext context,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProductMutationIdempotencyClaim> TryClaimAsync(
        ProductMutationIdempotencyRequest request,
        CancellationToken cancellationToken = default)
    {
        var claimId = Guid.NewGuid();
        var nowUtc = NormalizeTimestamp(_timeProvider.GetUtcNow().UtcDateTime);
        int rowsInserted;

        if (IsPostgresProvider())
        {
            rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""ProductRequestIdempotency""
                (""Id"", ""TenantId"", ""Key"", ""Operation"", ""ProductId"", ""ProductVariantId"", ""RequestFingerprint"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                ({claimId}, {request.TenantId}, {request.Key}, {request.Operation}, {request.ProductId}, {request.ProductVariantId}, {request.RequestFingerprint}, {nowUtc}, {nowUtc}, {false})
                ON CONFLICT (""TenantId"", ""Key"", ""Operation"") DO NOTHING",
                cancellationToken);
        }
        else if (IsSqlServerProvider())
        {
            rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [ProductRequestIdempotency]
                    ([Id], [TenantId], [Key], [Operation], [ProductId], [ProductVariantId], [RequestFingerprint], [CreatedAt], [UpdatedAt], [IsDeleted])
                SELECT
                    {claimId}, {request.TenantId}, {request.Key}, {request.Operation}, {request.ProductId}, {request.ProductVariantId}, {request.RequestFingerprint}, {nowUtc}, {nowUtc}, {false}
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [ProductRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [TenantId] = {request.TenantId}
                      AND [Key] = {request.Key}
                      AND [Operation] = {request.Operation})",
                cancellationToken);
        }
        else if (IsSqliteProvider())
        {
            rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT OR IGNORE INTO ""ProductRequestIdempotency""
                (""Id"", ""TenantId"", ""Key"", ""Operation"", ""ProductId"", ""ProductVariantId"", ""RequestFingerprint"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                ({claimId}, {request.TenantId}, {request.Key}, {request.Operation}, {request.ProductId}, {request.ProductVariantId}, {request.RequestFingerprint}, {nowUtc}, {nowUtc}, {false})",
                cancellationToken);
        }
        else
        {
            throw new NotSupportedException(
                $"Database provider '{_context.Database.ProviderName}' is not supported for product mutation idempotency.");
        }

        if (rowsInserted > 0)
        {
            var claimedRecord = await _context.ProductRequestIdempotencyRecords
                .FirstAsync(record =>
                    record.TenantId == request.TenantId &&
                    record.Key == request.Key &&
                    record.Operation == request.Operation,
                    cancellationToken);

            claimedRecord.ClaimLeaseUpdatedAtUtc = claimedRecord.UpdatedAt;

            return new ProductMutationIdempotencyClaim(claimedRecord, null);
        }

        var existingRecord = await TryResolveAsync(
            request.TenantId,
            request.Key,
            request.Operation,
            cancellationToken);

        if (existingRecord is null)
        {
            return new ProductMutationIdempotencyClaim(
                null,
                new ProductMutationIdempotencyReplay(ProductMutationIdempotencyReplayKind.InProgress, null));
        }

        if (!string.Equals(existingRecord.RequestFingerprint, request.RequestFingerprint, StringComparison.Ordinal))
        {
            return new ProductMutationIdempotencyClaim(
                null,
                new ProductMutationIdempotencyReplay(ProductMutationIdempotencyReplayKind.Conflict, existingRecord));
        }

        if (!existingRecord.ResponseStatusCode.HasValue)
        {
            return new ProductMutationIdempotencyClaim(
                null,
                new ProductMutationIdempotencyReplay(ProductMutationIdempotencyReplayKind.InProgress, existingRecord));
        }

        return new ProductMutationIdempotencyClaim(
            null,
            new ProductMutationIdempotencyReplay(ProductMutationIdempotencyReplayKind.Completed, existingRecord));
    }

    public Task<ProductRequestIdempotency?> TryResolveAsync(
        Guid tenantId,
        string key,
        string operation,
        CancellationToken cancellationToken = default) =>
        _context.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(record =>
                record.TenantId == tenantId &&
                record.Key == key &&
                record.Operation == operation,
                cancellationToken);

    public void Complete(
        ProductRequestIdempotency? record,
        int statusCode,
        object? responsePayload,
        Guid? productId = null,
        Guid? productVariantId = null)
    {
        if (record is null)
        {
            return;
        }

        record.ResponseStatusCode = statusCode;
        record.ResponseJson = responsePayload is null
            ? null
            : JsonSerializer.Serialize(responsePayload, JsonOptions);
        record.CompletedAtUtc = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        if (productId.HasValue)
        {
            record.ProductId = productId.Value;
        }

        if (productVariantId.HasValue)
        {
            record.ProductVariantId = productVariantId.Value;
        }
    }

    public string ScopeKey(string scope, string idempotencyKey)
    {
        var scopedKey = $"{scope}:{idempotencyKey.Trim()}";
        return scopedKey.Length <= 200
            ? scopedKey
            : $"{scope}:sha256:{HashToHex(scopedKey)}";
    }

    public async Task<ProductMutationIdempotencyRecoveryResult> TryRecoverAbandonedClaimAsync(
        ProductMutationIdempotencyRequest request,
        TimeSpan leaseDuration,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var staleCutoffUtc = NormalizeTimestamp(nowUtc - leaseDuration);

        var takenOver = await _context.ProductRequestIdempotencyRecords
            .Where(record =>
                record.TenantId == request.TenantId &&
                record.Key == request.Key &&
                record.Operation == request.Operation &&
                record.RequestFingerprint == request.RequestFingerprint &&
                record.ResponseStatusCode == null &&
                (record.UpdatedAt ?? record.CreatedAt) <= staleCutoffUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(record => record.UpdatedAt, NormalizeTimestamp(nowUtc)),
                cancellationToken);

        if (takenOver != 1)
        {
            return new ProductMutationIdempotencyRecoveryResult(
                ProductMutationIdempotencyRecoveryKind.NotRecoverable,
                null);
        }

        var recovered = await ReadClaimRecordAsync(
            request.TenantId,
            request.Key,
            request.Operation,
            cancellationToken);

        if (recovered is null)
        {
            return new ProductMutationIdempotencyRecoveryResult(
                ProductMutationIdempotencyRecoveryKind.NotRecoverable,
                recovered);
        }

        recovered.ClaimLeaseUpdatedAtUtc = recovered.UpdatedAt;
        return new ProductMutationIdempotencyRecoveryResult(
            ProductMutationIdempotencyRecoveryKind.Recovered,
            recovered);
    }

    public async Task<bool> TryRenewClaimLeaseAsync(
        ProductRequestIdempotency record,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var expectedLeaseUpdatedAtUtc = record.ClaimLeaseUpdatedAtUtc ?? record.UpdatedAt;
        if (record.Id == Guid.Empty || !expectedLeaseUpdatedAtUtc.HasValue)
        {
            return false;
        }

        var renewedAtUtc = NormalizeTimestamp(nowUtc);
        var renewed = await _context.ProductRequestIdempotencyRecords
            .Where(existing =>
                existing.Id == record.Id &&
                existing.TenantId == record.TenantId &&
                existing.ResponseStatusCode == null &&
                existing.UpdatedAt == expectedLeaseUpdatedAtUtc &&
                existing.UpdatedAt <= renewedAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(existing => existing.UpdatedAt, renewedAtUtc),
                cancellationToken);

        if (renewed == 1)
        {
            record.UpdatedAt = renewedAtUtc;
            record.ClaimLeaseUpdatedAtUtc = renewedAtUtc;
            return true;
        }

        return false;
    }

    public async Task<bool> TryWriteIncompleteClaimStateAsync(
        ProductRequestIdempotency record,
        string responseJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseJson);

        var expectedLeaseUpdatedAtUtc = record.ClaimLeaseUpdatedAtUtc ?? record.UpdatedAt;
        if (record.Id == Guid.Empty || !expectedLeaseUpdatedAtUtc.HasValue)
        {
            return false;
        }

        // Preserve the current lease/fencing token. The owner may annotate an
        // incomplete claim only while it still owns that exact token; a stale
        // worker cannot mark or overwrite a claim after takeover.
        var updated = await _context.ProductRequestIdempotencyRecords
            .Where(existing =>
                existing.Id == record.Id &&
                existing.TenantId == record.TenantId &&
                existing.ResponseStatusCode == null &&
                existing.UpdatedAt == expectedLeaseUpdatedAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(existing => existing.ResponseJson, responseJson),
                cancellationToken);

        if (updated == 1)
        {
            record.ResponseJson = responseJson;
            return true;
        }

        return false;
    }

    public async Task<ProductMutationIdempotencyCompletionResult> TryCompleteDurablyAsync(
        ProductRequestIdempotency record,
        int statusCode,
        object? responsePayload,
        DateTime? completedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (record.Id == Guid.Empty)
        {
            throw new ArgumentException(
                "The idempotency record must be a persisted row with a non-empty identifier.",
                nameof(record));
        }

        var responseJson = responsePayload is null
            ? null
            : JsonSerializer.Serialize(responsePayload, JsonOptions);
        var completionTimestamp = NormalizeTimestamp(
            completedAtUtc ?? _timeProvider.GetUtcNow().UtcDateTime);
        var expectedLeaseUpdatedAtUtc = record.ClaimLeaseUpdatedAtUtc ?? record.UpdatedAt;

        if (!expectedLeaseUpdatedAtUtc.HasValue)
        {
            return new ProductMutationIdempotencyCompletionResult(
                ProductMutationIdempotencyCompletionKind.LostOwnership,
                await ReadClaimByIdAsync(record.Id, record.TenantId, cancellationToken));
        }

        var updated = await _context.ProductRequestIdempotencyRecords
            .Where(existing =>
                existing.Id == record.Id &&
                existing.TenantId == record.TenantId &&
                existing.ResponseStatusCode == null &&
                existing.UpdatedAt == expectedLeaseUpdatedAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(existing => existing.ResponseStatusCode, statusCode)
                .SetProperty(existing => existing.ResponseJson, responseJson)
                .SetProperty(existing => existing.CompletedAtUtc, completionTimestamp)
                .SetProperty(existing => existing.UpdatedAt, completionTimestamp),
                cancellationToken);

        var fresh = await ReadClaimByIdAsync(record.Id, record.TenantId, cancellationToken);

        if (updated == 1 && fresh is not null)
        {
            ApplyCompletion(record, statusCode, responseJson, completionTimestamp);
            return new ProductMutationIdempotencyCompletionResult(
                ProductMutationIdempotencyCompletionKind.PersistedNow,
                fresh);
        }

        return fresh is not null && fresh.ResponseStatusCode.HasValue
            ? new ProductMutationIdempotencyCompletionResult(
                ProductMutationIdempotencyCompletionKind.AlreadyPersisted,
                fresh)
            : new ProductMutationIdempotencyCompletionResult(
                ProductMutationIdempotencyCompletionKind.LostOwnership,
                fresh);
    }

    private Task<ProductRequestIdempotency?> ReadClaimRecordAsync(
        Guid tenantId,
        string key,
        string operation,
        CancellationToken cancellationToken) =>
        _context.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(record =>
                record.TenantId == tenantId &&
                record.Key == key &&
                record.Operation == operation,
                cancellationToken);

    private async Task<ProductRequestIdempotency?> ReadClaimByIdAsync(
        Guid id,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await _context.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                record => record.Id == id && record.TenantId == tenantId,
                cancellationToken);

    private static void ApplyCompletion(
        ProductRequestIdempotency record,
        int statusCode,
        string? responseJson,
        DateTime completionTimestamp)
    {
        record.ResponseStatusCode = statusCode;
        record.ResponseJson = responseJson;
        record.CompletedAtUtc = completionTimestamp;
        record.UpdatedAt = completionTimestamp;
        record.ClaimLeaseUpdatedAtUtc = completionTimestamp;
    }

    private static DateTime NormalizeTimestamp(DateTime value) =>
        new(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            value.Minute,
            value.Second,
            DateTimeKind.Utc);

    private bool IsPostgresProvider() =>
        _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqliteProvider() =>
        _context.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private static string HashToHex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
