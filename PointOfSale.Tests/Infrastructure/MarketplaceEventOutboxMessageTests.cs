using PointOfSale.Core.Entities;
using PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Tests.Infrastructure;

public sealed class MarketplaceEventOutboxMessageTests
{
    [Fact]
    public void Create_WhenMetadataIsValid_CopiesOrderingMetadata()
    {
        var evt = CreateValidEvent();

        var message = MarketplaceEventOutboxMessage.Create(evt, "{}");

        Assert.Equal(evt.EventId, message.EventId);
        Assert.Equal(evt.EventId, message.Id);
        Assert.Equal(evt.EntityId, message.EntityId);
        Assert.Equal(evt.TenantId, message.TenantId);
        Assert.Equal(evt.CorrelationId, message.CorrelationId);
        Assert.Equal(evt.Version, message.Version);
        Assert.Equal(evt.OccurredAtUtc, message.OccurredAtUtc);
    }

    [Theory]
    [MemberData(nameof(InvalidOrderingMetadata))]
    public void Create_WhenOrderingMetadataIsInvalid_RejectsEventBeforeDurableOutbox(
        Action<TestMarketplaceEvent> corruptEvent,
        string expectedMessage)
    {
        var evt = CreateValidEvent();
        corruptEvent(evt);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MarketplaceEventOutboxMessage.Create(evt, "{}"));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_WhenPayloadIsMissing_RejectsEventBeforeDurableOutbox()
    {
        var evt = CreateValidEvent();

        Assert.Throws<ArgumentException>(() =>
            MarketplaceEventOutboxMessage.Create(evt, " "));
    }

    public static IEnumerable<object[]> InvalidOrderingMetadata()
    {
        yield return
        [
            (Action<TestMarketplaceEvent>)(evt => evt.EventId = Guid.Empty),
            "event id"
        ];
        yield return
        [
            (Action<TestMarketplaceEvent>)(evt => evt.EntityId = Guid.Empty),
            "entity id"
        ];
        yield return
        [
            (Action<TestMarketplaceEvent>)(evt => evt.AggregateRootId = Guid.Empty),
            "aggregate root id"
        ];
        yield return
        [
            (Action<TestMarketplaceEvent>)(evt => evt.TenantId = Guid.Empty),
            "tenant id"
        ];
        yield return
        [
            (Action<TestMarketplaceEvent>)(evt => evt.CorrelationId = " "),
            "correlation id"
        ];
        yield return
        [
            (Action<TestMarketplaceEvent>)(evt => evt.OccurredAtUtc = default),
            "occurrence timestamp"
        ];
        yield return
        [
            (Action<TestMarketplaceEvent>)(evt => evt.OccurredAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Local)),
            "UTC"
        ];
        yield return
        [
            (Action<TestMarketplaceEvent>)(evt => evt.Version = 0),
            "positive version"
        ];
        yield return
        [
            (Action<TestMarketplaceEvent>)(evt => evt.EventVersion = 0),
            "positive event version"
        ];
        yield return
        [
            (Action<TestMarketplaceEvent>)(evt => evt.EventVersion = evt.Version + 1),
            "must match"
        ];
    }

    private static TestMarketplaceEvent CreateValidEvent()
    {
        return new TestMarketplaceEvent
        {
            EventId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow.AddSeconds(-1),
            CorrelationId = Guid.NewGuid().ToString("N"),
            EntityId = Guid.NewGuid(),
            AggregateRootId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Version = 7,
            EventVersion = 7
        };
    }

    public sealed class TestMarketplaceEvent : IMarketplaceDomainEvent
    {
        public Guid EventId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public Guid? AggregateRootId { get; set; }
        public Guid? TenantId { get; set; }
        public int Version { get; set; }
        public int EventVersion { get; set; }
    }
}
