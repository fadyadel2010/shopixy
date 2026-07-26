namespace PointOfSale.Core.Events;

/// <summary>
/// Base class for payment method events.
/// 
/// PHASE 10.4.D1: Payment Methods Management
/// S40: Version is now required and must be explicitly set by callers to ensure monotonic ordering.
/// </summary>
public abstract class PaymentMethodEventBase
{
    private int _version;
    
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid PaymentMethodId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Monotonic version counter for this payment method - used as deterministic tie-breaker for projection freshness.
    /// Must be explicitly set by callers. Must be positive (> 0).
    /// S40: No default value - callers must provide an incrementing per-payment-method version.
    /// </summary>
    public int Version
    {
        get => _version;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentException(
                    $"PaymentMethod event Version must be positive (> 0). Received: {value}. " +
                    $"EventType: {GetType().Name}, PaymentMethodId: {PaymentMethodId}",
                    nameof(Version));
            }
            _version = value;
        }
    }
}

/// <summary>
/// Event raised when a payment method is created.
/// </summary>
public class PaymentMethodCreatedEvent : PaymentMethodEventBase
{
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public bool IsEnabled { get; set; }
    public int SortOrder { get; set; }
    public bool SupportsOnline { get; set; }
    public bool SupportsOffline { get; set; }
    public bool IsSandbox { get; set; }
    public string? Icon { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public decimal Fee { get; set; }
    public string FeeType { get; set; } = "fixed";
    public Dictionary<string, string>? PublicConfiguration { get; set; }

    /// <summary>
    /// Indicates whether the payment method configuration is currently valid for customer exposure.
    /// Nullable to support event versioning - null means "unknown/not set in this event version".
    /// </summary>
    public bool? IsConfigurationValid { get; set; }

    /// <summary>
    /// Legacy compatibility field retained for older outbox/event payloads.
    /// New code should use IsConfigurationValid.
    /// </summary>
    public bool? HasConfiguration { get; set; }
}

/// <summary>
/// Event raised when a payment method is updated.
/// </summary>
public class PaymentMethodUpdatedEvent : PaymentMethodEventBase
{
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public bool IsEnabled { get; set; }
    public int SortOrder { get; set; }
    public bool SupportsOnline { get; set; }
    public bool SupportsOffline { get; set; }
    public bool IsSandbox { get; set; }
    public string? Icon { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public decimal Fee { get; set; }
    public string FeeType { get; set; } = "fixed";
    public Dictionary<string, string>? PublicConfiguration { get; set; }

    /// <summary>
    /// Indicates whether the payment method configuration is currently valid for customer exposure.
    /// Nullable to support event versioning - null means "unknown/not set in this event version".
    /// </summary>
    public bool? IsConfigurationValid { get; set; }

    /// <summary>
    /// Legacy compatibility field retained for older outbox/event payloads.
    /// New code should use IsConfigurationValid.
    /// </summary>
    public bool? HasConfiguration { get; set; }

    /// <summary>
    /// Whether configuration fields should be refreshed in the read model.
    /// </summary>
    public bool ConfigurationUpdated { get; set; }
}

/// <summary>
/// Event raised when a payment method is enabled.
/// </summary>
public class PaymentMethodEnabledEvent : PaymentMethodEventBase
{
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Event raised when a payment method is disabled.
/// </summary>
public class PaymentMethodDisabledEvent : PaymentMethodEventBase
{
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Event raised when a payment method is deleted (soft delete typically).
/// </summary>
public class PaymentMethodDeletedEvent : PaymentMethodEventBase
{
}
