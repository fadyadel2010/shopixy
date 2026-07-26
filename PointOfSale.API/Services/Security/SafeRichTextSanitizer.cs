using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace PointOfSale.API.Services.Security;

internal static class SafeRichTextSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "b",
        "blockquote",
        "br",
        "code",
        "div",
        "em",
        "h1",
        "h2",
        "h3",
        "h4",
        "h5",
        "h6",
        "hr",
        "i",
        "li",
        "ol",
        "p",
        "pre",
        "s",
        "span",
        "strong",
        "u",
        "ul"
    };

    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br",
        "hr"
    };

    private static readonly HashSet<string> SafeHrefSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http",
        "https",
        "mailto",
        "tel"
    };

    private static readonly HashSet<string> AllowedNamedEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        "nbsp",   // Non-breaking space
        "amp",    // Ampersand
        "lt",     // Less than
        "gt",     // Greater than
        "quot",   // Quotation mark
        "apos",   // Apostrophe
        "copy",   // Copyright
        "reg",    // Registered trademark
        "trade",  // Trademark
        "euro",   // Euro sign
        "pound",  // Pound sign
        "yen",    // Yen sign
        "cent",   // Cent sign
        "sect",   // Section sign
        "para",   // Paragraph sign
        "middot", // Middle dot
        "bull",   // Bullet
        "hellip", // Horizontal ellipsis
        "prime",  // Prime
        "Prime",  // Double prime
        "lsaquo", // Left single angle quote
        "rsaquo", // Right single angle quote
        "laquo",  // Left double angle quote
        "raquo",  // Right double angle quote
        "lsquo",  // Left single quote
        "rsquo",  // Right single quote
        "ldquo",  // Left double quote
        "rdquo",  // Right double quote
        "ndash",  // En dash
        "mdash",  // Em dash
        "deg",    // Degree sign
        "plusmn", // Plus-minus sign
        "times",  // Multiplication sign
        "divide", // Division sign
        "frac14", // One quarter
        "frac12", // One half
        "frac34"  // Three quarters
    };

    private static readonly Regex DangerousElementRegex = new(
        "<\\s*(script|style|iframe|object|embed|form|input|button|textarea|select|meta|link|svg|math|base|noscript)\\b[^>]*>(.*?)<\\s*/\\s*\\1\\s*>|<\\s*(script|style|iframe|object|embed|form|input|button|textarea|select|meta|link|svg|math|base|noscript)\\b[^>]*/?\\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex HtmlCommentRegex = new(
        "<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex HtmlTagRegex = new(
        "<\\s*(?<closing>/)?\\s*(?<name>[a-z0-9]+)(?<attributes>[^<>]*?)(?<selfClosing>/)?\\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex AttributeRegex = new(
        "(?<name>[^\\s\"'=<>`/]+)(?:\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s\"'=<>`]+)))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Sanitize(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var strippedHtml = HtmlCommentRegex.Replace(html, string.Empty);
        strippedHtml = DangerousElementRegex.Replace(strippedHtml, string.Empty);

        var sanitized = new System.Text.StringBuilder(strippedHtml.Length);
        var currentIndex = 0;

        foreach (Match match in HtmlTagRegex.Matches(strippedHtml))
        {
            AppendSafeText(sanitized, strippedHtml.AsSpan(currentIndex, match.Index - currentIndex));
            currentIndex = match.Index + match.Length;

            var tagName = match.Groups["name"].Value.ToLowerInvariant();
            if (!AllowedTags.Contains(tagName))
            {
                continue;
            }

            var isClosingTag = match.Groups["closing"].Success;
            var isVoidTag = VoidTags.Contains(tagName);

            if (isClosingTag)
            {
                if (!isVoidTag)
                {
                    sanitized.Append("</").Append(tagName).Append('>');
                }

                continue;
            }

            sanitized.Append('<').Append(tagName);

            foreach (var attribute in SanitizeAttributes(tagName, match.Groups["attributes"].Value))
            {
                sanitized.Append(' ')
                    .Append(attribute.Name)
                    .Append("=\"")
                    .Append(HtmlEncoder.Default.Encode(attribute.Value))
                    .Append('"');
            }

            if (match.Groups["selfClosing"].Success || isVoidTag)
            {
                sanitized.Append(" />");
            }
            else
            {
                sanitized.Append('>');
            }
        }

        AppendSafeText(sanitized, strippedHtml.AsSpan(currentIndex));
        return sanitized.ToString().Trim();
    }

    private static IReadOnlyList<(string Name, string Value)> SanitizeAttributes(string tagName, string rawAttributes)
    {
        if (!string.Equals(tagName, "a", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(rawAttributes))
        {
            return Array.Empty<(string Name, string Value)>();
        }

        string? safeHref = null;
        string? safeTarget = null;
        string? relValue = null;

        foreach (Match attributeMatch in AttributeRegex.Matches(rawAttributes))
        {
            var name = attributeMatch.Groups["name"].Value.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("on", StringComparison.Ordinal))
            {
                continue;
            }

            var value = System.Net.WebUtility.HtmlDecode(
                attributeMatch.Groups["value"].Success ? attributeMatch.Groups["value"].Value : string.Empty).Trim();

            switch (name)
            {
                case "href":
                    if (IsSafeHref(value))
                    {
                        safeHref = value;
                    }
                    break;
                case "target":
                    if (string.Equals(value, "_blank", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(value, "_self", StringComparison.OrdinalIgnoreCase))
                    {
                        safeTarget = value.ToLowerInvariant();
                    }
                    break;
                case "rel":
                    relValue = value;
                    break;
            }
        }

        if (safeHref is null)
        {
            return Array.Empty<(string Name, string Value)>();
        }

        var sanitizedAttributes = new List<(string Name, string Value)>
        {
            ("href", safeHref)
        };

        if (safeTarget is not null)
        {
            sanitizedAttributes.Add(("target", safeTarget));

            if (string.Equals(safeTarget, "_blank", StringComparison.Ordinal))
            {
                sanitizedAttributes.Add(("rel", MergeRelValue(relValue)));
            }
        }

        return sanitizedAttributes;
    }

    private static bool IsSafeHref(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmedValue = value.Trim();
        if (trimmedValue.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (Uri.TryCreate(trimmedValue, UriKind.Absolute, out var absoluteUri))
        {
            return SafeHrefSchemes.Contains(absoluteUri.Scheme);
        }

        return Uri.TryCreate(trimmedValue, UriKind.Relative, out _);
    }

    private static string MergeRelValue(string? existingRel)
    {
        var relValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(existingRel))
        {
            foreach (var token in existingRel.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                relValues.Add(token.ToLowerInvariant());
            }
        }

        relValues.Add("noopener");
        relValues.Add("noreferrer");

        return string.Join(' ', relValues.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static void AppendSafeText(System.Text.StringBuilder builder, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return;
        }

        var textString = text.ToString();
        var i = 0;

        while (i < textString.Length)
        {
            var character = textString[i];

            if (character == '&')
            {
                // Look ahead to check if this is a valid HTML entity
                // Use IndexOf for more robust semicolon detection
                var semicolonIndex = textString.IndexOf(';', i + 1);
                
                // Allow up to 60 characters for long HTML5 named entities (e.g., &CounterClockwiseContourIntegral;)
                const int maxEntityLength = 60;
                var maxLookAhead = Math.Min(i + maxEntityLength + 1, textString.Length);
                
                // Check if semicolon exists within reasonable distance
                if (semicolonIndex != -1 && semicolonIndex < maxLookAhead)
                {
                    var entityEnd = semicolonIndex;
                    var entityContent = textString.Substring(i + 1, entityEnd - i - 1);

                    // Check if it's a valid entity
                    if (IsValidEntity(entityContent))
                    {
                        // SECURITY: Decode the entity and re-escape dangerous characters
                        // This prevents XSS via entity-encoded script tags like &#60;script&#62;
                        var decodedChar = DecodeEntity(entityContent);
                        
                        if (decodedChar == null)
                        {
                            // Could not decode, preserve the entity for safe named entities
                            // This handles entities like &nbsp;, &copy;, etc.
                            if (entityContent.Length > 0 && !entityContent.StartsWith("#", StringComparison.Ordinal))
                            {
                                builder.Append(textString, i, entityEnd - i + 1);
                            }
                            else
                            {
                                // Invalid numeric entity (e.g. &#x110000; — out-of-range code point).
                                // Preserve the literal entity text verbatim rather than collapsing it
                                // to a bare &amp;, which would silently corrupt user content.
                                builder.Append(textString, i, entityEnd - i + 1);
                            }
                        }
                        else if (string.Equals(decodedChar, "<", StringComparison.Ordinal))
                        {
                            builder.Append("&lt;");
                        }
                        else if (string.Equals(decodedChar, ">", StringComparison.Ordinal))
                        {
                            builder.Append("&gt;");
                        }
                        else if (string.Equals(decodedChar, "&", StringComparison.Ordinal))
                        {
                            builder.Append("&amp;");
                        }
                        else if (string.Equals(decodedChar, "\"", StringComparison.Ordinal))
                        {
                            builder.Append("&quot;");
                        }
                        else if (string.Equals(decodedChar, "'", StringComparison.Ordinal))
                        {
                            builder.Append("&#39;");
                        }
                        else
                        {
                            // Safe character (BMP or non-BMP), append as-is
                            builder.Append(decodedChar);
                        }
                        
                        i = entityEnd + 1;
                        continue;
                    }
                }

                // Not a valid entity, escape the ampersand
                builder.Append("&amp;");
                i++;
            }
            else if (character == '<')
            {
                builder.Append("&lt;");
                i++;
            }
            else if (character == '>')
            {
                builder.Append("&gt;");
                i++;
            }
            else
            {
                builder.Append(character);
                i++;
            }
        }
    }

    private static string? DecodeEntity(string entityContent)
    {
        if (string.IsNullOrEmpty(entityContent))
        {
            return null;
        }

        // Numeric entity: #[0-9]+ or #x[0-9A-Fa-f]+
        if (entityContent[0] == '#')
        {
            if (entityContent.Length < 2)
            {
                return null;
            }

            try
            {
                int codePoint;
                
                // Hex entity: #x[0-9A-Fa-f]+
                if (entityContent[1] == 'x' || entityContent[1] == 'X')
                {
                    if (entityContent.Length < 3)
                    {
                        return null;
                    }
                    
                    codePoint = Convert.ToInt32(entityContent.Substring(2), 16);
                }
                else
                {
                    // Decimal entity: #[0-9]+
                    codePoint = Convert.ToInt32(entityContent.Substring(1), 10);
                }

                // Validate code point using Rune.IsValid which checks:
                // - Range is within [0x0000..0xD7FF] or [0xE000..0x10FFFF]
                // - Explicitly rejects surrogate code points (U+D800–U+DFFF)
                if (!Rune.IsValid(codePoint))
                {
                    return null;
                }

                // Convert code point to string (handles both BMP and non-BMP characters)
                return char.ConvertFromUtf32(codePoint);
            }
            catch
            {
                return null;
            }
        }

        // Named entity: decode common ones
        return entityContent.ToLowerInvariant() switch
        {
            "lt" => "<",
            "gt" => ">",
            "amp" => "&",
            "quot" => "\"",
            "apos" => "'",
            "nbsp" => "\u00A0",
            "copy" => "\u00A9",
            "reg" => "\u00AE",
            "trade" => "\u2122",
            "euro" => "\u20AC",
            "pound" => "\u00A3",
            "yen" => "\u00A5",
            "cent" => "\u00A2",
            _ => null // Unknown entity
        };
    }

    private static bool IsValidEntity(string entityContent)
    {
        if (string.IsNullOrEmpty(entityContent))
        {
            return false;
        }

        // Numeric entity: #[0-9]+
        if (entityContent[0] == '#')
        {
            if (entityContent.Length < 2)
            {
                return false;
            }

            // Hex entity: #x[0-9A-Fa-f]+
            if (entityContent[1] == 'x' || entityContent[1] == 'X')
            {
                if (entityContent.Length < 3)
                {
                    return false;
                }

                for (var i = 2; i < entityContent.Length; i++)
                {
                    var c = entityContent[i];
                    if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                    {
                        return false;
                    }
                }

                return true;
            }

            // Decimal entity: #[0-9]+
            for (var i = 1; i < entityContent.Length; i++)
            {
                if (entityContent[i] < '0' || entityContent[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        // Named entity: must be in whitelist
        if (!char.IsLetter(entityContent[0]))
        {
            return false;
        }

        // Validate format: [A-Za-z][A-Za-z0-9]*
        for (var i = 1; i < entityContent.Length; i++)
        {
            if (!char.IsLetterOrDigit(entityContent[i]))
            {
                return false;
            }
        }

        // Check against whitelist of allowed named entities
        return AllowedNamedEntities.Contains(entityContent);
    }
}
