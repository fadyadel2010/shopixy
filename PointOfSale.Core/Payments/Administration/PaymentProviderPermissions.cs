namespace PointOfSale.Core.Payments.Administration;

public static class PaymentProviderPermissions
{
    public const string View = "PaymentProviders.View";
    public const string Edit = "PaymentProviders.Edit";
    public const string Configure = "PaymentProviders.Configure";
    public const string TestConnection = "PaymentProviders.TestConnection";
    public const string RotateSecrets = "PaymentProviders.RotateSecrets";
    public const string ViewSecretMetadata = "PaymentProviders.ViewSecretMetadata";

    public static readonly IReadOnlyList<string> All =
    [
        View,
        Edit,
        Configure,
        TestConnection,
        RotateSecrets,
        ViewSecretMetadata
    ];
}
