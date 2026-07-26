using Elasticsearch.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nest;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Operations;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

public interface ICommerceHubSearchIndexService
{
    Task<bool> EnsureIndexExistsAsync(CancellationToken cancellationToken = default);
    Task<bool> IndexAsync(
        CommerceHubProductSearchDocument document,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(
        Guid listingId,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(
        Guid tenantId,
        Guid listingId,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default);
    Task<CommerceHubSearchIndexStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<CommerceHubSearchIndexVerification> VerifyMigrationAsync(
        string smokeTestQuery = "samsung",
        CancellationToken cancellationToken = default);
    Task<CommerceHubSearchRebuildTarget> EnsureRebuildTargetAsync(CancellationToken cancellationToken = default);
    Task<bool> PromoteIndexAsync(string targetIndexName, CancellationToken cancellationToken = default);
    Task<CommerceHubSearchAliasMigrationResult> FinalizeAliasMigrationAsync(
        string smokeTestQuery = "samsung",
        CancellationToken cancellationToken = default);
    Task<HashSet<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> listingIds,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default);
    Task<HashSet<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<CommerceHubSearchDocumentIdentity> identities,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default);
    Task<long> DeleteStaleAsync(
        IReadOnlyCollection<CommerceHubSearchDocumentIdentity> liveIdentities,
        Guid? tenantId = null,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default);
    Task<long?> CountAsync(string? targetIndexName = null, CancellationToken cancellationToken = default);
}

public class CommerceHubSearchIndexService : ICommerceHubSearchIndexService
{
    private const string StrictMappingErrorMarker = "mapping set to strict";
    private const int ExistingIdsLookupBatchSize = 500;
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3)
    };
    private static readonly Regex StrictMappingFieldRegex = new(
        @"dynamic introduction of \[(?<field>[^\]]+)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IElasticClient _client;
    private readonly ElasticsearchSettings _settings;
    private readonly ILogger<CommerceHubSearchIndexService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _indexInitializationLock = new(1, 1);
    private volatile bool _indexInitialized;

    public CommerceHubSearchIndexService(
        IElasticClient client,
        IOptions<ElasticsearchSettings> settings,
        IServiceScopeFactory scopeFactory,
        ILogger<CommerceHubSearchIndexService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<bool> EnsureIndexExistsAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return true;
        }

        var status = await GetStatusAsync(cancellationToken);
        if (status.AliasExists || status.LegacyConcreteIndexExists)
        {
            if (status.LegacyConcreteIndexExists && !status.AliasExists)
            {
                _logger.LogWarning(
                    "CommerceHub Elasticsearch is running in legacy concrete-index mode | Index: {Index} | DesiredIndex: {DesiredIndex} | ReindexRequired: {RequiresReindex}",
                    status.ActiveIndexName,
                    status.DesiredIndexName,
                    status.RequiresReindex);
            }

            return await EnsureMappingAsync(
                status.WriteIndexName ?? status.ActiveIndexName ?? _settings.CommerceHubProductsIndexName,
                cancellationToken);
        }

        if (!await CreateVersionedIndexAsync(status.DesiredIndexName, cancellationToken))
        {
            return false;
        }

        var aliasAttached = await PromoteIndexAsync(status.DesiredIndexName, cancellationToken);
        if (aliasAttached)
        {
            await RecordSchemaVersionAsync(
                $"Elasticsearch:{_settings.CommerceHubProductsIndexName}",
                status.DesiredIndexName,
                new
                {
                    mode = "alias",
                    version = _settings.CommerceHubProductsIndexVersion
                },
                cancellationToken);
        }

        return aliasAttached;
    }

    public async Task<bool> IndexAsync(
        CommerceHubProductSearchDocument document,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return true;
        }

        try
        {
            var resolvedIndexName = await ResolveWriteIndexAsync(targetIndexName, cancellationToken);
            if (string.IsNullOrWhiteSpace(resolvedIndexName))
            {
                return false;
            }

            // Always use the low-level PUT API with explicit field construction.
            // NEST _client.IndexAsync is intentionally avoided because:
            // 1. Legacy index may lack new fields (rating, reviewCount)
            // 2. Versioned index may have been created in a prior session with an old mapping
            // 3. The self-healing loop handles any strict mapping rejection gracefully
            return await IndexLegacyDirectAsync(document, resolvedIndexName, Array.Empty<string>(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "CommerceHub Elasticsearch index sync failed | Action: Index | ListingId: {ListingId} | StoreId: {StoreId} | ProductId: {ProductId} | VariantId: {VariantId} | Published: {IsPublished}",
                document.Id,
                document.StoreId,
                document.ProductId,
                document.VariantId,
                document.IsPublished);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(
        Guid listingId,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default)
    {
        return await DeleteByDocumentIdsAsync(
            [listingId.ToString("N")],
            listingId,
            targetIndexName,
            cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        Guid tenantId,
        Guid listingId,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default)
    {
        var identity = new CommerceHubSearchDocumentIdentity(tenantId, listingId);
        return await DeleteByDocumentIdsAsync(
            [identity.GetDocumentId(), identity.GetLegacyDocumentId()],
            listingId,
            targetIndexName,
            cancellationToken);
    }

    private async Task<bool> DeleteByDocumentIdsAsync(
        IReadOnlyCollection<string> documentIds,
        Guid listingId,
        string? targetIndexName,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return true;
        }

        try
        {
            var resolvedIndexName = await ResolveWriteIndexAsync(targetIndexName, cancellationToken);
            if (string.IsNullOrWhiteSpace(resolvedIndexName))
            {
                return false;
            }

            foreach (var documentId in documentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
            {
                await ExecuteWithRetryAsync(
                    "delete",
                    async ct =>
                    {
                        var response = await _client.DeleteAsync<CommerceHubProductSearchDocument>(
                            documentId,
                            d => d.Index(resolvedIndexName),
                            ct);

                        if (!response.IsValid && response.Result != Result.NotFound)
                        {
                            throw BuildElasticsearchException(
                                "Delete",
                                response.ServerError?.Error?.Reason ?? response.OriginalException?.Message ?? "Unknown error");
                        }
                    },
                    cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "CommerceHub Elasticsearch index sync failed | Action: Delete | ListingId: {ListingId}",
                listingId);
            return false;
        }
    }

    public async Task<HashSet<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> listingIds,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedIds = listingIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (normalizedIds.Length == 0)
        {
            return new HashSet<Guid>();
        }

        return await GetExistingIdsByDocumentIdsAsync(
            normalizedIds.Select(id => (ListingId: id, DocumentId: id.ToString("N"))).ToArray(),
            targetIndexName,
            cancellationToken);
    }

    public async Task<HashSet<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<CommerceHubSearchDocumentIdentity> identities,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = identities
            .Where(identity => identity.TenantId != Guid.Empty && identity.ListingId != Guid.Empty)
            .GroupBy(identity => identity.GetDocumentId(), StringComparer.Ordinal)
            .Select(group => group.First())
            .SelectMany(identity => new[]
            {
                (identity.ListingId, DocumentId: identity.GetDocumentId()),
                (identity.ListingId, DocumentId: identity.GetLegacyDocumentId())
            })
            .ToArray();

        if (normalized.Length == 0)
        {
            return new HashSet<Guid>();
        }

        return await GetExistingIdsByDocumentIdsAsync(
            normalized,
            targetIndexName,
            cancellationToken);
    }

    private async Task<HashSet<Guid>> GetExistingIdsByDocumentIdsAsync(
        IReadOnlyCollection<(Guid ListingId, string DocumentId)> documentIds,
        string? targetIndexName,
        CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        if (!_settings.Enabled)
        {
            return documentIds.Select(item => item.ListingId).ToHashSet();
        }

        var resolvedIndexName = await ResolveLookupIndexAsync(targetIndexName, cancellationToken);
        if (string.IsNullOrWhiteSpace(resolvedIndexName))
        {
            return new HashSet<Guid>();
        }

        var existingIds = new HashSet<Guid>();
        foreach (var batch in documentIds.Chunk(ExistingIdsLookupBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lookup = batch.ToDictionary(item => item.DocumentId, item => item.ListingId, StringComparer.Ordinal);
            var response = await _client.LowLevel.DoRequestAsync<StringResponse>(
                global::Elasticsearch.Net.HttpMethod.POST,
                $"{resolvedIndexName}/_mget",
                cancellationToken,
                PostData.Serializable(new
                {
                    docs = batch.Select(item => new
                    {
                        _id = item.DocumentId
                    }).ToArray()
                }));

            if (!response.Success || string.IsNullOrWhiteSpace(response.Body))
            {
                throw BuildElasticsearchException(
                    "GetExistingIds",
                    response.Body ?? "Empty _mget response.");
            }

            using var payload = JsonDocument.Parse(response.Body);
            if (!payload.RootElement.TryGetProperty("docs", out var docsElement) ||
                docsElement.ValueKind != JsonValueKind.Array)
            {
                throw BuildElasticsearchException(
                    "GetExistingIds",
                    "Unexpected _mget response shape.");
            }

            foreach (var document in docsElement.EnumerateArray())
            {
                var found = document.TryGetProperty("found", out var foundElement) &&
                            foundElement.ValueKind == JsonValueKind.True;
                if (!found ||
                    !document.TryGetProperty("_id", out var idElement) ||
                    idElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var rawId = idElement.GetString();
                if (rawId != null && lookup.TryGetValue(rawId, out var listingId))
                {
                    existingIds.Add(listingId);
                }
            }
        }

        return existingIds;
    }

    public async Task<long> DeleteStaleAsync(
        IReadOnlyCollection<CommerceHubSearchDocumentIdentity> liveIdentities,
        Guid? tenantId = null,
        string? targetIndexName = null,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return 0;
        }

        var resolvedIndexName = await ResolveLookupIndexAsync(targetIndexName, cancellationToken);
        if (string.IsNullOrWhiteSpace(resolvedIndexName))
        {
            return 0;
        }

        var liveDocumentIds = liveIdentities
            .Where(identity => identity.TenantId != Guid.Empty && identity.ListingId != Guid.Empty)
            .SelectMany(identity => new[] { identity.GetDocumentId(), identity.GetLegacyDocumentId() })
            .ToHashSet(StringComparer.Ordinal);

        var staleDocumentIds = new List<string>();
        const string scrollTimeout = "1m";

        var searchResponse = await _client.SearchAsync<CommerceHubProductSearchDocument>(
            s =>
            {
                var descriptor = s
                    .Index(resolvedIndexName)
                    .Size(1000)
                    .Source(sf => sf.Includes(i => i
                        .Field(f => f.Id)
                        .Field(f => f.TenantId)))
                    .Scroll(scrollTimeout);

                return tenantId.HasValue
                    ? descriptor.Query(q => q.Term(t => t.Field(f => f.TenantId).Value(tenantId.Value)))
                    : descriptor.MatchAll();
            },
            cancellationToken);

        if (!searchResponse.IsValid)
        {
            throw BuildElasticsearchException(
                "DeleteStale",
                searchResponse.ServerError?.Error?.Reason ??
                searchResponse.OriginalException?.Message ??
                "Failed to scan CommerceHub index for stale documents.");
        }

        string? scrollId = searchResponse.ScrollId;
        try
        {
            while (true)
            {
                CollectStaleDocumentIds(searchResponse.Hits, liveDocumentIds, staleDocumentIds);

                if (!searchResponse.Documents.Any())
                {
                    break;
                }

                searchResponse = await _client.ScrollAsync<CommerceHubProductSearchDocument>(
                    scrollTimeout,
                    scrollId,
                    ct: cancellationToken);

                if (!searchResponse.IsValid)
                {
                    throw BuildElasticsearchException(
                        "DeleteStale",
                        searchResponse.ServerError?.Error?.Reason ??
                        searchResponse.OriginalException?.Message ??
                        "Failed to continue CommerceHub stale-document scan.");
                }

                scrollId = searchResponse.ScrollId;
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(scrollId))
            {
                await _client.ClearScrollAsync(c => c.ScrollId(scrollId), cancellationToken);
            }
        }

        if (staleDocumentIds.Count == 0)
        {
            return 0;
        }

        var deleted = 0L;
        foreach (var documentId in staleDocumentIds.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await _client.DeleteAsync<object>(
                documentId,
                d => d.Index(resolvedIndexName),
                cancellationToken);

            if (!response.IsValid && response.Result != Result.NotFound)
            {
                throw BuildElasticsearchException(
                    "DeleteStale",
                    response.ServerError?.Error?.Reason ??
                    response.OriginalException?.Message ??
                    $"Failed to delete stale CommerceHub search document {documentId}.");
            }

            deleted++;
        }

        _logger.LogWarning(
            "Deleted stale CommerceHub Elasticsearch documents from SQL-authoritative reindex | Index: {Index} | TenantId: {TenantId} | Deleted: {Deleted}",
            resolvedIndexName,
            tenantId,
            deleted);

        return deleted;
    }

    private static void CollectStaleDocumentIds(
        IReadOnlyCollection<IHit<CommerceHubProductSearchDocument>> hits,
        IReadOnlySet<string> liveDocumentIds,
        ICollection<string> staleDocumentIds)
    {
        foreach (var hit in hits)
        {
            if (string.IsNullOrWhiteSpace(hit.Id))
            {
                continue;
            }

            if (!liveDocumentIds.Contains(hit.Id))
            {
                staleDocumentIds.Add(hit.Id);
            }
        }
    }

    public async Task<CommerceHubSearchIndexStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var aliasName = _settings.CommerceHubProductsIndexName;
        var desiredIndexName = BuildVersionedIndexName(_settings.CommerceHubProductsIndexVersion);
        var legacyConcreteExists = (await _client.Indices.ExistsAsync(aliasName, ct: cancellationToken)).Exists;
        var desiredIndexExists = (await _client.Indices.ExistsAsync(desiredIndexName, ct: cancellationToken)).Exists;

        var aliasMap = await LoadAliasMapAsync(aliasName, cancellationToken);
        var aliasExists = aliasMap.Count > 0;
        var writeIndexEntry = aliasMap.FirstOrDefault(pair => pair.Value);
        var writeIndexName = string.IsNullOrWhiteSpace(writeIndexEntry.Key) ? null : writeIndexEntry.Key;
        var activeIndexName = writeIndexName ?? aliasMap.Keys.FirstOrDefault();

        if (!aliasExists && legacyConcreteExists)
        {
            activeIndexName = aliasName;
            writeIndexName = aliasName;
        }

        var missingFields = string.IsNullOrWhiteSpace(activeIndexName)
            ? Array.Empty<string>()
            : await GetMissingFieldsAsync(activeIndexName, cancellationToken);

        return new CommerceHubSearchIndexStatus
        {
            AliasName = aliasName,
            DesiredIndexName = desiredIndexName,
            DesiredVersion = _settings.CommerceHubProductsIndexVersion,
            AliasExists = aliasExists,
            LegacyConcreteIndexExists = legacyConcreteExists && !aliasExists,
            DesiredIndexExists = desiredIndexExists,
            PromotionSupported = aliasExists || !legacyConcreteExists,
            ActiveIndexName = activeIndexName,
            WriteIndexName = writeIndexName,
            ActiveVersion = ParseVersion(activeIndexName),
            MissingFields = missingFields
        };
    }

    public async Task<CommerceHubSearchIndexVerification> VerifyMigrationAsync(
        string smokeTestQuery = "samsung",
        CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        var aliasIndices = await GetAliasIndexNamesAsync(status.AliasName, cancellationToken);

        var desiredCount = status.DesiredIndexExists
            ? await CountAsync(status.DesiredIndexName, cancellationToken)
            : 0;
        var activeCount = string.IsNullOrWhiteSpace(status.ActiveIndexName)
            ? 0
            : await CountAsync(status.ActiveIndexName, cancellationToken);

        var desiredMappingDiagnostics = status.DesiredIndexExists
            ? await GetMappingDiagnosticsAsync(status.DesiredIndexName, cancellationToken)
            : (MissingFields: RequiredCompatibilityFields.ToList() as IReadOnlyCollection<string>, HasAttributeTemplates: false);

        var smokeTest = await RunSmokeSearchAsync(status.AliasName, smokeTestQuery, cancellationToken);

        return new CommerceHubSearchIndexVerification
        {
            AliasName = status.AliasName,
            DesiredIndexName = status.DesiredIndexName,
            ActiveIndexName = status.ActiveIndexName,
            WriteIndexName = status.WriteIndexName,
            AliasExists = status.AliasExists,
            AliasIndices = aliasIndices,
            LegacyConcreteIndexExists = status.LegacyConcreteIndexExists,
            DesiredIndexExists = status.DesiredIndexExists,
            DesiredDocumentCount = desiredCount,
            ActiveDocumentCount = activeCount,
            DesiredIndexMissingFields = desiredMappingDiagnostics.MissingFields,
            DesiredIndexHasAttributeTemplates = desiredMappingDiagnostics.HasAttributeTemplates,
            SmokeTestQuery = string.IsNullOrWhiteSpace(smokeTestQuery) ? "samsung" : smokeTestQuery.Trim(),
            SmokeTestPassed = smokeTest.Passed,
            SmokeTestHitCount = smokeTest.HitCount
        };
    }

    public async Task<CommerceHubSearchRebuildTarget> EnsureRebuildTargetAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        var targetIndexName = status.DesiredIndexName;

        if (!status.DesiredIndexExists)
        {
            await CreateVersionedIndexAsync(targetIndexName, cancellationToken);
        }

        return new CommerceHubSearchRebuildTarget
        {
            IndexName = targetIndexName,
            PromotionSupported = status.PromotionSupported,
            IsDesiredVersionTarget = true,
            RequiresPromotion = !status.IsDesiredVersionActive
        };
    }

    public async Task<bool> PromoteIndexAsync(string targetIndexName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetIndexName))
        {
            return false;
        }

        var status = await GetStatusAsync(cancellationToken);
        if (status.LegacyConcreteIndexExists && !status.AliasExists)
        {
            _logger.LogWarning(
                "CommerceHub Elasticsearch alias promotion blocked by legacy concrete index | Alias: {Alias} | LegacyIndex: {LegacyIndex} | TargetIndex: {TargetIndex}",
                status.AliasName,
                status.ActiveIndexName,
                targetIndexName);
            return false;
        }

        var actions = new List<object>();
        foreach (var existingIndex in await GetAliasIndexNamesAsync(status.AliasName, cancellationToken))
        {
            actions.Add(new
            {
                remove = new
                {
                    index = existingIndex,
                    alias = status.AliasName
                }
            });
        }

        actions.Add(new
        {
            add = new
            {
                index = targetIndexName,
                alias = status.AliasName,
                is_write_index = true
            }
        });

        var response = await _client.LowLevel.DoRequestAsync<StringResponse>(
            global::Elasticsearch.Net.HttpMethod.POST,
            "_aliases",
            cancellationToken,
            PostData.Serializable(new { actions }));

        if (!response.Success)
        {
            _logger.LogError(
                "Failed to promote CommerceHub Elasticsearch index | Alias: {Alias} | TargetIndex: {TargetIndex} | Error: {Error}",
                status.AliasName,
                targetIndexName,
                response.Body);
            return false;
        }

        await RecordSchemaVersionAsync(
            $"Elasticsearch:{status.AliasName}",
            targetIndexName,
            new
            {
                alias = status.AliasName,
                promotedAtUtc = DateTime.UtcNow
            },
            cancellationToken);

        _indexInitialized = false;
        return true;
    }

    public async Task<CommerceHubSearchAliasMigrationResult> FinalizeAliasMigrationAsync(
        string smokeTestQuery = "samsung",
        CancellationToken cancellationToken = default)
    {
        var verification = await VerifyMigrationAsync(smokeTestQuery, cancellationToken);
        if (!verification.DesiredIndexReady)
        {
            return new CommerceHubSearchAliasMigrationResult
            {
                Success = false,
                Message = "Desired CommerceHub index is not ready for alias promotion.",
                Verification = verification
            };
        }

        if (verification.AliasPointsToDesiredIndex)
        {
            return new CommerceHubSearchAliasMigrationResult
            {
                Success = true,
                AlreadyOnDesiredIndex = true,
                SmokeTestPassed = verification.SmokeTestPassed,
                SmokeTestHitCount = verification.SmokeTestHitCount,
                Message = "CommerceHub alias already points to the desired versioned index.",
                Verification = verification
            };
        }

        var result = new CommerceHubSearchAliasMigrationResult
        {
            RollbackRecommendedUntilUtc = DateTime.UtcNow.AddHours(48)
        };

        if (verification.LegacyConcreteIndexExists && !verification.AliasExists)
        {
            var backupIndexName = $"{verification.AliasName}_legacy_backup_{DateTime.UtcNow:yyyyMMddHHmmss}";
            var backupCreated = await BackupIndexAsync(verification.AliasName, backupIndexName, cancellationToken);
            if (!backupCreated)
            {
                result.Message = "Failed to create backup for the legacy CommerceHub index.";
                result.Verification = verification;
                return result;
            }

            result.BackupCreated = true;
            result.BackupIndexName = backupIndexName;

            var legacyCount = await CountAsync(verification.AliasName, cancellationToken);
            var backupCount = await CountAsync(backupIndexName, cancellationToken);
            if (legacyCount.HasValue && backupCount.HasValue && backupCount.Value < legacyCount.Value)
            {
                result.Message = "Legacy CommerceHub index backup count is lower than the source index count.";
                result.Verification = verification;
                return result;
            }

            result.LegacyIndexDeleted = await DeleteIndexInternalAsync(verification.AliasName, cancellationToken);
            if (!result.LegacyIndexDeleted)
            {
                result.Message = "Failed to delete the legacy concrete CommerceHub index before alias promotion.";
                result.Verification = verification;
                return result;
            }
        }

        result.AliasPromoted = await PromoteIndexAsync(verification.DesiredIndexName, cancellationToken);
        if (!result.AliasPromoted)
        {
            result.Message = "Failed to point the CommerceHub alias at the desired versioned index.";
            result.Verification = verification;
            return result;
        }

        var postPromotionVerification = await VerifyMigrationAsync(smokeTestQuery, cancellationToken);
        result.Success = postPromotionVerification.AliasPointsToDesiredIndex;
        result.SmokeTestPassed = postPromotionVerification.SmokeTestPassed;
        result.SmokeTestHitCount = postPromotionVerification.SmokeTestHitCount;
        result.Message = result.Success
            ? "CommerceHub alias successfully promoted to the desired versioned index. Keep the backup index for 24-48 hours before cleanup."
            : "CommerceHub alias promotion completed, but post-promotion verification did not fully pass.";
        result.Verification = postPromotionVerification;
        return result;
    }

    public async Task<long?> CountAsync(string? targetIndexName = null, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return 0;
        }

        var indexName = string.IsNullOrWhiteSpace(targetIndexName)
            ? _settings.CommerceHubProductsIndexName
            : targetIndexName;

        var response = await _client.CountAsync<CommerceHubProductSearchDocument>(
            c => c.Index(indexName),
            cancellationToken);

        return response.IsValid ? response.Count : null;
    }

    private async Task ExecuteWithRetryAsync(
        string operationName,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < RetryDelays.Length + 1; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await operation(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < RetryDelays.Length)
            {
                lastException = ex;
                var delay = RetryDelays[attempt];

                _logger.LogWarning(
                    ex,
                    "CommerceHub Elasticsearch operation retrying | Operation: {Operation} | Attempt: {Attempt} | DelayMs: {DelayMs}",
                    operationName,
                    attempt + 1,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }

        throw lastException ?? new InvalidOperationException($"CommerceHub Elasticsearch {operationName} failed.");
    }

    private static Exception BuildElasticsearchException(string operation, string reason) =>
        new InvalidOperationException($"CommerceHub Elasticsearch {operation} failed: {reason}");

    private async Task<bool> EnsureIndexReadyAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        if (_indexInitialized && !forceRefresh)
        {
            return true;
        }

        await _indexInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_indexInitialized && !forceRefresh)
            {
                return true;
            }

            var ready = await EnsureIndexExistsAsync(cancellationToken);
            _indexInitialized = ready;
            return ready;
        }
        finally
        {
            _indexInitializationLock.Release();
        }
    }

    private static bool LooksLikeStrictMappingError(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) &&
        reason.Contains(StrictMappingErrorMarker, StringComparison.OrdinalIgnoreCase);

    private static string? TryExtractStrictMappingField(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var match = StrictMappingFieldRegex.Match(reason);
        if (!match.Success)
        {
            return null;
        }

        var field = match.Groups["field"].Value;
        return string.IsNullOrWhiteSpace(field) ? null : field;
    }

    private static HashSet<string> ResolveLegacyFieldsToStrip(
        IEnumerable<string> missingFields,
        string? strictMappingReason = null)
    {
        var fieldsToStrip = new HashSet<string>(
            missingFields.Where(field => CompatibilityFieldMappings.ContainsKey(field)),
            StringComparer.OrdinalIgnoreCase);

        var strictField = TryExtractStrictMappingField(strictMappingReason);
        if (!string.IsNullOrWhiteSpace(strictField) &&
            CompatibilityFieldMappings.ContainsKey(strictField))
        {
            fieldsToStrip.Add(strictField);
        }

        return fieldsToStrip;
    }

    private async Task<bool> TryIndexLegacyCompatibleAsync(
        CommerceHubProductSearchDocument document,
        string indexName,
        string reason,
        CancellationToken cancellationToken)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.LegacyConcreteIndexExists ||
            status.AliasExists ||
            !string.Equals(status.ActiveIndexName, indexName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var missingFields = ResolveLegacyFieldsToStrip(status.MissingFields, reason);

        if (missingFields.Count == 0)
        {
            return false;
        }

        var fieldsToStrip = missingFields.ToArray();

        _logger.LogWarning(
            "CommerceHub Elasticsearch is using legacy compatibility indexing for the concrete index | Index: {Index} | ListingId: {ListingId} | MissingFields: {MissingFields} | Error: {Error}",
            indexName,
            document.Id,
            string.Join(", ", fieldsToStrip),
            reason);

        var response = await _client.LowLevel.DoRequestAsync<StringResponse>(
            global::Elasticsearch.Net.HttpMethod.PUT,
            $"{indexName}/_doc/{document.GetDocumentId()}",
            cancellationToken,
            PostData.Serializable(BuildLegacyCompatibleDocumentPayload(document, fieldsToStrip)));

        if (!response.Success)
        {
            _logger.LogWarning(
                "CommerceHub Elasticsearch legacy compatibility indexing failed | Index: {Index} | ListingId: {ListingId} | Error: {Error}",
                indexName,
                document.Id,
                response.Body);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Proactive legacy-compatible indexing: strips known missing fields and uses
    /// the low-level PUT API. Called BEFORE the NEST attempt when the system detects
    /// legacy mode with known missing fields, avoiding the strict-mapping retry storm.
    /// </summary>
    private async Task<bool> IndexLegacyDirectAsync(
        CommerceHubProductSearchDocument document,
        string indexName,
        IReadOnlyCollection<string> missingFields,
        CancellationToken cancellationToken)
    {
        var initialFieldsToStrip = ResolveLegacyFieldsToStrip(missingFields);

        _logger.LogDebug(
            "CommerceHub Elasticsearch using proactive legacy indexing | Index: {Index} | ListingId: {ListingId} | StrippedFields: {StrippedFields}",
            indexName,
            document.Id,
            string.Join(", ", initialFieldsToStrip));

        try
        {
            await ExecuteWithRetryAsync(
                "index-legacy",
                async ct =>
                {
                    var fieldsToStrip = new HashSet<string>(initialFieldsToStrip, StringComparer.OrdinalIgnoreCase);

                    while (true)
                    {
                        var response = await _client.LowLevel.DoRequestAsync<StringResponse>(
                            global::Elasticsearch.Net.HttpMethod.PUT,
                            $"{indexName}/_doc/{document.GetDocumentId()}",
                            ct,
                            PostData.Serializable(BuildLegacyCompatibleDocumentPayload(document, fieldsToStrip.ToArray())));

                        if (response.Success)
                        {
                            return;
                        }

                        var error = response.Body ?? "Unknown error";
                        var strictField = TryExtractStrictMappingField(error);
                        if (!string.IsNullOrWhiteSpace(strictField) &&
                            CompatibilityFieldMappings.ContainsKey(strictField) &&
                            fieldsToStrip.Add(strictField))
                        {
                            _logger.LogWarning(
                                "CommerceHub Elasticsearch legacy index rejected a compatibility field and will retry with it stripped | Index: {Index} | ListingId: {ListingId} | Field: {Field}",
                                indexName,
                                document.Id,
                                strictField);
                            continue;
                        }

                        throw BuildElasticsearchException(
                            "Index (legacy)",
                            error);
                    }
                },
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "CommerceHub Elasticsearch legacy direct index failed | Index: {Index} | ListingId: {ListingId} | StoreId: {StoreId} | ProductId: {ProductId} | VariantId: {VariantId} | Published: {IsPublished}",
                indexName,
                document.Id,
                document.StoreId,
                document.ProductId,
                document.VariantId,
                document.IsPublished);
            return false;
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildLegacyCompatibleDocumentPayload(
        CommerceHubProductSearchDocument document,
        IReadOnlyCollection<string> missingFields)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = document.Id,
            ["tenantId"] = document.TenantId,
            ["storeId"] = document.StoreId,
            ["productId"] = document.ProductId,
            ["variantId"] = document.VariantId,
            ["productName"] = document.ProductName,
            ["productNameNormalized"] = document.ProductNameNormalized,
            ["description"] = document.Description,
            ["storeName"] = document.StoreName,
            ["categoryId"] = document.CategoryId,
            ["categoryName"] = document.CategoryName,
            ["categoryPath"] = document.CategoryPath,
            ["currency"] = document.Currency,
            ["price"] = document.Price,
            ["inStock"] = document.InStock,
            ["totalStock"] = document.TotalStock,
            ["rating"] = document.Rating,
            ["reviewCount"] = document.ReviewCount,
            ["popularityScore"] = document.PopularityScore,
            ["lastClickAtUtc"] = document.LastClickAtUtc,
            ["attributes"] = document.Attributes,
            ["isPublished"] = document.IsPublished,
            ["createdAtUtc"] = document.CreatedAtUtc,
            ["updatedAtUtc"] = document.UpdatedAtUtc
        };

        foreach (var field in missingFields)
        {
            payload.Remove(field);
        }

        return payload;
    }

    private async Task<bool> EnsureMappingAsync(CancellationToken cancellationToken)
        => await EnsureMappingAsync(_settings.CommerceHubProductsIndexName, cancellationToken);

    private async Task<bool> EnsureMappingAsync(string indexName, CancellationToken cancellationToken)
    {
        var missingFields = (await GetMissingFieldsAsync(indexName, cancellationToken))
            .Where(field => CompatibilityFieldMappings.ContainsKey(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingFields.Count == 0)
        {
            return true;
        }

        _logger.LogWarning(
            "CommerceHub Elasticsearch index is missing compatibility fields and will be updated in place | Index: {Index} | MissingFields: {MissingFields}",
            indexName,
            string.Join(", ", missingFields));

        var response = await _client.LowLevel.Indices.PutMappingAsync<StringResponse>(
            indexName,
            GetPutMappingPayload(missingFields));

        if (!response.Success)
        {
            _logger.LogError(
                "Failed to update CommerceHub Elasticsearch mapping | Index: {Index} | MissingFields: {MissingFields} | Error: {Error}",
                indexName,
                string.Join(", ", missingFields),
                response.Body);
            return false;
        }

        return true;
    }

    private async Task<string?> ResolveWriteIndexAsync(string? explicitTargetIndexName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(explicitTargetIndexName))
        {
            return explicitTargetIndexName;
        }

        if (!await EnsureIndexReadyAsync(cancellationToken))
        {
            return null;
        }

        var status = await GetStatusAsync(cancellationToken);
        return status.WriteIndexName ?? _settings.CommerceHubProductsIndexName;
    }

    private async Task<string?> ResolveLookupIndexAsync(string? explicitTargetIndexName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(explicitTargetIndexName))
        {
            return explicitTargetIndexName;
        }

        var status = await GetStatusAsync(cancellationToken);
        return status.ActiveIndexName ??
               (status.AliasExists || status.LegacyConcreteIndexExists
                   ? status.AliasName
                   : null);
    }

    private async Task<bool> CreateVersionedIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        var existsResponse = await _client.Indices.ExistsAsync(indexName, ct: cancellationToken);
        if (existsResponse.Exists)
        {
            return true;
        }

        var createResponse = await _client.LowLevel.Indices.CreateAsync<StringResponse>(
            indexName,
            GetCreateIndexPayload());

        if (!createResponse.Success)
        {
            _logger.LogError(
                "Failed to create CommerceHub Elasticsearch index | Index: {Index} | Error: {Error}",
                indexName,
                createResponse.Body);
            return false;
        }

        await RecordSchemaVersionAsync(
            $"Elasticsearch:{_settings.CommerceHubProductsIndexName}",
            indexName,
            new
            {
                createdAtUtc = DateTime.UtcNow,
                version = ParseVersion(indexName) ?? _settings.CommerceHubProductsIndexVersion
            },
            cancellationToken);

        _logger.LogInformation(
            "Created CommerceHub Elasticsearch versioned index | Alias: {Alias} | Index: {Index}",
            _settings.CommerceHubProductsIndexName,
            indexName);

        return true;
    }

    private async Task<Dictionary<string, bool>> LoadAliasMapAsync(string aliasName, CancellationToken cancellationToken)
    {
        var response = await _client.LowLevel.DoRequestAsync<StringResponse>(
            global::Elasticsearch.Net.HttpMethod.GET,
            $"_alias/{aliasName}",
            cancellationToken);
        if (!response.Success || string.IsNullOrWhiteSpace(response.Body))
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(response.Body);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            _logger.LogWarning(
                "Unexpected CommerceHub Elasticsearch alias response shape | Alias: {Alias} | Payload: {Payload}",
                aliasName,
                response.Body);

            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        if (document.RootElement.TryGetProperty("error", out _))
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var indexProperty in document.RootElement.EnumerateObject())
        {
            if (indexProperty.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!indexProperty.Value.TryGetProperty("aliases", out var aliasesElement))
            {
                continue;
            }

            if (aliasesElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!aliasesElement.TryGetProperty(aliasName, out var aliasElement))
            {
                continue;
            }

            if (aliasElement.ValueKind != JsonValueKind.Object)
            {
                result[indexProperty.Name] = false;
                continue;
            }

            var isWriteIndex = aliasElement.TryGetProperty("is_write_index", out var writeElement) &&
                               writeElement.ValueKind == JsonValueKind.True;
            result[indexProperty.Name] = isWriteIndex;
        }

        return result;
    }

    private async Task<List<string>> GetAliasIndexNamesAsync(string aliasName, CancellationToken cancellationToken)
    {
        var aliasMap = await LoadAliasMapAsync(aliasName, cancellationToken);
        return aliasMap.Keys.ToList();
    }

    private async Task<(IReadOnlyCollection<string> MissingFields, bool HasAttributeTemplates)> GetMappingDiagnosticsAsync(
        string indexName,
        CancellationToken cancellationToken)
    {
        var response = await _client.LowLevel.DoRequestAsync<StringResponse>(
            global::Elasticsearch.Net.HttpMethod.GET,
            $"{indexName}/_mapping",
            cancellationToken);
        if (!response.Success || string.IsNullOrWhiteSpace(response.Body))
        {
            return (RequiredCompatibilityFields.ToList(), false);
        }

        using var document = JsonDocument.Parse(response.Body);
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasAttributeTemplates = false;

        foreach (var indexProperty in document.RootElement.EnumerateObject())
        {
            if (!TryResolveMappingDefinition(indexProperty.Value, out var mappingDefinition))
            {
                continue;
            }

            CollectMappedFieldNames(mappingDefinition, fields);
            hasAttributeTemplates |= HasAttributesTemplate(mappingDefinition);
        }

        if (fields.Count > 0 || hasAttributeTemplates)
        {
            var missingFields = RequiredCompatibilityFields
                .Where(field => !fields.Contains(field))
                .ToList();

            return (missingFields, hasAttributeTemplates);
        }

        return (RequiredCompatibilityFields.ToList(), false);
    }

    private static bool TryResolveMappingDefinition(JsonElement indexElement, out JsonElement mappingDefinition)
    {
        mappingDefinition = default;

        if (!indexElement.TryGetProperty("mappings", out var mappingsElement) ||
            mappingsElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return TryFindMappingDefinition(mappingsElement, out mappingDefinition);
    }

    private static bool TryFindMappingDefinition(JsonElement element, out JsonElement mappingDefinition)
    {
        mappingDefinition = default;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty("properties", out _) ||
            element.TryGetProperty("dynamic_templates", out _))
        {
            mappingDefinition = element;
            return true;
        }

        foreach (var child in element.EnumerateObject())
        {
            if (TryFindMappingDefinition(child.Value, out mappingDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectMappedFieldNames(JsonElement element, ISet<string> fieldNames)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("properties", out var propertiesElement) &&
                    propertiesElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in propertiesElement.EnumerateObject())
                    {
                        fieldNames.Add(property.Name);
                        CollectMappedFieldNames(property.Value, fieldNames);
                    }
                }

                foreach (var child in element.EnumerateObject())
                {
                    if (!string.Equals(child.Name, "properties", StringComparison.OrdinalIgnoreCase))
                    {
                        CollectMappedFieldNames(child.Value, fieldNames);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    CollectMappedFieldNames(child, fieldNames);
                }

                break;
        }
    }

    private static bool HasAttributesTemplate(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("dynamic_templates", out var dynamicTemplatesElement) &&
                    ContainsAttributesPathMatch(dynamicTemplatesElement))
                {
                    return true;
                }

                foreach (var child in element.EnumerateObject())
                {
                    if (HasAttributesTemplate(child.Value))
                    {
                        return true;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    if (HasAttributesTemplate(child))
                    {
                        return true;
                    }
                }

                break;
        }

        return false;
    }

    private static bool ContainsAttributesPathMatch(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("path_match", out var pathMatchElement) &&
                    pathMatchElement.ValueKind == JsonValueKind.String &&
                    string.Equals(pathMatchElement.GetString(), "attributes.*", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                foreach (var child in element.EnumerateObject())
                {
                    if (ContainsAttributesPathMatch(child.Value))
                    {
                        return true;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    if (ContainsAttributesPathMatch(child))
                    {
                        return true;
                    }
                }

                break;
        }

        return false;
    }

    private async Task<IReadOnlyCollection<string>> GetMissingFieldsAsync(string indexName, CancellationToken cancellationToken)
    {
        var diagnostics = await GetMappingDiagnosticsAsync(indexName, cancellationToken);
        return diagnostics.MissingFields;
    }

    private async Task<(bool Passed, long? HitCount)> RunSmokeSearchAsync(
        string aliasName,
        string smokeTestQuery,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = string.IsNullOrWhiteSpace(smokeTestQuery) ? "samsung" : smokeTestQuery.Trim();

        try
        {
            var response = await _client.SearchAsync<CommerceHubProductSearchDocument>(
                s => s
                    .Index(aliasName)
                    .Size(1)
                    .Query(q => q.QueryString(qs => qs.Query(normalizedQuery))),
                cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogWarning(
                    "CommerceHub Elasticsearch smoke search failed | Alias: {Alias} | Query: {Query} | Error: {Error}",
                    aliasName,
                    normalizedQuery,
                    response.ServerError?.Error?.Reason ?? response.OriginalException?.Message);
                return (false, null);
            }

            return (true, response.Total);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CommerceHub Elasticsearch smoke search threw unexpectedly | Alias: {Alias} | Query: {Query}",
                aliasName,
                normalizedQuery);
            return (false, null);
        }
    }

    private async Task<bool> BackupIndexAsync(string sourceIndexName, string backupIndexName, CancellationToken cancellationToken)
    {
        var sourceExists = await _client.Indices.ExistsAsync(sourceIndexName, ct: cancellationToken);
        if (!sourceExists.Exists)
        {
            return false;
        }

        var backupExists = await _client.Indices.ExistsAsync(backupIndexName, ct: cancellationToken);
        if (backupExists.Exists)
        {
            return true;
        }

        var response = await _client.LowLevel.DoRequestAsync<StringResponse>(
            global::Elasticsearch.Net.HttpMethod.POST,
            "_reindex",
            cancellationToken,
            PostData.Serializable(new
            {
                source = new { index = sourceIndexName },
                dest = new { index = backupIndexName },
                conflicts = "proceed"
            }));

        if (!response.Success)
        {
            _logger.LogError(
                "Failed to back up legacy CommerceHub index | SourceIndex: {SourceIndex} | BackupIndex: {BackupIndex} | Error: {Error}",
                sourceIndexName,
                backupIndexName,
                response.Body);
            return false;
        }

        _logger.LogInformation(
            "Backed up legacy CommerceHub index | SourceIndex: {SourceIndex} | BackupIndex: {BackupIndex}",
            sourceIndexName,
            backupIndexName);

        return true;
    }

    private async Task<bool> DeleteIndexInternalAsync(string indexName, CancellationToken cancellationToken)
    {
        var response = await _client.Indices.DeleteAsync(indexName, ct: cancellationToken);
        if (!response.IsValid && response.ServerError?.Status != 404)
        {
            _logger.LogError(
                "Failed to delete CommerceHub Elasticsearch index | Index: {Index} | Error: {Error}",
                indexName,
                response.ServerError?.Error?.Reason ?? response.OriginalException?.Message);
            return false;
        }

        _logger.LogWarning("Deleted legacy CommerceHub Elasticsearch index | Index: {Index}", indexName);
        return true;
    }

    private async Task RecordSchemaVersionAsync(
        string component,
        string version,
        object details,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var schemaVersionService = scope.ServiceProvider.GetService<ISystemSchemaVersionService>();
            if (schemaVersionService == null)
            {
                return;
            }

            await schemaVersionService.RecordAsync(component, version, details, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to record schema version for CommerceHub Elasticsearch | Component: {Component} | Version: {Version}",
                component,
                version);
        }
    }

    private string BuildVersionedIndexName(int version) =>
        $"{_settings.CommerceHubProductsIndexName}_v{Math.Max(1, version)}";

    private static int? ParseVersion(string? indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return null;
        }

        var markerIndex = indexName.LastIndexOf("_v", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        return int.TryParse(indexName[(markerIndex + 2)..], out var version) ? version : null;
    }

    private static readonly string[] RequiredCompatibilityFields =
    [
        "tenantId",
        "storeId",
        "productId",
        "variantId",
        "productName",
        "productNameNormalized",
        "description",
        "categoryId",
        "categoryName",
        "categoryPath",
        "price",
        "priceSource",
        "overridePresent",
        "currency",
        "inStock",
        "totalStock",
        "rating",
        "reviewCount",
        "storeName",
        "attributes",
        "isPublished",
        "createdAtUtc",
        "updatedAtUtc",
        "popularityScore",
        "lastClickAtUtc"
    ];

    private static readonly IReadOnlyDictionary<string, string> CompatibilityFieldMappings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tenantId"] = """{ "type": "keyword" }""",
            ["storeId"] = """{ "type": "keyword" }""",
            ["productId"] = """{ "type": "keyword" }""",
            ["variantId"] = """{ "type": "keyword" }""",
            ["productName"] =
                """
                {
                  "type": "text",
                  "fields": {
                    "keyword": { "type": "keyword", "ignore_above": 256 }
                  }
                }
                """,
            ["productNameNormalized"] =
                """
                {
                  "type": "text",
                  "analyzer": "standard",
                  "fields": {
                    "keyword": { "type": "keyword", "ignore_above": 256 }
                  }
                }
                """,
            ["description"] = """{ "type": "text" }""",
            ["categoryId"] = """{ "type": "keyword" }""",
            ["categoryName"] = """{ "type": "keyword", "ignore_above": 256 }""",
            ["categoryPath"] = """{ "type": "keyword" }""",
            ["price"] = """{ "type": "double" }""",
            ["priceSource"] = """{ "type": "keyword" }""",
            ["overridePresent"] = """{ "type": "boolean" }""",
            ["currency"] = """{ "type": "keyword" }""",
            ["inStock"] = """{ "type": "boolean" }""",
            ["totalStock"] = """{ "type": "double" }""",
            ["rating"] = """{ "type": "double" }""",
            ["reviewCount"] = """{ "type": "integer" }""",
            ["storeName"] =
                """
                {
                  "type": "text",
                  "fields": {
                    "keyword": { "type": "keyword", "ignore_above": 256 }
                  }
                }
                """,
            ["attributes"] =
                """
                {
                  "type": "object",
                  "dynamic": true
                }
                """,
            ["isPublished"] = """{ "type": "boolean" }""",
            ["createdAtUtc"] = """{ "type": "date" }""",
            ["updatedAtUtc"] = """{ "type": "date" }""",
            ["popularityScore"] = """{ "type": "double" }""",
            ["lastClickAtUtc"] = """{ "type": "date" }"""
        };

    private PostData GetCreateIndexPayload() =>
        PostData.String($$"""
        {
          "settings": {
            "number_of_shards": {{_settings.NumberOfShards}},
            "number_of_replicas": {{_settings.NumberOfReplicas}},
            "index.mapping.total_fields.limit": 2000
          },
          "mappings": {
            "dynamic": "strict",
            "dynamic_templates": [
              {
                "attributes_string": {
                  "path_match": "attributes.*",
                  "match_mapping_type": "string",
                  "mapping": {
                    "type": "keyword",
                    "ignore_above": 256
                  }
                }
              },
              {
                "attributes_number_long": {
                  "path_match": "attributes.*",
                  "match_mapping_type": "long",
                  "mapping": {
                    "type": "double"
                  }
                }
              },
              {
                "attributes_number_double": {
                  "path_match": "attributes.*",
                  "match_mapping_type": "double",
                  "mapping": {
                    "type": "double"
                  }
                }
              },
              {
                "attributes_boolean": {
                  "path_match": "attributes.*",
                  "match_mapping_type": "boolean",
                  "mapping": {
                    "type": "boolean"
                  }
                }
              }
            ],
            "properties": {
              "id": { "type": "keyword" },
              "tenantId": { "type": "keyword" },
              "storeId": { "type": "keyword" },
              "productId": { "type": "keyword" },
              "variantId": { "type": "keyword" },
              "productName": {
                "type": "text",
                "fields": {
                  "keyword": { "type": "keyword", "ignore_above": 256 }
                }
              },
              "productNameNormalized": {
                "type": "text",
                "fields": {
                  "keyword": { "type": "keyword", "ignore_above": 256 }
                }
              },
              "description": { "type": "text" },
              "categoryId": { "type": "keyword" },
              "categoryName": { "type": "keyword", "ignore_above": 256 },
              "categoryPath": { "type": "keyword" },
              "price": { "type": "double" },
              "priceSource": { "type": "keyword" },
              "overridePresent": { "type": "boolean" },
              "currency": { "type": "keyword" },
              "inStock": { "type": "boolean" },
              "totalStock": { "type": "double" },
              "rating": { "type": "double" },
              "reviewCount": { "type": "integer" },
              "popularityScore": { "type": "double" },
              "lastClickAtUtc": { "type": "date" },
              "storeName": {
                "type": "text",
                "fields": {
                  "keyword": { "type": "keyword", "ignore_above": 256 }
                }
              },
              "attributes": {
                "type": "object",
                "dynamic": true
              },
              "isPublished": { "type": "boolean" },
              "createdAtUtc": { "type": "date" },
              "updatedAtUtc": { "type": "date" }
            }
          }
        }
        """);

    private static PostData GetPutMappingPayload(IReadOnlyCollection<string> missingFields)
    {
        var propertiesJson = string.Join(
            ",\n",
            missingFields.Select(field => $"    \"{field}\": {CompatibilityFieldMappings[field]}"));

        return PostData.String(
            $$"""
            {
              "properties": {
            {{propertiesJson}}
              }
            }
            """);
    }
}

public class CommerceHubElasticsearchIndexInitializer : IHostedService
{
    private readonly ICommerceHubSearchIndexService _indexService;
    private readonly ILogger<CommerceHubElasticsearchIndexInitializer> _logger;

    public CommerceHubElasticsearchIndexInitializer(
        ICommerceHubSearchIndexService indexService,
        ILogger<CommerceHubElasticsearchIndexInitializer> logger)
    {
        _indexService = indexService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => RunInitializationAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunInitializationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _indexService.EnsureIndexExistsAsync(cancellationToken);
            _logger.LogInformation("CommerceHub Elasticsearch index initialization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CommerceHub Elasticsearch index initialization failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
