using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace PointOfSale.API.Security;

public static class CommerceHubOrderAccessCookie
{
    public const string CookieName = "commercehub_order_access";

    private const string CookiePath = "/api/commercehub";
    private const int MaxTrackedOrders = 12;

    public static void StoreOrderAccessToken(
        HttpContext httpContext,
        Guid marketplaceOrderId,
        string orderNumber,
        string token,
        TimeSpan lifetime,
        bool allowInsecureDevelopmentCookie)
    {
        if (marketplaceOrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(orderNumber) ||
            string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var entries = ReadEntries(httpContext.Request)
            .Where(entry => entry.MarketplaceOrderId != marketplaceOrderId)
            .ToList();

        entries.Insert(0, new StoredOrderAccessEntry(
            marketplaceOrderId,
            orderNumber.Trim(),
            token.Trim(),
            DateTimeOffset.UtcNow));

        if (entries.Count > MaxTrackedOrders)
        {
            entries = entries.Take(MaxTrackedOrders).ToList();
        }

        httpContext.Response.Cookies.Append(
            CookieName,
            JsonSerializer.Serialize(entries),
            CreateCookieOptions(lifetime, allowInsecureDevelopmentCookie));
    }

    public static string? ReadOrderAccessToken(HttpRequest request, string? orderIdOrNumber)
    {
        if (string.IsNullOrWhiteSpace(orderIdOrNumber))
        {
            return null;
        }

        var normalized = orderIdOrNumber.Trim();
        var entries = ReadEntries(request);

        if (Guid.TryParse(normalized, out var marketplaceOrderId))
        {
            var byId = entries.FirstOrDefault(entry => entry.MarketplaceOrderId == marketplaceOrderId);
            return NormalizeToken(byId?.Token);
        }

        var byOrderNumber = entries.FirstOrDefault(entry =>
            string.Equals(entry.OrderNumber, normalized, StringComparison.OrdinalIgnoreCase));
        return NormalizeToken(byOrderNumber?.Token);
    }

    public static void ClearOrderAccessToken(HttpContext httpContext, bool allowInsecureDevelopmentCookie)
    {
        httpContext.Response.Cookies.Delete(
            CookieName,
            CreateCookieOptions(TimeSpan.FromSeconds(1), allowInsecureDevelopmentCookie));
    }

    private static IReadOnlyList<StoredOrderAccessEntry> ReadEntries(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<StoredOrderAccessEntry>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<StoredOrderAccessEntry>>(raw);
            if (parsed == null || parsed.Count == 0)
            {
                return Array.Empty<StoredOrderAccessEntry>();
            }

            return parsed
                .Where(entry =>
                    entry.MarketplaceOrderId != Guid.Empty &&
                    !string.IsNullOrWhiteSpace(entry.OrderNumber) &&
                    !string.IsNullOrWhiteSpace(entry.Token))
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<StoredOrderAccessEntry>();
        }
    }

    private static string? NormalizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return token.Trim();
    }

    private static CookieOptions CreateCookieOptions(TimeSpan lifetime, bool allowInsecureDevelopmentCookie) => new()
    {
        HttpOnly = true,
        Secure = !allowInsecureDevelopmentCookie,
        SameSite = SameSiteMode.Strict,
        MaxAge = lifetime,
        Path = CookiePath,
        IsEssential = true
    };

    private sealed record StoredOrderAccessEntry(
        Guid MarketplaceOrderId,
        string OrderNumber,
        string Token,
        DateTimeOffset StoredAtUtc);
}
