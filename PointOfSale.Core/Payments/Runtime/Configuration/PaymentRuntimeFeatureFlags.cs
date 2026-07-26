namespace PointOfSale.Core.Payments.Runtime.Configuration;

public static class PaymentRuntimeFeatureFlags
{
    public const string NewPaymentRuntime = nameof(NewPaymentRuntime);
    public const string NewPolicyEngine = nameof(NewPolicyEngine);
    public const string DualRunPayments = nameof(DualRunPayments);
    public const string ReplayEngine = nameof(ReplayEngine);
    public const string ProviderAdapterV2 = nameof(ProviderAdapterV2);

    public static IReadOnlyDictionary<string, bool> Defaults { get; } =
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [NewPaymentRuntime] = false,
            [NewPolicyEngine] = false,
            [DualRunPayments] = false,
            [ReplayEngine] = false,
            [ProviderAdapterV2] = false
        };
}
