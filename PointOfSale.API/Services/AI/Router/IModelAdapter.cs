namespace PointOfSale.API.Services.AI.Router;

/// <summary>
/// Response from a model adapter
/// </summary>
public class ModelResponse
{
    /// <summary>
    /// Whether the request succeeded
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// The response content (JSON string)
    /// </summary>
    public string? Content { get; set; }
    
    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// Response latency in milliseconds
    /// </summary>
    public int LatencyMs { get; set; }
    
    /// <summary>
    /// Tokens used for the request (if available)
    /// </summary>
    public int TokensUsed { get; set; }
    
    /// <summary>
    /// The model that was used (for internal tracking only)
    /// </summary>
    public string? ModelId { get; set; }
    
    /// <summary>
    /// Create a success response
    /// </summary>
    public static ModelResponse CreateSuccess(string content, int latencyMs, int tokensUsed, string modelId)
    {
        return new ModelResponse
        {
            Success = true,
            Content = content,
            LatencyMs = latencyMs,
            TokensUsed = tokensUsed,
            ModelId = modelId
        };
    }
    
    /// <summary>
    /// Create a failure response
    /// </summary>
    public static ModelResponse CreateFailure(string error, int latencyMs, string modelId)
    {
        return new ModelResponse
        {
            Success = false,
            Error = error,
            LatencyMs = latencyMs,
            ModelId = modelId
        };
    }
}

/// <summary>
/// Interface for model adapters.
/// Each adapter wraps a specific LLM provider/model.
/// 
/// PHASE 2 RULES:
/// - Adapters NEVER expose model identity to responses
/// - Adapters NEVER retry internally
/// - Adapters return structured ModelResponse
/// - The router decides fallback, not the adapter
/// </summary>
public interface IModelAdapter
{
    /// <summary>
    /// Unique identifier for this model (internal use only)
    /// </summary>
    string ModelId { get; }
    
    /// <summary>
    /// Display name (for logging/metrics only, never exposed to customer)
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// Priority (1 = highest priority, used first)
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// Whether this adapter is enabled and available
    /// </summary>
    bool IsEnabled { get; }
    
    /// <summary>
    /// Request timeout in milliseconds
    /// </summary>
    int TimeoutMs { get; }
    
    /// <summary>
    /// Send a request to the model.
    /// Does NOT retry internally - returns failure immediately.
    /// </summary>
    Task<ModelResponse> SendAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
