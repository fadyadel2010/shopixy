using System.Diagnostics.Metrics;

namespace PointOfSale.Infrastructure.Instrumentation;

public static class OperationalTelemetryDimensions
{
    private const int DefaultBucketCount = 10;
    private const int MaxDimensionLength = 64;
    private static readonly HashSet<char> AllowedCharacters = new(
        "abcdefghijklmnopqrstuvwxyz0123456789_-".ToCharArray());

    public static KeyValuePair<string, object?> CreateTenantBucketTag(Guid tenantId)
    {
        var bytes = tenantId.ToByteArray();
        var hash = BitConverter.ToInt32(bytes, 0);
        var bucket = Math.Abs(hash % DefaultBucketCount);
        return new KeyValuePair<string, object?>("tenant_bucket", $"bucket_{bucket}");
    }

    public static KeyValuePair<string, object?> CreateOptionalTenantBucketTag(Guid? tenantId)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            return new KeyValuePair<string, object?>("tenant_bucket", "none");
        }

        return CreateTenantBucketTag(tenantId.Value);
    }

    public static string NormalizeTagValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = value.Trim().ToLowerInvariant().Replace(' ', '_');
        var filtered = new string(normalized.Where(static c => AllowedCharacters.Contains(c)).ToArray());
        if (string.IsNullOrWhiteSpace(filtered))
        {
            return "invalid";
        }

        return filtered.Length > MaxDimensionLength
            ? filtered[..MaxDimensionLength]
            : filtered;
    }
}
