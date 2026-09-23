namespace PointOfSale.Core.Tenancy;

/// <summary>
/// Canonical contract for the stable tenant slug used in storefront host routing.
/// </summary>
public static class StoreSlug
{
    public const int MinLength = 3;
    public const int MaxLength = 63;

    private static readonly HashSet<string> ReservedValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "admin", "app", "portal", "dashboard", "console", "panel",
        "static", "cdn", "assets", "media", "files", "img", "images", "upload", "uploads",
        "mail", "smtp", "imap", "pop", "mx", "email", "webmail",
        "dev", "staging", "test", "qa", "uat", "sandbox", "demo", "preview", "beta",
        "auth", "oauth", "sso", "login", "logout", "signin", "signout", "secure", "ssl", "certificate",
        "store", "shop", "buy", "sell", "ecommerce", "pos", "online", "web", "website",
        "localhost", "local", "internal", "intranet", "private",
        "help", "support", "contact", "about", "blog", "news", "status", "docs", "documentation",
        "legal", "terms", "privacy", "tos", "gdpr", "dmca",
        "platform", "saas", "tenant", "tenants", "account", "accounts", "billing", "payment"
    };

    public static string Normalize(string value) => value.Trim().ToLowerInvariant();

    public static bool TryNormalize(string? value, out string normalized, out string? errorMessage)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = "Please choose a store address";
            return false;
        }

        if (value.Any(char.IsControl))
        {
            errorMessage = "Control characters are not allowed";
            return false;
        }

        normalized = Normalize(value);
        if (normalized.Length < MinLength)
        {
            errorMessage = $"Store address must be at least {MinLength} characters";
            return false;
        }

        if (normalized.Length > MaxLength)
        {
            errorMessage = "Store address is too long";
            return false;
        }

        if (!IsAsciiLetterOrDigit(normalized[0]))
        {
            errorMessage = "Store address must start with a letter or number";
            return false;
        }

        if (!IsAsciiLetterOrDigit(normalized[^1]))
        {
            errorMessage = "Store address must end with a letter or number";
            return false;
        }

        if (normalized.Contains("--", StringComparison.Ordinal))
        {
            errorMessage = "Remove extra hyphens";
            return false;
        }

        if (normalized.Any(character => !IsAsciiLetterOrDigit(character) && character != '-'))
        {
            errorMessage = "Only lowercase English letters, numbers, and hyphens are allowed";
            return false;
        }

        if (ReservedValues.Contains(normalized))
        {
            errorMessage = "This address is not available";
            return false;
        }

        errorMessage = null;
        return true;
    }

    public static IReadOnlyList<string> GenerateSuggestions(string value, int count = 3)
    {
        var normalized = Normalize(value);
        var candidates = Enumerable.Range(1, 9)
            .Select(number => $"{normalized}-{number}")
            .Concat(new[] { $"{normalized}-shop", $"{normalized}-store", $"{normalized}-online" })
            .Concat(normalized.Contains('-') ? new[] { normalized.Replace("-", string.Empty) } : []);

        return candidates
            .Where(candidate => TryNormalize(candidate, out _, out _))
            .Distinct(StringComparer.Ordinal)
            .Take(count)
            .ToArray();
    }

    private static bool IsAsciiLetterOrDigit(char value) =>
        char.IsAsciiLetterLower(value) || char.IsAsciiDigit(value);
}
