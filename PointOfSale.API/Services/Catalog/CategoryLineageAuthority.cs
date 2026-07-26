using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Catalog;

public static class CategoryLineageAuthority
{
    public static async Task<CategoryLineageSnapshot> LoadTenantSnapshotAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Categories
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(category => category.TenantId == tenantId)
            .Select(category => new CategoryLineageRow(
                category.Id,
                category.TenantId,
                category.Name,
                category.Description,
                category.ParentCategoryId,
                category.CreatedAt,
                category.UpdatedAt,
                category.IsDeleted))
            .ToListAsync(cancellationToken);

        return new CategoryLineageSnapshot(tenantId, rows);
    }

    public static async Task<CategoryLineageRow?> RequireLiveCategoryAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadTenantSnapshotAsync(dbContext, tenantId, cancellationToken);
        return snapshot.TryGetLiveCategory(categoryId, out var row) ? row : null;
    }
}

public sealed class CategoryLineageSnapshot
{
    private readonly Dictionary<Guid, CategoryLineageRow> _rows;
    private readonly Dictionary<Guid, bool> _liveMemo = new();

    public CategoryLineageSnapshot(Guid tenantId, IReadOnlyCollection<CategoryLineageRow> rows)
    {
        TenantId = tenantId;
        Rows = rows;
        _rows = rows.ToDictionary(row => row.CategoryId);
    }

    public Guid TenantId { get; }

    public IReadOnlyCollection<CategoryLineageRow> Rows { get; }

    public bool IsLiveInLineage(Guid categoryId) =>
        IsLiveInLineage(categoryId, new HashSet<Guid>());

    public bool TryGetLiveCategory(Guid categoryId, out CategoryLineageRow? row)
    {
        if (_rows.TryGetValue(categoryId, out var candidate) && IsLiveInLineage(categoryId))
        {
            row = candidate;
            return true;
        }

        row = null;
        return false;
    }

    private bool IsLiveInLineage(Guid categoryId, HashSet<Guid> visiting)
    {
        if (_liveMemo.TryGetValue(categoryId, out var cached))
        {
            return cached;
        }

        if (!_rows.TryGetValue(categoryId, out var row) ||
            row.TenantId != TenantId ||
            row.IsDeleted)
        {
            _liveMemo[categoryId] = false;
            return false;
        }

        if (!row.ParentCategoryId.HasValue)
        {
            _liveMemo[categoryId] = true;
            return true;
        }

        var parentId = row.ParentCategoryId.Value;
        if (parentId == Guid.Empty ||
            parentId == categoryId ||
            !visiting.Add(categoryId))
        {
            _liveMemo[categoryId] = false;
            return false;
        }

        var isLive = IsLiveInLineage(parentId, visiting);
        visiting.Remove(categoryId);
        _liveMemo[categoryId] = isLive;
        return isLive;
    }
}

public sealed record CategoryLineageRow(
    Guid CategoryId,
    Guid TenantId,
    string Name,
    string? Description,
    Guid? ParentCategoryId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    bool IsDeleted)
{
    public DateTime AuthorityTimestampUtc
    {
        get
        {
            var timestamp = UpdatedAt ?? CreatedAt;
            return timestamp == default ? DateTime.UtcNow : timestamp;
        }
    }
}
