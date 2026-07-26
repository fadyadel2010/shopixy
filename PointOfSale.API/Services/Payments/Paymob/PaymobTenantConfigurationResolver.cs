using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments.Paymob;

public enum PaymobOperationalCapability
{
    PaymentIntent = 0,
    Webhook = 1,
    Callback = 2,
    Refund = 3,
    Void = 4,
    Capture = 5,
    TransactionInquiry = 6,
    Recovery = 7
}

public interface IPaymobTenantConfigurationResolver
{
    Task<PaymobTenantRuntimeCredentials> ResolveCredentialsAsync(
        Guid tenantId,
        PaymobOperationalCapability capability,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The single application authority for admitting tenant Paymob credentials.
/// It always reads the current tenant row and never falls back to global credentials.
/// </summary>
public sealed class PaymobTenantConfigurationResolver : IPaymobTenantConfigurationResolver
{
    private const string DataProtectorPurpose = "PaymentMethodSecrets";

    private readonly ApplicationDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly IOptions<PaymobOptions> _options;
    private readonly ILogger<PaymobTenantConfigurationResolver> _logger;

    public PaymobTenantConfigurationResolver(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<PaymobOptions> options,
        ILogger<PaymobTenantConfigurationResolver> logger)
    {
        _dbContext = dbContext;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectorPurpose);
        _options = options;
        _logger = logger;
    }

    public async Task<PaymobTenantRuntimeCredentials> ResolveCredentialsAsync(
        Guid tenantId,
        PaymobOperationalCapability capability,
        CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        if (!IsCapabilityEnabled(options, capability))
        {
            throw new PaymobNotConfiguredException(tenantId);
        }

        var paymentMethod = await _dbContext.TenantPaymentMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(pm =>
                pm.TenantId == tenantId &&
                pm.Code == "PAYMOB" &&
                pm.IsEnabled &&
                !pm.IsDeleted &&
                !string.IsNullOrWhiteSpace(pm.ConfigEncrypted),
                cancellationToken);

        if (paymentMethod is null)
        {
            throw new PaymobNotConfiguredException(tenantId);
        }

        var configuration = DecryptConfiguration(paymentMethod);
        if (!IsTenantConfigurationValid(paymentMethod, configuration, options))
        {
            throw new PaymobNotConfiguredException(tenantId);
        }

        return new PaymobTenantRuntimeCredentials(
            tenantId,
            paymentMethod.Id,
            configuration.ApiKey,
            configuration.IntegrationId,
            configuration.IframeId,
            configuration.HmacSecret,
            paymentMethod.IsSandbox);
    }

    private static bool IsCapabilityEnabled(
        PaymobOptions options,
        PaymobOperationalCapability capability)
    {
        if (!PaymobOptionsValidator.IsRuntimeEnabled(options) ||
            !options.FeatureFlags.CredentialValidationEnabled ||
            !options.FeatureFlags.EnvironmentValidationEnabled)
        {
            return false;
        }

        if (options.Environment == PaymobEnvironment.Sandbox &&
            (!options.SandboxMode ||
             !options.FeatureFlags.SandboxEnabled ||
             !options.Capabilities.SupportsSandboxEnvironment))
        {
            return false;
        }

        if (options.Environment == PaymobEnvironment.Production &&
            (options.SandboxMode ||
             !options.FeatureFlags.ProductionEnabled ||
             !options.Capabilities.SupportsProductionEnvironment))
        {
            return false;
        }

        if (!options.Capabilities.SupportsTenantScopedCredentials ||
            !options.Capabilities.SupportsOfficialHttpsApi)
        {
            return false;
        }

        return capability switch
        {
            PaymobOperationalCapability.PaymentIntent or PaymobOperationalCapability.Recovery =>
                options.FeatureFlags.PaymentExecutionEnabled &&
                options.Capabilities.PaymentExecutionCertified &&
                options.Capabilities.ProviderAdapterCertified,
            PaymobOperationalCapability.Webhook =>
                options.FeatureFlags.WebhookExecutionEnabled &&
                options.Capabilities.SupportsHmacVerification &&
                options.Capabilities.CallbackCertified,
            PaymobOperationalCapability.Callback =>
                options.FeatureFlags.CallbackExecutionEnabled &&
                options.Capabilities.SupportsHmacVerification &&
                options.Capabilities.CallbackCertified,
            PaymobOperationalCapability.Refund or
            PaymobOperationalCapability.Void or
            PaymobOperationalCapability.Capture or
            PaymobOperationalCapability.TransactionInquiry =>
                options.FeatureFlags.RefundExecutionEnabled &&
                options.Capabilities.ProviderAdapterCertified,
            _ => false
        };
    }

    private TenantPaymobConfiguration DecryptConfiguration(TenantPaymentMethod paymentMethod)
    {
        try
        {
            var decrypted = _dataProtector.Unprotect(paymentMethod.ConfigEncrypted!);
            var configuration = JsonSerializer.Deserialize<Dictionary<string, string>>(
                decrypted,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var validation = PaymentMethodConfigurationValidator.Validate(paymentMethod.Code, configuration);

            if (!validation.IsConfigurationValid)
            {
                return new TenantPaymobConfiguration();
            }

            return new TenantPaymobConfiguration
            {
                ApiKey = validation.NormalizedConfiguration.GetValueOrDefault("apiKey", string.Empty),
                IntegrationId = validation.NormalizedConfiguration.GetValueOrDefault("integrationId", string.Empty),
                IframeId = validation.NormalizedConfiguration.GetValueOrDefault("iframeId", string.Empty),
                HmacSecret = validation.NormalizedConfiguration.GetValueOrDefault("hmacSecret", string.Empty)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt tenant Paymob credentials");
            return new TenantPaymobConfiguration();
        }
    }

    private static bool IsTenantConfigurationValid(
        TenantPaymentMethod paymentMethod,
        TenantPaymobConfiguration configuration,
        PaymobOptions options)
    {
        if (paymentMethod.IsSandbox != options.SandboxMode)
        {
            return false;
        }

        if (!string.Equals(
                paymentMethod.IsSandbox ? PaymobEnvironment.Sandbox.ToString() : PaymobEnvironment.Production.ToString(),
                options.Environment.ToString(),
                StringComparison.Ordinal))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(configuration.ApiKey) &&
               !string.IsNullOrWhiteSpace(configuration.HmacSecret) &&
               long.TryParse(configuration.IntegrationId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
               long.TryParse(configuration.IframeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }
}
