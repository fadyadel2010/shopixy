using PointOfSale.API.DTOs.CommerceHub;

namespace PointOfSale.API.Services.CommerceHub;

public interface IPaymentRuntime
{
    Task<CommerceHubPaymentInitiateResponseDto> InitiateAsync(
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

public sealed class CommerceHubPaymentRuntime : IPaymentRuntime
{
    private readonly ICommerceHubPaymentService _paymentService;

    public CommerceHubPaymentRuntime(ICommerceHubPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public Task<CommerceHubPaymentInitiateResponseDto> InitiateAsync(
        CommerceHubPaymentInitiateRequestDto request,
        Guid? authenticatedCustomerId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        _paymentService.InitiateAsync(request, authenticatedCustomerId, correlationId, cancellationToken);

    public Task<CommerceHubPaymentWebhookResultDto> ProcessWebhookAsync(
        string? provider,
        string? signature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        _paymentService.ProcessWebhookAsync(provider, signature, rawPayload, correlationId, cancellationToken);

    public Task<CommerceHubPaymentStatusDto> GetPaymentStatusAsync(
        string? provider,
        Guid orderId,
        Guid? authenticatedCustomerId,
        string? orderAccessToken,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        _paymentService.GetPaymentStatusAsync(
            provider,
            orderId,
            authenticatedCustomerId,
            orderAccessToken,
            correlationId,
            cancellationToken);

    public Task<CommerceHubPaymentConfigurationStatusDto> GetConfigurationStatusAsync(
        string? provider,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        _paymentService.GetConfigurationStatusAsync(provider, tenantId, cancellationToken);

    public Task<CommerceHubPaymentCallbackResultDto> ValidateCallbackAsync(
        string? provider,
        IReadOnlyDictionary<string, string> callbackParams,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        _paymentService.ValidateCallbackAsync(provider, callbackParams, correlationId, cancellationToken);
}
