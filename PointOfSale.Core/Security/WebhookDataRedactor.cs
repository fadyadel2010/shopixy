using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PointOfSale.Core.Security;

/// <summary>
/// Redacts PII from webhook data before persistence to comply with data retention policies.
/// </summary>
public static class WebhookDataRedactor
{
    private const int MaxPayloadSummaryLength = 500;
    private const int MaxUserAgentLength = 100;

    private static readonly string[] PiiFieldTokens = new[]
    {
        "address", "street", "city", "state", "zip", "postal", "postcode",
        "phone", "mobile", "telephone", "email", "name", "firstname", "lastname",
        "recipient", "customer", "consignee", "shipper",
        "latitude", "longitude", "coordinates", "location",
        "signature", "notes", "comments", "description"
    };

    /// <summary>
    /// Creates a redacted summary of the webhook payload.
    /// Removes common PII fields and truncates to safe length.
    /// </summary>
    public static string RedactPayload(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return string.Empty;
        }

        try
        {
            // Try to parse as JSON and redact PII fields
            var jsonDoc = JsonDocument.Parse(rawPayload);
            var redacted = RedactJsonElement(jsonDoc.RootElement);
            var redactedJson = JsonSerializer.Serialize(redacted);

            // Truncate to max length
            return redactedJson.Length > MaxPayloadSummaryLength
                ? redactedJson.Substring(0, MaxPayloadSummaryLength) + "...[truncated]"
                : redactedJson;
        }
        catch
        {
            // If not valid JSON, just truncate and redact patterns
            var truncated = rawPayload.Length > MaxPayloadSummaryLength
                ? rawPayload.Substring(0, MaxPayloadSummaryLength) + "...[truncated]"
                : rawPayload;

            return RedactPiiPatterns(truncated);
        }
    }

    /// <summary>
    /// Redacts PII fields from JSON element recursively.
    /// </summary>
    private static object? RedactJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>();
                foreach (var property in element.EnumerateObject())
                {
                    var key = property.Name.ToLowerInvariant();
                    
                    // Redact common PII fields
                    if (IsPiiField(key))
                    {
                        obj[property.Name] = "[REDACTED]";
                    }
                    else
                    {
                        obj[property.Name] = RedactJsonElement(property.Value);
                    }
                }
                return obj;

            case JsonValueKind.Array:
                var array = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    array.Add(RedactJsonElement(item));
                }
                return array;

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
                return null;

            default:
                return element.ToString();
        }
    }

    /// <summary>
    /// Checks if a field name indicates PII content.
    /// </summary>
    private static bool IsPiiField(string fieldName)
    {
        foreach (var piiToken in PiiFieldTokens)
        {
            if (fieldName.IndexOf(piiToken, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Redacts PII patterns from plain text using regex.
    /// </summary>
    private static string RedactPiiPatterns(string text)
    {
        // Redact email addresses
        text = Regex.Replace(text, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", "[EMAIL_REDACTED]");

        // Redact phone numbers (various formats)
        text = Regex.Replace(text, @"\b\+?[\d\s\-\(\)]{10,}\b", "[PHONE_REDACTED]");

        // Redact potential addresses (street numbers + street names)
        text = Regex.Replace(text, @"\b\d+\s+[A-Za-z\s]+(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Lane|Ln|Drive|Dr)\b", "[ADDRESS_REDACTED]", RegexOptions.IgnoreCase);

        return text;
    }

    /// <summary>
    /// Redacts IP address to network prefix only (e.g., "203.0.113.45" -> "203.0.113.0/24").
    /// </summary>
    public static string? RedactIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        // Try to parse as a valid IP address
        if (IPAddress.TryParse(ipAddress, out var addr))
        {
            // Handle IPv6
            if (addr.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = addr.GetAddressBytes();
                
                // Convert first 8 bytes (4 hextets) to hexadecimal groups
                var hextets = new string[4];
                for (int i = 0; i < 4; i++)
                {
                    var hextet = (bytes[i * 2] << 8) | bytes[i * 2 + 1];
                    hextets[i] = hextet.ToString("x");
                }
                
                return $"{string.Join(":", hextets)}::/64";
            }
            
            // Handle IPv4
            if (addr.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = addr.GetAddressBytes();
                return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
            }
        }

        // If format not recognized, return generic redaction
        return "[IP_REDACTED]";
    }

    /// <summary>
    /// Truncates user agent string to remove detailed version information.
    /// </summary>
    public static string? TruncateUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        // Remove version numbers (e.g., "1.2.3.4" -> "x.x.x")
        var redacted = Regex.Replace(userAgent, @"\d+\.\d+(\.\d+)*", "x.x");

        // Truncate to max length
        if (redacted.Length > MaxUserAgentLength)
        {
            redacted = redacted.Substring(0, MaxUserAgentLength) + "...";
        }

        return redacted;
    }
}
