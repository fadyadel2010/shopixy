using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments.Fawry;

public enum FawryOperationalCapability
{
    PaymentExecution = 0,
    WebhookAuthentication = 1
}

public interface IFawryTenantConfigurationResolver
{
    Task<FawryTenantRuntimeCredentials> ResolveCredentialsAsync(
        Guid tenantId,
        FawryOperationalCapability capability,
        CancellationToken cancellationToken = default);

    Task<FawryTenantWebhookAuthority> ResolveWebhookAuthorityAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The application-owned authority for current tenant Fawry configuration.
/// Global Fawry credentials are never returned or used as a tenant fallback.
/// </summary>
public sealed class FawryTenantConfigurationResolver : IFawryTenantConfigurationResolver
{
    private const string DataProtectorPurpose = "PaymentMethodSecrets";
    private const int MinimumCredentialLength = 8;
    private const int MinimumSecureKeyLength = 12;

    private readonly ApplicationDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly IOptions<FawryOptions> _options;
    private readonly ILogger<FawryTenantConfigurationResolver> _logger;

    public FawryTenantConfigurationResolver(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<FawryOptions> options,
        ILogger<FawryTenantConfigurationResolver> logger)
    {
        _dbContext = dbContext;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectorPurpose);
        _options = options;
        _logger = logger;
    }

    public async Task<FawryTenantRuntimeCredentials> ResolveCredentialsAsync(
        Guid tenantId,
        FawryOperationalCapability capability,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw FawryApplicationException.InvalidRequest("Trusted tenant identity is required.");
        }

        var options = _options.Value;
        EnsureCapabilityEnabled(options, capability);

        if (capability == FawryOperationalCapability.WebhookAuthentication)
        {
            await EnsureActiveTenantAsync(tenantId, cancellationToken);
        }

        var paymentMethod = await _dbContext.TenantPaymentMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(pm =>
                pm.TenantId == tenantId &&
                pm.Code == "FAWRY",
                cancellationToken);

        if (paymentMethod is null)
        {
            throw FawryApplicationException.ConfigurationMissing();
        }

        if (paymentMethod.IsDeleted)
        {
            throw FawryApplicationException.ConfigurationDeleted();
        }

        if (!paymentMethod.IsEnabled)
        {
            throw FawryApplicationException.ProviderDisabled();
        }

        if (!paymentMethod.SupportsOnline)
        {
            throw FawryApplicationException.CapabilityDisabled();
        }

        EnsureTenantEnvironment(paymentMethod, options);

        var configuration = DecryptConfiguration(paymentMethod);
        if (configuration is null || !HasValidCredentialShape(configuration))
        {
            throw FawryApplicationException.InvalidCredentials();
        }

        return new FawryTenantRuntimeCredentials(
            tenantId,
            paymentMethod.Id,
            configuration.MerchantCode,
            configuration.ApiKey,
            configuration.SecureKey,
            paymentMethod.IsSandbox);
    }

    public async Task<FawryTenantWebhookAuthority> ResolveWebhookAuthorityAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var credentials = await ResolveCredentialsAsync(
            tenantId,
            FawryOperationalCapability.WebhookAuthentication,
            cancellationToken);
        var options = _options.Value;

        return new FawryTenantWebhookAuthority(
            credentials.TenantId,
            credentials.TenantPaymentMethodId,
            credentials.MerchantCode,
            credentials.SecureKey,
            options.Environment,
            credentials.IsSandbox,
            options.Callback.CallbackUrl!,
            options.Callback.WebhookUrl!);
    }

    private async Task EnsureActiveTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var activeTenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(tenant =>
                tenant.Id == tenantId &&
                tenant.Status == TenantStatus.Active &&
                tenant.DeletedAtUtc == null,
                cancellationToken);

        if (!activeTenant)
        {
            throw FawryApplicationException.ConfigurationMissing();
        }
    }

    private static void EnsureCapabilityEnabled(
        FawryOptions options,
        FawryOperationalCapability capability)
    {
        if (!FawryOptionsValidator.IsRuntimeEnabled(options) ||
            !options.FeatureFlags.CredentialValidationEnabled ||
            !options.Capabilities.SupportsTenantScopedCredentials ||
            !options.Capabilities.SupportsOfficialHttpsApi ||
            !options.Capabilities.SupportsSha256Signature)
        {
            throw FawryApplicationException.CapabilityDisabled();
        }

        if (!options.FeatureFlags.EnvironmentValidationEnabled)
        {
            throw FawryApplicationException.EnvironmentMismatch();
        }

        if (options.Environment == FawryEnvironment.Sandbox &&
            (!options.SandboxMode ||
             !options.FeatureFlags.SandboxEnabled ||
             !options.Capabilities.SupportsSandboxEnvironment))
        {
            throw FawryApplicationException.EnvironmentMismatch();
        }

        if (options.Environment == FawryEnvironment.Production &&
            (options.SandboxMode ||
             !options.FeatureFlags.ProductionEnabled ||
             !options.Capabilities.SupportsProductionEnvironment))
        {
            throw FawryApplicationException.EnvironmentMismatch();
        }

        var capabilityEnabled = capability switch
        {
            FawryOperationalCapability.PaymentExecution =>
                options.FeatureFlags.PaymentExecutionEnabled &&
                options.Capabilities.PaymentExecutionCertified &&
                options.Capabilities.ProviderAdapterCertified,
            FawryOperationalCapability.WebhookAuthentication =>
                options.FeatureFlags.CallbackExecutionEnabled &&
                options.FeatureFlags.WebhookExecutionEnabled &&
                options.Capabilities.CallbackCertified &&
                options.Capabilities.WebhookCertified &&
                options.Capabilities.SupportsConfiguredServerNotification &&
                options.Callback.Configured &&
                options.Callback.CallbackUrl is { IsAbsoluteUri: true } &&
                options.Callback.WebhookUrl is { IsAbsoluteUri: true },
            _ => false
        };

        if (!capabilityEnabled)
        {
            throw FawryApplicationException.CapabilityDisabled();
        }
    }

    private static void EnsureTenantEnvironment(
        TenantPaymentMethod paymentMethod,
        FawryOptions options)
    {
        var expectedSandbox = options.Environment == FawryEnvironment.Sandbox;
        if (paymentMethod.IsSandbox != expectedSandbox ||
            paymentMethod.IsSandbox != options.SandboxMode)
        {
            throw FawryApplicationException.EnvironmentMismatch();
        }
    }

    private TenantFawryConfiguration? DecryptConfiguration(TenantPaymentMethod paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod.ConfigEncrypted))
        {
            return null;
        }

        try
        {
            var decrypted = _dataProtector.Unprotect(paymentMethod.ConfigEncrypted);
            var configuration = JsonSerializer.Deserialize<Dictionary<string, string>>(
                decrypted,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var validation = PaymentMethodConfigurationValidator.Validate(paymentMethod.Code, configuration);

            if (!validation.IsConfigurationValid)
            {
                return null;
            }

            return new TenantFawryConfiguration(
                validation.NormalizedConfiguration.GetValueOrDefault("merchantCode", string.Empty),
                validation.NormalizedConfiguration.GetValueOrDefault("apiKey", string.Empty),
                validation.NormalizedConfiguration.GetValueOrDefault("webhookSecret", string.Empty));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to resolve tenant Fawry credential configuration | TenantPaymentMethodId: {TenantPaymentMethodId}",
                paymentMethod.Id);
            return null;
        }
    }

    private static bool HasValidCredentialShape(TenantFawryConfiguration configuration)
    {
        return configuration.MerchantCode.Trim().Length >= MinimumCredentialLength &&
               configuration.ApiKey.Trim().Length >= MinimumCredentialLength &&
               configuration.SecureKey.Trim().Length >= MinimumSecureKeyLength;
    }

    private sealed record TenantFawryConfiguration(
        string MerchantCode,
        string ApiKey,
        string SecureKey);
}

public sealed record FawryTenantRuntimeCredentials(
    Guid TenantId,
    Guid TenantPaymentMethodId,
    string MerchantCode,
    string ApiKey,
    string SecureKey,
    bool IsSandbox);

public sealed record FawryTenantWebhookAuthority(
    Guid TenantId,
    Guid TenantPaymentMethodId,
    string MerchantCode,
    string SecureKey,
    FawryEnvironment Environment,
    bool IsSandbox,
    Uri CallbackUrl,
    Uri WebhookUrl);
