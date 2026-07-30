using Microsoft.Extensions.DependencyInjection.Extensions;
using PointOfSale.API.Infrastructure.Idempotency;
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
        services.TryAddScoped<IProductAuthoringCommandMapper, ProductAuthoringCommandMapper>();
        services.TryAddScoped<IProductAuthoringReconstructionService, ProductAuthoringReconstructionService>();
        services.AddScoped<IProductAuthoringOrchestrator, ProductAuthoringOrchestrator>();

        return services;
    }
}
