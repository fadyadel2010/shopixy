using System.Text.Json;
using System.Text.Json.Serialization;

namespace PointOfSale.API.Services.Payments;

public sealed class PaymentIntentRoutingMetadata
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public string StorefrontBaseUrl { get; set; } = string.Empty;
    // Read-only compatibility for rows written before PAYMOB-FINAL-006 was closed.
    // New serialization never writes this plaintext field.
    public string? PaymentKey { get; set; }
    public string? ProtectedPaymobSession { get; set; }
    public string? PaymobMerchantOrderId { get; set; }
    public string? PaymobCheckpointState { get; set; }
    public DateTime? PaymobCheckpointedAtUtc { get; set; }
}

public static class PaymentIntentRoutingMetadataSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(
        Guid tenantId,
        Guid orderId,
        string storefrontBaseUrl,
        string? paymobMerchantOrderId = null,
        string? paymobCheckpointState = null,
        DateTime? paymobCheckpointedAtUtc = null,
        string? protectedPaymobSession = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        }

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order ID is required.", nameof(orderId));
        }

        if (string.IsNullOrWhiteSpace(storefrontBaseUrl))
        {
            throw new ArgumentException("Storefront base URL is required.", nameof(storefrontBaseUrl));
        }

        return JsonSerializer.Serialize(
            new PaymentIntentRoutingMetadata
            {
                TenantId = tenantId,
                OrderId = orderId,
                StorefrontBaseUrl = NormalizeBaseUrl(storefrontBaseUrl),
                ProtectedPaymobSession = string.IsNullOrWhiteSpace(protectedPaymobSession)
                    ? null
                    : protectedPaymobSession.Trim(),
                PaymobMerchantOrderId = string.IsNullOrWhiteSpace(paymobMerchantOrderId)
                    ? null
                    : paymobMerchantOrderId.Trim(),
                PaymobCheckpointState = string.IsNullOrWhiteSpace(paymobCheckpointState)
                    ? null
                    : paymobCheckpointState.Trim(),
                PaymobCheckpointedAtUtc = paymobCheckpointedAtUtc
            },
            JsonOptions);
    }

    public static PaymentIntentRoutingMetadata? Deserialize(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<PaymentIntentRoutingMetadata>(metadata, JsonOptions);
            if (parsed == null ||
                parsed.TenantId == Guid.Empty ||
                parsed.OrderId == Guid.Empty ||
                string.IsNullOrWhiteSpace(parsed.StorefrontBaseUrl))
            {
                return null;
            }

            parsed.StorefrontBaseUrl = NormalizeBaseUrl(parsed.StorefrontBaseUrl);
            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ExtractStorefrontBaseUrl(string? metadata)
    {
        return Deserialize(metadata)?.StorefrontBaseUrl;
    }

    public static bool HasPendingPaymobCheckpoint(string? metadata)
    {
        return !string.IsNullOrWhiteSpace(Deserialize(metadata)?.PaymobCheckpointState);
    }

    public static bool MatchesStoredIntent(
        PaymentIntentRoutingMetadata? metadata,
        Guid tenantId,
        Guid orderId)
    {
        return metadata != null &&
               metadata.TenantId == tenantId &&
               metadata.OrderId == orderId &&
               !string.IsNullOrWhiteSpace(metadata.StorefrontBaseUrl);
    }

    public static string NormalizeBaseUrl(string storefrontBaseUrl)
    {
        return storefrontBaseUrl.Trim().TrimEnd('/');
    }
}
