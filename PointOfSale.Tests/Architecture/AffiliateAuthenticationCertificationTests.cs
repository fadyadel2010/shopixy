namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateAuthenticationCertificationTests
{
    [Fact]
    public void LoginAndRefresh_MustRequireVerifiedActiveCurrentSqlAuthority()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateAuthController.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        var login = Slice(controller, "[HttpPost(\"login\")]", "[HttpPost(\"refresh\")]");
        Assert.Contains("VerifyHashedPassword", login, StringComparison.Ordinal);
        Assert.Contains("if (!affiliateUser.EmailVerified)", login, StringComparison.Ordinal);
        Assert.Contains("!affiliateUser.AffiliateAccount.IsActive", login, StringComparison.Ordinal);
        Assert.Contains("affiliateUser.SetRefreshToken(", login, StringComparison.Ordinal);

        var refresh = Slice(controller, "[HttpPost(\"refresh\")]", "[HttpPost(\"logout\")]");
        Assert.Contains("var presentedRefreshTokenHash = HashToken(presentedRefreshToken)", refresh, StringComparison.Ordinal);
        Assert.Contains("u.RefreshToken == presentedRefreshTokenHash", refresh, StringComparison.Ordinal);
        Assert.Contains("if (!affiliateUser.EmailVerified)", refresh, StringComparison.Ordinal);
        Assert.Contains("!affiliateUser.AffiliateAccount.IsActive", refresh, StringComparison.Ordinal);
        Assert.Contains("affiliateUser.SetRefreshToken(", refresh, StringComparison.Ordinal);

        Assert.Contains("user.EmailVerified", program, StringComparison.Ordinal);
        Assert.Contains("user.RefreshToken", program, StringComparison.Ordinal);
        Assert.Contains("user.TokensValidAfterUtc", program, StringComparison.Ordinal);
        Assert.Contains("MatchesAffiliateSessionBinding(sessionClaim, affiliateAuthority.RefreshToken)", program, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", program, StringComparison.Ordinal);
        Assert.Contains("context.Fail(\"Affiliate token is not current SQL authority\")", program, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshTokens_MustBeHashedRotatedAndReplayAudited()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateAuthController.cs");
        var user = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateUser.cs");

        Assert.Contains("SHA256.HashData", controller, StringComparison.Ordinal);
        Assert.Contains("var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))", controller, StringComparison.Ordinal);
        Assert.Contains("var refreshTokenHash = HashToken(refreshToken)", controller, StringComparison.Ordinal);
        Assert.Contains("new(\"affiliate_session\", refreshTokenHash)", controller, StringComparison.Ordinal);
        Assert.Contains("REFRESH_TOKEN_REPLAY_OR_INVALID", controller, StringComparison.Ordinal);
        Assert.Contains("REFRESH_TOKEN_REPLAY_DETECTED", controller, StringComparison.Ordinal);
        Assert.Contains("AffiliateRefreshTokenReplayEntries.Add", controller, StringComparison.Ordinal);
        Assert.Contains("TryRevokeReplayedAffiliateRefreshTokenAsync", controller, StringComparison.Ordinal);
        Assert.Contains("IsAffiliateRefreshReplayLedgerUniquenessViolation", controller, StringComparison.Ordinal);
        Assert.Contains("PreviousRefreshToken", controller, StringComparison.Ordinal);
        Assert.Contains("RevokeSessions(nowUtc)", controller, StringComparison.Ordinal);
        Assert.Contains("RecordAffiliateAuthAuditAsync", controller, StringComparison.Ordinal);

        Assert.Contains("SetRefreshToken(string refreshTokenHash", user, StringComparison.Ordinal);
        Assert.Contains("PreviousRefreshToken = RefreshToken", user, StringComparison.Ordinal);
        Assert.Contains("IsPreviousRefreshTokenReplay", user, StringComparison.Ordinal);
        Assert.Contains("ClearRefreshToken()", user, StringComparison.Ordinal);

        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var replayEntry = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateRefreshTokenReplayEntry.cs");
        var replayConfig = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateRefreshTokenReplayEntryConfiguration.cs");
        Assert.Contains("DbSet<AffiliateRefreshTokenReplayEntry>", dbContext, StringComparison.Ordinal);
        Assert.Contains("RecordRotation(", replayEntry, StringComparison.Ordinal);
        Assert.Contains("MarkReplayed", replayEntry, StringComparison.Ordinal);
        Assert.Contains("IX_AffiliateRefreshTokenReplayEntries_TokenHash", replayConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordReset_MustBeHashOnlySingleUseAndRevokeSessions()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateAuthController.cs");
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliatePasswordResetToken.cs");
        var user = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateUser.cs");

        var reset = Slice(controller, "[HttpPost(\"reset-password\")]", "private (string accessToken");
        Assert.Contains("AffiliatePasswordResetToken.HashToken(request.Token)", reset, StringComparison.Ordinal);
        Assert.Contains("token.CanConsume(now)", reset, StringComparison.Ordinal);
        Assert.Contains("token.Consume(now)", reset, StringComparison.Ordinal);
        Assert.Contains("token.AffiliateUser.RevokeSessions(now)", reset, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSessionCookies", reset, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateTokens", reset, StringComparison.Ordinal);

        Assert.Contains("SHA256.HashData", entity, StringComparison.Ordinal);
        Assert.Contains("ConsumedAtUtc", entity, StringComparison.Ordinal);
        Assert.Contains("TokensValidAfterUtc", user, StringComparison.Ordinal);
        Assert.Contains("RevokeSessions(DateTime nowUtc)", user, StringComparison.Ordinal);
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
