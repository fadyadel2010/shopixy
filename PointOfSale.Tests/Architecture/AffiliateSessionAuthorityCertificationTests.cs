namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateSessionAuthorityCertificationTests
{
    [Fact]
    public void AffiliateProtectedEndpoints_MustUseAffiliateOnlyPolicy()
    {
        var portal = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePortalController.cs");
        var analytics = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateAnalyticsController.cs");
        var links = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs");

        Assert.Contains("[Authorize(Policy = \"AffiliateOnly\")]", portal, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Policy = \"AffiliateOnly\")]", analytics, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Policy = \"AffiliateOnly\")]", links, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateAccessTokens_MustBeSubordinateToCurrentRefreshBindingAndCutoff()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateAuthController.cs");

        Assert.Contains("var sessionClaim = context.Principal?.FindFirst(\"affiliate_session\")?.Value", program, StringComparison.Ordinal);
        Assert.Contains("MatchesAffiliateSessionBinding(sessionClaim, affiliateAuthority.RefreshToken)", program, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", program, StringComparison.Ordinal);
        Assert.Contains("issuedAtUtc <= affiliateAuthority.TokensValidAfterUtc.Value", program, StringComparison.Ordinal);
        Assert.Contains("context.Fail(\"Affiliate token predates session cutoff\")", program, StringComparison.Ordinal);

        var logout = Slice(controller, "[HttpPost(\"logout\")]", "[HttpPost(\"verify-email\")]");
        Assert.Contains("HashToken(refreshToken)", logout, StringComparison.Ordinal);
        Assert.Contains("affiliateUser.ClearRefreshToken()", logout, StringComparison.Ordinal);
        Assert.Contains("ClearSessionCookies(HttpContext, AuthCookieConfiguration.Affiliate)", logout, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateJwtScheme_MustNotReadOtherIdentityCookies()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        var scheme = Slice(program, ".AddJwtBearer(\"AffiliateJwt\"", ".AddScheme<AuthenticationSchemeOptions, ManualWalletUploadTokenAuthenticationHandler>");
        Assert.Contains("AffiliateAccessTokenCookieName", scheme, StringComparison.Ordinal);
        Assert.DoesNotContain("PosAccessTokenCookieName", scheme, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperAdminAccessTokenCookieName", scheme, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubCustomerAccessTokenCookieName", scheme, StringComparison.Ordinal);
        Assert.Contains("ValidIssuer = affiliateJwtSettings.Issuer", scheme, StringComparison.Ordinal);
        Assert.Contains("ValidAudience = affiliateJwtSettings.Audience", scheme, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePortalActorReads_MustRemountOnBackendAdmittedActorAndBypassBrowserCache()
    {
        var portal = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.jsx");
        var authApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/affiliateAuthApi.js");
        var affiliateApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/affiliateApi.js");

        Assert.Contains("const currentAffiliateActorKey = profile.affiliateAccountId", portal, StringComparison.Ordinal);
        Assert.Contains("key={`${currentAffiliateActorKey}:${activeTab}`}", portal, StringComparison.Ordinal);

        Assert.Contains("const affiliateAuthorityReadConfig", authApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.get('/affiliate/profile', affiliateAuthorityReadConfig)", authApi, StringComparison.Ordinal);

        Assert.Contains("const affiliateAuthorityReadConfig", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.get('/affiliate/profile', affiliateAuthorityReadConfig)", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.get('/affiliate/dashboard', affiliateAuthorityReadConfig)", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.get('/affiliate/analytics/summary', { ...affiliateAuthorityReadConfig, params: { range } })", affiliateApi, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
