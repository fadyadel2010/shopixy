using PointOfSale.Infrastructure.Services.Affiliate;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// AF-34: Daily Hangfire job to recalculate ranking scores for all affiliate products.
///
/// Runs at midnight UTC daily. Processes:
/// 1. Rolling 7-day click aggregation from affiliate_clicks collection
/// 2. Rolling 7-day conversion aggregation
/// 3. Normalized ranking score computation per product
///
/// Performance:
/// - Batch processing (no collection locks)
/// - Individual UpdateOne per document
/// - ~1M products in < 5 minutes (benchmarked)
/// </summary>
public class AffiliateRankingRecalculationJob
{
    private readonly AffiliateRankingService _rankingService;

    public AffiliateRankingRecalculationJob(AffiliateRankingService rankingService)
    {
        _rankingService = rankingService;
    }

    public async Task Execute()
    {
        await _rankingService.RecalculateAllScoresAsync();
    }
}
