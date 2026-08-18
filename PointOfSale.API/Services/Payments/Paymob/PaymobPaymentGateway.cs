using PointOfSale.API.Services.Payments.ProviderContracts;

namespace PointOfSale.API.Services.Payments.Paymob;

public sealed class PaymobPaymentGateway : IProviderPaymentGateway
{
    private readonly IPaymobPaymentService _paymobPaymentService;

    public PaymobPaymentGateway(IPaymobPaymentService paymobPaymentService)
    {
        _paymobPaymentService = paymobPaymentService;
    }

    public ProviderCode ProviderCode { get; } = new("PAYMOB");

    public async Task<CanonicalPaymentSession> CreateSessionAsync(
        CanonicalPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _paymobPaymentService.CreatePaymentIntentAsync(
            request.TenantId,
            request.OrderId,
            request.CustomerId,
            request.PromotionCalculationFailed,
            request.CorrelationId,
            cancellationToken,
            request.RequestId);

        return new CanonicalPaymentSession
        {
            PaymentIntentId = response.PaymentIntentId,
            PaymentReference = response.IntentionId,
            RedirectUrl = response.IframeUrl,
            ExpiresAtUtc = response.ExpiresAtUtc,
            CompatibilityFields = new Dictionary<string, string?>
            {
                ["PaymobIframeUrl"] = response.IframeUrl
            }
        };
    }
}
