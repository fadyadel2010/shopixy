using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// Official FedEx runtime endpoint and authentication surface.
/// This foundation executes OAuth only; shipment, tracking, label, pickup, and other carrier operations remain separate phases.
/// </summary>
public sealed class FedExOptions
{
    public const string SandboxBaseUrl = "https://apis-sandbox.fedex.com/";
    public const string ProductionBaseUrl = "https://apis.fedex.com/";

    public const string OAuthTokenPath = "/oauth/token";
    public const int OfficialTokenLifetimeSeconds = 3600;

    public const string CreateShipmentPath = "/ship/v1/shipments";
    public const string ValidateShipmentPath = "/ship/v1/shipments/packages/validate";
    public const string RetrieveAsyncShipmentPath = "/ship/v1/shipments/results";
    public const string CancelShipmentPath = "/ship/v1/shipments/cancel";
    public const string TrackByTrackingNumberPath = "/track/v1/trackingnumbers";
    public const string PickupAvailabilityPath = "/pickup/v1/pickups/availabilities";
    public const string CreatePickupPath = "/pickup/v1/pickups";
    public const string CancelPickupPath = "/pickup/v1/pickups/cancel";
    public const string ServiceAndPackagingOptionsPath = "/availability/v1/packageandserviceoptions";
    public const string SpecialServiceOptionsPath = "/availability/v1/specialserviceoptions";
    public const string TransitTimesPath = "/availability/v1/transittimes";
    public const string AddressValidationPath = "/address/v1/addresses/resolve";

    public string SandboxBaseUri { get; init; } = SandboxBaseUrl;
    public string ProductionBaseUri { get; init; } = ProductionBaseUrl;
    public string OAuthTokenEndpointPath { get; init; } = OAuthTokenPath;
    public int TokenLifetimeSeconds { get; init; } = OfficialTokenLifetimeSeconds;
    public int TokenExpirationSkewSeconds { get; init; } = 60;
    public int OAuthMaxRetryCount { get; init; } = 1;
    public string OAuthHttpClientName { get; init; } = "FedExOAuth";
    public string TransportHttpClientName { get; init; } = "FedEx";
    public int TransportMaxRetryCount { get; init; } = 1;

    public Uri GetBaseUri(bool useSandbox) =>
        new(useSandbox ? SandboxBaseUri : ProductionBaseUri, UriKind.Absolute);

    public Uri GetOAuthTokenUri(bool useSandbox) =>
        new(GetBaseUri(useSandbox), OAuthTokenEndpointPath.TrimStart('/'));

    public Uri GetTransportUri(bool useSandbox, string endpointPath) =>
        new(GetBaseUri(useSandbox), endpointPath.TrimStart('/'));
}

public sealed record FedExOAuthRequest(
    string ClientId,
    string ClientSecret,
    string GrantType,
    string? ChildKey = null,
    string? ChildSecret = null,
    string? AccountNumber = null);

public sealed record FedExOAuthTokenResult(
    bool Success,
    string? AccessToken,
    string? TokenType,
    int? ExpiresIn,
    string? Scope,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? ExpiresAtUtc = null,
    bool FromCache = false);

public sealed record FedExAuthorizationHeaderResult(
    bool Success,
    AuthenticationHeaderValue? AuthorizationHeader,
    DateTimeOffset? ExpiresAtUtc,
    bool FromCache,
    string? ErrorCode,
    string? ErrorMessage);

public interface IFedExOAuthClient
{
    FedExOptions Options { get; }

    Task<FedExOAuthTokenResult> GetAccessTokenAsync(
        FedExOAuthRequest request,
        bool useSandbox,
        CancellationToken cancellationToken = default);

    Task<FedExAuthorizationHeaderResult> GetAuthorizationHeaderAsync(
        FedExOAuthRequest request,
        bool useSandbox,
        CancellationToken cancellationToken = default);

    Task<FedExAuthorizationHeaderResult> RefreshAuthorizationHeaderAfterUnauthorizedAsync(
        FedExOAuthRequest request,
        bool useSandbox,
        CancellationToken cancellationToken = default);
}

public sealed class FedExOAuthClient : IFedExOAuthClient
{
    private const string InvalidCredentialsCode = "FEDEX_OAUTH_INVALID_CREDENTIALS";
    private const string InvalidResponseCode = "FEDEX_OAUTH_INVALID_RESPONSE";
    private const string TokenUnavailableCode = "FEDEX_OAUTH_TOKEN_UNAVAILABLE";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FedExOAuthClient> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<TokenCacheKey, CachedToken> _cache = new();
    private readonly ConcurrentDictionary<TokenCacheKey, SemaphoreSlim> _locks = new();

    public FedExOAuthClient(
        FedExOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<FedExOAuthClient> logger)
        : this(options, httpClientFactory, logger, TimeProvider.System)
    {
    }

    public FedExOAuthClient(
        FedExOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<FedExOAuthClient> logger,
        TimeProvider timeProvider)
    {
        Options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public FedExOptions Options { get; }

    public Task<FedExOAuthTokenResult> GetAccessTokenAsync(
        FedExOAuthRequest request,
        bool useSandbox,
        CancellationToken cancellationToken = default) =>
        GetAccessTokenCoreAsync(request, useSandbox, forceRefresh: false, cancellationToken);

    public async Task<FedExAuthorizationHeaderResult> GetAuthorizationHeaderAsync(
        FedExOAuthRequest request,
        bool useSandbox,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenCoreAsync(request, useSandbox, forceRefresh: false, cancellationToken);
        return ToAuthorizationHeader(token);
    }

    public async Task<FedExAuthorizationHeaderResult> RefreshAuthorizationHeaderAfterUnauthorizedAsync(
        FedExOAuthRequest request,
        bool useSandbox,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateRequest(request);
        if (!validation.Success)
        {
            return ToAuthorizationHeader(validation);
        }

        _cache.TryRemove(CreateCacheKey(request, useSandbox), out _);
        var token = await GetAccessTokenCoreAsync(request, useSandbox, forceRefresh: true, cancellationToken);
        return ToAuthorizationHeader(token);
    }

    private async Task<FedExOAuthTokenResult> GetAccessTokenCoreAsync(
        FedExOAuthRequest request,
        bool useSandbox,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request);
        if (!validation.Success)
        {
            return validation;
        }

        var key = CreateCacheKey(request, useSandbox);
        if (!forceRefresh && TryGetValidCachedToken(key, out var cachedToken))
        {
            return cachedToken.ToResult(fromCache: true);
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && TryGetValidCachedToken(key, out cachedToken))
            {
                return cachedToken.ToResult(fromCache: true);
            }

            var acquired = await AcquireTokenAsync(request, useSandbox, cancellationToken);
            if (!acquired.Success)
            {
                return acquired;
            }

            var token = new CachedToken(
                acquired.AccessToken!,
                acquired.TokenType!,
                acquired.ExpiresIn!.Value,
                acquired.Scope,
                acquired.ExpiresAtUtc!.Value);
            _cache[key] = token;
            return token.ToResult(fromCache: false);
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGetValidCachedToken(TokenCacheKey key, out CachedToken token)
    {
        if (_cache.TryGetValue(key, out token!) && token.ExpiresAtUtc > EffectiveNowUtc())
        {
            return true;
        }

        token = null!;
        return false;
    }

    private DateTimeOffset EffectiveNowUtc() =>
        _timeProvider.GetUtcNow().AddSeconds(Math.Max(0, Options.TokenExpirationSkewSeconds));

    private async Task<FedExOAuthTokenResult> AcquireTokenAsync(
        FedExOAuthRequest request,
        bool useSandbox,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var attempts = Math.Max(0, Options.OAuthMaxRetryCount) + 1;
        var retryCount = 0;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            HttpStatusCode? statusCode = null;

            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Options.GetOAuthTokenUri(useSandbox))
                {
                    Content = new StringContent(FedExContractSerializer.SerializeOAuthForm(request))
                };
                httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

                var client = _httpClientFactory.CreateClient(Options.OAuthHttpClientName);
                using var response = await client.SendAsync(httpRequest, cancellationToken);
                statusCode = response.StatusCode;
                LogOAuthAttempt(correlationId, started, statusCode, retryCount);

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return ParseTokenResponse(body);
                }

                if (IsTransient(response.StatusCode) && attempt < attempts)
                {
                    retryCount++;
                    continue;
                }

                return new FedExOAuthTokenResult(
                    Success: false,
                    AccessToken: null,
                    TokenType: null,
                    ExpiresIn: null,
                    Scope: null,
                    ErrorCode: IsInvalidCredentialStatus(response.StatusCode) ? InvalidCredentialsCode : TokenUnavailableCode,
                    ErrorMessage: "FedEx OAuth token request was rejected.");
            }
            catch (HttpRequestException) when (attempt < attempts)
            {
                LogOAuthAttempt(correlationId, started, statusCode, retryCount);
                retryCount++;
            }
            catch (HttpRequestException)
            {
                LogOAuthAttempt(correlationId, started, statusCode, retryCount);
                return new FedExOAuthTokenResult(
                    Success: false,
                    AccessToken: null,
                    TokenType: null,
                    ExpiresIn: null,
                    Scope: null,
                    ErrorCode: TokenUnavailableCode,
                    ErrorMessage: "FedEx OAuth token request failed after transient retries.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
                LogOAuthAttempt(correlationId, started, statusCode, retryCount);
                retryCount++;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogOAuthAttempt(correlationId, started, statusCode, retryCount);
                return new FedExOAuthTokenResult(
                    Success: false,
                    AccessToken: null,
                    TokenType: null,
                    ExpiresIn: null,
                    Scope: null,
                    ErrorCode: TokenUnavailableCode,
                    ErrorMessage: "FedEx OAuth token request failed after transient retries.");
            }
            catch (JsonException)
            {
                LogOAuthAttempt(correlationId, started, statusCode, retryCount);
                return InvalidOAuthResponse("FedEx OAuth response JSON is malformed.");
            }
        }

        return new FedExOAuthTokenResult(
            Success: false,
            AccessToken: null,
            TokenType: null,
            ExpiresIn: null,
            Scope: null,
            ErrorCode: TokenUnavailableCode,
            ErrorMessage: "FedEx OAuth token request failed after transient retries.");
    }

    private FedExOAuthTokenResult ParseTokenResponse(string body)
    {
        try
        {
            var response = JsonSerializer.Deserialize<FedExOAuthResponse>(body, FedExContractSerializer.JsonOptions);
            if (response is null ||
                string.IsNullOrWhiteSpace(response.AccessToken) ||
                string.IsNullOrWhiteSpace(response.TokenType) ||
                !string.Equals(response.TokenType.Trim(), "bearer", StringComparison.OrdinalIgnoreCase) ||
                response.ExpiresIn is null or <= 0)
            {
                return InvalidOAuthResponse("FedEx OAuth response is missing required token fields or contains invalid values.");
            }

            var expiresAt = _timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn.Value);
            return new FedExOAuthTokenResult(
                Success: true,
                AccessToken: response.AccessToken,
                TokenType: response.TokenType,
                ExpiresIn: response.ExpiresIn,
                Scope: response.Scope,
                ErrorCode: null,
                ErrorMessage: null,
                ExpiresAtUtc: expiresAt,
                FromCache: false);
        }
        catch (JsonException)
        {
            return InvalidOAuthResponse("FedEx OAuth response JSON is malformed.");
        }
    }

    private static FedExOAuthTokenResult ValidateRequest(FedExOAuthRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.AccountNumber))
        {
            errors.Add("account_number is required for FedEx OAuth cache scope.");
        }

        var contractValidation = FedExContractValidator.Validate(request);
        errors.AddRange(contractValidation.Errors);

        if (errors.Count == 0)
        {
            return new FedExOAuthTokenResult(true, null, null, null, null, null, null);
        }

        return new FedExOAuthTokenResult(
            Success: false,
            AccessToken: null,
            TokenType: null,
            ExpiresIn: null,
            Scope: null,
            ErrorCode: InvalidCredentialsCode,
            ErrorMessage: string.Join(" ", errors));
    }

    private static FedExOAuthTokenResult InvalidOAuthResponse(string message) =>
        new(
            Success: false,
            AccessToken: null,
            TokenType: null,
            ExpiresIn: null,
            Scope: null,
            ErrorCode: InvalidResponseCode,
            ErrorMessage: message);

    private static FedExAuthorizationHeaderResult ToAuthorizationHeader(FedExOAuthTokenResult token)
    {
        if (!token.Success)
        {
            return new FedExAuthorizationHeaderResult(
                false,
                null,
                token.ExpiresAtUtc,
                token.FromCache,
                token.ErrorCode,
                token.ErrorMessage);
        }

        return new FedExAuthorizationHeaderResult(
            true,
            new AuthenticationHeaderValue("Bearer", token.AccessToken),
            token.ExpiresAtUtc,
            token.FromCache,
            null,
            null);
    }

    private static TokenCacheKey CreateCacheKey(FedExOAuthRequest request, bool useSandbox) =>
        new(
            Normalize(request.AccountNumber),
            useSandbox ? "sandbox" : "production",
            Normalize(request.ClientId));

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode == 429 ||
        (int)statusCode >= 500;

    private static bool IsInvalidCredentialStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private void LogOAuthAttempt(
        string correlationId,
        long started,
        HttpStatusCode? statusCode,
        int retryCount)
    {
        var durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _logger.LogInformation(
            "FedEx OAuth token request completed. CorrelationId={CorrelationId} DurationMs={DurationMs} HttpStatus={HttpStatus} RetryCount={RetryCount}",
            correlationId,
            durationMs,
            statusCode.HasValue ? (int)statusCode.Value : 0,
            retryCount);
    }

    private sealed record TokenCacheKey(string AccountNumber, string Environment, string ClientId);

    private sealed record CachedToken(
        string AccessToken,
        string TokenType,
        int ExpiresIn,
        string? Scope,
        DateTimeOffset ExpiresAtUtc)
    {
        public FedExOAuthTokenResult ToResult(bool fromCache) =>
            new(
                Success: true,
                AccessToken: AccessToken,
                TokenType: TokenType,
                ExpiresIn: ExpiresIn,
                Scope: Scope,
                ErrorCode: null,
                ErrorMessage: null,
                ExpiresAtUtc: ExpiresAtUtc,
                FromCache: fromCache);
    }

    private sealed record FedExOAuthResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);
}

public sealed record FedExUnsupportedOperationResult(
    bool Success,
    string ErrorCode,
    string ErrorMessage);
