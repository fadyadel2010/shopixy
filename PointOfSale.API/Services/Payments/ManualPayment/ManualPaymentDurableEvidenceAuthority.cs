using System.Text.Json;
using System.Text.Json.Serialization;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public enum ManualPaymentDurableEvidencePurpose
{
    Replay = 0,
    Recovery = 1,
    Audit = 2,
    OperationalContinuity = 3,
    OperatorReview = 4
}

public sealed record ManualPaymentDurableEvidenceFacts(
    string EvidenceVersion,
    string AdmissionCheckpoint,
    Guid TenantId,
    Guid OrderId,
    Guid PaymentIntentId,
    string PaymentReference,
    decimal Amount,
    string Currency,
    string Channel,
    string EvidenceType,
    string EvidenceStatus,
    string EvidenceId,
    string EvidenceHash,
    string EvidenceReference,
    string PayloadReference,
    DateTime SubmittedAtUtc,
    string InstructionVersion,
    string ProofMetadataVersion,
    string ProofIdentifier,
    string ProofSource,
    IReadOnlyDictionary<string, string> ProofMetadata,
    string? MerchantWalletNumber,
    string? WalletInstructions);

public interface IManualPaymentDurableEvidenceAuthority
{
    string WriteReceiptEvidence(
        PaymentIntent paymentIntent,
        string? existingProviderMetadata,
        ManualPaymentRuntimeApplicationResult initialization,
        ManualPaymentEvidenceApplicationResult evidenceAdmission,
        ManualPaymentProofMetadata proofMetadata);

    ManualPaymentDurableEvidenceFacts ValidateReceiptEvidence(
        PaymentIntent paymentIntent,
        ManualPaymentDurableEvidencePurpose purpose);
}

public sealed class ManualPaymentDurableEvidenceAuthority : IManualPaymentDurableEvidenceAuthority
{
    private const string CurrentEvidenceVersion = "manual-durable-evidence-v1";
    private const string ReceiptAdmissionCheckpoint = "MANUAL_PAYMENT_RECEIPT_ADMITTED_TO_REVIEW";
    private const string ProofMetadataVersion = "manual-proof-metadata-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public string WriteReceiptEvidence(
        PaymentIntent paymentIntent,
        string? existingProviderMetadata,
        ManualPaymentRuntimeApplicationResult initialization,
        ManualPaymentEvidenceApplicationResult evidenceAdmission,
        ManualPaymentProofMetadata proofMetadata)
    {
        ArgumentNullException.ThrowIfNull(paymentIntent);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(evidenceAdmission);
        ArgumentNullException.ThrowIfNull(proofMetadata);

        EnsureManualWalletIntent(paymentIntent);

        var evidence = evidenceAdmission.ProviderEvidence;
        var evidenceMetadata = evidence.Metadata.Values;
        var proofIdentifier = Required(evidenceMetadata, "proof_identifier");
        var proofSource = RequiredProofMetadata(proofMetadata, "proof_source");

        var legacy = ParseEnvelope(existingProviderMetadata, requireDurableEvidence: false);
        var facts = new ManualPaymentDurableEvidenceFacts(
            CurrentEvidenceVersion,
            ReceiptAdmissionCheckpoint,
            paymentIntent.TenantId,
            paymentIntent.OrderId,
            paymentIntent.Id,
            paymentIntent.PaymentReference,
            paymentIntent.Amount,
            NormalizeCurrency(paymentIntent.Currency),
            Required(evidenceMetadata, "channel"),
            evidence.Type.ToString(),
            evidence.Status.ToString(),
            evidence.Id.Value.ToString("N"),
            evidence.Hash.Value,
            evidence.Reference.Value,
            evidence.PayloadReference.Value,
            evidence.Timestamp.Value.UtcDateTime,
            Required(evidenceMetadata, "instruction_version"),
            ProofMetadataVersion,
            proofIdentifier,
            proofSource,
            proofMetadata.Values,
            legacy.MerchantWalletNumber,
            legacy.WalletInstructions);

        ValidateReceiptEvidence(paymentIntent, facts, ManualPaymentDurableEvidencePurpose.Replay);
        return JsonSerializer.Serialize(facts, JsonOptions);
    }

    public ManualPaymentDurableEvidenceFacts ValidateReceiptEvidence(
        PaymentIntent paymentIntent,
        ManualPaymentDurableEvidencePurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(paymentIntent);
        EnsureManualWalletIntent(paymentIntent);

        var facts = ParseEnvelope(paymentIntent.ProviderMetadata, requireDurableEvidence: true);
        ValidateReceiptEvidence(paymentIntent, facts, purpose);
        return facts;
    }

    private static void ValidateReceiptEvidence(
        PaymentIntent paymentIntent,
        ManualPaymentDurableEvidenceFacts facts,
        ManualPaymentDurableEvidencePurpose purpose)
    {
        EnsureSupportedPurpose(purpose);

        if (!string.Equals(facts.EvidenceVersion, CurrentEvidenceVersion, StringComparison.Ordinal) ||
            !string.Equals(facts.AdmissionCheckpoint, ReceiptAdmissionCheckpoint, StringComparison.Ordinal))
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence checkpoint is unsupported.");
        }

        if (!string.Equals(facts.EvidenceType, PointOfSale.Core.Payments.Runtime.Domain.EvidenceType.Receipt.ToString(), StringComparison.Ordinal))
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence type is unsupported.");
        }

        if (!string.Equals(facts.EvidenceStatus, EvidenceStatus.PendingReview.ToString(), StringComparison.Ordinal))
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence status is not executable.");
        }

        if (facts.TenantId != paymentIntent.TenantId ||
            facts.OrderId != paymentIntent.OrderId ||
            facts.PaymentIntentId != paymentIntent.Id ||
            !string.Equals(facts.PaymentReference, paymentIntent.PaymentReference, StringComparison.Ordinal) ||
            facts.Amount != paymentIntent.Amount ||
            !string.Equals(NormalizeCurrency(facts.Currency), NormalizeCurrency(paymentIntent.Currency), StringComparison.Ordinal))
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence conflicts with current SQL payment authority.");
        }

        if (string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl) ||
            !string.Equals(facts.ProofIdentifier, paymentIntent.ProofImageUrl.Trim(), StringComparison.Ordinal))
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable proof evidence conflicts with current proof authority.");
        }

        if (facts.SubmittedAtUtc == default ||
            paymentIntent.CreatedAt != default && facts.SubmittedAtUtc < paymentIntent.CreatedAt)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence submission time is invalid.");
        }

        RequireNonEmpty(facts.Channel, "channel");
        RequireNonEmpty(facts.EvidenceId, "evidence_id");
        RequireNonEmpty(facts.EvidenceReference, "evidence_reference");
        RequireNonEmpty(facts.PayloadReference, "payload_reference");
        RequireNonEmpty(facts.InstructionVersion, "instruction_version");
        RequireNonEmpty(facts.ProofMetadataVersion, "proof_metadata_version");
        RequireNonEmpty(facts.ProofSource, "proof_source");

        if (!Guid.TryParseExact(facts.EvidenceId, "N", out var evidenceId) || evidenceId == Guid.Empty)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence id is invalid.");
        }

        if (facts.EvidenceHash.Length != 64 ||
            facts.EvidenceHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence hash is invalid.");
        }

        if (facts.ProofMetadata.Count == 0 ||
            !facts.ProofMetadata.TryGetValue("proof_identifier", out var metadataProofIdentifier) ||
            !string.Equals(metadataProofIdentifier, facts.ProofIdentifier, StringComparison.Ordinal) ||
            !facts.ProofMetadata.TryGetValue("proof_source", out var metadataProofSource) ||
            !string.Equals(metadataProofSource, facts.ProofSource, StringComparison.Ordinal))
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable proof metadata is incomplete.");
        }
    }

    private static ManualPaymentDurableEvidenceFacts ParseEnvelope(
        string? providerMetadata,
        bool requireDurableEvidence)
    {
        if (string.IsNullOrWhiteSpace(providerMetadata))
        {
            if (requireDurableEvidence)
            {
                throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence is missing.");
            }

            return EmptyLegacyFacts();
        }

        try
        {
            using var document = JsonDocument.Parse(providerMetadata);
            var root = document.RootElement;

            if (!root.TryGetProperty("evidenceVersion", out _))
            {
                if (requireDurableEvidence)
                {
                    throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence is missing.");
                }

                return EmptyLegacyFacts(
                    ReadOptionalString(root, "merchantWalletNumber"),
                    ReadOptionalString(root, "walletInstructions"));
            }

            return JsonSerializer.Deserialize<ManualPaymentDurableEvidenceFacts>(providerMetadata, JsonOptions)
                   ?? throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence is invalid.");
        }
        catch (JsonException)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence is invalid.");
        }
    }

    private static ManualPaymentDurableEvidenceFacts EmptyLegacyFacts(
        string? merchantWalletNumber = null,
        string? walletInstructions = null) =>
        new(
            string.Empty,
            string.Empty,
            Guid.Empty,
            Guid.Empty,
            Guid.Empty,
            string.Empty,
            0m,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            default,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new Dictionary<string, string>(StringComparer.Ordinal),
            merchantWalletNumber,
            walletInstructions);

    private static string? ReadOptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void EnsureManualWalletIntent(PaymentIntent paymentIntent)
    {
        if (paymentIntent.Provider != PaymentProvider.ManualWallet)
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence only supports Manual Wallet payment intents.");
        }
    }

    private static void EnsureSupportedPurpose(ManualPaymentDurableEvidencePurpose purpose)
    {
        if (!Enum.IsDefined(purpose))
        {
            throw ManualPaymentApplicationException.InvalidRequest("Manual Payment durable evidence purpose is unsupported.");
        }
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw ManualPaymentApplicationException.InvalidRequest(
                $"Manual Payment durable evidence field '{key}' is missing.");
        }

        return value.Trim();
    }

    private static string RequiredProofMetadata(ManualPaymentProofMetadata metadata, string key) =>
        Required(metadata.Values, key);

    private static void RequireNonEmpty(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ManualPaymentApplicationException.InvalidRequest(
                $"Manual Payment durable evidence field '{fieldName}' is missing.");
        }
    }

    private static string NormalizeCurrency(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
}
