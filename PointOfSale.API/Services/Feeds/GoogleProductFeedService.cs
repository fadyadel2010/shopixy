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
/// FEED-2: Generates the Google Merchant Center product feed for a hashed
/// feed token.
/// </summary>
public interface IGoogleProductFeedService
{
    /// <summary>
    /// Full pipeline: raw token -> channel-scoped hash lookup -> tenant
    /// authority -> SHARED set-based projection -> Google RSS serialization.
    /// No per-product round trips, no channel-specific eligibility/pricing.
    /// </summary>
    Task<MetaProductFeedGenerationResult> GenerateAsync(string rawToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// FEED-2: Google channel adapter. Reuses the certified Feed-1A/1B engine:
/// - eligibility / pricing / inventory / variant semantics come exclusively
///   from the shared <see cref="IFeedProductProjection"/> (the Feed-1A
///   set-based projection) — Google never recalculates product truth;
/// - token validation BEFORE cache lookup, channel-scoped Redis cache key,
///   response safeguards (item cap + size backstop), cancellation, and
///   telemetry all follow the Feed-1B hardening contract with
///   channel = google.
/// The generation result envelope (<see cref="MetaProductFeedGenerationResult"/>)
/// is the shared channel-neutral contract; only the XML vocabulary differs.
/// </summary>
public sealed class GoogleProductFeedService : IGoogleProductFeedService
{
    private const string Channel = ProductFeedToken.GoogleChannel;

    private readonly ApplicationDbContext _dbContext;
    private readonly IFeedProductProjection _projection;
    private readonly IGoogleFeedXmlSerializer _xmlSerializer;
    private readonly StorefrontUrlResolver _urlResolver;
    private readonly IFeedStorefrontReadinessService _readiness;
    private readonly ILogger<GoogleProductFeedService> _logger;
    private readonly IMetaFeedCache _feedCache;
    private readonly MetaFeedTelemetry _telemetry;
    private readonly MetaFeedSafeguards _safeguards;

    public GoogleProductFeedService(
        ApplicationDbContext dbContext,
        IFeedProductProjection projection,
        IGoogleFeedXmlSerializer xmlSerializer,
        StorefrontUrlResolver urlResolver,
        ILogger<GoogleProductFeedService> logger,
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

        // Query 1: hashed-token lookup, channel-scoped to google. Raw tokens
        // are never stored or compared. SECURITY (Feed-1B parity): token
        // validation ALWAYS happens before any cache lookup, so a revoked
        // token can never be satisfied from cache. A Meta token can never
        // authorize the Google route (and vice versa).
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

        // Query 2: tenant authority — same active-tenant gate as the Meta feed.
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
        // google channel segment (never the raw token, never the Meta key).
        var (cacheOutcome, cached) = await _feedCache.TryGetAsync(tenant.Id, token.Id, Channel, cancellationToken);
        if (cacheOutcome == MetaFeedCacheLookupOutcome.Hit && cached != null)
        {
            _telemetry.RecordCacheHit(Channel);
            _telemetry.RecordRequest(cached.ItemCount == 0 ? MetaFeedTelemetry.ResultEmpty : MetaFeedTelemetry.ResultSuccess, Channel);
            _telemetry.RecordResponseSize(Encoding.UTF8.GetByteCount(cached.Xml), Channel);
            requestStopwatch.Stop();

            _logger.LogInformation(
                "Google feed served from cache | TenantId: {TenantId} | TokenId: {TokenId} | Channel: google | CacheResult: hit | Items: {ItemCount} | Excluded: {ExcludedCount} | Truncated: {Truncated} | DurationMs: {DurationMs}",
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
    /// safeguards (item cap + response size) -> Google RSS serialization ->
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
        // Storefront base URL must resolve before projecting anything — the feed
        // must never emit invented/localhost/broken product URLs.
        string baseUrl;
        try
        {
            baseUrl = _urlResolver.Resolve(tenant);
            if (!_readiness.IsPublicStorefrontBaseUrl(baseUrl, out var reasonCode))
            {
                _telemetry.RecordGenerationFailure(reasonCode, Channel);
                _telemetry.RecordRequest(MetaFeedTelemetry.ResultError, Channel);
                _logger.LogError(
                    "Google feed generation aborted: storefront URL is not feed-public | TenantId: {TenantId} | TokenId: {TokenId} | ReasonCode: {ReasonCode}",
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
                "Google feed generation aborted: storefront URL unresolved | TenantId: {TenantId} | TokenId: {TokenId}",
                tenant.Id, tokenId);
            return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.GenerationFailed);
        }

        var generationStopwatch = Stopwatch.StartNew();

        // Same certified projection as Meta — identical items, identical
        // exclusions. Google only differs in XML vocabulary.
        var projection = await _projection.ProjectAsync(
            tenant,
            _safeguards.MaxItems,
            cancellationToken: cancellationToken);

        // F-09: the deterministic item cap is enforced by the shared bounded
        // projection before channel serialization, not after materializing the
        // full catalog.
        var truncated = projection.HitItemCap;
        IReadOnlyList<FeedProduct> items = projection.Items;
        var excludedCount = projection.ExcludedItemCount;
        if (projection.HitItemCap)
        {
            _telemetry.RecordItemCapTruncation(Channel);
            _logger.LogWarning(
                "Google feed truncated at item cap | TenantId: {TenantId} | TokenId: {TokenId} | Cap: {Cap}",
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
            "Google feed generated | TenantId: {TenantId} | TokenId: {TokenId} | Channel: google | CacheResult: miss | Items: {ItemCount} | Excluded: {ExcludedCount} | Truncated: {Truncated} | GenerationMs: {GenerationMs} | TotalMs: {TotalMs} | ResponseBytes: {ResponseBytes}",
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
                "Google feed truncated at response-size safeguard | TenantId: {TenantId} | TokenId: {TokenId} | Bytes: {ByteCount} | MaxBytes: {MaxBytes} | ItemsKept: {ItemsKept}",
                tenantId, tokenId, byteCount, maxBytes, currentItems.Count);
        }

        return (current, currentItems);
    }
}
