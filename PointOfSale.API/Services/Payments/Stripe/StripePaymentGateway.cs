using PointOfSale.API.Services.Payments.ProviderContracts;

namespace PointOfSale.API.Services.Payments.Stripe;

public sealed class StripePaymentGateway : IProviderPaymentGateway
{
    private readonly IStripePaymentService _stripePaymentService;

    public StripePaymentGateway(IStripePaymentService stripePaymentService)
    {
        _stripePaymentService = stripePaymentService;
    }

    public ProviderCode ProviderCode { get; } = new("STRIPE");

    public async Task<CanonicalPaymentSession> CreateSessionAsync(
        CanonicalPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _stripePaymentService.CreatePaymentIntentAsync(
            request.TenantId,
            request.OrderId,
            request.CheckoutToken ?? string.Empty,
            request.CustomerId,
            request.PromotionCalculationFailed,
            request.CorrelationId,
            cancellationToken,
            request.RequestId);

        return new CanonicalPaymentSession
        {
            PaymentIntentId = response.PaymentIntentId,
            PaymentReference = response.StripePaymentIntentId,
            CompatibilityFields = new Dictionary<string, string?>
            {
                ["StripeClientSecret"] = response.ClientSecret,
                ["StripePublishableKey"] = response.PublishableKey
            }
        };
    }
}
