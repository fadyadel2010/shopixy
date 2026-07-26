using PointOfSale.Core.Constants;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// Manual Carrier Definition - a first-class, No-Op carrier adapter.
/// 
/// ARCHITECTURAL RULES:
/// - "manual" is NOT a fallback - it's a real carrier
/// - No API calls, no tracking sync, no webhooks
/// - Used when merchant handles fulfillment themselves
/// - MUST be registered in ShippingCarrierRegistry
/// 
/// This carrier returns success for all operations but
/// performs no external API calls.
/// </summary>
public class ManualCarrierDefinition : IShippingCarrierAdapter
{
    /// <inheritdoc />
    public string CarrierCode => CarrierCodes.Manual;
    
    /// <inheritdoc />
    public string DisplayName => "Manual Fulfillment";
    
    /// <summary>
    /// Gets shipping quote. Manual carrier doesn't provide quotes.
    /// </summary>
    public Task<ShippingQuoteResult> GetQuoteAsync(
        ShippingQuoteRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ShippingQuoteResult
        {
            Success = false,
            ErrorCode = "NOT_SUPPORTED",
            ErrorMessage = "Manual carrier does not provide shipping quotes",
            Quotes = new List<ShippingQuote>()
        });
    }
    
    /// <summary>
    /// Creates a shipment. For manual carrier, this is a no-op that returns success.
    /// The shipment is already persisted in our database - no external API call needed.
    /// </summary>
    public Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        // Manual carrier doesn't call any external API
        // Shipment is already created in our database
        return Task.FromResult(new CreateShipmentResult
        {
            Success = true,
            ExternalReferenceId = null,
            TrackingNumber = null, // Manual shipments can have tracking added later
            TrackingUrl = null,
            AwbNumber = null,
            LabelUrl = null,
            LabelData = null,
            LabelFormat = null,
            CarrierMetadata = null,
            RawResponse = "{\"carrier\":\"manual\",\"status\":\"created\"}",
            InitialStatus = ShipmentStatus.Pending // Manual shipments start as Pending
        });
    }
    
    /// <summary>
    /// Cancels a shipment. For manual carrier, this is a no-op.
    /// </summary>
    public Task<CancelShipmentResult> CancelShipmentAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CancelShipmentResult
        {
            Success = true,
            ErrorCode = null,
            ErrorMessage = null
        });
    }
    
    /// <summary>
    /// Gets tracking info. Manual carrier doesn't support tracking.
    /// </summary>
    public Task<ShipmentTrackingResult> GetTrackingAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ShipmentTrackingResult
        {
            Success = false,
            ErrorCode = "NOT_SUPPORTED",
            ErrorMessage = "Manual carrier does not support tracking sync",
            CurrentStatus = ShipmentStatus.Pending
        });
    }
    
    /// <summary>
    /// Validates credentials. Manual carrier doesn't require credentials.
    /// </summary>
    public Task<CarrierValidationResult> ValidateCredentialsAsync(
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CarrierValidationResult
        {
            Success = true,
            ErrorCode = null,
            ErrorMessage = null,
            AccountName = "Manual Fulfillment"
        });
    }
    
    /// <summary>
    /// Gets supported service levels. Manual carrier has only standard.
    /// </summary>
    public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels()
    {
        return new List<ServiceLevelInfo>
        {
            new ServiceLevelInfo
            {
                Code = "standard",
                Name = "Standard Delivery",
                Description = "Manual fulfillment - delivery time determined by merchant",
                TypicalDaysMin = 1,
                TypicalDaysMax = 7
            }
        };
    }
    
    /// <summary>
    /// Parses webhook. Manual carrier doesn't receive webhooks.
    /// </summary>
    public WebhookParseResult? ParseWebhook(
        string payload,
        string? signature,
        string? webhookSecret)
    {
        // Manual carrier doesn't receive webhooks
        return null;
    }
}
