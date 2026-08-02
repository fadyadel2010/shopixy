using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class ProductAuthoringVariantTemplateRecoverySourceTests
{
    [Fact]
    public void ProductAuthoringOrchestrator_MustMatchGeneratedVariantDrafts_ByCanonicalAttributeKey()
    {
        var source = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "ProductAuthoring",
            "ProductAuthoringOrchestrator.cs");

        Assert.Contains("ResolveAuthoredExistingVariantIds(authoredVariants, existingById, existingByAttributeKey)", source, StringComparison.Ordinal);
        Assert.Contains("ResolveExistingAuthoredVariant(", source, StringComparison.Ordinal);
        Assert.Contains("ComputeAuthoredVariantAttributeCombinationKey", source, StringComparison.Ordinal);
        Assert.Contains("ComputeExistingVariantAttributeCombinationKey", source, StringComparison.Ordinal);
        Assert.Contains("ProductVariantAttributeCombinationKey.Compute", source, StringComparison.Ordinal);
        Assert.Contains("!desiredExistingIds.Contains(existing.Id)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductResponseVariantReference_MustExposeCanonicalAuthoringFields()
    {
        var dtoSource = ReadRepoFile(
            "PointOfSale.API",
            "DTOs",
            "EntityExposureResponseDtos.cs");
        var mapperSource = ReadRepoFile(
            "PointOfSale.API",
            "Mappers",
            "SafeResponseMapper.cs");

        Assert.Contains("public decimal CostPrice { get; set; }", dtoSource, StringComparison.Ordinal);
        Assert.Contains("public decimal? Weight { get; set; }", dtoSource, StringComparison.Ordinal);
        Assert.Contains("public Guid? MediaId { get; set; }", dtoSource, StringComparison.Ordinal);
        Assert.Contains("CostPrice = variant.CostPrice", mapperSource, StringComparison.Ordinal);
        Assert.Contains("Weight = variant.Weight", mapperSource, StringComparison.Ordinal);
        Assert.Contains("MediaId = variant.MediaId", mapperSource, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] pathParts)
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(new[] { repoRoot }.Concat(pathParts).ToArray());
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PointOfSale repository root.");
    }
}
