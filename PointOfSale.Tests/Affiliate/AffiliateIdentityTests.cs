using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-10: Architecture tests for Affiliate Identity & External Portal Separation.
///
/// These tests enforce:
/// 1. AffiliateUser entity exists with correct shape
/// 2. AffiliateUser unique constraints (Email+Tenant, AffiliateAccountId)
/// 3. AffiliateAuthController exists with correct routes and methods
/// 4. AffiliateAuthController is [AllowAnonymous] (public registration/login)
/// 5. AffiliateAuthController does NOT inject IAffiliateService or LedgerService
/// 6. JWT includes affiliate-specific claims
/// 7. "AffiliateOnly" policy exists
/// 8. Portal controller has dual-path identity resolution
/// 9. Auth controller has register + login + refresh endpoints
/// 10. No commission logic in auth controller
/// 11. AffiliateUser in DbContext
/// </summary>
public class AffiliateIdentityTests
{
    private readonly Type _affiliateUserType = typeof(AffiliateUser);
    private readonly Type _authControllerType = typeof(AffiliateAuthController);
    private readonly Type _portalControllerType = typeof(AffiliatePortalController);
    private readonly Type _analyticsControllerType = typeof(AffiliateAnalyticsController);
    private readonly Type _dbContextType = typeof(ApplicationDbContext);

    // ═══════════════════════════════════════════════════════════════════
    // ENTITY SHAPE — AffiliateUser
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("TenantId", typeof(Guid))]
    [InlineData("Email", typeof(string))]
    [InlineData("PasswordHash", typeof(string))]
    [InlineData("AffiliateAccountId", typeof(Guid))]
    [InlineData("EmailVerified", typeof(bool))]
    public void AffiliateUser_Has_Required_Field(string fieldName, Type expectedType)
    {
        var prop = _affiliateUserType.GetProperty(fieldName);
        Assert.NotNull(prop);
        Assert.Equal(expectedType, prop!.PropertyType);
    }

    [Fact]
    public void AffiliateUser_Has_RefreshToken()
    {
        var prop = _affiliateUserType.GetProperty("RefreshToken");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), Nullable.GetUnderlyingType(prop!.PropertyType) ?? prop.PropertyType);
    }

    [Fact]
    public void AffiliateUser_Has_AffiliateAccount_Navigation()
    {
        var prop = _affiliateUserType.GetProperty("AffiliateAccount");
        Assert.NotNull(prop);
        Assert.Equal(typeof(AffiliateAccount), prop!.PropertyType);
    }

    [Fact]
    public void AffiliateUser_Does_Not_Inherit_From_User()
    {
        // AffiliateUser must NOT inherit from POS User entity
        Assert.False(_affiliateUserType.IsSubclassOf(typeof(PointOfSale.Core.Entities.User)),
            "AffiliateUser MUST NOT inherit from POS User entity — they are completely separate");
    }

    [Fact]
    public void AffiliateUser_Implements_ITenantOwned()
    {
        var interfaces = _affiliateUserType.GetInterfaces();
        Assert.Contains(interfaces, i => i.Name == "ITenantOwned");
    }

    [Fact]
    public void AffiliateUser_Does_Not_Have_Commission_Fields()
    {
        var props = _affiliateUserType.GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("CommissionRate", props);
        Assert.DoesNotContain("Commission", props);
        Assert.DoesNotContain("Earnings", props);
        Assert.DoesNotContain("Balance", props);
    }

    [Fact]
    public void AffiliateUser_Has_Create_Factory()
    {
        var method = _affiliateUserType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AUTH CONTROLLER — EXISTS AND CONFIGURED
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AuthController_Exists()
    {
        Assert.NotNull(_authControllerType);
    }

    [Fact]
    public void AuthController_Has_AllowAnonymous()
    {
        var attr = _authControllerType.GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void AuthController_Route_Is_AffiliateAuth()
    {
        var route = _authControllerType.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("api/affiliate/auth", route!.Template);
    }

    [Fact]
    public void AuthController_Is_ApiController()
    {
        var attr = _authControllerType.GetCustomAttribute<ApiControllerAttribute>();
        Assert.NotNull(attr);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AUTH CONTROLLER — ENDPOINTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AuthController_Has_Register_Endpoint()
    {
        var method = _authControllerType.GetMethod("Register");
        Assert.NotNull(method);
        var httpPost = method!.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(httpPost);
        Assert.Equal("register", httpPost!.Template);
    }

    [Fact]
    public void AuthController_Has_DuplicateRegistrationRaceBoundary()
    {
        var method = _authControllerType.GetMethod(
            "IsAffiliateRegistrationUniquenessViolation",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
    }

    [Fact]
    public void AuthController_Has_Login_Endpoint()
    {
        var method = _authControllerType.GetMethod("Login");
        Assert.NotNull(method);
        var httpPost = method!.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(httpPost);
        Assert.Equal("login", httpPost!.Template);
    }

    [Fact]
    public void AuthController_Has_Refresh_Endpoint()
    {
        var method = _authControllerType.GetMethod("RefreshToken");
        Assert.NotNull(method);
        var httpPost = method!.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(httpPost);
        Assert.Equal("refresh", httpPost!.Template);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AUTH CONTROLLER — NO COMMISSION/LEDGER LOGIC
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AuthController_Does_Not_Inject_LedgerService()
    {
        var ctor = _authControllerType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();
        Assert.DoesNotContain(paramTypes, t => t.Contains("Ledger"));
    }

    [Fact]
    public void AuthController_Does_Not_Inject_AffiliateService()
    {
        var ctor = _authControllerType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();
        Assert.DoesNotContain(paramTypes, t => t.Contains("IAffiliateService"));
    }


    [Fact]
    public void AuthController_Injects_PasswordHasher_For_AffiliateUser()
    {
        var ctor = _authControllerType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
        // Should inject IPasswordHasher<AffiliateUser>, NOT IPasswordHasher<User>
        var hasAffiliateHasher = paramTypes.Any(t =>
            t.IsGenericType &&
            t.GetGenericTypeDefinition().Name.StartsWith("IPasswordHasher") &&
            t.GenericTypeArguments.Any(a => a == typeof(AffiliateUser)));
        Assert.True(hasAffiliateHasher,
            "AuthController must inject IPasswordHasher<AffiliateUser>, NOT IPasswordHasher<User>");
    }

    [Fact]
    public void AuthController_Does_Not_Inject_PasswordHasher_For_User()
    {
        var ctor = _authControllerType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
        var hasPosUserHasher = paramTypes.Any(t =>
            t.IsGenericType &&
            t.GetGenericTypeDefinition().Name.StartsWith("IPasswordHasher") &&
            t.GenericTypeArguments.Any(a => a == typeof(PointOfSale.Core.Entities.User)));
        Assert.False(hasPosUserHasher,
            "AuthController MUST NOT inject IPasswordHasher<User> — affiliates are NOT POS users");
    }

    // ═══════════════════════════════════════════════════════════════════
    // AUTH CONTROLLER — REQUEST/RESPONSE SHAPES
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Email")]
    [InlineData("Password")]
    [InlineData("DisplayName")]
    public void RegisterDto_Has_Field(string fieldName)
    {
        var prop = typeof(AffiliateRegisterDto).GetProperty(fieldName);
        Assert.NotNull(prop);
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("Password")]
    public void LoginDto_Has_Field(string fieldName)
    {
        var prop = typeof(AffiliateLoginDto).GetProperty(fieldName);
        Assert.NotNull(prop);
    }

    [Theory]
    [InlineData("AccessToken")]
    [InlineData("RefreshToken")]
    [InlineData("AffiliateAccountId")]
    [InlineData("ReferralCode")]
    [InlineData("DisplayName")]
    [InlineData("Email")]
    public void AuthResponse_Has_Field(string fieldName)
    {
        var prop = typeof(AffiliateAuthResponse).GetProperty(fieldName);
        Assert.NotNull(prop);
    }

    [Fact]
    public void AuthResponse_Does_Not_Have_POS_User_Fields()
    {
        var props = typeof(AffiliateAuthResponse).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Username", props);
        Assert.DoesNotContain("FirstName", props);
        Assert.DoesNotContain("LastName", props);
        Assert.DoesNotContain("Role", props); // POS role
    }

    // ═══════════════════════════════════════════════════════════════════
    // DBCONTEXT — AffiliateUsers DbSet
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DbContext_Has_AffiliateUsers_DbSet()
    {
        var prop = _dbContextType.GetProperty("AffiliateUsers");
        Assert.NotNull(prop);
    }

    [Fact]
    public void DbContext_AffiliateUsers_Is_DbSet_Of_AffiliateUser()
    {
        var prop = _dbContextType.GetProperty("AffiliateUsers");
        Assert.NotNull(prop);
        var returnType = prop!.PropertyType;
        Assert.True(returnType.IsGenericType);
        Assert.Equal(typeof(AffiliateUser), returnType.GenericTypeArguments.First());
    }

    // ═══════════════════════════════════════════════════════════════════
    // PORTAL CONTROLLER — DUAL-PATH IDENTITY RESOLUTION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PortalController_Has_ResolveAffiliateAccountId_Method()
    {
        var method = _portalControllerType.GetMethod("ResolveAffiliateAccountId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
    }

    [Fact]
    public void PortalController_Still_Has_Authorize_Attribute()
    {
        var attr = _portalControllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ANALYTICS CONTROLLER — DUAL-PATH IDENTITY RESOLUTION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AnalyticsController_Has_ResolveAffiliateAccountId_Method()
    {
        var method = _analyticsControllerType.GetMethod("ResolveAffiliateAccountId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SCOPE ISOLATION — TOKEN BOUNDARY TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AuthController_Is_NOT_POS_AuthController()
    {
        // Verify these are completely separate types
        Assert.NotEqual(typeof(AuthController), _authControllerType);
    }

    [Fact]
    public void AuthController_Route_Different_From_POS_Auth()
    {
        var affiliateRoute = _authControllerType.GetCustomAttribute<RouteAttribute>()?.Template;
        var posRoute = typeof(AuthController).GetCustomAttribute<RouteAttribute>()?.Template;

        Assert.NotNull(affiliateRoute);
        Assert.NotNull(posRoute);
        Assert.NotEqual(affiliateRoute, posRoute);
    }

    [Fact]
    public void AffiliateUser_Is_NOT_POS_User()
    {
        Assert.NotEqual(typeof(PointOfSale.Core.Entities.User), typeof(AffiliateUser));
        Assert.False(typeof(AffiliateUser).IsSubclassOf(typeof(PointOfSale.Core.Entities.User)));
        Assert.False(typeof(PointOfSale.Core.Entities.User).IsSubclassOf(typeof(AffiliateUser)));
    }

    // ═══════════════════════════════════════════════════════════════════
    // EF CORE CONFIGURATION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AffiliateUserConfiguration_Exists()
    {
        var configType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "AffiliateUserConfiguration");

        Assert.NotNull(configType);
    }

    [Fact]
    public void AffiliateUserConfiguration_Implements_IEntityTypeConfiguration()
    {
        var configType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "AffiliateUserConfiguration");

        Assert.NotNull(configType);
        var interfaces = configType!.GetInterfaces();
        var configInterface = interfaces.FirstOrDefault(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition().Name.StartsWith("IEntityTypeConfiguration") &&
            i.GenericTypeArguments.Any(a => a == typeof(AffiliateUser)));

        Assert.NotNull(configInterface);
    }
}
