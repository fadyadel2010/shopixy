using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Shipping;

/// <summary>
/// Shipping quote service for storefront checkout.
///
/// LEGACY COMPATIBILITY:
/// This service returns carrier-rate compatibility quotes only. It does not
/// admit checkout shipping choices; canonical checkout admission is
/// ShippingMethod -> FulfillmentWarehouseId -> WarehouseResolution.
/// 
/// ARCHITECTURAL RULES:
/// - Queries ALL enabled carriers for quotes
/// - Normalizes and sorts results
/// - Caches quotes briefly (carrier rates can change)
/// - Frontend sees unified quote format, never carrier specifics
/// </summary>
public interface IShippingQuoteService
{
    /// <summary>
    /// Get shipping quotes from all enabled carriers.
    /// Called during checkout for rate display.
    /// </summary>
    Task<ShippingQuotesResponse> GetQuotesAsync(
        Guid tenantId,
        GetShippingQuotesRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for shipping quotes.
/// </summary>
public class GetShippingQuotesRequest
{
    public required string DestinationCity { get; set; }
    public required string DestinationCountry { get; set; }
    public string? DestinationZipCode { get; set; }
    
    public decimal WeightKg { get; set; }
    public int PackageCount { get; set; } = 1;
    
    public bool IsCashOnDelivery { get; set; }
    public decimal? CodAmount { get; set; }
    
    /// <summary>
    /// Order subtotal (for free shipping calculation).
    /// </summary>
    public decimal OrderSubtotal { get; set; }
}

/// <summary>
/// Response with all available shipping quotes.
/// </summary>
public class ShippingQuotesResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    
    public List<ShippingQuoteOption> Options { get; set; } = new();
}

/// <summary>
/// Shipping option for storefront display.
/// </summary>
public class ShippingQuoteOption
{
    public required string CarrierCode { get; set; }
    public required string CarrierName { get; set; }
    public required string ServiceLevel { get; set; }
    public required string ServiceName { get; set; }
    
    /// <summary>
    /// Display cost (may be 0 for free shipping).
    /// </summary>
    public decimal Cost { get; set; }
    
    /// <summary>
    /// Original cost before free shipping discount.
    /// </summary>
    public decimal OriginalCost { get; set; }
    
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Estimated delivery in human-readable format.
    /// Example: "2-3 business days"
    /// </summary>
    public required string EstimatedDelivery { get; set; }
    
    public int EstimatedDeliveryDaysMin { get; set; }
    public int EstimatedDeliveryDaysMax { get; set; }
    
    public bool SupportsCod { get; set; }
    public bool IsFreeShipping { get; set; }
    
    /// <summary>
    /// Carrier-specific quote ID (for booking).
    /// </summary>
    public string? QuoteId { get; set; }
}

/// <summary>
/// Implementation of shipping quote service.
/// </summary>
public class ShippingQuoteService : IShippingQuoteService
{
    private readonly IShippingCarrierRegistry _carrierRegistry;
    private readonly ICarrierIntegrationAuthority _carrierIntegrationAuthority;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ShippingQuoteService> _logger;
    
    private const string QuoteCachePrefix = "shipping:quotes:";
    private static readonly TimeSpan QuoteCacheDuration = TimeSpan.FromMinutes(5);
    
    public ShippingQuoteService(
        IShippingCarrierRegistry carrierRegistry,
        ICarrierIntegrationAuthority carrierIntegrationAuthority,
        IConnectionMultiplexer redis,
        ILogger<ShippingQuoteService> logger)
    {
        _carrierRegistry = carrierRegistry;
        _carrierIntegrationAuthority = carrierIntegrationAuthority;
        _redis = redis;
        _logger = logger;
    }
    
    public async Task<ShippingQuotesResponse> GetQuotesAsync(
        Guid tenantId,
        GetShippingQuotesRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Getting shipping quotes for tenant {TenantId} to {City}, {Country}",
            tenantId, request.DestinationCity, request.DestinationCountry);
        
        try
        {
            var carrierConfigs = await _carrierIntegrationAuthority.ListRuntimeConfigurationsAsync(
                tenantId,
                CarrierIntegrationPurpose.Quote,
                cancellationToken);
            
            if (!carrierConfigs.Any())
            {
                _logger.LogWarning("No enabled carriers for tenant {TenantId}", tenantId);
                return new ShippingQuotesResponse
                {
                    Success = true,
                    Options = new List<ShippingQuoteOption>()
                };
            }
            
            var allQuotes = new List<ShippingQuoteOption>();
            
            // Query each enabled carrier in parallel
            var quoteTasks = carrierConfigs
                .Select(config => GetCarrierQuoteAsync(tenantId, config, request, cancellationToken))
                .ToList();
            
            var results = await Task.WhenAll(quoteTasks);
            
            foreach (var result in results.Where(r => r != null))
            {
                allQuotes.AddRange(result!);
            }
            
            // Sort by cost (cheapest first), then by delivery time
            allQuotes = allQuotes
                .OrderBy(q => q.Cost)
                .ThenBy(q => q.EstimatedDeliveryDaysMin)
                .ToList();
            
            _logger.LogInformation(
                "Got {Count} shipping quotes for tenant {TenantId}",
                allQuotes.Count, tenantId);
            
            return new ShippingQuotesResponse
            {
                Success = true,
                Options = allQuotes
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting shipping quotes for tenant {TenantId}", tenantId);
            return new ShippingQuotesResponse
            {
                Success = false,
                ErrorMessage = "Failed to get shipping quotes"
            };
        }
    }
    
    private async Task<List<ShippingQuoteOption>?> GetCarrierQuoteAsync(
        Guid tenantId,
        CarrierIntegrationConfigurationSnapshot config,
        GetShippingQuotesRequest request,
        CancellationToken cancellationToken)
    {
        if (config.UseCarrierRates && CarrierOperationSupports.Describe(config.CarrierCode)
                .Single(operation => operation.Operation == "quote").Implementation != "Implemented") return null;
        var adapter = _carrierRegistry.GetAdapter(config.CarrierCode);
        // This anonymous compatibility path has no admitted packed parcel or warehouse
        // origin. DHL estimates use the authenticated shipment parcel-quote boundary.
        if (config.CarrierCode == PointOfSale.Core.Constants.CarrierCodes.Dhl) return null;
        if (adapter == null)
        {
            _logger.LogWarning(
                "Carrier adapter not found for {CarrierCode} (tenant may have invalid config)",
                config.CarrierCode);
            return null;
        }
        
        try
        {
            // Skip if COD required but carrier doesn't support it
            if (request.IsCashOnDelivery && !config.SupportsCod)
            {
                _logger.LogDebug(
                    "Skipping carrier {CarrierCode} - COD required but not supported",
                    config.CarrierCode);
                return null;
            }
            
            var credentialsResolution = await _carrierIntegrationAuthority.ResolveCredentialsAsync(
                tenantId,
                config.CarrierCode,
                CarrierIntegrationPurpose.Quote,
                cancellationToken);
            if (!credentialsResolution.Succeeded || credentialsResolution.Credentials == null)
            {
                _logger.LogWarning(
                    "Skipping carrier {CarrierCode} quote because integration credentials are unavailable: {Blockers}",
                    config.CarrierCode,
                    string.Join(",", credentialsResolution.Blockers));
                return null;
            }
            
            // Merchant fixed-price methods are not carrier rates and need no guessed origin.
            if (!config.UseCarrierRates && config.FixedShippingCost.HasValue)
                return BuildFixedRateQuotes(config, request);
            // Never substitute another account's origin or fabricate Cairo/EG.
            // This compatibility path still is not canonical warehouse/parcel evidence.
            if (string.IsNullOrWhiteSpace(config.PickupCity) || string.IsNullOrWhiteSpace(config.PickupCountry)) return null;
            var quoteRequest = new ShippingQuoteRequest
            {
                OriginCity = config.PickupCity,
                OriginCountry = config.PickupCountry,
                DestinationCity = request.DestinationCity,
                DestinationCountry = request.DestinationCountry,
                DestinationZipCode = request.DestinationZipCode,
                WeightKg = request.WeightKg,
                PackageCount = request.PackageCount,
                IsCashOnDelivery = request.IsCashOnDelivery,
                CodAmount = request.CodAmount
            };
            
            // Get quotes from carrier
            var result = await adapter.GetQuoteAsync(quoteRequest, credentialsResolution.Credentials, cancellationToken);
            
            if (!result.Success)
            {
                _logger.LogWarning(
                    "Carrier {CarrierCode} quote failed: {Error}",
                    config.CarrierCode, result.ErrorMessage);
                return null;
            }
            
            // Convert to quote options
            var options = result.Quotes.Select(q => 
            {
                var cost = ApplyMarkup(q.Cost, config.MarkupPercentage);
                var isFreeShipping = config.FreeShippingThreshold.HasValue && 
                                     request.OrderSubtotal >= config.FreeShippingThreshold.Value;
                
                return new ShippingQuoteOption
                {
                    CarrierCode = config.CarrierCode,
                    CarrierName = config.DisplayName,
                    ServiceLevel = q.ServiceLevel,
                    ServiceName = q.ServiceName,
                    Cost = isFreeShipping ? 0 : cost,
                    OriginalCost = cost,
                    Currency = q.Currency,
                    EstimatedDelivery = FormatDeliveryDays(q.EstimatedDeliveryDaysMin, q.EstimatedDeliveryDaysMax),
                    EstimatedDeliveryDaysMin = q.EstimatedDeliveryDaysMin,
                    EstimatedDeliveryDaysMax = q.EstimatedDeliveryDaysMax,
                    SupportsCod = q.SupportsCod,
                    IsFreeShipping = isFreeShipping,
                    QuoteId = q.CarrierQuoteId
                };
            }).ToList();
            
            return options;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting quote from carrier {CarrierCode}", config.CarrierCode);
            return null;
        }
    }
    
    private List<ShippingQuoteOption> BuildFixedRateQuotes(
        CarrierIntegrationConfigurationSnapshot config,
        GetShippingQuotesRequest request)
    {
        var isFreeShipping = config.FreeShippingThreshold.HasValue && 
                             request.OrderSubtotal >= config.FreeShippingThreshold.Value;
        var cost = config.FixedShippingCost ?? 0;
        
        return new List<ShippingQuoteOption>
        {
            new ShippingQuoteOption
            {
                CarrierCode = config.CarrierCode,
                CarrierName = config.DisplayName,
                ServiceLevel = config.DefaultServiceLevel,
                ServiceName = $"{config.DisplayName} Delivery",
                Cost = isFreeShipping ? 0 : cost,
                OriginalCost = cost,
                EstimatedDelivery = "3-5 business days",
                EstimatedDeliveryDaysMin = 3,
                EstimatedDeliveryDaysMax = 5,
                SupportsCod = config.SupportsCod,
                IsFreeShipping = isFreeShipping
            }
        };
    }
    
    private static decimal ApplyMarkup(decimal cost, decimal markupPercentage)
    {
        if (markupPercentage <= 0)
            return cost;
            
        return cost * (1 + markupPercentage / 100);
    }
    
    private static string FormatDeliveryDays(int min, int max)
    {
        if (min == max)
        {
            return min == 1 ? "1 business day" : $"{min} business days";
        }
        return $"{min}-{max} business days";
    }
}
