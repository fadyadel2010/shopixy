namespace PointOfSale.Infrastructure.Redis.Models;

public sealed class CacheEnvelope<T>
{
    public T Payload { get; set; } = default!;
    public DateTime CachedAtUtc { get; set; }
    public DateTime? ProjectionLastUpdatedAtUtc { get; set; }
    public DateTime? SourceEventOccurredAtUtc { get; set; }
    public long? ProjectionVersion { get; set; }
    public long? SourceVersion { get; set; }
    public long InvalidationGeneration { get; set; }
    public int SchemaVersion { get; set; } = 1;
}

public static class CacheProjectionFreshnessGuard
{
    public static bool ShouldBypass(
        DateTime? projectionLastUpdatedAtUtc,
        DateTime? sourceEventOccurredAtUtc,
        long? projectionVersion,
        long? sourceVersion,
        TimeSpan? maxProjectionLag)
    {
        if (projectionVersion.HasValue || sourceVersion.HasValue)
        {
            return !projectionVersion.HasValue ||
                   !sourceVersion.HasValue ||
                   projectionVersion.Value < sourceVersion.Value;
        }

        if (!maxProjectionLag.HasValue || !projectionLastUpdatedAtUtc.HasValue || !sourceEventOccurredAtUtc.HasValue)
        {
            return false;
        }

        return projectionLastUpdatedAtUtc.Value - sourceEventOccurredAtUtc.Value > maxProjectionLag.Value;
    }
}

public readonly record struct CacheReadResult<T>(T? Value, bool Refreshed)
    where T : class;

public sealed class CachePopulationResult<T>
    where T : class
{
    public T? Value { get; init; }
    public DateTime? ProjectionLastUpdatedAtUtc { get; init; }
    public DateTime? SourceEventOccurredAtUtc { get; init; }
    public long? ProjectionVersion { get; init; }
    public long? SourceVersion { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public bool SkipCache { get; init; }

    public static CachePopulationResult<T> FromValue(
        T value,
        DateTime? projectionLastUpdatedAtUtc = null,
        DateTime? sourceEventOccurredAtUtc = null,
        int schemaVersion = 1,
        bool skipCache = false,
        long? projectionVersion = null,
        long? sourceVersion = null)
    {
        return new CachePopulationResult<T>
        {
            Value = value,
            ProjectionLastUpdatedAtUtc = projectionLastUpdatedAtUtc,
            SourceEventOccurredAtUtc = sourceEventOccurredAtUtc,
            ProjectionVersion = projectionVersion,
            SourceVersion = sourceVersion,
            SchemaVersion = schemaVersion,
            SkipCache = skipCache
        };
    }
}
