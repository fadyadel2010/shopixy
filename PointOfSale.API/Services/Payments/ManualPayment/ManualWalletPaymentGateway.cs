using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.Services.Payments.ProviderContracts;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public sealed class ManualWalletPaymentGateway : IProviderPaymentGateway
{
    private readonly IPaymentService _paymentService;

    public ManualWalletPaymentGateway(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public ProviderCode ProviderCode { get; } = new("MANUALWALLET");

    public async Task<CanonicalPaymentSession> CreateSessionAsync(
        CanonicalPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _paymentService.CreatePaymentIntentAsync(
            request.TenantId,
            request.OrderId,
            new CreatePaymentIntentRequestDto
            {
                Provider = "ManualWallet",
                WalletType = request.WalletType,
                WalletPhoneNumber = request.WalletPhoneNumber,
                ProofFileKey = request.ProofFileKey
            },
            request.PromotionCalculationFailed,
            request.RequestId,
            request.CorrelationId,
            cancellationToken);

        return CanonicalPaymentSession.FromPaymentIntent(response) with
        {
            IsManualPayment = true
        };
    }
}
