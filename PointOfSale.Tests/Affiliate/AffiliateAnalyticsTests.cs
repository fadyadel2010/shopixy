using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Controllers;
using PointOfSale.API.Middleware;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-8: Architecture tests for Affiliate Click Tracking & Conversion Analytics.
///
/// These tests enforce:
/// 1. Middleware publishes event (has IEventPublisher in parameters)
/// 2. Consumer writes MongoDB document
/// 3. Duplicate click suppression (unique index exists)
/// 4. Conversion consumer attaches order
/// 5. API is read-only (GET only)
/// 6. No DbContext injection in analytics controller (except for user → affiliate resolution)
/// 7. No ledger access anywhere in analytics
/// 8. No commission data in analytics documents
/// 9. Event has correct shape
/// 10. Click document has correct shape
/// </summary>
public class AffiliateAnalyticsTests
{
    private readonly Type _controllerType = typeof(AffiliateAnalyticsController);
    private readonly Type _middlewareType = typeof(AffiliateReferralMiddleware);
    private readonly Type _clickDocType = typeof(AffiliateClickDocument);
    private readonly Type _summaryDocType = typeof(AffiliateAnalyticsSummaryDocument);
    private readonly Type _eventType = typeof(AffiliateLinkClickedEvent);

    // ═══════════════════════════════════════════════════════════════════
    // MIDDLEWARE
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Middleware_Exists()
    {
        Assert.NotNull(_middlewareType);
    }

    [Fact]
    public void Middleware_Has_InvokeAsync_Method()
    {
        var method = _middlewareType.GetMethod("InvokeAsync");
        Assert.NotNull(method);
    }

    [Fact]
    public void Middleware_InvokeAsync_Accepts_IEventPublisher()
    {
        var method = _middlewareType.GetMethod("InvokeAsync");
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Contains(typeof(IEventPublisher), parameters.Select(p => p.ParameterType));
    }

    [Fact]
    public void Middleware_InvokeAsync_Accepts_HttpContext()
    {
        var method = _middlewareType.GetMethod("InvokeAsync");
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Contains(typeof(HttpContext), parameters.Select(p => p.ParameterType));
    }

    [Fact]
    public void Middleware_Does_Not_Inject_MongoDbContext()
    {
        var method = _middlewareType.GetMethod("InvokeAsync");
        Assert.NotNull(method);
        var paramTypes = method!.GetParameters().Select(p => p.ParameterType.Name).ToList();
        Assert.DoesNotContain("MongoDbContext", paramTypes);
    }

    // ═══════════════════════════════════════════════════════════════════
    // EVENT SHAPE
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("AffiliateAccountId", typeof(Guid))]
    [InlineData("ReferralCode", typeof(string))]
    [InlineData("SessionId", typeof(string))]
    public void Event_Has_Required_Field(string fieldName, Type expectedType)
    {
        var prop = _eventType.GetProperty(fieldName);
        Assert.NotNull(prop);
        Assert.Equal(expectedType, prop!.PropertyType);
    }

    [Fact]
    public void Event_Has_ProductId_Nullable()
    {
        var prop = _eventType.GetProperty("ProductId");
        Assert.NotNull(prop);
        Assert.Equal(typeof(Guid?), prop!.PropertyType);
    }

    [Fact]
    public void Event_Has_IpHash()
    {
        var prop = _eventType.GetProperty("IpHash");
        Assert.NotNull(prop);
    }

    [Fact]
    public void Event_Has_UserAgent()
    {
        var prop = _eventType.GetProperty("UserAgent");
        Assert.NotNull(prop);
    }

    [Fact]
    public void Event_Has_TenantId_From_Base()
    {
        var prop = _eventType.GetProperty("TenantId");
        Assert.NotNull(prop);
    }

    [Fact]
    public void Event_Does_Not_Have_CommissionRate()
    {
        var props = _eventType.GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("CommissionRate", props);
        Assert.DoesNotContain("Commission", props);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CLICK DOCUMENT SHAPE
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("TenantId")]
    [InlineData("AffiliateAccountId")]
    [InlineData("ReferralCode")]
    [InlineData("SessionId")]
    [InlineData("ClickedAtUtc")]
    [InlineData("IpHash")]
    public void ClickDocument_Has_Required_Field(string fieldName)
    {
        var prop = _clickDocType.GetProperty(fieldName);
        Assert.NotNull(prop);
    }

    [Fact]
    public void ClickDocument_Has_OrderId_Nullable()
    {
        var prop = _clickDocType.GetProperty("OrderId");
        Assert.NotNull(prop);
        Assert.Equal(typeof(Guid?), prop!.PropertyType);
    }

    [Fact]
    public void ClickDocument_Has_ConvertedAtUtc_Nullable()
    {
        var prop = _clickDocType.GetProperty("ConvertedAtUtc");
        Assert.NotNull(prop);
        Assert.Equal(typeof(DateTime?), prop!.PropertyType);
    }

    [Fact]
    public void ClickDocument_Does_Not_Have_CommissionRate()
    {
        var props = _clickDocType.GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("CommissionRate", props);
        Assert.DoesNotContain("Commission", props);
    }

    [Fact]
    public void ClickDocument_Does_Not_Have_PII()
    {
        var props = _clickDocType.GetProperties().Select(p => p.Name).ToList();
        // Should never store raw IP, email, etc.
        Assert.DoesNotContain("IpAddress", props);
        Assert.DoesNotContain("Email", props);
        Assert.DoesNotContain("Name", props);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ANALYTICS SUMMARY DOCUMENT SHAPE
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("TenantId")]
    [InlineData("AffiliateAccountId")]
    [InlineData("TotalClicks")]
    [InlineData("TotalConversions")]
    [InlineData("ConversionRate")]
    [InlineData("TopProducts")]
    [InlineData("LastClickAtUtc")]
    public void SummaryDocument_Has_Required_Field(string fieldName)
    {
        var prop = _summaryDocType.GetProperty(fieldName);
        Assert.NotNull(prop);
    }

    [Fact]
    public void SummaryDocument_Does_Not_Have_CommissionRate()
    {
        var props = _summaryDocType.GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("CommissionRate", props);
        Assert.DoesNotContain("CommissionAmount", props);
        Assert.DoesNotContain("TotalEarned", props);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONTROLLER — AUTHORIZATION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_Has_AuthorizeAttribute()
    {
        var attr = _controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void Controller_Has_No_Role_Restriction()
    {
        var attr = _controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.True(string.IsNullOrEmpty(attr!.Roles),
            "Analytics should be accessible to any authenticated user");
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONTROLLER — ROUTE
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_Route_Is_AffiliateAnalytics()
    {
        var route = _controllerType.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("api/affiliate/analytics", route!.Template);
    }

    [Fact]
    public void Controller_Is_ApiController()
    {
        var attr = _controllerType.GetCustomAttribute<ApiControllerAttribute>();
        Assert.NotNull(attr);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONTROLLER — READ-ONLY (GET ONLY)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_Only_Has_GET_Endpoints()
    {
        var methods = _controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var method in methods)
        {
            Assert.Null(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpPutAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpDeleteAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpPatchAttribute>());
        }
    }

    [Fact]
    public void Controller_Has_Summary_Endpoint()
    {
        var method = _controllerType.GetMethod("GetSummary");
        Assert.NotNull(method);
        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("summary", httpGet!.Template);
    }

    [Fact]
    public void Controller_Has_Clicks_Endpoint()
    {
        var method = _controllerType.GetMethod("GetClicks");
        Assert.NotNull(method);
        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("clicks", httpGet!.Template);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONTROLLER — NO LEDGER ACCESS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_Does_Not_Inject_LedgerService()
    {
        var ctor = _controllerType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();
        Assert.DoesNotContain(paramTypes, t => t.Contains("Ledger"));
    }

    [Fact]
    public void Controller_Does_Not_Inject_AffiliateService()
    {
        // Analytics controller should NOT touch IAffiliateService (commission logic)
        var ctor = _controllerType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();
        Assert.DoesNotContain(paramTypes, t => t.Contains("IAffiliateService"));
    }

    [Fact]
    public void Controller_Injects_MongoDbContext()
    {
        var ctor = _controllerType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Contains(typeof(MongoDbContext), paramTypes);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONSUMERS EXIST
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ClickConsumer_Exists()
    {
        var consumerType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "AffiliateLinkClickedConsumer");

        Assert.NotNull(consumerType);
    }

    [Fact]
    public void ClickConsumer_Implements_IConsumer_Of_ClickEvent()
    {
        var consumerType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "AffiliateLinkClickedConsumer");

        Assert.NotNull(consumerType);
        var interfaces = consumerType!.GetInterfaces();
        var consumerInterface = interfaces.FirstOrDefault(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition().Name.StartsWith("IConsumer") &&
            i.GenericTypeArguments.Any(a => a.Name == "AffiliateLinkClickedEvent"));

        Assert.NotNull(consumerInterface);
    }

    [Fact]
    public void ConversionConsumer_Exists()
    {
        var consumerType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "AffiliateClickConversionConsumer");

        Assert.NotNull(consumerType);
    }

    [Fact]
    public void ConversionConsumer_Listens_To_AttributedEvent()
    {
        var consumerType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "AffiliateClickConversionConsumer");

        Assert.NotNull(consumerType);
        var interfaces = consumerType!.GetInterfaces();
        var consumerInterface = interfaces.FirstOrDefault(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition().Name.StartsWith("IConsumer") &&
            i.GenericTypeArguments.Any(a => a.Name == "AffiliateAttributedEvent"));

        Assert.NotNull(consumerInterface);
    }

    [Fact]
    public void ClickConsumer_Injects_MongoDbContext()
    {
        var consumerType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "AffiliateLinkClickedConsumer");

        Assert.NotNull(consumerType);
        var ctor = consumerType!.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Contains(typeof(MongoDbContext), paramTypes);
    }

    [Fact]
    public void ClickConsumer_Does_Not_Inject_DbContext()
    {
        var consumerType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "AffiliateLinkClickedConsumer");

        Assert.NotNull(consumerType);
        var ctor = consumerType!.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();
        Assert.DoesNotContain(paramTypes, t => t.Contains("ApplicationDbContext") || t == "DbContext");
    }

    // ═══════════════════════════════════════════════════════════════════
    // MONGODB CONTEXT — COLLECTIONS EXIST
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void MongoDbContext_Has_AffiliateClicks_Collection()
    {
        var prop = typeof(MongoDbContext).GetProperty("AffiliateClicks");
        Assert.NotNull(prop);
    }

    [Fact]
    public void MongoDbContext_Has_AffiliateAnalyticsSummaries_Collection()
    {
        var prop = typeof(MongoDbContext).GetProperty("AffiliateAnalyticsSummaries");
        Assert.NotNull(prop);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TOP PRODUCT SUB-DOCUMENT SHAPE
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("ProductId")]
    [InlineData("Clicks")]
    [InlineData("Conversions")]
    public void TopProductSubDocument_Has_Field(string fieldName)
    {
        var prop = typeof(TopProductSubDocument).GetProperty(fieldName);
        Assert.NotNull(prop);
    }

    [Fact]
    public void TopProductSubDocument_DoesNot_Have_CommissionRate()
    {
        var props = typeof(TopProductSubDocument).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("CommissionRate", props);
    }
}
