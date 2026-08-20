using System.Text;

namespace PointOfSale.API.Middleware;

/// <summary>
/// FEED-1B security fix (FEED-2 extended): public product feed URLs embed the
/// raw feed token as their final path segment (Feed-1A contract — the token
/// stays a URL credential). Cross-cutting request logging must never copy that
/// credential into logs, scopes, or activity tags, so any path under a public
/// feed channel route (/api/public/feeds/meta/, /api/public/feeds/google/, …)
/// is logged with the token segment replaced by a fixed placeholder.
///
/// Only public feed routes are affected; every other path is returned
/// unchanged. No partial masking, hashing, or decoding is performed — a fixed
/// placeholder is preferred because the secret must never correlate requests.
/// Feed correlation already uses safe identifiers (TenantId, TokenId, channel).
/// </summary>
internal static class PublicFeedPathRedactor
{
    /// <summary>
    /// Channel route prefixes that carry a raw feed token. FEED-2: every
    /// supported feed channel is redacted by the same fixed-placeholder rule.
    /// </summary>
    private static readonly string[] PublicFeedPathPrefixes =
    {
        "/api/public/feeds/meta/",
        "/api/public/feeds/google/",
        "/api/public/feeds/tiktok/",
        "/api/public/feeds/snapchat/"
    };

    private const string RedactedPlaceholder = "<REDACTED>";

    /// <summary>
    /// Returns <paramref name="path"/> with the feed token segment replaced by
    /// a fixed placeholder when the path targets a public feed route;
    /// otherwise returns the path unchanged. Matching is case-insensitive to
    /// mirror ASP.NET Core route matching; any extra segments after the token
    /// are redacted together with it so no path-derived secret can leak.
    /// </summary>
    public static string Redact(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path ?? string.Empty;
        }

        foreach (var prefix in PublicFeedPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return prefix + RedactedPlaceholder;
            }
        }

        return path;
    }

    /// <summary>
    /// Returns <paramref name="text"/> with every embedded occurrence of a
    /// public feed route redacted (deterministic prefix + final-segment
    /// redaction). Rendered log text such as scope messages or formatted
    /// messages can carry the path mid-string (for example the host's
    /// per-request scope renders "RequestPath:... RequestId:..."), so the
    /// token segment is located by the fixed route prefix and replaced with
    /// the placeholder wherever it appears. The token is URL-safe base64, so
    /// the segment ends at the first character outside that alphabet. Text
    /// that does not contain a feed route prefix is returned unchanged.
    /// </summary>
    public static string RedactEmbedded(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = text;
        foreach (var prefix in PublicFeedPathPrefixes)
        {
            result = RedactEmbeddedForPrefix(result, prefix);
        }

        return result;
    }

    private static string RedactEmbeddedForPrefix(string text, string prefix)
    {
        var index = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        var position = 0;
        while (index >= 0)
        {
            result.Append(text, position, index - position);

            var segmentStart = index + prefix.Length;
            var segmentEnd = segmentStart;
            while (segmentEnd < text.Length && IsTokenCharacter(text[segmentEnd]))
            {
                segmentEnd++;
            }

            result.Append(prefix);
            if (segmentEnd > segmentStart)
            {
                result.Append(RedactedPlaceholder);
            }

            position = segmentEnd;
            index = text.IndexOf(prefix, position, StringComparison.OrdinalIgnoreCase);
        }

        result.Append(text, position, text.Length - position);
        return result.ToString();
    }

    private static bool IsTokenCharacter(char c) =>
        char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_';
}
