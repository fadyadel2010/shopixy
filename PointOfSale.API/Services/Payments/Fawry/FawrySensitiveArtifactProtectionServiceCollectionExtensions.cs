namespace PointOfSale.API.Services.Payments.Fawry;

public static class FawrySensitiveArtifactProtectionServiceCollectionExtensions
{
    public static IServiceCollection AddFawrySensitiveArtifactProtection(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IFawrySensitiveArtifactProtector)))
        {
            throw new InvalidOperationException(
                "Fawry sensitive artifact protection duplicate registration is not allowed.");
        }

        services.AddSingleton<IFawrySensitiveArtifactProtector, FawrySensitiveArtifactProtector>();
        services.AddHostedService<FawrySensitiveArtifactBackfillHostedService>();
        return services;
    }
}
