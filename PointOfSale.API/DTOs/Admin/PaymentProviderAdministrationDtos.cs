namespace PointOfSale.API.DTOs.Admin;

public sealed class PaymentProviderCapabilityDto
{
    public string Code { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool Supported { get; init; }
    public bool RequiresConfiguration { get; init; }
}

public sealed class PaymentProviderSummaryDto
{
    public string ProviderCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string Environment { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public PaymentProviderHealthDto Health { get; init; } = new();
    public IReadOnlyList<PaymentProviderCapabilityDto> Capabilities { get; init; } =
        Array.Empty<PaymentProviderCapabilityDto>();
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}

public sealed class PaymentProviderConfigurationDto
{
    public string ProviderCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string Environment { get; init; } = string.Empty;
    public IReadOnlyList<PaymentProviderCapabilityDto> Capabilities { get; init; } =
        Array.Empty<PaymentProviderCapabilityDto>();
    public IReadOnlyDictionary<string, object?> Configuration { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
    public string Status { get; init; } = string.Empty;
    public PaymentProviderHealthDto Health { get; init; } = new();
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}

public sealed class PaymentProviderConfigurationRequest
{
    public string? ProviderCode { get; init; }
    public bool? Enabled { get; init; }
    public string? Environment { get; init; }
    public IReadOnlyDictionary<string, object?>? Configuration { get; init; }
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
    public IReadOnlyDictionary<string, string?>? SecretReplacements { get; init; }
}

public sealed class PaymentProviderConfigurationResponse
{
    public PaymentProviderConfigurationDto Provider { get; init; } = new();
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

public sealed class PaymentProviderConfigurationSaveResult
{
    public PaymentProviderConfigurationDto Provider { get; init; } = new();
    public IReadOnlyList<string> ChangedFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RedactedChangedFields { get; init; } = Array.Empty<string>();
}

public sealed class PaymentProviderListResponse
{
    public IReadOnlyList<PaymentProviderSummaryDto> Providers { get; init; } =
        Array.Empty<PaymentProviderSummaryDto>();
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
}

public sealed class PaymentProviderDashboardDto
{
    public IReadOnlyList<PaymentProviderDashboardProviderDto> Providers { get; init; } =
        Array.Empty<PaymentProviderDashboardProviderDto>();
    public PaymentProviderDashboardSummaryDto Summary { get; init; } = new();
    public IReadOnlyList<PaymentProviderCapabilityDto> Capabilities { get; init; } =
        Array.Empty<PaymentProviderCapabilityDto>();
    public IReadOnlyList<PaymentProviderDashboardHealthDto> Health { get; init; } =
        Array.Empty<PaymentProviderDashboardHealthDto>();
    public IReadOnlyList<PaymentProviderDashboardSecretSummaryDto> Secrets { get; init; } =
        Array.Empty<PaymentProviderDashboardSecretSummaryDto>();
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<PaymentProviderAdministrationActivityDto> RecentActivity { get; init; } =
        Array.Empty<PaymentProviderAdministrationActivityDto>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class PaymentProviderDashboardProviderDto
{
    public string ProviderCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public PaymentProviderHealthDto Health { get; init; } = new();
    public DateTime? LastCheckUtc { get; init; }
    public IReadOnlyList<PaymentProviderSecretMetadataDto> Secrets { get; init; } =
        Array.Empty<PaymentProviderSecretMetadataDto>();
    public IReadOnlyList<PaymentProviderCapabilityDto> Capabilities { get; init; } =
        Array.Empty<PaymentProviderCapabilityDto>();
    public IReadOnlyDictionary<string, object?> Configuration { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public string? Version { get; init; }
}

public sealed class PaymentProviderDashboardSummaryDto
{
    public int ProviderCount { get; init; }
    public int EnabledCount { get; init; }
    public int DisabledCount { get; init; }
    public int HealthyCount { get; init; }
    public int WarningCount { get; init; }
    public int ErrorCount { get; init; }
    public int UnknownHealthCount { get; init; }
    public int ConfiguredSecretCount { get; init; }
    public int CapabilityCount { get; init; }
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class PaymentProviderDashboardHealthDto
{
    public string ProviderCode { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public DateTime? LastCheckUtc { get; init; }
    public long? LastCheckDurationMs { get; init; }
    public string ConnectionQuality { get; init; } = "Unknown";
}

public sealed class PaymentProviderDashboardSecretSummaryDto
{
    public string ProviderCode { get; init; } = string.Empty;
    public int SecretCount { get; init; }
    public int ActiveSecretCount { get; init; }
    public DateTime? LastRotatedAtUtc { get; init; }
    public IReadOnlyList<PaymentProviderSecretMetadataDto> Secrets { get; init; } =
        Array.Empty<PaymentProviderSecretMetadataDto>();
}

public sealed class PaymentProviderAdministrationActivityDto
{
    public string ActivityId { get; init; } = string.Empty;
    public string ProviderCode { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; }
    public string? Actor { get; init; }
    public bool Success { get; init; }
    public string? Reference { get; init; }
}

public sealed class PaymentProviderActionResultDto
{
    public bool Success { get; init; }
    public string Status { get; init; } = "Unknown";
    public string Provider { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime CheckedAtUtc { get; init; }
    public long LatencyMs { get; init; }
    public string ErrorCategory { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public string? ProviderVersion { get; init; }
    public IReadOnlyList<PaymentProviderCapabilityDto> Capabilities { get; init; } =
        Array.Empty<PaymentProviderCapabilityDto>();
}

public sealed class PaymentProviderActionResponse
{
    public PaymentProviderActionResultDto Result { get; init; } = new();
    public PaymentProviderHealthDto Health { get; init; } = new();
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
}

public sealed class PaymentProviderSecretMetadataDto
{
    public string SecretId { get; init; } = string.Empty;
    public string ProviderCode { get; init; } = string.Empty;
    public string SecretType { get; init; } = string.Empty;
    public int Version { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? ActivatedAtUtc { get; init; }
    public DateTime? RetiredAtUtc { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
    public DateTime? LastRotatedAtUtc { get; init; }
    public DateTime? LastUsedAtUtc { get; init; }
}

public sealed class PaymentProviderSecretLifecycleResponse
{
    public string ProviderCode { get; init; } = string.Empty;
    public IReadOnlyList<PaymentProviderSecretMetadataDto> Secrets { get; init; } =
        Array.Empty<PaymentProviderSecretMetadataDto>();
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

public sealed class RotatePaymentProviderSecretRequest
{
    public string SecretType { get; init; } = string.Empty;
    public string NewSecret { get; init; } = string.Empty;
    public string ConfirmNewSecret { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public bool AdministratorConfirmation { get; init; }
}

public sealed class RotatePaymentProviderSecretResponse
{
    public string ProviderCode { get; init; } = string.Empty;
    public PaymentProviderSecretMetadataDto Secret { get; init; } = new();
    public IReadOnlyList<PaymentProviderSecretMetadataDto> Versions { get; init; } =
        Array.Empty<PaymentProviderSecretMetadataDto>();
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

public sealed class PaymentProviderHealthDto
{
    public string Status { get; init; } = "Unknown";
    public string Environment { get; init; } = string.Empty;
    public DateTime? LastSuccessfulCheckUtc { get; init; }
    public DateTime? LastFailedCheckUtc { get; init; }
    public long? LastCheckDurationMs { get; init; }
    public string? ProviderVersion { get; init; }
    public string ConnectionQuality { get; init; } = "Unknown";
    public IReadOnlyList<string> OperationalMessages { get; init; } = Array.Empty<string>();
    public PaymentProviderActionResultDto? LatestResult { get; init; }
    public PaymentProviderActionResultDto? PreviousResult { get; init; }
}
