namespace PointOfSale.Infrastructure.Elasticsearch.Services;

/// <summary>
/// Represents distinct phases in the index migration lifecycle.
/// </summary>
public enum CommerceHubSearchMigrationPhase
{
    /// <summary>Alias already points to the desired versioned index. No migration needed.</summary>
    Complete,

    /// <summary>System is running against the legacy concrete index. Migration is required.</summary>
    LegacyConcreteIndex,

    /// <summary>Desired versioned index exists but is empty. Reindex is required.</summary>
    DesiredIndexEmptyPendingReindex,

    /// <summary>Desired versioned index is populated but alias has not been switched yet.</summary>
    DesiredIndexReadyPendingFinalize,

    /// <summary>Alias exists but points to an older version. Reindex + finalize is required.</summary>
    AliasOnOlderVersion
}

public sealed class CommerceHubSearchIndexStatus
{
    public string AliasName { get; set; } = string.Empty;
    public string DesiredIndexName { get; set; } = string.Empty;
    public int DesiredVersion { get; set; }
    public bool AliasExists { get; set; }
    public bool LegacyConcreteIndexExists { get; set; }
    public bool DesiredIndexExists { get; set; }
    public bool PromotionSupported { get; set; }
    public string? ActiveIndexName { get; set; }
    public string? WriteIndexName { get; set; }
    public int? ActiveVersion { get; set; }
    public IReadOnlyCollection<string> MissingFields { get; set; } = Array.Empty<string>();

    public bool IsAliasMode => AliasExists;
    public bool IsDesiredVersionActive =>
        string.Equals(ActiveIndexName, DesiredIndexName, StringComparison.OrdinalIgnoreCase);
    public bool MappingCompatible => MissingFields.Count == 0;
    public bool RequiresReindex => LegacyConcreteIndexExists || !IsDesiredVersionActive || !MappingCompatible;

    /// <summary>
    /// Computed migration lifecycle phase for operational visibility.
    /// </summary>
    public CommerceHubSearchMigrationPhase MigrationPhase
    {
        get
        {
            if (IsDesiredVersionActive)
                return CommerceHubSearchMigrationPhase.Complete;

            if (LegacyConcreteIndexExists && !AliasExists)
            {
                if (!DesiredIndexExists)
                    return CommerceHubSearchMigrationPhase.LegacyConcreteIndex;

                // Desired exists — is it populated?
                return CommerceHubSearchMigrationPhase.DesiredIndexReadyPendingFinalize;
            }

            if (AliasExists && !IsDesiredVersionActive)
            {
                return DesiredIndexExists
                    ? CommerceHubSearchMigrationPhase.DesiredIndexReadyPendingFinalize
                    : CommerceHubSearchMigrationPhase.AliasOnOlderVersion;
            }

            return DesiredIndexExists
                ? CommerceHubSearchMigrationPhase.DesiredIndexEmptyPendingReindex
                : CommerceHubSearchMigrationPhase.LegacyConcreteIndex;
        }
    }
}

public sealed class CommerceHubSearchIndexVerification
{
    public string AliasName { get; set; } = string.Empty;
    public string DesiredIndexName { get; set; } = string.Empty;
    public string? ActiveIndexName { get; set; }
    public string? WriteIndexName { get; set; }
    public bool AliasExists { get; set; }
    public IReadOnlyCollection<string> AliasIndices { get; set; } = Array.Empty<string>();
    public bool LegacyConcreteIndexExists { get; set; }
    public bool DesiredIndexExists { get; set; }
    public long? DesiredDocumentCount { get; set; }
    public long? ActiveDocumentCount { get; set; }
    public IReadOnlyCollection<string> DesiredIndexMissingFields { get; set; } = Array.Empty<string>();
    public bool DesiredIndexHasAttributeTemplates { get; set; }
    public string SmokeTestQuery { get; set; } = "samsung";
    public bool SmokeTestPassed { get; set; }
    public long? SmokeTestHitCount { get; set; }

    public bool AliasPointsToDesiredIndex =>
        AliasExists &&
        string.Equals(WriteIndexName ?? ActiveIndexName, DesiredIndexName, StringComparison.OrdinalIgnoreCase);

    public bool DesiredIndexMappingCompatible => DesiredIndexMissingFields.Count == 0;

    /// <summary>
    /// v2 index is ready for promotion when:
    /// - It exists with correct mapping + dynamic templates
    /// - It has documents (or source also has 0 — empty marketplace is valid)
    /// </summary>
    public bool DesiredIndexReady =>
        DesiredIndexExists &&
        DesiredIndexMappingCompatible &&
        DesiredIndexHasAttributeTemplates &&
        (DesiredDocumentCount.GetValueOrDefault() > 0 || ActiveDocumentCount.GetValueOrDefault() == 0);
}

public sealed class CommerceHubSearchRebuildTarget
{
    public string IndexName { get; set; } = string.Empty;
    public bool PromotionSupported { get; set; }
    public bool IsDesiredVersionTarget { get; set; }
    public bool RequiresPromotion { get; set; }
}

public sealed record CommerceHubSearchDocumentIdentity(Guid TenantId, Guid ListingId)
{
    public string GetDocumentId() => $"{TenantId:N}:{ListingId:N}";
    public string GetLegacyDocumentId() => ListingId.ToString("N");
}

public sealed class CommerceHubSearchAliasMigrationResult
{
    public bool Success { get; set; }
    public bool AlreadyOnDesiredIndex { get; set; }
    public bool BackupCreated { get; set; }
    public string? BackupIndexName { get; set; }
    public bool LegacyIndexDeleted { get; set; }
    public bool AliasPromoted { get; set; }
    public bool SmokeTestPassed { get; set; }
    public long? SmokeTestHitCount { get; set; }
    public DateTime? RollbackRecommendedUntilUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public CommerceHubSearchIndexVerification? Verification { get; set; }

    /// <summary>
    /// Result of the post-promotion catch-up reindex (ensures no data lost during migration gap).
    /// </summary>
    public CommerceHubSearchMigrationCatchUpResult? CatchUpResult { get; set; }
}

/// <summary>
/// Result of the post-finalization catch-up reindex.
/// </summary>
public sealed class CommerceHubSearchMigrationCatchUpResult
{
    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}

/// <summary>
/// Application-level write readiness. Connectivity alone is insufficient while
/// the cluster or CommerceHub write index is still recovering.
/// </summary>
public sealed class CommerceHubSearchWriteReadiness
{
    public bool IsReady { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? ClusterStatus { get; init; }
    public string? WriteIndexName { get; init; }

    public static CommerceHubSearchWriteReadiness Ready(string writeIndexName, string? clusterStatus) =>
        new()
        {
            IsReady = true,
            Reason = "write-index-resolvable",
            WriteIndexName = writeIndexName,
            ClusterStatus = clusterStatus
        };

    public static CommerceHubSearchWriteReadiness NotReady(string reason, string? clusterStatus = null) =>
        new() { IsReady = false, Reason = reason, ClusterStatus = clusterStatus };
}

public sealed class CommerceHubSearchDependencyNotReadyException : InvalidOperationException
{
    public CommerceHubSearchDependencyNotReadyException(string reason)
        : base($"CommerceHub search dependency not ready: {reason}")
    {
        Reason = reason;
    }

    public string Reason { get; }
}
