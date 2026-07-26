using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using Xunit;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ProductVariantCloneImportAuthorityTests
{
    [Fact]
    public async Task ResolveUniqueSkuAsync_Regenerates_WhenClonePayloadCarriesExistingVariantSku()
    {
        var tenantId = Guid.NewGuid();
        var sourceProductId = Guid.NewGuid();
        var sourceVariantId = Guid.NewGuid();
        var cloneProductId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(CreateProduct(tenantId, sourceProductId, "Source"));
        db.ProductVariants.Add(new ProductVariant
        {
            Id = sourceVariantId,
            TenantId = tenantId,
            ProductId = sourceProductId,
            SKU = "SKU-CLONE",
            Barcode = "BC-SOURCE",
            Price = 10m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resolved = await ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync(
            db,
            tenantId,
            "SKU-CLONE",
            null,
            cloneProductId,
            Guid.Empty,
            CancellationToken.None);

        Assert.Equal("SKU-CLONE-1", resolved);
    }

    [Fact]
    public async Task ResolveUniqueSkuAsync_RejectsDetachedProductLevelOwnershipAsSemanticDuplicate()
    {
        var tenantId = Guid.NewGuid();
        var existingProductId = Guid.NewGuid();
        var cloneProductId = Guid.NewGuid();
        await using var db = CreateDbContext();

        var existingProduct = CreateProduct(tenantId, existingProductId, "Existing");
        existingProduct.SKU = "PRODUCT-SKU";
        db.Products.Add(existingProduct);
        await db.SaveChangesAsync();

        var resolved = await ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync(
            db,
            tenantId,
            "PRODUCT-SKU",
            null,
            cloneProductId,
            Guid.Empty,
            CancellationToken.None);

        Assert.Equal("PRODUCT-SKU-1", resolved);
    }

    [Fact]
    public async Task ResolveUniqueSkuAsync_AllowsSameSkuOwnedByDifferentTenant()
    {
        var ownerTenantId = Guid.NewGuid();
        var requestingTenantId = Guid.NewGuid();
        var ownerProductId = Guid.NewGuid();
        var cloneProductId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(CreateProduct(ownerTenantId, ownerProductId, "Owner"));
        db.ProductVariants.Add(new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = ownerTenantId,
            ProductId = ownerProductId,
            SKU = "SHARED-SKU",
            Price = 10m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resolved = await ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync(
            db,
            requestingTenantId,
            "SHARED-SKU",
            null,
            cloneProductId,
            Guid.Empty,
            CancellationToken.None);

        Assert.Equal("SHARED-SKU", resolved);
    }

    [Fact]
    public async Task IsBarcodeOwnedByAnotherSemanticEntityAsync_BlocksOrphanMappingCarryover()
    {
        var tenantId = Guid.NewGuid();
        var sourceProductId = Guid.NewGuid();
        var cloneProductId = Guid.NewGuid();
        await using var db = CreateDbContext();

        var sourceProduct = CreateProduct(tenantId, sourceProductId, "Source");
        sourceProduct.Barcode = "BAR-CARRYOVER";
        db.Products.Add(sourceProduct);
        await db.SaveChangesAsync();

        var conflict = await ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync(
            db,
            tenantId,
            "BAR-CARRYOVER",
            cloneProductId,
            Guid.Empty,
            CancellationToken.None);

        Assert.True(conflict);
    }

    [Fact]
    public async Task IsBarcodeOwnedByAnotherSemanticEntityAsync_AllowsSameBarcodeOwnedByDifferentTenant()
    {
        var ownerTenantId = Guid.NewGuid();
        var requestingTenantId = Guid.NewGuid();
        var ownerProductId = Guid.NewGuid();
        var cloneProductId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(CreateProduct(ownerTenantId, ownerProductId, "Owner"));
        db.ProductVariants.Add(new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = ownerTenantId,
            ProductId = ownerProductId,
            Barcode = "SHARED-BARCODE",
            Price = 10m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var conflict = await ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync(
            db,
            requestingTenantId,
            "SHARED-BARCODE",
            cloneProductId,
            Guid.Empty,
            CancellationToken.None);

        Assert.False(conflict);
    }

    [Fact]
    public async Task ProductUnitBarcodeAuthority_BlocksProductVariantAndSiblingUnitCollisions()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var otherProductId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var productUnitId = Guid.NewGuid();
        await using var db = CreateDbContext();

        var product = CreateProduct(tenantId, productId, "Product");
        var otherProduct = CreateProduct(tenantId, otherProductId, "Other");
        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = unitId,
            TenantId = tenantId,
            Name = "Carton",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.AddRange(product, otherProduct, unit);
        db.ProductVariants.Add(new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = otherProductId,
            Barcode = "VARIANT-BARCODE",
            Price = 10m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });
        db.ProductUnits.Add(new ProductUnit
        {
            Id = productUnitId,
            ProductId = productId,
            UnitId = unitId,
            Barcode = "UNIT-BARCODE",
            ConversionFactor = 12m,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True(await ProductVariantCloneImportAuthority
            .IsProductUnitBarcodeOwnedByAnotherSemanticEntityAsync(
                db,
                tenantId,
                "VARIANT-BARCODE",
                productId,
                Guid.Empty,
                CancellationToken.None));

        Assert.True(await ProductVariantCloneImportAuthority
            .IsProductUnitBarcodeOwnedByAnotherSemanticEntityAsync(
                db,
                tenantId,
                "UNIT-BARCODE",
                otherProductId,
                Guid.Empty,
                CancellationToken.None));

        Assert.False(await ProductVariantCloneImportAuthority
            .IsProductUnitBarcodeOwnedByAnotherSemanticEntityAsync(
                db,
                tenantId,
                "UNIT-BARCODE",
                productId,
                productUnitId,
                CancellationToken.None));
    }

    [Fact]
    public async Task ProductUnitBarcodeAuthority_AllowsSameBarcodeOwnedByDifferentTenant()
    {
        var ownerTenantId = Guid.NewGuid();
        var requestingTenantId = Guid.NewGuid();
        var ownerProductId = Guid.NewGuid();
        var requestingProductId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.AddRange(
            CreateProduct(ownerTenantId, ownerProductId, "Owner"),
            CreateProduct(requestingTenantId, requestingProductId, "Requester"));
        db.ProductVariants.Add(new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = ownerTenantId,
            ProductId = ownerProductId,
            Barcode = "TENANT-LOCAL-BARCODE",
            Price = 10m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var conflict = await ProductVariantCloneImportAuthority
            .IsProductUnitBarcodeOwnedByAnotherSemanticEntityAsync(
                db,
                requestingTenantId,
                "TENANT-LOCAL-BARCODE",
                requestingProductId,
                Guid.Empty,
                CancellationToken.None);

        Assert.False(conflict);
    }

    [Fact]
    public async Task CanInheritProductMediaAsync_RequiresCurrentProductOwnedImage()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var otherProductId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var staleImageId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.AddRange(
            CreateProduct(tenantId, productId, "Product"),
            CreateProduct(tenantId, otherProductId, "Other"));
        db.ProductMedia.AddRange(
            new ProductMedia
            {
                Id = imageId,
                TenantId = tenantId,
                ProductId = productId,
                MediaType = "image",
                Url = "/media/current.jpg",
                CreatedAtUtc = DateTime.UtcNow
            },
            new ProductMedia
            {
                Id = staleImageId,
                TenantId = tenantId,
                ProductId = otherProductId,
                MediaType = "image",
                Url = "/media/stale.jpg",
                CreatedAtUtc = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        Assert.True(await ProductVariantCloneImportAuthority.CanInheritProductMediaAsync(
            db,
            tenantId,
            productId,
            imageId,
            CancellationToken.None));

        Assert.False(await ProductVariantCloneImportAuthority.CanInheritProductMediaAsync(
            db,
            tenantId,
            productId,
            staleImageId,
            CancellationToken.None));
    }

    private static Product CreateProduct(Guid tenantId, Guid productId, string name)
    {
        return new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = name,
            CategoryId = Guid.NewGuid(),
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
