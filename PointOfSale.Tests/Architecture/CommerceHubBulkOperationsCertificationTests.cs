using System;
using System.IO;
using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubBulkOperationsCertificationTests
{
    [Fact]
    public void BulkOperations_MustProcessPerRowWithPartialFailuresAndScoping()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        // Tenant scope must be strictly preserved during bulk processing
        Assert.Contains("var tenantId = _tenantContext.RequiredTenantId;", controller, StringComparison.Ordinal);
        Assert.Contains("listing.TenantId == tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("variant.TenantId == tenantId", controller, StringComparison.Ordinal);

        // Per-row processing and outcome mapping must handle partial failures
        Assert.Contains("foreach (var listingId in listingIds)", controller, StringComparison.Ordinal);
        Assert.Contains("response.Outcomes.Add(new CommerceHubBulkListingOperationOutcomeDto", controller, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex) when (ex is CommerceHubValidationException or InvalidOperationException or ArgumentException)", controller, StringComparison.Ordinal);

        // All supported operational actions must be frozen
        Assert.Contains("\"unpublish\" => listing.Unpublish(),", controller, StringComparison.Ordinal);
        Assert.Contains("\"publish\" => ApplySafeBulkPublish(listing, variant, normalizedProductCurrency, liveCurrentCategoryIds),", controller, StringComparison.Ordinal);
        Assert.Contains("\"remove_override\" => ApplySafeBulkRemoveOverride(", controller, StringComparison.Ordinal);
        Assert.Contains("\"update_category\" => ApplySafeBulkCategoryUpdate(listing, variant, request.CategoryId!.Value, normalizedProductCurrency),", controller, StringComparison.Ordinal);

        // Visibility-increasing bulk operations must not bypass the single-listing authority gates.
        Assert.Contains("_userGovernanceService.EnsureVendorListingAllowedAsync", controller, StringComparison.Ordinal);
        Assert.Contains("TenantHasActiveShippingAsync", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", controller, StringComparison.Ordinal);
        Assert.Contains("EnsureLiveCurrentCategoryForBulk", controller, StringComparison.Ordinal);
        Assert.Contains("variant.SemanticLifecycleState != ProductVariantSemanticLifecycleState.Current", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void BulkOperations_MustPublishEventsAndEnforceAuditability()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        // Operations must publish integration events to allow downstream systems to converge asynchronously
        Assert.Contains("await PublishBulkListingEventAsync(operation, listing, variant, cancellationToken);", controller, StringComparison.Ordinal);
        Assert.Contains("new CommerceHubListingUnpublishedEvent", controller, StringComparison.Ordinal);
        Assert.Contains("new CommerceHubListingUpdatedEvent", controller, StringComparison.Ordinal);

        // Logging audits must record final operational outcomes
        Assert.Contains("_logger.LogInformation(", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHub bulk listing operation completed | TenantId: {TenantId} | Operation: {Operation} | Requested: {Requested} | Succeeded: {Succeeded} | Failed: {Failed}", controller, StringComparison.Ordinal);
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
