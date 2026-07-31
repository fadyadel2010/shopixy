using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.ProductAuthoring;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.ProductAuthoring;

public sealed class ProductAuthoringReconstructionService : IProductAuthoringReconstructionService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public ProductAuthoringReconstructionService(
        ApplicationDbContext context,
        ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<ProductAuthoringCommand> ReconstructForAdmissionAsync(
        ProductAuthoringCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Operation != ProductAuthoringOperation.Update ||
            !command.Identity.ProductId.HasValue ||
            command.Identity.ProductId.Value == Guid.Empty)
        {
            return command;
        }

        var tenantId = _tenantContext.RequiredTenantId;
        var productId = command.Identity.ProductId.Value;
        var productExists = await _context.Products
            .AsNoTracking()
            .AnyAsync(product => product.TenantId == tenantId && product.Id == productId, cancellationToken);

        if (!productExists)
        {
            return command;
        }

        var reconstructed = command;

        if (!MetadataFlag(command, "productUnitsSubmitted"))
        {
            var currentUnits = await LoadCurrentUnitsAsync(productId, cancellationToken);

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
                Variants = await LoadCurrentAuthoringVariantsAsync(
                    tenantId,
                    productId,
                    cancellationToken)
            };
        }

        return reconstructed;
    }

    public async Task<List<ProductAuthoringUnit>> LoadCurrentUnitsAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var units = await _context.ProductUnits
            .AsNoTracking()
            .Where(productUnit =>
                productUnit.ProductId == productId &&
                productUnit.Product.TenantId == tenantId &&
                !productUnit.IsDeleted)
            .ToListAsync(cancellationToken);

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

    private async Task<List<ProductAuthoringVariant>> LoadCurrentAuthoringVariantsAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var variants = await _context.ProductVariants
            .AsNoTracking()
            .WhereCurrentSemanticOwner()
            .Include(variant => variant.Attributes)
            .Where(variant =>
                variant.TenantId == tenantId &&
                variant.ProductId == productId &&
                !variant.IsDeleted)
            .ToListAsync(cancellationToken);

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
                    .ToDictionary(attribute => attribute.Name, attribute => attribute.Value),
                variant.Weight,
                variant.MediaId,
                variant.ImageUrl))
            .ToList();
    }

    private static bool MetadataFlag(ProductAuthoringCommand command, string key) =>
        command.Metadata.AdditionalMetadata.TryGetValue(key, out var value) &&
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
