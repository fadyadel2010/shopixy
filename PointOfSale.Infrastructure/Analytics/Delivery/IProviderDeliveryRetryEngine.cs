namespace PointOfSale.Infrastructure.Analytics.Delivery;

public interface IProviderDeliveryRetryEngine
{
    ProviderDeliveryRetryDecision Classify(
        ProviderDeliverySendResult result,
        int nextAttemptNumber,
        ProviderDeliveryOptions options,
        DateTime nowUtc);
}

public sealed record ProviderDeliveryRetryDecision(
    bool ShouldRetry,
    bool IsTerminal,
    bool ReplayEligible,
    string FailureClass,
    DateTime? NextRetryAtUtc,
    string Diagnostic);
