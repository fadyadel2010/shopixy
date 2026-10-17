using System.Net;

namespace PointOfSale.Tests.Security;

public sealed class Phase5HttpAuthorizationTests(PosCloseoutAuthorizationHost host) : IClassFixture<PosCloseoutAuthorizationHost>
{
    public static IEnumerable<object[]> Requests()
    {
        foreach (var (method, path, manager, warehouse) in new[] {
            ("GET", "/api/admin/payment-methods", true, false), ("GET", "/api/admin/payment-methods/catalog", true, false),
            ("PUT", "/api/admin/payment-methods/COD", false, false), ("PUT", "/api/admin/payment-methods/STRIPE/configure", false, false),
            ("POST", "/api/admin/payment-methods/COD/enable", false, false), ("POST", "/api/admin/payment-methods/COD/disable", false, false),
            ("GET", "/api/admin/payments/manual-wallet/pending", true, false),
            ("POST", "/api/admin/payments/manual-wallet/{0}/confirm", false, false), ("POST", "/api/admin/payments/manual-wallet/{0}/reject", false, false),
            ("GET", "/api/admin/payments/manual-wallet/proof-image/{0}", true, false),
            ("GET", "/api/shippingmethods", true, false), ("POST", "/api/shippingmethods", true, false),
            ("PUT", "/api/shippingmethods/{0}", true, false), ("DELETE", "/api/shippingmethods/{0}", true, false),
            ("GET", "/api/admin/shipments", true, true), ("GET", "/api/admin/shipments/{0}", true, true),
            ("POST", "/api/admin/shipments/{0}/cancel", true, true), ("POST", "/api/shipments", true, true),
        })
        foreach (var role in new[] { "Owner", "Admin", "Manager", "Warehouse", "Cashier", "anonymous", "untrusted-owner" }) {
            var admitted = role is "Owner" or "Admin" || role == "Manager" && manager || role == "Warehouse" && warehouse;
            yield return [method, string.Format(path, "11111111-1111-4111-8111-111111111111"), role,
                role == "anonymous" ? HttpStatusCode.Unauthorized : admitted ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden];
        }
    }
    [Theory, MemberData(nameof(Requests))]
    public async Task RealMvcPolicies(string method, string path, string role, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Test-Role", role);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }
}
