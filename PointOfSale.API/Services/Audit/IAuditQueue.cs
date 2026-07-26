using PointOfSale.API.DTOs;

namespace PointOfSale.API.Services.Audit;

public interface IAuditQueue
{
    void Enqueue(AuditLogItem item);
    Task<AuditLogItem> DequeueAsync(CancellationToken cancellationToken);
    void Retry(AuditLogItem item);
    long QueueDepth { get; }
    long RetryCount { get; }
}
