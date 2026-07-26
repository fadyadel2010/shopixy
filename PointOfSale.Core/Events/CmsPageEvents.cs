using System.Text.Json.Serialization;

namespace PointOfSale.Core.Events;

/// <summary>
/// Event emitted when a CMS page is published (Draft → Live).
/// Phase 9.11: Static CMS Pages Engine
/// 
/// Triggers:
/// - MongoDB projection update (storefront_pages)
/// - Redis cache invalidation
/// </summary>
public class CmsPagePublishedEvent : IntegrationEvent
{
    /// <summary>
    /// Store config ID (source of the CMS page data).
    /// </summary>
    public Guid StoreConfigId { get; set; }

    /// <summary>
    /// CMS page slug (e.g., "contact", "about", "privacy", "terms").
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Version number of the published page.
    /// WIRE COMPATIBILITY: Also serialized as "Version" for backward compatibility with in-flight messages.
    /// </summary>
    [JsonPropertyName("PageVersion")]
    public int PageVersion { get; set; }
    
    /// <summary>
    /// BACKWARD COMPATIBILITY: Alias for PageVersion to support rolling deployments.
    /// Consumers using the old "Version" property will continue to work.
    /// </summary>
    [JsonPropertyName("Version")]
    public int Version
    {
        get => PageVersion;
        set => PageVersion = value;
    }

    /// <summary>
    /// When the page was published.
    /// </summary>
    public DateTime PublishedAtUtc { get; set; }
}

/// <summary>
/// Event emitted when a CMS page is rolled back to a previous version.
/// Phase 9.11: Static CMS Pages Engine
/// </summary>
public class CmsPageRolledBackEvent : IntegrationEvent
{
    /// <summary>
    /// Store config ID.
    /// </summary>
    public Guid StoreConfigId { get; set; }

    /// <summary>
    /// CMS page slug.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Version number rolled back to.
    /// WIRE COMPATIBILITY: Also serialized as "Version" for backward compatibility with in-flight messages.
    /// </summary>
    [JsonPropertyName("PageVersion")]
    public int PageVersion { get; set; }
    
    /// <summary>
    /// BACKWARD COMPATIBILITY: Alias for PageVersion to support rolling deployments.
    /// Consumers using the old "Version" property will continue to work.
    /// </summary>
    [JsonPropertyName("Version")]
    public int Version
    {
        get => PageVersion;
        set => PageVersion = value;
    }

    /// <summary>
    /// When the rollback occurred.
    /// </summary>
    public DateTime RolledBackAtUtc { get; set; }
}

/// <summary>
/// Event emitted when navigation (header/footer) is published.
/// Phase 9.11: Navigation Engine
/// </summary>
public class NavigationPublishedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    /// <summary>
    /// Store config ID.
    /// </summary>
    public Guid StoreConfigId { get; set; }

    /// <summary>
    /// When navigation was published.
    /// </summary>
    public DateTime PublishedAtUtc { get; set; }

    public Guid GetAuthoritativeAggregateId() => StoreConfigId;
}
