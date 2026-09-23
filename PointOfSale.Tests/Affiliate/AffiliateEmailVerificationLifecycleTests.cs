using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Controllers;
using PointOfSale.API.Models;
using PointOfSale.API.Services.Auth;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-PA3.1: Deterministic regression coverage for the Affiliate
/// register -> verification email -> login remediation.
///
/// Root cause under regression: /api/affiliate/auth/* bypasses tenant resolution,
/// so CurrentTenantId is null; the tenant-filtered AffiliateAccount reached through
/// a REQUIRED navigation include silently removes every otherwise-valid token row.
/// The production lookup therefore uses IgnoreQueryFilters plus explicit soft-delete
/// guards, and consumption is arbitrated by a single conditional UPDATE.
/// </summary>
public sealed class AffiliateEmailVerificationLifecycleTests
{
    // ═══════════════════════════════════════════════════════════════════
    // RAW TOKEN GENERATION AND CANONICAL REPRESENTATION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void NewRawToken_AlwaysGeneratesCanonicalUppercaseHex()
    {
        for (var i = 0; i < 64; i++)
        {
            var rawToken = AffiliateEmailVerificationToken.NewRawToken();

            Assert.Equal(AffiliateEmailVerificationToken.RawTokenCharLength, rawToken.Length);
            Assert.True(
                AffiliateEmailVerificationToken.IsCanonicalRawToken(rawToken),
                $"Generated token was not canonical: {rawToken}");
        }
    }

    [Fact]
    public void NewRawToken_IsUrlSafeWithoutTransportAmbiguousCharacters()
    {
        for (var i = 0; i < 32; i++)
        {
            var rawToken = AffiliateEmailVerificationToken.NewRawToken();

            Assert.DoesNotContain('+', rawToken);
            Assert.DoesNotContain('/', rawToken);
            Assert.DoesNotContain('=', rawToken);
            Assert.DoesNotContain(' ', rawToken);
        }
    }

    [Fact]
    public void PreviouslyIssuedBase64Token_RemainsSupportedWithoutNormalization()
    {
        var legacyToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));

        Assert.True(AffiliateEmailVerificationToken.IsSupportedPresentedToken(legacyToken));
        Assert.False(AffiliateEmailVerificationToken.IsCanonicalRawToken(legacyToken));
        Assert.Equal(
            AffiliateEmailVerificationToken.HashToken(legacyToken),
            AffiliateEmailVerificationToken.HashToken(Uri.UnescapeDataString(Uri.EscapeDataString(legacyToken))));
    }

    [Fact]
    public void TokenCreation_RejectsNonUtcExpiry()
    {
        var rawToken = AffiliateEmailVerificationToken.NewRawToken();

        Assert.Throws<ArgumentException>(() => AffiliateEmailVerificationToken.Create(
            Guid.NewGuid(),
            rawToken,
            DateTime.SpecifyKind(DateTime.UtcNow.AddHours(24), DateTimeKind.Local)));
    }

    [Fact]
    public void Create_PersistsOnlySha256HashAndNeverTheRawToken()
    {
        var rawToken = AffiliateEmailVerificationToken.NewRawToken();

        var token = AffiliateEmailVerificationToken.Create(Guid.NewGuid(), rawToken, DateTime.UtcNow.AddHours(24));

        var expectedHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
        Assert.Equal(expectedHash, token.TokenHash);
        Assert.DoesNotContain(rawToken, token.TokenHash, StringComparison.Ordinal);
        Assert.False(
            token.GetType().GetProperties()
                .Any(property => property.PropertyType == typeof(string) &&
                                 property.GetValue(token)?.ToString()?.Contains(rawToken, StringComparison.Ordinal) == true),
            "Raw verification token must never be stored on the entity.");
        Assert.Null(token.ConsumedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsEmptyAndWhitespaceRawTokens(string? malformedToken)
    {
        var expiresAtUtc = DateTime.UtcNow.AddHours(24);

        if (malformedToken is null)
        {
            Assert.Throws<ArgumentException>(() =>
                AffiliateEmailVerificationToken.HashToken(null!));
            return;
        }

        Assert.Throws<ArgumentException>(() =>
            AffiliateEmailVerificationToken.Create(Guid.NewGuid(), malformedToken, expiresAtUtc));
    }

    [Fact]
    public void Create_RejectsStructurallyNonCanonicalRawTokens()
    {
        var canonicalShape = AffiliateEmailVerificationToken.NewRawToken();
        var expiresAtUtc = DateTime.UtcNow.AddHours(24);
        var malformedVariants = new[]
        {
            canonicalShape.ToLowerInvariant(),                       // lowercase hex
            canonicalShape[..^1],                                    // truncated (63 chars)
            canonicalShape + "A",                                    // extended (65 chars)
            new string('=', AffiliateEmailVerificationToken.RawTokenCharLength), // padded transport form
            new string('/', AffiliateEmailVerificationToken.RawTokenCharLength), // Base64 transport alphabet
            new string('G', AffiliateEmailVerificationToken.RawTokenCharLength), // outside hexadecimal range
        };

        foreach (var malformed in malformedVariants)
        {
            Assert.False(AffiliateEmailVerificationToken.IsCanonicalRawToken(malformed));
            Assert.Throws<ArgumentException>(() =>
                AffiliateEmailVerificationToken.Create(Guid.NewGuid(), malformed, expiresAtUtc));
        }
    }

    [Fact]
    public void TamperedToken_DoesNotMatchOriginalHash()
    {
        var rawToken = AffiliateEmailVerificationToken.NewRawToken();
        var originalToken = AffiliateEmailVerificationToken.Create(Guid.NewGuid(), rawToken, DateTime.UtcNow.AddHours(24));

        var tamperedLast = rawToken[..^1] + (rawToken[^1] == 'A' ? 'B' : 'A');
        Assert.NotEqual(rawToken, tamperedLast);
        Assert.NotEqual(originalToken.TokenHash, AffiliateEmailVerificationToken.HashToken(tamperedLast));
    }

    // ═══════════════════════════════════════════════════════════════════
    // EXPIRY BOUNDARY AND SINGLE-USE CONSUMPTION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CanConsume_ExactExpiryInstant_IsInvalid()
    {
        var expiresAtUtc = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        var token = AffiliateEmailVerificationToken.Create(
            Guid.NewGuid(), AffiliateEmailVerificationToken.NewRawToken(), expiresAtUtc);

        Assert.True(token.CanConsume(expiresAtUtc.AddTicks(-1)));
        Assert.False(token.CanConsume(expiresAtUtc));
        Assert.False(token.CanConsume(expiresAtUtc.AddTicks(1)));
    }

    [Fact]
    public void Consume_IsSingleUseAndGuardsExpiredState()
    {
        var expiresAtUtc = DateTime.UtcNow.AddHours(24);
        var token = AffiliateEmailVerificationToken.Create(
            Guid.NewGuid(), AffiliateEmailVerificationToken.NewRawToken(), expiresAtUtc);

        var consumedAt = DateTime.UtcNow;
        token.Consume(consumedAt);

        Assert.NotNull(token.ConsumedAtUtc);
        Assert.Equal(consumedAt, token.ConsumedAtUtc!.Value);
        Assert.False(token.CanConsume(consumedAt.AddSeconds(1)));
        Assert.Throws<InvalidOperationException>(() => token.Consume(consumedAt.AddSeconds(2)));
    }

    [Fact]
    public void Consume_RejectsExactExpiryBoundary()
    {
        var expiresAtUtc = DateTime.UtcNow.AddHours(-1);
        var token = AffiliateEmailVerificationToken.Create(
            Guid.NewGuid(), AffiliateEmailVerificationToken.NewRawToken(), expiresAtUtc);

        Assert.False(token.CanConsume(expiresAtUtc));
        Assert.Throws<InvalidOperationException>(() => token.Consume(expiresAtUtc));
    }

    // ═══════════════════════════════════════════════════════════════════
    // URL ROUND TRIP
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void VerificationLink_RoundTripsRawTokenExactly()
    {
        var rawToken = AffiliateEmailVerificationToken.NewRawToken();
        const string baseUrl = "http://localhost:5173/";

        var link = $"{baseUrl.TrimEnd('/')}/affiliate/verify-email?token={Uri.EscapeDataString(rawToken)}";

        var query = new Uri(link).Query.TrimStart('?');
        var tokenPair = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Single(pair => pair.StartsWith("token=", StringComparison.Ordinal));
        var presentedToken = Uri.UnescapeDataString(tokenPair["token=".Length..]);

        Assert.Equal(rawToken, presentedToken);
        Assert.True(AffiliateEmailVerificationToken.IsCanonicalRawToken(presentedToken));
        Assert.Equal(
            AffiliateEmailVerificationToken.HashToken(rawToken),
            AffiliateEmailVerificationToken.HashToken(presentedToken));
    }

    [Fact]
    public void VerificationLink_TokenSurvivesWhitespaceTrimmingOnFrontendExtraction()
    {
        var rawToken = AffiliateEmailVerificationToken.NewRawToken();
        var link = $"https://shopixy.test/affiliate/verify-email?token={rawToken}";

        // Mirrors AffiliateVerifyEmail.jsx: searchParams.get('token')?.trim() || ''
        var extracted = new Uri(link).Query.TrimStart('?')
            .Split('&')
            .Select(pair => pair.StartsWith("token=", StringComparison.Ordinal) ? pair["token=".Length..] : null)
            .Single(value => value != null)!
            .Trim();

        Assert.True(AffiliateEmailVerificationToken.IsCanonicalRawToken(extracted));
    }

    [Fact]
    public async Task ActualControllers_RegisterVerifyRepeatLoginAndProfile_CompleteWithoutBrowser()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var emailSender = new CapturingEmailSender();
        const string email = "lifecycle@example.com";
        const string password = "StrongPassword1!";

        await using var registerContext = fixture.CreateContext();
        var registerController = CreateAuthController(registerContext, emailSender);
        Assert.IsType<OkObjectResult>(await registerController.Register(
            new AffiliateRegisterDto
            {
                Email = email,
                Password = password,
                DisplayName = "Lifecycle Affiliate"
            },
            CancellationToken.None));

        Assert.NotNull(emailSender.TextBody);
        var link = new Uri(emailSender.TextBody![emailSender.TextBody.IndexOf("http", StringComparison.Ordinal)..]);
        Assert.Equal("http://localhost:5173", link.GetLeftPart(UriPartial.Authority));
        Assert.Equal("/affiliate/verify-email", link.AbsolutePath);
        var rawToken = Uri.UnescapeDataString(link.Query.TrimStart('?')["token=".Length..]);
        Assert.True(AffiliateEmailVerificationToken.IsCanonicalRawToken(rawToken));

        var unverified = await registerContext.AffiliateUsers
            .IgnoreQueryFilters()
            .Include(user => user.AffiliateAccount)
            .SingleAsync(user => user.Email == email);
        Assert.False(unverified.EmailVerified);
        Assert.Equal(AffiliateStatus.Pending, unverified.AffiliateAccount.Status);
        Assert.Equal(1, await registerContext.AffiliateEmailVerificationTokens.CountAsync(
            token => token.AffiliateUserId == unverified.Id && token.TokenHash == AffiliateEmailVerificationToken.HashToken(rawToken)));

        Assert.IsType<UnauthorizedObjectResult>(await registerController.Login(
            new AffiliateLoginDto { Email = email, Password = password },
            CancellationToken.None));

        await using var verifyContext = fixture.CreateContext();
        var verifyController = CreateAuthController(verifyContext, emailSender);
        Assert.IsType<OkObjectResult>(await verifyController.VerifyEmail(
            new AffiliateVerifyEmailDto { Token = rawToken },
            CancellationToken.None));

        await using var repeatContext = fixture.CreateContext();
        var repeatController = CreateAuthController(repeatContext, emailSender);
        Assert.IsType<OkObjectResult>(await repeatController.VerifyEmail(
            new AffiliateVerifyEmailDto { Token = rawToken },
            CancellationToken.None));

        await using var loginContext = fixture.CreateContext();
        var loginController = CreateAuthController(loginContext, emailSender);
        Assert.IsType<OkObjectResult>(await loginController.Login(
            new AffiliateLoginDto { Email = email, Password = password },
            CancellationToken.None));

        var setCookie = loginController.Response.Headers.SetCookie
            .Single(value => value!.StartsWith("affiliate_access_token=", StringComparison.Ordinal));
        var jwtValue = setCookie!["affiliate_access_token=".Length..setCookie.IndexOf(';')];
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(jwtValue);

        await using var profileContext = fixture.CreateContext();
        var profileController = new AffiliatePortalController(
            profileContext,
            null!,
            null!,
            null!,
            null!,
            null!,
            NullLogger<AffiliatePortalController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "AffiliateJwt"))
                }
            }
        };

        var profileResult = await profileController.GetProfile(CancellationToken.None);
        var profile = Assert.IsType<OkObjectResult>(profileResult.Result);
        Assert.NotNull(profile.Value);
    }

    [Fact]
    public async Task ActualVerification_AcceptsPersistedLegacyBase64Link()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var seeded = await fixture.SeedVerifiedPendingAffiliateAsync();
        var legacyToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));

        await using (var updater = fixture.CreateContext())
        {
            await updater.AffiliateEmailVerificationTokens
                .Where(token => token.Id == seeded.TokenId)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    token => token.TokenHash,
                    AffiliateEmailVerificationToken.HashToken(legacyToken)));
        }

        await using var context = fixture.CreateContext();
        var controller = CreateAuthController(context, new CapturingEmailSender());
        Assert.IsType<OkObjectResult>(await controller.VerifyEmail(
            new AffiliateVerifyEmailDto { Token = legacyToken },
            CancellationToken.None));
    }

    [Fact]
    public async Task ActualVerification_RejectsDeletedAccountAndDeletedToken()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var deletedAccount = await fixture.SeedVerifiedPendingAffiliateAsync(
            new SeedOptions { DeleteAccount = true });
        var deletedToken = await fixture.SeedVerifiedPendingAffiliateAsync();

        await using (var updater = fixture.CreateContext())
        {
            await updater.AffiliateEmailVerificationTokens
                .Where(token => token.Id == deletedToken.TokenId)
                .ExecuteUpdateAsync(update => update.SetProperty(token => token.IsDeleted, true));
        }

        await using var accountContext = fixture.CreateContext();
        var accountController = CreateAuthController(accountContext, new CapturingEmailSender());
        Assert.IsType<UnauthorizedObjectResult>(await accountController.VerifyEmail(
            new AffiliateVerifyEmailDto { Token = deletedAccount.RawToken },
            CancellationToken.None));

        await using var tokenContext = fixture.CreateContext();
        var tokenController = CreateAuthController(tokenContext, new CapturingEmailSender());
        Assert.IsType<UnauthorizedObjectResult>(await tokenController.VerifyEmail(
            new AffiliateVerifyEmailDto { Token = deletedToken.RawToken },
            CancellationToken.None));
    }

    [Fact]
    public async Task ActualVerification_RejectsWrongPurposeToken()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var seeded = await fixture.SeedVerifiedPendingAffiliateAsync();
        var resetRawToken = AffiliateEmailVerificationToken.NewRawToken();

        await using (var seeder = fixture.CreateContext())
        {
            seeder.AffiliatePasswordResetTokens.Add(
                AffiliatePasswordResetToken.Create(seeded.AffiliateUserId, resetRawToken, DateTime.UtcNow.AddHours(1)));
            await seeder.SaveChangesAsync();
        }

        await using var context = fixture.CreateContext();
        var controller = CreateAuthController(context, new CapturingEmailSender());
        Assert.IsType<UnauthorizedObjectResult>(await controller.VerifyEmail(
            new AffiliateVerifyEmailDto { Token = resetRawToken },
            CancellationToken.None));
    }

    [Fact]
    public async Task LoggingEmailSender_DoesNotLogCredentialBearingBodies()
    {
        var logger = new CapturingLogger<LoggingEmailSender>();
        var sender = new LoggingEmailSender(logger);
        var rawToken = AffiliateEmailVerificationToken.NewRawToken();

        await sender.SendEmailAsync(
            "affiliate@example.com",
            "Verify account",
            $"<a href=\"https://shopixy.test/verify?token={rawToken}\">verify</a>",
            $"https://shopixy.test/verify?token={rawToken}");

        Assert.DoesNotContain(logger.Messages, message => message.Contains(rawToken, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("token=", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════════
    // QUERY-FILTER ROOT CAUSE AND PERSISTED LIFECYCLE (SQLite relational)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FreshToken_TenantFilteredRequiredInclude_DropsTheRowWhileProductionShapeFindsIt()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var seeded = await fixture.SeedVerifiedPendingAffiliateAsync();

        await using var reader = fixture.CreateContext();
        var tokenHash = AffiliateEmailVerificationToken.HashToken(seeded.RawToken);

        // Committed broken query shape: required navigation into tenant-filtered
        // AffiliateAccount while CurrentTenantId == null silently removes the row.
        var droppedByFilters = await reader.AffiliateEmailVerificationTokens
            .Include(t => t.AffiliateUser)
            .ThenInclude(u => u.AffiliateAccount)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
        Assert.Null(droppedByFilters);

        // Production remediated shape used by AffiliateAuthController.VerifyEmail.
        var recovered = await reader.AffiliateEmailVerificationTokens
            .IgnoreQueryFilters()
            .Include(t => t.AffiliateUser)
            .ThenInclude(u => u.AffiliateAccount)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        Assert.NotNull(recovered);
        Assert.Equal(seeded.AffiliateUserId, recovered!.AffiliateUserId);
        Assert.False(recovered.AffiliateUser.EmailVerified);
        Assert.Equal(AffiliateStatus.Pending, recovered.AffiliateUser.AffiliateAccount!.Status);
        Assert.True(recovered.CanConsume(DateTime.UtcNow));
    }

    [Fact]
    public async Task ConditionalClaim_ArbitratesRepeatedClicksToExactlyOneWinner()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var seeded = await fixture.SeedVerifiedPendingAffiliateAsync();

        var now = DateTime.UtcNow;
        var firstClaim = await ClaimFreshTokenAsync(fixture, seeded.TokenId, now);
        var replayedClaim = await ClaimFreshTokenAsync(fixture, seeded.TokenId, now);

        Assert.Equal(1, firstClaim);
        Assert.Equal(0, replayedClaim);

        await using var reader = fixture.CreateContext();
        var persisted = await reader.AffiliateEmailVerificationTokens
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(t => t.Id == seeded.TokenId);

        Assert.NotNull(persisted.ConsumedAtUtc);
        Assert.Equal(now, persisted.ConsumedAtUtc!.Value);
    }

    [Fact]
    public async Task ConditionalClaim_ExpiredTokenClaimsZeroRows()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var seeded = await fixture.SeedVerifiedPendingAffiliateAsync(
            new SeedOptions { ExpiredToken = true });

        var claimedRows = await ClaimFreshTokenAsync(fixture, seeded.TokenId, DateTime.UtcNow);

        Assert.Equal(0, claimedRows);
    }

    [Fact]
    public async Task MalformedPresentedToken_ResolvesNoPersistedRow()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var seeded = await fixture.SeedVerifiedPendingAffiliateAsync();
        var malformedVariants = new[]
        {
            seeded.RawToken.ToLowerInvariant(),
            seeded.RawToken[..63],
            $"{seeded.RawToken}A",
            new string('+', 64),
        };

        await using var reader = fixture.CreateContext();
        foreach (var malformed in malformedVariants)
        {
            var resolved = await reader.AffiliateEmailVerificationTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TokenHash == AffiliateEmailVerificationToken.HashToken(malformed));
            Assert.Null(resolved);
        }
    }

    [Fact]
    public async Task SoftDeletedIdentity_IsVisibleWithIgnoreQueryFilters_AndMustBeRejectedExplicitly()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var deletedAccountSeed = await fixture.SeedVerifiedPendingAffiliateAsync(
            new SeedOptions { DeleteAccount = true });
        var deletedUserSeed = await fixture.SeedVerifiedPendingAffiliateAsync(
            new SeedOptions { DeleteUser = true });

        await using var reader = fixture.CreateContext();

        var accountRow = await reader.AffiliateEmailVerificationTokens
            .IgnoreQueryFilters()
            .Include(t => t.AffiliateUser)
            .ThenInclude(u => u.AffiliateAccount)
            .SingleAsync(t => t.Id == deletedAccountSeed.TokenId);
        var userRow = await reader.AffiliateEmailVerificationTokens
            .IgnoreQueryFilters()
            .Include(t => t.AffiliateUser)
            .ThenInclude(u => u.AffiliateAccount)
            .SingleAsync(t => t.Id == deletedUserSeed.TokenId);

        // IgnoreQueryFilters makes deleted identities visible again, so the explicit
        // production guards in VerifyEmail must reject both.
        Assert.True(accountRow.AffiliateUser.AffiliateAccount!.IsDeleted);
        Assert.True(userRow.AffiliateUser.IsDeleted);
    }

    [Fact]
    public async Task ResendReplacement_InvalidatesOutstandingTokensOnlyForThatAffiliate()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var primary = await fixture.SeedVerifiedPendingAffiliateAsync();
        var secondary = await fixture.SeedVerifiedPendingAffiliateAsync();

        var historicalConsumedRaw = AffiliateEmailVerificationToken.NewRawToken();
        await fixture.SeedAdditionalTokenAsync(
            primary.AffiliateUserId, historicalConsumedRaw, DateTime.UtcNow.AddHours(-22), consumed: true);

        var staleRawOne = AffiliateEmailVerificationToken.NewRawToken();
        var staleRawTwo = AffiliateEmailVerificationToken.NewRawToken();
        await fixture.SeedAdditionalTokenAsync(primary.AffiliateUserId, staleRawOne, DateTime.UtcNow.AddHours(24));
        await fixture.SeedAdditionalTokenAsync(primary.AffiliateUserId, staleRawTwo, DateTime.UtcNow.AddHours(24));

        var replacementRaw = AffiliateEmailVerificationToken.NewRawToken();
        await using (var resender = fixture.CreateContext())
        {
            // Mirrors IssueVerificationEmailAsync: delete outstanding, add newest.
            await resender.AffiliateEmailVerificationTokens
                .Where(t => t.AffiliateUserId == primary.AffiliateUserId && t.ConsumedAtUtc == null)
                .ExecuteDeleteAsync();

            resender.AffiliateEmailVerificationTokens.Add(
                AffiliateEmailVerificationToken.Create(primary.AffiliateUserId, replacementRaw, DateTime.UtcNow.AddHours(24)));
            await resender.SaveChangesAsync();
        }

        await using var reader = fixture.CreateContext();

        Assert.Null(await reader.AffiliateEmailVerificationTokens.FirstOrDefaultAsync(
            t => t.TokenHash == AffiliateEmailVerificationToken.HashToken(staleRawOne)));
        Assert.Null(await reader.AffiliateEmailVerificationTokens.FirstOrDefaultAsync(
            t => t.TokenHash == AffiliateEmailVerificationToken.HashToken(staleRawTwo)));

        var replacement = await reader.AffiliateEmailVerificationTokens.SingleAsync(
            t => t.TokenHash == AffiliateEmailVerificationToken.HashToken(replacementRaw));
        Assert.Equal(primary.AffiliateUserId, replacement.AffiliateUserId);
        Assert.Null(replacement.ConsumedAtUtc);

        var secondaryStillValid = await reader.AffiliateEmailVerificationTokens.SingleAsync(
            t => t.TokenHash == AffiliateEmailVerificationToken.HashToken(secondary.RawToken));
        Assert.Equal(secondary.AffiliateUserId, secondaryStillValid.AffiliateUserId);
        Assert.Null(secondaryStillValid.ConsumedAtUtc);

        var historical = await reader.AffiliateEmailVerificationTokens.SingleAsync(
            t => t.TokenHash == AffiliateEmailVerificationToken.HashToken(historicalConsumedRaw));
        Assert.NotNull(historical.ConsumedAtUtc);
    }

    [Fact]
    public async Task VerifiedLoginEligibility_RemainsBlockedUntilVerificationCompletesActivation()
    {
        using var fixture = await VerificationFixture.CreateAsync();
        var seeded = await fixture.SeedVerifiedPendingAffiliateAsync();

        await using (var verifier = fixture.CreateContext())
        {
            var token = await verifier.AffiliateEmailVerificationTokens
                .IgnoreQueryFilters()
                .Include(t => t.AffiliateUser)
                .ThenInclude(u => u.AffiliateAccount)
                .SingleAsync(t => t.Id == seeded.TokenId);

            Assert.False(token.AffiliateUser.EmailVerified);

            var now = DateTime.UtcNow;
            var claimedRows = await verifier.AffiliateEmailVerificationTokens
                .Where(t => t.Id == token.Id && t.ConsumedAtUtc == null && t.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(t => t.ConsumedAtUtc, now)
                    .SetProperty(t => t.UpdatedAt, now));
            Assert.Equal(1, claimedRows);

            token.Consume(now);
            token.AffiliateUser.VerifyEmail();
            if (token.AffiliateUser.AffiliateAccount?.Status == AffiliateStatus.Pending)
            {
                token.AffiliateUser.AffiliateAccount.Activate();
            }
            await verifier.SaveChangesAsync();
        }

        await using var reader = fixture.CreateContext();
        var verifiedUser = await reader.AffiliateUsers
            .IgnoreQueryFilters()
            .Include(u => u.AffiliateAccount)
            .SingleAsync(u => u.Id == seeded.AffiliateUserId);

        Assert.True(verifiedUser.EmailVerified);
        Assert.Equal(AffiliateStatus.Active, verifiedUser.AffiliateAccount!.Status);
        Assert.True(verifiedUser.AffiliateAccount.IsActive);
    }

    // ═══════════════════════════════════════════════════════════════════
    // OPT-IN REAL POSTGRESQL PROOF (skipped silently without infrastructure)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Postgres_FreshToken_TenantFilteredRequiredIncludeDropsRow_AndConditionalClaimArbitratesOnce()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) ||
            databaseScope is null)
        {
            return;
        }

        Guid tokenId;
        string rawToken;
        await using (var seeder = databaseScope.CreateDbContext())
        {
            (_, _, tokenId, rawToken) = await SeedAffiliateIdentityAsync(seeder, new SeedOptions());
        }

        await using (var reader = databaseScope.CreateDbContext())
        {
            var tokenHash = AffiliateEmailVerificationToken.HashToken(rawToken);

            var droppedByFilters = await reader.AffiliateEmailVerificationTokens
                .Include(t => t.AffiliateUser)
                .ThenInclude(u => u.AffiliateAccount)
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
            Assert.Null(droppedByFilters);

            var recovered = await reader.AffiliateEmailVerificationTokens
                .IgnoreQueryFilters()
                .Include(t => t.AffiliateUser)
                .ThenInclude(u => u.AffiliateAccount)
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
            Assert.NotNull(recovered);
            Assert.Equal(DateTimeKind.Utc, recovered!.ExpiresAtUtc.Kind);
        }

        var now = DateTime.UtcNow;
        await using (var claimer = databaseScope.CreateDbContext())
        {
            var claimedRows = await claimer.AffiliateEmailVerificationTokens
                .Where(t => t.Id == tokenId && t.ConsumedAtUtc == null && t.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(t => t.ConsumedAtUtc, now)
                    .SetProperty(t => t.UpdatedAt, now));
            Assert.Equal(1, claimedRows);
        }

        await using (var replayer = databaseScope.CreateDbContext())
        {
            var replayedRows = await replayer.AffiliateEmailVerificationTokens
                .Where(t => t.Id == tokenId && t.ConsumedAtUtc == null && t.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(t => t.ConsumedAtUtc, now)
                    .SetProperty(t => t.UpdatedAt, now));
            Assert.Equal(0, replayedRows);
        }

        Guid concurrentTokenId;
        await using (var seeder = databaseScope.CreateDbContext())
        {
            var concurrentRawToken = AffiliateEmailVerificationToken.NewRawToken();
            var concurrentToken = AffiliateEmailVerificationToken.Create(
                (await seeder.AffiliateEmailVerificationTokens
                    .IgnoreQueryFilters()
                    .Where(token => token.Id == tokenId)
                    .Select(token => token.AffiliateUserId)
                    .SingleAsync()),
                concurrentRawToken,
                DateTime.UtcNow.AddHours(24));
            seeder.AffiliateEmailVerificationTokens.Add(concurrentToken);
            await seeder.SaveChangesAsync();
            concurrentTokenId = concurrentToken.Id;
        }

        await using var firstConcurrentContext = databaseScope.CreateDbContext();
        await using var secondConcurrentContext = databaseScope.CreateDbContext();
        var releaseClaims = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrentNow = DateTime.UtcNow;

        async Task<int> ClaimConcurrently(ApplicationDbContext context)
        {
            await releaseClaims.Task;
            return await context.AffiliateEmailVerificationTokens
                .Where(token =>
                    token.Id == concurrentTokenId &&
                    !token.IsDeleted &&
                    token.ConsumedAtUtc == null &&
                    token.ExpiresAtUtc > concurrentNow)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(token => token.ConsumedAtUtc, concurrentNow)
                    .SetProperty(token => token.UpdatedAt, concurrentNow));
        }

        var firstConcurrentClaim = ClaimConcurrently(firstConcurrentContext);
        var secondConcurrentClaim = ClaimConcurrently(secondConcurrentContext);
        releaseClaims.SetResult();

        var concurrentResults = await Task.WhenAll(firstConcurrentClaim, secondConcurrentClaim);
        Assert.Equal(new[] { 0, 1 }, concurrentResults.Order());
    }

    // ═══════════════════════════════════════════════════════════════════
    // FIXTURE
    // ═══════════════════════════════════════════════════════════════════

    private static async Task<int> ClaimFreshTokenAsync(VerificationFixture fixture, Guid tokenId, DateTime now)
    {
        await using var context = fixture.CreateContext();
        return await context.AffiliateEmailVerificationTokens
            .Where(t => t.Id == tokenId && t.ConsumedAtUtc == null && t.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(update => update
                .SetProperty(t => t.ConsumedAtUtc, now)
                .SetProperty(t => t.UpdatedAt, now));
    }

    private static AffiliateAuthController CreateAuthController(
        ApplicationDbContext context,
        IEmailSender emailSender)
    {
        var controller = new AffiliateAuthController(
            context,
            null!,
            Options.Create(new AffiliateJwtSettings
            {
                Secret = "affiliate-verification-test-secret-that-is-long-enough",
                Issuer = "AffiliateVerificationTests",
                Audience = "AffiliateVerificationTests",
                AccessTokenExpirationMinutes = 15,
                RefreshTokenExpirationDays = 1
            }),
            new PasswordHasher<AffiliateUser>(),
            emailSender,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AffiliateIdentity:PublicBaseUrl"] = "http://localhost:5173"
                })
                .Build(),
            NullLogger<AffiliateAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        return controller;
    }

    private static async Task<(Guid AccountId, Guid UserId, Guid TokenId, string RawToken)> SeedAffiliateIdentityAsync(
        ApplicationDbContext context,
        SeedOptions options)
    {
        var tenantId = Guid.NewGuid();
        var account = AffiliateAccount.Create(
            tenantId, Guid.NewGuid(), $"AF{Guid.NewGuid():N}".ToUpperInvariant()[..10], "Verification Affiliate");
        context.AffiliateAccounts.Add(account);
        await context.SaveChangesAsync();

        var user = AffiliateUser.Create(tenantId, $"verify-{Guid.NewGuid():N}@example.com", "seed-hash", account.Id);
        context.AffiliateUsers.Add(user);
        await context.SaveChangesAsync();

        if (options.DeleteAccount)
        {
            account.IsDeleted = true;
        }
        if (options.DeleteUser)
        {
            user.IsDeleted = true;
        }
        if (options.DeleteAccount || options.DeleteUser)
        {
            await context.SaveChangesAsync();
        }

        var rawToken = AffiliateEmailVerificationToken.NewRawToken();
        var token = AffiliateEmailVerificationToken.Create(
            user.Id,
            rawToken,
            options.ExpiredToken ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(24));
        context.AffiliateEmailVerificationTokens.Add(token);
        await context.SaveChangesAsync();

        return (account.Id, user.Id, token.Id, rawToken);
    }

    private readonly struct SeedOptions
    {
        public bool ExpiredToken { get; init; }
        public bool DeleteUser { get; init; }
        public bool DeleteAccount { get; init; }
    }

    private sealed record SeededAffiliate(Guid AccountId, Guid AffiliateUserId, Guid TokenId, string RawToken);

    private sealed class CapturingEmailSender : IEmailSender
    {
        public string? TextBody { get; private set; }

        public Task SendEmailAsync(string to, string subject, string htmlBody, string textBody)
        {
            TextBody = textBody;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class VerificationFixture : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<ApplicationDbContext> _options;

        private VerificationFixture(SqliteConnection connection, DbContextOptions<ApplicationDbContext> options)
        {
            _connection = connection;
            _options = options;
        }

        public static async Task<VerificationFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();
            RegisterSqliteCompatibilityFunctions(connection);

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            // Single-argument constructor: no ITenantContext -> CurrentTenantId == null,
            // exactly the anonymous /api/affiliate/auth/* request environment.
            await using (var creator = new ApplicationDbContext(options))
            {
                await creator.Database.EnsureCreatedAsync();
            }

            return new VerificationFixture(connection, options);
        }

        private static void RegisterSqliteCompatibilityFunctions(SqliteConnection connection)
        {
            // Model check constraints use PostgreSQL's btrim(); SQLite has no built-in
            // equivalent, so a semantic shim keeps the relational schema creatable.
            connection.CreateFunction("btrim", (string? value) => value?.Trim());
            connection.CreateFunction(
                "btrim",
                (string? value, string? trimCharacters) => value?.Trim(trimCharacters?.ToCharArray() ?? Array.Empty<char>()));
            connection.CreateFunction("char_length", (string? value) => (long?)value?.Length);
            connection.CreateFunction(
                "regexp_replace",
                (string? value, string? pattern, string? replacement) =>
                    value is null ? null : System.Text.RegularExpressions.Regex.Replace(value, pattern ?? string.Empty, replacement ?? string.Empty));
            connection.CreateFunction(
                "regexp_replace",
                (string? value, string? pattern, string? replacement, string? flags) =>
                    value is null ? null : System.Text.RegularExpressions.Regex.Replace(value, pattern ?? string.Empty, replacement ?? string.Empty));
        }

        public ApplicationDbContext CreateContext() => new(_options);

        public async Task<SeededAffiliate> SeedVerifiedPendingAffiliateAsync(SeedOptions? options = null)
        {
            await using var context = CreateContext();
            var seed = await SeedAffiliateIdentityAsync(context, options ?? new SeedOptions());
            return new SeededAffiliate(seed.AccountId, seed.UserId, seed.TokenId, seed.RawToken);
        }

        public async Task SeedAdditionalTokenAsync(
            Guid affiliateUserId,
            string rawToken,
            DateTime expiresAtUtc,
            bool consumed = false)
        {
            await using var context = CreateContext();
            var token = AffiliateEmailVerificationToken.Create(affiliateUserId, rawToken, expiresAtUtc);
            if (consumed)
            {
                token.Consume(expiresAtUtc.AddSeconds(-1));
            }

            context.AffiliateEmailVerificationTokens.Add(token);
            await context.SaveChangesAsync();
        }

        public void Dispose() => _connection.Dispose();
    }
}
