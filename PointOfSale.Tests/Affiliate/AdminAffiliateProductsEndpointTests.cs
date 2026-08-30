using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// Shopixy Affiliate Refactor A: tests for the bounded Affiliate Products read API.
///
/// Coverage:
/// 1. Endpoint authorization metadata (Owner/Admin only, GET-only read, dedicated route)
/// 2. Tenant source / no client tenant input
/// 3. Pagination, search, list shape
/// 4. Authority separation (Product vs AffiliateProductPolicy vs AffiliateCategory master data)
/// </summary>
public class AdminAffiliateProductsEndpointTests
{
    private readonly Type _controllerType = typeof(AdminAffiliateProductsController);

    // ═══════════════════════════════════════════════════════════════════
    // AUTHORIZATION METADATA
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Controller_IsRestrictedTo_OwnerAndAdmin()
    {
        var attr = _controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Contains("Owner", attr!.Roles!);
        Assert.Contains("Admin", attr!.Roles!);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Cashier")]
    public void Controller_DoesNotAllow_LowerRoles(string forbiddenRole)
    {
        var attr = _controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        var roles = attr!.Roles!.Split(',').Select(role => role.Trim());
        Assert.DoesNotContain(forbiddenRole, roles);
    }

    [Fact]
    public void Controller_UsesDedicatedRoute()
    {
        var attr = _controllerType.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("api/admin/affiliate/products", attr!.Template);
    }

    [Fact]
    public void Controller_IsApiController()
    {
        Assert.NotNull(_controllerType.GetCustomAttribute<ApiControllerAttribute>());
    }

    [Fact]
    public void List_IsReadOnly_GetEndpointOnly()
    {
        var methods = _controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        var endpoint = _controllerType.GetMethod(nameof(AdminAffiliateProductsController.List));
        Assert.NotNull(endpoint);
        Assert.NotNull(endpoint!.GetCustomAttribute<HttpGetAttribute>());

        var httpMethodAttributes = methods
            .SelectMany(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>())
            .ToList();

        Assert.Single(httpMethodAttributes);
        Assert.All(httpMethodAttributes, method => Assert.Equal("GET", method.HttpMethods.Single()));

        foreach (var method in methods)
        {
            Assert.Null(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpPutAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpPatchAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpDeleteAttribute>());
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TENANT SOURCE — NO CLIENT TENANT INPUT
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_RequiresTenantContextAndDbContext()
    {
        var ctor = _controllerType.GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.Contains(typeof(ApplicationDbContext), paramTypes);
        Assert.Contains(typeof(ITenantContext), paramTypes);
    }

    [Fact]
    public void List_AcceptsNoTenantUserOrRoleInputFromClient()
    {
        var endpoint = _controllerType.GetMethod(nameof(AdminAffiliateProductsController.List));
        Assert.NotNull(endpoint);

        var forbiddenParameterNames = new[] { "tenantid", "userid", "role", "roles" };
        foreach (var parameter in endpoint!.GetParameters())
        {
            Assert.DoesNotContain(parameter.Name!.ToLowerInvariant(), forbiddenParameterNames);
        }
    }

    [Fact]
    public async Task List_DerivesTenantExclusivelyFromTrustedTenantContext()
    {
        var trustedTenantId = Guid.NewGuid();
        var foreignClaimTenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId)))
        {
            await SeedProductAsync(context, trustedTenantId, "Trusted Product", isActive: true);
            await SeedProductAsync(context, foreignClaimTenantId, "Foreign Product", isActive: true);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId));
        var controller = BuildController(dbContext, trustedTenantId, foreignClaimTenantId);

        var result = await controller.List(ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);
        var item = Assert.Single(payload);
        Assert.Equal("Trusted Product", item.ProductName);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LIST SHAPE — INCLUDES PRODUCTS WITHOUT POLICY + AUTHORITY SEPARATION
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task List_ReturnsProductsWithoutPolicy_AndSeparatesAuthorities()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var category = AffiliateCategory.CreateMainCategory("Phones", "phones");
            context.AffiliateCategories.Add(category);

            var enabledProduct = await SeedProductAsync(context, tenantId, "Enabled Product", isActive: false);
            context.AffiliateProductPolicies.Add(AffiliateProductPolicy.Create(
                tenantId,
                enabledProduct.Id,
                commissionRate: 0.15m,
                maxPerItem: 40m,
                categoryId: category.Id));

            await SeedProductAsync(context, tenantId, "Plain Product", isActive: true);

            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);
        Assert.Equal(2, payload.Count);

        var enabled = payload.Single(item => item.ProductName == "Enabled Product");
        Assert.False(enabled.IsProductActive);
        Assert.True(enabled.IsAffiliateEnabled);
        Assert.Equal(0.15m, enabled.CommissionRate);
        Assert.Equal(40m, enabled.MaxCommissionPerItem);
        Assert.NotNull(enabled.AffiliateCategoryId);
        Assert.Equal("Phones", enabled.AffiliateCategoryName);

        var plain = payload.Single(item => item.ProductName == "Plain Product");
        Assert.True(plain.IsProductActive);
        Assert.False(plain.IsAffiliateEnabled);
        Assert.Null(plain.CommissionRate);
        Assert.Null(plain.MaxCommissionPerItem);
        Assert.Null(plain.AffiliateCategoryId);
        Assert.Null(plain.AffiliateCategoryName);
    }

    [Fact]
    public async Task List_IgnoresPoliciesFromOtherTenants()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var product = await SeedProductAsync(context, tenantId, "Owned Product", isActive: true);

            context.AffiliateProductPolicies.Add(AffiliateProductPolicy.Create(
                foreignTenantId, product.Id, 0.20m, null, Guid.NewGuid()));

            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);
        var item = Assert.Single(payload);
        Assert.False(item.IsAffiliateEnabled);
        Assert.Null(item.CommissionRate);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PAGINATION + SEARCH
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task List_AppliesBoundedPaginationWithDeterministicOrdering()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            for (var index = 1; index <= 5; index++)
            {
                await SeedProductAsync(context, tenantId, $"Alpha {index:00}", isActive: true);
            }

            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(page: 2, pageSize: 2, ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);

        Assert.Equal(2, payload.Count);
        Assert.Equal(new[] { "Alpha 03", "Alpha 04" }, payload.Select(item => item.ProductName).ToArray());

        Assert.Equal("5", controller.Response.Headers["X-Total-Count"]);
        Assert.Equal("2", controller.Response.Headers["X-Page"]);
        Assert.Equal("2", controller.Response.Headers["X-Page-Size"]);
        Assert.Equal("3", controller.Response.Headers["X-Total-Pages"]);
    }

    [Fact]
    public async Task List_NormalizesInvalidPaginationInput()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            for (var index = 1; index <= 3; index++)
            {
                await SeedProductAsync(context, tenantId, $"Beta {index:00}", isActive: true);
            }

            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(page: -5, pageSize: 5000, ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);
        Assert.Equal(3, payload.Count);
        Assert.Equal("1", controller.Response.Headers["X-Page"]);
        Assert.Equal(PaginationGuard.MaxPageSize.ToString(), controller.Response.Headers["X-Page-Size"]);
    }

    [Fact]
    public async Task List_SearchesProductNameCaseInsensitively()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            await SeedProductAsync(context, tenantId, "Cotton Shirt", isActive: true);
            await SeedProductAsync(context, tenantId, "Wool Coat", isActive: true);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(search: "cotton", ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);
        var item = Assert.Single(payload);
        Assert.Equal("Cotton Shirt", item.ProductName);
        Assert.Equal("1", controller.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public async Task List_ExcludesSoftDeletedProducts()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var deleted = await SeedProductAsync(context, tenantId, "Deleted Product", isActive: true);
            deleted.IsDeleted = true;
            await SeedProductAsync(context, tenantId, "Live Product", isActive: true);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);
        var item = Assert.Single(payload);
        Assert.Equal("Live Product", item.ProductName);
    }

    [Theory]
    [InlineData("SKU-77")]
    [InlineData("sku-77")]
    public async Task List_SearchesSkuCaseInsensitively(string searchTerm)
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var skuProduct = await SeedProductAsync(context, tenantId, "Widget", isActive: true);
            skuProduct.SKU = "SKU-77";
            await SeedProductAsync(context, tenantId, "Other Product", isActive: true);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(search: searchTerm, ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);
        var item = Assert.Single(payload);
        Assert.Equal("Widget", item.ProductName);
        Assert.Equal("1", controller.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public async Task List_SearchesBarcode()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var barcodeProduct = await SeedProductAsync(context, tenantId, "Gadget", isActive: true);
            barcodeProduct.Barcode = "4006381333931";
            await SeedProductAsync(context, tenantId, "Other Product", isActive: true);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(search: "4006381333931", ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);
        var item = Assert.Single(payload);
        Assert.Equal("Gadget", item.ProductName);
    }

    [Fact]
    public async Task List_SearchesAffiliateCategoryAndParentCategoryNames()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var root = AffiliateCategory.CreateMainCategory("Electronics", "electronics");
        // EF assigns Ids on save; pin the parent Id up front so the subcategory
        // can be created and linked before any persistence occurs.
        root.Id = Guid.NewGuid();
        var child = AffiliateCategory.CreateSubcategory("Phones", "phones", root.Id);
        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            context.AffiliateCategories.Add(root);
            context.AffiliateCategories.Add(child);

            var childPolicyProduct = await SeedProductAsync(context, tenantId, "Phone Product", isActive: true);
            context.AffiliateProductPolicies.Add(AffiliateProductPolicy.Create(
                tenantId, childPolicyProduct.Id, 0.10m, null, child.Id));

            await SeedProductAsync(context, tenantId, "Unrelated Product", isActive: true);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));

        // By exact child category name.
        var byChildController = BuildController(dbContext, tenantId);
        var byChildResult = await byChildController.List(search: "phones", ct: CancellationToken.None);
        var byChildPayload = Assert.IsType<List<AffiliateProductListItem>>(
            Assert.IsType<OkObjectResult>(byChildResult.Result).Value);
        Assert.Equal("Phone Product", Assert.Single(byChildPayload).ProductName);

        // By parent category name — a policy on a subcategory must be discoverable.
        var byParentController = BuildController(dbContext, tenantId);
        var byParentResult = await byParentController.List(search: "electronics", ct: CancellationToken.None);
        var byParentPayload = Assert.IsType<List<AffiliateProductListItem>>(
            Assert.IsType<OkObjectResult>(byParentResult.Result).Value);
        Assert.Equal("Phone Product", Assert.Single(byParentPayload).ProductName);
    }

    [Fact]
    public async Task List_AffiliateStatusFilter_SeparatesEnabledDisabledAndNoPolicy()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var enabledProduct = await SeedProductAsync(context, tenantId, "Enabled Product", isActive: true);
            context.AffiliateProductPolicies.Add(AffiliateProductPolicy.Create(
                tenantId, enabledProduct.Id, 0.10m, null, Guid.NewGuid()));

            var disabledPolicyProduct = await SeedProductAsync(context, tenantId, "Persisted Disabled Product", isActive: true);
            var disabledPolicy = AffiliateProductPolicy.Create(
                tenantId, disabledPolicyProduct.Id, 0.15m, null, Guid.NewGuid());
            disabledPolicy.Disable();
            context.AffiliateProductPolicies.Add(disabledPolicy);

            await SeedProductAsync(context, tenantId, "No Policy Product", isActive: true);

            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));

        var enabledController = BuildController(dbContext, tenantId);
        var enabledResult = await enabledController.List(affiliateStatus: "enabled", ct: CancellationToken.None);
        var enabledPayload = Assert.IsType<List<AffiliateProductListItem>>(
            Assert.IsType<OkObjectResult>(enabledResult.Result).Value);
        var enabledItem = Assert.Single(enabledPayload);
        Assert.Equal("Enabled Product", enabledItem.ProductName);
        Assert.True(enabledItem.IsAffiliateEnabled);
        Assert.Equal(1, int.Parse(enabledController.Response.Headers["X-Total-Count"]));

        var disabledController = BuildController(dbContext, tenantId);
        var disabledResult = await disabledController.List(affiliateStatus: "disabled", ct: CancellationToken.None);
        var disabledPayload = Assert.IsType<List<AffiliateProductListItem>>(
            Assert.IsType<OkObjectResult>(disabledResult.Result).Value);
        Assert.Equal(2, disabledPayload.Count);
        Assert.All(disabledPayload, item => Assert.False(item.IsAffiliateEnabled));
        Assert.Contains(disabledPayload, item => item.ProductName == "No Policy Product");
        Assert.Contains(disabledPayload, item => item.ProductName == "Persisted Disabled Product");
        Assert.Equal(2, int.Parse(disabledController.Response.Headers["X-Total-Count"]));

        var allController = BuildController(dbContext, tenantId);
        var allResult = await allController.List(affiliateStatus: "all", ct: CancellationToken.None);
        var allPayload = Assert.IsType<List<AffiliateProductListItem>>(
            Assert.IsType<OkObjectResult>(allResult.Result).Value);
        Assert.Equal(3, allPayload.Count);
    }

    [Fact]
    public async Task List_ProductStatusFilter_UsesProductAuthorityOnly()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            await SeedProductAsync(context, tenantId, "Live Product", isActive: true);
            await SeedProductAsync(context, tenantId, "Archived Product", isActive: false);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));

        var activeController = BuildController(dbContext, tenantId);
        var activeResult = await activeController.List(productStatus: "active", ct: CancellationToken.None);
        var activePayload = Assert.IsType<List<AffiliateProductListItem>>(
            Assert.IsType<OkObjectResult>(activeResult.Result).Value);
        Assert.Equal("Live Product", Assert.Single(activePayload).ProductName);

        var inactiveController = BuildController(dbContext, tenantId);
        var inactiveResult = await inactiveController.List(productStatus: "inactive", ct: CancellationToken.None);
        var inactivePayload = Assert.IsType<List<AffiliateProductListItem>>(
            Assert.IsType<OkObjectResult>(inactiveResult.Result).Value);
        Assert.Equal("Archived Product", Assert.Single(inactivePayload).ProductName);
    }

    [Fact]
    public async Task List_CategoryFilter_MatchesExactGlobalAssignmentOnly()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var targetCategory = AffiliateCategory.CreateMainCategory("Home", "home");
        var otherCategory = AffiliateCategory.CreateMainCategory("Sports", "sports");

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            context.AffiliateCategories.Add(targetCategory);
            context.AffiliateCategories.Add(otherCategory);

            var assignedProduct = await SeedProductAsync(context, tenantId, "Assigned Product", isActive: true);
            context.AffiliateProductPolicies.Add(AffiliateProductPolicy.Create(
                tenantId, assignedProduct.Id, 0.10m, null, targetCategory.Id));

            var otherProduct = await SeedProductAsync(context, tenantId, "Other Assigned Product", isActive: true);
            context.AffiliateProductPolicies.Add(AffiliateProductPolicy.Create(
                tenantId, otherProduct.Id, 0.10m, null, otherCategory.Id));

            await SeedProductAsync(context, tenantId, "Unassigned Product", isActive: true);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(categoryId: targetCategory.Id, ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);
        var item = Assert.Single(payload);
        Assert.Equal("Assigned Product", item.ProductName);
        Assert.Equal(targetCategory.Id, item.AffiliateCategoryId);
    }

    [Fact]
    public async Task List_CombinesSearchAndFiltersWithCoherentCountsBeforePagination()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var category = AffiliateCategory.CreateMainCategory("Furniture", "furniture");

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            context.AffiliateCategories.Add(category);

            for (var index = 1; index <= 3; index++)
            {
                var product = await SeedProductAsync(context, tenantId, $"Chair {index:00}", isActive: true);
                if (index <= 2)
                {
                    context.AffiliateProductPolicies.Add(AffiliateProductPolicy.Create(
                        tenantId, product.Id, 0.10m, null, category.Id));
                }
            }

            var inactiveMatch = await SeedProductAsync(context, tenantId, "Chair Inactive", isActive: false);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(
            page: 1,
            pageSize: 2,
            search: "chair",
            affiliateStatus: "enabled",
            productStatus: "active",
            categoryId: category.Id,
            ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);

        // Two matching rows total, bounded page of two, coherent count metadata.
        Assert.Equal(2, payload.Count);
        Assert.Equal(2, int.Parse(controller.Response.Headers["X-Total-Count"]));
        Assert.Equal(1, int.Parse(controller.Response.Headers["X-Total-Pages"]));
        Assert.All(payload, item =>
        {
            Assert.True(item.IsAffiliateEnabled);
            Assert.True(item.IsProductActive);
            Assert.Equal(category.Id, item.AffiliateCategoryId);
        });
    }

    [Fact]
    public async Task List_ClampsOutOfRangePageToLastAvailablePage()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            for (var index = 1; index <= 5; index++)
            {
                await SeedProductAsync(context, tenantId, $"Clamp {index:00}", isActive: true);
            }

            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(page: 999, pageSize: 2, ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);

        Assert.Equal(new[] { "Clamp 05" }, payload.Select(item => item.ProductName).ToArray());
        Assert.Equal("3", controller.Response.Headers["X-Page"]);
        Assert.Equal("5", controller.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public async Task List_ReturnsEmptyCoherentPageWhenNothingMatches()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            await SeedProductAsync(context, tenantId, "Present Product", isActive: true);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(search: "does-not-match", ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);
        Assert.Empty(payload);
        Assert.Equal("0", controller.Response.Headers["X-Total-Count"]);
        Assert.Equal("0", controller.Response.Headers["X-Total-Pages"]);
    }

    [Fact]
    public async Task List_RejectsUnknownFilterValuesInsteadOfSilentlyBroadeningResults()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(
            affiliateStatus: "unexpected",
            productStatus: "active",
            ct: CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task List_ResolvesHierarchyForSubcategoryAssignments()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var root = AffiliateCategory.CreateMainCategory("Fashion", "fashion");
        root.Id = Guid.NewGuid();
        var child = AffiliateCategory.CreateSubcategory("Shoes", "shoes", root.Id);

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            context.AffiliateCategories.Add(root);
            context.AffiliateCategories.Add(child);

            var product = await SeedProductAsync(context, tenantId, "Sneaker", isActive: true);
            context.AffiliateProductPolicies.Add(AffiliateProductPolicy.Create(
                tenantId, product.Id, 0.10m, null, child.Id));
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = BuildController(dbContext, tenantId);

        var result = await controller.List(ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<List<AffiliateProductListItem>>(ok.Value);
        var item = Assert.Single(payload);
        Assert.Equal("Shoes", item.AffiliateCategoryName);
        Assert.Equal("Fashion", item.AffiliateParentCategoryName);
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static AdminAffiliateProductsController BuildController(
        ApplicationDbContext dbContext,
        Guid trustedTenantId,
        Guid? foreignClaimTenantId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, "Admin")
        };

        if (foreignClaimTenantId.HasValue)
        {
            claims.Add(new Claim("tenant_id", foreignClaimTenantId.Value.ToString()));
        }

        return new AdminAffiliateProductsController(
            dbContext,
            new TestTenantContext(trustedTenantId),
            NullLogger<AdminAffiliateProductsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            }
        };
    }

    private static async Task<Product> SeedProductAsync(
        ApplicationDbContext context,
        Guid tenantId,
        string name,
        bool isActive)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            CategoryId = Guid.NewGuid(),
            TaxRate = 0m,
            IsActive = isActive,
            PublishStatus = PublishStatus.Published,
            CreatedAt = DateTime.UtcNow
        };

        context.Products.Add(product);
        return await Task.FromResult(product);
    }
}
