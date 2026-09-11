namespace PointOfSale.API.Security;

public static class CustomerSecurityConstants
{
    public const string AuthenticationScheme = "CustomerJwt";
    public const string Role = "Customer";
    public const string RoleClaimType = "role";
    public const string SessionIssuedAtTicksClaim = "session_iat_ticks";
}
