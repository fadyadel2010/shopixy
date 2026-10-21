using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public enum ManualPaymentRetentionArtifactClass
{
    SubmittedProof = 0,
    ApprovedProof = 1,
    RejectedProof = 2,
    CancelledProof = 3
}

public enum ManualPaymentRetentionOperation
{
    Admission = 0,
    Access = 1,
    Cleanup = 2
}

public sealed record ManualPaymentRetentionDecision(
    ManualPaymentRetentionArtifactClass ArtifactClass,
    ManualPaymentRetentionOperation Operation,
    DateTime EvaluatedAtUtc,
    DateTime ExpiresAtUtc,
    bool Retain,
    bool Expire,
    string OutcomeCode);

public interface IManualPaymentRetentionAuthority
{
    ManualPaymentRetentionDecision ClassifyAndRetain(
        ManualPaymentRetentionArtifactClass artifactClass,
        DateTimeOffset observedAtUtc);

    ManualPaymentRetentionDecision EvaluateProofAccess(
        PaymentIntent paymentIntent,
        DateTimeOffset observedAtUtc);

    ManualPaymentRetentionDecision EvaluateCleanup(
        PaymentIntent paymentIntent,
        DateTimeOffset observedAtUtc);
}

public sealed class ManualPaymentRetentionAuthority : IManualPaymentRetentionAuthority
{
    private readonly IOptions<ManualPaymentOptions> _options;

    public ManualPaymentRetentionAuthority(IOptions<ManualPaymentOptions> options)
    {
        _options = options;
    }

    public ManualPaymentRetentionDecision ClassifyAndRetain(
        ManualPaymentRetentionArtifactClass artifactClass,
        DateTimeOffset observedAtUtc)
    {
        EnsureSupportedArtifactClass(artifactClass);

        var evaluatedAtUtc = ValidateObservedAt(observedAtUtc);
        var policy = ResolvePolicy();
        return new ManualPaymentRetentionDecision(
            artifactClass,
            ManualPaymentRetentionOperation.Admission,
            evaluatedAtUtc,
            evaluatedAtUtc.Add(policy.ProofAccessTtl),
            Retain: true,
            Expire: false,
            "MANUAL_PAYMENT_RETENTION_RETAIN");
    }

    public ManualPaymentRetentionDecision EvaluateProofAccess(
        PaymentIntent paymentIntent,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(paymentIntent);

        var evaluatedAtUtc = ValidateObservedAt(observedAtUtc);
        _ = ResolvePolicy();

        if (paymentIntent.Provider != PaymentProvider.ManualWallet)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment retention only supports Manual Wallet artifacts.");
        }

        var artifactClass = ClassifyPaymentIntent(paymentIntent);
        EnsureProofReferenceIsRetained(paymentIntent, evaluatedAtUtc);

        return new ManualPaymentRetentionDecision(
            artifactClass,
            ManualPaymentRetentionOperation.Access,
            evaluatedAtUtc,
            paymentIntent.ProofImageExpiresAtUtc!.Value,
            Retain: true,
            Expire: false,
            "MANUAL_PAYMENT_RETENTION_ACCESS_ALLOWED");
    }

    public ManualPaymentRetentionDecision EvaluateCleanup(
        PaymentIntent paymentIntent,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(paymentIntent);

        var evaluatedAtUtc = ValidateObservedAt(observedAtUtc);
        _ = ResolvePolicy();

        if (paymentIntent.Provider != PaymentProvider.ManualWallet)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment retention only supports Manual Wallet artifacts.");
        }

        var artifactClass = ClassifyPaymentIntent(paymentIntent);
        if (!paymentIntent.ProofImageExpiresAtUtc.HasValue || paymentIntent.ProofImageExpiresAtUtc.Value == default)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment proof retention policy is missing.");
        }

        if (paymentIntent.CreatedAt != default && paymentIntent.ProofImageExpiresAtUtc.Value <= paymentIntent.CreatedAt)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment proof retention policy conflicts with durable payment creation.");
        }

        var expired = paymentIntent.ProofImageExpiresAtUtc.Value <= evaluatedAtUtc;
        return new ManualPaymentRetentionDecision(
            artifactClass,
            ManualPaymentRetentionOperation.Cleanup,
            evaluatedAtUtc,
            paymentIntent.ProofImageExpiresAtUtc.Value,
            Retain: !expired,
            Expire: expired,
            expired ? "MANUAL_PAYMENT_RETENTION_EXPIRED" : "MANUAL_PAYMENT_RETENTION_RETAIN");
    }

    private static DateTime ValidateObservedAt(DateTimeOffset observedAtUtc)
    {
        if (observedAtUtc == default)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment retention evaluation time is required.");
        }

        return observedAtUtc.UtcDateTime;
    }

    private ManualPaymentExpirationPolicy ResolvePolicy()
    {
        var options = _options.Value;
        var validation = ManualPaymentConfigurationValidator.Validate(options);
        if (!validation.IsValid)
        {
            throw ManualPaymentApplicationException.InvalidConfiguration();
        }

        if (!options.Expiration.Configured || options.Expiration.ProofAccessTtl <= TimeSpan.Zero)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment proof retention policy is missing or invalid.");
        }

        return options.Expiration;
    }

    private static void EnsureSupportedArtifactClass(ManualPaymentRetentionArtifactClass artifactClass)
    {
        if (!Enum.IsDefined(artifactClass))
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment retention artifact class is unsupported.");
        }
    }

    private static ManualPaymentRetentionArtifactClass ClassifyPaymentIntent(PaymentIntent paymentIntent)
    {
        return paymentIntent.Status switch
        {
            PaymentIntentStatus.Pending or PaymentIntentStatus.AwaitingVerification => ManualPaymentRetentionArtifactClass.SubmittedProof,
            PaymentIntentStatus.Paid => ManualPaymentRetentionArtifactClass.ApprovedProof,
            PaymentIntentStatus.Rejected => ManualPaymentRetentionArtifactClass.RejectedProof,
            PaymentIntentStatus.Cancelled or PaymentIntentStatus.Expired or PaymentIntentStatus.Failed => ManualPaymentRetentionArtifactClass.CancelledProof,
            _ => throw ManualPaymentApplicationException.InvalidRequest("Manual Payment retention artifact class is unsupported.")
        };
    }

    private static void EnsureProofReferenceIsRetained(PaymentIntent paymentIntent, DateTime evaluatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl) || paymentIntent.ProofImageDeleted)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment proof artifact is expired or deleted.");
        }

        if (!paymentIntent.ProofImageExpiresAtUtc.HasValue || paymentIntent.ProofImageExpiresAtUtc.Value == default)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment proof retention policy is missing.");
        }

        if (paymentIntent.CreatedAt != default && paymentIntent.ProofImageExpiresAtUtc.Value <= paymentIntent.CreatedAt)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment proof retention policy conflicts with durable payment creation.");
        }

        if (paymentIntent.ProofImageExpiresAtUtc.Value <= evaluatedAtUtc)
        {
            throw ManualPaymentApplicationException.Expired();
        }
    }
}
