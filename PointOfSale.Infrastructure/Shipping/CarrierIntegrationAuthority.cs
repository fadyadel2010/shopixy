using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Shipping;

public enum CarrierIntegrationPurpose
{
    Quote,
    ShipmentCreation,
    ExternalRegistration,
    Cancellation,
    Tracking,
    WebhookTrust,
    CheckoutAdmission,
    StorefrontAdmission,
    CommerceHubAdmission,
    AdminValidation,
    AdminWrite
}

public enum CarrierWebhookValidationMode
{
    HmacSha256,
    SharedSecret,
    IpAllowlist,
    Unsupported
}

public sealed record CarrierIntegrationConfigurationSnapshot(
    Guid Id,
    string CarrierCode,
    string DisplayName,
    bool IsEnabled,
    bool IsDefault,
    bool HasApiKey,
    bool UseSandbox,
    bool SupportsCod,
    bool SupportsTracking,
    bool SupportsWebhooks,
    bool SupportsLabels,
    bool SupportsQuotes,
    bool SupportsCancellation,
    string? Configuration,
    string? PickupAddress,
    string? PickupCity,
    string? PickupCountry,
    string? PickupPhone,
    string? PickupContactName,
    string DefaultServiceLevel,
    bool UseCarrierRates,
    decimal? FixedShippingCost,
    decimal MarkupPercentage,
    decimal? FreeShippingThreshold,
    int SortOrder,
    DateTime? LastValidatedAtUtc,
    bool? LastValidationSucceeded,
    string? LastValidationError)
{
    public bool HasWebhookSecret { get; init; }
    public bool ValidationMatchesCurrentConfiguration { get; init; }
}

public sealed record CarrierCredentialsResolution(
    bool Succeeded,
    string CarrierCode,
    CarrierCredentials? Credentials,
    CarrierIntegrationConfigurationSnapshot? Configuration,
    IReadOnlyList<string> Blockers);

public sealed record CarrierIntegrationReadiness(
    string CarrierCode,
    string DisplayName,
    bool IsManual,
    bool Enabled,
    bool CanExecute,
    string? Environment,
    CarrierProductionCapability ProductionCapability,
    IReadOnlyList<string> Blockers,
    bool OriginReadinessRequired,
    bool OriginReadinessSatisfied,
    string CredentialAvailability);

public sealed record CarrierServiceLevelReadiness(
    string CarrierCode,
    string ServiceLevel,
    bool Exists,
    bool Supported,
    bool Enabled,
    bool CanExecute,
    IReadOnlyList<string> Blockers);

public sealed record CarrierIntegrationUpdate(
    string? DisplayName,
    bool? IsEnabled,
    bool? IsDefault,
    string? ApiKey,
    string? ApiSecret,
    string? AccountId,
    string? ApiUrl,
    bool? UseSandbox,
    bool? SupportsCod,
    bool? SupportsTracking,
    bool? SupportsWebhooks,
    bool? SupportsLabels,
    string? Configuration,
    string? PickupAddress,
    string? PickupCity,
    string? PickupCountry,
    string? PickupPhone,
    string? PickupContactName,
    string? DefaultServiceLevel,
    bool? UseCarrierRates,
    decimal? FixedShippingCost,
    decimal? MarkupPercentage,
    decimal? FreeShippingThreshold,
    int? SortOrder,
    string? Notes,
    string? WebhookSecret = null);

public sealed record CarrierIntegrationStoreResult(
    bool Succeeded,
    CarrierIntegrationConfigurationSnapshot? Configuration,
    CarrierIntegrationReadiness Readiness,
    IReadOnlyList<string> Blockers);

public sealed record CarrierWebhookTrustResolution(
    bool Succeeded,
    string CarrierCode,
    bool SupportsWebhooks,
    CarrierWebhookValidationMode ValidationMode,
    string ValidationMethod,
    string? WebhookSecret,
    IReadOnlyCollection<string> AllowedIps,
    IReadOnlyList<string> Blockers);

public interface ICarrierIntegrationAuthority
{
    // Configuration/connectivity only; never operational authorization.
    Task<CarrierCredentialsResolution> ResolveConnectionCredentialsAsync(Guid tenantId, string carrierCode,
        CancellationToken cancellationToken = default) => ResolveCredentialsAsync(tenantId, carrierCode, CarrierIntegrationPurpose.AdminValidation, cancellationToken);
    Task<IReadOnlyList<CarrierIntegrationConfigurationSnapshot>> ListRuntimeConfigurationsAsync(
        Guid tenantId,
        CarrierIntegrationPurpose purpose,
        CancellationToken cancellationToken = default);

    Task<CarrierCredentialsResolution> ResolveCredentialsAsync(
        Guid tenantId,
        string carrierCode,
        CarrierIntegrationPurpose purpose,
        CancellationToken cancellationToken = default);

    Task<CarrierIntegrationReadiness> GetIntegrationReadinessAsync(
        Guid tenantId,
        string carrierCode,
        Guid? shippingMethodId = null,
        Guid? warehouseId = null,
        CarrierIntegrationPurpose purpose = CarrierIntegrationPurpose.StorefrontAdmission,
        CancellationToken cancellationToken = default);

    Task<CarrierServiceLevelReadiness> GetServiceLevelReadinessAsync(
        Guid tenantId,
        string carrierCode,
        string? serviceLevel,
        CancellationToken cancellationToken = default);

    Task<CarrierIntegrationStoreResult> ValidateAndStoreIntegrationAsync(
        Guid tenantId,
        string carrierCode,
        CarrierIntegrationUpdate update,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteIntegrationAsync(
        Guid tenantId,
        string carrierCode,
        CancellationToken cancellationToken = default);

    Task<CarrierValidationResult> ValidateResolvedCredentialsAsync(
        Guid tenantId,
        string carrierCode,
        CancellationToken cancellationToken = default);

    Task<CarrierWebhookTrustResolution> ResolveWebhookTrustAsync(
        Guid tenantId,
        string carrierCode,
        CancellationToken cancellationToken = default);

    Task<CarrierWebhookTrustResolution> ResolveWebhookTrustForMutationAsync(
        Guid tenantId, string carrierCode, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Locked webhook trust authority is not supported by this implementation.");
}

public sealed class CarrierIntegrationAuthority : ICarrierIntegrationAuthority
{
    private readonly ApplicationDbContext _db;
    private readonly IShippingCarrierRegistry _carrierRegistry;
    private readonly ICarrierCapabilityRegistry _capabilityRegistry;
    private readonly ICarrierCredentialEncryptionService _credentialEncryption;
    private readonly ILogger<CarrierIntegrationAuthority> _logger;
    private readonly BostaPickupVerificationCache? _pickupVerification;

    public CarrierIntegrationAuthority(
        ApplicationDbContext db,
        IShippingCarrierRegistry carrierRegistry,
        ICarrierCapabilityRegistry capabilityRegistry,
        ICarrierCredentialEncryptionService credentialEncryption,
        ILogger<CarrierIntegrationAuthority> logger,
        BostaPickupVerificationCache? pickupVerification = null)
    {
        _db = db;
        _carrierRegistry = carrierRegistry;
        _capabilityRegistry = capabilityRegistry;
        _credentialEncryption = credentialEncryption;
        _logger = logger;
        _pickupVerification = pickupVerification;
    }

    public async Task<IReadOnlyList<CarrierIntegrationConfigurationSnapshot>> ListRuntimeConfigurationsAsync(
        Guid tenantId,
        CarrierIntegrationPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var configurations = await _db.Set<CarrierConfiguration>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId &&
                           item.IsEnabled &&
                           !item.IsDeleted)
            .OrderBy(item => item.SortOrder)
            .ToListAsync(cancellationToken);

        var result = new List<CarrierIntegrationConfigurationSnapshot>(configurations.Count);
        foreach (var config in configurations)
        {
            var readiness = await BuildReadinessAsync(
                tenantId,
                CarrierCodes.Normalize(config.CarrierCode),
                config,
                shippingMethodId: null,
                warehouseId: null,
                purpose,
                cancellationToken);
            if (readiness.CanExecute)
            {
                result.Add(MapConfiguration(config));
            }
        }

        return result;
    }

    public async Task<CarrierCredentialsResolution> ResolveCredentialsAsync(
        Guid tenantId,
        string carrierCode,
        CarrierIntegrationPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        if (CarrierCodes.IsManual(normalizedCarrierCode))
        {
            return new CarrierCredentialsResolution(
                true,
                CarrierCodes.Manual,
                new CarrierCredentials { ApiKey = string.Empty },
                null,
                Array.Empty<string>());
        }

        var config = await LoadConfigurationAsync(tenantId, normalizedCarrierCode, cancellationToken);
        if (config == null)
        {
            return new CarrierCredentialsResolution(
                false,
                normalizedCarrierCode,
                null,
                null,
                new[] { "CARRIER_CONFIGURATION_MISSING" });
        }

        var readiness = await BuildReadinessAsync(
            tenantId,
            normalizedCarrierCode,
            config,
            shippingMethodId: null,
            warehouseId: null,
            purpose,
            cancellationToken);

        if (!readiness.CanExecute && (normalizedCarrierCode == CarrierCodes.Mylerz || PurposeRequiresExecutableReadiness(purpose)))
        {
            return new CarrierCredentialsResolution(
                false,
                normalizedCarrierCode,
                null,
                MapConfiguration(config),
                readiness.Blockers);
        }

        return new CarrierCredentialsResolution(
            true,
            normalizedCarrierCode,
            BuildCredentials(config),
            MapConfiguration(config),
            readiness.Blockers);
    }

    public async Task<CarrierIntegrationReadiness> GetIntegrationReadinessAsync(
        Guid tenantId,
        string carrierCode,
        Guid? shippingMethodId = null,
        Guid? warehouseId = null,
        CarrierIntegrationPurpose purpose = CarrierIntegrationPurpose.StorefrontAdmission,
        CancellationToken cancellationToken = default)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        if (CarrierCodes.IsManual(normalizedCarrierCode))
        {
            return new CarrierIntegrationReadiness(
                CarrierCodes.Manual,
                _carrierRegistry.GetAdapter(CarrierCodes.Manual)?.DisplayName ?? "Manual Fulfillment",
                IsManual: true,
                Enabled: true,
                CanExecute: true,
                Environment: null,
                CarrierProductionCapability.ProductionReady,
                Array.Empty<string>(),
                OriginReadinessRequired: false,
                OriginReadinessSatisfied: true,
                CredentialAvailability: "NotRequired");
        }

        var config = await LoadConfigurationAsync(tenantId, normalizedCarrierCode, cancellationToken);
        return await BuildReadinessAsync(
            tenantId,
            normalizedCarrierCode,
            config,
            shippingMethodId,
            warehouseId,
            purpose,
            cancellationToken);
    }

    public async Task<CarrierServiceLevelReadiness> GetServiceLevelReadinessAsync(
        Guid tenantId,
        string carrierCode,
        string? serviceLevel,
        CancellationToken cancellationToken = default)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        var normalizedServiceLevel = serviceLevel?.Trim().ToLowerInvariant() ?? string.Empty;
        var adapter = _carrierRegistry.GetAdapter(normalizedCarrierCode);
        var supportedLevels = adapter?.GetSupportedServiceLevels() ?? Array.Empty<ServiceLevelInfo>();
        var exists = normalizedServiceLevel.Length > 0;
        var supported = exists && supportedLevels.Any(level =>
            string.Equals(level.Code, normalizedServiceLevel, StringComparison.OrdinalIgnoreCase));

        var carrierAccountIds = await _db.CarrierAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(account => account.TenantId == tenantId &&
                              account.CarrierCode == normalizedCarrierCode &&
                              !account.IsDeleted)
            .Select(account => account.Id)
            .ToListAsync(cancellationToken);

        var enablementRows = carrierAccountIds.Count == 0
            ? new List<CarrierServiceLevelEnablement>()
            : await _db.CarrierServiceLevelEnablements
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.TenantId == tenantId &&
                               carrierAccountIds.Contains(item.CarrierAccountId) &&
                               !item.IsDeleted)
                .ToListAsync(cancellationToken);

        // Absence of an explicit policy preserves the adapter's supported defaults.
        // Once policy rows exist for the carrier, they form an allow-list.
        var enabled = supported &&
                      (enablementRows.Count == 0 || enablementRows.Any(item =>
                          item.IsEnabled &&
                          string.Equals(item.ServiceLevelCode, normalizedServiceLevel, StringComparison.OrdinalIgnoreCase)));
        var blockers = new List<string>();
        if (!exists)
            blockers.Add("SERVICE_LEVEL_MISSING");
        else if (!supported)
            blockers.Add("SERVICE_LEVEL_UNSUPPORTED");
        else if (!enabled)
            blockers.Add("SERVICE_LEVEL_DISABLED");

        return new CarrierServiceLevelReadiness(
            normalizedCarrierCode,
            normalizedServiceLevel,
            exists,
            supported,
            enabled,
            blockers.Count == 0,
            blockers);
    }

    public async Task<CarrierIntegrationStoreResult> ValidateAndStoreIntegrationAsync(
        Guid tenantId,
        string carrierCode,
        CarrierIntegrationUpdate update,
        CancellationToken cancellationToken = default)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        var adapter = _carrierRegistry.GetAdapter(normalizedCarrierCode);
        if (adapter == null)
        {
            var unsupported = await GetIntegrationReadinessAsync(
                tenantId,
                normalizedCarrierCode,
                purpose: CarrierIntegrationPurpose.AdminWrite,
                cancellationToken: cancellationToken);
            return new CarrierIntegrationStoreResult(false, null, unsupported, new[] { "UNKNOWN_CARRIER" });
        }

        var config = await _db.Set<CarrierConfiguration>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.CarrierCode == normalizedCarrierCode &&
                        !item.IsDeleted,
                cancellationToken);

        if (config == null)
        {
            config = new CarrierConfiguration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CarrierCode = normalizedCarrierCode,
                DisplayName = adapter.DisplayName,
                CreatedAt = DateTime.UtcNow
            };
            if (normalizedCarrierCode == CarrierCodes.Bosta)
            {
                // These are adapter capabilities, not caller-authored claims. Bosta
                // has no remote quote or label-retrieval implementation.
                config.SupportsCod = true;
                config.SupportsTracking = true;
                config.SupportsWebhooks = true;
                config.SupportsLabels = false;
                config.SupportsQuotes = false;
                config.SupportsCancellation = true;
            }
            _db.Set<CarrierConfiguration>().Add(config);
        }

        if (update.DisplayName != null)
            config.DisplayName = update.DisplayName;
        if (update.IsEnabled.HasValue)
            config.IsEnabled = update.IsEnabled.Value;
        if (update.ApiKey != null)
            config.ApiKey = _credentialEncryption.Encrypt(update.ApiKey);
        if (update.ApiSecret != null)
            config.ApiSecret = _credentialEncryption.Encrypt(update.ApiSecret);
        if (update.WebhookSecret != null)
            config.WebhookSecret = string.IsNullOrWhiteSpace(update.WebhookSecret)
                ? null
                : _credentialEncryption.Encrypt(update.WebhookSecret);
        if (update.AccountId != null)
            config.AccountId = update.AccountId;
        if (update.ApiUrl != null)
            config.ApiUrl = update.ApiUrl;
        if (update.UseSandbox.HasValue)
            config.UseSandbox = update.UseSandbox.Value;
        if (update.SupportsCod.HasValue)
            config.SupportsCod = update.SupportsCod.Value;
        if (update.SupportsTracking.HasValue)
            config.SupportsTracking = update.SupportsTracking.Value;
        if (update.SupportsWebhooks.HasValue)
            config.SupportsWebhooks = update.SupportsWebhooks.Value;
        if (update.SupportsLabels.HasValue)
            config.SupportsLabels = update.SupportsLabels.Value;
        if (update.Configuration != null)
            config.Configuration = _credentialEncryption.Encrypt(update.Configuration);
        if (update.PickupAddress != null)
            config.PickupAddress = update.PickupAddress;
        if (update.PickupCity != null)
            config.PickupCity = update.PickupCity;
        if (update.PickupCountry != null)
            config.PickupCountry = update.PickupCountry;
        if (update.PickupPhone != null)
            config.PickupPhone = update.PickupPhone;
        if (update.PickupContactName != null)
            config.PickupContactName = update.PickupContactName;
        if (update.DefaultServiceLevel != null)
            config.DefaultServiceLevel = update.DefaultServiceLevel;
        if (update.UseCarrierRates.HasValue)
            config.UseCarrierRates = update.UseCarrierRates.Value;
        if (update.FixedShippingCost.HasValue)
            config.FixedShippingCost = update.FixedShippingCost;
        if (update.MarkupPercentage.HasValue)
            config.MarkupPercentage = update.MarkupPercentage.Value;
        if (update.FreeShippingThreshold.HasValue)
            config.FreeShippingThreshold = update.FreeShippingThreshold;
        if (update.SortOrder.HasValue)
            config.SortOrder = update.SortOrder.Value;
        if (update.Notes != null)
            config.Notes = update.Notes;

        if (update.IsDefault.HasValue && update.IsDefault.Value)
        {
            var otherDefaults = await _db.Set<CarrierConfiguration>()
                .Where(item => item.TenantId == tenantId &&
                               item.CarrierCode != normalizedCarrierCode &&
                               item.IsDefault)
                .ToListAsync(cancellationToken);
            foreach (var other in otherDefaults)
            {
                other.IsDefault = false;
            }

            config.IsDefault = true;
        }
        else if (update.IsDefault.HasValue)
        {
            config.IsDefault = false;
        }

        var currentFingerprint = ComputeCredentialConfigurationFingerprint(config);
        if (!FixedTimeEquals(config.CredentialConfigurationFingerprint, currentFingerprint))
        {
            config.LastValidatedAtUtc = null;
            config.LastValidationSucceeded = null;
            config.LastValidationError = null;
            config.LastValidatedConfigurationFingerprint = null;
        }

        config.CredentialConfigurationFingerprint = currentFingerprint;
        config.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var readiness = await BuildReadinessAsync(
            tenantId,
            normalizedCarrierCode,
            config,
            shippingMethodId: null,
            warehouseId: null,
            CarrierIntegrationPurpose.AdminWrite,
            cancellationToken);

        return new CarrierIntegrationStoreResult(
            true,
            MapConfiguration(config),
            readiness,
            readiness.Blockers);
    }

    public async Task<CarrierCredentialsResolution> ResolveConnectionCredentialsAsync(Guid tenantId, string carrierCode,
        CancellationToken cancellationToken = default)
    {
        var code = CarrierCodes.Normalize(carrierCode);
        var config = await LoadConfigurationAsync(tenantId, code, cancellationToken);
        return config == null
            ? new(false, code, null, null, new[] { "CARRIER_CONFIGURATION_MISSING" })
            : new(true, code, BuildCredentials(config), MapConfiguration(config), Array.Empty<string>());
    }

    public async Task<CarrierValidationResult> ValidateResolvedCredentialsAsync(
        Guid tenantId,
        string carrierCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        var adapter = _carrierRegistry.GetAdapter(normalizedCarrierCode);
        if (adapter == null)
        {
            return new CarrierValidationResult
            {
                Success = false,
                ErrorCode = "CARRIER_NOT_FOUND",
                ErrorMessage = "Carrier adapter not found"
            };
        }

        var configurationBeforeValidation = await LoadConfigurationAsync(
            tenantId,
            normalizedCarrierCode,
            cancellationToken);
        if (configurationBeforeValidation == null)
        {
            return new CarrierValidationResult
            {
                Success = false,
                ErrorCode = "CARRIER_CONFIGURATION_MISSING",
                ErrorMessage = "Carrier configuration is missing"
            };
        }

        var validationFingerprint = ComputeCredentialConfigurationFingerprint(configurationBeforeValidation);
        var resolution = await ResolveConnectionCredentialsAsync(tenantId, normalizedCarrierCode, cancellationToken);

        if (!resolution.Succeeded || resolution.Credentials == null)
        {
            return new CarrierValidationResult
            {
                Success = false,
                ErrorCode = resolution.Blockers.FirstOrDefault() ?? "CREDENTIALS_UNAVAILABLE",
                ErrorMessage = "Carrier credentials are unavailable"
            };
        }

        var validationStartedAtUtc = DateTime.UtcNow;
        CarrierValidationResult result;
        try { result = await adapter.ValidateCredentialsAsync(resolution.Credentials, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { result = Adapters.CarrierConnectionDiagnostics.Failure("CONNECTION_TIMEOUT"); }
        catch (TimeoutException) { result = Adapters.CarrierConnectionDiagnostics.Failure("CONNECTION_TIMEOUT"); }
        catch (HttpRequestException) { result = Adapters.CarrierConnectionDiagnostics.Failure("NETWORK_ERROR"); }
        catch (Exception) { result = Adapters.CarrierConnectionDiagnostics.Failure("CONNECTION_TEST_EXCEPTION"); }
        var config = await LoadConfigurationAsync(tenantId, normalizedCarrierCode, cancellationToken);
        if (config != null)
        {
            // EF identity resolution otherwise returns the pre-request tracked row.
            // Re-read before admitting evidence from an in-flight provider request.
            await _db.Entry(config).ReloadAsync(cancellationToken);
            if (config.LastValidatedAtUtc > validationStartedAtUtc)
                return Adapters.CarrierConnectionDiagnostics.Failure("CONNECTION_TEST_SUPERSEDED");
            var currentFingerprint = ComputeCredentialConfigurationFingerprint(config);
            if (!FixedTimeEquals(validationFingerprint, currentFingerprint))
            {
                config.CredentialConfigurationFingerprint = currentFingerprint;
                config.LastValidatedAtUtc = null;
                config.LastValidationSucceeded = null;
                config.LastValidationError = null;
                config.LastValidatedConfigurationFingerprint = null;
                config.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);

                return new CarrierValidationResult
                {
                    Success = false,
                    ErrorCode = "CARRIER_CONFIGURATION_CHANGED_DURING_VALIDATION",
                    ErrorMessage = "Carrier configuration changed during validation; validate the current configuration again."
                };
            }

            config.CredentialConfigurationFingerprint = currentFingerprint;
            result.TestedAtUtc = validationStartedAtUtc;
            config.LastValidatedAtUtc = validationStartedAtUtc;
            config.LastValidationSucceeded = result.Success;
            config.LastValidationError = result.ErrorMessage;
            config.LastValidatedConfigurationFingerprint = currentFingerprint;
            config.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<bool> DeleteIntegrationAsync(
        Guid tenantId,
        string carrierCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        var config = await _db.Set<CarrierConfiguration>()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.CarrierCode == normalizedCarrierCode,
                cancellationToken);
        if (config == null)
        {
            return false;
        }

        _db.Set<CarrierConfiguration>().Remove(config);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CarrierWebhookTrustResolution> ResolveWebhookTrustForMutationAsync(
        Guid tenantId, string carrierCode, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql() || _db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Locked webhook trust requires the owned PostgreSQL business transaction.");
        var normalized = CarrierCodes.Normalize(carrierCode);
        // Called only AFTER order -> shipment -> inbox locks. Credential reads stay in this authority.
        var config = await _db.Set<CarrierConfiguration>().FromSqlInterpolated($"SELECT * FROM \"CarrierConfigurations\" WHERE \"TenantId\"={tenantId} AND \"CarrierCode\"={normalized} AND NOT \"IsDeleted\" FOR UPDATE")
            .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        // Evaluate exactly the locked snapshot. Do not re-read a phantom replacement configuration.
        return ResolveWebhookTrust(normalized, config);
    }

    public async Task<CarrierWebhookTrustResolution> ResolveWebhookTrustAsync(
        Guid tenantId,
        string carrierCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        var config = await LoadConfigurationAsync(tenantId, normalizedCarrierCode, cancellationToken);
        return ResolveWebhookTrust(normalizedCarrierCode, config);
    }

    private CarrierWebhookTrustResolution ResolveWebhookTrust(string normalizedCarrierCode, CarrierConfiguration? config)
    {
        var capability = _capabilityRegistry.GetCapability(normalizedCarrierCode);
        if (config == null ||
            !config.IsEnabled ||
            !config.SupportsWebhooks ||
            capability?.SupportsWebhooks != true)
        {
            return new CarrierWebhookTrustResolution(
                false,
                normalizedCarrierCode,
                SupportsWebhooks: false,
                CarrierWebhookValidationMode.Unsupported,
                "configuration",
                WebhookSecret: null,
                Array.Empty<string>(),
                new[] { "WEBHOOK_CONFIGURATION_INVALID" });
        }

        var policy = ResolveWebhookTrustPolicy(normalizedCarrierCode, config);
        var webhookSecret = _credentialEncryption.Decrypt(config.WebhookSecret);
        var secretRequired = policy.Mode is CarrierWebhookValidationMode.HmacSha256 or CarrierWebhookValidationMode.SharedSecret;
        if (secretRequired && string.IsNullOrWhiteSpace(webhookSecret))
        {
            return new CarrierWebhookTrustResolution(
                false,
                normalizedCarrierCode,
                SupportsWebhooks: true,
                policy.Mode,
                policy.ValidationMethod,
                WebhookSecret: null,
                policy.AllowedIps,
                new[] { "WEBHOOK_SECRET_MISSING" });
        }

        return new CarrierWebhookTrustResolution(
            policy.Mode != CarrierWebhookValidationMode.Unsupported,
            normalizedCarrierCode,
            SupportsWebhooks: true,
            policy.Mode,
            policy.ValidationMethod,
            webhookSecret,
            policy.AllowedIps,
            policy.Mode == CarrierWebhookValidationMode.Unsupported
                ? new[] { "WEBHOOK_VALIDATION_UNSUPPORTED" }
                : Array.Empty<string>());
    }

    private async Task<CarrierConfiguration?> LoadConfigurationAsync(
        Guid tenantId,
        string carrierCode,
        CancellationToken cancellationToken) =>
        await _db.Set<CarrierConfiguration>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.CarrierCode == carrierCode &&
                        !item.IsDeleted,
                cancellationToken);

    private async Task<CarrierIntegrationReadiness> BuildReadinessAsync(
        Guid tenantId,
        string carrierCode,
        CarrierConfiguration? config,
        Guid? shippingMethodId,
        Guid? warehouseId,
        CarrierIntegrationPurpose purpose,
        CancellationToken cancellationToken)
    {
        var capability = _capabilityRegistry.GetCapability(carrierCode);
        var adapter = _carrierRegistry.GetAdapter(carrierCode);
        var blockers = new List<string>();
        var displayName = capability?.DisplayName ?? adapter?.DisplayName ?? carrierCode;
        var productionCapability = capability?.ProductionCapability ?? CarrierProductionCapability.Unavailable;

        if (carrierCode == CarrierCodes.Mylerz)
            blockers.Add(Adapters.MylerzAdapter.ContractUnavailableErrorCode);

        if (capability == null || adapter == null)
        {
            blockers.Add("UNSUPPORTED_CARRIER");
        }

        if (productionCapability is CarrierProductionCapability.Unavailable)
        {
            blockers.Add("CARRIER_UNAVAILABLE");
        }
        else if (productionCapability is CarrierProductionCapability.NotCertified)
        {
            blockers.Add("CARRIER_NOT_CERTIFIED");
        }

        if (config == null)
        {
            blockers.Add("CARRIER_CONFIGURATION_MISSING");
        }
        else if (PurposeRequiresEnabledConfiguration(purpose) && !config.IsEnabled)
        {
            blockers.Add("CARRIER_CONFIGURATION_DISABLED");
        }

        var credentials = config == null ? null : BuildCredentials(config);
        var missingCredentialFields = config == null || capability == null
            ? Array.Empty<string>()
            : GetMissingRequiredCredentialFields(capability, credentials!, config);
        blockers.AddRange(missingCredentialFields);

        // No provider calls in readiness/claim transactions. Missing explicit ID
        // means provider-default mode, not missing credentials. A bounded
        // server-owned proof from authenticated discovery is still required.
        if (carrierCode == CarrierCodes.Bosta && purpose is CarrierIntegrationPurpose.ShipmentCreation or CarrierIntegrationPurpose.ExternalRegistration)
        {
            if (credentials?.ExtraConfig.TryGetValue("businessLocationId", out var pickupId) == true)
            {
                if (!BostaPickupSelections.ValidId(pickupId)) blockers.Add("BOSTA_BUSINESS_LOCATION_INVALID");
            }
            // Both modes require current authenticated ownership evidence. A
            // syntactically valid saved override is not proof after key rotation.
            {
                var account = await _db.CarrierAccounts.IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(a => a.TenantId == tenantId && a.CarrierCode == CarrierCodes.Bosta && !a.IsDeleted, cancellationToken);
                if (config == null || credentials == null || account == null || account.Environment.ToString() != (credentials.UseSandbox ? "Sandbox" : "Production") ||
                    _pickupVerification?.Read(tenantId, config.Id, account.Id, account.ActiveCredentialReferenceId, credentials) == null)
                    blockers.Add("BOSTA_PICKUP_VERIFICATION_REQUIRED");
            }
        }

        var validationMatchesCurrentConfiguration = config != null &&
                                                    FixedTimeEquals(
                                                        config.CredentialConfigurationFingerprint,
                                                        ComputeCredentialConfigurationFingerprint(config)) &&
                                                    FixedTimeEquals(
                                                        config.LastValidatedConfigurationFingerprint,
                                                        config.CredentialConfigurationFingerprint);

        if (config != null &&
            capability != null &&
            capability.ConnectionTestSupported &&
            PurposeRequiresVerifiedConnection(purpose) &&
            missingCredentialFields.Length == 0 &&
            (config.LastValidationSucceeded != true || !validationMatchesCurrentConfiguration))
        {
            blockers.Add("CARRIER_CONNECTION_NOT_VERIFIED");
        }

        var bostaOriginRequired = carrierCode == CarrierCodes.Bosta && purpose is CarrierIntegrationPurpose.ShipmentCreation or CarrierIntegrationPurpose.ExternalRegistration;
        var originReadinessRequired = bostaOriginRequired || capability?.RequiresOriginReadiness == true && warehouseId.HasValue;
        if (carrierCode == CarrierCodes.Dhl && PurposeRequiresVerifiedConnection(purpose))
        {
            // Reference-data authentication does not certify account/service entitlement.
            blockers.Add("DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED");
        }
        var originReadinessSatisfied = bostaOriginRequired
            ? !blockers.Any(b => b is "BOSTA_BUSINESS_LOCATION_INVALID" or "BOSTA_PICKUP_VERIFICATION_REQUIRED")
            : !originReadinessRequired ||
                                       await IsOriginReadinessSatisfiedAsync(
                                           tenantId,
                                           carrierCode,
                                           warehouseId!.Value,
                                           cancellationToken);
        if (!bostaOriginRequired && originReadinessRequired && !originReadinessSatisfied)
        {
            blockers.Add("ORIGIN_READINESS_MISSING");
        }

        if (carrierCode == CarrierCodes.Bosta && PurposeRequiresPublishedGeographyMapping(purpose))
        {
            var account = await _db.CarrierAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a =>
                a.TenantId == tenantId && a.CarrierCode == CarrierCodes.Bosta && !a.IsDeleted, cancellationToken);
            if (account?.State != CarrierAccountState.Active) blockers.Add("BOSTA_INTEGRATION_ACTIVATION_REQUIRED");
            if (purpose is CarrierIntegrationPurpose.ShipmentCreation or CarrierIntegrationPurpose.ExternalRegistration)
                blockers.Add("BOSTA_PROVIDER_ACCOUNT_ELIGIBILITY_UNVERIFIED");
        }

        if (carrierCode == CarrierCodes.Aramex &&
            capability?.RequiresGeographyMapping == true &&
            PurposeRequiresPublishedGeographyMapping(purpose) &&
            (config == null || !await HasPublishedGeographyMappingAsync(tenantId, carrierCode, config.UseSandbox, cancellationToken)))
        {
            blockers.Add("CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED");
        }

        if (shippingMethodId.HasValue && shippingMethodId.Value == Guid.Empty)
        {
            blockers.Add("SHIPPING_METHOD_CONTEXT_INVALID");
        }

        var canExecute = blockers.Count == 0;
        var credentialAvailability = CarrierCodes.IsManual(carrierCode)
            ? "NotRequired"
            : carrierCode == CarrierCodes.Mylerz
                ? "Unverified"
            : config == null
                ? "Missing"
                : missingCredentialFields.Length == 0
                    ? "Available"
                    : "Missing";

        return new CarrierIntegrationReadiness(
            carrierCode,
            displayName,
            IsManual: false,
            Enabled: config?.IsEnabled == true,
            CanExecute: canExecute,
            Environment: config == null || capability?.EnvironmentSupported != true ? null : config.UseSandbox ? "Sandbox" : "Production",
            productionCapability,
            blockers,
            originReadinessRequired,
            originReadinessSatisfied,
            credentialAvailability);
    }

    private async Task<bool> IsOriginReadinessSatisfiedAsync(
        Guid tenantId,
        string carrierCode,
        Guid warehouseId,
        CancellationToken cancellationToken) =>
        await (
                from assignment in _db.CarrierWarehouseAssignments.IgnoreQueryFilters().AsNoTracking()
                join account in _db.CarrierAccounts.IgnoreQueryFilters().AsNoTracking()
                    on assignment.CarrierAccountId equals account.Id
                where assignment.TenantId == tenantId &&
                      account.TenantId == tenantId &&
                      assignment.WarehouseId == warehouseId &&
                      assignment.IsEnabled &&
                      assignment.OriginReadinessPublished &&
                      !assignment.IsDeleted &&
                      !account.IsDeleted &&
                      account.CarrierCode == carrierCode &&
                      account.State == CarrierAccountState.Active &&
                      account.OperationalState == CarrierOperationalState.Ready
                select assignment.Id)
            .AnyAsync(cancellationToken);

    private async Task<bool> HasPublishedGeographyMappingAsync(
        Guid tenantId,
        string carrierCode,
        bool useSandbox,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var environment = useSandbox
            ? CarrierGeographicEnvironment.Sandbox
            : CarrierGeographicEnvironment.Production;

        string? bostaReferenceScope = null;
        if (carrierCode == CarrierCodes.Bosta)
        {
            var accountEnvironment = useSandbox ? CarrierAccountEnvironment.Sandbox : CarrierAccountEnvironment.Production;
            var account = await _db.CarrierAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a =>
                a.TenantId == tenantId && a.CarrierCode == CarrierCodes.Bosta && a.Environment == accountEnvironment && !a.IsDeleted, cancellationToken);
            if (account?.ActiveCredentialReferenceId == null) return false;
            bostaReferenceScope = $"bosta-v1:{account.Id:N}:{account.ActiveCredentialReferenceId:N}";
        }

        return await (
            from provider in _db.CarrierProviders.AsNoTracking()
            join release in _db.CarrierGeographicMappingReleases.AsNoTracking()
                on provider.Id equals release.CarrierProviderId
            join publication in _db.CarrierGeographicMappingPublications.AsNoTracking()
                on release.Id equals publication.MappingReleaseId
            where provider.IsActive &&
                  provider.CarrierCode == carrierCode &&
                  release.TenantId == tenantId &&
                  release.Environment == environment &&
                  release.AccountScopeKind == CarrierGeographicAccountScopeKind.TenantProviderAccount &&
                  release.AccountScopeId == tenantId &&
                  release.Status == GeographicReleaseStatus.Published &&
                  release.PublishedAtUtc != null &&
                  release.EffectiveFromUtc <= now &&
                  (release.EffectiveToUtc == null || release.EffectiveToUtc > now) &&
                  (release.FreshnessExpiresAtUtc == null || release.FreshnessExpiresAtUtc > now) &&
                  publication.ActivatedAtUtc != null &&
                  publication.ActivatedAtUtc <= now &&
                  publication.SupersededAtUtc == null &&
                  !_db.CarrierGeographicMappingPublications.AsNoTracking().Any(later => later.ActivatedAtUtc <= now && later.ActivatedAtUtc > publication.ActivatedAtUtc &&
                      later.MappingRelease.CarrierProviderId == release.CarrierProviderId && later.MappingRelease.TenantId == release.TenantId &&
                      later.MappingRelease.Environment == release.Environment && later.MappingRelease.AccountScopeKind == release.AccountScopeKind &&
                      later.MappingRelease.AccountScopeId == release.AccountScopeId) &&
                  (bostaReferenceScope == null || _db.CarrierGeographicSyncRuns.AsNoTracking().Any(run =>
                      run.TenantId == tenantId && run.Environment == environment && run.SourceVersion == bostaReferenceScope &&
                      run.CompletedAtUtc > now.AddDays(-1) && run.Status == CarrierGeographicSyncStatus.Completed &&
                      _db.CarrierGeographicMappings.AsNoTracking().Any(mapping => mapping.MappingReleaseId == release.Id && mapping.Source == "bosta-sync:" + run.Id.ToString()))) &&
                  _db.CarrierGeographicMappings.AsNoTracking().Any(mapping =>
                      mapping.MappingReleaseId == release.Id &&
                      mapping.CarrierProviderId == provider.Id &&
                      mapping.RequiredForOperation == "ShipmentRegistration" &&
                      mapping.MappingStatus == CarrierGeographicMappingStatus.Active &&
                      (mapping.VerificationStatus == CarrierGeographicVerificationStatus.Reviewed ||
                       mapping.VerificationStatus == CarrierGeographicVerificationStatus.ManuallyApproved) &&
                      mapping.EffectiveFromUtc <= now &&
                      (mapping.EffectiveToUtc == null || mapping.EffectiveToUtc > now))
            select release.Id).AnyAsync(cancellationToken);
    }

    private CarrierCredentials BuildCredentials(CarrierConfiguration config)
    {
        var credentials = new CarrierCredentials
        {
            ApiKey = _credentialEncryption.Decrypt(config.ApiKey),
            ApiSecret = _credentialEncryption.Decrypt(config.ApiSecret),
            AccountId = config.AccountId,
            ApiUrl = config.ApiUrl,
            UseSandbox = config.UseSandbox
        };

        if (string.IsNullOrWhiteSpace(config.Configuration))
        {
            return credentials;
        }

        try
        {
            credentials.ExtraConfig =
                JsonSerializer.Deserialize<Dictionary<string, string>>(DecryptConfigurationPayload(config))
                ?? new Dictionary<string, string>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Invalid carrier configuration JSON for {CarrierCode} and tenant {TenantId}",
                config.CarrierCode,
                config.TenantId);
        }

        return credentials;
    }

    private static string[] GetMissingRequiredCredentialFields(
        CarrierCapabilityDescriptor capability,
        CarrierCredentials credentials,
        CarrierConfiguration config) =>
        capability.CredentialFields
            .Where(field => field.Required && string.IsNullOrWhiteSpace(ReadCredentialField(credentials, config, field.Target)))
            .Select(field => $"CREDENTIAL_{field.Key.ToUpperInvariant()}_MISSING")
            .ToArray();

    private static string? ReadCredentialField(CarrierCredentials credentials, CarrierConfiguration config, string target)
    {
        if (string.Equals(target, "ApiKey", StringComparison.OrdinalIgnoreCase))
            return credentials.ApiKey;
        if (string.Equals(target, "ApiSecret", StringComparison.OrdinalIgnoreCase))
            return credentials.ApiSecret;
        if (string.Equals(target, "AccountId", StringComparison.OrdinalIgnoreCase))
            return credentials.AccountId;
        if (string.Equals(target, "ApiUrl", StringComparison.OrdinalIgnoreCase))
            return credentials.ApiUrl;
        if (string.Equals(target, "WebhookSecret", StringComparison.OrdinalIgnoreCase))
            return config.WebhookSecret;

        const string extraPrefix = "ExtraConfig.";
        if (target.StartsWith(extraPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var key = target[extraPrefix.Length..];
            return credentials.ExtraConfig.TryGetValue(key, out var value) ? value : null;
        }

        return null;
    }

    private CarrierIntegrationConfigurationSnapshot MapConfiguration(CarrierConfiguration config) =>
        new(
            config.Id,
            config.CarrierCode,
            config.DisplayName,
            config.IsEnabled,
            config.IsDefault,
            !string.IsNullOrWhiteSpace(config.ApiKey),
            config.UseSandbox,
            config.CarrierCode != CarrierCodes.Mylerz && config.SupportsCod,
            config.CarrierCode != CarrierCodes.Mylerz && config.SupportsTracking,
            config.CarrierCode != CarrierCodes.Mylerz && config.SupportsWebhooks,
            config.CarrierCode != CarrierCodes.Mylerz && config.SupportsLabels,
            config.CarrierCode != CarrierCodes.Mylerz && config.SupportsQuotes,
            config.CarrierCode != CarrierCodes.Mylerz && config.SupportsCancellation,
            config.Configuration,
            config.PickupAddress,
            config.PickupCity,
            config.PickupCountry,
            config.PickupPhone,
            config.PickupContactName,
            config.DefaultServiceLevel,
            config.UseCarrierRates,
            config.FixedShippingCost,
            config.MarkupPercentage,
            config.FreeShippingThreshold,
            config.SortOrder,
            config.LastValidatedAtUtc,
            config.LastValidationSucceeded,
            config.LastValidationError)
        {
            HasWebhookSecret = !string.IsNullOrWhiteSpace(config.WebhookSecret),
            ValidationMatchesCurrentConfiguration =
                FixedTimeEquals(
                    config.CredentialConfigurationFingerprint,
                    ComputeCredentialConfigurationFingerprint(config)) &&
                FixedTimeEquals(
                    config.LastValidatedConfigurationFingerprint,
                    config.CredentialConfigurationFingerprint)
        };

    private string ComputeCredentialConfigurationFingerprint(CarrierConfiguration config)
    {
        var credentials = BuildCredentials(config);
        var canonical = new StringBuilder()
            .Append(CarrierCodes.Normalize(config.CarrierCode)).Append('\n')
            .Append(credentials.ApiKey ?? string.Empty).Append('\n')
            .Append(credentials.ApiSecret ?? string.Empty).Append('\n')
            .Append(credentials.AccountId ?? string.Empty).Append('\n')
            .Append(credentials.ApiUrl ?? string.Empty).Append('\n')
            .Append(credentials.UseSandbox ? "1" : "0").Append('\n')
            .Append(config.SupportsWebhooks ? "1" : "0").Append('\n')
            .Append(_credentialEncryption.Decrypt(config.WebhookSecret) ?? string.Empty).Append('\n');

        foreach (var pair in credentials.ExtraConfig.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            canonical.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static bool FixedTimeEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private string DecryptConfigurationPayload(CarrierConfiguration config) =>
        _credentialEncryption.Decrypt(config.Configuration);

    private static bool PurposeRequiresEnabledConfiguration(CarrierIntegrationPurpose purpose) =>
        purpose is CarrierIntegrationPurpose.Quote
            or CarrierIntegrationPurpose.ShipmentCreation
            or CarrierIntegrationPurpose.ExternalRegistration
            or CarrierIntegrationPurpose.WebhookTrust
            or CarrierIntegrationPurpose.CheckoutAdmission
            or CarrierIntegrationPurpose.StorefrontAdmission
            or CarrierIntegrationPurpose.CommerceHubAdmission
            or CarrierIntegrationPurpose.AdminWrite;

    private static bool PurposeRequiresVerifiedConnection(CarrierIntegrationPurpose purpose) =>
        purpose is CarrierIntegrationPurpose.Quote
            or CarrierIntegrationPurpose.ShipmentCreation
            or CarrierIntegrationPurpose.ExternalRegistration
            or CarrierIntegrationPurpose.WebhookTrust
            or CarrierIntegrationPurpose.CheckoutAdmission
            or CarrierIntegrationPurpose.StorefrontAdmission
            or CarrierIntegrationPurpose.CommerceHubAdmission;

    private static bool PurposeRequiresPublishedGeographyMapping(CarrierIntegrationPurpose purpose) =>
        purpose is CarrierIntegrationPurpose.ShipmentCreation
            or CarrierIntegrationPurpose.ExternalRegistration
            or CarrierIntegrationPurpose.CheckoutAdmission
            or CarrierIntegrationPurpose.StorefrontAdmission
            or CarrierIntegrationPurpose.CommerceHubAdmission;

    private static bool PurposeRequiresExecutableReadiness(CarrierIntegrationPurpose purpose) =>
        purpose is not CarrierIntegrationPurpose.AdminValidation
            and not CarrierIntegrationPurpose.Cancellation
            and not CarrierIntegrationPurpose.Tracking;

    private CarrierWebhookTrustPolicy ResolveWebhookTrustPolicy(
        string carrierCode,
        CarrierConfiguration carrierConfig)
    {
        var configuredMethod = ReadCarrierConfigurationValue(carrierConfig, "webhookSignatureMethod")
            ?.Trim()
            .ToLowerInvariant();

        var allowlistedIps = ReadCarrierConfigurationStringList(
            carrierConfig,
            "webhookIpAllowlist",
            "webhookAllowedIps");

        return configuredMethod switch
        {
            "hmac" or "hmacsha256" or "hmac-sha256" or "sha256" =>
                new CarrierWebhookTrustPolicy(
                    CarrierWebhookValidationMode.HmacSha256,
                    "hmac_sha256",
                    allowlistedIps),
            "sharedsecret" or "shared-secret" or "token" or "bearer" =>
                new CarrierWebhookTrustPolicy(
                    CarrierWebhookValidationMode.SharedSecret,
                    "shared_secret",
                    allowlistedIps),
            "ip" or "ipallowlist" or "ip-allowlist" =>
                new CarrierWebhookTrustPolicy(
                    CarrierWebhookValidationMode.IpAllowlist,
                    "ip_allowlist",
                    allowlistedIps),
            _ when string.Equals(carrierCode, CarrierCodes.Dhl, StringComparison.Ordinal) =>
                new CarrierWebhookTrustPolicy(
                    CarrierWebhookValidationMode.HmacSha256,
                    "hmac_sha256",
                    allowlistedIps),
            _ when string.Equals(carrierCode, CarrierCodes.Bosta, StringComparison.Ordinal) ||
                   string.Equals(carrierCode, CarrierCodes.Aramex, StringComparison.Ordinal) =>
                new CarrierWebhookTrustPolicy(
                    CarrierWebhookValidationMode.SharedSecret,
                    "shared_secret",
                    allowlistedIps),
            _ when allowlistedIps.Count > 0 =>
                new CarrierWebhookTrustPolicy(
                    CarrierWebhookValidationMode.IpAllowlist,
                    "ip_allowlist",
                    allowlistedIps),
            _ =>
                new CarrierWebhookTrustPolicy(
                    CarrierWebhookValidationMode.Unsupported,
                    "unsupported",
                    allowlistedIps)
        };
    }

    private string? ReadCarrierConfigurationValue(
        CarrierConfiguration config,
        params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(config.Configuration))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(DecryptConfigurationPayload(config));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var propertyName in propertyNames)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => bool.TrueString,
                        JsonValueKind.False => bool.FalseString,
                        _ => null
                    };
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private IReadOnlyCollection<string> ReadCarrierConfigurationStringList(
        CarrierConfiguration config,
        params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(config.Configuration))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(DecryptConfigurationPayload(config));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            foreach (var propertyName in propertyNames)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        return property.Value
                            .EnumerateArray()
                            .Where(item => item.ValueKind == JsonValueKind.String)
                            .Select(item => item.GetString()?.Trim())
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .Cast<string>()
                            .ToArray();
                    }

                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value
                            .GetString()?
                            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                            .ToArray()
                            ?? Array.Empty<string>();
                    }
                }
            }
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }

        return Array.Empty<string>();
    }

    private sealed record CarrierWebhookTrustPolicy(
        CarrierWebhookValidationMode Mode,
        string ValidationMethod,
        IReadOnlyCollection<string> AllowedIps);
}

public sealed class NullCarrierIntegrationAuthority : ICarrierIntegrationAuthority
{
    public static NullCarrierIntegrationAuthority Instance { get; } = new();

    private NullCarrierIntegrationAuthority()
    {
    }

    public Task<IReadOnlyList<CarrierIntegrationConfigurationSnapshot>> ListRuntimeConfigurationsAsync(
        Guid tenantId,
        CarrierIntegrationPurpose purpose,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CarrierIntegrationConfigurationSnapshot>>(Array.Empty<CarrierIntegrationConfigurationSnapshot>());

    public Task<CarrierCredentialsResolution> ResolveCredentialsAsync(
        Guid tenantId,
        string carrierCode,
        CarrierIntegrationPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var normalized = CarrierCodes.Normalize(carrierCode);
        var isManual = CarrierCodes.IsManual(normalized);
        return Task.FromResult(new CarrierCredentialsResolution(
            isManual,
            normalized,
            isManual ? new CarrierCredentials { ApiKey = string.Empty } : null,
            null,
            isManual ? Array.Empty<string>() : new[] { "CARRIER_INTEGRATION_AUTHORITY_UNAVAILABLE" }));
    }

    public Task<CarrierIntegrationReadiness> GetIntegrationReadinessAsync(
        Guid tenantId,
        string carrierCode,
        Guid? shippingMethodId = null,
        Guid? warehouseId = null,
        CarrierIntegrationPurpose purpose = CarrierIntegrationPurpose.StorefrontAdmission,
        CancellationToken cancellationToken = default)
    {
        var normalized = CarrierCodes.Normalize(carrierCode);
        var isManual = CarrierCodes.IsManual(normalized);
        return Task.FromResult(new CarrierIntegrationReadiness(
            normalized,
            isManual ? "Manual Fulfillment" : normalized,
            isManual,
            Enabled: isManual,
            CanExecute: isManual,
            Environment: null,
            isManual ? CarrierProductionCapability.ProductionReady : CarrierProductionCapability.Unavailable,
            isManual ? Array.Empty<string>() : new[] { "CARRIER_INTEGRATION_AUTHORITY_UNAVAILABLE" },
            OriginReadinessRequired: false,
            OriginReadinessSatisfied: true,
            CredentialAvailability: isManual ? "NotRequired" : "Missing"));
    }

    public Task<CarrierServiceLevelReadiness> GetServiceLevelReadinessAsync(
        Guid tenantId,
        string carrierCode,
        string? serviceLevel,
        CancellationToken cancellationToken = default)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        var normalizedServiceLevel = serviceLevel?.Trim().ToLowerInvariant() ?? string.Empty;
        var manual = CarrierCodes.IsManual(normalizedCarrierCode) && normalizedServiceLevel.Length > 0;
        return Task.FromResult(new CarrierServiceLevelReadiness(
            normalizedCarrierCode,
            normalizedServiceLevel,
            Exists: normalizedServiceLevel.Length > 0,
            Supported: manual,
            Enabled: manual,
            CanExecute: manual,
            manual ? Array.Empty<string>() : new[] { "CARRIER_INTEGRATION_AUTHORITY_UNAVAILABLE" }));
    }

    public async Task<CarrierIntegrationStoreResult> ValidateAndStoreIntegrationAsync(
        Guid tenantId,
        string carrierCode,
        CarrierIntegrationUpdate update,
        CancellationToken cancellationToken = default)
    {
        var readiness = await GetIntegrationReadinessAsync(
            tenantId,
            carrierCode,
            purpose: CarrierIntegrationPurpose.AdminWrite,
            cancellationToken: cancellationToken);
        return new CarrierIntegrationStoreResult(false, null, readiness, readiness.Blockers);
    }

    public Task<bool> DeleteIntegrationAsync(
        Guid tenantId,
        string carrierCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<CarrierValidationResult> ValidateResolvedCredentialsAsync(
        Guid tenantId,
        string carrierCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CarrierValidationResult
        {
            Success = false,
            ErrorCode = "CARRIER_INTEGRATION_AUTHORITY_UNAVAILABLE",
            ErrorMessage = "Carrier integration authority is unavailable"
        });

    public Task<CarrierWebhookTrustResolution> ResolveWebhookTrustAsync(
        Guid tenantId,
        string carrierCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CarrierWebhookTrustResolution(
            false,
            CarrierCodes.Normalize(carrierCode),
            SupportsWebhooks: false,
            CarrierWebhookValidationMode.Unsupported,
            "configuration",
            WebhookSecret: null,
            Array.Empty<string>(),
            new[] { "CARRIER_INTEGRATION_AUTHORITY_UNAVAILABLE" }));
}
