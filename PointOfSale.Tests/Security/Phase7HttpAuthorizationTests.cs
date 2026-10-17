using System.Net;

namespace PointOfSale.Tests.Security;

public sealed class Phase7HttpAuthorizationTests(PosCloseoutAuthorizationHost host) : IClassFixture<PosCloseoutAuthorizationHost>
{
    public static IEnumerable<object[]> Requests()
    {
        foreach (var (method, path) in new[] {
            ("GET", "/api/admin/commerce-ai"), ("GET", "/api/admin/commerce-ai/status"), ("GET", "/api/admin/commerce-ai/insights"), ("POST", "/api/admin/commerce-ai/toggle"),
            ("GET", "/api/admin/store-config"), ("PUT", "/api/admin/store-config/commerce-ai"),
            ("GET", "/api/admin/storefront/auth-providers"), ("GET", "/api/admin/storefront/auth-providers/Google"), ("PUT", "/api/admin/storefront/auth-providers/Google"),
            ("POST", "/api/admin/storefront/auth-providers/Google/enable"), ("POST", "/api/admin/storefront/auth-providers/Google/disable"), ("POST", "/api/admin/storefront/auth-providers/Google/rotate-secret")
        })
        foreach (var role in new[] { "Owner", "Admin", "Manager", "Warehouse", "Cashier", "anonymous", "untrusted-owner" })
            yield return [method, path, role, role == "anonymous" ? HttpStatusCode.Unauthorized : role is "Owner" or "Admin" ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden];
    }

    [Theory, MemberData(nameof(Requests))]
    public async Task RealMvcPolicies_ConfigurationAuthority_NoBusinessMutations(string method, string path, string role, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path); request.Headers.Add("X-Test-Role", role);
        using var response = await host.Client.SendAsync(request); Assert.Equal(expected, response.StatusCode);
    }
}
