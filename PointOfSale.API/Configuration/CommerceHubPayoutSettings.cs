namespace PointOfSale.API.Configuration;

public class CommerceHubPayoutSettings
{
    public int PayoutDelayDays { get; set; } = 7;
    public int LargePayableDelayDays { get; set; } = 14;
    public decimal LargePayableAlertAmount { get; set; } = 1000m;
    
    /// <summary>
    /// Tolerance threshold for ledger drift detection.
    /// Differences below this amount are considered acceptable rounding/timing variance.
    /// </summary>
    public decimal LedgerDriftToleranceThreshold { get; set; } = 0.01m;
}
