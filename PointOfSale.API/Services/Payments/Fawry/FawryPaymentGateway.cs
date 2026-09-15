using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.Services.Payments.ProviderContracts;

namespace PointOfSale.API.Services.Payments.Fawry;

public sealed class FawryPaymentGateway : IProviderPaymentGateway
{
    private readonly IPaymentService _paymentService;

    public FawryPaymentGateway(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public ProviderCode ProviderCode { get; } = new("FAWRY");

    public Task<CanonicalPaymentSession> CreateSessionAsync(
        CanonicalPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        // T-09 defense in depth: Fawry has no certified financial settlement
        // authority. The PaymentService boundary is the primary guard.
        throw new PaymentFawryFinancialAuthorityException();
    }
}
