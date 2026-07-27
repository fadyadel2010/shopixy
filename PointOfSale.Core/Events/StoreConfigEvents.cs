namespace PointOfSale.Core.Events;

/// <summary>
/// Event emitted when store configuration is updated.
/// Phase 7.1: Theme & Store Config System
/// 
/// Usage:
/// - CDN invalidation (future)
/// - Frontend cache refresh
/// - Audit logging
/// </summary>
public class StoreConfigUpdatedEvent : IntegrationEvent
{
    /// <summary>
    /// Store config ID.
    /// </summary>
    public Guid StoreConfigId { get; set; }

    /// <summary>
    /// Theme identifier that was applied.
    /// </summary>
    public string ThemeId { get; set; } = string.Empty;

    /// <summary>
    /// Store name (for logging/notifications).
    /// </summary>
    public string StoreName { get; set; } = string.Empty;

    /// <summary>
    /// When the config was updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Event emitted when the storefront runtime theme is activated.
/// </summary>
public class StorefrontThemeActivatedEvent : IntegrationEvent
{
    public Guid StoreConfigId { get; set; }

    public string PreviousThemeId { get; set; } = string.Empty;

    public string ThemeId { get; set; } = string.Empty;

    public string StoreName { get; set; } = string.Empty;

    public DateTime ActivatedAtUtc { get; set; }
}

/// <summary>
/// Event emitted when store configuration is created for the first time.
/// </summary>
public class StoreConfigCreatedEvent : IntegrationEvent
{
    /// <summary>
    /// Store config ID.
    /// </summary>
    public Guid StoreConfigId { get; set; }

    /// <summary>
    /// Theme identifier.
    /// </summary>
    public string ThemeId { get; set; } = string.Empty;

    /// <summary>
    /// Store name.
    /// </summary>
    public string StoreName { get; set; } = string.Empty;

    /// <summary>
    /// When the config was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Event emitted when a store configuration version is published (Live).
/// Phase 9.6: Versioning
/// </summary>
public class StoreConfigPublishedEvent : StoreConfigUpdatedEvent
{
    public int ConfigVersion { get; set; }
    
    /// <summary>
    /// BACKWARD COMPATIBILITY: Alias for ConfigVersion to support rolling deployments.
    /// Consumers using the old "Version" property will continue to work.
    /// </summary>
    public int Version
    {
        get => ConfigVersion;
        set => ConfigVersion = value;
    }
}

/// <summary>
/// Event emitted when a store configuration is rolled back to a previous version.
/// Phase 9.6: Versioning
/// </summary>
public class StoreConfigRolledBackEvent : StoreConfigUpdatedEvent
{
    public int ConfigVersion { get; set; }
    
    /// <summary>
    /// BACKWARD COMPATIBILITY: Alias for ConfigVersion to support rolling deployments.
    /// Consumers using the old "Version" property will continue to work.
    /// </summary>
    public int Version
    {
        get => ConfigVersion;
        set => ConfigVersion = value;
    }
}
