using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces;
using Polly;
using Polly.Extensions.Http;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// n8n-based recovery orchestrator.
/// Calls n8n webhook to trigger abandoned cart recovery workflow.
/// Phase: Smart Abandoned Cart Recovery Engine - Phase 2
/// </summary>
public class N8nRecoveryOrchestrator : IRecoveryOrchestrator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ITenantAutomationSettingsService _settingsService;
    private readonly ILogger<N8nRecoveryOrchestrator> _logger;
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);
    
    private const int MaxRetries = 3;
    private const int TimeoutSeconds = 5;

    public N8nRecoveryOrchestrator(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ITenantAutomationSettingsService settingsService,
        ILogger<N8nRecoveryOrchestrator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<bool> TriggerRecoveryWorkflowAsync(
        Guid tenantId,
        Guid abandonedCartId,
        Guid cartId,
        string? sessionId,
        Guid? customerId,
        string? phoneNumber,
        string? email,
        string? contactChannel,
        string cartSnapshotJson,
        int firstMessageDelayMinutes,
        int secondMessageDelayMinutes,
        TimeSpan workingHoursFrom,
        TimeSpan workingHoursTo,
        string storeName,
        string storefrontBaseUrl,
        string recoveryLink,
        CancellationToken ct = default)
    {
        var webhookUrl = _configuration["AbandonedCart:N8nWebhookUrl"];
        
        if (string.IsNullOrEmpty(webhookUrl))
        {
            _logger.LogError("[Recovery Orchestrator] n8n webhook URL not configured");
            return false;
        }

        var settings = await _settingsService.GetSettingsAsync(tenantId, ct);
        var webhookSecret = settings.N8nWebhookApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            _logger.LogError(
                "[Recovery Orchestrator] n8n webhook signing secret not configured | TenantId: {TenantId} | CartId: {CartId} | AbandonedCartId: {AbandonedCartId}",
                tenantId,
                cartId,
                abandonedCartId);
            return false;
        }

        // Extract host for safe logging (avoid leaking URL secrets)
        string webhookHost;
        try
        {
            var uri = new Uri(webhookUrl);
            webhookHost = uri.Host;
        }
        catch
        {
            webhookHost = "[invalid-url]";
        }

        var triggeredAtUtc = DateTime.UtcNow;
        var payload = new
        {
            tenantId,
            abandonedCartId,
            cartId,
            sessionId,
            customerId,
            phoneNumber,
            email,
            contactChannel,
            cartSnapshot = JsonSerializer.Deserialize<object>(cartSnapshotJson),
            firstMessageDelayMinutes,
            secondMessageDelayMinutes,
            workingHoursFrom = workingHoursFrom.ToString(@"hh\:mm"),
            workingHoursTo = workingHoursTo.ToString(@"hh\:mm"),
            storeName,
            storefrontBaseUrl,
            recoveryLink,
            triggeredAtUtc
        };
        var payloadJson = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        var idempotencyKey = BuildIdempotencyKey(tenantId, abandonedCartId, cartId);

        try
        {
            var client = _httpClientFactory.CreateClient("N8nRecovery");
            client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);

            // Retry policy: 3 attempts with exponential backoff
            var retryPolicy = HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(
                    MaxRetries,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                _logger.LogWarning(
                    "[Recovery Orchestrator] Retry {RetryCount}/{MaxRetries} | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | ContactChannel: {ContactChannel} | Reason: {Reason}",
                    retryCount,
                    MaxRetries,
                    tenantId,
                    cartId,
                    sessionId,
                    contactChannel,
                    outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                    });

            var response = await retryPolicy.ExecuteAsync(async () =>
            {
                _logger.LogDebug(
                    "[Recovery Orchestrator] Calling n8n webhook | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | ContactChannel: {ContactChannel} | Host: {Host}",
                    tenantId,
                    cartId,
                    sessionId,
                    contactChannel,
                    webhookHost);

                using var request = BuildSignedRequest(
                    webhookUrl,
                    payloadJson,
                    webhookSecret,
                    tenantId,
                    abandonedCartId,
                    cartId,
                    idempotencyKey);

                return await client.SendAsync(request, ct);
            });

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "[Recovery Orchestrator] Webhook accepted | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | ContactChannel: {ContactChannel} | StatusCode: {StatusCode}",
                    tenantId,
                    cartId,
                    sessionId,
                    contactChannel,
                    response.StatusCode);
                return true;
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                // Scrub response body to avoid leaking sensitive data - log only length and truncated preview
                var scrubbedResponse = responseBody.Length > 100 
                    ? $"{responseBody.Substring(0, 100)}... [truncated, total length: {responseBody.Length}]"
                    : responseBody;
                
                _logger.LogError(
                    "[Recovery Orchestrator] Webhook rejected | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | ContactChannel: {ContactChannel} | Host: {Host} | StatusCode: {StatusCode} | ResponseLength: {ResponseLength} | ResponsePreview: {ResponsePreview}",
                    tenantId,
                    cartId,
                    sessionId,
                    contactChannel,
                    webhookHost,
                    response.StatusCode,
                    responseBody.Length,
                    scrubbedResponse);
                return false;
            }
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex,
                "[Recovery Orchestrator] Webhook timeout | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | ContactChannel: {ContactChannel}",
                tenantId,
                cartId,
                sessionId,
                contactChannel);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Recovery Orchestrator] Webhook error | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | ContactChannel: {ContactChannel}",
                tenantId,
                cartId,
                sessionId,
                contactChannel);
            return false;
        }
    }

    private static HttpRequestMessage BuildSignedRequest(
        string webhookUrl,
        string payloadJson,
        string webhookSecret,
        Guid tenantId,
        Guid abandonedCartId,
        Guid cartId,
        string idempotencyKey)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N");
        var signatureBase = string.Join(
            ".",
            tenantId.ToString("D"),
            abandonedCartId.ToString("D"),
            cartId.ToString("D"),
            timestamp,
            nonce,
            idempotencyKey,
            payloadJson);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureBase));
        var signature = Convert.ToHexString(signatureBytes).ToLowerInvariant();

        var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("X-Recovery-Signature-Version", "v1");
        request.Headers.TryAddWithoutValidation("X-Recovery-Signature", $"sha256={signature}");
        request.Headers.TryAddWithoutValidation("X-Recovery-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Recovery-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("X-Recovery-Tenant-Id", tenantId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Recovery-Abandoned-Cart-Id", abandonedCartId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Recovery-Cart-Id", cartId.ToString("D"));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return request;
    }

    private static string BuildIdempotencyKey(Guid tenantId, Guid abandonedCartId, Guid cartId) =>
        $"recovery-trigger:{tenantId:D}:{abandonedCartId:D}:{cartId:D}";
}
