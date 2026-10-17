using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PointOfSale.API.Services.AI.Models;

/// <summary>
/// Request from the storefront to the chat endpoint.
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// The customer's message.
    /// </summary>
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Session ID for conversation continuity.
    /// </summary>
    [StringLength(128)]
    public string? SessionId { get; set; }

    /// <summary>
    /// Product IDs currently visible to the customer (for context).
    /// </summary>
    [MaxLength(20)]
    public List<Guid>? ProductIdsInView { get; set; }

    /// <summary>
    /// Current page context (e.g., "product:123", "category:electronics", "checkout").
    /// </summary>
    [StringLength(1024)]
    public string? CurrentPage { get; set; }

    /// <summary>
    /// Language preference (auto-detected from message if not provided).
    /// </summary>
    [StringLength(10)]
    public string? Language { get; set; }
}

/// <summary>
/// Response from the AI assistant.
/// </summary>
public class ChatResponse
{
    /// <summary>
    /// Response type: message, question, or order_draft.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    /// <summary>
    /// Human-readable message to display to the customer.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Order draft information (if intent detected).
    /// </summary>
    [JsonPropertyName("orderDraft")]
    public OrderDraftResponse? OrderDraft { get; set; }

    /// <summary>
    /// List of missing information needed to proceed.
    /// </summary>
    [JsonPropertyName("missingInfo")]
    public List<string> MissingInfo { get; set; } = new();

    /// <summary>
    /// Indicates if this is a fallback response due to AI failure.
    /// </summary>
    [JsonPropertyName("isFallback")]
    public bool IsFallback { get; set; }
}

/// <summary>
/// Order draft response from AI.
/// </summary>
public class OrderDraftResponse
{
    /// <summary>
    /// Intent: none, create, update, or confirm.
    /// </summary>
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "none";

    /// <summary>
    /// Items in the draft order.
    /// </summary>
    [JsonPropertyName("items")]
    public List<OrderDraftItemResponse> Items { get; set; } = new();

    /// <summary>
    /// Selected shipping method ID.
    /// </summary>
    [JsonPropertyName("shippingMethodId")]
    public Guid? ShippingMethodId { get; set; }
}

/// <summary>
/// Item in the order draft response.
/// </summary>
public class OrderDraftItemResponse
{
    /// <summary>
    /// Product ID.
    /// </summary>
    [JsonPropertyName("productId")]
    public Guid ProductId { get; set; }

    /// <summary>
    /// Variant ID (for products with variants).
    /// </summary>
    [JsonPropertyName("variantId")]
    public Guid? VariantId { get; set; }

    /// <summary>
    /// Quantity.
    /// </summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Valid response types.
/// </summary>
public static class ChatResponseTypes
{
    public const string Message = "message";
    public const string Question = "question";
    public const string OrderDraft = "order_draft";

    public static readonly HashSet<string> ValidTypes = new()
    {
        Message,
        Question,
        OrderDraft
    };
}

/// <summary>
/// Valid order draft intents.
/// </summary>
public static class OrderDraftIntents
{
    public const string None = "none";
    public const string Create = "create";
    public const string Update = "update";
    public const string Confirm = "confirm";

    public static readonly HashSet<string> ValidIntents = new()
    {
        None,
        Create,
        Update,
        Confirm
    };
}

/// <summary>
/// Valid missing info types.
/// </summary>
public static class MissingInfoTypes
{
    public const string Quantity = "quantity";
    public const string ShippingMethod = "shipping_method";
    public const string Address = "address";
    public const string VariantSelection = "variant_selection";
    public const string ProductSelection = "product_selection";
    public const string ContactInfo = "contact_info";
}

/// <summary>
/// Configuration for a specific AI model.
/// PHASE 2: Multi-model routing support.
/// </summary>
public class ModelConfig
{
    /// <summary>
    /// Whether this model is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Priority (1 = highest, used first)
    /// </summary>
    public int Priority { get; set; } = 1;
    
    /// <summary>
    /// Provider name (e.g., "openrouter", "azure", "local")
    /// </summary>
    public string Provider { get; set; } = "openrouter";
    
    /// <summary>
    /// Model identifier (e.g., "deepseek/deepseek-r1")
    /// </summary>
    public string ModelId { get; set; } = string.Empty;
    
    /// <summary>
    /// API endpoint URL
    /// </summary>
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1/chat/completions";
    
    /// <summary>
    /// API key for authentication
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Maximum tokens for response
    /// </summary>
    public int MaxTokens { get; set; } = 1000;
}

/// <summary>
/// Configuration options for the AI Commerce Assistant.
/// PHASE 2: Extended with model routing and rate limiting.
/// PHASE 6.5: Added daily/monthly cost ceiling protection.
/// </summary>
public class CommerceAIOptions
{
    public const string SectionName = "CommerceAI";
    
    /// <summary>
    /// Whether the AI assistant is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    // =========================================================================
    // PHASE 1: Legacy single-model configuration (fallback if Models not set)
    // =========================================================================
    
    /// <summary>
    /// Provider name (e.g., "openrouter", "azure", "local")
    /// </summary>
    public string Provider { get; set; } = "openrouter";
    
    /// <summary>
    /// Model name (e.g., "deepseek-chat-v3")
    /// </summary>
    public string Model { get; set; } = "deepseek/deepseek-chat";
    
    /// <summary>
    /// API endpoint URL
    /// </summary>
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1/chat/completions";
    
    /// <summary>
    /// API key for authentication
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Maximum tokens for response
    /// </summary>
    public int MaxTokens { get; set; } = 1000;
    
    /// <summary>
    /// Temperature for response generation (0.0-1.0)
    /// </summary>
    public double Temperature { get; set; } = 0.3;
    
    // =========================================================================
    // PHASE 2: Multi-model routing configuration
    // =========================================================================
    
    /// <summary>
    /// Model-specific configurations.
    /// Keys: "DeepSeek", "GptOss", "Gemma"
    /// </summary>
    public Dictionary<string, ModelConfig> Models { get; set; } = new();
    
    /// <summary>
    /// Maximum retries across all models
    /// </summary>
    public int MaxRetries { get; set; } = 2;
    
    /// <summary>
    /// Whether to enable model fallback on failure
    /// </summary>
    public bool EnableFallback { get; set; } = true;
    
    // =========================================================================
    // RATE LIMITING
    // =========================================================================
    
    /// <summary>
    /// Whether rate limiting is enabled
    /// </summary>
    public bool RateLimitingEnabled { get; set; } = true;
    
    /// <summary>
    /// Maximum requests per minute per tenant
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 60;
    
    /// <summary>
    /// Maximum requests per hour per tenant
    /// </summary>
    public int RateLimitPerHour { get; set; } = 500;
    
    /// <summary>
    /// Maximum requests per day per tenant (PHASE 6.5: Cost ceiling protection)
    /// </summary>
    public int RateLimitPerDay { get; set; } = 10000;
    
    /// <summary>
    /// Maximum requests per month per tenant (PHASE 6.5: Cost ceiling protection)
    /// </summary>
    public int RateLimitPerMonth { get; set; } = 250000;
    
    // =========================================================================
    // CONTEXT LIMITS
    // =========================================================================
    
    /// <summary>
    /// Maximum products to include in context
    /// </summary>
    public int MaxProductsInContext { get; set; } = 20;
    
    /// <summary>
    /// Maximum shipping methods to include in context
    /// </summary>
    public int MaxShippingMethodsInContext { get; set; } = 10;
    
    // =========================================================================
    // CACHING & DEBUGGING
    // =========================================================================
    
    /// <summary>
    /// Whether to enable response caching
    /// </summary>
    public bool EnableCaching { get; set; } = false;
    
    /// <summary>
    /// Cache duration in seconds
    /// </summary>
    public int CacheDurationSeconds { get; set; } = 300;
    
    /// <summary>
    /// Whether to enable debug logging
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;
}
