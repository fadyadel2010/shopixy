namespace PointOfSale.API.Services.Affiliate;

/// <summary>
/// AF-12: Affiliate Mode Metrics Service.
/// 
/// Tracks realtime metrics for Affiliate Mode operations:
/// - Affiliate Mode request volume
/// - Blocked product access attempts
/// - Affiliate Mode activation rate
/// - Per-affiliate traffic metrics
/// 
/// Uses Redis hot storage for realtime counters with hourly windows.
/// </summary>
public interface IAffiliateModeMetricsService
{
    /// <summary>
    /// Records an Affiliate Mode request.
    /// Increments: affiliate_mode_requests_total
    /// </summary>
    Task RecordAffiliateModeRequestAsync(
        Guid tenantId,
        Guid affiliateAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a blocked product access attempt in Affiliate Mode.
    /// Increments: affiliate_mode_blocked_products_total
    /// </summary>
    Task RecordBlockedProductAccessAsync(
        Guid tenantId,
        Guid affiliateAccountId,
        Guid productId,
        string endpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records Affiliate Mode activation (when middleware sets the flag).
    /// Increments: affiliate_mode_activations_total
    /// </summary>
    Task RecordAffiliateModeActivationAsync(
        Guid tenantId,
        Guid affiliateAccountId,
        string referralCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets Affiliate Mode request count for a time window.
    /// </summary>
    Task<long> GetAffiliateModeRequestCountAsync(
        Guid tenantId,
        int windowMinutes = 60,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets blocked product access count for a time window.
    /// </summary>
    Task<long> GetBlockedProductCountAsync(
        Guid tenantId,
        int windowMinutes = 60,
        CancellationToken cancellationToken = default);
}
