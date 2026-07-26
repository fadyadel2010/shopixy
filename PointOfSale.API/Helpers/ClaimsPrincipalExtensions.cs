using System.Security.Claims;

namespace PointOfSale.API.Helpers;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetCustomerId(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var claimValue = principal.FindFirst("CustomerId")?.Value
            ?? principal.FindFirst("customer_id")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(claimValue, out var customerId) && customerId != Guid.Empty
            ? customerId
            : null;
    }
}
