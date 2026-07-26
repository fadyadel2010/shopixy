using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments.Paymob;

public sealed class PaymobSensitiveArtifactBackfillHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<PaymobSensitiveArtifactBackfillHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fire-and-forget: this backfill scans potentially large tables and must not
        // block host startup (Kestrel) if the store is slow or a row is unclassifiable.
        _ = Task.Run(() => RunBackfillAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunBackfillAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<IPaymobSensitiveArtifactProtector>();

            var changedArtifacts = await ProtectLegacyArtifactsAsync(dbContext, protector, cancellationToken);
            if (changedArtifacts > 0)
            {
                logger.LogInformation(
                    "Protected {ArtifactCount} legacy Paymob durable security artifacts.",
                    changedArtifacts);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Paymob sensitive artifact backfill failed during startup; continuing without it.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task<int> ProtectLegacyArtifactsAsync(
        ApplicationDbContext dbContext,
        IPaymobSensitiveArtifactProtector protector,
        CancellationToken cancellationToken)
    {
        var changedArtifacts = 0;
        var intents = await dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .Where(intent =>
                intent.Provider == PaymentProvider.Paymob &&
                (intent.RedirectUrl != null ||
                 (intent.ProviderMetadata != null && intent.ProviderMetadata.Contains("\"paymentKey\""))))
            .ToListAsync(cancellationToken);

        foreach (var intent in intents)
        {
            var metadata = PaymentIntentRoutingMetadataSerializer.Deserialize(intent.ProviderMetadata);
            if (metadata is null)
            {
                if (ContainsPaymentToken(intent.RedirectUrl) || ContainsLegacyPaymentKey(intent.ProviderMetadata))
                {
                    throw new InvalidOperationException(
                        $"Paymob payment intent '{intent.Id}' contains an unclassifiable plaintext payment artifact. " +
                        "Recovery-safe protection cannot proceed without valid routing metadata.");
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(metadata.ProtectedPaymobSession))
            {
                _ = protector.UnprotectPaymentSession(metadata.ProtectedPaymobSession);
                if (!string.IsNullOrWhiteSpace(intent.RedirectUrl))
                {
                    intent.RedirectUrl = null;
                    changedArtifacts++;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(metadata.PaymentKey))
            {
                if (ContainsPaymentToken(intent.RedirectUrl))
                {
                    throw new InvalidOperationException(
                        $"Paymob payment intent '{intent.Id}' has a token-bearing iframe URL without its recovery payment key.");
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(intent.RedirectUrl))
            {
                throw new InvalidOperationException(
                    $"Paymob payment intent '{intent.Id}' has a plaintext payment key without its recovery iframe URL.");
            }

            var protectedSession = protector.ProtectPaymentSession(metadata.PaymentKey, intent.RedirectUrl);
            intent.ProviderMetadata = PaymentIntentRoutingMetadataSerializer.Serialize(
                metadata.TenantId,
                metadata.OrderId,
                metadata.StorefrontBaseUrl,
                metadata.PaymobMerchantOrderId,
                metadata.PaymobCheckpointState,
                metadata.PaymobCheckpointedAtUtc,
                protectedSession);
            intent.RedirectUrl = null;
            changedArtifacts += 2;
        }

        var webhookLogs = await dbContext.PaymentWebhookLogs
            .IgnoreQueryFilters()
            .Where(log =>
                log.Provider.ToLower() == "paymob" &&
                (!log.RawPayload.StartsWith(PaymobSensitiveArtifactProtector.WebhookPayloadPrefix) ||
                 (log.Signature != null &&
                  !log.Signature.StartsWith(PaymobSensitiveArtifactProtector.SignatureDigestPrefix))))
            .ToListAsync(cancellationToken);

        foreach (var log in webhookLogs)
        {
            if (!log.RawPayload.StartsWith(
                    PaymobSensitiveArtifactProtector.WebhookPayloadPrefix,
                    StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(log.RawPayload))
                {
                    throw new InvalidOperationException(
                        $"Paymob webhook log '{log.Id}' has no recoverable payload to protect.");
                }

                log.RawPayload = protector.ProtectWebhookPayload(log.RawPayload);
                changedArtifacts++;
            }
            if (!string.IsNullOrWhiteSpace(log.Signature) &&
                !log.Signature.StartsWith(
                    PaymobSensitiveArtifactProtector.SignatureDigestPrefix,
                    StringComparison.Ordinal))
            {
                log.Signature = protector.DigestHmacSignature(log.Signature);
                changedArtifacts++;
            }
        }

        if (changedArtifacts > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return changedArtifacts;
    }

    private static bool ContainsPaymentToken(string? value) =>
        value?.Contains("payment_token=", StringComparison.OrdinalIgnoreCase) == true;

    private static bool ContainsLegacyPaymentKey(string? value) =>
        value?.Contains("\"paymentKey\"", StringComparison.OrdinalIgnoreCase) == true;
}
