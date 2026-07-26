using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments.Stripe;

public enum StripeOperationalCapability
{
    PaymentIntent = 0,
    Webhook = 1,
    Refund = 2,
    Capture = 3,
    CheckoutSession = 4,
    CustomerPortal = 5
}

public interface IStripeTenantConfigurationResolver
{
    Task<StripeTenantRuntimeCredentials> ResolveCredentialsAsync(
        Guid tenantId,
        StripeOperationalCapability capability,
        CancellationToken cancellationToken = default);
}

public sealed class StripeTenantConfigurationResolver : IStripeTenantConfigurationResolver
{
    private const string DataProtectorPurpose = "PaymentMethodSecrets";

    private readonly ApplicationDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly IOptions<StripeOptions> _options;
    private readonly ILogger<StripeTenantConfigurationResolver> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public StripeTenantConfigurationResolver(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<StripeOptions> options,
        ILogger<StripeTenantConfigurationResolver> logger)
    {
        _dbContext = dbContext;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectorPurpose);
        _options = options;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<StripeTenantRuntimeCredentials> ResolveCredentialsAsync(
        Guid tenantId,
        StripeOperationalCapability capability,
        CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        if (!IsCapabilityEnabled(options, capability))
        {
            throw new StripeNotConfiguredException(tenantId);
        }

        var paymentMethod = await _dbContext.TenantPaymentMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(pm =>
                pm.TenantId == tenantId &&
                pm.Code == "STRIPE" &&
                pm.IsEnabled &&
                !pm.IsDeleted &&
                !string.IsNullOrWhiteSpace(pm.ConfigEncrypted),
                cancellationToken);

        if (paymentMethod is null)
        {
            throw new StripeNotConfiguredException(tenantId);
        }

        var configuration = DecryptConfiguration(paymentMethod);
        if (!IsTenantConfigurationValid(paymentMethod, configuration, options))
        {
            throw new StripeNotConfiguredException(tenantId);
        }

        return new StripeTenantRuntimeCredentials(
            tenantId,
            paymentMethod.Id,
            configuration.PublishableKey,
            configuration.SecretKey,
            configuration.WebhookSecret,
            paymentMethod.IsSandbox);
    }

    private static bool IsCapabilityEnabled(
        StripeOptions options,
        StripeOperationalCapability capability)
    {
        if (!StripeOptionsValidator.IsRuntimeEnabled(options))
        {
            return false;
        }

        if (options.Environment == StripeEnvironment.Sandbox &&
            (!options.SandboxMode || !options.FeatureFlags.SandboxEnabled))
        {
            return false;
        }

        if (options.Environment == StripeEnvironment.Production &&
            (options.SandboxMode || !options.FeatureFlags.ProductionEnabled))
        {
            return false;
        }

        return capability switch
        {
            StripeOperationalCapability.PaymentIntent => options.FeatureFlags.PaymentIntentsEnabled,
            StripeOperationalCapability.Webhook => options.FeatureFlags.WebhooksEnabled,
            StripeOperationalCapability.Refund => options.FeatureFlags.RefundsEnabled,
            StripeOperationalCapability.Capture => options.FeatureFlags.CaptureEnabled,
            StripeOperationalCapability.CheckoutSession => options.FeatureFlags.CheckoutSessionsEnabled,
            StripeOperationalCapability.CustomerPortal => options.FeatureFlags.CustomerPortalEnabled,
            _ => false
        };
    }

    private TenantStripeConfiguration DecryptConfiguration(TenantPaymentMethod paymentMethod)
    {
        try
        {
            var decrypted = _dataProtector.Unprotect(paymentMethod.ConfigEncrypted!);
            var configuration = JsonSerializer.Deserialize<Dictionary<string, string>>(decrypted, _jsonOptions);
            var validation = PaymentMethodConfigurationValidator.Validate(paymentMethod.Code, configuration);

            if (!validation.IsConfigurationValid)
            {
                return new TenantStripeConfiguration();
            }

            return new TenantStripeConfiguration
            {
                PublishableKey = validation.NormalizedConfiguration.GetValueOrDefault("publishableKey", string.Empty),
                SecretKey = validation.NormalizedConfiguration.GetValueOrDefault("secretKey", string.Empty),
                WebhookSecret = validation.NormalizedConfiguration.GetValueOrDefault("webhookSecret", string.Empty)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt tenant Stripe credentials");
            return new TenantStripeConfiguration();
        }
    }

    private static bool IsTenantConfigurationValid(
        TenantPaymentMethod paymentMethod,
        TenantStripeConfiguration configuration,
        StripeOptions options)
    {
        if (paymentMethod.IsSandbox != options.SandboxMode)
        {
            return false;
        }

        if (!string.Equals(
                paymentMethod.IsSandbox ? StripeEnvironment.Sandbox.ToString() : StripeEnvironment.Production.ToString(),
                options.Environment.ToString(),
                StringComparison.Ordinal))
        {
            return false;
        }

        var publishablePrefix = paymentMethod.IsSandbox ? "pk_test_" : "pk_live_";
        var secretPrefix = paymentMethod.IsSandbox ? "sk_test_" : "sk_live_";
        var restrictedPrefix = paymentMethod.IsSandbox ? "rk_test_" : "rk_live_";

        return configuration.PublishableKey.StartsWith(publishablePrefix, StringComparison.Ordinal)
            && (configuration.SecretKey.StartsWith(secretPrefix, StringComparison.Ordinal) ||
                configuration.SecretKey.StartsWith(restrictedPrefix, StringComparison.Ordinal))
            && configuration.WebhookSecret.StartsWith("whsec_", StringComparison.Ordinal);
    }
}
