using System.Net;

namespace PointOfSale.Tests.Security;

// Reuses real MVC attributes, canonical permission conventions and the SQL-
// authority handler. The resource filter prevents ALL business operations.
public sealed class StorefrontPhase1HttpAuthorizationTests(PosCloseoutAuthorizationHost host) : IClassFixture<PosCloseoutAuthorizationHost>
{
    public static IEnumerable<object[]> Requests()
    {
        foreach (var (method, path, analytics) in new[] {
            ("GET", "/api/admin/store-config", false),
            ("PUT", "/api/admin/store-config/status", false),
            ("GET", "/api/admin/wishlist-analytics", true),
            ("GET", "/api/admin/storefront/checkout-fields", false),
            ("POST", "/api/admin/storefront/checkout-fields", false),
            ("PATCH", "/api/admin/storefront/checkout-fields/{0}", false),
            ("DELETE", "/api/admin/storefront/checkout-fields/{0}", false),
            ("PUT", "/api/admin/storefront/checkout-fields/order", false),
            ("GET", "/api/admin/storefront/domains", false),
            ("GET", "/api/admin/storefront/domains/dashboard", false),
            ("POST", "/api/admin/storefront/domains", false),
            ("PATCH", "/api/admin/storefront/domains/{0}", false),
            ("PUT", "/api/admin/storefront/domains/{0}/verification/start", false),
            ("PUT", "/api/admin/storefront/domains/{0}/verification/verify", false),
            ("PUT", "/api/admin/storefront/domains/{0}/verification/retry", false),
        })
        foreach (var role in new[] { "Owner", "Admin", "Manager", "Cashier", "Warehouse", "anonymous", "untrusted-owner" })
        {
            var admitted = role is "Owner" or "Admin" || analytics && role == "Manager";
            yield return [method, string.Format(path, "11111111-1111-4111-8111-111111111111"), role,
                role == "anonymous" ? HttpStatusCode.Unauthorized : admitted ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden];
        }
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task Phase1Routes_EnforceAuthenticationRolesAndTrustedTenantAuthority(string method, string path, string role, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Test-Role", role);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }
}
