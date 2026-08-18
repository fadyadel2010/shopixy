using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Payments.ProviderContracts;

namespace PointOfSale.API.Services.Payments.Paymob;

public sealed class PaymobCommerceHubRefundGateway : IProviderRefundGateway
{
    private const string UnsupportedMessage =
        "CommerceHub Paymob refunds are disabled until the modern Super Admin credential contract is provider-certified.";

    public ProviderCode ProviderCode { get; } = new("Paymob");

    public Task<CommerceHubGatewayRefundResult> RefundAsync(
        CanonicalRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NotSupported(request.Payment?.ProviderTransactionId ?? request.Payment?.ProviderIntentId));
    }

    public Task<CommerceHubGatewayRefundResult> RefreshRefundStatusAsync(
        CanonicalRefundStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NotSupported(request.RefundReference));
    }

    private CommerceHubGatewayRefundResult NotSupported(string? providerReference) =>
        new(
            Accepted: false,
            IsConfirmed: false,
            Provider: ProviderCode.Value,
            Reference: providerReference,
            GatewayStatus: "not_supported",
            FailureReason: UnsupportedMessage);
}
