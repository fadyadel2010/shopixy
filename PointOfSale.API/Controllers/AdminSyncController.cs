using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin synchronization endpoints for CQRS data repair.
/// 
/// PHASE 11.4.X — VARIANT PROJECTION REPAIR
/// 
/// These endpoints are ONE-TIME operations to fix historical data issues
/// where variants exist in PostgreSQL but not in MongoDB.
/// </summary>
[ApiController]
[Route("api/admin/sync")]
[Authorize(Roles = "Owner,Admin")]
public class AdminSyncController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IProductSqlAuthorityProjectionWriter _productProjectionWriter;
    private readonly IStorefrontProductCacheInvalidator _productCacheInvalidator;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminSyncController> _logger;

    public AdminSyncController(
        ApplicationDbContext context,
        IProductSqlAuthorityProjectionWriter productProjectionWriter,
        IStorefrontProductCacheInvalidator productCacheInvalidator,
        ITenantContext tenantContext,
        ILogger<AdminSyncController> logger)
    {
        _context = context;
        _productProjectionWriter = productProjectionWriter;
        _productCacheInvalidator = productCacheInvalidator;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Synchronizes all product variants from PostgreSQL to MongoDB.
    /// 
    /// This is a ONE-TIME operation to fix historical data where:
    /// - Variants exist in PostgreSQL (ProductVariants table)
    /// - But are missing from MongoDB (products.variants[])
    /// 
    /// SAFE TO RUN: Idempotent - can be run multiple times without side effects.
    /// </summary>
    [HttpPost("product-variants")]
    [ProducesResponseType(typeof(SyncResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncProductVariants(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var correlationId = HttpContext.TraceIdentifier;

        _logger.LogInformation(
            "Starting product variants sync | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            tenantId, correlationId);

        var result = new SyncResultDto
        {
            Operation = "SyncProductVariants",
            TenantId = tenantId,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            // Get all tenant products from PostgreSQL. Deleted/unpublished/invalid-category rows
            // are still loaded so the repair can converge Mongo to a tombstone instead of
            // preserving stale storefront/homepage visibility.
            var productsWithVariants = await _context.Products
                .Include(p => p.Variants.Where(v => !v.IsDeleted))
                    .ThenInclude(v => v.Attributes)
                .Include(p => p.Variants.Where(v => !v.IsDeleted))
                    .ThenInclude(v => v.Stocks)
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId)
                .ToListAsync(cancellationToken);

            _logger.LogInformation(
                "Found {ProductCount} products to sync | TenantId: {TenantId}",
                productsWithVariants.Count, tenantId);

            int synced = 0;
            int skipped = 0;
            int errors = 0;
            var errorMessages = new List<string>();

            foreach (var product in productsWithVariants)
            {
                try
                {
                    var repairResult = await ApplySqlProductRebuildAuthorityAsync(
                        product,
                        cancellationToken);
                    if (!repairResult.Applied)
                    {
                        skipped++;
                        continue;
                    }

                    synced++;
                    _logger.LogDebug(
                        "SQL-authoritative product rebuild applied | ProductId: {ProductId} | Slug: {Slug} | Result: {Result}",
                        product.Id,
                        product.Slug,
                        repairResult.Result);
                }
                catch (Exception ex)
                {
                    errors++;
                    errorMessages.Add($"Error syncing product {product.Id}: {ex.Message}");
                    _logger.LogError(ex,
                        "Error syncing variants for product {ProductId} | Slug: {Slug}",
                        product.Id, product.Slug);
                }
            }

            // Clear Redis cache for products
            await ClearProductCacheAsync(tenantId, cancellationToken);

            result.CompletedAt = DateTime.UtcNow;
            result.TotalProcessed = productsWithVariants.Count;
            result.SuccessCount = synced;
            result.SkippedCount = skipped;
            result.ErrorCount = errors;
            result.Errors = errorMessages.Take(10).ToList(); // Limit error messages
            result.Success = errors == 0;

            _logger.LogInformation(
                "Product variants sync completed | TenantId: {TenantId} | Synced: {Synced} | Skipped: {Skipped} | Errors: {Errors}",
                tenantId, synced, skipped, errors);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Product variants sync failed | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId, correlationId);

            result.CompletedAt = DateTime.UtcNow;
            result.Success = false;
            result.Errors = new List<string> { ex.Message };

            return StatusCode(500, result);
        }
    }

    /// <summary>
    /// Synchronizes a single product's variants from PostgreSQL to MongoDB.
    /// </summary>
    [HttpPost("product-variants/{productId}")]
    [ProducesResponseType(typeof(SingleSyncResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncSingleProductVariants(
        Guid productId, 
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogInformation(
            "Syncing variants for single product | ProductId: {ProductId} | TenantId: {TenantId}",
            productId, tenantId);

        // Load product with variants
        var product = await _context.Products
            .Include(p => p.Variants.Where(v => !v.IsDeleted))
                .ThenInclude(v => v.Attributes)
            .Include(p => p.Variants.Where(v => !v.IsDeleted))
                .ThenInclude(v => v.Stocks)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId, cancellationToken);

        if (product == null)
        {
            return NotFound(new { message = "Product not found" });
        }

        // DIAGNOSTIC: Log variant and attribute counts
        var variantDocs = BuildVariantDocuments(product, DateTime.UtcNow);
        _logger.LogInformation(
            "Syncing product {ProductId} | Variants: {VariantCount} | Attributes per variant: [{AttributeCounts}]",
            productId,
            variantDocs.Count,
            string.Join(", ", variantDocs.Select(v => $"{v.SKU}:{v.Attributes.Count}")));

        var repairResult = await ApplySqlProductRebuildAuthorityAsync(
            product,
            cancellationToken);

        // Clear cache for this product
        await ClearSingleProductCacheAsync(tenantId, productId, product.Slug, cancellationToken);

        return Ok(new SingleSyncResultDto
        {
            ProductId = productId,
            Slug = product.Slug,
            VariantsSynced = variantDocs.Count,
            MongoModified = repairResult.Applied,
            Success = true
        });
    }

    private async Task<(bool Applied, string Result)> ApplySqlProductRebuildAuthorityAsync(
        Product product,
        CancellationToken cancellationToken)
    {
        var tenantId = product.TenantId;
        var appliedOccurredAtUtc = product.UpdatedAt ?? product.CreatedAt;
        var appliedVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion);
        var repairEventId = Guid.NewGuid();
        var writeResult = await _productProjectionWriter.ApplyAsync(
            new ProductSqlAuthorityProjectionWriteRequest(
                tenantId,
                product.Id,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = product.Id,
                    CreatedAtUtc = product.CreatedAt
                },
                repairEventId,
                appliedOccurredAtUtc,
                appliedVersion),
            cancellationToken);

        if (writeResult.Tombstoned)
        {
            _logger.LogInformation(
                "Admin product rebuild converged to tombstone from SQL authority | ProductId: {ProductId} | TenantId: {TenantId} | Applied: {Applied} | Version: {Version} | IsDeleted: {IsDeleted}",
                product.Id,
                tenantId,
                writeResult.Applied,
                appliedVersion,
                product.IsDeleted);
        }
        else if (!writeResult.Applied)
        {
            _logger.LogInformation(
                "Skipped stale admin product rebuild because projection already has same or newer SQL authority | ProductId: {ProductId} | TenantId: {TenantId} | Version: {Version}",
                product.Id,
                tenantId,
                appliedVersion);
        }

        return writeResult.Disposition switch
        {
            ProductSqlAuthorityProjectionWriteDisposition.Upserted => (true, "Upserted"),
            ProductSqlAuthorityProjectionWriteDisposition.Tombstoned => (true, "Tombstoned"),
            ProductSqlAuthorityProjectionWriteDisposition.SkippedStaleTombstone => (false, "SkippedStaleTombstone"),
            _ => (false, "SkippedStaleUpsert")
        };
    }

    private static List<ProductVariantDocument> BuildVariantDocuments(Product product, DateTime syncedAtUtc)
    {
        return product.Variants
            .Where(variant => !variant.IsDeleted)
            .OrderByDescending(variant => variant.IsDefault)
            .ThenBy(variant => variant.SortOrder)
            .ThenBy(variant => variant.CreatedAt)
            .Select(variant => new ProductVariantDocument
            {
                VariantId = variant.Id,
                SKU = variant.SKU,
                Barcode = variant.Barcode,
                Price = variant.Price,
                OnlinePrice = variant.OnlinePrice,
                CostPrice = variant.CostPrice,
                MediaId = variant.MediaId,
                ImageUrl = variant.ImageUrl,
                IsDefault = variant.IsDefault,
                IsActive = variant.IsActive,
                TotalStock = CalculateVariantAvailableStock(variant),
                SortOrder = variant.SortOrder,
                Attributes = variant.Attributes
                    .Where(attribute => !attribute.IsDeleted)
                    .OrderBy(attribute => attribute.SortOrder)
                    .Select(attribute => new VariantAttributeDocument
                    {
                        Name = attribute.Name,
                        Value = attribute.Value,
                        SortOrder = attribute.SortOrder
                    })
                    .ToList(),
                SyncedAtUtc = syncedAtUtc,
                StockUpdatedAtUtc = variant.StockUpdatedAtUtc,
                LastStockUpdatedAtUtc = variant.StockUpdatedAtUtc,
                LastStockEventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion),
                LastEventOccurredAtUtc = variant.UpdatedAt ?? variant.CreatedAt,
                LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion),
                Weight = variant.Weight
            })
            .ToList();
    }

    private static decimal CalculateVariantAvailableStock(ProductVariant variant)
    {
        return Math.Max(
            0m,
            variant.Stocks?
                .Where(stock => !stock.IsDeleted)
                .Sum(stock => stock.Quantity - stock.ReservedQuantity) ?? 0m);
    }

    /// <summary>
    /// Clears all product caches for a tenant.
    /// </summary>
    private async Task ClearProductCacheAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await _productCacheInvalidator.InvalidateTenantAsync(
            tenantId,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Clears cache for a single product.
    /// </summary>
    private async Task ClearSingleProductCacheAsync(
        Guid tenantId,
        Guid productId,
        string? slug,
        CancellationToken cancellationToken)
    {
        await _productCacheInvalidator.InvalidateProductAsync(
            tenantId,
            productId,
            slug,
            cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Result DTO for sync operations.
/// </summary>
public class SyncResultDto
{
    public string Operation { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TotalProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public bool Success { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Result DTO for single product sync.
/// </summary>
public class SingleSyncResultDto
{
    public Guid ProductId { get; set; }
    public string? Slug { get; set; }
    public int VariantsSynced { get; set; }
    public bool MongoModified { get; set; }
    public bool Success { get; set; }
}
