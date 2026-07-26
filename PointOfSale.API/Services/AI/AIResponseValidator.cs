using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI;

/// <summary>
/// Validates and parses AI responses.
/// Ensures responses conform to the required schema.
/// </summary>
public interface IAIResponseValidator
{
    /// <summary>
    /// Validate and parse a raw AI response.
    /// </summary>
    /// <param name="rawResponse">Raw JSON string from AI</param>
    /// <param name="context">Original context for validation</param>
    /// <param name="parsed">Parsed response if valid</param>
    /// <param name="error">Error message if invalid</param>
    /// <returns>True if valid</returns>
    bool TryValidate(
        string rawResponse,
        CommerceContext context,
        out ChatResponse? parsed,
        out string? error);
}

/// <summary>
/// Implementation of the response validator.
/// </summary>
public class AIResponseValidator : IAIResponseValidator
{
    private readonly ILogger<AIResponseValidator> _logger;

    public AIResponseValidator(ILogger<AIResponseValidator> logger)
    {
        _logger = logger;
    }

    public bool TryValidate(
        string rawResponse,
        CommerceContext context,
        out ChatResponse? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            error = "Empty response from AI";
            return false;
        }

        // Try to extract JSON from the response (AI might include markdown code blocks)
        var jsonContent = ExtractJson(rawResponse);
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            error = "No valid JSON found in response";
            return false;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            parsed = JsonSerializer.Deserialize<ChatResponse>(jsonContent, options);

            if (parsed == null)
            {
                error = "Failed to deserialize response";
                return false;
            }

            // Validate type field
            if (!ChatResponseTypes.ValidTypes.Contains(parsed.Type))
            {
                error = $"Invalid response type: {parsed.Type}";
                return false;
            }

            // Validate message is not empty
            if (string.IsNullOrWhiteSpace(parsed.Message))
            {
                error = "Response message cannot be empty";
                return false;
            }

            // Validate orderDraft if present
            if (parsed.OrderDraft != null)
            {
                if (!OrderDraftIntents.ValidIntents.Contains(parsed.OrderDraft.Intent))
                {
                    error = $"Invalid order draft intent: {parsed.OrderDraft.Intent}";
                    return false;
                }

                // PHASE 6.5: STRICT VALIDATION - Reject hallucinated product IDs
                var validProductIds = context.Products
                    .Select(p => p.Id)
                    .ToHashSet();

                var hallucinatedProducts = parsed.OrderDraft.Items
                    .Where(i => !validProductIds.Contains(i.ProductId))
                    .ToList();

                if (hallucinatedProducts.Any())
                {
                    var hallucinatedIds = string.Join(", ", hallucinatedProducts.Select(p => p.ProductId));
                    error = $"AI referenced non-existent product IDs: {hallucinatedIds}";
                    
                    _logger.LogError(
                        "AI HALLUCINATION DETECTED | Non-existent products: {ProductIds}",
                        hallucinatedIds);
                    
                    // REJECT entire response - hallucination is unacceptable
                    return false;
                }

                // Validate quantities
                foreach (var item in parsed.OrderDraft.Items)
                {
                    if (item.Quantity < 1)
                    {
                        item.Quantity = 1; // Default to 1
                    }
                }

                // Validate shipping method if specified
                if (parsed.OrderDraft.ShippingMethodId.HasValue)
                {
                    var validShippingIds = context.Shipping.Methods
                        .Select(m => m.Id)
                        .ToHashSet();

                    if (!validShippingIds.Contains(parsed.OrderDraft.ShippingMethodId.Value))
                    {
                        error = $"AI referenced non-existent shipping method: {parsed.OrderDraft.ShippingMethodId}";
                        
                        _logger.LogError(
                            "AI HALLUCINATION DETECTED | Non-existent shipping method: {ShippingMethodId}",
                            parsed.OrderDraft.ShippingMethodId);
                        
                        // REJECT entire response - hallucination is unacceptable
                        return false;
                    }
                }
            }

            // Ensure missingInfo is not null
            parsed.MissingInfo ??= new List<string>();

            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI response JSON: {RawResponse}", rawResponse);
            error = $"JSON parse error: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating AI response");
            error = $"Validation error: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Extract JSON from a response that might contain markdown code blocks.
    /// </summary>
    private static string ExtractJson(string response)
    {
        var trimmed = response.Trim();

        // Check for markdown code block
        if (trimmed.StartsWith("```"))
        {
            var lines = trimmed.Split('\n');
            var jsonLines = new List<string>();
            var inCodeBlock = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        break; // End of code block
                    }
                    inCodeBlock = true;
                    continue;
                }

                if (inCodeBlock)
                {
                    jsonLines.Add(line);
                }
            }

            return string.Join('\n', jsonLines);
        }

        // Try to find JSON object directly
        var startIndex = trimmed.IndexOf('{');
        var endIndex = trimmed.LastIndexOf('}');

        if (startIndex >= 0 && endIndex > startIndex)
        {
            return trimmed.Substring(startIndex, endIndex - startIndex + 1);
        }

        return trimmed;
    }
}
