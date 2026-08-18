using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments.Fawry;

public sealed class FawrySensitiveArtifactBackfillHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<FawrySensitiveArtifactBackfillHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        RunBackfillAsync(cancellationToken);

    private async Task RunBackfillAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IFawrySensitiveArtifactProtector>();

        var changedArtifacts = await ProtectLegacyArtifactsAsync(
            dbContext,
            protector,
            cancellationToken);

        if (changedArtifacts > 0)
        {
            logger.LogInformation(
                "Protected {ArtifactCount} legacy Fawry durable security artifacts.",
                changedArtifacts);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task<int> ProtectLegacyArtifactsAsync(
        ApplicationDbContext dbContext,
        IFawrySensitiveArtifactProtector protector,
        CancellationToken cancellationToken)
    {
        var webhookLogs = await dbContext.PaymentWebhookLogs
            .IgnoreQueryFilters()
            .Where(log => log.Provider.ToLower() == "fawry")
            .ToListAsync(cancellationToken);

        var changedArtifacts = 0;
        foreach (var log in webhookLogs)
        {
            if (!log.RawPayload.StartsWith(
                    FawrySensitiveArtifactProtector.WebhookPayloadPrefix,
                    StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(log.RawPayload))
                {
                    throw new InvalidOperationException(
                        $"Fawry webhook log '{log.Id}' has no recoverable payload to protect.");
                }

                try
                {
                    log.RawPayload = protector.ProtectWebhookPayload(log.RawPayload);
                }
                catch (Exception exception) when (exception is ArgumentException or CryptographicException)
                {
                    throw new InvalidOperationException(
                        $"Fawry webhook log '{log.Id}' cannot be protected without losing legacy evidence.",
                        exception);
                }

                changedArtifacts++;
            }
            else
            {
                _ = protector.UnprotectWebhookPayload(log.RawPayload);
            }

            if (!string.IsNullOrWhiteSpace(log.Signature) &&
                !log.Signature.StartsWith(
                    FawrySensitiveArtifactProtector.SignatureDigestPrefix,
                    StringComparison.Ordinal))
            {
                log.Signature = protector.DigestMessageSignature(log.Signature);
                changedArtifacts++;
            }
            else if (!string.IsNullOrWhiteSpace(log.Signature) && !IsValidSignatureDigest(log.Signature))
            {
                throw new InvalidOperationException(
                    $"Fawry webhook log '{log.Id}' contains an invalid durable signature digest.");
            }
        }

        if (changedArtifacts > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return changedArtifacts;
    }

    private static bool IsValidSignatureDigest(string value) =>
        value.Length == FawrySensitiveArtifactProtector.SignatureDigestPrefix.Length + 64 &&
        !value.AsSpan(FawrySensitiveArtifactProtector.SignatureDigestPrefix.Length)
            .ContainsAnyExcept("0123456789abcdef");
}
