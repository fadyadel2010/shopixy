namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontVersioningAuthorityCertificationTests
{
    [Fact]
    public void RollbackMustCreateMonotonicPublicationAndPreserveCurrentDrafts()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");

        Assert.Contains("restoredFromVersion: versionNumber", service, StringComparison.Ordinal);
        Assert.Contains("publishedVersion: config.LiveVersion", service, StringComparison.Ordinal);
        Assert.Contains("CreateAndAppendVersionSnapshot(", service, StringComparison.Ordinal);
        Assert.Contains("ExtractHistoricalLiveState", service, StringComparison.Ordinal);
        Assert.Contains("PreserveDraftBranches(currentRoot, mergedRoot)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("config.LiveVersion = versionNumber", service, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalSnapshotsMustRemainSubordinateToCurrentThemeAuthority()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");

        Assert.Contains("ValidateRollbackThemeAuthorityAsync", service, StringComparison.Ordinal);
        Assert.Contains("_dbContext.ThemePresets", service, StringComparison.Ordinal);
        Assert.Contains("ValidateThemeOverrideShape(overridesObject)", service, StringComparison.Ordinal);
        Assert.Contains("theme preset", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VersionApisMustRemainTenantAndConcurrencyBound()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
        Assert.Contains("ValidateTenantHeader(traceId)", controller, StringComparison.Ordinal);
        Assert.Contains("RequireExpectedRowVersion(traceId)", controller, StringComparison.Ordinal);
        Assert.Contains(".Where", ReadRepoFile("PointOfSale.API/Services/Admin/StoreConfigAuditService.cs"), StringComparison.Ordinal);
        Assert.Contains("c.TenantId == tenantId", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionReplayMustReloadCurrentSqlInsteadOfSnapshotPayload()
    {
        var consumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs");

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentStoreConfigProjectionAuthorityAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshStoreConfigEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("KeyDeleteAsync", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifactsMustRecordP2GeTruth()
    {
        Assert.Contains(
            "PASS P2-GE Storefront Versioning Authority",
            ReadRepoFile("docs/architecture/storefront-versioning-authority-certification-findings.md"),
            StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("PASS P2-GE", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
