using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PointOfSale.Infrastructure.Eventing;

public sealed record MarketplaceDeadLetterAlert(
    string ConsumerName,
    string EventType,
    Guid EventId,
    string? CorrelationId,
    string Error,
    DateTime DetectedAtUtc);

public interface IMarketplaceEventAlertHook
{
    Task NotifyDeadLetterAsync(MarketplaceDeadLetterAlert alert, CancellationToken cancellationToken = default);
}

public sealed class LoggingMarketplaceEventAlertHook : IMarketplaceEventAlertHook
{
    private readonly ILogger<LoggingMarketplaceEventAlertHook> _logger;

    public LoggingMarketplaceEventAlertHook(ILogger<LoggingMarketplaceEventAlertHook> logger)
    {
        _logger = logger;
    }

    public Task NotifyDeadLetterAsync(MarketplaceDeadLetterAlert alert, CancellationToken cancellationToken = default)
    {
        _logger.LogError(
            "Marketplace dead-letter alert | Consumer: {ConsumerName} | EventType: {EventType} | EventId: {EventId} | CorrelationId: {CorrelationId} | Error: {Error}",
            alert.ConsumerName,
            alert.EventType,
            alert.EventId,
            alert.CorrelationId,
            alert.Error);

        return Task.CompletedTask;
    }
}

public sealed class WebhookMarketplaceEventAlertHook : IMarketplaceEventAlertHook
{
    private readonly HttpClient? _httpClient;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IOptions<MarketplaceEventAlertingOptions> _options;
    private readonly ILogger<WebhookMarketplaceEventAlertHook> _logger;

    // Legacy constructor for backward compatibility (captures HttpClient at registration - not recommended)
    public WebhookMarketplaceEventAlertHook(
        HttpClient httpClient,
        IOptions<MarketplaceEventAlertingOptions> options,
        ILogger<WebhookMarketplaceEventAlertHook> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    // Preferred constructor that uses IHttpClientFactory for proper handler rotation and DNS refresh
    public WebhookMarketplaceEventAlertHook(
        IHttpClientFactory httpClientFactory,
        IOptions<MarketplaceEventAlertingOptions> options,
        ILogger<WebhookMarketplaceEventAlertHook> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyDeadLetterAsync(MarketplaceDeadLetterAlert alert, CancellationToken cancellationToken = default)
    {
        var webhookUrl = _options.Value.WebhookUrl;
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return;
        }

        // Create per-request timeout using CancellationTokenSource to avoid modifying _httpClient.Timeout
        var timeoutSeconds = Math.Clamp(_options.Value.HttpTimeoutSeconds, 2, 120);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(alert), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_options.Value.WebhookBearerToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Value.WebhookBearerToken);
        }

        try
        {
            // Use factory to create client per operation for proper handler rotation, or fall back to captured client
            var httpClient = _httpClientFactory?.CreateClient(nameof(WebhookMarketplaceEventAlertHook)) ?? _httpClient!;
            using var response = await httpClient.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Marketplace dead-letter webhook failed | StatusCode: {StatusCode} | Consumer: {ConsumerName} | EventType: {EventType} | EventId: {EventId}",
                    (int)response.StatusCode,
                    alert.ConsumerName,
                    alert.EventType,
                    alert.EventId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Marketplace dead-letter webhook threw exception | Consumer: {ConsumerName} | EventType: {EventType} | EventId: {EventId}",
                alert.ConsumerName,
                alert.EventType,
                alert.EventId);
        }
    }
}

public sealed class CompositeMarketplaceEventAlertHook : IMarketplaceEventAlertHook
{
    private readonly LoggingMarketplaceEventAlertHook _loggingHook;
    private readonly WebhookMarketplaceEventAlertHook _webhookHook;

    public CompositeMarketplaceEventAlertHook(
        LoggingMarketplaceEventAlertHook loggingHook,
        WebhookMarketplaceEventAlertHook webhookHook)
    {
        _loggingHook = loggingHook;
        _webhookHook = webhookHook;
    }

    public async Task NotifyDeadLetterAsync(MarketplaceDeadLetterAlert alert, CancellationToken cancellationToken = default)
    {
        await _loggingHook.NotifyDeadLetterAsync(alert, cancellationToken);
        await _webhookHook.NotifyDeadLetterAsync(alert, cancellationToken);
    }
}
