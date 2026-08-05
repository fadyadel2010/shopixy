using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Attributes;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront shipping quotes API.
/// 
/// CARRIER-AGNOSTIC SHIPPING MODULE
/// 
/// This controller provides shipping quotes to checkout.
/// Frontend sees unified quote format, never carrier specifics.
/// 
/// ARCHITECTURAL RULES:
/// ❌ Frontend never knows carrier API details
/// ❌ Frontend never calculates shipping
/// ✅ Backend handles all carrier logic
/// ✅ Quotes are normalized and sorted
/// </summary>
[ApiController]
[Route("api/storefront/shipping")]
public class StorefrontShippingQuotesController : ControllerBase
{
    private readonly IShippingQuoteService _quoteService;
    private readonly ITenantContext _tenantContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StorefrontShippingQuotesController> _logger;
    
    public StorefrontShippingQuotesController(
        IShippingQuoteService quoteService,
        ITenantContext tenantContext,
        ApplicationDbContext dbContext,
        ILogger<StorefrontShippingQuotesController> logger)
    {
        _quoteService = quoteService;
        _tenantContext = tenantContext;
        _dbContext = dbContext;
        _logger = logger;
    }
    
    /// <summary>
    /// Get shipping quotes for checkout.
    /// Returns rates from all enabled carriers.
    /// </summary>
    /// <remarks>
    /// Called during checkout to display shipping options.
    /// 
    /// Response includes:
    /// - All enabled carriers for tenant
    /// - Multiple service levels per carrier (standard, express, etc.)
    /// - COD availability per option
    /// - Free shipping if threshold met
    /// 
    /// Sorted by cost (cheapest first).
    /// </remarks>
    [HttpPost("quotes")]
    [AllowAnonymous] // Guest checkout must work
    [AllowAnonymousTenant]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ShippingQuotesResponseDto>> GetQuotes(
        [FromBody] GetShippingQuotesRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || _tenantContext.TenantId.Value == Guid.Empty)
        {
            return BadRequest(new { error = "Tenant context is required" });
        }

        var tenantId = _tenantContext.TenantId.Value;

        if (request == null)
        {
            return BadRequest(new { error = "Shipping quote request is required." });
        }

        var dtoValidationError = request.Validate();
        if (dtoValidationError != null)
        {
            return BadRequest(new { error = dtoValidationError });
        }
        
        _logger.LogInformation(
            "GET shipping quotes for tenant {TenantId} to {City}, {Country}",
            tenantId, request.DestinationCity, request.DestinationCountry);

        if (!string.IsNullOrWhiteSpace(request.DestinationCountry) &&
            !CountryCodeHelper.TryNormalizeCountryCode(request.DestinationCountry, out _))
        {
            return BadRequest(new { error = "Destination country must be a valid ISO 3166-1 alpha-2 code." });
        }

        var tenantCountryCode = await ResolveTenantCountryCodeAsync(tenantId, cancellationToken);
        
        var serviceRequest = new GetShippingQuotesRequest
        {
            DestinationCity = request.DestinationCity ?? "",
            DestinationCountry = CountryCodeHelper.NormalizeOrDefault(request.DestinationCountry, tenantCountryCode),
            DestinationZipCode = request.DestinationZipCode,
            WeightKg = request.WeightKg ?? 1.0m,
            PackageCount = request.PackageCount ?? 1,
            IsCashOnDelivery = request.IsCashOnDelivery ?? false,
            CodAmount = request.CodAmount,
            OrderSubtotal = request.OrderSubtotal ?? 0
        };
        
        var result = await _quoteService.GetQuotesAsync(tenantId, serviceRequest, cancellationToken);
        
        if (!result.Success)
        {
            return StatusCode(500, new { error = result.ErrorMessage });
        }

        return Ok(new ShippingQuotesResponseDto
        {
            Success = true,
            Options = result.Options.Select(o => new ShippingQuoteOptionDto
            {
                CarrierCode = o.CarrierCode,
                CarrierName = o.CarrierName,
                ServiceLevel = o.ServiceLevel,
                ServiceName = o.ServiceName,
                Cost = o.Cost,
                OriginalCost = o.OriginalCost,
                Currency = o.Currency,
                EstimatedDelivery = o.EstimatedDelivery,
                EstimatedDeliveryDaysMin = o.EstimatedDeliveryDaysMin,
                EstimatedDeliveryDaysMax = o.EstimatedDeliveryDaysMax,
                SupportsCod = o.SupportsCod,
                IsFreeShipping = o.IsFreeShipping,
                QuoteId = o.QuoteId
            }).ToList()
        });
    }

    private async Task<string?> ResolveTenantCountryCodeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Country)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

#region DTOs

/// <summary>
/// Request for shipping quotes.
/// </summary>
public class GetShippingQuotesRequestDto
{
    /// <summary>
    /// Destination city name.
    /// </summary>
    public string? DestinationCity { get; set; }
    
    /// <summary>
    /// Destination country code (ISO 2-letter).
    /// If omitted, the tenant default country is used.
    /// </summary>
    public string? DestinationCountry { get; set; }
    
    /// <summary>
    /// Destination ZIP/postal code (optional).
    /// </summary>
    public string? DestinationZipCode { get; set; }
    
    /// <summary>
    /// Total package weight in Kg.
    /// Default: 1.0
    /// </summary>
    public decimal? WeightKg { get; set; }
    
    /// <summary>
    /// Number of packages.
    /// Default: 1
    /// </summary>
    public int? PackageCount { get; set; }
    
    /// <summary>
    /// Whether order is Cash on Delivery.
    /// </summary>
    public bool? IsCashOnDelivery { get; set; }
    
    /// <summary>
    /// COD amount (required if IsCashOnDelivery).
    /// </summary>
    public decimal? CodAmount { get; set; }
    
    /// <summary>
    /// Order subtotal (for free shipping threshold).
    /// </summary>
    public decimal? OrderSubtotal { get; set; }

    public string? Validate()
    {
        if (WeightKg.HasValue && WeightKg.Value < 0m)
        {
            return "Weight cannot be negative.";
        }

        if (PackageCount.HasValue && PackageCount.Value <= 0)
        {
            return "Package count must be greater than zero.";
        }

        if (IsCashOnDelivery == true && CodAmount.HasValue && CodAmount.Value < 0m)
        {
            return "COD amount cannot be negative.";
        }

        if (OrderSubtotal.HasValue && OrderSubtotal.Value < 0m)
        {
            return "Order subtotal cannot be negative.";
        }

        return null;
    }
}

/// <summary>
/// Response with shipping options.
/// </summary>
public class ShippingQuotesResponseDto
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<ShippingQuoteOptionDto> Options { get; set; } = new();
}

/// <summary>
/// Individual shipping option for display.
/// </summary>
public class ShippingQuoteOptionDto
{
    /// <summary>
    /// Carrier identifier (e.g., "bosta", "aramex").
    /// </summary>
    public required string CarrierCode { get; set; }
    
    /// <summary>
    /// Carrier display name (e.g., "Bosta", "Aramex").
    /// </summary>
    public required string CarrierName { get; set; }
    
    /// <summary>
    /// Service level code (e.g., "standard", "express").
    /// </summary>
    public required string ServiceLevel { get; set; }
    
    /// <summary>
    /// Service display name (e.g., "Bosta Standard Delivery").
    /// </summary>
    public required string ServiceName { get; set; }
    
    /// <summary>
    /// Final shipping cost (0 if free shipping).
    /// </summary>
    public decimal Cost { get; set; }
    
    /// <summary>
    /// Original cost before free shipping discount.
    /// </summary>
    public decimal OriginalCost { get; set; }
    
    /// <summary>
    /// Currency code (e.g., "EGP", "USD").
    /// </summary>
    public required string Currency { get; set; }
    
    /// <summary>
    /// Human-readable delivery estimate (e.g., "2-4 business days").
    /// </summary>
    public required string EstimatedDelivery { get; set; }
    
    /// <summary>
    /// Minimum delivery days.
    /// </summary>
    public int EstimatedDeliveryDaysMin { get; set; }
    
    /// <summary>
    /// Maximum delivery days.
    /// </summary>
    public int EstimatedDeliveryDaysMax { get; set; }
    
    /// <summary>
    /// Whether this option supports Cash on Delivery.
    /// </summary>
    public bool SupportsCod { get; set; }
    
    /// <summary>
    /// Whether free shipping is applied.
    /// </summary>
    public bool IsFreeShipping { get; set; }
    
    /// <summary>
    /// Quote ID for booking (if required by carrier).
    /// </summary>
    public string? QuoteId { get; set; }
}

#endregion
