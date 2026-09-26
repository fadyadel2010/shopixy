namespace PointOfSale.Infrastructure.Redis.Models;

public sealed class CacheEnvelope<T>
{
    public T Payload { get; set; } = default!;
    public DateTime CachedAtUtc { get; set; }
    public DateTime? ProjectionLastUpdatedAtUtc { get; set; }
    public DateTime? SourceEventOccurredAtUtc { get; set; }
    public long InvalidationGeneration { get; set; }
    public int SchemaVersion { get; set; } = 1;
}

public readonly record struct CacheReadResult<T>(T? Value, bool Refreshed)
    where T : class;

public sealed class CachePopulationResult<T>
    where T : class
{
    public T? Value { get; init; }
    public DateTime? ProjectionLastUpdatedAtUtc { get; init; }
    public DateTime? SourceEventOccurredAtUtc { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public bool SkipCache { get; init; }

    public static CachePopulationResult<T> FromValue(
        T value,
        DateTime? projectionLastUpdatedAtUtc = null,
        DateTime? sourceEventOccurredAtUtc = null,
        int schemaVersion = 1,
        bool skipCache = false)
    {
        return new CachePopulationResult<T>
        {
            Value = value,
            ProjectionLastUpdatedAtUtc = projectionLastUpdatedAtUtc,
            SourceEventOccurredAtUtc = sourceEventOccurredAtUtc,
            SchemaVersion = schemaVersion,
            SkipCache = skipCache
        };
    }
}
