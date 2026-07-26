using PointOfSale.API.Services.Catalog;

namespace PointOfSale.Tests;

public sealed class CategoryLineageAuthorityTests
{
    [Fact]
    public void IsLiveInLineage_WhenParentIsDeleted_HidesChild()
    {
        var tenantId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var snapshot = new CategoryLineageSnapshot(
            tenantId,
            new[]
            {
                new CategoryLineageRow(parentId, tenantId, "Parent", null, null, now, now, IsDeleted: true),
                new CategoryLineageRow(childId, tenantId, "Child", null, parentId, now, now, IsDeleted: false)
            });

        Assert.False(snapshot.IsLiveInLineage(parentId));
        Assert.False(snapshot.IsLiveInLineage(childId));
        Assert.False(snapshot.TryGetLiveCategory(childId, out _));
    }

    [Fact]
    public void IsLiveInLineage_WhenParentIsMissing_HidesOrphan()
    {
        var tenantId = Guid.NewGuid();
        var orphanId = Guid.NewGuid();
        var missingParentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var snapshot = new CategoryLineageSnapshot(
            tenantId,
            new[]
            {
                new CategoryLineageRow(orphanId, tenantId, "Orphan", null, missingParentId, now, now, IsDeleted: false)
            });

        Assert.False(snapshot.IsLiveInLineage(orphanId));
        Assert.False(snapshot.TryGetLiveCategory(orphanId, out _));
    }

    [Fact]
    public void IsLiveInLineage_WhenCycleExists_HidesCycle()
    {
        var tenantId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var snapshot = new CategoryLineageSnapshot(
            tenantId,
            new[]
            {
                new CategoryLineageRow(firstId, tenantId, "First", null, secondId, now, now, IsDeleted: false),
                new CategoryLineageRow(secondId, tenantId, "Second", null, firstId, now, now, IsDeleted: false)
            });

        Assert.False(snapshot.IsLiveInLineage(firstId));
        Assert.False(snapshot.IsLiveInLineage(secondId));
    }
}
