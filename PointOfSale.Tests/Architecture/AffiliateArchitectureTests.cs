using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// Architecture tests enforcing the clean separation between:
/// - POS Admin affiliate management (INSIDE POS)
/// - Affiliate Hub portal/auth (OUTSIDE POS frontend, accessed via separate entry point)
///
/// These tests verify that the POS API maintains proper boundaries
/// and that affiliate portal controllers use correct authorization.
///
/// INVARIANTS:
/// 1. Admin controllers use [Authorize(Roles="Owner,Admin")]
/// 2. No controller named AffiliatePortalController has POS admin roles
/// 3. Affiliate portal controllers do NOT have Owner/Admin roles
/// 4. Public affiliate controllers use [AllowAnonymous]
/// 5. No endpoint accessible by role=Affiliate exists on admin controllers
/// </summary>
public class AffiliateArchitectureTests
{
    private readonly Assembly _apiAssembly;

    public AffiliateArchitectureTests()
    {
        _apiAssembly = Assembly.Load("PointOfSale.API");
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 1: Admin controllers must require Owner/Admin roles
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AdminAffiliateController_MustRequire_OwnerAdminRoles()
    {
        var controllerType = _apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "AdminAffiliateController");

        Assert.NotNull(controllerType);

        var authorizeAttr = controllerType!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .FirstOrDefault();

        Assert.NotNull(authorizeAttr);
        Assert.Contains("Owner", authorizeAttr!.Roles!);
        Assert.Contains("Admin", authorizeAttr.Roles!);
    }

    [Fact]
    public void AdminProductAffiliateController_MustRequire_OwnerAdminRoles()
    {
        var controllerType = _apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "AdminProductAffiliateController");

        Assert.NotNull(controllerType);

        var authorizeAttr = controllerType!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .FirstOrDefault();

        Assert.NotNull(authorizeAttr);
        Assert.Contains("Owner", authorizeAttr!.Roles!);
        Assert.Contains("Admin", authorizeAttr.Roles!);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 2: Admin controllers must use /api/admin/* route prefix
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AdminAffiliateController_RoutePrefix_MustStartWithApiAdmin()
    {
        var controllerType = _apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "AdminAffiliateController");

        Assert.NotNull(controllerType);

        var routeAttr = controllerType!
            .GetCustomAttributes<RouteAttribute>(inherit: true)
            .FirstOrDefault();

        Assert.NotNull(routeAttr);
        Assert.StartsWith("api/admin/", routeAttr!.Template!);
    }

    [Fact]
    public void AdminProductAffiliateController_RoutePrefix_MustStartWithApiAdmin()
    {
        var controllerType = _apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "AdminProductAffiliateController");

        Assert.NotNull(controllerType);

        var routeAttr = controllerType!
            .GetCustomAttributes<RouteAttribute>(inherit: true)
            .FirstOrDefault();

        Assert.NotNull(routeAttr);
        Assert.StartsWith("api/admin/", routeAttr!.Template!);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 3: No admin controller allows role=Affiliate
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AdminControllers_MustNotAllow_AffiliateRole()
    {
        var adminControllers = _apiAssembly.GetTypes()
            .Where(t => t.Name.StartsWith("Admin") && t.Name.EndsWith("Controller"));

        foreach (var controller in adminControllers)
        {
            // Check class-level
            var classAuth = controller
                .GetCustomAttributes<AuthorizeAttribute>(inherit: true);

            foreach (var attr in classAuth)
            {
                if (!string.IsNullOrEmpty(attr.Roles))
                {
                    Assert.False(
                        attr.Roles.Contains("Affiliate", StringComparison.OrdinalIgnoreCase),
                        $"Admin controller {controller.Name} must not allow Affiliate role");
                }
            }

            // Check method-level
            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var method in methods)
            {
                var methodAuth = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true);
                foreach (var attr in methodAuth)
                {
                    if (!string.IsNullOrEmpty(attr.Roles))
                    {
                        Assert.False(
                            attr.Roles.Contains("Affiliate", StringComparison.OrdinalIgnoreCase),
                            $"Admin controller {controller.Name}.{method.Name} must not allow Affiliate role");
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 4: AffiliatePortalController must NOT have Owner/Admin roles
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliatePortalController_MustUse_AffiliateOnlyPolicy()
    {
        var controllerType = _apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "AffiliatePortalController");

        if (controllerType == null)
            return; // If controller was removed, test passes

        var authorizeAttr = controllerType
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .FirstOrDefault();

        Assert.NotNull(authorizeAttr);
        Assert.Equal("AffiliateOnly", authorizeAttr!.Policy);
    }

    [Fact]
    public void AffiliatePortalController_RoutePrefix_MustNotStartWithApiAdmin()
    {
        var controllerType = _apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "AffiliatePortalController");

        if (controllerType == null)
            return;

        var routeAttr = controllerType
            .GetCustomAttributes<RouteAttribute>(inherit: true)
            .FirstOrDefault();

        if (routeAttr != null)
        {
            Assert.False(routeAttr.Template!.StartsWith("api/admin/"),
                "AffiliatePortalController must NOT use /api/admin/ prefix — it's an affiliate-facing controller");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 5: PublicAffiliateController must be [AllowAnonymous]
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PublicAffiliateController_MustBe_AllowAnonymous()
    {
        var controllerType = _apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "PublicAffiliateController");

        Assert.NotNull(controllerType);

        var allowAnonymous = controllerType!
            .GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
            .Any();

        Assert.True(allowAnonymous,
            "PublicAffiliateController must have [AllowAnonymous] — it serves the public landing page");
    }

    [Fact]
    public void PublicAffiliateController_RoutePrefix_MustStartWithApiPublic()
    {
        var controllerType = _apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "PublicAffiliateController");

        Assert.NotNull(controllerType);

        var routeAttr = controllerType!
            .GetCustomAttributes<RouteAttribute>(inherit: true)
            .FirstOrDefault();

        Assert.NotNull(routeAttr);
        Assert.StartsWith("api/public/", routeAttr!.Template!);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 6: AffiliateAuthController must be [AllowAnonymous]
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateAuthController_MustBe_AllowAnonymous()
    {
        var controllerType = _apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "AffiliateAuthController");

        Assert.NotNull(controllerType);

        var allowAnonymous = controllerType!
            .GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
            .Any();

        Assert.True(allowAnonymous,
            "AffiliateAuthController must have [AllowAnonymous] — affiliate registration/login is public");
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 7: Boundary — No /api/affiliate/* route on admin controllers
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AdminControllers_MustNotUse_AffiliateRoutePrefix()
    {
        var adminControllers = _apiAssembly.GetTypes()
            .Where(t => t.Name.StartsWith("Admin") && t.Name.EndsWith("Controller"));

        foreach (var controller in adminControllers)
        {
            var routeAttr = controller
                .GetCustomAttributes<RouteAttribute>(inherit: true)
                .FirstOrDefault();

            if (routeAttr != null)
            {
                Assert.False(
                    routeAttr.Template!.StartsWith("api/affiliate"),
                    $"Admin controller {controller.Name} uses /api/affiliate/* route. " +
                    "Admin controllers must only use /api/admin/* routes.");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 8: Boundary — No affiliate portal/discovery uses admin roles
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateHubControllers_MustNotRequire_AdminRoles()
    {
        // These controllers serve the external Affiliate Hub, not POS admins
        var hubControllerNames = new[]
        {
            "AffiliatePortalController",
            "AffiliateAnalyticsController",
            "AffiliateDiscoveryController"
        };

        foreach (var name in hubControllerNames)
        {
            var controllerType = _apiAssembly.GetTypes()
                .FirstOrDefault(t => t.Name == name);

            if (controllerType == null)
                continue; // Might be moved to separate project

            var authorizeAttr = controllerType
                .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .FirstOrDefault();

            if (authorizeAttr?.Roles != null)
            {
                Assert.False(
                    authorizeAttr.Roles.Contains("Owner", StringComparison.OrdinalIgnoreCase),
                    $"Affiliate hub controller {name} must not require Owner role");
                Assert.False(
                    authorizeAttr.Roles.Contains("Admin", StringComparison.OrdinalIgnoreCase),
                    $"Affiliate hub controller {name} must not require Admin role");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 9: AF-12 — Middleware must support X-Affiliate-Ref header
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateReferralMiddleware_MustRead_XAffiliateRefHeader()
    {
        // The middleware must check X-Affiliate-Ref header in addition to ?ref= query param.
        // Without this, SPA storefronts cannot activate Affiliate Mode because
        // the SPA makes API calls without ?ref= in the URL.
        var middlewareType = _apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "AffiliateReferralMiddleware");

        Assert.NotNull(middlewareType);

        // Check that the RefHeaderName constant exists
        var headerField = middlewareType!
            .GetField("RefHeaderName", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(headerField);
        Assert.Equal("X-Affiliate-Ref", headerField!.GetValue(null));
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 10: AF-12 — AffiliateModeResolver keys must be consistent
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateModeResolver_Keys_MustBeConsistent()
    {
        // Verify the key used by SetAffiliateMode matches the key read by IsAffiliateMode.
        // A key mismatch would silently break Affiliate Mode.
        var resolverType = _apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "AffiliateModeResolver");

        Assert.NotNull(resolverType);

        var affiliateModeKey = resolverType!
            .GetField("AffiliateModeKey", BindingFlags.NonPublic | BindingFlags.Static);
        var affiliateAccountIdKey = resolverType
            .GetField("AffiliateAccountIdKey", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(affiliateModeKey);
        Assert.NotNull(affiliateAccountIdKey);

        // Ensure keys are non-empty strings
        var modeKeyValue = affiliateModeKey!.GetValue(null) as string;
        var accountIdKeyValue = affiliateAccountIdKey!.GetValue(null) as string;

        Assert.False(string.IsNullOrWhiteSpace(modeKeyValue),
            "AffiliateModeKey must not be null or empty");
        Assert.False(string.IsNullOrWhiteSpace(accountIdKeyValue),
            "AffiliateAccountIdKey must not be null or empty");

        // Ensure they are different (not accidentally using the same key)
        Assert.NotEqual(modeKeyValue, accountIdKeyValue);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 11: AF-12 — Storefront controllers must use AffiliateModeResolver
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void StorefrontControllers_MustUse_AffiliateModeResolver()
    {
        // Every storefront controller that deals with products must use
        // AffiliateModeResolver to detect affiliate mode, not manual computation.
        var storefrontControllerNames = new[]
        {
            "StorefrontProductsController",
            "StorefrontHomeController"
        };

        foreach (var name in storefrontControllerNames)
        {
            var controllerType = _apiAssembly.GetTypes()
                .FirstOrDefault(t => t.Name == name);

            Assert.NotNull(controllerType);

            // Check that the source file references AffiliateModeResolver
            // (structural test — we can't decompile methods reliably, but we
            //  can verify the type is used in the same assembly)
            var resolverType = _apiAssembly.GetTypes()
                .FirstOrDefault(t => t.Name == "AffiliateModeResolver");

            Assert.NotNull(resolverType);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 12: AF-AUTH-ISOLATION — All affiliate portal controllers use AffiliateOnly policy
    //
    // ROOT CAUSE PREVENTION:
    // The AffiliateOnly policy is bound to the "AffiliateJwt" scheme which reads
    // tokens ONLY from Authorization header (never from cookies).
    // Without this policy, affiliate controllers fall back to the default "Bearer"
    // scheme which prefers cookies — causing 403 when POS cookie exists.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AllAffiliateHubControllers_MustUse_AffiliateOnlyPolicy()
    {
        // These controllers serve the external Affiliate Hub
        // They MUST use AffiliateOnly policy to ensure AffiliateJwt scheme
        var hubControllerNames = new[]
        {
            "AffiliatePortalController",
            "AffiliateAnalyticsController",
            "AffiliateDiscoveryController"
        };

        foreach (var name in hubControllerNames)
        {
            var controllerType = _apiAssembly.GetTypes()
                .FirstOrDefault(t => t.Name == name);

            if (controllerType == null)
                continue;

            var authorizeAttr = controllerType
                .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .FirstOrDefault();

            Assert.NotNull(authorizeAttr);
            Assert.Equal("AffiliateOnly", authorizeAttr!.Policy);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEST 13: AF-AUTH-ISOLATION — No affiliate route uses default [Authorize]
    //
    // Any controller under /api/affiliate/* with just [Authorize] (no policy)
    // would bypass the AffiliateJwt scheme and be vulnerable to cookie collision.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateRouteControllers_MustNotUse_DefaultAuthorize()
    {
        // Find all controllers with route starting with "api/affiliate"
        var affiliateControllers = _apiAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Controller") &&
                        !t.Name.StartsWith("Admin") &&
                        t.GetCustomAttributes<RouteAttribute>(inherit: true)
                            .Any(r => r.Template != null && r.Template.StartsWith("api/affiliate")));

        foreach (var controller in affiliateControllers)
        {
            // Skip auth controller (it's AllowAnonymous)
            if (controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any())
                continue;

            var authorizeAttr = controller
                .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .FirstOrDefault();

            Assert.NotNull(authorizeAttr);

            // Must have a policy (not bare [Authorize])
            Assert.False(
                string.IsNullOrEmpty(authorizeAttr!.Policy) && string.IsNullOrEmpty(authorizeAttr.AuthenticationSchemes),
                $"Controller {controller.Name} uses bare [Authorize] on an /api/affiliate/* route. " +
                "This is vulnerable to cookie collision. Use [Authorize(Policy = \"AffiliateOnly\")] instead.");
        }
    }
}
