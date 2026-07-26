using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PointOfSale.Core.Payments.Runtime.Contracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Infrastructure.Payments.Runtime;

public static class PaymentRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentRuntimeFoundation(this IServiceCollection services)
    {
        services.TryAddSingleton<PaymentRuntimeOrchestrator>();
        services.TryAddSingleton<IPaymentRuntime>(serviceProvider =>
            serviceProvider.GetRequiredService<PaymentRuntimeOrchestrator>());

        return services;
    }
}
