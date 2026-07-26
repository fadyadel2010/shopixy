using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.SuperAdmin.Models;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Services;

public sealed class SuperAdminRootSeeder : IHostedService
{
    private const string SuperAdminEmailEnvironmentVariable = "SUPERADMIN_EMAIL";
    private const string SuperAdminPasswordEnvironmentVariable = "SUPERADMIN_PASSWORD";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SuperAdminRootSeeder> _logger;

    public SuperAdminRootSeeder(
        IServiceScopeFactory scopeFactory,
        ILogger<SuperAdminRootSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fire-and-forget: seeding must not block host startup (Kestrel) if the
        // backing store (Postgres/Mongo) is slow or temporarily unavailable.
        _ = Task.Run(() => RunSeedAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunSeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<SuperAdminUser>>();

            var systemUsers = await dbContext.SuperAdmins
                .Where(admin => admin.IsSystem)
                .ToListAsync(cancellationToken);

            if (systemUsers.Count > 1)
            {
                throw new InvalidOperationException("Multiple system Super Admin users detected. Only one root user is allowed.");
            }

            if (systemUsers.Count == 1)
            {
                _logger.LogInformation("System Super Admin already exists");
                return;
            }

            var email = EmailNormalizer.Normalize(Environment.GetEnvironmentVariable(SuperAdminEmailEnvironmentVariable));
            var password = Environment.GetEnvironmentVariable(SuperAdminPasswordEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    $"Root Super Admin is missing and environment variables {SuperAdminEmailEnvironmentVariable} and {SuperAdminPasswordEnvironmentVariable} must be configured.");
            }

            var rootUser = SuperAdminUser.Create(email, "TEMP_HASH", isSystem: true, utcNow: DateTime.UtcNow);
            rootUser.UpdatePasswordHash(passwordHasher.HashPassword(rootUser, password));
            rootUser.ClearRefreshToken();

            dbContext.SuperAdmins.Add(rootUser);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("System Super Admin root user created from environment configuration");
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            _logger.LogWarning(
                ex,
                "Skipping Super Admin root seeding because PostgreSQL is unavailable during startup.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Super Admin root seeding failed during startup. The application will continue without automatic root seeding.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsDatabaseUnavailable(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is NpgsqlException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }
}
