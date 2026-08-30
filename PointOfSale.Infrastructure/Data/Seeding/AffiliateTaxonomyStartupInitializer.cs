using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Data.Seeding;

/// <summary>
/// AF-30B: Awaited startup boundary that runs <see cref="AffiliateTaxonomySeeder"/>
/// once while the host starts, BEFORE Kestrel accepts requests and before cache
/// warmup can observe an empty taxonomy.
///
/// Unlike fire-and-forget seeders, StartAsync awaits the seed: failures escape
/// and fail host startup visibly instead of being logged-and-swallowed. The
/// deterministic test-host branch in Program.cs strips hosted services, so tests
/// exercise <see cref="AffiliateTaxonomySeeder"/> directly.
/// </summary>
public sealed class AffiliateTaxonomyStartupInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AffiliateTaxonomyStartupInitializer> _logger;

    public AffiliateTaxonomyStartupInitializer(
        IServiceScopeFactory scopeFactory,
        ILogger<AffiliateTaxonomyStartupInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[AF-30B] Ensuring affiliate taxonomy baseline at startup...");
        using var scope = _scopeFactory.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<AffiliateTaxonomySeeder>();
        await seeder.SeedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class AffiliateTaxonomySeedingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the insert-only affiliate taxonomy bootstrap exactly once:
    /// a scoped <see cref="AffiliateTaxonomySeeder"/> (repository-consistent,
    /// directly testable) plus its awaited <see cref="IHostedService"/> shell.
    /// No migration framework is introduced and no request-path seeding occurs.
    /// </summary>
    public static IServiceCollection AddAffiliateTaxonomyBootstrap(this IServiceCollection services)
    {
        services.AddScoped<AffiliateTaxonomySeeder>();
        services.AddHostedService<AffiliateTaxonomyStartupInitializer>();
        return services;
    }
}
