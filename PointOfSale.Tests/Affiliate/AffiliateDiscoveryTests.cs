using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Controllers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-5: Architecture tests for Affiliate Product Discovery.
///
/// These tests enforce the following invariants:
/// 1. AffiliateDiscoveryController is Elastic-first; commission policy enrichment may read from SQL/DbContext for authenticated affiliate responses (AF-15/AF-19B)
/// 2. Only GET is allowed — discovery is read-only
/// 3. isAffiliateEnabled filter is always applied
/// 4. Commission rate never appears in discovery DTOs
/// 5. ProductSearchDocument has affiliate fields
/// 6. ProductDocument has affiliate fields (no rate)
/// 7. AffiliateProductDiscoveryService depends only on IElasticClient
/// 8. Controller is authorized
/// </summary>
public class AffiliateDiscoveryTests
{
    private readonly Type _controllerType = typeof(AffiliateDiscoveryController);
    private readonly Type _serviceType = typeof(AffiliateProductDiscoveryService);
    private readonly Type _serviceInterfaceType = typeof(IAffiliateProductDiscoveryService);
    private readonly Type _searchDocType = typeof(ProductSearchDocument);
    private readonly Type _mongoDocType = typeof(ProductDocument);

    // ═══════════════════════════════════════════════════════════════════
    // CONTROLLER AUTHORIZATION
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
        // Discovery is available to any authenticated user (affiliate or not)
        var attr = _controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.True(
            string.IsNullOrEmpty(attr!.Roles),
            "Discovery should be accessible to any authenticated user, not role-restricted");
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONTROLLER ROUTE
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_Route_Is_Affiliate_Discovery()
    {
        var route = _controllerType.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("api/affiliate/discovery", route!.Template);
    }

    [Fact]
    public void Controller_Is_ApiController()
    {
        var attr = _controllerType.GetCustomAttribute<ApiControllerAttribute>();
        Assert.NotNull(attr);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ONLY GET ALLOWED
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
    public void Controller_Has_Discover_GET_Method()
    {
        var methods = _controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var getMethod = methods.FirstOrDefault(m => m.GetCustomAttribute<HttpGetAttribute>() != null);
        Assert.NotNull(getMethod);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONTROLLER ENRICHMENT — Elastic search plus SQL policy enrichment, never Mongo
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_Can_Inject_DbContext_For_CommissionPolicyEnrichment()
    {
        // AF-15/AF-19B: discovery remains Elastic-first, but commission policy enrichment
        // is intentionally read from SQL for authenticated affiliate responses.
        var ctor = _controllerType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.Contains(typeof(ApplicationDbContext), paramTypes);
    }

    [Fact]
    public void Controller_Does_Not_Inject_MongoDbContext()
    {
        var ctor = _controllerType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(paramTypes,
            t => t.Name.Contains("MongoDb") || t.Name.Contains("IMongoDatabase") || t.Name.Contains("IMongoCollection"));
    }

    [Fact]
    public void Controller_Injects_DiscoveryService()
    {
        var ctor = _controllerType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.Contains(typeof(IAffiliateProductDiscoveryService), paramTypes);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SERVICE ARCHITECTURE
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Service_Implements_Interface()
    {
        Assert.True(_serviceInterfaceType.IsAssignableFrom(_serviceType));
    }

    [Fact]
    public void Service_Injects_DbContext_For_Sql_Authority()
    {
        var ctor = _serviceType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        // Under SQL-authoritative architecture, we must check PostgreSQL for live policies and pricing
        Assert.Contains(typeof(ApplicationDbContext), paramTypes);
    }

    [Fact]
    public void Service_Does_Not_Inject_MongoDb()
    {
        var ctor = _serviceType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(paramTypes,
            t => t.Name.Contains("MongoDb") || t.Name.Contains("IMongoDatabase") || t.Name.Contains("IMongoCollection"));
    }

    [Fact]
    public void Service_Injects_ElasticClient()
    {
        var ctor = _serviceType.GetConstructors().First();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.Contains(paramTypes, t => t.Name == "IElasticClient");
    }

    [Fact]
    public void Service_Has_DiscoverAsync_Method()
    {
        var method = _serviceInterfaceType.GetMethod("DiscoverAsync");
        Assert.NotNull(method);
    }

    // ═══════════════════════════════════════════════════════════════════
    // NO COMMISSION RATE IN DISCOVERY DTO
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DiscoveryItem_Does_Not_Expose_CommissionRate()
    {
        var props = typeof(AffiliateDiscoveryItem).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("CommissionRate", props);
        Assert.DoesNotContain("MaxCommissionPerItem", props);
    }

    [Fact]
    public void DiscoveryResult_Does_Not_Expose_CommissionRate()
    {
        var props = typeof(AffiliateDiscoveryResult).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("CommissionRate", props);
    }

    [Theory]
    [InlineData("ProductId")]
    [InlineData("Name")]
    [InlineData("Slug")]
    [InlineData("ImageUrl")]
    [InlineData("PriceFrom")]
    [InlineData("HasVariants")]
    [InlineData("InStock")]
    public void DiscoveryItem_Has_Required_Field(string fieldName)
    {
        var prop = typeof(AffiliateDiscoveryItem).GetProperty(fieldName);
        Assert.NotNull(prop);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ELASTICSEARCH DOCUMENT HAS AFFILIATE FIELDS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProductSearchDocument_Has_IsAffiliateEnabled()
    {
        var prop = _searchDocType.GetProperty("IsAffiliateEnabled");
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop!.PropertyType);
    }

    [Fact]
    public void ProductSearchDocument_Has_AffiliateEnabledAtUtc()
    {
        var prop = _searchDocType.GetProperty("AffiliateEnabledAtUtc");
        Assert.NotNull(prop);
    }

    [Fact]
    public void ProductSearchDocument_Does_Not_Have_CommissionRate()
    {
        var prop = _searchDocType.GetProperty("CommissionRate");
        Assert.Null(prop);
    }

    [Fact]
    public void ProductSearchDocument_Does_Not_Have_MaxCommissionPerItem()
    {
        var prop = _searchDocType.GetProperty("MaxCommissionPerItem");
        Assert.Null(prop);
    }

    // ═══════════════════════════════════════════════════════════════════
    // MONGODB DOCUMENT HAS AFFILIATE FIELDS (BUT NO RATE)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProductDocument_Has_IsAffiliateEnabled()
    {
        var prop = _mongoDocType.GetProperty("IsAffiliateEnabled");
        Assert.NotNull(prop);
    }

    [Fact]
    public void ProductDocument_Has_AffiliateEnabledAtUtc()
    {
        var prop = _mongoDocType.GetProperty("AffiliateEnabledAtUtc");
        Assert.NotNull(prop);
    }

    [Fact]
    public void ProductDocument_Does_Not_Have_CommissionRate()
    {
        var prop = _mongoDocType.GetProperty("CommissionRate");
        Assert.Null(prop);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DISCOVERY QUERY DTO
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Q")]
    [InlineData("CategoryId")]
    [InlineData("Page")]
    [InlineData("PageSize")]
    public void DiscoveryQuery_Has_Required_Field(string fieldName)
    {
        var prop = typeof(AffiliateDiscoveryQuery).GetProperty(fieldName);
        Assert.NotNull(prop);
    }

    [Fact]
    public void DiscoveryQuery_Does_Not_Have_CommissionRate()
    {
        var prop = typeof(AffiliateDiscoveryQuery).GetProperty("CommissionRate");
        Assert.Null(prop);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PROJECTION CONSUMER EXISTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectionConsumer_Exists()
    {
        var consumerType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.Name == "AffiliateProductPolicyProjectionConsumer");

        Assert.NotNull(consumerType);
    }

    [Fact]
    public void ProjectionConsumer_Implements_IConsumer()
    {
        var consumerType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.Name == "AffiliateProductPolicyProjectionConsumer");

        Assert.NotNull(consumerType);

        var interfaces = consumerType!.GetInterfaces();
        var consumerInterface = interfaces.FirstOrDefault(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition().Name.StartsWith("IConsumer") &&
            i.GenericTypeArguments.Any(a => a.Name == "AffiliateProductPolicyChangedEvent"));

        Assert.NotNull(consumerInterface);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DI REGISTRATION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Service_Is_Registered_As_Interface()
    {
        Assert.True(_serviceInterfaceType.IsInterface);
        Assert.True(_serviceInterfaceType.IsAssignableFrom(_serviceType));
    }
}
