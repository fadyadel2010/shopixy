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

    public async Task<CanonicalPaymentSession> CreateSessionAsync(
        CanonicalPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _paymentService.CreatePaymentIntentAsync(
            request.TenantId,
            request.OrderId,
            new CreatePaymentIntentRequestDto { Provider = "Fawry" },
            request.PromotionCalculationFailed,
            request.RequestId,
            request.CorrelationId,
            cancellationToken);

        return CanonicalPaymentSession.FromPaymentIntent(response);
    }
}
