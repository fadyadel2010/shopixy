using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.CommerceHub;

internal static class CommerceHubPaymentSessionRecoveryAuthority
{
    internal const string ProviderSessionReinitFencePrefix = "PROVIDER_SESSION_REINIT_FENCE:";
    private const string ProviderSessionOutcomeUnknownPrefix = "PROVIDER_SESSION_OUTCOME_UNKNOWN:";

    public static bool HasProviderSessionPersistenceGap(CommerceHubPayment payment)
    {
        if (payment.Status != CommerceHubPaymentRecordStatus.Pending || HasReusableSession(payment))
        {
            return false;
        }

        if (IsProviderSessionReinitFence(payment.FailureReason) ||
            IsProviderSessionOutcomeUnknown(payment.FailureReason))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(payment.ProviderIntentId) ||
            !string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
        {
            return true;
        }

        return payment.Attempts.Any(attempt =>
                IsProviderSessionReinitFence(attempt.FailureReason) ||
                IsProviderSessionOutcomeUnknown(attempt.FailureReason) ||
                attempt.CompletedAtUtc != null &&
                attempt.Status == CommerceHubPaymentRecordStatus.Pending &&
                string.IsNullOrWhiteSpace(attempt.FailureReason) &&
                (!string.IsNullOrWhiteSpace(attempt.ProviderIntentId) ||
                 !string.IsNullOrWhiteSpace(attempt.ProviderTransactionId))) ||
            payment.Logs.Any(log =>
                log.EventType.StartsWith("initiate.", StringComparison.OrdinalIgnoreCase) &&
                (log.EventType.EndsWith(".outcome_unknown", StringComparison.OrdinalIgnoreCase) ||
                 !log.EventType.EndsWith(".failed", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(log.ProviderEventId)));
    }

    public static bool HasReusableSession(CommerceHubPayment payment) =>
        !string.IsNullOrWhiteSpace(payment.CheckoutUrl) ||
        !string.IsNullOrWhiteSpace(payment.PaymentToken);

    public static bool IsProviderSessionReinitFence(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.StartsWith(ProviderSessionReinitFencePrefix, StringComparison.Ordinal);
    }

    public static bool IsProviderSessionOutcomeUnknown(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.StartsWith(ProviderSessionOutcomeUnknownPrefix, StringComparison.Ordinal);
    }
}
