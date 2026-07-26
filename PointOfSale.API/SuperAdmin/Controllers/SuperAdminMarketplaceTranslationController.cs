using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.Services.AI.Marketplace;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/marketplace/translate")]
public sealed class SuperAdminMarketplaceTranslationController : SuperAdminBaseController
{
    private static readonly HashSet<string> UntranslatedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "type",
        "alignment",
        "imageUrl",
        "url",
        "ctaLink",
        "slug",
        "cmsPageSlug",
        "openGraphImageUrl",
        "source",
        "layout",
        "icon",
        "platform"
    };

    private readonly IAiTranslationService _translationService;

    public SuperAdminMarketplaceTranslationController(IAiTranslationService translationService)
    {
        _translationService = translationService;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Translate(
        [FromBody] MarketplaceTranslationRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_TRANSLATION_REQUEST_INVALID", "Request body is required.");
        }

        if (request.Content.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_TRANSLATION_REQUEST_INVALID", "Content is required.");
        }

        if (string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.To))
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_TRANSLATION_REQUEST_INVALID", "Both from and to languages are required.");
        }

        try
        {
            var translated = await TranslateElementAsync(request.Content, request.From.Trim(), request.To.Trim(), cancellationToken);
            return OkResponse(new MarketplaceTranslationResponse
            {
                Content = translated,
                From = request.From.Trim(),
                To = request.To.Trim()
            });
        }
        catch (InvalidOperationException exception)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_TRANSLATION_FAILED", exception.Message);
        }
    }

    private async Task<JsonElement> TranslateElementAsync(
        JsonElement element,
        string from,
        string to,
        CancellationToken cancellationToken,
        string? propertyName = null)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                if (!ShouldTranslate(propertyName, value))
                {
                    return JsonSerializer.SerializeToElement(value);
                }

                var translated = await _translationService.TranslateAsync(value, from, to, cancellationToken);
                return JsonSerializer.SerializeToElement(translated);

            case JsonValueKind.Array:
                var translatedItems = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    translatedItems.Add(ConvertJson(await TranslateElementAsync(item, from, to, cancellationToken, propertyName)));
                }

                return JsonSerializer.SerializeToElement(translatedItems);

            case JsonValueKind.Object:
                var translatedObject = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    var translatedProperty = await TranslateElementAsync(property.Value, from, to, cancellationToken, property.Name);
                    translatedObject[property.Name] = ConvertJson(translatedProperty);
                }

                return JsonSerializer.SerializeToElement(translatedObject);

            default:
                return JsonSerializer.SerializeToElement(ConvertJson(element));
        }
    }

    private static bool ShouldTranslate(string? propertyName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(propertyName) &&
            UntranslatedPropertyNames.Contains(propertyName))
        {
            return false;
        }

        if (value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !Guid.TryParse(value, out _);
    }

    private static object? ConvertJson(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Deserialize<object>(value.GetRawText()),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var integerValue)
                ? integerValue
                : value.TryGetDecimal(out var decimalValue)
                    ? decimalValue
                    : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
