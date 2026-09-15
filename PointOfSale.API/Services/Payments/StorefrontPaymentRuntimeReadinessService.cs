using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments;

public sealed record StorefrontPaymentRuntimeReadiness(
    bool IsReady,
    string? FirstFailedGate)
{
    public static StorefrontPaymentRuntimeReadiness Ready { get; } = new(true, null);

    public static StorefrontPaymentRuntimeReadiness Blocked(string gate) => new(false, gate);
}

public interface IStorefrontPaymentRuntimeReadinessService
{
    Task<StorefrontPaymentRuntimeReadiness> EvaluateAsync(
        Guid tenantId,
        string providerCode,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The fail-closed availability authority shared by the storefront payment catalog.
/// It evaluates the current SQL tenant row before provider runtime admission; a stale
/// read-model projection can therefore never expose a disabled or deleted provider.
/// </summary>
public sealed class StorefrontPaymentRuntimeReadinessService : IStorefrontPaymentRuntimeReadinessService
{
    private const string DataProtectorPurpose = "PaymentMethodSecrets";

    private readonly ApplicationDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly IStripeTenantConfigurationResolver _stripeResolver;
    private readonly IPaymobTenantConfigurationResolver _paymobResolver;
    private readonly IFawryTenantConfigurationResolver _fawryResolver;
    private readonly IManualPaymentCapabilityAdmissionAuthority _manualPaymentAdmission;
    private readonly IOptions<StripeOptions> _stripeOptions;
    private readonly IOptions<PaymobOptions> _paymobOptions;
    private readonly IOptions<FawryOptions> _fawryOptions;
    private readonly ILogger<StorefrontPaymentRuntimeReadinessService> _logger;

    public StorefrontPaymentRuntimeReadinessService(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        IStripeTenantConfigurationResolver stripeResolver,
        IPaymobTenantConfigurationResolver paymobResolver,
        IFawryTenantConfigurationResolver fawryResolver,
        IManualPaymentCapabilityAdmissionAuthority manualPaymentAdmission,
        IOptions<StripeOptions> stripeOptions,
        IOptions<PaymobOptions> paymobOptions,
        IOptions<FawryOptions> fawryOptions,
        ILogger<StorefrontPaymentRuntimeReadinessService> logger)
    {
        _dbContext = dbContext;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectorPurpose);
        _stripeResolver = stripeResolver;
        _paymobResolver = paymobResolver;
        _fawryResolver = fawryResolver;
        _manualPaymentAdmission = manualPaymentAdmission;
        _stripeOptions = stripeOptions;
        _paymobOptions = paymobOptions;
        _fawryOptions = fawryOptions;
        _logger = logger;
    }

    public async Task<StorefrontPaymentRuntimeReadiness> EvaluateAsync(
        Guid tenantId,
        string providerCode,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("TENANT_REQUIRED");
        }

        var normalizedCode = providerCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalizedCode is not ("COD" or "MANUALWALLET" or "PAYMOB" or "STRIPE" or "FAWRY"))
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("PROVIDER_UNSUPPORTED");
        }

        if (FawryFinancialAuthorityPolicy.IsFawry(normalizedCode))
        {
            return StorefrontPaymentRuntimeReadiness.Blocked(FawryFinancialAuthorityPolicy.Code);
        }

        var paymentMethod = await _dbContext.TenantPaymentMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(method =>
                method.TenantId == tenantId &&
                method.Code == normalizedCode,
                cancellationToken);

        var tenantGate = EvaluateTenantMethodGate(paymentMethod, normalizedCode);
        if (tenantGate is not null)
        {
            return StorefrontPaymentRuntimeReadiness.Blocked(tenantGate);
        }

        try
        {
            return normalizedCode switch
            {
                "COD" => StorefrontPaymentRuntimeReadiness.Ready,
                "STRIPE" => await EvaluateStripeAsync(tenantId, cancellationToken),
                "PAYMOB" => await EvaluatePaymobAsync(tenantId, cancellationToken),
                "MANUALWALLET" => await EvaluateManualPaymentAsync(tenantId, paymentMethod!, cancellationToken),
                _ => StorefrontPaymentRuntimeReadiness.Blocked("PROVIDER_UNSUPPORTED")
            };
        }
        catch (PaymentException exception)
        {
            return StorefrontPaymentRuntimeReadiness.Blocked(exception.ErrorCode);
        }
        catch (FawryApplicationException exception)
        {
            return StorefrontPaymentRuntimeReadiness.Blocked(exception.Code);
        }
        catch (ManualPaymentApplicationException exception)
        {
            return StorefrontPaymentRuntimeReadiness.Blocked(exception.Code);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Storefront payment runtime readiness evaluation failed | TenantId: {TenantId} | Provider: {Provider}",
                tenantId,
                normalizedCode);
            return StorefrontPaymentRuntimeReadiness.Blocked("RUNTIME_READINESS_UNAVAILABLE");
        }
    }

    private static string? EvaluateTenantMethodGate(TenantPaymentMethod? paymentMethod, string providerCode)
    {
        if (paymentMethod is null)
        {
            return "TENANT_METHOD_MISSING";
        }

        if (paymentMethod.IsDeleted)
        {
            return "TENANT_METHOD_DELETED";
        }

        if (!paymentMethod.IsEnabled)
        {
            return "TENANT_METHOD_DISABLED";
        }

        var requiredCapabilityAvailable = providerCode switch
        {
            "COD" or "MANUALWALLET" => paymentMethod.SupportsOffline,
            _ => paymentMethod.SupportsOnline
        };

        return requiredCapabilityAvailable ? null : "TENANT_METHOD_CAPABILITY_DISABLED";
    }

    private async Task<StorefrontPaymentRuntimeReadiness> EvaluateStripeAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var options = _stripeOptions.Value;
        if (!StripeOptionsValidator.IsRuntimeEnabled(options))
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("STRIPE_RUNTIME_DISABLED");
        }

        if (options.Environment == StripeEnvironment.Sandbox &&
            (!options.SandboxMode || !options.FeatureFlags.SandboxEnabled))
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("STRIPE_SANDBOX_DISABLED");
        }

        if (options.Environment == StripeEnvironment.Production &&
            (options.SandboxMode || !options.FeatureFlags.ProductionEnabled))
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("STRIPE_PRODUCTION_DISABLED");
        }

        if (!options.FeatureFlags.PaymentIntentsEnabled)
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("STRIPE_PAYMENT_INTENT_CAPABILITY_DISABLED");
        }

        if (!options.FeatureFlags.WebhooksEnabled)
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("STRIPE_WEBHOOK_CAPABILITY_DISABLED");
        }

        await _stripeResolver.ResolveCredentialsAsync(tenantId, StripeOperationalCapability.PaymentIntent, cancellationToken);
        await _stripeResolver.ResolveCredentialsAsync(tenantId, StripeOperationalCapability.Webhook, cancellationToken);
        return StorefrontPaymentRuntimeReadiness.Ready;
    }

    private async Task<StorefrontPaymentRuntimeReadiness> EvaluatePaymobAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var options = _paymobOptions.Value;
        if (!PaymobOptionsValidator.IsRuntimeEnabled(options))
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("PAYMOB_RUNTIME_DISABLED");
        }

        if (!options.FeatureFlags.CredentialValidationEnabled ||
            !options.FeatureFlags.EnvironmentValidationEnabled)
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("PAYMOB_RUNTIME_VALIDATION_DISABLED");
        }

        if (options.Environment == PaymobEnvironment.Sandbox &&
            (!options.SandboxMode || !options.FeatureFlags.SandboxEnabled || !options.Capabilities.SupportsSandboxEnvironment))
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("PAYMOB_SANDBOX_DISABLED");
        }

        if (options.Environment == PaymobEnvironment.Production &&
            (options.SandboxMode || !options.FeatureFlags.ProductionEnabled || !options.Capabilities.SupportsProductionEnvironment))
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("PAYMOB_PRODUCTION_DISABLED");
        }

        if (!options.Capabilities.SupportsTenantScopedCredentials ||
            !options.Capabilities.SupportsOfficialHttpsApi ||
            !options.Capabilities.ProviderAdapterCertified ||
            !options.Capabilities.PaymentExecutionCertified ||
            !options.FeatureFlags.PaymentExecutionEnabled)
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("PAYMOB_PAYMENT_EXECUTION_CAPABILITY_DISABLED");
        }

        if (!options.Capabilities.SupportsHmacVerification ||
            !options.Capabilities.CallbackCertified ||
            !options.FeatureFlags.WebhookExecutionEnabled)
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("PAYMOB_WEBHOOK_CAPABILITY_DISABLED");
        }

        if (!options.FeatureFlags.CallbackExecutionEnabled ||
            !options.Callback.Configured ||
            options.Callback.CallbackUrl is not { IsAbsoluteUri: true } ||
            options.Callback.WebhookUrl is not { IsAbsoluteUri: true })
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("PAYMOB_CALLBACK_CAPABILITY_DISABLED");
        }

        await _paymobResolver.ResolveCredentialsAsync(tenantId, PaymobOperationalCapability.PaymentIntent, cancellationToken);
        await _paymobResolver.ResolveCredentialsAsync(tenantId, PaymobOperationalCapability.Webhook, cancellationToken);
        await _paymobResolver.ResolveCredentialsAsync(tenantId, PaymobOperationalCapability.Callback, cancellationToken);
        return StorefrontPaymentRuntimeReadiness.Ready;
    }

    private async Task<StorefrontPaymentRuntimeReadiness> EvaluateManualPaymentAsync(
        Guid tenantId,
        TenantPaymentMethod paymentMethod,
        CancellationToken cancellationToken)
    {
        if (!HasValidManualWalletConfiguration(paymentMethod))
        {
            return StorefrontPaymentRuntimeReadiness.Blocked("MANUAL_PAYMENT_TENANT_CONFIGURATION_INVALID");
        }

        var requiredCapabilities = new[]
        {
            ManualPaymentOperationalCapability.ReferencePaymentInitialization,
            ManualPaymentOperationalCapability.EvidenceAdmission,
            ManualPaymentOperationalCapability.ProofUpload,
            ManualPaymentOperationalCapability.PendingReviewRead,
            ManualPaymentOperationalCapability.ProofAccess,
            ManualPaymentOperationalCapability.OperatorApproval,
            ManualPaymentOperationalCapability.OperatorRejection
        };

        foreach (var capability in requiredCapabilities)
        {
            await _manualPaymentAdmission.AdmitAsync(tenantId, capability, cancellationToken: cancellationToken);
        }

        return StorefrontPaymentRuntimeReadiness.Ready;
    }

    private bool HasValidManualWalletConfiguration(TenantPaymentMethod paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod.ConfigEncrypted))
        {
            return false;
        }

        try
        {
            var decrypted = _dataProtector.Unprotect(paymentMethod.ConfigEncrypted);
            var configuration = JsonSerializer.Deserialize<Dictionary<string, string>>(
                decrypted,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return PaymentMethodConfigurationValidator.Validate(paymentMethod.Code, configuration)
                .IsConfigurationValid;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Manual payment tenant configuration could not be decrypted | TenantPaymentMethodId: {TenantPaymentMethodId}",
                paymentMethod.Id);
            return false;
        }
    }
}
