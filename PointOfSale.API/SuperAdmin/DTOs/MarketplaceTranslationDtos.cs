using System.Text.Json;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class MarketplaceTranslationRequest
{
    public JsonElement Content { get; set; }
    public string From { get; set; } = "en";
    public string To { get; set; } = "ar";
}

public sealed class MarketplaceTranslationResponse
{
    public JsonElement Content { get; set; }
    public string From { get; set; } = "en";
    public string To { get; set; } = "ar";
}
