using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Nest;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Tenancy;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.Scripts;

/// <summary>
/// AF-16E: Reindex all affiliate products with StorefrontBaseUrl and tenant currency.
///
/// CRITICAL: This script backfills StorefrontBaseUrl for existing products.
/// Run this ONCE after deploying AF-16E to production.
///
/// USAGE:
/// - Inject this service in a controller or background job
/// - Call ReindexAsync() with cancellation token
/// - Monitor logs for progress and errors
/// </summary>
public class AF16E_ReindexAffiliateProductsWithStorefrontUrl
{
    private const SqlAuthorityOverlayKind AffiliateRepairAuthorityKinds =
        SqlAuthorityOverlayKind.StoreMetadata |
        SqlAuthorityOverlayKind.Pricing |
        SqlAuthorityOverlayKind.Tombstone;

    private readonly MongoDbContext _mongoDbContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly IElasticClient _elasticClient;
    private readonly ElasticsearchSettings _elasticSettings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AF16E_ReindexAffiliateProductsWithStorefrontUrl> _logger;

    public AF16E_ReindexAffiliateProductsWithStorefrontUrl(
        MongoDbContext mongoDbContext,
        ApplicationDbContext dbContext,
        IElasticClient elasticClient,
        Microsoft.Extensions.Options.IOptions<ElasticsearchSettings> elasticSettings,
        IConfiguration configuration,
        ILogger<AF16E_ReindexAffiliateProductsWithStorefrontUrl> logger)
    {
        _mongoDbContext = mongoDbContext;
        _dbContext = dbContext;
        _elasticClient = elasticClient;
        _elasticSettings = elasticSettings.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<ReindexResult> ReindexAsync(CancellationToken ct = default) =>
        ReindexCoreAsync(tenantScopeId: null, ct);

    public Task<ReindexResult> ReindexTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
        {
            var result = new ReindexResult
            {
                Success = false,
                ErrorMessage = "Tenant scope is required for tenant-scoped reindex."
            };
            return Task.FromResult(result);
        }

        return ReindexCoreAsync(tenantId, ct);
    }

    private async Task<ReindexResult> ReindexCoreAsync(Guid? tenantScopeId, CancellationToken ct)
    {
        _logger.LogInformation(
            "[AF-16E] Starting affiliate products reindex with StorefrontBaseUrl and currency | TenantScopeId: {TenantScopeId}",
            tenantScopeId);

        var result = new ReindexResult();
        var startTime = DateTime.UtcNow;

        try
        {
            // Step 1: Load all tenants with their StorefrontBaseUrl
            var tenants = await _dbContext.Tenants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(t => t.IsActive &&
                            t.DeletedAtUtc == null &&
                            !string.IsNullOrEmpty(t.Slug) &&
                            (tenantScopeId == null || t.Id == tenantScopeId.Value))
                .Select(t => new { t.Id, t.Name, t.Slug, t.StorefrontBaseUrl, t.Currency })
                .ToListAsync(ct);

            _logger.LogInformation("[AF-16E] Loaded {Count} tenants", tenants.Count);

            var tenantProjectionMap = new Dictionary<Guid, (string StorefrontBaseUrl, string Currency)>();
            var urlTemplate = _configuration["Storefront:UrlTemplate"];

            foreach (var tenant in tenants)
            {
                try
                {
                    tenantProjectionMap[tenant.Id] = (
                        StorefrontUrlAuthority.ResolveOrigin(tenant.StorefrontBaseUrl, tenant.Slug, urlTemplate),
                        NormalizeCurrency(
                            tenant.Currency,
                            $"Script: AF16E_ReindexAffiliateProductsWithStorefrontUrl, Method: {nameof(ReindexCoreAsync)}, TenantId: {tenant.Id}"));
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "[AF-16E] Skipping tenant without authoritative currency | TenantId: {TenantId}",
                        tenant.Id);
                    result.SkippedTenants++;
                }
            }

            var affiliateVisibilityAuthority = await LoadAffiliateVisibilityAuthorityAsync(ct);

            // Step 2: Load all public affiliate products from MongoDB
            var allProducts = await _mongoDbContext.PublicAffiliateProducts
                .Find(tenantScopeId.HasValue
                    ? Builders<PublicAffiliateProductDocument>.Filter.Eq(d => d.TenantId, tenantScopeId.Value)
                    : Builders<PublicAffiliateProductDocument>.Filter.Empty)
                .ToListAsync(ct);

            _logger.LogInformation("[AF-16E] Found {Count} affiliate products to reindex", allProducts.Count);

            // Step 3: Update each product with StorefrontBaseUrl
            foreach (var product in allProducts)
            {
                try
                {
                    _ = RequireAffiliateRepairAuthority(product.TenantId, product.ProductId);

                    if (!IsAffiliateVisibilityAllowed(
                            affiliateVisibilityAuthority,
                            product.TenantId,
                            product.ProductId))
                    {
                        await RemoveStalePublicAffiliateProductAsync(product, ct);
                        result.StalePublicProductsDeleted++;
                        continue;
                    }

                    if (!tenantProjectionMap.TryGetValue(product.TenantId, out var projection))
                    {
                        _logger.LogWarning(
                            "[AF-16E] Removing affiliate product because current tenant storefront/currency authority is missing | ProductId: {ProductId} | TenantId: {TenantId}",
                            product.ProductId, product.TenantId);
                        await RemoveStalePublicAffiliateProductAsync(product, ct);
                        result.StalePublicProductsDeleted++;
                        continue;
                    }

                    product.StorefrontBaseUrl = projection.StorefrontBaseUrl;
                    product.Currency = projection.Currency;

                    var publicProductFilter = Builders<PublicAffiliateProductDocument>.Filter.And(
                        Builders<PublicAffiliateProductDocument>.Filter.Eq(d => d.ProductId, product.ProductId),
                        Builders<PublicAffiliateProductDocument>.Filter.Eq(d => d.TenantId, product.TenantId));
                    var publicProductUpdate = Builders<PublicAffiliateProductDocument>.Update
                        .Set(d => d.StorefrontBaseUrl, projection.StorefrontBaseUrl)
                        .Set(d => d.Currency, projection.Currency);

                    await _mongoDbContext.PublicAffiliateProducts.UpdateOneAsync(
                        publicProductFilter,
                        publicProductUpdate,
                        cancellationToken: ct);

                    // Update Elasticsearch
                    if (_elasticSettings.Enabled)
                    {
                        await _elasticClient.UpdateAsync<PublicAffiliateProductSearchDocument, object>(
                            BuildPublicAffiliateSearchDocumentId(product.TenantId, product.ProductId),
                            u => u
                            .Index(_elasticSettings.PublicAffiliateProductsIndexName)
                            .Doc(new
                            {
                                Currency = projection.Currency,
                                StorefrontBaseUrl = projection.StorefrontBaseUrl
                            })
                            .RetryOnConflict(3),
                            ct);
                    }

                    result.UpdatedProducts++;

                    if (result.UpdatedProducts % 100 == 0)
                    {
                        _logger.LogInformation("[AF-16E] Progress: {Count} products updated", result.UpdatedProducts);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AF-16E] Failed to update product | ProductId: {ProductId}", product.ProductId);
                    result.FailedProducts++;
                }
            }

            var intelligenceProducts = await _mongoDbContext.AffiliateProductIntelligence
                .Find(tenantScopeId.HasValue
                    ? Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.TenantId, tenantScopeId.Value)
                    : Builders<AffiliateProductIntelligenceDocument>.Filter.Empty)
                .ToListAsync(ct);

            _logger.LogInformation("[AF-16E] Found {Count} affiliate intelligence products to reindex", intelligenceProducts.Count);

            foreach (var product in intelligenceProducts)
            {
                try
                {
                    _ = RequireAffiliateRepairAuthority(product.TenantId, product.ProductId);

                    if (!IsAffiliateVisibilityAllowed(
                            affiliateVisibilityAuthority,
                            product.TenantId,
                            product.ProductId))
                    {
                        await _mongoDbContext.AffiliateProductIntelligence.DeleteOneAsync(
                            Builders<AffiliateProductIntelligenceDocument>.Filter.And(
                                Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.Id, product.Id),
                                Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.TenantId, product.TenantId)),
                            ct);

                        result.StaleIntelligenceProductsDeleted++;
                        continue;
                    }

                    if (!tenantProjectionMap.TryGetValue(product.TenantId, out var projection))
                    {
                        _logger.LogWarning(
                            "[AF-16E] Removing affiliate intelligence document because current tenant storefront/currency authority is missing | ProductId: {ProductId} | TenantId: {TenantId}",
                            product.ProductId,
                            product.TenantId);
                        await _mongoDbContext.AffiliateProductIntelligence.DeleteOneAsync(
                            Builders<AffiliateProductIntelligenceDocument>.Filter.And(
                                Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.Id, product.Id),
                                Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.TenantId, product.TenantId)),
                            ct);
                        result.StaleIntelligenceProductsDeleted++;
                        continue;
                    }

                    product.Currency = projection.Currency;

                    var intelligenceFilter = Builders<AffiliateProductIntelligenceDocument>.Filter.And(
                        Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.Id, product.Id),
                        Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.TenantId, product.TenantId));
                    var intelligenceUpdate = Builders<AffiliateProductIntelligenceDocument>.Update
                        .Set(d => d.Currency, projection.Currency);

                    await _mongoDbContext.AffiliateProductIntelligence.UpdateOneAsync(
                        intelligenceFilter,
                        intelligenceUpdate,
                        cancellationToken: ct);

                    result.UpdatedIntelligenceProducts++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AF-16E] Failed to update affiliate intelligence product | ProductId: {ProductId}", product.ProductId);
                    result.FailedProducts++;
                }
            }

            result.Success = true;
            result.DurationSeconds = (DateTime.UtcNow - startTime).TotalSeconds;

            _logger.LogInformation(
                "[AF-16E] Reindex complete | PublicUpdated: {PublicUpdated} | IntelligenceUpdated: {IntelligenceUpdated} | PublicSkipped: {PublicSkipped} | IntelligenceSkipped: {IntelligenceSkipped} | PublicStaleDeleted: {PublicStaleDeleted} | IntelligenceStaleDeleted: {IntelligenceStaleDeleted} | Failed: {Failed} | Duration: {Duration}s",
                result.UpdatedProducts, result.UpdatedIntelligenceProducts, result.SkippedProducts, result.SkippedIntelligenceProducts, result.StalePublicProductsDeleted, result.StaleIntelligenceProductsDeleted, result.FailedProducts, result.DurationSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AF-16E] Reindex failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static SqlTruthRebuildRequest RequireAffiliateRepairAuthority(Guid tenantId, Guid productId) =>
        SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.Affiliate,
            tenantId,
            productId,
            AffiliateRepairAuthorityKinds);

    private static string NormalizeCurrency(string? currency, string context) =>
        CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(currency, context);

    private async Task<HashSet<string>> LoadAffiliateVisibilityAuthorityAsync(CancellationToken ct)
    {
        var rows = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Join(
                _dbContext.Tenants
                    .AsNoTracking()
                    .IgnoreQueryFilters(),
                product => product.TenantId,
                tenant => tenant.Id,
                (product, tenant) => new { product, tenant })
            .Where(row => row.tenant.Status == TenantStatus.Active && row.tenant.DeletedAtUtc == null)
            .Join(
                _dbContext.AffiliateProductPolicies
                    .AsNoTracking()
                    .IgnoreQueryFilters(),
                row => new { row.product.TenantId, ProductId = row.product.Id },
                policy => new { policy.TenantId, ProductId = policy.ProductId },
                (row, policy) => new
                {
                    row.product.TenantId,
                    ProductId = row.product.Id,
                    ProductIsVisible = !row.product.IsDeleted && row.product.IsActive && row.product.IsOnlineVisible,
                    PolicyIsVisible = !policy.IsDeleted && policy.IsEnabled
                })
            .Where(row => row.ProductIsVisible && row.PolicyIsVisible)
            .Select(row => new
            {
                row.TenantId,
                row.ProductId
            })
            .ToListAsync(ct);

        return rows
            .Select(row => BuildAffiliateVisibilityKey(row.TenantId, row.ProductId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task RemoveStalePublicAffiliateProductAsync(
        PublicAffiliateProductDocument product,
        CancellationToken ct)
    {
        await _mongoDbContext.PublicAffiliateProducts.DeleteOneAsync(
            Builders<PublicAffiliateProductDocument>.Filter.And(
                Builders<PublicAffiliateProductDocument>.Filter.Eq(d => d.ProductId, product.ProductId),
                Builders<PublicAffiliateProductDocument>.Filter.Eq(d => d.TenantId, product.TenantId)),
            ct);

        if (!_elasticSettings.Enabled)
        {
            return;
        }

        var response = await _elasticClient.DeleteAsync<PublicAffiliateProductSearchDocument>(
            BuildPublicAffiliateSearchDocumentId(product.TenantId, product.ProductId),
            descriptor => descriptor.Index(_elasticSettings.PublicAffiliateProductsIndexName),
            ct);

        if (!response.IsValid && response.Result != Result.NotFound)
        {
            _logger.LogWarning(
                "[AF-16E] Failed to delete stale public affiliate product from Elasticsearch | ProductId: {ProductId} | TenantId: {TenantId} | Error: {Error}",
                product.ProductId,
                product.TenantId,
                response.ServerError?.Error?.Reason ?? response.OriginalException?.Message ?? "Unknown");
        }

        var legacyResponse = await _elasticClient.DeleteAsync<PublicAffiliateProductSearchDocument>(
            product.ProductId,
            descriptor => descriptor.Index(_elasticSettings.PublicAffiliateProductsIndexName),
            ct);

        if (!legacyResponse.IsValid && legacyResponse.Result != Result.NotFound)
        {
            _logger.LogWarning(
                "[AF-16E] Failed to delete legacy public affiliate product from Elasticsearch | ProductId: {ProductId} | TenantId: {TenantId} | Error: {Error}",
                product.ProductId,
                product.TenantId,
                legacyResponse.ServerError?.Error?.Reason ?? legacyResponse.OriginalException?.Message ?? "Unknown");
        }
    }

    private static bool IsAffiliateVisibilityAllowed(
        HashSet<string> authority,
        Guid tenantId,
        Guid productId) =>
        authority.Contains(BuildAffiliateVisibilityKey(tenantId, productId));

    private static string BuildAffiliateVisibilityKey(Guid tenantId, Guid productId) =>
        $"{tenantId:N}:{productId:N}";

    private static string BuildPublicAffiliateSearchDocumentId(Guid tenantId, Guid productId) =>
        TenantScopedDerivativeKey.ForTenantEntity(tenantId, productId);
}

public class ReindexResult
{
    public bool Success { get; set; }
    public int UpdatedProducts { get; set; }
    public int UpdatedIntelligenceProducts { get; set; }
    public int SkippedProducts { get; set; }
    public int SkippedIntelligenceProducts { get; set; }
    public int FailedProducts { get; set; }
    public int SkippedTenants { get; set; }
    public int StalePublicProductsDeleted { get; set; }
    public int StaleIntelligenceProductsDeleted { get; set; }
    public double DurationSeconds { get; set; }
    public string? ErrorMessage { get; set; }
}
