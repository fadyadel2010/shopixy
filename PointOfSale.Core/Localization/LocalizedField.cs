using System.Text.Json.Serialization;

namespace PointOfSale.Core.Localization;

public sealed class LocalizedField
{
    [JsonPropertyName("en")]
    public string? En { get; set; }

    [JsonPropertyName("ar")]
    public string? Ar { get; set; }

    [JsonIgnore]
    public bool HasValue =>
        !string.IsNullOrWhiteSpace(En) ||
        !string.IsNullOrWhiteSpace(Ar);
}
