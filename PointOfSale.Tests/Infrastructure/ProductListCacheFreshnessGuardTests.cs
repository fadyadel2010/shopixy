using PointOfSale.Infrastructure.Redis.Models;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ProductListCacheFreshnessGuardTests
{
    [Fact]
    public void RevisionProof_AllowsOldBusinessEventWhenProjectionCaughtUp()
    {
        var sourceEventUtc = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-30), DateTimeKind.Utc);
        var projectionReadUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        Assert.False(CacheProjectionFreshnessGuard.ShouldBypass(
            projectionReadUtc,
            sourceEventUtc,
            projectionVersion: 42,
            sourceVersion: 42,
            maxProjectionLag: TimeSpan.FromSeconds(45)));
    }

    [Fact]
    public void RevisionProof_RejectsGenuineProjectionLagOnPopulationOrRead()
    {
        Assert.True(CacheProjectionFreshnessGuard.ShouldBypass(
            projectionLastUpdatedAtUtc: DateTime.UtcNow,
            sourceEventOccurredAtUtc: DateTime.UtcNow,
            projectionVersion: 41,
            sourceVersion: 42,
            maxProjectionLag: TimeSpan.FromSeconds(45)));
    }

    [Fact]
    public void RevisionProof_FailsClosedWhenEnvelopeProofIsIncomplete()
    {
        Assert.True(CacheProjectionFreshnessGuard.ShouldBypass(
            projectionLastUpdatedAtUtc: DateTime.UtcNow,
            sourceEventOccurredAtUtc: DateTime.UtcNow,
            projectionVersion: 42,
            sourceVersion: null,
            maxProjectionLag: TimeSpan.FromSeconds(45)));
    }

    [Fact]
    public void TimestampFallback_KeepsBoundaryAndUtcBehaviorForNonRevisionCallers()
    {
        var sourceEventUtc = DateTime.SpecifyKind(new DateTime(2026, 9, 27, 12, 0, 0), DateTimeKind.Utc);
        var exactlyAtBoundary = sourceEventUtc.AddSeconds(45);
        var beyondBoundary = sourceEventUtc.AddTicks(TimeSpan.FromSeconds(45).Ticks + 1);

        Assert.False(CacheProjectionFreshnessGuard.ShouldBypass(
            exactlyAtBoundary,
            sourceEventUtc,
            projectionVersion: null,
            sourceVersion: null,
            maxProjectionLag: TimeSpan.FromSeconds(45)));

        Assert.True(CacheProjectionFreshnessGuard.ShouldBypass(
            beyondBoundary,
            sourceEventUtc,
            projectionVersion: null,
            sourceVersion: null,
            maxProjectionLag: TimeSpan.FromSeconds(45)));
    }
}
