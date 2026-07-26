namespace PointOfSale.Infrastructure.Eventing;

public sealed class MarketplaceEventAlertingOptions
{
    public const string SectionName = "MarketplaceEventAlerting";

    /// <summary>
    /// Optional webhook endpoint for operational dead-letter escalation.
    /// Empty disables remote escalation and keeps local logging only.
    /// </summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Optional bearer token for webhook authorization.
    /// </summary>
    public string? WebhookBearerToken { get; set; }

    public int HttpTimeoutSeconds { get; set; } = 10;
}
