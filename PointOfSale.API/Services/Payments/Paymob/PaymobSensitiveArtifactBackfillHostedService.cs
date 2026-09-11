using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments.Paymob;

public sealed class PaymobSensitiveArtifactBackfillHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<PaymobSensitiveArtifactBackfillHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        RunBackfillAsync(cancellationToken);

    private async Task RunBackfillAsync(CancellationToken cancellationToken)
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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task<int> ProtectLegacyArtifactsAsync(
        ApplicationDbContext dbContext,
        IPaymobSensitiveArtifactProtector protector,
        CancellationToken cancellationToken)
    {
        using var paymentBoundary = CommerceHubBoundaryMutationScope.BeginPayment();
        using var sensitiveArtifactBackfillBoundary = CommerceHubBoundaryMutationScope.BeginSensitiveArtifactBackfill();
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

        var commerceHubPayments = await dbContext.CommerceHubPayments
            .IgnoreQueryFilters()
            .Where(payment =>
                payment.Provider == PaymentProvider.Paymob &&
                (payment.CheckoutUrl != null || payment.PaymentToken != null))
            .ToListAsync(cancellationToken);

        foreach (var payment in commerceHubPayments)
        {
            if (payment.PaymentToken?.StartsWith(
                    PaymobSensitiveArtifactProtector.PaymentSessionPrefix,
                    StringComparison.Ordinal) == true)
            {
                _ = protector.UnprotectPaymentSession(payment.PaymentToken);
                if (!string.IsNullOrWhiteSpace(payment.CheckoutUrl))
                {
                    payment.CheckoutUrl = null;
                    changedArtifacts++;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(payment.PaymentToken) && string.IsNullOrWhiteSpace(payment.CheckoutUrl))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(payment.PaymentToken) || string.IsNullOrWhiteSpace(payment.CheckoutUrl))
            {
                throw new InvalidOperationException(
                    $"CommerceHub Paymob payment '{payment.Id}' has an incomplete plaintext checkout session.");
            }

            payment.PaymentToken = protector.ProtectPaymentSession(payment.PaymentToken, payment.CheckoutUrl);
            payment.CheckoutUrl = null;
            changedArtifacts += 2;
        }

        var commerceHubLogs = await dbContext.CommerceHubPaymentLogs
            .Where(log =>
                log.Payment.Provider == PaymentProvider.Paymob &&
                !log.RawResponse.StartsWith(PaymobSensitiveArtifactProtector.WebhookPayloadPrefix))
            .ToListAsync(cancellationToken);

        foreach (var log in commerceHubLogs)
        {
            if (string.IsNullOrWhiteSpace(log.RawResponse))
            {
                throw new InvalidOperationException(
                    $"CommerceHub Paymob payment log '{log.Id}' has no recoverable payload to protect.");
            }

            log.RawResponse = protector.ProtectWebhookPayload(log.RawResponse);
            changedArtifacts++;
        }

        var unresolvedWebhooks = await dbContext.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .Where(webhook =>
                webhook.Provider == PaymentProvider.Paymob &&
                !webhook.RawPayload.StartsWith(PaymobSensitiveArtifactProtector.WebhookPayloadPrefix))
            .ToListAsync(cancellationToken);

        foreach (var webhook in unresolvedWebhooks)
        {
            if (string.IsNullOrWhiteSpace(webhook.RawPayload))
            {
                throw new InvalidOperationException(
                    $"CommerceHub unresolved Paymob webhook '{webhook.Id}' has no recoverable payload to protect.");
            }

            webhook.RawPayload = protector.ProtectWebhookPayload(webhook.RawPayload);
            changedArtifacts++;
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
