using System.Net;

namespace PointOfSale.Tests.Security;

public sealed class StorefrontPhase3HttpAuthorizationTests(PosCloseoutAuthorizationHost host) : IClassFixture<PosCloseoutAuthorizationHost>
{
    public static IEnumerable<object[]> Requests()
    {
        foreach (var (method, path) in new[] {
            ("GET", "/api/coupons"), ("POST", "/api/coupons"), ("GET", "/api/coupons/{0}"), ("PUT", "/api/coupons/{0}"), ("PATCH", "/api/coupons/{0}/toggle"), ("DELETE", "/api/coupons/{0}"),
            ("GET", "/api/promotions"), ("POST", "/api/promotions"), ("GET", "/api/promotions/stats"), ("GET", "/api/promotions/{0}"), ("PUT", "/api/promotions/{0}"), ("PATCH", "/api/promotions/{0}/toggle"), ("DELETE", "/api/promotions/{0}"),
            ("GET", "/api/admin/smart-offers"), ("POST", "/api/admin/smart-offers"), ("GET", "/api/admin/smart-offers/stats"), ("GET", "/api/admin/smart-offers/{0}"), ("PUT", "/api/admin/smart-offers/{0}"), ("DELETE", "/api/admin/smart-offers/{0}"),
        })
        foreach (var role in new[] { "Owner", "Admin", "Manager", "Cashier", "Warehouse", "anonymous", "untrusted-owner" })
            yield return [method, string.Format(path, "11111111-1111-4111-8111-111111111111"), role,
                role == "anonymous" ? HttpStatusCode.Unauthorized : role is "Owner" or "Admin" or "Manager" ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden];
    }
    [Theory]
    [MemberData(nameof(Requests))]
    public async Task Phase3RolesAndTrustedAuthority(string method, string path, string role, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Test-Role", role);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }
}
