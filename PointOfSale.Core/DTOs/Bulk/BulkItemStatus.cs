using System.Text.Json;
using System.Text.Json.Serialization;

namespace PointOfSale.Core.DTOs.Bulk;

[JsonConverter(typeof(BulkItemStatusJsonConverter))]
public enum BulkItemStatus
{
    Processed,
    Unchanged,
    Failed
}

public static class BulkItemStatusExtensions
{
    public static string ToWireString(this BulkItemStatus status) =>
        status switch
        {
            BulkItemStatus.Processed => "processed",
            BulkItemStatus.Unchanged => "unchanged",
            BulkItemStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}

public sealed class BulkItemStatusJsonConverter : JsonConverter<BulkItemStatus>
{
    public override BulkItemStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("Bulk item status is required.");
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "processed" => BulkItemStatus.Processed,
            "unchanged" => BulkItemStatus.Unchanged,
            "failed" => BulkItemStatus.Failed,
            _ => throw new JsonException($"Unknown bulk item status '{value}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, BulkItemStatus value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToWireString());
}
