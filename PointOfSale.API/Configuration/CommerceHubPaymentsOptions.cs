namespace PointOfSale.API.Configuration;

public class CommerceHubPaymentsOptions
{
    public decimal PlatformCommissionRate { get; set; } = 0.10m;
    public int MaxAttemptsPerOrder { get; set; } = 3;
    public int CommissionRulesCacheDurationSeconds { get; set; } = 30;
    public CommerceHubStripeOptions Stripe { get; set; } = new();
}

public class CommerceHubStripeOptions
{
    public bool Enabled { get; set; }
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}
