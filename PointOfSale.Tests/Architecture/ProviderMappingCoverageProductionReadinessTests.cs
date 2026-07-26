namespace PointOfSale.Tests.Architecture;

public sealed class ProviderMappingCoverageProductionReadinessTests
{
    [Fact]
    public void Resolver_UsesLatestActivatedPublication_IncludingRollback()
    {
        var contracts = Read("PointOfSale.Core/Interfaces/Geographic/GeographicContracts.cs");
        var repository = Read("PointOfSale.Infrastructure/Geographic/GeographicRepository.cs");

        Assert.Contains("IQueryable<CarrierGeographicMappingPublication> MappingPublications", contracts, StringComparison.Ordinal);
        Assert.Contains("from publication in _repository.MappingPublications", repository, StringComparison.Ordinal);
        Assert.Contains("publication.ActivatedAtUtc != null", repository, StringComparison.Ordinal);
        Assert.Contains("orderby publication.ActivatedAtUtc descending", repository, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderByDescending(release => release.EffectiveFromUtc)", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_ContainsNoPublishedProviderMappingSeed_DisguisedAsRuntimeAuthority()
    {
        var root = FindRoot();
        var migrationDirectory = Path.Combine(root, "PointOfSale.Infrastructure", "Migrations");
        var migrationSources = Directory
            .EnumerateFiles(migrationDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(
            migrationSources,
            source => source.Contains("INSERT INTO \"CarrierGeographicMappings\"", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            migrationSources,
            source => source.Contains("INSERT INTO \"CarrierProviders\"", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("BostaAdapter.cs")]
    [InlineData("AramexAdapter.cs")]
    [InlineData("MylerzAdapter.cs")]
    [InlineData("DhlAdapter.cs")]
    [InlineData("ManualCarrierDefinition.cs")]
    public void Adapters_RemainWireSerializers_WithoutGeographicAuthority(string adapterFile)
    {
        var source = Read($"PointOfSale.Infrastructure/Shipping/Adapters/{adapterFile}");

        Assert.DoesNotContain("ApplicationDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierGeographicMappingService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GeographicPlaceAlias", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GeographicPlaceHierarchyEdge", source, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        return File.ReadAllText(Path.Combine(
            FindRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
