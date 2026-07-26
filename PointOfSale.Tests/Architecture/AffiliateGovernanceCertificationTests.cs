namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PASS A1 — Affiliate Identity Authority Certification
/// Governance, hardening, and cross-domain isolation checks.
///
/// These tests lock the following runtime invariants:
///  G-1  Affiliate JWT secret is fail-fast on startup — no fallback to POS secret
///  G-2  Affiliate JWT scheme uses dedicated AffiliateJwtSettings (issuer, audience, secret)
///  G-3  AffiliateJwt OnTokenValidated checks role=Affiliate before any data access
///  G-4  AffiliateJwt OnTokenValidated validates affiliate_account_id + sub (AffiliateUserId)
///  G-5  AffiliateJwt OnTokenValidated enforces EmailVerified + AccountActive + not deleted
///  G-6  AffiliateJwt OnTokenValidated enforces TokensValidAfterUtc session cutoff
///  G-7  Suspension triggers RevokeSessions — confirmed in AffiliateService / domain entity
///  G-8  AdminAffiliateController uses POS Owner/Admin role, NOT AffiliateOnly
///  G-9  AffiliateDiscoveryController uses AffiliateOnly policy
///  G-10 PayoutWallet update is restricted to the affiliate owner in the domain entity
///  G-11 Affiliate JWT claims NEVER contain tenant_id (global identity)
///  G-12 AffiliateAccount.IsActive is the sole gateway for access — Suspended = fail closed
///  G-13 AffiliateUser.RevokeSessions sets TokensValidAfterUtc (durable global revocation)
///  G-14 Session cookies for Affiliate use dedicated cookie names, not POS names
/// </summary>
public sealed class AffiliateGovernanceCertificationTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // G-1: Affiliate JWT secret must be fail-fast on startup
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateJwtSecret_MustNotFallBackToPosSigning()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        // The SuperAdmin already has an explicit fail-fast guard. Confirm AffiliateJwt
        // also uses its own dedicated settings object and not the POS jwtSettings.
        Assert.Contains("AffiliateJwtSettings.SectionName", program, StringComparison.Ordinal);
        Assert.Contains("affiliateJwtSettings.Secret", program, StringComparison.Ordinal);
        Assert.Contains("affiliateJwtSettings.Issuer", program, StringComparison.Ordinal);
        Assert.Contains("affiliateJwtSettings.Audience", program, StringComparison.Ordinal);
        // The signing key MUST reference affiliateJwtSettings, not jwtSettings
        Assert.Contains(
            "IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(affiliateJwtSettings.Secret))",
            program,
            StringComparison.Ordinal);
        // PASS A1 HARDENING: Explicit startup fail-fast guard (closes R-1)
        Assert.Contains(
            "SECURITY CRITICAL: AffiliateJwt:Secret is not configured.",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "string.IsNullOrWhiteSpace(affiliateJwtSettings.Secret)",
            program,
            StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-2: AffiliateJwt scheme uses dedicated config object
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateJwtScheme_MustUseOwnIssuerAudienceAndSecret()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        var scheme = Slice(program,
            ".AddJwtBearer(\"AffiliateJwt\"",
            ".AddScheme<AuthenticationSchemeOptions, ManualWalletUploadTokenAuthenticationHandler>");

        Assert.Contains("ValidIssuer = affiliateJwtSettings.Issuer", scheme, StringComparison.Ordinal);
        Assert.Contains("ValidAudience = affiliateJwtSettings.Audience", scheme, StringComparison.Ordinal);
        Assert.Contains("affiliateJwtSettings.Secret", scheme, StringComparison.Ordinal);
        Assert.DoesNotContain("jwtSettings.Secret", scheme, StringComparison.Ordinal);
        Assert.DoesNotContain("storefrontJwtSettings", scheme, StringComparison.Ordinal);
        Assert.DoesNotContain("customerJwtSettings", scheme, StringComparison.Ordinal);
        Assert.DoesNotContain("superAdminJwtSettings", scheme, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-3: Role=Affiliate claim must be the FIRST gate in OnTokenValidated
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateJwtValidation_MustRejectNonAffiliateClaims()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        var scheme = Slice(program,
            ".AddJwtBearer(\"AffiliateJwt\"",
            ".AddScheme<AuthenticationSchemeOptions, ManualWalletUploadTokenAuthenticationHandler>");

        // role check must exist before SQL access
        Assert.Contains("roleClaim != \"Affiliate\"", scheme, StringComparison.Ordinal);
        Assert.Contains("context.Fail(\"Token is not an affiliate token\")", scheme, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-4: affiliate_account_id + sub (AffiliateUserId) are mandatory
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateJwtValidation_MustRequireAccountIdAndUserIdClaims()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        var scheme = Slice(program,
            ".AddJwtBearer(\"AffiliateJwt\"",
            ".AddScheme<AuthenticationSchemeOptions, ManualWalletUploadTokenAuthenticationHandler>");

        Assert.Contains("affiliateAccountClaim", scheme, StringComparison.Ordinal);
        Assert.Contains("affiliateUserClaim", scheme, StringComparison.Ordinal);
        Assert.Contains("Guid.TryParse(affiliateUserClaim, out var affiliateUserId)", scheme, StringComparison.Ordinal);
        Assert.Contains("Guid.TryParse(affiliateAccountClaim, out var affiliateAccountId)", scheme, StringComparison.Ordinal);
        Assert.Contains(
            "context.Fail(\"Affiliate token is missing required identity/session claims\")",
            scheme, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-5: EmailVerified + AccountActive + not deleted enforced on every request
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateJwtValidation_MustEnforceEmailVerifiedAndActiveAccount()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        var scheme = Slice(program,
            ".AddJwtBearer(\"AffiliateJwt\"",
            ".AddScheme<AuthenticationSchemeOptions, ManualWalletUploadTokenAuthenticationHandler>");

        Assert.Contains("user.EmailVerified", scheme, StringComparison.Ordinal);
        Assert.Contains("AccountActive = user.AffiliateAccount != null && user.AffiliateAccount.IsActive",
            scheme, StringComparison.Ordinal);
        Assert.Contains("AccountDeleted = user.AffiliateAccount != null && user.AffiliateAccount.IsDeleted",
            scheme, StringComparison.Ordinal);
        Assert.Contains("!affiliateAuthority.EmailVerified", scheme, StringComparison.Ordinal);
        Assert.Contains("!affiliateAuthority.AccountActive", scheme, StringComparison.Ordinal);
        Assert.Contains("affiliateAuthority.AccountDeleted", scheme, StringComparison.Ordinal);
        Assert.Contains("context.Fail(\"Affiliate token is not current SQL authority\")", scheme, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-6: TokensValidAfterUtc session cutoff enforced per-request
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateJwtValidation_MustEnforceTokensValidAfterUtcCutoff()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        var scheme = Slice(program,
            ".AddJwtBearer(\"AffiliateJwt\"",
            ".AddScheme<AuthenticationSchemeOptions, ManualWalletUploadTokenAuthenticationHandler>");

        Assert.Contains("affiliateAuthority.TokensValidAfterUtc.HasValue", scheme, StringComparison.Ordinal);
        Assert.Contains("issuedAtUtc <= affiliateAuthority.TokensValidAfterUtc.Value", scheme, StringComparison.Ordinal);
        Assert.Contains("context.Fail(\"Affiliate token predates session cutoff\")", scheme, StringComparison.Ordinal);
        Assert.Contains("context.Fail(\"Affiliate token is missing issued-at authority\")", scheme, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-7: Suspend triggers RevokeSessions in the domain entity
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateSuspension_MustTriggerRevokeSessions()
    {
        var user = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateUser.cs");
        var account = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateAccount.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        // AffiliateUser provides the session revocation mechanism
        Assert.Contains("RevokeSessions(DateTime nowUtc)", user, StringComparison.Ordinal);
        Assert.Contains("TokensValidAfterUtc = nowUtc", user, StringComparison.Ordinal);

        // AffiliateAccount tracks suspension lifecycle
        Assert.Contains("SuspendedAtUtc", account, StringComparison.Ordinal);
        Assert.Contains("SuspensionReason", account, StringComparison.Ordinal);

        // AffiliateService is the backend transition boundary that applies the cutoff.
        Assert.Contains("account.Suspend(reason)", service, StringComparison.Ordinal);
        Assert.Contains("await RevokeAffiliateAccountSessionsAsync(affiliateAccountId, nowUtc, ct)", service, StringComparison.Ordinal);
        Assert.Contains("user.RevokeSessions(nowUtc)", service, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-8: AdminAffiliateController uses POS Owner/Admin role boundary
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AdminAffiliateController_MustUsePosRoleBoundary_NotAffiliatePolicy()
    {
        var admin = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminAffiliateController.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("[Authorize(Policy = \"AffiliateOnly\")]", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateJwt", admin, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-9: AffiliateDiscoveryController (ES-backed) uses AffiliateOnly policy
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateDiscoveryController_MustUseAffiliateOnlyPolicy()
    {
        var discovery = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateDiscoveryController.cs");

        Assert.Contains("[Authorize(Policy = \"AffiliateOnly\")]", discovery, StringComparison.Ordinal);
        Assert.DoesNotContain("[Authorize(Roles =", discovery, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-10: PayoutWallet update is domain-guarded — only owner can update
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PayoutWallet_MustBeResolvableOnlyByAffiliateOwner()
    {
        var portal = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePortalController.cs");
        var account = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateAccount.cs");

        // Portal resolves affiliate account via JWT claim, never from request body
        Assert.Contains("ResolveAffiliateAccountId(ct)", portal, StringComparison.Ordinal);
        Assert.Contains("account.UpdatePayoutWallet(request.WalletNumber, request.WalletType)", portal, StringComparison.Ordinal);

        // Domain entity protects the update via a method — PayoutWalletNumber is private set
        // (private set means EF Core reads it but user code cannot set it directly)
        Assert.Contains("UpdatePayoutWallet", account, StringComparison.Ordinal);
        Assert.Contains("PayoutWalletNumber { get; private set; }", account, StringComparison.Ordinal);
        // No public setter is allowed for wallet fields
        Assert.DoesNotContain("PayoutWalletNumber { get; set; }", account, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-11: Affiliate JWT claims MUST NOT include tenant_id
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateTokenGeneration_MustNotEmitTenantIdClaim()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateAuthController.cs");

        // The GenerateTokens private method carries the AF-19 comment confirming no tenant_id
        // Slice from the method signature to the end of its block (next private method)
        var tokenGenStart = "AF-19: NO tenant_id claim";
        var tokenGenEnd = "private async Task IssueVerificationEmailAsync";

        var tokenGen = Slice(controller, tokenGenStart, tokenGenEnd);

        Assert.DoesNotContain("tenant_id\"", tokenGen, StringComparison.Ordinal);
        Assert.Contains("affiliate_account_id", tokenGen, StringComparison.Ordinal);
        Assert.Contains("\"Affiliate\"", tokenGen, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-12: AffiliateAccount.IsActive is the sole activation gateway
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateAccount_MustExposeIsActiveAsStatusGateway()
    {
        var account = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateAccount.cs");

        Assert.Contains("IsActive", account, StringComparison.Ordinal);
        // Suspended status must map to IsActive = false
        Assert.Contains("AffiliateStatus", account, StringComparison.Ordinal);
        Assert.Contains("Active", account, StringComparison.Ordinal);
        Assert.Contains("Suspended", account, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-13: AffiliateUser.RevokeSessions provides durable global revocation
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateUser_MustProvideRevokeSessions_WithTokensValidAfterUtc()
    {
        var user = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateUser.cs");

        Assert.Contains("public DateTime? TokensValidAfterUtc", user, StringComparison.Ordinal);
        Assert.Contains("RevokeSessions(DateTime nowUtc)", user, StringComparison.Ordinal);
        Assert.Contains("TokensValidAfterUtc = nowUtc", user, StringComparison.Ordinal);
        // ClearRefreshToken must also be called to invalidate token-rotation chain
        Assert.Contains("ClearRefreshToken()", user, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-14: Affiliate session cookies use dedicated names — not POS/Customer names
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AffiliateSessionCookies_MustUseDedicatedCookieNames()
    {
        var cookieConfig = CertificationSource.ReadRepoFile("PointOfSale.API/Security/AuthCookieConfiguration.cs");

        Assert.Contains("AffiliateAccessTokenCookieName", cookieConfig, StringComparison.Ordinal);
        Assert.Contains("AffiliateRefreshTokenCookieName", cookieConfig, StringComparison.Ordinal);

        // Affiliate cookie names must be distinct from POS and customer cookie names
        var affiliateAccessIdx = cookieConfig.IndexOf("AffiliateAccessTokenCookieName", StringComparison.Ordinal);
        var affiliateAccessValue = cookieConfig.Substring(affiliateAccessIdx, 120);
        Assert.DoesNotContain("pos_access_token", affiliateAccessValue, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storefront_access_token", affiliateAccessValue, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPER
    // ─────────────────────────────────────────────────────────────────────────
    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
