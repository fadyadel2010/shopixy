namespace PointOfSale.API.Security;

public static class ManualWalletUploadTokenAuthenticationDefaults
{
    public const string SchemeName = "ManualWalletUploadToken";
    public const string HeaderName = "X-Manual-Wallet-Upload-Token";
    public const string CheckoutSessionIdClaimType = "checkout_session_id";
    public const string RecoveryNonceClaimType = "checkout_session_recovery_nonce";
    public const string TenantIdClaimType = "tenant_id";
}
