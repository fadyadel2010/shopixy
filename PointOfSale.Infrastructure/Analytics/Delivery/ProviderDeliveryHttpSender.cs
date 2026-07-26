using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public sealed class ProviderDeliveryHttpSender : IProviderDeliveryHttpSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IReadOnlyDictionary<string, IProviderDeliveryHttpRequestCustomizer> _customizers;
    private readonly ProviderDeliveryOptions _options;
    private readonly ILogger<ProviderDeliveryHttpSender> _logger;

    public ProviderDeliveryHttpSender(
        IHttpClientFactory httpClientFactory,
        IEnumerable<IProviderDeliveryHttpRequestCustomizer> customizers,
        IOptions<ProviderDeliveryOptions> options,
        ILogger<ProviderDeliveryHttpSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _customizers = customizers.ToDictionary(
            customizer => customizer.ProviderName,
            StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProviderDeliverySendResult> SendAsync(
        ProviderDeliveryOutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var activity = ProviderDeliveryTelemetry.ActivitySource.StartActivity(
                "ProviderDelivery.HttpSend",
                ActivityKind.Client);
            activity?.SetTag("provider", message.Provider);
            activity?.SetTag("tenant.id", message.TenantId);
            activity?.SetTag("provider_delivery.message_id", message.Id);

            using var request = new HttpRequestMessage(
                new HttpMethod(message.HttpMethod),
                ResolveEndpoint(message))
            {
                Content = new StringContent(message.PayloadJson, Encoding.UTF8, message.ContentType)
            };

            ApplyHeaders(message, request);
            if (_customizers.TryGetValue(message.Provider, out var customizer))
            {
                await customizer.CustomizeAsync(message, request, cancellationToken);
            }

            var client = _httpClientFactory.CreateClient(nameof(ProviderDeliveryHttpSender));
            client.Timeout = TimeSpan.FromSeconds(message.TimeoutSeconds ?? _options.HttpTimeoutSeconds);

            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            var retryAfter = response.Headers.RetryAfter?.Delta;
            if (!retryAfter.HasValue &&
                response.Headers.RetryAfter?.Date is { } retryAfterDate)
            {
                retryAfter = retryAfterDate.UtcDateTime - DateTime.UtcNow;
            }

            return response.IsSuccessStatusCode
                ? ProviderDeliverySendResult.Ok((int)response.StatusCode, responseBody, stopwatch.Elapsed)
                : ProviderDeliverySendResult.Fail(
                    (int)response.StatusCode,
                    responseBody,
                    $"HTTP {(int)response.StatusCode}",
                    stopwatch.Elapsed,
                    retryAfter is { } delta && delta > TimeSpan.Zero ? delta : null);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return ProviderDeliverySendResult.Fail(null, null, "provider request timeout", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Provider delivery send failed before provider response | MessageId: {MessageId} | Provider: {Provider}",
                message.Id,
                message.Provider);

            return ProviderDeliverySendResult.Fail(null, null, ex.Message, stopwatch.Elapsed);
        }
    }

    private static string ResolveEndpoint(ProviderDeliveryOutboxMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.EndpointUrl))
        {
            throw new InvalidOperationException(
                $"Provider delivery message {message.Id} does not contain an endpoint URL.");
        }

        return message.EndpointUrl;
    }

    private static void ApplyHeaders(
        ProviderDeliveryOutboxMessage message,
        HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(message.HeadersJson))
        {
            return;
        }

        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(message.HeadersJson, JsonOptions);
        if (headers == null)
        {
            return;
        }

        foreach (var (name, value) in headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }
    }
}
