using PointOfSale.Core.Constants;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// FedEx carrier capability declaration for the canonical runtime foundation.
/// Capabilities are declared as officially documented but not runtime-certified.
/// </summary>
public sealed class FedExCarrierDefinition : ShippingCarrierDefinition
{
    public override string CarrierCode => CarrierCodes.FedEx;
    public override string DisplayName => "FedEx";
    public override string? LogoUrl => null;

    public override bool SupportsCod => false;
    public override bool SupportsPrepaid => true;
    public override bool SupportsTracking => true;
    public override bool SupportsWebhooks => true;
    public override bool SupportsPickupScheduling => true;
    public override bool SupportsLabels => true;
    public override bool SupportsCancellation => true;
    public override bool SupportsQuotes => false;

    public override IReadOnlyList<string> SupportedCountries => Array.Empty<string>();
    public override string DefaultCurrency => "USD";
}
