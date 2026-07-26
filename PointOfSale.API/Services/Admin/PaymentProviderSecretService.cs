using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Payments.Administration;

namespace PointOfSale.API.Services.Admin;

public sealed class PaymentProviderSecretService : IPaymentProviderSecretService
{
    private const string PaymobProviderCode = "PAYMOB";
    private const string LifecycleMetadataKey = "secretLifecycle";
    private const string ActiveStatus = "Active";
    private const string PendingActivationStatus = "PendingActivation";
    private const string RetiredStatus = "Retired";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, SecretDefinition> SecretDefinitions =
        new Dictionary<string, SecretDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["API Key"] = new("apiKey", "API Key", 8),
            ["ApiKey"] = new("apiKey", "API Key", 8),
            ["apiKey"] = new("apiKey", "API Key", 8),
            ["api_key"] = new("apiKey", "API Key", 8),
            ["HMAC Secret"] = new("hmacSecret", "HMAC Secret", 16),
            ["HmacSecret"] = new("hmacSecret", "HMAC Secret", 16),
            ["hmacSecret"] = new("hmacSecret", "HMAC Secret", 16),
            ["hmac_secret"] = new("hmacSecret", "HMAC Secret", 16)
        };

    private readonly IPaymentProviderAdministrationService _administrationService;

    public PaymentProviderSecretService(IPaymentProviderAdministrationService administrationService)
    {
        _administrationService = administrationService;
    }

    public PaymentProviderSecretLifecycleResponse GetSecretMetadata(string providerCode)
    {
        var provider = _administrationService.GetProviderConfiguration(providerCode);
        var operational = _administrationService.GetProviderOperationalConfiguration(provider.ProviderCode);
        var versions = LoadVersionRecords(provider, operational);

        return new PaymentProviderSecretLifecycleResponse
        {
            ProviderCode = provider.ProviderCode,
            Secrets = versions.Select(ToMetadataDto).ToList(),
            Permissions = PaymentProviderPermissions.All
        };
    }

    public RotatePaymentProviderSecretResponse RotateSecret(
        string providerCode,
        RotatePaymentProviderSecretRequest request,
        Guid? actorId,
        string? actorEmail)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = _administrationService.GetProviderConfiguration(providerCode);
        var normalizedProviderCode = NormalizeProviderCode(provider.ProviderCode);
        if (!string.Equals(normalizedProviderCode, PaymobProviderCode, StringComparison.Ordinal))
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status400BadRequest,
                "INVALID_PROVIDER",
                "The requested payment provider does not support secret lifecycle management.");
        }

        var definition = ResolveSecretDefinition(request.SecretType);
        ValidateRotationRequest(request, definition);

        var operational = _administrationService.GetProviderOperationalConfiguration(normalizedProviderCode);
        var currentSecrets = operational.Credentials;
        if (currentSecrets.TryGetValue(definition.StorageKey, out var currentSecret) &&
            SecureEquals(currentSecret, request.NewSecret.Trim()))
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status409Conflict,
                "DUPLICATE_REPLACEMENT_SECRET",
                "Replacement secret matches the current active secret.");
        }

        var now = DateTime.UtcNow;
        var replacementFingerprint = CreateFingerprint(request.NewSecret.Trim());
        var versions = LoadVersionRecords(provider, operational).ToList();
        var existingForType = versions
            .Where(version => string.Equals(version.SecretType, definition.DisplayName, StringComparison.Ordinal))
            .ToList();

        if (existingForType.Any(version => string.Equals(version.Fingerprint, replacementFingerprint, StringComparison.Ordinal)))
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status409Conflict,
                "DUPLICATE_REPLACEMENT_SECRET",
                "Replacement secret matches an existing secret version.");
        }

        foreach (var activeVersion in existingForType.Where(version => version.Status == ActiveStatus))
        {
            activeVersion.Status = RetiredStatus;
            activeVersion.RetiredAtUtc = now;
            activeVersion.LastRotatedAtUtc = now;
        }

        var versionNumber = existingForType.Count == 0
            ? 1
            : existingForType.Max(version => version.Version) + 1;

        var pending = new PaymentProviderSecretVersionRecord
        {
            SecretId = BuildSecretId(normalizedProviderCode, definition.DisplayName),
            ProviderCode = normalizedProviderCode,
            SecretType = definition.DisplayName,
            Version = versionNumber,
            CreatedAtUtc = now,
            CreatedBy = ResolveActor(actorId, actorEmail),
            ActivatedAtUtc = null,
            RetiredAtUtc = null,
            Status = PendingActivationStatus,
            Fingerprint = replacementFingerprint,
            LastRotatedAtUtc = existingForType.Count == 0 ? null : now,
            LastUsedAtUtc = null
        };

        pending.Status = ActiveStatus;
        pending.ActivatedAtUtc = now;
        versions.Add(pending);

        var metadata = new Dictionary<string, object?>(provider.Metadata, StringComparer.Ordinal)
        {
            [LifecycleMetadataKey] = versions
                .OrderBy(version => version.SecretType, StringComparer.Ordinal)
                .ThenBy(version => version.Version)
                .ToList()
        };

        var saveResult = _administrationService.SaveProviderConfiguration(
            normalizedProviderCode,
            new PaymentProviderConfigurationRequest
            {
                ProviderCode = normalizedProviderCode,
                Enabled = provider.Enabled,
                Environment = provider.Environment,
                Configuration = provider.Configuration,
                Metadata = metadata,
                SecretReplacements = new Dictionary<string, string?>
                {
                    [definition.StorageKey] = request.NewSecret.Trim()
                }
            });

        var refreshedOperational = _administrationService.GetProviderOperationalConfiguration(normalizedProviderCode);
        var refreshedVersions = LoadVersionRecords(saveResult.Provider, refreshedOperational)
            .OrderByDescending(version => version.SecretType == definition.DisplayName)
            .ThenByDescending(version => version.Version)
            .ToList();

        return new RotatePaymentProviderSecretResponse
        {
            ProviderCode = normalizedProviderCode,
            Secret = ToMetadataDto(pending),
            Versions = refreshedVersions.Select(ToMetadataDto).ToList(),
            Permissions = PaymentProviderPermissions.All
        };
    }

    private static void ValidateRotationRequest(
        RotatePaymentProviderSecretRequest request,
        SecretDefinition definition)
    {
        if (!request.AdministratorConfirmation)
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status403Forbidden,
                "SECRET_ROTATION_CONFIRMATION_REQUIRED",
                "Current administrator confirmation is required before rotating a provider secret.");
        }

        var newSecret = request.NewSecret?.Trim() ?? string.Empty;
        var confirmation = request.ConfirmNewSecret?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(newSecret))
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status400BadRequest,
                "EMPTY_REPLACEMENT_SECRET",
                "Replacement secret cannot be empty.");
        }

        if (!string.Equals(newSecret, confirmation, StringComparison.Ordinal))
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status400BadRequest,
                "SECRET_CONFIRMATION_MISMATCH",
                "Replacement secret and confirmation must match.");
        }

        if (newSecret.Length < definition.MinimumLength)
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status400BadRequest,
                "INVALID_SECRET_FORMAT",
                "Replacement secret failed provider-independent format validation.");
        }
    }

    private static SecretDefinition ResolveSecretDefinition(string secretType)
    {
        if (string.IsNullOrWhiteSpace(secretType) ||
            !SecretDefinitions.TryGetValue(secretType.Trim(), out var definition))
        {
            throw new PaymentProviderAdministrationException(
                StatusCodes.Status400BadRequest,
                "INVALID_SECRET_TYPE",
                "The requested provider secret type is not supported.");
        }

        return definition;
    }

    private static IReadOnlyList<PaymentProviderSecretVersionRecord> LoadVersionRecords(
        PaymentProviderConfigurationDto provider,
        PaymentProviderOperationalConfiguration operational)
    {
        var versions = DeserializeVersions(provider.Metadata.TryGetValue(LifecycleMetadataKey, out var rawValue)
            ? rawValue
            : null);

        if (versions.Count > 0)
        {
            return versions;
        }

        var migrated = new List<PaymentProviderSecretVersionRecord>();
        foreach (var definition in SecretDefinitions.Values
                     .GroupBy(definition => definition.StorageKey, StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            if (!operational.Credentials.TryGetValue(definition.StorageKey, out var secret) ||
                string.IsNullOrWhiteSpace(secret))
            {
                continue;
            }

            migrated.Add(new PaymentProviderSecretVersionRecord
            {
                SecretId = BuildSecretId(provider.ProviderCode, definition.DisplayName),
                ProviderCode = provider.ProviderCode,
                SecretType = definition.DisplayName,
                Version = 1,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = "migration",
                ActivatedAtUtc = DateTime.UtcNow,
                Status = ActiveStatus,
                Fingerprint = CreateFingerprint(secret),
                LastRotatedAtUtc = null,
                LastUsedAtUtc = null
            });
        }

        return migrated;
    }

    private static List<PaymentProviderSecretVersionRecord> DeserializeVersions(object? rawValue)
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

            return JsonSerializer.Deserialize<List<PaymentProviderSecretVersionRecord>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static PaymentProviderSecretMetadataDto ToMetadataDto(PaymentProviderSecretVersionRecord record) => new()
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

    private static bool SecureEquals(string currentSecret, string replacementSecret)
    {
        var current = Encoding.UTF8.GetBytes(currentSecret);
        var replacement = Encoding.UTF8.GetBytes(replacementSecret);
        return current.Length == replacement.Length &&
               CryptographicOperations.FixedTimeEquals(current, replacement);
    }

    private static string CreateFingerprint(string secret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var hex = Convert.ToHexString(hash);
        return $"{hex[..4]}-{hex.Substring(4, 4)}-....";
    }

    private static string BuildSecretId(string providerCode, string secretType)
    {
        var normalizedType = secretType
            .Replace(" ", "-", StringComparison.Ordinal)
            .ToLowerInvariant();
        return $"{NormalizeProviderCode(providerCode)}-{normalizedType}";
    }

    private static string NormalizeProviderCode(string providerCode) => providerCode.Trim().ToUpperInvariant();

    private static string ResolveActor(Guid? actorId, string? actorEmail)
    {
        if (!string.IsNullOrWhiteSpace(actorEmail))
        {
            return actorEmail.Trim();
        }

        return actorId?.ToString("D") ?? "unknown-super-admin";
    }

    private sealed record SecretDefinition(string StorageKey, string DisplayName, int MinimumLength);

    private sealed class PaymentProviderSecretVersionRecord
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
