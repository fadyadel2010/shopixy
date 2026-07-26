using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.Paymob;

public static class PaymobFoundationServiceCollectionExtensions
{
    public const string HttpClientName = "PaymobOfficialApi";

    public static IServiceCollection AddPaymobFoundation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IPaymobFoundationHealthContributor)))
        {
            throw new InvalidOperationException("Paymob foundation duplicate registration is not allowed.");
        }

        services.AddOptions<PaymobOptions>()
            .Bind(configuration.GetSection(PaymobOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PaymobOptions>, PaymobOptionsValidator>();

        services.AddHttpClient(HttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<PaymobOptions>>().Value;
            client.BaseAddress = options.BaseUrl;
            client.Timeout = options.Timeout;
        });

        services.AddSingleton<IPaymobFoundationHealthContributor, PaymobFoundationHealthContributor>();

        return services;
    }
}
