namespace PointOfSale.Tests.Architecture;

public sealed class CustomerRecoveryCertificationTests
{
    [Fact]
    public void Pass161RecoveryVerificationProfileAndAudit_MustBeImplemented()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/customer-identity-certification-findings.md");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var endpoints = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/api/endpoints.ts");

        // Backend endpoints exist
        Assert.Contains("[HttpPost(\"forgot-password\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"reset-password\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"verify-email\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"resend-verification\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPut(\"profile\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"change-password\")]", controller, StringComparison.Ordinal);

        // Service implements hashed single-use tokens
        Assert.Contains("CommerceHubCustomerPasswordResetToken.HashToken", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCustomerEmailVerificationToken.HashToken", service, StringComparison.Ordinal);
        Assert.Contains("RecordIdentityEventAsync", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCustomerIdentityEvent", service, StringComparison.Ordinal);
        Assert.Contains("_emailSender.SendEmailAsync", service, StringComparison.Ordinal);
        Assert.Contains("ExecuteUpdateAsync", service, StringComparison.Ordinal);
        Assert.Contains("ChangeEmailAsync", service, StringComparison.Ordinal);
        Assert.Contains("EMAIL_NOT_VERIFIED", service, StringComparison.Ordinal);
        Assert.Contains("genericMessage", service, StringComparison.Ordinal);

        // Password reset revokes all sessions
        Assert.Contains("customer.RevokeAllSessions(now)", service, StringComparison.Ordinal);

        // Frontend endpoints exist
        Assert.Contains("forgotPassword:", endpoints, StringComparison.Ordinal);
        Assert.Contains("resetPassword:", endpoints, StringComparison.Ordinal);
        Assert.Contains("verifyEmail:", endpoints, StringComparison.Ordinal);
        Assert.Contains("resendVerification:", endpoints, StringComparison.Ordinal);
        Assert.Contains("updateProfile:", endpoints, StringComparison.Ordinal);
        Assert.Contains("changePassword:", endpoints, StringComparison.Ordinal);
        Assert.Contains("changeEmail:", endpoints, StringComparison.Ordinal);

        // Certification findings document the implementations
        Assert.Contains("PASS 16.1 Customer Identity Certification Findings", findings, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass161CustomerIdentityFrontend_MustExposeRecoveryVerificationAndSettingsJourneys()
    {
        var router = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/router/index.tsx");
        var settings = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerSettingsPage.tsx");
        var verification = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerVerifyEmailPage.tsx");

        Assert.Contains("path: \"forgot-password\"", router, StringComparison.Ordinal);
        Assert.Contains("path: \"reset-password\"", router, StringComparison.Ordinal);
        Assert.Contains("path: \"verify-email\"", router, StringComparison.Ordinal);
        Assert.Contains("path: \"customer/settings\"", router, StringComparison.Ordinal);
        Assert.Contains("revokeAllSessions", settings, StringComparison.Ordinal);
        Assert.Contains("changeEmail", settings, StringComparison.Ordinal);
        Assert.Contains("verifyEmail", verification, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass161IdentityLifecycleEvidence_MustBeAppendOnlySql()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCustomerIdentityEvent.cs");
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubCustomerIdentityEventConfiguration.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");

        // Entity has required event types
        Assert.Contains("customer_registered", entity, StringComparison.Ordinal);
        Assert.Contains("customer_logged_in", entity, StringComparison.Ordinal);
        Assert.Contains("password_reset_requested", entity, StringComparison.Ordinal);
        Assert.Contains("password_reset_completed", entity, StringComparison.Ordinal);
        Assert.Contains("email_verified", entity, StringComparison.Ordinal);
        Assert.Contains("password_changed", entity, StringComparison.Ordinal);
        Assert.Contains("profile_updated", entity, StringComparison.Ordinal);
        Assert.Contains("all_sessions_revoked", entity, StringComparison.Ordinal);

        // EF config uses composite indexes for forensic queries
        Assert.Contains("IX_CHCustomerIdentityEvents_Customer_Occurred", config, StringComparison.Ordinal);
        Assert.Contains("IX_CHCustomerIdentityEvents_EventType_Occurred", config, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<CommerceHubCustomerIdentityEvent>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCustomerIdentityEvents", migration, StringComparison.Ordinal);
        Assert.Contains("IX_CHCustomerIdentityEvents_Customer_Occurred", migration, StringComparison.Ordinal);
        Assert.Contains("IX_CHCustomerIdentityEvents_EventType_Occurred", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass161PasswordResetToken_MustUseHashedSingleUseExpiringTokens()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCustomerPasswordResetToken.cs");
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubCustomerPasswordResetTokenConfiguration.cs");

        Assert.Contains("SHA256", entity, StringComparison.Ordinal);
        Assert.Contains("RAW TOKEN IS NEVER STORED", entity, StringComparison.Ordinal);
        Assert.Contains("IsUsed", entity, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc", entity, StringComparison.Ordinal);
        Assert.Contains("IX_CHCustomerPasswordResetTokens_TokenHash", config, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass161EmailVerificationToken_MustUseHashedSingleUseExpiringTokens()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCustomerEmailVerificationToken.cs");
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubCustomerEmailVerificationTokenConfiguration.cs");

        Assert.Contains("SHA256", entity, StringComparison.Ordinal);
        Assert.Contains("RAW TOKEN IS NEVER STORED", entity, StringComparison.Ordinal);
        Assert.Contains("IsUsed", entity, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc", entity, StringComparison.Ordinal);
        Assert.Contains("IX_CHCustomerEmailVerificationTokens_TokenHash", config, StringComparison.Ordinal);
    }
}
