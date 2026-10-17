using System.Net;

namespace PointOfSale.Tests.Security;

public sealed class Phase6HttpAuthorizationTests(PosCloseoutAuthorizationHost host) : IClassFixture<PosCloseoutAuthorizationHost>
{
    public static IEnumerable<object[]> Requests()
    {
        foreach (var (method, path, manager) in new[] {
            ("GET", "/api/admin/pixel-tracking/config", true), ("PUT", "/api/admin/pixel-tracking/config", false),
            ("GET", "/api/admin/pixel-tracking/preview?orderTotal=600&strategyStr=FIXED&value=0", true),
            ("GET", "/api/admin/analytics/funnels", true), ("GET", "/api/admin/analytics/funnels/purchase_funnel/realtime", true),
            ("GET", "/api/admin/analytics/attribution/summary", true), ("GET", "/api/admin/analytics/attribution/orders", true),
            ("GET", "/api/admin/analytics/attribution/campaigns", true), ("GET", "/api/admin/analytics/attribution/models", true),
            ("GET", "/api/admin/analytics/attribution/sessions/p6-session", true),
            ("GET", "/api/admin/analytics/campaign-costs", true), ("POST", "/api/admin/analytics/campaign-costs", false),
            ("PUT", "/api/admin/analytics/campaign-costs/11111111-1111-4111-8111-111111111111", false),
            ("DELETE", "/api/admin/analytics/campaign-costs/11111111-1111-4111-8111-111111111111", false),
            ("GET", "/api/admin/analytics/roi/summary", true), ("GET", "/api/admin/analytics/roi/campaigns", true),
        })
        foreach (var role in new[] { "Owner", "Admin", "Manager", "Warehouse", "Cashier", "anonymous", "untrusted-owner" }) {
            var admitted = role is "Owner" or "Admin" || role == "Manager" && manager;
            yield return [method, path, role, role == "anonymous" ? HttpStatusCode.Unauthorized : admitted ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden];
        }
    }
    [Theory, MemberData(nameof(Requests))]
    public async Task RealMvcPolicies_NoBusinessMutations(string method, string path, string role, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Test-Role", role);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }
}
