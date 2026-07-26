using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-4B: Architecture tests for the Admin Affiliate Management API.
///
/// These tests verify:
/// 1. Authorization is restricted to Owner/Admin roles
/// 2. Route conventions follow /api/admin/affiliates/*
/// 3. Only GET + POST methods exist (no PUT/PATCH/DELETE)
/// 4. DTO shapes are correct and don't expose sensitive data
/// 5. Controller dependencies are correct
/// 6. Settlement DTOs have required fields
/// </summary>
public class AdminAffiliateApiTests
{
    private readonly Type _controllerType = typeof(AdminAffiliateController);

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
        Assert.Equal("api/admin/affiliates", attr!.Template);
    }

    [Fact]
    public void Controller_IsApiController()
    {
        Assert.NotNull(_controllerType.GetCustomAttribute<ApiControllerAttribute>());
    }

    [Theory]
    [InlineData("ListAffiliates", typeof(HttpGetAttribute))]
    [InlineData("GetAffiliateDetail", typeof(HttpGetAttribute))]
    [InlineData("GetAffiliateOrders", typeof(HttpGetAttribute))]
    [InlineData("GetAffiliateEarnings", typeof(HttpGetAttribute))]
    [InlineData("ApproveCommissions", typeof(HttpPostAttribute))]
    [InlineData("RecordPayment", typeof(HttpPostAttribute))]
    [InlineData("SuspendAffiliate", typeof(HttpPostAttribute))]
    [InlineData("ActivateAffiliate", typeof(HttpPostAttribute))]
    public void Endpoint_HasCorrectHttpMethod(string methodName, Type expectedAttrType)
    {
        var method = _controllerType.GetMethod(methodName);
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute(expectedAttrType));
    }

    // ═══════════════════════════════════════════════════════════════════
    // ENDPOINT COUNT — includes settlement, payout, backfill, health, and debug/admin support actions
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_Has_Exactly_19_HttpEndpoints()
    {
        var methods = _controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
            .ToList();

        Assert.Equal(19, methods.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // NO PUT / PATCH / DELETE — admin portal is settlement-oriented
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_HasNo_HttpPut_Endpoints()
    {
        var methods = _controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var method in methods)
        {
            Assert.Null(method.GetCustomAttribute<HttpPutAttribute>());
        }
    }

    [Fact]
    public void Controller_HasNo_HttpPatch_Endpoints()
    {
        var methods = _controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var method in methods)
        {
            Assert.Null(method.GetCustomAttribute<HttpPatchAttribute>());
        }
    }

    [Fact]
    public void Controller_HasNo_HttpDelete_Endpoints()
    {
        var methods = _controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var method in methods)
        {
            Assert.Null(method.GetCustomAttribute<HttpDeleteAttribute>());
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONSTRUCTOR DEPENDENCIES
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_RequiresMongoDbContext()
    {
        var ctor = _controllerType.GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Contains(typeof(MongoDbContext), paramTypes);
    }

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
    public void Constructor_RequiresTenantContext()
    {
        var ctor = _controllerType.GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Contains(typeof(ITenantContext), paramTypes);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DTO SHAPE TESTS — AdminAffiliateListItem
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("AffiliateAccountId", typeof(Guid))]
    [InlineData("DisplayName", typeof(string))]
    [InlineData("ReferralCode", typeof(string))]
    [InlineData("Status", typeof(string))]
    [InlineData("TotalEarned", typeof(decimal))]
    [InlineData("TotalApproved", typeof(decimal))]
    [InlineData("TotalPaid", typeof(decimal))]
    [InlineData("NetEarnings", typeof(decimal))]
    [InlineData("TotalOrders", typeof(int))]
    public void AdminAffiliateListItem_HasRequiredFields(string propName, Type expectedType)
    {
        var prop = typeof(AdminAffiliateListItem).GetProperty(propName);
        Assert.NotNull(prop);
        Assert.Equal(expectedType, prop!.PropertyType);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DTO SHAPE TESTS — AdminAffiliateDetailResponse
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("AffiliateAccountId")]
    [InlineData("DisplayName")]
    [InlineData("ReferralCode")]
    [InlineData("Status")]
    [InlineData("SuspensionReason")]
    [InlineData("TotalEarned")]
    [InlineData("TotalApproved")]
    [InlineData("TotalPaid")]
    [InlineData("TotalReversed")]
    [InlineData("NetEarnings")]
    [InlineData("TotalOrders")]
    [InlineData("LastCommissionAtUtc")]
    public void AdminAffiliateDetailResponse_HasField(string propName)
    {
        var prop = typeof(AdminAffiliateDetailResponse).GetProperty(propName);
        Assert.NotNull(prop);
    }

    [Fact]
    public void DetailResponse_Status_IsString()
    {
        var prop = typeof(AdminAffiliateDetailResponse).GetProperty("Status");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DTO SECURITY — no commission rates exposed
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ListItem_DoesNot_Expose_CommissionRate()
    {
        var props = typeof(AdminAffiliateListItem).GetProperties()
            .Select(p => p.Name).ToList();
        Assert.DoesNotContain("CommissionRate", props);
        Assert.DoesNotContain("Rate", props);
    }

    [Fact]
    public void DetailResponse_DoesNot_Expose_CommissionRate()
    {
        var props = typeof(AdminAffiliateDetailResponse).GetProperties()
            .Select(p => p.Name).ToList();
        Assert.DoesNotContain("CommissionRate", props);
        Assert.DoesNotContain("Rate", props);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SETTLEMENT ACTION DTOs
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SettlementRequest_HasOrderIds()
    {
        var prop = typeof(AdminSettlementActionRequest).GetProperty("OrderIds");
        Assert.NotNull(prop);
    }

    [Fact]
    public void SettlementRequest_HasApplyAll()
    {
        var prop = typeof(AdminSettlementActionRequest).GetProperty("ApplyAll");
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop!.PropertyType);
    }

    [Fact]
    public void SettlementResponse_HasProcessedCount()
    {
        var prop = typeof(AdminSettlementActionResponse).GetProperty("ProcessedCount");
        Assert.NotNull(prop);
        Assert.Equal(typeof(int), prop!.PropertyType);
    }

    [Fact]
    public void SettlementResponse_HasFailedCount()
    {
        var prop = typeof(AdminSettlementActionResponse).GetProperty("FailedCount");
        Assert.NotNull(prop);
        Assert.Equal(typeof(int), prop!.PropertyType);
    }

    [Fact]
    public void SettlementResponse_HasErrors()
    {
        var prop = typeof(AdminSettlementActionResponse).GetProperty("Errors");
        Assert.NotNull(prop);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SUSPEND REQUEST DTO
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SuspendRequest_HasReason()
    {
        var prop = typeof(AdminSuspendRequest).GetProperty("Reason");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LIST RESPONSE — pagination shape
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Affiliates")]
    [InlineData("Page")]
    [InlineData("PageSize")]
    [InlineData("TotalCount")]
    [InlineData("TotalPages")]
    public void AdminListResponse_HasPaginationField(string propName)
    {
        var prop = typeof(AdminAffiliateListResponse).GetProperty(propName);
        Assert.NotNull(prop);
    }

    [Fact]
    public void AdminListResponse_Default_HasEmptyList()
    {
        var dto = new AdminAffiliateListResponse();
        Assert.NotNull(dto.Affiliates);
        Assert.Empty(dto.Affiliates);
    }

    // ═══════════════════════════════════════════════════════════════════
    // APPROVE / PAY ENDPOINTS ACCEPT ID PARAMETER
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("ApproveCommissions")]
    [InlineData("RecordPayment")]
    [InlineData("SuspendAffiliate")]
    [InlineData("ActivateAffiliate")]
    public void WriteEndpoint_AcceptsGuidId(string methodName)
    {
        var method = _controllerType.GetMethod(methodName);
        Assert.NotNull(method);
        var idParam = method!.GetParameters().FirstOrDefault(p => p.Name == "id");
        Assert.NotNull(idParam);
        Assert.Equal(typeof(Guid), idParam!.ParameterType);
    }
}
