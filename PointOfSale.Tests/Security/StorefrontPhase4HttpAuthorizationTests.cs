using System.Net;

namespace PointOfSale.Tests.Security;

public sealed class StorefrontPhase4HttpAuthorizationTests(PosCloseoutAuthorizationHost host) : IClassFixture<PosCloseoutAuthorizationHost>
{
    public static IEnumerable<object[]> Requests()
    {
        foreach (var (method, path) in new[] {
            ("GET", "/api/admin/orders"), ("GET", "/api/admin/orders/{0}"), ("PUT", "/api/admin/orders/{0}/status"), ("POST", "/api/admin/orders/{0}/cancel"),
            ("GET", "/api/admin/abandoned-checkouts"), ("GET", "/api/admin/abandoned-checkouts/stats"), ("GET", "/api/admin/abandoned-checkouts/{0}"),
            ("GET", "/api/admin/phones"), ("POST", "/api/admin/phones/block"), ("POST", "/api/admin/phones/unblock"),
            ("GET", "/api/admin/online-customers"), ("GET", "/api/admin/online-customers/{0}"),
            ("GET", "/api/admin/ecommerce/abandoned-cart-recovery/settings"), ("PUT", "/api/admin/ecommerce/abandoned-cart-recovery/settings"), ("GET", "/api/admin/ecommerce/abandoned-cart-recovery/eligibility"), ("GET", "/api/admin/ecommerce/abandoned-cart-recovery/stats"),
            ("GET", "/api/admin/whatsapp/accounts"), ("POST", "/api/admin/whatsapp/accounts"), ("PUT", "/api/admin/whatsapp/accounts/{0}"), ("POST", "/api/admin/whatsapp/accounts/{0}/disable"),
            ("GET", "/api/admin/whatsapp/templates"), ("POST", "/api/admin/whatsapp/templates"), ("PUT", "/api/admin/whatsapp/templates/{0}"), ("DELETE", "/api/admin/whatsapp/templates/{0}"),
            ("GET", "/api/admin/whatsapp/logs"), ("GET", "/api/admin/whatsapp/analytics"), ("GET", "/api/admin/whatsapp/killswitch/status"),
            ("GET", "/api/orders/{0}/fulfillment/actions"), ("POST", "/api/orders/{0}/fulfillment/confirm"), ("POST", "/api/orders/{0}/fulfillment/complete"),
            ("GET", "/api/payments/lifecycle/{0}/actions"), ("POST", "/api/payments/lifecycle/{0}/cod-delivered"),
        })
        foreach (var role in new[] { "Owner", "Admin", "Manager", "Cashier", "Warehouse", "anonymous", "untrusted-owner" }) {
            var admitted = role is "Owner" or "Admin" or "Manager";
            if (role == "Warehouse" && method == "POST" && path.Contains("/fulfillment/")) admitted = true;
            if (role == "Manager" && method == "POST" && path.Contains("/payments/lifecycle/")) admitted = false;
            yield return [method, string.Format(path, "11111111-1111-4111-8111-111111111111"), role,
                role == "anonymous" ? HttpStatusCode.Unauthorized : admitted ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden];
        }
    }
    [Theory, MemberData(nameof(Requests))]
    public async Task Phase4CurrentMvcAuthority(string method, string path, string role, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Test-Role", role);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }
}
