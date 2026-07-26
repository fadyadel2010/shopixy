namespace PointOfSale.Core.Events;

/// <summary>
/// Event emitted when the live homepage section set for a tenant (or the global scope)
/// is published and downstream read models must refresh.
/// </summary>
public class HomepageSectionsPublishedEvent : IntegrationEvent
{
    /// <summary>
    /// Publish timestamp of the live homepage snapshot.
    /// </summary>
    public DateTime PublishedAtUtc { get; set; }

    /// <summary>
    /// Number of live sections after publish.
    /// </summary>
    public int SectionCount { get; set; }
}
