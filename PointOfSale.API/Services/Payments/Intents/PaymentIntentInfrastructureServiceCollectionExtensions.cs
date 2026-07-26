using Microsoft.Extensions.DependencyInjection;

namespace PointOfSale.API.Services.Payments.Intents;

public static class PaymentIntentInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentIntentInfrastructure(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IPaymentIntentDispatcher)))
        {
            throw new InvalidOperationException("Payment Intent Infrastructure duplicate registration is not allowed.");
        }

        services.AddScoped<IPaymentIntentOrderDomainStore, EfPaymentIntentOrderDomainStore>();
        services.AddScoped<IPaymentIntentDispatcher, PaymentIntentDispatcher>();
        services.AddScoped<IPaymentWorkflowExecutionService, PaymentWorkflowExecutionService>();
        services.AddScoped<IPaymentIntentHandler, AdvanceCollectionHandler>();
        services.AddScoped<IPaymentIntentHandler, AwaitEvidenceHandler>();
        services.AddScoped<IPaymentIntentHandler, QueueOperationalReviewHandler>();
        services.AddScoped<IPaymentIntentHandler, CompleteAdmissionWorkflowHandler>();
        services.AddScoped<IPaymentIntentHandler, RejectAdmissionHandler>();

        return services;
    }
}
