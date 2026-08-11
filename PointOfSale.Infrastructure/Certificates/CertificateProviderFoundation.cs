using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Certificates;

public sealed class CertificateProviderOptions
{
    public const string SectionName = "CertificateProvider";

    public bool Enabled { get; set; }
    public ProviderType Provider { get; set; } = ProviderType.LetsEncrypt;
    public CertificateProviderEnvironment Environment { get; set; } = CertificateProviderEnvironment.Staging;
    public string AccountReference { get; set; } = string.Empty;
    public string? ConfiguredIssuer { get; set; }
    public string? ContactEmail { get; set; }
    public Uri? DirectoryUrl { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 30;
    public string UserAgent { get; set; } = "Shopixy-CertificateProviderFoundation/5.3";
    public bool HonorProviderRetryAfter { get; set; } = true;
    public CertificateRevocationControlOptions RevocationControl { get; set; } = new();

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Clamp(RequestTimeoutSeconds, 1, 120));
}

public sealed class CertificateRevocationControlOptions
{
    public bool Enabled { get; set; }
    public Uri? BaseUrl { get; set; }
    public string RevokePath { get; set; } = "/v1/certificates/revoke";
    public string HealthPath { get; set; } = "/v1/health";
    public int RequestTimeoutSeconds { get; set; } = 30;
    public string AuthorizationScheme { get; set; } = "Bearer";
    public string BearerToken { get; set; } = string.Empty;
    public string ControlSurfaceReference { get; set; } = string.Empty;

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Clamp(RequestTimeoutSeconds, 1, 120));
}

public sealed class CertificateProviderOptionsValidator : IValidateOptions<CertificateProviderOptions>
{
    public ValidateOptionsResult Validate(string? name, CertificateProviderOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AccountReference))
        {
            errors.Add("CertificateProvider:AccountReference is required when the provider is enabled.");
        }

        if (options.DirectoryUrl is null || !options.DirectoryUrl.IsAbsoluteUri)
        {
            errors.Add("CertificateProvider:DirectoryUrl must be an absolute URI when the provider is enabled.");
        }

        if (options.RequestTimeoutSeconds is <= 0 or > 120)
        {
            errors.Add("CertificateProvider:RequestTimeoutSeconds must be between 1 and 120.");
        }

        if (options.Provider != ProviderType.LetsEncrypt)
        {
            errors.Add("DOMAIN-5.3 provider foundation currently admits only the canonical LetsEncrypt/Caddy-managed ACME provider.");
        }

        if (options.RevocationControl.Enabled)
        {
            ValidateRevocationControl(options.RevocationControl, errors);
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    internal static void ValidateRevocationControl(CertificateRevocationControlOptions options, List<string> errors)
    {
        if (options.BaseUrl is null || !options.BaseUrl.IsAbsoluteUri)
        {
            errors.Add("CertificateProvider:RevocationControl:BaseUrl must be an absolute URI when revocation control is enabled.");
        }
        else
        {
            if (!string.Equals(options.BaseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("CertificateProvider:RevocationControl:BaseUrl must use HTTPS.");
            }

            if (!string.IsNullOrEmpty(options.BaseUrl.Query) || !string.IsNullOrEmpty(options.BaseUrl.Fragment))
            {
                errors.Add("CertificateProvider:RevocationControl:BaseUrl must not include query string or fragment components.");
            }
        }

        if (!IsSafeRelativePath(options.RevokePath))
        {
            errors.Add("CertificateProvider:RevocationControl:RevokePath must be an absolute relative path.");
        }

        if (!IsSafeRelativePath(options.HealthPath))
        {
            errors.Add("CertificateProvider:RevocationControl:HealthPath must be an absolute relative path.");
        }

        if (options.RequestTimeoutSeconds is <= 0 or > 120)
        {
            errors.Add("CertificateProvider:RevocationControl:RequestTimeoutSeconds must be between 1 and 120.");
        }

        if (!string.Equals(options.AuthorizationScheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("CertificateProvider:RevocationControl:AuthorizationScheme must be Bearer.");
        }

        if (string.IsNullOrWhiteSpace(options.BearerToken))
        {
            errors.Add("CertificateProvider:RevocationControl:BearerToken is required when revocation control is enabled.");
        }
    }

    private static bool IsSafeRelativePath(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith("/", StringComparison.Ordinal)
        && !value.StartsWith("//", StringComparison.Ordinal)
        && Uri.TryCreate(value, UriKind.Relative, out _)
        && !value.Contains("..", StringComparison.Ordinal);
}

public interface ICertificateRevocationControlClient
{
    Task<CertificateRevocationControlResult> RevokeAsync(
        CertificateRevocationControlRequest request,
        CancellationToken cancellationToken);

    Task<CertificateRevocationControlHealthResult> CheckHealthAsync(
        CancellationToken cancellationToken);
}

public sealed record CertificateRevocationControlRequest(
    Guid TenantId,
    Guid DomainId,
    Guid CertificateId,
    Guid OperationId,
    string IdempotencyKey,
    ProviderType Provider,
    CertificateProviderEnvironment Environment,
    string AccountReference,
    string ReasonCode,
    string? ProviderCertificateReference,
    string? EdgeCertificateReference,
    CertificateRevocationCertificateIdentity CertificateIdentity,
    string? CorrelationId);

public sealed record CertificateRevocationControlResult(
    bool Succeeded,
    string Status,
    string? RevocationReference,
    CertificateProviderError? Error);

public sealed record CertificateRevocationControlHealthResult(
    bool Available,
    string SafeMessage,
    string? FailureCode);

public sealed class HttpCertificateRevocationControlClient : ICertificateRevocationControlClient
{
    public const string HttpClientName = "CertificateProvider.RevocationControl";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<CertificateProviderOptions> _options;
    private readonly ILogger<HttpCertificateRevocationControlClient> _logger;

    public HttpCertificateRevocationControlClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<CertificateProviderOptions> options,
        ILogger<HttpCertificateRevocationControlClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CertificateRevocationControlResult> RevokeAsync(
        CertificateRevocationControlRequest request,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var validationError = ValidateControlOptions(options.RevocationControl, request.CorrelationId);
        if (validationError is not null)
        {
            return new CertificateRevocationControlResult(false, "CONTROL_SURFACE_UNAVAILABLE", null, validationError);
        }

        request.CertificateIdentity.Validate();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RevocationControl.RequestTimeout);

        try
        {
            var client = CreateClient(options.RevocationControl);
            using var message = new HttpRequestMessage(HttpMethod.Post, BuildUri(options.RevocationControl.BaseUrl!, options.RevocationControl.RevokePath))
            {
                Content = JsonContent.Create(new RevocationControlHttpRequest(
                    request.TenantId,
                    request.DomainId,
                    request.CertificateId,
                    request.OperationId,
                    request.IdempotencyKey,
                    request.Provider.ToString(),
                    request.Environment.ToString(),
                    request.AccountReference,
                    request.ReasonCode,
                    request.ProviderCertificateReference,
                    request.EdgeCertificateReference,
                    request.CertificateIdentity.Generation,
                    request.CertificateIdentity.NormalizedHostName,
                    request.CertificateIdentity.Thumbprint,
                    request.CertificateIdentity.Fingerprint,
                    request.CertificateIdentity.SerialNumber,
                    request.CertificateIdentity.Issuer,
                    request.CertificateIdentity.Subject,
                    request.CertificateIdentity.NotBeforeUtc,
                    request.CertificateIdentity.NotAfterUtc,
                    request.CorrelationId),
                    options: JsonOptions)
            };

            message.Headers.Authorization = new(options.RevocationControl.AuthorizationScheme, options.RevocationControl.BearerToken);
            message.Headers.TryAddWithoutValidation("X-Correlation-Id", request.CorrelationId ?? string.Empty);

            var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return await MapResponseAsync(response, request.CorrelationId, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new CertificateRevocationControlResult(
                false,
                "TIMEOUT",
                null,
                CertificateProviderError.Create(
                    CertificateProviderErrorCategory.Timeout,
                    "CA_REVOCATION_TIMEOUT",
                    retryable: true,
                    safeMessage: "The certificate revocation control surface timed out.",
                    correlationId: request.CorrelationId));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Certificate revocation control request failed | Status: {StatusCode}",
                ex.StatusCode?.ToString() ?? "none");

            return new CertificateRevocationControlResult(
                false,
                "CONTROL_SURFACE_UNAVAILABLE",
                null,
                CertificateProviderError.Create(
                    CertificateProviderErrorCategory.ProviderUnavailable,
                    "CA_REVOCATION_CONTROL_SURFACE_UNAVAILABLE",
                    retryable: true,
                    safeMessage: "The certificate revocation control surface is unavailable.",
                    providerStatus: ex.StatusCode?.ToString(),
                    correlationId: request.CorrelationId));
        }
        catch (JsonException)
        {
            return new CertificateRevocationControlResult(
                false,
                "PERMANENT_PROVIDER_FAILURE",
                null,
                CertificateProviderError.Create(
                    CertificateProviderErrorCategory.UnknownProviderError,
                    "CA_REVOCATION_CONTROL_RESPONSE_INVALID",
                    retryable: false,
                    safeMessage: "The certificate revocation control surface returned an invalid response.",
                    correlationId: request.CorrelationId));
        }
    }

    public async Task<CertificateRevocationControlHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue.RevocationControl;
        var validationErrors = new List<string>();
        CertificateProviderOptionsValidator.ValidateRevocationControl(options, validationErrors);
        if (!options.Enabled || validationErrors.Count > 0)
        {
            return new CertificateRevocationControlHealthResult(
                false,
                options.Enabled
                    ? "Certificate revocation control configuration is invalid."
                    : "Certificate revocation control is disabled.",
                "CA_REVOCATION_CONTROL_CONFIGURATION_INVALID");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);

        try
        {
            var client = CreateClient(options);
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(options.BaseUrl!, options.HealthPath));
            request.Headers.Authorization = new(options.AuthorizationScheme, options.BearerToken);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode
                ? new CertificateRevocationControlHealthResult(true, "Certificate revocation control is reachable.", null)
                : new CertificateRevocationControlHealthResult(false, "Certificate revocation control health check failed.", $"HTTP_{(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new CertificateRevocationControlHealthResult(false, "Certificate revocation control health check timed out.", "CA_REVOCATION_CONTROL_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return new CertificateRevocationControlHealthResult(false, "Certificate revocation control is unreachable.", "CA_REVOCATION_CONTROL_UNREACHABLE");
        }
    }

    private HttpClient CreateClient(CertificateRevocationControlOptions options)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(_options.CurrentValue.UserAgent);
        return client;
    }

    private static async Task<CertificateRevocationControlResult> MapResponseAsync(
        HttpResponseMessage response,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var retryAfter = response.Headers.RetryAfter?.Date?.UtcDateTime;
        if (retryAfter is null && response.Headers.RetryAfter?.Delta is { } delta)
        {
            retryAfter = DateTime.UtcNow.Add(delta);
        }

        RevocationControlHttpResponse? body = null;
        if (response.Content.Headers.ContentLength is not 0)
        {
            body = await response.Content.ReadFromJsonAsync<RevocationControlHttpResponse>(JsonOptions, cancellationToken);
        }

        if (response.IsSuccessStatusCode && body is not null)
        {
            var status = NormalizeStatus(body.Status);
            if (status is "SUCCESS" or "ALREADY_REVOKED")
            {
                return new CertificateRevocationControlResult(true, status, body.RevocationReference, null);
            }

            return new CertificateRevocationControlResult(
                false,
                status,
                null,
                MapStatusToError(status, body.SafeMessage, body.ProviderStatus, body.ProviderReference, correlationId, body.RetryAfterUtc?.UtcDateTime ?? retryAfter));
        }

        var httpStatus = $"HTTP_{(int)response.StatusCode}";
        var fallbackStatus = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "UNAUTHORIZED",
            HttpStatusCode.NotFound => "CONTROL_SURFACE_UNAVAILABLE",
            HttpStatusCode.TooManyRequests => "RATE_LIMITED",
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "TIMEOUT",
            _ when (int)response.StatusCode >= 500 => "TEMPORARY_PROVIDER_FAILURE",
            _ => "PERMANENT_PROVIDER_FAILURE",
        };

        return new CertificateRevocationControlResult(
            false,
            fallbackStatus,
            null,
            MapStatusToError(
                fallbackStatus,
                body?.SafeMessage ?? "Certificate revocation control request failed.",
                body?.ProviderStatus ?? httpStatus,
                body?.ProviderReference,
                correlationId,
                body?.RetryAfterUtc?.UtcDateTime ?? retryAfter));
    }

    private static CertificateProviderError? ValidateControlOptions(
        CertificateRevocationControlOptions options,
        string? correlationId)
    {
        var errors = new List<string>();
        CertificateProviderOptionsValidator.ValidateRevocationControl(options, errors);
        return !options.Enabled || errors.Count > 0
            ? CertificateProviderError.Create(
                CertificateProviderErrorCategory.ConfigurationError,
                "CADDY_CA_REVOCATION_CONTROL_UNAVAILABLE",
                retryable: false,
                safeMessage: "No authorized CA revocation control surface is configured.",
                correlationId: correlationId)
            : null;
    }

    private static Uri BuildUri(Uri baseUrl, string path) =>
        new(baseUrl, path);

    private static string NormalizeStatus(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? "PERMANENT_PROVIDER_FAILURE"
            : status.Trim().ToUpperInvariant();

    private static CertificateProviderError MapStatusToError(
        string status,
        string? safeMessage,
        string? providerStatus,
        string? providerReference,
        string? correlationId,
        DateTime? retryAfterUtc)
    {
        var (category, code, retryable) = status switch
        {
            "NOT_FOUND" => (CertificateProviderErrorCategory.CertificateRejected, "CA_REVOCATION_CERTIFICATE_NOT_FOUND", false),
            "UNAUTHORIZED" => (CertificateProviderErrorCategory.AuthorizationDenied, "CA_REVOCATION_UNAUTHORIZED", false),
            "RATE_LIMITED" => (CertificateProviderErrorCategory.RateLimited, "CA_REVOCATION_RATE_LIMITED", true),
            "TEMPORARY_PROVIDER_FAILURE" => (CertificateProviderErrorCategory.ProviderUnavailable, "CA_REVOCATION_TEMPORARY_PROVIDER_FAILURE", true),
            "PERMANENT_PROVIDER_FAILURE" => (CertificateProviderErrorCategory.UnknownProviderError, "CA_REVOCATION_PERMANENT_PROVIDER_FAILURE", false),
            "INVALID_CERTIFICATE" => (CertificateProviderErrorCategory.CertificateRejected, "CA_REVOCATION_INVALID_CERTIFICATE", false),
            "CERTIFICATE_NOT_REVOCABLE" => (CertificateProviderErrorCategory.CertificateRejected, "CA_REVOCATION_CERTIFICATE_NOT_REVOCABLE", false),
            "CONTROL_SURFACE_UNAVAILABLE" => (CertificateProviderErrorCategory.ProviderUnavailable, "CA_REVOCATION_CONTROL_SURFACE_UNAVAILABLE", true),
            "TIMEOUT" => (CertificateProviderErrorCategory.Timeout, "CA_REVOCATION_TIMEOUT", true),
            "CANCELLED" => (CertificateProviderErrorCategory.Timeout, "CA_REVOCATION_CANCELLED", true),
            _ => (CertificateProviderErrorCategory.UnknownProviderError, "CA_REVOCATION_UNKNOWN_RESULT", false),
        };

        return CertificateProviderError.Create(
            category,
            code,
            retryable,
            string.IsNullOrWhiteSpace(safeMessage) ? "Certificate revocation failed." : safeMessage,
            providerStatus: providerStatus,
            providerReference: providerReference,
            correlationId: correlationId,
            retryAfterUtc: retryAfterUtc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(retryAfterUtc.Value, DateTimeKind.Utc)));
    }

    private sealed record RevocationControlHttpRequest(
        Guid TenantId,
        Guid DomainId,
        Guid CertificateId,
        Guid OperationId,
        string IdempotencyKey,
        string Provider,
        string Environment,
        string AccountReference,
        string ReasonCode,
        string? ProviderCertificateReference,
        string? EdgeCertificateReference,
        int Generation,
        string NormalizedHostName,
        string? Thumbprint,
        string? Fingerprint,
        string? SerialNumber,
        string? Issuer,
        string? Subject,
        DateTimeOffset? NotBeforeUtc,
        DateTimeOffset? NotAfterUtc,
        string? CorrelationId);

    private sealed record RevocationControlHttpResponse(
        string? Status,
        string? RevocationReference,
        string? SafeMessage,
        string? ProviderStatus,
        string? ProviderReference,
        DateTimeOffset? RetryAfterUtc);
}

public sealed class CertificateProviderAccountSelector : ICertificateProviderAccountSelector
{
    private readonly IOptionsMonitor<CertificateProviderOptions> _options;

    public CertificateProviderAccountSelector(IOptionsMonitor<CertificateProviderOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public CertificateProviderAccountSelection Select(CertificateProviderAccountSelectionRequest request)
    {
        request.Validate();
        var options = _options.CurrentValue;

        if (options.Enabled)
        {
            if (request.Provider != options.Provider)
            {
                throw new InvalidOperationException("Requested certificate provider does not match configured provider.");
            }

            if (request.Environment != options.Environment)
            {
                throw new InvalidOperationException("Requested certificate provider environment does not match configured environment.");
            }

            if (!string.Equals(request.AccountReference, options.AccountReference, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Requested certificate account reference does not match configured account reference.");
            }
        }

        return new CertificateProviderAccountSelection(
            request.Provider,
            request.Environment,
            request.AccountReference,
            request.ConfiguredIssuer);
    }
}

public sealed class CaddyManagedAcmeCertificateProvider : ICertificateProvider
{
    public const string HttpClientName = "CertificateProvider.CaddyManagedAcme";
    public const bool SupportsLiveIssuance = true;
    public const bool SupportsLiveRevocation = true;
    public static bool SupportsLiveExecution => SupportsLiveIssuance && SupportsLiveRevocation;

    private readonly IOptionsMonitor<CertificateProviderOptions> _options;
    private readonly IOptionsMonitor<CaddyEdgeOptions> _edgeOptions;
    private readonly ICaddyEdgeClient _edgeClient;
    private readonly IEdgeCertificateProbe _certificateProbe;
    private readonly ICertificateRevocationControlClient _revocationControlClient;
    private readonly ILogger<CaddyManagedAcmeCertificateProvider> _logger;

    public CaddyManagedAcmeCertificateProvider(
        IOptionsMonitor<CertificateProviderOptions> options,
        IOptionsMonitor<CaddyEdgeOptions> edgeOptions,
        ICaddyEdgeClient edgeClient,
        IEdgeCertificateProbe certificateProbe,
        ICertificateRevocationControlClient revocationControlClient,
        ILogger<CaddyManagedAcmeCertificateProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _edgeOptions = edgeOptions ?? throw new ArgumentNullException(nameof(edgeOptions));
        _edgeClient = edgeClient ?? throw new ArgumentNullException(nameof(edgeClient));
        _certificateProbe = certificateProbe ?? throw new ArgumentNullException(nameof(certificateProbe));
        _revocationControlClient = revocationControlClient ?? throw new ArgumentNullException(nameof(revocationControlClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CertificateProviderAccountResult> EnsureAccountAsync(
        AcmeAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        return await ExecuteAsync(
            request.Context,
            request.Provider,
            request.Environment,
            "account",
            request.CorrelationId,
            cancellationToken,
            () =>
            {
                var configError = ValidateEnabledConfiguration(request.Provider, request.Environment, request.AccountReference, request.CorrelationId);
                if (configError is not null)
                {
                    return CertificateProviderAccountResult.Failure(configError);
                }

                var account = new AcmeAccountReference(
                    request.Provider,
                    request.Environment,
                    request.AccountReference,
                    AcmeAccountRegistrationStatus.Pending,
                    CertificateSecretCustody.ExternalEdgeStorage,
                    ProviderAccountIdentifier: request.AccountReference,
                    ContactEmail: request.ContactEmail,
                    CorrelationId: request.CorrelationId);

                return CertificateProviderAccountResult.Success(account);
            });
    }

    public Task<CertificateProviderOrderResult> CreateOrderAsync(
        CertificateIssuanceRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        return ExecuteAsync(
            request.Context,
            request.Account.Provider,
            request.Account.Environment,
            "order",
            request.CorrelationId,
            cancellationToken,
            async () =>
            {
                var configError = ValidateEnabledConfiguration(
                    request.Account.Provider,
                    request.Account.Environment,
                    request.Account.AccountReference,
                    request.CorrelationId);
                if (configError is not null)
                {
                    return CertificateProviderOrderResult.Failure(configError);
                }

                if (request.SubjectAlternativeNames.Count != 1 ||
                    !request.SubjectAlternativeNames.Contains(request.NormalizedHostName, StringComparer.Ordinal))
                {
                    return CertificateProviderOrderResult.Failure(CertificateProviderError.Create(
                        CertificateProviderErrorCategory.DomainRejected,
                        "CADDY_MULTI_IDENTIFIER_ORDER_UNSUPPORTED",
                        retryable: false,
                        safeMessage: "Caddy-managed tenant certificates must contain exactly the admitted hostname.",
                        correlationId: request.CorrelationId));
                }

                var route = await _edgeClient.EnsureDomainRouteAsync(new CaddyDomainRouteRequest(
                    request.Context.TenantId,
                    request.Context.DomainId,
                    request.Context.CertificateId!.Value,
                    request.Context.OperationId,
                    request.NormalizedHostName,
                    request.CorrelationId ?? string.Empty,
                    request.Context.FencingToken), cancellationToken);

                if (!route.Succeeded)
                {
                    return CertificateProviderOrderResult.Failure(NormalizeEdgeFailure(
                        route.FailureCode,
                        route.SafeFailureReason,
                        route.Retryable,
                        request.CorrelationId));
                }

                var operationReference = BuildReference("operation", request.NormalizedHostName, request.Context);
                var authorizationReference = BuildReference("authorization", request.NormalizedHostName, request.Context);
                var now = DateTimeOffset.UtcNow;
                var probe = await ProbeAsync(request.Context, request.NormalizedHostName, request.CorrelationId, cancellationToken);
                if (!probe.Succeeded && !probe.Retryable)
                {
                    return CertificateProviderOrderResult.Failure(NormalizeProbeFailure(probe, request.CorrelationId));
                }

                var succeeded = probe.Succeeded && probe.Certificate is not null;
                var order = new CertificateOrder(
                    request.Context.OperationId,
                    BuildReference("order", request.NormalizedHostName, request.Context),
                    succeeded ? CertificateProviderOrderStatus.Valid : CertificateProviderOrderStatus.Processing,
                    request.NormalizedHostName,
                    request.SubjectAlternativeNames,
                    Array.AsReadOnly(new[] { authorizationReference }),
                    now,
                    now.AddMinutes(30),
                    succeeded ? now : null);

                return CertificateProviderOrderResult.Success(
                    order,
                    BuildOperationMetadata(operationReference, order.ProviderOrderReference, authorizationReference, probe));
            });
    }

    public Task<CertificateProviderAuthorizationResult> GetAuthorizationAsync(
        CertificateAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        return ExecuteAsync(
            request.Context,
            request.Provider,
            request.Environment,
            "authorization",
            request.CorrelationId,
            cancellationToken,
            async () =>
            {
                var configError = ValidateEnabledConfiguration(
                    request.Provider,
                    request.Environment,
                    _options.CurrentValue.AccountReference,
                    request.CorrelationId);
                if (configError is not null)
                {
                    return CertificateProviderAuthorizationResult.Failure(configError);
                }

                if (!TryParseReference(request.AuthorizationReference, "authorization", request.Context, out var host))
                {
                    return CertificateProviderAuthorizationResult.Failure(InvalidReferenceError(request.CorrelationId));
                }

                var route = await _edgeClient.GetDomainRouteStateAsync(new CaddyDomainRouteQuery(
                    request.Context.TenantId,
                    request.Context.DomainId,
                    host,
                    request.CorrelationId ?? string.Empty), cancellationToken);
                if (!route.Exists || !route.MatchesDesiredState)
                {
                    return CertificateProviderAuthorizationResult.Failure(NormalizeEdgeFailure(
                        route.FailureCode ?? "CADDY_ROUTE_NOT_ADMITTED",
                        route.SafeFailureReason ?? "The hostname is not admitted at the Caddy edge.",
                        retryable: true,
                        request.CorrelationId));
                }

                var probe = await ProbeAsync(request.Context, host, request.CorrelationId, cancellationToken);
                if (!probe.Succeeded && !probe.Retryable)
                {
                    return CertificateProviderAuthorizationResult.Failure(NormalizeProbeFailure(probe, request.CorrelationId));
                }

                var challengeReferences = Array.AsReadOnly(new[]
                {
                    BuildReference("http-01", host, request.Context),
                    BuildReference("tls-alpn-01", host, request.Context),
                });
                return CertificateProviderAuthorizationResult.Success(new CertificateAuthorization(
                    request.AuthorizationReference,
                    host,
                    probe.Succeeded ? CertificateProviderAuthorizationStatus.Valid : CertificateProviderAuthorizationStatus.Pending,
                    DateTimeOffset.UtcNow.AddMinutes(30),
                    CertificateChallengePolicy.Default.AllowedChallengeTypes,
                    challengeReferences));
            });
    }

    public Task<CertificateProviderChallengeResult> GetChallengeAsync(
        CertificateChallengeRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        return ExecuteAsync(
            request.Context,
            request.Provider,
            request.Environment,
            "challenge",
            request.CorrelationId,
            cancellationToken,
            async () =>
            {
                var configError = ValidateEnabledConfiguration(
                    request.Provider,
                    request.Environment,
                    _options.CurrentValue.AccountReference,
                    request.CorrelationId);
                if (configError is not null)
                {
                    return CertificateProviderChallengeResult.Failure(configError);
                }

                var kind = request.ChallengeType == CertificateChallengeType.Http01 ? "http-01" : "tls-alpn-01";
                if (!TryParseReference(request.ChallengeReference, kind, request.Context, out var host))
                {
                    return CertificateProviderChallengeResult.Failure(InvalidReferenceError(request.CorrelationId));
                }

                var route = await _edgeClient.GetDomainRouteStateAsync(new CaddyDomainRouteQuery(
                    request.Context.TenantId,
                    request.Context.DomainId,
                    host,
                    request.CorrelationId ?? string.Empty), cancellationToken);
                if (!route.Exists || !route.MatchesDesiredState)
                {
                    return CertificateProviderChallengeResult.Failure(NormalizeEdgeFailure(
                        route.FailureCode ?? "CADDY_ROUTE_NOT_ADMITTED",
                        route.SafeFailureReason ?? "The hostname is not admitted at the Caddy edge.",
                        retryable: true,
                        request.CorrelationId));
                }

                return CertificateProviderChallengeResult.Success(new CertificateChallenge(
                    request.ChallengeReference,
                    request.ChallengeType,
                    host,
                    Secret: null,
                    SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["executor"] = "caddy-automatic-https",
                        ["custody"] = "external-edge-storage",
                        ["state"] = "admitted",
                    }));
            });
    }

    public Task<CertificateProviderOperationResult> GetOperationStatusAsync(
        CertificateProviderOperationQuery query,
        CancellationToken cancellationToken = default)
    {
        query.Validate();
        return ExecuteAsync(
            query.Context,
            query.Provider,
            query.Environment,
            "status",
            query.CorrelationId,
            cancellationToken,
            async () =>
            {
                var configError = ValidateEnabledConfiguration(
                    query.Provider,
                    query.Environment,
                    _options.CurrentValue.AccountReference,
                    query.CorrelationId);
                if (configError is not null)
                {
                    return CertificateProviderOperationResult.Failure(configError);
                }

                if (!TryParseReference(query.ProviderOperationReference, "operation", query.Context, out var host))
                {
                    return CertificateProviderOperationResult.Failure(InvalidReferenceError(query.CorrelationId));
                }

                var route = await _edgeClient.GetDomainRouteStateAsync(new CaddyDomainRouteQuery(
                    query.Context.TenantId,
                    query.Context.DomainId,
                    host,
                    query.CorrelationId ?? string.Empty), cancellationToken);
                if (!route.Exists || !route.MatchesDesiredState)
                {
                    return CertificateProviderOperationResult.Failure(NormalizeEdgeFailure(
                        route.FailureCode ?? "CADDY_ROUTE_NOT_ADMITTED",
                        route.SafeFailureReason ?? "The hostname is not admitted at the Caddy edge.",
                        retryable: true,
                        query.CorrelationId));
                }

                var probe = await ProbeAsync(query.Context, host, query.CorrelationId, cancellationToken);
                if (!probe.Succeeded && !probe.Retryable)
                {
                    return CertificateProviderOperationResult.Failure(NormalizeProbeFailure(probe, query.CorrelationId));
                }

                var orderReference = BuildReference("order", host, query.Context);
                var authorizationReference = BuildReference("authorization", host, query.Context);
                return CertificateProviderOperationResult.Success(BuildOperationMetadata(
                    query.ProviderOperationReference,
                    orderReference,
                    authorizationReference,
                    probe));
            });
    }

    public Task<CertificateProviderRevocationResult> RevokeCertificateAsync(
        CertificateRevocationProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request.Validate();

        return ExecuteAsync(
            request.Context,
            request.Provider,
            request.Environment,
            "revocation",
            request.CorrelationId,
            cancellationToken,
            async () =>
            {
                var configError = ValidateEnabledConfiguration(
                    request.Provider,
                    request.Environment,
                    _options.CurrentValue.AccountReference,
                    request.CorrelationId);
                if (configError is not null)
                {
                    return CertificateProviderRevocationResult.Failure(configError);
                }

                if (request.CertificateIdentity is null)
                {
                    return CertificateProviderRevocationResult.Failure(CertificateProviderError.Create(
                        CertificateProviderErrorCategory.CertificateRejected,
                        "CA_REVOCATION_CERTIFICATE_IDENTITY_REQUIRED",
                        retryable: false,
                        safeMessage: "Certificate revocation requires exact safe certificate identity metadata.",
                        correlationId: request.CorrelationId));
                }

                request.CertificateIdentity.Validate();

                var controlResult = await _revocationControlClient.RevokeAsync(new CertificateRevocationControlRequest(
                    request.Context.TenantId,
                    request.Context.DomainId,
                    request.Context.CertificateId!.Value,
                    request.Context.OperationId,
                    request.Context.IdempotencyKey,
                    request.Provider,
                    request.Environment,
                    _options.CurrentValue.AccountReference,
                    request.ReasonCode,
                    request.ProviderCertificateReference,
                    request.EdgeCertificateReference,
                    request.CertificateIdentity,
                    request.CorrelationId), cancellationToken);

                if (!controlResult.Succeeded)
                {
                    return CertificateProviderRevocationResult.Failure(controlResult.Error!);
                }

                return string.Equals(controlResult.Status, "ALREADY_REVOKED", StringComparison.Ordinal)
                    ? CertificateProviderRevocationResult.AlreadyRevoked(controlResult.RevocationReference)
                    : CertificateProviderRevocationResult.Success(controlResult.RevocationReference);
            });
    }

    private Task<T> ExecuteAsync<T>(
        CertificateTenantOperationContext context,
        ProviderType provider,
        CertificateProviderEnvironment environment,
        string providerOperation,
        string? correlationId,
        CancellationToken cancellationToken,
        Func<T> action)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = context.TenantId,
            ["DomainId"] = context.DomainId,
            ["CertificateId"] = context.CertificateId,
            ["OperationId"] = context.OperationId,
            ["OperationType"] = context.OperationType.ToString(),
            ["IdempotencyKey"] = context.IdempotencyKey,
            ["Generation"] = context.Generation,
            ["ProviderType"] = provider.ToString(),
            ["ProviderEnvironment"] = environment.ToString(),
            ["CorrelationId"] = correlationId ?? string.Empty
        });

        var stopwatch = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = action();
            stopwatch.Stop();

            _logger.LogInformation(
                "Certificate provider foundation operation completed | ProviderOperation: {ProviderOperation} | DurationMs: {DurationMs} | Outcome: {Outcome}",
                providerOperation,
                stopwatch.ElapsedMilliseconds,
                "normalized");

            return Task.FromResult(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "Certificate provider foundation operation cancelled | ProviderOperation: {ProviderOperation} | DurationMs: {DurationMs}",
                providerOperation,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<T> ExecuteAsync<T>(
        CertificateTenantOperationContext context,
        ProviderType provider,
        CertificateProviderEnvironment environment,
        string providerOperation,
        string? correlationId,
        CancellationToken cancellationToken,
        Func<Task<T>> action)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = context.TenantId,
            ["DomainId"] = context.DomainId,
            ["CertificateId"] = context.CertificateId,
            ["OperationId"] = context.OperationId,
            ["OperationType"] = context.OperationType.ToString(),
            ["Generation"] = context.Generation,
            ["ProviderType"] = provider.ToString(),
            ["ProviderEnvironment"] = environment.ToString(),
            ["CorrelationId"] = correlationId ?? string.Empty,
        });

        var stopwatch = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await action();
            _logger.LogInformation(
                "Certificate provider edge operation completed | ProviderOperation: {ProviderOperation} | DurationMs: {DurationMs}",
                providerOperation,
                stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Certificate provider edge operation cancelled | ProviderOperation: {ProviderOperation} | DurationMs: {DurationMs}",
                providerOperation,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<EdgeCertificateProbeResult> ProbeAsync(
        CertificateTenantOperationContext context,
        string host,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (!context.CertificateId.HasValue || context.CertificateId.Value == Guid.Empty)
        {
            return new EdgeCertificateProbeResult(
                false,
                false,
                host,
                "CERTIFICATE_CONTEXT_INVALID",
                "Certificate identity is required for edge execution.",
                null);
        }

        return await _certificateProbe.ProbeAsync(new EdgeCertificateProbeRequest(
            context.TenantId,
            context.DomainId,
            context.CertificateId.Value,
            context.OperationId,
            host,
            ExpectedThumbprint: null,
            ExpectedFingerprint: null,
            ExpectedSerialNumber: null,
            ExpectedIssuer: null,
            ExpectedNotAfterUtc: null,
            correlationId ?? string.Empty), cancellationToken);
    }

    private static CertificateProviderOperationMetadata BuildOperationMetadata(
        string operationReference,
        string orderReference,
        string authorizationReference,
        EdgeCertificateProbeResult probe)
    {
        var certificate = probe.Certificate;
        var metadata = certificate is null
            ? null
            : new CertificateMetadata(
                certificate.Issuer,
                certificate.Subject,
                certificate.SubjectAlternativeNames,
                certificate.Thumbprint,
                certificate.Fingerprint,
                certificate.NotBeforeUtc,
                certificate.NotAfterUtc,
                certificate.SerialNumber,
                certificate.SignatureAlgorithm);

        return new CertificateProviderOperationMetadata(
            operationReference,
            operationReference,
            orderReference,
            Array.AsReadOnly(new[] { authorizationReference }),
            metadata,
            metadata is null ? CertificateProviderOperationStatus.Pending : CertificateProviderOperationStatus.Succeeded);
    }

    private static string BuildReference(string kind, string host, CertificateTenantOperationContext context) =>
        $"caddy-auto/v1/{kind}/{host}/{context.OperationId:N}/{context.Generation}";

    private static bool TryParseReference(
        string reference,
        string expectedKind,
        CertificateTenantOperationContext context,
        out string host)
    {
        host = string.Empty;
        var parts = reference.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 6 ||
            !string.Equals(parts[0], "caddy-auto", StringComparison.Ordinal) ||
            !string.Equals(parts[1], "v1", StringComparison.Ordinal) ||
            !string.Equals(parts[2], expectedKind, StringComparison.Ordinal) ||
            !Guid.TryParseExact(parts[4], "N", out var operationId) ||
            operationId != context.OperationId ||
            !int.TryParse(parts[5], out var generation) ||
            generation != context.Generation)
        {
            return false;
        }

        try
        {
            EdgeCertificateHostName.Validate(parts[3]);
            host = parts[3];
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static CertificateProviderError InvalidReferenceError(string? correlationId) =>
        CertificateProviderError.Create(
            CertificateProviderErrorCategory.InvalidState,
            "CADDY_OPERATION_REFERENCE_INVALID",
            retryable: false,
            safeMessage: "The Caddy-managed certificate operation reference is invalid.",
            correlationId: correlationId);

    private static CertificateProviderError NormalizeEdgeFailure(
        string? code,
        string? reason,
        bool retryable,
        string? correlationId) =>
        CertificateProviderError.Create(
            code is "CADDY_EDGE_DISABLED" or "CADDY_EDGE_CONFIGURATION_INVALID"
                ? CertificateProviderErrorCategory.ConfigurationError
                : CertificateProviderErrorCategory.ProviderUnavailable,
            code ?? "CADDY_EDGE_FAILURE",
            retryable,
            reason ?? "Caddy edge execution failed.",
            correlationId: correlationId);

    private static CertificateProviderError NormalizeProbeFailure(
        EdgeCertificateProbeResult probe,
        string? correlationId) =>
        CertificateProviderError.Create(
            probe.FailureCode == "SNI_NON_PUBLIC_ENDPOINT"
                ? CertificateProviderErrorCategory.DomainRejected
                : probe.FailureCode == "SNI_TLS_VALIDATION_FAILED"
                    ? CertificateProviderErrorCategory.ChallengeFailed
                    : CertificateProviderErrorCategory.NetworkFailure,
            probe.FailureCode ?? "SNI_PROBE_FAILED",
            probe.Retryable,
            probe.SafeFailureReason ?? "External certificate observation failed.",
            correlationId: correlationId);

    private CertificateProviderError? ValidateEnabledConfiguration(
        ProviderType provider,
        CertificateProviderEnvironment environment,
        string accountReference,
        string? correlationId)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return CertificateProviderError.Create(
                CertificateProviderErrorCategory.ConfigurationError,
                "CERTIFICATE_PROVIDER_DISABLED",
                retryable: false,
                safeMessage: "Certificate provider foundation is not enabled.",
                providerReference: accountReference,
                correlationId: correlationId);
        }

        if (provider != options.Provider || environment != options.Environment)
        {
            return CertificateProviderError.Create(
                CertificateProviderErrorCategory.ConfigurationError,
                "CERTIFICATE_PROVIDER_ENVIRONMENT_MISMATCH",
                retryable: false,
                safeMessage: "Certificate provider request does not match configured provider environment.",
                providerReference: accountReference,
                correlationId: correlationId);
        }

        if (string.IsNullOrWhiteSpace(options.AccountReference)
            || !string.Equals(options.AccountReference, accountReference, StringComparison.Ordinal))
        {
            return CertificateProviderError.Create(
                CertificateProviderErrorCategory.ConfigurationError,
                "CERTIFICATE_PROVIDER_ACCOUNT_MISMATCH",
                retryable: false,
                safeMessage: "Certificate provider account reference does not match configuration.",
                providerReference: accountReference,
                correlationId: correlationId);
        }

        var edgeValidation = new CaddyEdgeOptionsValidator().Validate(null, _edgeOptions.CurrentValue);
        if (!_edgeOptions.CurrentValue.Enabled || edgeValidation.Failed)
        {
            return CertificateProviderError.Create(
                CertificateProviderErrorCategory.ConfigurationError,
                "CADDY_EDGE_CONFIGURATION_INVALID",
                retryable: false,
                safeMessage: "Caddy edge execution is disabled or invalid.",
                correlationId: correlationId);
        }

        return null;
    }
}

public sealed class CertificateProviderHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<CertificateProviderOptions> _options;
    private readonly ICertificateRevocationControlClient? _revocationControlClient;

    public CertificateProviderHealthCheck(
        IOptionsMonitor<CertificateProviderOptions> options,
        ICertificateRevocationControlClient? revocationControlClient = null)
    {
        _options = options;
        _revocationControlClient = revocationControlClient;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Certificate provider execution is disabled."));
        }

        var validation = new CertificateProviderOptionsValidator().Validate(null, options);
        if (validation.Failed)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Certificate provider configuration is invalid: " +
                string.Join("; ", validation.Failures)));
        }

        if (!CaddyManagedAcmeCertificateProvider.SupportsLiveExecution)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                CaddyManagedAcmeCertificateProvider.SupportsLiveIssuance
                    ? "Caddy-managed issuance and status observation are available, but no authorized CA revocation control surface is configured."
                    : "The configured certificate provider adapter does not implement live issuance execution."));
        }

        if (!options.RevocationControl.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Caddy-managed issuance and status observation are available, but no authorized CA revocation control surface is configured."));
        }

        var controlValidationErrors = new List<string>();
        CertificateProviderOptionsValidator.ValidateRevocationControl(options.RevocationControl, controlValidationErrors);
        if (controlValidationErrors.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Certificate revocation control configuration is invalid: " +
                string.Join("; ", controlValidationErrors)));
        }

        if (_revocationControlClient is null)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Certificate revocation control client is not registered."));
        }

        return CheckRevocationControlHealthAsync(cancellationToken);
    }

    private async Task<HealthCheckResult> CheckRevocationControlHealthAsync(CancellationToken cancellationToken)
    {
        var control = await _revocationControlClient!.CheckHealthAsync(cancellationToken);
        return control.Available
            ? HealthCheckResult.Healthy("Certificate provider execution and revocation control are configured.")
            : HealthCheckResult.Unhealthy(control.SafeMessage);
    }
}

public static class CertificateProviderFoundationServiceCollectionExtensions
{
    public static IServiceCollection AddCertificateProviderFoundation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CertificateProviderOptions>()
            .Bind(configuration.GetSection(CertificateProviderOptions.SectionName));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CertificateProviderOptions>, CertificateProviderOptionsValidator>());

        services.AddHttpClient(HttpCertificateRevocationControlClient.HttpClientName);
        services.TryAddSingleton<ICertificateRevocationControlClient, HttpCertificateRevocationControlClient>();
        services.TryAddScoped<ICertificateProviderAccountSelector, CertificateProviderAccountSelector>();
        services.TryAddScoped<ICertificateProvider, CaddyManagedAcmeCertificateProvider>();
        services.TryAddSingleton<CertificateProviderHealthCheck>();

        return services;
    }
}
