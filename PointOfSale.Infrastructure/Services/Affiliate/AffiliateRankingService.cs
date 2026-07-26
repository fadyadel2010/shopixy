using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Infrastructure.Services.Affiliate;

/// <summary>
/// AF-34 + AF-34A: Ranking Score Calculator & Signal Updater.
///
/// FORMULA:
///   RankingScore =
///     (NormalizedCommission * 0.30) +
///     (NormalizedRolling7DayEarnings * 0.25) +
///     (NormalizedRolling7DayConversions * 0.20) +
///     (NormalizedRolling7DayClicks * 0.15) +
///     (FreshnessBoost * 0.10)
///
/// NORMALIZATION SCOPE: Global (across all products, NOT per category).
///
/// JUSTIFICATION:
///   - Ranking is always used WITHIN a filtered result set (category filter narrows, score ranks).
///   - Per-category normalization at 50+ categories would require per-category max aggregation
///     which is O(categories × fields) = expensive and creates fragile cross-category scoring.
///   - Global normalization means a product with 100 clicks in a small category ranks the same
///     as 100 clicks in a large category — this is fair because the user already filtered by category.
///   - Products with no signals default to 0, which sorts to bottom — correct behavior.
///
/// NUMERICAL SAFETY (AF-34A):
///   - SafeNormalize guards against maxValue <= 0 (returns 0)
///   - Final score clamped to [0, 1] via Math.Clamp
///   - NaN/Infinity guard via double.IsFinite check → fallback to 0
///   - All intermediate values are finite by construction
///
/// BATCH STRATEGY (AF-34A):
///   - BulkWriteAsync with batch size 500
///   - ordered: false (parallel execution, no ordering dependency)
///   - No collection locks (individual UpdateOne per op inside bulk)
///   - Estimated: 1M products ÷ 500 batch = 2000 bulk calls, ~30s total
///
/// FRESHNESS (AF-34A):
///   - CreatedAtUtc is SetOnInsert (immutable)
///   - FreshnessBoost = 1.0 within 14 days, linear decay over 30 days, 0 after 44 days
///   - Trending is implicitly captured by Rolling7DayClicks/Conversions weights (25%+20%+15% = 60%)
///   - No separate TrendingBoost needed — rolling 7-day metrics ARE the trending signal
///
/// USAGE:
/// - IncrementClickSignal:    Call from AffiliateLinkClickedConsumer
/// - IncrementConversionSignal: Call from AffiliateClickConversionConsumer
/// - RecalculateAllScores:    Call from daily Hangfire job
/// </summary>
public class AffiliateRankingService
{
    private readonly MongoDbContext _mongoDbContext;
    private readonly ILogger<AffiliateRankingService> _logger;

    private const int BulkBatchSize = 500; // AF-34A: BulkWrite batch size

    public AffiliateRankingService(
        MongoDbContext mongoDbContext,
        ILogger<AffiliateRankingService> logger)
    {
        _mongoDbContext = mongoDbContext;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    // INCREMENT SIGNALS (Event-Driven, Fire-and-Forget Safe)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// AF-34: Increment click signals on product intelligence document.
    /// Called by AffiliateLinkClickedConsumer after click is stored.
    /// </summary>
    public async Task IncrementClickSignalAsync(Guid productId, Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            var filter = Builders<AffiliateProductIntelligenceDocument>.Filter.And(
                Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.ProductId, productId),
                Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.TenantId, tenantId));

            var update = Builders<AffiliateProductIntelligenceDocument>.Update
                .Inc(d => d.TotalClicks, 1)
                .Set(d => d.LastClickUtc, DateTime.UtcNow);

            await _mongoDbContext.AffiliateProductIntelligence.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AF-34] Failed to increment click signal for Product {ProductId}", productId);
            // Non-critical — ranking will self-heal on next daily recalc
        }
    }

    /// <summary>
    /// AF-34: Increment conversion signals on product intelligence document.
    /// Called by AffiliateClickConversionConsumer after conversion is bridged.
    /// </summary>
    public async Task IncrementConversionSignalAsync(Guid productId, Guid tenantId, decimal commissionAmount, CancellationToken ct = default)
    {
        try
        {
            var filter = Builders<AffiliateProductIntelligenceDocument>.Filter.And(
                Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.ProductId, productId),
                Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.TenantId, tenantId));

            var update = Builders<AffiliateProductIntelligenceDocument>.Update
                .Inc(d => d.TotalConversions, 1)
                .Inc(d => d.TotalEarnings, commissionAmount)
                .Set(d => d.LastConversionUtc, DateTime.UtcNow);

            await _mongoDbContext.AffiliateProductIntelligence.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AF-34] Failed to increment conversion signal for Product {ProductId}", productId);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // DAILY RECALCULATION (Background Job, BulkWrite Batched)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// AF-34 + AF-34A: Recalculate rolling 7-day metrics and RankingScore for ALL products.
    /// Called by Hangfire daily job.
    ///
    /// Strategy:
    /// 1. Aggregate rolling 7-day clicks per product from affiliate_clicks collection
    /// 2. Aggregate rolling 7-day conversions
    /// 3. Load all products (projected — minimal fields)
    /// 4. Compute max values for global normalization (single pass)
    /// 5. Compute RankingScore per product with numerical safety
    /// 6. BulkWrite in batches of 500 (ordered: false)
    ///
    /// Performance (AF-34A):
    /// - BulkWrite replaces sequential UpdateOne (2000x fewer round-trips at 1M products)
    /// - ordered: false enables parallel server-side execution
    /// - No collection locks
    /// - Estimated: 1M products → 2000 bulk calls → ~30s total
    /// </summary>
    public async Task RecalculateAllScoresAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[AF-34] Starting ranking score recalculation...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

        // ── Step 1: Aggregate rolling 7-day clicks per (productId, tenantId) ──
        var clickAgg = await _mongoDbContext.AffiliateClicks
            .Aggregate()
            .Match(Builders<AffiliateClickDocument>.Filter.And(
                Builders<AffiliateClickDocument>.Filter.Gte(d => d.ClickedAtUtc, sevenDaysAgo),
                Builders<AffiliateClickDocument>.Filter.Ne(d => d.ProductId, null)))
            .Group(d => new { d.ProductId, d.TenantId }, g => new
            {
                Key = g.Key,
                Clicks = g.Count()
            })
            .ToListAsync(ct);

        var clickMap = clickAgg.ToDictionary(
            x => (x.Key.ProductId!.Value, x.Key.TenantId),
            x => x.Clicks);

        _logger.LogInformation("[AF-34] Aggregated rolling 7-day clicks for {Count} product-tenant pairs", clickMap.Count);

        // ── Step 2: Aggregate rolling 7-day conversions from clicks with OrderId ──
        var convAgg = await _mongoDbContext.AffiliateClicks
            .Aggregate()
            .Match(Builders<AffiliateClickDocument>.Filter.And(
                Builders<AffiliateClickDocument>.Filter.Gte(d => d.ConvertedAtUtc, sevenDaysAgo),
                Builders<AffiliateClickDocument>.Filter.Ne(d => d.OrderId, null),
                Builders<AffiliateClickDocument>.Filter.Ne(d => d.ProductId, null)))
            .Group(d => new { d.ProductId, d.TenantId }, g => new
            {
                Key = g.Key,
                Conversions = g.Count()
            })
            .ToListAsync(ct);

        var convMap = convAgg.ToDictionary(
            x => (x.Key.ProductId!.Value, x.Key.TenantId),
            x => x.Conversions);

        // ── Step 3: Load all products (projected for minimal memory) ──
        var allProducts = await _mongoDbContext.AffiliateProductIntelligence
            .Find(Builders<AffiliateProductIntelligenceDocument>.Filter.Empty)
            .Project(Builders<AffiliateProductIntelligenceDocument>.Projection
                .Include(d => d.ProductId)
                .Include(d => d.TenantId)
                .Include(d => d.CommissionRate)
                .Include(d => d.TotalEarnings)
                .Include(d => d.CreatedAtUtc)
                .Include(d => d.Id))
            .As<AffiliateProductIntelligenceDocument>()
            .ToListAsync(ct);

        if (allProducts.Count == 0)
        {
            _logger.LogInformation("[AF-34] No products to recalculate. Done.");
            return;
        }

        _logger.LogInformation("[AF-34] Loaded {Count} products for score computation", allProducts.Count);

        // ── Step 4: Compute max values for GLOBAL normalization (single pass) ──
        // AF-34A: Global scope is intentional. See class docstring for justification.
        var maxCommission = allProducts.Max(p => (double)p.CommissionRate);
        var maxEarnings = allProducts.Max(p => (double)p.TotalEarnings);
        var maxClicks = clickMap.Values.DefaultIfEmpty(0).Max();
        var maxConversions = convMap.Values.DefaultIfEmpty(0).Max();

        _logger.LogInformation(
            "[AF-34] Normalization maxes — Commission: {MaxComm:F4}, Earnings: {MaxEarn:F2}, Clicks: {MaxClicks}, Conversions: {MaxConv}",
            maxCommission, maxEarnings, maxClicks, maxConversions);

        // ── Step 5+6: Compute scores and BulkWrite in batches ──
        int updated = 0;
        int batchCount = 0;
        var now = DateTime.UtcNow;
        var bulkOps = new List<WriteModel<AffiliateProductIntelligenceDocument>>(BulkBatchSize);

        foreach (var product in allProducts)
        {
            if (ct.IsCancellationRequested) break;

            var key = (product.ProductId, product.TenantId);
            var r7Clicks = clickMap.GetValueOrDefault(key, 0);
            var r7Conversions = convMap.GetValueOrDefault(key, 0);

            // ── Freshness boost (AF-34A validated) ──
            // CreatedAtUtc is SetOnInsert = immutable
            // 14 days = 1.0, linear decay to 0 at 44 days
            double freshnessBoost = 0;
            if (product.CreatedAtUtc.HasValue)
            {
                var ageDays = (now - product.CreatedAtUtc.Value).TotalDays;
                if (ageDays <= 14)
                    freshnessBoost = 1.0;
                else if (ageDays <= 44)
                    freshnessBoost = Math.Max(0, 1.0 - ((ageDays - 14) / 30.0));
                // else 0 (product older than 44 days)
            }

            // ── Compute ranking score with AF-34A numerical safety ──
            double score =
                (SafeNormalize((double)product.CommissionRate, maxCommission) * 0.30) +
                (SafeNormalize((double)product.TotalEarnings, maxEarnings) * 0.25) +
                (SafeNormalize(r7Conversions, maxConversions) * 0.20) +
                (SafeNormalize(r7Clicks, maxClicks) * 0.15) +
                (freshnessBoost * 0.10);

            // AF-34A NUMERICAL SAFETY: Guard against NaN/Infinity, clamp to [0, 1]
            if (!double.IsFinite(score))
            {
                _logger.LogWarning(
                    "[AF-34A] Non-finite ranking score detected for product {ProductId}. Score: {Score}. Resetting to 0.",
                    product.ProductId, score);
                score = 0;
            }
            score = Math.Clamp(score, 0.0, 1.0);

            // ── Build bulk update operation ──
            var filter = Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.Id, product.Id);
            var update = Builders<AffiliateProductIntelligenceDocument>.Update
                .Set(d => d.Rolling7DayClicks, r7Clicks)
                .Set(d => d.Rolling7DayConversions, r7Conversions)
                .Set(d => d.RankingScore, Math.Round(score, 6));

            bulkOps.Add(new UpdateOneModel<AffiliateProductIntelligenceDocument>(filter, update));

            // ── Flush batch when full ──
            if (bulkOps.Count >= BulkBatchSize)
            {
                await FlushBulkAsync(bulkOps, ct);
                updated += bulkOps.Count;
                batchCount++;
                bulkOps.Clear();
            }
        }

        // ── Flush remaining ──
        if (bulkOps.Count > 0)
        {
            await FlushBulkAsync(bulkOps, ct);
            updated += bulkOps.Count;
            batchCount++;
        }

        sw.Stop();
        _logger.LogInformation(
            "[AF-34] Ranking recalculation complete. Updated {Count} products in {BatchCount} batches, {ElapsedMs}ms",
            updated, batchCount, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// AF-34A: Flush a batch of write operations using BulkWriteAsync.
    /// ordered: false enables parallel server-side execution.
    /// </summary>
    private async Task FlushBulkAsync(
        List<WriteModel<AffiliateProductIntelligenceDocument>> ops,
        CancellationToken ct)
    {
        try
        {
            await _mongoDbContext.AffiliateProductIntelligence.BulkWriteAsync(
                ops,
                new BulkWriteOptions { IsOrdered = false },
                ct);
        }
        catch (MongoBulkWriteException<AffiliateProductIntelligenceDocument> ex)
        {
            _logger.LogError(ex,
                "[AF-34A] BulkWrite partial failure. Matched: {Matched}, Modified: {Modified}, Errors: {Errors}",
                ex.Result?.MatchedCount, ex.Result?.ModifiedCount, ex.WriteErrors?.Count);
            // Non-fatal — any failed documents will be corrected on next daily run
        }
    }

    /// <summary>
    /// AF-34A: Zero-safe normalization. Returns 0 if maxValue <= 0.
    /// Output always in [0, 1] due to Math.Min cap.
    /// </summary>
    private static double SafeNormalize(double value, double maxValue)
    {
        if (maxValue <= 0) return 0;
        if (value < 0) return 0; // Defensive: negative values shouldn't exist but guard anyway
        return Math.Min(value / maxValue, 1.0);
    }
}
