using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Controllers;
using PointOfSale.Infrastructure.MongoDB.Services;
using Xunit;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-4: Affiliate Portal API architecture tests.
///
/// Validates:
/// 1. Controller authorization attributes
/// 2. Route conventions
/// 3. DTO shape completeness
/// 4. No business logic in controller
/// 5. MongoDB-only data source for financial endpoints
/// </summary>
public class AffiliatePortalApiTests
{
    private readonly Type _controllerType = typeof(AffiliatePortalController);

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
    public void Controller_IsApiController()
    {
        var attr = _controllerType.GetCustomAttribute<ApiControllerAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void Controller_HasCorrectRoutePrefix()
    {
        var attr = _controllerType.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("api/affiliate", attr!.Template);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ENDPOINT ROUTES
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("GetDashboard", "dashboard")]
    [InlineData("GetOrders", "orders")]
    [InlineData("GetEarningsSummary", "earnings-summary")]
    [InlineData("GetProfile", "profile")]
    public void ReadEndpoint_HasCorrectRoute(string methodName, string expectedRoute)
    {
        var method = _controllerType.GetMethod(methodName);
        Assert.NotNull(method);

        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal(expectedRoute, httpGet!.Template);
    }

    [Fact]
    public void RegisterEndpoint_IsHttpPost_OnAuthController()
    {
        // Register was moved from AffiliatePortalController to AffiliateAuthController
        var authControllerType = typeof(AffiliateAuthController);
        var method = authControllerType.GetMethod("Register");
        Assert.NotNull(method);

        var httpPost = method!.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(httpPost);
        Assert.Equal("register", httpPost!.Template);
    }

    [Fact]
    public void ReadEndpoints_StayHttpGet_AndSelfServiceMutationsUseExplicitWriteVerbs()
    {
        foreach (var methodName in new[]
                 {
                     "GetDashboard",
                     "GetOrders",
                     "GetEarningsSummary",
                     "GetProfile",
                     "GetLandingPages",
                     "DiscoverCategories",
                     "DiscoverProducts",
                     "GetProductDetail",
                     "GetPayouts"
                 })
        {
            var method = _controllerType.GetMethod(methodName);
            Assert.NotNull(method);
            Assert.NotNull(method!.GetCustomAttribute<HttpGetAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpPutAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpDeleteAttribute>());
        }

        var updateWallet = _controllerType.GetMethod("UpdatePayoutWallet");
        Assert.NotNull(updateWallet);
        Assert.NotNull(updateWallet!.GetCustomAttribute<HttpPutAttribute>());
        Assert.Null(updateWallet.GetCustomAttribute<HttpPostAttribute>());
        Assert.Null(updateWallet.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(updateWallet.GetCustomAttribute<HttpDeleteAttribute>());

        var requestPayout = _controllerType.GetMethod("RequestPayout");
        Assert.NotNull(requestPayout);
        Assert.NotNull(requestPayout!.GetCustomAttribute<HttpPostAttribute>());
        Assert.Null(requestPayout.GetCustomAttribute<HttpPutAttribute>());
        Assert.Null(requestPayout.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(requestPayout.GetCustomAttribute<HttpDeleteAttribute>());
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONSTRUCTOR — DEPENDENCY VALIDATION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_InjectsMongoDatabase()
    {
        var ctor = _controllerType.GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.FullName ?? "").ToList();

        Assert.Contains(paramTypes, t => t.Contains("MongoDbContext"));
    }

    [Fact]
    public void Controller_InjectsAffiliateService()
    {
        var ctor = _controllerType.GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();

        Assert.Contains(paramTypes, t => t.Contains("IAffiliateService"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // DTO SHAPES
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DashboardResponse_HasSettlementFields()
    {
        var props = typeof(AffiliateDashboardResponse).GetProperties()
            .Select(p => p.Name).ToList();

        Assert.Contains("TotalEarned", props);
        Assert.Contains("TotalApproved", props);
        Assert.Contains("TotalPaid", props);
        Assert.Contains("TotalReversed", props);
        Assert.Contains("NetEarnings", props);
        Assert.Contains("TotalOrders", props);
    }

    [Fact]
    public void OrdersResponse_HasPagination()
    {
        var props = typeof(AffiliateOrdersResponse).GetProperties()
            .Select(p => p.Name).ToList();

        Assert.Contains("Page", props);
        Assert.Contains("PageSize", props);
        Assert.Contains("TotalCount", props);
        Assert.Contains("TotalPages", props);
        Assert.Contains("Orders", props);
    }

    [Fact]
    public void OrderDto_HasAllRequiredFields()
    {
        var props = typeof(AffiliateOrderDto).GetProperties()
            .Select(p => p.Name).ToList();

        Assert.Contains("OrderId", props);
        Assert.Contains("OrderNumber", props);
        Assert.Contains("Status", props);
        Assert.Contains("TotalCommission", props);
        Assert.Contains("Currency", props);
        Assert.Contains("CreatedAtUtc", props);
        Assert.Contains("Items", props);
    }

    [Fact]
    public void OrderItemDto_HasSettlementStatus()
    {
        var props = typeof(AffiliateOrderItemDto).GetProperties()
            .Select(p => p.Name).ToList();

        Assert.Contains("SettlementStatus", props);
        Assert.Contains("IsReversed", props);
        Assert.Contains("CommissionAmount", props);
        Assert.Contains("ProductName", props);
    }

    [Fact]
    public void ProfileResponse_HasReferralCode()
    {
        var props = typeof(AffiliateProfileResponse).GetProperties()
            .Select(p => p.Name).ToList();

        Assert.Contains("ReferralCode", props);
        Assert.Contains("DisplayName", props);
        Assert.Contains("Status", props);
    }

    [Fact]
    public void EarningsSummaryDto_HasPerCurrencyBreakdown()
    {
        var props = typeof(AffiliateEarningsSummaryDto).GetProperties()
            .Select(p => p.Name).ToList();

        Assert.Contains("Currency", props);
        Assert.Contains("TotalEarned", props);
        Assert.Contains("TotalApproved", props);
        Assert.Contains("TotalPaid", props);
        Assert.Contains("NetEarnings", props);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DEFAULT VALUES
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DashboardResponse_Defaults_AreZero()
    {
        var dto = new AffiliateDashboardResponse();
        Assert.Equal(0m, dto.TotalEarned);
        Assert.Equal(0m, dto.TotalApproved);
        Assert.Equal(0m, dto.TotalPaid);
        Assert.Equal(0m, dto.NetEarnings);
        Assert.Equal(0, dto.TotalOrders);
        Assert.Null(dto.LastCommissionAtUtc);
    }

    [Fact]
    public void OrderItemDto_Default_SettlementStatus_IsEarned()
    {
        var dto = new AffiliateOrderItemDto();
        Assert.Equal("Earned", dto.SettlementStatus);
        Assert.False(dto.IsReversed);
    }

    [Fact]
    public void OrdersResponse_Default_HasEmptyList()
    {
        var dto = new AffiliateOrdersResponse();
        Assert.NotNull(dto.Orders);
        Assert.Empty(dto.Orders);
    }

    // ═══════════════════════════════════════════════════════════════════
    // STATUS FILTER SUPPORT
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetOrders_HasStatusFilterParameter()
    {
        var method = _controllerType.GetMethod("GetOrders");
        Assert.NotNull(method);

        var statusParam = method!.GetParameters()
            .FirstOrDefault(p => p.Name == "status");
        Assert.NotNull(statusParam);
        Assert.True(statusParam!.HasDefaultValue);
        Assert.Null(statusParam.DefaultValue);
    }

    [Fact]
    public void GetOrders_HasPageAndPageSizeParameters()
    {
        var method = _controllerType.GetMethod("GetOrders");
        Assert.NotNull(method);

        var paramNames = method!.GetParameters().Select(p => p.Name).ToList();
        Assert.Contains("page", paramNames);
        Assert.Contains("pageSize", paramNames);
    }

    // ═══════════════════════════════════════════════════════════════════
    // NO PATCH / DELETE OPERATIONS — portal supports explicit wallet + payout self-service mutations
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_Has_Single_HttpPut_Endpoint_ForPayoutWallet()
    {
        var methods = _controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        var putMethods = methods
            .Where(method => method.GetCustomAttribute<HttpPutAttribute>() != null)
            .Select(method => method.Name)
            .ToList();

        Assert.Single(putMethods);
        Assert.Equal("UpdatePayoutWallet", putMethods[0]);
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
    // ARCHITECTURE: NO COMMISSION RATES IN RESPONSE DTOs
    // The UI must never see commission rates — prevents client-side
    // calculation. Only pre-computed amounts from MongoDB.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DashboardDto_DoesNot_Expose_CommissionRate()
    {
        var props = typeof(AffiliateDashboardResponse).GetProperties()
            .Select(p => p.Name).ToList();

        Assert.DoesNotContain("CommissionRate", props);
        Assert.DoesNotContain("Rate", props);
    }

    [Fact]
    public void OrderDto_DoesNot_Expose_CommissionRate()
    {
        var props = typeof(AffiliateOrderDto).GetProperties()
            .Select(p => p.Name).ToList();

        Assert.DoesNotContain("CommissionRate", props);
    }

    [Fact]
    public void OrderItemDto_DoesNot_Expose_CommissionRate()
    {
        var props = typeof(AffiliateOrderItemDto).GetProperties()
            .Select(p => p.Name).ToList();

        Assert.DoesNotContain("CommissionRate", props);
    }

    // ═══════════════════════════════════════════════════════════════════
    // STATUS FIELD IS STRING (not enum integer)
    // Kill List #10: Never expose integer enum values to frontend
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProfileResponse_Status_IsString()
    {
        var prop = typeof(AffiliateProfileResponse).GetProperty("Status");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    [Fact]
    public void OrderDto_Status_IsString()
    {
        var prop = typeof(AffiliateOrderDto).GetProperty("Status");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    [Fact]
    public void OrderItemDto_SettlementStatus_IsString()
    {
        var prop = typeof(AffiliateOrderItemDto).GetProperty("SettlementStatus");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }
}
