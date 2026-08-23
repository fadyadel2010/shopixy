using Microsoft.Extensions.DependencyInjection;
using Moq;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.API.Services.ProductAuthoring.Bulk;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Interfaces.Bulk;
using PointOfSale.Tests.Architecture;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkFoundationArchitectureTests
{
    private static readonly string[] BulkProductionFiles =
    [
        "PointOfSale.API/Services/ProductAuthoring/Bulk/BulkProductRequestValidator.cs",
        "PointOfSale.API/Services/ProductAuthoring/Bulk/BulkProductLoader.cs",
        "PointOfSale.API/Services/ProductAuthoring/Bulk/BulkPreparationService.cs",
        "PointOfSale.API/Services/ProductAuthoring/Bulk/BulkOperationResultAggregator.cs",
        "PointOfSale.API/Services/ProductAuthoring/Bulk/BulkIdempotencyService.cs",
        "PointOfSale.API/Services/ProductAuthoring/Bulk/BulkAuthorizationService.cs"
    ];

    [Fact]
    public void RequestContract_CannotCarryTenantUserOrOperationAuthority()
    {
        var propertyNames = typeof(BulkProductRequest)
            .GetProperties()
            .Concat(typeof(BulkProductRequestItem).GetProperties())
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Tenant", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("User", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Action", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Operation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Loader_RetainsTenantFilterAndUsesOneSetBasedNoTrackingQueryShape()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/ProductAuthoring/Bulk/BulkProductLoader.cs");

        Assert.Contains("RequiredTenantId", source, StringComparison.Ordinal);
        Assert.Contains(".AsNoTracking()", source, StringComparison.Ordinal);
        Assert.Contains("productIds.Contains(product.Id)", source, StringComparison.Ordinal);
        Assert.Contains("product.TenantId == tenantId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IgnoreQueryFilters", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FoundationFiles_ContainNoBusinessMutationOrSideEffectInfrastructure()
    {
        var source = string.Join(
            Environment.NewLine,
            BulkProductionFiles.Select(CertificationSource.ReadRepoFile));
        var prohibited = new[]
        {
            "SaveChanges",
            "ExecuteSql",
            "FromSql",
            "IgnoreQueryFilters",
            "ProductAuthoringOrchestrator",
            "IProductAuthoringOrchestrator",
            "IEventPublisher",
            "IMarketplaceEventOutboxService",
            "IPublishEndpoint",
            "IDistributedCache",
            "IsActive =",
            "PublishStatus =",
            "IsOnlineVisible =",
            "IsDeleted ="
        };

        foreach (var term in prohibited)
        {
            Assert.DoesNotContain(term, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Phase6E_ExposesBackendDeleteWithoutFrontendActivation()
    {
        var controller = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/ProductsController.cs");
        var productsPage = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/pages/Products.jsx");

        var requiredRoutes = new[]
        {
            "bulk/activate",
            "bulk/deactivate",
            "bulk/publish",
            "bulk/unpublish",
            "bulk/delete"
        };

        foreach (var route in requiredRoutes)
        {
            Assert.Contains(route, controller, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(route, productsPage, StringComparison.OrdinalIgnoreCase);
        }

    }

    [Fact]
    public void FoundationServices_AreRegisteredThroughExistingAuthoringBoundary()
    {
        var services = new ServiceCollection();

        services.AddProductAuthoringInfrastructure();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBulkProductRequestValidator) &&
            descriptor.ImplementationType == typeof(BulkProductRequestValidator));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBulkProductLoader) &&
            descriptor.ImplementationType == typeof(BulkProductLoader));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBulkPreparationService) &&
            descriptor.ImplementationType == typeof(BulkPreparationService));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBulkOperationResultAggregator) &&
            descriptor.ImplementationType == typeof(BulkOperationResultAggregator));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBulkIdempotencyService) &&
            descriptor.ImplementationType == typeof(BulkIdempotencyService));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBulkAuthorizationService) &&
            descriptor.ImplementationType == typeof(BulkAuthorizationService));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBulkProductLifecycleService) &&
            descriptor.ImplementationType == typeof(BulkProductLifecycleService));
    }

    [Fact]
    public void LifecycleService_UsesAuthoringAuthorityWithoutDirectSideEffectBypasses()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/ProductAuthoring/Bulk/BulkProductLifecycleService.cs");

        Assert.Contains("_orchestrator.AuthorAsync", source, StringComparison.Ordinal);
        Assert.Contains("ProductAuthoringOperation.Update", source, StringComparison.Ordinal);
        Assert.Contains("ProductAuthoringOperation.Archive", source, StringComparison.Ordinal);
        Assert.Contains("ProductAuthoringOperation.Remove", source, StringComparison.Ordinal);
        Assert.Contains("CanDeleteProductAsync", source, StringComparison.Ordinal);
        Assert.Contains("preparedProductIds.Contains(product.Id)", source, StringComparison.Ordinal);
        Assert.Contains("product.TenantId == tenantId", source, StringComparison.Ordinal);
        Assert.Contains("_context.ChangeTracker.Clear()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefaultAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteUpdate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Remove(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".RemoveRange(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IgnoreQueryFilters", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginTransaction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IEventPublisher", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IMarketplaceEventOutboxService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IDistributedCache", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Mongo", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Elastic", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("product.IsActive =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("product.PublishStatus =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("product.IsOnlineVisible =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("product.IsDeleted =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductMedia", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_UsesActionSpecificRoutesAndManagerMutationBoundary()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/ProductsController.cs");

        Assert.Contains("[HttpPost(\"bulk/activate\")]", source, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"bulk/deactivate\")]", source, StringComparison.Ordinal);
        Assert.Equal(2, Count(source, "BulkOperationType.Activate") + Count(source, "BulkOperationType.Deactivate"));
        Assert.True(Count(source, "[Authorize(Roles = \"Owner,Admin,Manager\")]") >= 4);
        Assert.DoesNotContain("bulk/action", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase6D_PublishUnpublishRoutes_BindEndpointSelectedOperationsUnderManagerBoundary()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/ProductsController.cs");

        Assert.Contains("[HttpPost(\"bulk/publish\")]", source, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"bulk/unpublish\")]", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "BulkOperationType.Publish"));
        Assert.Equal(1, Count(source, "BulkOperationType.Unpublish"));

        foreach (var route in new[] { "publish", "unpublish" })
        {
            var attributeIndex = source.IndexOf(
                $"[HttpPost(\"bulk/{route}\")]",
                StringComparison.Ordinal);
            var boundaryIndex = source.IndexOf(
                "[Authorize(Roles = \"Owner,Admin,Manager\")]",
                attributeIndex,
                StringComparison.Ordinal);
            Assert.True(boundaryIndex > attributeIndex);
            Assert.True(boundaryIndex - attributeIndex < 200);
        }
    }

    [Fact]
    public void Phase6E_DeleteRoute_BindsServerOperationUnderOwnerAdminBoundary()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/ProductsController.cs");

        var routeIndex = source.IndexOf("[HttpPost(\"bulk/delete\")]", StringComparison.Ordinal);
        var authorizationIndex = source.IndexOf(
            "[Authorize(Roles = \"Owner,Admin\")]",
            routeIndex,
            StringComparison.Ordinal);

        Assert.True(routeIndex >= 0);
        Assert.True(authorizationIndex > routeIndex);
        Assert.True(authorizationIndex - routeIndex < 100);
        Assert.Equal(1, Count(source, "BulkOperationType.Delete"));
        Assert.DoesNotContain("bulk/action", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BulkIdempotencyService_DistinguishesPublishAndUnpublishFingerprints()
    {
        var items = new[] { new NormalizedBulkProductItem(Guid.NewGuid(), 7u) };
        var idempotencyService = new BulkIdempotencyService(
            Mock.Of<PointOfSale.API.Infrastructure.Idempotency.IProductMutationIdempotencyService>(),
            new TestTenantContext(Guid.NewGuid()));
        var publishFingerprint = idempotencyService.BuildFingerprint(BulkOperationType.Publish, items);

        Assert.NotEqual(
            publishFingerprint,
            idempotencyService.BuildFingerprint(BulkOperationType.Unpublish, items),
            StringComparer.Ordinal);
        Assert.Equal(
            publishFingerprint,
            idempotencyService.BuildFingerprint(BulkOperationType.Publish, items),
            StringComparer.Ordinal);
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static IEnumerable<int> IndicesOf(string source, string value)
    {
        var searchIndex = 0;
        while (true)
        {
            var found = source.IndexOf(value, searchIndex, StringComparison.Ordinal);
            if (found < 0)
            {
                yield break;
            }

            yield return found;
            searchIndex = found + value.Length;
        }
    }
}
