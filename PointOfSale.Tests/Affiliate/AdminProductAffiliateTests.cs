using System.Reflection;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-4C: Architecture tests for the Admin Product Affiliate Enablement API.
///
/// These tests verify:
/// 1. Authorization is restricted to Owner/Admin roles
/// 2. Route follows /api/admin/products/{productId}/affiliate
/// 3. Only GET + POST methods exist (no PUT/PATCH/DELETE)
/// 4. DTOs don't expose commission rate in public APIs
/// 5. Controller uses IAffiliateService for writes (no direct DB)
/// 6. Controller does not publish authoritative policy events post-commit
/// 7. ProductDocument has IsAffiliateEnabled but NOT CommissionRate
/// 8. Event has correct shape
/// 9. Disable is soft-disable, not delete
/// </summary>
public class AdminProductAffiliateTests
{
    private readonly Type _controllerType = typeof(AdminProductAffiliateController);

    // ═══════════════════════════════════════════════════════════════════
    // AUTHORIZATION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_HasAuthorizeAttribute()
    {
        var attr = _controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void Controller_IsRestrictedTo_OwnerAndAdmin()
    {
        var attr = _controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Contains("Owner", attr!.Roles!);
        Assert.Contains("Admin", attr!.Roles!);
    }

    [Fact]
    public void Controller_DoesNotAllow_Manager()
    {
        var attr = _controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        var roles = attr!.Roles!.Split(',').Select(r => r.Trim()).ToList();
        Assert.DoesNotContain("Manager", roles);
    }

    [Fact]
    public void Controller_DoesNotAllow_Cashier()
    {
        var attr = _controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        var roles = attr!.Roles!.Split(',').Select(r => r.Trim()).ToList();
        Assert.DoesNotContain("Cashier", roles);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ROUTE CONVENTIONS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_HasCorrectBaseRoute()
    {
        var attr = _controllerType.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("api/admin/products/{productId:guid}/affiliate", attr!.Template);
    }

    [Fact]
    public void Controller_IsApiController()
    {
        Assert.NotNull(_controllerType.GetCustomAttribute<ApiControllerAttribute>());
    }

    // ═══════════════════════════════════════════════════════════════════
    // ENDPOINT METHODS — includes category/template management and combined enable flow
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_Has_Exactly_6_HttpEndpoints()
    {
        var methods = _controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
            .ToList();

        Assert.Equal(6, methods.Count);
    }

    [Theory]
    [InlineData("GetSettings", typeof(HttpGetAttribute))]
    [InlineData("EnableAffiliate", typeof(HttpPostAttribute))]
    [InlineData("EnableAffiliateWithCategory", typeof(HttpPostAttribute))]
    [InlineData("DisableAffiliate", typeof(HttpPostAttribute))]
    [InlineData("UpdateTemplate", typeof(HttpPostAttribute))]
    [InlineData("UpdateCategory", typeof(HttpPostAttribute))]
    public void Endpoint_HasCorrectHttpMethod(string methodName, Type expectedAttrType)
    {
        var method = _controllerType.GetMethod(methodName);
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute(expectedAttrType));
    }

    // ═══════════════════════════════════════════════════════════════════
    // NO PUT / PATCH / DELETE
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_HasNo_HttpPut_Endpoints()
    {
        var methods = _controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var method in methods)
            Assert.Null(method.GetCustomAttribute<HttpPutAttribute>());
    }

    [Fact]
    public void Controller_HasNo_HttpPatch_Endpoints()
    {
        var methods = _controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var method in methods)
            Assert.Null(method.GetCustomAttribute<HttpPatchAttribute>());
    }

    [Fact]
    public void Controller_HasNo_HttpDelete_Endpoints()
    {
        var methods = _controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var method in methods)
            Assert.Null(method.GetCustomAttribute<HttpDeleteAttribute>());
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONSTRUCTOR DEPENDENCIES
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_RequiresAffiliateService()
    {
        var ctor = _controllerType.GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Contains(typeof(IAffiliateService), paramTypes);
    }

    [Fact]
    public void Constructor_RequiresApplicationDbContext()
    {
        var ctor = _controllerType.GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Contains(typeof(ApplicationDbContext), paramTypes);
    }

    [Fact]
    public void Constructor_DoesNotRequireEventPublisher()
    {
        var ctor = _controllerType.GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
        Assert.DoesNotContain(paramTypes, type => type.Name == "IEventPublisher");
    }

    [Fact]
    public void Constructor_DoesNot_HaveMongoDbContext()
    {
        // AF-4C reads policy from Postgres only — no Mongo read needed
        var ctor = _controllerType.GetConstructors().Single();
        var paramNames = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();
        Assert.DoesNotContain("MongoDbContext", paramNames);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DTO SHAPE — ProductAffiliateSettingsResponse
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("ProductId", typeof(Guid))]
    [InlineData("IsEnabled", typeof(bool))]
    [InlineData("CommissionRate", typeof(decimal?))]
    [InlineData("MaxCommissionPerItem", typeof(decimal?))]
    [InlineData("TemplateType", typeof(string))]
    [InlineData("AffiliateCategoryId", typeof(Guid?))]
    public void SettingsResponse_HasRequiredField(string propName, Type expectedType)
    {
        var prop = typeof(ProductAffiliateSettingsResponse).GetProperty(propName);
        Assert.NotNull(prop);
        Assert.Equal(expectedType, prop!.PropertyType);
    }

    [Fact]
    public void SettingsResponse_HasExactly6Properties()
    {
        var props = typeof(ProductAffiliateSettingsResponse).GetProperties();
        Assert.Equal(6, props.Length);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DTO SHAPE — EnableAffiliateRequest
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void EnableRequest_HasCommissionRate()
    {
        var prop = typeof(EnableAffiliateRequest).GetProperty("CommissionRate");
        Assert.NotNull(prop);
        Assert.Equal(typeof(decimal), prop!.PropertyType);
    }

    [Fact]
    public void EnableRequest_HasMaxCommissionPerItem()
    {
        var prop = typeof(EnableAffiliateRequest).GetProperty("MaxCommissionPerItem");
        Assert.NotNull(prop);
        Assert.Equal(typeof(decimal?), prop!.PropertyType);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SECURITY — No commission rate in ProductDocument (MongoDB/Elastic)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProductDocument_Has_IsAffiliateEnabled()
    {
        var prop = typeof(ProductDocument).GetProperty("IsAffiliateEnabled");
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop!.PropertyType);
    }

    [Fact]
    public void ProductDocument_DoesNot_Expose_CommissionRate()
    {
        var props = typeof(ProductDocument).GetProperties()
            .Select(p => p.Name).ToList();
        Assert.DoesNotContain("CommissionRate", props);
        Assert.DoesNotContain("AffiliateCommissionRate", props);
    }

    [Fact]
    public void ProductDocument_DoesNot_Expose_MaxCommissionPerItem()
    {
        var props = typeof(ProductDocument).GetProperties()
            .Select(p => p.Name).ToList();
        Assert.DoesNotContain("MaxCommissionPerItem", props);
        Assert.DoesNotContain("AffiliateMaxCommission", props);
    }

    // ═══════════════════════════════════════════════════════════════════
    // EVENT SHAPE — AffiliateProductPolicyChangedEvent
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PolicyChangedEvent_Exists()
    {
        Assert.NotNull(typeof(AffiliateProductPolicyChangedEvent));
    }

    [Theory]
    [InlineData("ProductId", typeof(Guid))]
    [InlineData("IsEnabled", typeof(bool))]
    [InlineData("CommissionRate", typeof(decimal))]
    [InlineData("MaxCommissionPerItem", typeof(decimal?))]
    public void PolicyChangedEvent_HasField(string propName, Type expectedType)
    {
        var prop = typeof(AffiliateProductPolicyChangedEvent).GetProperty(propName);
        Assert.NotNull(prop);
        Assert.Equal(expectedType, prop!.PropertyType);
    }

    [Fact]
    public void PolicyChangedEvent_HasTenantId_FromBase()
    {
        // TenantId is on IntegrationEvent base class
        var prop = typeof(AffiliateProductPolicyChangedEvent).GetProperty("TenantId");
        Assert.NotNull(prop);
    }

    // ═══════════════════════════════════════════════════════════════════
    // IAffiliateService — DisableAffiliateForProductAsync exists
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IAffiliateService_Has_EnableAffiliateForProductAsync()
    {
        var method = typeof(IAffiliateService).GetMethod("EnableAffiliateForProductAsync");
        Assert.NotNull(method);
    }

    [Fact]
    public void IAffiliateService_Has_DisableAffiliateForProductAsync()
    {
        var method = typeof(IAffiliateService).GetMethod("DisableAffiliateForProductAsync");
        Assert.NotNull(method);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ENDPOINT PARAMETERS — productId always present
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("GetSettings")]
    [InlineData("EnableAffiliate")]
    [InlineData("EnableAffiliateWithCategory")]
    [InlineData("DisableAffiliate")]
    [InlineData("UpdateTemplate")]
    [InlineData("UpdateCategory")]
    public void Endpoint_AcceptsGuidProductId(string methodName)
    {
        var method = _controllerType.GetMethod(methodName);
        Assert.NotNull(method);
        var param = method!.GetParameters().FirstOrDefault(p => p.Name == "productId");
        Assert.NotNull(param);
        Assert.Equal(typeof(Guid), param!.ParameterType);
    }

    [Fact]
    public void EnableAffiliate_AcceptsRequestBody()
    {
        var method = _controllerType.GetMethod("EnableAffiliate");
        Assert.NotNull(method);
        var bodyParam = method!.GetParameters().FirstOrDefault(p => p.ParameterType == typeof(EnableAffiliateRequest));
        Assert.NotNull(bodyParam);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DISABLE = SOFT DISABLE — verify no HttpDelete
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DisableEndpoint_Is_HttpPost_NotDelete()
    {
        var method = _controllerType.GetMethod("DisableAffiliate");
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<HttpPostAttribute>());
        Assert.Null(method!.GetCustomAttribute<HttpDeleteAttribute>());
    }
}
