using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Payments.ProviderContracts;

namespace PointOfSale.API.Services.Payments.Cod;

public sealed class CodCommerceHubRefundGateway : IProviderRefundGateway
{
    public ProviderCode ProviderCode { get; } = new("COD");

    public Task<CommerceHubGatewayRefundResult> RefundAsync(
        CanonicalRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CommerceHubGatewayRefundResult(
            true,
            true,
            ProviderCode.Value,
            $"COD-{request.IdempotencyKey}",
            "confirmed",
            null));
    }

    public Task<CommerceHubGatewayRefundResult> RefreshRefundStatusAsync(
        CanonicalRefundStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CommerceHubGatewayRefundResult(
            true,
            true,
            ProviderCode.Value,
            request.RefundReference,
            "confirmed",
            null));
    }
}
