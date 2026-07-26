using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;

namespace PointOfSale.API.Services.Payments;

public sealed record PaymentWebhookRequestContext(
    string Provider,
    string TraceId,
    string RawPayload,
    string Signature,
    WebhookTenantResolutionResult Resolution,
    string? SourceIp,
    string? UserAgent)
{
    public Guid TenantId => Resolution.TenantId;
}

public sealed class PaymentWebhookRequestPreparationException : Exception
{
    public int StatusCode { get; }
    public string? ErrorCode { get; }
    public bool UseApiErrorResponse { get; }

    public PaymentWebhookRequestPreparationException(
        int statusCode,
        string message,
        string? errorCode = null,
        bool useApiErrorResponse = false)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        UseApiErrorResponse = useApiErrorResponse;
    }

    public ApiErrorResponse ToApiError(string traceId) =>
        new(ErrorCode ?? "WEBHOOK_REQUEST_INVALID", Message, traceId);
}

public interface IPaymentWebhookRequestPipeline
{
    Task<PaymentWebhookRequestContext> PrepareAsync(
        HttpContext httpContext,
        string provider,
        string traceId,
        Func<HttpRequest, string?> signatureResolver,
        Func<HttpRequest, Guid?> claimedTenantResolver,
        CancellationToken cancellationToken = default);
}

public sealed class PaymentWebhookRequestPipeline : IPaymentWebhookRequestPipeline
{
    private readonly IPaymentWebhookTenantResolver _tenantResolver;
    private readonly ILogger<PaymentWebhookRequestPipeline> _logger;

    public PaymentWebhookRequestPipeline(
        IPaymentWebhookTenantResolver tenantResolver,
        ILogger<PaymentWebhookRequestPipeline> logger)
    {
        _tenantResolver = tenantResolver;
        _logger = logger;
    }

    public async Task<PaymentWebhookRequestContext> PrepareAsync(
        HttpContext httpContext,
        string provider,
        string traceId,
        Func<HttpRequest, string?> signatureResolver,
        Func<HttpRequest, Guid?> claimedTenantResolver,
        CancellationToken cancellationToken = default)
    {
        var signature = signatureResolver(httpContext.Request)?.Trim();
        if (string.IsNullOrWhiteSpace(signature))
        {
            WebhookRequestBodyReader.RecordInvalidSignature(httpContext, provider, "payment", "missing_signature");
            WebhookRequestBodyReader.RecordPayloadRejected(httpContext, provider, "missing_signature", "payment");
            throw new PaymentWebhookRequestPreparationException(
                StatusCodes.Status401Unauthorized,
                "Webhook signature is required.",
                errorCode: "INVALID_SIGNATURE",
                useApiErrorResponse: true);
        }

        string rawPayload;
        try
        {
            rawPayload = await WebhookRequestBodyReader.ReadJsonBodyAsync(
                httpContext,
                provider,
                WebhookRequestLimits.PaymentProviderMaxBytes,
                "payment",
                cancellationToken);
        }
        catch (WebhookRequestRejectedException ex)
        {
            throw new PaymentWebhookRequestPreparationException(
                ex.StatusCode,
                ex.Message,
                ex.ErrorCode,
                useApiErrorResponse: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to read webhook request body | Provider: {Provider} | TraceId: {TraceId}",
                provider,
                traceId);
            throw new PaymentWebhookRequestPreparationException(
                StatusCodes.Status500InternalServerError,
                "Failed to read request body");
        }

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            throw new PaymentWebhookRequestPreparationException(
                StatusCodes.Status400BadRequest,
                "Empty request body");
        }

        var resolution = await _tenantResolver.ResolveAsync(provider, rawPayload, cancellationToken);
        if (resolution == null)
        {
            WebhookRequestBodyReader.RecordPayloadRejected(httpContext, provider, "tenant_resolution_failed", "payment");
            _logger.LogWarning(
                "Webhook tenant resolution failed | Provider: {Provider} | TraceId: {TraceId}",
                provider,
                traceId);

            throw new PaymentWebhookRequestPreparationException(
                StatusCodes.Status500InternalServerError,
                "Payment record was not found for this webhook.");
        }

        var claimedTenantId = claimedTenantResolver(httpContext.Request);
        if (claimedTenantId.HasValue && claimedTenantId.Value != resolution.TenantId)
        {
            _logger.LogWarning(
                "Webhook tenant mismatch | Provider: {Provider} | ClaimedTenantId: {ClaimedTenantId} | ResolvedTenantId: {ResolvedTenantId} | ResolutionKey: {ResolutionKey} | ResolutionValue: {ResolutionValue} | TraceId: {TraceId}",
                provider,
                claimedTenantId,
                resolution.TenantId,
                resolution.ResolutionKey,
                resolution.ResolutionValue,
                traceId);

            throw new PaymentWebhookRequestPreparationException(
                StatusCodes.Status401Unauthorized,
                "Webhook tenant does not match the stored payment record.",
                errorCode: TenantErrorCodes.TenantMismatch,
                useApiErrorResponse: true);
        }

        ApplyResolvedTenantContext(httpContext, resolution.TenantId);

        return new PaymentWebhookRequestContext(
            provider,
            traceId,
            rawPayload,
            signature,
            resolution,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.FirstOrDefault());
    }

    private static void ApplyResolvedTenantContext(HttpContext httpContext, Guid tenantId)
    {
        var tenantContext = httpContext.RequestServices.GetService<PointOfSale.Infrastructure.MultiTenancy.TenantContext>();
        tenantContext?.SetTenantId(tenantId);
    }
}
