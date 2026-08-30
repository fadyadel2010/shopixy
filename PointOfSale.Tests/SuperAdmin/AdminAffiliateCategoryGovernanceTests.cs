using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Controllers;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

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
    public void EveryExposedCategoryMutation_MustRequireSuperAdmin_AndNoDeleteOrPatchRouteExists()
    {
        var actions = typeof(AdminAffiliateCategoryController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => new
            {
                Method = method,
                HttpAttributes = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).ToArray()
            })
            .Where(action => action.HttpAttributes.Length > 0)
            .ToArray();

        var mutationActions = actions
            .Where(action => action.HttpAttributes
                .SelectMany(attribute => attribute.HttpMethods)
                .Any(verb => !string.Equals(verb, "GET", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.NotEmpty(mutationActions);
        Assert.All(mutationActions, action =>
        {
            var authorize = action.Method.GetCustomAttribute<AuthorizeAttribute>(inherit: true);
            Assert.NotNull(authorize);
            Assert.Equal(SuperAdminSecurityConstants.PolicyName, authorize!.Policy);
        });

        var exposedVerbs = actions
            .SelectMany(action => action.HttpAttributes)
            .SelectMany(attribute => attribute.HttpMethods)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("PATCH", exposedVerbs);
        Assert.DoesNotContain("DELETE", exposedVerbs);
    }

    [Fact]
    public void GlobalAffiliateCategoryReads_MayRemainTenantAdminVisible()
    {
        AssertMethodRoles(nameof(AdminAffiliateCategoryController.GetCategoryTree), "Owner,Admin");
        AssertMethodRoles(nameof(AdminAffiliateCategoryController.GetCategory), "Owner,Admin");
    }

    [Fact]
    public async Task DeactivateCategory_PublicationFailureEscapesBeforeMutationCommit()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"affiliate-category-publication-{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        var category = AffiliateCategory.CreateMainCategory("Electronics", "electronics");
        dbContext.AffiliateCategories.Add(category);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var publisher = new FailingCategoryEventPublisher(dbContext);
        var controller = new AdminAffiliateCategoryController(
            dbContext,
            NullLogger<AdminAffiliateCategoryController>.Instance,
            publisher);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.DeactivateCategory(category.Id, CancellationToken.None));

        Assert.True(publisher.SawPendingCategoryMutation);
        Assert.IsAssignableFrom<IGlobalPlatformIntegrationEvent>(publisher.PublishedEvent);
        Assert.Null(publisher.PublishedEvent!.TenantId);
        dbContext.ChangeTracker.Clear();
        Assert.True((await dbContext.AffiliateCategories.SingleAsync()).IsActive);
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

    private sealed class FailingCategoryEventPublisher(ApplicationDbContext dbContext) : IEventPublisher
    {
        public IntegrationEvent? PublishedEvent { get; private set; }
        public bool SawPendingCategoryMutation { get; private set; }

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            PublishedEvent = @event;
            SawPendingCategoryMutation = dbContext.ChangeTracker
                .Entries<AffiliateCategory>()
                .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
            throw new InvalidOperationException("Simulated outbox staging failure.");
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent => throw new NotSupportedException();

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent => throw new NotSupportedException();
    }
}
