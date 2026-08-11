using System.Globalization;
using System.Net;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Middleware;

/// <summary>
/// Implements <see cref="IHostNormalizer"/> with pure-syntactic normalization.
///
/// Normalization steps (DOMAIN-1.6 Section 4, DOMAIN-1.5 CPD-8):
///   1. Null/empty guard — returns <c>null</c> for null/whitespace-only input.
///   2. Trim whitespace.
///   3. Remove a single trailing '.' (FQDN root label).
///   4. Strip IPv6 brackets: <c>[::1]:5000</c> → <c>::1:5000</c>, then port removal.
///   5. Strip the port suffix (everything after the last ':' for IPv4,
///      or after the closing ']' for IPv6).
///   6. Lowercase the resulting hostname.
///
/// This normalizer performs NO DNS resolution, NO database lookup, and NO
/// tenant or domain mapping. It is used by HostResolutionMiddleware to
/// produce a canonical host string that future Domain registry lookups
/// can query against.
/// </summary>
public sealed class HostNormalizer : IHostNormalizer
{
    /// <inheritdoc />
    public string? Normalize(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        // Step 1: Trim leading/trailing whitespace.
        var trimmed = host.Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.IndexOfAny(['/', '\\', '?', '#', '@']) >= 0)
        {
            return null;
        }

        // Step 2: Remove IPv6 brackets before any port parsing.
        // Input could be: "[::1]:5000", "[2001:db8::1]:443", "::1", "2001:db8::1"
        var bracketStart = trimmed.IndexOf('[');
        var bracketEnd = trimmed.IndexOf(']');

        string hostPart;
        if (bracketStart == 0 && bracketEnd > bracketStart)
        {
            // IPv6 with brackets: extract the content between brackets.
            hostPart = trimmed.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
            var suffix = trimmed[(bracketEnd + 1)..];
            if (suffix.Length > 0 &&
                (!suffix.StartsWith(':') || !TryValidatePort(suffix.AsSpan(1))))
            {
                return null;
            }
        }
        else if (bracketStart >= 0 || bracketEnd >= 0)
        {
            return null;
        }
        else
        {
            // No brackets: could be "host:port", "host", or bare IPv6 "::1".
            // For bare IPv6 without brackets, there is no port to strip.
            hostPart = trimmed;

            // Step 3: Strip port suffix ONLY if this is a "host:port" pattern,
            // not a bare IPv6 address. The distinguishing factor:
            // - A "host:port" has exactly ONE ':' with a numeric suffix.
            // - A bare IPv6 address has MULTIPLE ':' segments (e.g. "::1", "2001:db8::1").
            var colonCount = hostPart.AsSpan().Count(':');
            if (colonCount == 1)
            {
                var lastColonIndex = hostPart.IndexOf(':');
                var afterColon = hostPart.AsSpan(lastColonIndex + 1);
                if (TryValidatePort(afterColon))
                {
                    hostPart = hostPart.Substring(0, lastColonIndex);
                }
                else
                {
                    return null;
                }
            }
            // If colonCount > 1, this is a bare IPv6 address — leave as-is.
        }

        // Step 4: Remove trailing dot (FQDN root label), e.g. "example.com." → "example.com"
        if (hostPart.Length > 0 && hostPart[^1] == '.')
        {
            hostPart = hostPart[..^1];
        }

        if (hostPart.Length == 0)
        {
            return null;
        }

        if (IPAddress.TryParse(hostPart, out var address))
        {
            return address.ToString().ToLowerInvariant();
        }

        string normalized;
        try
        {
            normalized = new IdnMapping
            {
                AllowUnassigned = false,
                UseStd3AsciiRules = true,
            }.GetAscii(hostPart).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (normalized.Length > 253 ||
            normalized.StartsWith('.') ||
            normalized.EndsWith('.') ||
            normalized.Split('.').Any(label =>
                label.Length is < 1 or > 63 ||
                label[0] == '-' ||
                label[^1] == '-' ||
                label.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'))))
        {
            return null;
        }

        return normalized;
    }

    private static bool TryValidatePort(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return false;
        }
        var port = 0;
        foreach (var c in span)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }

            if (port > 6553)
            {
                return false;
            }

            port = (port * 10) + (c - '0');
            if (port > ushort.MaxValue)
            {
                return false;
            }
        }
        return port > 0;
    }
}
