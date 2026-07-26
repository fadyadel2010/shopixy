using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class SettingsAuthorityIsolationArchitectureTests
{
    [Fact]
    public void TenantFacingSettings_MustBeQueriedAndWrittenWithTenantScope()
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var controller = File.ReadAllText(Path.Combine(solutionRoot, "PointOfSale.API", "Controllers", "SettingsController.cs"));
        var taxService = File.ReadAllText(Path.Combine(solutionRoot, "PointOfSale.API", "Services", "Pricing", "TaxCalculationService.cs"));
        var systemSetting = File.ReadAllText(Path.Combine(solutionRoot, "PointOfSale.Core", "Entities", "SystemSetting.cs"));
        var dbContext = File.ReadAllText(Path.Combine(solutionRoot, "PointOfSale.Infrastructure", "Data", "ApplicationDbContext.cs"));
        
        var migrationsFolder = Path.Combine(solutionRoot, "PointOfSale.Infrastructure", "Migrations");
        var migrationFiles = Directory.GetFiles(migrationsFolder, "*TenantScopedSystemSettingsAuthority*.cs")
            .Where(file => !file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName)
            .ToList();
        Assert.NotEmpty(migrationFiles);

        Assert.Contains("public Guid? TenantId { get; set; }", systemSetting, StringComparison.Ordinal);
        Assert.Contains("LoadActiveTenantSettingsDictionaryAsync(tenantId)", controller, StringComparison.Ordinal);
        Assert.Contains("UpdateSettingAsync(Guid tenantId, string key, string value, string category)", controller, StringComparison.Ordinal);
        Assert.Contains("!s.IsDeleted && s.TenantId == tenantId && s.Key == key", controller, StringComparison.Ordinal);
        Assert.Contains("!setting.IsDeleted && setting.TenantId == tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("setting.TenantId == tenantId &&", taxService, StringComparison.Ordinal);
        Assert.Contains("HasIndex(e => new { e.TenantId, e.Key })", dbContext, StringComparison.Ordinal);

        var migrationContainsExpectedText = migrationFiles.Any(file =>
            File.ReadAllText(file).Contains("duplicate global tenant-facing SystemSettings rows exist", StringComparison.Ordinal));
        Assert.True(migrationContainsExpectedText,
            "At least one migration file should contain the text 'duplicate global tenant-facing SystemSettings rows exist'");
        Assert.DoesNotContain("await _context.SystemSettings.ToListAsync()", controller, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(setting => setting.Key == \"Tax.Enable\" || setting.Key == \"Tax.Rate\")", taxService, StringComparison.Ordinal);
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
