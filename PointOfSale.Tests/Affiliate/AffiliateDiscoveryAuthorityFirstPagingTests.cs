using System;
using System.IO;
using Xunit;

namespace PointOfSale.Tests.Affiliate;

public sealed class AffiliateDiscoveryAuthorityFirstPagingTests
{
    [Fact]
    public void Discovery_MustApplySqlAuthorityBeforeExactTotalAndFinalPagination()
    {
        var source = ReadRepositoryFile(
            "PointOfSale.Infrastructure",
            "Elasticsearch",
            "Services",
            "AffiliateProductDiscoveryService.cs");

        Assert.Contains(".Scroll(CandidateScrollTimeout)", source, StringComparison.Ordinal);
        Assert.Contains("ReadAllSearchCandidatesAsync", source, StringComparison.Ordinal);
        Assert.Contains("RecomposeDiscoveryItemsFromSqlAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("(categoryId.HasValue && product.CategoryId != categoryId.Value)", source, StringComparison.Ordinal);
        Assert.Contains("var total = deliverableItems.Count;", source, StringComparison.Ordinal);
        Assert.Contains("deliverableItems.Skip((int)skip).Take(query.PageSize)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".From(skip)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Total = items.Count", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(relativeSegments).ToArray()));
    }
}
