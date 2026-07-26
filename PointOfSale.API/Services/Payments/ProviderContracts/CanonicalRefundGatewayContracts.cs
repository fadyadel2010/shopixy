using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Payments.ProviderContracts;

public sealed record CanonicalRefundRequest(
    CommerceHubPayment? Payment,
    decimal Amount,
    string Currency,
    string Reason,
    string IdempotencyKey);

public sealed record CanonicalRefundStatusRequest(
    CommerceHubPayment? Payment,
    string? RefundReference,
    string? LastKnownGatewayStatus);

public interface IProviderRefundGateway
{
    ProviderCode ProviderCode { get; }

    Task<CommerceHubGatewayRefundResult> RefundAsync(
        CanonicalRefundRequest request,
        CancellationToken cancellationToken = default);

    Task<CommerceHubGatewayRefundResult> RefreshRefundStatusAsync(
        CanonicalRefundStatusRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRefundGateway
{
    Task<CommerceHubGatewayRefundResult> RefundAsync(
        CanonicalRefundRequest request,
        CancellationToken cancellationToken = default);

    Task<CommerceHubGatewayRefundResult> RefreshRefundStatusAsync(
        CanonicalRefundStatusRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class RefundGateway : IRefundGateway
{
    private readonly IReadOnlyDictionary<string, IProviderRefundGateway> _gateways;

    public RefundGateway(IEnumerable<IProviderRefundGateway> gateways)
    {
        _gateways = gateways.ToDictionary(
            gateway => gateway.ProviderCode.Normalized,
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<CommerceHubGatewayRefundResult> RefundAsync(
        CanonicalRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resolve(request.Payment).RefundAsync(request, cancellationToken);
    }

    public Task<CommerceHubGatewayRefundResult> RefreshRefundStatusAsync(
        CanonicalRefundStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resolve(request.Payment).RefreshRefundStatusAsync(request, cancellationToken);
    }

    private IProviderRefundGateway Resolve(CommerceHubPayment? payment)
    {
        var providerCode = payment?.Provider.ToString() ?? "COD";
        if (_gateways.TryGetValue(providerCode, out var gateway))
        {
            return gateway;
        }

        throw new InvalidOperationException($"Refund provider '{providerCode}' is not registered.");
    }
}
