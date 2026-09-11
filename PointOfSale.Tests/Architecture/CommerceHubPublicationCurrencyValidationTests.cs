namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPublicationCurrencyValidationTests
{
    [Fact]
    public void PublishRepublishAndUpdate_MustValidateProductCurrencyBeforeListingMutation()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");

        Assert.Contains("var variant = await LoadVariantAsync(command.TenantId, command.VariantId, cancellationToken);", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(tenant.Currency);", handlers, StringComparison.Ordinal);
        Assert.Contains("listing = CommerceHubListing.Create(", handlers, StringComparison.Ordinal);
        Assert.Contains("var changed = listing.Publish(command.CategoryId, normalizedAttrs, schemaSnapshot);", handlers, StringComparison.Ordinal);
        Assert.Contains("changed = listing.Update(", handlers, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminPublicationUi_MustExposeAndCommunicateCurrencyPolicy()
    {
        var dtos = ReadRepoFile("PointOfSale.API/DTOs/CommerceHub/CommerceHubAdminDtos.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");
        var ui = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubSettings.jsx");

        Assert.Contains("public string ProductCurrency { get; set; } = \"EGP\";", dtos, StringComparison.Ordinal);
        Assert.Contains("public bool IsCommerceHubCurrencyEligible { get; set; } = true;", dtos, StringComparison.Ordinal);
        Assert.Contains("public string? CommerceHubCurrencyPolicyMessage { get; set; }", dtos, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPublicationCurrencyPolicy.BuildUnsupportedCurrencyMessage", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHub currently supports EGP products only.", ui, StringComparison.Ordinal);
        Assert.Contains("disabled={isSaving || !state.categoryId || isCurrencyBlocked || isPublicationBlocked || !canManageCatalog}", ui, StringComparison.Ordinal);
        Assert.Contains("showError(getCommerceHubCurrencyPolicyMessage(variant))", ui, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
