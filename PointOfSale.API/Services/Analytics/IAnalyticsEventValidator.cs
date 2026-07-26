using PointOfSale.API.DTOs.Analytics;
using PointOfSale.Core.Analytics;

namespace PointOfSale.API.Services.Analytics;

/// <summary>
/// Validation result for an analytics event.
/// </summary>
public class AnalyticsValidationResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public AnalyticsEvent? Event { get; set; }

    public static AnalyticsValidationResult Success(AnalyticsEvent evt) => new()
    {
        IsValid = true,
        Event = evt
    };

    public static AnalyticsValidationResult Failure(string error) => new()
    {
        IsValid = false,
        Error = error
    };
}

/// <summary>
/// Interface for analytics event validation.
/// </summary>
public interface IAnalyticsEventValidator
{
    /// <summary>
    /// Validates and transforms an analytics event request.
    /// </summary>
    /// <param name="request">The request DTO</param>
    /// <param name="tenantId">Tenant ID (from X-Tenant-Id header)</param>
    /// <param name="customerId">Customer ID (from JWT, null for guests)</param>
    /// <param name="ipAddress">Client IP (from HttpContext)</param>
    /// <param name="userAgent">User agent (from headers)</param>
    /// <param name="acceptLanguage">Accept-Language header</param>
    /// <param name="currency">Tenant currency</param>
    /// <returns>Validation result with transformed event</returns>
    AnalyticsValidationResult Validate(
        AnalyticsEventRequestDto request,
        Guid tenantId,
        Guid? customerId,
        string ipAddress,
        string userAgent,
        string? acceptLanguage,
        string currency);
}
