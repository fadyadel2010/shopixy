using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public interface IManualPaymentProviderAdapter
{
    ManualPaymentProviderAdapterResult CreatePendingPayment(ManualPaymentCanonicalRequest request);

    ManualPaymentProviderAdapterResult RejectUnsupportedCapability(
        ManualPaymentCanonicalRequest request,
        string capability);
}

public sealed class ManualPaymentProviderAdapter : IManualPaymentProviderAdapter
{
    public ManualPaymentProviderAdapterResult CreatePendingPayment(ManualPaymentCanonicalRequest request)
    {
        try
        {
            var providerRequest = ManualPaymentProviderRequestFactory.Create(request);
            var providerEvidence = ManualPaymentProviderEvidence.Pending(providerRequest, request.EvidenceTimestampUtc);
            var canonicalEvidence = ManualPaymentCanonicalEvidenceFactory.FromProviderEvidence(providerEvidence);

            return ManualPaymentProviderAdapterResult.Success(providerRequest, providerEvidence, canonicalEvidence);
        }
        catch (ManualPaymentRejectedException exception)
        {
            var providerRequest = ManualPaymentProviderRequestFactory.Rejected(request, exception.Code, exception.Message);
            var providerEvidence = ManualPaymentProviderEvidence.Rejected(
                providerRequest,
                exception.Code,
                exception.Message,
                request.EvidenceTimestampUtc);
            var canonicalEvidence = ManualPaymentCanonicalEvidenceFactory.FromProviderEvidence(providerEvidence);

            return ManualPaymentProviderAdapterResult.CreateRejected(providerRequest, providerEvidence, canonicalEvidence);
        }
        catch (ArgumentException exception)
        {
            return ManualPaymentProviderAdapterResult.Failed(
                CanonicalProviderError.Validation(
                    ManualPaymentProviderReference.ProviderName,
                    "MANUAL_PROVIDER_REQUEST_INVALID",
                    exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return ManualPaymentProviderAdapterResult.Failed(
                CanonicalProviderError.Configuration(
                    ManualPaymentProviderReference.ProviderName,
                    "MANUAL_PROVIDER_CONFIGURATION_INVALID",
                    exception.Message));
        }
    }

    public ManualPaymentProviderAdapterResult RejectUnsupportedCapability(
        ManualPaymentCanonicalRequest request,
        string capability)
    {
        var normalizedCapability = string.IsNullOrWhiteSpace(capability) ? "unknown" : capability.Trim();
        var providerRequest = ManualPaymentProviderRequestFactory.Rejected(
            request,
            "MANUAL_CAPABILITY_UNSUPPORTED",
            $"Manual Payment capability '{normalizedCapability}' is not supported by the provider adapter.");
        var providerEvidence = ManualPaymentProviderEvidence.Rejected(
            providerRequest,
            "MANUAL_CAPABILITY_UNSUPPORTED",
            "Unsupported capability.",
            request.EvidenceTimestampUtc,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["unsupported_capability"] = normalizedCapability
            });
        var canonicalEvidence = ManualPaymentCanonicalEvidenceFactory.FromProviderEvidence(providerEvidence);

        return ManualPaymentProviderAdapterResult.CreateRejected(providerRequest, providerEvidence, canonicalEvidence);
    }
}

public static class ManualPaymentProviderRequestFactory
{
    public static ManualPaymentProviderRequest Create(ManualPaymentCanonicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateLineage(request);
        var tenantId = RequiredGuid(request.TenantId, nameof(request.TenantId));
        var paymentIntentId = RequiredGuid(request.PaymentIntentId, nameof(request.PaymentIntentId));
        var amount = NormalizeAmount(request.Amount);
        var currency = NormalizeCurrency(request.Currency);
        var customer = ManualPaymentCustomer.Normalize(request.Customer);
        var channel = ManualPaymentChannelMapper.Map(request.Channel);
        var channelConfiguration = ResolveChannel(request, channel);
        var instructionVersion = RequiredText(request.InstructionVersion, nameof(request.InstructionVersion));
        var instructions = ResolveInstructions(request, channelConfiguration);
        var expirationUtc = ResolveExpiration(request);

        if (!request.ReferenceNumberRequired.HasValue)
        {
            throw new ManualPaymentRejectedException(
                "MANUAL_REFERENCE_POLICY_MISSING",
                "Manual Payment reference-number policy is required.");
        }

        if (!request.ProofRequired)
        {
            throw new ManualPaymentRejectedException(
                "MANUAL_PROOF_POLICY_MISSING",
                "Manual Payment proof policy is required.");
        }

        return new ManualPaymentProviderRequest(
            ManualPaymentProviderReference.ProviderName,
            tenantId,
            paymentIntentId,
            request.ActionId,
            request.CorrelationId,
            amount,
            currency,
            customer,
            request.ReferenceNumberRequired.Value,
            request.ProofRequired,
            channel,
            channelConfiguration.DisplayName.Trim(),
            instructions,
            expirationUtc,
            instructionVersion,
            ManualPaymentProviderReference.BuildInstructionIdentifier(tenantId, paymentIntentId, channel, instructionVersion),
            ManualPaymentProviderStatus.PendingManualPayment,
            PaymentStatus.AwaitingCustomer,
            request.EvidenceTimestampUtc);
    }

    public static ManualPaymentProviderRequest Rejected(
        ManualPaymentCanonicalRequest request,
        string reasonCode,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateLineage(request);

        var channel = Enum.IsDefined(request.Channel) ? request.Channel : ManualPaymentChannel.CustomInstructions;
        var tenantId = request.TenantId == Guid.Empty ? Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") : request.TenantId;
        var paymentIntentId = request.PaymentIntentId == Guid.Empty ? Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee") : request.PaymentIntentId;
        var instructionVersion = string.IsNullOrWhiteSpace(request.InstructionVersion)
            ? "unavailable"
            : request.InstructionVersion.Trim();

        return new ManualPaymentProviderRequest(
            ManualPaymentProviderReference.ProviderName,
            tenantId,
            paymentIntentId,
            request.ActionId,
            request.CorrelationId,
            request.Amount <= 0m ? 0m : request.Amount,
            string.IsNullOrWhiteSpace(request.Currency) ? "UNKNOWN" : request.Currency.Trim().ToUpperInvariant(),
            ManualPaymentCustomer.Normalize(request.Customer, allowMissing: true),
            request.ReferenceNumberRequired ?? false,
            request.ProofRequired,
            channel,
            channel.ToString(),
            string.Empty,
            request.ExpirationUtc,
            instructionVersion,
            ManualPaymentProviderReference.BuildInstructionIdentifier(tenantId, paymentIntentId, channel, instructionVersion),
            ManualPaymentProviderStatus.Rejected,
            PaymentStatus.Rejected,
            request.EvidenceTimestampUtc,
            reasonCode,
            reason);
    }

    private static void ValidateLineage(ManualPaymentCanonicalRequest request)
    {
        RequiredGuid(request.ActionId.Value, nameof(request.ActionId));
        RequiredText(request.CorrelationId.Value, nameof(request.CorrelationId));
        if (request.EvidenceTimestampUtc == default)
        {
            throw new ArgumentException("Manual Payment evidence timestamp is required.");
        }
    }

    private static ManualPaymentChannelConfiguration ResolveChannel(
        ManualPaymentCanonicalRequest request,
        ManualPaymentChannel channel)
    {
        if (request.SupportedChannels.Count == 0)
        {
            throw new ManualPaymentRejectedException(
                "MANUAL_CHANNEL_CONFIGURATION_MISSING",
                "Manual Payment requires supported channel configuration.");
        }

        var channelConfiguration = request.SupportedChannels.FirstOrDefault(item => item.Enabled && item.Channel == channel);
        if (channelConfiguration is null)
        {
            throw new ManualPaymentRejectedException(
                "MANUAL_CHANNEL_UNSUPPORTED",
                "Manual Payment channel is not supported by the provided configuration.");
        }

        if (string.IsNullOrWhiteSpace(channelConfiguration.DisplayName) ||
            string.IsNullOrWhiteSpace(channelConfiguration.Instructions))
        {
            throw new ManualPaymentRejectedException(
                "MANUAL_CHANNEL_CONFIGURATION_INVALID",
                "Manual Payment channel configuration must include display name and instructions.");
        }

        return channelConfiguration;
    }

    private static string ResolveInstructions(
        ManualPaymentCanonicalRequest request,
        ManualPaymentChannelConfiguration channelConfiguration)
    {
        if (!request.InstructionsConfigured || string.IsNullOrWhiteSpace(request.DefaultInstructions))
        {
            throw new ManualPaymentRejectedException(
                "MANUAL_INSTRUCTIONS_MISSING",
                "Manual Payment instructions must be configured.");
        }

        return string.Concat(request.DefaultInstructions.Trim(), "\n", channelConfiguration.Instructions.Trim());
    }

    private static DateTimeOffset ResolveExpiration(ManualPaymentCanonicalRequest request)
    {
        if (!request.ExpirationConfigured || request.ExpirationUtc is null)
        {
            throw new ManualPaymentRejectedException(
                "MANUAL_EXPIRATION_POLICY_MISSING",
                "Manual Payment expiration policy must be configured.");
        }

        if (request.ExpirationUtc.Value <= request.EvidenceTimestampUtc)
        {
            throw new ManualPaymentRejectedException(
                "MANUAL_EXPIRATION_POLICY_INVALID",
                "Manual Payment expiration must be later than the evidence timestamp.");
        }

        return request.ExpirationUtc.Value;
    }

    private static Guid RequiredGuid(Guid value, string fieldName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"Canonical Manual Payment field '{fieldName}' is required.");
        }

        return value;
    }

    private static string RequiredText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Canonical Manual Payment field '{fieldName}' is required.");
        }

        return value.Trim();
    }

    private static decimal NormalizeAmount(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new ArgumentException("Manual Payment amount must be positive.");
        }

        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCurrency(string currency)
    {
        var normalized = RequiredText(currency, nameof(currency)).ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character => character < 'A' || character > 'Z'))
        {
            throw new ArgumentException("Manual Payment currency must be an ISO-4217 code.");
        }

        return normalized;
    }
}

public static class ManualPaymentCanonicalEvidenceFactory
{
    public static ProviderEvidence FromProviderEvidence(ManualPaymentProviderEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider_status"] = evidence.ProviderStatus.ToString(),
            ["canonical_status"] = evidence.CanonicalStatus.ToString(),
            ["instruction_identifier"] = evidence.InstructionIdentifier,
            ["reference_required"] = evidence.ReferenceRequired.ToString(CultureInfo.InvariantCulture),
            ["proof_required"] = evidence.ProofRequired.ToString(CultureInfo.InvariantCulture),
            ["channel"] = ManualPaymentChannelMapper.ToCanonical(evidence.Channel),
            ["instruction_version"] = evidence.InstructionVersion,
            ["expires_utc"] = evidence.ExpirationUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
        };

        foreach (var (key, value) in evidence.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                metadata[key.Trim()] = value;
            }
        }

        var status = evidence.ProviderStatus == ManualPaymentProviderStatus.Rejected
            ? EvidenceStatus.Rejected
            : EvidenceStatus.Recorded;
        var evidenceType = evidence.ProviderStatus == ManualPaymentProviderStatus.Rejected
            ? EvidenceType.ProviderNotification
            : EvidenceType.Receipt;
        var evidenceReference = evidence.ProviderStatus == ManualPaymentProviderStatus.Rejected
            ? $"{evidence.InstructionIdentifier}:rejected"
            : evidence.InstructionIdentifier;

        return ProviderEvidenceFactory.Create(
            new EvidenceId(ManualPaymentProviderReference.CreateDeterministicId(
                "manual-provider-evidence",
                evidence.ActionId.Value.ToString("N"),
                evidenceReference,
                evidence.ProviderStatus.ToString(),
                evidence.EvidenceTimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))),
            evidence.ActionId,
            evidenceType,
            status,
            new EvidenceReference(evidenceReference),
            new EvidenceSource(ManualPaymentProviderReference.ProviderName),
            new EvidenceTimestamp(evidence.EvidenceTimestampUtc),
            new EvidenceHash(ManualPaymentProviderReference.Hash(
                evidence.InstructionIdentifier,
                evidence.ProviderStatus.ToString(),
                evidence.CanonicalStatus.ToString(),
                evidence.Channel.ToString(),
                evidence.CorrelationId.Value)),
            new ProviderPayloadReference($"manual_payment:instruction:{evidence.InstructionIdentifier}"),
            evidence.CorrelationId,
            new EvidenceMetadata(metadata));
    }
}

public static class ManualPaymentChannelMapper
{
    public static ManualPaymentChannel Map(ManualPaymentChannel channel)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ManualPaymentRejectedException(
                "MANUAL_CHANNEL_UNKNOWN",
                "Manual Payment channel is unknown.");
        }

        return channel;
    }

    public static string ToCanonical(ManualPaymentChannel channel) => channel switch
    {
        ManualPaymentChannel.BankTransfer => "bank_transfer",
        ManualPaymentChannel.VodafoneCash => "vodafone_cash",
        ManualPaymentChannel.Instapay => "instapay",
        ManualPaymentChannel.WalletTransfer => "wallet_transfer",
        ManualPaymentChannel.CashDeposit => "cash_deposit",
        ManualPaymentChannel.CustomInstructions => "custom_instructions",
        ManualPaymentChannel.OfflineInvoice => "offline_invoice",
        _ => throw new ManualPaymentRejectedException("MANUAL_CHANNEL_UNKNOWN", "Manual Payment channel is unknown.")
    };
}

public static class ManualPaymentProviderReference
{
    public const string ProviderName = "manual_payment";

    public static string BuildInstructionIdentifier(
        Guid tenantId,
        Guid paymentIntentId,
        ManualPaymentChannel channel,
        string instructionVersion) =>
        string.Concat(
            ProviderName,
            ":",
            tenantId.ToString("N"),
            ":",
            paymentIntentId.ToString("N"),
            ":",
            ManualPaymentChannelMapper.ToCanonical(channel),
            ":",
            instructionVersion.Trim());

    public static Guid CreateDeterministicId(params string[] parts)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    public static string Hash(params string[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)))).ToLowerInvariant();
}

public sealed record ManualPaymentCanonicalRequest(
    Guid TenantId,
    Guid PaymentIntentId,
    PaymentActionId ActionId,
    ActionCorrelationId CorrelationId,
    decimal Amount,
    string Currency,
    ManualPaymentCustomer Customer,
    bool? ReferenceNumberRequired,
    bool ProofRequired,
    ManualPaymentChannel Channel,
    IReadOnlyList<ManualPaymentChannelConfiguration> SupportedChannels,
    bool InstructionsConfigured,
    string DefaultInstructions,
    bool ExpirationConfigured,
    DateTimeOffset? ExpirationUtc,
    string InstructionVersion,
    DateTimeOffset EvidenceTimestampUtc);

public sealed record ManualPaymentProviderRequest(
    string ProviderName,
    Guid TenantId,
    Guid PaymentIntentId,
    PaymentActionId ActionId,
    ActionCorrelationId CorrelationId,
    decimal Amount,
    string Currency,
    ManualPaymentCustomer Customer,
    bool ReferenceNumberRequired,
    bool ProofRequired,
    ManualPaymentChannel Channel,
    string ChannelDisplayName,
    string Instructions,
    DateTimeOffset? ExpirationUtc,
    string InstructionVersion,
    string InstructionIdentifier,
    ManualPaymentProviderStatus ProviderStatus,
    PaymentStatus CanonicalStatus,
    DateTimeOffset RequestTimestampUtc,
    string? RejectionCode = null,
    string? RejectionReason = null);

public sealed record ManualPaymentProviderEvidence(
    Guid TenantId,
    Guid PaymentIntentId,
    PaymentActionId ActionId,
    ActionCorrelationId CorrelationId,
    string InstructionIdentifier,
    bool ReferenceRequired,
    bool ProofRequired,
    DateTimeOffset? ExpirationUtc,
    ManualPaymentChannel Channel,
    string InstructionVersion,
    DateTimeOffset EvidenceTimestampUtc,
    ManualPaymentProviderStatus ProviderStatus,
    PaymentStatus CanonicalStatus,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ManualPaymentProviderEvidence Pending(
        ManualPaymentProviderRequest request,
        DateTimeOffset evidenceTimestampUtc)
    {
        return new ManualPaymentProviderEvidence(
            request.TenantId,
            request.PaymentIntentId,
            request.ActionId,
            request.CorrelationId,
            request.InstructionIdentifier,
            request.ReferenceNumberRequired,
            request.ProofRequired,
            request.ExpirationUtc,
            request.Channel,
            request.InstructionVersion,
            evidenceTimestampUtc,
            ManualPaymentProviderStatus.PendingManualPayment,
            PaymentStatus.AwaitingCustomer,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["amount"] = request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                ["currency"] = request.Currency,
                ["customer_reference"] = request.Customer.CustomerReference,
                ["channel_display_name"] = request.ChannelDisplayName
            });
    }

    public static ManualPaymentProviderEvidence Rejected(
        ManualPaymentProviderRequest request,
        string rejectionCode,
        string rejectionReason,
        DateTimeOffset evidenceTimestampUtc,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider_error_code"] = rejectionCode,
            ["provider_error_reason"] = rejectionReason
        };

        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                values[key] = value;
            }
        }

        return new ManualPaymentProviderEvidence(
            request.TenantId,
            request.PaymentIntentId,
            request.ActionId,
            request.CorrelationId,
            request.InstructionIdentifier,
            request.ReferenceNumberRequired,
            request.ProofRequired,
            request.ExpirationUtc,
            request.Channel,
            request.InstructionVersion,
            evidenceTimestampUtc,
            ManualPaymentProviderStatus.Rejected,
            PaymentStatus.Rejected,
            values);
    }
}

public sealed record ManualPaymentCustomer(
    string CustomerReference,
    string? Email,
    string? Phone)
{
    public static ManualPaymentCustomer Normalize(ManualPaymentCustomer customer, bool allowMissing = false)
    {
        if (customer is null)
        {
            if (allowMissing)
            {
                return new ManualPaymentCustomer("unknown", null, null);
            }

            throw new ArgumentException("Manual Payment customer is required.");
        }

        if (string.IsNullOrWhiteSpace(customer.CustomerReference))
        {
            if (allowMissing)
            {
                return customer with { CustomerReference = "unknown" };
            }

            throw new ArgumentException("Manual Payment customer reference is required.");
        }

        return customer with
        {
            CustomerReference = customer.CustomerReference.Trim(),
            Email = string.IsNullOrWhiteSpace(customer.Email) ? null : customer.Email.Trim(),
            Phone = string.IsNullOrWhiteSpace(customer.Phone) ? null : customer.Phone.Trim()
        };
    }
}

public sealed record ManualPaymentProviderAdapterResult
{
    public bool Succeeded { get; init; }
    public bool Rejected { get; init; }
    public ManualPaymentProviderRequest? ProviderRequest { get; init; }
    public ManualPaymentProviderEvidence? ProviderEvidence { get; init; }
    public ProviderEvidence? CanonicalEvidence { get; init; }
    public CanonicalProviderError? Error { get; init; }

    private ManualPaymentProviderAdapterResult()
    {
    }

    public static ManualPaymentProviderAdapterResult Success(
        ManualPaymentProviderRequest request,
        ManualPaymentProviderEvidence providerEvidence,
        ProviderEvidence canonicalEvidence) =>
        new()
        {
            Succeeded = true,
            ProviderRequest = request,
            ProviderEvidence = providerEvidence,
            CanonicalEvidence = canonicalEvidence
        };

    public static ManualPaymentProviderAdapterResult CreateRejected(
        ManualPaymentProviderRequest request,
        ManualPaymentProviderEvidence providerEvidence,
        ProviderEvidence canonicalEvidence) =>
        new()
        {
            Rejected = true,
            ProviderRequest = request,
            ProviderEvidence = providerEvidence,
            CanonicalEvidence = canonicalEvidence
        };

    public static ManualPaymentProviderAdapterResult Failed(CanonicalProviderError error) =>
        new()
        {
            Error = error
        };
}

public enum ManualPaymentProviderStatus
{
    Unknown = 0,
    PendingManualPayment = 1,
    Rejected = 2
}

public sealed class ManualPaymentRejectedException : Exception
{
    public ManualPaymentRejectedException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
