using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using MassTransit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Models;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using Xunit;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.Integration;

/// <summary>
/// Integration tests for StorefrontSocialLoginService Facebook Bearer flow.
/// Validates Authorization header usage, response parsing, and cancellation behavior.
/// </summary>
public class StorefrontSocialLoginIntegrationTests : IDisposable
{
    private readonly TestDatabaseScope _databaseScope;
    private readonly ApplicationDbContext _context;
    private readonly Guid _tenantId;
    private readonly Mock<IEventPublisher> _publishEndpointMock;
    private readonly Mock<IStorefrontRefreshTokenService> _refreshTokenServiceMock;
    private readonly Mock<IStorefrontAuthRateLimiter> _rateLimiterMock;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly StorefrontJwtSettings _jwtSettings;
    
    // Captured values from IssueTokenAsync calls for assertion
    private string? _lastCapturedJti;
    private DateTime? _lastCapturedExpiry;

    public StorefrontSocialLoginIntegrationTests()
    {
        _databaseScope = TestDatabaseScope.Open();
        _tenantId = Guid.NewGuid();
        _context = _databaseScope.CreateDbContext(_tenantId);
        _publishEndpointMock = new Mock<IEventPublisher>();
        _refreshTokenServiceMock = new Mock<IStorefrontRefreshTokenService>();
        _rateLimiterMock = new Mock<IStorefrontAuthRateLimiter>();
        _dataProtectionProvider = TestDataProtectionProviderFactory.CreateProvider();

        _jwtSettings = new StorefrontJwtSettings
        {
            Secret = "ThisIsATestSecretKeyThatIsLongEnoughForJwtTokenGeneration123!",
            Issuer = "StorefrontAPI-Test",
            Audience = "StorefrontClient-Test",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        };

        // Setup refresh token service mock with callback to capture JTI and expiry
        _refreshTokenServiceMock
            .Setup(x => x.IssueTokenAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, string, string?, string?, DateTime?, CancellationToken>(
                (_, _, _, _, jti, expiry, _) =>
                {
                    _lastCapturedJti = jti;
                    _lastCapturedExpiry = expiry;
                })
            .ReturnsAsync(new RefreshTokenResult { RawRefreshToken = "test-refresh-token" });
    }

    #region FacebookLoginAsync Tests

    [Fact]
    public async Task FacebookLoginAsync_SendsBearerAuthorizationHeader()
    {
        // Arrange
        var accessToken = "test-facebook-access-token";
        var capturedRequests = new List<HttpRequestMessage>();

        var handler = new MockHttpMessageHandler((request, cancellationToken) =>
        {
            capturedRequests.Add(request);

            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/debug_token" => CreateFacebookDebugTokenResponse(),
                "/me" => CreateFacebookProfileResponse(email: "test@example.com"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        });

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory);

        SetupFacebookProvider();

        // Act
        var beforeCall = DateTime.UtcNow;
        await service.FacebookLoginAsync(_tenantId, accessToken, "127.0.0.1", "TestAgent");
        var afterCall = DateTime.UtcNow;

        // Assert
        Assert.Equal(2, capturedRequests.Count);

        var debugTokenRequest = Assert.Single(capturedRequests.Where(request => request.RequestUri?.AbsolutePath == "/debug_token"));
        Assert.NotNull(debugTokenRequest.Headers.Authorization);
        Assert.Equal("Bearer", debugTokenRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-facebook-app-id|test-facebook-app-secret", debugTokenRequest.Headers.Authorization.Parameter);

        var profileRequest = Assert.Single(capturedRequests.Where(request => request.RequestUri?.AbsolutePath == "/me"));
        Assert.NotNull(profileRequest.Headers.Authorization);
        Assert.Equal("Bearer", profileRequest.Headers.Authorization!.Scheme);
        Assert.Equal(accessToken, profileRequest.Headers.Authorization.Parameter);

        // Assert: JTI and expiry were forwarded to IssueTokenAsync
        AssertValidJtiAndExpiry(beforeCall, afterCall);
    }

    [Fact]
    public async Task FacebookLoginAsync_DebugTokenValidation_UsesInputTokenWithoutLeakingAppAccessTokenInQueryString()
    {
        // Arrange
        var accessToken = "test-facebook-access-token";
        var capturedRequests = new List<HttpRequestMessage>();

        var handler = new MockHttpMessageHandler((request, cancellationToken) =>
        {
            capturedRequests.Add(request);

            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/debug_token" => CreateFacebookDebugTokenResponse(),
                "/me" => CreateFacebookProfileResponse(email: "test@example.com"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        });

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory);

        SetupFacebookProvider();

        // Act
        var beforeCall = DateTime.UtcNow;
        await service.FacebookLoginAsync(_tenantId, accessToken, "127.0.0.1", "TestAgent");
        var afterCall = DateTime.UtcNow;

        // Assert
        Assert.Equal(2, capturedRequests.Count);

        var debugTokenRequest = Assert.Single(capturedRequests.Where(request => request.RequestUri?.AbsolutePath == "/debug_token"));
        var debugTokenQuery = QueryHelpers.ParseQuery(debugTokenRequest.RequestUri?.Query ?? string.Empty);
        Assert.Equal("test-facebook-access-token", debugTokenQuery["input_token"].ToString());
        Assert.False(debugTokenQuery.ContainsKey("access_token"));

        var profileRequest = Assert.Single(capturedRequests.Where(request => request.RequestUri?.AbsolutePath == "/me"));
        var profileQuery = QueryHelpers.ParseQuery(profileRequest.RequestUri?.Query ?? string.Empty);
        Assert.False(profileQuery.ContainsKey("access_token"));
        Assert.DoesNotContain(accessToken, profileRequest.RequestUri?.Query ?? string.Empty);

        // Assert: JTI and expiry were forwarded to IssueTokenAsync
        AssertValidJtiAndExpiry(beforeCall, afterCall);
    }

    [Fact]
    public async Task FacebookLoginAsync_UsesCorrectFacebookGraphApiUrl()
    {
        // Arrange
        var accessToken = "test-facebook-access-token";
        var capturedUris = new List<Uri>();

        var handler = new MockHttpMessageHandler((request, cancellationToken) =>
        {
            if (request.RequestUri is not null)
            {
                capturedUris.Add(request.RequestUri);
            }

            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/debug_token" => CreateFacebookDebugTokenResponse(),
                "/me" => CreateFacebookProfileResponse(email: "test@example.com"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        });

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory);

        SetupFacebookProvider();

        // Act
        var beforeCall = DateTime.UtcNow;
        await service.FacebookLoginAsync(_tenantId, accessToken, "127.0.0.1", "TestAgent");
        var afterCall = DateTime.UtcNow;

        // Assert
        Assert.Equal(2, capturedUris.Count);

        var debugTokenUri = Assert.Single(capturedUris.Where(uri => uri.AbsolutePath == "/debug_token"));
        Assert.Equal("graph.facebook.com", debugTokenUri.Host);
        var debugTokenQuery = QueryHelpers.ParseQuery(debugTokenUri.Query);
        Assert.Equal("test-facebook-access-token", debugTokenQuery["input_token"].ToString());

        var profileUri = Assert.Single(capturedUris.Where(uri => uri.AbsolutePath == "/me"));
        Assert.Equal("graph.facebook.com", profileUri.Host);
        Assert.Equal("/me", profileUri.AbsolutePath);
        var profileQuery = QueryHelpers.ParseQuery(profileUri.Query);
        Assert.Equal("id,email,name,picture", profileQuery["fields"].ToString());

        // Assert: JTI and expiry were forwarded to IssueTokenAsync
        AssertValidJtiAndExpiry(beforeCall, afterCall);
    }

    [Fact]
    public async Task FacebookLoginAsync_ParsesSuccessfulResponseCorrectly()
    {
        // Arrange
        var facebookUserId = "123456789";
        var facebookEmail = "test@example.com";
        var facebookName = "Test User";
        var pictureUrl = "https://example.com/picture.jpg";

        var handler = new MockHttpMessageHandler((request, cancellationToken) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/debug_token" => CreateFacebookDebugTokenResponse(userId: facebookUserId),
                "/me" => CreateFacebookProfileResponse(
                    userId: facebookUserId,
                    email: facebookEmail,
                    name: facebookName,
                    pictureUrl: pictureUrl),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        });

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory);

        SetupFacebookProvider();

        // Act
        var beforeCall = DateTime.UtcNow;
        var result = await service.FacebookLoginAsync(_tenantId, "test-token", "127.0.0.1", "TestAgent");
        var afterCall = DateTime.UtcNow;

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Customer);
        Assert.Equal(facebookEmail, result.Customer.Email);
        Assert.Equal(facebookName, result.Customer.DisplayName);

        // Assert: JTI and expiry were forwarded to IssueTokenAsync
        AssertValidJtiAndExpiry(beforeCall, afterCall);
    }

    [Fact]
    public async Task FacebookLoginAsync_ForwardsNonNullJtiAndExpiry_ToIssueTokenAsync()
    {
        // Arrange
        // Use a fresh per-test mock so we can attach a Callback without
        // affecting the shared constructor-level setup used by other tests.
        string? capturedJti = null;
        DateTime? capturedExpiry = null;

        var refreshTokenMock = new Mock<IStorefrontRefreshTokenService>();
        refreshTokenMock
            .Setup(x => x.IssueTokenAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, string, string?, string?, DateTime?, CancellationToken>(
                (_, _, _, _, jti, expiry, _) =>
                {
                    capturedJti = jti;
                    capturedExpiry = expiry;
                })
            .ReturnsAsync(new RefreshTokenResult { RawRefreshToken = "test-refresh-token", Success = true });

        var handler = new MockHttpMessageHandler((request, cancellationToken) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/debug_token" => CreateFacebookDebugTokenResponse(),
                "/me" => CreateFacebookProfileResponse(email: "jti-test@example.com"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        });

        var httpClientFactory = CreateHttpClientFactory(handler);

        // Build a service with the capturing mock instead of the shared one
        var service = new StorefrontSocialLoginService(
            _context,
            _dataProtectionProvider,
            refreshTokenMock.Object,
            _rateLimiterMock.Object,
            Options.Create(_jwtSettings),
            _publishEndpointMock.Object,
            httpClientFactory.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StorefrontSocialLoginService>.Instance);

        SetupFacebookProvider();

        // Record the lower bound of the expected expiry window before the call
        var beforeCall = DateTime.UtcNow;

        // Act
        var result = await service.FacebookLoginAsync(_tenantId, "test-token", "127.0.0.1", "TestAgent");

        var afterCall = DateTime.UtcNow;

        // Assert – service must succeed so we know the code path reached IssueTokenAsync
        Assert.True(result.Success, $"Login failed unexpectedly: {result.Error}");

        // The JTI forwarded to IssueTokenAsync must be a non-empty GUID string so that
        // the refresh token record can be tied to the exact access token for revocation.
        Assert.NotNull(capturedJti);
        Assert.True(Guid.TryParse(capturedJti, out _),
            $"Expected JTI to be a valid GUID, but got: '{capturedJti}'");

        // The expiry forwarded to IssueTokenAsync must fall inside the configured
        // AccessTokenExpirationMinutes window so token revocation uses the right TTL.
        Assert.NotNull(capturedExpiry);
        var expectedExpiryLow  = beforeCall.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
        var expectedExpiryHigh = afterCall .AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
        Assert.InRange(capturedExpiry.Value, expectedExpiryLow, expectedExpiryHigh);
    }

    [Fact]
    public async Task FacebookLoginAsync_ReturnsInvalidToken_WhenFacebookReturnsError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((request, cancellationToken) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Invalid OAuth access token\"}}", System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory);

        SetupFacebookProvider();

        // Act
        var result = await service.FacebookLoginAsync(_tenantId, "invalid-token", "127.0.0.1", "TestAgent");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("INVALID_TOKEN", result.Error);
    }

    [Fact]
    public async Task FacebookLoginAsync_ReturnsEmailRequired_WhenEmailNotProvided()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((request, cancellationToken) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/debug_token" => CreateFacebookDebugTokenResponse(),
                "/me" => CreateFacebookProfileResponse(email: null),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        });

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory);

        SetupFacebookProvider();

        // Act
        var result = await service.FacebookLoginAsync(_tenantId, "test-token", "127.0.0.1", "TestAgent");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("EMAIL_REQUIRED", result.Error);
    }

    [Fact]
    public async Task FacebookLoginAsync_ReturnsProviderDisabled_WhenFacebookNotEnabled()
    {
        // Arrange - No Facebook provider setup
        var called = false;
        var handler = new MockHttpMessageHandler((request, cancellationToken) =>
        {
            called = true;
            return Task.FromResult(CreateFacebookProfileResponse());
        });

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory);

        // Act
        var result = await service.FacebookLoginAsync(_tenantId, "test-token", "127.0.0.1", "TestAgent");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("PROVIDER_DISABLED", result.Error);
        Assert.False(called);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task FacebookLoginAsync_RespectsCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new MockHttpMessageHandler((request, cancellationToken) =>
        {
            // The HttpClient will throw OperationCanceledException when cancelled
            var response = request.RequestUri?.AbsolutePath == "/debug_token"
                ? CreateFacebookDebugTokenResponse()
                : CreateFacebookProfileResponse();
            return Task.FromResult(response);
        });

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory);

        SetupFacebookProvider();

        // Act & Assert - EF Core respects the cancellation token and throws OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.FacebookLoginAsync(_tenantId, "test-token", "127.0.0.1", "TestAgent", cts.Token));
    }

    [Fact]
    public async Task FacebookLoginAsync_HandlesHttpClientCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var requestReceived = new TaskCompletionSource<bool>();

        var handler = new MockHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestReceived.TrySetResult(true);
            
            // Simulate a delay that would be cancelled
            await Task.Delay(100, cancellationToken);
            
            return request.RequestUri?.AbsolutePath == "/debug_token"
                ? CreateFacebookDebugTokenResponse()
                : CreateFacebookProfileResponse();
        });

        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = CreateService(httpClientFactory);

        SetupFacebookProvider();

        // Act - Start the login and wait for request to begin before canceling
        var task = service.FacebookLoginAsync(_tenantId, "test-token", "127.0.0.1", "TestAgent", cts.Token);
        
        // Wait for the HTTP request to start
        await requestReceived.Task;
        
        // Now cancel after the request has started
        cts.Cancel();

        // Assert - Cancellation should propagate as OperationCanceledException, not be converted to INVALID_TOKEN
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    #endregion

    #region Helper Methods

    private void AssertValidJtiAndExpiry(DateTime beforeCall, DateTime afterCall)
    {
        // Assert: JTI must be a valid GUID
        Assert.NotNull(_lastCapturedJti);
        Assert.True(Guid.TryParse(_lastCapturedJti, out _), $"Expected JTI to be a valid GUID, but got: '{_lastCapturedJti}'");
        
        // Assert: Expiry must fall within the expected window
        Assert.NotNull(_lastCapturedExpiry);
        var expectedExpiryLow = beforeCall.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
        var expectedExpiryHigh = afterCall.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
        Assert.InRange(_lastCapturedExpiry.Value, expectedExpiryLow, expectedExpiryHigh);
    }

    private StorefrontSocialLoginService CreateService(Mock<IHttpClientFactory> httpClientFactory)
    {
        return new StorefrontSocialLoginService(
            _context,
            _dataProtectionProvider,
            _refreshTokenServiceMock.Object,
            _rateLimiterMock.Object,
            Options.Create(_jwtSettings),
            _publishEndpointMock.Object,
            httpClientFactory.Object,
            NullLogger<StorefrontSocialLoginService>.Instance);
    }

    private Mock<IHttpClientFactory> CreateHttpClientFactory(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);
        return httpClientFactory;
    }

    private void SetupFacebookProvider()
    {
        var protector = _dataProtectionProvider.CreateProtector("Storefront.AuthProviders.Secrets");
        var providerSetting = new StorefrontAuthProviderSettings
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Provider = AuthProvider.Facebook,
            ClientId = "test-facebook-app-id",
            ClientSecretEncrypted = protector.Protect("test-facebook-app-secret"),
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.StorefrontAuthProviderSettings.Add(providerSetting);
        _context.SaveChanges();
    }

    private static HttpResponseMessage CreateFacebookProfileResponse(
        string userId = "123456789",
        string? email = "test@example.com",
        string? name = "Test User",
        string? pictureUrl = "https://example.com/picture.jpg")
    {
        var json = JsonSerializer.Serialize(new
        {
            id = userId,
            email,
            name,
            picture = new { data = new { url = pictureUrl } }
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateFacebookDebugTokenResponse(
        string userId = "123456789",
        bool isValid = true,
        string appId = "test-facebook-app-id")
    {
        var json = JsonSerializer.Serialize(new
        {
            data = new
            {
                app_id = appId,
                type = "USER",
                application = "PointOfSale Test App",
                data_access_expires_at = 0,
                expires_at = 0,
                is_valid = isValid,
                scopes = new[] { "email", "public_profile" },
                user_id = userId
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    #endregion

    public void Dispose()
    {
        _context.Dispose();
        _databaseScope.Dispose();
    }
}

/// <summary>
/// Mock HTTP message handler for testing HttpClient interactions.
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _handler(request, cancellationToken);
    }
}
