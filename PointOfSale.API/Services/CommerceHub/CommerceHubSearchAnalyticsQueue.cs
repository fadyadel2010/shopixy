using System.Threading.Channels;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubSearchAnalyticsQueue
{
    void Enqueue(CommerceHubSearchAnalyticsEvent item);
    ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken);
    ValueTask<CommerceHubSearchAnalyticsEvent> DequeueAsync(CancellationToken cancellationToken);
    bool TryDequeue(out CommerceHubSearchAnalyticsEvent? item);
}

public class CommerceHubSearchAnalyticsQueue : ICommerceHubSearchAnalyticsQueue
{
    private readonly Channel<CommerceHubSearchAnalyticsEvent> _channel;

    public CommerceHubSearchAnalyticsQueue()
    {
        _channel = Channel.CreateBounded<CommerceHubSearchAnalyticsEvent>(new BoundedChannelOptions(10_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    }

    public void Enqueue(CommerceHubSearchAnalyticsEvent item)
    {
        _channel.Writer.TryWrite(item);
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) =>
        _channel.Reader.WaitToReadAsync(cancellationToken);

    public ValueTask<CommerceHubSearchAnalyticsEvent> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);

    public bool TryDequeue(out CommerceHubSearchAnalyticsEvent? item) =>
        _channel.Reader.TryRead(out item);
}
