using System.Net;

namespace PointOfSale.Tests.Security;

public sealed class CustomerTrustHttpAuthorizationTests(PosCloseoutAuthorizationHost host) : IClassFixture<PosCloseoutAuthorizationHost>
{
    public static IEnumerable<object[]> Requests()
    {
        foreach (var (method, path) in new[] {
            ("GET", "/api/admin/orders/{0}/customer-trust"),
            ("POST", "/api/admin/orders/{0}/customer-phone/block"),
            ("POST", "/api/admin/orders/{0}/customer-phone/unblock"),
            ("POST", "/api/admin/orders/{0}/customer-phone/report-spam"),
            ("GET", "/api/admin/phones"),
            ("POST", "/api/admin/phones/unblock"),
        })
        foreach (var role in new[] { "Owner", "Admin", "Manager", "Cashier", "Warehouse", "anonymous", "untrusted-owner" })
            yield return [method, string.Format(path, "11111111-1111-4111-8111-111111111111"), role,
                role == "anonymous" ? HttpStatusCode.Unauthorized : role is "Owner" or "Admin" or "Manager" ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden];
    }

    [Theory, MemberData(nameof(Requests))]
    public async Task TrustAndBlockActions_EnforceActualMvcRolePolicies(string method, string path, string role, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Test-Role", role);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }
}
