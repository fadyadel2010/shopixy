using System.Buffers;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PointOfSale.API.Infrastructure.Idempotency;

public static class DeterministicRequestHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string ComputeHash(object? payload, int decimalScale = 2)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        var json = JsonSerializer.SerializeToElement(payload, SerializerOptions);

        WriteNormalizedValue(writer, json, decimalScale);
        writer.Flush();

        var hash = SHA256.HashData(buffer.WrittenSpan);
        return Convert.ToHexString(hash);
    }

    private static void WriteNormalizedValue(Utf8JsonWriter writer, JsonElement element, int decimalScale)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteNormalizedValue(writer, property.Value, decimalScale);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteNormalizedValue(writer, item, decimalScale);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.Number:
                if (element.TryGetDecimal(out var decimalValue))
                {
                    writer.WriteNumberValue(Math.Round(decimalValue, decimalScale, MidpointRounding.AwayFromZero));
                    break;
                }

                if (element.TryGetDouble(out var doubleValue) &&
                    !double.IsNaN(doubleValue) &&
                    !double.IsInfinity(doubleValue))
                {
                    writer.WriteNumberValue(Math.Round(doubleValue, decimalScale, MidpointRounding.AwayFromZero));
                    break;
                }

                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
        }
    }
}
