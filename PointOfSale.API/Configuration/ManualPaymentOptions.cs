using Microsoft.Extensions.Options;
using PointOfSale.API.Services.Payments.ManualPayment;

namespace PointOfSale.API.Configuration;

public sealed record ManualPaymentOptions
{
    public const string SectionName = "CommerceHub:Payments:ManualPayment";

    public bool Enabled { get; init; }
    // Legacy configuration/diagnostic metadata only. Manual payments have no gateway mode.
    public ManualPaymentEnvironment Environment { get; init; } = ManualPaymentEnvironment.Sandbox;
    public bool SandboxMode { get; init; } = true;
    public ManualPaymentFeatureFlags FeatureFlags { get; init; } = new();
    public ManualPaymentCapabilityModel Capabilities { get; init; } = new();
    public ManualPaymentInstructionConfiguration Instructions { get; init; } = new();
    public ManualPaymentProofConfiguration Proof { get; init; } = new();
    public ManualPaymentExpirationPolicy Expiration { get; init; } = new();
    public IReadOnlyList<ManualPaymentChannelConfiguration> Channels { get; init; } = Array.Empty<ManualPaymentChannelConfiguration>();
}

public sealed record ManualPaymentFeatureFlags
{
    public bool Enabled { get; init; }
    public bool SandboxEnabled { get; init; } = true;
    public bool ProductionEnabled { get; init; }
    public bool FoundationHealthEnabled { get; init; } = true;
    public bool ConfigurationValidationEnabled { get; init; } = true;
    public bool ProofUploadEnabled { get; init; }
    public bool ReviewExecutionEnabled { get; init; }
    public bool ApprovalExecutionEnabled { get; init; }
    public bool RejectionExecutionEnabled { get; init; }
    public bool ExpirationExecutionEnabled { get; init; }
    public bool RefundExecutionEnabled { get; init; }
}

public sealed record ManualPaymentCapabilityModel
{
    public static ManualPaymentCapabilityModel FoundationOnly { get; } = new();

    public bool SupportsTenantScopedConfiguration { get; init; } = true;
    public bool SupportsSandboxEnvironment { get; init; } = true;
    public bool SupportsProductionEnvironment { get; init; } = true;
    public bool SupportsInstructionConfiguration { get; init; } = true;
    public bool SupportsReceiptProofUpload { get; init; } = true;
    public bool SupportsBankTransfer { get; init; } = true;
    public bool SupportsVodafoneCash { get; init; } = true;
    public bool SupportsInstapay { get; init; } = true;
    public bool SupportsWalletTransfer { get; init; } = true;
    public bool SupportsCashDeposit { get; init; } = true;
    public bool SupportsCustomInstructions { get; init; } = true;
    public bool SupportsOfflineInvoice { get; init; } = true;
    public bool SupportsReferenceNumber { get; init; } = true;
    public bool SupportsExpirationPolicy { get; init; } = true;
    public bool SupportsAcceptedProofTypes { get; init; } = true;
    public bool ProviderAdapterCertified { get; init; }
    public bool RuntimeBridgeCertified { get; init; }
    public bool EvidenceAdapterCertified { get; init; }
    public bool ReviewWorkflowCertified { get; init; }
    public bool ApprovalWorkflowCertified { get; init; }
    public bool PersistenceCertified { get; init; }
}

public sealed record ManualPaymentInstructionConfiguration
{
    public bool Configured { get; init; }
    public string DefaultInstructions { get; init; } = string.Empty;
    public bool RequireReferenceNumber { get; init; } = true;
    public bool RequireCustomerProof { get; init; } = true;
}

public sealed record ManualPaymentProofConfiguration
{
    public IReadOnlyList<string> AcceptedContentTypes { get; init; } = new[]
    {
        "image/jpeg",
        "image/png",
        "application/pdf"
    };

    public long MaximumUploadBytes { get; init; } = 5 * 1024 * 1024;
}

public sealed record ManualPaymentExpirationPolicy
{
    public bool Configured { get; init; }
    public TimeSpan PendingPaymentTtl { get; init; } = TimeSpan.FromDays(2);
    public TimeSpan ProofAccessTtl { get; init; } = TimeSpan.FromDays(7);
}

public sealed record ManualPaymentChannelConfiguration
{
    public ManualPaymentChannel Channel { get; init; }
    public bool Enabled { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;
}

public enum ManualPaymentEnvironment
{
    Sandbox = 0,
    Production = 1
}

public enum ManualPaymentChannel
{
    BankTransfer = 0,
    VodafoneCash = 1,
    Instapay = 2,
    WalletTransfer = 3,
    CashDeposit = 4,
    CustomInstructions = 5,
    OfflineInvoice = 6
}

public sealed class ManualPaymentOptionsValidator : IValidateOptions<ManualPaymentOptions>
{
    public ValidateOptionsResult Validate(string? name, ManualPaymentOptions options)
    {
        var result = ManualPaymentConfigurationValidator.Validate(options);
        return result.IsValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(result.Errors);
    }

    /// <summary>
    /// Legacy deployment activation diagnostic.
    ///
    /// Manual Wallet has no gateway, so an independent deployment-level runtime-enable
    /// switch can only make an otherwise correctly configured and operational wallet
    /// unavailable. It is retained for backwards-compatible configuration binding and
    /// reporting only; the activation contract does not read it.
    /// </summary>
    public static bool IsProviderEnabled(ManualPaymentOptions options) =>
        options.Enabled || options.FeatureFlags.Enabled;
}

public static class ManualPaymentConfigurationValidator
{
    private const long MaxCertifiedUploadBytes = 25 * 1024 * 1024;
    private static readonly HashSet<string> SupportedProofContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/pdf"
    };

    public static ManualPaymentConfigurationValidationResult Validate(ManualPaymentOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // The Manual Payment foundation is a platform capability, not a gateway.
        //
        // There is deliberately no independent deployment-level runtime-enable switch:
        // a merchant wallet is activated by the tenant-owned payment method state, and
        // the runtime additionally requires a complete, valid foundation - a usable
        // manual channel, customer instructions, customer proof, an expiration policy and
        // the certified review, approval and rejection workflows. A deployment may narrow
        // the foundation (for example restrict the supported channels) but it can no
        // longer hide an otherwise correctly configured and operational wallet.
        const bool enabled = true;

        ValidateProofConfiguration(options.Proof, errors);

        ValidateEnabledConfiguration(options, errors);

        if (!options.Capabilities.SupportsTenantScopedConfiguration)
        {
            errors.Add("Manual Payment foundation requires tenant-scoped configuration capability.");
        }

        if (!options.Capabilities.SupportsInstructionConfiguration)
        {
            errors.Add("Manual Payment foundation requires instruction configuration capability.");
        }

        if (!options.Capabilities.SupportsAcceptedProofTypes)
        {
            errors.Add("Manual Payment foundation requires accepted proof type capability.");
        }

        if (options.FeatureFlags.ProofUploadEnabled && !options.Capabilities.SupportsReceiptProofUpload)
        {
            errors.Add("Manual Payment proof upload requires certified proof upload capability.");
        }

        if ((options.FeatureFlags.ReviewExecutionEnabled ||
             options.FeatureFlags.ApprovalExecutionEnabled ||
             options.FeatureFlags.RejectionExecutionEnabled) &&
            (!options.Capabilities.ReviewWorkflowCertified || !options.Capabilities.ApprovalWorkflowCertified))
        {
            errors.Add("Manual Payment review, approval, and rejection execution require separately certified workflow capabilities.");
        }

        if ((options.FeatureFlags.ExpirationExecutionEnabled || options.FeatureFlags.RefundExecutionEnabled) &&
            !options.Capabilities.EvidenceAdapterCertified)
        {
            errors.Add("Manual Payment expiration and refund execution require separately certified evidence/runtime capabilities.");
        }

        return new ManualPaymentConfigurationValidationResult(
            errors.Count == 0,
            enabled,
            options.Environment,
            options.SandboxMode,
            options.Instructions.Configured,
            options.Expiration.Configured,
            options.Channels.Count(channel => channel.Enabled),
            options.Proof.AcceptedContentTypes.Count,
            options.Proof.MaximumUploadBytes,
            errors,
            warnings);
    }

    private static void ValidateEnabledConfiguration(ManualPaymentOptions options, ICollection<string> errors)
    {
        var enabledChannels = options.Channels.Where(channel => channel.Enabled).ToArray();
        if (enabledChannels.Length == 0)
        {
            errors.Add("Manual Payment requires at least one enabled supported payment channel.");
        }

        if (!options.Instructions.Configured || string.IsNullOrWhiteSpace(options.Instructions.DefaultInstructions))
        {
            errors.Add("Manual Payment requires configured customer instructions before enabling the provider.");
        }

        if (!options.Instructions.RequireCustomerProof)
        {
            errors.Add("Manual Payment requires customer proof at foundation certification.");
        }

        if (!options.Expiration.Configured)
        {
            errors.Add("Manual Payment requires an explicit expiration policy before enabling the provider.");
        }

        if (options.Expiration.PendingPaymentTtl <= TimeSpan.Zero || options.Expiration.ProofAccessTtl <= TimeSpan.Zero)
        {
            errors.Add("Manual Payment expiration policy values must be positive.");
        }

        foreach (var channel in enabledChannels)
        {
            if (!Enum.IsDefined(channel.Channel))
            {
                errors.Add("Manual Payment has an unsupported payment channel configured.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(channel.DisplayName) || string.IsNullOrWhiteSpace(channel.Instructions))
            {
                errors.Add($"Manual Payment channel {channel.Channel} requires display name and instructions.");
            }
        }
    }

    private static void ValidateProofConfiguration(ManualPaymentProofConfiguration proof, ICollection<string> errors)
    {
        if (proof.MaximumUploadBytes <= 0 || proof.MaximumUploadBytes > MaxCertifiedUploadBytes)
        {
            errors.Add("Manual Payment maximum proof upload size must be positive and no greater than 25 MiB.");
        }

        if (proof.AcceptedContentTypes.Count == 0)
        {
            errors.Add("Manual Payment requires at least one accepted proof content type.");
        }

        foreach (var contentType in proof.AcceptedContentTypes)
        {
            if (string.IsNullOrWhiteSpace(contentType) || !SupportedProofContentTypes.Contains(contentType.Trim()))
            {
                errors.Add($"Manual Payment proof content type '{contentType}' is unsupported.");
            }
        }
    }
}
