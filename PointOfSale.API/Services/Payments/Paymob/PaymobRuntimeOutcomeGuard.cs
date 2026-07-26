using PointOfSale.API.Services.Payments;

namespace PointOfSale.API.Services.Payments.Paymob;

internal static class PaymobRuntimeOutcomeGuard
{
    internal const string RejectionCode = "PAYMOB_RUNTIME_OUTCOME_REJECTED";

    internal static bool IsRequiredOutcomeMissing(PaymobWebhookRuntimeAdmissionResult runtimeAdmission)
    {
        ArgumentNullException.ThrowIfNull(runtimeAdmission);

        return runtimeAdmission.Admission.AdmittedToRuntime &&
               !runtimeAdmission.AdmittedToRuntime;
    }

    internal static void EnsureRequiredOutcome(PaymobWebhookRuntimeAdmissionResult runtimeAdmission)
    {
        if (!IsRequiredOutcomeMissing(runtimeAdmission))
        {
            return;
        }

        throw new PaymentException(
            RejectionCode,
            "Paymob webhook did not produce the required canonical Runtime outcome.");
    }
}
