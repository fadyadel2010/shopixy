using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Payments.Administration;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Admin;

public sealed class PaymentProviderAdministrationService : IPaymentProviderAdministrationService
{
    private const string ProviderSettingsCategory = "CommerceHubPaymentProviders";
    private const string LocalSecretMarker = "local-secret-redacted";
    private const string SecretMask = "**************";
    private const string WebhookPath = "/api/payments/paymob/webhook";
    private const string BrowserCallbackPath = "/api/commercehub/payments/paymob/callback";
    private const string PaymobProviderCode = "PAYMOB";
    private const string LifecycleMetadataKey = "secretLifecycle";
    private const string ActiveStatus = "Active";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<string> SecretKeyFragments =
    [
        "secret",
        "password",
        "token",
        "apikey",
        "api_key",
        "privatekey",
        "private_key",
        "clientsecret",
        "client_secret"
    ];

    private static readonly ISet<string> AllowedSecretDisplayKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "apiKeyConfigured",
        "apiKeyMask",
        "secretKeyConfigured",
        "secretKeyMask",
        "hmacSecretConfigured",
        "hmacSecretMask"
    };

    private static readonly ISet<string> AllowedSecretReplacementKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "apiKey",
        "secretKey",
        "hmacSecret"
    };

    private static readonly IReadOnlyDictionary<string, DashboardSecretDefinition> DashboardSecretDefinitions =
        new Dictionary<string, DashboardSecretDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["apiKey"] = new("apiKey", "API Key"),
            ["secretKey"] = new("secretKey", "Secret Key"),
            ["hmacSecret"] = new("hmacSecret", "HMAC Secret")
        };

    private readonly IReadOnlyDictionary<string, PaymentProviderRegistration> _registrations;
    private readonly ConcurrentDictionary<string, PaymentProviderConfiguration> _configurations;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _protectedSecrets;
    private readonly ApplicationDbContext? _dbContext;
    private readonly IDataProtector? _secretProtector;

    public PaymentProviderAdministrationService()
    {
        var registrations = BuildRegistrations();
        _registrations = registrations.ToDictionary(
            registration => NormalizeProviderCode(registration.ProviderCode),
            registration => registration,
            StringComparer.OrdinalIgnoreCase);

        if (_registrations.Count != registrations.Count)
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status409Conflict,
                "DUPLICATE_PROVIDER",
                "Duplicate payment provider registrations are not allowed.");
        }

        _configurations = new ConcurrentDictionary<string, PaymentProviderConfiguration>(StringComparer.OrdinalIgnoreCase);
        _protectedSecrets = new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
        {
            var configuration = CreateDefaultConfiguration(registration);
            _configurations[configuration.ProviderCode] = configuration;
            _protectedSecrets[configuration.ProviderCode] = new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public PaymentProviderAdministrationService(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider)
        : this()
    {
        _dbContext = dbContext;
        _secretProtector = dataProtectionProvider.CreateProtector("PaymentProviderAdministration.PaymobSecrets.v1");
    }

    public IReadOnlyList<PaymentProviderSummaryDto> GetProviders() =>
        _registrations.Keys
            .Select(providerCode => LoadProviderState(providerCode).Configuration)
            .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ToSummaryDto)
            .ToList();

    public PaymentProviderDashboardDto GetDashboard(
        IReadOnlyList<PaymentProviderAdministrationActivityDto>? recentActivity = null)
    {
        var states = LoadProviderStates();
        var providers = states
            .Select(state => ToDashboardProviderDto(state.Configuration, state.ProtectedSecrets))
            .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var capabilities = providers
            .SelectMany(provider => provider.Capabilities)
            .GroupBy(capability => capability.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(capability => capability.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var health = providers
            .Select(provider => new PaymentProviderDashboardHealthDto
            {
                ProviderCode = provider.ProviderCode,
                Status = provider.Health.Status,
                Environment = provider.Environment,
                LastCheckUtc = provider.LastCheckUtc,
                LastCheckDurationMs = provider.Health.LastCheckDurationMs,
                ConnectionQuality = provider.Health.ConnectionQuality
            })
            .ToList();
        var secrets = providers
            .Select(provider => new PaymentProviderDashboardSecretSummaryDto
            {
                ProviderCode = provider.ProviderCode,
                SecretCount = provider.Secrets.Count,
                ActiveSecretCount = provider.Secrets.Count(secret => string.Equals(secret.Status, ActiveStatus, StringComparison.Ordinal)),
                LastRotatedAtUtc = provider.Secrets
                    .Select(secret => (DateTime?)(secret.LastRotatedAtUtc ?? secret.ActivatedAtUtc ?? secret.CreatedAtUtc))
                    .DefaultIfEmpty(null)
                    .Max(),
                Secrets = provider.Secrets
            })
            .ToList();

        return new PaymentProviderDashboardDto
        {
            Providers = providers,
            Summary = new PaymentProviderDashboardSummaryDto
            {
                ProviderCount = providers.Count,
                EnabledCount = providers.Count(provider => provider.Enabled),
                DisabledCount = providers.Count(provider => !provider.Enabled),
                HealthyCount = providers.Count(provider => string.Equals(provider.Health.Status, "Healthy", StringComparison.OrdinalIgnoreCase)),
                WarningCount = providers.Count(provider => string.Equals(provider.Health.Status, "Warning", StringComparison.OrdinalIgnoreCase)),
                ErrorCount = providers.Count(provider => string.Equals(provider.Health.Status, "Error", StringComparison.OrdinalIgnoreCase)),
                UnknownHealthCount = providers.Count(provider => string.Equals(provider.Health.Status, "Unknown", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(provider.Health.Status)),
                ConfiguredSecretCount = providers.Sum(provider => provider.Secrets.Count),
                CapabilityCount = capabilities.Count,
                GeneratedAtUtc = DateTime.UtcNow
            },
            Capabilities = capabilities,
            Health = health,
            Secrets = secrets,
            Permissions = PaymentProviderPermissions.All,
            RecentActivity = recentActivity ?? Array.Empty<PaymentProviderAdministrationActivityDto>(),
            Warnings = BuildDashboardWarnings(providers)
        };
    }

    public PaymentProviderConfigurationDto GetProviderConfiguration(string providerCode)
    {
        var normalizedProviderCode = RequireKnownProvider(providerCode);
        return ToConfigurationDto(LoadProviderState(normalizedProviderCode).Configuration);
    }

    public PaymentProviderOperationalConfiguration GetProviderOperationalConfiguration(string providerCode)
    {
        var normalizedProviderCode = RequireKnownProvider(providerCode);
        var state = LoadProviderState(normalizedProviderCode);
        var credentials = state.ProtectedSecrets
            .ToDictionary(
                item => item.Key,
                item => UnprotectStoredCredential(item.Value),
                StringComparer.Ordinal);

        return new PaymentProviderOperationalConfiguration(
            state.Configuration.ProviderCode,
            state.Configuration.DisplayName,
            state.Configuration.Enabled,
            state.Configuration.Environment,
            state.Configuration.Configuration,
            credentials,
            state.Configuration.Capabilities.Select(ToCapabilityDto).ToList(),
            BuildHealthDto(state.Configuration));
    }

    public PaymentProviderConfigurationSaveResult SaveProviderConfiguration(
        string providerCode,
        PaymentProviderConfigurationRequest request)
    {
        if (request == null)
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status400BadRequest,
                "MISSING_PROVIDER_CONFIGURATION",
                "Payment provider configuration request is required.");
        }

        var normalizedProviderCode = RequireKnownProvider(providerCode);
        if (!string.IsNullOrWhiteSpace(request.ProviderCode) &&
            !string.Equals(NormalizeProviderCode(request.ProviderCode), normalizedProviderCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status400BadRequest,
                "PROVIDER_CODE_MISMATCH",
                "The route provider code must match the request provider code.");
        }

        ValidateConfigurationEnvelope(request.Configuration);
        ValidateConfigurationEnvelope(request.Metadata);
        var secretReplacements = NormalizeSecretReplacements(request.SecretReplacements);

        var currentState = LoadProviderState(normalizedProviderCode);
        var current = currentState.Configuration;
        var normalizedConfiguration = BuildPaymobConfiguration(current, request);
        ValidatePaymobSecretReplacements(normalizedConfiguration, secretReplacements);
        var protectedSecrets = new Dictionary<string, string>(currentState.ProtectedSecrets, StringComparer.Ordinal);

        foreach (var replacement in secretReplacements)
        {
            protectedSecrets[replacement.Key] = ProtectSecret(replacement.Value);
        }

        ValidatePaymobConfiguration(normalizedConfiguration, protectedSecrets);

        var metadata = request.Metadata == null
            ? current.Metadata
            : MergeMetadata(current.Metadata, request.Metadata);

        var next = current with
        {
            DisplayName = RequireString(normalizedConfiguration, "displayName"),
            Description = RequireString(normalizedConfiguration, "description"),
            Enabled = RequireBool(normalizedConfiguration, "enabled"),
            Environment = RequireString(normalizedConfiguration, "environment"),
            Configuration = ApplySecretStatus(normalizedConfiguration, protectedSecrets),
            Metadata = MergeMetadata(metadata, new Dictionary<string, object?>
            {
                ["administrationPhase"] = "PAYMENT-ADMIN-2",
                ["providerConfigurationImplemented"] = true
            }),
            Status = HasRequiredPaymobCredentials(protectedSecrets) ? "Configured" : "PendingConfiguration"
        };

        PersistProviderState(next, protectedSecrets);

        var changedFields = DetermineChangedFields(
            ToConfigurationDto(current),
            ToConfigurationDto(next),
            secretReplacements.Keys);

        return new PaymentProviderConfigurationSaveResult
        {
            Provider = ToConfigurationDto(next),
            ChangedFields = changedFields,
            RedactedChangedFields = RedactChangedFields(changedFields)
        };
    }

    public PaymentProviderConfigurationDto RecordProviderActionResult(
        string providerCode,
        PaymentProviderActionResultDto result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var normalizedProviderCode = RequireKnownProvider(providerCode);
        var currentState = LoadProviderState(normalizedProviderCode);
        var current = currentState.Configuration;
        var metadata = new Dictionary<string, object?>(current.Metadata, StringComparer.Ordinal);
        var latestResult = SerializeResult(result);

        metadata["connectivityLatestResult"] = latestResult;
        metadata["connectivityPreviousResult"] = current.Metadata.TryGetValue("connectivityLatestResult", out var previous)
            ? NormalizeJsonBackedValue(previous)
            : null;
        metadata["connectivityLastCheckDurationMs"] = result.LatencyMs;
        metadata["connectivityProviderVersion"] = result.ProviderVersion;
        metadata["connectivityQuality"] = DeriveConnectionQuality(result.LatencyMs);
        metadata["connectivityMessages"] = result.Errors.Count > 0
            ? result.Errors.ToArray()
            : result.Warnings.ToArray();

        if (result.Success)
        {
            metadata["connectivityLastSuccessfulCheckUtc"] = result.CheckedAtUtc;
        }
        else
        {
            metadata["connectivityLastFailedCheckUtc"] = result.CheckedAtUtc;
        }

        var next = current with
        {
            Status = result.Status,
            Metadata = metadata
        };

        PersistProviderState(next, currentState.ProtectedSecrets);

        return ToConfigurationDto(next);
    }

    private static IReadOnlyList<PaymentProviderRegistration> BuildRegistrations()
    {
        return
        [
            new PaymentProviderRegistration(
                PaymobProviderCode,
                "Paymob",
                "Card and wallet payment provider registration prepared for CommerceHub administration.",
                BuildDefaultCapabilities(),
                new Dictionary<string, object?>
                {
                    ["administrationPhase"] = "PAYMENT-ADMIN-2",
                    ["providerConfigurationImplemented"] = true
                })
        ];
    }

    private static IReadOnlyList<PaymentProviderCapability> BuildDefaultCapabilities()
    {
        return
        [
            new PaymentProviderCapability(
                "payments",
                "Payments",
                "Accept provider-backed payments.",
                true,
                true),
            new PaymentProviderCapability(
                "refunds",
                "Refunds",
                "Provider-backed refunds are not certified for the modern credential contract.",
                false,
                false),
            new PaymentProviderCapability(
                "capture",
                "Capture",
                "Capture authorized payment amounts.",
                false,
                false),
            new PaymentProviderCapability(
                "void",
                "Void",
                "Void an authorized payment before capture.",
                false,
                false),
            new PaymentProviderCapability(
                "recurring",
                "Recurring",
                "Support recurring payment administration metadata.",
                false,
                true),
            new PaymentProviderCapability(
                "3ds",
                "3DS",
                "Support payer authentication capabilities.",
                true,
                true),
            new PaymentProviderCapability(
                "tokenization",
                "Tokenization",
                "Support reusable payment token metadata.",
                false,
                true),
            new PaymentProviderCapability(
                "webhooks",
                "Webhooks",
                "Receive asynchronous provider payment notifications.",
                true,
                false),
            new PaymentProviderCapability(
                "payouts",
                "Payouts",
                "Support provider payout administration metadata.",
                false,
                true),
            new PaymentProviderCapability(
                "settlement",
                "Settlement",
                "Support provider settlement administration metadata.",
                false,
                true),
            new PaymentProviderCapability(
                "sandbox",
                "Sandbox",
                "Support non-production provider administration.",
                true,
                false),
            new PaymentProviderCapability(
                "production",
                "Production",
                "Support production provider administration.",
                true,
                true)
        ];
    }

    private static PaymentProviderConfiguration CreateDefaultConfiguration(PaymentProviderRegistration registration)
    {
        var providerCode = NormalizeProviderCode(registration.ProviderCode);
        var configuration = ApplySecretStatus(CreateDefaultPaymobConfiguration(registration), new Dictionary<string, string>());

        return new PaymentProviderConfiguration(
            providerCode,
            registration.DisplayName,
            registration.Description,
            Enabled: false,
            Environment: "Sandbox",
            registration.Capabilities,
            configuration,
            "PendingConfiguration",
            registration.Metadata);
    }

    private static Dictionary<string, object?> CreateDefaultPaymobConfiguration(PaymentProviderRegistration registration) =>
        new(StringComparer.Ordinal)
        {
            ["enabled"] = false,
            ["displayName"] = registration.DisplayName,
            ["environment"] = "Sandbox",
            ["sandbox"] = true,
            ["currency"] = "EGP",
            ["description"] = registration.Description,
            ["integrationId"] = string.Empty,
            ["publicKey"] = string.Empty,
            ["merchantIdentifier"] = string.Empty,
            ["providerAccountName"] = string.Empty,
            ["timeoutSeconds"] = 30,
            ["retryCount"] = 2,
            ["requestTimeoutSeconds"] = 30,
            ["maximumRetryAttempts"] = 3,
            ["webhookMethod"] = "POST",
            ["webhookUrl"] = string.Empty,
            ["webhookPath"] = WebhookPath,
            ["browserCallbackMethod"] = "GET",
            ["browserCallbackUrl"] = string.Empty,
            ["browserCallbackPath"] = BrowserCallbackPath
        };

    private PaymentProviderState LoadProviderState(string providerCode)
    {
        var normalizedProviderCode = RequireKnownProvider(providerCode);

        if (_dbContext == null)
        {
            return CreateLocalProviderState(normalizedProviderCode);
        }

        var settingKey = BuildSettingKey(normalizedProviderCode);
        var setting = _dbContext.SystemSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => !item.IsDeleted && item.TenantId == null && item.Key == settingKey)
            .OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
            .ThenByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();

        if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
        {
            return CreateLocalProviderState(normalizedProviderCode);
        }

        return CreateProviderStateFromStoredValue(normalizedProviderCode, setting.Value);
    }

    private IReadOnlyList<PaymentProviderState> LoadProviderStates()
    {
        var providerCodes = _registrations.Keys
            .OrderBy(providerCode => providerCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (_dbContext == null)
        {
            return providerCodes.Select(CreateLocalProviderState).ToList();
        }

        var settingKeys = providerCodes
            .Select(BuildSettingKey)
            .ToArray();
        var settings = _dbContext.SystemSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => !item.IsDeleted && item.TenantId == null && settingKeys.Contains(item.Key))
            .ToList();
        var latestSettingsByKey = settings
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
                    .ThenByDescending(item => item.CreatedAt)
                    .ThenByDescending(item => item.Id)
                    .First(),
                StringComparer.Ordinal);

        return providerCodes
            .Select(providerCode =>
            {
                var settingKey = BuildSettingKey(providerCode);
                return latestSettingsByKey.TryGetValue(settingKey, out var setting) &&
                       !string.IsNullOrWhiteSpace(setting.Value)
                    ? CreateProviderStateFromStoredValue(providerCode, setting.Value)
                    : CreateLocalProviderState(providerCode);
            })
            .ToList();
    }

    private PaymentProviderState CreateLocalProviderState(string normalizedProviderCode)
    {
        var protectedSecrets = _protectedSecrets.TryGetValue(normalizedProviderCode, out var localProtectedSecrets)
            ? localProtectedSecrets
            : new Dictionary<string, string>(StringComparer.Ordinal);

        return new PaymentProviderState(
            _configurations[normalizedProviderCode],
            protectedSecrets);
    }

    private PaymentProviderState CreateProviderStateFromStoredValue(
        string normalizedProviderCode,
        string storedValue)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<StoredPaymentProviderConfiguration>(storedValue, JsonOptions);
            if (stored == null)
            {
                return CreateLocalProviderState(normalizedProviderCode);
            }

            var registration = _registrations[normalizedProviderCode];
            var protectedSecrets = stored.ProtectedSecrets ?? new Dictionary<string, string>(StringComparer.Ordinal);
            var configuration = new Dictionary<string, object?>(CreateDefaultPaymobConfiguration(registration), StringComparer.Ordinal);

            foreach (var item in ToObjectDictionary(stored.Configuration))
            {
                configuration[item.Key] = item.Value;
            }

            var migrationRequired =
                (protectedSecrets.ContainsKey("apiKey") || configuration.ContainsKey("iframeId")) &&
                (!protectedSecrets.ContainsKey("secretKey") ||
                 string.IsNullOrWhiteSpace(ReadString(configuration, "publicKey", string.Empty)));
            configuration["migrationRequired"] = migrationRequired;
            configuration = ApplySecretStatus(configuration, protectedSecrets);

            var providerConfiguration = new PaymentProviderConfiguration(
                normalizedProviderCode,
                string.IsNullOrWhiteSpace(stored.DisplayName) ? registration.DisplayName : stored.DisplayName,
                string.IsNullOrWhiteSpace(stored.Description) ? registration.Description : stored.Description,
                stored.Enabled && !migrationRequired,
                string.IsNullOrWhiteSpace(stored.Environment) ? "Sandbox" : stored.Environment,
                registration.Capabilities,
                configuration,
                migrationRequired ? "MigrationRequired" : stored.Status ?? "PendingConfiguration",
                MergeMetadata(registration.Metadata, ToObjectDictionary(stored.Metadata)));

            return new PaymentProviderState(providerConfiguration, protectedSecrets);
        }
        catch (JsonException)
        {
            return CreateLocalProviderState(normalizedProviderCode);
        }
    }

    private void PersistProviderState(
        PaymentProviderConfiguration configuration,
        IReadOnlyDictionary<string, string> protectedSecrets)
    {
        _configurations[configuration.ProviderCode] = configuration;
        _protectedSecrets[configuration.ProviderCode] = new Dictionary<string, string>(protectedSecrets, StringComparer.Ordinal);

        if (_dbContext == null)
        {
            return;
        }

        var settingKey = BuildSettingKey(configuration.ProviderCode);
        var setting = _dbContext.SystemSettings
            .IgnoreQueryFilters()
            .FirstOrDefault(item => !item.IsDeleted && item.TenantId == null && item.Key == settingKey);

        var stored = new StoredPaymentProviderConfiguration
        {
            ProviderCode = configuration.ProviderCode,
            DisplayName = configuration.DisplayName,
            Description = configuration.Description,
            Enabled = configuration.Enabled,
            Environment = configuration.Environment,
            Status = configuration.Status,
            Configuration = configuration.Configuration
                .Where(item => !AllowedSecretDisplayKeys.Contains(item.Key))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            Metadata = configuration.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            ProtectedSecrets = new Dictionary<string, string>(protectedSecrets, StringComparer.Ordinal)
        };

        var serialized = JsonSerializer.Serialize(stored, JsonOptions);

        if (setting == null)
        {
            setting = new SystemSetting
            {
                Id = Guid.NewGuid(),
                TenantId = null,
                Key = settingKey,
                Value = serialized,
                Description = $"CommerceHub payment provider administration configuration for {configuration.ProviderCode}.",
                Category = ProviderSettingsCategory,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = serialized;
            setting.Description = $"CommerceHub payment provider administration configuration for {configuration.ProviderCode}.";
            setting.Category = ProviderSettingsCategory;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        _dbContext.SaveChanges();
    }

    private string RequireKnownProvider(string providerCode)
    {
        var normalizedProviderCode = NormalizeProviderCode(providerCode);
        if (string.IsNullOrWhiteSpace(normalizedProviderCode))
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status400BadRequest,
                "MISSING_PROVIDER",
                "Payment provider code is required.");
        }

        if (!_registrations.ContainsKey(normalizedProviderCode))
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status404NotFound,
                "UNKNOWN_PROVIDER",
                "The requested payment provider is not registered.");
        }

        return normalizedProviderCode;
    }

    private static void ValidateConfigurationEnvelope(IReadOnlyDictionary<string, object?>? values)
    {
        if (values == null)
        {
            return;
        }

        foreach (var key in values.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new PaymentProviderAdministrationException(
                    StatusCodes.Status400BadRequest,
                    "INVALID_PROVIDER_CONFIGURATION",
                    "Configuration keys must be non-empty.");
            }

            if (AllowedSecretDisplayKeys.Contains(key))
            {
                continue;
            }

            if (string.Equals(key, "secretLifecycle", StringComparison.Ordinal))
            {
                continue;
            }

            var normalizedKey = key.Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();

            if (SecretKeyFragments.Any(fragment => normalizedKey.Contains(fragment.Replace("_", string.Empty, StringComparison.Ordinal))))
            {
                throw new PaymentProviderAdministrationException(
                    StatusCodes.Status400BadRequest,
                    "PROVIDER_SECRET_NOT_ACCEPTED",
                    "Provider secrets must be submitted through replacement fields only.");
            }
        }
    }

    private static IReadOnlyDictionary<string, string> NormalizeSecretReplacements(
        IReadOnlyDictionary<string, string?>? values)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        if (values == null)
        {
            return replacements;
        }

        foreach (var item in values)
        {
            if (!AllowedSecretReplacementKeys.Contains(item.Key))
            {
                throw new PaymentProviderAdministrationException(
                    StatusCodes.Status400BadRequest,
                    "PROVIDER_SECRET_REPLACEMENT_INVALID",
                    "Unknown provider credential replacement field.");
            }

            if (string.IsNullOrWhiteSpace(item.Value))
            {
                continue;
            }

            replacements[item.Key] = item.Value.Trim();
        }

        return replacements;
    }

    private static Dictionary<string, object?> BuildPaymobConfiguration(
        PaymentProviderConfiguration current,
        PaymentProviderConfigurationRequest request)
    {
        var configuration = new Dictionary<string, object?>(current.Configuration, StringComparer.Ordinal);
        if (request.Configuration != null)
        {
            foreach (var item in request.Configuration)
            {
                if (AllowedSecretDisplayKeys.Contains(item.Key))
                {
                    continue;
                }

                var normalizedValue = NormalizeJsonBackedValue(item.Value);
                if ((string.Equals(item.Key, "webhookUrl", StringComparison.Ordinal) ||
                     string.Equals(item.Key, "browserCallbackUrl", StringComparison.Ordinal)) &&
                    normalizedValue is string url)
                {
                    normalizedValue = url.Trim();
                }

                configuration[item.Key] = normalizedValue;
            }
        }

        if (request.Enabled.HasValue)
        {
            configuration["enabled"] = request.Enabled.Value;
        }

        var environment = NormalizeEnvironment(request.Environment) ?? ReadString(configuration, "environment", current.Environment);
        configuration["environment"] = environment;
        configuration["sandbox"] = string.Equals(environment, "Sandbox", StringComparison.OrdinalIgnoreCase);
        configuration["webhookMethod"] = "POST";
        configuration["webhookPath"] = WebhookPath;
        configuration["browserCallbackMethod"] = "GET";
        configuration["browserCallbackPath"] = BrowserCallbackPath;

        return configuration;
    }

    private static void ValidatePaymobConfiguration(
        IReadOnlyDictionary<string, object?> configuration,
        IReadOnlyDictionary<string, string> protectedSecrets)
    {
        var errors = new List<string>();
        var environment = ReadString(configuration, "environment", string.Empty);
        var currency = ReadString(configuration, "currency", string.Empty);
        var integrationId = ReadString(configuration, "integrationId", string.Empty);
        var publicKey = ReadString(configuration, "publicKey", string.Empty);
        var merchantIdentifier = ReadString(configuration, "merchantIdentifier", string.Empty);
        var webhookUrl = ReadString(configuration, "webhookUrl", string.Empty);
        var browserCallbackUrl = ReadString(configuration, "browserCallbackUrl", string.Empty);
        var displayName = ReadString(configuration, "displayName", string.Empty);
        var description = ReadString(configuration, "description", string.Empty);
        var enabled = ReadBool(configuration, "enabled", false);

        if (string.IsNullOrWhiteSpace(displayName))
        {
            errors.Add("Display Name is required.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            errors.Add("Description is required.");
        }

        if (!string.Equals(environment, "Sandbox", StringComparison.Ordinal) &&
            !string.Equals(environment, "Production", StringComparison.Ordinal))
        {
            errors.Add("Unknown Environment.");
        }

        if (!Regex.IsMatch(currency, "^[A-Z]{3}$", RegexOptions.CultureInvariant))
        {
            errors.Add("Invalid Currency.");
        }

        if (enabled && !protectedSecrets.ContainsKey("apiKey"))
        {
            errors.Add("Missing API Key.");
        }

        if (enabled && !protectedSecrets.ContainsKey("secretKey"))
        {
            errors.Add("Missing Secret Key.");
        }

        if (enabled && !protectedSecrets.ContainsKey("hmacSecret"))
        {
            errors.Add("Missing HMAC Secret.");
        }

        if (enabled && !IsPositiveInteger(integrationId))
        {
            errors.Add("Invalid Integration ID.");
        }

        if (enabled && !IsPositiveInteger(merchantIdentifier))
        {
            errors.Add("Invalid Merchant Identifier.");
        }

        if (enabled && !IsPaymobKey(publicKey, "egy_pk_", environment))
        {
            errors.Add("Invalid Public Key for the selected environment.");
        }

        if (enabled && !HasAbsoluteHttpsUrl(webhookUrl))
        {
            errors.Add("Webhook URL must be an absolute HTTPS URL.");
        }

        if (enabled && !HasAbsoluteHttpsUrl(browserCallbackUrl))
        {
            errors.Add("Browser Callback URL must be an absolute HTTPS URL.");
        }

        AddNonNegativeIntegerValidation(configuration, "timeoutSeconds", "Negative Timeout.", errors);
        AddNonNegativeIntegerValidation(configuration, "retryCount", "Negative Retry Count.", errors);
        AddNonNegativeIntegerValidation(configuration, "requestTimeoutSeconds", "Negative Request Timeout.", errors);
        AddNonNegativeIntegerValidation(configuration, "maximumRetryAttempts", "Negative Maximum Retry Attempts.", errors);

        if (errors.Count > 0)
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status400BadRequest,
                "PAYMOB_CONFIGURATION_VALIDATION_FAILED",
                string.Join(" ", errors));
        }
    }

    private static bool IsPaymobKey(string value, string prefix, string environment)
    {
        var marker = string.Equals(environment, "Sandbox", StringComparison.Ordinal) ? "test" : "live";
        return value.StartsWith($"{prefix}{marker}_", StringComparison.Ordinal);
    }

    private static void ValidatePaymobSecretReplacements(
        IReadOnlyDictionary<string, object?> configuration,
        IReadOnlyDictionary<string, string> secretReplacements)
    {
        if (!secretReplacements.TryGetValue("secretKey", out var secretKey))
        {
            return;
        }

        var environment = ReadString(configuration, "environment", string.Empty);
        if (!IsPaymobKey(secretKey, "egy_sk_", environment))
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status400BadRequest,
                "PAYMOB_CONFIGURATION_VALIDATION_FAILED",
                "Invalid Secret Key for the selected environment.");
        }
    }

    private static bool HasRequiredPaymobCredentials(IReadOnlyDictionary<string, string> protectedSecrets) =>
        protectedSecrets.ContainsKey("apiKey") &&
        protectedSecrets.ContainsKey("secretKey") &&
        protectedSecrets.ContainsKey("hmacSecret");

    private static bool HasAbsoluteHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private string ProtectSecret(string secret)
    {
        if (_secretProtector == null)
        {
            return secret;
        }

        return _secretProtector.Protect(secret);
    }

    private string UnprotectStoredCredential(string storedCredential)
    {
        if (string.IsNullOrWhiteSpace(storedCredential))
        {
            return string.Empty;
        }

        if (_secretProtector == null)
        {
            return storedCredential == LocalSecretMarker ? string.Empty : storedCredential;
        }

        try
        {
            return _secretProtector.Unprotect(storedCredential);
        }
        catch
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status400BadRequest,
                "PROVIDER_CREDENTIALS_UNREADABLE",
                "Stored provider credentials could not be read.");
        }
    }

    private static IReadOnlyList<string> DetermineChangedFields(
        PaymentProviderConfigurationDto current,
        PaymentProviderConfigurationDto next,
        IEnumerable<string> secretReplacementKeys)
    {
        var changedFields = new SortedSet<string>(StringComparer.Ordinal);

        if (current.Enabled != next.Enabled)
        {
            changedFields.Add("enabled");
        }

        if (!string.Equals(current.Environment, next.Environment, StringComparison.Ordinal))
        {
            changedFields.Add("environment");
        }

        if (!string.Equals(current.DisplayName, next.DisplayName, StringComparison.Ordinal))
        {
            changedFields.Add("displayName");
        }

        if (!string.Equals(current.Description, next.Description, StringComparison.Ordinal))
        {
            changedFields.Add("description");
        }

        foreach (var key in next.Configuration.Keys.Where(key => !AllowedSecretDisplayKeys.Contains(key)))
        {
            current.Configuration.TryGetValue(key, out var currentValue);
            next.Configuration.TryGetValue(key, out var nextValue);
            if (!string.Equals(ToStableString(currentValue), ToStableString(nextValue), StringComparison.Ordinal))
            {
                changedFields.Add(key);
            }
        }

        foreach (var key in secretReplacementKeys)
        {
            changedFields.Add(key switch
            {
                "apiKey" => "credentials.accountApiKey",
                "secretKey" => "credentials.apiCredential",
                _ => "credentials.signatureCredential"
            });
        }

        return changedFields.ToList();
    }

    private static IReadOnlyList<string> RedactChangedFields(IReadOnlyList<string> changedFields) =>
        changedFields
            .Select(field => field.StartsWith("credentials.", StringComparison.Ordinal) ? $"{field}:redacted" : field)
            .ToList();

    private static Dictionary<string, object?> ApplySecretStatus(
        IReadOnlyDictionary<string, object?> configuration,
        IReadOnlyDictionary<string, string> protectedSecrets)
    {
        var publicConfiguration = new Dictionary<string, object?>(configuration, StringComparer.Ordinal)
        {
            ["apiKeyConfigured"] = protectedSecrets.ContainsKey("apiKey"),
            ["apiKeyMask"] = SecretMask,
            ["secretKeyConfigured"] = protectedSecrets.ContainsKey("secretKey"),
            ["secretKeyMask"] = SecretMask,
            ["hmacSecretConfigured"] = protectedSecrets.ContainsKey("hmacSecret"),
            ["hmacSecretMask"] = SecretMask,
            ["webhookMethod"] = "POST",
            ["browserCallbackMethod"] = "GET",
            ["webhookPath"] = WebhookPath,
            ["browserCallbackPath"] = BrowserCallbackPath
        };

        return publicConfiguration;
    }

    private static string NormalizeProviderCode(string? providerCode) =>
        string.IsNullOrWhiteSpace(providerCode)
            ? string.Empty
            : providerCode.Trim().ToUpperInvariant();

    private static string? NormalizeEnvironment(string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment))
        {
            return null;
        }

        var normalized = environment.Trim();
        return normalized.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
            ? "Sandbox"
            : normalized.Equals("production", StringComparison.OrdinalIgnoreCase)
                ? "Production"
                : normalized;
    }

    private static IReadOnlyDictionary<string, object?> MergeMetadata(
        IReadOnlyDictionary<string, object?> current,
        IReadOnlyDictionary<string, object?> incoming)
    {
        var merged = new Dictionary<string, object?>(current, StringComparer.Ordinal);
        foreach (var entry in incoming)
        {
            merged[entry.Key] = NormalizeJsonBackedValue(entry.Value);
        }

        return merged;
    }

    private static PaymentProviderSummaryDto ToSummaryDto(PaymentProviderConfiguration provider) => new()
    {
        ProviderCode = provider.ProviderCode,
        DisplayName = provider.DisplayName,
        Description = provider.Description,
        Enabled = provider.Enabled,
        Environment = provider.Environment,
        Status = provider.Status,
        Health = BuildHealthDto(provider),
        Capabilities = provider.Capabilities.Select(ToCapabilityDto).ToList(),
        Metadata = provider.Metadata
    };

    private PaymentProviderDashboardProviderDto ToDashboardProviderDto(
        PaymentProviderConfiguration provider,
        IReadOnlyDictionary<string, string> protectedSecrets)
    {
        var health = BuildHealthDto(provider);
        var secrets = BuildDashboardSecretMetadata(provider, protectedSecrets);

        return new PaymentProviderDashboardProviderDto
        {
            ProviderCode = provider.ProviderCode,
            DisplayName = provider.DisplayName,
            Description = provider.Description,
            Enabled = provider.Enabled,
            Environment = provider.Environment,
            Status = provider.Status,
            Health = health,
            LastCheckUtc = health.LastSuccessfulCheckUtc ?? health.LastFailedCheckUtc,
            Secrets = secrets,
            Capabilities = provider.Capabilities.Select(ToCapabilityDto).ToList(),
            Configuration = provider.Configuration,
            Metadata = provider.Metadata,
            Permissions = PaymentProviderPermissions.All,
            Version = health.ProviderVersion
        };
    }

    private IReadOnlyList<PaymentProviderSecretMetadataDto> BuildDashboardSecretMetadata(
        PaymentProviderConfiguration provider,
        IReadOnlyDictionary<string, string> protectedSecrets)
    {
        var versions = DeserializeDashboardSecretVersions(provider.Metadata.TryGetValue(LifecycleMetadataKey, out var rawValue)
            ? rawValue
            : null);

        if (versions.Count == 0)
        {
            foreach (var definition in DashboardSecretDefinitions.Values
                         .GroupBy(definition => definition.StorageKey, StringComparer.Ordinal)
                         .Select(group => group.First()))
            {
                if (!protectedSecrets.TryGetValue(definition.StorageKey, out var storedSecret) ||
                    string.IsNullOrWhiteSpace(storedSecret))
                {
                    continue;
                }

                var secret = UnprotectStoredCredential(storedSecret);
                if (string.IsNullOrWhiteSpace(secret))
                {
                    continue;
                }

                versions.Add(new DashboardSecretVersionRecord
                {
                    SecretId = BuildDashboardSecretId(provider.ProviderCode, definition.DisplayName),
                    ProviderCode = provider.ProviderCode,
                    SecretType = definition.DisplayName,
                    Version = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = "migration",
                    ActivatedAtUtc = DateTime.UtcNow,
                    Status = ActiveStatus,
                    Fingerprint = CreateDashboardFingerprint(secret),
                    LastRotatedAtUtc = null,
                    LastUsedAtUtc = null
                });
            }
        }

        return versions
            .OrderBy(version => version.SecretType, StringComparer.Ordinal)
            .ThenByDescending(version => version.Version)
            .Select(ToDashboardSecretMetadataDto)
            .ToList();
    }

    private static IReadOnlyList<string> BuildDashboardWarnings(
        IReadOnlyList<PaymentProviderDashboardProviderDto> providers)
    {
        var warnings = new List<string>();

        if (providers.Count == 0)
        {
            warnings.Add("No payment providers are registered for administration.");
        }

        foreach (var provider in providers)
        {
            if (!provider.Enabled)
            {
                warnings.Add($"{provider.ProviderCode}: provider is disabled.");
            }

            if (provider.Secrets.Count == 0)
            {
                warnings.Add($"{provider.ProviderCode}: no secret metadata is available.");
            }
        }

        return warnings;
    }

    private static List<DashboardSecretVersionRecord> DeserializeDashboardSecretVersions(object? rawValue)
    {
        if (rawValue == null)
        {
            return [];
        }

        try
        {
            var json = rawValue is JsonElement element
                ? element.GetRawText()
                : JsonSerializer.Serialize(rawValue, JsonOptions);

            return JsonSerializer.Deserialize<List<DashboardSecretVersionRecord>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static PaymentProviderSecretMetadataDto ToDashboardSecretMetadataDto(DashboardSecretVersionRecord record) => new()
    {
        SecretId = record.SecretId,
        ProviderCode = record.ProviderCode,
        SecretType = record.SecretType,
        Version = record.Version,
        CreatedAtUtc = record.CreatedAtUtc,
        CreatedBy = record.CreatedBy,
        ActivatedAtUtc = record.ActivatedAtUtc,
        RetiredAtUtc = record.RetiredAtUtc,
        Status = record.Status,
        Fingerprint = record.Fingerprint,
        LastRotatedAtUtc = record.LastRotatedAtUtc,
        LastUsedAtUtc = record.LastUsedAtUtc
    };

    private static string CreateDashboardFingerprint(string secret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var hex = Convert.ToHexString(hash);
        return $"{hex[..4]}-{hex.Substring(4, 4)}-....";
    }

    private static string BuildDashboardSecretId(string providerCode, string secretType)
    {
        var normalizedType = secretType
            .Replace(" ", "-", StringComparison.Ordinal)
            .ToLowerInvariant();
        return $"{NormalizeProviderCode(providerCode)}-{normalizedType}";
    }

    private static PaymentProviderConfigurationDto ToConfigurationDto(PaymentProviderConfiguration provider) => new()
    {
        ProviderCode = provider.ProviderCode,
        DisplayName = provider.DisplayName,
        Description = provider.Description,
        Enabled = provider.Enabled,
        Environment = provider.Environment,
        Capabilities = provider.Capabilities.Select(ToCapabilityDto).ToList(),
        Configuration = provider.Configuration,
        Status = provider.Status,
        Health = BuildHealthDto(provider),
        Metadata = provider.Metadata
    };

    private static PaymentProviderCapabilityDto ToCapabilityDto(PaymentProviderCapability capability) => new()
    {
        Code = capability.Code,
        DisplayName = capability.DisplayName,
        Description = capability.Description,
        Supported = capability.Supported,
        RequiresConfiguration = capability.RequiresConfiguration
    };

    private static bool IsPositiveInteger(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

    private static void AddNonNegativeIntegerValidation(
        IReadOnlyDictionary<string, object?> configuration,
        string key,
        string message,
        ICollection<string> errors)
    {
        var value = ReadString(configuration, key, "0");
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            errors.Add(message);
        }
    }

    private static string RequireString(IReadOnlyDictionary<string, object?> configuration, string key) =>
        ReadString(configuration, key, string.Empty);

    private static bool RequireBool(IReadOnlyDictionary<string, object?> configuration, string key) =>
        ReadBool(configuration, key, false);

    private static string ReadString(
        IReadOnlyDictionary<string, object?> configuration,
        string key,
        string fallback)
    {
        if (!configuration.TryGetValue(key, out var value) || value == null)
        {
            return fallback;
        }

        value = NormalizeJsonBackedValue(value);
        if (value == null)
        {
            return fallback;
        }

        return value switch
        {
            string text => text.Trim(),
            bool boolean => boolean ? "true" : "false",
            int integer => integer.ToString(CultureInfo.InvariantCulture),
            long integer => integer.ToString(CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString()?.Trim() ?? fallback
        };
    }

    private static bool ReadBool(
        IReadOnlyDictionary<string, object?> configuration,
        string key,
        bool fallback)
    {
        if (!configuration.TryGetValue(key, out var value) || value == null)
        {
            return fallback;
        }

        value = NormalizeJsonBackedValue(value);
        return value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text.Trim(), out var parsed) => parsed,
            _ => fallback
        };
    }

    private static object? NormalizeJsonBackedValue(object? value)
    {
        if (value is not JsonElement element)
        {
            return value;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var integer) => integer,
            JsonValueKind.Number when element.TryGetInt64(out var longInteger) => longInteger,
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static Dictionary<string, object?> ToObjectDictionary(Dictionary<string, object?>? values)
    {
        var converted = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (values == null)
        {
            return converted;
        }

        foreach (var item in values)
        {
            converted[item.Key] = NormalizeJsonBackedValue(item.Value);
        }

        return converted;
    }

    private static string ToStableString(object? value) =>
        value == null
            ? string.Empty
            : NormalizeJsonBackedValue(value) switch
            {
                null => string.Empty,
                string text => text,
                bool boolean => boolean.ToString(CultureInfo.InvariantCulture),
                int integer => integer.ToString(CultureInfo.InvariantCulture),
                long integer => integer.ToString(CultureInfo.InvariantCulture),
                decimal number => number.ToString(CultureInfo.InvariantCulture),
                double number => number.ToString(CultureInfo.InvariantCulture),
                var normalized => JsonSerializer.Serialize(normalized, JsonOptions)
            };

    private static PaymentProviderHealthDto BuildHealthDto(PaymentProviderConfiguration provider)
    {
        var metadata = provider.Metadata;
        var latestResult = ReadActionResult(metadata, "connectivityLatestResult");
        var previousResult = ReadActionResult(metadata, "connectivityPreviousResult");
        var lastSuccessfulCheck = ReadDateTime(metadata, "connectivityLastSuccessfulCheckUtc");
        var lastFailedCheck = ReadDateTime(metadata, "connectivityLastFailedCheckUtc");
        var messages = ReadStringArray(metadata, "connectivityMessages");

        return new PaymentProviderHealthDto
        {
            Status = string.IsNullOrWhiteSpace(provider.Status) ? "Unknown" : provider.Status,
            Environment = provider.Environment,
            LastSuccessfulCheckUtc = lastSuccessfulCheck,
            LastFailedCheckUtc = lastFailedCheck,
            LastCheckDurationMs = ReadLong(metadata, "connectivityLastCheckDurationMs"),
            ProviderVersion = ReadString(metadata, "connectivityProviderVersion", string.Empty),
            ConnectionQuality = ReadString(metadata, "connectivityQuality", "Unknown"),
            OperationalMessages = messages,
            LatestResult = latestResult,
            PreviousResult = previousResult
        };
    }

    private static Dictionary<string, object?> SerializeResult(PaymentProviderActionResultDto result) =>
        new(StringComparer.Ordinal)
        {
            ["success"] = result.Success,
            ["status"] = result.Status,
            ["provider"] = result.Provider,
            ["environment"] = result.Environment,
            ["message"] = result.Message,
            ["checkedAtUtc"] = result.CheckedAtUtc,
            ["latencyMs"] = result.LatencyMs,
            ["errorCategory"] = result.ErrorCategory,
            ["warnings"] = result.Warnings.ToArray(),
            ["errors"] = result.Errors.ToArray(),
            ["providerVersion"] = result.ProviderVersion
        };

    private static PaymentProviderActionResultDto? ReadActionResult(
        IReadOnlyDictionary<string, object?> metadata,
        string key)
    {
        if (!metadata.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return null;
        }

        try
        {
            var json = rawValue is JsonElement element
                ? element.GetRawText()
                : JsonSerializer.Serialize(NormalizeJsonBackedValue(rawValue), JsonOptions);
            return JsonSerializer.Deserialize<PaymentProviderActionResultDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTime? ReadDateTime(
        IReadOnlyDictionary<string, object?> metadata,
        string key)
    {
        var text = ReadString(metadata, key, string.Empty);
        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static long? ReadLong(
        IReadOnlyDictionary<string, object?> metadata,
        string key)
    {
        var text = ReadString(metadata, key, string.Empty);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<string> ReadStringArray(
        IReadOnlyDictionary<string, object?> metadata,
        string key)
    {
        if (!metadata.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return Array.Empty<string>();
        }

        rawValue = NormalizeJsonBackedValue(rawValue);
        if (rawValue is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();
        }

        if (rawValue is IEnumerable<string> strings)
        {
            return strings.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }

        return Array.Empty<string>();
    }

    private static string DeriveConnectionQuality(long latencyMs)
    {
        return latencyMs switch
        {
            <= 1000 => "Excellent",
            <= 3000 => "Good",
            <= 10000 => "Degraded",
            _ => "Poor"
        };
    }

    private static string BuildSettingKey(string providerCode) =>
        $"CommerceHub.PaymentProviders.{providerCode}.AdministrationConfiguration";

    private sealed record PaymentProviderState(
        PaymentProviderConfiguration Configuration,
        IReadOnlyDictionary<string, string> ProtectedSecrets);

    private sealed class StoredPaymentProviderConfiguration
    {
        public string ProviderCode { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Environment { get; set; } = string.Empty;
        public string? Status { get; set; }
        public Dictionary<string, object?>? Configuration { get; set; }
        public Dictionary<string, object?>? Metadata { get; set; }
        public Dictionary<string, string>? ProtectedSecrets { get; set; }
    }

    private sealed record DashboardSecretDefinition(string StorageKey, string DisplayName);

    private sealed class DashboardSecretVersionRecord
    {
        public string SecretId { get; set; } = string.Empty;
        public string ProviderCode { get; set; } = string.Empty;
        public string SecretType { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? ActivatedAtUtc { get; set; }
        public DateTime? RetiredAtUtc { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Fingerprint { get; set; } = string.Empty;
        public DateTime? LastRotatedAtUtc { get; set; }
        public DateTime? LastUsedAtUtc { get; set; }
    }
}
