namespace PointOfSale.Core.Security;

/// <summary>
/// Single deterministic server-side normalizer for POS identity keys.
/// Used for login lookups and global uniqueness checks on User identity.
/// Client-side normalization is advisory only and must never be trusted.
/// Identity keys are deliberately restricted to printable ASCII so the .NET
/// normalizer and PostgreSQL UPPER(BTRIM(column)) are exactly equivalent,
/// independent of database collation or Unicode case-folding differences.
/// </summary>
public static class PosIdentityNormalizer
{
    public const int MaxUsernameLength = 100;
    public const int MaxEmailLength = 200;

    public static string NormalizeUsername(string? username) =>
        Normalize(username, MaxUsernameLength, nameof(username));

    public static string NormalizeEmail(string? email) =>
        Normalize(email, MaxEmailLength, nameof(email));

    private static string Normalize(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identity value is required.", paramName);
        }

        var trimmed = value.Trim(' ');
        if (trimmed.Any(character => character is < '!' or > '~'))
        {
            throw new ArgumentException("Identity value must contain printable ASCII characters only.", paramName);
        }

        var normalized = trimmed.ToUpperInvariant();
        if (normalized.Length == 0 || normalized.Length > maxLength)
        {
            throw new ArgumentException($"Identity value must be 1-{maxLength} characters after trimming.", paramName);
        }

        return normalized;
    }

    public static bool TryNormalizeUsername(string? username, out string normalized)
    {
        try
        {
            normalized = NormalizeUsername(username);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    public static bool TryNormalizeEmail(string? email, out string normalized)
    {
        try
        {
            normalized = NormalizeEmail(email);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }
    }
}
