using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class PosProductAuthorityCertificationTests
{
    [Fact]
    public void ProductOwnershipAndLifecycle_MustRemainTenantScopedAndRetireChildren()
    {
        var products = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("e.TenantId == CurrentTenantId", dbContext, StringComparison.Ordinal);
        Assert.Contains("var liveVariants = await _context.ProductVariants", products, StringComparison.Ordinal);
        Assert.Contains("variant.IsDeleted = true;", products, StringComparison.Ordinal);
        Assert.Contains("variant.IsActive = false;", products, StringComparison.Ordinal);
        Assert.Contains("product.IsOnlineVisible = false;", products, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus = PublishStatus.Archived;", products, StringComparison.Ordinal);
        Assert.Contains("new ProductVariantDeletedEvent", products, StringComparison.Ordinal);
        Assert.Contains("new VariantDeletedEvent", products, StringComparison.Ordinal);
    }

    [Fact]
    public void VariantAuthority_MustRejectSupersededAndSimpleProductManipulation()
    {
        var products = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");
        var variants = ReadRepoFile("PointOfSale.API/Controllers/ProductVariantsController.cs");

        Assert.Contains("ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(v)", products, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentSemanticOwner()", variants, StringComparison.Ordinal);
        Assert.Contains("SimpleProductAdditionalVariantForbidden", variants, StringComparison.Ordinal);
        Assert.Contains("SimpleProductNonDefaultVariantForbidden", variants, StringComparison.Ordinal);
        Assert.Contains("SimpleProductDefaultVariantReassignmentForbidden", variants, StringComparison.Ordinal);
    }

    [Fact]
    public void CategoryAndUnitAuthority_MustUseCurrentTenantSqlLineage()
    {
        var products = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");
        var categories = ReadRepoFile("PointOfSale.API/Services/Catalog/CategoryLineageAuthority.cs");
        var units = ReadRepoFile("PointOfSale.API/Controllers/UnitsController.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("RequireLiveCategoryAsync", products, StringComparison.Ordinal);
        Assert.Contains("category.TenantId == tenantId", categories, StringComparison.Ordinal);
        Assert.Contains("candidate.TenantId == tenantId", units, StringComparison.Ordinal);
        Assert.Contains("candidate.IsActive", products, StringComparison.Ordinal);
        Assert.Contains("e.Product.TenantId == CurrentTenantId", dbContext, StringComparison.Ordinal);
        Assert.Contains("e.Unit.TenantId == CurrentTenantId", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void SkuAndBarcodeAuthority_MustIncludeProductsVariantsAndProductUnits()
    {
        var products = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");
        var authority = ReadRepoFile("PointOfSale.Infrastructure/Reliability/ProductVariantCloneImportAuthority.cs");
        var productConfig = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/ProductConfiguration.cs");
        var variantConfig = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/ProductVariantConfiguration.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260607190000_PosProductAuthorityHardening.cs");

        Assert.Contains("IsProductUnitBarcodeOwnedByAnotherSemanticEntityAsync", products, StringComparison.Ordinal);
        Assert.Contains("dbContext.ProductVariants", authority, StringComparison.Ordinal);
        Assert.Contains("dbContext.Products", authority, StringComparison.Ordinal);
        Assert.Contains("dbContext.ProductUnits", authority, StringComparison.Ordinal);
        Assert.Contains("variant.TenantId == tenantId", authority, StringComparison.Ordinal);
        Assert.Contains("new { p.TenantId, p.SKU }", productConfig, StringComparison.Ordinal);
        Assert.Contains("new { p.TenantId, p.Barcode }", productConfig, StringComparison.Ordinal);
        Assert.Contains("new { v.TenantId, v.SKU }", variantConfig, StringComparison.Ordinal);
        Assert.Contains("new { v.TenantId, v.Barcode }", variantConfig, StringComparison.Ordinal);
        Assert.Contains("IX_Products_TenantId_Barcode", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_Products_TenantId_SKU", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_Products_TenantId_Slug", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_ProductUnits_Barcode_Active", dbContext, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", migration, StringComparison.Ordinal);
        Assert.Contains("TR_Products_CatalogBarcodeAuthority", migration, StringComparison.Ordinal);
        Assert.Contains("TR_ProductVariants_CatalogBarcodeAuthority", migration, StringComparison.Ordinal);
        Assert.Contains("TR_ProductUnits_CatalogBarcodeAuthority", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchAndQueryAuthority_MustExcludeStaleVariantOwners()
    {
        var products = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");

        Assert.Contains(
            "v.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current",
            products,
            StringComparison.Ordinal);
        Assert.Contains("v.SemanticSupersededAtUtc == null", products, StringComparison.Ordinal);
        Assert.Contains("v.SemanticReplacementVariantId == null", products, StringComparison.Ordinal);
        Assert.Contains(
            "ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(v)",
            products,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProductUnitSqlAuthority_MustFreezeBindingBaseDefaultAndBarcodeUniqueness()
    {
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260607190000_PosProductAuthorityHardening.cs");

        Assert.Contains("UX_ProductUnits_ProductId_UnitId_Active", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_ProductUnits_ProductId_Base_Active", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_ProductUnits_ProductId_Default_Active", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_ProductUnits_Barcode_Active", dbContext, StringComparison.Ordinal);
        Assert.Contains("migration blocked: duplicate live ProductUnit bindings", migration, StringComparison.Ordinal);
        Assert.Contains("migration blocked: products with multiple live base units", migration, StringComparison.Ordinal);
        Assert.Contains("migration blocked: products with multiple live default units", migration, StringComparison.Ordinal);
        Assert.Contains("migration blocked: duplicate live ProductUnit barcodes", migration, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
