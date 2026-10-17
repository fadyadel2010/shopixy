using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI.Router;

/// <summary>
/// Model router implementation with fallback logic.
/// 
/// PHASE 2 RULES:
/// - Routes to models by priority (1 = highest)
/// - Falls back to next model on failure
/// - NEVER exposes model identity to customer
/// - Returns safe response if ALL models fail
/// - Customer experience remains identical regardless of model
/// </summary>
public class ModelRouter : IModelRouter
{
    private readonly IEnumerable<IModelAdapter> _adapters;
    private readonly IAIPromptManager _promptManager;
    private readonly IAIResponseValidator _responseValidator;
    private readonly IAIRateLimiter _rateLimiter;
    private readonly ISafeResponseBuilder _safeResponseBuilder;
    private readonly CommerceAIOptions _options;
    private readonly ILogger<ModelRouter> _logger;
    
    public ModelRouter(
        IEnumerable<IModelAdapter> adapters,
        IAIPromptManager promptManager,
        IAIResponseValidator responseValidator,
        IAIRateLimiter rateLimiter,
        ISafeResponseBuilder safeResponseBuilder,
        IOptions<CommerceAIOptions> options,
        ILogger<ModelRouter> logger)
    {
        _adapters = adapters;
        _promptManager = promptManager;
        _responseValidator = responseValidator;
        _rateLimiter = rateLimiter;
        _safeResponseBuilder = safeResponseBuilder;
        _options = options.Value;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public async Task<ChatResponse> RouteAsync(
        CommerceContext context,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString();
        
        try
        {
            // Check rate limit
            var rateLimitResult = await _rateLimiter.CheckAsync(context.TenantId, cancellationToken);
            if (!rateLimitResult.Allowed)
            {
                _logger.LogWarning(
                    "Rate limit exceeded | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    context.TenantId, correlationId);
                return _safeResponseBuilder.CreateRateLimitedResponse();
            }
            
            // Build prompts
            var systemPrompt = _promptManager.GetSystemPrompt();
            var userPrompt = _promptManager.BuildUserPrompt(context);
            if (!AIProviderTransport.WithinPromptBudget(systemPrompt, userPrompt)) return _safeResponseBuilder.CreateFallbackResponse();
            
            // Get available adapters sorted by priority
            var availableAdapters = _adapters
                .Where(a => a.IsEnabled)
                .OrderBy(a => a.Priority)
                .ToList();
            
            if (availableAdapters.Count == 0)
            {
                _logger.LogError("No AI models configured or enabled");
                return _safeResponseBuilder.CreateFallbackResponse();
            }
            
            // PHASE 6.5: Clarified retry semantics
            // MaxRetries = total number of model attempts (not retries per model)
            int maxAttempts = _options.MaxRetries > 0 
                ? Math.Min(_options.MaxRetries, availableAdapters.Count)
                : availableAdapters.Count;
            
            _logger.LogDebug(
                "Starting AI routing | Available models: {ModelCount} | Max attempts: {MaxAttempts} | CorrelationId: {CorrelationId}",
                availableAdapters.Count, maxAttempts, correlationId);
            
            // Try each adapter in priority order up to maxAttempts
            int attemptCount = 0;
            
            foreach (var adapter in availableAdapters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attemptCount >= maxAttempts)
                    break;

                if (attemptCount > 0 && !(await _rateLimiter.CheckAsync(context.TenantId, cancellationToken)).Allowed)
                    return _safeResponseBuilder.CreateRateLimitedResponse();
                    
                attemptCount++;
                
                _logger.LogDebug(
                    "Attempting model | Model: {Model} | Attempt: {Attempt}/{Max} | CorrelationId: {CorrelationId}",
                    adapter.DisplayName, attemptCount, maxAttempts, correlationId);
                
                var modelResponse = await adapter.SendAsync(systemPrompt, userPrompt, cancellationToken);
                
                if (!modelResponse.Success)
                {
                    _logger.LogWarning(
                        "Model failed | Model: {Model} | Error: {Error} | Latency: {Latency}ms | CorrelationId: {CorrelationId}",
                        adapter.DisplayName, modelResponse.Error, modelResponse.LatencyMs, correlationId);
                    
                    // Continue to next model if fallback is enabled
                    if (_options.EnableFallback)
                        continue;
                    else
                        break;
                }

                await _rateLimiter.RecordRequestAsync(
                    context.TenantId,
                    modelResponse.TokensUsed,
                    modelResponse.ModelId ?? adapter.ModelId,
                    cancellationToken);
                
                // Validate response
                if (_responseValidator.TryValidate(modelResponse.Content!, context, out var chatResponse, out var validationError))
                {
                    _logger.LogInformation(
                        "AI request successful | Model: {Model} | Type: {Type} | Latency: {Latency}ms | Tokens: {Tokens} | CorrelationId: {CorrelationId}",
                        adapter.DisplayName, chatResponse!.Type, modelResponse.LatencyMs, modelResponse.TokensUsed, correlationId);
                    
                    return chatResponse;
                }
                else
                {
                    _logger.LogWarning(
                        "Response validation failed | Model: {Model} | Error: {Error} | CorrelationId: {CorrelationId}",
                        adapter.DisplayName, validationError, correlationId);
                    
                    // Try next model
                    if (_options.EnableFallback)
                        continue;
                    else
                        break;
                }
            }
            
            // All models failed
            _logger.LogError(
                "All AI models failed | Attempts: {Attempts} | CorrelationId: {CorrelationId}",
                attemptCount, correlationId);
            
            return _safeResponseBuilder.CreateFallbackResponse();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("AI router request cancelled | CorrelationId: {CorrelationId}", correlationId);
            return _safeResponseBuilder.CreateFallbackResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in AI router | CorrelationId: {CorrelationId}", correlationId);
            return _safeResponseBuilder.CreateFallbackResponse();
        }
    }
}
