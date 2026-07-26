using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Payments.Adapters;

public interface IPaymentAdapter
{
    PaymentProvider Provider { get; }

    Task<PaymentInitiationResult> InitiateAsync(
        PaymentIntent intent,
        CancellationToken cancellationToken);
}

public class PaymentInitiationResult
{
    public string? ReferenceCode { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? RedirectUrl { get; set; }
    public string? ProviderIntentId { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
