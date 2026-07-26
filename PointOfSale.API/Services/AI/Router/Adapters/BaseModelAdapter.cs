using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PointOfSale.API.Services.AI.Router.Adapters;

/// <summary>
/// Base model adapter with common HTTP request logic.
/// All specific adapters inherit from this.
/// </summary>
public abstract class BaseModelAdapter : IModelAdapter
{
    protected readonly HttpClient HttpClient;
    protected readonly ILogger Logger;
    
    public abstract string ModelId { get; }
    public abstract string DisplayName { get; }
    public abstract int Priority { get; }
    public abstract bool IsEnabled { get; }
    public abstract int TimeoutMs { get; }
    
    protected abstract string Endpoint { get; }
    protected abstract string ApiKey { get; }
    protected abstract int MaxTokens { get; }
    protected abstract double Temperature { get; }
    
    protected BaseModelAdapter(HttpClient httpClient, ILogger logger)
    {
        HttpClient = httpClient;
        Logger = logger;
    }
    
    /// <inheritdoc />
    public async Task<ModelResponse> SendAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            if (!IsEnabled || string.IsNullOrEmpty(ApiKey))
            {
                return ModelResponse.CreateFailure("Model not configured", 0, ModelId);
            }
            
            // Build request body (OpenAI-compatible format)
            var requestBody = new
            {
                model = ModelId,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = Temperature,
                max_tokens = MaxTokens,
                response_format = new { type = "json_object" }
            };
            
            var jsonContent = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            
            // Configure request
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Content = httpContent;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            
            // Add OpenRouter-specific headers
            request.Headers.Add("HTTP-Referer", "https://pos.local");
            request.Headers.Add("X-Title", "CommerceAI");
            
            // Create timeout token
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(TimeoutMs));
            
            // Send request
            var response = await HttpClient.SendAsync(request, cts.Token);
            stopwatch.Stop();
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.LogWarning(
                    "Model request failed | Model: {Model} | Status: {Status} | Error: {Error}",
                    ModelId, response.StatusCode, errorBody);
                    
                return ModelResponse.CreateFailure(
                    $"HTTP {(int)response.StatusCode}",
                    (int)stopwatch.ElapsedMilliseconds,
                    ModelId);
            }
            
            // Parse response
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var content = ParseResponse(responseBody, out var tokensUsed);
            
            if (string.IsNullOrEmpty(content))
            {
                return ModelResponse.CreateFailure(
                    "Empty response",
                    (int)stopwatch.ElapsedMilliseconds,
                    ModelId);
            }
            
            Logger.LogDebug(
                "Model request succeeded | Model: {Model} | Latency: {Latency}ms | Tokens: {Tokens}",
                ModelId, stopwatch.ElapsedMilliseconds, tokensUsed);
            
            return ModelResponse.CreateSuccess(
                content,
                (int)stopwatch.ElapsedMilliseconds,
                tokensUsed,
                ModelId);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            Logger.LogWarning(
                "Model request timed out | Model: {Model} | Timeout: {Timeout}ms",
                ModelId, TimeoutMs);
                
            return ModelResponse.CreateFailure(
                "Timeout",
                (int)stopwatch.ElapsedMilliseconds,
                ModelId);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Model HTTP request failed | Model: {Model}", ModelId);
            
            return ModelResponse.CreateFailure(
                "Connection error",
                (int)stopwatch.ElapsedMilliseconds,
                ModelId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Unexpected model error | Model: {Model}", ModelId);
            
            return ModelResponse.CreateFailure(
                "Internal error",
                (int)stopwatch.ElapsedMilliseconds,
                ModelId);
        }
    }
    
    /// <summary>
    /// Parse the response content from the API response body.
    /// </summary>
    protected virtual string? ParseResponse(string responseBody, out int tokensUsed)
    {
        tokensUsed = 0;
        
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            
            // Extract usage info
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("total_tokens", out var tokens))
                {
                    tokensUsed = tokens.GetInt32();
                }
            }
            
            // Standard OpenAI format: { choices: [{ message: { content: "..." } }] }
            if (root.TryGetProperty("choices", out var choices) && 
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString();
                }
            }
            
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
