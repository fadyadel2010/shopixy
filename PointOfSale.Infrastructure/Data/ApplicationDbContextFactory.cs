using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using PointOfSale.Infrastructure.Configuration;

namespace PointOfSale.Infrastructure.Data;

/// <summary>
/// Design-time factory for ApplicationDbContext.
/// Used by EF Core tools (dotnet ef migrations) to create DbContext at design time.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var environmentName =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";

        // Build configuration from appsettings.json in the API project
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../PointOfSale.API"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.local.json", optional: true); // Local overrides (gitignored)

        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            configuration.AddUserSecrets<ApplicationDbContextFactory>(optional: true);
        }

        var builtConfiguration = configuration.Build();

        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? builtConfiguration.GetConnectionString("DefaultConnection");
        connectionString = PostgresConnectionStringPolicy.Normalize(
            connectionString,
            string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase));

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
