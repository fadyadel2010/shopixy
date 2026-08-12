using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Services.Shipping;

/// <summary>
/// Converts legacy carrier webhook secrets to the canonical encrypted-at-rest format
/// before the application accepts webhook traffic. A failed conversion fails startup;
/// continuing would leave recoverable authentication material in plaintext.
/// </summary>
public sealed class CarrierWebhookSecretBackfillHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<CarrierWebhookSecretBackfillHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var encryption = scope.ServiceProvider.GetRequiredService<ICarrierCredentialEncryptionService>();

        var changed = await ProtectLegacySecretsAsync(dbContext, encryption, cancellationToken);
        if (changed > 0)
        {
            logger.LogInformation(
                "Protected {SecretCount} legacy carrier webhook secrets.",
                changed);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task<int> ProtectLegacySecretsAsync(
        ApplicationDbContext dbContext,
        ICarrierCredentialEncryptionService encryption,
        CancellationToken cancellationToken)
    {
        var configurations = await dbContext.Set<CarrierConfiguration>()
            .IgnoreQueryFilters()
            .Where(item => item.WebhookSecret != null && item.WebhookSecret != string.Empty)
            .ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var configuration in configurations)
        {
            if (encryption.IsEncrypted(configuration.WebhookSecret))
            {
                continue;
            }

            configuration.WebhookSecret = encryption.Encrypt(configuration.WebhookSecret!);
            configuration.CredentialConfigurationFingerprint = null;
            configuration.LastValidatedConfigurationFingerprint = null;
            configuration.LastValidatedAtUtc = null;
            configuration.LastValidationSucceeded = null;
            configuration.LastValidationError = null;
            configuration.UpdatedAt = DateTime.UtcNow;
            changed++;
        }

        if (changed > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }
}
