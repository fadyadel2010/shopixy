using System.ComponentModel.DataAnnotations;

namespace PointOfSale.Infrastructure.Services.Affiliate;

/// <summary>
/// Configuration options for affiliate settlement orchestration.
/// </summary>
public class AffiliateSettlementOptions
{
    /// <summary>
    /// Number of minutes before considering accounting outbox messages as stale.
    /// Default: 15 minutes.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "AccountingStaleMinutes must be greater than 0")]
    public int AccountingStaleMinutes { get; set; } = 15;

    /// <summary>
    /// Number of minutes before considering integration outbox messages as stale.
    /// Default: 30 minutes.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "IntegrationStaleMinutes must be greater than 0")]
    public int IntegrationStaleMinutes { get; set; } = 30;

    /// <summary>
    /// Requires payout execution to be backed by trusted external transfer evidence.
    /// Defaults to true so production cannot mark affiliate money paid from a typed reference alone.
    /// </summary>
    public bool RequireVerifiedExternalPayoutEvidence { get; set; } = true;

    /// <summary>
    /// Break-glass rollout switch for deployments that still need manual payout evidence capture.
    /// Keep false in real-money production unless the override is separately audited.
    /// </summary>
    public bool AllowManualPayoutEvidenceOverride { get; set; } = false;

    /// <summary>
    /// Requires manual fulfillment delivery evidence to come from a trusted verification authority
    /// before affiliate commissions can be approved.
    /// </summary>
    public bool RequireVerifiedManualDeliveryEvidence { get; set; } = true;

    /// <summary>
    /// Break-glass rollout switch for manual fulfillment proof that has not yet been provider-verified.
    /// Keep false in real-money production unless the override is separately audited.
    /// </summary>
    public bool AllowManualDeliveryEvidenceOverride { get; set; } = false;

    /// <summary>
    /// Shared HMAC secret held by the trusted bank/provider reconciliation connector.
    /// Supply through environment or secret manager; never commit a production value.
    /// </summary>
    public string ExternalEvidenceHmacSecret { get; set; } = string.Empty;

    [Range(1, 60)]
    public int ExternalEvidenceMaxAgeMinutes { get; set; } = 10;
}

public static class AffiliateSettlementOptionsProductionGuard
{
    public static IReadOnlyList<string> GetViolations(AffiliateSettlementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var violations = new List<string>();

        if (!options.RequireVerifiedExternalPayoutEvidence)
        {
            violations.Add($"{nameof(AffiliateSettlementOptions.RequireVerifiedExternalPayoutEvidence)} must be true");
        }

        if (options.AllowManualPayoutEvidenceOverride)
        {
            violations.Add($"{nameof(AffiliateSettlementOptions.AllowManualPayoutEvidenceOverride)} must be false");
        }

        if (!options.RequireVerifiedManualDeliveryEvidence)
        {
            violations.Add($"{nameof(AffiliateSettlementOptions.RequireVerifiedManualDeliveryEvidence)} must be true");
        }

        if (options.AllowManualDeliveryEvidenceOverride)
        {
            violations.Add($"{nameof(AffiliateSettlementOptions.AllowManualDeliveryEvidenceOverride)} must be false");
        }

        if (string.IsNullOrWhiteSpace(options.ExternalEvidenceHmacSecret) ||
            options.ExternalEvidenceHmacSecret.Length < 32)
        {
            violations.Add($"{nameof(AffiliateSettlementOptions.ExternalEvidenceHmacSecret)} must contain at least 32 characters");
        }

        return violations;
    }

    public static bool IsProductionSafe(AffiliateSettlementOptions options) =>
        GetViolations(options).Count == 0;
}
