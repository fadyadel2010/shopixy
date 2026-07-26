using System.Collections.ObjectModel;
using System.Globalization;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public interface IManualPaymentEvidenceAdapter
{
    ManualPaymentEvidenceAdapterResult Translate(ManualPaymentEvidenceSubmission submission);
}

public sealed class ManualPaymentEvidenceAdapter : IManualPaymentEvidenceAdapter
{
    public ManualPaymentEvidenceAdapterResult Translate(ManualPaymentEvidenceSubmission submission)
    {
        try
        {
            var evidence = ManualPaymentEvidenceTranslator.Translate(submission);
            return evidence.Status == EvidenceStatus.Rejected
                ? ManualPaymentEvidenceAdapterResult.CreateRejected(evidence)
                : ManualPaymentEvidenceAdapterResult.Success(evidence);
        }
        catch (ManualPaymentConfigurationMismatchException exception)
        {
            return ManualPaymentEvidenceAdapterResult.Failed(
                CanonicalProviderError.Configuration(
                    ManualPaymentProviderReference.ProviderName,
                    exception.Code,
                    exception.Message));
        }
        catch (ArgumentException exception)
        {
            return ManualPaymentEvidenceAdapterResult.Failed(
                CanonicalProviderError.Validation(
                    ManualPaymentProviderReference.ProviderName,
                    "MANUAL_EVIDENCE_SUBMISSION_INVALID",
                    exception.Message));
        }
    }
}

public static class ManualPaymentEvidenceTranslator
{
    public static ProviderEvidence Translate(ManualPaymentEvidenceSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        RequiredGuid(submission.TenantId, nameof(submission.TenantId));
        RequiredGuid(submission.PaymentId, nameof(submission.PaymentId));
        RequiredGuid(submission.ActionId.Value, nameof(submission.ActionId));
        RequiredText(submission.CorrelationId.Value, nameof(submission.CorrelationId));

        if (submission.SubmittedAtUtc == default)
        {
            throw new ArgumentException("Manual Payment evidence submission timestamp is required.");
        }

        if (!string.Equals(submission.ConfiguredProviderName, ManualPaymentProviderReference.ProviderName, StringComparison.Ordinal))
        {
            throw new ManualPaymentConfigurationMismatchException(
                "MANUAL_EVIDENCE_CONFIGURATION_MISMATCH",
                "Manual Payment evidence configuration does not match the Manual Payment provider.");
        }

        var channel = ManualPaymentChannelMapper.Map(submission.Channel);
        var normalizedChannel = ManualPaymentChannelMapper.ToCanonical(channel);
        if (submission.SupportedChannels.Count == 0 || !submission.SupportedChannels.Contains(channel))
        {
            return CreateRejected(submission, channel, "MANUAL_EVIDENCE_CHANNEL_UNSUPPORTED", "UnsupportedChannel");
        }

        var proofIdentifier = NormalizeOptional(submission.ProofIdentifier);
        if (proofIdentifier is null || submission.ProofMetadata.Values.Count == 0)
        {
            return CreateRejected(submission, channel, "MANUAL_EVIDENCE_PROOF_MISSING", "ProofMissing");
        }

        var referenceNumber = NormalizeOptional(submission.ReferenceNumber);
        if (submission.ReferenceNumberRequired && referenceNumber is null)
        {
            return CreateRejected(submission, channel, "MANUAL_EVIDENCE_REFERENCE_MISSING", "ReferenceMissing");
        }

        var expired = submission.ExpiresAtUtc.HasValue && submission.SubmittedAtUtc > submission.ExpiresAtUtc.Value;
        var metadata = CreateMetadata(submission, normalizedChannel, proofIdentifier, referenceNumber, expired, rejectedCode: null, rejectedReason: null);
        var reference = string.Concat("manual_payment:evidence:", proofIdentifier);

        return CreateProviderEvidence(
            submission,
            EvidenceStatus.PendingReview,
            reference,
            proofIdentifier,
            expired ? "ExpiredSubmission" : "EvidenceSubmitted",
            metadata);
    }

    private static ProviderEvidence CreateRejected(
        ManualPaymentEvidenceSubmission submission,
        ManualPaymentChannel channel,
        string code,
        string reason)
    {
        var normalizedChannel = ManualPaymentChannelMapper.ToCanonical(channel);
        var proofIdentifier = NormalizeOptional(submission.ProofIdentifier) ?? "missing-proof";
        var referenceNumber = NormalizeOptional(submission.ReferenceNumber);
        var metadata = CreateMetadata(submission, normalizedChannel, proofIdentifier, referenceNumber, expired: false, code, reason);
        var reference = string.Concat("manual_payment:evidence:", proofIdentifier, ":rejected:", code);

        return CreateProviderEvidence(
            submission,
            EvidenceStatus.Rejected,
            reference,
            proofIdentifier,
            "Rejected",
            metadata);
    }

    private static ProviderEvidence CreateProviderEvidence(
        ManualPaymentEvidenceSubmission submission,
        EvidenceStatus status,
        string reference,
        string proofIdentifier,
        string outcome,
        IReadOnlyDictionary<string, string> metadata)
    {
        var hashSource = string.Join(
            "|",
            submission.TenantId.ToString("N"),
            submission.PaymentId.ToString("N"),
            submission.ActionId.Value.ToString("N"),
            submission.CorrelationId.Value,
            reference,
            status.ToString(),
            outcome,
            proofIdentifier,
            submission.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return ProviderEvidenceFactory.Create(
            new EvidenceId(ManualPaymentProviderReference.CreateDeterministicId("manual-evidence-adapter", hashSource)),
            submission.ActionId,
            EvidenceType.Receipt,
            status,
            new EvidenceReference(reference),
            new EvidenceSource(ManualPaymentProviderReference.ProviderName),
            new EvidenceTimestamp(submission.SubmittedAtUtc),
            new EvidenceHash(ManualPaymentProviderReference.Hash(hashSource)),
            new ProviderPayloadReference(string.Concat("manual_payment:proof:", proofIdentifier)),
            submission.CorrelationId,
            new EvidenceMetadata(metadata));
    }

    private static IReadOnlyDictionary<string, string> CreateMetadata(
        ManualPaymentEvidenceSubmission submission,
        string normalizedChannel,
        string proofIdentifier,
        string? referenceNumber,
        bool expired,
        string? rejectedCode,
        string? rejectedReason)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider_status"] = rejectedCode is null ? "EvidenceSubmitted" : "Rejected",
            ["canonical_status"] = rejectedCode is null ? "ManualReview" : "Rejected",
            ["evidence_outcome"] = rejectedCode is null ? expired ? "ExpiredSubmission" : "EvidenceSubmitted" : "RejectedProviderEvidence",
            ["tenant_id"] = submission.TenantId.ToString("N"),
            ["payment_id"] = submission.PaymentId.ToString("N"),
            ["channel"] = normalizedChannel,
            ["reference_required"] = submission.ReferenceNumberRequired.ToString(CultureInfo.InvariantCulture),
            ["reference_status"] = referenceNumber is null ? "ReferenceMissing" : "ReferencePresent",
            ["reference_number"] = referenceNumber ?? string.Empty,
            ["proof_status"] = string.IsNullOrWhiteSpace(proofIdentifier) || proofIdentifier == "missing-proof" ? "ProofMissing" : "ProofPresent",
            ["proof_identifier"] = proofIdentifier,
            ["submission_status"] = expired ? "ExpiredSubmission" : "EvidenceSubmitted",
            ["submitted_utc"] = submission.SubmittedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["instruction_version"] = submission.InstructionVersion.Trim(),
            ["customer_notes_present"] = (!string.IsNullOrWhiteSpace(submission.CustomerNotes)).ToString(CultureInfo.InvariantCulture)
        };

        if (submission.ExpiresAtUtc.HasValue)
        {
            metadata["expires_utc"] = submission.ExpiresAtUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        foreach (var (key, value) in submission.ProofMetadata.Values)
        {
            metadata[string.Concat("proof_metadata_", key)] = value;
        }

        if (rejectedCode is not null)
        {
            metadata["provider_error_code"] = rejectedCode;
            metadata["provider_error_reason"] = rejectedReason ?? "Rejected";
        }

        return new ReadOnlyDictionary<string, string>(metadata);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void RequiredGuid(Guid value, string fieldName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"Manual Payment evidence field '{fieldName}' is required.");
        }
    }

    private static void RequiredText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Manual Payment evidence field '{fieldName}' is required.");
        }
    }
}

public sealed record ManualPaymentEvidenceSubmission
{
    public ManualPaymentEvidenceSubmission(
        Guid tenantId,
        Guid paymentId,
        PaymentActionId actionId,
        ActionCorrelationId correlationId,
        ManualPaymentChannel channel,
        IReadOnlyCollection<ManualPaymentChannel> supportedChannels,
        bool referenceNumberRequired,
        string? referenceNumber,
        string? proofIdentifier,
        ManualPaymentProofMetadata? proofMetadata,
        DateTimeOffset submittedAtUtc,
        DateTimeOffset? expiresAtUtc,
        string? customerNotes,
        string instructionVersion,
        string configuredProviderName = ManualPaymentProviderReference.ProviderName)
    {
        TenantId = tenantId;
        PaymentId = paymentId;
        ActionId = actionId;
        CorrelationId = correlationId;
        Channel = channel;
        SupportedChannels = Array.AsReadOnly((supportedChannels ?? Array.Empty<ManualPaymentChannel>()).ToArray());
        ReferenceNumberRequired = referenceNumberRequired;
        ReferenceNumber = referenceNumber;
        ProofIdentifier = proofIdentifier;
        ProofMetadata = proofMetadata ?? new ManualPaymentProofMetadata();
        SubmittedAtUtc = submittedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        CustomerNotes = customerNotes;
        InstructionVersion = string.IsNullOrWhiteSpace(instructionVersion)
            ? throw new ArgumentException("Manual Payment instruction version is required.")
            : instructionVersion.Trim();
        ConfiguredProviderName = string.IsNullOrWhiteSpace(configuredProviderName) ? string.Empty : configuredProviderName.Trim();
    }

    public Guid TenantId { get; init; }
    public Guid PaymentId { get; init; }
    public PaymentActionId ActionId { get; init; }
    public ActionCorrelationId CorrelationId { get; init; }
    public ManualPaymentChannel Channel { get; init; }
    public IReadOnlyCollection<ManualPaymentChannel> SupportedChannels { get; init; }
    public bool ReferenceNumberRequired { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? ProofIdentifier { get; init; }
    public ManualPaymentProofMetadata ProofMetadata { get; init; }
    public DateTimeOffset SubmittedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string? CustomerNotes { get; init; }
    public string InstructionVersion { get; init; }
    public string ConfiguredProviderName { get; init; }
}

public sealed record ManualPaymentProofMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public ManualPaymentProofMetadata(IReadOnlyDictionary<string, string>? values = null)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (values is not null)
        {
            foreach (var (key, value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(key) && value is not null)
                {
                    normalized[key.Trim()] = value.Trim();
                }
            }
        }

        Values = new ReadOnlyDictionary<string, string>(normalized);
    }
}

public sealed record ManualPaymentEvidenceAdapterResult
{
    public bool Succeeded { get; init; }
    public bool Rejected { get; init; }
    public ProviderEvidence? Evidence { get; init; }
    public CanonicalProviderError? Error { get; init; }

    private ManualPaymentEvidenceAdapterResult()
    {
    }

    public static ManualPaymentEvidenceAdapterResult Success(ProviderEvidence evidence) =>
        new() { Succeeded = true, Evidence = evidence };

    public static ManualPaymentEvidenceAdapterResult CreateRejected(ProviderEvidence evidence) =>
        new() { Rejected = true, Evidence = evidence };

    public static ManualPaymentEvidenceAdapterResult Failed(CanonicalProviderError error) =>
        new() { Error = error };
}

public sealed class ManualPaymentConfigurationMismatchException : Exception
{
    public ManualPaymentConfigurationMismatchException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
