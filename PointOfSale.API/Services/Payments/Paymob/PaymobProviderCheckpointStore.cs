using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments.Paymob;

public interface IPaymobProviderCheckpointStore
{
    Task<PaymobProviderCheckpoint> LoadAsync(
        PaymentAction action,
        string merchantOrderId,
        CancellationToken cancellationToken);

    Task BeginOrderRegistrationAsync(
        PaymentAction action,
        string merchantOrderId,
        CancellationToken cancellationToken);

    Task PersistAcceptedOrderAsync(
        PaymentAction action,
        string merchantOrderId,
        PaymobOrderRegistrationResponse order,
        CancellationToken cancellationToken);

    Task PersistPaymentKeyAsync(
        PaymentAction action,
        string merchantOrderId,
        string providerOrderId,
        string paymentKey,
        string iframeUrl,
        CancellationToken cancellationToken);
}

public sealed class PaymobProviderCheckpointStore : IPaymobProviderCheckpointStore
{
    public const string RegistrationStartedState = "order_registration_started";
    public const string OrderAcceptedState = "provider_order_accepted";
    public const string PaymentKeyAcceptedState = "payment_key_accepted_runtime_pending";

    private readonly ApplicationDbContext _dbContext;
    private readonly IPaymobSensitiveArtifactProtector _artifactProtector;

    public PaymobProviderCheckpointStore(
        ApplicationDbContext dbContext,
        IPaymobSensitiveArtifactProtector artifactProtector)
    {
        _dbContext = dbContext;
        _artifactProtector = artifactProtector;
    }

    public async Task<PaymobProviderCheckpoint> LoadAsync(
        PaymentAction action,
        string merchantOrderId,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(action, out var identity))
        {
            return PaymobProviderCheckpoint.Unmanaged;
        }

        var intent = await LoadIntentAsync(identity, cancellationToken);
        var metadata = PaymentIntentRoutingMetadataSerializer.Deserialize(intent.ProviderMetadata)
            ?? throw InvalidCheckpoint("Paymob routing metadata is missing or invalid.");

        EnsureMerchantOrderIdentity(metadata, merchantOrderId);

        var providerOrderId = Normalize(intent.ProviderIntentId);
        var protectedSession = await LoadProtectedSessionAsync(intent, metadata, merchantOrderId, cancellationToken);
        var paymentKey = Normalize(protectedSession?.PaymentKey);
        var iframeUrl = Normalize(protectedSession?.IframeUrl);

        if (string.Equals(metadata.PaymobCheckpointState, RegistrationStartedState, StringComparison.Ordinal) &&
            providerOrderId is null)
        {
            throw new PaymobProviderCheckpointException(
                "PAYMOB_PROVIDER_ORDER_OUTCOME_UNKNOWN",
                "Paymob order registration may already have been accepted. Automatic order recreation is blocked until the accepted provider order is reconciled.");
        }

        if (paymentKey is not null && (providerOrderId is null || iframeUrl is null))
        {
            throw InvalidCheckpoint("The Paymob payment-key checkpoint is incomplete.");
        }

        return new PaymobProviderCheckpoint(
            IsManaged: true,
            MerchantOrderId: merchantOrderId,
            ProviderOrderId: providerOrderId,
            PaymentKey: paymentKey,
            IframeUrl: iframeUrl,
            State: metadata.PaymobCheckpointState);
    }

    public async Task BeginOrderRegistrationAsync(
        PaymentAction action,
        string merchantOrderId,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(action, out var identity))
        {
            return;
        }

        var intent = await LoadIntentAsync(identity, cancellationToken);
        if (!string.IsNullOrWhiteSpace(intent.ProviderIntentId))
        {
            return;
        }

        var metadata = PaymentIntentRoutingMetadataSerializer.Deserialize(intent.ProviderMetadata)
            ?? throw InvalidCheckpoint("Paymob routing metadata is missing or invalid.");
        EnsureMerchantOrderIdentity(metadata, merchantOrderId);

        if (string.Equals(metadata.PaymobCheckpointState, RegistrationStartedState, StringComparison.Ordinal))
        {
            throw new PaymobProviderCheckpointException(
                "PAYMOB_PROVIDER_ORDER_OUTCOME_UNKNOWN",
                "Paymob order registration is already fenced and cannot be repeated without reconciliation.");
        }

        intent.ProviderMetadata = Serialize(
            metadata,
            merchantOrderId,
            RegistrationStartedState,
            metadata.ProtectedPaymobSession);
        intent.FailureCode = null;
        intent.FailureReason = null;
        intent.FailedAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task PersistAcceptedOrderAsync(
        PaymentAction action,
        string merchantOrderId,
        PaymobOrderRegistrationResponse order,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(action, out var identity))
        {
            return;
        }

        if (order.Id <= 0)
        {
            throw InvalidCheckpoint("Paymob returned an invalid provider order id.");
        }

        var providerOrderId = order.Id.ToString(CultureInfo.InvariantCulture);
        var intent = await LoadIntentAsync(identity, cancellationToken);
        var metadata = PaymentIntentRoutingMetadataSerializer.Deserialize(intent.ProviderMetadata)
            ?? throw InvalidCheckpoint("Paymob routing metadata is missing or invalid.");
        EnsureMerchantOrderIdentity(metadata, merchantOrderId);

        if (!string.IsNullOrWhiteSpace(intent.ProviderIntentId) &&
            !string.Equals(intent.ProviderIntentId, providerOrderId, StringComparison.Ordinal))
        {
            throw InvalidCheckpoint("A different Paymob provider order is already checkpointed for this payment intent.");
        }

        intent.ProviderIntentId = providerOrderId;
        intent.ProviderMetadata = Serialize(
            metadata,
            merchantOrderId,
            OrderAcceptedState,
            metadata.ProtectedPaymobSession);
        intent.FailureCode = null;
        intent.FailureReason = null;
        intent.FailedAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task PersistPaymentKeyAsync(
        PaymentAction action,
        string merchantOrderId,
        string providerOrderId,
        string paymentKey,
        string iframeUrl,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(action, out var identity))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(providerOrderId) ||
            string.IsNullOrWhiteSpace(paymentKey) ||
            string.IsNullOrWhiteSpace(iframeUrl))
        {
            throw InvalidCheckpoint("Paymob payment-key checkpoint data is incomplete.");
        }

        var intent = await LoadIntentAsync(identity, cancellationToken);
        var metadata = PaymentIntentRoutingMetadataSerializer.Deserialize(intent.ProviderMetadata)
            ?? throw InvalidCheckpoint("Paymob routing metadata is missing or invalid.");
        EnsureMerchantOrderIdentity(metadata, merchantOrderId);

        if (!string.Equals(intent.ProviderIntentId, providerOrderId, StringComparison.Ordinal))
        {
            throw InvalidCheckpoint("The Paymob payment key does not belong to the checkpointed provider order.");
        }

        var protectedSession = _artifactProtector.ProtectPaymentSession(paymentKey, iframeUrl);
        intent.RedirectUrl = null;
        intent.ProviderMetadata = Serialize(
            metadata,
            merchantOrderId,
            PaymentKeyAcceptedState,
            protectedSession);
        intent.FailureCode = null;
        intent.FailureReason = null;
        intent.FailedAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<PaymentIntent> LoadIntentAsync(
        PaymobCheckpointIdentity identity,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        return await _dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                intent => intent.Id == identity.LocalPaymentIntentId &&
                          intent.TenantId == identity.TenantId &&
                          intent.OrderId == identity.OrderId &&
                          intent.Provider == PaymentProvider.Paymob &&
                          intent.Status == PaymentIntentStatus.Pending &&
                          !intent.IsDeleted,
                cancellationToken)
            ?? throw InvalidCheckpoint("The durable Paymob payment intent checkpoint owner was not found.");
    }

    private static string Serialize(
        PaymentIntentRoutingMetadata metadata,
        string merchantOrderId,
        string state,
        string? protectedPaymobSession)
    {
        return PaymentIntentRoutingMetadataSerializer.Serialize(
            metadata.TenantId,
            metadata.OrderId,
            metadata.StorefrontBaseUrl,
            merchantOrderId,
            state,
            DateTime.UtcNow,
            protectedPaymobSession);
    }

    private async Task<PaymobProtectedPaymentSession?> LoadProtectedSessionAsync(
        PaymentIntent intent,
        PaymentIntentRoutingMetadata metadata,
        string merchantOrderId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(metadata.ProtectedPaymobSession))
        {
            return _artifactProtector.UnprotectPaymentSession(metadata.ProtectedPaymobSession);
        }

        if (string.IsNullOrWhiteSpace(metadata.PaymentKey))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(intent.RedirectUrl))
        {
            throw InvalidCheckpoint("The legacy Paymob payment-key checkpoint has no recoverable iframe URL.");
        }

        var migrated = new PaymobProtectedPaymentSession(
            metadata.PaymentKey.Trim(),
            intent.RedirectUrl.Trim());
        var protectedSession = _artifactProtector.ProtectPaymentSession(
            migrated.PaymentKey,
            migrated.IframeUrl);

        intent.RedirectUrl = null;
        intent.ProviderMetadata = Serialize(
            metadata,
            merchantOrderId,
            metadata.PaymobCheckpointState ?? PaymentKeyAcceptedState,
            protectedSession);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return migrated;
    }

    private static void EnsureMerchantOrderIdentity(
        PaymentIntentRoutingMetadata metadata,
        string merchantOrderId)
    {
        if (!string.IsNullOrWhiteSpace(metadata.PaymobMerchantOrderId) &&
            !string.Equals(metadata.PaymobMerchantOrderId, merchantOrderId, StringComparison.Ordinal))
        {
            throw InvalidCheckpoint("The durable Paymob merchant-order checkpoint does not match the current request.");
        }
    }

    private static bool TryResolveIdentity(PaymentAction action, out PaymobCheckpointIdentity identity)
    {
        var values = action.Metadata.Values;
        if (TryGuid(values, "tenantId", out var tenantId) &&
            TryGuid(values, "orderId", out var orderId) &&
            TryGuid(values, "local_payment_intent_id", out var localPaymentIntentId))
        {
            identity = new PaymobCheckpointIdentity(tenantId, orderId, localPaymentIntentId);
            return true;
        }

        identity = default;
        return false;
    }

    private static bool TryGuid(
        IReadOnlyDictionary<string, string> values,
        string key,
        out Guid value)
    {
        value = Guid.Empty;
        return values.TryGetValue(key, out var raw) && Guid.TryParse(raw, out value) && value != Guid.Empty;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static PaymobProviderCheckpointException InvalidCheckpoint(string message)
    {
        return new PaymobProviderCheckpointException("PAYMOB_PROVIDER_CHECKPOINT_INVALID", message);
    }

    private readonly record struct PaymobCheckpointIdentity(
        Guid TenantId,
        Guid OrderId,
        Guid LocalPaymentIntentId);
}

public sealed record PaymobProviderCheckpoint(
    bool IsManaged,
    string MerchantOrderId,
    string? ProviderOrderId,
    string? PaymentKey,
    string? IframeUrl,
    string? State)
{
    public static PaymobProviderCheckpoint Unmanaged { get; } = new(
        false,
        string.Empty,
        null,
        null,
        null,
        null);
}

public sealed class PaymobProviderCheckpointException : InvalidOperationException
{
    public string Code { get; }

    public PaymobProviderCheckpointException(string code, string message)
        : base(message)
    {
        Code = code;
    }
}
