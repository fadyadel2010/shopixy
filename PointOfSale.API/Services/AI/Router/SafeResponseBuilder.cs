using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI.Router;

/// <summary>
/// Builds safe fallback responses when all models fail.
/// 
/// PHASE 2 RULES:
/// - NEVER mention errors, models, or technical issues
/// - NEVER apologize for system behavior
/// - Return polite, customer-friendly message
/// - Ask customer to repeat their request
/// </summary>
public interface ISafeResponseBuilder
{
    /// <summary>
    /// Create a safe fallback response.
    /// Used when:
    /// - All models fail
    /// - Response validation fails
    /// - Timeout occurs
    /// - Context is too large
    /// </summary>
    ChatResponse CreateFallbackResponse();
    
    /// <summary>
    /// Create a rate-limited response.
    /// Used when tenant exceeds rate limits.
    /// NOTE: Does NOT mention rate limits to customer.
    /// </summary>
    ChatResponse CreateRateLimitedResponse();
}

/// <summary>
/// Implementation of safe response builder.
/// </summary>
public class SafeResponseBuilder : ISafeResponseBuilder
{
    /// <inheritdoc />
    public ChatResponse CreateFallbackResponse()
    {
        return new ChatResponse
        {
            Type = ChatResponseTypes.Message,
            Message = "I need a moment to make sure I give you accurate information. Could you please repeat your last request?",
            OrderDraft = null,
            MissingInfo = new List<string>(),
            IsFallback = true
        };
    }
    
    /// <inheritdoc />
    public ChatResponse CreateRateLimitedResponse()
    {
        // NOTE: We do NOT mention rate limiting to the customer
        return new ChatResponse
        {
            Type = ChatResponseTypes.Message,
            Message = "I want to give you the best assistance. Could you please try your request again in a moment?",
            OrderDraft = null,
            MissingInfo = new List<string>(),
            IsFallback = true
        };
    }
}
