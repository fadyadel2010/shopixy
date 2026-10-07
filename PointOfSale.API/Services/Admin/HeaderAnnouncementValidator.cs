using System.Text.RegularExpressions;
using PointOfSale.Core.Models;

namespace PointOfSale.API.Services.Admin;

public static class HeaderAnnouncementValidator
{
    public static HeaderAnnouncementConfig? Validate(HeaderAnnouncementConfig? config)
    {
        if (config == null) return null;
        config.Text ??= string.Empty;
        config.TextAr ??= string.Empty;
        config.Link ??= string.Empty;
        config.BackgroundColor ??= string.Empty;
        config.TextColor ??= string.Empty;
        if (config.Text.Length > 500 || config.TextAr.Length > 500)
            throw new ArgumentException("Announcement text must be 500 characters or fewer.");
        if (!IsSafeLink(config.Link)) throw new ArgumentException("Announcement link must be a storefront path or an HTTP(S) URL.");
        foreach (var color in new[] { config.BackgroundColor, config.TextColor })
            if (color.Length != 0 && !Regex.IsMatch(color, "^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$"))
                throw new ArgumentException("Announcement colors must be valid hex colors.");
        return config;
    }

    public static bool IsSafeLink(string value)
    {
        if (value.Length == 0) return true;
        if (value.Length > 2048 || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)) || value.Contains('\\')) return false;
        if (value.StartsWith('/') && !value.StartsWith("//")) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
    }
}
