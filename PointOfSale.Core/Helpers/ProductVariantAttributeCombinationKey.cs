using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.Core.Helpers;

public static class ProductVariantAttributeCombinationKey
{
    public const int MaxLength = 80;
    private const string Prefix = "sha256:";

    public static string? Compute(IEnumerable<(string Name, string Value)> attributes)
    {
        var normalized = attributes
            .Select(attribute => (
                Name: Normalize(attribute.Name),
                Value: Normalize(attribute.Value)))
            .Where(attribute => attribute.Name.Length > 0 || attribute.Value.Length > 0)
            .OrderBy(attribute => attribute.Name, StringComparer.Ordinal)
            .ThenBy(attribute => attribute.Value, StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
        {
            return null;
        }

        var payload = string.Join(
            '\n',
            normalized.Select(attribute => $"{attribute.Name.Length}:{attribute.Name}={attribute.Value.Length}:{attribute.Value}"));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Prefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool HasDuplicateAttributeNames(
        IEnumerable<(string Name, string Value)> attributes,
        out string[] duplicateNames)
    {
        duplicateNames = attributes
            .Select(attribute => Normalize(attribute.Name))
            .Where(name => name.Length > 0)
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return duplicateNames.Length > 0;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
                ' ',
                value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }
}
