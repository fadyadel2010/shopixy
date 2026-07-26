using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.Controllers;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.Tests.SuperAdmin;

public sealed class AdminAffiliateCategoryGovernanceTests
{
    [Fact]
    public void GlobalAffiliateCategoryMutations_MustRequireSuperAdminPolicy()
    {
        var controllerAuthorize = typeof(AdminAffiliateCategoryController)
            .GetCustomAttribute<AuthorizeAttribute>(inherit: true);

        Assert.NotNull(controllerAuthorize);
        Assert.True(string.IsNullOrWhiteSpace(controllerAuthorize!.Roles));

        AssertMethodPolicy(nameof(AdminAffiliateCategoryController.CreateMainCategory));
        AssertMethodPolicy(nameof(AdminAffiliateCategoryController.CreateSubcategory));
        AssertMethodPolicy(nameof(AdminAffiliateCategoryController.UpdateCategory));
        AssertMethodPolicy(nameof(AdminAffiliateCategoryController.ActivateCategory));
        AssertMethodPolicy(nameof(AdminAffiliateCategoryController.DeactivateCategory));
    }

    [Fact]
    public void GlobalAffiliateCategoryReads_MayRemainTenantAdminVisible()
    {
        AssertMethodRoles(nameof(AdminAffiliateCategoryController.GetCategoryTree), "Owner,Admin");
        AssertMethodRoles(nameof(AdminAffiliateCategoryController.GetCategory), "Owner,Admin");
    }

    private static void AssertMethodPolicy(string methodName)
    {
        var method = typeof(AdminAffiliateCategoryController).GetMethod(methodName);
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>(inherit: true);
        Assert.NotNull(authorize);
        Assert.Equal(SuperAdminSecurityConstants.PolicyName, authorize!.Policy);
    }

    private static void AssertMethodRoles(string methodName, string expectedRoles)
    {
        var method = typeof(AdminAffiliateCategoryController).GetMethod(methodName);
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>(inherit: true);
        Assert.NotNull(authorize);
        Assert.Equal(expectedRoles, authorize!.Roles);
    }
}
