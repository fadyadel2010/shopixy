namespace PointOfSale.API.SuperAdmin.Models;

public static class SuperAdminSecurityConstants
{
    public const string Role = "SuperAdmin";
    public const string PolicyName = "RequireSuperAdmin";
    public const string AuthenticationScheme = "SuperAdminJwt";
    public const string AuthTypeClaim = "auth_type";
    public const string AuthTypeValue = "super_admin";
    public const string TokenVersionClaim = "token_version";
    public const string RateLimitPolicyName = "SuperAdminAuthRateLimit";
    public const string GovernanceWriteRateLimitPolicyName = "SuperAdminGovernanceWriteRateLimit";
}
