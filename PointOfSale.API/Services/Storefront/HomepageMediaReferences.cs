using System.Text.Json.Nodes;

namespace PointOfSale.API.Services.Storefront;

public static class HomepageMediaReferences
{
    public const string Prefix = "/api/storefront/homepage-media/";
    public static string Create(Guid id) => $"{Prefix}{id:D}";

    public static bool TryRead(string? value, out Guid id)
    {
        id = Guid.Empty;
        return value != null && value.StartsWith(Prefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(value[Prefix.Length..], "D", out id) && id != Guid.Empty;
    }

    public static IEnumerable<string> Images(JsonObject? composition)
    {
        if (composition?["sections"] is not JsonArray sections) yield break;
        foreach (var section in sections.OfType<JsonObject>())
        {
            if (section["settings"] is not JsonObject settings) continue;
            if (section["type"] is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type) ||
                !PointOfSale.Core.Constants.StorefrontSectionContracts.SupportedTypes.Contains(type)) continue;
            foreach (var field in PointOfSale.Core.Constants.StorefrontSectionContracts.GetFields(type))
            {
                if (field.Type == "image" && settings[field.Key] is JsonValue value && value.TryGetValue<string>(out var image))
                    yield return image;
            }
        }
    }

    public static bool IsPublished(string? customizationJson, Guid id)
    {
        var root = JsonNode.Parse(customizationJson ?? "{}") as JsonObject;
        var home = root?["pages"]?["home"]?["live"] as JsonObject;
        if (home?["sections"] is not JsonArray sections) return false;
        var enabled = new JsonObject { ["sections"] = new JsonArray(sections.OfType<JsonObject>()
            .Where(section => section["enabled"]?.GetValue<bool>() != false).Select(section => section.DeepClone()).ToArray()) };
        return Images(enabled).Any(image => TryRead(image, out var candidate) && candidate == id);
    }
}
