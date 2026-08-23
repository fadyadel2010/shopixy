using Microsoft.Extensions.DependencyInjection.Extensions;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services.ProductAuthoring.Bulk;
using PointOfSale.Core.Interfaces.Bulk;
using PointOfSale.Core.Interfaces.ProductAuthoring;

namespace PointOfSale.API.Services.ProductAuthoring;

public static class ProductAuthoringServiceCollectionExtensions
{
    public static IServiceCollection AddProductAuthoringInfrastructure(this IServiceCollection services)
    {
        services.TryAddScoped<IProductAuthoringValidator, ProductAuthoringValidator>();
        services.TryAddScoped<IProductAuthoringPolicy, ProductAuthoringPolicy>();
        services.TryAddScoped<IProductAuthoringTransactionObserver>(_ => NoOpProductAuthoringTransactionObserver.Instance);
        services.TryAddScoped<IProductMutationIdempotencyService, ProductMutationIdempotencyService>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IProductAuthoringCommandMapper, ProductAuthoringCommandMapper>();
        services.TryAddScoped<IProductAuthoringReconstructionService, ProductAuthoringReconstructionService>();
        services.AddScoped<IProductAuthoringOrchestrator, ProductAuthoringOrchestrator>();

        services.TryAddScoped<IBulkProductRequestValidator, BulkProductRequestValidator>();
        services.TryAddScoped<IBulkProductLoader, BulkProductLoader>();
        services.TryAddScoped<IBulkPreparationService, BulkPreparationService>();
        services.TryAddScoped<IBulkOperationResultAggregator, BulkOperationResultAggregator>();
        services.TryAddScoped<IBulkIdempotencyService, BulkIdempotencyService>();
        services.TryAddScoped<IBulkAuthorizationService, BulkAuthorizationService>();
        services.TryAddScoped<IBulkProductLifecycleService, BulkProductLifecycleService>();

        return services;
    }
}
