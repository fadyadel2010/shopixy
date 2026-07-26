using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Checkout;

public enum CheckoutIdempotencyClaimStatus
{
    Claimed,
    Replay,
    PayloadConflict,
    InProgress,
    ExistingOrder,
    /// <summary>
    /// Fast-path lookup: no completed record found for this checkoutId.
    /// Caller should proceed with the normal cart-snapshot + full-claim flow.
    /// </summary>
    NotFound
}

public sealed record CheckoutIdempotencyClaimResult(
    CheckoutIdempotencyClaimStatus Status,
    OrderRequestIdempotency? Record = null,
    CheckoutResponseDto? ReplayResponse = null,
    Guid? ExistingOrderId = null,
    string? ConflictMessage = null,
    string? CheckoutId = null);

public interface ICheckoutRequestIdempotencyCoordinator
{
    /// <summary>
    /// Attempts to claim an idempotency slot for the given checkout.
    /// When <paramref name="snapshotHash"/> is <c>null</c> the call is a
    /// lookup-only fast-path that returns <see cref="CheckoutIdempotencyClaimStatus.Replay"/>
    /// or <see cref="CheckoutIdempotencyClaimStatus.ExistingOrder"/> for already-completed
    /// checkouts, or <see cref="CheckoutIdempotencyClaimStatus.NotFound"/> otherwise.
    /// No new claim row is inserted when <paramref name="snapshotHash"/> is <c>null</c>.
    /// </summary>
    Task<CheckoutIdempotencyClaimResult> TryClaimAsync(
        Guid tenantId,
        string checkoutId,
        string requestHash,
        string? snapshotHash,
        CancellationToken cancellationToken = default);

    Task MarkOrderCreatedAsync(
        OrderRequestIdempotency record,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<OrderRequestIdempotency> BindOrderCreatedForCommitAsync(
        OrderRequestIdempotency record,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        OrderRequestIdempotency record,
        CheckoutResponseDto response,
        CancellationToken cancellationToken = default);

    Task DeleteClaimAsync(
        OrderRequestIdempotency? record,
        CancellationToken cancellationToken = default);
}

public sealed class CheckoutRequestIdempotencyCoordinator : ICheckoutRequestIdempotencyCoordinator
{
    private const int SuccessStatusCode = StatusCodes.Status200OK;
    private const int DefaultStaleClaimGraceSeconds = 30;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _context;
    private readonly ILogger<CheckoutRequestIdempotencyCoordinator> _logger;
    private readonly TimeSpan _staleClaimWindow;

    public CheckoutRequestIdempotencyCoordinator(
        ApplicationDbContext context,
        IOptions<CheckoutExecutionLockOptions> lockOptions,
        ILogger<CheckoutRequestIdempotencyCoordinator> logger)
    {
        _context = context;
        _logger = logger;
        
        // Align stale window with execution lock TTL + grace period
        // Fall back to 5 minutes for backward compatibility if configuration is missing
        var lockTtl = lockOptions.Value.LockTtl;
        _staleClaimWindow = lockTtl > TimeSpan.Zero
            ? lockTtl.Add(TimeSpan.FromSeconds(DefaultStaleClaimGraceSeconds))
            : TimeSpan.FromMinutes(5);
    }

    public async Task<CheckoutIdempotencyClaimResult> TryClaimAsync(
        Guid tenantId,
        string checkoutId,
        string requestHash,
        string? snapshotHash,
        CancellationToken cancellationToken = default)
    {
        var storageKey = BuildStorageKey(checkoutId);

        // -----------------------------------------------------------------
        // Fast-path: lookup-only when snapshotHash is null.
        // Returns completed records (Replay / ExistingOrder) without
        // attempting to insert a new claim row.  This lets callers check
        // for a previously-completed checkout *before* reading the cart,
        // which may have been cleared or expired after the first success.
        // -----------------------------------------------------------------
        if (snapshotHash == null)
        {
            var existing = await _context.OrderRequestIdempotencyRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.TenantId == tenantId &&
                    r.Key == storageKey,
                    cancellationToken);

            if (existing == null || existing.IsDeleted)
            {
                return await TryRecoverCompletedByRequestHashAsync(
                    tenantId,
                    requestHash,
                    cancellationToken);
            }

            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new CheckoutIdempotencyClaimResult(CheckoutIdempotencyClaimStatus.NotFound);
            }

            if (existing.ResponseStatusCode == SuccessStatusCode &&
                TryDeserializeStoredResponse(existing.ResponsePayload, out var replayResponse))
            {
                _logger.LogInformation(
                    "Checkout idempotency fast-path replay | TenantId: {TenantId} | CheckoutId: {CheckoutId} | OrderId: {OrderId}",
                    tenantId, checkoutId, replayResponse!.OrderId);

                return new CheckoutIdempotencyClaimResult(
                    CheckoutIdempotencyClaimStatus.Replay,
                    ReplayResponse: replayResponse,
                    CheckoutId: ExtractCheckoutIdFromStorageKey(existing.Key));
            }

            if (existing.OrderId.HasValue)
            {
                _logger.LogInformation(
                    "Checkout idempotency fast-path existing order | TenantId: {TenantId} | CheckoutId: {CheckoutId} | OrderId: {OrderId}",
                    tenantId, checkoutId, existing.OrderId.Value);

                return new CheckoutIdempotencyClaimResult(
                    CheckoutIdempotencyClaimStatus.ExistingOrder,
                    ExistingOrderId: existing.OrderId.Value,
                    CheckoutId: ExtractCheckoutIdFromStorageKey(existing.Key));
            }

            // Record exists but checkout hasn't completed — caller should
            // proceed with the normal cart-snapshot + full-claim path.
            return new CheckoutIdempotencyClaimResult(CheckoutIdempotencyClaimStatus.NotFound);
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var claimId = Guid.NewGuid();
            var claimToken = Guid.NewGuid().ToString("N");
            var nowUtc = DateTime.UtcNow;
            var rowsInserted = await TryInsertClaimAsync(
                claimId,
                tenantId,
                storageKey,
                requestHash,
                snapshotHash,
                claimToken,
                nowUtc,
                cancellationToken);

            if (rowsInserted > 0)
            {
                var claimedRecord = await _context.OrderRequestIdempotencyRecords
                    .IgnoreQueryFilters()
                    .FirstAsync(record =>
                        record.TenantId == tenantId &&
                        record.Key == storageKey,
                        cancellationToken);

                _logger.LogInformation(
                    "Checkout idempotency claimed | TenantId: {TenantId} | CheckoutId: {CheckoutId}",
                    tenantId,
                    checkoutId);

                return new CheckoutIdempotencyClaimResult(
                    CheckoutIdempotencyClaimStatus.Claimed,
                    Record: claimedRecord);
            }

            var existingRecord = await _context.OrderRequestIdempotencyRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(record =>
                    record.TenantId == tenantId &&
                    record.Key == storageKey,
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

            if (string.IsNullOrWhiteSpace(existingRecord.SnapshotHash))
            {
                // MIGRATION SAFETY: a pre-migration row has no SnapshotHash.
                // Only reject it as a conflict when the record is still in-flight
                // (no stored response and no durable OrderId).  A completed legacy
                // record must be allowed to replay so that safe retries during
                // rollout are not broken — the Replay / ExistingOrder branches
                // below will handle it correctly.
                var legacyRecordIsCompleted =
                    (existingRecord.ResponseStatusCode == SuccessStatusCode &&
                     !string.IsNullOrWhiteSpace(existingRecord.ResponsePayload)) ||
                    existingRecord.OrderId.HasValue;

                if (!legacyRecordIsCompleted)
                {
                    return new CheckoutIdempotencyClaimResult(
                        CheckoutIdempotencyClaimStatus.PayloadConflict,
                        ConflictMessage: "Idempotency-Key was already used before cart snapshot validation was available. Regenerate the checkout key and retry.");
                }

                // Completed legacy record — fall through to the Replay /
                // ExistingOrder checks immediately below.
            }

            if (!string.Equals(existingRecord.SnapshotHash, snapshotHash, StringComparison.Ordinal))
            {
                return new CheckoutIdempotencyClaimResult(
                    CheckoutIdempotencyClaimStatus.PayloadConflict,
                    ConflictMessage: "Idempotency-Key was already used for a different cart snapshot. Regenerate the checkout key and retry.");
            }

            if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new CheckoutIdempotencyClaimResult(
                    CheckoutIdempotencyClaimStatus.PayloadConflict,
                    ConflictMessage: "Idempotency-Key was already used for a different checkout payload.");
            }

            if (existingRecord.ResponseStatusCode == SuccessStatusCode &&
                TryDeserializeStoredResponse(existingRecord.ResponsePayload, out var replayResponse))
            {
                _logger.LogInformation(
                    "Checkout idempotency replay | TenantId: {TenantId} | CheckoutId: {CheckoutId} | OrderId: {OrderId}",
                    tenantId,
                    checkoutId,
                    replayResponse!.OrderId);

                return new CheckoutIdempotencyClaimResult(
                    CheckoutIdempotencyClaimStatus.Replay,
                    ReplayResponse: replayResponse,
                    CheckoutId: checkoutId);
            }

            if (existingRecord.OrderId.HasValue)
            {
                _logger.LogWarning(
                    "Checkout idempotency recovering from durable order binding | TenantId: {TenantId} | CheckoutId: {CheckoutId} | OrderId: {OrderId}",
                    tenantId,
                    checkoutId,
                    existingRecord.OrderId.Value);

                return new CheckoutIdempotencyClaimResult(
                    CheckoutIdempotencyClaimStatus.ExistingOrder,
                    ExistingOrderId: existingRecord.OrderId.Value,
                    CheckoutId: checkoutId);
            }

            var claimAge = nowUtc - (existingRecord.ClaimTimestampUtc ?? existingRecord.CreatedAt);
            if (claimAge < _staleClaimWindow)
            {
                return new CheckoutIdempotencyClaimResult(CheckoutIdempotencyClaimStatus.InProgress);
            }

            var rowsDeleted = await DeleteStaleClaimAsync(
                tenantId,
                storageKey,
                nowUtc.Add(-_staleClaimWindow),
                cancellationToken);

            if (rowsDeleted > 0)
            {
                continue;
            }

            return new CheckoutIdempotencyClaimResult(CheckoutIdempotencyClaimStatus.InProgress);
        }

        return new CheckoutIdempotencyClaimResult(CheckoutIdempotencyClaimStatus.InProgress);
    }

    public async Task MarkOrderCreatedAsync(
        OrderRequestIdempotency record,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var currentRecord = await BindOrderCreatedForCommitAsync(record, orderId, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrderRequestIdempotency> BindOrderCreatedForCommitAsync(
        OrderRequestIdempotency record,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var currentRecord = await LoadOwnedRecordForMutationAsync(record, cancellationToken);

        if (currentRecord.OrderId != orderId || !currentRecord.CompletedAtUtc.HasValue)
        {
            var nowUtc = DateTime.UtcNow;
            currentRecord.OrderId = orderId;
            currentRecord.CompletedAtUtc ??= nowUtc;
            currentRecord.UpdatedAt = nowUtc;
        }

        SyncOwnedRecordState(record, currentRecord);

        return currentRecord;
    }

    public async Task CompleteAsync(
        OrderRequestIdempotency record,
        CheckoutResponseDto response,
        CancellationToken cancellationToken = default)
    {
        var currentRecord = await LoadOwnedRecordAsync(record, cancellationToken);

        var completedAtUtc = currentRecord.CompletedAtUtc ?? DateTime.UtcNow;

        currentRecord.OrderId = response.OrderId;
        currentRecord.ResponseStatusCode = SuccessStatusCode;
        currentRecord.ResponsePayload = JsonSerializer.Serialize(response, JsonOptions);
        currentRecord.CompletedAtUtc = completedAtUtc;
        currentRecord.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        record.OrderId = currentRecord.OrderId;
        record.ResponseStatusCode = currentRecord.ResponseStatusCode;
        record.ResponsePayload = currentRecord.ResponsePayload;
        record.CompletedAtUtc = currentRecord.CompletedAtUtc;
        record.UpdatedAt = currentRecord.UpdatedAt;
    }

    public async Task DeleteClaimAsync(
        OrderRequestIdempotency? record,
        CancellationToken cancellationToken = default)
    {
        if (record == null)
        {
            return;
        }

        var currentRecord = await _context.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == record.Id, cancellationToken);

        if (currentRecord == null)
        {
            return;
        }

        if (!string.Equals(currentRecord.ClaimToken, record.ClaimToken, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Skipping checkout idempotency cleanup for stolen claim | RecordId: {RecordId}",
                record.Id);
            return;
        }

        if (currentRecord.OrderId.HasValue || currentRecord.CompletedAtUtc.HasValue)
        {
            _logger.LogInformation(
                "Skipping checkout idempotency cleanup for durable completed record | RecordId: {RecordId} | OrderId: {OrderId}",
                currentRecord.Id,
                currentRecord.OrderId);
            return;
        }

        _context.OrderRequestIdempotencyRecords.Remove(currentRecord);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private bool IsPostgresProvider() =>
        _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<OrderRequestIdempotency> LoadOwnedRecordAsync(
        OrderRequestIdempotency record,
        CancellationToken cancellationToken)
    {
        var currentRecord = await _context.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == record.Id, cancellationToken);

        if (currentRecord == null || !string.Equals(currentRecord.ClaimToken, record.ClaimToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Checkout idempotency claim token validation failed.");
        }

        return currentRecord;
    }

    private async Task<OrderRequestIdempotency> LoadOwnedRecordForMutationAsync(
        OrderRequestIdempotency record,
        CancellationToken cancellationToken)
    {
        var trackedRecord = _context.ChangeTracker
            .Entries<OrderRequestIdempotency>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(entity => entity.Id == record.Id);

        if (trackedRecord != null)
        {
            if (!string.Equals(trackedRecord.ClaimToken, record.ClaimToken, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Checkout idempotency claim token validation failed.");
            }

            return trackedRecord;
        }

        return await LoadOwnedRecordAsync(record, cancellationToken);
    }

    private static void SyncOwnedRecordState(
        OrderRequestIdempotency target,
        OrderRequestIdempotency source)
    {
        target.OrderId = source.OrderId;
        target.CompletedAtUtc = source.CompletedAtUtc;
        target.UpdatedAt = source.UpdatedAt;
        target.ResponseStatusCode = source.ResponseStatusCode;
        target.ResponsePayload = source.ResponsePayload;
    }

    private async Task<int> TryInsertClaimAsync(
        Guid claimId,
        Guid tenantId,
        string storageKey,
        string requestHash,
        string snapshotHash,
        string claimToken,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""OrderRequestIdempotency""
                (""Id"", ""TenantId"", ""Key"", ""RequestHash"", ""SnapshotHash"", ""ClaimToken"", ""ClaimTimestampUtc"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                ({claimId}, {tenantId}, {storageKey}, {requestHash}, {snapshotHash}, {claimToken}, {nowUtc}, {nowUtc}, {nowUtc}, {false})
                ON CONFLICT (""TenantId"", ""Key"") DO NOTHING", cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [OrderRequestIdempotency]
                    ([Id], [TenantId], [Key], [RequestHash], [SnapshotHash], [ClaimToken], [ClaimTimestampUtc], [CreatedAt], [UpdatedAt], [IsDeleted])
                SELECT
                    {claimId}, {tenantId}, {storageKey}, {requestHash}, {snapshotHash}, {claimToken}, {nowUtc}, {nowUtc}, {nowUtc}, {false}
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [OrderRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Key] = {storageKey})", cancellationToken);
        }

        var existingTrackedClaim = _context.ChangeTracker
            .Entries<OrderRequestIdempotency>()
            .Any(entry =>
                entry.State != EntityState.Detached &&
                entry.State != EntityState.Deleted &&
                entry.Entity.TenantId == tenantId &&
                entry.Entity.Key == storageKey);

        if (existingTrackedClaim)
        {
            return 0;
        }

        var existingPersistedClaim = await _context.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(record =>
                record.TenantId == tenantId &&
                record.Key == storageKey,
                cancellationToken);

        if (existingPersistedClaim)
        {
            return 0;
        }

        var claim = new OrderRequestIdempotency
        {
            Id = claimId,
            TenantId = tenantId,
            Key = storageKey,
            RequestHash = requestHash,
            SnapshotHash = snapshotHash,
            ClaimToken = claimToken,
            ClaimTimestampUtc = nowUtc,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            IsDeleted = false
        };

        _context.OrderRequestIdempotencyRecords.Add(claim);

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

    private async Task<int> DeleteStaleClaimAsync(
        Guid tenantId,
        string storageKey,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM ""OrderRequestIdempotency""
                WHERE ""TenantId"" = {tenantId}
                  AND ""Key"" = {storageKey}
                  AND ""OrderId"" IS NULL
                  AND ""ResponseStatusCode"" IS NULL
                  AND ""CompletedAtUtc"" IS NULL
                  AND COALESCE(""ClaimTimestampUtc"", ""CreatedAt"") < {cutoffUtc}", cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM [OrderRequestIdempotency]
                WHERE [TenantId] = {tenantId}
                  AND [Key] = {storageKey}
                  AND [OrderId] IS NULL
                  AND [ResponseStatusCode] IS NULL
                  AND [CompletedAtUtc] IS NULL
                  AND COALESCE([ClaimTimestampUtc], [CreatedAt]) < {cutoffUtc}", cancellationToken);
        }

        var staleRecord = await _context.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(record =>
                record.TenantId == tenantId &&
                record.Key == storageKey &&
                record.OrderId == null &&
                record.ResponseStatusCode == null &&
                record.CompletedAtUtc == null &&
                (record.ClaimTimestampUtc ?? record.CreatedAt) < cutoffUtc,
                cancellationToken);

        if (staleRecord == null)
        {
            return 0;
        }

        _context.OrderRequestIdempotencyRecords.Remove(staleRecord);
        return await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> DeleteRecordByIdAsync(Guid recordId, CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM ""OrderRequestIdempotency""
                WHERE ""Id"" = {recordId}", cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM [OrderRequestIdempotency]
                WHERE [Id] = {recordId}", cancellationToken);
        }

        var record = await _context.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == recordId, cancellationToken);

        if (record == null)
        {
            return 0;
        }

        _context.OrderRequestIdempotencyRecords.Remove(record);
        return await _context.SaveChangesAsync(cancellationToken);
    }

    private static string BuildStorageKey(string checkoutId) =>
        $"storefront:checkout:{checkoutId}";

    private static string? ExtractCheckoutIdFromStorageKey(string storageKey)
    {
        const string prefix = "storefront:checkout:";
        return storageKey.StartsWith(prefix, StringComparison.Ordinal)
            ? storageKey[prefix.Length..]
            : null;
    }

    private async Task<CheckoutIdempotencyClaimResult> TryRecoverCompletedByRequestHashAsync(
        Guid tenantId,
        string requestHash,
        CancellationToken cancellationToken)
    {
        // First, check for any active in-progress claims with the same requestHash
        // to prevent duplicate live claims under different checkout IDs
        var activeClaim = await _context.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(record =>
                record.TenantId == tenantId &&
                !record.IsDeleted &&
                record.RequestHash == requestHash &&
                !record.OrderId.HasValue &&
                !record.ResponseStatusCode.HasValue)
            .OrderByDescending(record => record.ClaimTimestampUtc ?? record.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeClaim != null)
        {
            var activeCheckoutId = ExtractCheckoutIdFromStorageKey(activeClaim.Key);
            var claimAge = DateTime.UtcNow - (activeClaim.ClaimTimestampUtc ?? activeClaim.CreatedAt);

            // If the active claim is still within the stale window, treat it as in-progress
            if (claimAge < _staleClaimWindow)
            {
                _logger.LogInformation(
                    "Checkout idempotency found active claim by request hash | TenantId: {TenantId} | ActiveCheckoutId: {ActiveCheckoutId} | ClaimAge: {ClaimAge}",
                    tenantId,
                    activeCheckoutId ?? activeClaim.Key,
                    claimAge);

                return new CheckoutIdempotencyClaimResult(
                    CheckoutIdempotencyClaimStatus.InProgress,
                    CheckoutId: activeCheckoutId);
            }

            // If the claim is stale, log it but continue to check for completed records
            _logger.LogWarning(
                "Checkout idempotency found stale active claim by request hash | TenantId: {TenantId} | ActiveCheckoutId: {ActiveCheckoutId} | ClaimAge: {ClaimAge}",
                tenantId,
                activeCheckoutId ?? activeClaim.Key,
                claimAge);
        }

        // Now check for completed records
        var candidates = await _context.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(record =>
                record.TenantId == tenantId &&
                !record.IsDeleted &&
                record.RequestHash == requestHash &&
                (record.OrderId.HasValue || record.ResponseStatusCode == SuccessStatusCode))
            .OrderByDescending(record => record.CompletedAtUtc ?? record.UpdatedAt ?? record.CreatedAt)
            .Take(5)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var recoveredCheckoutId = ExtractCheckoutIdFromStorageKey(candidate.Key);

            if (candidate.ResponseStatusCode == SuccessStatusCode &&
                TryDeserializeStoredResponse(candidate.ResponsePayload, out var replayResponse))
            {
                _logger.LogInformation(
                    "Checkout idempotency pre-cart replay recovered by request hash | TenantId: {TenantId} | CheckoutId: {CheckoutId} | OrderId: {OrderId}",
                    tenantId,
                    recoveredCheckoutId ?? candidate.Key,
                    replayResponse!.OrderId);

                return new CheckoutIdempotencyClaimResult(
                    CheckoutIdempotencyClaimStatus.Replay,
                    ReplayResponse: replayResponse,
                    CheckoutId: recoveredCheckoutId);
            }

            if (candidate.OrderId.HasValue)
            {
                _logger.LogInformation(
                    "Checkout idempotency pre-cart existing order recovered by request hash | TenantId: {TenantId} | CheckoutId: {CheckoutId} | OrderId: {OrderId}",
                    tenantId,
                    recoveredCheckoutId ?? candidate.Key,
                    candidate.OrderId.Value);

                return new CheckoutIdempotencyClaimResult(
                    CheckoutIdempotencyClaimStatus.ExistingOrder,
                    ExistingOrderId: candidate.OrderId.Value,
                    CheckoutId: recoveredCheckoutId);
            }
        }

        return new CheckoutIdempotencyClaimResult(CheckoutIdempotencyClaimStatus.NotFound);
    }

    private static bool TryDeserializeStoredResponse(string? payload, out CheckoutResponseDto? response)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            response = null;
            return false;
        }

        try
        {
            response = JsonSerializer.Deserialize<CheckoutResponseDto>(payload, JsonOptions);
            return response != null;
        }
        catch (JsonException)
        {
            response = null;
            return false;
        }
    }
}
