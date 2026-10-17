using System.Net;

namespace PointOfSale.Tests.Security;

// Real MVC permission/role admission. The shared resource filter prevents every
// business mutation; upstream authentication and trusted-authority claims are controlled.
public sealed class StorefrontPhase2HttpAuthorizationTests(PosCloseoutAuthorizationHost host) : IClassFixture<PosCloseoutAuthorizationHost>
{
    public static IEnumerable<object[]> Requests()
    {
        foreach (var (method, path) in new[] {
            ("PUT", "/api/admin/store-config/pages/home"), ("POST", "/api/admin/store-config/pages/home/publish"),
            ("GET", "/api/admin/store-config/versions"), ("POST", "/api/admin/store-config/rollback/1"),
            ("PUT", "/api/admin/store-config/theme/overrides"), ("POST", "/api/admin/store-config/theme/publish"),
            ("POST", "/api/admin/store-config/theme/activate"),
            ("GET", "/api/admin/pages"), ("POST", "/api/admin/pages"), ("PUT", "/api/admin/pages/{0}"),
            ("DELETE", "/api/admin/pages/{0}"), ("POST", "/api/admin/pages/{0}/publish"),
            ("POST", "/api/admin/pages/{0}/rollback"), ("POST", "/api/admin/pages/{0}/preview-link"),
            ("GET", "/api/admin/cms/navigation"), ("GET", "/api/admin/cms/navigation/targets"),
            ("PUT", "/api/admin/cms/navigation"), ("POST", "/api/admin/cms/navigation/publish"),
            ("GET", "/api/admin/footer"), ("PUT", "/api/admin/footer"),
        })
        foreach (var role in new[] { "Owner", "Admin", "Manager", "Cashier", "Warehouse", "anonymous", "untrusted-owner" })
            yield return [method, string.Format(path, "11111111-1111-4111-8111-111111111111"), role,
                role == "anonymous" ? HttpStatusCode.Unauthorized : role is "Owner" or "Admin" ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden];
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task Phase2Endpoints_EnforceTrustedTenantAndDesignAuthority(string method, string path, string role, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Test-Role", role);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }
}
