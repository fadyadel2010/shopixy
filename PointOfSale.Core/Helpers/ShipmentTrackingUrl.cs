namespace PointOfSale.Core.Helpers;

/// <summary>Shared Manual tracking-template contract; no provider operation.</summary>
public static class ShipmentTrackingUrl
{
    public static bool IsValidTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return true;
        if (template.Any(char.IsControl) || template.Contains('\\')) return false;
        if (!Uri.TryCreate(template, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        // Placeholders may describe path/query, never substitute host authority.
        var authorityEnd = template.IndexOfAny(['/', '?', '#'], template.IndexOf("://", StringComparison.Ordinal) + 3);
        var authority = authorityEnd < 0 ? template : template[..authorityEnd];
        return !authority.Contains('{') && !authority.Contains('}');
    }

    public static string? Build(string? template, string? trackingNumber)
    {
        if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(trackingNumber) || !IsValidTemplate(template)) return null;
        var encoded = Uri.EscapeDataString(trackingNumber);
        return template.Replace("{tracking_number}", encoded, StringComparison.Ordinal)
            .Replace("{trackingNumber}", encoded, StringComparison.Ordinal);
    }
}
