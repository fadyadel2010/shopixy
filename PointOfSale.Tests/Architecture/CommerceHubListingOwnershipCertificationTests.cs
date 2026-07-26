using System;
using System.IO;
using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubListingOwnershipCertificationTests
{
    [Fact]
    public void ListingOwnership_MustRemainTenantBoundAndPreventDrift()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var configuration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubListingConfiguration.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        // ── Tenant isolation guard must be enforced in all command handlers ──
        Assert.Contains("CommerceHubTenantIsolationGuard.EnsureTenantAccess", handlers, StringComparison.Ordinal);

        // ── EF Core configuration must enforce tenant-scoped unique constraint ──
        Assert.Contains("UQ_CommerceHubListings_TenantId_VariantId", configuration, StringComparison.Ordinal);
        Assert.Contains("builder.Property(x => x.TenantId)", configuration, StringComparison.Ordinal);
        Assert.Contains(".IsRequired()", configuration, StringComparison.Ordinal);

        // ── Admin controller must require Owner/Admin authorization ──
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);

        // ── Handlers must resolve tenant identity from trusted middleware ──
        Assert.Contains("tenantContext.RequiredTenantId", handlers, StringComparison.Ordinal);

        // ── Handlers must enforce variant lineage within tenant boundary ──
        Assert.Contains(".WherePubliclyVisibleLineage(", handlers, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryAndProjection_MustIsolateWithinTenantBoundaries()
    {
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");

        // ── Projection consumer must validate TenantId on incoming messages ──
        Assert.Contains("context.Message.TenantId", projection, StringComparison.Ordinal);

        // ── Projection consumer must use ProjectionConsumerEventFence for idempotency ──
        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);

        // ── Projection consumer must apply SQL authority overlay ──
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", projection, StringComparison.Ordinal);

        // ── Auto-healing must detect and block cross-tenant access ──
        Assert.Contains("[SECURITY] Cross-tenant access attempt detected", autoHealing, StringComparison.Ordinal);

        // ── Auto-healing must scope recovery identity by tenant ──
        Assert.Contains("RecoveryIdentity(document.TenantId, document.Id)", autoHealing, StringComparison.Ordinal);
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
