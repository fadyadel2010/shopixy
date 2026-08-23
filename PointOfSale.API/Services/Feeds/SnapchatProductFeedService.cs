using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Feeds;
using PointOfSale.API.Instrumentation;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// FEED-4: Generates the Snapchat Catalog product feed for a hashed feed token.
///
/// Channel adapter over the SAME certified Feed-1A/1B engine as Meta, Google,
/// and TikTok:
/// - eligibility / pricing / inventory / variant semantics come exclusively
///   from the shared <see cref="IFeedProductProjection"/> — Snapchat never
///   recalculates product truth;
/// - token validation BEFORE cache lookup, channel-scoped Redis cache key,
///   response safeguards (item cap + size backstop), cancellation, and
///   telemetry all follow the Feed-1B hardening contract with channel = snapchat.
/// The generation result envelope (<see cref="MetaProductFeedGenerationResult"/>)
/// is the shared channel-neutral contract; only the XML vocabulary differs.
/// </summary>
public interface ISnapchatProductFeedService
{
    /// <summary>
    /// Full pipeline: raw token -> channel-scoped hash lookup -> tenant
    /// authority -> SHARED set-based projection -> Snapchat RSS serialization.
    /// No per-product round trips, no channel-specific eligibility/pricing.
    /// </summary>
    Task<MetaProductFeedGenerationResult> GenerateAsync(string rawToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// FEED-4: Snapchat channel adapter. Reuses the certified Feed-1A/1B engine.
/// Snapchat's catalog format uses the Google Merchant attribute namespace
/// (xmlns:g), the same as Meta and Google — only the channel identifier and
/// response description differ.
/// </summary>
public sealed class SnapchatProductFeedService : ISnapchatProductFeedService
{
    private const string Channel = ProductFeedToken.SnapchatChannel;

    private readonly ApplicationDbContext _dbContext;
    private readonly IFeedProductProjection _projection;
    private readonly ISnapchatFeedXmlSerializer _xmlSerializer;
    private readonly StorefrontUrlResolver _urlResolver;
    private readonly IFeedStorefrontReadinessService _readiness;
    private readonly ILogger<SnapchatProductFeedService> _logger;
    private readonly IMetaFeedCache _feedCache;
    private readonly MetaFeedTelemetry _telemetry;
    private readonly MetaFeedSafeguards _safeguards;

    public SnapchatProductFeedService(
        ApplicationDbContext dbContext,
        IFeedProductProjection projection,
        ISnapchatFeedXmlSerializer xmlSerializer,
        StorefrontUrlResolver urlResolver,
        ILogger<SnapchatProductFeedService> logger,
        IMetaFeedCache feedCache,
        MetaFeedTelemetry telemetry,
        MetaFeedSafeguards? safeguards = null,
        IFeedStorefrontReadinessService? readiness = null)
    {
        _dbContext = dbContext;
        _projection = projection;
        _xmlSerializer = xmlSerializer;
        _urlResolver = urlResolver;
        _readiness = readiness ?? StrictFeedStorefrontReadinessService.Instance;
        _logger = logger;
        _feedCache = feedCache;
        _telemetry = telemetry;
        _safeguards = safeguards ?? MetaFeedSafeguards.Default;
    }

    public async Task<MetaProductFeedGenerationResult> GenerateAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        var requestStopwatch = Stopwatch.StartNew();
        var maxAgeSeconds = (int)_feedCache.GetTtl().TotalSeconds;

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.TokenNotFound);
        }

        // Query 1: hashed-token lookup, channel-scoped to snapchat. Raw tokens
        // are never stored or compared. SECURITY (Feed-1B parity): token
        // validation ALWAYS happens before any cache lookup, so a revoked
        // token can never be satisfied from cache. A Meta, Google, or TikTok
        // token can never authorize the Snapchat route (and vice versa).
        var tokenHash = ProductFeedToken.HashToken(rawToken.Trim());
        var token = await _dbContext.ProductFeedTokens
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TokenHash == tokenHash && !item.IsDeleted && item.Channel == Channel)
            .Select(item => new { item.Id, item.TenantId, item.RevokedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);

        if (token == null || token.RevokedAtUtc != null)
        {
            return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.TokenNotFound);
        }

        // Query 2: tenant authority — same active-tenant gate as Meta and TikTok.
        if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, token.TenantId, cancellationToken))
        {
            return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.TenantInactive);
        }

        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.Id == token.TenantId, cancellationToken);
        if (tenant == null)
        {
            return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.TenantInactive);
        }

        // Feed-1B parity: cache lookup keyed by tenant + token ROW identity +
        // snapchat channel segment (never the raw token). Redis failures are
        // isolated inside the cache wrapper and fall through to SQL generation.
        var (cacheOutcome, cached) = await _feedCache.TryGetAsync(tenant.Id, token.Id, Channel, cancellationToken);
        if (cacheOutcome == MetaFeedCacheLookupOutcome.Hit && cached != null)
        {
            _telemetry.RecordCacheHit(Channel);
            _telemetry.RecordRequest(cached.ItemCount == 0 ? MetaFeedTelemetry.ResultEmpty : MetaFeedTelemetry.ResultSuccess, Channel);
            _telemetry.RecordResponseSize(Encoding.UTF8.GetByteCount(cached.Xml), Channel);
            requestStopwatch.Stop();

            _logger.LogInformation(
                "Snapchat feed served from cache | TenantId: {TenantId} | TokenId: {TokenId} | Channel: snapchat | CacheResult: hit | Items: {ItemCount} | Excluded: {ExcludedCount} | Truncated: {Truncated} | DurationMs: {DurationMs}",
                tenant.Id, token.Id, cached.ItemCount, cached.ExcludedItemCount, cached.Truncated, requestStopwatch.ElapsedMilliseconds);

            return new MetaProductFeedGenerationResult(
                MetaFeedGenerationOutcome.Success,
                cached.Xml,
                tenant.Id,
                token.Id,
                cached.ItemCount,
                cached.ExcludedItemCount,
                CacheHit: true,
                Truncated: cached.Truncated,
                CacheMaxAgeSeconds: maxAgeSeconds);
        }

        _telemetry.RecordCacheMiss(Channel);

        MetaProductFeedGenerationResult coldResult;
        try
        {
            coldResult = await GenerateFromSqlAsync(tenant, token.Id, maxAgeSeconds, requestStopwatch, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _telemetry.RecordGenerationFailure("canceled", Channel);
            throw;
        }
        catch (Exception)
        {
            _telemetry.RecordGenerationFailure("projection_error", Channel);
            _telemetry.RecordRequest(MetaFeedTelemetry.ResultError, Channel);
            throw;
        }

        return coldResult;
    }

    /// <summary>
    /// Cold path: storefront URL authority -> SHARED set-based projection ->
    /// safeguards (item cap + response size) -> Snapchat RSS serialization ->
    /// cache population -> telemetry. Cached value is exactly the XML served
    /// here, so cached and uncached output stay byte-identical.
    /// </summary>
    private async Task<MetaProductFeedGenerationResult> GenerateFromSqlAsync(
        Tenant tenant,
        Guid tokenId,
        int maxAgeSeconds,
        Stopwatch requestStopwatch,
        CancellationToken cancellationToken)
    {
        string baseUrl;
        try
        {
            baseUrl = _urlResolver.Resolve(tenant);
            if (!_readiness.IsPublicStorefrontBaseUrl(baseUrl, out var reasonCode))
            {
                _telemetry.RecordGenerationFailure(reasonCode, Channel);
                _telemetry.RecordRequest(MetaFeedTelemetry.ResultError, Channel);
                _logger.LogError(
                    "Snapchat feed generation aborted: storefront URL is not feed-public | TenantId: {TenantId} | TokenId: {TokenId} | ReasonCode: {ReasonCode}",
                    tenant.Id, tokenId, reasonCode);
                return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.GenerationFailed);
            }
        }
        catch (InvalidOperationException ex)
        {
            _telemetry.RecordGenerationFailure("url_unresolved", Channel);
            _telemetry.RecordRequest(MetaFeedTelemetry.ResultError, Channel);
            _logger.LogError(
                ex,
                "Snapchat feed generation aborted: storefront URL unresolved | TenantId: {TenantId} | TokenId: {TokenId}",
                tenant.Id, tokenId);
            return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.GenerationFailed);
        }

        var generationStopwatch = Stopwatch.StartNew();

        // Same certified projection as Meta, Google, and TikTok — identical items,
        // identical exclusions. Snapchat only differs in the channel label.
        var projection = await _projection.ProjectAsync(
            tenant,
            _safeguards.MaxItems,
            item => IsChannelReady(item, tenant.Id, tokenId),
            cancellationToken);
        var excludedCount = projection.ExcludedItemCount;

        // F-09: cap after Snapchat readiness, but inside the bounded shared
        // projection so excluded early items do not force full-catalog
        // materialization.
        var truncated = projection.HitItemCap;
        IReadOnlyList<FeedProduct> items = projection.Items;
        if (projection.HitItemCap)
        {
            _telemetry.RecordItemCapTruncation(Channel);
            _logger.LogWarning(
                "Snapchat feed truncated at item cap | TenantId: {TenantId} | TokenId: {TokenId} | Cap: {Cap}",
                tenant.Id, tokenId, _safeguards.MaxItems);
        }

        var xml = _xmlSerializer.Serialize(tenant.Name, baseUrl, items);

        // Feed-1B response-size backstop: shrink deterministically and
        // re-serialize — the response is always complete valid RSS.
        (xml, items) = EnforceMaxResponseBytes(xml, items, tenant.Name, baseUrl, ref truncated, tenant.Id, tokenId);
        generationStopwatch.Stop();

        var finalItemCount = items.Count;
        var responseBytes = Encoding.UTF8.GetByteCount(xml);

        await _feedCache.SetAsync(
            tenant.Id,
            tokenId,
            new MetaFeedCachePayload
            {
                Xml = xml,
                ItemCount = finalItemCount,
                ExcludedItemCount = excludedCount,
                Truncated = truncated,
                GeneratedAtUtc = DateTime.UtcNow
            },
            Channel,
            cancellationToken);

        _telemetry.RecordGenerationDuration(generationStopwatch.Elapsed, Channel);
        _telemetry.RecordResponseSize(responseBytes, Channel);
        _telemetry.RecordRequest(finalItemCount == 0 ? MetaFeedTelemetry.ResultEmpty : MetaFeedTelemetry.ResultSuccess, Channel);
        requestStopwatch.Stop();

        _logger.LogInformation(
            "Snapchat feed generated | TenantId: {TenantId} | TokenId: {TokenId} | Channel: snapchat | CacheResult: miss | Items: {ItemCount} | Excluded: {ExcludedCount} | Truncated: {Truncated} | GenerationMs: {GenerationMs} | TotalMs: {TotalMs} | ResponseBytes: {ResponseBytes}",
            tenant.Id, tokenId, finalItemCount, excludedCount, truncated,
            generationStopwatch.ElapsedMilliseconds, requestStopwatch.ElapsedMilliseconds, responseBytes);

        return new MetaProductFeedGenerationResult(
            MetaFeedGenerationOutcome.Success,
            xml,
            tenant.Id,
            tokenId,
            finalItemCount,
            excludedCount,
            CacheHit: false,
            Truncated: truncated,
            CacheMaxAgeSeconds: maxAgeSeconds);
    }

    private bool IsChannelReady(
        FeedProduct item,
        Guid tenantId,
        Guid tokenId)
    {
        if (FeedChannelReadiness.IsReady(Channel, item, out var reasonCode))
        {
            return true;
        }

        _logger.LogInformation(
            "Snapchat feed item excluded: channel readiness failed | TenantId: {TenantId} | TokenId: {TokenId} | ProductId: {ProductId} | VariantId: {VariantId} | ReasonCode: {ReasonCode}",
            tenantId, tokenId, item.ProductId, item.VariantId, reasonCode);
        return false;
    }

    /// <summary>
    /// Feed-1B parity: when the rendered XML exceeds the response-size
    /// backstop, keep the deterministic FIRST items (projection order never
    /// changes), re-serializing a complete valid RSS document.
    /// </summary>
    private (string Xml, IReadOnlyList<FeedProduct> Items) EnforceMaxResponseBytes(
        string xml,
        IReadOnlyList<FeedProduct> items,
        string tenantName,
        string baseUrl,
        ref bool truncated,
        Guid tenantId,
        Guid tokenId)
    {
        var maxBytes = _safeguards.MaxResponseBytes;
        var current = xml;
        var currentItems = items;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var byteCount = Encoding.UTF8.GetByteCount(current);
            if (byteCount <= maxBytes || currentItems.Count == 0)
            {
                return (current, currentItems);
            }

            var estimated = (long)((double)maxBytes / byteCount * currentItems.Count * 0.95);
            var target = (int)Math.Max(1, Math.Min(estimated, currentItems.Count - 1));
            currentItems = currentItems.Take(target).ToList();
            current = _xmlSerializer.Serialize(tenantName, baseUrl, currentItems);
            truncated = true;

            _logger.LogWarning(
                "Snapchat feed truncated at response-size safeguard | TenantId: {TenantId} | TokenId: {TokenId} | Bytes: {ByteCount} | MaxBytes: {MaxBytes} | ItemsKept: {ItemsKept}",
                tenantId, tokenId, byteCount, maxBytes, currentItems.Count);
        }

        return (current, currentItems);
    }
}
