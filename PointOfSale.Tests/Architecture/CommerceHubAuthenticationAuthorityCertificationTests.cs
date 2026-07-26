namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubAuthenticationAuthorityCertificationTests
{
    [Fact]
    public void RegistrationAndVerification_MustNotCreateAuthenticatedAccess()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");

        Assert.Contains("[HttpPost(\"register\")]", controller, StringComparison.Ordinal);
        Assert.Contains("return Ok(new { message = result.Message });", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCustomerAccessCookie(result.Response!)", RegistrationAction(controller), StringComparison.Ordinal);

        Assert.Contains("[HttpPost(\"verify-email\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCustomerAccessCookie", VerifyEmailAction(controller), StringComparison.Ordinal);
        Assert.DoesNotContain("BuildAuthResponse(customer)", VerifyEmailServiceMethod(service), StringComparison.Ordinal);

        Assert.Contains("Message = \"Account created. Check your email to verify the address before signing in.\"", service, StringComparison.Ordinal);
        Assert.Contains("if (!customer.IsEmailVerified)", service, StringComparison.Ordinal);
        Assert.Contains("return Fail(\"EMAIL_NOT_VERIFIED\"", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerProtectedAuthEndpoints_MustUseCustomerJwtScheme()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");

        Assert.Contains("[Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]", MeAction(controller), StringComparison.Ordinal);
        Assert.Contains("[Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]", RevokeAllAction(controller), StringComparison.Ordinal);
        Assert.Contains("[Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]", UpdateProfileAction(controller), StringComparison.Ordinal);
        Assert.Contains("[Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]", ChangePasswordAction(controller), StringComparison.Ordinal);
        Assert.Contains("[Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]", ChangeEmailAction(controller), StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticationIncidents_MustHaveIdentityLifecycleEvidence()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCustomerIdentityEvent.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");

        Assert.Contains("Registered", entity, StringComparison.Ordinal);
        Assert.Contains("LoggedIn", entity, StringComparison.Ordinal);
        Assert.Contains("LoggedOut", entity, StringComparison.Ordinal);
        Assert.Contains("PasswordResetRequested", entity, StringComparison.Ordinal);
        Assert.Contains("PasswordResetCompleted", entity, StringComparison.Ordinal);
        Assert.Contains("PasswordChanged", entity, StringComparison.Ordinal);
        Assert.Contains("EmailVerificationRequested", entity, StringComparison.Ordinal);
        Assert.Contains("EmailVerified", entity, StringComparison.Ordinal);
        Assert.Contains("EmailChanged", entity, StringComparison.Ordinal);
        Assert.Contains("AllSessionsRevoked", entity, StringComparison.Ordinal);

        Assert.Contains("RecordIdentityEventAsync", service, StringComparison.Ordinal);
        Assert.Contains("RecordIdentityEventAsync", controller, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<CommerceHubCustomerIdentityEvent>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCustomerIdentityEvents", migration, StringComparison.Ordinal);
        Assert.Contains("IX_CHCustomerIdentityEvents_Customer_Occurred", migration, StringComparison.Ordinal);
        Assert.Contains("IX_CHCustomerIdentityEvents_EventType_Occurred", migration, StringComparison.Ordinal);
    }

    private static string RegistrationAction(string source) => Slice(source, "[HttpPost(\"register\")]", "[HttpPost(\"login\")]");

    private static string VerifyEmailAction(string source) => Slice(source, "[HttpPost(\"verify-email\")]", "[HttpPost(\"resend-verification\")]");

    private static string VerifyEmailServiceMethod(string source) => Slice(source, "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync", "public async Task<CommerceHubCustomerAuthResult> ResendVerificationEmailAsync");

    private static string MeAction(string source) => Slice(source, "[HttpGet(\"me\")]", "[HttpPost(\"logout\")]");

    private static string RevokeAllAction(string source) => Slice(source, "[HttpPost(\"revoke-all-sessions\")]", "[HttpPost(\"forgot-password\")]");

    private static string UpdateProfileAction(string source) => Slice(source, "[HttpPut(\"profile\")]", "[HttpPost(\"change-password\")]");

    private static string ChangePasswordAction(string source) => Slice(source, "[HttpPost(\"change-password\")]", "[HttpPost(\"change-email\")]");

    private static string ChangeEmailAction(string source) => Slice(source, "[HttpPost(\"change-email\")]", "private Guid? ResolveCustomerId()");

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
