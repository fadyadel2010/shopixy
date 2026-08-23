using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Bulk;

namespace PointOfSale.API.Services.ProductAuthoring.Bulk;

public sealed class BulkIdempotencyService : IBulkIdempotencyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string InterruptedClaimState = "{\"state\":\"interrupted\",\"reason\":\"request_cancelled\"}";
    public const int MaxIdempotencyKeyLength = 200;

    /// <summary>
    /// Storage scope used by the product authoring boundary for per-product
    /// mutation idempotency records. Bulk item reconciliation derives the exact
    /// same scoped storage key the orchestrator normalizes commands to.
    /// </summary>
    public const string AuthoringScope = "authoring";

    /// <summary>
    /// Storage identity of the single outer bulk claim. Every bulk lifecycle
    /// operation claims the same (tenant, scoped key, this operation) row so the
    /// existing unique index makes the claim authoritative across all five
    /// operations, including concurrent attempts. The concrete operation remains
    /// encoded in the request fingerprint.
    /// </summary>
    public const string BulkScope = "bulk";

    public static readonly TimeSpan DefaultClaimLeaseDuration = TimeSpan.FromSeconds(90);

    private readonly IProductMutationIdempotencyService _inner;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public BulkIdempotencyService(
        IProductMutationIdempotencyService inner,
        ITenantContext tenantContext,
        TimeProvider? timeProvider = null)
    {
        _inner = inner;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public TimeSpan ClaimLeaseDuration => DefaultClaimLeaseDuration;

    public string OuterClaimOperation => OuterClaimStorageOperation;

    /// <summary>Canonical server-side bulk operation names used in fingerprints and reporting.</summary>
    public static string GetCanonicalOperationName(BulkOperationType operation) =>
        operation switch
        {
            BulkOperationType.Activate => "BulkProductActivate",
            BulkOperationType.Deactivate => "BulkProductDeactivate",
            BulkOperationType.Publish => "BulkProductPublish",
            BulkOperationType.Unpublish => "BulkProductUnpublish",
            BulkOperationType.Delete => "BulkProductDelete",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported bulk operation.")
        };

    internal static string OuterClaimStorageOperation => "BulkProduct";

    public BulkIdempotencyKeyValidationResult ValidateKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BulkIdempotencyKeyValidationResult.Invalid(
                BulkErrorCodes.IdempotencyKeyRequired,
                "Idempotency-Key is required for bulk operations.");
        }

        if (idempotencyKey.Trim().Length > MaxIdempotencyKeyLength)
        {
            return BulkIdempotencyKeyValidationResult.Invalid(
                BulkErrorCodes.IdempotencyKeyTooLong,
                $"Idempotency-Key must not exceed {MaxIdempotencyKeyLength} characters.");
        }

        return BulkIdempotencyKeyValidationResult.Valid();
    }

    public string BuildFingerprint(
        BulkOperationType operation,
        IReadOnlyList<NormalizedBulkProductItem> normalizedItems,
        IReadOnlyDictionary<string, string?>? relevantOptions = null)
    {
        var operationName = GetCanonicalOperationName(operation);
        ArgumentNullException.ThrowIfNull(normalizedItems);

        var distinctItems = new Dictionary<Guid, uint>();
        foreach (var item in normalizedItems)
        {
            if (item.ProductId == Guid.Empty)
            {
                throw new ArgumentException("Normalized bulk items cannot contain an empty product ID.", nameof(normalizedItems));
            }

            if (distinctItems.TryGetValue(item.ProductId, out var existingVersion) &&
                existingVersion != item.ExpectedVersion)
            {
                throw new ArgumentException(
                    "Normalized bulk items cannot contain conflicting expected versions.",
                    nameof(normalizedItems));
            }

            distinctItems[item.ProductId] = item.ExpectedVersion;
        }

        var sortedItems = distinctItems
            .OrderBy(item => item.Key)
            .Select(item => new BulkFingerprintItem(item.Key.ToString("N"), item.Value))
            .ToArray();

        var sortedOptions = relevantOptions?
            .OrderBy(option => option.Key, StringComparer.Ordinal)
            .ToDictionary(option => option.Key, option => option.Value, StringComparer.Ordinal);

        return DeterministicRequestHasher.ComputeHash(
            new BulkFingerprintPayload(operationName, sortedItems, sortedOptions));
    }

    /// <summary>
    /// Deterministic per-product mutation identity for one item of a bulk
    /// execution: fingerprinted by operation, outer idempotency key, and the
    /// product identity, hashed so no tenant data appears in storage keys.
    /// Command building and recovery reconciliation must both use this exact
    /// derivation.
    /// </summary>
    public string BuildPerProductIdempotencyKey(
        BulkOperationType operation,
        string idempotencyKey,
        Guid productId)
    {
        var value = $"{operation}:{idempotencyKey.Trim()}:{productId:N}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
        return $"bulk-item:{hash}";
    }

    public async Task<ProductAuthoringResult?> TryResolveCommittedBulkItemMutationAsync(
        BulkOperationType operation,
        string idempotencyKey,
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty ||
            productId == Guid.Empty ||
            string.IsNullOrWhiteSpace(idempotencyKey) ||
            operation is not (
                BulkOperationType.Activate or
                BulkOperationType.Deactivate or
                BulkOperationType.Publish or
                BulkOperationType.Unpublish or
                BulkOperationType.Delete))
        {
            return null;
        }

        var storageKey = _inner.ScopeKey(
            AuthoringScope,
            BuildPerProductIdempotencyKey(operation, idempotencyKey, productId));
        var storageOperation = AuthoringStorageOperation(
            BulkItemAuthoringOperation(operation));

        var record = await _inner.TryResolveAsync(
            tenantId,
            storageKey,
            storageOperation,
            cancellationToken);

        // Fail closed: only an exact tenant/identity-matched record proves a
        // committed mutation for this deterministic bulk item identity.
        if (record is null ||
            record.IsDeleted ||
            record.TenantId != tenantId ||
            record.ProductId != productId)
        {
            return null;
        }

        // Only a durably completed accepted mutation proves the product change
        // committed. Missing, incomplete, rejected, or conflicting records are
        // not proof; current validation semantics stay authoritative there.
        if (record.ResponseStatusCode != StatusCodes.Status202Accepted ||
            !record.CompletedAtUtc.HasValue ||
            string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return null;
        }

        ProductAuthoringResult? storedResult;
        try
        {
            storedResult = JsonSerializer.Deserialize<ProductAuthoringResult>(
                record.ResponseJson,
                JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (storedResult is null ||
            storedResult.Status != ProductAuthoringStatus.Accepted ||
            storedResult.ProductId is null ||
            storedResult.ProductId.Value != productId)
        {
            return null;
        }

        return storedResult;
    }

    /// <summary>
    /// Authoring operation each bulk lifecycle operation delegates to, matching
    /// the command mapping used when the item mutation is executed.
    /// </summary>
    public static ProductAuthoringOperation BulkItemAuthoringOperation(BulkOperationType operation) =>
        operation switch
        {
            BulkOperationType.Activate => ProductAuthoringOperation.Update,
            BulkOperationType.Deactivate => ProductAuthoringOperation.Archive,
            BulkOperationType.Publish => ProductAuthoringOperation.Update,
            BulkOperationType.Unpublish => ProductAuthoringOperation.Update,
            BulkOperationType.Delete => ProductAuthoringOperation.Remove,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported bulk operation.")
        };

    private static string AuthoringStorageOperation(ProductAuthoringOperation operation) =>
        operation switch
        {
            ProductAuthoringOperation.Create => "AuthoringCreate",
            ProductAuthoringOperation.Update => "AuthoringUpdate",
            ProductAuthoringOperation.Archive => "AuthoringArchive",
            ProductAuthoringOperation.Remove => "AuthoringRemove",
            _ => "AuthoringUnknown"
        };

    public async Task<BulkIdempotencyClaim> TryClaimAsync(
        BulkOperationType operation,
        string idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = RequireValidKey(idempotencyKey);
        var tenantId = _tenantContext.RequiredTenantId;
        var scopedKey = _inner.ScopeKey(BulkScope, normalizedKey);

        var claim = await ClaimOuterAsync(tenantId, scopedKey, fingerprint, cancellationToken);
        if (claim.Replay?.Kind != BulkIdempotencyReplayKind.InProgress || claim.Replay.Record is null)
        {
            return claim;
        }

        // The key is already claimed by an in-progress identical request. A live
        // claim stays fenced by its lease; an abandoned claim becomes recoverable
        // once its lease expires. Takeover is a single atomic conditional update,
        // so exactly one caller can recover at a time.
        var recovery = await _inner.TryRecoverAbandonedClaimAsync(
            new ProductMutationIdempotencyRequest(
                tenantId,
                scopedKey,
                OuterClaimStorageOperation,
                Guid.Empty,
                null,
                fingerprint),
            ClaimLeaseDuration,
            UtcNow,
            cancellationToken);

        if (recovery.Kind == ProductMutationIdempotencyRecoveryKind.Recovered &&
            recovery.Record is not null)
        {
            return new BulkIdempotencyClaim(recovery.Record, null, Recovered: true);
        }

        // Someone else recovered/completed between the claim read and the
        // takeover attempt; re-read so completed work still replays exactly.
        var freshRecord = await ResolveStoredRecordAsync(tenantId, scopedKey, cancellationToken);
        if (freshRecord is null)
        {
            return claim;
        }

        if (!string.Equals(freshRecord.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new BulkIdempotencyClaim(
                null,
                new BulkIdempotencyReplay(BulkIdempotencyReplayKind.Conflict, freshRecord));
        }

        return new BulkIdempotencyClaim(
            null,
            new BulkIdempotencyReplay(MapResolutionKind(freshRecord), freshRecord));
    }

    public async Task<BulkIdempotencyResolution> TryResolveAsync(
        BulkOperationType operation,
        string idempotencyKey,
        string expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = RequireValidKey(idempotencyKey);
        var tenantId = _tenantContext.RequiredTenantId;
        var scopedKey = _inner.ScopeKey(BulkScope, normalizedKey);

        var existingRecord = await ResolveStoredRecordAsync(tenantId, scopedKey, cancellationToken);

        if (existingRecord is null)
        {
            return new BulkIdempotencyResolution(BulkIdempotencyReplayKind.NotFound, null);
        }

        if (!string.Equals(existingRecord.RequestFingerprint, expectedFingerprint, StringComparison.Ordinal))
        {
            return new BulkIdempotencyResolution(BulkIdempotencyReplayKind.Conflict, existingRecord);
        }

        return new BulkIdempotencyResolution(MapResolutionKind(existingRecord), existingRecord);
    }

    public void Complete(
        ProductRequestIdempotency? record,
        int statusCode,
        object? responsePayload)
    {
        _inner.Complete(record, statusCode, responsePayload, Guid.Empty, null);
    }

    public BulkOperationResult? ReadStoredResponse(ProductRequestIdempotency? record)
    {
        if (string.IsNullOrWhiteSpace(record?.ResponseJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BulkOperationResult>(record.ResponseJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string? ReadStoredFailureMessage(ProductRequestIdempotency? record)
    {
        if (record?.ResponseStatusCode is null or < 400 ||
            string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(record.ResponseJson);
            if (payload.RootElement.ValueKind != JsonValueKind.Object ||
                !payload.RootElement.TryGetProperty("message", out var messageElement) ||
                messageElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var message = messageElement.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(message) ? null : message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool StoredResponseMatches(ProductRequestIdempotency? record, BulkOperationResult aggregate)
    {
        if (record is null ||
            record.ResponseStatusCode != 200 ||
            string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return false;
        }

        var stored = ReadStoredResponse(record);
        if (stored is null)
        {
            return false;
        }

        return string.Equals(
            JsonSerializer.Serialize(stored, JsonOptions),
            JsonSerializer.Serialize(aggregate, JsonOptions),
            StringComparison.Ordinal);
    }

    public bool IsInterruptedClaim(ProductRequestIdempotency? record) =>
        record is
        {
            ResponseStatusCode: null,
            ResponseJson: InterruptedClaimState
        };

    public Task<bool> TryMarkInterruptedAsync(
        ProductRequestIdempotency record,
        CancellationToken cancellationToken = default) =>
        _inner.TryWriteIncompleteClaimStateAsync(
            record,
            InterruptedClaimState,
            cancellationToken);

    public Task<bool> RenewClaimLeaseAsync(
        ProductRequestIdempotency record,
        CancellationToken cancellationToken = default) =>
        _inner.TryRenewClaimLeaseAsync(record, UtcNow, cancellationToken);

    public async Task<BulkOperationResult?> TryCompleteAndVerifyAsync(
        ProductRequestIdempotency record,
        BulkOperationResult aggregate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        var completion = await _inner.TryCompleteDurablyAsync(
            record,
            StatusCodes.Status200OK,
            aggregate,
            UtcNow,
            cancellationToken);

        if (completion.Kind == ProductMutationIdempotencyCompletionKind.LostOwnership)
        {
            return null;
        }

        // Success may only be returned after a fresh database read proves that
        // the durable completion status plus the exact replay payload are stored.
        var fresh = completion.Record;
        return StoredResponseMatches(fresh, aggregate) ? ReadStoredResponse(fresh) : null;
    }

    public async Task<bool> FinalizeFailureAsync(
        ProductRequestIdempotency record,
        string message,
        CancellationToken cancellationToken = default)
    {
        var completion = await _inner.TryCompleteDurablyAsync(
            record,
            StatusCodes.Status500InternalServerError,
            new { message },
            UtcNow,
            cancellationToken);

        return completion.Kind != ProductMutationIdempotencyCompletionKind.LostOwnership;
    }

    private async Task<BulkIdempotencyClaim> ClaimOuterAsync(
        Guid tenantId,
        string scopedKey,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var claim = await _inner.TryClaimAsync(
            new ProductMutationIdempotencyRequest(
                tenantId,
                scopedKey,
                OuterClaimStorageOperation,
                Guid.Empty,
                null,
                fingerprint),
            cancellationToken);

        var replay = claim.Replay is null
            ? null
            : new BulkIdempotencyReplay(MapReplayKind(claim.Replay.Kind), claim.Replay.Record);

        return new BulkIdempotencyClaim(claim.Record, replay);
    }

    private async Task<ProductRequestIdempotency?> ResolveStoredRecordAsync(
        Guid tenantId,
        string scopedKey,
        CancellationToken cancellationToken) =>
        await _inner.TryResolveAsync(tenantId, scopedKey, OuterClaimStorageOperation, cancellationToken);

    private static BulkIdempotencyReplayKind MapResolutionKind(ProductRequestIdempotency record) =>
        record.ResponseStatusCode.HasValue
            ? BulkIdempotencyReplayKind.Completed
            : BulkIdempotencyReplayKind.InProgress;

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private string RequireValidKey(string idempotencyKey)
    {
        var validation = ValidateKey(idempotencyKey);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"{validation.ErrorCode}: {validation.Message}", nameof(idempotencyKey));
        }

        return idempotencyKey.Trim();
    }

    private static BulkIdempotencyReplayKind MapReplayKind(ProductMutationIdempotencyReplayKind kind) =>
        kind switch
        {
            ProductMutationIdempotencyReplayKind.Completed => BulkIdempotencyReplayKind.Completed,
            ProductMutationIdempotencyReplayKind.Conflict => BulkIdempotencyReplayKind.Conflict,
            ProductMutationIdempotencyReplayKind.InProgress => BulkIdempotencyReplayKind.InProgress,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported idempotency replay kind.")
        };

    private sealed record BulkFingerprintPayload(
        string Operation,
        IReadOnlyList<BulkFingerprintItem> Items,
        IReadOnlyDictionary<string, string?>? Options);

    private sealed record BulkFingerprintItem(string ProductId, uint ExpectedVersion);
}
