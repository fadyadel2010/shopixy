using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class ProductAuthoringReconstructionCharacterizationTests
{
    public static TheoryData<string, PublishStatus, bool, bool, IReadOnlyList<ProductAuthoringUnit>, IReadOnlyList<ProductAuthoringVariant>> ReconstructionCases()
    {
        var suppliedUnitId = Guid.NewGuid();
        var suppliedVariantId = Guid.NewGuid();
        var suppliedUnits = new List<ProductAuthoringUnit>
        {
            new(Guid.NewGuid(), suppliedUnitId, 1m, 4m, 9m, "SUP-UNIT", IsDefault: true, IsBaseUnit: true)
        };
        var suppliedVariants = new List<ProductAuthoringVariant>
        {
            new(suppliedVariantId, "SUP-VAR", "SUP-BC", 19m, 18m, 8m, IsActive: true, IsDefault: true, new Dictionary<string, string>
            {
                ["Color"] = "Green"
            })
        };

        return new TheoryData<string, PublishStatus, bool, bool, IReadOnlyList<ProductAuthoringUnit>, IReadOnlyList<ProductAuthoringVariant>>
        {
            { "Units omitted", PublishStatus.Published, false, true, Array.Empty<ProductAuthoringUnit>(), suppliedVariants },
            { "Variants omitted", PublishStatus.Published, true, false, suppliedUnits, Array.Empty<ProductAuthoringVariant>() },
            { "Units and variants supplied", PublishStatus.Published, true, true, suppliedUnits, suppliedVariants },
            { "Mixed update", PublishStatus.Published, false, true, Array.Empty<ProductAuthoringUnit>(), suppliedVariants },
            { "Partial draft update", PublishStatus.Draft, false, false, Array.Empty<ProductAuthoringUnit>(), Array.Empty<ProductAuthoringVariant>() },
            { "Partial published update", PublishStatus.Published, false, false, Array.Empty<ProductAuthoringUnit>(), Array.Empty<ProductAuthoringVariant>() }
        };
    }

    [Theory]
    [MemberData(nameof(ReconstructionCases))]
    public async Task ReconstructForAdmission_MatchesLegacyReconstruction(
        string scenario,
        PublishStatus publishStatus,
        bool productUnitsSubmitted,
        bool variantsSubmitted,
        IReadOnlyList<ProductAuthoringUnit> suppliedUnits,
        IReadOnlyList<ProductAuthoringVariant> suppliedVariants)
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var productId = await SeedCurrentProductGraphAsync(context, tenantId, publishStatus);
        var command = CreateUpdateCommand(
            tenantId,
            productId,
            publishStatus,
            productUnitsSubmitted,
            variantsSubmitted,
            suppliedUnits,
            suppliedVariants);
        var service = new ProductAuthoringReconstructionService(context, new TestTenantContext(tenantId));

        var legacy = await LegacyReconstructForAdmissionAsync(context, tenantId, command);
        var extracted = await service.ReconstructForAdmissionAsync(command);

        AssertReconstructedGraphEqual(legacy, extracted, scenario);
    }

    [Fact]
    public async Task OmittedCollections_ReconstructDefaultUnitAndDefaultVariantFromCurrentSqlState()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var productId = await SeedCurrentProductGraphAsync(context, tenantId, PublishStatus.Published);
        var command = CreateUpdateCommand(
            tenantId,
            productId,
            PublishStatus.Published,
            productUnitsSubmitted: false,
            variantsSubmitted: false,
            units: Array.Empty<ProductAuthoringUnit>(),
            variants: Array.Empty<ProductAuthoringVariant>());
        var service = new ProductAuthoringReconstructionService(context, new TestTenantContext(tenantId));

        Assert.True(await context.Products.AnyAsync(product => product.Id == productId), "Seeded Product is not visible through tenant filters.");
        Assert.True(await context.ProductUnits.IgnoreQueryFilters().AnyAsync(unit => unit.ProductId == productId), "Seeded ProductUnits were not persisted.");
        Assert.True(await context.ProductUnits.AnyAsync(unit => unit.ProductId == productId), "Seeded ProductUnits are not visible through tenant filters.");

        var reconstructed = await service.ReconstructForAdmissionAsync(command);

        var defaultUnit = Assert.Single(reconstructed.Units.Items, unit => unit.IsDefault);
        var baseUnit = Assert.Single(reconstructed.Units.Items, unit => unit.IsBaseUnit);
        var defaultVariant = Assert.Single(reconstructed.Variants, variant => variant.IsDefault);
        Assert.Equal(defaultUnit.UnitId, reconstructed.Units.DefaultUnitId);
        Assert.Equal(baseUnit.UnitId, reconstructed.Units.BaseUnitId);
        Assert.Equal("VAR-DEFAULT", defaultVariant.Sku);
        Assert.DoesNotContain(reconstructed.Units.Items, unit => unit.Barcode == "DELETED-UNIT");
        Assert.DoesNotContain(reconstructed.Variants, variant => variant.Sku == "DELETED-VAR");
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .EnableDetailedErrors()
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static async Task<Guid> SeedCurrentProductGraphAsync(
        ApplicationDbContext context,
        Guid tenantId,
        PublishStatus publishStatus)
    {
        var productId = Guid.NewGuid();
        var baseUnitId = Guid.NewGuid();
        var defaultUnitId = Guid.NewGuid();
        var deletedUnitId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var defaultVariantId = Guid.NewGuid();
        var deletedVariantId = Guid.NewGuid();

        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = categoryId,
            Name = "Reconstructed product",
            PublishStatus = publishStatus,
            IsOnlineVisible = publishStatus == PublishStatus.Published,
            HasVariants = publishStatus == PublishStatus.Published,
            CreatedAt = DateTime.UtcNow
        };

        context.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Reconstruction"
        });
        var baseUnit = new PointOfSale.Core.Entities.Unit { Id = baseUnitId, TenantId = tenantId, Name = "Piece", Symbol = "pc", IsActive = true };
        var defaultUnit = new PointOfSale.Core.Entities.Unit { Id = defaultUnitId, TenantId = tenantId, Name = "Box", Symbol = "box", IsActive = true };
        var deletedUnit = new PointOfSale.Core.Entities.Unit { Id = deletedUnitId, TenantId = tenantId, Name = "Deleted", Symbol = "del", IsActive = true };

        context.Units.AddRange(baseUnit, defaultUnit, deletedUnit);
        context.Products.Add(product);
        context.ProductUnits.AddRange(
            new ProductUnit
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                UnitId = baseUnitId,
                ConversionFactor = 1m,
                PurchasePrice = 3m,
                SellingPrice = 10m,
                Barcode = "BASE-UNIT",
                IsBaseUnit = true,
                IsDefault = false,
                Product = product,
                Unit = baseUnit
            },
            new ProductUnit
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                UnitId = defaultUnitId,
                ConversionFactor = 6m,
                PurchasePrice = 15m,
                SellingPrice = 60m,
                Barcode = "DEFAULT-UNIT",
                IsBaseUnit = false,
                IsDefault = true,
                Product = product,
                Unit = defaultUnit
            },
            new ProductUnit
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                UnitId = deletedUnitId,
                ConversionFactor = 12m,
                SellingPrice = 100m,
                Barcode = "DELETED-UNIT",
                IsBaseUnit = false,
                IsDefault = false,
                IsDeleted = true,
                Product = product,
                Unit = deletedUnit
            });

        context.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProductId = productId,
                SKU = "VAR-SECONDARY",
                Barcode = "VAR-SECONDARY-BC",
                Price = 11m,
                OnlinePrice = 12m,
                CostPrice = 5m,
                IsActive = true,
                IsDefault = false,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow,
                Attributes =
                {
                    new ProductVariantAttribute { Id = Guid.NewGuid(), Name = "Color", Value = "Blue" }
                }
            },
            new ProductVariant
            {
                Id = defaultVariantId,
                TenantId = tenantId,
                ProductId = productId,
                SKU = "VAR-DEFAULT",
                Barcode = "VAR-DEFAULT-BC",
                Price = 20m,
                OnlinePrice = 18m,
                CostPrice = 9m,
                IsActive = true,
                IsDefault = true,
                SortOrder = 1,
                CreatedAt = DateTime.UtcNow,
                Attributes =
                {
                    new ProductVariantAttribute { Id = Guid.NewGuid(), Name = "Size", Value = "XL" },
                    new ProductVariantAttribute { Id = Guid.NewGuid(), Name = "Ignored", Value = "Deleted", IsDeleted = true }
                }
            },
            new ProductVariant
            {
                Id = deletedVariantId,
                TenantId = tenantId,
                ProductId = productId,
                SKU = "DELETED-VAR",
                Price = 30m,
                CostPrice = 10m,
                IsActive = false,
                IsDefault = false,
                IsDeleted = true,
                CreatedAt = DateTime.UtcNow
            });

        await context.SaveChangesAsync();
        return productId;
    }

    private static ProductAuthoringCommand CreateUpdateCommand(
        Guid tenantId,
        Guid productId,
        PublishStatus publishStatus,
        bool productUnitsSubmitted,
        bool variantsSubmitted,
        IReadOnlyList<ProductAuthoringUnit> units,
        IReadOnlyList<ProductAuthoringVariant> variants) =>
        new(
            ProductAuthoringOperation.Update,
            new ProductAuthoringIdentity(tenantId, productId, Guid.NewGuid(), "SKU", "BARCODE"),
            new ProductAuthoringContent("Updated", null, null, null, null, null, [], [], []),
            new ProductAuthoringPricing(5m, 12m, 12m, false, 0m),
            new ProductAuthoringPublication(publishStatus, true, publishStatus == PublishStatus.Published, true, false),
            new ProductAuthoringSeo(null, null, null, null),
            new ProductAuthoringShippingProfile(0m, 0m, 0m, 0m, "kg", "cm"),
            new ProductAuthoringInventoryProfile("Piece", 0m, true, publishStatus == PublishStatus.Published, 10, false),
            new ProductAuthoringUnits(
                units.FirstOrDefault(unit => unit.IsBaseUnit)?.UnitId,
                units.FirstOrDefault(unit => unit.IsDefault)?.UnitId,
                units),
            variants,
            new ProductAuthoringMetadata(
                false,
                null,
                null,
                "default",
                Guid.NewGuid().ToString("N"),
                $"reconstruction-{Guid.NewGuid():N}",
                null,
                new Dictionary<string, string>
                {
                    ["productUnitsSubmitted"] = productUnitsSubmitted ? "true" : "false",
                    ["variantsSubmitted"] = variantsSubmitted ? "true" : "false"
                }));

    private static async Task<ProductAuthoringCommand> LegacyReconstructForAdmissionAsync(
        ApplicationDbContext context,
        Guid tenantId,
        ProductAuthoringCommand command)
    {
        if (command.Operation != ProductAuthoringOperation.Update ||
            !command.Identity.ProductId.HasValue ||
            command.Identity.ProductId.Value == Guid.Empty)
        {
            return command;
        }

        var reconstructed = command;
        if (!MetadataFlag(command, "productUnitsSubmitted"))
        {
            var currentUnits = await LegacyLoadCurrentAuthoringUnitsAsync(context, tenantId, command.Identity.ProductId.Value);
            reconstructed = reconstructed with
            {
                Units = new ProductAuthoringUnits(
                    currentUnits.FirstOrDefault(unit => unit.IsBaseUnit)?.UnitId,
                    currentUnits.FirstOrDefault(unit => unit.IsDefault)?.UnitId,
                    currentUnits)
            };
        }

        if (!MetadataFlag(command, "variantsSubmitted"))
        {
            reconstructed = reconstructed with
            {
                Variants = await LegacyLoadCurrentAuthoringVariantsAsync(context, tenantId, command.Identity.ProductId.Value)
            };
        }

        return reconstructed;
    }

    private static async Task<List<ProductAuthoringUnit>> LegacyLoadCurrentAuthoringUnitsAsync(
        ApplicationDbContext context,
        Guid tenantId,
        Guid productId)
    {
        var units = await context.ProductUnits
            .AsNoTracking()
            .Where(productUnit =>
                productUnit.ProductId == productId &&
                productUnit.Product.TenantId == tenantId &&
                !productUnit.IsDeleted)
            .ToListAsync();

        return units
            .Select(unit => new ProductAuthoringUnit(
                unit.Id,
                unit.UnitId,
                unit.ConversionFactor,
                unit.PurchasePrice,
                unit.SellingPrice,
                unit.Barcode,
                unit.IsDefault,
                unit.IsBaseUnit))
            .ToList();
    }

    private static async Task<List<ProductAuthoringVariant>> LegacyLoadCurrentAuthoringVariantsAsync(
        ApplicationDbContext context,
        Guid tenantId,
        Guid productId)
    {
        var variants = await context.ProductVariants
            .AsNoTracking()
            .WhereCurrentSemanticOwner()
            .Include(variant => variant.Attributes)
            .Where(variant =>
                variant.TenantId == tenantId &&
                variant.ProductId == productId &&
                !variant.IsDeleted)
            .ToListAsync();

        return variants
            .Select(variant => new ProductAuthoringVariant(
                variant.Id,
                variant.SKU,
                variant.Barcode,
                variant.Price,
                variant.OnlinePrice,
                variant.CostPrice,
                variant.IsActive,
                variant.IsDefault,
                variant.Attributes
                    .Where(attribute => !attribute.IsDeleted)
                    .ToDictionary(attribute => attribute.Name, attribute => attribute.Value)))
            .ToList();
    }

    private static bool MetadataFlag(ProductAuthoringCommand command, string key) =>
        command.Metadata.AdditionalMetadata.TryGetValue(key, out var value) &&
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static void AssertReconstructedGraphEqual(
        ProductAuthoringCommand expected,
        ProductAuthoringCommand actual,
        string scenario)
    {
        Assert.Equal(expected.Units.BaseUnitId, actual.Units.BaseUnitId);
        Assert.Equal(expected.Units.DefaultUnitId, actual.Units.DefaultUnitId);
        Assert.Equal(
            expected.Units.Items.OrderBy(unit => unit.UnitId).Select(UnitSnapshot),
            actual.Units.Items.OrderBy(unit => unit.UnitId).Select(UnitSnapshot));
        Assert.Equal(
            expected.Variants.OrderBy(variant => variant.VariantId).Select(VariantSnapshot),
            actual.Variants.OrderBy(variant => variant.VariantId).Select(VariantSnapshot));
        Assert.Equal(expected.Publication.PublishStatus, actual.Publication.PublishStatus);
    }

    private static object UnitSnapshot(ProductAuthoringUnit unit) => new
    {
        unit.ProductUnitId,
        unit.UnitId,
        unit.ConversionFactor,
        unit.PurchasePrice,
        unit.SellingPrice,
        unit.Barcode,
        unit.IsDefault,
        unit.IsBaseUnit
    };

    private static object VariantSnapshot(ProductAuthoringVariant variant) => new
    {
        variant.VariantId,
        variant.Sku,
        variant.Barcode,
        variant.Price,
        variant.OnlinePrice,
        variant.CostPrice,
        variant.IsActive,
        variant.IsDefault,
        Attributes = string.Join(
            "|",
            variant.Attributes
                .OrderBy(attribute => attribute.Key)
                .Select(attribute => $"{attribute.Key}={attribute.Value}"))
    };
}
