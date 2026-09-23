using System.Text.RegularExpressions;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Security;

namespace PointOfSale.Tests.Security;

public sealed class PosSessionPermissionProjectionTests
{
    [Fact]
    public void SessionDto_ProjectsServerDerivedPermissions_FromTrustedSqlRole()
    {
        var authSource = File.ReadAllText(ApiPath("Controllers", "AuthController.cs"));

        Assert.Contains(
            "EffectivePermissions = PosRolePermissionCatalog.GetPermissions(user.Role)",
            authSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("request.EffectivePermissions", authSource, StringComparison.Ordinal);
        Assert.DoesNotContain("request.Permissions", authSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationContract_CannotSupplyPermissionsOrRole()
    {
        Assert.Null(typeof(RegisterRequest).GetProperty("EffectivePermissions"));
        Assert.Null(typeof(RegisterRequest).GetProperty("Permissions"));
        Assert.Null(typeof(RegisterRequest).GetProperty("Role"));

        var authSource = File.ReadAllText(ApiPath("Controllers", "AuthController.cs"));
        Assert.DoesNotContain("\"effectivePermissions\"", authSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendKnownPermissions_MatchBackendCatalogExactly()
    {
        var frontend = File.ReadAllText(WebPath("src", "auth", "posPermissions.js"));
        // Every catalog permission contains a dot; this excludes non-permission
        // string literals such as 'string' in the validator implementation.
        var frontendPermissions = Regex.Matches(frontend, "'([a-z][a-z0-9.\\-]*\\.[a-z0-9.\\-]*)'")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(PosPermissions.All.Count, frontendPermissions.Count);
        foreach (var permission in PosPermissions.All)
        {
            Assert.True(
                frontendPermissions.Contains(permission),
                $"Backend permission '{permission}' is missing from the frontend known set.");
        }
    }

    [Fact]
    public void Permissions_AreNeverPersistedToBrowserStorage()
    {
        var authStore = File.ReadAllText(WebPath("src", "store", "authStore.js"));
        var sessionAuthority = File.ReadAllText(WebPath("src", "auth", "posVendorSessionAuthority.js"));

        Assert.DoesNotContain("setItem", authStore, StringComparison.Ordinal);
        Assert.DoesNotContain("effectivePermissions", authStore, StringComparison.Ordinal);

        // posVendorSessionAuthority legitimately touches localStorage only for the
        // cross-tab logout signal; effective permissions must never be persisted.
        foreach (var line in sessionAuthority.Split('\n'))
        {
            if (line.Contains("effectivePermissions", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("setItem", StringComparison.Ordinal) ||
                 line.Contains("localStorage", StringComparison.Ordinal) ||
                 line.Contains("sessionStorage", StringComparison.Ordinal)))
            {
                Assert.Fail($"Permissions must never be persisted to browser storage: {line.Trim()}");
            }
        }
    }

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API" }.Concat(parts).ToArray()));

    private static string WebPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Web" }.Concat(parts).ToArray()));
}
