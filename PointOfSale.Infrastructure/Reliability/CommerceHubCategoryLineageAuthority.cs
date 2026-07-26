using Microsoft.EntityFrameworkCore;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Reliability;

public sealed record CommerceHubCategoryLineageSnapshot(
    Guid CategoryId,
    string Name,
    IReadOnlyList<string> Path)
{
    public string PathText => string.Join(" > ", Path);
}

/// <summary>
/// SQL-backed lineage checks for global CommerceHub category visibility.
/// Leaf category ids in listings, homepage JSON, search hits, or cached suggestions are evidence only.
/// </summary>
public static class CommerceHubCategoryLineageAuthority
{
    public static async Task<bool> IsLiveAsync(
        ApplicationDbContext dbContext,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        return await LoadLiveSnapshotAsync(dbContext, categoryId, cancellationToken) != null;
    }

    public static async Task<CommerceHubCategoryLineageSnapshot?> LoadLiveSnapshotAsync(
        ApplicationDbContext dbContext,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        if (categoryId == Guid.Empty)
        {
            return null;
        }

        var currentCategoryId = categoryId;
        var visited = new HashSet<Guid>();
        var names = new List<string>();
        string? leafName = null;

        while (currentCategoryId != Guid.Empty)
        {
            if (!visited.Add(currentCategoryId))
            {
                return null;
            }

            var category = await dbContext.CommerceHubCategories
                .AsNoTracking()
                .Where(item => item.Id == currentCategoryId && item.IsActive)
                .Select(item => new
                {
                    item.Id,
                    item.Name,
                    item.ParentId
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (category == null)
            {
                return null;
            }

            leafName ??= category.Name;
            names.Insert(0, category.Name);

            if (!category.ParentId.HasValue)
            {
                break;
            }

            if (category.ParentId.Value == category.Id)
            {
                return null;
            }

            currentCategoryId = category.ParentId.Value;
        }

        return leafName == null
            ? null
            : new CommerceHubCategoryLineageSnapshot(categoryId, leafName, names);
    }

    public static async Task<HashSet<Guid>> LoadLiveIdsAsync(
        ApplicationDbContext dbContext,
        IEnumerable<Guid> categoryIds,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<Guid>();
        foreach (var categoryId in categoryIds.Where(id => id != Guid.Empty).Distinct())
        {
            if (await IsLiveAsync(dbContext, categoryId, cancellationToken))
            {
                result.Add(categoryId);
            }
        }

        return result;
    }
}
