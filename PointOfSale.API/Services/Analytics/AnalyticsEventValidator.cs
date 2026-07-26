using Microsoft.Extensions.Logging;
using PointOfSale.API.DTOs.Analytics;
using PointOfSale.Core.Analytics;

namespace PointOfSale.API.Services.Analytics;

/// <summary>
/// Validates and transforms analytics event requests.
/// 
/// SECURITY RESPONSIBILITIES:
/// 1. Validate event type is known
/// 2. Validate timestamp is reasonable (not future, not too old)
/// 3. Resolve actor identity from JWT (ignore frontend claims)
/// 4. Capture IP and UserAgent from server context
/// 5. Validate payload has required fields for event type
/// 6. Sanitize string inputs (length limits, no script injection)
/// 
/// NEVER TRUST FRONTEND FOR:
/// - CustomerId (resolve from JWT)
/// - IpAddress (capture from HttpContext)
/// - UserAgent (capture from headers)
/// - IsAuthenticated (derive from JWT presence)
/// - Monetary Value (must be derived server-side for authoritative events)
/// </summary>
public class AnalyticsEventValidator : IAnalyticsEventValidator
{
    private readonly ILogger<AnalyticsEventValidator> _logger;

    // Validation constants
    private const int MaxPageUrlLength = 2048;
    private const int MaxReferrerLength = 2048;
    private const int MaxSearchQueryLength = 500;
    private const int MaxSessionIdLength = 64;
    private const int MaxLanguageLength = 10;
    private const int MaxProductNameLength = 500;
    private const int MaxProductSkuLength = 100;
    private const int MaxFilterKeyLength = 100;
    private const int MaxFilterValueLength = 500;
    private const int MaxFiltersCount = 20;
    private const int MaxExtraCount = 50;
    private const int MaxUtmValueLength = 200;
    private readonly TimeSpan MaxEventAge = TimeSpan.FromHours(24);

    public AnalyticsEventValidator(ILogger<AnalyticsEventValidator> logger)
    {
        _logger = logger;
    }

    public AnalyticsValidationResult Validate(
        AnalyticsEventRequestDto request,
        Guid tenantId,
        Guid? customerId,
        string ipAddress,
        string userAgent,
        string? acceptLanguage,
        string currency)
    {
        var now = DateTime.UtcNow;

        // 1. Validate tenant
        if (tenantId == Guid.Empty)
        {
            return AnalyticsValidationResult.Failure("TenantId is required");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return AnalyticsValidationResult.Failure("Currency is required");
        }

        // 2. Validate event type
        if (!Enum.IsDefined(typeof(AnalyticsEventType), request.Type))
        {
            return AnalyticsValidationResult.Failure($"Invalid event type: {request.Type}");
        }

        // 3. Validate timestamp
        var occurredAt = request.OccurredAtUtc ?? now;
        if (occurredAt > now.AddMinutes(5))
        {
            return AnalyticsValidationResult.Failure("Event timestamp is in the future");
        }
        if (occurredAt < now.Subtract(MaxEventAge))
        {
            return AnalyticsValidationResult.Failure("Event is too old (>24 hours)");
        }

        // 4. Validate context
        if (request.Context == null)
        {
            return AnalyticsValidationResult.Failure("Context is required");
        }
        if (string.IsNullOrWhiteSpace(request.Context.PageUrl))
        {
            return AnalyticsValidationResult.Failure("PageUrl is required");
        }
        if (request.Context.PageUrl.Length > MaxPageUrlLength)
        {
            return AnalyticsValidationResult.Failure($"PageUrl exceeds max length ({MaxPageUrlLength})");
        }

        // Validate sanitized URLs (must not be empty after sanitization)
        var sanitizedPageUrl = SanitizeAnalyticsUrl(request.Context.PageUrl, MaxPageUrlLength);
        if (string.IsNullOrWhiteSpace(sanitizedPageUrl))
        {
            return AnalyticsValidationResult.Failure("PageUrl is invalid or empty after sanitization");
        }

        var sanitizedReferrer = SanitizeAnalyticsUrl(request.Context.Referrer, MaxReferrerLength);
        var utm = ExtractUtmParameters(request.Context.PageUrl);
        // Referrer is optional, but if provided, it must not be empty after sanitization
        if (!string.IsNullOrWhiteSpace(request.Context.Referrer) && string.IsNullOrWhiteSpace(sanitizedReferrer))
        {
            return AnalyticsValidationResult.Failure("Referrer is invalid or empty after sanitization");
        }

        // 5. Validate actor (guest must have SessionId)
        if (customerId == null)
        {
            if (request.Actor?.SessionId == null || string.IsNullOrWhiteSpace(request.Actor.SessionId))
            {
                return AnalyticsValidationResult.Failure("SessionId is required for guest users");
            }
            if (request.Actor.SessionId.Length > MaxSessionIdLength)
            {
                return AnalyticsValidationResult.Failure($"SessionId exceeds max length ({MaxSessionIdLength})");
            }
        }

        // 6. Validate payload based on event type
        var payloadValidation = ValidatePayload(request.Type, request.Payload);
        if (!payloadValidation.IsValid)
        {
            return payloadValidation;
        }

        // Build the canonical event
        var analyticsEvent = new AnalyticsEvent
        {
            EventId = request.EventId ?? Guid.NewGuid(),
            TenantId = tenantId,
            Type = request.Type,
            OccurredAtUtc = occurredAt,
            ReceivedAtUtc = now,
            SchemaVersion = 1,
            Actor = new AnalyticsActor
            {
                // SECURITY: CustomerId from JWT, NOT from frontend
                CustomerId = customerId,
                SessionId = request.Actor?.SessionId,
                // SECURITY: Derived from JWT presence, NOT from frontend
                IsAuthenticated = customerId.HasValue
            },
            Context = new AnalyticsContext
            {
                PageUrl = sanitizedPageUrl,
                Referrer = sanitizedReferrer,
                // SECURITY: Captured server-side, NOT from frontend
                UserAgent = userAgent,
                IpAddress = AnonymizeIp(ipAddress),
                Language = ExtractLanguage(request.Context.Language, acceptLanguage),
                Currency = currency.Trim().ToUpperInvariant(),
                DeviceType = DeriveDeviceType(userAgent),
                ScreenWidth = request.Context.ScreenWidth,
                ScreenHeight = request.Context.ScreenHeight,
                UtmSource = utm.GetValueOrDefault("utm_source"),
                UtmMedium = utm.GetValueOrDefault("utm_medium"),
                UtmCampaign = utm.GetValueOrDefault("utm_campaign"),
                UtmContent = utm.GetValueOrDefault("utm_content"),
                UtmTerm = utm.GetValueOrDefault("utm_term")
            },
            Payload = MapPayload(request.Payload)
        };

        return AnalyticsValidationResult.Success(analyticsEvent);
    }

    private static bool IsPayloadOptional(AnalyticsEventType type)
    {
        return type switch
        {
            AnalyticsEventType.PageView => true,
            AnalyticsEventType.PaymentFailed => true,
            AnalyticsEventType.CheckoutFinalizeFailure => true,
            AnalyticsEventType.AuthStateDegraded => true,
            AnalyticsEventType.CheckoutSelectionInvalidated => true,
            AnalyticsEventType.CartStateMismatchDetected => true,
            AnalyticsEventType.GuestPaymentRecoverySuccess => true,
            AnalyticsEventType.CartUnexpectedClearBlocked => true,
            AnalyticsEventType.TrackingTokenRemovedFromUrl => true,
            AnalyticsEventType.AuthDegradedRecovery => true,
            AnalyticsEventType.CheckoutStateLeakPrevented => true,
            _ => false
        };
    }

    private AnalyticsValidationResult ValidatePayload(AnalyticsEventType type, AnalyticsPayloadDto? payload)
    {
        if (payload == null)
        {
            // Some events don't require payload
            if (IsPayloadOptional(type))
            {
                return AnalyticsValidationResult.Success(null!);
            }
            return AnalyticsValidationResult.Failure("Payload is required for this event type");
        }

        var semanticValidation = type switch
        {
            AnalyticsEventType.PageView => AnalyticsValidationResult.Success(null!),
            
            AnalyticsEventType.ProductImpression => payload.ProductId.HasValue
                ? AnalyticsValidationResult.Success(null!)
                : AnalyticsValidationResult.Failure("ProductId is required for ProductImpression"),
            
            AnalyticsEventType.ProductView => payload.ProductId.HasValue
                ? AnalyticsValidationResult.Success(null!)
                : AnalyticsValidationResult.Failure("ProductId is required for ProductView"),
            
            AnalyticsEventType.SearchPerformed => !string.IsNullOrWhiteSpace(payload.SearchQuery)
                ? AnalyticsValidationResult.Success(null!)
                : AnalyticsValidationResult.Failure("SearchQuery is required for SearchPerformed"),
            
            AnalyticsEventType.FilterApplied => payload.Filters?.Count > 0
                ? AnalyticsValidationResult.Success(null!)
                : AnalyticsValidationResult.Failure("Filters is required for FilterApplied"),
            
            AnalyticsEventType.AddToCart => ValidateCartEvent(payload, "AddToCart"),
            
            AnalyticsEventType.RemoveFromCart => ValidateCartEvent(payload, "RemoveFromCart"),
            
            AnalyticsEventType.BeginCheckout => AnalyticsValidationResult.Success(null!),
            
            AnalyticsEventType.PurchaseCompleted => payload.OrderId.HasValue
                ? AnalyticsValidationResult.Success(null!)
                : AnalyticsValidationResult.Failure("OrderId is required for PurchaseCompleted"),
            
            AnalyticsEventType.PaymentFailed => AnalyticsValidationResult.Success(null!),

            AnalyticsEventType.CheckoutFinalizeFailure => AnalyticsValidationResult.Success(null!),

            AnalyticsEventType.AuthStateDegraded => AnalyticsValidationResult.Success(null!),

            AnalyticsEventType.CheckoutSelectionInvalidated => AnalyticsValidationResult.Success(null!),

            AnalyticsEventType.CartStateMismatchDetected => AnalyticsValidationResult.Success(null!),

            AnalyticsEventType.GuestPaymentRecoverySuccess => AnalyticsValidationResult.Success(null!),

            AnalyticsEventType.CartUnexpectedClearBlocked => AnalyticsValidationResult.Success(null!),

            AnalyticsEventType.TrackingTokenRemovedFromUrl => AnalyticsValidationResult.Success(null!),

            AnalyticsEventType.AuthDegradedRecovery => AnalyticsValidationResult.Success(null!),

            AnalyticsEventType.CheckoutStateLeakPrevented => AnalyticsValidationResult.Success(null!),
            
            _ => AnalyticsValidationResult.Failure($"Unknown event type: {type}")
        };

        if (!semanticValidation.IsValid)
        {
            return semanticValidation;
        }

        return ValidatePayloadShape(payload);
    }

    private AnalyticsValidationResult ValidateCartEvent(AnalyticsPayloadDto payload, string eventName)
    {
        if (!payload.ProductId.HasValue)
        {
            return AnalyticsValidationResult.Failure($"ProductId is required for {eventName}");
        }
        if (!payload.Quantity.HasValue || payload.Quantity <= 0)
        {
            return AnalyticsValidationResult.Failure($"Quantity must be positive for {eventName}");
        }
        return AnalyticsValidationResult.Success(null!);
    }

    private AnalyticsPayload MapPayload(AnalyticsPayloadDto? dto)
    {
        if (dto == null) return new AnalyticsPayload();

        return new AnalyticsPayload
        {
            ProductId = dto.ProductId,
            VariantId = dto.VariantId,
            OrderId = dto.OrderId,
            CategoryId = dto.CategoryId,
            // Client-submitted value is never authoritative. Server-originated
            // purchase events are emitted elsewhere with server-derived totals.
            Value = null,
            Quantity = dto.Quantity,
            SearchQuery = Sanitize(dto.SearchQuery, MaxSearchQueryLength),
            ProductName = Sanitize(dto.ProductName, MaxProductNameLength),
            ProductSku = Sanitize(dto.ProductSku, MaxProductSkuLength),
            Filters = SanitizeFilters(dto.Filters),
            Extra = SanitizeExtra(dto.Extra)
        };
    }

    private static string? Sanitize(string? input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        
        var trimmed = input.Trim();
        
        if (trimmed.Length > maxLength)
        {
            return trimmed.Substring(0, maxLength);
        }
        
        return trimmed;
    }

    private static string? SanitizeAnalyticsUrl(string? input, int maxLength)
    {
        var sanitized = Sanitize(input, maxLength);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return sanitized;
        }

        if (Uri.TryCreate(sanitized, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.GetLeftPart(UriPartial.Path);
        }

        var queryIndex = sanitized.IndexOf('?');
        var hashIndex = sanitized.IndexOf('#');
        var cutIndex = queryIndex >= 0 && hashIndex >= 0
            ? Math.Min(queryIndex, hashIndex)
            : Math.Max(queryIndex, hashIndex);

        return cutIndex >= 0 ? sanitized[..cutIndex] : sanitized;
    }

    private static Dictionary<string, string> ExtractUtmParameters(string pageUrl)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var queryText = Uri.TryCreate(pageUrl, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri.Query
                : pageUrl.Contains('?')
                    ? pageUrl[(pageUrl.IndexOf('?') + 1)..].Split('#')[0]
                    : string.Empty;
            var query = System.Web.HttpUtility.ParseQueryString(queryText);

            foreach (var key in new[] { "utm_source", "utm_medium", "utm_campaign", "utm_content", "utm_term" })
            {
                var value = Sanitize(query[key], MaxUtmValueLength);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values[key] = value.ToLowerInvariant();
                }
            }
        }
        catch
        {
            return values;
        }

        return values;
    }

    private Dictionary<string, string>? SanitizeFilters(Dictionary<string, string>? filters)
    {
        if (filters == null || filters.Count == 0) return null;

        return filters
            .Take(MaxFiltersCount)
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(
                kv => Sanitize(kv.Key, MaxFilterKeyLength)!,
                kv => Sanitize(kv.Value, MaxFilterValueLength) ?? string.Empty);
    }

    private Dictionary<string, object>? SanitizeExtra(Dictionary<string, object>? extra)
    {
        if (extra == null || extra.Count == 0) return null;

        return extra
            .Take(MaxExtraCount)
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(
                kv => Sanitize(kv.Key, MaxFilterKeyLength)!,
                kv => kv.Value);
    }

    private AnalyticsValidationResult ValidatePayloadShape(AnalyticsPayloadDto payload)
    {
        if ((payload.SearchQuery?.Length ?? 0) > MaxSearchQueryLength)
        {
            return AnalyticsValidationResult.Failure($"SearchQuery exceeds max length ({MaxSearchQueryLength})");
        }

        if ((payload.ProductName?.Length ?? 0) > MaxProductNameLength)
        {
            return AnalyticsValidationResult.Failure($"ProductName exceeds max length ({MaxProductNameLength})");
        }

        if ((payload.ProductSku?.Length ?? 0) > MaxProductSkuLength)
        {
            return AnalyticsValidationResult.Failure($"ProductSku exceeds max length ({MaxProductSkuLength})");
        }

        if (payload.Filters?.Count > MaxFiltersCount)
        {
            return AnalyticsValidationResult.Failure($"Filters exceeds max count ({MaxFiltersCount})");
        }

        if (payload.Filters != null)
        {
            foreach (var filter in payload.Filters)
            {
                if (string.IsNullOrWhiteSpace(filter.Key))
                {
                    return AnalyticsValidationResult.Failure("Filter key is required");
                }

                if (filter.Key.Length > MaxFilterKeyLength)
                {
                    return AnalyticsValidationResult.Failure($"Filter key exceeds max length ({MaxFilterKeyLength})");
                }

                if ((filter.Value?.Length ?? 0) > MaxFilterValueLength)
                {
                    return AnalyticsValidationResult.Failure($"Filter value exceeds max length ({MaxFilterValueLength})");
                }
            }
        }

        if (payload.Extra?.Count > MaxExtraCount)
        {
            return AnalyticsValidationResult.Failure($"Extra exceeds max count ({MaxExtraCount})");
        }

        if (payload.Extra != null)
        {
            foreach (var entry in payload.Extra)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    return AnalyticsValidationResult.Failure("Extra key is required");
                }

                if (entry.Key.Length > MaxFilterKeyLength)
                {
                    return AnalyticsValidationResult.Failure($"Extra key exceeds max length ({MaxFilterKeyLength})");
                }
            }
        }

        return AnalyticsValidationResult.Success(null!);
    }

    private static string ExtractLanguage(string? frontendLanguage, string? acceptLanguage)
    {
        // Prefer frontend-provided language
        if (!string.IsNullOrWhiteSpace(frontendLanguage) && frontendLanguage.Length <= 10)
        {
            return frontendLanguage.ToLowerInvariant()[..2];
        }

        // Fall back to Accept-Language header
        if (!string.IsNullOrWhiteSpace(acceptLanguage))
        {
            var first = acceptLanguage.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first.Split('-').First().ToLowerInvariant();
            }
        }

        return "en";
    }

    private static string DeriveDeviceType(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "unknown";
        
        var ua = userAgent.ToLowerInvariant();
        
        if (ua.Contains("mobile") || ua.Contains("android") || ua.Contains("iphone"))
        {
            return "mobile";
        }
        if (ua.Contains("tablet") || ua.Contains("ipad"))
        {
            return "tablet";
        }
        if (ua.Contains("windows") || ua.Contains("macintosh") || ua.Contains("linux"))
        {
            return "desktop";
        }
        
        return "unknown";
    }

    private static string AnonymizeIp(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return "0.0.0.0";

        // Anonymize last octet for IPv4
        if (ipAddress.Contains('.'))
        {
            var parts = ipAddress.Split('.');
            if (parts.Length == 4)
            {
                return $"{parts[0]}.{parts[1]}.{parts[2]}.0";
            }
        }

        // Anonymize last segment for IPv6
        if (ipAddress.Contains(':'))
        {
            var lastColon = ipAddress.LastIndexOf(':');
            if (lastColon > 0)
            {
                return ipAddress[..lastColon] + ":0";
            }
        }

        return "0.0.0.0";
    }
}
