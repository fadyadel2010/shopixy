using PointOfSale.API.Services.Affiliate;

namespace PointOfSale.API.Helpers;

/// <summary>
/// AF-12: Extension methods for Affiliate Mode operations.
/// </summary>
public static class AffiliateModeExtensions
{
    /// <summary>
    /// Records a blocked product access attempt when Affiliate Mode filters out a product.
    /// This is a fire-and-forget operation that won't block the request.
    /// </summary>
    public static void RecordBlockedProductAccess(
        this HttpContext context,
        Guid tenantId,
        Guid productId,
        string endpoint)
    {
        // Only record if Affiliate Mode is active
        if (!AffiliateModeResolver.IsAffiliateMode(context))
            return;

        var affiliateAccountId = AffiliateModeResolver.GetAffiliateAccountId(context);
        if (!affiliateAccountId.HasValue)
            return;

        // Fire-and-forget: Record metric without blocking the response
        var metricsService = context.RequestServices.GetService<IAffiliateModeMetricsService>();
        if (metricsService != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await metricsService.RecordBlockedProductAccessAsync(
                        tenantId,
                        affiliateAccountId.Value,
                        productId,
                        endpoint);
                }
                catch
                {
                    // Swallow exceptions - metrics are non-critical
                }
            });
        }
    }

    /// <summary>
    /// Records an Affiliate Mode request (any storefront request with Affiliate Mode active).
    /// This is a fire-and-forget operation that won't block the request.
    /// </summary>
    public static void RecordAffiliateModeRequest(
        this HttpContext context,
        Guid tenantId)
    {
        // Only record if Affiliate Mode is active
        if (!AffiliateModeResolver.IsAffiliateMode(context))
            return;

        var affiliateAccountId = AffiliateModeResolver.GetAffiliateAccountId(context);
        if (!affiliateAccountId.HasValue)
            return;

        // Fire-and-forget: Record metric without blocking the response
        var metricsService = context.RequestServices.GetService<IAffiliateModeMetricsService>();
        if (metricsService != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await metricsService.RecordAffiliateModeRequestAsync(
                        tenantId,
                        affiliateAccountId.Value);
                }
                catch
                {
                    // Swallow exceptions - metrics are non-critical
                }
            });
        }
    }

    /// <summary>
    /// AF-12: Adds X-Affiliate-Mode response header for frontend detection.
    /// Frontend can use this to show "🎯 Shopping via Partner" badge.
    /// </summary>
    public static void AddAffiliateModeHeaders(
        this HttpContext context)
    {
        if (!AffiliateModeResolver.IsAffiliateMode(context))
            return;

        var response = context.Response;
        
        // Add main affiliate mode flag
        response.Headers["X-Affiliate-Mode"] = "true";
        
        // Add affiliate account ID for tracking
        var affiliateAccountId = AffiliateModeResolver.GetAffiliateAccountId(context);
        if (affiliateAccountId.HasValue)
        {
            response.Headers["X-Affiliate-Account-Id"] = affiliateAccountId.Value.ToString();
        }
    }
}
