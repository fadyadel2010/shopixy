using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.Admin;

public interface IPaymentProviderSecretService
{
    PaymentProviderSecretLifecycleResponse GetSecretMetadata(string providerCode);

    RotatePaymentProviderSecretResponse RotateSecret(
        string providerCode,
        RotatePaymentProviderSecretRequest request,
        Guid? actorId,
        string? actorEmail);
}
