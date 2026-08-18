using System.Text.Json;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments;

public interface IPaymentWebhookTenantResolver
{
    Task<WebhookTenantResolutionResult?> ResolveAsync(
        string provider,
        string rawPayload,
        CancellationToken cancellationToken = default);
}

public sealed record WebhookTenantResolutionResult(
    Guid TenantId,
    Guid PaymentIntentId,
    Guid OrderId,
    string ResolutionKey,
    string ResolutionValue);

public sealed class PaymentWebhookTenantResolver : IPaymentWebhookTenantResolver
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PaymentWebhookTenantResolver> _logger;

    public PaymentWebhookTenantResolver(
        ApplicationDbContext dbContext,
        ILogger<PaymentWebhookTenantResolver> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<WebhookTenantResolutionResult?> ResolveAsync(
        string provider,
        string rawPayload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;

            return provider.Trim().ToLowerInvariant() switch
            {
                "stripe" => await ResolveStripeAsync(root, cancellationToken),
                "paymob" => await ResolvePaymobAsync(root, cancellationToken),
                "fawry" => await ResolveFawryAsync(root, cancellationToken),
                _ => await ResolveGenericAsync(root, cancellationToken)
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Webhook tenant resolution failed due to malformed JSON | Provider: {Provider}",
                provider);
            return null;
        }
    }

    private async Task<WebhookTenantResolutionResult?> ResolveStripeAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("data", out var dataElement) ||
            !dataElement.TryGetProperty("object", out var objectElement))
        {
            return null;
        }

        var objectId = GetString(objectElement, "id");
        var nestedPaymentIntentId = GetString(objectElement, "payment_intent");
        var providerIntentId = !string.IsNullOrWhiteSpace(nestedPaymentIntentId)
            ? nestedPaymentIntentId
            : objectId;

        if (!string.IsNullOrWhiteSpace(providerIntentId))
        {
            var resolved = await ResolveByProviderIntentIdAsync(
                PaymentProvider.Stripe,
                providerIntentId,
                cancellationToken);
            if (resolved != null)
            {
                return resolved with
                {
                    ResolutionKey = "ProviderIntentId",
                    ResolutionValue = providerIntentId
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(nestedPaymentIntentId) &&
            !string.IsNullOrWhiteSpace(objectId))
        {
            var resolved = await ResolveByProviderPaymentIdAsync(
                PaymentProvider.Stripe,
                objectId,
                cancellationToken);
            if (resolved != null)
            {
                return resolved with
                {
                    ResolutionKey = "ProviderPaymentId",
                    ResolutionValue = objectId
                };
            }
        }

        if (TryGetProperty(objectElement, "metadata", out var metadataElement))
        {
            var paymentReference = GetString(
                metadataElement,
                "payment_reference",
                "paymentReference",
                "canonical_paymentReference");
            if (!string.IsNullOrWhiteSpace(paymentReference))
            {
                var resolved = await ResolveByPaymentReferenceAsync(
                    paymentReference,
                    cancellationToken);
                if (resolved != null)
                {
                    return resolved with
                    {
                        ResolutionKey = "PaymentReference",
                        ResolutionValue = paymentReference
                    };
                }
            }

            var orderIdRaw = GetString(metadataElement, "order_id", "orderId", "canonical_orderId");
            if (Guid.TryParse(orderIdRaw, out var orderId))
            {
                var resolved = await ResolveByOrderIdAsync(orderId, cancellationToken);
                if (resolved != null)
                {
                    return resolved with
                    {
                        ResolutionKey = "OrderId",
                        ResolutionValue = orderId.ToString("D")
                    };
                }
            }
        }

        return null;
    }

    private async Task<WebhookTenantResolutionResult?> ResolvePaymobAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("obj", out var transactionElement))
        {
            return null;
        }

        if (TryGetProperty(transactionElement, "order", out var orderElement))
        {
            var paymobOrderId = GetString(orderElement, "id");
            if (!string.IsNullOrWhiteSpace(paymobOrderId))
            {
                var resolved = await ResolveByProviderIntentIdAsync(
                    PaymentProvider.Paymob,
                    paymobOrderId,
                    cancellationToken);
                if (resolved != null)
                {
                    return resolved with
                    {
                        ResolutionKey = "ProviderIntentId",
                        ResolutionValue = paymobOrderId
                    };
                }
            }

            var merchantOrderId = GetString(orderElement, "merchant_order_id");
            if (Guid.TryParse(merchantOrderId, out var merchantReferenceId))
            {
                var resolved = await ResolveUniquePaymentIntentAsync(
                    paymentIntent => paymentIntent.Provider == PaymentProvider.Paymob &&
                                     paymentIntent.Id == merchantReferenceId,
                    cancellationToken);
                resolved ??= await ResolveUniquePaymentIntentAsync(
                    paymentIntent => paymentIntent.Provider == PaymentProvider.Paymob &&
                                     paymentIntent.OrderId == merchantReferenceId,
                    cancellationToken);
                if (resolved != null)
                {
                    return resolved with
                    {
                        ResolutionKey = "MerchantOrderId",
                        ResolutionValue = merchantReferenceId.ToString("D")
                    };
                }
            }
        }

        var providerPaymentId = GetString(transactionElement, "id");
        if (!string.IsNullOrWhiteSpace(providerPaymentId))
        {
            var resolved = await ResolveByProviderPaymentIdAsync(
                PaymentProvider.Paymob,
                providerPaymentId,
                cancellationToken);
            if (resolved != null)
            {
                return resolved with
                {
                    ResolutionKey = "ProviderPaymentId",
                    ResolutionValue = providerPaymentId
                };
            }
        }

        return null;
    }

    private async Task<WebhookTenantResolutionResult?> ResolveFawryAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var referenceCode = GetString(root, "fawryRefNumber", "referenceNumber");
        if (!string.IsNullOrWhiteSpace(referenceCode))
        {
            var resolved = await ResolveByReferenceCodeAsync(referenceCode, cancellationToken);
            if (resolved != null)
            {
                return resolved with
                {
                    ResolutionKey = "ReferenceCode",
                    ResolutionValue = referenceCode
                };
            }
        }

        var merchantReference = GetString(root, "merchantRefNumber", "merchantRefNum");
        if (!string.IsNullOrWhiteSpace(merchantReference))
        {
            var resolved = await ResolveByPaymentReferenceAsync(merchantReference, cancellationToken);
            if (resolved != null)
            {
                return resolved with
                {
                    ResolutionKey = "PaymentReference",
                    ResolutionValue = merchantReference
                };
            }
        }

        return null;
    }

    private async Task<WebhookTenantResolutionResult?> ResolveGenericAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        if (TryGetProperty(root, "data", out var dataElement) &&
            TryGetProperty(dataElement, "object", out var objectElement))
        {
            var providerIntentId = GetString(objectElement, "id", "payment_intent");
            if (!string.IsNullOrWhiteSpace(providerIntentId))
            {
                var resolved = await ResolveByProviderIntentIdAcrossProvidersAsync(
                    providerIntentId,
                    cancellationToken);
                if (resolved != null)
                {
                    return resolved with
                    {
                        ResolutionKey = "ProviderIntentId",
                        ResolutionValue = providerIntentId
                    };
                }
            }

            if (TryGetProperty(objectElement, "metadata", out var metadataElement))
            {
                var paymentReference = GetString(metadataElement, "payment_reference", "paymentReference");
                if (!string.IsNullOrWhiteSpace(paymentReference))
                {
                    var resolved = await ResolveByPaymentReferenceAsync(
                        paymentReference,
                        cancellationToken);
                    if (resolved != null)
                    {
                        return resolved with
                        {
                            ResolutionKey = "PaymentReference",
                            ResolutionValue = paymentReference
                        };
                    }
                }
            }
        }

        var rootPaymentReference = GetString(root, "payment_reference", "paymentReference", "referenceNumber");
        if (!string.IsNullOrWhiteSpace(rootPaymentReference))
        {
            var resolved = await ResolveByPaymentReferenceAsync(rootPaymentReference, cancellationToken) ??
                           await ResolveByReferenceCodeAsync(rootPaymentReference, cancellationToken);

            if (resolved != null)
            {
                return resolved with
                {
                    ResolutionKey = "PaymentReference",
                    ResolutionValue = rootPaymentReference
                };
            }
        }

        return null;
    }

    private Task<WebhookTenantResolutionResult?> ResolveByProviderIntentIdAsync(
        PaymentProvider provider,
        string providerIntentId,
        CancellationToken cancellationToken)
    {
        return ResolveUniquePaymentIntentAsync(
            paymentIntent => paymentIntent.Provider == provider &&
                             paymentIntent.ProviderIntentId == providerIntentId,
            cancellationToken);
    }

    private Task<WebhookTenantResolutionResult?> ResolveByProviderIntentIdAcrossProvidersAsync(
        string providerIntentId,
        CancellationToken cancellationToken)
    {
        return ResolvePaymentIntentAsync(
            paymentIntent => paymentIntent.ProviderIntentId == providerIntentId,
            cancellationToken);
    }

    private Task<WebhookTenantResolutionResult?> ResolveByProviderPaymentIdAsync(
        PaymentProvider provider,
        string providerPaymentId,
        CancellationToken cancellationToken)
    {
        return ResolveUniquePaymentIntentAsync(
            paymentIntent => paymentIntent.Provider == provider &&
                             paymentIntent.ProviderPaymentId == providerPaymentId,
            cancellationToken);
    }

    private Task<WebhookTenantResolutionResult?> ResolveByPaymentReferenceAsync(
        string paymentReference,
        CancellationToken cancellationToken)
    {
        return ResolvePaymentIntentAsync(
            paymentIntent => paymentIntent.PaymentReference == paymentReference,
            cancellationToken);
    }

    private Task<WebhookTenantResolutionResult?> ResolveByReferenceCodeAsync(
        string referenceCode,
        CancellationToken cancellationToken)
    {
        return ResolvePaymentIntentAsync(
            paymentIntent => paymentIntent.ReferenceCode == referenceCode,
            cancellationToken);
    }

    private Task<WebhookTenantResolutionResult?> ResolveByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        return ResolvePaymentIntentAsync(
            paymentIntent => paymentIntent.OrderId == orderId,
            cancellationToken);
    }

    private async Task<WebhookTenantResolutionResult?> ResolvePaymentIntentAsync(
        Expression<Func<PaymentIntent, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var paymentIntent = await _dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(predicate)
            .OrderByDescending(entity => entity.CreatedAt)
            .Select(entity => new
            {
                entity.TenantId,
                entity.Id,
                entity.OrderId
            })
            .FirstOrDefaultAsync(cancellationToken);

        return paymentIntent == null
            ? null
            : new WebhookTenantResolutionResult(
                paymentIntent.TenantId,
                paymentIntent.Id,
                paymentIntent.OrderId,
                ResolutionKey: string.Empty,
                ResolutionValue: string.Empty);
    }

    private async Task<WebhookTenantResolutionResult?> ResolveUniquePaymentIntentAsync(
        Expression<Func<PaymentIntent, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var matches = await _dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(predicate)
            .OrderByDescending(entity => entity.CreatedAt)
            .Select(entity => new
            {
                entity.TenantId,
                entity.Id,
                entity.OrderId
            })
            .Take(2)
            .ToArrayAsync(cancellationToken);

        return matches.Length == 1
            ? new WebhookTenantResolutionResult(
                matches[0].TenantId,
                matches[0].Id,
                matches[0].OrderId,
                ResolutionKey: string.Empty,
                ResolutionValue: string.Empty)
            : null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static string? GetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }

            if (property.ValueKind == JsonValueKind.Number)
            {
                return property.GetRawText();
            }
        }

        return null;
    }
}
