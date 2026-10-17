namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// Catalog identity only. Operational support and merchant coverage remain
/// unverified until the applicable Egypt contract is implemented and admitted.
/// </summary>
public sealed class MylerzCarrierDefinition : ShippingCarrierDefinition
{
    public override string CarrierCode => "mylerz";
    public override string DisplayName => "Mylerz";
    public override bool SupportsCod => false;
    public override bool SupportsPrepaid => false;
    public override bool SupportsTracking => false;
    public override bool SupportsWebhooks => false;
    public override bool SupportsPickupScheduling => false;
    public override bool SupportsLabels => false;
    public override bool SupportsCancellation => false;
    public override bool SupportsQuotes => false;
    public override IReadOnlyList<string> SupportedCountries => Array.Empty<string>();
    public override string DefaultCurrency => "EGP";
}
