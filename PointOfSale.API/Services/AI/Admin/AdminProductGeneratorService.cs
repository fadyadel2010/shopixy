using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.API.Services.AI.Router;

namespace PointOfSale.API.Services.AI.Admin;

public class AdminProductGeneratorService : IAdminProductGeneratorService
{
    private const string SystemPrompt = """
You are an ecommerce product copywriting assistant.

Generate structured product data for an online store.

Return JSON only.

Fields required:

name
shortDescription
description
brand
manufacturer
tags
metaTitle
metaDescription
metaKeywords
slug
features
faq

Rules:

Product name must be concise.
Description must be persuasive but factual.
Tags must be useful for search.
Slug must be URL friendly.
Meta description must be SEO optimized.

Do not include explanations.
Return JSON only.
""";

    private static readonly string[] RequiredFields =
    {
        "name",
        "shortDescription",
        "description",
        "brand",
        "manufacturer",
        "tags",
        "metaTitle",
        "metaDescription",
        "metaKeywords",
        "slug",
        "features",
        "faq"
    };

    private readonly HttpClient _httpClient;
    private readonly CommerceAIOptions _options;
    private readonly IAIRateLimiter _rateLimiter;
    private readonly ILogger<AdminProductGeneratorService> _logger;

    public AdminProductGeneratorService(
        HttpClient httpClient,
        IOptions<CommerceAIOptions> options,
        IAIRateLimiter rateLimiter,
        ILogger<AdminProductGeneratorService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<AdminAiGeneratedProductResponse> GenerateProductAsync(
        Guid tenantId,
        AdminAiProductGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString();

        if (!_options.Enabled)
        {
            throw new AdminProductGenerationException(
                StatusCodes.Status503ServiceUnavailable,
                "AI is disabled",
                "Commerce AI is currently unavailable for product generation.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.Endpoint)
            || string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new AdminProductGenerationException(
                StatusCodes.Status503ServiceUnavailable,
                "AI configuration missing",
                "Commerce AI is not fully configured for product generation.");
        }

        var rateLimitResult = await _rateLimiter.CheckAsync(tenantId, cancellationToken);
        if (!rateLimitResult.Allowed)
        {
            throw new AdminProductGenerationException(
                StatusCodes.Status429TooManyRequests,
                "Rate limit exceeded",
                "AI generation is temporarily busy for this workspace. Please try again shortly.",
                rateLimitResult.RetryAfterSeconds);
        }

        try
        {
            var userPrompt = BuildUserPrompt(request);
            var aiContent = await SendRequestAsync(userPrompt, correlationId, cancellationToken);
            var generatedProduct = ParseGeneratedProduct(aiContent);

            await _rateLimiter.RecordRequestAsync(
                tenantId,
                EstimateTokenUsage(userPrompt, aiContent),
                _options.Model,
                cancellationToken);

            _logger.LogInformation(
                "AI product generated | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId,
                correlationId);

            return generatedProduct;
        }
        catch (AdminProductGenerationException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "AI product generation timed out | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId,
                correlationId);

            throw new AdminProductGenerationException(
                StatusCodes.Status504GatewayTimeout,
                "AI timeout",
                "The AI request took too long. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "AI product generation request failed | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId,
                correlationId);

            throw new AdminProductGenerationException(
                StatusCodes.Status502BadGateway,
                "AI request failed",
                "The AI service could not be reached. Please try again.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "AI product generation returned malformed JSON | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId,
                correlationId);

            throw CreateInvalidJsonException();
        }
    }

    private async Task<string> SendRequestAsync(string userPrompt, string correlationId, CancellationToken cancellationToken)
    {
        var initialTimeoutSeconds = Math.Max(_options.TimeoutSeconds, 60);
        var initialMaxTokens = Math.Min(_options.MaxTokens, 900);
        var useStructuredResponseFormat = !_options.Provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase);

        try
        {
            return await SendRequestCoreAsync(
                userPrompt,
                correlationId,
                initialMaxTokens,
                initialTimeoutSeconds,
                useStructuredResponseFormat,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var retryTimeoutSeconds = Math.Max(initialTimeoutSeconds, 120);
            var retryMaxTokens = Math.Min(initialMaxTokens, 700);

            _logger.LogWarning(
                "Initial AI product generation attempt timed out, retrying with relaxed settings | Model: {Model} | CorrelationId: {CorrelationId}",
                _options.Model,
                correlationId);

            return await SendRequestCoreAsync(
                userPrompt,
                correlationId,
                retryMaxTokens,
                retryTimeoutSeconds,
                false,
                cancellationToken);
        }
    }

    private async Task<string> SendRequestCoreAsync(
        string userPrompt,
        string correlationId,
        int maxTokens,
        int timeoutSeconds,
        bool includeStructuredResponseFormat,
        CancellationToken cancellationToken)
    {
        object requestBody = includeStructuredResponseFormat
            ? new
            {
                model = _options.Model,
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = _options.Temperature,
                max_tokens = maxTokens,
                response_format = new { type = "json_object" }
            }
            : new
            {
                model = _options.Model,
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = _options.Temperature,
                max_tokens = maxTokens
            };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };

        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        if (_options.Provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            httpRequest.Headers.Add("HTTP-Referer", "https://pos.local");
            httpRequest.Headers.Add("X-Title", "CommerceAI");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        _logger.LogInformation(
            "Sending AI product generation request | Model: {Model} | TimeoutSeconds: {TimeoutSeconds} | MaxTokens: {MaxTokens} | StructuredOutput: {StructuredOutput} | CorrelationId: {CorrelationId}",
            _options.Model,
            timeoutSeconds,
            maxTokens,
            includeStructuredResponseFormat,
            correlationId);

        using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
        var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "AI product generation returned non-success | StatusCode: {StatusCode} | Model: {Model} | CorrelationId: {CorrelationId}",
                response.StatusCode,
                _options.Model,
                correlationId);

            throw new AdminProductGenerationException(
                StatusCodes.Status502BadGateway,
                "AI returned an error",
                "The AI service returned an unexpected response. Please try again.");
        }

        var content = ExtractContent(responseBody);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AdminProductGenerationException(
                StatusCodes.Status502BadGateway,
                "AI response empty",
                "The AI returned an empty response. Please try again.");
        }

        return content;
    }

    private static string BuildUserPrompt(AdminAiProductGenerationRequest request)
    {
        var payload = new
        {
            productIdea = request.ProductIdea.Trim(),
            language = string.IsNullOrWhiteSpace(request.Language) ? "Auto" : request.Language.Trim(),
            country = string.IsNullOrWhiteSpace(request.Country) ? "Auto" : request.Country.Trim(),
            tone = string.IsNullOrWhiteSpace(request.Tone) ? "marketing" : request.Tone.Trim(),
            featureCount = request.FeatureCount,
            faqCount = request.FaqCount,
            faqFormat = new
            {
                question = "string",
                answer = "string"
            }
        };

        return """
Generate product content from this request:
""" + Environment.NewLine + JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private AdminAiGeneratedProductResponse ParseGeneratedProduct(string rawContent)
    {
        var cleanedJson = StripMarkdownCodeFence(rawContent);

        try
        {
            using var document = JsonDocument.Parse(cleanedJson);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw CreateInvalidJsonException();
            }

            foreach (var requiredField in RequiredFields)
            {
                if (!root.TryGetProperty(requiredField, out _))
                {
                    throw CreateInvalidJsonException($"The AI response did not include the required '{requiredField}' field.");
                }
            }

            return new AdminAiGeneratedProductResponse
            {
                Name = ReadRequiredString(root, "name"),
                ShortDescription = ReadRequiredString(root, "shortDescription"),
                Description = ReadRequiredString(root, "description"),
                Brand = ReadRequiredString(root, "brand"),
                Manufacturer = ReadRequiredString(root, "manufacturer"),
                Tags = ReadStringList(root, "tags"),
                MetaTitle = ReadRequiredString(root, "metaTitle"),
                MetaDescription = ReadRequiredString(root, "metaDescription"),
                MetaKeywords = ReadStringList(root, "metaKeywords"),
                Slug = SanitizeSlug(ReadRequiredString(root, "slug")),
                Features = ReadStringList(root, "features"),
                Faq = ReadFaqList(root, "faq")
            };
        }
        catch (JsonException)
        {
            throw CreateInvalidJsonException();
        }
    }

    private static string? ExtractContent(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }
        }

        if (root.TryGetProperty("content", out var directContent)
            && directContent.ValueKind == JsonValueKind.String)
        {
            return directContent.GetString();
        }

        if (root.TryGetProperty("response", out var response)
            && response.ValueKind == JsonValueKind.String)
        {
            return response.GetString();
        }

        return null;
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        var element = root.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw CreateInvalidJsonException($"The AI response field '{propertyName}' was not a string.");
        }

        return element.GetString()?.Trim() ?? string.Empty;
    }

    private static List<string> ReadStringList(JsonElement root, string propertyName)
    {
        var element = root.GetProperty(propertyName);

        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString()?
                .Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();
        }

        throw CreateInvalidJsonException($"The AI response field '{propertyName}' was not an array.");
    }

    private static List<AdminAiGeneratedFaqItemDto> ReadFaqList(JsonElement root, string propertyName)
    {
        var element = root.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw CreateInvalidJsonException("The AI response field 'faq' was not an array.");
        }

        var items = new List<AdminAiGeneratedFaqItemDto>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                var question = item.TryGetProperty("question", out var questionElement) && questionElement.ValueKind == JsonValueKind.String
                    ? questionElement.GetString()?.Trim() ?? string.Empty
                    : string.Empty;
                var answer = item.TryGetProperty("answer", out var answerElement) && answerElement.ValueKind == JsonValueKind.String
                    ? answerElement.GetString()?.Trim() ?? string.Empty
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(question) || !string.IsNullOrWhiteSpace(answer))
                {
                    items.Add(new AdminAiGeneratedFaqItemDto
                    {
                        Question = question,
                        Answer = answer
                    });
                }
            }
            else if (item.ValueKind == JsonValueKind.String)
            {
                var question = item.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(question))
                {
                    items.Add(new AdminAiGeneratedFaqItemDto
                    {
                        Question = question
                    });
                }
            }
        }

        return items;
    }

    private static string StripMarkdownCodeFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var lines = trimmed.Split('\n').ToList();
        if (lines.Count > 0)
        {
            lines.RemoveAt(0);
        }

        if (lines.Count > 0 && lines[^1].Trim().StartsWith("```", StringComparison.Ordinal))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines).Trim();
    }

    private static string SanitizeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return string.Empty;
        }

        var normalized = slug.Trim().ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        var lastWasDash = false;

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static int EstimateTokenUsage(string prompt, string response)
    {
        return Math.Max(1, (prompt.Length + response.Length) / 4);
    }

    private static AdminProductGenerationException CreateInvalidJsonException(string? message = null)
    {
        return new AdminProductGenerationException(
            StatusCodes.Status502BadGateway,
            "Invalid AI JSON",
            message ?? "The AI returned invalid product JSON. Please try again.");
    }
}

public class AdminProductGenerationException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public string UserMessage { get; }
    public int? RetryAfterSeconds { get; }

    public AdminProductGenerationException(
        int statusCode,
        string errorCode,
        string userMessage,
        int? retryAfterSeconds = null)
        : base(userMessage)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        UserMessage = userMessage;
        RetryAfterSeconds = retryAfterSeconds;
    }
}
