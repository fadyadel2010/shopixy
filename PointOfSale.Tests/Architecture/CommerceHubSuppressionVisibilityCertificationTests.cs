using System;
using System.IO;
using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSuppressionVisibilityCertificationTests
{
    [Fact]
    public void SuppressionVisibility_MustBeExposedWithActionableMetadata()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        // Suppressed listing visibility and its metadata must be strictly exposed
        Assert.Contains("IsSuppressed = listing.RestorePublicationOnActivate", controller, StringComparison.Ordinal);
        Assert.Contains("SuppressionReason = listing.RestorePublicationOnActivate", controller, StringComparison.Ordinal);
        Assert.Contains("SuppressionTimestampUtc = listing.RestorePublicationOnActivate ? listing.UpdatedAt : null", controller, StringComparison.Ordinal);
        Assert.Contains("SuppressionOwner = listing.RestorePublicationOnActivate ? \"MarketplaceGovernance\" : null", controller, StringComparison.Ordinal);
        
        // Suppressed status must reflect active governance-hidden state
        Assert.Contains("Marketplace governance hidden this listing. Merchant publication intent is preserved for conditional restore.", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void OrphanVisibility_MustExposeAllDisappearedCatalogLineages()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        // Orphaned listings must be visible, identifying when catalog authority disappeared
        Assert.Contains("IsOrphaned = invalidPublicAuthority", controller, StringComparison.Ordinal);
        Assert.Contains("productMissing ||", controller, StringComparison.Ordinal);
        Assert.Contains("variantMissing ||", controller, StringComparison.Ordinal);
        Assert.Contains("categoryMissing ||", controller, StringComparison.Ordinal);
        Assert.Contains("Product authority is missing or deleted.", controller, StringComparison.Ordinal);
        Assert.Contains("Variant authority is missing or deleted.", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHub category authority is missing or inactive.", controller, StringComparison.Ordinal);
        Assert.Contains("Product is no longer public CommerceHub authority.", controller, StringComparison.Ordinal);
        Assert.Contains("Variant is no longer active.", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void GovernanceBoundary_MustPreventMerchantEscalationOrVisibilityMutations()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        // Bulk operations must fail closed to prevent merchants from republishing suppressed or orphaned listings
        Assert.Contains("ApplySafeBulkPublish", controller, StringComparison.Ordinal);
        Assert.Contains("ApplySafeBulkCategoryUpdate", controller, StringComparison.Ordinal);
        Assert.Contains("if (listing.RestorePublicationOnActivate)", controller, StringComparison.Ordinal);
        Assert.Contains("throw new CommerceHubValidationException(\"Suppressed listings cannot be republished by merchant bulk operation.\")", controller, StringComparison.Ordinal);
        Assert.Contains("throw new CommerceHubValidationException(\"Suppressed listings cannot be category-mutated by merchant bulk operation.\")", controller, StringComparison.Ordinal);
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
