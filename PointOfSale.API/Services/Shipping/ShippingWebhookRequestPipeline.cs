using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Constants;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Services.Shipping;

public sealed record ShippingWebhookTenantResolution(
    Guid TenantId,
    Guid ShipmentId,
    string? TrackingNumber,
    string ResolutionSource);

public sealed record ShippingWebhookTenantResolutionFailure(
    string Reason,
    string ResolutionSource,
    int StatusCode,
    string ErrorCode,
    string ErrorMessage);

public sealed class ShippingWebhookTenantResolutionResult
{
    private ShippingWebhookTenantResolutionResult(
        ShippingWebhookTenantResolution? resolution,
        ShippingWebhookTenantResolutionFailure? failure)
    {
        Resolution = resolution;
        Failure = failure;
    }

    public bool Success => Resolution != null;
    public ShippingWebhookTenantResolution? Resolution { get; }
    public ShippingWebhookTenantResolutionFailure? Failure { get; }

    public static ShippingWebhookTenantResolutionResult Resolved(ShippingWebhookTenantResolution resolution) =>
        new(resolution, null);

    public static ShippingWebhookTenantResolutionResult Failed(
        string reason,
        string resolutionSource,
        int statusCode,
        string errorCode,
        string errorMessage) =>
        new(null, new ShippingWebhookTenantResolutionFailure(
            reason,
            resolutionSource,
            statusCode,
            errorCode,
            errorMessage));
}

public sealed record ShippingWebhookRequestContext(
    string CarrierCode,
    string RawPayload,
    string? Signature,
    ShippingWebhookTenantResolution Resolution,
    string? SourceIp,
    string? UserAgent,
    string TraceId)
{
    public Guid TenantId => Resolution.TenantId;
}

public sealed class ShippingWebhookRequestPreparationException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public string CarrierCode { get; }

    public ShippingWebhookRequestPreparationException(
        string carrierCode,
        int statusCode,
        string errorCode,
        string message)
        : base(message)
    {
        CarrierCode = carrierCode;
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public ApiErrorResponse ToApiError(string traceId) =>
        new(ErrorCode, Message, traceId);
}

public interface IShippingWebhookTenantResolver
{
    /// <summary>
    /// Resolves the tenant and shipment context from a shipping webhook payload.
    /// </summary>
    /// <param name="carrierCode">
    /// The carrier code identifying the shipping provider. 
    /// This method defensively normalizes the carrier code internally, 
    /// but callers should prefer passing pre-normalized values using CarrierCodes.Normalize().
    /// </param>
    /// <param name="payload">The raw webhook payload from the carrier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A resolution result containing tenant context or failure details.</returns>
    Task<ShippingWebhookTenantResolutionResult> ResolveAsync(
        string carrierCode,
        string payload,
        CancellationToken cancellationToken = default);
}

public interface IShippingWebhookRequestPipeline
{
    Task<ShippingWebhookRequestContext> PrepareAsync(
        HttpContext httpContext,
        string carrierCode,
        string payload,
        string? signature,
        CancellationToken cancellationToken = default);
}

public sealed class ShippingWebhookRequestPipeline : IShippingWebhookRequestPipeline
{
    private readonly IShippingWebhookTenantResolver _tenantResolver;
    private readonly TenantContext _tenantContext;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<ShippingWebhookRequestPipeline> _logger;

    public ShippingWebhookRequestPipeline(
        IShippingWebhookTenantResolver tenantResolver,
        TenantContext tenantContext,
        CommerceReliabilityTelemetry telemetry,
        ILogger<ShippingWebhookRequestPipeline> logger)
    {
        _tenantResolver = tenantResolver;
        _tenantContext = tenantContext;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<ShippingWebhookRequestContext> PrepareAsync(
        HttpContext httpContext,
        string carrierCode,
        string payload,
        string? signature,
        CancellationToken cancellationToken = default)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        var traceId = httpContext.TraceIdentifier;
        var result = await _tenantResolver.ResolveAsync(
            normalizedCarrierCode,
            payload,
            cancellationToken);

        if (!result.Success)
        {
            var failure = result.Failure!;
            _telemetry.RecordShippingWebhookTenantResolutionFailed(
                normalizedCarrierCode,
                failure.Reason,
                failure.ResolutionSource);

            _logger.LogWarning(
                "Shipping webhook tenant resolution failed | CarrierCode: {CarrierCode} | Reason: {Reason} | ResolutionSource: {ResolutionSource} | TraceId: {TraceId}",
                normalizedCarrierCode,
                failure.Reason,
                failure.ResolutionSource,
                traceId);

            throw new ShippingWebhookRequestPreparationException(
                normalizedCarrierCode,
                failure.StatusCode,
                failure.ErrorCode,
                failure.ErrorMessage);
        }

        var resolution = result.Resolution!;
        _tenantContext.SetTenantId(resolution.TenantId);
        _telemetry.RecordShippingWebhookTenantResolved(
            normalizedCarrierCode,
            resolution.ResolutionSource);

        _logger.LogInformation(
            "Shipping webhook tenant resolved | CarrierCode: {CarrierCode} | TenantId: {TenantId} | ShipmentId: {ShipmentId} | TrackingNumber: {TrackingNumber} | ResolutionSource: {ResolutionSource} | TraceId: {TraceId}",
            normalizedCarrierCode,
            resolution.TenantId,
            resolution.ShipmentId,
            resolution.TrackingNumber,
            resolution.ResolutionSource,
            traceId);

        return new ShippingWebhookRequestContext(
            normalizedCarrierCode,
            payload,
            signature,
            resolution,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.FirstOrDefault(),
            traceId);
    }
}

public sealed class ShippingWebhookTenantResolver : IShippingWebhookTenantResolver
{
    private static readonly string[] TenantIdPropertyNames =
    [
        "tenantId",
        "tenant_id",
        "merchantTenantId",
        "merchant_tenant_id"
    ];

    private static readonly string[] ShipmentIdPropertyNames =
    [
        "shipmentId",
        "shipment_id",
        "internalShipmentId",
        "internal_shipment_id"
    ];

    private static readonly string[] TrackingPropertyNames =
    [
        "trackingNumber",
        "tracking_number",
        "awb",
        "awbNumber",
        "awb_number",
        "waybill",
        "waybillNumber",
        "shipmentNumber",
        "shipment_number"
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly IShippingCarrierRegistry _carrierRegistry;
    private readonly ILogger<ShippingWebhookTenantResolver> _logger;

    public ShippingWebhookTenantResolver(
        ApplicationDbContext dbContext,
        IShippingCarrierRegistry carrierRegistry,
        ILogger<ShippingWebhookTenantResolver> logger)
    {
        _dbContext = dbContext;
        _carrierRegistry = carrierRegistry;
        _logger = logger;
    }

    public async Task<ShippingWebhookTenantResolutionResult> ResolveAsync(
        string carrierCode,
        string payload,
        CancellationToken cancellationToken = default)
    {
        // Defensively normalize carrierCode to ensure resilience regardless of caller behavior
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        
        if (CarrierCodes.IsManual(normalizedCarrierCode))
        {
            return Fail(
                "manual_carrier_not_allowed",
                "carrier",
                StatusCodes.Status404NotFound,
                "WEBHOOK_CARRIER_NOT_SUPPORTED",
                "Carrier webhook endpoint not found.");
        }

        var adapter = _carrierRegistry.GetAdapter(normalizedCarrierCode);
        if (adapter == null)
        {
            return Fail(
                "carrier_not_supported",
                "carrier",
                StatusCodes.Status404NotFound,
                "WEBHOOK_CARRIER_NOT_SUPPORTED",
                "Carrier webhook endpoint not found.");
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Fail(
                "empty_payload",
                "payload",
                StatusCodes.Status400BadRequest,
                "WEBHOOK_EMPTY_PAYLOAD",
                "Webhook payload is required.");
        }

        var parseResult = adapter.ParseWebhook(payload, null, null);
        var trackingNumber = parseResult?.TrackingNumber?.Trim();
        var claimedTenantId = TryExtractGuid(payload, TenantIdPropertyNames);
        var claimedShipmentId = TryExtractGuid(payload, ShipmentIdPropertyNames);

        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            trackingNumber = TryExtractString(payload, TrackingPropertyNames)?.Trim();
        }

        if (claimedShipmentId.HasValue)
        {
            return await ResolveByShipmentIdAsync(
                normalizedCarrierCode,
                claimedShipmentId.Value,
                claimedTenantId,
                trackingNumber,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            return Fail(
                "tracking_number_missing",
                "payload",
                StatusCodes.Status400BadRequest,
                "WEBHOOK_INVALID_PAYLOAD",
                "Webhook payload could not be validated.");
        }

        return await ResolveByTrackingNumberAsync(
            normalizedCarrierCode,
            trackingNumber,
            claimedTenantId,
            cancellationToken);
    }

    private async Task<ShippingWebhookTenantResolutionResult> ResolveByShipmentIdAsync(
        string carrierCode,
        Guid shipmentId,
        Guid? claimedTenantId,
        string? trackingNumber,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.Id == shipmentId && s.CarrierCode == carrierCode && !s.IsDeleted);

        if (claimedTenantId.HasValue)
        {
            query = query.Where(s => s.TenantId == claimedTenantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(trackingNumber))
        {
            query = query.Where(s => s.TrackingNumber == trackingNumber);
        }

        var shipment = await query
            .Select(s => new
            {
                s.Id,
                s.TenantId,
                s.TrackingNumber
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (shipment == null)
        {
            _logger.LogWarning(
                "Shipping webhook shipment-id resolution failed | CarrierCode: {CarrierCode} | ShipmentId: {ShipmentId} | ClaimedTenantId: {ClaimedTenantId}",
                carrierCode,
                shipmentId,
                claimedTenantId);

            return Fail(
                "shipment_not_found",
                "shipment_id",
                StatusCodes.Status404NotFound,
                "WEBHOOK_TARGET_NOT_FOUND",
                "Webhook target not found.");
        }

        return ShippingWebhookTenantResolutionResult.Resolved(
            new ShippingWebhookTenantResolution(
                shipment.TenantId,
                shipment.Id,
                shipment.TrackingNumber,
                claimedTenantId.HasValue ? "shipment_id_with_claimed_tenant" : "shipment_id"));
    }

    private async Task<ShippingWebhookTenantResolutionResult> ResolveByTrackingNumberAsync(
        string carrierCode,
        string trackingNumber,
        Guid? claimedTenantId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.CarrierCode == carrierCode && s.TrackingNumber == trackingNumber && !s.IsDeleted);

        if (claimedTenantId.HasValue)
        {
            query = query.Where(s => s.TenantId == claimedTenantId.Value);
        }

        var matches = await query
            .Select(s => new
            {
                s.Id,
                s.TenantId,
                s.TrackingNumber
            })
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            _logger.LogWarning(
                "Shipping webhook tracking resolution failed | CarrierCode: {CarrierCode} | TrackingNumber: {TrackingNumber} | ClaimedTenantId: {ClaimedTenantId}",
                carrierCode,
                trackingNumber,
                claimedTenantId);

            return Fail(
                "shipment_not_found",
                claimedTenantId.HasValue ? "tracking_number_with_claimed_tenant" : "tracking_number",
                StatusCodes.Status404NotFound,
                "WEBHOOK_TARGET_NOT_FOUND",
                "Webhook target not found.");
        }

        if (matches.Count > 1)
        {
            _logger.LogError(
                "Shipping webhook tracking resolution ambiguous | CarrierCode: {CarrierCode} | TrackingNumber: {TrackingNumber} | ClaimedTenantId: {ClaimedTenantId}",
                carrierCode,
                trackingNumber,
                claimedTenantId);

            return Fail(
                "tenant_resolution_ambiguous",
                claimedTenantId.HasValue ? "tracking_number_with_claimed_tenant" : "tracking_number",
                StatusCodes.Status409Conflict,
                "WEBHOOK_TENANT_AMBIGUOUS",
                "Webhook target could not be resolved safely.");
        }

        var match = matches[0];
        return ShippingWebhookTenantResolutionResult.Resolved(
            new ShippingWebhookTenantResolution(
                match.TenantId,
                match.Id,
                match.TrackingNumber,
                claimedTenantId.HasValue ? "tracking_number_with_claimed_tenant" : "tracking_number"));
    }

    private static ShippingWebhookTenantResolutionResult Fail(
        string reason,
        string resolutionSource,
        int statusCode,
        string errorCode,
        string errorMessage) =>
        ShippingWebhookTenantResolutionResult.Failed(
            reason,
            resolutionSource,
            statusCode,
            errorCode,
            errorMessage);

    private static Guid? TryExtractGuid(string payload, IReadOnlyList<string> propertyNames)
    {
        var value = TryExtractString(payload, propertyNames);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? TryExtractString(string payload, IReadOnlyList<string> propertyNames)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return TryFindString(document.RootElement, propertyNames);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private const int MaxJsonTraversalDepth = 64;

    private static string? TryFindString(JsonElement element, IReadOnlyList<string> propertyNames, int depth = 0)
    {
        if (depth >= MaxJsonTraversalDepth)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }

                    if (property.Value.ValueKind == JsonValueKind.Number ||
                        property.Value.ValueKind == JsonValueKind.True ||
                        property.Value.ValueKind == JsonValueKind.False)
                    {
                        return property.Value.ToString();
                    }
                }

                var nested = TryFindString(property.Value, propertyNames, depth + 1);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = TryFindString(item, propertyNames, depth + 1);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
