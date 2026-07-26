using System.Diagnostics;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Instrumentation;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubPaymentOrchestrationService
{
    Task<CommerceHubPaymentInitiateResponseDto> ExecuteAsync(
        CommerceHubPaymentInitiateRequestDto request,
        Guid? authenticatedCustomerId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPaymentWebhookResultDto> ProcessWebhookAsync(
        string? provider,
        string? signature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPaymentStatusDto> GetPaymentStatusAsync(
        string? provider,
        Guid orderId,
        Guid? authenticatedCustomerId,
        string? orderAccessToken,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPaymentConfigurationStatusDto> GetConfigurationStatusAsync(
        string? provider,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPaymentCallbackResultDto> ValidateCallbackAsync(
        string? provider,
        IReadOnlyDictionary<string, string> callbackParams,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubPaymentOrchestrationService : ICommerceHubPaymentOrchestrationService
{
    public const string ActivitySourceName = "PointOfSale.CommerceHub.PaymentOrchestration";

    private const string CanonicalExecutionBoundary = "IPaymentRuntime";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private readonly IPaymentRuntime _paymentRuntime;
    private readonly CommerceHubPaymentOrchestrationTelemetry _telemetry;
    private readonly ILogger<CommerceHubPaymentOrchestrationService> _logger;

    public CommerceHubPaymentOrchestrationService(
        IPaymentRuntime paymentRuntime,
        CommerceHubPaymentOrchestrationTelemetry telemetry,
        ILogger<CommerceHubPaymentOrchestrationService> logger)
    {
        _paymentRuntime = paymentRuntime;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<CommerceHubPaymentInitiateResponseDto> ExecuteAsync(
        CommerceHubPaymentInitiateRequestDto request,
        Guid? authenticatedCustomerId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = request.Provider;
        using var activity = ActivitySource.StartActivity("CommerceHub.Payment.Execute", ActivityKind.Internal);
        activity?.SetTag("commercehub.payment.provider", provider);
        activity?.SetTag("commercehub.payment.order_id", request.OrderId);
        activity?.SetTag("commercehub.payment.correlation_id", correlationId);

        _telemetry.RecordPaymentExecutionStarted(provider);
        _telemetry.RecordProviderSelected(provider);
        _logger.LogInformation(
            "CommerceHub payment orchestration started. Event={EventName}; Provider={Provider}; OrderId={OrderId}; CorrelationId={CorrelationId}; Boundary={Boundary}; CanonicalService={CanonicalService}",
            CommerceHubPaymentOrchestrationTelemetry.PaymentExecutionStarted,
            provider,
            request.OrderId,
            correlationId,
            CanonicalExecutionBoundary,
            _paymentRuntime.GetType().Name);

        try
        {
            var response = await _paymentRuntime.InitiateAsync(
                request,
                authenticatedCustomerId,
                correlationId,
                cancellationToken);

            _telemetry.RecordPaymentExecutionSucceeded(response.Provider, response.Status);
            activity?.SetTag("commercehub.payment.id", response.PaymentId);
            activity?.SetTag("commercehub.payment.marketplace_order_id", response.MarketplaceOrderId);
            activity?.SetTag("commercehub.payment.status", response.Status);
            _logger.LogInformation(
                "CommerceHub payment orchestration succeeded. Event={EventName}; Provider={Provider}; PaymentId={PaymentId}; MarketplaceOrderId={MarketplaceOrderId}; Status={Status}; CorrelationId={CorrelationId}",
                CommerceHubPaymentOrchestrationTelemetry.PaymentExecutionSucceeded,
                response.Provider,
                response.PaymentId,
                response.MarketplaceOrderId,
                response.Status,
                correlationId);

            return response;
        }
        catch (CommerceHubCheckoutException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Code);
            RecordBusinessFailure(provider, correlationId, exception.Code, exception);
            throw;
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Operation cancelled");
            _telemetry.RecordPaymentExecutionFailed(provider, "OPERATION_CANCELLED");
            throw;
        }
        catch (Exception exception)
        {
            _telemetry.RecordPaymentExecutionFailed(provider, "UNEXPECTED_INFRASTRUCTURE_FAILURE");
            activity?.SetStatus(ActivityStatusCode.Error, "Unexpected infrastructure failure");
            _logger.LogError(
                exception,
                "CommerceHub payment orchestration failed unexpectedly. Event={EventName}; Provider={Provider}; OrderId={OrderId}; CorrelationId={CorrelationId}",
                CommerceHubPaymentOrchestrationTelemetry.PaymentExecutionFailed,
                provider,
                request.OrderId,
                correlationId);

            throw new CommerceHubPaymentOrchestrationException(
                "Unexpected infrastructure failure during CommerceHub payment orchestration.",
                exception);
        }
    }

    public async Task<CommerceHubPaymentWebhookResultDto> ProcessWebhookAsync(
        string? provider,
        string? signature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("CommerceHub.Payment.Webhook", ActivityKind.Internal);
        activity?.SetTag("commercehub.payment.provider", provider);
        activity?.SetTag("commercehub.payment.correlation_id", correlationId);

        _telemetry.RecordWebhookReceived(provider ?? string.Empty);
        _logger.LogInformation(
            "CommerceHub payment orchestration received webhook. Event={EventName}; Provider={Provider}; CorrelationId={CorrelationId}; Boundary={Boundary}; CanonicalService={CanonicalService}",
            CommerceHubPaymentOrchestrationTelemetry.WebhookReceived,
            provider,
            correlationId,
            CanonicalExecutionBoundary,
            _paymentRuntime.GetType().Name);

        try
        {
            return await _paymentRuntime.ProcessWebhookAsync(
                provider,
                signature,
                rawPayload,
                correlationId,
                cancellationToken);
        }
        catch (CommerceHubCheckoutException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unexpected infrastructure failure");
            _logger.LogError(
                exception,
                "CommerceHub payment webhook orchestration failed unexpectedly. Provider={Provider}; CorrelationId={CorrelationId}",
                provider,
                correlationId);

            throw new CommerceHubPaymentOrchestrationException(
                "Unexpected infrastructure failure during CommerceHub payment webhook orchestration.",
                exception);
        }
    }

    public Task<CommerceHubPaymentStatusDto> GetPaymentStatusAsync(
        string? provider,
        Guid orderId,
        Guid? authenticatedCustomerId,
        string? orderAccessToken,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        return _paymentRuntime.GetPaymentStatusAsync(
            provider,
            orderId,
            authenticatedCustomerId,
            orderAccessToken,
            correlationId,
            cancellationToken);
    }

    public Task<CommerceHubPaymentConfigurationStatusDto> GetConfigurationStatusAsync(
        string? provider,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return _paymentRuntime.GetConfigurationStatusAsync(provider, tenantId, cancellationToken);
    }

    public Task<CommerceHubPaymentCallbackResultDto> ValidateCallbackAsync(
        string? provider,
        IReadOnlyDictionary<string, string> callbackParams,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        return _paymentRuntime.ValidateCallbackAsync(
            provider,
            callbackParams,
            correlationId,
            cancellationToken);
    }

    private void RecordBusinessFailure(
        string provider,
        string correlationId,
        string reasonCode,
        Exception exception)
    {
        _telemetry.RecordPaymentExecutionFailed(provider, reasonCode);
        _logger.LogWarning(
            exception,
            "CommerceHub payment orchestration failed. Event={EventName}; Provider={Provider}; ReasonCode={ReasonCode}; CorrelationId={CorrelationId}",
            CommerceHubPaymentOrchestrationTelemetry.PaymentExecutionFailed,
            provider,
            reasonCode,
            correlationId);
    }
}

public sealed class CommerceHubPaymentOrchestrationException : Exception
{
    public CommerceHubPaymentOrchestrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
