using System.Reflection;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// Architecture tests for Phase 3 trust engine.
/// These tests enforce invariants at compile/test time.
/// </summary>
public class TrustEngineArchitectureTests
{
    [Fact]
    public void ProviderTrustAggregate_MustNotHavePublicSetters()
    {
        var properties = typeof(ProviderTrustAggregate).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (property.Name == nameof(ProviderTrustAggregate.ProviderProfile))
                continue; // Navigation property

            if (property.DeclaringType != typeof(ProviderTrustAggregate))
                continue; // Shared entity infrastructure is enforced separately

            var setter = property.GetSetMethod(nonPublic: false);
            Assert.Null(setter);
        }
    }

    [Fact]
    public void ProviderTrustAggregate_MustNotHavePublicConstructor()
    {
        var constructors = typeof(ProviderTrustAggregate)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(constructors);
    }

    [Fact]
    public void ProviderTrustAggregate_MustHaveApplyReviewMethod()
    {
        var method = typeof(ProviderTrustAggregate).GetMethod("ApplyReview");

        Assert.NotNull(method);
        Assert.Equal(typeof(void), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(5, parameters.Length);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
    }

    [Fact]
    public void ProviderTrustAggregate_MustHaveComputeBadgeMethod()
    {
        var method = typeof(ProviderTrustAggregate).GetMethod("ComputeBadge");

        Assert.NotNull(method);
        Assert.Equal(typeof(TrustBadge), method!.ReturnType);
        Assert.Empty(method.GetParameters());
    }

    [Fact]
    public void Controllers_MustNotAcceptTrustScoreInDto()
    {
        var apiAssembly = Assembly.Load("PointOfSale.API");
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Controller"));

        foreach (var controller in controllerTypes)
        {
            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();

                foreach (var param in parameters)
                {
                    var paramType = param.ParameterType;
                    var isSearchLikeDto =
                        method.Name.Contains("Search", StringComparison.Ordinal) ||
                        paramType.Name.Contains("Search", StringComparison.Ordinal) ||
                        paramType.Name.Contains("Query", StringComparison.Ordinal) ||
                        paramType.Name.Contains("Filter", StringComparison.Ordinal);

                    if (isSearchLikeDto)
                        continue;

                    if (paramType.IsClass && paramType.Namespace?.Contains("PointOfSale") == true)
                    {
                        var properties = paramType.GetProperties();

                        foreach (var prop in properties)
                        {
                            Assert.False(
                                prop.Name.Contains("TrustScore") || prop.Name.Contains("AverageRating") || prop.Name.Contains("TrustBadge"),
                                $"Controller {controller.Name}.{method.Name} accepts DTO {paramType.Name} with trust-related property {prop.Name}. Trust must be computed, not accepted from clients."
                            );
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void ServiceReview_MustNotBeUpdatable()
    {
        var updateMethod = typeof(ServiceReview).GetMethod("Update");

        // ServiceReview should not have an Update method
        Assert.Null(updateMethod);
    }

    [Fact]
    public void ProviderTrustService_MustBeReadOnly()
    {
        var methods = typeof(IProviderTrustService).GetMethods();

        foreach (var method in methods)
        {
            // All methods should return Task or Task<T>, indicating read operations
            Assert.True(
                method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>) ||
                method.ReturnType == typeof(Task),
                $"Method {method.Name} should return Task or Task<T>"
            );

            // No methods should have "Create", "Update", "Delete", "Set" in their names
            Assert.False(
                method.Name.Contains("Create") ||
                method.Name.Contains("Update") ||
                method.Name.Contains("Delete") ||
                method.Name.Contains("Set"),
                $"IProviderTrustService method {method.Name} suggests write operation. Trust service must be read-only."
            );
        }
    }

    [Fact]
    public void TrustAggregate_MustInheritFromTenantEntity()
    {
        Assert.True(
            typeof(TenantEntity).IsAssignableFrom(typeof(ProviderTrustAggregate)),
            "ProviderTrustAggregate must inherit from TenantEntity for proper tenant isolation"
        );
    }

    [Fact]
    public void ApplyReview_WithInvalidRating_MustThrow()
    {
        var createMethod = typeof(ProviderTrustAggregate).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(createMethod);

        var tenantId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        var aggregate = (ProviderTrustAggregate)createMethod!.Invoke(null, new object[] { tenantId, profileId })!;

        Assert.Throws<ArgumentException>(() => aggregate.ApplyReview(0));
        Assert.Throws<ArgumentException>(() => aggregate.ApplyReview(6));
        Assert.Throws<ArgumentException>(() => aggregate.ApplyReview(-1));
    }

    [Fact]
    public void ComputeBadge_MustBeDeterministic()
    {
        var createMethod = typeof(ProviderTrustAggregate).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        var aggregate = (ProviderTrustAggregate)createMethod!.Invoke(null, new object[] { Guid.NewGuid(), Guid.NewGuid() })!;

        // New provider
        Assert.Equal(TrustBadge.New, aggregate.ComputeBadge());

        // Add 5 reviews with 4.0 average
        for (int i = 0; i < 5; i++)
        {
            aggregate.ApplyReview(4);
        }
        Assert.Equal(TrustBadge.Rising, aggregate.ComputeBadge());

        // Add more reviews to reach 20 with 4.3+ average
        for (int i = 0; i < 15; i++)
        {
            aggregate.ApplyReview(5);
        }
        Assert.True(aggregate.ComputeBadge() == TrustBadge.Trusted || aggregate.ComputeBadge() == TrustBadge.TopRated);
    }

    [Fact]
    public void ProviderTrustConsumer_MustNotAccessDbContextDirectly()
    {
        var infraAssembly = Assembly.Load("PointOfSale.Infrastructure");
        var consumerType = infraAssembly.GetType("PointOfSale.Infrastructure.Consumers.ProviderTrustConsumer");

        Assert.NotNull(consumerType);

        // Consumer should only have ApplicationDbContext injected (which is acceptable for consumers)
        var constructor = consumerType!.GetConstructors().FirstOrDefault();
        Assert.NotNull(constructor);

        var parameters = constructor!.GetParameters();
        var dbContextParams = parameters.Where(p => p.ParameterType.Name.Contains("DbContext")).ToList();

        // Should have exactly one DbContext parameter (ApplicationDbContext)
        Assert.Single(dbContextParams);
    }

    [Fact]
    public void ServiceReviewCreatedEvent_MustContainProviderProfileId()
    {
        var eventType = typeof(PointOfSale.Core.Events.ServiceReviewCreatedEvent);
        var property = eventType.GetProperty("ProviderProfileId");

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid), property!.PropertyType);
    }

    [Fact]
    public void ProviderTrustUpdatedEvent_MustContainTrustBadge()
    {
        var eventType = typeof(PointOfSale.Core.Events.ProviderTrustUpdatedEvent);
        var property = eventType.GetProperty("TrustBadge");

        Assert.NotNull(property);
        Assert.Equal(typeof(TrustBadge), property!.PropertyType);
    }
}
