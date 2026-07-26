using System.Text.Json.Serialization;

namespace PointOfSale.Core.Events;

public class StorefrontCmsPagePublishedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid PageGroupId { get; set; }
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
    
    public DateTime PublishedAtUtc { get; set; }

    public Guid GetAuthoritativeAggregateId() => PageGroupId;
}

public class StorefrontCmsPageDeletedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid PageGroupId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public DateTime DeletedAtUtc { get; set; }

    public Guid GetAuthoritativeAggregateId() => PageGroupId;
}

public class MarketplaceCmsPagePublishedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    public Guid PageGroupId { get; set; }
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
    
    public DateTime PublishedAtUtc { get; set; }
}

public class MarketplaceCmsPageDeletedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    public Guid PageGroupId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public DateTime DeletedAtUtc { get; set; }
}

public class StorefrontFooterUpdatedEvent : IntegrationEvent
{
    public DateTime UpdatedAtUtc { get; set; }
}

public class MarketplaceFooterUpdatedEvent : IntegrationEvent
{
    public DateTime UpdatedAtUtc { get; set; }
}
