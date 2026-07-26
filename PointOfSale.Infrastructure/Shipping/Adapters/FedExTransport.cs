using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

public sealed record FedExTransportRequest<TRequest>(
    HttpMethod Method,
    string EndpointPath,
    TRequest Body,
    FedExOAuthRequest OAuthRequest,
    bool UseSandbox,
    string? CorrelationId = null);

public sealed record FedExTransportResponse(
    HttpStatusCode StatusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    string RawJson,
    string CorrelationId,
    Uri RequestUri,
    TimeSpan Duration,
    int RetryCount);

public sealed record FedExTransportError(
    string ErrorCode,
    string ErrorMessage,
    HttpStatusCode? StatusCode,
    bool IsTransient);

public sealed record FedExTransportResult(
    bool Success,
    FedExTransportResponse? Response,
    FedExTransportError? Error);

public interface IFedExTransportClient
{
    Task<FedExTransportResult> SendJsonAsync<TRequest>(
        FedExTransportRequest<TRequest> request,
        CancellationToken cancellationToken = default);
}

public sealed class FedExTransportClient : IFedExTransportClient
{
    public const string UnsupportedRequestCode = "FEDEX_TRANSPORT_UNSUPPORTED_REQUEST";
    public const string AuthenticationUnavailableCode = "FEDEX_TRANSPORT_AUTHENTICATION_UNAVAILABLE";
    public const string TimeoutCode = "FEDEX_TRANSPORT_TIMEOUT";
    public const string NetworkFailureCode = "FEDEX_TRANSPORT_NETWORK_FAILURE";

    private readonly FedExOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFedExOAuthClient _oauthClient;
    private readonly ILogger<FedExTransportClient> _logger;

    public FedExTransportClient(
        FedExOptions options,
        IHttpClientFactory httpClientFactory,
        IFedExOAuthClient oauthClient,
        ILogger<FedExTransportClient> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _oauthClient = oauthClient;
        _logger = logger;
    }

    public async Task<FedExTransportResult> SendJsonAsync<TRequest>(
        FedExTransportRequest<TRequest> request,
        CancellationToken cancellationToken = default)
    {
        if (!IsCertifiedRequestDto(request.Body))
        {
            return Failure(
                UnsupportedRequestCode,
                "FedEx transport accepts only certified FedEx DTOs.",
                statusCode: null,
                isTransient: false);
        }

        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId.Trim();
        var requestUri = _options.GetTransportUri(request.UseSandbox, request.EndpointPath);
        var attempts = Math.Max(0, _options.TransportMaxRetryCount) + 1;
        var retryCount = 0;
        var refreshedAfterUnauthorized = false;
        var authorization = await _oauthClient.GetAuthorizationHeaderAsync(
            request.OAuthRequest,
            request.UseSandbox,
            cancellationToken);

        if (!authorization.Success)
        {
            return Failure(
                AuthenticationUnavailableCode,
                authorization.ErrorMessage ?? "FedEx authorization header could not be generated.",
                statusCode: null,
                isTransient: false);
        }

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                using var httpRequest = CreateRequest(request, requestUri, correlationId, authorization.AuthorizationHeader!);
                var client = _httpClientFactory.CreateClient(_options.TransportHttpClientName);
                using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var duration = Stopwatch.GetElapsedTime(started);
                var transportResponse = new FedExTransportResponse(
                    response.StatusCode,
                    CaptureHeaders(response),
                    rawJson,
                    correlationId,
                    requestUri,
                    duration,
                    retryCount);

                LogTransportAttempt(correlationId, requestUri, duration, response.StatusCode, retryCount);

                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshedAfterUnauthorized)
                {
                    refreshedAfterUnauthorized = true;
                    authorization = await _oauthClient.RefreshAuthorizationHeaderAfterUnauthorizedAsync(
                        request.OAuthRequest,
                        request.UseSandbox,
                        cancellationToken);
                    if (!authorization.Success)
                    {
                        return Failure(
                            AuthenticationUnavailableCode,
                            authorization.ErrorMessage ?? "FedEx authorization refresh failed after unauthorized response.",
                            response.StatusCode,
                            isTransient: false,
                            transportResponse);
                    }

                    retryCount++;
                    continue;
                }

                if (IsRetryableStatus(response.StatusCode) && attempt < attempts)
                {
                    retryCount++;
                    continue;
                }

                return response.IsSuccessStatusCode
                    ? new FedExTransportResult(true, transportResponse, null)
                    : Failure(
                        $"FEDEX_TRANSPORT_HTTP_{(int)response.StatusCode}",
                        "FedEx HTTP response was returned without transport-level success.",
                        response.StatusCode,
                        IsRetryableStatus(response.StatusCode),
                        transportResponse);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
                LogTransportAttempt(correlationId, requestUri, Stopwatch.GetElapsedTime(started), null, retryCount);
                retryCount++;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var duration = Stopwatch.GetElapsedTime(started);
                LogTransportAttempt(correlationId, requestUri, duration, null, retryCount);
                return Failure(TimeoutCode, "FedEx HTTP transport timed out.", null, isTransient: true);
            }
            catch (HttpRequestException) when (attempt < attempts)
            {
                LogTransportAttempt(correlationId, requestUri, Stopwatch.GetElapsedTime(started), null, retryCount);
                retryCount++;
            }
            catch (HttpRequestException ex)
            {
                LogTransportAttempt(correlationId, requestUri, Stopwatch.GetElapsedTime(started), null, retryCount);
                return Failure(NetworkFailureCode, ex.Message, null, isTransient: true);
            }
        }

        return Failure(NetworkFailureCode, "FedEx HTTP transport failed after transient retries.", null, isTransient: true);
    }

    private static HttpRequestMessage CreateRequest<TRequest>(
        FedExTransportRequest<TRequest> request,
        Uri requestUri,
        string correlationId,
        AuthenticationHeaderValue authorizationHeader)
    {
        var httpRequest = new HttpRequestMessage(request.Method, requestUri)
        {
            Content = new StringContent(FedExContractSerializer.SerializeJson(request.Body))
        };
        httpRequest.Headers.Authorization = authorizationHeader;
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Headers.TryAddWithoutValidation("x-customer-transaction-id", correlationId);
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return httpRequest;
    }

    private static bool IsCertifiedRequestDto<TRequest>(TRequest request) =>
        request is FedExShipmentRequest ||
        request is FedExTrackingNumbersRequest ||
        request is FedExPickupAvailabilityRequest ||
        request is FedExCreatePickupRequest ||
        request is FedExAddressValidationRequest ||
        request is FedExServiceAvailabilityRequest;

    private static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode == 429 ||
        (int)statusCode >= 500;

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CaptureHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            headers[header.Key] = header.Value.ToList();
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = header.Value.ToList();
        }

        return headers;
    }

    private static FedExTransportResult Failure(
        string errorCode,
        string errorMessage,
        HttpStatusCode? statusCode,
        bool isTransient,
        FedExTransportResponse? response = null) =>
        new(false, response, new FedExTransportError(errorCode, errorMessage, statusCode, isTransient));

    private void LogTransportAttempt(
        string correlationId,
        Uri requestUri,
        TimeSpan duration,
        HttpStatusCode? statusCode,
        int retryCount)
    {
        _logger.LogInformation(
            "FedEx HTTP request completed. CorrelationId={CorrelationId} Uri={Uri} DurationMs={DurationMs} RetryCount={RetryCount} HttpStatus={HttpStatus}",
            correlationId,
            requestUri,
            duration.TotalMilliseconds,
            retryCount,
            statusCode.HasValue ? (int)statusCode.Value : 0);
    }
}
