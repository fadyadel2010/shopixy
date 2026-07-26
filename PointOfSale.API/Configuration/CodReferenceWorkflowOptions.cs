using Microsoft.Extensions.Options;
using PointOfSale.API.Services.Payments.Cod;

namespace PointOfSale.API.Configuration;

public sealed record CodReferenceWorkflowOptions
{
    public const string SectionName = "Payments:CashOnDelivery:ReferenceWorkflow";

    public bool Enabled { get; init; }
    public CodReferenceWorkflowEnvironment Environment { get; init; } = CodReferenceWorkflowEnvironment.Disabled;
    public CodReferenceWorkflowFeatureFlags FeatureFlags { get; init; } = new();
    public CodCapabilityModel Capabilities { get; init; } = CodCapabilityModel.FoundationOnly;
    public CodCollectionPolicyConfiguration CollectionPolicy { get; init; } = new();
    public CodEligibilityPolicyConfiguration EligibilityPolicy { get; init; } = new();
    public CodSettlementPolicyConfiguration SettlementPolicy { get; init; } = new();
    public CodEvidencePolicyConfiguration EvidencePolicy { get; init; } = new();
    public CodReplayPolicyConfiguration ReplayPolicy { get; init; } = new();
    public CodRecoveryPolicyConfiguration RecoveryPolicy { get; init; } = new();
    public CodRetentionPolicyConfiguration RetentionPolicy { get; init; } = new();
}

public sealed record CodReferenceWorkflowFeatureFlags
{
    public bool Enabled { get; init; }
    public bool FoundationHealthEnabled { get; init; } = true;
    public bool ConfigurationValidationEnabled { get; init; } = true;
    public bool StartupValidationEnabled { get; init; } = true;
    public bool RuntimeExecutionEnabled { get; init; }
    public bool EvidenceAdmissionEnabled { get; init; }
    public bool LifecycleMutationEnabled { get; init; }
    public bool SettlementExecutionEnabled { get; init; }
    public bool ReplayExecutionEnabled { get; init; }
    public bool RecoveryExecutionEnabled { get; init; }
    public bool RetentionExecutionEnabled { get; init; }
}

public sealed record CodCollectionPolicyConfiguration
{
    public bool Configured { get; init; }
    public bool DeliveryAndCollectionAreIndependentFacts { get; init; } = true;
    public bool RequiresAcceptedLifecycleAuthority { get; init; } = true;
    public bool AllowsCheckoutCollectionFinality { get; init; }
}

public sealed record CodEligibilityPolicyConfiguration
{
    public bool Configured { get; init; }
    public bool RequiresPaymentMethodSelection { get; init; } = true;
    public bool RequiresCurrentOrderAuthority { get; init; } = true;
    public bool RequiresBoundedContextAuthority { get; init; } = true;
}

public sealed record CodSettlementPolicyConfiguration
{
    public bool Configured { get; init; }
    public bool RequiresCollectionBeforeSettlement { get; init; } = true;
    public bool KeepsTreasurySeparateFromPaymentFinality { get; init; } = true;
}

public sealed record CodEvidencePolicyConfiguration
{
    public bool Configured { get; init; }
    public bool EvidenceOnly { get; init; } = true;
    public bool RequiresSourceProvenance { get; init; } = true;
    public bool RequiresActorProvenance { get; init; } = true;
    public bool RequiresAmountAndCurrency { get; init; } = true;
}

public sealed record CodReplayPolicyConfiguration
{
    public bool Configured { get; init; }
    public bool RequiresDurableReplayIdentity { get; init; } = true;
    public bool DuplicateEvidenceFailsClosed { get; init; } = true;
}

public sealed record CodRecoveryPolicyConfiguration
{
    public bool Configured { get; init; }
    public bool RequiresCurrentSqlAuthority { get; init; } = true;
    public bool AmbiguousOutcomeRequiresReview { get; init; } = true;
}

public sealed record CodRetentionPolicyConfiguration
{
    public bool Configured { get; init; }
    public bool RequiresEvidenceRetentionPolicy { get; init; } = true;
    public TimeSpan MinimumEvidenceRetention { get; init; } = TimeSpan.FromDays(90);
}

public enum CodReferenceWorkflowEnvironment
{
    Disabled = 0,
    Sandbox = 1,
    Production = 2
}

public sealed class CodReferenceWorkflowOptionsValidator : IValidateOptions<CodReferenceWorkflowOptions>
{
    public ValidateOptionsResult Validate(string? name, CodReferenceWorkflowOptions options)
    {
        var result = CodReferenceWorkflowConfigurationValidator.Validate(options);
        return result.IsValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(result.Errors);
    }
}

public static class CodReferenceWorkflowConfigurationValidator
{
    public static CodReferenceWorkflowConfigurationValidationResult Validate(CodReferenceWorkflowOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var enabled = options.Enabled || options.FeatureFlags.Enabled;

        if (!options.Enabled && options.FeatureFlags.Enabled)
        {
            errors.Add("COD Reference Workflow must be enabled when the foundation feature flag is enabled.");
        }

        if (!enabled && options.Environment != CodReferenceWorkflowEnvironment.Disabled)
        {
            warnings.Add("COD Reference Workflow is disabled; configured environment is informational only.");
        }

        if (enabled && options.Environment == CodReferenceWorkflowEnvironment.Disabled)
        {
            errors.Add("COD Reference Workflow requires Sandbox or Production environment when enabled.");
        }

        if (!options.Capabilities.ReferenceWorkflowIdentityDefined)
        {
            errors.Add("COD Reference Workflow requires canonical reference workflow identity capability.");
        }

        if (!options.Capabilities.AuthorityContractsDefined)
        {
            errors.Add("COD Reference Workflow requires authority contract capability.");
        }

        ValidateExecutionFlags(options, errors);

        if (enabled)
        {
            ValidateEnabledPolicies(options, errors);
        }

        return new CodReferenceWorkflowConfigurationValidationResult(
            errors.Count == 0,
            enabled,
            options.Environment,
            options.CollectionPolicy.Configured,
            options.EligibilityPolicy.Configured,
            options.SettlementPolicy.Configured,
            options.EvidencePolicy.Configured,
            options.ReplayPolicy.Configured,
            options.RecoveryPolicy.Configured,
            options.RetentionPolicy.Configured,
            errors,
            warnings);
    }

    private static void ValidateExecutionFlags(
        CodReferenceWorkflowOptions options,
        ICollection<string> errors)
    {
        if (options.FeatureFlags.RuntimeExecutionEnabled)
        {
            errors.Add("COD Reference Workflow Foundation must not enable Runtime execution.");
        }

        if (options.FeatureFlags.EvidenceAdmissionEnabled)
        {
            errors.Add("COD Reference Workflow Foundation must not enable evidence admission.");
        }

        if (options.FeatureFlags.LifecycleMutationEnabled)
        {
            errors.Add("COD Reference Workflow Foundation must not enable lifecycle mutation.");
        }

        if (options.FeatureFlags.SettlementExecutionEnabled)
        {
            errors.Add("COD Reference Workflow Foundation must not enable settlement execution.");
        }

        if (options.FeatureFlags.ReplayExecutionEnabled ||
            options.FeatureFlags.RecoveryExecutionEnabled ||
            options.FeatureFlags.RetentionExecutionEnabled)
        {
            errors.Add("COD Reference Workflow Foundation must not enable replay, recovery, or retention execution.");
        }
    }

    private static void ValidateEnabledPolicies(
        CodReferenceWorkflowOptions options,
        ICollection<string> errors)
    {
        if (!options.CollectionPolicy.Configured ||
            !options.EligibilityPolicy.Configured ||
            !options.SettlementPolicy.Configured ||
            !options.EvidencePolicy.Configured ||
            !options.ReplayPolicy.Configured ||
            !options.RecoveryPolicy.Configured ||
            !options.RetentionPolicy.Configured)
        {
            errors.Add("COD Reference Workflow requires every canonical policy configuration before enabling.");
        }

        if (!options.CollectionPolicy.DeliveryAndCollectionAreIndependentFacts ||
            !options.CollectionPolicy.RequiresAcceptedLifecycleAuthority ||
            options.CollectionPolicy.AllowsCheckoutCollectionFinality)
        {
            errors.Add("COD collection policy must preserve independent facts and accepted lifecycle authority.");
        }

        if (!options.SettlementPolicy.RequiresCollectionBeforeSettlement ||
            !options.SettlementPolicy.KeepsTreasurySeparateFromPaymentFinality)
        {
            errors.Add("COD settlement policy must require prior collection and keep treasury separate from payment finality.");
        }

        if (!options.EvidencePolicy.EvidenceOnly ||
            !options.EvidencePolicy.RequiresSourceProvenance ||
            !options.EvidencePolicy.RequiresActorProvenance ||
            !options.EvidencePolicy.RequiresAmountAndCurrency)
        {
            errors.Add("COD evidence policy must remain evidence-only with source, actor, amount, and currency provenance.");
        }

        if (!options.ReplayPolicy.RequiresDurableReplayIdentity ||
            !options.ReplayPolicy.DuplicateEvidenceFailsClosed)
        {
            errors.Add("COD replay policy must require durable identity and fail closed for duplicate evidence.");
        }

        if (!options.RecoveryPolicy.RequiresCurrentSqlAuthority ||
            !options.RecoveryPolicy.AmbiguousOutcomeRequiresReview)
        {
            errors.Add("COD recovery policy must require current SQL authority and review for ambiguous outcomes.");
        }

        if (!options.RetentionPolicy.RequiresEvidenceRetentionPolicy ||
            options.RetentionPolicy.MinimumEvidenceRetention < TimeSpan.FromDays(30))
        {
            errors.Add("COD retention policy must define evidence retention of at least 30 days.");
        }
    }
}
