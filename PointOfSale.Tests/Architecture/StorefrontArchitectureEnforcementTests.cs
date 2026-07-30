namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontArchitectureEnforcementTests
{
    [Fact]
    public void ProductUnitDatabaseInvariants_MustBeMachineEnforced()
    {
        var report = CertificationSource.ReadRepoFile(
            "docs/architecture/storefront-arch-2-machine-enforced-governance.md");
        var dbContext = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20260729120000_StorefrontProductUnitDbEnforcement.cs");

        Assert.Contains("SHOPIXY ARCH-2 Machine-Enforced Governance", report, StringComparison.Ordinal);
        Assert.Contains("Final certification: ARCHITECTURE HARDENED", report, StringComparison.Ordinal);
        Assert.Contains("Database Enforcement Report", report, StringComparison.Ordinal);
        Assert.Contains("Projection Admission Policy", report, StringComparison.Ordinal);
        Assert.Contains("Architecture Enforcement Matrix", report, StringComparison.Ordinal);

        Assert.Contains("CK_ProductUnits_ConversionFactor_Positive", dbContext, StringComparison.Ordinal);
        Assert.Contains("\\\"ConversionFactor\\\" > 0", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_ProductUnits_BaseUnit_ConversionFactor_One", dbContext, StringComparison.Ordinal);
        Assert.Contains("NOT \\\"IsBaseUnit\\\" OR \\\"ConversionFactor\\\" = 1", dbContext, StringComparison.Ordinal);

        Assert.Contains("ARCH-2 ProductUnit enforcement blocked", migration, StringComparison.Ordinal);
        Assert.Contains("WHERE \"IsDeleted\" = false", migration, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT \"CK_ProductUnits_ConversionFactor_Positive\"", migration, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT \"CK_ProductUnits_BaseUnit_ConversionFactor_One\"", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionWrites_MustPassAdmissionBeforePersistence()
    {
        var admission = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionAdmissionPolicy.cs");
        var boundary = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs");
        var mongoRegistrations = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/MongoDbServiceCollectionExtensions.cs");
        var writer = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs");

        Assert.Contains("public interface IStorefrontProductProjectionAdmissionPolicy", admission, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorityIdentity", admission, StringComparison.Ordinal);
        Assert.Contains("RequireCurrentProjectionContract", admission, StringComparison.Ordinal);
        Assert.Contains("RequireDefaultUnitAdmission", admission, StringComparison.Ordinal);
        Assert.Contains("RequireVariantAdmission", admission, StringComparison.Ordinal);
        Assert.Contains("RequireValidPublicationState", admission, StringComparison.Ordinal);
        Assert.Contains("RequireComputedStorefrontEffectivePrice", admission, StringComparison.Ordinal);
        Assert.Contains("STOREFRONT_PRODUCT_ADMISSION_DEFAULT_UNIT_REQUIRED", admission, StringComparison.Ordinal);
        Assert.Contains("STOREFRONT_PRODUCT_ADMISSION_VARIANT_ID_REQUIRED", admission, StringComparison.Ordinal);
        Assert.Contains("STOREFRONT_PRODUCT_ADMISSION_PROJECTION_VERSION_STALE", admission, StringComparison.Ordinal);

        Assert.True(
            boundary.IndexOf("_admissionPolicy.Validate(mutation)", StringComparison.Ordinal) <
            boundary.IndexOf("_repository.UpsertAsync(projection", StringComparison.Ordinal));
        Assert.Contains("AddScoped<IStorefrontProductProjectionAdmissionPolicy, StorefrontProductProjectionAdmissionPolicy>()", mongoRegistrations, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticAnalysisRules_MustBlockDuplicatedStorefrontBusinessAuthority()
    {
        var productCardRuntime = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/components/products/ProductCardRuntime.tsx");
        var runtimeSectionContainer = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx");
        var productHero = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx");
        var useProducts = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/hooks/useProducts.ts");
        var cartService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Cart/CartService.cs");
        var productService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontProductService.cs");

        Assert.Contains("product.availability?.canAddToCart === true", productCardRuntime, StringComparison.Ordinal);
        Assert.Contains("availability?.canAddToCart === true", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.Contains("model.canAddToCart", productHero, StringComparison.Ordinal);
        Assert.Contains("const availability = item.availability;", useProducts, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(product)", cartService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant)", cartService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", productService, StringComparison.Ordinal);

        Assert.DoesNotContain("calculateDiscountedPrice", productCardRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("promotionDiscountValue / 100", productCardRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("product.isAvailable && product.inStock", productCardRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedVariant.totalStock", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.DoesNotContain("item.hasVariants ? true : item.inStock", useProducts, StringComparison.Ordinal);
        Assert.DoesNotContain("variant.OnlinePrice ?? variant.Price", cartService, StringComparison.Ordinal);
        Assert.DoesNotContain("product.StorefrontEffectivePrice", cartService, StringComparison.Ordinal);
    }

    [Fact]
    public void CiPolicy_MustExecuteGovernanceAdmissionStaticAndMutationGates()
    {
        var workflow = CertificationSource.ReadRepoFile(
            ".github/workflows/architecture-governance.yml");

        Assert.Contains("ARCH-2 Machine-Enforced Governance", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj", workflow, StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName~PointOfSale.Tests.Architecture", workflow, StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName~ProductSqlAuthorityProjectionWriterTests", workflow, StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName~StorefrontArchitectureMutationTests", workflow, StringComparison.Ordinal);
        Assert.Contains("npm test -- --run", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run build", workflow, StringComparison.Ordinal);
    }
}
