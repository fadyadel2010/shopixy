using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Bulk;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.ProductAuthoring.Bulk;

public sealed class BulkProductLoader : IBulkProductLoader
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private IReadOnlyDictionary<Guid, Product> _loadedProducts =
        new Dictionary<Guid, Product>();

    public IReadOnlyDictionary<Guid, Product> LoadedProducts => _loadedProducts;

    public BulkProductLoader(ApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<BulkProductProjection>> LoadProjectionsAsync(
        IReadOnlySet<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        if (productIds.Count == 0)
        {
            _loadedProducts = new Dictionary<Guid, Product>();
            return Array.Empty<BulkProductProjection>();
        }

        var tenantId = _tenantContext.RequiredTenantId;

        var products = await _context.Products
            .AsNoTracking()
            .Where(product => product.TenantId == tenantId && productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);

        _loadedProducts = products.ToDictionary(product => product.Id);
        return products
            .Select(product => new BulkProductProjection(
                product.Id,
                product.RowVersion,
                product.IsActive,
                product.PublishStatus,
                product.IsOnlineVisible))
            .ToArray();
    }
}
