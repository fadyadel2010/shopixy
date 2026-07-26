namespace PointOfSale.Tests.Architecture;

public sealed class GeographicAdministrationPosAdminOwnershipTests
{
    [Fact]
    public void GeographicAdministration_MustNotBePlacedUnderSuperAdmin()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var superAdminFiles = Directory.EnumerateFiles(Path.Combine(root, "PointOfSale.API", "SuperAdmin"), "*.cs", SearchOption.AllDirectories);

        foreach (var file in superAdminFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("GeographicAdministration", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CarrierGeographic", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GeographicCatalog", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CountryProfile", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GeographicAdministration_ControllerMustBePosAdminOwned()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminGeographicAdministrationController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/GeographicAdministrationService.cs");
        var dtos = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/GeographicAdministration/GeographicAdministrationDtos.cs");

        Assert.Contains("[Route(\"api/admin/geographic\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("api/super-admin", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequireSuperAdmin", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperAdmin", dtos, StringComparison.Ordinal);
        Assert.Contains("GeographicAuditActorKind.PosAdmin", service, StringComparison.Ordinal);
        Assert.Contains("FourEyesApprovalRequired", service, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminArchitecture_MustRemainMarketplaceOnlyForGeographicRegression()
    {
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");

        Assert.Contains("POS Admin owns Geographic Administration", projectContext, StringComparison.Ordinal);
        Assert.Contains("Super Admin owns ONLY CommerceHub Marketplace", projectContext, StringComparison.Ordinal);
        Assert.Contains("POS Admin owns Geographic Administration", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Super Admin owns ONLY CommerceHub Marketplace", architectureTruth, StringComparison.Ordinal);
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
