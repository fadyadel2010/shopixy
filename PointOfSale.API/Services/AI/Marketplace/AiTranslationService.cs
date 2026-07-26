using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.API.Services.AI.Router;

namespace PointOfSale.API.Services.AI.Marketplace;

public sealed class AiTranslationService : IAiTranslationService
{
    private const string SystemPrompt = "You are a professional e-commerce translator.";
    private static readonly SemaphoreSlim TranslationConcurrencyGate = new(2, 2);

    private readonly HttpClient _httpClient;
    private readonly CommerceAIOptions _options;
    private readonly IAIRateLimiter _rateLimiter;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AiTranslationService> _logger;

    public AiTranslationService(
        HttpClient httpClient,
        IOptions<CommerceAIOptions> options,
        IAIRateLimiter rateLimiter,
        IMemoryCache cache,
        ILogger<AiTranslationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _rateLimiter = rateLimiter;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> TranslateAsync(
        string input,
        string from,
        string to,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        EnsureConfigured();

        var cacheKey = $"marketplace-translation:{from}:{to}:{input.Trim().ToLowerInvariant()}";
        if (_options.EnableCaching &&
            _cache.TryGetValue(cacheKey, out string? cachedTranslation) &&
            !string.IsNullOrWhiteSpace(cachedTranslation))
        {
            return cachedTranslation;
        }

        var rateLimitResult = await _rateLimiter.CheckAsync(Guid.Empty, cancellationToken);
        if (!rateLimitResult.Allowed)
        {
            throw new InvalidOperationException("AI translation is temporarily rate limited. Please try again shortly.");
        }

        var prompt = BuildPrompt(input, from, to);
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model = _options.Model,
                    messages = new[]
                    {
                        new { role = "system", content = SystemPrompt },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.1,
                    max_tokens = Math.Min(_options.MaxTokens, 1000)
                }),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        if (_options.Provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("HTTP-Referer", "https://pos.local");
            request.Headers.Add("X-Title", "CommerceAI");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(15, _options.TimeoutSeconds)));

        await TranslationConcurrencyGate.WaitAsync(timeoutCts.Token);

        try
        {
            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Marketplace translation request failed | StatusCode: {StatusCode} | From: {From} | To: {To}",
                    response.StatusCode,
                    from,
                    to);

                throw new InvalidOperationException("The AI translation service returned an unexpected response.");
            }

            var content = ExtractContent(responseBody);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("The AI translation service returned an empty response.");
            }

            var normalizedTranslation = content.Trim().Trim('"');
            await _rateLimiter.RecordRequestAsync(
                Guid.Empty,
                Math.Max(1, (prompt.Length + normalizedTranslation.Length) / 4),
                _options.Model,
                cancellationToken);

            if (_options.EnableCaching)
            {
                _cache.Set(
                    cacheKey,
                    normalizedTranslation,
                    TimeSpan.FromSeconds(Math.Max(60, _options.CacheDurationSeconds)));
            }

            return normalizedTranslation;
        }
        finally
        {
            TranslationConcurrencyGate.Release();
        }
    }

    private void EnsureConfigured()
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Commerce AI is currently unavailable for translation.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Endpoint) ||
            string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("Commerce AI is not fully configured for translation.");
        }
    }

    private static string BuildPrompt(string input, string from, string to)
    {
        return $"""
Translate the following text from {from} to {to}.

Rules:
- Keep meaning accurate
- Use natural Arabic
- Do not translate brand names
- Keep formatting
- No explanations

Text:
{input}
""";
    }

    private static string? ExtractContent(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }
        }

        return null;
    }
}
